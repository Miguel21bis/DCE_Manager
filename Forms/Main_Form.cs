using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;
using SearchOption = System.IO.SearchOption;



//namespace DCE_Manager
//{
//    public partial class Main_Form : Form
//    {
//        // 1. champs privés

//        // 2. propriétés publiques

//        // 3. constructeur

//        // 4. méthodes d'initialisation

//        // 5. méthodes de chargement des données

//        // 7. méthodes utilitaires
//    }
//}


namespace DCE_Manager
{
    public partial class Main_Form : Form
    {

        private bool _isUpdatingState = false;
        private bool _isInitializing = true; // champ de classe
        private string _cachedDcsRootPath;
        private string _cachedSavedGamesPath;
        private string _cachedOvGmePath;
        private Updater_ScriptsMod scriptsModUpdater;
        private Updater_DCEManager dceManagerUpdater;
        // 1. Déclarer la variable de classe
        //private CampaignGridLeft _campaignGridLeft;
        // Propriété publique en lecture seule
        public CampaignGridLeft CampaignGridLeft { get; private set; }

        public static Main_Form Instance { get; private set; }
        public DataTable dataTable;
        public string currentState = "Init";
        public List<Squad> currentSquads = new List<Squad>();
        public Updater_Campaign campaignUpdater;


        //constructeur :
        public Main_Form()
        {

            // C'est ici qu'on passe "this" (qui représente le Form en cours)
            campaignUpdater = new Updater_Campaign(this);

            KeyPreview = true;

            InitializeComponent();

            // 2. Instancier en passant "this"
            CampaignGridLeft = new CampaignGridLeft(this);


            dropZoneControl1.FilesSelected += DropZone_FilesSelected;
            //InitializeDropZone();

            InitActionRows();

            Updater_Campaign.InitCampaignUpdateGrid(CampaignDataGridView);
            //this.Shown += Form1_Shown;

            scriptsModUpdater = new Updater_ScriptsMod(this);
            dceManagerUpdater = new Updater_DCEManager(this);

            //*************************
            CampaignGridLeft.GridCampaigns_Init_DataGridView();
            //*************************

            CampaignDataGridView.CellContentClick += CampaignGridLeft.CampaignDataGridView_CellContentClick;

            Instance = this;  // Initialiser l'instance statique dans le constructeur


            pictureBox_Update_ScriptsMod.Click += (s, e) =>
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://github.com/{GithubHelper.GithubAccount}/{GithubHelper.Repository_ScriptsMod}",
                UseShellExecute = true
            });

            pictureBox_Update_DCE_Manager.Click += (s, e) =>
            Process.Start(new ProcessStartInfo
            {
                FileName = $"https://github.com/{GithubHelper.GithubAccount}/{GithubHelper.Repository_Manager}",
                UseShellExecute = true
            });


            tabControl_LEFT.Selected += new TabControlEventHandler(TabControl1_SelectedAsync);
            CampaignTab.Selected += new TabControlEventHandler(CampaignTab_Selected);

            // Abonner l'événement FormClosed à une méthode
            this.FormClosed += new FormClosedEventHandler(Form1_FormClosed);

            //VersionDceManager.Text = VersionLongDceManager();
            VersionDceManager.Text = GetVersionDceManager();

            textBox_id_client.Text = Statistics.CreateIdClient();
            AjusterLargeurTextBox(textBox_id_client);

            // Appel de la méthode de chargement
            LoadConfiguration();

            _isInitializing = false;

            _ = scriptsModUpdater.CheckGithubScriptsModVersionAsync();

            _ = dceManagerUpdater.CheckGithubDCEManagerVersionAsync();

