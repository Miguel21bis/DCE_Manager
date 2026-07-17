using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Update;
using DCE_Manager.Utils;
using Microsoft.VisualBasic.FileIO;
using Ookii.Dialogs.WinForms;
using static DCE_Manager.Utils.FormUtils;
using SearchOption = System.IO.SearchOption;



//namespace DCE_Manager
//{
//    public partial class Form1 : Form
//    {
//        // 1. champs privés

//        // 2. propriétés publiques

//        // 3. constructeur

//        // 4. méthodes d'initialisation

//        // 5. méthodes de chargement des données

//        // 6. méthodes DataGridView

//        // 7. méthodes utilitaires
//    }
//}


namespace DCE_Manager
{
    public partial class Form1 : Form
    {

        // Cache des images de campagnes (évite rechargement disque)
        private Dictionary<string, Image> campaignImageCache = new Dictionary<string, Image>();

        private bool _isUpdatingState = false;

        public static Form1 Instance { get; private set; }

        public DataTable dataTable;

        public string currentState = "Init";
        public List<Squad> currentSquads = new List<Squad>();

        private CampaignEdit _currentCampaignEdit;

        private ScriptsModUpdater scriptsModUpdater;

        private DCEManagerUpdater dceManagerUpdater;

        public CampaignUpdater campaignUpdater;

        private bool _isInitializing = true; // champ de classe

        private string _cachedDcsRootPath;
        private string _cachedSavedGamesPath;
        private string _cachedOvGmePath;

        private string _selectedCampaignZipPath = "";


        //constructeur :
        public Form1()
        {

            // C'est ici qu'on passe "this" (qui représente le Form en cours)
            campaignUpdater = new CampaignUpdater(this);

            KeyPreview = true;

            InitializeComponent();

            //BindConfiguration();

            dropZoneControl1.FilesSelected += DropZone_FilesSelected;
            //InitializeDropZone();

            InitActionRows();

            CampaignUpdater.InitCampaignUpdateGrid(CampaignDataGridView);
            //this.Shown += Form1_Shown;

            scriptsModUpdater = new ScriptsModUpdater(this);
            dceManagerUpdater = new DCEManagerUpdater(this);

            //*************************
            InitializeCampaignGrid();
            //*************************

            CampaignDataGridView.CellContentClick += CampaignDataGridView_CellContentClick;

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

            //ScriptsModStatusLabel.Text = "Status : Checking...";

            textBox_id_client.Text = Statistic.CreateIdClient();
            AjusterLargeurTextBox(textBox_id_client);


            // Appel de la méthode de chargement
            LoadConfiguration();



            _isInitializing = false;

            _ = scriptsModUpdater.CheckGithubScriptsModVersionAsync();

            _ = dceManagerUpdater.CheckGithubDCEManagerVersionAsync();

            _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, textBox_SavedGames.Text);

            //ParamManager.NbLancement++;

            _ = Statistic.EnvoiStatsAsync(checkBox_Stat_anonym.Checked);

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
            //toolTip1.SetToolTip(textBox_Campaign, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(Label_DCS, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");

            toolTip1.SetToolTip(m_But_Install_Browse_SavedGame, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(textBox_PATH_DCS_Root, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");
            //toolTip1.SetToolTip(Label_SavedGame, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");


            //affiche le changelog
            textBox_changelog.Text = DCE_Manager.Properties.Resources.changelog;

            //coche ou pas le checkbox du scriptmod
            checkBoxMod();

            //Affiche le changelog
            string ChangelogFileSM = textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua";
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


       
        }//public Form1()

        //private void BindConfiguration()
        //{
        //    // "Text" = la propriété de la TextBox à lier
        //    // AppConfig = l'objet source
        //    // nameof(AppConfig.PATH_DCS_Root) = la propriété source
        //    textBox_PATH_DCS_Root.DataBindings.Add("Text", typeof(AppConfig), nameof(ParamConf.PATH_DCS_Root), true, DataSourceUpdateMode.OnPropertyChanged);
        //    textBox_SavedGames.DataBindings.Add("Text", typeof(AppConfig), nameof(ParamConf.PATH_SavedGames_DCS), true, DataSourceUpdateMode.OnPropertyChanged);
        //    textBox_OvGME.DataBindings.Add("Text", typeof(AppConfig), nameof(ParamConf.PATH_OVGME_MOD), true, DataSourceUpdateMode.OnPropertyChanged);
        //}

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
                    if (ParamConf.configDictionary.TryGetValue("LastGithubCheckUtc", out string dateStr) && DateTime.TryParse(dateStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt)) ParamUpdater.LastGithubCheckUtc = dt;

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
            _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, textBox_SavedGames.Text);

            InitializeDCS_Installation_Path();

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

        //public void ResetUpdateTabsForNewConfig()
        //{
        //    // Reset visuel immédiat, avant que les vérifs async ne reviennent
        //    CampaignUpdater.InitCampaignUpdateGrid(CampaignDataGridView);
        //    DCEManagerInstalledVersion.Text = "";

        //    InitializeDCS_Installation_Path();

        //    _ = scriptsModUpdater.CheckGithubScriptsModVersionAsync();
        //    _ = dceManagerUpdater.CheckGithubDCEManagerVersionAsync();
        //    _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, textBox_SavedGames.Text);
        //}

        private void InitGrid()
        {
            dataGridViewCampaigns.Columns.Clear();

            // ===== CONFIG GLOBALE =====
            dataGridViewCampaigns.AllowUserToResizeColumns = true;
            dataGridViewCampaigns.AllowUserToResizeRows = true;
            dataGridViewCampaigns.RowHeadersVisible = false;
            dataGridViewCampaigns.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridViewCampaigns.MultiSelect = false;

            // ===== COLONNE CLONE =====
            AddButtonColumn("Clone", "＋", 40);

            // ===== IMAGE =====
            var colImg = new DataGridViewImageColumn()
            {
                Name = "Image",
                HeaderText = "",
                Width = 90,
                ImageLayout = DataGridViewImageCellLayout.Zoom
            };
            dataGridViewCampaigns.Columns.Add(colImg);

            // ===== TEXTE =====
            dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Name",
                HeaderText = "Campaign",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });

