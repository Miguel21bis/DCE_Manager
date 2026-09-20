using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // ---------------------------------------------------------------------------
    // Mode ligne de commande :
    //
    //   DCE_Manager.exe --debrief "NomCampagne"
    //   DCE_Manager.exe --debrief "NomCampagne" --saved-games "C:\...\Saved Games\DCS"
    //
    // Lance par EventsTracker.lua a la fin d'une mission DCS, a la place de
    // l'ancienne fenetre console DOS.
    //
    // --saved-games est optionnel mais fortement conseille : la meme campagne peut
    // exister dans plusieurs installations de DCS (DCS, DCS_PartB, DCS.openbeta...)
    // et le nom seul ne dit pas laquelle debriefer. EventsTracker tourne DANS une
    // installation precise et connait son chemin (campL.path), donc il n'a rien a
    // deviner. Sans l'argument, on se rabat sur une recherche decrite plus bas.
    //
    // DEUX CAS, selon que DCE_Manager tourne deja ou non :
    //
    //   - DCE_Manager est deja ouvert : on lui passe la demande par un tuyau nomme
    //     et on ressort immediatement. C'est lui qui ouvre le runner, avec sa
    //     configuration deja chargee. Pas de deuxieme instance.
    //
    //   - personne ne repond sur le tuyau : on est seul, on charge une config
    //     allegee depuis options.txt et on ouvre le runner nous-memes, sans jamais
    //     ouvrir Main_Form.
    //
    // Le chemin de l'exe arrive jusqu'ici par le chemin inverse :
    //   ScriptsModRunner_Form pose DCEM_EXE_PATH dans l'environnement de luae.exe
    //   -> MAIN_NextMission.lua le range dans campL.DCEManagerExe
    //   -> campL part dans le .miz (l10n/DEFAULT/camp_status.lua)
    //   -> EventsTracker.lua le relit en jeu et nous rappelle ici.
    // Une mission generee "a l'ancienne" (double-clic sur le .bat) n'a pas ce
    // champ, donc garde l'ancien comportement : rien a desactiver.
    // ---------------------------------------------------------------------------
    internal static class DebriefCli
    {
        private const string Flag = "--debrief";
        private const string FlagSavedGames = "--saved-games";

        // Nom du tuyau nomme. Il sert aussi de detecteur d'instance : si la
        // connexion aboutit, c'est qu'une instance ecoute, donc qu'elle tourne.
        // Pas besoin de Mutex en plus.
        private const string PipeName = "DCE_Manager_Debrief";

        // Memes valeurs que le bouton Debriefing des QuickActions (zone 2) dans
        // Campaigns_List_Grid_Left : ce .bat ne suit pas la convention BAT_xxx.lua,
        // d'ou le nom de script Lua passe explicitement.
        private const string BatFile = "DEBUG_DebriefMission.bat";
        private const string LuaScript = "Debrief_Master.lua";

        // Fichier temporaire ecrit par EventsTracker.lua a la fin d'une mission.
        // Sa presence a la racine du dossier de campagne = il y a vraiment quelque
        // chose a debriefer ici. C'est ce qui permet de departager deux copies de
        // la meme campagne dans deux installations de DCS.
        private const string EndOfMissionFile = "camp_status.lua";

        // -----------------------------------------------------------------------
        // 1. LIGNE DE COMMANDE
        // -----------------------------------------------------------------------

        // Lit la ligne de commande sans toucher a la signature de Main().
        public static bool IsRequested(out string campaignName)
        {
            campaignName = GetArgValue(Flag);
            return !string.IsNullOrWhiteSpace(campaignName);
        }

        // Retourne la valeur qui suit un argument, ou null s'il est absent.
        private static string GetArgValue(string flag)
        {
            string[] args = Environment.GetCommandLineArgs(); // [0] = l'exe lui-meme

            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (i + 1 < args.Length)
                    return args[i + 1];

                return null;
            }

            return null;
        }

        // -----------------------------------------------------------------------
        // 2. INSTANCE DEJA OUVERTE
        // -----------------------------------------------------------------------

        // Essaie de passer la demande a l'instance deja lancee.
        // Retourne false si personne n'ecoute : c'est le cas normal quand
        // DCE_Manager n'est pas ouvert, pas une erreur.
        public static bool SendToRunningInstance(string campaignName)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    pipe.Connect(500); // ms : si personne n'ecoute, on abandonne vite

                    byte[] data = Encoding.UTF8.GetBytes(campaignName);
                    pipe.Write(data, 0, data.Length);
                    pipe.Flush();
                }

                FormUtils.LogRegister("DebriefCli | demande transmise a l'instance deja ouverte : " + campaignName);
                return true;
            }
            catch (TimeoutException)
            {
                return false; // personne au bout du tuyau : on fera le travail nous-memes
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("DebriefCli | echec de la transmission : " + ex.Message);
                return false;
            }
        }

        // Appelee une fois par Main_Form, juste apres LoadConfiguration().
        // Ouvre le tuyau en arriere-plan et attend d'eventuelles demandes.
        public static void StartListener(Form owner)
        {
            var thread = new Thread(() => ListenLoop(owner));
            thread.IsBackground = true; // ne retient pas la fermeture de l'appli
            thread.Name = "DebriefCli listener";
            thread.Start();
        }

        private static void ListenLoop(Form owner)
        {
            while (!owner.IsDisposed)
            {
                try
                {
                    // Un NamedPipeServerStream ne sert qu'une connexion : on en
                    // recree un a chaque tour de boucle.
                    using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In))
                    {
                        pipe.WaitForConnection();

                        var buffer = new byte[1024];
                        int read = pipe.Read(buffer, 0, buffer.Length);

                        if (read <= 0)
                            continue;

                        string campaignName = Encoding.UTF8.GetString(buffer, 0, read).Trim();

                        if (string.IsNullOrEmpty(campaignName) || owner.IsDisposed)
                            continue;

                        // On est sur un thread de fond : interdit de toucher a une
                        // Form directement, d'ou le BeginInvoke.
                        owner.BeginInvoke((MethodInvoker)(() => RunInsideMainInstance(owner, campaignName)));
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("DebriefCli | listener : " + ex.Message);
                    Thread.Sleep(1000); // evite de boucler a vide a pleine vitesse
                }
            }
        }

        // -----------------------------------------------------------------------
        // 3. LANCEMENT DU DEBRIEFING
        // -----------------------------------------------------------------------

        // Cas "DCE_Manager etait deja ouvert". Sa configuration est deja chargee :
        // on n'y touche surtout pas, sinon on lui ecraserait ses chemins.
        private static void RunInsideMainInstance(Form owner, string campaignName)
        {
            FormUtils.LogRegister("DebriefCli | debriefing demande depuis DCS pour " + campaignName);

            string folderPath = CampaignFolder(ParamConf.PATH_SavedGames_DCS, campaignName);

            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show(owner,
                    "Campaign folder not found in the current configuration (" + ParamConf.CurrentConfigName + "):\r\n" + folderPath +
                    "\r\n\r\nSwitch DCE_Manager to the right configuration, then use the Debriefing button.",
                    "DCE_Manager - Debriefing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // La fenetre est probablement derriere DCS : on la remonte.
            if (owner.WindowState == FormWindowState.Minimized)
                owner.WindowState = FormWindowState.Normal;

            owner.Activate();

            OpenRunner(campaignName, folderPath, owner);

            // La mission a change : nombre de missions, bouton Skip, icone de
            // debriefing... la grille doit etre relue.
            var mainForm = owner as Main_Form;

            if (mainForm != null && mainForm.CampaignGridLeft != null)
                _ = mainForm.CampaignGridLeft.LoadCampaignsAsync(selectCampaignName: campaignName);
        }

        // Cas "DCE_Manager n'etait pas ouvert" : appele directement par Program.cs.
        public static void Run(string campaignName)
        {
            FormUtils.LogRegister("DebriefCli | demarrage en mode --debrief pour " + campaignName);

            // WinForms n'installe son SynchronizationContext qu'au demarrage d'une
            // boucle de messages (Application.Run / ShowDialog). Or ProcessConsoleBridge
            // capture SynchronizationContext.Current DANS SON CONSTRUCTEUR, qui tourne
            // comme initialiseur de champ de ScriptsModRunner_Form, donc avant. En
            // lancement normal Main_Form l'a installe depuis longtemps ; ici il n'y a
            // rien eu avant, et sans ca la sortie de luae.exe serait relayee depuis un
            // thread de fond - l'affichage ne se mettrait jamais a jour.
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

            if (!LoadMinimalConfig(campaignName))
            {
                MessageBox.Show(
                    "DCE_Manager could not find its configuration for the campaign \"" + campaignName + "\".\r\n\r\n" +
                    "Open DCE_Manager normally once so it can save its settings, then try again.",
                    "DCE_Manager - Debriefing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            OpenRunner(campaignName, CampaignFolder(ParamConf.PATH_SavedGames_DCS, campaignName), null);

            FormUtils.LogRegister("DebriefCli | fin du debriefing de " + campaignName);
        }

        private static void OpenRunner(string campaignName, string folderPath, IWin32Window owner)
        {
            string batPath = Path.Combine(folderPath, BatFile);

            if (!File.Exists(batPath))
            {
                MessageBox.Show("File not found:\r\n" + batPath,
                    "DCE_Manager - Debriefing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            FormUtils.LogRegister("DebriefCli | ouverture du runner sur " + folderPath);

            using (var runner = new ScriptsModRunner_Form(batPath, folderPath, campaignName, LuaScript))
            {
                if (owner != null)
                    runner.ShowDialog(owner);
                else
                    runner.ShowDialog(); // pas de fenetre parente dans ce mode
            }
        }

        private static string CampaignFolder(string savedGames, string campaignName)
        {
            return Path.Combine(savedGames ?? "", @"Mods\tech\DCE\Missions\Campaigns", campaignName);
        }

        // Compare deux chemins en ignorant la casse et l'antislash final.
        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;

            return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        // -----------------------------------------------------------------------
        // 4. CONFIGURATION ALLEGEE (uniquement quand on est seul)
        // -----------------------------------------------------------------------
        // Version depouillee de Main_Form.LoadConfiguration() : pas de comboBox,
        // pas de textBox, juste les chemins.
        //
        // Choix de l'installation de DCS, dans cet ordre :
        //   1. celle passee en --saved-games (l'appelant sait, on ne devine pas)
        //   2. celle dont le dossier de campagne contient camp_status.lua, donc
        //      celle qui a vraiment une fin de mission en attente
        //   3. celle dont le dossier de campagne existe, meme sans debriefing
        //   4. celle marquee "display" (la derniere selectionnee dans l'interface)
        //
        // L'etape 2 est indispensable : la meme campagne peut exister dans deux
        // installations, et sans elle on debriefe une copie ou rien n'a ete joue.
        // -----------------------------------------------------------------------
        private static bool LoadMinimalConfig(string campaignName)
        {
            string pathFile = Path.Combine(ParamManager.pathManager, "options.txt");

            if (!File.Exists(pathFile))
            {
                FormUtils.LogRegister("DebriefCli | options.txt introuvable : " + pathFile);
                return false;
            }

            try
            {
                ParamConf.configDictionary.Clear();

                foreach (string line in File.ReadAllLines(pathFile))
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.Contains("="))
                        continue;

                    string[] parts = line.Split(new[] { '=' }, 2);
                    ParamConf.configDictionary[parts[0].Trim()] = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "DebriefCli_LoadMinimalConfig", pathFile, false, true);
                return false;
            }

            // Tous les ids de configuration presents dans le fichier ("config_<id>_...").
            var configIds = new List<int>();

            foreach (string key in ParamConf.configDictionary.Keys)
            {
                if (!key.StartsWith("config_"))
                    continue;

                int nextUnderscore = key.IndexOf('_', 7);

                if (nextUnderscore <= 7)
                    continue;

                int id;
                if (int.TryParse(key.Substring(7, nextUnderscore - 7), out id) && !configIds.Contains(id))
                    configIds.Add(id);
            }

            string askedSavedGames = GetArgValue(FlagSavedGames);
            string displayName;
            ParamConf.configDictionary.TryGetValue("display", out displayName);

            int askedId = 0;      // 1. --saved-games
            int withDebriefId = 0; // 2. camp_status.lua present
            int existsId = 0;      // 3. dossier present
            int displayId = 0;     // 4. configuration affichee

            foreach (int id in configIds)
            {
                string prefix = "config_" + id + "_";

                string savedGames;
                if (!ParamConf.configDictionary.TryGetValue(prefix + "pathSavedGames", out savedGames) || string.IsNullOrWhiteSpace(savedGames))
                    continue;

                if (askedId == 0 && SamePath(savedGames, askedSavedGames))
                    askedId = id;

                string campaignFolder = CampaignFolder(savedGames, campaignName);

                if (Directory.Exists(campaignFolder))
                {
                    if (existsId == 0)
                        existsId = id;

                    if (withDebriefId == 0 && File.Exists(Path.Combine(campaignFolder, EndOfMissionFile)))
                        withDebriefId = id;
                }

                string name;
                if (displayId == 0 && !string.IsNullOrEmpty(displayName) &&
                    ParamConf.configDictionary.TryGetValue(prefix, out name) &&
                    name == displayName)
                {
                    displayId = id;
                }
            }

            int chosenId;
            string reason;

            if (askedId != 0)
            {
                chosenId = askedId;
                reason = "demandee en --saved-games";
            }
            else if (withDebriefId != 0)
            {
                chosenId = withDebriefId;
                reason = "seule a contenir " + EndOfMissionFile;
            }
            else if (existsId != 0)
            {
                chosenId = existsId;
                reason = "dossier de campagne present (pas de fin de mission en attente)";
            }
            else
            {
                chosenId = displayId;
                reason = "repli sur la configuration affichee";
            }

            // --saved-games pointe vers une installation que options.txt ne connait
            // pas : on l'utilise quand meme plutot que de refuser.
            if (chosenId == 0 && !string.IsNullOrWhiteSpace(askedSavedGames) && Directory.Exists(askedSavedGames))
            {
                ParamConf.PATH_SavedGames_DCS = askedSavedGames;
                ParamConf.CurrentConfigName = "(hors options.txt)";

                FormUtils.LogRegister("DebriefCli | installation hors options.txt utilisee telle quelle : " + askedSavedGames);
                return true;
            }

            if (chosenId == 0)
            {
                FormUtils.LogRegister("DebriefCli | aucune configuration ne contient la campagne " + campaignName);
                return false;
            }

            string chosenPrefix = "config_" + chosenId + "_";

            ParamConf.NumSelectConfig = chosenId;

            string tmp;
            ParamConf.CurrentConfigName = ParamConf.configDictionary.TryGetValue(chosenPrefix, out tmp) ? tmp : "Main";
            ParamConf.PATH_DCS_Root = ParamConf.configDictionary.TryGetValue(chosenPrefix + "pathDCS", out tmp) ? tmp : "";
            ParamConf.PATH_SavedGames_DCS = ParamConf.configDictionary.TryGetValue(chosenPrefix + "pathSavedGames", out tmp) ? tmp : "";
            ParamConf.PATH_OVGME_MOD = ParamConf.configDictionary.TryGetValue(chosenPrefix + "pathOVGME", out tmp) ? tmp : "";

            if (ParamConf.configDictionary.TryGetValue("verScriptsMod", out tmp) && !string.IsNullOrWhiteSpace(tmp))
                ParamScriptsMod.verScriptsMod = tmp;

            FormUtils.LogRegister("DebriefCli | configuration " + ParamConf.CurrentConfigName + " (id " + chosenId + ") retenue : " + reason);
            FormUtils.LogRegister("DebriefCli | Saved Games = " + ParamConf.PATH_SavedGames_DCS);

            return !string.IsNullOrWhiteSpace(ParamConf.PATH_SavedGames_DCS);
        }
    }
}
