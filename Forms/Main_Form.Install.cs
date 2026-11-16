using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
using Microsoft.Win32;

namespace DCE_Manager
{
    // Compte-rendu d'une extraction de zip de campagne (voir ExtractZipFileToDirectory dans Main_Form.cs).
    // Pourquoi : avant, les fichiers ignorés ou en erreur disparaissaient sans trace, et le message
    // final disait toujours "successfully installed".
    public class ZipExtractReport
    {
        public int Extracted;                      // fichiers écrits sur le disque
        public int Failed;                         // fichiers qui n'ont pas pu être écrits
        public bool Refused;                       // zip refusé avant d'extraire quoi que ce soit

        // raison -> nombre de fichiers ignorés (détail écrit dans log.txt)
        public Dictionary<string, int> Skipped = new Dictionary<string, int>();

        // problèmes anormaux, à montrer à l'utilisateur à la fin de l'installation
        public List<string> Warnings = new List<string>();

        public int SkippedTotal
        {
            get { return Skipped.Values.Sum(); }
        }

        public void AddSkipped(string reason)
        {
            int n;
            Skipped.TryGetValue(reason, out n);
            Skipped[reason] = n + 1;
        }

        public void AddWarning(string text)
        {
            if (!Warnings.Contains(text))
                Warnings.Add(text);
        }
    }

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

                ZipExtractReport zipReport = ExtractZipFileToDirectory(_selectedCampaignZipPath, true);

                // Zip refusé : le message a déjà été montré. On s'arrête ici (avant, l'installation
                // continuait et finissait quand même par "successfully installed").
                if (zipReport.Refused)
                {
                    Cursor.Current = Cursors.Default;
                    return;
                }

                // Le dossier de la campagne doit porter le même nom que le title de Init\camp_init.lua.
                // Sinon path.bat serait écrit dans un dossier parasite, et aucun .miz ne serait patché.
                if (!Directory.Exists(ParamCampaign.PathCampaign))
                {
                    FormUtils.LogRegister("Install | dossier de campagne introuvable après extraction : " + ParamCampaign.PathCampaign);

                    MessageBox.Show(
                        "The campaign folder was not found after the install:\r\n" + ParamCampaign.PathCampaign +
                        "\r\n\r\nThe title in Init\\camp_init.lua ('" + NameCampaign + "') must be the same as the campaign folder name." +
                        "\r\nPlease check the ZIP.",
                        "Error");

                    Cursor.Current = Cursors.Default;
                    return;
                }

                // Active / Debug / Debriefing ne sont jamais installés avec leur contenu (parties de l'auteur),
                // mais les dossiers vides doivent exister. Sans effet s'ils existent déjà.
                foreach (string sessionFolder in new string[] { "Active", "Debug", "Debriefing" })
                {
                    Directory.CreateDirectory(Path.Combine(ParamCampaign.PathCampaign, sessionFolder));
                }

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

                // Bilan de l'extraction : ce qui a échoué ou ce qui n'était pas reconnu.
                // (Les fichiers ignorés normalement - Active/Debug/Debriefing, ScriptsMod déjà installé - sont dans log.txt.)
                string bilan = "";

                if (zipReport.Failed > 0)
                    bilan += "\r\n\r\n" + zipReport.Failed + " file(s) could not be installed. See log.txt for details.";

                foreach (string warning in zipReport.Warnings)
                    bilan += "\r\n\r\n" + warning;

                bool toutEstOk = zipReport.Failed == 0 && zipReport.Warnings.Count == 0;

                string resultText = ParamCampaign.NameCampaign + (toutEstOk ? " successfully installed." : " installed, but with problems.");

                MessageBox.Show(resultText + bilan + rappel, toutEstOk ? "Information" : "Warning");
                FormUtils.LogRegister(resultText + bilan + rappel);

                // Bascule sur l'onglet Campaigns et se place sur la ligne de la campagne installée
                await ShowInstalledCampaignAsync(ParamCampaign.NameCampaign);

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

