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
using DCE_Manager.UserControls;



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
        private Updater_Catalog catalogUpdater;
        private ucHome homeView;

        private ucCampaign campaignView;

        public ucCampaign CampaignView
        {
            get { return campaignView; }
        }


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
            // Instance doit exister AVANT tout le reste du constructeur.
            // Pourquoi : ucCampaign, CampaignGridLeft et compagnie passent par
            //            Main_Form.Instance dès leur construction. Quand l'affectation
            //            arrivait en fin de constructeur, tous ces appels partaient sur null
            //            (ou étaient silencieusement ignorés à cause des "?.").
            Instance = this;

            // C'est ici qu'on passe "this" (qui représente le Form en cours)
            campaignUpdater = new Updater_Campaign(this);

            KeyPreview = true;

            InitializeComponent();

            //// Barre du haut : suit la largeur de la fenêtre, le combo Config reste collé à droite
            //panel_top.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            //label_Config.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            //comboBox_Config.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            //but_Configuration_Edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            //// La fenêtre démarre toujours à la taille de l'écran, quelle que soit l'échelle Windows
            //this.Load += (s, e) => { this.WindowState = FormWindowState.Maximized; };

            // La partie gauche prend la place qui reste entre elle et le panneau de droite
            this.Resize += (s, e) => AjusterLargeurGauche();
            this.Load += (s, e) => AjusterLargeurGauche();

            InitSplitter();

            homeView = new ucHome();
            homeView.Dock = DockStyle.Fill;
            panelRightView.Controls.Add(homeView);

            campaignView = new ucCampaign();
            campaignView.Dock = DockStyle.Fill;
            panelRightView.Controls.Add(campaignView);

            //linkLabel_Logo.LinkClicked += linkLabel_Logo_LinkClicked;

            // 2. Instancier en passant "this"
            CampaignGridLeft = new CampaignGridLeft(this);


            dropZoneControl1.FilesSelected += DropZone_FilesSelected;
            //InitializeDropZone();

            InitActionRows();

            Updater_Campaign.InitCampaignUpdateGrid(CampaignDataGridView);
            //this.Shown += Form1_Shown;

            scriptsModUpdater = new Updater_ScriptsMod(this);
            dceManagerUpdater = new Updater_DCEManager(this);
            catalogUpdater = new Updater_Catalog(this);

            //*************************
            CampaignGridLeft.GridCampaigns_Init_DataGridView();
            //*************************

            CampaignDataGridView.CellContentClick += CampaignGridLeft.CampaignDataGridView_CellContentClick;


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
            //CampaignTab.Selected += new TabControlEventHandler(CampaignTab_Selected);

            // Abonner l'événement FormClosed à une méthode
            this.FormClosed += new FormClosedEventHandler(Form1_FormClosed);

            // Appel de la méthode de chargement
            LoadConfiguration();

            // Écoute les demandes de debriefing venant de DCS (DCE_Manager.exe --debrief).
            // Après LoadConfiguration() : le listener a besoin des chemins pour travailler.
            DebriefCli.StartListener(this);

            homeView.SetClientId(Statistics.CreateIdClient());
            homeView.SetDceManagerVersion(GetVersionDceManager());
            homeView.SetScriptsModVersion(scriptsModUpdater.GetLocalScriptsModVersion());
            homeView.SetInstalledCampaignsCount(ParamConf.InstalledCampaignCount);

            _isInitializing = false;

            _ = scriptsModUpdater.CheckGithubScriptsModVersionAsync();

            _ = dceManagerUpdater.CheckGithubDCEManagerVersionAsync();

            _ = campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, ParamConf.PATH_SavedGames_DCS);
            _ = catalogUpdater.CheckCatalogAsync();

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


            ////affiche le changelog
            //textBox_changelog.Text = DCE_Manager.Properties.Resources.changelog;

            //coche ou pas le checkbox du scriptmod
            checkBoxMod();

            ////Affiche le changelog
            //string ChangelogFileSM = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua";
            //bool ChangelogFileSMExist = File.Exists(ChangelogFileSM);

            //if (ChangelogFileSMExist)
            //{
            //    using (StreamReader reader = new StreamReader(ChangelogFileSM))
            //    {
            //        string line;
            //        while ((line = reader.ReadLine()) != null)
            //        {
            //            if (((line.Length >= 2 && line.Substring(0, 2) != "--") | (line.Length <= 2)) &&
            //                !System.Text.RegularExpressions.Regex.IsMatch(line, "versionDCE") &&
            //                !System.Text.RegularExpressions.Regex.IsMatch(line, "VersionDCE")
            //                )
            //            {
            //                textBox_ChangelogScriptsMod.Text = textBox_ChangelogScriptsMod.Text + line + "\r\n";
            //            }
            //        }
            //        reader.Close();
            //    }
            //}

            //if (ChangelogFileSMExist)
            //{
            //    string line;

            //    StreamReader sr = new StreamReader(ChangelogFileSM);

            //    while ((line = sr.ReadLine()) != null)
            //    {
            //        if (!System.Text.RegularExpressions.Regex.IsMatch(line, "versionDCE"))
            //        {
            //            textBox_ChangelogScriptsMod.Text = textBox_ChangelogScriptsMod.Text + line + "\r\n";
            //        }
            //    }
            //    sr.Close();
            //}

            panelRightView.Controls.Add(campaignView);
        }



        // Évite l'écart (ou le chevauchement) entre la partie gauche et le panneau de droite
        private void AjusterLargeurGauche()
        {
            if (tabControl_LEFT == null || panelRightView == null) return;

            int w = panelRightView.Left - tabControl_LEFT.Left - 8;
            if (w > 300) tabControl_LEFT.Width = w;
        }

        // ===== Séparateur déplaçable entre la partie gauche et le panneau de droite =====
        private Panel _splitter;
        private bool _splitterDragging;

        private const int SplitterWidth = 8;   // = l'écart laissé par AjusterLargeurGauche()
        private const int MinPaneWidth = 300;  // largeur minimale de chaque côté

        private void InitSplitter()
        {
            _splitter = new Panel();
            _splitter.Cursor = Cursors.VSplit; // curseur "<->"
            _splitter.SetBounds(panelRightView.Left - SplitterWidth, panelRightView.Top, SplitterWidth, panelRightView.Height);

            Controls.Add(_splitter);
            // Même ancrage que panelRightView : il suit le bord droit quand on agrandit la fenêtre
            _splitter.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;

            _splitter.MouseEnter += (s, e) => { if (!_splitterDragging) _splitter.BackColor = SystemColors.ControlDark; };
            _splitter.MouseLeave += (s, e) => { if (!_splitterDragging) _splitter.BackColor = SystemColors.Control; };

            _splitter.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    _splitterDragging = true; // la souris reste capturée par le panel pendant le glisser
            };

            _splitter.MouseUp += (s, e) =>
            {
                _splitterDragging = false;
                _splitter.BackColor = SystemColors.Control;
            };

            _splitter.MouseMove += (s, e) =>
            {
                if (_splitterDragging)
                    MoveSplitterTo(PointToClient(Cursor.Position).X);
            };
        }

        // mouseX = position de la souris dans la Form. Le bord droit de la partie droite ne bouge pas.
        private void MoveSplitterTo(int mouseX)
        {
            int right = panelRightView.Right;
            int newLeft = mouseX + SplitterWidth / 2; // le séparateur se centre sous la souris

            int minLeft = tabControl_LEFT.Left + MinPaneWidth + SplitterWidth;
            int maxLeft = right - MinPaneWidth;
            newLeft = Math.Max(minLeft, Math.Min(maxLeft, newLeft));

            if (newLeft == panelRightView.Left)
                return;

            SuspendLayout();

            panelRightView.SetBounds(newLeft, panelRightView.Top, right - newLeft, panelRightView.Height);
            panel_Down.SetBounds(newLeft, panel_Down.Top, panel_Down.Right - newLeft, panel_Down.Height); // le bouton Exit reste aligné
            _splitter.Left = newLeft - SplitterWidth;
            AjusterLargeurGauche();

            ResumeLayout();
        }

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
                    ParamConf.PATH_DCS_Root = ParamConf.configDictionary.TryGetValue(prefix + "pathDCS", out var pDcs) ? pDcs : "";
                    textBox_PATH_DCS_Root.Text = ParamConf.PATH_DCS_Root;
                    //RefreshMissionScriptingCheck();

                    ParamConf.PATH_SavedGames_DCS = ParamConf.configDictionary.TryGetValue(prefix + "pathSavedGames", out var pSaved) ? pSaved : "";
                    textBox_SavedGames.Text = ParamConf.PATH_SavedGames_DCS;

                    ParamConf.PATH_OVGME_MOD = ParamConf.configDictionary.TryGetValue(prefix + "pathOVGME", out var pOvgme) ? pOvgme : "";
                    textBox_OvGME.Text = ParamConf.PATH_OVGME_MOD;
                    ParamConf.InstalledCampaignCount = (ParamConf.configDictionary.TryGetValue(prefix + "installedCampCount", out var pCampCount) && int.TryParse(pCampCount, out int campCountParsed))
                            ? campCountParsed
                            : 0;
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

            checkBoxMod();

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

        // Met à jour la case MissionScripting de la page d'accueil (ucHome)
        public void checkBoxMod()
        {
            homeView.RefreshMissionScriptingCheck();
        }

        ////check sanitizeModule ?
        //public void checkBoxMod()
        //{
        //    string pathFile = textBox_PATH_DCS_Root.Text + @"\Scripts\MissionScripting.lua";

        //    Boolean find_OS = false;
        //    Boolean find_IO = false;

        //    if (File.Exists(pathFile))
        //    {

        //        //checkBoxSanitize.Enabled = true;
        //        using (StreamReader reader = new StreamReader(pathFile))
        //        {
        //            string line;

        //            while ((line = reader.ReadLine()) != null)
        //            {
        //                int nbcaractereOS = line.IndexOf("sanitizeModule('os");
        //                if (nbcaractereOS > -1)
        //                {

        //                    //MessageBox.Show("passe _o_",  nbcaractere.ToString());
        //                    int nbCaractTiret = line.IndexOf("--");
        //                    if (nbCaractTiret > -1 && nbCaractTiret < nbcaractereOS)
        //                    {
        //                        find_OS = true;
        //                    }
        //                }
        //                int nbcaractereIO = line.IndexOf("sanitizeModule('io");
        //                if (nbcaractereIO > -1)
        //                {
        //                    int nbCaractTiret = line.IndexOf("--");
        //                    if (nbCaractTiret > -1 && nbCaractTiret < nbcaractereIO)
        //                    {
        //                        find_IO = true;
        //                    }
        //                }
        //            }
        //        }

        //        if (find_OS && find_IO)
        //        {
        //            //checkBoxSanitize.Checked = true;
        //        }
        //        else
        //        {
        //            //checkBoxSanitize.Checked = false;
        //        }
        //    }
        //    else
        //    {
        //        //checkBoxSanitize.Enabled = false;
        //    }
        //}

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

        // Installe le contenu d'un zip de campagne (paquet de distribution) dans Saved Games.
        // Renvoie un compte-rendu (ZipExtractReport, défini dans Main_Form.Install.cs) : fichiers installés,
        // ignorés (avec la raison) et en erreur. Rien n'est ignoré en silence : tout est écrit dans log.txt.
        public ZipExtractReport ExtractZipFileToDirectory(string sourceZipFilePath, bool overwrite)
        {
            ZipExtractReport report = new ZipExtractReport();
            HashSet<string> rootsLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var archive = ZipFile.Open(sourceZipFilePath, ZipArchiveMode.Read))
            {
                // 0. Contrôle de structure, UNE seule fois et AVANT d'écrire quoi que ce soit :
                // on refuse un zip dont le premier dossier porte le nom du zip (dossier "enveloppe").
                // Pourquoi : l'ancien contrôle cherchait le nom du dossier dans TOUT le chemin du zip
                // (un zip rangé dans un dossier "Mods" ou "tech" était refusé) et s'arrêtait à mi-chemin.
                string zipName = Path.GetFileNameWithoutExtension(sourceZipFilePath).Replace("_", " ");

                foreach (ZipArchiveEntry check in archive.Entries)
                {
                    string checkName = check.FullName.Replace('\\', '/');

                    if (!checkName.Contains("/"))
                        continue; // fichier posé à la racine : pas un dossier

                    string checkRoot = checkName.Split('/')[0].Replace("_", " ");

                    if (checkRoot != "" && checkRoot.Equals(zipName, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "Incompatible ZIP file structure.\r\n" +
                            "The first folder in the ZIP ('" + checkRoot + "') has the same name as the ZIP file.\r\n" +
                            "Please check the content of the ZIP.",
                            "Error ExtractZipFileToDirectory");

                        report.Refused = true;
                        return report;
                    }
                }

                // ScriptsMod.NG déjà installé ? On le teste UNE fois, avant d'extraire. Testé à chaque entrée,
                // il devenait "déjà installé" dès que son premier fichier (UTIL_Changelog.lua) était écrit,
                // et tout le reste de ScriptsMod.NG, dans l'ordre du zip, n'était plus installé.
                string scriptsModPath = Path.Combine(ParamConf.PATH_SavedGames_DCS, "Mods", "tech", "DCE", "ScriptsMod.NG");
                bool scriptsModAlreadyInstalled = Directory.Exists(scriptsModPath) &&
                                                  File.Exists(Path.Combine(scriptsModPath, "UTIL_Changelog.lua"));

                // Dossier OvGME (gestionnaire de mods) de la configuration : optionnel, donc peut être vide ou introuvable.
                bool ovGmeFolderAvailable = !string.IsNullOrWhiteSpace(ParamConf.PATH_OVGME_MOD) &&
                                            Directory.Exists(ParamConf.PATH_OVGME_MOD);
                const string ovGmeSkipReason = "mission script mod (OvGME folder not set)";

                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    // Certains outils écrivent des "\" dans les zips : on travaille toujours avec des "/"
                    string DestFullName = file.FullName.Replace('\\', '/');
                    bool isFolderEntry = DestFullName.EndsWith("/");
                    string[] words = DestFullName.Split('/');
                    string root = words[0];
                    string LowerWordZero = root.ToLowerInvariant();

                    if (root == "")
                    {
                        report.AddSkipped("invalid path");
                        continue;
                    }

                    // Un fichier posé directement à la racine du zip (readme, pdf, exe...) n'a aucune destination logique.
                    // Avant, il partait dans Saved Games\Liveries.
                    if (words.Length == 1)
                    {
                        report.AddSkipped("file at the root of the ZIP");
                        continue;
                    }

                    string destinationDirectoryName = "";
                    string skipReason = ""; // si rempli : on n'extrait pas ce fichier

                    // Déterminer le répertoire cible en fonction du dossier racine du zip
                    if (root.Equals("Liveries", StringComparison.OrdinalIgnoreCase))
                    {
                        // Le dossier racine EST déjà "Liveries" : sans ce cas, la règle "nom d'avion" juste en dessous
                        // le rangeait dans Saved Games\Liveries\Liveries\...
                        destinationDirectoryName = ParamConf.PATH_SavedGames_DCS;
                    }
                    else if (!LowerWordZero.Contains("mod") && !root.Contains("tech") &&
                             !LowerWordZero.Contains("savedgame") && !LowerWordZero.Contains("ovgme"))
                    {
                        // Dossier racine = nom d'un avion : Saved Games\Liveries\<avion>\...
                        destinationDirectoryName = Path.Combine(ParamConf.PATH_SavedGames_DCS, "Liveries");
                    }
                    else if (root.Contains("tech"))
                    {
                        destinationDirectoryName = Path.Combine(ParamConf.PATH_SavedGames_DCS, "Mods");
                    }
                    else if (root.Contains("Missionscript_mod") || root.Contains("MOD") || LowerWordZero.Contains("ovgme"))
                    {
                        destinationDirectoryName = ParamConf.PATH_SavedGames_DCS;
                        DestFullName = DestFullName.Substring(root.Length + 1); // on enlève le dossier racine

                        // Mod MissionScripting du créateur de campagne (dossier OvGME_Path / Missionscript_mod) :
                        // il passe par un gestionnaire de mods. Le dossier OvGME n'est plus obligatoire dans la
                        // configuration : s'il n'existe pas, on n'installe pas ces fichiers, et on le dit à la fin.
                        if ((LowerWordZero.Contains("ovgme") || root.Contains("Missionscript_mod")) && !ovGmeFolderAvailable)
                        {
                            if (!isFolderEntry)
                                report.AddSkipped(ovGmeSkipReason);

                            continue;
                        }
                    }
                    else if (LowerWordZero.Contains("savedgame"))
                    {
                        destinationDirectoryName = ParamConf.PATH_SavedGames_DCS;
                        DestFullName = DestFullName.Substring(root.Length + 1); // on enlève le dossier racine

                        // ScriptsMod.NG : on ne l'écrase pas s'il était déjà installé avant cette installation
                        if (scriptsModAlreadyInstalled && words.Contains("ScriptsMod.NG"))
                            skipReason = "ScriptsMod.NG already installed";
                    }
                    else if (root.Contains("Mods"))
                    {
                        destinationDirectoryName = ParamConf.PATH_SavedGames_DCS;
                    }

                    // On note une fois, dans le log, où va chaque dossier racine du zip
                    if (rootsLogged.Add(root))
                    {
                        FormUtils.LogRegister("Install | folder '" + root + "' in the ZIP -> " +
                            (destinationDirectoryName == "" ? "(not recognized, skipped)" : destinationDirectoryName));
                    }

                    // Dossier racine inconnu (ex: "Modules") : avant, le dossier cible restait vide et
                    // Directory.CreateDirectory("") faisait planter toute l'installation en plein milieu.
                    if (destinationDirectoryName == "")
                    {
                        report.AddSkipped("unknown folder '" + root + "'");
                        report.AddWarning("The folder '" + root + "' in the ZIP is not known. Its files were not installed.");
                        continue;
                    }

                    // Créer le répertoire cible si nécessaire
                    DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
                    string destinationDirectoryFullPath = di.FullName;
                    string completeFileName;

                    try
                    {
                        completeFileName = Path.GetFullPath(Path.Combine(destinationDirectoryFullPath, DestFullName));
                    }
                    catch (Exception ex)
                    {
                        report.Failed++;
                        FormUtils.LogRegister("Install | invalid path in the ZIP : " + file.FullName + " : " + ex.Message);
                        continue;
                    }

                    // Vérification pour éviter les vulnérabilités Zip-Slip (entrée qui sort du dossier cible)
                    if (!IsInsideFolder(completeFileName, destinationDirectoryFullPath))
                    {
                        report.AddSkipped("unsafe path (outside the destination folder)");
                        report.AddWarning("Some files in the ZIP had an unsafe path. They were not installed.");
                        FormUtils.LogRegister("Install | unsafe path ignored : " + file.FullName);
                        continue;
                    }

                    // Si l'entrée du zip est un dossier, le créer sans extraire de fichiers
                    // (même Active / Debug / Debriefing : les dossiers vides doivent exister)
                    if (isFolderEntry)
                    {
                        try
                        {
                            Directory.CreateDirectory(completeFileName);
                        }
                        catch (Exception ex)
                        {
                            report.Failed++;
                            FormUtils.LogRegister("Install | cannot create folder " + completeFileName + " : " + ex.Message);
                        }
                        continue;
                    }

                    // Parties en cours de l'auteur (Campaigns\<campagne>\Active, Debug, Debriefing) : jamais installées
                    if (skipReason == "" && IsInCampaignSessionFolder(words))
                        skipReason = "campaign session folders (Active / Debug / Debriefing)";

                    if (skipReason != "")
                    {
                        report.AddSkipped(skipReason);
                        continue;
                    }

                    // Extraction. Le dossier parent est créé ici aussi : un zip sans entrées de dossier
                    // (certains outils n'en écrivent pas) faisait échouer l'extraction de ses fichiers.
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(completeFileName));
                        file.ExtractToFile(completeFileName, overwrite);
                        report.Extracted++;
                    }
                    catch (Exception ex)
                    {
                        report.Failed++;
                        FormUtils.LogRegister("Install | cannot extract " + file.FullName + " : " + ex.Message);
                    }
                }
            }

            // Fichiers du mod MissionScripting non installés : on prévient l'utilisateur (popup de fin d'installation)
            int ovGmeSkippedCount;
            if (report.Skipped.TryGetValue("mission script mod (OvGME folder not set)", out ovGmeSkippedCount))
            {
                report.AddWarning(
                    ovGmeSkippedCount + " file(s) of the MissionScripting mod (OvGME folder of the ZIP) were NOT installed, " +
                    "because no OvGME / mod manager folder is set in the configuration.\r\n" +
                    "The campaign maker wanted them installed. Without this mod, the campaign may not work " +
                    "(set the OvGME folder in the configuration and install the campaign again).");
            }

            FormUtils.LogRegister("Install | " + Path.GetFileName(sourceZipFilePath) + " : " + report.Extracted +
                " installed, " + report.SkippedTotal + " skipped, " + report.Failed + " failed.");

            foreach (KeyValuePair<string, int> skipped in report.Skipped)
            {
                FormUtils.LogRegister("Install |    skipped " + skipped.Value + " : " + skipped.Key);
            }

            return report;
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

                Main_Form.Instance.CampaignView.buttonSaveChgtCampaign.Visible = false;
                Main_Form.Instance.CampaignView.buttonResetBackup.Visible = false;
                Main_Form.Instance.CampaignView.radioButton_INIT_CAMP.Visible = false;
                Main_Form.Instance.CampaignView.radioButton_ACTIVE_CAMP.Visible = false;
            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage1 Install      +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            if (e.TabPage == tabPageLeft_Install)
            {
                checkBoxMod();
                //groupBoxDroiteAccueil.Visible = true;


                CampaignGridLeft.UpdateCampaignButtonsVisibility();


            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage2   CAMPAIGNS GRID    +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeft_Campaigns)
            {
                Cursor.Current = Cursors.WaitCursor;

                //groupBoxDroiteAccueil.Visible = false;
                 await CampaignGridLeft.LoadCampaignsAsync();

                Cursor.Current = Cursors.Default;

                CampaignGridLeft.UpdateCampaignButtonsVisibility();
            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage3   UPDATE    +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeft_Update)
            {
                //groupBoxDroiteAccueil.Visible = true;
                FormUtils.MakeRoundedButton(ScriptsModUpdateButton, 10);

                FormUtils.MakeRoundedButton(DCEManagerUpdateButton, 10);

                FormUtils.MakeRoundedButton(buttonCampaignCancel, 10);


                //CampaignTab.Visible = false;
                ShowHome();

                DCEManagerInstalledVersion.Text = GetVersionDceManager();


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
                ShowHome();

                tabPageLeft_About.Controls.Clear(); // enlève textBox_changelog etc. existants

                ucAbout aboutView = new ucAbout();
                aboutView.Dock = DockStyle.Fill;

                panel_About_Credit_Icon.Dock = DockStyle.Bottom; // garde sa Height (43) du Designer

                tabPageLeft_About.Controls.Add(aboutView);
                aboutView.Controls.Add(panel_About_Credit_Icon); // ajouté APRÈS -> se cale en bas sans rien casser

                CampaignGridLeft.UpdateCampaignButtonsVisibility();
            }


            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage?   NEWS    +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeftCatalog)
            {
                catalogUpdater.DisplayCatalog();

                CampaignGridLeft.UpdateCampaignButtonsVisibility();
            }

        }



        public string GetVersionDceManager()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;

            // On ne garde que les 3 premiers composants (Major, Minor, Build)
            string versionString = String.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);

            ParamConf.DCE_Manager_LocVer = versionString;
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



        // Les deux handlers ne font rien tant que la campagne n'est pas prête.
        // Pourquoi : le radio Init est désormais coché par défaut dans le Designer,
        //            donc ces handlers peuvent être atteints très tôt.
        public void radioButton_OOB_INIT_CheckedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingState)
                return;

            if (!campaignView.IsOobInit)
                return;

            currentState = "Init";
            CampaignGridLeft?.RefreshGrids();
        }

        public void radioButton_OOB_ACTIVE_CheckedChanged(object sender, EventArgs e)
        {
            if (_isUpdatingState)
                return;

            if (!campaignView.IsOobActive)
                return;

            currentState = "Active";
            CampaignGridLeft?.RefreshGrids();
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


        public void ShowHome()
        {
            homeView.BringToFront();
        }

        // Met à jour le label d'accueil + garde la valeur en mémoire/cache (options.txt) pour
        // pouvoir l'afficher au prochain lancement sans recalcul. Appelé depuis
        // CampaignGridLeft.LoadCampaignsAsync() à chaque (re)chargement de la grid Campaigns.
        public void UpdateInstalledCampaignsLabel(int count)
        {
            ParamConf.InstalledCampaignCount = count;
            homeView.SetInstalledCampaignsCount(count);
            Configuration_Form.Save_Config();
        }

        public void ShowCampaign()
        {
            campaignView.BringToFront();
        }

        private void but_Level_CampMaker_Click(object sender, EventArgs e)
        {
            ParamConf.UserLevel = UserLevel.CampaignMaker;
            CampaignGridLeft.UpdateCampaignSetupColumnVisibility();

            but_Level_CampMaker.BackColor = System.Drawing.Color.DodgerBlue;
            but_Level_CampMaker.ForeColor = System.Drawing.Color.White; // Pour garder le texte lisible

            // Retour à la couleur par défaut
            but_level_User.BackColor = System.Drawing.SystemColors.Control;
            but_level_User.ForeColor = System.Drawing.Color.Black; // Pour garder le texte lisible
        }

        private void but_level_User_Click(object sender, EventArgs e)
        {
            ParamConf.UserLevel = DCE_Manager.UserLevel.Player;
            CampaignGridLeft.UpdateCampaignSetupColumnVisibility();

            but_level_User.BackColor = System.Drawing.Color.DodgerBlue;
            but_level_User.ForeColor = System.Drawing.Color.White; // Pour garder le texte lisible

            // Retour à la couleur par défaut
            but_Level_CampMaker.BackColor = System.Drawing.SystemColors.Control;
            but_Level_CampMaker.ForeColor = System.Drawing.Color.Black; // Pour garder le texte lisible
        }

        private void linkLabel_Logo_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ShowVoronoyCreditWindow();
        }

        // Petite fenêtre : lien Instagram cliquable + QR code (image chargée depuis
        // Data\About\Icons\qr_voronoy_instagram.png). Tant que ce fichier n'existe
        // pas encore, la zone QR reste simplement vide (pas de placeholder moche) -
        // dépose le vrai PNG à cet endroit quand tu l'auras, aucun code à retoucher.
        private void ShowVoronoyCreditWindow()
        {


            //const string instagramUrl = "https://www.instagram.com/PAULINE_USERNAME/";
            const string instagramUrl = "https://www.instagram.com/__voronoy_?igsh=MWFmYW44dXlzcW9rNA==";

            var creditForm = new Form();
            creditForm.Text = "Logo designed by Voronoy";
            creditForm.ClientSize = new Size(300, 360);
            creditForm.StartPosition = FormStartPosition.CenterParent;
            creditForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            creditForm.MaximizeBox = false;
            creditForm.MinimizeBox = false;

            var linkInsta = new LinkLabel();
            linkInsta.Text = "View Voronoy’s Instagram profile";
            linkInsta.AutoSize = true;
            linkInsta.Left = 20;
            linkInsta.Top = 20;
            linkInsta.LinkClicked += (s, ev) => { System.Diagnostics.Process.Start(instagramUrl); };

            var pictureBoxQr = new PictureBox();
            pictureBoxQr.Left = 20;
            pictureBoxQr.Top = 55;
            pictureBoxQr.Width = 260;
            pictureBoxQr.Height = 260;
            pictureBoxQr.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBoxQr.Cursor = Cursors.Hand;
            pictureBoxQr.Click += (s, ev) => { System.Diagnostics.Process.Start(instagramUrl); };

            string qrPath = Path.Combine(Application.StartupPath, "Data", "About", "Icons", "qr_Voronoy_instagram.png");
            if (File.Exists(qrPath))
            {
                try { pictureBoxQr.Image = Image.FromFile(qrPath); }
                catch { /* fichier illisible : zone laissée vide */ }
            }

            creditForm.Controls.Add(pictureBoxQr);
            creditForm.Controls.Add(linkInsta);

            creditForm.ShowDialog(this);
        }

        private void panel_configuration_Paint(object sender, PaintEventArgs e)
        {

        }

        // ============================================================================
        // A coller dans Main_Form.cs (dans la classe Main_Form)
        // Version "inline" du crédit Voronoy, affichée DANS panelRightView (comme
        // homeView / campaignView), sans le lien portfolio, juste nom + Instagram + QR.
        // Ne touche pas à ShowVoronoyCreditWindow() (la popup) qui reste utilisable.
        //
        // Pour tester : appelle ShowVoronoyCreditInline() depuis n'importe où
        // (ex: temporairement dans le constructeur après panelRightView.Controls.Add(campaignView),
        // ou sur un bouton de test).
        // ============================================================================

        private Panel panelVoronoyCreditInline;

        public void ShowVoronoyCreditInline()
        {
            if (panelVoronoyCreditInline == null)
            {
                panelVoronoyCreditInline = BuildVoronoyCreditInlinePanel();
                panelVoronoyCreditInline.Dock = DockStyle.Fill;
                panelRightView.Controls.Add(panelVoronoyCreditInline);
            }

            panelVoronoyCreditInline.BringToFront();
        }

        //private Panel BuildVoronoyCreditInlinePanel()
        //{
        //    const string instagramUrl = "https://www.instagram.com/__voronoy_?igsh=MWFmYW44dXlzcW9rNA==";
        //    string iconsFolder = Path.Combine(Application.StartupPath, "Data", "About", "Icons");

        //    // --- root : occupe tout panelRightView, sert juste à centrer la carte ---
        //    var root = new Panel();
        //    root.BackColor = SystemColors.Control;

        //    // --- la carte elle-même (taille fixe, centrée dans root) ---
        //    var card = new Panel();
        //    card.Width = 640;
        //    card.Height = 260;
        //    card.BackColor = Color.White;
        //    card.BorderStyle = BorderStyle.FixedSingle;

        //    root.Resize += (s, e) =>
        //    {
        //        card.Left = (root.ClientSize.Width - card.Width) / 2;
        //        card.Top = (root.ClientSize.Height - card.Height) / 2;
        //    };

        //    // --- colonne de gauche : logo + nom + role ---
        //    var leftPanel = new Panel();
        //    leftPanel.Dock = DockStyle.Left;
        //    leftPanel.Width = 160;

        //    var logoBox = new PictureBox();
        //    logoBox.Width = 70;
        //    logoBox.Height = 70;
        //    logoBox.Left = (leftPanel.Width - logoBox.Width) / 2;
        //    logoBox.Top = 40;
        //    logoBox.SizeMode = PictureBoxSizeMode.Zoom;

        //    string logoPath = Path.Combine(iconsFolder, "voronoy_logo.png");
        //    if (File.Exists(logoPath))
        //    {
        //        try { logoBox.Image = Image.FromFile(logoPath); }
        //        catch { /* fichier illisible : zone laissée vide */ }
        //    }

        //    var nameLabel = new Label();
        //    nameLabel.Text = "Voronoy";
        //    nameLabel.Font = new Font(leftPanel.Font.FontFamily, 11f, FontStyle.Bold);
        //    nameLabel.ForeColor = Color.MediumPurple;
        //    nameLabel.TextAlign = ContentAlignment.MiddleCenter;
        //    nameLabel.Left = 0;
        //    nameLabel.Top = logoBox.Bottom + 10;
        //    nameLabel.Width = leftPanel.Width;
        //    nameLabel.Height = 20;

        //    var roleLabel = new Label();
        //    roleLabel.Text = "Logo Designer";
        //    roleLabel.Font = new Font(leftPanel.Font, FontStyle.Italic);
        //    roleLabel.ForeColor = Color.DimGray;
        //    roleLabel.TextAlign = ContentAlignment.MiddleCenter;
        //    roleLabel.Left = 0;
        //    roleLabel.Top = nameLabel.Bottom + 2;
        //    roleLabel.Width = leftPanel.Width;
        //    roleLabel.Height = 18;

        //    leftPanel.Controls.Add(logoBox);
        //    leftPanel.Controls.Add(nameLabel);
        //    leftPanel.Controls.Add(roleLabel);

        //    // --- colonne de droite : QR code ---
        //    var rightPanel = new Panel();
        //    rightPanel.Dock = DockStyle.Right;
        //    rightPanel.Width = 190;

        //    var qrBox = new PictureBox();
        //    qrBox.Width = 150;
        //    qrBox.Height = 150;
        //    qrBox.Left = (rightPanel.Width - qrBox.Width) / 2;
        //    qrBox.Top = (card.Height - qrBox.Height) / 2;
        //    qrBox.SizeMode = PictureBoxSizeMode.Zoom;
        //    qrBox.Cursor = Cursors.Hand;
        //    qrBox.Click += (s, e) => { System.Diagnostics.Process.Start(instagramUrl); };

        //    string qrPath = Path.Combine(iconsFolder, "qr_Voronoy_instagram.png");
        //    if (File.Exists(qrPath))
        //    {
        //        try { qrBox.Image = Image.FromFile(qrPath); }
        //        catch { /* fichier illisible : zone laissée vide */ }
        //    }

        //    rightPanel.Controls.Add(qrBox);

        //    // --- colonne du milieu : texte + lien Instagram (pas de lien portfolio) ---
        //    var middlePanel = new Panel();
        //    middlePanel.Dock = DockStyle.Fill;

        //    var descriptionLabel = new Label();
        //    descriptionLabel.Text = "The DCE Manager logo was designed by Voronoy.";
        //    descriptionLabel.Font = new Font(middlePanel.Font.FontFamily, 10f);
        //    descriptionLabel.Left = 20;
        //    descriptionLabel.Top = 60;
        //    descriptionLabel.Width = 260;
        //    descriptionLabel.Height = 40;

        //    var thanksLabel = new Label();
        //    thanksLabel.Text = "Thanks!";
        //    thanksLabel.Font = new Font(middlePanel.Font.FontFamily, 10f, FontStyle.Bold);
        //    thanksLabel.Left = 20;
        //    thanksLabel.Top = descriptionLabel.Bottom + 6;
        //    thanksLabel.AutoSize = true;

        //    var linkInsta = new LinkLabel();
        //    linkInsta.Text = "instagram.com/__voronoy_";
        //    linkInsta.AutoSize = true;
        //    linkInsta.Left = 20;
        //    linkInsta.Top = thanksLabel.Bottom + 12;
        //    linkInsta.LinkClicked += (s, e) => { System.Diagnostics.Process.Start(instagramUrl); };

        //    middlePanel.Controls.Add(descriptionLabel);
        //    middlePanel.Controls.Add(thanksLabel);
        //    middlePanel.Controls.Add(linkInsta);

        //    // IMPORTANT : Fill (middlePanel) ajouté AVANT Left/Right (leftPanel/rightPanel)
        //    card.Controls.Add(middlePanel);
        //    card.Controls.Add(leftPanel);
        //    card.Controls.Add(rightPanel);

        //    root.Controls.Add(card);

        //    return root;
        //}

        // ============================================================================
        // A coller dans Main_Form.cs (dans la classe Main_Form)
        // Version "inline" du crédit Voronoy, taillée pile pour un panel de 496x593
        // (celui prévu à cet effet), inspirée de la maquette sombre/dorée envoyée.
        // Toujours sans le lien portfolio (juste Instagram), comme demandé au départ -
        // dis-moi si tu changes d'avis, c'est une ligne à ajouter.
        //
        // Remplace juste BuildVoronoyCreditInlinePanel() par cette version ;
        // ShowVoronoyCreditInline() ne change pas.
        // ============================================================================

        private Panel BuildVoronoyCreditInlinePanel()
        {
            const string instagramUrl = "https://www.instagram.com/__voronoy_?igsh=MWFmYW44dXlzcW9rNA==";
            string iconsFolder = Path.Combine(Application.StartupPath, "Data", "About", "Icons");

            Color colorBackground = Color.FromArgb(38, 40, 32);   // fond sombre kaki
            Color colorGold = Color.FromArgb(200, 165, 95);        // accents dorés
            Color colorText = Color.Gainsboro;                     // texte clair secondaire

            // --- carte : taille fixe 496x593, pensée pour remplir le panel prévu ---
            var card = new Panel();
            card.Width = 496;
            card.Height = 593;
            card.BackColor = colorBackground;
            card.BorderStyle = BorderStyle.FixedSingle;

            // --- titre ---
            var titleLabel = new Label();
            titleLabel.Text = "LOGO DESIGNER";
            titleLabel.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            titleLabel.ForeColor = colorGold;
            titleLabel.TextAlign = ContentAlignment.MiddleCenter;
            titleLabel.Left = 0;
            titleLabel.Top = 16;
            titleLabel.Width = card.Width;
            titleLabel.Height = 28;

            // --- badge / logo (110x110, centré) ---
            var logoBox = new PictureBox();
            logoBox.Width = 110;
            logoBox.Height = 110;
            logoBox.Left = (card.Width - logoBox.Width) / 2;
            logoBox.Top = titleLabel.Bottom + 12;
            logoBox.SizeMode = PictureBoxSizeMode.Zoom;

            string logoPath = Path.Combine(iconsFolder, "voronoy_logo.png");
            if (File.Exists(logoPath))
            {
                try { logoBox.Image = Image.FromFile(logoPath); }
                catch { logoBox.Image = DrawFallbackBadge(colorGold, colorBackground, logoBox.Width); }
            }
            else
            {
                logoBox.Image = DrawFallbackBadge(colorGold, colorBackground, logoBox.Width);
            }

            // --- "Designed by" / "VORONOY" ---
            var designedByLabel = new Label();
            designedByLabel.Text = "Designed by";
            designedByLabel.Font = new Font("Segoe UI", 9f);
            designedByLabel.ForeColor = colorText;
            designedByLabel.TextAlign = ContentAlignment.MiddleCenter;
            designedByLabel.Left = 0;
            designedByLabel.Top = logoBox.Bottom + 10;
            designedByLabel.Width = card.Width;
            designedByLabel.Height = 16;

            var nameLabel = new Label();
            nameLabel.Text = "VORONOY";
            nameLabel.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            nameLabel.ForeColor = colorGold;
            nameLabel.TextAlign = ContentAlignment.MiddleCenter;
            nameLabel.Left = 0;
            nameLabel.Top = designedByLabel.Bottom + 2;
            nameLabel.Width = card.Width;
            nameLabel.Height = 28;

            // --- séparateur ---
            var divider = new Panel();
            divider.BackColor = colorGold;
            divider.Left = 60;
            divider.Top = nameLabel.Bottom + 10;
            divider.Width = card.Width - 120;
            divider.Height = 1;

            // --- texte de remerciement ---
            var thanksLabel = new Label();
            thanksLabel.Text = "Special thanks to Voronoy for designing the DCE Manager logo.";
            thanksLabel.Font = new Font("Segoe UI", 9f);
            thanksLabel.ForeColor = colorText;
            thanksLabel.TextAlign = ContentAlignment.MiddleCenter;
            thanksLabel.Left = 40;
            thanksLabel.Top = divider.Bottom + 12;
            thanksLabel.Width = card.Width - 80;
            thanksLabel.Height = 36;

            // --- QR code (190x190, centré) ---
            var qrBox = new PictureBox();
            qrBox.Width = 190;
            qrBox.Height = 190;
            qrBox.Left = (card.Width - qrBox.Width) / 2;
            qrBox.Top = thanksLabel.Bottom + 12;
            qrBox.SizeMode = PictureBoxSizeMode.Zoom;
            qrBox.BackColor = Color.White;
            qrBox.Padding = new Padding(6);
            qrBox.Cursor = Cursors.Hand;
            qrBox.Click += (s, e) => { System.Diagnostics.Process.Start(instagramUrl); };

            string qrPath = Path.Combine(iconsFolder, "qr_Voronoy_instagram.png");
            if (File.Exists(qrPath))
            {
                try { qrBox.Image = Image.FromFile(qrPath); }
                catch { /* fichier illisible : zone laissée vide (fond blanc visible) */ }
            }

            // --- pseudo Instagram ---
            var handleLabel = new Label();
            handleLabel.Text = "@_voronoy_";
            handleLabel.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            handleLabel.ForeColor = colorGold;
            handleLabel.TextAlign = ContentAlignment.MiddleCenter;
            handleLabel.Left = 0;
            handleLabel.Top = qrBox.Bottom + 8;
            handleLabel.Width = card.Width;
            handleLabel.Height = 20;

            // --- bouton "View on Instagram" ---
            var instaButton = new Button();
            instaButton.Text = "View on Instagram";
            instaButton.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            instaButton.ForeColor = colorGold;
            instaButton.BackColor = Color.FromArgb(55, 58, 46);
            instaButton.FlatStyle = FlatStyle.Flat;
            instaButton.FlatAppearance.BorderColor = colorGold;
            instaButton.FlatAppearance.BorderSize = 1;
            instaButton.Width = 260;
            instaButton.Height = 34;
            instaButton.Left = (card.Width - instaButton.Width) / 2;
            instaButton.Top = handleLabel.Bottom + 14;
            instaButton.Cursor = Cursors.Hand;
            instaButton.Click += (s, e) => { System.Diagnostics.Process.Start(instagramUrl); };

            card.Controls.Add(titleLabel);
            card.Controls.Add(logoBox);
            card.Controls.Add(designedByLabel);
            card.Controls.Add(nameLabel);
            card.Controls.Add(divider);
            card.Controls.Add(thanksLabel);
            card.Controls.Add(qrBox);
            card.Controls.Add(handleLabel);
            card.Controls.Add(instaButton);

            return card;
        }

        // Rond doré/sombre avec un "V" au centre, tant que voronoy_logo.png n'existe pas encore
        private Image DrawFallbackBadge(Color goldColor, Color backgroundColor, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brushBack = new SolidBrush(backgroundColor))
                    g.FillEllipse(brushBack, 0, 0, size, size);

                using (var penGold = new Pen(goldColor, 3f))
                    g.DrawEllipse(penGold, 2, 2, size - 4, size - 4);

                using (var font = new Font("Georgia", size * 0.4f, FontStyle.Bold))
                using (var brushGold = new SolidBrush(goldColor))
                {
                    var textSize = g.MeasureString("V", font);
                    g.DrawString("V", font, brushGold, (size - textSize.Width) / 2, (size - textSize.Height) / 2 - 2);
                }
            }
            return bmp;
        }


        private void pic_qrCodeScan_Click(object sender, EventArgs e)
        {
            ShowVoronoyCreditInline();

        }

        private void button_EXIT_Click_1(object sender, EventArgs e)
        {
            Close();
        }


        private void button_EXIT_Click(object sender, EventArgs e)
        {

            Close();
        }




    }

}