            _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, ParamConf.PATH_SavedGames_DCS);

            _ = Statistics.EnvoiStatsAsync(checkBox_Stat_anonym.Checked);

            InitializeDCS_Installation_Path();


            ToolTip toolTip1 = new ToolTip();
            toolTip1.ShowAlways = true;
            toolTip1.ToolTipTitle = "Example :";
            toolTip1.UseFading = true;
            toolTip1.UseAnimation = true;
            toolTip1.IsBalloon = true;
            toolTip1.ShowAlways = true;
            toolTip1.AutoPopDelay = 20000;
            toolTip1.InitialDelay = 1000;
            toolTip1.ReshowDelay = 5000;
            toolTip1.SetToolTip(m_But_Install_Browse_DcsPath, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(Label_DCS, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");

            toolTip1.SetToolTip(m_But_Install_Browse_SavedGame, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(textBox_PATH_DCS_Root, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");
           

            //affiche le changelog
            textBox_changelog.Text = DCE_Manager.Properties.Resources.changelog;

            //coche ou pas le checkbox du scriptmod
            checkBoxMod();

            //Affiche le changelog
            string ChangelogFileSM = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua";
            bool ChangelogFileSMExist = File.Exists(ChangelogFileSM);

            if (ChangelogFileSMExist)
            {
                using (StreamReader reader = new StreamReader(ChangelogFileSM))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (((line.Length >= 2 && line.Substring(0, 2) != "--") | (line.Length <= 2)) &&
                            !System.Text.RegularExpressions.Regex.IsMatch(line, "versionDCE") &&
                            !System.Text.RegularExpressions.Regex.IsMatch(line, "VersionDCE")
                            )
                        {
                            textBox_ChangelogScriptsMod.Text = textBox_ChangelogScriptsMod.Text + line + "\r\n";
                        }
                    }
                    reader.Close();
                }
            }

            if (ChangelogFileSMExist)
            {
                string line;

                StreamReader sr = new StreamReader(ChangelogFileSM);

                while ((line = sr.ReadLine()) != null)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(line, "versionDCE"))
                    {
                        textBox_ChangelogScriptsMod.Text = textBox_ChangelogScriptsMod.Text + line + "\r\n";
                    }
                }
                sr.Close();
            }

            //*******************************************************************************************************************************
            //telecharge news.lua pour afficher les news***********************************************************************************
            //*******************************************************************************************************************************

            //bool DownloadRequis = true;

            //string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
            //bool pathManagerExists = System.IO.Directory.Exists(pathManager);
            //string newsLocFile = "news.lua";

            //bool newsLocFileExists = File.Exists(pathManager + newsLocFile);
            //if (newsLocFileExists)
            //{
            //    //DateTime fInfo = DateTime.Now;
            //    FileInfo fInfo = new FileInfo(pathManager + newsLocFile);
            //    int size = unchecked((int)fInfo.Length);                                    //taille en octets 
            //    //if (size < 10 | DateTime.Now >= fInfo.LastWriteTime.AddMinutes(1))
            //    if (size < 10 | DateTime.Now >= fInfo.LastWriteTime.AddDays(1))
            //    {
            //        DownloadRequis = true;
            //    }
            //    else
            //    {
            //        DownloadRequis = false;
            //    }
            //}

            ////https://drive.google.com/uc?export=download&id=
            ////https://drive.google.com/file/d/1yjkhowWJbourfAqdSD2V1xqLo8E4E4C1/view?usp=sharing

            //string googleLinkThisFile = "1yjkhowWJbourfAqdSD2V1xqLo8E4E4C1";

            //if (!newsLocFileExists | DownloadRequis)
            //{
            //    //telecharge le fichier contenant les news
            //    using (WebClient client = new WebClient())
            //    {
            //        try
            //        {
 
            //            if (ParamServ.ServerSelected == ParamServ.FileServerName02)
            //            {
            //                client.DownloadFile(ParamServ.ServerSelected + googleLinkThisFile, pathManager + newsLocFile);
            //            }
            //            else
            //            {
            //                client.DownloadFile(ParamServ.ServerSelected + @"\news.lua", pathManager + newsLocFile);
            //            }

            //            FormUtils.LogRegister("LogRegister 2418 Download news.lua " + "\r\n");
            //        }
            //        catch (Exception ex)
            //        {
            //            try
            //            {
            //                //MessageBox.Show("Please select another server, this one is too long.", "Report");
            //                client.DownloadFile(ParamServ.FileServerName03 + @"\news.lua", pathManager + newsLocFile);
            //            }
            //            catch (Exception ex2)
            //            {
            //                FormUtils.ErrorGeneral_BoxOrLog(ex2, "Failed server:", ParamServ.ServerSelected, false, true);                          
            //            }
            //            FormUtils.ErrorGeneral_BoxOrLog(ex, "Failed server:", ParamServ.ServerSelected, false, true);

            //        }
            //        client.Dispose();
            //    }
            //}

            //newsLocFileExists = File.Exists(pathManager + newsLocFile);

            //if (newsLocFileExists)
            //{

            //    //Affiche la fenetre News POPUP
            //    string NewsBox0 = "";
            //    bool newsRegBox0 = false;
            //    bool textBox_NewsAffiche0 = false;
            //    string NewsFile0 = ParamManager.pathManager + @"\news.lua";
            //    bool NewsFilexist0 = File.Exists(NewsFile0);

            //    string line;
            //    //System.IO.IOException : 'Le processus ne peut pas accéder au fichier 'D:\_D_Documents\DCE_Manager\news.lua', car il est en cours d'utilisation par un autre processus.'
            //    StreamReader sr = new StreamReader(NewsFile0);

            //    while ((line = sr.ReadLine()) != null)
            //    {
            //        if (!System.Text.RegularExpressions.Regex.IsMatch(line, "versionNews"))
            //        {
            //            if (newsRegBox0 & !System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
            //            {
            //                NewsBox0 = NewsBox0 + line + "\r\n";
            //            }
            //            if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStart"))
            //            {
            //                newsRegBox0 = true;
            //            }
            //            else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
            //            {
            //                newsRegBox0 = false;
            //            }
            //            else if (newsRegBox0 == false)
            //            {
            //                //textBox_News.Text = textBox_News.Text + line + "\r\n";
            //            }
            //        }
            //        else if (System.Text.RegularExpressions.Regex.IsMatch(line, "versionNews"))
            //        {
            //            string[] words = line.Split('=');
            //            words[1] = words[1].Replace("\"", "");
            //            words[1] = words[1].Replace(" ", "");
            //            string v1_newsLua = words[1];
            //            string v2_optionsTxt = DceNews.LastNewsVersion;
            //            //v1>v2?
            //            bool resultVersion = FormUtils.CompareVersion(v1_newsLua, v2_optionsTxt);

            //            //regarde si la version du fichier News est supérieur à LastNewsVersion
            //            if (resultVersion)
            //            {
            //                textBox_NewsAffiche0 = true;
            //                DceNews.LastNewsVersion = v1_newsLua;
                            
            //            }
            //        }
            //    }

            //    sr.Close();


            //    if (textBox_NewsAffiche0)
            //    {
            //        //MessageBox.Show(NewsBox0, "News");

            //        tabPageLeftNews.Text = "News (1)";
            //        //tabPage5.Refresh();

            //        FormUtils.ModifierLigneBis(NewsFile0, "lastNewsAffiche=true", "lastNewsAffiche=false");

            //        FormUtils.ModifierLigneBis(NewsFile0, "lastNewsAffiche = true", "lastNewsAffiche = false");

            //    }
            //}

            ////Affiche le taB News
            //string NewsBox = "";
            //bool newsRegBox = false;
            ////bool textBox_NewsAffiche = false;
            //string NewsFile = ParamManager.pathManager + @"\news.lua";
            //bool NewsFilexist = File.Exists(NewsFile);

            //if (NewsFilexist)
            //{
            //    string line;
            //    StreamReader sr = new StreamReader(NewsFile);

            //    panel_News.Controls.Clear(); // Nettoyer les anciens contrôles du panel
            //    panel_News.AutoScroll = true; // Activer le défilement si nécessaire

            //    int yPos = 0; // Position de départ pour les contrôles dans le panel

            //    while ((line = sr.ReadLine()) != null)
            //    {
            //        if (!System.Text.RegularExpressions.Regex.IsMatch(line, "versionNews"))
            //        {
            //            if (newsRegBox & !System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
            //            {
            //                NewsBox = NewsBox + line + "\r\n";
            //            }
            //            if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStart"))
            //            {
            //                newsRegBox = true;
            //            }
            //            else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
            //            {
            //                newsRegBox = false;
            //            }
            //            else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsAffiche"))
            //            {
            //                //string[] words = line.Split('=');
            //                //if (words[1].Contains("true"))
            //                //textBox_NewsAffiche = true;
            //            }
            //            else if (newsRegBox == false)
            //            {
            //                if (System.Text.RegularExpressions.Regex.IsMatch(line, "="))
            //                {
            //                    string[] words = line.Split('=');
            //                    string txtLink = words[0];
            //                    string linkFull = words[1];

            //                    // Créer et configurer le LinkLabel
            //                    LinkLabel linkLabel = new LinkLabel();
            //                    linkLabel.Text = txtLink;
            //                    linkLabel.LinkArea = new LinkArea(0, txtLink.Length); // Rendre tout le texte cliquable
            //                    linkLabel.AutoSize = true;
            //                    linkLabel.Location = new Point(0, yPos); // Définir l'emplacement dans le panel
            //                    linkLabel.LinkClicked += (sender, e) => System.Diagnostics.Process.Start(linkFull);

            //                    // Ajouter le LinkLabel au panel
            //                    panel_News.Controls.Add(linkLabel);
            //                    yPos += linkLabel.Height + 5; // Mettre à jour la position y pour le prochain contrôle
            //                }
            //                else
            //                {
            //                    Label textLabel = new Label();
            //                    textLabel.Text = line;
            //                    textLabel.AutoSize = true;
            //                    textLabel.Location = new Point(0, yPos);
            //                    panel_News.Controls.Add(textLabel);
            //                    yPos += textLabel.Height + 5; // Mettre à jour la position y pour le prochain contrôle
            //                }
            //            }
            //        }
            //    }

            //    sr.Close();

            //}


       
        }//public Main_Form()


        private void LoadConfiguration()
        {
            string pathOptionInstaller = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DCE_Manager");
            string pathFile = Path.Combine(pathOptionInstaller, "options.txt");

            if (Directory.Exists(pathOptionInstaller) && File.Exists(pathFile))
            {
                try
                {
                    // 1. Lecture complète du fichier
                    ParamConf.configDictionary.Clear();
                    using (FileStream fs = new FileStream(pathFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(line) && line.Contains("="))
                            {
                                var parts = line.Split(new[] { '=' }, 2);
                                string key = parts[0].Trim();
                                string value = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                                ParamConf.configDictionary[key] = value;
                            }
                        }
                    }

                    // 2. Traitement des variables globales standards
                    if (ParamConf.configDictionary.TryGetValue("upgradeTxtDownload", out string upgradeTime)) ParamConf.UpgradeTime = upgradeTime;
                    if (ParamConf.configDictionary.TryGetValue("LastNewsVersion", out string lastNews)) ParamConf.LastNewsVersion = lastNews;
                    if (ParamConf.configDictionary.TryGetValue("NbLancement", out string nbL) && int.TryParse(nbL, out int nb)) ParamManager.NbLancement = nb;
                    if (ParamConf.configDictionary.TryGetValue("ASTI_MissionFile", out string astiM)) ParamConf.AstiMissionFile = astiM;
                    if (ParamConf.configDictionary.TryGetValue("ASTI_importTemplateFolder", out string astiF)) ParamConf.AstiImportTemplateFolder = astiF;
                    if (ParamConf.configDictionary.TryGetValue("LastGithubCheckUtc", out string dateStr) && DateTime.TryParse(dateStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt)) Updater_Param.LastGithubCheckUtc = dt;

                    // 3. Détection dynamique de TOUTES les configurations existantes par ID
                    ParamConf.configMap.Clear();
                    comboBox_Config.Items.Clear();
                    ParamConf.NumMaxConfig = 0;

                    // Trouver tous les IDs uniques de configurations utilisés dans le fichier
                    HashSet<int> configIds = new HashSet<int>();
                    foreach (var key in ParamConf.configDictionary.Keys)
                    {
                        if (key.StartsWith("config_"))
                        {
                            int nextUnderscore = key.IndexOf('_', 7);
                            if (nextUnderscore > 7)
                            {
                                string idStr = key.Substring(7, nextUnderscore - 7);
                                if (int.TryParse(idStr, out int id))
                                {
                                    configIds.Add(id);
                                    if (id > ParamConf.NumMaxConfig) ParamConf.NumMaxConfig = id;
                                }
                            }
                        }
                    }

                    // Pour chaque ID trouvé, on récupère son nom ou on lui en attribue un s'il est manquant
                    foreach (int id in configIds)
                    {
                        string nameKey = $"config_{id}_";
                        string configName = string.Empty;

                        if (ParamConf.configDictionary.TryGetValue(nameKey, out string storedName) && !string.IsNullOrWhiteSpace(storedName))
                        {
                            configName = storedName;
                        }
                        else
                        {
                            // Récupération automatique si le nom était absent : on nomme par défaut ou "Main" si ID 1
                            configName = (id == 1) ? "Main" : $"Configuration_{id}";
                            ParamConf.configDictionary[nameKey] = configName;
                        }

                        // Enregistrement sécurisé sans doublon de nom dans l'UI
                        if (!ParamConf.configMap.ContainsKey(configName))
                        {
                            ParamConf.configMap.Add(configName, id);
                            comboBox_Config.Items.Add(configName);
                        }
                    }

                    // 4. Application de la configuration active ("display")
                    string selectedItem = "";
                    if (ParamConf.configDictionary.TryGetValue("display", out string displayValue) && !string.IsNullOrEmpty(displayValue))
                    {
                        selectedItem = displayValue;
                        if (ParamConf.configMap.TryGetValue(displayValue, out int configNumber))
                        {
                            ParamConf.NumSelectConfig = configNumber;
                        }
                    }

                    // Fallback de sélection si "display" est introuvable
                    if (!string.IsNullOrEmpty(selectedItem) && comboBox_Config.Items.Contains(selectedItem))
                    {
                        comboBox_Config.SelectedItem = selectedItem;
                        ParamConf.CurrentConfigName = selectedItem;
                    }
                    else if (comboBox_Config.Items.Count > 0)
                    {
                        comboBox_Config.SelectedIndex = 0;
                        ParamConf.CurrentConfigName = comboBox_Config.SelectedItem.ToString();
                        if (ParamConf.configMap.TryGetValue(ParamConf.CurrentConfigName, out int fallbackId))
                        {
                            ParamConf.NumSelectConfig = fallbackId;
                        }
                    }

                    this.Text = "DCE_Manager - " + ParamConf.CurrentConfigName;

                    // 5. Hydrater l'interface avec les chemins du profil sélectionné
                    string prefix = $"config_{ParamConf.NumSelectConfig}_";
                    textBox_PATH_DCS_Root.Text = ParamConf.configDictionary.TryGetValue(prefix + "pathDCS", out var pDcs) ? pDcs : "";
                    ParamConf.PATH_DCS_Root = textBox_PATH_DCS_Root.Text;

                    textBox_SavedGames.Text = ParamConf.configDictionary.TryGetValue(prefix + "pathSavedGames", out var pSaved) ? pSaved : "";
                    ParamConf.PATH_SavedGames_DCS = textBox_SavedGames.Text;

                    textBox_OvGME.Text = ParamConf.configDictionary.TryGetValue(prefix + "pathOVGME", out var pOvgme) ? pOvgme : "";
                    ParamConf.PATH_OVGME_MOD = textBox_OvGME.Text;
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "Form1_LoadConfig", pathOptionInstaller, true, true);
                }

                // SÉCURITÉ PREMIER LANCEMENT : Si aucune configuration n'a été chargée
                if (comboBox_Config.Items.Count == 0)
                {
                    // On crée un profil par défaut "Main" associé à l'ID 1
                    ParamConf.NumMaxConfig = 1;
                    ParamConf.NumSelectConfig = 1;
                    ParamConf.CurrentConfigName = "Main";

                    ParamConf.configDictionary["config_1_"] = "Main";
                    ParamConf.configDictionary["config_1_pathDCS"] = "";
                    ParamConf.configDictionary["config_1_pathSavedGames"] = "";
                    ParamConf.configDictionary["config_1_pathOVGME"] = "";
                    ParamConf.configDictionary["display"] = "Main";

                    ParamConf.configMap.Add("Main", 1);
                    comboBox_Config.Items.Add("Main");
                    comboBox_Config.SelectedItem = "Main";

                    // On génère le premier fichier options.txt propre instantanément
                    Configuration_Form.Save_Config();
                }
            }
        }



        private void comboBox_Config_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isInitializing)
                return; // on ignore les changements déclenchés pendant le chargement initial

            // 1. On récupère le nom sélectionné (sécurisé avec "as string")
            string selectedName = comboBox_Config.SelectedItem as string;

            if (string.IsNullOrEmpty(selectedName)) return;

            // 2. On le stocke "seulement" en mémoire dans notre nouvelle variable
            ParamConf.CurrentConfigName = selectedName;

            // 3. On cherche le numéro de configuration correspondant
            if (ParamConf.configMap.TryGetValue(selectedName, out int configNumber))
            {
                ParamConf.NumSelectConfig = configNumber;
            }

            // Mise à jour de l'ancien dictionnaire si vous en avez encore besoin ailleurs
            ParamConf.configDictionary["display"] = selectedName;

            ParamConf.CurrentConfigName = selectedName;

            // 4. On remplit les TextBox de façon sécurisée (pour éviter le "vrai bordel" / crashs)
            string prefix = $"config_{ParamConf.NumSelectConfig}_";

            //textBox_Campaign.Text = ParamConf.configDictionary.TryGetValue(prefix + "pathZipCampaign", out var p1) ? p1 : "";
            textBox_PATH_DCS_Root.Text = ParamConf.configDictionary.TryGetValue(prefix + "pathDCS", out var p2) ? p2 : "";
            ParamConf.PATH_DCS_Root = ParamConf.configDictionary.TryGetValue(prefix + "pathDCS", out var p2b) ? p2b : "";

            textBox_SavedGames.Text = ParamConf.configDictionary.TryGetValue(prefix + "pathSavedGames", out var p3) ? p3 : "";
            ParamConf.PATH_SavedGames_DCS = ParamConf.configDictionary.TryGetValue(prefix + "pathSavedGames", out var p3b) ? p3b : "";

            textBox_OvGME.Text = ParamConf.configDictionary.TryGetValue(prefix + "pathOVGME", out var p4) ? p4 : "";
            ParamConf.PATH_OVGME_MOD = ParamConf.configDictionary.TryGetValue(prefix + "pathOVGME", out var p4b) ? p4b : "";

            // Le reste de vos appels asynchrones...+
            _ = scriptsModUpdater.CheckGithubScriptsModVersionAsync();
            _ = dceManagerUpdater.CheckGithubDCEManagerVersionAsync();
            _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, ParamConf.PATH_SavedGames_DCS);

            InitializeDCS_Installation_Path();

            // Si l'onglet Campaign est actif, on recharge sa grille avec les campagnes
            // de la config qui vient d'être sélectionnée (sinon elle reste affichée avec les anciennes)
            if (tabControl_LEFT.SelectedTab == tabPageLeft_Campaigns)
            {
                _ = CampaignGridLeft.LoadCampaignsAsync();
            }

            this.Text = "DCE_Manager " + " - " + selectedName;
        }

        private void but_EditConfig_Click(object sender, EventArgs e)
        {
            // On crée une nouvelle instance de la fenêtre Configuration_Form
            using (Configuration_Form astiWindow = new Configuration_Form())
            {
                // ShowDialog() ouvre la fenêtre de manière bloquante (modale)
                astiWindow.ShowDialog(this);
            } // La fenêtre est proprement détruite en mémoire une fois fermée grâce au "using"

        }

        public void UpdateComboBoxConfigList()
        {
            comboBox_Config.Items.Clear();
            foreach (var configName in ParamConf.configMap.Keys)
            {
                if (!string.IsNullOrWhiteSpace(configName))
                {
                    comboBox_Config.Items.Add(configName);
                }
            }

            if (!string.IsNullOrEmpty(ParamConf.CurrentConfigName))
            {
                comboBox_Config.SelectedItem = ParamConf.CurrentConfigName;
            }
        }




        private void AjusterLargeurTextBox(TextBox tb)
        {
            using (Graphics g = tb.CreateGraphics())
            {
                SizeF size = g.MeasureString(tb.Text, tb.Font);
                tb.Width = (int)size.Width + 10; // marge de 10 pixels
            }
        }

        // Méthode exécutée après la fermeture du formulaire
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            // Inscrire ici les actions à réaliser après la fermeture
            string pathOptionInstaller = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager";
            string filePath = pathOptionInstaller + @"\options.txt";

            // Écriture dans le fichier config
            try
            {
                Configuration_Form.Save_Config();
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Form1_FormClosed Error writing config: ", filePath, true, true);
            }
        }

        //check sanitizeModule ?
        public void checkBoxMod()
        {
            string pathFile = textBox_PATH_DCS_Root.Text + @"\Scripts\MissionScripting.lua";

            Boolean find_OS = false;
            Boolean find_IO = false;

            if (File.Exists(pathFile))
            {
                
                //checkBoxSanitize.Enabled = true;
                using (StreamReader reader = new StreamReader(pathFile))
                {
                    string line;
                    
                    while ((line = reader.ReadLine()) != null)
                    {
                        int nbcaractereOS = line.IndexOf("sanitizeModule('os");
                        if (nbcaractereOS > -1)
                        {

                            //MessageBox.Show("passe _o_",  nbcaractere.ToString());
                            int nbCaractTiret = line.IndexOf("--");
                            if (nbCaractTiret > -1 && nbCaractTiret < nbcaractereOS)
                            {
                                find_OS = true;
                            }
                        }
                        int nbcaractereIO = line.IndexOf("sanitizeModule('io");
                        if (nbcaractereIO > -1)
                        {
                            int nbCaractTiret = line.IndexOf("--");
                            if (nbCaractTiret > -1 && nbCaractTiret < nbcaractereIO)
                            {
                                find_IO = true;
                            }
                        }
                    }
                }

                if (find_OS && find_IO)
                {
                    //checkBoxSanitize.Checked = true;
                }
                else
                {
                    //checkBoxSanitize.Checked = false;
                }
            }
            else
            {
                //checkBoxSanitize.Enabled = false;
            }
        }

        public void CopyFilesRecursively(string sourcePath, string targetPath)
        {
            int i = 0;
            int n = 0;
            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "CopyFilesRecursively", sourcePath, true, true);
                }
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                //File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);                
                try
                {
                    File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
                    n++;
                }
                catch (FileNotFoundException e1)
                {
                    i++;
                }
                catch (Exception ex)
                {
                    i++;
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "CopyFilesRecursively", sourcePath, false, true);
                }
            }

            if (i > 0)
            {
                MessageBox.Show("Number of files that cannot be copied: " + i.ToString(), "Error");
            }
            else if (i == 0)
            {
                MessageBox.Show("Number of files copied: " + n.ToString(), "Report");
            }
            if (i == 0)
            {
                //my code goes here
            }
        }

        public void ExtractZipFileToDirectory(string sourceZipFilePath, bool overwrite)
        {
            //MessageBox.Show("ExtractZipFileToDirectory checkBox_OvwNGfolder.Checked? " + checkBox_OvwNGfolder.Checked.ToString());

            using (var archive = ZipFile.Open(sourceZipFilePath, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    string DestFullName = file.FullName;
                    string[] words = DestFullName.Split('/');
                    string LowerWordZero = words[0].ToLowerInvariant();

                    bool extractAutorise = true;
                    string destinationDirectoryName = "";

                    // Valider la structure du fichier zip
                    if (System.Text.RegularExpressions.Regex.IsMatch(sourceZipFilePath.Replace("_", " "), words[0]))
                    {
                        MessageBox.Show("Incompatible ZIP file structure.", "Error ExtractZipFileToDirectory");
                        return;
                    }

                    // Vérification des extensions de fichiers inutiles
                    if (words.Length <= 2 && (Path.GetExtension(words[0]) == ".pdf" || Path.GetExtension(words[0]) == ".exe" || Path.GetExtension(words[0]) == ".txt"))
                        continue;

                    // Déterminer le répertoire cible en fonction du contenu du zip
                    if (!LowerWordZero.Contains("mod") && !words[0].Contains("tech") &&
                        !Regex.IsMatch(LowerWordZero, "savedgame") && !Regex.IsMatch(LowerWordZero, "ovgme"))
                    {
                        destinationDirectoryName = Path.Combine(ParamConf.PATH_SavedGames_DCS, "Liveries");
                    }
                    else if (words[0].Contains("tech"))
                    {
                        destinationDirectoryName = Path.Combine(ParamConf.PATH_SavedGames_DCS, "Mods");
                    }
                    else if (words[0].Contains("Missionscript_mod") || words[0].Contains("MOD") || Regex.IsMatch(LowerWordZero, "ovgme"))
                    {
                        destinationDirectoryName = ParamConf.PATH_SavedGames_DCS;
                        DestFullName = DestFullName.Replace(words[0] + "/", "");
                    }
                    else if (Regex.IsMatch(LowerWordZero, "savedgame"))
                    {
                        destinationDirectoryName = ParamConf.PATH_SavedGames_DCS;
                        DestFullName = DestFullName.Replace(words[0] + "/", "");

                        // Vérification pour "ScriptsMod.NG"
                        if (words.Contains("ScriptsMod.NG"))
                        {
                            // Construire le chemin complet vers le dossier ScriptsMod.NG en utilisant le répertoire de base (destinationRoot)
                            string scriptsModPath = Path.Combine(destinationDirectoryName, "Mods", "tech", "DCE", "ScriptsMod.NG");

                            // Vérifier si le dossier ScriptsMod.NG existe déjà et s'il contient UTIL_Changelog.lua
                            if (Directory.Exists(scriptsModPath) && File.Exists(Path.Combine(scriptsModPath, "UTIL_Changelog.lua")))
                            {
                                extractAutorise = false; // Interdire la création et l'extraction des fichiers dans ScriptsMod.NG

                                // Permettre l'extraction uniquement si la case checkBox_OvwNGfolder est cochée
                                //if (checkBox_OvwNGfolder.Checked)
                                //{
                                //    extractAutorise = true;
                                //}
                            }
                        }

                        else
                        {
                            foreach (string word in words)
                            {
                                if (Regex.IsMatch(word, "Active", RegexOptions.IgnoreCase) || Regex.IsMatch(word, "Debug", RegexOptions.IgnoreCase) || Regex.IsMatch(word, "Debriefing", RegexOptions.IgnoreCase))
                                {
                                    extractAutorise = false;
                                    break;
                                }
                            }
                        }

                    }
                    else if (words[0].Contains("Liveries") || words[0].Contains("Mods") || words[0].Contains("aircraft"))
                    {
                        destinationDirectoryName = ParamConf.PATH_SavedGames_DCS;
                    }

                    // Créer le répertoire cible si nécessaire
                    DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
                    string destinationDirectoryFullPath = di.FullName;
                    string completeFileName = Path.GetFullPath(Path.Combine(destinationDirectoryFullPath, DestFullName));

                    // Vérification pour éviter les vulnérabilités Zip-Slip
                    if (!completeFileName.StartsWith(destinationDirectoryFullPath, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Trying to extract file outside of destination directory.");

                    // Si l'entrée du zip est un dossier, le créer sans extraire de fichiers
                    if (file.Name == "")
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(completeFileName));
                        }
                        catch (Exception ex)
                        {
                            FormUtils.ErrorGeneral_BoxOrLog(ex, "The process failed", completeFileName, true, true);
                        }
                        continue;
                    }

                    // Si l'extraction est autorisée pour le fichier, l'extraire
                    if (extractAutorise)
                    {
                        try
                        {
                            file.ExtractToFile(completeFileName, overwrite);
                        }
                        catch (Exception ex)
                        {
                            FormUtils.ErrorGeneral_BoxOrLog(ex, "Error extracting file", file.FullName, true, true);
                        }
                    }
                }
            }
        }

        public static System.Drawing.Icon Question { get; }

        //private const int column_width = 150;
        //private const int row_height = 50;

        // private TabControl tabControl_SquadEditMain; ???
        public class MyTabControl : TabControl
        {
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
            public new TabPage SelectedTab
            {
                get { return base.SelectedTab; }
                set { base.SelectedTab = value; }
            }
        }



        private void button_EXIT_Click(object sender, EventArgs e)
        {

            Close();
        }

        private void linkLabelOvGME_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                VisitLinkOvGME();
            }
            catch (Exception ex)
            {
                //MessageBox.Show("Unable to open link that was clicked.");
                //FormUtils.ShowErrorMessage(ex.Message + " \n Unable to open link that was clicked.");
                FormUtils.ErrorGeneral_BoxOrLog(ex, " Unable to open link that was clicked. " , " " , true, true);

            }
        }
        private void VisitLinkOvGME()
        {
            // Change the color of the link text by setting LinkVisited
            // to true.
            //linkLabelOvGME.LinkVisited = true;
            //Call the Process.Start method to open the default browser
            //with a URL:
            System.Diagnostics.Process.Start("https://wiki.hoggitworld.com/view/PATH_OVGME_MOD#Download_the_installer");
        }

        //private void linkLabelCampaign_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        //{
        //    try
        //    {
        //        VisitLinkCampaign();
        //    }
        //    catch (Exception ex)
        //    {
        //        //MessageBox.Show("Unable to open link that was clicked.");
        //        //FormUtils.ShowErrorMessage(ex.Message + " \n Unable to open link that was clicked.");
        //        FormUtils.ErrorGeneral_BoxOrLog(ex, " Unable to open link that was clicked. ", " ", true, true);
        //    }
        //}
        //private void VisitLinkCampaign()
        //{
        //    // Change the color of the link text by setting LinkVisited
        //    // to true.
        //    linkLabelCampaign.LinkVisited = true;
        //    //Call the Process.Start method to open the default browser
        //    //with a URL:
        //    System.Diagnostics.Process.Start("https://forums.eagle.ru/topic/162712-dce-campaigns/");
        //}

      

        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =
        // Update du ScriptsMod
        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =

        private async void ScriptsModUpdateButton_Click( object sender, EventArgs e)
        {
            FormUtils.LogRegister(
                "Test GitHub Version : " +
                ParamGithub.LastVersion +
                "\r\nAsset : " +
                ParamGithub.AssetName +
                "\r\nURL : " +
                ParamGithub.DownloadUrl);

            await scriptsModUpdater.DownloadLatestScriptsMod();


        }





        async void TabControl1_SelectedAsync(object sender, TabControlEventArgs e)
        {

            if (e.TabPage != tabPageLeft_Campaigns)
            {
                // On quitte (ou on n'est pas sur) l'onglet Campaigns → reset complet
                CampaignGridLeft.ResetCurrentCampaign();

                buttonSaveChgtCampaign.Visible = false;
                buttonResetBackup.Visible = false;
                radioButton_OOB_INIT.Visible = false;
                radioButton_OOB_ACTIVE.Visible = false;
            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage1 Install      +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            if (e.TabPage == tabPageLeft_Install)
            {
                checkBoxMod();
                groupBoxDroiteAccueil.Visible = true;
                //groupBoxCampEdit.Visible = false;
                //groupBox_staticTemplate.Visible = false;
                //groupBoxCampEdit.Text = "";

                //CampaignTab.Visible = false;

                CampaignGridLeft.UpdateCampaignButtonsVisibility();


            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage2   CAMPAIGNS GRID    +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeft_Campaigns)
            {
                Cursor.Current = Cursors.WaitCursor;

                groupBoxDroiteAccueil.Visible = false;
                 _ = CampaignGridLeft.LoadCampaignsAsync();

                Cursor.Current = Cursors.Default;

                CampaignGridLeft.UpdateCampaignButtonsVisibility();
            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage3   UPDATE    +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeft_Update)
            {
                groupBoxDroiteAccueil.Visible = true;
                FormUtils.MakeRoundedButton( ScriptsModUpdateButton, 10);

                FormUtils.MakeRoundedButton( DCEManagerUpdateButton, 10);

                FormUtils.MakeRoundedButton(  buttonCampaignCancel, 10);


                //CampaignTab.Visible = false;

                DCEManagerInstalledVersion.Text = VersionDceManager.Text;

                if (String.IsNullOrEmpty(ParamConf.PATH_SavedGames_DCS))
                {
                    MessageBox.Show("You must enter the path to the SavedGame folder in the \"Install\" tab ", "Report");
                    return;
                }

                Directory.CreateDirectory(ParamManager.pathManager);
                Updater_Param.CreateFolders();

                CampaignGridLeft.UpdateCampaignButtonsVisibility();

            }
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage?   ABOUT    +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeft_About)
            {
                groupBoxDroiteAccueil.Visible = true;

                //CampaignTab.Visible = false;

                if (textBox_ChangelogScriptsMod.Text == "")
                {

                    //Affiche le changelog
                    string ChangelogFileSM = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua";
                    bool ChangelogFileSMExist = File.Exists(ChangelogFileSM);

                    textBox_ChangelogScriptsMod.Text = "";

                    const Int32 BufferSize = 4096;

                    if (ChangelogFileSMExist)
                    {
                        using (StreamReader reader = new StreamReader(ChangelogFileSM, Encoding.UTF8, true, BufferSize))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (((line.Length >= 2 && line.Substring(0, 2) != "--") | (line.Length <= 2)) &&
                                    !System.Text.RegularExpressions.Regex.IsMatch(line, "versionDCE") &&
                                    !System.Text.RegularExpressions.Regex.IsMatch(line, "VersionDCE")
                                    )
                                {
                                    textBox_ChangelogScriptsMod.Text = textBox_ChangelogScriptsMod.Text + line + "\r\n";
                                }
                            }
                            reader.Close();
                        }
                    }
                }

                CampaignGridLeft.UpdateCampaignButtonsVisibility();
            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage?   NEWS    +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeftNews)
            {
                groupBoxDroiteAccueil.Visible = true;
                //CampaignTab.Visible = false;
                CampaignGridLeft.UpdateCampaignButtonsVisibility();

            }

            //else
            //{
            //    // On quitte l’onglet CampaignTab → on cache tout
            //    //buttonSaveChgtCampaign.Visible = false;
            //    //buttonResetBackup.Visible = false;
            //    //radioButton_OOB_INIT.Visible = false;
            //    //radioButton_OOB_ACTIVE.Visible = false;
            //    CampaignGridLeft.UpdateCampaignButtonsVisibility();
            //}
        }




        //int UpdateA = 1;
        //public System.Windows.Forms.Label UpdateAddNewLabelA(string NameCamp)
        //{
        //    System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

        //    txt.Top = UpdateA * 20 + 23;
        //    txt.Left = 25;
        //    txt.AutoSize = true;
        //    //txt.Size = new System.Drawing.Size(170, 20);
        //    txt.Text = NameCamp;
        //    UpdateA = UpdateA + 1;
        //    return txt;
        //}
        //int UpdateB = 1;

        //int UpdateC = 1;


        public string GetVersionDceManager()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;

            // On ne garde que les 3 premiers composants (Major, Minor, Build)
            string versionString = String.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);

            ParamManager.verDceManager = versionString;
            return versionString;
        }

 
        private async void DCEManagerUpdateButton_Click( object sender, EventArgs e)
        {
            {
                DCEManagerUpdateButton.Enabled = false;

                await dceManagerUpdater.DownloadLatestDCEManager();

                DCEManagerUpdateButton.Enabled = true;
            }

        }

        
        private void button_Log_Click(object sender, EventArgs e)
        {

            string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
            bool pathManagerExists = System.IO.Directory.Exists(pathManager);

            bool fileExists = File.Exists(pathManager + @"\log.txt");
            if (pathManagerExists && fileExists)
            {
                Process process = new Process();
                // Configure the process using the StartInfo properties.

                process.StartInfo.FileName = pathManager + "log.txt";
                process.StartInfo.Arguments = " ";
                process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                process.StartInfo.WorkingDirectory = pathManager;

                process.Start();
            }
        }


        private void buttonDocFolder_Click(object sender, EventArgs e)
        {
            string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
            bool pathManagerExists = System.IO.Directory.Exists(pathManager);

            if (pathManagerExists  )
            {
                //Process process = new Process();
                // Configure the process using the StartInfo properties.
                Process.Start(pathManager);
            }
        }

        private void pictureBox5_Click(object sender, EventArgs e)
        {
            Process process = new Process();
            // Configure the process using the StartInfo properties.BoxOvGME
            //process.StartInfo.FileName = ParamCampaign.pathCampaign + @"\FirstMission.bat";
            process.StartInfo.FileName = textBox_PATH_DCS_Root.Text + @"\bin\DCS.exe";
            process.StartInfo.Arguments = " ";
            process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            process.StartInfo.WorkingDirectory = textBox_PATH_DCS_Root.Text + @"\bin";

            process.Start();
        }

        private void pictureBoxOvGME_Click(object sender, EventArgs e)
        {
            string OvGME_Path = FormUtils.IsApplicationInstalled("OvGME");
            string Empty = "";
            bool result = Empty.Equals(OvGME_Path);

            //MessageBox.Show(OvGME_Path, OvGME_Path);
            if (!result)
            {

                Process process = new Process();

                process.StartInfo.FileName = OvGME_Path + @"\OvGME.exe";
                process.StartInfo.Arguments = " ";
                process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                process.StartInfo.WorkingDirectory = OvGME_Path;

                process.Start();
            }
        }

       
        private void ScriptModInstalledVersion_Click(object sender, EventArgs e)
        {
            Process process = new Process();
            // Configure the process using the StartInfo properties.
            process.StartInfo.FileName = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\ScriptsMod.NG";
            process.StartInfo.Arguments = " ";
            process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            //process.StartInfo.WorkingDirectory = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" ;

            process.Start();
        }


        internal class version
        {
            private string v1;

            public version(string v1)
            {
                this.v1 = v1;
            }

            internal object CompareTo(Version version2)
            {
                throw new NotImplementedException();
            }
        }


        private void checkBoxSanitize_CheckedChanged(object sender, EventArgs e)
        {
            string pathFile = textBox_PATH_DCS_Root.Text + @"\Scripts\MissionScripting.lua";
            var tmp = Environment.GetEnvironmentVariable("tmp");
            string pathFileTemp = tmp + @"\MissionScriptingTMP.lua";

            if (File.Exists(pathFile))
            {
                
                try
                {
                    File.Copy(pathFile, pathFileTemp, true);
                }
                 catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "checkBoxSanitize_CheckedChanged", pathFile, true, true);
                }
            

                using (StreamReader reader = new StreamReader(pathFileTemp))
                {
                    string line;
                    int i = 1;
                    while ((line = reader.ReadLine()) != null)
                    {

                        if (line.IndexOf("sanitizeModule('os") > -1 | line.IndexOf("sanitizeModule('io") > -1)
                        {
                            //if (checkBoxSanitize.Checked == true)
                            //{
                            //    string ligneModifiee = "--" + line;
                            //    FormUtils.ModifierLigneByNumber(pathFile, i, ligneModifiee);
                            //}
                            //else
                            //{
                            //    string ligneModifiee = line.Replace("--", "");
                            //    FormUtils.ModifierLigneByNumber(pathFile, i, ligneModifiee);
                            //}
                               
                        }

                        i++;
                    }
                }
            }
        }

        void CampaignTab_Selected(object sender, TabControlEventArgs e)
        {
            CampaignGridLeft.UpdateCampaignButtonsVisibility();
        }



        //private void buttonSaveActive_Click(object sender, EventArgs e)
        //{
        //    string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Active\oob_air.lua";
           
        //    CampaignGridLeft.ModifiedCampaign(pathFile, null, "Active");

        //    PublicTable.errorTable.Clear();
        //    textBox_Bugs.Text = "";
        //    tabPage12.Text = "Bugs";

        //    //CampaignEdit1(sender, e, pathFile, groupBoxCampEdit.Text);
        //}

        //private void buttonResetBackup_Click(object sender, EventArgs e)
        //{
        //    // Initializes the variables to pass to the MessageBox.Show method.
        //    string message = "Do you really want to go back to the original values?";
        //    string caption = "Caution";
        //    MessageBoxButtons buttons = MessageBoxButtons.YesNo;
        //    DialogResult result;

        //    // Displays the MessageBox.
        //    result = MessageBox.Show(message, caption, buttons);
        //    if (result == System.Windows.Forms.DialogResult.Yes)
        //    {
        //        string pathFileBackup = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init_backup_DTT.lua";
        //        string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init.lua";
        //        //string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_initCLONE.lua";

        //        //sauvegarde la fichier oob_air_init pour éviter de l'écraser et le réutiliser si pb
        //        if (File.Exists(pathFileBackup))
        //        {
        //            try
        //            {
        //                File.Copy(pathFileBackup, pathFile, true);
        //            }
        //            catch (IOException iox)
        //            {
        //                //MessageBox.Show(iox.Message, "Info");
        //                FormUtils.ShowErrorMessage(iox.Message);
        //            }
        //        }
        //    }

        //    // Remplacez l'appel statique CampaignEdit.LoadSquads(); par un appel sur l'instance _currentCampaignEdit
        //    if (_currentCampaignEdit != null)
        //    {
        //        _currentCampaignEdit.LoadSquads();
        //    }


        //    string path = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign;

        //}

        public bool ButtonPreview = false;
        public void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A)
            {
                ButtonPreview = true;
            }
        }

        public void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            ////if (e.KeyCode == Keys.Control)
            if (e.KeyCode == Keys.A)
            {
                ButtonPreview = false;
            }
        }


        private void VersionDceManager_Click(object sender, EventArgs e)
        {
            //Pour devenir DEV

            if (ButtonPreview == true ) {

                GetVersionDceManager();
                //but_GPS_LL.Visible = true;
                //LabelStatut.Text = "DEV";
                this.Text = "DCE_Manager - DEV - " + ParamConf.CurrentConfigName;
                ScriptsModUpdateButton.Text = "Update DEV";
                textBox_id_client.Visible = true;
            }

        }


        public void radioButton_OOB_INIT_CheckedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingState)
                return;

            if (!radioButton_OOB_INIT.Checked) return;

            currentState = "Init";
            CampaignGridLeft.RefreshGrids();
        }

        public void radioButton_OOB_ACTIVE_CheckedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingState)
                return;

            if (!radioButton_OOB_ACTIVE.Checked) return;

            currentState = "Active";
            CampaignGridLeft.RefreshGrids();
        }


        private void linkLabel_Icons8_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // Ouvre automatiquement le navigateur web par défaut vers le site d'Icons8
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://icons8.com",
                UseShellExecute = true // Requis sous .NET Core / .NET 5+ pour ouvrir un l'URL
            });

        }

        private void Readme_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                // Remplace par la vraie URL de ton dépôt GitHub.
                // L'ancre #privacy-analytics ciblera directement ton titre.

                //string url = "https://github.com/Miguel21bis/DCE_Manager/blob/main/README.md#privacy--analytics";
                string url =
                    $"https://github.com/{GithubHelper.GithubAccount}/{GithubHelper.Repository_Manager}/blob/main/README.md#privacy--analytics";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true // Requis pour .NET Core / .NET 5+ / .NET 6+
                });
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Impossible d'ouvrir le lien de confidentialité : " + ex.Message);
            }
        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            FormUtils.LogRegister("FormMain Form1_Shown comboBox_Config_SelectedIndexChanged() ");

            await campaignUpdater.RefreshCampaignUpdates(
                CampaignDataGridView,
                ParamConf.PATH_SavedGames_DCS);
        }

        private void InitActionRows()
        {
            // Assignation de l'événement Clic pour TOUS les composants de la ligne Log
            panel_ViewLog.Click += Action_ViewLog;
            lbl_ViewLogTitle.Click += Action_ViewLog;
            lbl_ViewLogDesc.Click += Action_ViewLog;
            pic_ViewLogIcon.Click += Action_ViewLog; // Si vous avez nommé votre PictureBox ainsi
            pic_ViewLog_ArrowLog.Click += Action_ViewLog;

            // Même chose pour la ligne Documentation
            panel_OpenDoc.Click += Action_OpenDoc;
            lbl_OpenDocTitle.Click += Action_OpenDoc;
            lbl_OpenDocDesc.Click += Action_OpenDoc;
            pic_OpenDocIcon.Click += Action_ViewLog; // Si vous avez nommé votre PictureBox ainsi
            pic_OpenDoc_ArrowLog.Click += Action_ViewLog;
        }

        // --- LES ACTIONS REELLES ---

        private void Action_ViewLog(object sender, EventArgs e)
        {
            // Chemin vers votre fichier log
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DCE_Manager", "log.txt");

            if (File.Exists(logPath))
            {
                // Ouvre le fichier avec le bloc-notes (ou l'éditeur par défaut du système)
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("Le fichier log n'a pas encore été créé.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void Action_OpenDoc(object sender, EventArgs e)
        {
            // Chemin direct vers C:\Users\toto\Documents\DCE_Manager
            string docFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DCE_Manager");

            if (Directory.Exists(docFolder))
            {
                try
                {
                    // Ouvre l'explorateur Windows directement sur ce dossier
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = docFolder,
                        UseShellExecute = true,
                        Verb = "open" // Force explicitement l'action d'ouverture
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Impossible d'ouvrir le dossier : {ex.Message}", "Erreur Système", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show($"Le dossier est introuvable à l'emplacement :\n{docFolder}", "Dossier Manquant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static bool CanDeleteFile(string path)
        {
            try
            {
                using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void but_ASTI_Click(object sender, EventArgs e)
        {
            // On crée une nouvelle instance de la fenêtre ASTI_Form
            using (ASTI_Form astiWindow = new ASTI_Form())
            {
                // ShowDialog() ouvre la fenêtre de manière bloquante (modale)
                astiWindow.ShowDialog(this);
            } // La fenêtre est proprement détruite en mémoire une fois fermée grâce au "using"
        }

        private void buttonSaveChgtCampaign_Click(object sender, EventArgs e)
        {
            CampaignGridLeft.CurrentCampaignEdit?.buttonSaveChgtCampaign_Click(sender, e);
        }

        private void buttonResetBackup_Click(object sender, EventArgs e)
        {
            CampaignGridLeft.CurrentCampaignEdit?.buttonResetBackup_Click(sender, e);
        }



    }

}