        // Après une installation de campagne : affiche l'onglet Campaigns, recharge la liste
        // et se place sur la ligne de la campagne (sélection + défilement).
        // Public : aussi appelé par le catalogue (Updater_Catalog).
        public async Task ShowInstalledCampaignAsync(string campaignFolderName)
        {
            tabControl_LEFT.SelectedTab = tabPageLeft_Campaigns;

            await CampaignGridLeft.LoadCampaignsAsync(selectCampaignName: campaignFolderName);
        }

        // Vrai si fullPath est le dossier "folder" lui-même, ou quelque chose à l'intérieur.
        // Pourquoi : un simple StartsWith(folder) acceptait aussi "...\DCS.openbeta" quand folder = "...\DCS"
        // (même début de chemin, mais un autre dossier). Sert à bloquer les entrées de zip qui sortent du dossier cible.
        internal static bool IsInsideFolder(string fullPath, string folder)
        {
            char[] separators = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

            string root = Path.GetFullPath(folder).TrimEnd(separators);
            string path = fullPath.TrimEnd(separators);

            return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // Vrai si l'entrée de zip (chemin découpé sur "/") est dans Campaigns\<campagne>\Active, \Debug ou \Debriefing.
        // Pourquoi : ces dossiers contiennent les parties en cours de l'auteur, on ne les installe pas.
        // On teste le NOM EXACT du dossier, juste sous le dossier de la campagne - jamais le nom du fichier
        // ni un morceau de nom (avant, "DEBUG_DebriefMission.bat" était écarté parce qu'il contient "debug").
        internal static bool IsInCampaignSessionFolder(string[] words)
        {
            for (int k = 0; k < words.Length; k++)
            {
                if (!words[k].Equals("Campaigns", StringComparison.OrdinalIgnoreCase))
                    continue;

                // words[k + 1] = nom de la campagne, words[k + 2] = son sous-dossier.
                // Le dernier élément est le nom du fichier (ou "" pour une entrée dossier) : ce n'est pas un dossier.
                int f = k + 2;

                if (f >= words.Length - 1)
                    return false;

                return words[f].Equals("Active", StringComparison.OrdinalIgnoreCase) ||
                       words[f].Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
                       words[f].Equals("Debriefing", StringComparison.OrdinalIgnoreCase);
            }

            return false;
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
            SavePathsFromTextBoxes();
        }

        // Enregistre ce qui est dans les 3 TextBox (même vide ou invalide), puis relance les vérifications.
        // Utilisée par le bouton Save ET par le guide des 3 étapes (Main_Form.Guide.cs).
        private void SavePathsFromTextBoxes()
        {
            // Sauvegarde explicite : on enregistre ce que l'utilisateur a tapé (même vide ou invalide).
            // Save_Config() ne touche plus aux chemins vides : c'est ici que se fait un effacement volontaire.
            string prefix = "config_" + ParamConf.NumSelectConfig + "_";
            ParamConf.configDictionary[prefix + "pathDCS"] = textBox_PATH_DCS_Root.Text;
            ParamConf.configDictionary[prefix + "pathSavedGames"] = textBox_SavedGames.Text;
            ParamConf.configDictionary[prefix + "pathOVGME"] = textBox_OvGME.Text;

            InitializeDCS_Installation_Path();     // 1. Synchronise ParamConf.PATH_* depuis les TextBox
            Configuration_Form.Save_Config();      // 2. Persiste ces valeurs à jour dans options.txt

            checkBoxMod();
            _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, textBox_SavedGames.Text);

            // Les chemins ont changé : ScriptsMod est peut-être maintenant détectable (ou installable)
            homeView.SetScriptsModVersion(scriptsModUpdater.GetLocalScriptsModVersion());
            _ = scriptsModUpdater.CheckGithubScriptsModVersionAsync();
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

        // Premier lancement : pas d'options.txt, donc aucune configuration.
        // Pourquoi : sans profil "Main", Save_Config() range les chemins sous "config_0_" puis les purge
        //            (clé sans nom) : les chemins saisis ne survivaient pas au premier lancement.
        private void EnsureDefaultConfig()
        {
            if (comboBox_Config.Items.Count > 0)
                return;

            ParamConf.NumMaxConfig = 1;
            ParamConf.NumSelectConfig = 1;
            ParamConf.CurrentConfigName = "Main";

            ParamConf.configDictionary["config_1_"] = "Main";
            ParamConf.configDictionary["config_1_pathDCS"] = "";
            ParamConf.configDictionary["config_1_pathSavedGames"] = "";
            ParamConf.configDictionary["config_1_pathOVGME"] = "";
            ParamConf.configDictionary["display"] = "Main";

            ParamConf.configMap.Clear();
            ParamConf.configMap.Add("Main", 1);
            comboBox_Config.Items.Add("Main");
            comboBox_Config.SelectedItem = "Main";

            this.Text = "DCE_Manager - Main";

            Configuration_Form.Save_Config();
        }

        // Appelée une fois la fenêtre affichée (voir Shown dans le constructeur de Main_Form).
        // Démarre le bandeau des 3 étapes (chemins DCS, ScriptsMod, première campagne) : tout est dans Main_Form.Guide.cs.
        private void GuideFirstRun()
        {
            StartSetupGuide();
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


        // =====================================================================================
        // GUIDE DES 3 ÉTAPES : bandeau dans la barre du haut (panel_top), visible sur tous les onglets
        //   1 = chemins DCS   2 = ScriptsMod   3 = au moins une campagne installée
        // Rien n'est mémorisé : chaque étape est recalculée sur le disque, pour la configuration active.
        // Donc une nouvelle configuration (chemins vides) relance le guide toute seule.
        // Un Timer de 2 s met le bandeau à jour. Quand tout est vert, il ne fait plus aucun accès disque.
        // =====================================================================================

        private SetupGuideControl _setupGuide;
        private System.Windows.Forms.Timer _guideTimer;
        private int _guideConfigId = -1;          // configuration pour laquelle le bandeau a été calculé
        private bool _guideAllDone;               // les 3 étapes sont vertes
        private bool _guideRunning;               // évite les appels imbriqués (le Timer tourne aussi pendant une MessageBox)
        private bool _scriptsModBusy;             // téléchargement de ScriptsMod en cours
        private bool _guideBandShown;             // la bande est affichée (et le contenu de la fenêtre décalé vers le bas)
        private readonly HashSet<int> _guidePathsProposed = new HashSet<int>();    // configurations pour lesquelles les dossiers détectés ont déjà été proposés
        private readonly HashSet<int> _scriptsModAutoTried = new HashSet<int>();   // configurations pour lesquelles l'installation automatique a déjà été tentée

        // Crée le bandeau (une seule fois) puis le met à jour.
        private void StartSetupGuide()
        {
            if (_setupGuide == null)
                InitSetupGuide();

            RefreshSetupGuide();
        }

        private void InitSetupGuide()
        {
            // La bande se place JUSTE SOUS la barre du haut, sur toute sa largeur.
            // (Dans la barre elle-même il ne reste pas assez de place pour 3 étapes lisibles.)
            // Elle reste cachée tant qu'ApplyGuideBand(true) n'a pas décalé le reste de la fenêtre.
            int bandHeight = Math.Max(52, panel_top.Height * 3 / 2);

            _setupGuide = new SetupGuideControl();
            _setupGuide.SetBounds(panel_top.Left, panel_top.Bottom + 6, panel_top.Width, bandHeight);
            _setupGuide.Visible = false;
            _setupGuide.StepClicked += SetupGuide_StepClicked;
            Controls.Add(_setupGuide);
            _setupGuide.BringToFront();

            FormUtils.LogRegister("Guide | bandeau créé : " + _setupGuide.Bounds);

            _guideTimer = new System.Windows.Forms.Timer();
            _guideTimer.Interval = 2000;
            _guideTimer.Tick += (s, e) => RefreshSetupGuide();
            _guideTimer.Start();
        }

        // Affiche ou cache la bande : tout le reste de la fenêtre descend (ou remonte) de sa hauteur,
        // et la fenêtre grandit (ou rétrécit) d'autant, pour que rien ne soit rogné.
        // Fenêtre agrandie au maximum : elle ne peut pas grandir, donc ce sont les zones étirables qui raccourcissent.
        private void ApplyGuideBand(bool show)
        {
            if (show == _guideBandShown)
                return;

            _guideBandShown = show;

            int delta = (_setupGuide.Height + 6) * (show ? 1 : -1);
            bool canResize = WindowState == FormWindowState.Normal;

            // Positions de départ de tous les contrôles à déplacer
            List<Control> moved = new List<Control>();
            List<Rectangle> before = new List<Rectangle>();

            foreach (Control c in Controls)
            {
                if (c == panel_top || c == _setupGuide || c.Dock != DockStyle.None)
                    continue;

                moved.Add(c);
                before.Add(c.Bounds);
            }

            // 1) La fenêtre grandit d'abord (les ancrages repositionnent déjà ce qui colle au bas)
            if (canResize)
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + delta);

            // 2) Puis chaque contrôle est placé à sa position finale exacte
            for (int i = 0; i < moved.Count; i++)
            {
                Control c = moved[i];
                Rectangle o = before[i];

                bool top = (c.Anchor & AnchorStyles.Top) != 0;
                bool bottom = (c.Anchor & AnchorStyles.Bottom) != 0;

                if (canResize)
                    c.SetBounds(o.Left, o.Top + delta, o.Width, o.Height);
                else if (top && bottom)
                    c.SetBounds(o.Left, o.Top + delta, o.Width, o.Height - delta);   // zone étirable : elle raccourcit
                else if (!bottom)
                    c.SetBounds(o.Left, o.Top + delta, o.Width, o.Height);           // zone collée en haut : elle descend
                // collée seulement en bas (bouton Exit) : elle ne bouge pas
            }

            _setupGuide.Visible = show;
        }

        // Étape 1 faite : les deux chemins sont valides (InitializeDCS_Installation_Path vide ceux qui sont invalides).
        private static bool Step1Done()
        {
            return !string.IsNullOrWhiteSpace(ParamConf.PATH_DCS_Root) && !string.IsNullOrWhiteSpace(ParamConf.PATH_SavedGames_DCS);
        }

        // Étape 2 faite : le dossier ScriptsMod.NG existe et n'est pas vide.
        private static bool IsScriptsModInstalled()
        {
            if (string.IsNullOrWhiteSpace(ParamConf.PATH_SavedGames_DCS))
                return false;

            string folder = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\ScriptsMod.NG");

            try { return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any(); }
            catch { return false; }
        }

        // Étape 3 faite : au moins une campagne complète (avec son camp_init.lua). On s'arrête à la première trouvée.
        private static bool HasCampaign()
        {
            if (string.IsNullOrWhiteSpace(ParamConf.PATH_SavedGames_DCS))
                return false;

            string root = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns");

            try
            {
                if (!Directory.Exists(root))
                    return false;

                foreach (string dir in Directory.EnumerateDirectories(root))
                {
                    if (File.Exists(Path.Combine(dir, "Init", "camp_init.lua")))
                        return true;
                }
            }
            catch { /* dossier verrouillé : on considère "pas encore" */ }

            return false;
        }

        // Recalcule les 3 étapes et redessine le bandeau (ne redessine que si quelque chose a changé).
        private void UpdateSetupGuideView()
        {
            bool s1 = Step1Done();
            bool s2 = IsScriptsModInstalled();
            bool s3 = HasCampaign();

            SetupGuideControl.StepState st1 = s1 ? SetupGuideControl.StepState.Done : SetupGuideControl.StepState.Todo;
            SetupGuideControl.StepState st2 = s2 ? SetupGuideControl.StepState.Done : (_scriptsModBusy ? SetupGuideControl.StepState.Working : SetupGuideControl.StepState.Todo);
            SetupGuideControl.StepState st3 = s3 ? SetupGuideControl.StepState.Done : SetupGuideControl.StepState.Todo;

            string h1 = s1 ? "done" : "click to choose";
            string h2 = s2 ? "done" : (_scriptsModBusy ? "installing..." : (s1 ? "click to install" : "starts after step 1"));
            string h3 = s3 ? "done" : "click to choose one";

            _setupGuide.SetSteps(st1, st2, st3, h1, h2, h3);

            _guideAllDone = s1 && s2 && s3;
            ApplyGuideBand(!_guideAllDone);
        }

        // Appelée au démarrage, par le Timer et après chaque action du guide.
        private void RefreshSetupGuide()
        {
            if (_setupGuide == null || _guideRunning)
                return;

            int cfg = ParamConf.NumSelectConfig;

            // Tout est vert et la configuration n'a pas changé : rien à surveiller (aucun accès disque)
            if (cfg == _guideConfigId && _guideAllDone)
                return;

            _guideRunning = true;

            try
            {
                _guideConfigId = cfg;
                UpdateSetupGuideView();

                if (_guideAllDone)
                    return;

                if (!Step1Done())
                {
                    // Étape 1 : une seule proposition par configuration (pas de popup à répétition)
                    if (_guidePathsProposed.Add(cfg))
                    {
                        GuideStep1();
                        UpdateSetupGuideView();
                    }
                }
                else if (!IsScriptsModInstalled() && !_scriptsModBusy && _scriptsModAutoTried.Add(cfg))
                {
                    // Étape 2 : les chemins sont bons, ScriptsMod s'installe tout seul (une tentative par configuration).
                    // Si ça échoue, le bouton du bandeau permet de recommencer.
                    _ = InstallScriptsModFromGuideAsync();
                }
            }
            finally
            {
                _guideRunning = false;
            }
        }

        // Clic sur une étape. Les étapes se font dans l'ordre : un clic sur une étape trop avancée renvoie à la première qui manque.
        private void SetupGuide_StepClicked(int step)
        {
            bool s1 = Step1Done();
            bool s2 = IsScriptsModInstalled();

            int target = step;

            if (!s1)
                target = 0;
            else if (!s2 && step == 2)
                target = 1;

            if (target == 0)
            {
                if (!s1)
                    GuideStep1();
            }
            else if (target == 1)
            {
                if (!s2 && !_scriptsModBusy)
                    _ = InstallScriptsModFromGuideAsync();
            }
            else
            {
                if (!HasCampaign())
                    tabControl_LEFT.SelectedTab = tabPageLeftCatalog;   // l'utilisateur choisit sa campagne dans le Catalog
            }

            RefreshSetupGuide();
        }

        // Étape 1 : propose les dossiers DCS trouvés ; sinon (ou si refusé) ouvre l'onglet Install, champs de saisie visibles.
        private void GuideStep1()
        {
            if (ProposeDetectedFolders())
                return;

            tabControl_LEFT.SelectedTab = tabPageLeft_Install;

            if (!but_PATH_SAVE.Visible)
                but_PATH_Modify_Click(null, EventArgs.Empty);
        }

        // Étape 2 : télécharge et installe ScriptsMod (même chemin que le bouton de l'onglet Update).
        private async Task InstallScriptsModFromGuideAsync()
        {
            if (_scriptsModBusy)
                return;

            _scriptsModBusy = true;
            UpdateSetupGuideView();   // l'étape passe à l'orange

            try
            {
                await scriptsModUpdater.DownloadLatestScriptsMod();

                homeView.SetScriptsModVersion(scriptsModUpdater.GetLocalScriptsModVersion());
                CampaignGridLeft.RefreshScriptsModState();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Guide | ScriptsMod install : " + ex.Message);
            }
            finally
            {
                _scriptsModBusy = false;
                UpdateSetupGuideView();
            }
        }

        // ---------- Détection automatique des dossiers DCS ----------

        private class DcsFolders
        {
            public string Root;         // dossier d'installation (contient "bin")
            public string SavedGames;   // Saved Games\DCS ou DCS.openbeta (contient "Logs")
        }

        // Cherche les installations de DCS : registre Windows, puis dossiers habituels sur les disques fixes.
        // Peu coûteux (quelques Directory.Exists) et appelé seulement quand l'étape 1 n'est pas faite.
        private List<DcsFolders> DetectDcsFolders()
        {
            List<string> roots = new List<string>();

            // 1) Registre : écrit par l'installeur de DCS
            foreach (string name in new[] { "DCS World", "DCS World OpenBeta" })
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Eagle Dynamics\" + name))
                    {
                        string p = key == null ? null : key.GetValue("Path") as string;

                        if (!string.IsNullOrWhiteSpace(p))
                            roots.Add(p.Trim().TrimEnd('\\'));
                    }
                }
                catch { /* clé illisible : on passe à la suite */ }
            }