            // Colonne pour ouvrir le dossier
            AddButtonColumn("Folder", "📂", 55);


            dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Version",
                Width = 60,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }

            });

            dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Missions",
                Width = 50,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }

            });

            dataGridViewCampaigns.Columns.Add(new DataGridViewTextBoxColumn()
            {
                Name = "Aircraft",
                Width = 90
            });

            // ===== BOUTONS =====
            
            AddButtonColumn("First", "▶", 55);
            AddButtonColumn("Skip", "⏭", 55, useColumnTextForButtonValue: false);
            AddButtonColumn("Config", "⚙", 55);
            AddButtonColumn("Delete", "🗑", 55);


            // ===== STYLE BOUTONS =====
            foreach (DataGridViewColumn col in dataGridViewCampaigns.Columns)
            {
                //bloquer le redimensionnement
                //col.Resizable = DataGridViewTriState.False;

                if (col is DataGridViewButtonColumn)
                {
                    col.DefaultCellStyle.BackColor = Color.FromArgb(230, 230, 230);
                    col.DefaultCellStyle.ForeColor = Color.Black;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    col.DefaultCellStyle.Font = new Font("Segoe UI", 14, FontStyle.Bold);
                }
            }
        }
        private void InitStyle()
        {
            //######## STYLE ##########
            dataGridViewCampaigns.BackgroundColor = Color.FromArgb(240, 240, 240);

            dataGridViewCampaigns.DefaultCellStyle.BackColor = Color.White;
            dataGridViewCampaigns.DefaultCellStyle.ForeColor = Color.Black;

            dataGridViewCampaigns.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);

            dataGridViewCampaigns.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
            dataGridViewCampaigns.DefaultCellStyle.SelectionForeColor = Color.White;

            dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230, 230, 230);
            dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;

            dataGridViewCampaigns.CellBorderStyle = DataGridViewCellBorderStyle.None;
            dataGridViewCampaigns.RowTemplate.Height = 60;

            dataGridViewCampaigns.DefaultCellStyle.Padding = new Padding(0);
            //dataGridViewCampaigns.DefaultCellStyle.Padding = new Padding(10);

            dataGridViewCampaigns.ColumnHeadersHeight = 35;

            // SCROLL
            dataGridViewCampaigns.ScrollBars = ScrollBars.Both;

            // SCROLL//empêche le mode “compression”
            dataGridViewCampaigns.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

            dataGridViewCampaigns.CellMouseEnter += (s, e) =>
            {
                if (e.RowIndex >= 0)
                    dataGridViewCampaigns.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.FromArgb(220, 235, 252);
            };

            dataGridViewCampaigns.CellMouseLeave += (s, e) =>
            {
                if (e.RowIndex >= 0)
                    dataGridViewCampaigns.Rows[e.RowIndex].DefaultCellStyle.BackColor =
                        (e.RowIndex % 2 == 0) ? Color.White : Color.FromArgb(245, 245, 245);
            };

            //############# InitGrid END
        }

        // Initialise entièrement le DataGridView des campagnes.
        // Pourquoi : allège le constructeur et regroupe toute la configuration du grid au même endroit.
        private void InitializeCampaignGrid()
        {
            InitGrid();
            InitStyle();

            dataGridViewCampaigns.CellClick += dataGridViewCampaigns_CellClick;

            dataGridViewCampaigns.RowTemplate.Height = 70;

            dataGridViewCampaigns.DefaultCellStyle.Font = new Font("Segoe UI", 9);
            dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);

            dataGridViewCampaigns.EnableHeadersVisualStyles = false;
            dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            dataGridViewCampaigns.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;

            dataGridViewCampaigns.GridColor = Color.LightGray;
            dataGridViewCampaigns.BorderStyle = BorderStyle.None;
        }


        private void AddButtonColumn(string name, string text, int width, bool useColumnTextForButtonValue = true)
        {
            dataGridViewCampaigns.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = name,
                Text = text,
                UseColumnTextForButtonValue = useColumnTextForButtonValue,
                Width = width,
                FlatStyle = FlatStyle.Flat
            });
        }

        private void dataGridViewCampaigns_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            // Ignore header
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (e.ColumnIndex >= dataGridViewCampaigns.Columns.Count)
                return;

            string columnName = dataGridViewCampaigns.Columns[e.ColumnIndex].Name;

            // Récupérer les infos de la ligne
            string name = dataGridViewCampaigns.Rows[e.RowIndex].Cells["Name"].Value?.ToString();

            if (string.IsNullOrEmpty(name))
                return;

            string basePath = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\";
            string folderPath = Path.Combine(basePath, name);

            if (columnName == "First")
            {
                string batPath = Path.Combine(folderPath, "FirstMission.bat");

                if (File.Exists(batPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = batPath,
                        WorkingDirectory = folderPath,
                        UseShellExecute = true
                    });
                }

                return;
            }
            else if (columnName == "Skip")
            {
                string nbMissionTextSkip = dataGridViewCampaigns.Rows[e.RowIndex].Cells["Missions"].Value?.ToString();

                int nbMissionSkip;
                int.TryParse(nbMissionTextSkip, out nbMissionSkip);

                if (nbMissionSkip <= 0)
                    return; // bouton grisé : aucune mission jouée, on ignore le clic

                string batPath = Path.Combine(folderPath, "SkipMission.bat");

                if (File.Exists(batPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = batPath,
                        WorkingDirectory = folderPath,
                        UseShellExecute = true
                    });
                }
            }
            else if (columnName == "Config")
            {
                string filePath = Path.Combine(folderPath, @"Init\conf_mod.lua");

                if (File.Exists(filePath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
            }
            else if (columnName == "Delete")
            {
                var confirm = MessageBox.Show(
                    "Delete campaign " + name + " ?",
                    "Confirm",
                    MessageBoxButtons.YesNo
                );

                if (confirm == DialogResult.Yes)
                {
                    try
                    {
                        List<string> filesToDelete = Directory.Exists(folderPath)
                            ? Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories).ToList()
                            : new List<string>();

                        string parentFolder = Path.GetDirectoryName(folderPath) ?? "";
                        string prefix = name + "-";

                        string baseName = name;

                        HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            baseName,
                            baseName + "_first",
                            baseName + "_ongoing"
                        };

                        var extraFiles = Directory.GetFiles(parentFolder)
                            .Where(f => expected.Contains(Path.GetFileNameWithoutExtension(f)))
                            .ToList();

                        filesToDelete.AddRange(extraFiles);

                        //foreach (string file in filesToDelete)
                        //    FormUtils.LogRegister($"À supprimer : {file}");

                        foreach (string file in filesToDelete)
                        {
                            if (!CanDeleteFile(file))
                            {
                                MessageBox.Show("Impossible de supprimer car le fichier est utilisé :\r\n" + file);
                                return;
                            }
                        }

                        Directory.Delete(folderPath, true);

                        foreach (string file in filesToDelete.Where(File.Exists))
                        {
                            File.Delete(file); 
                            //FormUtils.LogRegister($"À supprimer : {file}");
                        }


                        // Recharge la liste
                        LoadCampaignsAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error: " + ex.Message);
                    }
                }
                return; //  IMPORTANT : stoppe ici
            }
            else if (columnName == "Clone")
            {
                CampaignPlusClickOneEvent(null, null, basePath, name);
                return;
            }
            // Si on clique sur la colonne "Folder"
            // Ouvre le dossier de la campagne dans l'explorateur Windows
            else if (columnName == "Folder")
            {
                if (Directory.Exists(folderPath))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo()
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + folderPath + "\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(
                        "Campaign folder not found:\r\n" + folderPath,
                        "Folder",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                }
            }

            dataGridViewCampaigns.ClearSelection();
            dataGridViewCampaigns.Rows[e.RowIndex].Selected = true;

            // Coche automatiquement Init ou Active selon le nombre de missions jouées
            string nbMissionText = dataGridViewCampaigns.Rows[e.RowIndex].Cells["Missions"].Value?.ToString();

            int nbMission = 0;
            int.TryParse(nbMissionText, out nbMission);


            // 1. Charger la campagne AVANT
            CampaignEdit1(null, null, folderPath + "\\" + name, name);

            // 2. Ensuite seulement appliquer le state UI
            if (nbMission <= 0)
            {
                radioButton_OOB_INIT.Checked = true;
            }
            else
            {
                radioButton_OOB_ACTIVE.Checked = true;

            }

        }

        // Charge toutes les campagnes (code existant déplacé ici)
        public async Task LoadCampaignsAsync()
        {
            dataGridViewCampaigns.Rows.Clear();

            List<CampaignInfo> campaignUpdateList = new List<CampaignInfo>();

            var LoadCampaigns = Stopwatch.StartNew();

            int nbCampaign = 0;

            bool folderCampExists = System.IO.Directory.Exists(textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns");

            if (folderCampExists)
            {
                foreach (string subFolder in Directory.GetDirectories(textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns"))
                {

                    // 🔥 cache local des fichiers (1 lecture max)
                    string campInitContent = null;
                    string campStatusContent = null;
                    string oobAirContent = null;

                    string pathCampInitFile = subFolder + @"\Init\camp_init.lua";
                    if (File.Exists(pathCampInitFile))
                        campInitContent = File.ReadAllText(pathCampInitFile);

                    string pathCampstatusFile = subFolder + @"\Active\camp_status.lua";
                    if (File.Exists(pathCampstatusFile))
                        campStatusContent = File.ReadAllText(pathCampstatusFile);

                    string path_oob_air;


                    //  COPIE ICI TOUT TON CODE ACTUEL DE LA BOUCLE
                    string[] NameCampTab = subFolder.Split('\\');
                    string NameCamp = NameCampTab[NameCampTab.Count() - 1];

                    bool folderLocExists = System.IO.Directory.Exists(subFolder);

                    //cherche la version inscrite dans path.bat
                    string PathBatFile = subFolder + @"\Init\path.bat";
                    bool fileExistPathBat = File.Exists(PathBatFile);

                    if (fileExistPathBat)
                    {
                        if (ParamConf.PATH_DCS_Root != "" & ParamConf.PATH_SavedGames_DCS != "")
                        {

                            string textPathBat = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                           "set \"pathDCS=" + ParamConf.PATH_DCS_Root + "\\\"\r\n" +
                           "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                           "set \"PATH_SavedGames_DCS=" + ParamConf.PATH_SavedGames_DCS + "\\\"\r\n" +
                           "REM DCE ScriptMod version not any / or \\ and no space before and after = \r\n" +
                           "set \"versionPackageICM=" + TestFile.ScriptsMod + "\"\r\n" +
                           "\r\n" +
                           "\r\n" +
                           "REM After each change, You must launch the FirsMission.bat for it to be taken into account.";

                            System.IO.File.WriteAllText(PathBatFile, textPathBat);
                        }



                        nbCampaign++;

                        //Cherche la version de la campagne
                        string VerCamp = "";
                        string campaignId = "";
                        string repositoryUrl = "";
                        if (campInitContent != null)
                        {
                            Match match;

                            match = Regex.Match(
                                campInitContent,
                                @"(?<!\w)version\s*=\s*""([^""]+)""");

                            if (match.Success)
                                VerCamp = match.Groups[1].Value;

                            match = Regex.Match(
                                campInitContent,
                                @"campaignId\s*=\s*""([^""]+)""");

                            if (match.Success)
                                campaignId = match.Groups[1].Value;

                            match = Regex.Match(
                                campInitContent,
                                @"repositoryUrl\s*=\s*""([^""]+)""");

                            if (match.Success)
                                repositoryUrl = match.Groups[1].Value;
                        }


                        //Cherche le nombre de mission joué
                        //['mission'] = 1,
                        string NbMission = "0";
                        if (campStatusContent != null)
                        {
                            var match = Regex.Match(campStatusContent, @"mission[""']?\]\s*=\s*(\d+)");
                            if (match.Success)
                                NbMission = (Int32.Parse(match.Groups[1].Value) - 1).ToString();
                        }


                        //cherche si une campagne doit etre reset a la suite d'un update 
                        //TODO ? non, il faudra sortir "reset si upadate fait"
                        var campaignNameTab = new Dictionary<string, string>();

                        string colorFM = "";
                        string colorSM = "";
                        if (folderLocExists)
                        {

                            //string path_oob_air = "";
                            if (Int32.Parse(NbMission) >= 1)
                                path_oob_air = subFolder + @"\Active\oob_air.lua";
                            else
                                path_oob_air = subFolder + @"\Init\oob_air_init.lua";

                            if (File.Exists(path_oob_air))
                                oobAirContent = File.ReadAllText(path_oob_air);


                            string type = "default";

                            if (!string.IsNullOrEmpty(oobAirContent))
                            {
                                string content = oobAirContent;

                                // Supprime les commentaires Lua
                                content = Regex.Replace(content, @"--\[\[.*?\]\]", "", RegexOptions.Singleline);
                                content = Regex.Replace(content, @"--.*?$", "", RegexOptions.Multiline);

                                // Trouve player = true ou ["player"] = true
                                Match playerMatch = Regex.Match(
                                    content,
                                    @"(?:\[\s*""player""\s*\]|player)\s*=\s*true",
                                    RegexOptions.IgnoreCase);

                                if (playerMatch.Success)
                                {
                                    int playerPos = playerMatch.Index;

                                    // Remonte jusqu'au { du bloc contenant ce player
                                    int level = 0;
                                    int blockStart = -1;

                                    for (int i = playerPos; i >= 0; i--)
                                    {
                                        if (content[i] == '}')
                                        {
                                            level++;
                                        }
                                        else if (content[i] == '{')
                                        {
                                            if (level == 0)
                                            {
                                                blockStart = i;
                                                break;
                                            }

                                            level--;
                                        }
                                    }

                                    if (blockStart >= 0)
                                    {
                                        // Redescend jusqu'à la } correspondante
                                        level = 1;
                                        int blockEnd = -1;

                                        for (int i = blockStart + 1; i < content.Length; i++)
                                        {
                                            if (content[i] == '{')
                                                level++;
                                            else if (content[i] == '}')
                                                level--;

                                            if (level == 0)
                                            {
                                                blockEnd = i;
                                                break;
                                            }
                                        }

                                        if (blockEnd > blockStart)
                                        {
                                            string block = content.Substring(blockStart, blockEnd - blockStart + 1);

                                            Match typeMatch = Regex.Match(
                                                block,
                                                @"(?:\[\s*""type""\s*\]|type)\s*=\s*""([^""]+)""",
                                                RegexOptions.IgnoreCase);

                                            if (typeMatch.Success)
                                                type = typeMatch.Groups[1].Value;
                                        }
                                    }
                                }
                            }


                            //check si plusieurs images par type d'avion existe dans le dossier image
                            string filePNGbyePlane = (textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + @"\Images\planescreen_" + type + ".png");
                            string filePNG = (textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".png");
                            string fileBMP = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".bmp";

                            // Copie l'image spécifique à l'avion vers l'image principale de la campagne.
                            // Pourquoi : File.Copy utilise l'API native Windows et consomme moins de CPU que CopyTo.
                            if (File.Exists(filePNGbyePlane))
                            {
                                //File.Copy(filePNGbyePlane, filePNG, true);
                                try
                                {
                                    File.Copy(filePNGbyePlane, filePNG, true);
                                }
                                catch (IOException)
                                {
                                    // ignore si en cours d'utilisation
                                }

                                if (File.Exists(fileBMP))
                                {
                                    File.Delete(fileBMP);
                                }
                            }


                            // Image (avec cache)
                            Image img = null;
                            string imagePath = filePNG;

                            if (campaignImageCache.ContainsKey(imagePath))
                            {
                                img = campaignImageCache[imagePath];
                            }
                            else if (File.Exists(imagePath))
                            {
                                try
                                {
                                    var fileInfo = new FileInfo(imagePath);
                                    if (fileInfo.Length < 100) // seuil sécurité
                                    {
                                        throw new Exception("Image corrompue ou vide : " + imagePath);
                                    }

                                    if (fileInfo.Length < 100)
                                    {
                                        throw new Exception("Image corrompue ou vide : " + imagePath);
                                    }

                                    for (int i = 0; i < 3; i++)
                                    {
                                        try
                                        {
                                            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                            using (var temp = Image.FromStream(fs))
                                            {
                                                img = new Bitmap(temp);
                                            }
                                            break;
                                        }
                                        catch (IOException)
                                        {
                                            System.Threading.Thread.Sleep(50);
                                        }
                                    }

                                    if (img == null)
                                    {
                                        throw new Exception("Impossible de charger l'image après plusieurs tentatives : " + imagePath);
                                    }

                                    campaignImageCache[imagePath] = img;
                                }
                                catch (Exception ex)
                                {
                                    throw new Exception("Erreur lors du chargement de l'image : " + imagePath, ex);
                                }
                            }

                            // Ajout dans le DataGridView
                            campaignUpdateList.Add(
                                new CampaignInfo()
                                {
                                    Name = NameCamp,
                                    CampaignId = campaignId,
                                    RepositoryUrl = repositoryUrl,
                                    LocalVersion = VerCamp,
                                    Folder = subFolder
                                });

                            // Le bouton Skip ne doit être visible que si au moins une mission a été jouée
                            int nbMissionParsed;
                            int.TryParse(NbMission, out nbMissionParsed);
                            bool skipVisible = nbMissionParsed > 0;

                            dataGridViewCampaigns.Rows.Add(
                                null,       // Clone (bouton)
                                img,        // Image
                                NameCamp,   // Name
                                null,       // Folder
                                VerCamp,    // Version
                                NbMission,  // Missions
                                type,       // Aircraft
                                null,       // First
                                skipVisible ? "⏭" : "",   // Skip (vide = invisible)
                                null,       // Config
                                null        // Delete
                            );

                            int rowIndex = dataGridViewCampaigns.Rows.Count - 1;

                            // Aucune mission jouée : le bouton reste vide (pas d'icône) et non cliquable
                            dataGridViewCampaigns.Rows[rowIndex].Cells["Skip"].ReadOnly = !skipVisible;

                            // Exemple : bouton Skip rouge si besoin (uniquement si visible)
                            if (skipVisible && colorSM == "red")
                            {
                                dataGridViewCampaigns.Rows[rowIndex].Cells["Skip"].Style.BackColor = Color.DarkRed;
                            }

                            // Exemple : bouton First rouge
                            if (colorFM == "red")
                            {
                                dataGridViewCampaigns.Rows[rowIndex].Cells["First"].Style.BackColor = Color.DarkRed;
                            }

                        }
                    }
                }

            }

             LoadCampaigns.Stop();
            FormUtils.LogRegister($"LoadCampaigns : {LoadCampaigns.ElapsedMilliseconds} ms");
        }


        public string SavedGamesPath
        {
            get { return textBox_SavedGames.Text; }
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
                        destinationDirectoryName = Path.Combine(textBox_SavedGames.Text, "Liveries");
                    }
                    else if (words[0].Contains("tech"))
                    {
                        destinationDirectoryName = Path.Combine(textBox_SavedGames.Text, "Mods");
                    }
                    else if (words[0].Contains("Missionscript_mod") || words[0].Contains("MOD") || Regex.IsMatch(LowerWordZero, "ovgme"))
                    {
                        destinationDirectoryName = textBox_OvGME.Text;
                        DestFullName = DestFullName.Replace(words[0] + "/", "");
                    }
                    else if (Regex.IsMatch(LowerWordZero, "savedgame"))
                    {
                        destinationDirectoryName = textBox_SavedGames.Text;
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
                        destinationDirectoryName = textBox_SavedGames.Text;
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

        public void ExtractZipFileToDirectoryLight(string sourceZipFilePath, bool overwrite)
        {
            //MessageBox.Show("ExtractZipFileToDirectory LIGHT");
            //FormUtils.LogRegister("ExtractZipFileToDirectoryLigh PASS A ");

            using (var archive = ZipFile.Open(sourceZipFilePath, ZipArchiveMode.Read))
            {
                string destinationRoot = Path.Combine(textBox_SavedGames.Text, @"Mods\tech\DCE\Missions\Campaigns");
                Directory.CreateDirectory(destinationRoot); // S'assurer que le dossier racine existe

                //FormUtils.LogRegister("ExtractZipFileToDirectoryLigh PASS B "+ destinationRoot);

                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    string DestFullName = file.FullName; // Chemin dans le fichier zip (y compris les sous-dossiers)
                    string[] words = DestFullName.Split('/');
                    string lowerWordZero = words[0].ToLowerInvariant();

                    bool extractAutorise = true;

                    //FormUtils.LogRegister("ExtractZipFileToDirectoryLigh PASS C extractAutorise " + extractAutorise);

                    // Filtrer les fichiers inutiles (PDF, EXE, TXT à la racine du zip)
                    if (words.Length <= 2 && (Path.GetExtension(words[0]) == ".pdf" || Path.GetExtension(words[0]) == ".exe" || Path.GetExtension(words[0]) == ".txt"))
                    {
                        continue;
                    }

                    foreach (string word in words)
                    {
                        if (Regex.IsMatch(word, "Active", RegexOptions.IgnoreCase) || Regex.IsMatch(word, "Debug", RegexOptions.IgnoreCase) || Regex.IsMatch(word, "Debriefing", RegexOptions.IgnoreCase))
                        {
                            extractAutorise = false;
                            break;
                        }
                    }


                    // Créer le répertoire cible si nécessaire
                    DirectoryInfo di = Directory.CreateDirectory(destinationRoot);
                    string destinationDirectoryFullPath = di.FullName;
                    string completeFileName = Path.GetFullPath(Path.Combine(destinationDirectoryFullPath, DestFullName));

                    //FormUtils.LogRegister("ExtractZipFileToDirectoryLigh PASS F completeFileName " + completeFileName);

                    // Vérification anti-Zip-Slip pour sécuriser le chemin de destination
                    if (!Path.GetFullPath(destinationDirectoryFullPath).StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Tentative d'extraction en dehors du répertoire de destination.");
                    }

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


        public static void UpdateProperty_OLD(object obj, string propertyName, string key, object value)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentNullException(nameof(propertyName));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));

            Type type = obj.GetType();
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

            // If the property is found directly, update it
            if (property != null)
            {
                if (!property.CanWrite)
                {
                    throw new InvalidOperationException($"Property '{propertyName}' on '{type.FullName}' is read-only");
                }

                if (typeof(IDictionary).IsAssignableFrom(property.PropertyType))
                {
                    var dictionary = property.GetValue(obj) as IDictionary;
                    if (dictionary != null)
                    {
                        var dictionaryType = property.PropertyType;
                        var valueType = dictionaryType.GetGenericArguments()[1];
                        if (value != null && !valueType.IsAssignableFrom(value.GetType()))
                        {
                            value = Convert.ChangeType(value, valueType);
                        }

                        if (dictionary.Contains(key))
                        {
                            dictionary[key] = value;
                        }
                        else
                        {
                            dictionary.Add(key, value);
                        }
                    }
                }
                else if (typeof(IList).IsAssignableFrom(property.PropertyType))
                {
                    var list = property.GetValue(obj) as IList;
                    if (list != null)
                    {
                        if (int.TryParse(key, out int index) && index >= 0 && index < list.Count)
                        {
                            var listType = property.PropertyType.GetGenericArguments()[0];
                            if (value != null && !listType.IsAssignableFrom(value.GetType()))
                            {
                                value = Convert.ChangeType(value, listType);
                            }
                            list[index] = value;
                        }
                        else
                        {
                            throw new ArgumentOutOfRangeException(nameof(key), "The index provided is out of range of the list.");
                        }
                    }
                }
                else
                {
                    if (value != null && !property.PropertyType.IsAssignableFrom(value.GetType()))
                    {
                        value = Convert.ChangeType(value, property.PropertyType);
                    }
                    property.SetValue(obj, value);
                }
            }
            else
            {
                // If the property is not found directly, check if any property is a dictionary
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var prop in properties)
                {
                    if (typeof(IDictionary).IsAssignableFrom(prop.PropertyType))
                    {
                        var dictionary = prop.GetValue(obj) as IDictionary;
                        if (dictionary != null && dictionary.Contains(propertyName))
                        {
                            dictionary[propertyName] = value;
                            return;
                        }
                    }
                }
                throw new ArgumentException($"Property '{propertyName}' not found on '{type.FullName}' and no dictionary containing this key was found.");
            }
        }
   


        public static System.Drawing.Icon Question { get; }

        private const int column_width = 150;
        private const int row_height = 50;


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


        public void CampaignPlusClickOneEvent(object sender, EventArgs e, string path, string OldNameCamp)
        {
            
            // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            //UpdateSharedData();

            //Test.Form3_Clonage CloneForm = new Test.Form3_Clonage(this, path, OldNameCamp);
            DCE_Manager.FormClonage CloneForm = new DCE_Manager.FormClonage(this, path, OldNameCamp);
            CloneForm.Show();


        }

        public void CampaignEdit1(object sender, EventArgs e, string path, string NameCamp)
        {
            var time_CampaignEdit1 = Stopwatch.StartNew();

            // 🔧 Nettoyage de l'ancienne instance
            if (_currentCampaignEdit != null)
            {
                _currentCampaignEdit.Dispose(); // 🔥 IMPORTANT
            }

            // 🔧 Nouvelle instance
            _currentCampaignEdit = new CampaignEdit(this, NameCamp);

            time_CampaignEdit1.Stop();
        }



        async void TabControl1_SelectedAsync(object sender, TabControlEventArgs e)
        {

            //tabPage2.Controls.Clear();

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage1       +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            if (e.TabPage == tabPageLeft_Install)
            {
                checkBoxMod();
                groupBoxDroiteAccueil.Visible = true;
                //groupBoxCampEdit.Visible = false;
                //groupBox_staticTemplate.Visible = false;
                //groupBoxCampEdit.Text = "";

                CampaignTab.Visible = false;
                

            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage3       +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeft_Update)
            {
                groupBoxDroiteAccueil.Visible = true;
                //groupBoxCampEdit.Visible = false;
                //groupBox_staticTemplate.Visible = false;

                FormUtils.MakeRoundedButton( ScriptsModUpdateButton, 10);

                FormUtils.MakeRoundedButton( DCEManagerUpdateButton, 10);

                FormUtils.MakeRoundedButton(  buttonCampaignCancel, 10);

                //groupBoxCampEdit.Text = "";

                CampaignTab.Visible = false;

                DCEManagerInstalledVersion.Text = VersionDceManager.Text;

                if (String.IsNullOrEmpty(textBox_SavedGames.Text))
                {
                    MessageBox.Show("You must enter the path to the SavedGame folder in the \"Install\" tab ", "Report");
                    return;
                }

                Directory.CreateDirectory(ParamManager.pathManager);
                ParamUpdater.CreateFolders();

                //CampaignUpdater.InitCampaignUpdateGrid(CampaignDataGridView);

                //await campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, textBox_SavedGames.Text);

            }
            else if (e.TabPage == tabPageLeft_About)
            {
                groupBoxDroiteAccueil.Visible = true;
                //groupBoxCampEdit.Visible = false;
                //groupBox_staticTemplate.Visible = false;
                //groupBoxCampEdit.Text = "";
                CampaignTab.Visible = false;

                if (textBox_ChangelogScriptsMod.Text == "")
                {

                    //Affiche le changelog
                    string ChangelogFileSM = textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua";
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
            }
            else if (e.TabPage == tabPageLeftNews)
            {
                groupBoxDroiteAccueil.Visible = true;
                //groupBoxCampEdit.Visible = false;
                //groupBox_staticTemplate.Visible = false;
                //groupBoxCampEdit.Text = "";
                CampaignTab.Visible = false;

            }
            else if (e.TabPage == tabPageLeft_Campaigns)
            {
                Cursor.Current = Cursors.WaitCursor;

                groupBoxDroiteAccueil.Visible = false;
                //groupBoxCampEdit.Visible = true;
                //groupBox_staticTemplate.Visible = false;
                //groupBoxCampEdit.Text = "";

                _ = LoadCampaignsAsync();
                
                Cursor.Current = Cursors.Default;

                UpdateCampaignButtonsVisibility();
            }
            else
            {
                // On quitte l’onglet CampaignTab → on cache tout
                buttonSaveChgtCampaign.Visible = false;
                buttonResetBackup.Visible = false;
                radioButton_OOB_INIT.Visible = false;
                radioButton_OOB_ACTIVE.Visible = false;
            }
        }




        int UpdateA = 1;
        public System.Windows.Forms.Label UpdateAddNewLabelA(string NameCamp)
        {
            System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

            txt.Top = UpdateA * 20 + 23;
            txt.Left = 25;
            txt.AutoSize = true;
            //txt.Size = new System.Drawing.Size(170, 20);
            txt.Text = NameCamp;
            UpdateA = UpdateA + 1;
            return txt;
        }
        int UpdateB = 1;




        int UpdateC = 1;
       

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
            process.StartInfo.FileName = textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG";
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
            UpdateCampaignButtonsVisibility();
        }
        private void UpdateCampaignButtonsVisibility()
        {
            //bool show =  groupBoxCampEdit.Text != "" && (CampaignTab.SelectedTab == tabPage14 || CampaignTab.SelectedTab == tabPage15);
            bool show =  (CampaignTab.SelectedTab == tabPage14 || CampaignTab.SelectedTab == tabPage15);

            buttonSaveChgtCampaign.Visible = show;
            buttonResetBackup.Visible = show;
            radioButton_OOB_INIT.Visible = show;
            radioButton_OOB_ACTIVE.Visible = show;
        }

        public void modifiedCampaign(string pathFile, string pathFileBackup, string folderName)
        {
            //sauvegarde la fichier oob_air_init pour éviter de l'écraser et le réutiliser si pb
            if (pathFileBackup != null && !File.Exists(pathFileBackup))
            {
                try
                {
                    File.Copy(pathFile, pathFileBackup, true);
                }
                catch (IOException iox)
                {
                    FormUtils.ShowErrorMessage(iox.Message);
                    //MessageBox.Show(iox.Message, "Info");
                }
            }
            //Form1.groupBoxCampEdit.Text = nameCamp;

            string[,,,] TEMPtableOobAirBBB = new string[3, 100, 100, 4];

            TEMPtableOobAirBBB = PublicTable.tableOobAiriNIT;

           
            //active l'unité du squad sélectionné
            foreach (var squad in List_oob_air_Manager.List_oob_air)
            {

                if (squad.Player)
                {
                    squad.Squad_Inactive = false;
                }
            }

            ////ecrit les Class de tous les squad dans le fichier oob_air
            //FormUtils.WriteListClassSquadsToFile(pathFile, folderName);

            if (!List_oob_air_Manager.List_oob_air.Any())
            {
                MessageBox.Show("No squads to save.");
                return;
            }
            // REF: FormMain - remplacer modifiedCampaign
            CampaignSaver.Save(pathFile, pathFileBackup, folderName);

            MessageBox.Show("Changes saved.", "Report");
        }


        public void buttonSaveChgtCampaign_Click(object sender, EventArgs e)
        {
            string pathFileBackup = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init_backup_DTT.lua";

            string pathFile = "";
            string FolderName = "";

            if (radioButton_OOB_INIT.Checked)
            {
                pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init.lua";
                FolderName = "Init";
            }
            else if (radioButton_OOB_ACTIVE.Checked)
            {
                pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Active\oob_air.lua";

                FolderName = "Active";
            }

            modifiedCampaign(pathFile, pathFileBackup, FolderName);

            // Recherche le squad actuellement marqué Player = true
            string newType = null;

            foreach (var squad in currentSquads)
            {
                if (squad.Player)
                {
                    newType = squad.Type;
                    break;
                }
            }

            // Si aucun squad player trouvé, on ne fait rien
            if (!string.IsNullOrEmpty(newType))
            {
                for (int i = 0; i < dataGridViewCampaigns.Rows.Count; i++)
                {
                    string rowName = dataGridViewCampaigns.Rows[i].Cells["Name"].Value?.ToString();

                    if (rowName == ParamCampaignSelected.NameCampaign)
                    {
                        // Met à jour la colonne Aircraft
                        dataGridViewCampaigns.Rows[i].Cells["Aircraft"].Value = newType;

                        // Met à jour aussi l'image si elle dépend du type
                        string imagePath = Path.Combine(
                            textBox_SavedGames.Text,
                            "Mods", "tech", "DCE", "Missions", "Campaigns",
                            ParamCampaignSelected.NameCampaign,
                            "Images",
                            "planescreen_" + newType + ".png");

                        if (File.Exists(imagePath))
                        {
                            using (var temp = Image.FromFile(imagePath))
                            {
                                dataGridViewCampaigns.Rows[i].Cells["Image"].Value = new Bitmap(temp);
                            }
                        }

                        break;
                    }
                }
            }

            PublicTable.errorTable.Clear();
            textBox_Bugs.Text = "";
            tabPage12.Text = "Bugs";

        }

        private void buttonSaveActive_Click(object sender, EventArgs e)
        {
            string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Active\oob_air.lua";
           
            modifiedCampaign(pathFile, null, "Active");

            PublicTable.errorTable.Clear();
            textBox_Bugs.Text = "";
            tabPage12.Text = "Bugs";

            //CampaignEdit1(sender, e, pathFile, groupBoxCampEdit.Text);
        }

        private void buttonResetBackup_Click(object sender, EventArgs e)
        {
            // Initializes the variables to pass to the MessageBox.Show method.
            string message = "Do you really want to go back to the original values?";
            string caption = "Caution";
            MessageBoxButtons buttons = MessageBoxButtons.YesNo;
            DialogResult result;

            // Displays the MessageBox.
            result = MessageBox.Show(message, caption, buttons);
            if (result == System.Windows.Forms.DialogResult.Yes)
            {
                string pathFileBackup = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init_backup_DTT.lua";
                string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_init.lua";
                //string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign + @"\Init\oob_air_initCLONE.lua";

                //sauvegarde la fichier oob_air_init pour éviter de l'écraser et le réutiliser si pb
                if (File.Exists(pathFileBackup))
                {
                    try
                    {
                        File.Copy(pathFileBackup, pathFile, true);
                    }
                    catch (IOException iox)
                    {
                        //MessageBox.Show(iox.Message, "Info");
                        FormUtils.ShowErrorMessage(iox.Message);
                    }
                }
            }

            // Remplacez l'appel statique CampaignEdit.LoadSquads(); par un appel sur l'instance _currentCampaignEdit
            if (_currentCampaignEdit != null)
            {
                _currentCampaignEdit.LoadSquads();
            }


            string path = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaignSelected.NameCampaign;

        }

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

        

        //private void butClient_Click(object sender, EventArgs e)
        //{
        //    //LabelStatut.Text = "User";
        //    this.Text = "DCE_Manager - User - " + ParamConf.CurrentConfigName;
        //    ParamManager.userLevel = 1;

        //    textBox_id_client.Visible = false;
        //    ScriptsModUpdateButton.Text = "Update";
        //}

        //private void but_Expert_Click(object sender, EventArgs e)
        //{

        //    textBox_id_client.Visible = false;
        //    //LabelStatut.Text = "Expert";
        //    this.Text = "DCE_Manager - Expert - " + ParamConf.CurrentConfigName;
        //    ParamManager.userLevel = 2;
        //}


        //private void butCampMaker_Click(object sender, EventArgs e)
        //{

        //    textBox_id_client.Visible = false;
        //    LabelStatut.Text = "CampaignMaker";
        //    this.Text = "DCE_Manager - CampaignMaker - " + ParamConf.CurrentConfigName;
        //    ScriptsModUpdateButton.Text = "Update";
        //    ParamManager.userLevel = 3;

        //}

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

        public void RefreshGrids()
        {
            CampaignEdit.LoadGridStatic(dataGridViewBlue, currentSquads, "blue", currentState);
            CampaignEdit.LoadGridStatic(dataGridViewRed, currentSquads, "red", currentState);

        }

        public void radioButton_OOB_INIT_CheckedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingState)
                return;

            if (!radioButton_OOB_INIT.Checked) return;

            currentState = "Init";
            RefreshGrids();
        }

        public void radioButton_OOB_ACTIVE_CheckedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingState)
                return;

            if (!radioButton_OOB_ACTIVE.Checked) return;

            currentState = "Active";
            RefreshGrids();
        }

        private async void CampaignDataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {


            if (e.RowIndex < 0)
                return;

            if (CampaignDataGridView.Columns[e.ColumnIndex].Name == "Repo")
            {
                CampaignInfo campaignRepo = campaignUpdater.GetCampaignFromRow(e.RowIndex);

                if (campaignRepo != null && !string.IsNullOrWhiteSpace(campaignRepo.RepositoryUrl))
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = campaignRepo.RepositoryUrl,
                        UseShellExecute = true
                    });
                }

                return;
            }

            if (CampaignDataGridView.Columns[e.ColumnIndex].Name != "Action")
                return;

            CampaignInfo campaign = campaignUpdater.GetCampaignFromRow(e.RowIndex);

            if (campaign == null || string.IsNullOrWhiteSpace(campaign.DownloadUrl))
                return;

            if (!campaign.UpdateAvailable)
                return;


            //CampaignDataGridView.Enabled = false;
            //groupBox_DwlCampaign.Visible = true;

            pictureBoxCampaignDownload.Visible = true;

            labelCampaignDownload.Visible = true;

            progressBarCampaignDownload.Visible = true;

            buttonCampaignCancel.Visible = true;

            try
            {

                buttonCampaignCancel.Enabled = true;
                buttonCampaignCancel.Visible = true;
                labelCampaignDownload.Visible = true;


                string zipFile = await campaignUpdater.DownloadCampaign(
                    campaign,
                    progressBarCampaignDownload,
                    labelCampaignDownload,
                    labelCampaignDld_Pct,
                    labelCampaignTitle);

                if (string.IsNullOrEmpty(zipFile))
                {
                    FormUtils.LogRegister("Campaign download cancelled.");

                    return;
                }

                FormUtils.LogRegister("Campaign downloaded : " + zipFile);

                campaignUpdater.ExtractCampaignZip(
                    zipFile,
                    textBox_SavedGames.Text,
                    campaign);

                //FormUtils.LogRegister("FormMain Campaign installed RefreshCampaignUpdates()");

                //await campaignUpdater.RefreshCampaignUpdates(
                //    CampaignDataGridView,
                //    textBox_SavedGames.Text);

                FormUtils.LogRegister("FormMain Campaign installed - rafraîchissement local (sans requête GitHub)");

                campaignUpdater.RefreshCampaignUpdatesLocalOnly(
                    CampaignDataGridView,
                    textBox_SavedGames.Text);


            }
            finally
            {
                CampaignDataGridView.Enabled = true;

                buttonCampaignCancel.Enabled = false;
                buttonCampaignCancel.Visible = false;

                progressBarCampaignDownload.Visible = false;
                labelCampaignDownload.Visible = false;
                labelCampaignDld_Pct.Visible = false;
                labelCampaignDownload.Visible = false;
                groupBox_DwlCampaign.Visible = true;

                pictureBoxCampaignDownload.Image = Properties.Resources.icons8_ok_24;

            }

            //string zipFile = await campaignUpdater.DownloadCampaign(campaign);


        }

        private void buttonCampaignCancel_Click(object sender, EventArgs e)
        {
            FormUtils.LogRegister("buttonCampaignCancel_Click");

            campaignUpdater.CancelDownload();


            labelCampaignTitle.Visible = false;
            pictureBoxCampaignDownload.Visible= false;
        }

        private void groupBox_UpdateCampaign_Enter(object sender, EventArgs e)
        {

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
                textBox_SavedGames.Text);
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

        private static bool CanDeleteFile(string path)
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


    }

}