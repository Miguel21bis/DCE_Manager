using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Controls; // Pour que le DropZoneControl soit reconnu
using DCE_Manager.Parameters;
using DCE_Manager.UserControls;
using DCE_Manager.Utils;
using Ookii.Dialogs.WinForms;
using System.Threading.Tasks;

namespace DCE_Manager
{
    // Même namespace, même nom de classe (Main_Form)
    public partial class Main_Form
    {
        private string _selectedCampaignZipPath = "";


        private void InitializeDCS_Installation_Path()
        {
            // 1. Récupérer le chemin actuellement affiché dans la TextBox
            string DCS_RootPath = textBox_PATH_DCS_Root.Text;

            // 2. Vérifier si le chemin n'est pas vide et si le dossier "bin" existe dedans
            string comb_DCS_RootPath = !string.IsNullOrWhiteSpace(DCS_RootPath) ? Path.Combine(DCS_RootPath, "bin") : string.Empty;

            if (!string.IsNullOrWhiteSpace(DCS_RootPath) && Directory.Exists(comb_DCS_RootPath))
            {
                // Le chemin est valide !
                pic_DCS_Root.Image = Properties.Resources.icons8_ok_24;
                Label_subLabel_DCS.Text = DCS_RootPath;
                ParamConf.PATH_DCS_Root = DCS_RootPath;

                homeView.pic_Accueil_DCS_Status.Image = Properties.Resources.icons8_ok_24;
                homeView.label_Accueil_PATH_DCS.Text = TruncatePathLeft(DCS_RootPath);
            }
            else
            {
                // Le chemin est vide ou invalide
                pic_DCS_Root.Image = Properties.Resources.icons8_warning_blue_30;
                Label_subLabel_DCS.Text = "";
                ParamConf.PATH_DCS_Root = "";

                homeView.pic_Accueil_DCS_Status.Image = Properties.Resources.icons8_warning_blue_30;
                homeView.label_Accueil_PATH_DCS.Text = "";
            }

            //---textBox_SavedGames

            // 1. Récupérer le chemin actuellement affiché dans la TextBox
            string savedGamesPath = textBox_SavedGames.Text;

            // 2. Vérifier si le chemin n'est pas vide et si le dossier "log" existe dedans
            string comb_savedGamesPath = !string.IsNullOrWhiteSpace(savedGamesPath) ? Path.Combine(savedGamesPath, "Logs") : string.Empty;

            if (!string.IsNullOrWhiteSpace(savedGamesPath) && Directory.Exists(comb_savedGamesPath))
            {
                // Le chemin est valide !
                pic_SavedGame.Image = Properties.Resources.icons8_ok_24;
                label_subLabel_SavedGame_Folder.Text = savedGamesPath;
                ParamConf.PATH_SavedGames_DCS = savedGamesPath;
                //ParamConf.SavedGames = true;
                homeView.pic_Accueil_SavedGames_Satus.Image = Properties.Resources.icons8_ok_24;
                homeView.label_Accueil_PATH_SavedGames.Text = TruncatePathLeft(savedGamesPath);
            }
            else
            {
                // Le chemin est vide ou invalide
                pic_SavedGame.Image = Properties.Resources.icons8_warning_blue_30;
                label_subLabel_SavedGame_Folder.Text = "";
                ParamConf.PATH_SavedGames_DCS = "";
                //ParamConf.test_DCS_Root = false;
                homeView.pic_Accueil_SavedGames_Satus.Image = Properties.Resources.icons8_warning_blue_30;
                homeView.label_Accueil_PATH_SavedGames.Text = "";
            }

            //--OvGMEPath
            // 1. Récupérer le chemin actuellement affiché dans la TextBox
            string ovGMEPath = textBox_OvGME.Text;

            // 2. Vérifier si le chemin n'est pas vide et si le dossier existe
            // (Optionnel : tu peux aussi vérifier Path.Combine(ovGMEPath, "ovgme.exe") si tu veux être sûr)
            if (!string.IsNullOrWhiteSpace(ovGMEPath) && Directory.Exists(ovGMEPath))
            {
                // Le chemin est valide !
                //pic_OVGME.Image = Properties.Resources.icons8_ok_24;
                label_sub_OVGME.Text = ovGMEPath;
                ParamConf.PATH_OVGME_MOD = ovGMEPath;

                // Si tu as une variable de test comme pour DCS, n'oublie pas de la mettre à true :
                // ParamConf.OvGME_Root = true;
                homeView.pic_Accueil_sanitize_status.Image = Properties.Resources.icons8_ok_24;
                //homeView.label_Accueil_PATH_ovgme.Text = TruncatePathLeft(ovGMEPath);
            }
            else
            {
                // Le chemin est vide ou invalide
                //pic_OVGME.Image = Properties.Resources.icons8_warning_blue_30;
                label_sub_OVGME.Text = ""; // Ou laisser vide "" si tu préfères
                ParamConf.PATH_OVGME_MOD = "";

                // ParamConf.OvGME_Root = false;
                homeView.pic_Accueil_sanitize_status.Image = Properties.Resources.icons8_warning_blue_30;
                //homeView.label_Accueil_PATH_ovgme.Text = "";
            }
        }