            // 2) Dossiers habituels, sur chaque disque fixe
            string[] usual =
            {
                @"Eagle Dynamics\DCS World",
                @"Eagle Dynamics\DCS World OpenBeta",
                @"Program Files\Eagle Dynamics\DCS World",
                @"Program Files\Eagle Dynamics\DCS World OpenBeta",
                @"Program Files (x86)\Steam\steamapps\common\DCSWorld",
                @"SteamLibrary\steamapps\common\DCSWorld"
            };

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                        continue;
                }
                catch { continue; }

                foreach (string rel in usual)
                    roots.Add(Path.Combine(drive.RootDirectory.FullName, rel));
            }

            string savedBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");

            List<DcsFolders> result = new List<DcsFolders>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in roots)
            {
                if (!seen.Add(root))
                    continue;

                try
                {
                    if (!Directory.Exists(Path.Combine(root, "bin")))
                        continue;

                    // DCS OpenBeta range ses données dans "DCS.openbeta", la version stable dans "DCS"
                    bool beta = root.IndexOf("openbeta", StringComparison.OrdinalIgnoreCase) >= 0;
                    string saved = Path.Combine(savedBase, beta ? "DCS.openbeta" : "DCS");

                    // Même contrôle que InitializeDCS_Installation_Path : le dossier "Logs" doit exister
                    // (il n'existe qu'après le premier lancement de DCS)
                    if (!Directory.Exists(Path.Combine(saved, "Logs")))
                        continue;

                    result.Add(new DcsFolders { Root = root, SavedGames = saved });
                }
                catch { /* chemin illisible : on l'ignore */ }
            }

            return result;
        }

        // Montre chaque installation trouvée ; l'utilisateur valide d'un clic. Rien n'est enregistré sans son accord.
        // Renvoie true si des dossiers ont été acceptés et enregistrés.
        private bool ProposeDetectedFolders()
        {
            foreach (DcsFolders pair in DetectDcsFolders())
            {
                DialogResult answer = MessageBox.Show(
                    "DCS was found on this PC.\r\n\r\n" +
                    "DCS folder:\r\n" + pair.Root + "\r\n\r\n" +
                    "Saved Games folder:\r\n" + pair.SavedGames + "\r\n\r\n" +
                    "Use these folders?",
                    "Step 1 - DCS folders",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (answer == DialogResult.Yes)
                {
                    textBox_PATH_DCS_Root.Text = pair.Root;
                    textBox_SavedGames.Text = pair.SavedGames;

                    HidePathEditingControls();
                    SavePathsFromTextBoxes();

                    return true;
                }
            }

            return false;
        }

    }

    // Bandeau des 3 étapes : dessiné à la main, un seul contrôle léger (pas de panneaux ni d'images).
    // Rouge = à faire, orange = en cours, grosse coche verte = fait. Un clic sur une étape lance l'action (voir Main_Form).
    internal class SetupGuideControl : Control
    {
        public enum StepState { Todo, Working, Done }

        private readonly StepState[] _states = { StepState.Todo, StepState.Todo, StepState.Todo };
        private readonly string[] _hints = { "", "", "" };
        private readonly string[] _titles = { "DCS folders", "ScriptsMod", "First campaign" };
        private readonly ToolTip _tip = new ToolTip();
        private int _hover = -1;

        private static readonly Color ColorTodo = Color.FromArgb(200, 40, 40);
        private static readonly Color ColorWorking = Color.FromArgb(230, 140, 20);
        private static readonly Color ColorDone = Color.FromArgb(40, 160, 70);

        public event Action<int> StepClicked;

        public SetupGuideControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        // Ne redessine que si quelque chose a vraiment changé (appelé toutes les 2 s par le Timer).
        public void SetSteps(StepState s1, StepState s2, StepState s3, string h1, string h2, string h3)
        {
            StepState[] newStates = { s1, s2, s3 };
            string[] newHints = { h1, h2, h3 };
            bool changed = false;

            for (int i = 0; i < 3; i++)
            {
                if (_states[i] != newStates[i] || _hints[i] != newHints[i])
                    changed = true;

                _states[i] = newStates[i];
                _hints[i] = newHints[i];
            }

            if (changed)
                Invalidate();
        }

        private int StepAt(int x)
        {
            int w = Width / 3;

            if (w <= 0)
                return -1;

            return Math.Max(0, Math.Min(2, x / w));
        }

        private string TipFor(int i)
        {
            if (_states[i] == StepState.Done)
                return _titles[i] + ": done";

            if (i == 0)
                return "Click to choose your DCS folder and your Saved Games folder";

            if (i == 1)
                return _states[1] == StepState.Working
                    ? "Downloading and installing ScriptsMod..."
                    : "ScriptsMod builds the missions. It installs by itself once step 1 is done. Click to try again.";

            return "Click to open the Catalog and install your first campaign";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int i = StepAt(e.X);

            if (i == _hover)
                return;

            _hover = i;
            Cursor = (i >= 0 && _states[i] != StepState.Done) ? Cursors.Hand : Cursors.Default;

            if (i >= 0)
                _tip.SetToolTip(this, TipFor(i));

            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            if (e.Button != MouseButtons.Left)
                return;

            int i = StepAt(e.X);

            if (i >= 0 && StepClicked != null)
                StepClicked(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = Width / 3;
            int d = Height - 14;   // diamètre du rond

            using (Font titleFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (Font hintFont = new Font("Segoe UI", 8.5f))
            using (Font numberFont = new Font("Segoe UI", 14f, FontStyle.Bold))
            using (Pen separator = new Pen(Color.Gainsboro))
            using (Pen border = new Pen(Color.Silver))
            {
                for (int i = 0; i < 3; i++)
                {
                    Rectangle cell = new Rectangle(i * w, 0, w, Height);
                    StepState state = _states[i];

                    Color color = state == StepState.Done ? ColorDone : (state == StepState.Working ? ColorWorking : ColorTodo);

                    // Survol : léger voile gris, seulement sur une étape pas encore faite
                    if (i == _hover && state != StepState.Done)
                    {
                        using (SolidBrush hoverBrush = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
                            g.FillRectangle(hoverBrush, cell);
                    }

                    // Rond : rouge + numéro, orange + "...", vert + grosse coche
                    Rectangle badge = new Rectangle(cell.Left + 12, (Height - d) / 2, d, d);

                    using (SolidBrush brush = new SolidBrush(color))
                        g.FillEllipse(brush, badge);

                    if (state == StepState.Done)
                    {
                        using (Pen check = new Pen(Color.White, Math.Max(3f, d * 0.12f)))
                        {
                            check.StartCap = LineCap.Round;
                            check.EndCap = LineCap.Round;
                            check.LineJoin = LineJoin.Round;

                            g.DrawLines(check, new PointF[]
                            {
                                new PointF(badge.Left + d * 0.27f, badge.Top + d * 0.53f),
                                new PointF(badge.Left + d * 0.43f, badge.Top + d * 0.69f),
                                new PointF(badge.Left + d * 0.74f, badge.Top + d * 0.33f)
                            });
                        }
                    }
                    else
                    {
                        string symbol = state == StepState.Working ? "..." : (i + 1).ToString();
                        TextRenderer.DrawText(g, symbol, numberFont, badge, Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }

                    // Titre (en couleur de l'état) + petite phrase grise dessous
                    int textLeft = badge.Right + 12;
                    int textWidth = Math.Max(10, cell.Right - textLeft - 8);
                    Rectangle titleRect = new Rectangle(textLeft, 0, textWidth, Height / 2 + 2);
                    Rectangle hintRect = new Rectangle(textLeft, Height / 2 + 2, textWidth, Height - Height / 2 - 2);

                    TextRenderer.DrawText(g, _titles[i], titleFont, titleRect, color,
                        TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, _hints[i], hintFont, hintRect, Color.DimGray,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                    if (i < 2)
                        g.DrawLine(separator, cell.Right - 1, 10, cell.Right - 1, Height - 10);
                }

                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tip.Dispose();

            base.Dispose(disposing);
        }
    }
}
