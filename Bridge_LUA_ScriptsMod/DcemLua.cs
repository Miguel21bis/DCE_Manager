using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // ---------------------------------------------------------------------------
    // Point de passage UNIQUE entre DCE_Manager (C#) et ScriptsMod (Lua).
    //
    // Tout ce qui, dans le reste du projet, construisait un chemin vers un .lua ou
    // ouvrait un état NLua passe désormais par ici. Objectif : un seul nom de
    // fichier Lua écrit en dur dans tout le C#, DCEM_Bootstrap.lua.
    //
    // DEUX FAÇONS DONT LE C# CONSOMME UN FICHIER LUA - ne pas les confondre :
    //
    //   1) fichiers EXÉCUTÉS (lua.DoFile) : DCEM_Function.lua, UTIL_Data.lua...
    //      Un relais posé à l'ancien emplacement les couvre : le relais fait un
    //      dofile vers le vrai fichier, le C# ne voit rien.
    //
    //   2) fichiers LUS COMME DU TEXTE (File.ReadAllLines) : UTIL_REF_conf_mod.lua,
    //      UTIL_REF_camp_init.lua. Ceux-là, UN RELAIS NE LES SAUVE PAS - le C# lit
    //      les 35 lignes du relais au lieu du template, et reconstruit conf_mod.lua
    //      à partir de rien. Pour eux il faut le VRAI chemin, d'où Resolve().
    //
    // Resolve() ne réimplémente pas la règle de recherche : il demande à Lua sa
    // liste IncludeDirs (une seule fois par session, via le bootstrap) et se
    // contente de l'appliquer. Ajouter un dossier côté Lua reste donc une ligne de
    // Lua, sans recompilation.
    // ---------------------------------------------------------------------------
    internal static class DcemLua
    {
        // -----------------------------------------------------------------------
        // NIVEAU D'API - règle d'incrément
        // -----------------------------------------------------------------------
        // RequiredApiLevel = le plus PETIT niveau de ScriptsMod qui fournit tout ce
        // que ce DCE_Manager utilise. Il est posé dans la globale DCEM_API_REQUIRED
        // avant le chargement du bootstrap, et ScriptsMod le compare à son propre
        // SCRIPTSMOD_API.level / .min.
        //
        // ON L'INCRÉMENTE quand le C# se met à dépendre de quelque chose de neuf
        // côté Lua :
        //   - il appelle une fonction globale qui n'existait pas avant
        //   - il lit une table/un champ que l'ancien ScriptsMod ne produisait pas
        //   - il compte sur un fichier chargé par le bootstrap et pas avant
        //   - la signature ou le sens d'un retour Lua a changé
        //
        // ON NE L'INCRÉMENTE PAS quand :
        //   - un fichier Lua est déplacé ou renommé (c'est Resolve/Include qui gère)
        //   - le C# change en interne sans rien demander de plus au Lua
        //   - on ajoute un fichier Lua que le C# n'utilise pas
        //
        // Côté Lua, symétriquement : SCRIPTSMOD_API.level monte quand le contrat
        // change de forme, SCRIPTSMOD_API.min ne monte QUE le jour où les relais
        // sont supprimés (à partir de là les vieux DCE_Manager sont refusés avec un
        // message clair au lieu de planter).
        //
        // Test avant d'incrémenter : « un ScriptsMod de la version précédente
        // ferait-il encore tourner ce DCE_Manager ? » Si oui, on ne touche à rien.
        // -----------------------------------------------------------------------
        public const int RequiredApiLevel = 2;

        // Le seul nom de fichier Lua écrit en dur dans tout le projet C#.
        private const string BootstrapFile = "DCEM_Bootstrap.lua";

        // Suffixe du dossier ScriptsMod.XX
        public static string PackageVersion = "NG";

        private static string _dceRoot;
        private static List<string> _includeDirs;
        private static readonly List<string> _warnings = new List<string>();
        private static bool _sessionStarted;
        private static bool _warningsShown;

        // -----------------------------------------------------------------------
        // 1. CHEMINS
        // -----------------------------------------------------------------------

        // ATTENTION : SEUL ENDROIT DU PROJET où la racine DCE est calculée.
        // ParamConf.PATH_SavedGames_DCS a changé de comportement ; le jour où la
        // bonne source est arrêtée, c'est cette propriété (ou un
        // DcemLua.DceRoot = ... au démarrage) qu'on modifie, et rien d'autre.
        public static string DceRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_dceRoot))
                    return _dceRoot;

                return Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE");
            }
            set { _dceRoot = value; }
        }

        public static string ScriptsModPath
        {
            get { return Path.Combine(DceRoot, "ScriptsMod." + PackageVersion); }
        }

        public static string CampaignsPath
        {
            get { return Path.Combine(DceRoot, @"Missions\Campaigns"); }
        }

        public static string CampaignPath(string campaignName)
        {
            return Path.Combine(CampaignsPath, campaignName);
        }

        // ex: CampaignInitFile(nom, "conf_mod.lua") -> ...\Campaigns\<nom>\Init\conf_mod.lua
        public static string CampaignInitFile(string campaignName, string fileName)
        {
            return Path.Combine(CampaignPath(campaignName), "Init", fileName);
        }

        // Lua n'aime pas les antislashs dans les chaînes qu'on lui pose ("\M" est
        // une séquence d'échappement invalide dès qu'un fichier généré les recopie).
        public static string ToLuaPath(string path)
        {
            return path == null ? "" : path.Replace('\\', '/');
        }

        // Où le fichier <name> se trouve VRAIMENT aujourd'hui, en appliquant l'ordre
        // de recherche déclaré par ScriptsMod (UTIL/, DATA/, racine...).
        // Si rien n'est trouvé, renvoie le chemin racine : l'appelant fera son
        // File.Exists() habituel et affichera son propre message.
        public static string Resolve(string fileName)
        {
            EnsureSession();

            foreach (string dir in _includeDirs)
            {
                string candidate = Path.Combine(ScriptsModPath, dir.Replace('/', '\\'), fileName);

                if (File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(ScriptsModPath, fileName);
        }

        // -----------------------------------------------------------------------
        // 2. ÉTATS LUA
        // -----------------------------------------------------------------------

        // Un état nu, juste avec le bon encodage. Pour les fichiers de données purs
        // (conf_mod.lua, camp_init.lua) qui n'ont besoin d'aucun contexte.
        //
        // Encodage : les .lua du dépôt sont en UTF-8 sans BOM (vérifié). KeraLua,
        // lui, part sur de l'ASCII par défaut, ce qui hache les accents dans tout
        // ce qui fait l'aller-retour Lua -> C#.
        public static Lua NewState()
        {
            Lua lua = new Lua();
            lua.State.Encoding = Encoding.UTF8;
            return lua;
        }

        // Un état prêt à faire tourner du ScriptsMod : globales posées, bootstrap
        // chargé, avertissements collectés.
        // campaignName peut être null (chargement hors campagne).
        public static Lua NewCampaignState(string campaignName)
        {
            Lua lua = NewState();
            Bootstrap(lua, campaignName);
            return lua;
        }

        // Pose le contrat C# -> Lua puis charge le point d'entrée unique.
        public static void Bootstrap(Lua lua, string campaignName)
        {
            string scriptsMod = ScriptsModPath;

            lua["pathScriptsMod"] = ToLuaPath(scriptsMod);
            lua["DCEM_API_REQUIRED"] = RequiredApiLevel;
            lua["generator"] = "DCE_Manager";

            // UTIL_Include.lua lit VersionPackageICM (majuscule) ; du code plus
            // ancien lit versionPackageICM. On pose les deux, ça ne coûte rien.
            lua["VersionPackageICM"] = PackageVersion;
            lua["versionPackageICM"] = PackageVersion;

            lua["PATH_SavedGames_DCS"] = ToLuaPath(ParamConf.PATH_SavedGames_DCS);

            if (!string.IsNullOrEmpty(campaignName))
            {
                string campaign = CampaignPath(campaignName);
                lua["pathCampaign"] = ToLuaPath(campaign);

                // DCEM_Function.lua écrit ses fichiers de trace dans Debug\ et
                // plante si le dossier n'existe pas.
                Directory.CreateDirectory(Path.Combine(campaign, "Debug"));
            }

            // Plusieurs fichiers historiques testent Debug.debug sans vérifier que
            // Debug existe. Le bootstrap le pose aussi, on garde ça pour le repli.
            //
            // DoString et pas lua["Debug"] = new Dictionary<...> : une Dictionary
            // posée en globale arrive côté Lua comme un objet CLR, et « Debug.debug »
            // n'y répond pas comme sur une vraie table. Ici on veut une table Lua.
            lua.DoString("Debug = Debug or { debug = false }");

            string bootstrap = Path.Combine(scriptsMod, BootstrapFile);

            if (File.Exists(bootstrap))
            {
                lua.DoFile(bootstrap);
                CollectWarnings(lua);
            }
            else
            {
                // ScriptsMod antérieur au découpage : tout est à la racine, ce qui
                // reste vrai puisque la racine fait partie des dossiers fouillés.
                AddWarning("ScriptsMod ne fournit pas " + BootstrapFile
                    + " : version antérieure à l'API " + RequiredApiLevel
                    + ". Repli sur les chemins historiques (racine de ScriptsMod). "
                    + "Mettez à jour ScriptsMod.");
            }
        }

        // Ouvre un état une fois par session, uniquement pour récupérer la liste des
        // dossiers déclarée par ScriptsMod et faire remonter la vérification de
        // compatibilité au plus tôt.
        private static void EnsureSession()
        {
            if (_sessionStarted)
                return;

            _sessionStarted = true;
            _includeDirs = new List<string>();

            try
            {
                using (Lua lua = NewState())
                {
                    Bootstrap(lua, null);

                    LuaTable dirs = lua["IncludeDirs"] as LuaTable;

                    if (dirs != null)
                    {
                        int i = 1;

                        while (true)
                        {
                            object v = dirs[i];

                            if (v == null)
                                break;

                            _includeDirs.Add(v.ToString());
                            i++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddWarning("Échec du chargement de " + BootstrapFile + " : " + ex.Message
                    + ". Repli sur les chemins historiques.");
            }

            // Pas de bootstrap, ou bootstrap muet : on ne connaît que la racine.
            if (_includeDirs.Count == 0)
                _includeDirs.Add("");
        }

        // Force une nouvelle session (après un changement de configuration DCSA/DCSB,
        // ou une mise à jour de ScriptsMod pendant que l'appli tourne).
        public static void ResetSession()
        {
            _sessionStarted = false;
            _includeDirs = null;
            _warnings.Clear();
            _warningsShown = false;
        }

        // -----------------------------------------------------------------------
        // 3. AVERTISSEMENTS DE COMPATIBILITÉ
        // -----------------------------------------------------------------------

        public static IList<string> Warnings
        {
            get { return _warnings; }
        }

        private static void AddWarning(string message)
        {
            if (string.IsNullOrEmpty(message) || _warnings.Contains(message))
                return;

            _warnings.Add(message);
            FormUtils.LogRegister("DcemLua | COMPAT | " + message);
        }

        private static void CollectWarnings(Lua lua)
        {
            LuaTable compat = lua["DCEM_Compat"] as LuaTable;

            if (compat == null)
                return;

            LuaTable warnings = compat["warnings"] as LuaTable;

            if (warnings == null)
                return;

            int i = 1;

            while (true)
            {
                object v = warnings[i];

                if (v == null)
                    break;

                AddWarning(v.ToString());
                i++;
            }
        }

        // À appeler une fois au démarrage (Main_Form.Load) et/ou après le premier
        // chargement de campagne. Ne bloque rien : s'il n'y a aucun avertissement,
        // l'utilisateur ne voit jamais cette boîte.
        public static void ShowWarningsOnce(IWin32Window owner)
        {
            EnsureSession();

            if (_warningsShown || _warnings.Count == 0)
                return;

            _warningsShown = true;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DCE_Manager et ScriptsMod ne sont pas de la même génération.");
            sb.AppendLine("Tout continue de fonctionner, mais pensez à mettre à jour :");
            sb.AppendLine();

            foreach (string w in _warnings)
                sb.AppendLine("- " + w);

            MessageBox.Show(owner, sb.ToString(), "Compatibilité ScriptsMod",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