        private void m_But_Install_Browse_DcsPath_Click(object sender, EventArgs e)
        {
            // Utiliser VistaFolderBrowserDialog pour une meilleure sélection de dossiers
            using (VistaFolderBrowserDialog folderBrowserDialog = new VistaFolderBrowserDialog())
            {
                // Vérifiez si VistaFolderBrowserDialog est pris en charge (pour les versions plus anciennes de Windows)
                if (!VistaFolderBrowserDialog.IsVistaFolderDialogSupported)
                {
                    MessageBox.Show("This feature is not supported on your version of Windows.");
                    return;
                }

                // Définir les propriétés du dialogue de dossier
                folderBrowserDialog.Description = "Select a folder";
                folderBrowserDialog.UseDescriptionForTitle = true; // Utiliser la description comme titre
                folderBrowserDialog.ShowNewFolderButton = false; // Permet de créer un nouveau dossier

                // Pré-sélectionner le répertoire textBox_PATH_DCS_Root.Text
                string savedGamesPath = textBox_PATH_DCS_Root.Text;
                if (Directory.Exists(savedGamesPath))
                {
                    folderBrowserDialog.SelectedPath = savedGamesPath;
                }

                // Afficher le dialogue et vérifier si l'utilisateur a sélectionné un dossier
                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {

                    // Récupérer le chemin du dossier sélectionné
                    string folderPath = folderBrowserDialog.SelectedPath;

                    // Afficher le chemin dans la TextBox
                    textBox_PATH_DCS_Root.Text = folderPath;
                    ParamConf.test_DCS_Root = true;

                    string combPath = Path.Combine(folderPath, "bin");
                    if (Directory.Exists(combPath))
                    {
                        // Le chemin est valide !
                        pic_DCS_Root.Image = Properties.Resources.icons8_ok_24;
                        Label_subLabel_DCS.Text = folderPath;
                        ParamConf.PATH_DCS_Root = folderPath;
                    }
                    else
                    {
                        // Le chemin est vide ou invalide
                        pic_DCS_Root.Image = Properties.Resources.icons8_warning_blue_30;
                        Label_subLabel_DCS.Text = "";
                        ParamConf.PATH_DCS_Root = "";

                        //MessageBox.Show("This folder does not seem to be the one of DCS.", words[words.Length - 1]);
                        MessageBox.Show("This directory does not appear to be the root folder of DCS: \r\n" + folderPath, "Error");
                    }
                }
                else
                {
                    //FormUtils.ShowErrorMessage("No folder selected");
                }
            }
        }

