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

        private readonly CampaignUpdater campaignUpdater = new CampaignUpdater();


        //constructeur :
        public Form1()
        {
            KeyPreview = true;

            InitializeComponent();

            scriptsModUpdater = new ScriptsModUpdater(this);
            dceManagerUpdater = new DCEManagerUpdater(this);

            //*************************
            InitializeCampaignGrid();
            //*************************

            CampaignDataGridView.CellContentClick += CampaignDataGridView_CellContentClick;

            Instance = this;  // Initialiser l'instance statique dans le constructeur

            
            // Abonnement aux événements de boutons avec les méthodes de la classe ASTI
            but_ASTI.Click += (sender, e) => ASTI.but_ASTI_Click(this);

            but_ASTI_Browse_Template.Click += (sender, e) => ASTI.but_ASTI_Browse_Template_Click(this);
            but_ASTI_Open_templateFolder.Click += (sender, e) => ASTI.but_ASTI_Open_templateFolder_Click(this);

            but_ASTI_Browse_MissionFile.Click += (sender, e) => ASTI.but_ASTI_Browse_Mission_Click(this);
            
            but_ASTI_Process.Click += (sender, e) => ASTI.but_ASTI_Process_Click(this);

            but_GPS_LL.Click += (sender, e) => ASTI.GPS_LL_Click(this);


            tabControl.Selected += new TabControlEventHandler(TabControl1_SelectedAsync);
            CampaignTab.Selected += new TabControlEventHandler(CampaignTab_Selected);

            // Abonner l'événement FormClosed à une méthode
            this.FormClosed += new FormClosedEventHandler(Form1_FormClosed);

            //this.tabControl1.SelectedTab = tabPage2;

            //VersionDceManager.Text = VersionLongDceManager();
            VersionDceManager.Text = GetVersionDceManager();

            ScriptsModStatusLabel.Text = "Status : Checking...";

            textBox_id_client.Text = CreateIdClient();
            AjusterLargeurTextBox(textBox_id_client);


            string pathOptionInstaller = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager";
            bool exists = System.IO.Directory.Exists(pathOptionInstaller);
            bool fileExists = File.Exists(pathOptionInstaller + @"\options.txt");
            string pathFile = pathOptionInstaller + @"\options.txt";

            if (exists & fileExists)
            {
                try
                {
                    // Utiliser un FileStream avec FileShare.Read pour permettre à d'autres processus de lire le fichier
                    using (FileStream fs = new FileStream(pathFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {

                             // Vérifier si la ligne contient une clé-valeur
                            if (!string.IsNullOrWhiteSpace(line) && line.Contains("="))
                            {
                                // Diviser la ligne à la première occurrence de '=' pour obtenir la clé et la valeur
                                var parts = line.Split(new[] { '=' }, 2);

                                string key = parts[0].Trim();  // Clé
                                string value = parts.Length > 1 ? parts[1].Trim() : string.Empty;  // Valeur

                                // Ajouter la clé-valeur au dictionnaire global
                                ParamConf.configDictionary[key] = value;
                            }
                        }
                    }

                    //Dictionary<string, int> configMap = new Dictionary<string, int>();

                    foreach (var entry in ParamConf.configDictionary)
                    {
                        if (entry.Key == "config_0_pathZipCampaign")
                          textBox_Campaign.Text = entry.Value;
                            
                        if (entry.Key == "config_0_" + "pathDCS")
                            textBox_DCS.Text = entry.Value;

                        if (entry.Key == "config_0_" + "pathSavedGames")
                            textBox_SavedGames.Text = entry.Value;

                        if (entry.Key == "config_0_" + "pathOVGME")
                            textBox_OvGME.Text = entry.Value;

                        if (entry.Key == "upgradeTxtDownload")
                            ParamDownload.UpgradeTime = entry.Value;

                        if (entry.Key == "LastNewsVersion")
                            DceNews.LastNewsVersion = entry.Value;

                        if (entry.Key == "NbLancement")
                        {
                            string nbL = entry.Value;
                            ParamManager.NbLancement = Int32.Parse(nbL);
                        }
                        else if (entry.Key == "ASTI_MissionFile")
                        {
                            string nbL = entry.Value;
                            SharedData.textBox_ASTI_MissionFile = nbL;
                        }
                        else if (entry.Key == "ASTI_importTemplateFolder")
                        {
                            string nbL = entry.Value;
                            SharedData.textBox_ASTI_importTemplateFolder = nbL;
                            but_ASTI_Open_templateFolder.Visible = true;
                        }


                       if (entry.Key.StartsWith("config_") && entry.Key.EndsWith("_"))
                        {
                            // Exemple config_2_=toto2
                            // Extraire la partie entre "config_" et "_"
                            string numStr = entry.Key.Replace("config_", "").TrimEnd('_');

                            // Conversion sécurisée de la chaîne en entier
                            if (int.TryParse(numStr, out int testNum))
                            {
                                // Comparer avec NumMaxConfig
                                if (testNum > ParamConf.NumMaxConfig)
                                {
                                    ParamConf.NumMaxConfig = testNum;
                                }

                                // Ajouter à la ComboBox
                                comboBox_Config.Items.Add(entry.Value);

                                // Vérifier l'existence d'un doublon dans configMap avant d'ajouter
                                if (!ParamConf.configMap.ContainsKey(entry.Value))
                                {
                                    // Ajouter au dictionnaire seulement si la clé est unique
                                    ParamConf.configMap.Add(entry.Value, testNum);
                                }
                                else
                                {
                                    // Si un doublon est détecté, afficher ou loguer l'erreur
                                    string message = $"Doublon détecté pour la clé '{entry.Value}'. Valeur existante : {ParamConf.configMap[entry.Value]}";
                                    MessageBox.Show( message,"error");
                                }
                            }
                            else
                            {
                                // Si la conversion échoue, afficher ou loguer l'erreur
                                string message = $"La valeur '{numStr}' n'est pas un nombre valide.";
                                FormUtils.ErrorGeneral_BoxOrLog(
                                    new Exception("Conversion de chaîne en entier échouée"),
                                    message,
                                    $"Clé: {entry.Key}, Valeur: {entry.Value}",
                                    true,   // Afficher le message dans une MessageBox
                                    true    // Enregistrer dans le log
                                );
                            }
                        }

                    }


                    //****************************************
                    string SelectedItem = "";

                    var dictionaryCopy = new Dictionary<string, string>(ParamConf.configDictionary);

                    foreach (var entry in dictionaryCopy)
                    {
                        
                        //display=MegaUno
                        if (entry.Key == "display")
                        {

                            SelectedItem = entry.Value;

                            string configName = entry.Value;

                            //MessageBox.Show(configName, "configName");

                            if (ParamConf.configMap.TryGetValue(configName, out int configNumber))
                            {
                                ParamConf.NumSelectConfig = configNumber;
                                //MessageBox.Show(ParamConf.NumSelectConfig.ToString() + " SelectedItem "+ SelectedItem, "ParamConf.NumSelectConfig");
                            }
                        }

                        //display=MegaUno
                        if (entry.Key.Contains("upgradeTxtDownload="))
                        {
                            ParamDownload.UpgradeTime = entry.Value;
                        }

                        comboBox_Config.SelectedItem = SelectedItem;

                        textBox_Campaign.Text = ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathZipCampaign"];

                        textBox_DCS.Text = ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathDCS"];

                        textBox_SavedGames.Text = ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathSavedGames"];

                        textBox_OvGME.Text = ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathOVGME"];

                    }
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "Form1", pathOptionInstaller, true, true);
                }
            }

         
            _ = scriptsModUpdater.CheckGithubScriptsModVersionAsync();

            _ = dceManagerUpdater.CheckGithubDCEManagerVersionAsync();

            ParamManager.NbLancement++;

            _ = EnvoiStatsAsync();


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
            toolTip1.SetToolTip(m_ButtonDcsPath, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(textBox_Campaign, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(Label_DCS, @"C:\Eagle Dynamics\DCS World or DCS World OpenBeta");

            toolTip1.SetToolTip(m_ButtonSavedGames, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(textBox_DCS, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");
            toolTip1.SetToolTip(Label_SavedGames, @"C:\Users\yourname\Saved Games\DCS World or DCS World OpenBeta");


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
            AddButtonColumn("Skip", "⏭", 55);
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
        private void AddButtonColumn(string name, string text, int width)
        {
            dataGridViewCampaigns.Columns.Add(new DataGridViewButtonColumn()
            {
                Name = name,
                Text = text,
                UseColumnTextForButtonValue = true,
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
                        Directory.Delete(folderPath, true);

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
                        if (textBox_DCS.Text != "" & textBox_SavedGames.Text != "")
                        {

                            string textPathBat = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                           "set \"pathDCS=" + textBox_DCS.Text + "\\\"\r\n" +
                           "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                           "set \"pathSavedGames=" + textBox_SavedGames.Text + "\\\"\r\n" +
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
                                @"campaignId\s*=\s*""([^""]+)""");

                            if (match.Success)
                                campaignId = match.Groups[1].Value;

                            match = Regex.Match(
                                campInitContent,
                                @"repositoryUrl\s*=\s*""([^""]+)""");

                            if (match.Success)
                                repositoryUrl = match.Groups[1].Value;
                        }
                        //if (campInitContent != null)
                        //{
                        //    var match = Regex.Match(campInitContent, @"version\s*=\s*""([^""]+)""");
                        //    if (match.Success)
                        //        VerCamp = match.Groups[1].Value;
                        //}


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

                                    // 🔵 BONUS → À PLACER ICI (voir explication plus bas)
                                    //var fileInfo = new FileInfo(imagePath);
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

                                    // 🔴 sécurité : si après retry img toujours null
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

                            dataGridViewCampaigns.Rows.Add(
                                null,       // Clone (bouton)
                                img,        // Image
                                NameCamp,   // Name
                                null,       // Folder
                                VerCamp,    // Version
                                NbMission,  // Missions
                                type,       // Aircraft
                                null,       // First
                                null,       // Skip
                                null,       // Config
                                null        // Delete
                            );

                            int rowIndex = dataGridViewCampaigns.Rows.Count - 1;

                            // Exemple : bouton Skip rouge si besoin
                            if (colorSM == "red")
                            {
                                dataGridViewCampaigns.Rows[rowIndex].Cells["Skip"].Style.BackColor = Color.DarkRed;
                            }

                            // Exemple : bouton First rouge
                            if (colorFM == "red")
                            {
                                dataGridViewCampaigns.Rows[rowIndex].Cells["First"].Style.BackColor = Color.DarkRed;
                            }

                            //A = A + 1;

                        }
                    }
                }

            }

            //tabPage2.ResumeLayout();

            //CampaignUpdater updater =
            //    new CampaignUpdater();

            //await updater.RefreshCampaignUpdates(
            //    CampaignDataGridView,
            //    textBox_SavedGames.Text);

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

        // Méthode qui met à jour la valeur partagée
        public void UpdateSharedData()
        {
            SharedData.comboBox_Config = comboBox_Config.Text;
            SharedData.textBox_Campaign = textBox_Campaign.Text;
            SharedData.textBox_DCS = textBox_DCS.Text;
            SharedData.textBox_SavedGames = textBox_SavedGames.Text;
            SharedData.textBox_OvGME = textBox_OvGME.Text;
            SharedData.textBox_ASTI_MissionFile = textBox_ASTI_MissionFile.Text;
            SharedData.textBox_ASTI_importTemplateFolder = textBox_ASTI_importTemplateFolder.Text;

        }

        private async Task EnvoiStatsAsync()
        {
            try
            {
                await EnvoiStats();
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors du step one", "", true, true);
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
                FormUtils.UpdateConfigFileFromDictionary();
                //MessageBox.Show("parametre dans options enregistré, normalement ");
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Form1_FormClosed Error writing config: ", filePath, true, true);
            }
        }

        public bool IsUrlExist(string url, int timeOutMs = 1)
        {
            WebRequest webRequest = WebRequest.Create(url);
            webRequest.Method = "HEAD";
            webRequest.Timeout = timeOutMs;

            try
            {
                var response = webRequest.GetResponse();
                /* response is `200 OK` */
                response.Close();
            }
            catch
            {
                /* Any other response */
                FormUtils.LogRegister("LogRegister 672 No response : " + url);
                return false;
            }

            return true;
        }

        static string CreateIdClient()
        {
            // Récupérer les informations matérielles
            string processorId = GetProcessorId();
            string diskSerial = GetDiskSerial();
            string motherboardSerial = GetMotherboardSerial();

            // Combiner les informations en une chaîne
            string combinedInfo = $"{processorId}-{diskSerial}-{motherboardSerial}";

            // Générer un identifiant unique avec SHA-256
            string clientId = GenerateSHA256Hash(combinedInfo);
            return clientId;
        }

        static string GetProcessorId()
        {
            string processorId = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select ProcessorId from Win32_Processor");

            foreach (ManagementObject obj in searcher.Get())
            {
                processorId = obj["ProcessorId"]?.ToString() ?? "Unknown";
            }

            return processorId;
        }

        static string GetDiskSerial()
        {
            string diskSerial = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select SerialNumber from Win32_DiskDrive");

            foreach (ManagementObject obj in searcher.Get())
            {
                diskSerial = obj["SerialNumber"]?.ToString().Trim() ?? "Unknown";
                break; // Utiliser seulement le premier disque pour cet exemple
            }

            return diskSerial;
        }

        static string GetMotherboardSerial()
        {
            string motherboardSerial = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select SerialNumber from Win32_BaseBoard");

            foreach (ManagementObject obj in searcher.Get())
            {
                motherboardSerial = obj["SerialNumber"]?.ToString().Trim() ?? "Unknown";
            }

            return motherboardSerial;
        }

        static string GenerateSHA256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        //static async Task Main(string[] args)

        static async Task EnvoiStats()
        {
            // Vérifier si la clé "LastStats" existe, sinon la définir avec une valeur par défaut
            if (!ParamConf.configDictionary.TryGetValue("LastStats", out string lastStatsValue) || string.IsNullOrEmpty(lastStatsValue))
            {
                lastStatsValue = DateTime.MinValue.ToString(); // Valeur par défaut si la clé est absente
                ParamConf.configDictionary["LastStats"] = lastStatsValue;
            }


            // Vérifiez si une requête a déjà été envoyée aujourd'hui
            DateTime lastSentDate;
            if (DateTime.TryParse(lastStatsValue, out lastSentDate))
            {
                if (lastSentDate.Date == DateTime.Today)
                {
                    return; // Déjà envoyé aujourd'hui
                }
            }

            // Continuer avec l'envoi des statistiques
            var data = new
            {
                id_client = CreateIdClient(),
                usage_count = ParamManager.NbLancement,
                token = appsettings.token
            };

            using (var client = new HttpClient())
            {
                try
                {
                    var content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("id_client", data.id_client),
                        new KeyValuePair<string, string>("usage_count", data.usage_count.ToString()),
                        new KeyValuePair<string, string>("verDceManager", ParamManager.verDceManager),
                        new KeyValuePair<string, string>("verScriptsMod", ParamScriptsMod.verScriptsMod),
                        new KeyValuePair<string, string>("token", data.token)
                    });

                    HttpResponseMessage response = await client.PostAsync(appsettings.url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        ParamConf.configDictionary["LastStats"] = DateTime.Now.ToString();
                        ParamManager.NbLancement = 0;
                    }
                    else
                    {
                        //MessageBox.Show($"Erreur: {response.StatusCode}", "Erreur step one");
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors du step one", "", false, true);
                }
            }
        }

       

        //check sanitizeModule ?
        public void checkBoxMod()
        {
            string pathFile = textBox_DCS.Text + @"\Scripts\MissionScripting.lua";

            Boolean find_OS = false;
            Boolean find_IO = false;

            if (File.Exists(pathFile))
            {
                
                checkBoxSanitize.Enabled = true;
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
                    checkBoxSanitize.Checked = true;
                }
                else
                {
                    checkBoxSanitize.Checked = false;
                }
            }
            else
            {
                checkBoxSanitize.Enabled = false;
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
            MessageBox.Show("ExtractZipFileToDirectory checkBox_OvwNGfolder.Checked? " + checkBox_OvwNGfolder.Checked.ToString());

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
                                if (checkBox_OvwNGfolder.Checked)
                                {
                                    extractAutorise = true;
                                }
                            }
                        }


                        //// Vérification pour "ScriptsMod.NG"
                        //if (words.Contains("ScriptsMod.NG"))
                        //{
                        //    string scriptsModPath = Path.Combine(destinationDirectoryName, @"\Mods\tech\DCE\ScriptsMod.NG");

                        //    FormUtils.LogRegister("Passe A if (words.Contains(ScriptsMod.NG)) " + scriptsModPath + " | extractAutorise ? " + extractAutorise.ToString());

                        //    // Vérifier si le dossier ScriptsMod.NG existe déjà et s'il contient UTIL_Changelog.lua
                        //    if (Directory.Exists(scriptsModPath) && File.Exists(Path.Combine(scriptsModPath, "UTIL_Changelog.lua")))
                        //    {
                        //        extractAutorise = false;// Interdire la création et l'extraction des fichiers dans ScriptsMod.NG

                        //        FormUtils.LogRegister("Passe B if UTIL_Changelog.lua exist | extractAutorise? " + extractAutorise.ToString());

                        //        if (checkBox_OvwNGfolder.Checked) 
                        //        {
                        //            extractAutorise = true;
                        //            FormUtils.LogRegister("Passe B if checkBox_OvwNGfolder.Checked | extractAutorise? " + extractAutorise.ToString());
                        //        }
                        //    }
                        //}
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

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        
        private void m_ButtonDcsPath_Click(object sender, EventArgs e)
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

                // Pré-sélectionner le répertoire textBox_DCS.Text
                string savedGamesPath = textBox_DCS.Text;
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
                    ParamConf.configDictionary.AddOrUpdate("config_" + ParamConf.NumSelectConfig + "_pathDCS", SharedData.textBox_DCS);
                    textBox_DCS.Text = folderPath;
                    TestPath.DCS_Root = true;

                    string combPath = Path.Combine(folderPath, "bin");
                    if (Directory.Exists(combPath))
                    {
                    }
                    else
                    {
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
        private void m_ButtonSavedGame_Click(object sender, EventArgs e)
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
                    ParamConf.configDictionary.AddOrUpdate("config_" + ParamConf.NumSelectConfig + "_pathSavedGames", SharedData.textBox_DCS);
                    textBox_SavedGames.Text = folderPath;
                    TestPath.OVGME = true;

                    string combPath = Path.Combine(folderPath, "Logs");
                    if (Directory.Exists(combPath))
                    {
                    }
                    else
                    {
                        MessageBox.Show("This directory does not seem to be the DCS Saved Games folder: \r\n" + folderPath, "Error");
                    }
                }
                else
                {
                    //FormUtils.ShowErrorMessage("No folder selected");
                }
            }
        }


        private void m_buttonOvGME_Click(object sender, EventArgs e)
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
                    ParamConf.configDictionary.AddOrUpdate("config_" + ParamConf.NumSelectConfig + "_pathOVGME", SharedData.textBox_DCS);
                    textBox_OvGME.Text = folderPath;
                    
                }
                else
                {
                    //FormUtils.ShowErrorMessage("No folder selected");
                }
            }

        }


        private void Button_choiceCampaign_Click(object sender, EventArgs e)
        {

            TestFile.structureValide = false;
            //TestFile.presenceMisScript = false;
            //TestFile.presenceScriptMod = false;

            OpenFileDialog fdlg = new OpenFileDialog();
            string Downloads = "";
            if (textBox_Campaign.Text == "")
            {
                Downloads = System.Environment.ExpandEnvironmentVariables("%USERPROFILE%\\Downloads");
            }
            Downloads = textBox_Campaign.Text;
            fdlg.InitialDirectory = Downloads;
            fdlg.DefaultExt = ".zip";
            fdlg.Filter = "ZIP  (.ZIP)|*.zip";
            fdlg.FilterIndex = 2;
            fdlg.RestoreDirectory = true;
            if (fdlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                textBox_Campaign.Text = fdlg.FileName;

                //controle si les elements attendu sont dans le fichier zip
                using (var archive = ZipFile.Open(fdlg.FileName, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry file in archive.Entries)
                    {
                        if (file.FullName.Contains("tech"))
                        {
                            TestFile.structureValide = true;
                            //button_InstallCampaign.Visible = true;
                        }
                        if (file.FullName.Contains("Missionscript_mod"))
                        {
                            TestFile.presenceMisScript = true;
                        }

                        //check la version simplifié
                        if (file.FullName.Contains("camp_init"))
                        {
                            TestFile.presenceCampInit = true;
                        }
                        if (file.FullName.Contains("oob_air_init"))
                        {
                            TestFile.presenceOobAirInit = true;
                        }
                        //if (file.FullName.Contains("ScriptsMod"))
                        //{
                        //    TestFile.presenceScriptMod = true;
                        //}
                    }
                }
                if (TestFile.structureValide == false & TestFile.presenceCampInit == false)
                {
                    MessageBox.Show("\"Tech\" path not found.\n Automatic installation canceled", "Error");
                }
                if (TestFile.presenceMisScript == false & TestFile.presenceCampInit == false)
                {
                    MessageBox.Show("Missionscript not found", "Information");
                }
                //if (TestFile.presenceScriptMod == false)
                //{
                //    MessageBox.Show("ScriptsMod not found", "Information");
                //}

            }

            //if(TestFile.structureValide )
            //{
            //    button_InstallCampaign.Visible = true;
            //}
            //else if(TestFile.presenceCampInit && TestFile.presenceOobAirInit)
            // {
            //    button_InstallCampaign.Visible = true;
            //}
        }


        private void button_InstallCampaign_Click(object sender, EventArgs e)
        {

            string combPathDCS = Path.Combine(textBox_DCS.Text, "bin");
            if (Directory.Exists(combPathDCS))
            {
                TestPath.DCS_Root = true;
            }
            else
            {
                MessageBox.Show("This directory does not appear to be the root folder of DCS: \r\n" + textBox_DCS.Text, "Error");
                //button_InstallCampaign.Visible = false;
                return;
            }



            string combPathSavedGame = Path.Combine(textBox_SavedGames.Text, "Logs");
            if (Directory.Exists(combPathSavedGame))
            {
                TestPath.OVGME = true;
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
                TestPath.DCE_alreadyInstalled = true;
            }

            Cursor.Current = Cursors.WaitCursor;

            //bool findNameCampaign = false;
            //bool findScriptsMod = false;

            string NameCampaign = "";
            string zipPath = textBox_Campaign.Text;

            if (File.Exists(textBox_Campaign.Text))
            {

                //cherche le nom de la campagne dans le fichier zip
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        //bool containsSearchResult = entry.FullName.Contains("Campaigns");

                        //if (containsSearchResult & findNameCampaign == false)
                        //{

                        //    string[] words = entry.FullName.Split('/');
                        //    string stringNum = Convert.ToString(words.Length);

                        //    for (int nbFileToUpdat = 0; nbFileToUpdat < words.Length; nbFileToUpdat++)
                        //    {
                        //        string stringMot = Convert.ToString(words[nbFileToUpdat].Length);
                        //        if (entry.Name == "" && words[nbFileToUpdat].Contains("Campaigns") & ((nbFileToUpdat + 1) < words.Length) && (words[nbFileToUpdat + 1].Length > 0) & findNameCampaign == false)
                        //        {
                        //            NameCampaign = words[nbFileToUpdat + 1];
                        //            ParamCampaign.NameCampaign = words[nbFileToUpdat + 1];
                        //            findNameCampaign = true;
                        //            break;

                        //        }
                        //    }
                        //}

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


                //if (TestPath.DCE_alreadyInstalled == false && TestFile.structureValide == false  && TestFile.presenceOobAirInit && TestFile.presenceCampInit)
                if (TestPath.DCE_alreadyInstalled == false  && TestFile.presenceOobAirInit && TestFile.presenceCampInit)
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
                            TestPath.DCE_alreadyInstalled = true;
                        }
                    }
                    else if (result == DialogResult.No)
                    {
                        return;
                    }

                }


                //---------------REGARDE si la CAMPAGNE est déjà installée ----------------------

                ParamCampaign.PathCampaign = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaign.NameCampaign;

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

                if (TestFile.structureValide  )
                {
                    ExtractZipFileToDirectory(textBox_Campaign.Text, true);
                }
                else if (TestPath.DCE_alreadyInstalled && TestFile.presenceOobAirInit && TestFile.presenceCampInit)
                {
                    ExtractZipFileToDirectoryLight(textBox_Campaign.Text, true);
                }
                else
                {
                    MessageBox.Show(NameCampaign + "or " + ParamCampaign.NameCampaign + " impossible to add this campaign.\r\n" +
                         "  ", "Report");
                    return;
                }

                TestFile.ScriptsMod = "NG";



                //ecrit dans le fichier path.bat de la campagne installée:


                //REM Core or Main DCS ou DCS.beta path, always end the line with \
                //set "pathDCS=D:\___DCS___\"

                //REM DCS or DCS.beta saved game path, always end the line with \
                //set "pathSavedGames=Saved Games\DCS.openbeta\" 

                //REM DCE ScriptMod version not any / or \ and no space before and after =
                //set "versionPackageICM=20.43.59"

                string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + ParamCampaign.NameCampaign + @"\Init\path.bat";


                string textPathBat = "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                               "set \"pathDCS=" + textBox_DCS.Text + "\\\"\r\n" +
                               "REM Core or Main DCS ou DCS.beta path, always end the line with \\ \r\n" +
                               "set \"pathSavedGames=" + textBox_SavedGames.Text + "\\\"\r\n" +
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
                        //string txt = "";
                        //foreach (ZipArchiveEntry entryB in archive.Entries)
                        //{
                        //    txt = txt + entryB.Name + "\r\n";
                        //}
                        //MessageBox.Show(txt, "info");

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
                string pathOngoingMission = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + fileNameB;

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
                    MessageBox.Show($"The file {fileNameB} cannot be found in this directory: " + textBox_SavedGames.Text + @"Mods\tech\DCE\Missions\Campaigns");
                }

                MessageBox.Show(ParamCampaign.NameCampaign + " successfully installed.\r\n \r\n" +
                    "   Don't forget to activate the ‘MissionScript’ mod with OVGME ", "Information");

                FormUtils.LogRegister(ParamCampaign.NameCampaign + " successfully installed.\r\n \r\n" +
                    "   Don't forget to activate the ‘MissionScript’ mod with OVGME ");

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
            string[] wordsBarre = textBox_Campaign.Text.Split('\\');
            string[] wordsPoint = textBox_Campaign.Text.Split('.');

            if (wordsPoint.Count() > 1 & wordsBarre.Count() > 1)
            {
                textBox_Campaign.Text = textBox_Campaign.Text.Replace(wordsBarre[wordsBarre.Count() - 1], "");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {

            Close();
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {

        }

        public void toolTip1_Popup(object sender, PopupEventArgs e)
        {

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
            linkLabelOvGME.LinkVisited = true;
            //Call the Process.Start method to open the default browser
            //with a URL:
            System.Diagnostics.Process.Start("https://wiki.hoggitworld.com/view/OVGME#Download_the_installer");
        }

        private void linkLabelCampaign_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                VisitLinkCampaign();
            }
            catch (Exception ex)
            {
                //MessageBox.Show("Unable to open link that was clicked.");
                //FormUtils.ShowErrorMessage(ex.Message + " \n Unable to open link that was clicked.");
                FormUtils.ErrorGeneral_BoxOrLog(ex, " Unable to open link that was clicked. ", " ", true, true);
            }
        }
        private void VisitLinkCampaign()
        {
            // Change the color of the link text by setting LinkVisited
            // to true.
            linkLabelCampaign.LinkVisited = true;
            //Call the Process.Start method to open the default browser
            //with a URL:
            System.Diagnostics.Process.Start("https://forums.eagle.ru/topic/162712-dce-campaigns/");
        }

      

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

        private void label5_Click(object sender, EventArgs e)
        {

        }


        private void ScriptsModUpdate_Click(object sender, EventArgs e)
        {

        }

        private void textBox1_TextChanged_2(object sender, EventArgs e)
        {

        }



        public void CampaignPlusClickOneEvent(object sender, EventArgs e, string path, string OldNameCamp)
        {
            
            // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            UpdateSharedData();

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
                groupBoxCampEdit.Visible = false;
                groupBox_staticTemplate.Visible = false;
                groupBoxCampEdit.Text = "";

                CampaignTab.Visible = false;
                

            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage3       +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPageLeft_Update)
            {
                groupBoxDroiteAccueil.Visible = true;
                groupBoxCampEdit.Visible = false;
                groupBox_staticTemplate.Visible = false;

                FormUtils.MakeRoundedButton( ScriptsModUpdateButton, 10);

                FormUtils.MakeRoundedButton(
                    DCEManagerUpdateButton,
                    10);

                FormUtils.MakeRoundedButton(
                    buttonCampaignCancel,
                    10);

                groupBoxCampEdit.Text = "";

                CampaignTab.Visible = false;

                DCEManagerInstalledVersion.Text = VersionDceManager.Text;
                if (String.IsNullOrEmpty(textBox_SavedGames.Text))
                {
                    MessageBox.Show("You must enter the path to the SavedGame folder in the \"Install\" tab ", "Report");
                    return;
                }

                Directory.CreateDirectory(ParamManager.pathManager);
                ParamUpdater.CreateFolders();

                CampaignUpdater.InitCampaignUpdateGrid(CampaignDataGridView);

                await campaignUpdater.RefreshCampaignUpdates(CampaignDataGridView, textBox_SavedGames.Text);

            }
            else if (e.TabPage == tabPageLeft_About)
            {
                groupBoxDroiteAccueil.Visible = true;
                groupBoxCampEdit.Visible = false;
                groupBox_staticTemplate.Visible = false;
                groupBoxCampEdit.Text = "";
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
                groupBoxCampEdit.Visible = false;
                groupBox_staticTemplate.Visible = false;
                groupBoxCampEdit.Text = "";
                CampaignTab.Visible = false;

            }
            else if (e.TabPage == tabPageLeft_Campaigns)
            {
                Cursor.Current = Cursors.WaitCursor;

                groupBoxDroiteAccueil.Visible = false;
                groupBoxCampEdit.Visible = true;
                groupBox_staticTemplate.Visible = false;
                groupBoxCampEdit.Text = "";

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

        private void comboBox_Config_SelectedIndexChanged(object sender, EventArgs e)
        {

            //TODO, doit y avoir melange dans configMap, un vrai bordel
            if (ParamConf.configMap.TryGetValue((string)comboBox_Config.SelectedItem, out int configNumber))
            {
                ParamConf.NumSelectConfig = configNumber;
            }

            ParamConf.configDictionary["display"] = (string)comboBox_Config.SelectedItem;

            textBox_Campaign.Text = ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathZipCampaign"];

            textBox_DCS.Text = ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathDCS"];

            textBox_SavedGames.Text = ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathSavedGames"];

            textBox_OvGME.Text = ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathOVGME"];
            
            //CheckVersionScriptsModLocal();

            //MessageBox.Show("ParamConf.NumSelectConfig "+ ParamConf.NumSelectConfig.ToString(), textBox_Campaign.Text);

        }


        private void m_Button_AddConfig_Click(object sender, EventArgs e)
        {

            string SelectedItem = "";

            if (textBox_Config.Text == "")
            {
                MessageBox.Show("Please enter a name for this new configuration", "Error");
                return;
            }

            if (textBox_Campaign.Text == "" | textBox_DCS.Text == "" | textBox_SavedGames.Text == "" | textBox_OvGME.Text == "")
            {
                MessageBox.Show("Please fill in the paths in the 4 boxes", "Error");
                return;
            }

            ParamConf.NumMaxConfig = ParamConf.NumMaxConfig + 1;
            ParamConf.NumSelectConfig = ParamConf.NumMaxConfig;
            

            ParamConf.configDictionary.Add("config_" + ParamConf.NumSelectConfig + "_", textBox_Config.Text);

            ParamConf.configDictionary.Add("config_" + ParamConf.NumSelectConfig + "_pathZipCampaign" , textBox_Campaign.Text);

            ParamConf.configDictionary.Add("config_" + ParamConf.NumSelectConfig + "_pathDCS", textBox_DCS.Text);

            ParamConf.configDictionary.Add("config_" + ParamConf.NumSelectConfig + "_pathSavedGames", textBox_SavedGames.Text);

            ParamConf.configDictionary.Add("config_" + ParamConf.NumSelectConfig + "_pathOVGME", textBox_OvGME.Text);


            comboBox_Config.Items.Add(textBox_Config.Text);

            SelectedItem = textBox_Config.Text;

            ParamConf.configDictionary["display"] = textBox_Config.Text;

            comboBox_Config.SelectedItem = SelectedItem;

            textBox_Config.Text = "";

            // Utiliser la fonction pour mettre à jour le fichier avec le contenu du dictionnaire
            FormUtils.UpdateConfigFileFromDictionary();

        }


        private void but_EditConfig_Click(object sender, EventArgs e)
        {
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_"] = comboBox_Config.Text;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathZipCampaign"] = textBox_Campaign.Text;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathDCS"] = textBox_DCS.Text;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathSavedGames"] = textBox_SavedGames.Text;
            ParamConf.configDictionary["config_" + ParamConf.NumSelectConfig + "_pathOVGME"] = textBox_OvGME.Text;

            // Utiliser la fonction pour mettre à jour le fichier avec le contenu du dictionnaire
            bool result = FormUtils.UpdateConfigFileFromDictionary();

            if(result==true)
            {
                MessageBox.Show("The " + comboBox_Config.Text + " configuration paths have been save", "Report");
            }
            
        }

        private void m_Button_Config_Del_Click(object sender, EventArgs e)
        {

            if (comboBox_Config.Text == "")
            {
                MessageBox.Show("No configuration name selected", "Error");
            }
            else
            {
                ParamConf.configDictionary.Remove("config_" + ParamConf.NumSelectConfig + "_");

                ParamConf.configDictionary.Remove("config_" + ParamConf.NumSelectConfig + "_pathZipCampaign");

                ParamConf.configDictionary.Remove("config_" + ParamConf.NumSelectConfig + "_pathDCS");

                ParamConf.configDictionary.Remove("config_" + ParamConf.NumSelectConfig + "_pathSavedGames");

                ParamConf.configDictionary.Remove("config_" + ParamConf.NumSelectConfig + "_pathOVGME");

                ParamConf.NumSelectConfig = 0;
                ParamConf.NumMaxConfig = ParamConf.NumMaxConfig - 1;

                ParamConf.configMap.Remove(comboBox_Config.Text);

                comboBox_Config.Items.Remove(comboBox_Config.Text);

                comboBox_Config.DisplayMember = "";

                ParamConf.configDictionary["display"] = comboBox_Config.Text;

                textBox_Campaign.Text = "";
                textBox_DCS.Text = "";
                textBox_SavedGames.Text = "";
                textBox_OvGME.Text = "";
                textBox_Config.Text = "";

                // Utiliser la fonction pour mettre à jour le fichier avec le contenu du dictionnaire
                FormUtils.UpdateConfigFileFromDictionary();
            }
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
            process.StartInfo.FileName = textBox_DCS.Text + @"\bin\DCS.exe";
            process.StartInfo.Arguments = " ";
            process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            process.StartInfo.WorkingDirectory = textBox_DCS.Text + @"\bin";

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

       

        private void label_server_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox6_Click(object sender, EventArgs e)
        {

        }

        private void textBox_ChangelogScriptsMod_TextChanged(object sender, EventArgs e)
        {

        }

        private void labelUpdateDceManager_Click(object sender, EventArgs e)
        {

        }

        private void label11_Click(object sender, EventArgs e)
        {

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

        private void label9_Click(object sender, EventArgs e)
        {

        }

        private void txtBoxFolderCreateUpdate_TextChanged(object sender, EventArgs e)
        {
            //txtBoxFolderCreateUpdate
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

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }



        public static bool PointIsWithinCircle(double circleRadius, double circleCenterPointX, double circleCenterPointY, double pointToCheckX, double pointToCheckY)
        {
            return (Math.Pow(pointToCheckX - circleCenterPointX, 2) + Math.Pow(pointToCheckY - circleCenterPointY, 2)) < (Math.Pow(circleRadius, 2));
        }

        private void C_DataMap_Click(object sender, EventArgs e)
        {
            Boolean makeCircleMap = true;
            Boolean makeUniquePointMap = false;

            if (makeCircleMap == true)
            {

                //MessageBox.Show("AA START makeCircleMap");

                int[,] ArrayCircle = new int[100000, 3];


                string texteFinal = null;

                Bitmap img = new Bitmap(@"D:\DCS_Maps\DCS_F10_Caucasus.png");
                Color newColor = Color.Red;
                Bitmap img2 = new Bitmap(@"D:\DCS_Maps\DCS_F10_Caucasus2.png");

                //texteFinal = "Width: " + img.Width.ToString() + " Height: " + img.Height.ToString() + "\r\n";


                //circleSAR = {
                //    {
                //       pixel_x = 1,   
                //       pixel_y = 276,  
                //       radius = 5,  
                //   },  
                //   {
                //                    pixel_x = 31,   
                //       pixel_y = 756,  
                //       radius = 35,  
                //   },  
                //   {
                //                    pixel_x = 38,   
                //       pixel_y = 838,  
                //       radius = 42,  
                //   }, 

                texteFinal = "circleSAR = {";

                int NColor = 1;
                Color SColors = Color.Black;

                //Width: 18869 Height: 10391
                for (int i = 1; i < (img.Width / 1); i += 25) //25
                {
                    for (int j = 1; j < (img.Height / 1); j += 25)
                    {
                        //bool breakInCircle = false;
                        Color pixel = img.GetPixel(i, j);
                        if (pixel.B != 198 && pixel.G >= 150)
                        {
                            //bool badLand = false;
                            bool breakInCircle = false;
                            int centerX = 0;
                            int centerY = 0;
                            int max_ii = 0;
                            int max_jj = 0;
                            int max_m = 0;
                            bool breakBoolean = false;
                            int maxPointLibre = 0;

                            if (NColor == 3)
                            {
                                NColor = 1;
                                SColors = Color.Black;
                            }
                            else if (NColor == 1)
                            {
                                NColor = 2;
                                SColors = Color.Blue;
                            }
                            else if (NColor == 2)
                            {
                                NColor = 3;
                                SColors = Color.Red;
                            }

                            for (int m = 0; m <= 100; m += 15)
                            {
                                //si le cercle est déjà dans un autre, aucun regard et creation, on passe au suivant                        
                                for (int n = 0; n < 100000; n++)
                                {
                                    if (ArrayCircle[n, 0] != 0)
                                    {
                                        int center_x = ArrayCircle[n, 0];
                                        int center_y = ArrayCircle[n, 1];
                                        int radius = ArrayCircle[n, 2];

                                        if (Math.Pow((i - center_x), 2) + Math.Pow((j - center_y), 2) <= Math.Pow(radius, 2))
                                        {
                                            breakInCircle = true;
                                            break;
                                        }
                                    }
                                }
                                if (breakInCircle)
                                    break;

                                for (int ii = i + m; ii < i + m + 5; ii += 5) //110
                                {
                                    for (int jj = j + m; jj < j + m + 5; jj += 5) //110 115
                                    {
                                        //if ((ii >= 0 & ii < img.Width) & (jj >= 0 & jj < img.Height))
                                        if (breakBoolean != true && (i >= 0 & i < img.Width) & (jj >= 0 & jj < img.Height))
                                        {

                                            Color pixelB = img.GetPixel(i, jj);
                                            max_ii = i + m - 5;
                                            max_jj = j + m - 5;
                                            maxPointLibre++;
                                            max_m = m - 15;

                                            //if (pixelB.B == 198 || pixelB.G <= 160)
                                            if ((pixelB.B == 198 || pixelB.G <= 139) & pixelB.G != 0 & (pixelB.G >= 75 & pixelB.G >= 79))
                                            {
                                                //badLand = true;
                                                breakBoolean = true;

                                                break;
                                            }

                                            //if ((nbFileToUpdat >= 0 & nbFileToUpdat < img.Width) & (jj >= 0 & jj < img.Height))
                                            //{
                                            //    img2.SetPixel(nbFileToUpdat, jj, SColors);
                                            //}

                                        }
                                        if (breakBoolean != true && (ii >= 0 & ii < img.Width) & (j >= 0 & j < img.Height))
                                        {

                                            Color pixelB = img.GetPixel(ii, j);
                                            max_ii = i + m - 5;
                                            max_jj = j + m - 5;
                                            //maxPointLibre++;
                                            max_m = m - 15;

                                            //if (pixelB.B == 198 || pixelB.G <= 160)
                                            if ((pixelB.B == 198 || pixelB.G <= 139) & pixelB.G != 0 & (pixelB.G >= 75 & pixelB.G >= 79))
                                            {
                                                //badLand = true;
                                                breakBoolean = true;

                                                break;
                                            }


                                            //if ((ii >= 0 & ii < img.Width) & (j >= 0 & j < img.Height))
                                            //{
                                            //    img2.SetPixel(ii, j, SColors);
                                            //}

                                        }
                                        if (breakBoolean != true && (ii >= 0 & ii < img.Width) & (jj >= 0 & jj < img.Height))
                                        {
                                            Color pixelB = img.GetPixel(ii, jj);
                                            max_ii = i + m - 5;
                                            max_jj = j + m - 5;
                                            //maxPointLibre++;
                                            max_m = m - 15;

                                            //if (pixelB.B == 198 || pixelB.G <= 160)
                                            if ((pixelB.B == 198 || pixelB.G <= 139) & pixelB.G != 0 & (pixelB.G >= 75 & pixelB.G >= 79))
                                            {
                                                //badLand = true;
                                                breakBoolean = true;
                                                break;
                                            }


                                            //if ((ii >= 0 & ii < img.Width) & (jj >= 0 & jj < img.Height))
                                            //{
                                            //    img2.SetPixel(ii, jj, SColors);
                                            //}

                                        }
                                    }
                                    if (breakBoolean)
                                        break;
                                }
                                if (breakBoolean)
                                    break;
                            }

                            if (!breakInCircle)
                            {

                                //centerX = (nbFileToUpdat + max_ii) / 2;
                                //centerY = (j + max_jj) / 2;

                                centerX = i + (max_m / 2);
                                centerY = j + (max_m / 2);

                                int rayonX = (max_ii - i) / 2;
                                int rayonY = (max_jj - j) / 2;
                                int rayon = 0;
                                if (rayonX <= rayonY)
                                {
                                    rayon = rayonX;
                                }
                                else
                                {
                                    rayon = rayonY;
                                }


                                //si le cercle est déjà dans un autre, aucun regard et creation, on passe au suivant                        
                                for (int n = 0; n < 100000; n++)
                                {
                                    if (ArrayCircle[n, 0] != 0)
                                    {
                                        int center_x = ArrayCircle[n, 0];
                                        int center_y = ArrayCircle[n, 1];
                                        int radius = ArrayCircle[n, 2];

                                        for (double ia = 0.0; ia < 360.0; ia += 36)
                                        {
                                            double angle = ia * System.Math.PI / 180;

                                            int x = (int)(centerX + radius * System.Math.Cos(angle));

                                            int y = (int)(centerY + radius * System.Math.Sin(angle));

                                            if (Math.Pow((x - center_x), 2) + Math.Pow((y - center_y), 2) <= Math.Pow(radius, 2))
                                            {
                                                breakInCircle = true;
                                                break;
                                            }
                                        }

                                        //if (Math.Pow((centerX - center_x), 2) + Math.Pow((centerY - center_y), 2) <= Math.Pow(radius, 2))
                                        //{
                                        //    breakInCircle = true;
                                        //    break;
                                        //}
                                    }
                                }

                                //if (badLand == false)
                                if (rayon > 2 & !breakInCircle)    //maxPointLibre >= 1 && && max_ii !=0 && max_jj != 0
                                {

                                    //texteFinal = (texteFinal + (nbFileToUpdat.ToString() + " _ " + j.ToString() + " : " + pixel.ToString() + " :: X " + centerX.ToString() + "  Y: " + centerY.ToString() + "  r: " + rayon.ToString() + "\r\n"));

                                    texteFinal = texteFinal +
                                        "\r\n" +
                                        "   {   \r\n" +
                                        "       pixel_x = " + centerX.ToString() + ",   \r\n" +
                                        "       pixel_y = " + centerY.ToString() + ",  \r\n" +
                                        "       radius = " + rayon.ToString() + ",  \r\n" +
                                        "   },  ";

                                    int radius = rayon;

                                    for (double ia = 0.0; ia < 360.0; ia += 36)
                                    {
                                        double angle = ia * System.Math.PI / 180;

                                        int x = (int)(centerX + radius * System.Math.Cos(angle));

                                        int y = (int)(centerY + radius * System.Math.Sin(angle));

                                        if ((x >= 0 & x < img.Width) & (y >= 0 & y < img.Height))
                                        {
                                            img2.SetPixel(x, y, SColors);
                                        }
                                    }


                                    for (int n = 0; n < 100000; n++)
                                    {
                                        if (ArrayCircle[n, 0] == 0)
                                        {
                                            ArrayCircle[n, 0] = centerX;
                                            ArrayCircle[n, 1] = centerY;
                                            ArrayCircle[n, 2] = rayon;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                texteFinal = texteFinal + "      \r\n" +
                    "}";
                StreamWriter sr2 = new StreamWriter(@"D:\DCS_Maps\DCS_F10_Caucasus.txt");
                sr2.WriteLine(texteFinal);
                sr2.Close();

                //MessageBox.Show("A ok");

                img2.Save(@"D:\DCS_Maps\DCS_F10_Caucasus3.png");
                //MessageBox.Show("B ok");

            } // if (makeCercleMap)


            if (makeUniquePointMap == true)
            {
                //MessageBox.Show("C1 START");

                ////**** TEST UNE POSITION CONNUE ****
                ////Width: 18869 Height: 10391
                // axe_X : 18869 pixel (gauche à droite)
                // axe_Y : 10391 pixel (haut en bas)
                // 0/0 point origine en haut à droite

                //trouver 2 bases eloigné, dessiner 2 cerles dessus et trouver la distance équivalente sur DCS
                //426 nm => 788952 metre
                // 1 pixel = 41.81 metre

                //position Vaziani
                int testX = 17640; // 15963;
                int testY = 8013; // 7472;
                //["y"] = 904028,
                //["x"] = -319918,
                
                Bitmap img10 = new Bitmap(@"D:\DCS_Maps\DCS_F10_Caucasus2.png");

                //Sukhumi
                //int testX = 10444;
                //int testY = 5900;

                img10.SetPixel(testX, testY, Color.LightBlue);

                for (int radius = 10; radius <= 200; radius += 10)
                {
                    for (double ia = 0.0; ia < 360.0; ia += 36)
                    {
                        double angle = ia * System.Math.PI / 180;

                        int x = (int)(testX + radius * System.Math.Cos(angle));

                        int y = (int)(testY + radius * System.Math.Sin(angle));

                        if ((x >= 0 & x < img10.Width) & (y >= 0 & y < img10.Height))
                        {
                            img10.SetPixel(x, y, Color.Red);
                        }
                    }
                }



                ////dessine un gros gras rond rouge
                //for (int radius = 1; radius <= 20; radius += 1)
                //{
                //    for (double ia = 0.0; ia < 360.0; ia += 2)
                //    {
                //        double angle = ia * System.Math.PI / 180;

                //        int x = (int)(testX + radius * System.Math.Cos(angle));

                //        int y = (int)(testY + radius * System.Math.Sin(angle));

                //        if ((x >= 0 & x < img10.Width) & (y >= 0 & y < img10.Height))
                //        {
                //            img10.SetPixel(x, y, Color.Red);
                //        }
                //    }
                //}

                img10.Save(@"D:\DCS_Maps\DCS_F10_CaucasusUniquePoint.png");
                //MessageBox.Show("C2 ok");

            }   // if (makeUniquePointMap)
        }
        


        private void C_DataMapCity_Click_1(object sender, EventArgs e)
        {

            int[,] ArrayCircle = new int[100000, 3];


            string texteFinal = null;
            //bool premierPixelIdentique = false;

            //Bitmap img = new Bitmap(@"D:\DCS_Maps\DCS_F10_Caucasus.png");
            Bitmap img = new Bitmap(@"D:\DCS_Maps\DCS_F10_Caucasus2.png");
            Color newColor = Color.Red;
            Bitmap img2 = new Bitmap(@"D:\DCS_Maps\DCS_F10_Caucasus2.png");

            //texteFinal = "Width: " + img.Width.ToString() + " Height: " + img.Height.ToString() + "\r\n";


            //circleSAR = {
            //    {
            //       pixel_x = 1,   
            //       pixel_y = 276,  
            //       radius = 5,  
            //   },  
            //   {
            //                    pixel_x = 31,   
            //       pixel_y = 756,  
            //       radius = 35,  
            //   },  
            //   {
            //                    pixel_x = 38,   
            //       pixel_y = 838,  
            //       radius = 42,  
            //   }, 

            texteFinal = "circleCity = {";

            int NColor = 1;
            Color SColors = Color.Black;

            //Width: 18869 Height: 10391
            for (int i = 1; i < (img.Width / 1); i += 25) //25
            {
                for (int j = 1; j < (img.Height / 1); j += 25)
                {
                    //bool breakInCircle = false;
                    Color pixel = img.GetPixel(i, j);

                    ////la mer: R=126 V185 B198
                    if (pixel.B != 198)        //&& pixel.G >= 150
                    {
                        bool breakInCircle = false;
                        int centerX = 0;
                        int centerY = 0;
                        int max_ii = 0;
                        int max_jj = 0;
                        int max_m = 0;
                        bool breakBoolean = false;
                        bool breakBoolean1 = false;
                        bool breakBoolean2 = false;
                        bool breakBoolean3 = false;
                        bool foundCity = false;


                        int maxPointLibre = 0;

                        if (NColor == 3)
                        {
                            NColor = 1;
                            SColors = Color.Black;
                        }
                        else if (NColor == 1)
                        {
                            NColor = 2;
                            SColors = Color.Blue;
                        }
                        else if (NColor == 2)
                        {
                            NColor = 3;
                            SColors = Color.Red;
                        }

                        for (int m = 0; m <= 50; m += 15)
                        {
                            ////si le cercle est déjà dans un autre, aucun regard et creation, on passe au suivant                        
                            //for (int n = 0; n < 100000; n++)
                            //{
                            //    if (ArrayCircle[n, 0] != 0)
                            //    {
                            //        int center_x = ArrayCircle[n, 0];
                            //        int center_y = ArrayCircle[n, 1];
                            //        int radius = ArrayCircle[n, 2];

                            //        if (Math.Pow((nbFileToUpdat - center_x), 2) + Math.Pow((j - center_y), 2) <= Math.Pow(radius, 2))
                            //        {
                            //            breakInCircle = true;
                            //            break;
                            //        }
                            //    }
                            //}
                            //if (breakInCircle)
                            //    break;

                            for (int ii = i + m - 50; ii < i + m + 5; ii += 5) //110
                            {
                                for (int jj = j + m - 50; jj < j + m + 5; jj += 5) //110 115
                                {
                                    if (breakBoolean != true && (i >= 0 & i < img.Width) & (jj >= 0 & jj < img.Height))
                                    {

                                        Color pixelB = img.GetPixel(i, jj);
                                        max_ii = i + m;
                                        max_jj = j + m;
                                        maxPointLibre++;
                                        max_m = m;

                                        ////la mer: R=126 V185 B198
                                        //if (pixelB.R < 126 )
                                        //{
                                        //    breakBoolean1 = true;
                                        //    //break;
                                        //}
                                        //la ville R198 V131 R0

                                        if (pixelB.R == 198 & pixelB.G == 131)
                                        {
                                            foundCity = true;
                                        }
                                    }
                                    if (breakBoolean != true && (ii >= 0 & ii < img.Width) & (j >= 0 & j < img.Height))
                                    {

                                        Color pixelB = img.GetPixel(ii, j);
                                        max_ii = i + m;
                                        max_jj = j + m;
                                        //maxPointLibre++;
                                        max_m = m - 15;


                                        if (pixelB.R == 198 & pixelB.G == 131)
                                        {
                                            foundCity = true;
                                        }

                                    }
                                    if (breakBoolean != true && (ii >= 0 & ii < img.Width) & (jj >= 0 & jj < img.Height))
                                    {
                                        Color pixelB = img.GetPixel(ii, jj);
                                        max_ii = i + m;
                                        max_jj = j + m;
                                        //maxPointLibre++;
                                        max_m = m;


                                        if (pixelB.R == 198 & pixelB.G == 131)
                                        {
                                            foundCity = true;
                                        }
                                    }
                                    if (breakBoolean1 == true & breakBoolean2 == true & breakBoolean3 == true)
                                    {
                                        breakBoolean = true;
                                        max_m = m - 15;
                                        max_ii = ii - 5;
                                        max_jj = jj - 5;
                                    }

                                }
                                if (breakBoolean)
                                    break;
                            }
                            if (breakBoolean)
                                break;
                        }

                        if (foundCity)
                        {
                            //centerX = nbFileToUpdat + (max_m / 2);
                            //centerY = j + (max_m / 2);

                            //int rayonX = (max_ii - nbFileToUpdat) / 2;
                            //int rayonY = (max_jj - j) / 2;

                            centerX = i + (max_m / 1);
                            centerY = j + (max_m / 1);

                            int rayonX = (max_ii - i) / 1;
                            int rayonY = (max_jj - j) / 1;


                            int rayon = 0;
                            if (rayonX <= rayonY)
                            {
                                rayon = rayonX;
                            }
                            else
                            {
                                rayon = rayonY;
                            }

                            rayon = rayon * 2;

                            //si le cercle est déjà dans un autre, aucun regard et creation, on passe au suivant                        
                            for (int n = 0; n < 100000; n++)
                            {
                                if (ArrayCircle[n, 0] != 0)
                                {
                                    int center_x = ArrayCircle[n, 0];
                                    int center_y = ArrayCircle[n, 1];
                                    int radius = ArrayCircle[n, 2];

                                    for (double ia = 0.0; ia < 360.0; ia += 36)
                                    {
                                        double angle = ia * System.Math.PI / 180;

                                        int x = (int)(centerX + radius * System.Math.Cos(angle));

                                        int y = (int)(centerY + radius * System.Math.Sin(angle));

                                        if (Math.Pow((x - center_x), 2) + Math.Pow((y - center_y), 2) <= Math.Pow(radius, 2))
                                        {
                                            breakInCircle = true;
                                            break;
                                        }
                                    }

                                    //if (Math.Pow((centerX - center_x), 2) + Math.Pow((centerY - center_y), 2) <= Math.Pow(radius, 2))
                                    //{
                                    //    breakInCircle = true;
                                    //    break;
                                    //}
                                }
                            }

                            //if (badLand == false)
                            if (rayon > 15 & !breakInCircle)    //maxPointLibre >= 1 && && max_ii !=0 && max_jj != 0
                            {

                                //texteFinal = (texteFinal + (nbFileToUpdat.ToString() + " _ " + j.ToString() + " : " + pixel.ToString() + " :: X " + centerX.ToString() + "  Y: " + centerY.ToString() + "  r: " + rayon.ToString() + "\r\n"));

                                texteFinal = texteFinal +
                                    "\r\n" +
                                    "   {   \r\n" +
                                    "       pixel_x = " + centerX.ToString() + ",   \r\n" +
                                    "       pixel_y = " + centerY.ToString() + ",  \r\n" +
                                    "       radius = " + rayon.ToString() + ",  \r\n" +
                                    "   },  ";

                                int radius = rayon;

                                for (double ia = 0.0; ia < 360.0; ia += 36)
                                {
                                    double angle = ia * System.Math.PI / 180;

                                    int x = (int)(centerX + radius * System.Math.Cos(angle));

                                    int y = (int)(centerY + radius * System.Math.Sin(angle));

                                    if ((x >= 0 & x < img.Width) & (y >= 0 & y < img.Height))
                                    {
                                        img2.SetPixel(x, y, SColors);
                                    }
                                }

                                for (int n = 0; n < 100000; n++)
                                {
                                    if (ArrayCircle[n, 0] == 0)
                                    {
                                        ArrayCircle[n, 0] = centerX;
                                        ArrayCircle[n, 1] = centerY;
                                        ArrayCircle[n, 2] = rayon;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            texteFinal = texteFinal + "      \r\n" +
                "}";
            StreamWriter sr2 = new StreamWriter(@"D:\DCS_Maps\DCS_F10_CaucasusCity.txt");
            sr2.WriteLine(texteFinal);
            sr2.Close();

            //MessageBox.Show("A ok");

            img2.Save(@"D:\DCS_Maps\DCS_F10_CaucasusCity.png");
            //MessageBox.Show("B ok");

        }

        private void checkBoxSanitize_CheckedChanged(object sender, EventArgs e)
        {
            string pathFile = textBox_DCS.Text + @"\Scripts\MissionScripting.lua";
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
                            if (checkBoxSanitize.Checked == true)
                            {
                                string ligneModifiee = "--" + line;
                                FormUtils.ModifierLigneByNumber(pathFile, i, ligneModifiee);
                            }
                            else
                            {
                                string ligneModifiee = line.Replace("--", "");
                                FormUtils.ModifierLigneByNumber(pathFile, i, ligneModifiee);
                            }
                               
                        }

                        i++;
                    }
                }
            }
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }

        private void tabPage2_TextChanged(object sender, EventArgs e)
        {

        }

        void CampaignTab_Selected(object sender, TabControlEventArgs e)
        {
            UpdateCampaignButtonsVisibility();
        }
        private void UpdateCampaignButtonsVisibility()
        {
            bool show =
                groupBoxCampEdit.Text != "" &&
                (CampaignTab.SelectedTab == tabPage14 || CampaignTab.SelectedTab == tabPage15);

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
            // Form1.groupBoxCampEdit.Text = nameCamp;

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
            string pathFileBackup = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Init\oob_air_init_backup_DTT.lua";

            string pathFile = "";
            string FolderName = "";

            if (radioButton_OOB_INIT.Checked)
            {
                pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Init\oob_air_init.lua";
                FolderName = "Init";
            }
            else if (radioButton_OOB_ACTIVE.Checked)
            {
                pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Active\oob_air.lua";

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

                    if (rowName == groupBoxCampEdit.Text)
                    {
                        // Met à jour la colonne Aircraft
                        dataGridViewCampaigns.Rows[i].Cells["Aircraft"].Value = newType;

                        // Met à jour aussi l'image si elle dépend du type
                        string imagePath = Path.Combine(
                            textBox_SavedGames.Text,
                            "Mods", "tech", "DCE", "Missions", "Campaigns",
                            groupBoxCampEdit.Text,
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
            string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Active\oob_air.lua";
           
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
                string pathFileBackup = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Init\oob_air_init_backup_DTT.lua";
                string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Init\oob_air_init.lua";
                //string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Init\oob_air_initCLONE.lua";

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


            string path = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text;

        }

        private void tabPage8_Click(object sender, EventArgs e)
        {

        }

        private void toolTip3_Popup(object sender, PopupEventArgs e)
        {

        }

        private void groupBoxCampEdit_Enter(object sender, EventArgs e)
        {

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

        

        private void butClient_Click(object sender, EventArgs e)
        {
            groupBox4.Visible = false;
            checkBoxActiveFolder.Visible = false;
            checkBox_OvwNGfolder.Visible = false;
            checkBoxSanitize.Visible = false;
            LabelStatut.Text = "User";
            ParamManager.userLevel = 1;

            textBox_id_client.Visible = false;
            ScriptsModUpdateButton.Text = "Update";
        }

        private void but_Expert_Click(object sender, EventArgs e)
        {
            groupBox4.Visible = true;
            checkBoxActiveFolder.Visible = true;
            checkBox_OvwNGfolder.Visible = true;
            checkBoxSanitize.Visible = true;
            textBox_id_client.Visible = false;
            LabelStatut.Text = "Expert";
            ParamManager.userLevel = 2;
        }


        private void butCampMaker_Click(object sender, EventArgs e)
        {

            textBox_id_client.Visible = false;
            LabelStatut.Text = "CampaignMaker";
            ScriptsModUpdateButton.Text = "Update";
            ParamManager.userLevel = 3;

        }

        private void VersionDceManager_Click(object sender, EventArgs e)
        {
            //Pour devenir DEV

            if (ButtonPreview == true ) {

                GetVersionDceManager();
                but_GPS_LL.Visible = true;
                LabelStatut.Text = "DEV";
                ScriptsModUpdateButton.Text = "Update DEV";
                textBox_id_client.Visible = true;
            }

        }


        private void textBox_id_client_TextChanged(object sender, EventArgs e)
        {

        }

        private void dataGridViewBlue_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void dataGridViewRed_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

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

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        private void groupBox6_Enter(object sender, EventArgs e)
        {

        }

        private async void CampaignDataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {


            if (e.RowIndex < 0)
                return;

            if (CampaignDataGridView.Columns[e.ColumnIndex].Name != "Action")
                return;

            CampaignInfo campaign = campaignUpdater.GetCampaignFromRow(e.RowIndex);

            if (campaign == null)
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

                FormUtils.LogRegister("Campaign installed");

                await campaignUpdater.RefreshCampaignUpdates(
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

        private void labelCampaignTitle_Click(object sender, EventArgs e)
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
    }

}