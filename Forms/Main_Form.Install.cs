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
            }
            else
            {
                // Le chemin est vide ou invalide
                pic_DCS_Root.Image = Properties.Resources.icons8_warning_blue_30;
                Label_subLabel_DCS.Text = "";
                ParamConf.PATH_DCS_Root = "";

                homeView.pic_Accueil_DCS_Status.Image = Properties.Resources.icons8_warning_blue_30;
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
            }
            else
            {
                // Le chemin est vide ou invalide
                pic_SavedGame.Image = Properties.Resources.icons8_warning_blue_30;
                label_subLabel_SavedGame_Folder.Text = "";
                ParamConf.PATH_SavedGames_DCS = "";
                //ParamConf.test_DCS_Root = false;
                homeView.pic_Accueil_SavedGames_Satus.Image = Properties.Resources.icons8_warning_blue_30;
            }

            //--OvGMEPath
            // 1. Récupérer le chemin actuellement affiché dans la TextBox
            string ovGMEPath = textBox_OvGME.Text;

            // 2. Vérifier si le chemin n'est pas vide et si le dossier existe
            // (Optionnel : tu peux aussi vérifier Path.Combine(ovGMEPath, "ovgme.exe") si tu veux être sûr)
            if (!string.IsNullOrWhiteSpace(ovGMEPath) && Directory.Exists(ovGMEPath))
            {
                // Le chemin est valide !
                pic_OVGME.Image = Properties.Resources.icons8_ok_24;
                label_sub_OVGME.Text = ovGMEPath;
                ParamConf.PATH_OVGME_MOD = ovGMEPath;

                // Si tu as une variable de test comme pour DCS, n'oublie pas de la mettre à true :
                // ParamConf.OvGME_Root = true;
                homeView.pic_Accueil_ovgme_status.Image = Properties.Resources.icons8_ok_24;
            }
            else
            {
                // Le chemin est vide ou invalide
                pic_OVGME.Image = Properties.Resources.icons8_warning_blue_30;
                label_sub_OVGME.Text = ""; // Ou laisser vide "" si tu préfères
                ParamConf.PATH_OVGME_MOD = "";

                // ParamConf.OvGME_Root = false;
                homeView.pic_Accueil_ovgme_status.Image = Properties.Resources.icons8_warning_blue_30;
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
                    pic_OVGME.Image = Properties.Resources.icons8_ok_24;
                    label_sub_OVGME.Text = folderPath;

                }
                else
                {
                    // Le chemin est vide ou invalide
                    pic_OVGME.Image = Properties.Resources.icons8_warning_blue_30;
                    label_sub_OVGME.Text = ""; // Ou laisser vide "" si tu préfères

                    //FormUtils.ShowErrorMessage("No folder selected");
                }
            }

        }

        private void button_InstallCampaign_Click(object sender, EventArgs e)
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

            //bool findNameCampaign = false;
            //bool findScriptsMod = false;

            string NameCampaign = "";
            //string zipPath = textBox_Campaign.Text;

            string zipPath = _selectedCampaignZipPath;

            if (File.Exists(_selectedCampaignZipPath))
            {

                //cherche le nom de la campagne dans le fichier zip
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.Contains("camp_init"))
                        {

                            // Ouvrir le fichier texte pour lecture
                            if (entry != null)
                            {
                                using (StreamReader reader = new StreamReader(entry.Open()))
                                {
                                    string line;
                                    int lineNumber = 0;
                                    while ((line = reader.ReadLine()) != null)
                                    {
                                        lineNumber++;
                                        if (line.Contains("title"))
                                        {
                                            //	title = "Crisis in PG-Blue",		--Title of campaign (name of missions)

                                            string tempTXT = (string)line;
                                            string[] words_A = tempTXT.Split(',');
                                            string[] words = words_A[0].Split('=');
                                            ParamCampaign.NameCampaign = words[1].Replace("\"", "");

                                            ParamCampaign.NameCampaign = ParamCampaign.NameCampaign.TrimStart();
                                            ParamCampaign.NameCampaign = ParamCampaign.NameCampaign.TrimEnd();
                                            NameCampaign = ParamCampaign.NameCampaign;
                                            //findNameCampaign = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }


                //if (ParamConf.DCE_alreadyInstalled == false && TestFile.structureValide == false  && TestFile.presenceOobAirInit && TestFile.presenceCampInit)
                if (ParamConf.test_DCE_alreadyInstalled == false && TestFile.presenceOobAirInit && TestFile.presenceCampInit)
                {
                    // Assurez-vous que l'application Windows Forms est configurée

                    // Afficher la boîte de message avec les boutons Yes et No
                    DialogResult result = MessageBox.Show(
                        "There are no DCE directories, so you need to create them.",    // Message à afficher
                        "Create DCE directory",               // Titre de la boîte de message
                        MessageBoxButtons.YesNo,      // Boutons à afficher
                        MessageBoxIcon.Question       // Icône à afficher
                    );

                    // Vérifier le résultat de la boîte de message
                    if (result == DialogResult.Yes)
                    {
                        FormUtils.CreateDCE_Folder();

                        //string combPathDCE_2 = Path.Combine(textBox_SavedGames.Text, @"Mods\tech\DCE\Missions\Campaigns");
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

                //MessageBox.Show(ParamCampaign.PathCampaign, "info");

                if (Directory.Exists(ParamCampaign.PathCampaign))
                {
                    // Afficher la boîte de message avec les boutons Yes et No
                    DialogResult result = MessageBox.Show(
                    "The campaign already seems to be already installed. Do you want to overrun it?.",    // Message à afficher
                    "Attention",               // Titre de la boîte de message
                    MessageBoxButtons.YesNo,      // Boutons à afficher
                    MessageBoxIcon.Question       // Icône à afficher
                 );

                    // Vérifier le résultat de la boîte de message
                    if (result == DialogResult.No)
                    {
                        return;
                    }
                }



                //---------------ENREGISTRE ici les fichiers de la campagne ----------------------

                ExtractZipFileToDirectory(_selectedCampaignZipPath, true);

                 TestFile.ScriptsMod = "NG";



                //ecrit dans le fichier path.bat de la campagne installée:


                //REM Core or Main DCS ou DCS.beta path, always end the line with \
                //set "pathDCS=D:\___DCS___\"

                //REM DCS or DCS.beta saved game path, always end the line with \
                //set "pathSavedGames=Saved Games\DCS.openbeta\" 

                //REM DCE ScriptMod version not any / or \ and no space before and after =
                //set "versionPackageICM=20.43.59"

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

                System.IO.File.WriteAllText(pathFile, textPathBat);

                //******************new system

                string pathStatus = textBox_SavedGames.Text.Replace(@"\", "/") + "/";
                string fileNameA = ParamCampaign.NameCampaign + "_first.miz";
                string pathFirstMission = Path.Combine(textBox_SavedGames.Text, @"Mods\tech\DCE\Missions\Campaigns", fileNameA);
                string tempFilePath = Path.Combine(Path.GetTempPath(), "camp_status.lua"); // Chemin temporaire pour extraire et modifier le fichier


                if (File.Exists(pathFirstMission))
                {

                    // Ouverture du fichier zip en mode Update pour modifier son contenu
                    using (ZipArchive archive = ZipFile.Open(pathFirstMission, ZipArchiveMode.Update))
                    {

                        // Chercher le fichier "camp_status.lua" dans le sous-dossier "l10n" à l'intérieur de l'archive
                        ZipArchiveEntry entry = archive.GetEntry("l10n/DEFAULT/camp_status.lua");

                        if (entry != null)
                        {
                            // 1. Extraction du fichier "camp_status.lua" vers un emplacement temporaire
                            entry.ExtractToFile(tempFilePath, true);

                            // 2. Modification du fichier temporaire
                            int nbLigneMod = FormUtils.ModifierLigne(tempFilePath, "['path']", "	['path'] = '" + pathStatus + "',", 0);
                            if (nbLigneMod < 1)
                            {
                                nbLigneMod = FormUtils.ModifierLigne(tempFilePath, "[\"path\"]", "	[\"path\"] = '" + pathStatus + "',", 0);
                            }

                            // 3. Suppression de l'ancienne entrée dans le fichier zip
                            entry.Delete();

                            // 4. Réintégration du fichier modifié dans le zip dans le même sous-dossier "l10n"
                            archive.CreateEntryFromFile(tempFilePath, "l10n/DEFAULT/camp_status.lua");
                        }
                        else
                        {
                            MessageBox.Show("The ‘camp_status.lua’ file cannot be found in the ‘l10n’ folder in the archive.");
                        }
                    }

                    // Nettoyage : Suppression du fichier temporaire après modification
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                else
                {
                    MessageBox.Show($"The file {fileNameA} cannot be found in this directory: " + textBox_SavedGames.Text + @"Mods\tech\DCE\Missions\Campaigns");
                }


                //************************************ONGOIN.MIZ


                string fileNameB = ParamCampaign.NameCampaign + "_ongoing.miz";
                string pathOngoingMission = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns\" + fileNameB;

                if (File.Exists(pathFirstMission))
                {

                    // Ouverture du fichier zip en mode Update pour modifier son contenu
                    using (ZipArchive archive = ZipFile.Open(pathOngoingMission, ZipArchiveMode.Update))
                    {
                        // Chercher le fichier "camp_status.lua" dans le sous-dossier "l10n" à l'intérieur de l'archive
                        ZipArchiveEntry entry = archive.GetEntry("l10n/DEFAULT/camp_status.lua");

                        if (entry != null)
                        {
                            // 1. Extraction du fichier "camp_status.lua" vers un emplacement temporaire
                            entry.ExtractToFile(tempFilePath, true);

                            // 2. Modification du fichier temporaire
                            int nbLigneMod = FormUtils.ModifierLigne(tempFilePath, "['path']", "	['path'] = '" + pathStatus + "',", 0);
                            if (nbLigneMod < 1)
                            {
                                nbLigneMod = FormUtils.ModifierLigne(tempFilePath, "[\"path\"]", "	[\"path\"] = '" + pathStatus + "',", 0);
                            }

                            // 3. Suppression de l'ancienne entrée dans le fichier zip
                            entry.Delete();

                            // 4. Réintégration du fichier modifié dans le zip dans le même sous-dossier "l10n"
                            archive.CreateEntryFromFile(tempFilePath, "l10n/DEFAULT/camp_status.lua");
                        }
                        else
                        {
                            MessageBox.Show("Le fichier 'camp_status.lua' est introuvable dans le dossier 'l10n' de l'archive.");
                        }
                    }

                    // Nettoyage : Suppression du fichier temporaire après modification
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                else
                {
                    MessageBox.Show($"The file {fileNameB} cannot be found in this directory: " + ParamConf.PATH_SavedGames_DCS + @"Mods\tech\DCE\Missions\Campaigns");
                }

                MessageBox.Show(ParamCampaign.NameCampaign + " successfully installed.\r\n \r\n" +
                    "   Don't forget to activate the ‘MissionScript’ mod with PATH_OVGME_MOD ", "Information");

                FormUtils.LogRegister(ParamCampaign.NameCampaign + " successfully installed.\r\n \r\n" +
                    "   Don't forget to activate the ‘MissionScript’ mod with PATH_OVGME_MOD ");

                //******************FIN du new system


            }
            else
            {
                //button_InstallCampaign.Visible = false;
                MessageBox.Show("Zip file not found", "Error");
            }

            Cursor.Current = Cursors.Default;

            //button_InstallCampaign.Visible = false;

            //affiche la ligne campagne en enlevant la derniere campagne, (folder seul sans fichier)
            string[] wordsBarre = _selectedCampaignZipPath.Split('\\');
            string[] wordsPoint = _selectedCampaignZipPath.Split('.');

            if (wordsPoint.Count() > 1 & wordsBarre.Count() > 1)
            {
                _selectedCampaignZipPath = _selectedCampaignZipPath.Replace(wordsBarre[wordsBarre.Count() - 1], "");
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

        //// La méthode qui reçoit les fichiers
        //public void DropZone_FilesSelected(object sender, string[] files)
        //{
        //    FormUtils.LogRegister("DropZone_FilesSelected: DragEnter déclenché");

        //    foreach (var file in files)
        //    {
        //        // Comme vous êtes virtuellement "dans" Main_Form, vous avez accès à tout !
        //        // Vous pouvez directement utiliser vos variables du fichier principal :
        //        this._selectedCampaignZipPath = file;

        //        MessageBox.Show($"Fichier reçu dans Main_Form : {file}");

        //        // Vous pouvez appeler votre updater sans problème :
        //        // this.campaignUpdater.MaMethode(file);
        //    }

        public void DropZone_FilesSelected(object sender, string[] files)
        {
            if (files.Length > 0)
            {
                this._selectedCampaignZipPath = files[0];
                dropZoneControl1.SetSelectedFile(files[0]);   // ← bordure verte + nom du fichier affiché
            }
        }

    }
}