        private void m_But_Install_Browse_SavedGame_Click(object sender, EventArgs e)
        {
            // Utiliser VistaFolderBrowserDialog pour une meilleure sélection de dossiers
            using (VistaFolderBrowserDialog folderBrowserDialog = new VistaFolderBrowserDialog())
            {
                // Vérifiez si VistaFolderBrowserDialog est pris en charge (pour les versions plus anciennes de Windows)
                if (!VistaFolderBrowserDialog.IsVistaFolderDialogSupported)
                {
                    MessageBox.Show("This feature is not supported on your version of Windows.");
                    return;
                }

                // Définir les propriétés du dialogue de dossier
                folderBrowserDialog.Description = "Select a folder";
                folderBrowserDialog.UseDescriptionForTitle = true; // Utiliser la description comme titre
                folderBrowserDialog.ShowNewFolderButton = true; // Permet de créer un nouveau dossier

                // Pré-sélectionner le répertoire "Saved Games"
                string savedGamesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Saved Games";
                if (Directory.Exists(savedGamesPath))
                {
                    folderBrowserDialog.SelectedPath = savedGamesPath;
                    FormUtils.ShowErrorMessage("PasseA Directory.Exists");
                }
                else if (Directory.Exists(textBox_SavedGames.Text))
                {
                    folderBrowserDialog.SelectedPath = textBox_SavedGames.Text;
                }

                // Afficher le dialogue et vérifier si l'utilisateur a sélectionné un dossier
                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {

                    // Récupérer le chemin du dossier sélectionné
                    string folderPath = folderBrowserDialog.SelectedPath;

                    // Afficher le chemin dans la TextBox
                    textBox_SavedGames.Text = folderPath;
                    //ParamConf.PATH_OVGME_MOD = true;

                    string combPath = Path.Combine(folderPath, "Logs");
                    if (Directory.Exists(combPath))
                    {
                        // Le chemin est valide !
                        pic_SavedGame.Image = Properties.Resources.icons8_ok_24;
                        label_subLabel_SavedGame_Folder.Text = folderPath;
                    }
                    else
                    {
                        // Le chemin est vide ou invalide
                        pic_SavedGame.Image = Properties.Resources.icons8_warning_blue_30;
                        label_subLabel_SavedGame_Folder.Text = "";

                        MessageBox.Show("This directory does not seem to be the DCS Saved Games folder: \r\n" + folderPath, "Error");
                    }
                }
                else
                {
                    // Le chemin est vide ou invalide
                    pic_SavedGame.Image = Properties.Resources.icons8_warning_blue_30;
                    label_subLabel_SavedGame_Folder.Text = "";
                    //FormUtils.ShowErrorMessage("No folder selected");
                }
            }
        }

        private void m_but_Install_Browse_OVGME_Click(object sender, EventArgs e)
        {
            // Utiliser VistaFolderBrowserDialog pour une meilleure sélection de dossiers
            using (VistaFolderBrowserDialog folderBrowserDialog = new VistaFolderBrowserDialog())
            {
                // Vérifiez si VistaFolderBrowserDialog est pris en charge (pour les versions plus anciennes de Windows)
                if (!VistaFolderBrowserDialog.IsVistaFolderDialogSupported)
                {
                    MessageBox.Show("This feature is not supported on your version of Windows.");
                    return;
                }

                // Définir les propriétés du dialogue de dossier
                folderBrowserDialog.Description = "Select a folder";
                folderBrowserDialog.UseDescriptionForTitle = true; // Utiliser la description comme titre
                folderBrowserDialog.ShowNewFolderButton = true; // Permet de créer un nouveau dossier

                // Pré-sélectionner le répertoire "Saved Games"
                string savedGamesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Saved Games";
                if (Directory.Exists(savedGamesPath))
                {
                    folderBrowserDialog.SelectedPath = savedGamesPath;
                }
                else if (Directory.Exists(textBox_OvGME.Text))
                {
                    folderBrowserDialog.SelectedPath = textBox_OvGME.Text;
                }

                // Afficher le dialogue et vérifier si l'utilisateur a sélectionné un dossier
                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {

                    // Récupérer le chemin du dossier sélectionné
                    string folderPath = folderBrowserDialog.SelectedPath;

                    // Afficher le chemin dans la TextBox
                    textBox_OvGME.Text = folderPath;

                    // Le chemin est valide !
                    //pic_OVGME.Image = Properties.Resources.icons8_ok_24;
                    label_sub_OVGME.Text = folderPath;

                }
                else
                {
                    // Le chemin est vide ou invalide
                    //pic_OVGME.Image = Properties.Resources.icons8_warning_blue_30;
                    label_sub_OVGME.Text = ""; // Ou laisser vide "" si tu préfères

                    //FormUtils.ShowErrorMessage("No folder selected");
                }
            }

        }

        private async void button_InstallCampaign_Click(object sender, EventArgs e)
        {

            string combPathDCS = Path.Combine(textBox_PATH_DCS_Root.Text, "bin");
            if (Directory.Exists(combPathDCS))
            {
                ParamConf.test_DCS_Root = true;
            }
            else
            {
                MessageBox.Show("This directory does not appear to be the root folder of DCS: \r\n" + textBox_PATH_DCS_Root.Text, "Error");
                //button_InstallCampaign.Visible = false;
                return;
            }



            string combPathSavedGame = Path.Combine(textBox_SavedGames.Text, "Logs");
            if (Directory.Exists(combPathSavedGame))
            {
                ParamConf.test_DCS_SavedGames = true;
            }
            else
            {
                MessageBox.Show("This directory does not seem to be the DCS Saved Games folder: \r\n" + textBox_SavedGames.Text, "Error");
                //button_InstallCampaign.Visible = false;
                return;
            }


            string combPathDCE = Path.Combine(textBox_SavedGames.Text, @"Mods\tech\DCE\Missions\Campaigns");
            if (Directory.Exists(combPathDCE))
            {
                ParamConf.test_DCE_alreadyInstalled = true;
            }

            Cursor.Current = Cursors.WaitCursor;

            string NameCampaign = "";

            string zipPath = _selectedCampaignZipPath;

            if (File.Exists(_selectedCampaignZipPath))
            {
                // Format léger "Export campagne" (bouton Export de la grid, une seule campagne) :
                // pris en charge à part, tout le reste de cette méthode est conçu pour l'autre
                // format, le paquet de distribution complet (ScriptsMod + Mods\tech\... + SavedGames).
                if (CampaignImporter.LooksLikeExportedCampaignZip(zipPath))
                {
                    string campaignsRootImport = Path.Combine(textBox_SavedGames.Text, @"Mods\tech\DCE\Missions\Campaigns");
                    string savedGamesRootImport = textBox_SavedGames.Text;

                    string importedName = null;
                    Exception extractionError = null;

                    // Extraction sur un thread du pool + petite form de progression maison.
                    // Le "await" rend la main à la boucle de messages pendant l'opération :
                    // c'est ce qui évite le ContextSwitchDeadlock sur les gros packages.
                    FormUtils.LogRegister("Import | format 'Export campagne' détecté, extraction de " + zipPath);

                    using (var progressForm = new CampaignProgress_Form("Import campaign"))
                    {
                        progressForm.Show(this);
                        this.Enabled = false;   // évite les clics parasites pendant l'extraction

                        var progress = new Progress<Tuple<int, string>>(
                            p => progressForm.UpdateProgress(new CampaignProgressInfo
                            {
                                OverallPercent = p.Item1,
                                OverallText = "Extracting campaign package...",
                                DetailPercent = p.Item1,
                                DetailText = p.Item2
                            }));

                        IProgress<Tuple<int, string>> reporter = progress;

                        try
                        {
                            importedName = await Task.Run(() =>
                                CampaignImporter.ExtractExportedCampaignZip(
                                    zipPath, campaignsRootImport, savedGamesRootImport,
                                    (percent, parentFolder) => reporter.Report(Tuple.Create(percent, parentFolder))));
                        }
                        catch (Exception ex)
                        {
                            extractionError = ex;
                        }
                        finally
                        {
                            this.Enabled = true;
                        }
                    }

                    Cursor.Current = Cursors.Default;

                    if (extractionError != null)
                    {
                        FormUtils.ErrorGeneral_BoxOrLog(extractionError, "Import campaign package", zipPath, true, true);
                        return;
                    }

                    // Régénère path.bat/.cmp/.png avec les chemins DCS locaux et rafraîchit la
                    // grid - même mécanique que le bouton 🔧 Repair.
                    await CampaignGridLeft.RepairCampaignAsync(importedName, "Import");

                    _selectedCampaignZipPath = "";
                    dropZoneControl1.ResetState();

                    _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, ParamConf.PATH_SavedGames_DCS);

                    Cursor.Current = Cursors.Default;
                    return;
                }

                // Cherche le titre de la campagne dans le zip
                string titleProblem;
                NameCampaign = ReadCampaignTitleFromZip(zipPath, out titleProblem);

                if (NameCampaign == "")
                {
                    MessageBox.Show(titleProblem, "Error");
                    Cursor.Current = Cursors.Default;
                    return;
                }

                ParamCampaign.NameCampaign = NameCampaign;

                if (ParamConf.test_DCE_alreadyInstalled == false && TestFile.presenceOobAirInit && TestFile.presenceCampInit)
                {
                    DialogResult result = MessageBox.Show(
                        "There are no DCE directories, so you need to create them.",
                        "Create DCE directory",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (result == DialogResult.Yes)
                    {
                        FormUtils.CreateDCE_Folder();

                        if (Directory.Exists(combPathDCE))
                        {
                            ParamConf.test_DCE_alreadyInstalled = true;
                        }
                    }
                    else if (result == DialogResult.No)
                    {
                        return;
                    }

                }


                //---------------REGARDE si la CAMPAGNE est déjà installée ----------------------

                ParamCampaign.PathCampaign = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaign.NameCampaign;

                if (Directory.Exists(ParamCampaign.PathCampaign))
                {
                    DialogResult result = MessageBox.Show(
                    "The campaign already seems to be already installed. Do you want to overrun it?.",
                    "Attention",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                 );

                    if (result == DialogResult.No)
                    {
                        return;
                    }
                }



                //---------------ENREGISTRE ici les fichiers de la campagne ----------------------

                ExtractZipFileToDirectory(_selectedCampaignZipPath, true);

                TestFile.ScriptsMod = "NG";

                string pathFile = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaign.NameCampaign + @"\Init\path.bat";

                string textPathBat = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                               "set \"pathDCS=" + ParamConf.PATH_DCS_Root + "\\\"\r\n" +
                               "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                               "set \"pathSavedGames=" + ParamConf.PATH_SavedGames_DCS + "\\\"\r\n" +
                               "REM DCE ScriptMod version not any / or \\ and no space before and after = \r\n" +
                               "set \"versionPackageICM=" + TestFile.ScriptsMod + "\"\r\n" +
                               "\r\n" +
                               "\r\n" +
                               "REM After each change, You must launch the FirsMission.bat for it to be taken into account.";

                Directory.CreateDirectory(Path.GetDirectoryName(pathFile));

                System.IO.File.WriteAllText(pathFile, textPathBat);

                //******************new system

                string pathStatus = textBox_SavedGames.Text.Replace(@"\", "/") + "/";
                string campaignsRootPath = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns");

                // Tous les .miz de la campagne rangés à côté de son dossier (_first, _ongoing, ou autres noms futurs) :
                // on ne dépend plus d'une liste de noms de fichiers attendus.
                int nbMizPatched = 0;

                if (Directory.Exists(campaignsRootPath))
                {
                    foreach (string mizPath in Directory.GetFiles(campaignsRootPath, ParamCampaign.NameCampaign + "*.miz"))
                    {
                        if (PatchCampStatusPathInMiz(mizPath, pathStatus))
                            nbMizPatched++;
                    }
                }

                if (nbMizPatched == 0)
                {
                    MessageBox.Show("No mission file could be updated with your Saved Games path.\r\nThe campaign may not start correctly. See log.txt for details.", "Warning");
                }//affiche la ligne campagne en enlevant la derniere campagne, (

                string rappel = DcsPatcher.IsPatched(ParamConf.PATH_DCS_Root)
    ? ""
    : "\r\n\r\nReminder: enable the 'MissionScripting' option on the Home page.";

                MessageBox.Show(ParamCampaign.NameCampaign + " successfully installed." + rappel, "Information");
                FormUtils.LogRegister(ParamCampaign.NameCampaign + " successfully installed." + rappel);

                //******************FIN du new system


            }
            else
            {
                //button_InstallCampaign.Visible = false;
                MessageBox.Show("Zip file not found", "Error");
            }

            Cursor.Current = Cursors.Default;

            // Installation terminée : on vide la zone de dépôt (nom du zip + bordure verte)
            _selectedCampaignZipPath = "";
            dropZoneControl1.ResetState();
        }

        // Cherche le titre de la campagne dans le zip : ligne title de Init/camp_init.lua.
        // Renvoie "" si on ne trouve pas, et "problem" explique pourquoi (texte affiché à l'utilisateur).
        // Pourquoi : l'ancien code prenait le 1er "title" venu dans n'importe quel fichier contenant "camp_init",
        // et ne distinguait pas "fichier absent" de "ligne title absente".
        private string ReadCampaignTitleFromZip(string zipPath, out string problem)
        {
            problem = "";

            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string p = entry.FullName.Replace('\\', '/');

                    // On veut exactement ".../Init/camp_init.lua" (ou "Init/camp_init.lua" à la racine)
                    if (!p.Equals("Init/camp_init.lua", StringComparison.OrdinalIgnoreCase) &&
                        !p.EndsWith("/Init/camp_init.lua", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using (StreamReader reader = new StreamReader(entry.Open()))
                    {
                        string line;

                        while ((line = reader.ReadLine()) != null)
                        {
                            line = line.Trim();

                            if (line.StartsWith("--"))
                                continue;

                            // Accepte  title = "x"  /  camp.title = "x"  /  ["title"] = "x"
                            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                                line,
                                @"(^|[^\w])title\W*=\s*""([^""]*)""",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

                            if (m.Success && m.Groups[2].Value.Trim() != "")
                                return m.Groups[2].Value.Trim();
                        }
                    }

                    FormUtils.LogRegister("Install | no usable 'title' line in " + p + " (zip: " + zipPath + ")");
                    problem = "Init\\camp_init.lua was found in this ZIP, but it has no 'title' line, or the title is empty.\r\nMake sure this is a valid campaign package.";
                    return "";
                }

                // camp_init.lua absent : on trace le début du contenu pour comprendre la structure du zip
                FormUtils.LogRegister("Install | camp_init.lua not found in " + zipPath + " (" + archive.Entries.Count + " entries). First entries :");

                int n = 0;
                foreach (ZipArchiveEntry e in archive.Entries)
                {
                    FormUtils.LogRegister("   " + e.FullName);
                    if (++n >= 15) break;
                }

                problem = "Init\\camp_init.lua was not found in this ZIP.\r\nMake sure this is a valid campaign package (the ZIP content is written in log.txt).";
                return "";
            }
        }

        // Nom du fichier d'état de campagne dans les .miz : un seul endroit à changer si DCE le renomme.
        private const string CampStatusFileName = "camp_status.lua";

        // Met à jour la ligne ['path'] du camp_status.lua contenu dans un .miz.
        // Retourne true si le .miz a bien été modifié.
        // Pourquoi : l'ancien code attendait "l10n/DEFAULT/camp_status.lua" au caractère près ; un .miz dont les entrées
        // sont écrites avec des \ (ou rangées autrement) était déclaré "introuvable". Ici on cherche par le seul nom du
        // fichier, et on réécrit l'entrée sous son nom d'origine.
        private bool PatchCampStatusPathInMiz(string mizPath, string pathStatus)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "dcem_" + Guid.NewGuid().ToString("N") + ".lua");

            try
            {
                using (ZipArchive archive = ZipFile.Open(mizPath, ZipArchiveMode.Update))
                {
                    ZipArchiveEntry entry = null;

                    foreach (ZipArchiveEntry e in archive.Entries)
                    {
                        string n = e.FullName.Replace('\\', '/');

                        if (n.Equals(CampStatusFileName, StringComparison.OrdinalIgnoreCase) ||
                            n.EndsWith("/" + CampStatusFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            entry = e;
                            break;
                        }
                    }

                    if (entry == null)
                    {
                        FormUtils.LogRegister("Install | no " + CampStatusFileName + " in " + Path.GetFileName(mizPath) + " : nothing to patch.");
                        return false;
                    }

                    string entryName = entry.FullName;   // on garde le nom d'origine tel quel
                    entry.ExtractToFile(tempFile, true);

                    int nbLigneMod = FormUtils.ModifierLigne(tempFile, "['path']", "\t['path'] = '" + pathStatus + "',", 0);
                    if (nbLigneMod < 1)
                    {
                        nbLigneMod = FormUtils.ModifierLigne(tempFile, "[\"path\"]", "\t[\"path\"] = '" + pathStatus + "',", 0);
                    }

                    if (nbLigneMod < 1)
                    {
                        FormUtils.LogRegister("Install | no ['path'] line in " + CampStatusFileName + " of " + Path.GetFileName(mizPath));
                        return false;
                    }

                    entry.Delete();
                    archive.CreateEntryFromFile(tempFile, entryName);
                    return true;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Install | cannot patch " + Path.GetFileName(mizPath) + " : " + ex.Message);
                return false;
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        // Active le mode édition des chemins.
        // Pourquoi : garder l'interface épurée (labels en lecture seule) tant que rien n'est modifié.
        private void but_PATH_Modify_Click(object sender, EventArgs e)
        {
            // On mémorise les valeurs actuelles pour pouvoir les restaurer en cas d'annulation
            _cachedDcsRootPath = textBox_PATH_DCS_Root.Text;
            _cachedSavedGamesPath = textBox_SavedGames.Text;
            _cachedOvGmePath = textBox_OvGME.Text;

            textBox_PATH_DCS_Root.Visible = true;
            textBox_SavedGames.Visible = true;
            textBox_OvGME.Visible = true;

            m_But_Install_Browse_DcsPath.Visible = true;
            m_But_Install_Browse_SavedGame.Visible = true;
            m_but_Install_Browse_OVGME.Visible = true;

            but_PATH_Modify.Visible = false;
            but_PATH_SAVE.Visible = true;
            but_PATH_CANCEL.Visible = true;
        }

        // Valide les nouveaux chemins et les enregistre dans la configuration active.
        private void but_PATH_SAVE_Click(object sender, EventArgs e)
        {
            HidePathEditingControls();

            InitializeDCS_Installation_Path();     // 1. Synchronise ParamConf.PATH_* depuis les TextBox
            Configuration_Form.Save_Config();      // 2. Persiste ces valeurs à jour dans options.txt

            checkBoxMod();
            _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, textBox_SavedGames.Text);
        }

        // Annule l'édition : restaure les chemins précédemment enregistrés.
        private void but_PATH_CANCEL_Click(object sender, EventArgs e)
        {
            textBox_PATH_DCS_Root.Text = _cachedDcsRootPath;
            textBox_SavedGames.Text = _cachedSavedGamesPath;
            textBox_OvGME.Text = _cachedOvGmePath;

            HidePathEditingControls();

            InitializeDCS_Installation_Path();

            checkBoxMod();
        }

        // Factorise le retour au mode "lecture seule", commun à Save et Cancel.
        private void HidePathEditingControls()
        {
            textBox_PATH_DCS_Root.Visible = false;
            textBox_SavedGames.Visible = false;
            textBox_OvGME.Visible = false;

            m_But_Install_Browse_DcsPath.Visible = false;
            m_But_Install_Browse_SavedGame.Visible = false;
            m_but_Install_Browse_OVGME.Visible = false;

            but_PATH_Modify.Visible = true;
            but_PATH_SAVE.Visible = false;
            but_PATH_CANCEL.Visible = false;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr changeInfo);

        // Lancé en administrateur, Windows (UIPI) bloque le glisser-déposer venant de l'Explorateur.
        // On autorise les trois messages utilisés par le dépôt de fichiers (essai : efficacité non garantie).
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            try
            {
                const uint MSGFLT_ALLOW = 1;
                uint[] messages = { 0x0233 /*WM_DROPFILES*/, 0x004A /*WM_COPYDATA*/, 0x0049 /*WM_COPYGLOBALDATA*/ };

                foreach (uint msg in messages)
                {
                    ChangeWindowMessageFilterEx(this.Handle, msg, MSGFLT_ALLOW, IntPtr.Zero);
                    ChangeWindowMessageFilterEx(dropZoneControl1.Handle, msg, MSGFLT_ALLOW, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("OnHandleCreated | drag&drop filter : " + ex.Message);
            }
        }

        public void DropZone_FilesSelected(object sender, string[] files)
        {
            if (files.Length > 0)
            {
                this._selectedCampaignZipPath = files[0];
                dropZoneControl1.SetSelectedFile(files[0]);   // ← bordure verte + nom du fichier affiché
            }
        }

        private string TruncatePathLeft(string path, int maxLength = 27)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
                return path;

            // Conserve les 'maxLength' derniers caractères et ajoute "..." au début
            return "..." + path.Substring(path.Length - maxLength);
        }

    }
}