using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using Microsoft.VisualBasic.FileIO;
using NLua;
//using DCE_Manager.ASTI;
using Ookii.Dialogs.WinForms;
using SearchOption = System.IO.SearchOption;

namespace DCE_Manager
{

    public partial class Form1 : Form
    {
        public static Form1 Instance { get; private set; }

        public DataTable dataTable;

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

        public Form1()
        {
            KeyPreview = true;

            InitializeComponent();

            Instance = this;  // Initialiser l'instance statique dans le constructeur
            
            // Abonnement aux événements de boutons avec les méthodes de la classe ASTI
            but_ASTI.Click += (sender, e) => ASTI.but_ASTI_Click(this);

            but_ASTI_Browse_Template.Click += (sender, e) => ASTI.but_ASTI_Browse_Template_Click(this);
            but_ASTI_Open_templateFolder.Click += (sender, e) => ASTI.but_ASTI_Open_templateFolder_Click(this);

            but_ASTI_Browse_MissionFile.Click += (sender, e) => ASTI.but_ASTI_Browse_Mission_Click(this);
            
            but_ASTI_Process.Click += (sender, e) => ASTI.but_ASTI_Process_Click(this);

            but_GPS_LL.Click += (sender, e) => ASTI.GPS_LL_Click(this);



            tabControl.Selected += new TabControlEventHandler(TabControl1_Selected);
            CampaignTab.Selected += new TabControlEventHandler(CampaignTab_Selected);

            // Abonner l'événement FormClosed à une méthode
            this.FormClosed += new FormClosedEventHandler(Form1_FormClosed);

            //this.tabControl1.SelectedTab = tabPage2;

            VersionDceManager.Text = VersionLongDceManager();

            textBox_id_client.Text = CreateIdClient();
            AjusterLargeurTextBox(textBox_id_client);


            if (ParamServ.Server01Exit)
            {
                comboBox_Server.Items.Add("Server 1");
            }
            if (ParamServ.Server02Exit)
            {
                comboBox_Server.Items.Add("Server 2");
            }
            if (ParamServ.Server02Exit)
            {
                comboBox_Server.Items.Add("Server 3");
            }


            //comboBox_Server.SelectedItem = "Server 2";

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

                            //    // Lire toutes les lignes du fichier
                            //    foreach (var line in File.ReadLines(pathOptionInstaller))
                            //{
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

                        if (entry.Key == "ServerNickNameSelected")
                            ParamServ.ServerNickNameSelected = entry.Value;

                        if (entry.Key == "PathDevUpgrade")
                            txtBoxFolderCreateUpdate.Text = entry.Value;

                        if (entry.Key == "PathDevFileUpgrade")
                            textBoxCreateFileUpdate.Text = entry.Value;

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

                    //string txt = "";
                    //foreach (var entry in ParamConf.configMap)
                    //{
                    //    txt = txt + entry.Key + " = " + entry.Value + "\r\n";
                    //}
                    //MessageBox.Show(txt, "ParamConf.configMap");


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

            ParamManager.NbLancement++;

            //EnvoiStats();

            _ = EnvoiStatsAsync();


            if (ParamServ.ServerNickNameSelected == ParamServ.ServerNickName01 && ParamServ.Server01Exit)
            {
                ParamServ.ServerSelected = ParamServ.FileServerName01;
                ParamServ.fileTypeServer = ParamServ.fileTypeServer01;
                ParamServ.FileServDgUpgradeTXT = ParamServ.FileServDgUpgradeTXT01;
                comboBox_Server.SelectedItem = "Server 1";
            }
            //ServerNickName02 = "GoogleDrive";
            else if (ParamServ.ServerNickNameSelected == ParamServ.ServerNickName02 && ParamServ.Server02Exit)
            //if (ParamServ.ServerNickNameSelected == ParamServ.ServerNickName02)
            {
                ParamServ.ServerSelected = ParamServ.FileServerName02;
                ParamServ.fileTypeServer = ParamServ.fileTypeServer02;
                ParamServ.FileServDgUpgradeTXT = ParamServ.FileServDgUpgradeTXT02;
                comboBox_Server.SelectedItem = "Server 2";
            }
            else if (ParamServ.ServerNickNameSelected == ParamServ.ServerNickName03 && ParamServ.Server03Exit)
            {
                ParamServ.ServerSelected = ParamServ.FileServerName03;
                ParamServ.fileTypeServer = ParamServ.fileTypeServer03;
                ParamServ.FileServDgUpgradeTXT = ParamServ.FileServDgUpgradeTXT03;
                comboBox_Server.SelectedItem = "Server 3";
            }
            else
            {
                ParamServ.ServerSelected = ParamServ.FileServerName02;
                ParamServ.fileTypeServer = ParamServ.fileTypeServer02;
                ParamServ.FileServDgUpgradeTXT = ParamServ.FileServDgUpgradeTXT02;
                comboBox_Server.SelectedItem = "Server 2";
            }

            


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

            bool DownloadRequis = true;

            string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
            bool pathManagerExists = System.IO.Directory.Exists(pathManager);
            string newsLocFile = "news.lua";

            bool newsLocFileExists = File.Exists(pathManager + newsLocFile);
            if (newsLocFileExists)
            {
                //DateTime fInfo = DateTime.Now;
                FileInfo fInfo = new FileInfo(pathManager + newsLocFile);
                int size = unchecked((int)fInfo.Length);                                    //taille en octets 
                //if (size < 10 | DateTime.Now >= fInfo.LastWriteTime.AddMinutes(1))
                if (size < 10 | DateTime.Now >= fInfo.LastWriteTime.AddDays(1))
                {
                    DownloadRequis = true;
                }
                else
                {
                    DownloadRequis = false;
                }
            }

            //https://drive.google.com/uc?export=download&id=
            //https://drive.google.com/file/d/1yjkhowWJbourfAqdSD2V1xqLo8E4E4C1/view?usp=sharing

            string googleLinkThisFile = "1yjkhowWJbourfAqdSD2V1xqLo8E4E4C1";

            if (!newsLocFileExists | DownloadRequis)
            {
                //telecharge le fichier contenant les news
                using (WebClient client = new WebClient())
                {
                    try
                    {
 
                        if (ParamServ.ServerSelected == ParamServ.FileServerName02)
                        {
                            client.DownloadFile(ParamServ.ServerSelected + googleLinkThisFile, pathManager + newsLocFile);
                        }
                        else
                        {
                            client.DownloadFile(ParamServ.ServerSelected + @"\news.lua", pathManager + newsLocFile);
                        }

                        FormUtils.LogRegister("LogRegister 2418 Download news.lua " + "\r\n");
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            //MessageBox.Show("Please select another server, this one is too long.", "Report");
                            client.DownloadFile(ParamServ.FileServerName03 + @"\news.lua", pathManager + newsLocFile);
                        }
                        catch (Exception ex2)
                        {
                            FormUtils.ErrorGeneral_BoxOrLog(ex2, "Failed server:", ParamServ.ServerSelected, false, true);                          
                        }
                        FormUtils.ErrorGeneral_BoxOrLog(ex, "Failed server:", ParamServ.ServerSelected, false, true);

                    }
                    client.Dispose();
                }
            }

            newsLocFileExists = File.Exists(pathManager + newsLocFile);

            if (newsLocFileExists)
            {

                //Affiche la fenetre News POPUP
                string NewsBox0 = "";
                bool newsRegBox0 = false;
                bool textBox_NewsAffiche0 = false;
                string NewsFile0 = ParamManager.pathManager + @"\news.lua";
                bool NewsFilexist0 = File.Exists(NewsFile0);

                string line;
                //System.IO.IOException : 'Le processus ne peut pas accéder au fichier 'D:\_D_Documents\DCE_Manager\news.lua', car il est en cours d'utilisation par un autre processus.'
                StreamReader sr = new StreamReader(NewsFile0);

                while ((line = sr.ReadLine()) != null)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(line, "versionNews"))
                    {
                        if (newsRegBox0 & !System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
                        {
                            NewsBox0 = NewsBox0 + line + "\r\n";
                        }
                        if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStart"))
                        {
                            newsRegBox0 = true;
                        }
                        else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
                        {
                            newsRegBox0 = false;
                        }
                        //else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsAffiche"))
                        //{
                        //    string[] words = line.Split('=');
                        //    if (words[1].Contains("true"))
                        //        textBox_NewsAffiche0 = true;
                        //}
                        else if (newsRegBox0 == false)
                        {
                            //textBox_News.Text = textBox_News.Text + line + "\r\n";
                        }
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(line, "versionNews"))
                    {
                        string[] words = line.Split('=');
                        words[1] = words[1].Replace("\"", "");
                        words[1] = words[1].Replace(" ", "");
                        string v1_newsLua = words[1];
                        string v2_optionsTxt = DceNews.LastNewsVersion;
                        //v1>v2?
                        bool resultVersion = FormUtils.CompareVersion(v1_newsLua, v2_optionsTxt);

                        //regarde si la version du fichier News est supérieur à LastNewsVersion
                        if (resultVersion)
                        {
                            textBox_NewsAffiche0 = true;
                            DceNews.LastNewsVersion = v1_newsLua;
                            
                        }
                    }
                }

                sr.Close();


                if (textBox_NewsAffiche0)
                {
                    //MessageBox.Show(NewsBox0, "News");

                    tabPage5.Text = "News (1)";
                    //tabPage5.Refresh();

                    FormUtils.ModifierLigneBis(NewsFile0, "lastNewsAffiche=true", "lastNewsAffiche=false");

                    FormUtils.ModifierLigneBis(NewsFile0, "lastNewsAffiche = true", "lastNewsAffiche = false");

                }
            }

            //Affiche le taB News
            string NewsBox = "";
            bool newsRegBox = false;
            //bool textBox_NewsAffiche = false;
            string NewsFile = ParamManager.pathManager + @"\news.lua";
            bool NewsFilexist = File.Exists(NewsFile);

            if (NewsFilexist)
            {
                string line;
                StreamReader sr = new StreamReader(NewsFile);

                panel_News.Controls.Clear(); // Nettoyer les anciens contrôles du panel
                panel_News.AutoScroll = true; // Activer le défilement si nécessaire

                int yPos = 0; // Position de départ pour les contrôles dans le panel

                while ((line = sr.ReadLine()) != null)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(line, "versionNews"))
                    {
                        if (newsRegBox & !System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
                        {
                            NewsBox = NewsBox + line + "\r\n";
                        }
                        if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStart"))
                        {
                            newsRegBox = true;
                        }
                        else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
                        {
                            newsRegBox = false;
                        }
                        else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsAffiche"))
                        {
                            //string[] words = line.Split('=');
                            //if (words[1].Contains("true"))
                            //textBox_NewsAffiche = true;
                        }
                        else if (newsRegBox == false)
                        {
                            if (System.Text.RegularExpressions.Regex.IsMatch(line, "="))
                            {
                                string[] words = line.Split('=');
                                string txtLink = words[0];
                                string linkFull = words[1];

                                // Créer et configurer le LinkLabel
                                LinkLabel linkLabel = new LinkLabel();
                                linkLabel.Text = txtLink;
                                linkLabel.LinkArea = new LinkArea(0, txtLink.Length); // Rendre tout le texte cliquable
                                linkLabel.AutoSize = true;
                                linkLabel.Location = new Point(0, yPos); // Définir l'emplacement dans le panel
                                linkLabel.LinkClicked += (sender, e) => System.Diagnostics.Process.Start(linkFull);

                                // Ajouter le LinkLabel au panel
                                panel_News.Controls.Add(linkLabel);
                                yPos += linkLabel.Height + 5; // Mettre à jour la position y pour le prochain contrôle
                            }
                            else
                            {
                                Label textLabel = new Label();
                                textLabel.Text = line;
                                textLabel.AutoSize = true;
                                textLabel.Location = new Point(0, yPos);
                                panel_News.Controls.Add(textLabel);
                                yPos += textLabel.Height + 5; // Mettre à jour la position y pour le prochain contrôle
                            }
                        }
                    }
                }

                sr.Close();

                //string line;
                //StreamReader sr = new StreamReader(NewsFile);

                //while ((line = sr.ReadLine()) != null)
                //{
                //    if (!System.Text.RegularExpressions.Regex.IsMatch(line, "versionNews"))
                //    {
                //        if (newsRegBox & !System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
                //        {
                //            NewsBox = NewsBox + line + "\r\n";
                //        }
                //        if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStart"))
                //        {
                //            newsRegBox = true;
                //        }
                //        else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsStop"))
                //        {
                //            newsRegBox = false;
                //        }
                //        else if (System.Text.RegularExpressions.Regex.IsMatch(line, "lastNewsAffiche"))
                //        {
                //            //string[] words = line.Split('=');
                //            //if (words[1].Contains("true"))
                //            //textBox_NewsAffiche = true;
                //        }
                //        else if (newsRegBox == false)
                //        {
                //            if (System.Text.RegularExpressions.Regex.IsMatch(line, "="))
                //            {
                //                string[] words = line.Split('=');
                //                string txtLink = words[0];
                //                string linkFull = words[1];

                //                // Créer et configurer le LinkLabel
                //                LinkLabel linkLabel = new LinkLabel();
                //                linkLabel.Text = txtLink;
                //                linkLabel.LinkArea = new LinkArea(0, 10); // Rendre tout le texte cliquable
                //                linkLabel.AutoSize = true;
                //                //linkLabel.Location = new Point(10, 10); // Définir l'emplacement sur le formulaire
                //                linkLabel.LinkClicked += new LinkLabelLinkClickedEventHandler(FormUtils.LinkLabel_LinkClicked);

                //                // Ajouter le LinkLabel au formulaire
                //                this.Controls.Add(linkLabel);



                //                textBox_News.Text = textBox_News.Text + txtLink + "\r\n";
                //            }
                //            else
                //            {
                //                textBox_News.Text = textBox_News.Text + line + "\r\n";
                //            }

                //        }
                //    }
                //}
                //sr.Close();
            }
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

        void client_DownloadFileCompleted(object se, System.ComponentModel.AsyncCompletedEventArgs ea)
        {
            string contentState = "DownLoad Success!!!";
            
            if (ea.Error != null)
            {
                contentState = ea.Error.Message;
            }
            else
            {
                CheckVersionScriptsModLocal();
            }
        }

        //presence upgrade.txt, sinon, il le telecharge
        public void CheckPresenceUpgadeTxtFile()
        {
            string upgradelocFile = "upgrade.txt";
            string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
            bool pathManagerExists = System.IO.Directory.Exists(pathManager);
            if (!pathManagerExists)
                try
                {
                    System.IO.Directory.CreateDirectory(pathManager);
                }
                catch (Exception ex)
                {
                    //MessageBox.Show(e.ToString(), "The process failed");
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "CheckPresenceUpgadeTxtFile", pathManager, true, true);
                }
            

            bool RequisOK = false;
            bool fileUpdateExists = File.Exists(pathManager + upgradelocFile);
            FileInfo upgradeInitFileInfo = new FileInfo(pathManager + upgradelocFile);

            if (fileUpdateExists && !FormUtils.IsFileLocked(upgradeInitFileInfo))
            {
                FileInfo fInfo = new FileInfo(pathManager + upgradelocFile);
                //int size = fInfo.Length;//taille en octets 
                //MessageBox.Show(fInfo.Length.ToString(), "Info Taille, ce n'est pas la taille qui compte...");
                if (fInfo.Length < 5000)
                {
                    //TODO regarder les droits avant de tenter une suppression
                    File.Delete(pathManager + upgradelocFile);
                    fileUpdateExists = false;
                }
            }

            if (fileUpdateExists)
            {
                //DateTime fInfo = DateTime.Now;
                FileInfo fInfo = new FileInfo(pathManager + upgradelocFile);
                int size = unchecked((int)fInfo.Length);                                    //taille en octets  
                                                                                            //if (size < 10 | DateTime.Now >= fInfo.LastWriteTime.AddMinutes(2))      //debug
                                                                                            //if (size < 10 | DateTime.Now >= fInfo.LastWriteTime.AddMinutes(60))
                if (size < 10 | DateTime.Now >= fInfo.LastWriteTime.AddDays(1))
                {
                    //FormUtils.LogRegister("LogRegister 313 Check RequisOK size: "
                    //        + size.ToString() + " Now? >= "
                    //        + DateTime.Now.ToString()
                    //        + fInfo.LastWriteTime.ToString() + " AddDays(1)? "
                    //        + fInfo.LastWriteTime.AddDays(1));
                    RequisOK = true;
                }
            }
            
            if (!fileUpdateExists | RequisOK)
            {
                //telecharge le fichier contenant les mises à jour possible
                using (WebClient client = new WebClient())
                {
                    try
                    {
                        //client.DownloadFile(ParamServ.FileServDgUpgradeTXT, pathManager + upgradelocFile);

                        //client.DownloadProgressChanged += new DownloadProgressChangedEventHandler(client_DownloadProgressChanged);
                        client.DownloadFileCompleted += new System.ComponentModel.AsyncCompletedEventHandler(client_DownloadFileCompleted);
                        client.DownloadFileAsync(new Uri(ParamServ.FileServDgUpgradeTXT), pathManager + upgradelocFile);

                        FormUtils.LogRegister("Download upgrade.txt ");

                    }
                    catch (Exception ex)
                    {                   

                        //string errorDetails = $"Error: {ex.Message}, StackTrace: {ex.StackTrace}, FileServDgUpgradeTXT: {ParamServ.FileServDgUpgradeTXT}";
                        //FormUtils.LogRegister("Error in LogRegister: " + errorDetails);

                        //MessageBox.Show("Failed to download upgrade.txt ", "Report");
                        //MessageBox.Show("Failed to download upgrade.txt " +"\r\n" +
                        //                errorDetails, "Error");

                        FormUtils.ErrorGeneral_BoxOrLog(ex, "Failed to download upgrade.txt", ParamServ.FileServDgUpgradeTXT, true, true);

                        return;
                    }
                    client.Dispose();
                }
            }
            //else
            //{
                CheckVersionScriptsModLocal();
            //}
        }

        public void CheckVersionScriptsModLocal()
        {
            string upgradelocFile = "upgrade.txt";
            string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";


            if (string.IsNullOrWhiteSpace(pathManager))
            {
                #if DEBUG
                    throw new ArgumentException("Le chemin pathManager est NULL ou vide !");
                #else
                  FormUtils.LogRegister("Le chemin pathManager est NULL ou vide " + pathManager);
                #endif
            }

            if (string.IsNullOrWhiteSpace(upgradelocFile))
            {
                #if DEBUG
                    throw new ArgumentException("Le nom du fichier upgradelocFile est NULL ou vide !");
                #else
                     FormUtils.LogRegister("Le nom du fichier upgradelocFile est NULL ou vide " + upgradelocFile);
                #endif
            }



            bool pathManagerExists = System.IO.Directory.Exists(pathManager);

            //string str = "1.1.1";
            //Version DceManagerVer = new Version(str);

            Version DceManagerVer = Assembly.GetExecutingAssembly().GetName().Version;
            String.Format("{0}.{1}.{2}", DceManagerVer.Major, DceManagerVer.Minor, DceManagerVer.Build);

            int AvailableMajor = 0;
            int AvailableMinor = 0;
            int AvailableBuild = 0;

            int FileServer_major = 0;
            int FileServer_minor = 0;
            int FileServer_build = 0;

            string pathManagerPlusFile = pathManager + upgradelocFile;


            if (pathManagerPlusFile.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
#if DEBUG
                     throw new ArgumentException($"Le chemin pathManager contient des caractères interdits : {pathManagerPlusFile}");
#else
                   FormUtils.LogRegister($"Le chemin pathManagerPlusFile contient des caractères interdits : {pathManagerPlusFile}");
#endif
            }


            bool upgradeFileExists = File.Exists(pathManagerPlusFile);

            FileInfo upgradeFileInfo = new FileInfo(pathManagerPlusFile);

            bool upToDate = true;
            bool DcemanagUpToDate = true;

            // parse le fichier upgrade pour connaitre la version DCE
            //if (upgradeFileExists && !FormUtils.IsFileLocked(upgradeFileInfo))
            if (upgradeFileExists)
            {
                try
                {
                    // Utiliser un FileStream avec FileShare.Read pour permettre à d'autres processus de lire le fichier
                    using (FileStream fs = new FileStream(pathManagerPlusFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (StreamReader sr = new StreamReader(fs))
                    {

                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            string lineClean = line.Replace(" ", "");
                            if (!String.IsNullOrEmpty(lineClean) && lineClean.Substring(0, 2) != "--")                                                  //ne prend pas en compte les lignes commençant par --
                            {
                                //DEVversionDCE.ScriptsMod = "20.44.71"
                                if (lineClean.Contains("versionDCE.ScriptsMod="))
                                {
                                    //recherche la version de DCE
                                    lineClean = lineClean.Replace("\"", "");
                                    lineClean = lineClean.Replace("versionDCE.ScriptsMod=", "");

                                    string[] words = lineClean.Split('.');
                                    AvailableMajor = Int32.Parse(words[0]);
                                    AvailableMinor = Int32.Parse(words[1]);
                                    AvailableBuild = Int32.Parse(words[2]);
                                }
                                else if (lineClean.Contains("versionManager="))
                                {
                                    //recherche la version de DCE
                                    lineClean = lineClean.Replace(" ", "");
                                    lineClean = lineClean.Replace("versionManager=", "");
                                    lineClean = lineClean.Replace("\"", "");

                                    string[] words = lineClean.Split('.');
                                    FileServer_major = Int32.Parse(words[0]);
                                    FileServer_minor = Int32.Parse(words[1]);
                                    FileServer_build = Int32.Parse(words[2]);
                                }
                            }
                        }
                        ScriptsModAvailableVersion.Text = AvailableMajor + "." + AvailableMinor + "." + AvailableBuild;
                        ScriptsModAvailableVersion.Update();
                        DceManagerAvailableVersion.Text = FileServer_major + "." + FileServer_minor + "." + FileServer_build;
                        DceManagerAvailableVersion.Update();
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "CheckVersionScriptsModLocal", pathManager + upgradelocFile, false, true);
                }
            }

            string folderLoc ;
            string TypeClient = "NG";

            folderLoc = textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod." + TypeClient + @"\";

            if (string.IsNullOrWhiteSpace(folderLoc))
            {
#if DEBUG
                    throw new ArgumentException("Le chemin folderLoc est NULL ou vide !");
#else
                  FormUtils.LogRegister("Le chemin folderLoc est NULL ou vide " + folderLoc);
#endif
            }

             bool folderLocExists = System.IO.Directory.Exists(folderLoc);


            if (!folderLocExists)
            {
                //System.IO.Directory.CreateDirectory(folderLoc);
                try
                {
                    System.IO.Directory.CreateDirectory(folderLoc);
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "The process failed", pathManager + upgradelocFile, true, true);
                }
            }

            ////test la presence du dossier "db_loadouts"
            //if (!System.IO.Directory.Exists(folderLoc + @"\db_loadouts")) 
            //{
            //    try
            //    {
            //        System.IO.Directory.CreateDirectory(folderLoc + @"\db_loadouts");
            //    }
            //    catch (Exception ex)
            //    {
            //        FormUtils.ErrorGeneral_BoxOrLog(ex, "The process failed", pathManager + upgradelocFile, true, true);
            //    }
            //}

            //test la presence du dossier "Mission Scripts"
            if (!System.IO.Directory.Exists(folderLoc + @"\Mission Scripts"))
            {
               try
                {
                    System.IO.Directory.CreateDirectory(folderLoc + @"\Mission Scripts");
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "The process failed", pathManager + upgradelocFile, true, true);
                }
            }


            //compare les versions de tous les fichiers
            if (upgradeFileExists && !FormUtils.IsFileLocked(upgradeFileInfo))
            {

                int nbFileToUpdat = 0;
                ParamDownload.NbFileOutToDate = 0;

                try
                {
                    // Utiliser un FileStream avec FileShare.Read pour permettre à d'autres processus de lire le fichier
                    using (FileStream fs = new FileStream(pathManager + upgradelocFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {

                            //string lineClean = line.Replace(" ", "");
                            string lineClean = line;
                            if (!String.IsNullOrEmpty(lineClean) && lineClean.Substring(0, 2) != "--")                                                              //ne prend pas en compte les lignes commençant par --
                            {

                                //Ne traite que les fichiers versionDCE["ATO_FlightPlan.lua"] = "1.38.107"
                                //DEVversionDCE["ATO_FlightPlan.lua"] = "1.38.107" = 1HOjcQ1eBvYLIm114wt-Ojx_mCJ7prrds

                                string fileName = "";
                                string FileRefDate = "";
                                int[] FileRefVersion = new int[3];

                                if (lineClean.Contains("versionDCE[\""))
                                {
                                    //versionDCE.ScriptsMod = "20.43.70"
                                    //versionDCE["ATO_FlightPlan.lua"] = "1.38.107"
                                    //recherche les fichiers à mettre à jour

                                    lineClean = lineClean.Replace("https://drive.google.com/file/d/", "");
                                    //lineClean = lineClean.Replace("/view?usp=sharing", "");

                                    if (lineClean.IndexOf("/view?") > -1)
                                    {
                                        string[] partView = lineClean.Split('/');
                                        lineClean = partView[0];
                                    }

                                    lineClean = lineClean.Replace("\"", "");
                                    string[] part = lineClean.Split('=');
                                    part[1] = part[1].TrimEnd();
                                    part[1] = part[1].TrimStart();

                                    if (part.Length >= 3)
                                    {
                                        part[2] = part[2].TrimEnd();
                                        part[2] = part[2].TrimStart();
                                    }


                                    string[] pre = part[0].Split('[');
                                    string[] post = pre[1].Split(']');
                                    fileName = post[0];
                                    fileName = fileName.TrimEnd();
                                    fileName = fileName.TrimStart();

                                    //remet le caractere \ a la place du | pour tous
                                    fileName = fileName.Replace("|", "\\");

                                    //remet l'espace du path "Mission Scripts" préalablement supprimé
                                    if (fileName.Contains("MissionScripts"))
                                    {
                                        fileName = fileName.Replace("MissionScripts", "Mission Scripts");
                                        //remet le caractere \ a la place du | pour les  "Mission Scripts" 
                                        //fileName = fileName.Replace("|", "\\");
                                    }
                                    ////remet l'espace du path "Mission Scripts" préalablement supprimé
                                    //if (fileName.Contains("db_loadouts"))
                                    //{
                                    //    //remet le caractere \ a la place du | pour les  "db_loadouts" 
                                    //    fileName = fileName.Replace("|", "\\");
                                    //}
                                    //else if (fileName.Contains("Mission Scripts"))
                                    //{
                                    //    //remet le caractere \ a la place du | pour les  "Mission Scripts" 
                                    //    fileName = fileName.Replace("|", "\\");
                                    //}

                                    if (part[1].Contains("."))
                                    {
                                        string[] tempRefVersion = part[1].Split('.');

                                        FileRefVersion[0] = Int32.Parse(tempRefVersion[0]);
                                        FileRefVersion[1] = Int32.Parse(tempRefVersion[1]);
                                        FileRefVersion[2] = Int32.Parse(tempRefVersion[2]);

                                    }
                                    else if (part[1].Contains(":") & part[1].Contains("/"))
                                    {
                                        //31/03/2021 09:37:31
                                        //versionDCE["Mission Scripts\beacon.oog"] = "24/04/2022 12:34:45" = https://drive.google.com/file/d/1lPkuy5u8eTqtw2gcaQYmUfH_RKuhTphC/view?usp=sharing

                                        FileRefDate = part[1];
                                    }
                                }

                                if (!String.IsNullOrEmpty(fileName))
                                {

                                    bool fichierSain = true;
                                    bool fileLocExists = File.Exists(folderLoc + fileName);
                                    string ext = "";

                                    int FileLoc_major = 0;
                                    int FileLoc_minor = 0;
                                    int FileLoc_build = 0;

                                    Boolean fileLocDateNeedUpdate = false;
                                    Boolean isLuaTxtFile = false;

                                    //FormUtils.LogRegister("DEBUG filename raw |" + fileName + "|");

                                    //foreach (char c in fileName)
                                    //{
                                    //    if (Path.GetInvalidFileNameChars().Contains(c))
                                    //        FormUtils.LogRegister("INVALID CHAR : " + (int)c);
                                    //}

                                    FileInfo fInfo = new FileInfo(folderLoc + fileName);
                                    //int size = fInfo.Length;//taille en octets 

                                    if (fileLocExists)
                                    {
                                        if (fInfo.Length < 1)
                                            fichierSain = false;

                                        ext = Path.GetExtension(folderLoc + fileName);

                                    }

                                    if (ext == ".lua" | ext == ".txt")
                                    {
                                        isLuaTxtFile = true;

                                        if (fileLocExists && fichierSain)
                                        {
                                            string FileToRead = folderLoc + fileName;
                                            using (StreamReader ReaderObject = new StreamReader(FileToRead))
                                            {

                                                string Line;
                                                while ((Line = ReaderObject.ReadLine()) != null)
                                                {
                                                    Line = Line.Replace(" ", "");
                                                    //versionDCE.ATO_FlightPlan = "1.38.107"
                                                    if (Line.Contains("versionDCE["))
                                                    {
                                                        //placer ici le code pour virer le commentaire : --48CEF tout ça
                                                        if (Line.IndexOf("--") > -1)
                                                        {
                                                            Line = Line.Substring(0, Line.IndexOf("--"));
                                                        }

                                                        Line = Line.Replace("\"", "");
                                                        string[] part = Line.Split('=');
                                                        string[] tempVersion = part[1].Split('.');

                                                        FileLoc_major = Int32.Parse(tempVersion[0]);
                                                        FileLoc_minor = Int32.Parse(tempVersion[1]);
                                                        FileLoc_build = Int32.Parse(tempVersion[2]);

                                                        //FormUtils.LogRegister("LogRegister 1351 folderLoc, |"+ fileToRead + "| " + FileLoc_major + "| " + FileLoc_minor + "| " + FileLoc_build + "| ");


                                                        break;
                                                    }
                                                    //else
                                                    //{
                                                    //    ExistVersion = false;
                                                    //}
                                                }
                                            }
                                        }

                                    }
                                    else
                                    //autres fichiers de type ogg png
                                    {
                                        DateTime dtFileLoc = File.GetLastWriteTime(folderLoc + fileName);

                                        if (FileRefDate != "")
                                        {
                                            //FormUtils.LogRegister("fileUpdateArray[nbFileToUpdat, 5]?  |" + fileUpdateArray[nbFileToUpdat, 5].ToString() + "| ");

                                            //31/03/2021 09:37:31
                                            DateTime dt_RefFile = DateTime.ParseExact(FileRefDate, "dd/MM/yyyy HH:mm:ss",
                                                System.Globalization.CultureInfo.InvariantCulture);

                                            if (dt_RefFile > dtFileLoc)
                                            {
                                                fileLocDateNeedUpdate = true;
                                            }
                                            else
                                            {
                                                fileLocDateNeedUpdate = false;
                                            }
                                        }
                                    }


                                    if (FileRefVersion[0] > FileLoc_major || FileRefVersion[1] > FileLoc_minor || FileRefVersion[2] > FileLoc_build || (!isLuaTxtFile && fileLocDateNeedUpdate) || !fileLocExists)
                                    {

                                        FormUtils.LogRegister("LogRegister 1394 this file is not up to date, or is missing  |" + fileName + "| ");

                                        nbFileToUpdat++;
                                    }

                                    //if (fileDate)
                                    //{

                                    //}

                                }
                                //else
                                //{
                                //    FormUtils.LogRegister("LogRegister 1479 this file is not up to date, or is missing  |" + fileName + "| ");

                                //    nbFileToUpdat++;
                                //}

                                //**********************************
                                //check la version de DCE_Manager
                                //*********************************

                                //versionManager = "3.7.5"
                                //versionManagerGoogleDrive = 1gn-wgHxxh8i-ke7LpUsu65x9aF5RtPBA
                                //if (lineClean.Contains("versionDCE[\""))
                                if (lineClean.Contains("versionManager") && !lineClean.Contains("versionManagerGoogleDrive"))
                                {
                                    //recherche la version de DCE
                                    lineClean = lineClean.Replace(" ", "");
                                    lineClean = lineClean.Replace("versionManager=", "");
                                    lineClean = lineClean.Replace("\"", "");

                                    string[] words = lineClean.Split('.');
                                    FileServer_major = Int32.Parse(words[0]);
                                    FileServer_minor = Int32.Parse(words[1]);
                                    FileServer_build = Int32.Parse(words[2]);

                                    if ((FileServer_major > DceManagerVer.Major) | (FileServer_minor > DceManagerVer.Minor) | (FileServer_build > DceManagerVer.Build))
                                    {
                                        FormUtils.LogRegister("LogRegister 1426 DCE_Manager is not up to date,  |" + DceManagerVer.Major + "| " + DceManagerVer.Minor + "| " + DceManagerVer.Build + "| ");


                                        DcemanagUpToDate = false;
                                    }
                                }
                            }
                        }
                    }

                    if (nbFileToUpdat > 0)
                    {
                        FormUtils.LogRegister("LogRegister 1438 Number of out-of-date files:   |" + nbFileToUpdat + "| ");
                        upToDate = false;
                        ParamDownload.NbFileOutToDate = nbFileToUpdat;
                    }
                    if (DcemanagUpToDate == false)
                    {
                        ParamDownload.NbFileOutToDate = ParamDownload.NbFileOutToDate + 1;
                    }
                }

                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "CheckVersionScriptsModLocal", pathManager + upgradelocFile, false, true);
                }
            }

            int major = 0;
            int minor = 0;
            int build = 0;
            //versionDCE["UTIL_Changelog.lua"] = "20.58.205"

            if (File.Exists((textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua")))
            {
                using (StreamReader reader = new StreamReader(textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua"))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {

                        string lineClean = line.Replace(" ", "");
                        if (!String.IsNullOrEmpty(lineClean))              //&& lineClean.Substring(0, 2) != "--"                                                 //ne prend pas en compte les lignes commençant par --
                        {
                            //versionDCE["UTIL_Changelog.lua"] = "20.58.205"
                            if (lineClean.Contains("versionDCE["))
                            {
                                //recherche la version de DCE
                                lineClean = lineClean.Replace("\"", "");
                                if (lineClean.Contains("versionDCE[UTIL_Changelog.txt"))
                                    lineClean = lineClean.Replace("versionDCE[UTIL_Changelog.txt]=", "");

                                if (lineClean.Contains("versionDCE[UTIL_Changelog.lua"))
                                    lineClean = lineClean.Replace("versionDCE[UTIL_Changelog.lua]=", "");

                                string[] words = lineClean.Split('.');
                                major = Int32.Parse(words[0]);
                                minor = Int32.Parse(words[1]);
                                build = Int32.Parse(words[2]);
                            }
                        }
                    }
                    reader.Close();
                }
            }

            string tempLocVersion = major.ToString() + "." + minor.ToString() + "." + build.ToString();

            ParamScriptsMod.verScriptsMod = tempLocVersion;
            
            if (!upToDate)
            {
                ScriptModInstalledVersion.Font = new Font(ScriptModInstalledVersion.Font, FontStyle.Strikeout);
            }
            else
            {
                ScriptModInstalledVersion.Font = new Font(ScriptModInstalledVersion.Font, FontStyle.Regular);
            }

            ScriptModInstalledVersion.Text = tempLocVersion;

            //compare si la version est supérieur pour afficher/ou non/ le boutton download
            //ou si des fichiers ne sont pas à jour
            if ((AvailableMajor > major) | (AvailableMinor > minor) | (AvailableBuild > build) | !upToDate | !DcemanagUpToDate)
            {
                if(!upToDate)
                {
                    ScriptsModUpdateButton.Visible = true;
                }
                if (!DcemanagUpToDate)
                {
                    button_UpdateManager.Visible = true;
                }
                tabPage3.Text = "Update " + ParamDownload.NbFileOutToDate.ToString() + " File(s)";
                tabPage3.Refresh();
            }
            else
            {
                ScriptsModUpdateButton.Visible = false;
                tabPage3.Text = "Update";
                tabPage3.Refresh();
            }

            DceManagerInstalledVersion.Text = VersionDceManager.Text;
            //ParamManager.verDceManager = VersionDceManager.Text;

        }//CheckVersionScriptsModLocal


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

            using (var archive = ZipFile.Open(sourceZipFilePath, ZipArchiveMode.Read))
            {
                string destinationRoot = Path.Combine(textBox_SavedGames.Text, @"Mods\tech\DCE\Missions\Campaigns");
                Directory.CreateDirectory(destinationRoot); // S'assurer que le dossier racine existe

                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    string DestFullName = file.FullName; // Chemin dans le fichier zip (y compris les sous-dossiers)
                    string[] words = DestFullName.Split('/');
                    string lowerWordZero = words[0].ToLowerInvariant();

                    bool extractAutorise = true;

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


        public void ExtractZipFileToDirectoryLight_OLD(string sourceZipFilePath, bool overwrite)
        {
            MessageBox.Show("ExtractZipFileTodirectory LIGHT)");
            using (var archive = ZipFile.Open(sourceZipFilePath, ZipArchiveMode.Read))
            {
                string destinationDirectoryName = "";
                string DestFullName = "";

                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    DestFullName = file.FullName;

                    string[] words = DestFullName.Split('/');
                    string LowerWordZero = words[0].ToLowerInvariant();

                    bool extractAutorise = true;

                    if (words.Length <= 2 && (Path.GetExtension(words[0]) == ".pdf" || Path.GetExtension(words[0]) == ".exe" || Path.GetExtension(words[0]) == ".txt"))
                    {
                        //MessageBox.Show("Passe" + DestFullName, file.Name);
                        continue;
                    }


                    DestFullName = DestFullName.Replace(words[0] + "/", "");
                    destinationDirectoryName = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\"+ ParamCampaign.NameCampaign + @"\";

                    //regarde s'il est interdit d'écraser le dossier Active
                    if (checkBoxActiveFolder.Checked == true)
                    {
                        foreach (var word in words)
                        {
                            if (System.Text.RegularExpressions.Regex.IsMatch(word, "Active"))
                            {
                                extractAutorise = false;
                            }
                            if (System.Text.RegularExpressions.Regex.IsMatch(word, "Debug"))
                            {
                                extractAutorise = false;
                            }
                        }
                    }


                    DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
                    string destinationDirectoryFullPath = di.FullName;

                    string completeFileName = Path.GetFullPath(Path.Combine(destinationDirectoryFullPath, DestFullName));

                    if (!completeFileName.StartsWith(destinationDirectoryFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Trying to extract file outside of destination directory. See this link for more info: https://snyk.io/research/zip-slip-vulnerability");
                    }

                    if (file.Name == "")
                    {// Assuming Empty for Directory
                        //Directory.CreateDirectory(Path.GetDirectoryName(completeFileName));
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(completeFileName));
                        }
                        catch (Exception ex)
                        {
                            FormUtils.ErrorGeneral_BoxOrLog(ex, "CheckVersionScriptsModLocal", completeFileName, true, true);
                        }
                        continue;
                    }

                    if (extractAutorise)
                    {
                        file.ExtractToFile(completeFileName, true);
                    }

                }
            }
        }

        public static void UpdateProperty(object obj, string propertyName, string key, object value)
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

        //// Source: https://prograide.com/pregunta/76591/comparez-les-numeros-de-version-sans-utiliser-la-fonction-de-division
        //static bool CompareVersion(string v1, string v2)
        //{
        //    //string v1 = "1.23.56.1487";
        //    //string v2 = "1.24.55.487";
        //    var version1 = new Version(v1);
        //    var version2 = new Version(v2);
        //    var result = version1.CompareTo(version2);

        //    if (result > 0)
        //        return true;
        //    else if (result <= 0)
        //        return false;
        //    else
        //        return false;
        //}


        public void UpdateScriptsMod()
        {
            string tempLog = "";                                                                                            //creation du fichier log
            string upgradelocFile = "upgrade.txt";
            bool succeed = true;

            if (String.IsNullOrEmpty(textBox_SavedGames.Text))
            {
                MessageBox.Show("You must enter the path to the SavedGame folder in the \"Install\" tab ", "Report");
                return;
            }

            string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
            //string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager02\";
            bool pathManagerExists = System.IO.Directory.Exists(pathManager);
            if (!pathManagerExists)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(pathManager);
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "CheckVersionScriptsModLocal", pathManager, true, true);
                }
            }
                

            string fileServerName = "";
            string fileLocName = "";

            if (!File.Exists(pathManager + upgradelocFile))
            {

                MessageBox.Show("the upgrade.txt file cannot be found. ", "Report");
                return;

            }


            // parse le fichier upgrade pour connaitre la version DCE
            //string pathUpgradeFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\";
            //bool exists = System.IO.Directory.Exists(pathUpgradeFile);
            bool upgradeFileExists = File.Exists(pathManager + upgradelocFile);
            bool ExistVersion = false;

            string major = "";
            string minor = "";
            string build = "";
            string fileName = "";
            string TypeClient = "";

            int FileServer_major = 0;
            int FileServer_minor = 0;
            int FileServer_build = 0;

            int FileLoc_major = 0;
            int FileLoc_minor = 0;
            int FileLoc_build = 0;

            int nbFileUpdated = 0;
            int nbFileCompleted = 0;
            int nbFileErreur = 0;
            int nbErrorAsyn = 0;

            string[,] fileUpdateArray = new string[150, 6];

            //Display frmAbout as a modal dialog
            DCE_Manager.FormDownload tf = new DCE_Manager.FormDownload();
            tf.Show();

            if (upgradeFileExists)
            {

                int i = 0;

                try
                {
                    // Utiliser un FileStream avec FileShare.Read pour permettre à d'autres processus de lire le fichier
                    using (FileStream fs = new FileStream(pathManager + upgradelocFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {

                            //string lineClean = line.Replace(" ", "");
                            string lineClean = line;

                            if (!String.IsNullOrEmpty(lineClean) && lineClean.Substring(0, 2) != "--")                                                              //ne prend pas en compte les lignes commençant par --
                            {
                                //DEVversionDCE.ScriptsMod = "20.44.71"
                                //DEVversionDCE["ATO_FlightPlan.lua"] = "1.38.107"
                                //DEVversionDCEGoogleDrive["ATO_FlightPlan.lua"] = 1HOjcQ1eBvYLIm114wt-Ojx_mCJ7prrds
                                //versionCampaign["India-Pak War-71 - MiG-19\Init\targetlist_init.lua"] = "1.132.200" = https://drive.google.com/file/d/1Cc69upYFJ1n0GWbdYnoFcSePy6g4Mt-6/view?usp=sharing

                                // ne traite que la version scriptsMod Client
                                //if (LabelStatut.Text == "User" && lineClean.Contains("versionDCE.ScriptsMod=") && !lineClean.Contains("DEVversionDCE"))
                                if (lineClean.Contains("versionDCE.ScriptsMod"))
                                {
                                    //recherche la version de DCE
                                    lineClean = lineClean.Replace("versionDCE.ScriptsMod", "");
                                    lineClean = lineClean.Replace("=", "");
                                    lineClean = lineClean.Replace("\"", "");

                                    lineClean = lineClean.TrimEnd();
                                    lineClean = lineClean.TrimStart();

                                    string[] words = lineClean.Split('.');
                                    major = words[0];
                                    minor = words[1];
                                    build = words[2];
                                    TypeClient = "NG";
                                }

                                //Ne traite que les fichiers 
                                //versionDCE["ATO_FlightPlan.lua"] = "1.38.107" = 1HOjcQ1eBvYLIm114wt-Ojx_mCJ7prrds

                                else if (lineClean.Contains("versionDCE[\""))
                                {
                                    //versionDCE.ScriptsMod = "20.43.70"
                                    //versionDCE["ATO_FlightPlan.lua"] = "1.38.107"
                                    //recherche les fichiers à mettre à jour

                                    lineClean = lineClean.Replace("https://drive.google.com/file/d/", "");
                                    //lineClean = lineClean.Replace("/view?usp=sharing", "");

                                    //ATTENTION aux lignes dessous, elles empechent le telechargement des ogg
                                    //if (lineClean.IndexOf("/view?") > -1)
                                    
                                    if (lineClean.IndexOf("/view?usp") > -1)
                                    {
                                        lineClean = lineClean.Replace("/view?usp", "");
                                    }

                                    lineClean = lineClean.Replace("\"", "");
                                    string[] part = lineClean.Split('=');

                                    //part1: "1.38.107"
                                    part[1] = part[1].TrimEnd();
                                    part[1] = part[1].TrimStart();

                                    if (part.Length >= 3)
                                    {
                                        //part2  1HOjcQ1eBvYLIm114wt-Ojx_mCJ7prrds
                                        part[2] = part[2].TrimEnd();
                                        part[2] = part[2].TrimStart();
                                    }


                                    string[] pre = part[0].Split('[');
                                    string[] post = pre[1].Split(']');
                                    fileName = post[0];
                                    fileName = fileName.TrimEnd();
                                    fileName = fileName.TrimStart();

                                    //remet l'espace du path "Mission Scripts" préalablement supprimé
                                    if (fileName.Contains("MissionScripts"))
                                        fileName = fileName.Replace("MissionScripts", "Mission Scripts");

                                    if (part[1].Contains("."))
                                    {
                                         string[] FileVersion = part[1].Split('.');

                                        if (fileName.Length > 0)
                                            fileUpdateArray[i, 0] = (fileName);

                                        fileUpdateArray[i, 1] = FileVersion[0];
                                        fileUpdateArray[i, 2] = FileVersion[1];
                                        fileUpdateArray[i, 3] = FileVersion[2];
                                    }
                                    else if (part[1].IndexOf(":") > -1 && part[1].IndexOf("/") > -1)
                                    {

                                        //31/03/2021 09:37:31
                                        //versionDCE["Mission Scripts\beacon.oog"] = "24/04/2022 12:34:45" = https://drive.google.com/file/d/1lPkuy5u8eTqtw2gcaQYmUfH_RKuhTphC/view?usp=sharing
                                        if (fileName.Length > 0)
                                            fileUpdateArray[i, 0] = (fileName);

                                        fileUpdateArray[i, 1] = part[1];

                                        fileUpdateArray[i, 5] = part[1];

                                        //FormUtils.LogRegister("LogRegister 1802 fileUpdateArray[nbFileToUpdat, 5] |" + fileName + " " + part[1] + "|");
                                    }

                                    //cherche le lien GoogleDrive apres le 2eme =
                                    if (part.Count() >= 3 && part[2] != null)
                                    {
                                        fileUpdateArray[i, 4] = part[2];
                                    }

                                    i++;
                                }
                            }
                        }
                    
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "UpdateScriptsMod", pathManager + upgradelocFile, false, true);
                }
            }
            else
            {
                MessageBox.Show("Unable to find the file " + "\r\n" + pathManager + upgradelocFile, "Error");
                return;
            }

            string folderLoc = "";

            folderLoc = textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod." + TypeClient + @"\";

            bool folderLocExists = System.IO.Directory.Exists(folderLoc);


            if (!folderLocExists)
            {
                
                try
                {
                    System.IO.Directory.CreateDirectory(folderLoc);
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "CheckVersionScriptsModLocal", folderLoc, true, true);
                }
            }


            ////test la presence du dossier "Mission Scripts"
            //if (!System.IO.Directory.Exists(folderLoc + @"\Mission Scripts")) 
            //{
                
            //    try
            //    {
            //        System.IO.Directory.CreateDirectory(folderLoc + @"\Mission Scripts");
            //    }
            //    catch (Exception ex)
            //    {
            //        FormUtils.ErrorGeneral_BoxOrLog(ex, "CheckVersionScriptsModLocal", folderLoc + @"\Mission Scripts", true, true);
            //    }
            //}

            //telecharge les fichiers à mettre à jour
            if (succeed)
            {

               var urlList = new Dictionary<string, string>();

                //foreach (string fileUpdate in fileUpdateArray)
                for (int i = 0; i < 150; i++)
                {

                    //tf.progressBarA.Increment(nbFileToUpdat);

                    string fileUpdate = fileUpdateArray[i, 0];

                    if (!String.IsNullOrEmpty(fileUpdate))
                    {
                        fileUpdate = fileUpdate.Replace("|", "\\");
                        fileUpdate = fileUpdate.Trim();
                        fileLocName = fileUpdate;
                    }
                    

                    if (!String.IsNullOrEmpty(fileUpdate))
                    {
                        bool fichierSain = true;
                        string pathCombine = Path.Combine(folderLoc, fileUpdate);
                        bool fileUpdateExists = File.Exists(pathCombine);
                        string ext = "";
                        bool downloadAutorise = false;

                        FileInfo fInfo = new FileInfo(pathCombine);
                        //int size = fInfo.Length;//taille en octets 

                        if (fileUpdateExists)
                        {
                            if (fInfo.Length < 1)
                                fichierSain = false;

                            ext = Path.GetExtension(pathCombine);
                            //FormUtils.LogRegister("LogRegister 1899 fileUpdateExists |" + folderLoc + fileUpdate + "|");
                        }
                        else
                        {
                            downloadAutorise = true; // si le fichier n'existe pas en local, on ne peut pas verifier sa version/date, donc on télécharge
                            //FormUtils.LogRegister("LogRegister 1904 NePasExister |" + folderLoc + fileUpdate + "|");
                        }

                        if (fileUpdateExists)
                        {
                            if (ext == ".lua" | ext == ".txt")
                            {
                                if (fileUpdateExists && fichierSain)
                                {
                                    //string fileToRead = folderLoc + fileUpdate;
                                    string fileToRead = Path.Combine(folderLoc, fileUpdate);
                                    using (StreamReader ReaderObject = new StreamReader(fileToRead))
                                    {
                                        try
                                        {
                                            FileServer_major = Int32.Parse(fileUpdateArray[i, 1]);
                                        }
                                        catch (Exception ex)
                                        {
                                            //MessageBox.Show(fileUpdateArray[nbFileToUpdat, 0].ToString(), "version error");
                                            FormUtils.ErrorGeneral_BoxOrLog(ex, "UpdateScriptsMod():  version error", fileToRead, true, true);
                                        }

                                        try
                                        {
                                            FileServer_minor = Int32.Parse(fileUpdateArray[i, 2]);
                                        }
                                        catch (Exception ex)
                                        {
                                            //MessageBox.Show(fileUpdateArray[nbFileToUpdat, 0].ToString(), "version error");
                                            FormUtils.ErrorGeneral_BoxOrLog(ex, "UpdateScriptsMod() : version error", fileToRead, true, true);
                                        }

                                        try
                                        {
                                            FileServer_build = Int32.Parse(fileUpdateArray[i, 3]);
                                        }
                                        catch (Exception ex)
                                        {
                                            //MessageBox.Show(fileUpdateArray[nbFileToUpdat, 0].ToString(), "version error");
                                            FormUtils.ErrorGeneral_BoxOrLog(ex, "UpdateScriptsMod() : version error", fileToRead, true, true);
                                        }


                                        string Line;
                                        // ReaderObject reads a single line, stores it in Line string variable and then displays it on console
                                        while ((Line = ReaderObject.ReadLine()) != null)
                                        {
                                            Line = Line.Replace(" ", "");
                                            //versionDCE.ATO_FlightPlan = "1.38.107"
                                            if (Line.Contains("versionDCE["))
                                            {
                                                //placer ici le code pour virer le commentaire : --48CEF tout ça
                                                if (Line.IndexOf("--") > -1)
                                                {
                                                    Line = Line.Substring(0, Line.IndexOf("--"));
                                                }

                                                Line = Line.Replace("\"", "");
                                                string[] part = Line.Split('=');
                                                string[] FileVersion = part[1].Split('.');

                                                FileLoc_major = Int32.Parse(FileVersion[0]);
                                                FileLoc_minor = Int32.Parse(FileVersion[1]);
                                                FileLoc_build = Int32.Parse(FileVersion[2]);
                                                ExistVersion = true;
                                                break;
                                            }
                                            else
                                            {
                                                ExistVersion = false;
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            //autres fichiers de type ogg png
                            {
                                string pathCombineOtherFile = Path.Combine(folderLoc, fileUpdate);
                                DateTime dtFileLoc = File.GetLastWriteTime(pathCombineOtherFile);

                                if (fileUpdateArray[i, 5] != null)
                                {
                                    //FormUtils.LogRegister("LogRegister + 1983 fileUpdateArray[nbFileToUpdat, 5]?  |" + fileUpdateArray[nbFileToUpdat, 5].ToString() + "| ");

                                    //31/03/2021 09:37:31
                                    DateTime dt_WebFile = DateTime.ParseExact(fileUpdateArray[i, 5], "dd/MM/yyyy HH:mm:ss",
                                        System.Globalization.CultureInfo.InvariantCulture);

                                    if (dt_WebFile > dtFileLoc)
                                    {
                                        downloadAutorise = true;
                                        //FormUtils.LogRegister("LogRegister 1996 TRUE |" + folderLoc + fileUpdate + "|" + downloadAutorise.ToString());
                                    }
                                    else
                                    {
                                        downloadAutorise = false;
                                        //FormUtils.LogRegister("LogRegister 1996 FALSE |" + folderLoc + fileUpdate + "|"+ downloadAutorise.ToString());
                                    }
                                }

                                //downloadAutorise = true;
                            }
                        }
                        
                        if (downloadAutorise ||  !fichierSain || !ExistVersion || (FileServer_major > FileLoc_major) || (FileServer_minor > FileLoc_minor) || (FileServer_build > FileLoc_build))
                        {
                            string typeClientServ = "";
                            if (ParamServ.fileTypeServer == "drivegoogle")
                            {
                                typeClientServ = "";
                                fileServerName = fileUpdateArray[i, 4];
                                //fileServerName = "https://drive.google.com/uc?export=download&id=" + fileUpdateArray[i, 4];
                                fileServerName = fileServerName.Replace("|", "\\");

                            }
                            else
                            {
                                typeClientServ = "ScriptsMod." + TypeClient + @"\";
                                fileServerName = fileUpdate;
                                fileServerName = fileServerName.Replace("|", "\\");
                            }

                            using (WebClient client = new WebClient())
                            {
                                try
                                {
                                    string fullPath = Path.Combine(folderLoc, fileLocName);

                                    // récupère le dossier du fichier
                                    string directory = Path.GetDirectoryName(fullPath);

                                    // crée le dossier s'il n'existe pas
                                    if (!Directory.Exists(directory))
                                    {
                                        Directory.CreateDirectory(directory);
                                    }

                                    client.DownloadProgressChanged += new DownloadProgressChangedEventHandler(client_DownloadProgressChanged);
                                    client.DownloadFileCompleted += new System.ComponentModel.AsyncCompletedEventHandler(client_DownloadFileCompleted);
                                    //client.DownloadFileAsync(new Uri(ParamServ.ServerSelected + typeClientServ + fileServerName), folderLoc + fileLocName,  fileLocName);
                                    client.DownloadFileAsync(
                                        new Uri(ParamServ.ServerSelected + typeClientServ + fileServerName),
                                        fullPath,
                                        fileLocName
                                    );

                                    nbFileUpdated++;

                                    string entryLog = " Updated: " + folderLoc + fileLocName + "\r\n"
                                       + " Date downloadAutorise: " + downloadAutorise.ToString() + " fileUpdateExists " + fileUpdateExists.ToString()
                                       + " " + ParamServ.ServerSelected.ToString() + typeClientServ.ToString() + fileServerName.ToString() + "\r\n" + "\r\n" + "\r\n" + "\r\n"; 

                                    tempLog = tempLog + entryLog;
                                   
                                    FormUtils.LogRegister("LogRegister 2478 |" + entryLog);

                                }
                                catch (Exception ex)
                                {
                                    //string toto = ex.StackTrace;
                                    //nbFileErreur++;
                                    //tempLog = tempLog + fileLocName + " Failed server: " + ParamServ.ServerSelected + typeClientServ + fileServerName + "\r\n";
                                    //tempLog = tempLog + "Or local: " + folderLoc + fileLocName + "\r\n";
                                    //tempLog = tempLog + toto + "\r\n";

                                    nbFileErreur++;
                                    FormUtils.ErrorGeneral_BoxOrLog(ex, " Failed server: "+ ParamServ.ServerSelected + typeClientServ + fileServerName, "Or local: " + folderLoc + fileLocName, false, true);

                                }

                                client.Dispose();
                            }
                        }
                    }
                }

                tf.progressBarA.Visible = true;
                tf.progressBarA.Minimum = 0;
                // Set Maximum to the total number of files to copy.
                tf.progressBarA.Maximum = nbFileUpdated;
                tf.progressBarC.Minimum = 0;
                tf.progressBarC.Maximum = nbFileUpdated;

                if (nbFileUpdated > 0)
                {
                    ScriptsModAvailableVersion.Text = major + "." + minor + "." + build;
                    ScriptsModAvailableVersion.Update();
                    //FormUtils.LogRegister("LogRegister 1966 TypeClient " + TypeClient);

                }
                else if (nbFileUpdated == 0 & nbFileErreur == 0)
                {
                    ScriptsModAvailableVersion.Text = major + "." + minor + "." + build;
                    ScriptsModAvailableVersion.Update();
                    tempLog = tempLog + "No update required " + "\r\n";

                }
            }   // if (succeed)
            

            if (nbFileErreur > 0)
            {
                //MessageBox.Show("Number of error " + nbFileErreur.ToString() 
                //    +"\r\n"
                //    + "check log", "Error");

            }
            else if (nbFileErreur == 0)
            {
                //MessageBox.Show("Number of error " + nbFileErreur.ToString() 
                //    +"\r\n"
                //    + "check log", "Error");
            }
            
            if (nbFileUpdated == 0 & nbFileErreur == 0)
            {
                tf.progressBarA.Visible = false;
                tf.progressBarB.Visible = false;
                
                tf.label2Form2.Visible = true;
                tf.label2Form2.Text = " No update required";

                tf.button1_OK.Visible = true;
            }
            else if (nbFileUpdated > 0)
            {
                ScriptModInstalledVersion.Text = major.ToString() + "." + minor.ToString() + "." + build.ToString();
                ScriptsModUpdateButton.Visible = false;
            }

            void client_DownloadFileCompleted(object se, System.ComponentModel.AsyncCompletedEventArgs ea)
            {
                string contentState = "DownLoad Success!!!";
                
                tf.labelNameFile.Visible = true;
                tf.labelNameFile.Text = (string) ea.UserState;    //fileLocName
                tf.labelNameFile.Update();
                //System.Threading.Thread.Sleep(100);

                if (ea.Error != null)
                {
                    nbErrorAsyn++;

                    contentState = ea.Error.Message;
                    FormUtils.LogRegister("LogRegister 2568 " + contentState + " || " + tf.labelNameFile.Text);
                    tf.progressBarC.ForeColor = Color.Red;
                    tf.progressBarC.Visible = true;
                    tf.progressBarC.Value += 1;
                    tf.labelError.Visible = true;
                }
                else
                {
                    //tf.progressBarA.Increment(nbFileToUpdat);
                    tf.progressBarA.Value += 1;

                    object userstate = ea.UserState;
                    //this.ClientScript.RegisterStartupScript(this.GetType(), "finishDownload", "FinishProgress('" + contentState + "');", true);

                    if (nbFileCompleted >= (nbFileUpdated - 1))
                    {
                        tf.labelNameFile.Visible = false;

                        tf.label1Form2.Visible = true;
                        tf.label1Form2.Text = " Number of files updated: " + nbFileUpdated.ToString();
                        tf.label1Form2.Update();
                        tf.label2Form2.Visible = true;
                        tf.label2Form2.Text = ScriptsModAvailableVersion.Text = major + "." + minor + "." + build;
                        tf.label2Form2.Update();
                        tf.progressBarB.Visible = false;
                        tf.button1_OK.Visible = true;

                        tf.progressBarA.Visible = false;

                        CheckVersionScriptsModLocal();

                    }
                    else
                    {
                        nbFileCompleted = nbFileCompleted + 1;
                    }

                }

                if(nbErrorAsyn > 0 && (nbErrorAsyn + nbFileCompleted >= (nbFileUpdated - 1)))
                {

                    tf.label1Form2.Visible = true;
                    tf.label1Form2.Text = "about " + nbErrorAsyn.ToString() + " files failed. Change server or retest later..";

                    tf.label2Form2.Visible = true;
                    tf.label2Form2.Text = "about " + nbErrorAsyn.ToString() + " files failed. Change server or retest later..";

                    tf.button_Cancel.Visible = true;

                    //CheckVersionScriptsModLocal();
                }
            }
            void client_DownloadProgressChanged(object se, DownloadProgressChangedEventArgs ea)
            {
                tf.progressBarB.Visible = true;
                tf.progressBarB.Value = ea.ProgressPercentage;
            }

            ////Affiche le changelog
            //string ChangelogFileSM = textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua";
            //bool ChangelogFileSMExist = File.Exists(ChangelogFileSM);

            textBox_ChangelogScriptsMod.Text = "";

            //FormUtils.LogRegister("LogRegister 2535 " + tempLog);
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


        //private void groupBox1_Enter(object sender, EventArgs e)
        //{

        //}

        //private void textBox2_TextChanged(object sender, EventArgs e)
        //{

        //}

        //private void textBox3_TextChanged(object sender, EventArgs e)
        //{

        //}


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

        //private void checkBoxSM_CheckedChanged(object sender, EventArgs e)
        //{

        //}


        //private void tabControl2_SelectedIndexChanged(object sender, EventArgs e)
        //{

        //}


        //private void textBox1_TextChanged_1(object sender, EventArgs e)
        //{

        //}

        //private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        //{

        //}


        //private void listBox1_SelectedIndexChanged_1(object sender, EventArgs e)
        //{

        //}

        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =
        // Reset/Delet ScriptsMod
        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =
        private void buttonResetSM_Click(object sender, EventArgs e)
        {

            DialogResult dialogResult = MessageBox.Show("Deleting the current ScriptsMod ?", "Attention", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                string folderLoc = textBox_SavedGames.Text + @"\Mods\tech\DCE\ScriptsMod.NG\";

                if (System.IO.Directory.Exists(folderLoc))
                {
                    int count = 0;

                    System.IO.DirectoryInfo di = new DirectoryInfo(folderLoc);

                    foreach (FileInfo file in di.EnumerateFiles())
                    {
                        //file.Delete();
                        FileSystem.DeleteFile(file.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                        //FileIO.FileSystem.DeleteDirectory(path, FileIO.UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                        count++;
                    }
                    foreach (DirectoryInfo dir in di.EnumerateDirectories())
                    {
                        dir.Delete(true);
                    }

                    ScriptModInstalledVersion.Text = "";

                    //MessageBox.Show("Deleted " + count.ToString() + " files");

                    UpdateScriptsMod();
                }
            }
            else if (dialogResult == DialogResult.No)
            {
                //do something else
            }



        }

        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =
        // Update du ScriptsMod
        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =
        private void CheckUpdate_Click(object sender, EventArgs e)
        {
            UpdateScriptsMod();

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }


        private void ScriptsModUpdate_Click(object sender, EventArgs e)
        {

        }

        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =
        // Update des campagnes
        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =
        public void CampUpdate_Click(object sender, EventArgs e)
        {

            panel1.Controls.Clear();
            UpdateA = 1;
            UpdateB = 1;
            UpdateC = 1;
            string tempLog = "";                                                           //creation du fichier log
            string OnlyThisCamp = "";

            Button button = sender as Button;
            if (button != null & button.Tag != null)
            {
                OnlyThisCamp = button.Tag.ToString();
            }


            if (String.IsNullOrEmpty(textBox_SavedGames.Text))
            {
                MessageBox.Show("You must enter the path to the SavedGame folder in the \"Install\" tab ", "Report");
                return;
            }

            string folderLoc = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\";
            bool folderLocExists = System.IO.Directory.Exists(folderLoc);

            string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
            bool pathManagerExists = System.IO.Directory.Exists(pathManager);
            if (!pathManagerExists)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(pathManager);
                }
                catch (Exception e2)
                {
                    //Console.WriteLine("The process failed: {0}", e2.ToString());
                    //MessageBox.Show("The process failed: {0}", e2.ToString());
                    FormUtils.ShowErrorMessage(e2.Message );
                }
            }
               

            //string fileLocName = "";

            //telecharge le fichier contenant les mises à jour possible
            //using (WebClient client = new WebClient())
            //{
            //    try
            //    {
            //        //client.DownloadFile(ParamServ.ServerSelected + ParamServ.FileServDgUpgradeTXT, pathManager + "upgrade.txt");
            //        client.DownloadFile(ParamServ.FileServDgUpgradeTXT, pathManager + "upgrade.txt");
            //        FormUtils.LogRegister("Download upgrade.txt " + "\r\n");
            //    }
            //    catch (Exception ex)
            //    {
            //        string toto = ex.StackTrace;
            //        FormUtils.LogRegister(fileLocName + " Failed server: " + ParamServ.ServerSelected + ParamServ.FileServDgUpgradeTXT + "\r\n");
            //        FormUtils.LogRegister("Or local: " + pathManager + "upgrade.txt" + "\r\n");
            //        FormUtils.LogRegister(toto + "\r\n");

            //        MessageBox.Show("Failed to download upgrade.txt ", "Report");
            //        return;
            //    }
            //}

            // parse le fichier upgrade pour connaitre la version DCE
            bool fileExists = File.Exists(pathManager + @"\upgrade.txt");
            bool ExistVersion = false;

            string File_name = "";
            string changeFolder = "";

            int FileServer_major = 0;
            int FileServer_minor = 0;
            int FileServer_build = 0;

            int FileLoc_major = 0;
            int FileLoc_minor = 0;
            int FileLoc_build = 0;

            int nbFileToUpdate = 0;
            int nbFileUpdated = 0;
            int nbFileErreur = 0;

            string campName = "";

            bool FolderExists = false;

            string[,] fileUpdateArray = new string[50, 9];
            // 0 fileUpdate
            // 1 = major scriptmod
            // 2 = minus
            // 3 = build
            // 4 = campaignName
            // 5 : date heure du fichier//"31/03/2021 23:22:45"
            // 6 :versionCampaign (folder OVGME par exemple, pour changer de folder) 
            // 7 : firstmission or skipmission
            // 8 : googledrive link

            if (fileExists)
            {
                //string[] lines = System.IO.File.ReadAllLines(pathManager + @"\upgrade.txt");
                char[] MyChar = { ' ' };
                ////versionCampaign.Crisis in PG-Gazelle\Init\db_loadouts.lua = "1.37.100"

                int i = 0;
                //foreach (string line in lines)
                //{
                try
                {
                    // Utiliser un FileStream avec FileShare.Read pour permettre à d'autres processus de lire le fichier
                    using (FileStream fs = new FileStream(pathManager + @"\upgrade.txt", FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            string lineClean = line;
                            if (!String.IsNullOrEmpty(lineClean) && lineClean.Substring(0, 2) != "--")                                                              //ne prend pas en compte les lignes commençant par --
                            {
                                if (lineClean.Contains("versionCampaign["))
                                {
                                    //recherche la version du fichier update
                                    //versionCampaign["Crisis in PG-Tomcat-CVN\Init\targetlist_init.lua"] = "1.1.3"
                                    //versionCampaign["Crisis in PG-Tomcat-CVN\Init\targetlist_init.lua"] = "1.1.3" = 1E96iq9jWRTadVKicZdevDX0ov944isTo = firstmission
                                    //lineClean = lineClean.Replace("versionCampaign", "");

                                    lineClean = lineClean.Replace("https://drive.google.com/file/d/", "");
                                    lineClean = lineClean.Replace("/view?usp=sharing", "");


                                    //recherche les fichiers à mettre à jour
                                    lineClean = lineClean.Replace("\"", "");

                                    string[] part = lineClean.Split('=');

                                    string[] campTab = lineClean.Split('\\');
                                    campName = campTab[0];

                                    string[] pre = part[0].Split('[');                              //versionCampaign  [  Crisis in PG-Tomcat-CVN\Init\targetlist_init.lua]
                                    string[] post = pre[1].Split(']');                              //Crisis in PG-Tomcat-CVN\Init\targetlist_init.lua  ]
                                    File_name = post[0];                                            //Crisis in PG-Tomcat-CVN\Init\targetlist_init.lua

                                    string[] words = part[0].Split('.');
                                    string[] FileVersion = part[1].Split('.');

                                    if (File_name.Length > 0)
                                        fileUpdateArray[i, 0] = (File_name);

                                    if (FileVersion.Count() == 3)
                                    {
                                        fileUpdateArray[i, 1] = FileVersion[0];
                                        fileUpdateArray[i, 2] = FileVersion[1];
                                        fileUpdateArray[i, 3] = FileVersion[2];
                                    }
                                    else
                                    {
                                        string dating = part[1].TrimEnd(MyChar);

                                        fileUpdateArray[i, 5] = dating;                            //"31/03/2021 23:22:45"
                                    }


                                    // 8 : googledrive link
                                    fileUpdateArray[i, 8] = part[2].Replace(" ", "");

                                    string[] campaignFolderTab = File_name.Split('\\');              //Crisis in PG-Tomcat-CVN  \  Init  \  targetlist_init.lua
                                    string campaignName = campaignFolderTab[0];

                                    fileUpdateArray[i, 4] = campaignName;
                                    fileUpdateArray[i, 6] = "versionCampaign";

                                    string ActionMission = part[part.Count() - 1].ToLowerInvariant();
                                    ActionMission = ActionMission.TrimEnd(MyChar);
                                    ActionMission = ActionMission.TrimStart(MyChar);

                                    if (ActionMission == "skipmission" | ActionMission == "firstmission")
                                    {
                                        fileUpdateArray[i, 7] = ActionMission;
                                    }


                                    i++;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
       
                    //// Obtenir les détails supplémentaires dans la pile (StackTrace)
                    //var stackTrace = new System.Diagnostics.StackTrace(ex, true);
                    //var frame = stackTrace.GetFrame(0);

                    //// Récupérer la ligne exacte et le fichier source (si disponibles)
                    //var lineNumber = frame?.GetFileLineNumber() ?? 0;
                    //var fileName = frame?.GetFileName() ?? "Unknown File";

                    //FormUtils.LogRegister($"Error: {ex.Message}, StackTrace: {ex.StackTrace}, Line: {lineNumber}, File: {fileName}");
                    FormUtils.ErrorGeneral_BoxOrLog(ex, " CampUpdate_Click ", pathManager + @"\upgrade.txt", false, true);

                }
            }

            var campaignNameTab = new Dictionary<string, int>();
            string FileServerName = "";
            //déroule le tableau de tous les fichiers potentiellement upgradable
            for (int i = 0; i < 50; i++)
            {
                bool DtWebSupLoc = false;
                bool OtherFolder = false;
                bool campaignFolderLocExists = false;
                string campaignName = "";

                if (fileUpdateArray[i, 0] != null)  // && !String.IsNullOrEmpty(fileUpdate)
                {
                    //versionCampaign["India-Pak War-71 - MiG-19\Init\targetlist_init.lua"] = "1.132.200"

                    folderLoc = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\"; //modifCampaigns
                    folderLocExists = System.IO.Directory.Exists(folderLoc);
                    campaignName = fileUpdateArray[i, 4].ToString();
                    campaignFolderLocExists = System.IO.Directory.Exists(folderLoc + campaignName);
                    changeFolder = "";

                    string fileUpdate = fileUpdateArray[i, 0].ToString();
                    FormUtils.LogRegister(fileUpdate);
                    if (System.Text.RegularExpressions.Regex.IsMatch(fileUpdate, "OvGME"))
                    {
                        //fileUpdateArray[nbFileToUpdat, 0]: India-Pak War-71 - MiG-19\OvGME_Path\Indo-Pak War Mod-DCE\Scripts\Database\db_countries.lua
                        //folderLoc: c: \users\miguel\saved games\dcs_installer\mods\tech\dce\missions\campaigns\
                        fileUpdate = fileUpdate.Replace(fileUpdateArray[i, 4] + @"\OvGME_Path\", "");
                        folderLoc = textBox_OvGME.Text + @"\";
                        changeFolder = "Campaigns\\" + fileUpdateArray[i, 4] + @"\OvGME_Path\";

                        FolderExists = System.IO.Directory.Exists(textBox_OvGME.Text);
                        if (!FolderExists)
                        {
                            MessageBox.Show("\"OVGME Mod Folder\" path must be filled in the Instal tab ", "Report");
                        }

                        OtherFolder = true;

                    }
                    else
                    {
                        FolderExists = System.IO.Directory.Exists(folderLoc + campaignName);
                    }

                    if (changeFolder == "" && fileUpdateArray[i, 6] == "versionCampaign")
                    {
                        changeFolder = "Campaigns\\";
                    }
                    if (changeFolder == "" && fileUpdateArray[i, 6] == "versionDCE")
                    {
                        changeFolder = "Campaigns\\";
                    }

                    string pathCombine = Path.Combine(folderLoc, fileUpdate);
                    bool fileUpdateExists = File.Exists(pathCombine);

                    if (campaignFolderLocExists && !campaignNameTab.ContainsKey(campaignName))
                    {
                        UpdateAddNewLabelA(campaignName);
                        campaignNameTab[campaignName] = 0;
                    }

                    //cherche la version du fichier local, il l'ouvre pour cela
                    if (fileUpdateExists)
                    {
                        //string pathCombine = folderLoc + fileUpdate;
                        using (StreamReader ReaderObject = new StreamReader(pathCombine))
                        {
                            if (fileUpdateArray[i, 1] != null)
                            {
                                FileServer_major = Int32.Parse(fileUpdateArray[i, 1]);
                                FileServer_minor = Int32.Parse(fileUpdateArray[i, 2]);
                                FileServer_build = Int32.Parse(fileUpdateArray[i, 3]);
                            }

                            DateTime dtFileLoc = File.GetLastWriteTime(pathCombine);

                            if (fileUpdateArray[i, 5] != null)
                            {
                                //31/03/2021 09:37:31
                                DateTime dt_WebFile = DateTime.ParseExact(fileUpdateArray[i, 5], " dd/MM/yyyy HH:mm:ss",
                                    System.Globalization.CultureInfo.InvariantCulture);

                                if (dt_WebFile > dtFileLoc)
                                {
                                    DtWebSupLoc = true;
                                    //MessageBox.Show(fileUpdateArray[nbFileToUpdat, 5] + " AA DT superieur à " + dtFileLoc.ToString(), fileUpdate);
                                    //FormUtils.LogRegister("Ligne 1816 " + fileUpdate + " | " + fileUpdateArray[nbFileToUpdat, 5] + " AA DT superieur à " + dtFileLoc.ToString());
                                }
                                else
                                {
                                    //MessageBox.Show(fileUpdateArray[nbFileToUpdat, 5] + " BB DT inferieur à " + dtFileLoc.ToString(), fileUpdate);
                                    //FormUtils.LogRegister("Ligne 1821 " + fileUpdate + " | " + fileUpdateArray[nbFileToUpdat, 5] + " BB DT inferieur à " + dtFileLoc.ToString());
                                    DtWebSupLoc = false;
                                }
                            }

                            //recherche la version du fichier lua
                            string Line;
                            while ((Line = ReaderObject.ReadLine()) != null)
                            {
                                //versionCampaign["Crisis in PG-Tomcat-CVN\Init\targetlist_init.lua"] = "1.1.3"                                   
                                if (Line.Contains("versionCampaign[") | Line.Contains("versionDCE[")) //versionDCE
                                {
                                    Line = Line.Replace("\"", "");
                                    string[] part = Line.Split('=');
                                    string[] FileVersion = part[1].Split('.');

                                    FileLoc_major = Int32.Parse(FileVersion[0]);
                                    FileLoc_minor = Int32.Parse(FileVersion[1]);
                                    FileLoc_build = Int32.Parse(FileVersion[2]);
                                    ExistVersion = true;
                                    break;
                                }
                                else
                                {

                                    ExistVersion = false;
                                }
                            }
                        }
                    }


                    string[] FileExtTab = fileUpdate.Split('.');
                    string FileExt = FileExtTab[FileExtTab.Count() - 1];

                    bool passe = false;
                    if (OtherFolder) // ovgme update autorisé
                    {
                        if (DtWebSupLoc)
                        {
                            passe = true;
                        }
                        else if (!DtWebSupLoc) // ovgme update interdit
                        {
                            passe = false;
                        }

                        //if (!ExistVersion)
                        //{
                        //    passe = false;
                        //}
                    }
                    else if (FileExt == "lua")
                    {
                        if (!ExistVersion)
                        {
                            passe = true;
                        }

                        if (DtWebSupLoc)
                        {
                            passe = true;
                        }
                    }
                    else   // les autre types de fichier .miz .png etc...
                    {
                        //FormUtils.LogRegister("Ligne  passe 02 else DtWebSupLoc " + DtWebSupLoc.ToString());
                        if (!ExistVersion)
                        {
                            passe = false;
                        }

                        if (DtWebSupLoc)
                        {
                            passe = true;
                        }
                    }

                    
                    if ((FolderExists) && (passe | !fileUpdateExists | ((FileServer_major > FileLoc_major) | (FileServer_minor > FileLoc_minor) | (FileServer_build > FileLoc_build))))
                    {
                        string TypeClientServ = "";
                        if (ParamServ.fileTypeServer == "drivegoogle")
                        {
                            TypeClientServ = "";
                            FileServerName = fileUpdateArray[i, 8];
                        }
                        else
                        {
                            TypeClientServ = "ScriptsMod." + @"\";
                            FileServerName = fileUpdate;
                        }

                        //telecharge le fichier contenant les mises à jour possible
                        using (WebClient client = new WebClient())
                        {
                            try
                            {
                                if (OnlyThisCamp == campaignName)
                                {
                                    //client.DownloadFile(ParamServ.ServerSelected + changeFolder + fileUpdate, folderLoc + fileUpdate);

                                    client.DownloadFile(ParamServ.ServerSelected + TypeClientServ + FileServerName, pathCombine);


                                    tempLog = tempLog + " Updated: " + fileUpdate + "\r\n";
                                    nbFileUpdated++;

                                    if (fileUpdateArray[i, 7] == "firstmission")
                                    {
                                        //cherche l'horaire du fichier
                                        string PathFile = folderLoc + campaignName + "_first.miz";
                                        DateTime dtFileLoc = File.GetLastWriteTime(PathFile);
                                        //Crisis in PG - Gazelle\Init\db_loadouts.lua                                       
                                        string[] nameCampaignTemp = fileUpdateArray[i, 0].Split('\\');
                                        string nameCampaign = nameCampaignTemp[0];
                                        //FormUtils.addLigne("resetMission=" + nameCampaign + "=firstmission=" + dtFileLoc, ParamManager.pathManager + "options.txt", true);
                                    }
                                    else if (fileUpdateArray[i, 7] == "skipmission")
                                    {
                                        //cherche l'horaire du fichier
                                        string PathFile = folderLoc + campaignName + "_ongoing.miz";
                                        DateTime dtFileLoc = File.GetLastWriteTime(PathFile);
                                        //Crisis in PG - Gazelle\Init\db_loadouts.lua                                       
                                        string[] nameCampaignTemp = fileUpdateArray[i, 0].Split('\\');
                                        string nameCampaign = nameCampaignTemp[0];
                                        //FormUtils.addLigne("resetMission=" + nameCampaign + "=skipmission=" + dtFileLoc, ParamManager.pathManager + "options.txt", true);

                                    }

                                }
                                else
                                {
                                    nbFileToUpdate++;
                                    campaignNameTab[campaignName] = campaignNameTab[campaignName] + 1;
                                    tempLog = tempLog + " can be updated: " + fileUpdate + "\r\n";
                                }

                            }
                            catch (Exception ex)
                            {
                                string toto = ex.StackTrace;
                                tempLog = tempLog + "Failed server: " + ParamServ.ServerSelected + TypeClientServ + FileServerName + "\r\n";

                                tempLog = tempLog + "Or local: " + pathCombine + "\r\n";

                                tempLog = tempLog + toto + "\r\n";
                                //MessageBox.Show("Update failed, check log", "Report");
                                FormUtils.ShowErrorMessage(ex.Message + " \n Update failed, check log");

                                FormUtils.ErrorGeneral_BoxOrLog(ex, " Update failed, check log ", pathManager + @"\upgrade.txt", false, true);

                            }
                        }
                    }
                }
            }

            foreach (KeyValuePair<string, int> name in campaignNameTab)
            {
                if (name.Value > 0)
                {
                    UpdateAddNewLabelB("Nb of files to update: " + name.Value);
                    UpdateaddNewButtonC(name.Key);
                }
                else
                {
                    UpdateAddNewLabelB("No update required ");
                    UpdateC = UpdateC + 1;
                    tempLog = tempLog + "No update required : " + "\r\n";

                }
            }

            if (nbFileUpdated > 0)
            {
                MessageBox.Show("Number of files downloaded: " + nbFileUpdated, "Report");
            }


            if (nbFileErreur > 0)
                MessageBox.Show("Number of error " + nbFileErreur.ToString(), "Report");

            FormUtils.LogRegister(tempLog);
        }


        private void textBox1_TextChanged_2(object sender, EventArgs e)
        {

        }



        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =
        // Function pour dessiner les icones de toutes les campagnes
        // = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = = =



        //public void ConvertToBitmap(string fileName)
        //{
        //    var bmp1 = Image.FromFile(fileName + ".png");
        //    bmp1.Save(fileName + ".bmp", System.Drawing.Imaging.ImageFormat.Bmp);

        //}

        private readonly int hLigne = 44; //hauteur des lignes du tableau        //24 initialement
        private readonly int decalLargeur = 30;      //decalle tout à droite de x points


        //ajoute le + pour cloner une campagne
        public System.Windows.Forms.PictureBox DrawIconPlus(string NameCamp, string path, int A)
        {
            System.Windows.Forms.PictureBox pictureBox0 = new System.Windows.Forms.PictureBox
            {
                Location = new Point(5, ((A + 1) * hLigne) - 20), //+20 largeur         
                Size = new System.Drawing.Size(20, 20),
                SizeMode = PictureBoxSizeMode.StretchImage
            };
            tabPage2.Controls.Add(pictureBox0);
            tabPage2.Controls.SetChildIndex(pictureBox0, 1);
            pictureBox0.Image = DCE_Manager.Properties.Resources.iconePlus;
            toolTip1.SetToolTip(pictureBox0, "Clone this Campaign");
            pictureBox0.Cursor = System.Windows.Forms.Cursors.Hand;
            pictureBox0.Click += new EventHandler((sender, e) => CampaignPlusClickOneEvent(sender, e, path, NameCamp));

            return pictureBox0;
        }

        ////dessine l'image de la campagne
        //public System.Windows.Forms.PictureBox DrawIconeCampaign_OLD(string NameCamp, string path)
        //{
        //    System.Windows.Forms.PictureBox pictureBox2 = new System.Windows.Forms.PictureBox();
        //    pictureBox2.Size = new System.Drawing.Size(60, 30);     //Hauteur, largeur
        //                                                            // pictureBox2.Location = new Point(22, ((A ) * hLigne) + 20);
        //    pictureBox2.Location = new Point(42, ((A) * hLigne) + 20);

        //    pictureBox2.SizeMode = PictureBoxSizeMode.StretchImage;
        //    tabPage2.Controls.Add(pictureBox2);
        //    if (!File.Exists(path + ".bmp"))
        //    {
        //        FormUtils.ConvertToBitmap(path);
        //    }

        //    //coupe le lien, permet de supprimer l'image sans erreur
        //    string imagePath = path + ".bmp";
        //    using (Image image = Image.FromFile(imagePath, true))
        //    {
        //        pictureBox2.Image?.Dispose();
        //        pictureBox2.Image = new Bitmap(image);
        //    }

        //    pictureBox2.Cursor = System.Windows.Forms.Cursors.Hand;

        //    pictureBox2.Click += new EventHandler((sender, e) => CampaignEdit1(sender, e, path, NameCamp));

        //    return pictureBox2;
        //}

        // Affiche une icône de campagne dans un PictureBox
        public System.Windows.Forms.PictureBox DrawIconeCampaign(string nameCamp, string campaignPath, int A)
        {
            var pictureBox2 = new System.Windows.Forms.PictureBox
            {
                Size = new System.Drawing.Size(60, 30),
                Location = new Point(42, ((A) * hLigne) + 20),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Cursor = System.Windows.Forms.Cursors.Hand
            };

            tabPage2.Controls.Add(pictureBox2);

            // Vérifie si l'image existe en format .bmp, sinon la crée
            string bitmapPath = campaignPath + ".bmp";
            if (!File.Exists(bitmapPath))
            {
                FormUtils.ConvertToBitmap(campaignPath);
            }

            // Charger l'image BMP sans verrouiller le fichier
            try
            {
                using (FileStream bmpStream = new FileStream(bitmapPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    pictureBox2.Image?.Dispose(); // Libère l'ancienne image si elle existe
                    pictureBox2.Image = new Bitmap(bmpStream); // Charge l'image dans le PictureBox
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors du chargement de l'image BMP", bitmapPath, true, true);
            }

            // Assignation de l'événement clic
            pictureBox2.Click += (sender, e) => CampaignEdit1(sender, e, campaignPath, nameCamp);

            return pictureBox2;
        }





        //dessine une icone en cas de pb de path
        public System.Windows.Forms.PictureBox DrawIconeError(int Width, int Height, string str, int A)
        {
            Icon newIcon = SystemIcons.Warning;
            Image bmp = newIcon.ToBitmap();

            System.Windows.Forms.PictureBox pictureBox1 = new System.Windows.Forms.PictureBox
            {
                Location = new Point(52 + decalLargeur, ((A) * hLigne) - 20), //+20 largeur
                SizeMode = PictureBoxSizeMode.StretchImage,
                Size = new System.Drawing.Size(20, 20)
            };

            tabPage2.Controls.Add(pictureBox1);
            tabPage2.Controls.SetChildIndex(pictureBox1, 1);

            pictureBox1.Image = bmp;

            toolTip1.SetToolTip(pictureBox1, str);

            return pictureBox1;
        }

        //ajoute le label nom de campaign
        public System.Windows.Forms.Label Label_NameCampaign(string NameCamp, int A)
        {
            System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

            tabPage2.Controls.Add(txt);
            txt.Top = A * hLigne + 30;
            txt.Left = 72 + decalLargeur;
            txt.AutoSize = true;
            txt.Size = new System.Drawing.Size(170, 20);    // txt.Size = new System.Drawing.Size(170, 20);
            txt.Text = NameCamp;
            //A = A + 1;
            //txt.Click += new EventHandler(ButtonClickOneEvent);
            txt.Cursor = System.Windows.Forms.Cursors.Hand;
            txt.Click += new EventHandler((sender, e) => ButtonClickOneEvent(sender, e, NameCamp));

            return txt;
        }

        //ajoute le label version de campagne
        private System.Windows.Forms.Label Label_VerCampaign(string VerCamp, int A)
        {
            System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

            tabPage2.Controls.Add(txt);
            txt.Top = A * hLigne + 30;
            txt.Left = 350 + decalLargeur;
            //txt.AutoSize = true;
            txt.AutoSize = false;
            txt.Size = new System.Drawing.Size(40, 20);
            txt.Text = VerCamp;
            txt.TextAlign = ContentAlignment.MiddleCenter;
            //A = A + 1;
            return txt;
        }

        ////ajoute le label NbMission
        //public System.Windows.Forms.Label NbMissionPlayed(string NameCamp, string NbMission)
        //{
        //    System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

        //    tabPage2.Controls.Add(txt);
        //    txt.Top = A * hLigne + 30;
        //    txt.Left = 350 + decalLargeur;
        //    //txt.AutoSize = true;
        //    txt.AutoSize = false;
        //    txt.Size = new System.Drawing.Size(40, 20);
        //    txt.Text = "Nb: " + NbMission;
        //    txt.TextAlign = ContentAlignment.MiddleCenter;

        //    A = A + 1;
        //    return txt;
        //}

        //int B = 1;
        //ajoute le boutton FirstMission
        private System.Windows.Forms.Button Button_FirstMission(string NameCamp, string color, int A) //ajoute le boutton FirstMission
        {
            System.Windows.Forms.Button but = new System.Windows.Forms.Button();

            tabPage2.Controls.Add(but);
            but.Top = A * hLigne + 20;
            but.Left = 395 + decalLargeur;
            but.Tag = NameCamp;
            but.Size = new System.Drawing.Size(80, hLigne - 10);
            but.Font = new Font("Georgia", 7);
            but.Text = "Start\r\nCampaign";
            //but.TextAlign = ContentAlignment.BottomRight;

            but.UseVisualStyleBackColor = true;
            but.BackColor = SystemColors.Control;
            //control.ForeColor = SystemColors.ControlText;

            toolTip1.SetToolTip(but, "Be careful, this will erase all the history of your campaign...");

            //but.Click += new EventHandler(ButtonClickOneEvent);
            but.Cursor = System.Windows.Forms.Cursors.Hand;
            but.Click += new EventHandler((sender, e) => ButtonClickOneEvent(sender, e, "FirstMission.bat"));

            //B = B + 1;
            return but;
        }

        //int C = 1;
        //ajoute le boutton SkipMission
        public System.Windows.Forms.Button Button_SkipMission(string NameCamp, string color, int A) //ajoute le boutton SkipMission
        {
            System.Windows.Forms.Button but = new System.Windows.Forms.Button();

            tabPage2.Controls.Add(but);
            but.Top = A * hLigne + 20;
            but.Left = 480 + decalLargeur;
            but.Tag = NameCamp;
            but.Size = new System.Drawing.Size(80, hLigne - 10);
            but.Font = new Font("Georgia", 7);
            but.Text = "SkipMission";
            but.UseVisualStyleBackColor = true;
            if (color != null && color == "red")
            {
                but.BackColor = Color.Red;
            }
            else
            {
                but.BackColor = SystemColors.Control;
            }



            //but.Click += new EventHandler(ButtonClickOneEvent);
            but.Cursor = System.Windows.Forms.Cursors.Hand;
            but.Click += new EventHandler((sender, e) => ButtonClickOneEvent(sender, e, "SkipMission.bat"));

            //myTimer.Elapsed += new ElapsedEventHandler((sender, e) => PlayMusicEvent(sender, e, musicNote));

            return but;
        }

        //ajoute le boutton Configuration
        public System.Windows.Forms.Button Button_Configuration(string NameCamp, string color, int A) //ajoute le boutton Configuration
        {
            System.Windows.Forms.Button but = new System.Windows.Forms.Button();
            //tabPage2.Controls.Add(but);

            tabPage2.Controls.Add(but);
            but.Top = A * hLigne + 20;
            but.Left = 570 + decalLargeur;
            but.Tag = NameCamp + @"\Init\";
            but.Size = new System.Drawing.Size(85, hLigne - 10);
            but.Font = new Font("Georgia", 7);
            but.Text = "Configuration";
            but.UseVisualStyleBackColor = true;
            if (color != null && color == "red")
            {
                but.BackColor = Color.Red;
                toolTip1.SetToolTip(but, "After an update, the mission must be generated");
            }
            else
            {
                but.BackColor = SystemColors.Control;
            }

            //but.Click += new EventHandler(ButtonClickOneEvent( NameCamp + @"\Init\conf_mod.lua"));
            but.Cursor = System.Windows.Forms.Cursors.Hand;
            but.Click += new EventHandler((sender, e) => ButtonClickOneEvent(sender, e, "conf_mod.lua"));

            toolTip1.SetToolTip(but, "After modification, you must restart a SkipMission");

            //D = D + 1;
            return but;
        }

        ////affiche la version sur scriptsmod
        //int E = 1;
        //public System.Windows.Forms.Label addNewLabelE(string NameCamp, string VerScriptsMod)
        //{
        //    System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

        //    tabPage2.Controls.Add(txt);
        //    txt.Top = A * hLigne + 26;
        //    txt.Left = 650 + decalLargeur;
        //    //txt.AutoSize = true;
        //    txt.Size = new System.Drawing.Size(90, 20);
        //    txt.Text = VerScriptsMod;
        //    txt.TextAlign = ContentAlignment.MiddleCenter;
        //    return txt;

        //}

        //ajoute le label NbMission
        private System.Windows.Forms.Label NbMissionPlayed(string NbMission, int A)
        {
            System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

            tabPage2.Controls.Add(txt);
            txt.Top = A * hLigne + 26;
            txt.Left = 650 + decalLargeur;
            //txt.AutoSize = true;
            txt.AutoSize = false;
            txt.Size = new System.Drawing.Size(90, 20);
            if (NbMission == "0")
            {
                txt.Text = " ";
            }
            else
            {
                txt.Text = "Nb: " + NbMission;
            }
            
            txt.TextAlign = ContentAlignment.MiddleCenter;

            return txt;
        }

        //public System.Windows.Forms.PictureBox DrawIconDel(string NameCamp, string path)
        //{

        //    System.Windows.Forms.PictureBox pictureBox3 = new System.Windows.Forms.PictureBox();
        //    pictureBox3.Location = new Point(730 + decalLargeur, ((A) * hLigne) - 20); //+20 largeur

        //    pictureBox3.Size = new System.Drawing.Size(20, 20);
        //    pictureBox3.SizeMode = PictureBoxSizeMode.StretchImage;

        //    tabPage2.Controls.Add(pictureBox3);
        //    tabPage2.Controls.SetChildIndex(pictureBox3, 1);
        //    pictureBox3.Cursor = System.Windows.Forms.Cursors.Hand;
        //    pictureBox3.Image = DCE_Manager.Properties.Resources.iconDel;

        //    pictureBox3.Click += new EventHandler((sender, e) => CampaignDeleteClickOneEvent(sender, e, path, NameCamp));

        //    //toolTip1.SetToolTip(pictureBox1, str);

        //    return pictureBox3;
        //}
        // Créer une liste pour garder une référence à toutes les CheckBox associées aux campagnes
        private List<Tuple<CheckBox, string>> checkBoxCampaigns = new List<Tuple<CheckBox, string>>();

        public System.Windows.Forms.CheckBox CheckboxDel(string NameCamp, int A)
        {
            // Créer une instance de CheckBox
            System.Windows.Forms.CheckBox checkBoxDelete = new System.Windows.Forms.CheckBox
            {
                // Positionnement de la case à cocher
                Location = new Point(730 + decalLargeur, (A * hLigne) + 30),
                Size = new System.Drawing.Size(20, 20),
                //Text = NameCamp,  // Ajoute le nom de la campagne à la CheckBox
            };

            // Ajouter la case à cocher au tabPage
            tabPage2.Controls.Add(checkBoxDelete);
            tabPage2.Controls.SetChildIndex(checkBoxDelete, 1);

            // Ajouter la CheckBox et son nom de campagne dans une liste pour plus tard
            checkBoxCampaigns.Add(new Tuple<CheckBox, string>(checkBoxDelete, NameCamp));

            // Retourner la case à cocher
            return checkBoxDelete;
        }

        //inutile, c'etait un test pour afficher des erreurs
        //public System.Windows.Forms.Label addNewLabelErrorPath(string ErreurPath)
        //{
        //    System.Windows.Forms.Label txt2 = new System.Windows.Forms.Label();

        //    tabPage2.Controls.Add(txt2);
        //    txt2.Top = (E-1) * hLigne + 23;
        //    txt2.Left = 730 + decalLargeur;
        //    txt2.AutoSize = true;
        //    txt2.Size = new System.Drawing.Size(90, 20);
        //    txt2.Text = ErreurPath;
        //    //txt.TextAlign = ContentAlignment.MiddleCenter;
        //    //E = E + 1;
        //    return txt2;

        //}


        public void CampaignPlusClickOneEvent(object sender, EventArgs e, string path, string OldNameCamp)
        {
            
            // Assurez-vous d'appeler UpdateSharedData avant d'ouvrir Form3_Clonage
            UpdateSharedData();

            //Test.Form3_Clonage CloneForm = new Test.Form3_Clonage(this, path, OldNameCamp);
            DCE_Manager.FormClonage CloneForm = new DCE_Manager.FormClonage(this, path, OldNameCamp);
            CloneForm.ShowDialog();

            tabControl.SelectedIndex = 3;
            tabControl.SelectedIndex = 1;

        }

        public void CampaignEdit1(object sender, EventArgs e, string path, string NameCamp)
        {
            DCE_Manager.CampaignEdit EditCampaignForm = new DCE_Manager.CampaignEdit(this, NameCamp);
            //EditCampaignForm.ShowDialog();

        }
        private void But_delete_campaign_Click(object sender, EventArgs e)
        {
            // Lister les campagnes à supprimer
            List<string> campaignsToDelete = new List<string>();

            // Parcourir toutes les cases à cocher et vérifier celles qui sont cochées
            foreach (var checkBoxCampaign in checkBoxCampaigns)
            {
                CheckBox checkBox = checkBoxCampaign.Item1;
                string campaignName = checkBoxCampaign.Item2;

                if (checkBox.Checked)
                {
                    // Ajouter le nom de la campagne à la liste des campagnes à supprimer
                    campaignsToDelete.Add(campaignName);
                }
            }

            // Si aucune campagne n'est sélectionnée, afficher un message et sortir de la fonction
            if (campaignsToDelete.Count == 0)
            {
                MessageBox.Show("No campaign selected for suppression.", "Information");
                return;
            }

            // Construire un message avec les noms des campagnes à supprimer
            string campaignsToDeleteMessage = string.Join("\r\n", campaignsToDelete);
            string confirmationMessage = "The following campaigns will be deleted :\r\n\r\n" + campaignsToDeleteMessage + "\r\n\r\nAre you sure ?";

            // Afficher une fenêtre de confirmation avec la liste des campagnes
            DialogResult dialogResult = MessageBox.Show(confirmationMessage, "Delete confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            // Si l'utilisateur confirme la suppression
            if (dialogResult == DialogResult.Yes)
            {
                // Supprimer les campagnes sélectionnées
                foreach (string campaign in campaignsToDelete)
                {
                    // Appel à la fonction de suppression pour chaque campagne
                    DeleteCampaign(campaign);
                }

                // Rafraîchir la page après suppression
                tabControl.SelectedIndex = 3;
                tabControl.SelectedIndex = 1;
            }
            else if (dialogResult == DialogResult.No)
            {
                // Si l'utilisateur annule la suppression, ne fais rien
                MessageBox.Show("Deletion cancelled.", "Cancellation");
            }
        }

        public void DeleteCampaign(string OldNameCamp)
        {
            // Afficher le curseur en cercle tournant
            Cursor.Current = Cursors.WaitCursor;

            string path = Path.Combine(textBox_SavedGames.Text, @"Mods\tech\DCE\Missions\Campaigns");
            string folderLoc = Path.Combine(path, OldNameCamp);

            // Vérifier si le dossier de la campagne existe
            if (Directory.Exists(folderLoc))
            {
                DirectoryInfo di = new DirectoryInfo(folderLoc);

                // Supprimer les fichiers du dossier
                foreach (FileInfo file in di.EnumerateFiles())
                {
                    try
                    {
                        // Vérifier et fermer le fichier s'il est ouvert, puis le supprimer
                        file.IsReadOnly = false; // S'assurer que le fichier est en écriture
                        FileSystem.DeleteFile(file.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }
                    catch (IOException ioEx)
                    {
                        FormUtils.LogRegister($"Error deleting file {file.FullName}: {ioEx.Message}");
                    }
                }

                // Supprimer les sous-dossiers
                foreach (DirectoryInfo dir in di.EnumerateDirectories())
                {
                    try
                    {
                        dir.Delete(true);
                    }
                    catch (IOException ioEx)
                    {
                        FormUtils.LogRegister($"Error deleting subfolder {dir.FullName}: {ioEx.Message}");
                    }
                }

                // Supprimer le dossier de la campagne
                try
                {
                    di.Delete(true);
                }
                catch (IOException ioEx)
                {
                    FormUtils.LogRegister($"Error deleting folder {di.FullName}: {ioEx.Message}");
                }
            }

            // Supprimer les fichiers .cmp et .png associés à la campagne
            DeleteFileToRecycleBin(Path.Combine(path, OldNameCamp + ".cmp"));
            DeleteFileToRecycleBin(Path.Combine(path, OldNameCamp + "_first.miz"));
            DeleteFileToRecycleBin(Path.Combine(path, OldNameCamp + "_ongoing.miz"));
            DeleteFileToRecycleBin(Path.Combine(path, OldNameCamp + ".png"));
            DeleteFileToRecycleBin(Path.Combine(path, OldNameCamp + ".bmp"));

            ScriptModInstalledVersion.Text = "";

            // Remettre le curseur par défaut à la fin de la tâche
            Cursor.Current = Cursors.Default;
        }

        private void DeleteFileToRecycleBin(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal); // Retirer le verrouillage en lecture seule
                    FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                catch (IOException ioEx)
                {
                    FormUtils.LogRegister($"Erreur lors de la suppression du fichier {filePath}: {ioEx.Message}");
                }
            }
        }


        public void ButtonClickOneEvent(object sender, EventArgs e, string path)
        {
            if (sender is Button button)
            {
                //// verif du fichier requiredModules
                string ModRequiredFile = (textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + button.Tag + @"\Init\requiredModules.lua");
                string msg = "";

                if (File.Exists(ModRequiredFile))
                {

                    try
                    {
                        // Utiliser un FileStream avec FileShare.Read pour permettre à d'autres processus de lire le fichier
                        using (FileStream fs = new FileStream(ModRequiredFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (StreamReader sr = new StreamReader(fs))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {
                                if (!System.Text.RegularExpressions.Regex.IsMatch(line, "versionDCE"))
                                {
                                    msg = msg + line + "\r\n";
                                }
                            }
                            MessageBox.Show(msg, "Module required:");
                        }
                    }

                    catch (Exception ex)
                    {
                        //FormUtils.LogRegister($"Error: {ex.Message}, StackTrace: {ex.StackTrace}, Line: {lineNumber}, File: {fileName}");
                        FormUtils.ErrorGeneral_BoxOrLog(ex, "ButtonClickOneEvent", ModRequiredFile, false, true);
                    }
                }


                //if (LabelStatut.Text == "DEV")
                if (ParamManager.userLevel == 5)
                {

                    string missionName = "Test Mission";

                    // Passez des arguments au formulaire
                    FormMissionGenerator MissGene = new FormMissionGenerator(missionName, this);

                    // Utilisez ShowDialog pour afficher le formulaire de manière modale
                    MissGene.ShowDialog();

                }
                else
                {
                    Process process = new Process();
                    // Configure the process using the StartInfo properties.
                    process.StartInfo.FileName = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + button.Tag + @"\" + path;
                    process.StartInfo.Arguments = " ";
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                    process.StartInfo.WorkingDirectory = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + button.Tag + @"\";

                    process.Start();

                }

            }

            if (sender is Label label)
            {
                Process process = new Process();
                // Configure the process using the StartInfo properties.
                process.StartInfo.FileName = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + label.Text;
                process.StartInfo.Arguments = " ";
                process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                //process.StartInfo.WorkingDirectory = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" ;

                process.Start();

            }

            if (sender is PictureBox)
            {
                Process process = new Process();
                // Configure the process using the StartInfo properties.
                process.StartInfo.FileName = path;
                process.StartInfo.Arguments = " ";
                process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;

                process.Start();

            }
        }

        void TabControl1_Selected(object sender, TabControlEventArgs e)
        {
            //MessageBox.Show(e.TabPage.ToString(), "info");

            tabPage2.Controls.Clear();
            //A = 0;
            //B = 1;
            //C = 1;
            //D = 1;
            //E = 1;

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage1       +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            if (e.TabPage == tabPage1)
            {
                checkBoxMod();
                //button_InstallCampaign.Visible = true;
                groupBoxDroiteAccueil.Visible = true;
                groupBoxCampEdit.Visible = false;
                groupBox_staticTemplate.Visible = false;

                tabPage7.Controls.Clear();
                tabPage8.Controls.Clear();
                tabPage9.Controls.Clear();
                tabPage10.Controls.Clear();
                groupBoxCampEdit.Text = "";

                CampaignTab.Visible = false;
                buttonSaveChgtCampaign.Visible = false;
                buttonSaveActive.Visible = false;
                buttonResetBackup.Visible = false;

                //string pathFile = textBox_DCS.Text + @"\Scripts\MissionScripting.lua";

                //Boolean find_OS = false;
                //Boolean find_IO = false;

                //if (File.Exists(pathFile))
                //{
                //    checkBoxSanitize.Enabled = true;
                //    using (StreamReader reader = new StreamReader(pathFile))
                //    {
                //        string line;


                //        while ((line = reader.ReadLine()) != null)
                //        {
                //            int nbcaractereOS = line.IndexOf("sanitizeModule('os");
                //            if (nbcaractereOS > -1)
                //            {

                //                //MessageBox.Show("passe _o_",  nbcaractere.ToString());
                //                int nbCaractTiret = line.IndexOf("--");
                //                if (nbCaractTiret > -1 && nbCaractTiret < nbcaractereOS )
                //                {
                //                    find_OS = true;
                //                }
                //            }
                //            int nbcaractereIO = line.IndexOf("sanitizeModule('io");
                //            if (nbcaractereIO > -1)
                //            {
                //                int nbCaractTiret = line.IndexOf("--");
                //                if (nbCaractTiret > -1 && nbCaractTiret < nbcaractereIO)
                //                {
                //                    find_IO = true;
                //                }
                //            }
                //        }
                //    }

                //    if (find_OS && find_IO)
                //    {
                //        checkBoxSanitize.Checked = true;
                //    }
                //    else
                //    {
                //        checkBoxSanitize.Checked = false;
                //    }
                //}
                //else
                //{
                //    checkBoxSanitize.Enabled = false;
                //}

            }
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage2       +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

            else if (e.TabPage == tabPage2)
            {
                // Afficher le curseur en cercle tournant
                Cursor.Current = Cursors.WaitCursor;

                groupBoxDroiteAccueil.Visible = false;
                groupBoxCampEdit.Visible = true;
                groupBox_staticTemplate.Visible = false;
                tabPage7.Controls.Clear();
                tabPage8.Controls.Clear();
                tabPage9.Controls.Clear();
                tabPage10.Controls.Clear();
                groupBoxCampEdit.Text = "";
                int nbCampaign = 0;
                int A = 0;

                bool folderCampExists = System.IO.Directory.Exists(textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns");
                if (folderCampExists)
                {
                    foreach (string subFolder in Directory.GetDirectories(textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns"))
                    {
                        string[] NameCampTab = subFolder.Split('\\');
                        string NameCamp = NameCampTab[NameCampTab.Count() - 1];

                        bool folderLocExists = System.IO.Directory.Exists(subFolder);

                        //string VerScriptsMod = "unknown ";

                        bool erreurPath = false;
                        //string CheckPath_SG = "";
                        //string CheckPath_DCS = "";
                        string erreurPathString = "Error path.bat: ";

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


                            //nbCampaign++;
                            //string fileToRead = PathBatFile;
                            //using (StreamReader ReaderObject = new StreamReader(fileToRead))
                            //{
                            //    string Line = "";
                            //    while ((Line = ReaderObject.ReadLine()) != null)
                            //    {
                            //        if (Line.IndexOf("versionPackageICM") > -1)
                            //        {
                            //            VerScriptsMod = Line.Replace("set \"versionPackageICM=", "");
                            //            string[] words = VerScriptsMod.Split('"');
                            //            VerScriptsMod = words[0];

                            //            //VerScriptsMod = VerScriptsMod.Replace("\"", "");
                            //        }
                            //    }
                            //}

                            ////cherche la ligne pathSavedGames pour s'assurer que le path est coherent
                            //bool PSD_bug = false;
                            //using (StreamReader ReaderObject = new StreamReader(fileToRead))
                            //{
                            //    string Line = "";
                            //    while ((Line = ReaderObject.ReadLine()) != null)
                            //    {
                            //        if (Line.IndexOf("pathSavedGames") > -1)
                            //        {
                            //            //set "pathSavedGames=C:\Users\Miguel\Saved Games\DCS\" xyz
                            //            CheckPath_SG = Line.Replace("set \"pathSavedGames=", "");

                            //            //C:\Users\Miguel\Saved Games\DCS\" xyz
                            //            string[] words = CheckPath_SG.Split('"');

                            //            //C:\Users\Miguel\Saved Games\DCS\
                            //            CheckPath_SG = words[0];

                            //            if (CheckPath_SG.Substring(CheckPath_SG.Length - 1, 1) == "\\")
                            //            {
                            //                CheckPath_SG = CheckPath_SG.Substring(0, CheckPath_SG.Length - 1);
                            //            }
                            //            //C:\Users\Miguel\Saved Games\DCS

                            //            if (CheckPath_SG.ToLower() != textBox_SavedGames.Text.ToLower())
                            //            {

                            //                PSD_bug = true;

                            //                erreurPath = true;

                            //                erreurPathString = erreurPathString + CheckPath_SG + " <> " + textBox_SavedGames.Text;
                            //                //MessageBox.Show("Texte de mon erreur personnalisé", "Titre de la messagebox", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            //            }
                            //        }
                            //    }
                            //}

                            //if (PSD_bug)
                            //{

                            //    (int nbLigne, int nb_Error) = FormUtils.ModifierLigneBis(PathBatFile, "set \"pathSavedGames=", "set \"pathSavedGames=" + textBox_SavedGames.Text + "\\\"");
                            //    if (nbLigne > 0 & nb_Error == 0)
                            //        erreurPath = false;
                            //}

                            ////cherche la ligne pathDCS pour s'assurer que le path est coherent
                            //bool PDCS_bug = false;
                            //using (StreamReader ReaderObject = new StreamReader(fileToRead))
                            //{
                            //    string Line = "";
                            //    while ((Line = ReaderObject.ReadLine()) != null)
                            //    {
                            //        if (Line.IndexOf("pathDCS") > -1)
                            //        {
                            //            CheckPath_DCS = Line.Replace("set \"pathDCS=", "");

                            //            //C:\Users\Miguel\Saved Games\DCS\" xyz
                            //            string[] words = CheckPath_DCS.Split('"');

                            //            //C:\Users\Miguel\Saved Games\DCS\
                            //            CheckPath_DCS = words[0];

                            //            if (CheckPath_DCS.Substring(CheckPath_DCS.Length - 1, 1) == "\\")
                            //            {
                            //                CheckPath_DCS = CheckPath_DCS.Substring(0, CheckPath_DCS.Length - 1);
                            //            }

                            //            if (CheckPath_DCS.ToLower() != textBox_DCS.Text.ToLower())
                            //            {

                            //                PDCS_bug = true;
                            //                erreurPath = true;
                            //                erreurPathString = erreurPathString + " " + CheckPath_DCS + " <> " + textBox_DCS.Text;
                            //            }
                            //        }
                            //    }
                            //}
                            //if (PDCS_bug)
                            //{

                            //    (int nbLigne, int nb_Error) = FormUtils.ModifierLigneBis(PathBatFile, "set \"pathDCS=", "set \"pathDCS=" + textBox_DCS.Text + "\\\"");
                            //    if (nbLigne > 0 & nb_Error == 0)
                            //        erreurPath = false;
                            //}


                            nbCampaign++;

                            //Cherche la version de la campagne
                            bool CampaignOriginal = false;
                            string VerCamp = "";
                            string PathCampInitFile = subFolder + @"\Init\camp_init.lua";
                            bool fileExistPathCampInitFile = File.Exists(PathCampInitFile);
                            if (fileExistPathCampInitFile)
                            {
                                string FileToRead2 = PathCampInitFile;
                                using (StreamReader ReaderObject = new StreamReader(FileToRead2))
                                {
                                    string Line = "";
                                    while ((Line = ReaderObject.ReadLine()) != null)
                                    {
                                        if (Line.IndexOf("version") > -1)   //version = "V18-gc",
                                        {
                                            string[] wordUno = Line.Split('=');
                                            string[] wordDeuz = wordUno[1].Split(',');
                                            VerCamp = wordDeuz[0];
                                            VerCamp = VerCamp.Replace("\"", "");

                                            VerCamp = VerCamp.TrimStart().TrimEnd();
                                        
                                        }
                                        if (Line.IndexOf("CampaignOriginal") > -1)
                                        {

                                            string[] wordUno = Line.Split('=');
                                            string[] wordDeuz = wordUno[1].Split(',');
                                            //wordDeuz[0] = wordDeuz[0].Replace(" ", "");
                                            wordDeuz[0] = wordDeuz[0].TrimStart().TrimEnd();
                                            CampaignOriginal = Convert.ToBoolean(wordDeuz[0]);
                                        }
                                    }
                                }
                            }

                           
                            DrawIconPlus(NameCamp, textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns", A);
                       

                            //Cherche le nombre de mission joué
                            //['mission'] = 1,
                            string NbMission = "0";
                            string PathCampstatusFile = subFolder + @"\Active\camp_status.lua";
                            bool fileExistPathCampstatusFile = File.Exists(PathCampstatusFile);
                            if (fileExistPathCampstatusFile)
                            {
                                string FileToRead2 = PathCampstatusFile;
                                using (StreamReader ReaderObject = new StreamReader(FileToRead2))
                                {
                                    string Line = "";
                                    while ((Line = ReaderObject.ReadLine()) != null)
                                    {
                                        if (Line.IndexOf("['mission']") > -1 || Line.IndexOf("[\"mission\"]") > -1)
                                        {
                                            string[] wordUno = Line.Split('=');
                                            string[] wordDeuz = wordUno[1].Split(',');
                                            NbMission = wordDeuz[0].Replace(" ", "");
                                            NbMission = (Int32.Parse(NbMission) - 1).ToString();
                                            break;
                                        }
                                    }
                                }
                            }

                            //cherche si une campagne doit etre reset a la suite d'un update
                            var campaignNameTab = new Dictionary<string, string>();

                            string colorFM = "";
                            string colorSM = "";
                            if (folderLocExists)
                            {

                                //DrawIconeCampaign

                                //cherche la couleur pour le bouton resetCampagn
                                if (campaignNameTab.ContainsKey(NameCamp))
                                {
                                    if (campaignNameTab[NameCamp] == "firstmission")
                                    {
                                        //cherche la date de creation/modif du fichier FirstMission.miz ou ongoing.miz
                                        //pour detecter si la generation de mission a été faite, et ainsi enlever la couleur rouge du button

                                        //TF-71-Hornet-GC22_first.miz
                                        //TF-71-Hornet-GC22_ongoing.miz

                                        DateTime dtNow = DateTime.Now;
                                        dtNow.ToString("MM/dd/yyyy HH:mm:ss");

                                        string PathFirstFile = subFolder + "_first.miz";
                                        if (File.Exists(PathFirstFile))
                                        {
                                            DateTime dtFileLoc = File.GetLastWriteTime(PathFirstFile);


                                            if (dtFileLoc < dtNow)
                                            {

                                            }
                                        }
                                        colorFM = "red";
                                    }
                                    else if (campaignNameTab[NameCamp] == "skipmission")
                                    {
                                        colorSM = "red";
                                    }
                                }

                                string path_oob_air = "";
                                if (Int32.Parse((string)NbMission) >= 1)
                                {
                                    path_oob_air = subFolder + @"\Active\oob_air.lua";
                                }
                                else
                                {
                                    path_oob_air = subFolder + @"\Init\oob_air_init.lua";
                                }


                                LuaParse C_db_oob_air = new LuaParse();

                                ParamCampaign.NameFileParse = path_oob_air;

                                C_db_oob_air.Parse(File.ReadAllText(path_oob_air));

                                string type = "default";
                                string type_temp = "default";
                                bool tag_break = false;

                                foreach (KeyValuePair<string, LuaObject> entry in C_db_oob_air.Val)
                                {
                                    foreach (KeyValuePair<string, LuaObject> entry2 in entry.Value)
                                    {
                                        bool tagPlayer = false;
                                        bool tagType = false;
                                        foreach (KeyValuePair<string, LuaObject> entry3 in entry2.Value)
                                        {

                                            //type = "A-10C_2",
                                            if (entry3.Key == "type")
                                            {
                                                type_temp = entry3.Value.luaobj.ToString();
                                                tagType = true;
                                            }
                                            //player = false,
                                            if (entry3.Key == "player" && entry3.Value.luaobj.ToString() == "true")
                                            {
                                                tagPlayer = true;
                                            }

                                        }

                                        if (tagPlayer && tagType)
                                        {
                                            tag_break = true;
                                            type = type_temp;
                                            break;
                                        }
                                    }
                                    if (tag_break)
                                        break;
                                }


                                //check si plusieurs images par type d'avion existe dans le dossier image
                                string filePNGbyePlane = (textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + @"\Images\planescreen_" + type + ".png");
                                string filePNG = (textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".png");
                                string fileBMP = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".bmp";

                                //FormUtils.LogRegister("LogRegister 4825 filePNGbyePlane  |" + filePNGbyePlane + "| ");

                                if (File.Exists(filePNGbyePlane))
                                {
                                    //File.Delete(fileIMG);
                                    File.Copy(filePNGbyePlane, filePNG, true);

                                    if (File.Exists(fileBMP))
                                    {
                                        //Image image2 = Image.FromFile(fileBMP);
                                        //image2.Dispose();
                                        File.Delete(fileBMP);
                                    }

                                    //FormUtils.LogRegister("LogRegister 4839 filePNGbyePlane exist");

                                }

                                //ajoute l'image de la campagne
                                //string filePNG = (textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp + ".png");
                                string fileIMG = (textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp);
                                bool ExistsfilePNG = File.Exists(filePNG);
                                if (ExistsfilePNG)
                                {
                                    DrawIconeCampaign(NameCamp, fileIMG, A);
                                }
                                else
                                {

                                    string[] fileNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
                                    foreach (String fileName in fileNames)
                                    {
                                        if (fileName.IndexOf("etiquetteLogo.png") > -1)
                                        {
                                            using (FileStream fileStream = File.Create(filePNG))
                                            {
                                                Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName).CopyTo(fileStream);
                                            }
                                            DrawIconeCampaign(NameCamp, fileIMG, A);
                                        }
                                    }



                                }

                                Label_NameCampaign(NameCamp, A);                                                           //ajoute le nom et chemin des campaign
                                Label_VerCampaign(VerCamp, A);                                                 //ajoute la version de la campagne
                                //NbMissionPlayed(NameCamp, NbMission);                                               //ajoute la version de la campagne
                                Button_FirstMission(NameCamp, colorFM, A);                                                //ajoute le boutton FirstMission
                                Button_SkipMission(NameCamp, colorSM, A);                                                //ajoute le bouton Nextmission

                                bool confModFile = File.Exists(subFolder + @"\Init\conf_mod.lua");
                                if (confModFile)
                                {
                                    Button_Configuration(NameCamp, null, A);
                                }
                                else
                                {
                                    //D = D + 1;
                                }
                                //addNewLabelE(NameCamp, VerScriptsMod);
                                NbMissionPlayed(NbMission, A);//ajoute le label nb de mission joué

                                if (erreurPath)
                                {
                                    //addNewLabelErrorPath( erreurPathString);                            //affiche les erreurs trouvé sur le path
                                    DrawIconeError(10, 10, erreurPathString, A);                            //affiche les erreurs trouvé sur le path
                                }

                                //DrawIconDel(NameCamp, textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns");
                                CheckboxDel(NameCamp, A);

                                A = A + 1;

                            }
                        }
                        
                    }


                    //ajoute ici le bouton Delete
                    System.Windows.Forms.Button but = new System.Windows.Forms.Button();

                    //pictureBox3.Location = new Point(730 + decalLargeur, ((A) * hLigne) - 20); //+20 largeur

                    if(nbCampaign > 0)
                    {
                        tabPage2.Controls.Add(but);
                        but.Top = A * hLigne + 20;
                        but.Left = 690;// + decalLargeur;
                        but.Size = new System.Drawing.Size(80, hLigne - 10);
                        but.Font = new Font("Georgia", 7);
                        but.Text = "Delete";

                        but.UseVisualStyleBackColor = true;
                        but.BackColor = SystemColors.Control;

                        but.Cursor = System.Windows.Forms.Cursors.Hand;
                        but.Click += new EventHandler(this.But_delete_campaign_Click);
                    }

                }
                else
                {
                    //MessageBox.Show("\"DCS Saved Game Folder\" path must be filled in the Instal tab ", "Report");
                }

                // Remettre le curseur par défaut à la fin de la tâche
                Cursor.Current = Cursors.Default;
            }

            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++  tabPage3       +++++++++++++++++++++++++
            //+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
            else if (e.TabPage == tabPage3)
            {
                groupBoxDroiteAccueil.Visible = true;
                groupBoxCampEdit.Visible = false;
                groupBox_staticTemplate.Visible = false;

                tabPage7.Controls.Clear();
                tabPage8.Controls.Clear();
                tabPage9.Controls.Clear();
                tabPage10.Controls.Clear();
                groupBoxCampEdit.Text = "";

                CampaignTab.Visible = false;
                buttonSaveChgtCampaign.Visible = false;
                buttonSaveActive.Visible = false;
                buttonResetBackup.Visible = false;

                panel1.Controls.Clear();
                ScriptsModUpdateButton.Visible = false;
                //button_UpdateManager.Visible = false;
                DceManagerInstalledVersion.Text = VersionDceManager.Text;
                if (String.IsNullOrEmpty(textBox_SavedGames.Text))
                {
                    MessageBox.Show("You must enter the path to the SavedGame folder in the \"Install\" tab ", "Report");
                    return;
                }

                string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
                bool pathManagerExists = System.IO.Directory.Exists(pathManager);
                if (!pathManagerExists)
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(pathManager);
                    }
                    catch (Exception e2)
                    {
                        //Console.WriteLine("The process failed: {0}", e2.ToString());
                        //MessageBox.Show("The process failed: {0}", e2.ToString());
                        FormUtils.ShowErrorMessage(e2.Message);
                    }
                }
                    

                //met à jour immediatement la version des fichiers présent sur le serveur
                //et propose (ou pas) l'affichage du bouton update
                CheckPresenceUpgadeTxtFile();


            }
            else if (e.TabPage == tabPage4)
            {
                groupBoxDroiteAccueil.Visible = true;
                groupBoxCampEdit.Visible = false;
                groupBox_staticTemplate.Visible = false;

                tabPage7.Controls.Clear();
                tabPage8.Controls.Clear();
                tabPage9.Controls.Clear();
                tabPage10.Controls.Clear();
                groupBoxCampEdit.Text = "";

                CampaignTab.Visible = false;
                buttonSaveChgtCampaign.Visible = false;
                buttonSaveActive.Visible = false;
                buttonResetBackup.Visible = false;

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
            else if (e.TabPage == tabPage5)
            {
                groupBoxDroiteAccueil.Visible = true;
                groupBoxCampEdit.Visible = false;
                groupBox_staticTemplate.Visible = false;

                tabPage7.Controls.Clear();
                tabPage8.Controls.Clear();
                tabPage9.Controls.Clear();
                tabPage10.Controls.Clear();

                groupBoxCampEdit.Text = "";

                CampaignTab.Visible = false;
                buttonSaveChgtCampaign.Visible = false;
                buttonSaveActive.Visible = false;
                buttonResetBackup.Visible = false;
            }
            else if (e.TabPage == tabPage12)
            {
               
            }
        }




        int UpdateA = 1;
        public System.Windows.Forms.Label UpdateAddNewLabelA(string NameCamp)
        {
            System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

            panel1.Controls.Add(txt);
            txt.Top = UpdateA * 20 + 23;
            txt.Left = 25;
            txt.AutoSize = true;
            //txt.Size = new System.Drawing.Size(170, 20);
            txt.Text = NameCamp;
            UpdateA = UpdateA + 1;
            return txt;
        }
        int UpdateB = 1;



        public System.Windows.Forms.Label UpdateAddNewLabelB(string NameCamp)
        {
            System.Windows.Forms.Label txt = new System.Windows.Forms.Label();

            panel1.Controls.Add(txt);
            //tabControl1.TabPages.Add(tabPage2);

            panel1.Controls.Add(txt);
            txt.Top = UpdateB * 20 + 23;
            txt.Left = 200;
            txt.AutoSize = true;
            //txt.Size = new System.Drawing.Size(170, 20);
            txt.Text = NameCamp;
            UpdateB = UpdateB + 1;
            return txt;
        }
        int UpdateC = 1;
        //private object syncObject;

        public System.Windows.Forms.Button UpdateaddNewButtonC(string NameCamp) //ajoute le bouton Nextmission
        {
            System.Windows.Forms.Button but = new System.Windows.Forms.Button();
            panel1.Controls.Add(but);

            panel1.Controls.Add(but);
            but.Top = UpdateC * 20 + 23;
            but.Left = 320;
            but.Tag = NameCamp;
            but.Size = new System.Drawing.Size(70, 20);
            but.Text = "Update";
            but.UseVisualStyleBackColor = true;
            but.BackColor = SystemColors.Control;

            but.Click += new EventHandler(CampUpdate_Click);

            UpdateC = UpdateC + 1;
            return but;
        }

        public string VersionLongDceManager()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            string versionString = String.Format("{0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision);
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
            
            CheckVersionScriptsModLocal();

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

                //string txt = "";
                //foreach (var entry in ParamConf.configDictionary)
                //{
                //    txt = txt + entry.Key + " = " + entry.Value + "\r\n";
                //}
                //MessageBox.Show(txt, "ParamConf.configDictionary");

                // Utiliser la fonction pour mettre à jour le fichier avec le contenu du dictionnaire
                FormUtils.UpdateConfigFileFromDictionary();
            }
        }

        //button_UpdateManager_Click
        private async void button_UpdateManager_ClickAsync(object sender, EventArgs e)
        {

            int FileServer_major = 0;
            int FileServer_minor = 0;
            int FileServer_build = 0;

            int nbFileUpdated = 0;

            bool succeed = true;
            string tempLog = "";
            string FileServerName = "";
            string FileLocName = "";

            string pathManager = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\DCE_Manager\";
            bool pathManagerExists = System.IO.Directory.Exists(pathManager);
            if (!pathManagerExists)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(pathManager);
                }
                catch (Exception e2)
                {
                    //Console.WriteLine("The process failed: {0}", e2.ToString());
                    //MessageBox.Show("The process failed: {0}", e2.ToString());
                    FormUtils.ShowErrorMessage(e2.Message);
                }
            }


            //string DownloadFolder = System.Environment.ExpandEnvironmentVariables("%USERPROFILE%\\Downloads\\");

            // Récupérer le chemin du dossier Téléchargements
            string downloadFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";

            // Vérifier si le dossier existe et le créer si nécessaire
            if (!Directory.Exists(downloadFolder))
            {
                ////Directory.CreateDirectory(downloadFolder);

                //// Créer une boîte de dialogue pour sélectionner un dossier
                //FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
                //folderBrowserDialog.Description = "Select the folder where you want to save the file";

                //// Afficher la boîte de dialogue et récupérer le chemin sélectionné
                //if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                //{
                //    downloadFolder = folderBrowserDialog.SelectedPath;
                //    // Utiliser le chemin downloadFolder pour enregistrer le fichier
                //}

                //**************************************
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

                    //// Pré-sélectionner le répertoire "Saved Games"
                    //string savedGamesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Saved Games";
                    //if (Directory.Exists(savedGamesPath))
                    //{
                    //    folderBrowserDialog.SelectedPath = savedGamesPath;
                    //}

                    // Afficher le dialogue et vérifier si l'utilisateur a sélectionné un dossier
                    if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                    {
                        // Récupérer le chemin du dossier sélectionné
                        downloadFolder = folderBrowserDialog.SelectedPath;
                    }
                }
                //*********************************************
            }


            string NameServer = "";
            
            // parse le fichier upgrade pour connaitre la version DCE
            //string pathUpgradeFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\";

            bool exists = System.IO.Directory.Exists(pathManager);
            string pathFile = pathManager + "upgrade.txt";
            bool fileExists = File.Exists(pathFile);
            bool failedUpdate = false;

            string[,] fileUpdateArray = new string[50, 4];

            if (fileExists) //exists &
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
                            string lineClean = line.Replace(" ", "");
                            if (!String.IsNullOrEmpty(lineClean) && lineClean.Substring(0, 2) != "--")                                                              //ne prend pas en compte les lignes commençant par --
                            {
                                //versionManager = "3.7.5"
                                //versionManagerGoogleDrive = 1gn-wgHxxh8i-ke7LpUsu65x9aF5RtPBA

                                if (lineClean.Contains("versionManager="))
                                {
                                    //recherche la version de DCE
                                    lineClean = lineClean.Replace(" ", "");
                                    lineClean = lineClean.Replace("versionManager=", "");
                                    lineClean = lineClean.Replace("\"", "");

                                    string[] words = lineClean.Split('.');
                                    FileServer_major = Int32.Parse(words[0]);
                                    FileServer_minor = Int32.Parse(words[1]);
                                    FileServer_build = Int32.Parse(words[2]);

                                    if (ParamServ.fileTypeServer == "ftp")
                                    {
                                        FileServerName = "DCE_Manager_V" + FileServer_major + "." + FileServer_minor + "." + FileServer_build + ".zip";
                                    }

                                }
                                else if (lineClean.Contains("versionManagerGoogleDrive=") && ParamServ.fileTypeServer == "drivegoogle")
                                {
                                    lineClean = lineClean.Replace("versionManagerGoogleDrive=", "");
                                    lineClean = lineClean.Replace(" ", "");
                                    FileServerName = lineClean;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.ErrorGeneral_BoxOrLog(ex, "button_UpdateManager_Click", pathFile, false, true);
                }
            }
            else
            {
                //FormUtils.ErrorGeneral_BoxOrLog(ex, "button_UpdateManager_Click", pathFile, false, true);
            }

            //telecharge le fichier à mettre à jour
            if (succeed)
            {

                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                String.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);

                if ((FileServer_major > version.Major) | (FileServer_minor > version.Minor) | (FileServer_build > version.Build))
                {
                    string downloadUrl = "";
                    string destinationPath = "";

                    try
                    {
                        if (ParamServ.fileTypeServer == "drivegoogle")
                        {
                            //client.DownloadFile(fileServerName, DownloadFolder + "DCE_Manager.zip");
                            downloadUrl = FileServerName;
                            destinationPath = downloadFolder + @"\DCE_Manager.zip";

                        }
                        else
                        {
                            //client.DownloadFile(ParamServ.ServerSelected + @"_DCE_Manager\" + fileServerName, DownloadFolder + fileServerName);
                            downloadUrl = ParamServ.ServerSelected + "_DCE_Manager/" + FileServerName;
                            destinationPath = Path.Combine(downloadFolder, FileServerName);
                        }

                        // Log avant téléchargement
                        FormUtils.LogRegister($"Attempt to download directly from {downloadUrl} to {destinationPath}");

                        // Téléchargement du fichier
                        bool success = await FormUtils.TéléchargerFichierAvecHttpClient(downloadUrl, destinationPath);
                        if (!success)
                            failedUpdate = true;


                        nbFileUpdated++;
                        tempLog += $" Updated: {destinationPath}\r\n";
                    }
                    catch (WebException webEx)
                    {
                        // Détails de l'erreur WebException
                        //FormUtils.ErrorGeneral_BoxOrLog(webEx, "Erreur Web pendant le téléchargement", downloadUrl, false, true);
                        failedUpdate = true;

                        // Affiche le message d'erreur détaillé dans un messagebox
                        MessageBox.Show($"A download error has occurred:\n{webEx.Message}\n" +
                                        $"Statut : {webEx.Status}\n" +
                                        $"URL : {downloadUrl}", "Download error");

                        FormUtils.LogRegister($"A download error has occurred:\n{webEx.Message}\n" +
                                        $"Statut : {webEx.Status}\n" +
                                        $"URL : {downloadUrl}");
                    }
                    catch (Exception ex)
                    {
                        // Log général pour toutes les autres exceptions
                        FormUtils.ErrorGeneral_BoxOrLog(ex, "button_UpdateManager_Click", $"Erreur serveur : {downloadUrl}", true, true);
                        failedUpdate = true;
                    }

                    if (nbFileUpdated > 0 & failedUpdate == false)
                    {
                        DceManagerAvailableVersion.Text = "DCE_Manager downloaded " + FileLocName;
                        tempLog = tempLog + "DCE_Manager downloaded " + FileLocName + " " + FileServerName + "\r\n";

                        FormUtils.LogRegister(downloadFolder + FileLocName);

                        MessageBox.Show("The program will close and open the downloaded Zip file." + "\r\n" +
                            "It is up to you to extract it and place it wherever you want.", "Report");

                        Process process = new Process();
                        // Configure the process using the StartInfo properties.

                        process.StartInfo.FileName = downloadFolder + FileLocName;
                        process.StartInfo.Arguments = " ";
                        process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
                        process.StartInfo.WorkingDirectory = downloadFolder;

                        process.Start();

                        Application.Exit();


                    }
                }
            }

            //MessageBox.Show(tempLog, "Report");
            FormUtils.LogRegister(tempLog);

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

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            string NameServer = (string)comboBox_Server.SelectedItem;

            //MessageBox.Show(NameServer, (string)ParamServ.ServerNickName01);
            //ServerNickName01 = "000webhostapp";
            if (NameServer == ParamServ.ServerNickName01 && ParamServ.Server01Exit)
            {
                ParamServ.ServerSelected = ParamServ.FileServerName01;
                ParamServ.ServerNickNameSelected = NameServer;
                ParamServ.fileTypeServer = ParamServ.fileTypeServer01;
                ParamServ.FileServDgUpgradeTXT = ParamServ.FileServDgUpgradeTXT01;

                CheckPresenceUpgadeTxtFile();
            }
            //ServerNickName02 = "GoogleDrive";
            else if (NameServer == ParamServ.ServerNickName02 && ParamServ.Server02Exit)
                //if (NameServer == ParamServ.ServerNickName02)
            {
                ParamServ.ServerSelected = ParamServ.FileServerName02;
                ParamServ.ServerNickNameSelected = NameServer;
                ParamServ.fileTypeServer = ParamServ.fileTypeServer02;
                ParamServ.FileServDgUpgradeTXT = ParamServ.FileServDgUpgradeTXT02;

                CheckPresenceUpgadeTxtFile();
            }
            else if (NameServer == ParamServ.ServerNickName03 && ParamServ.Server03Exit)
            {
                ParamServ.ServerSelected = ParamServ.FileServerName03;
                ParamServ.ServerNickNameSelected = NameServer;
                ParamServ.fileTypeServer = ParamServ.fileTypeServer03;
                ParamServ.FileServDgUpgradeTXT = ParamServ.FileServDgUpgradeTXT03;

                CheckPresenceUpgadeTxtFile();
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

        private void butCreateUpdateBrowse_Click(object sender, EventArgs e)
        {

            //DialogResult fdlg = new FolderBrowserDialog();

            FolderBrowserDialog folderDlg = new FolderBrowserDialog();

            DialogResult result = folderDlg.ShowDialog();

            string CreateFolder = "";
            if (txtBoxFolderCreateUpdate.Text == "")
            {
                CreateFolder = System.Environment.ExpandEnvironmentVariables("%USERPROFILE%\\Downloads");
            }
            else
            {
                CreateFolder = txtBoxFolderCreateUpdate.Text;
            }


            if (result == DialogResult.OK)
            {
                txtBoxFolderCreateUpdate.Text = folderDlg.SelectedPath;
                //Environment.SpecialFolder root = folderDlg.RootFolder;
            }

            //string[] fichiers = Directory.GetFiles(txtBoxFolderCreateUpdate.Text, "*.lua", SearchOption.AllDirectories);

            FormUtils.LogRegister("txtBoxFolderCreateUpdate.Text |" + txtBoxFolderCreateUpdate.Text + "|");

        }

        public void ButCreaUpdate_Click(object sender, EventArgs e)
        {

            //***********************************************************************
            //************Creation du dictionary N°version des fichiers à mettre à jour*************
            //***********************************************************************

            int i = 0;
            var CreateUpdateList = new Dictionary<string, string>();

            string[] fichiers = Directory.GetFiles(txtBoxFolderCreateUpdate.Text, "*.*", SearchOption.AllDirectories);

            foreach (var file in fichiers)
            {

                string[] filePath = file.Split('\\');

                string[] ext = filePath[filePath.Length - 1].Split('.');
                if ((ext[1] == "lua") || (ext[1] == "txt"))
                {

                    StreamReader sr = new StreamReader(file);

                    Boolean breakLoop = false;
                    string line = null;
                    string ligneRecherche = "versionDCE[";
                    while ((sr.Peek() != -1))
                    {

                        //versionDCE["MAIN_NextMission.lua"] = "1.26.111"
                        line = sr.ReadLine();
                        if (!breakLoop && line.IndexOf(ligneRecherche) > -1)
                        {
                            string[] part = line.Split('=');
                            string[] post = part[0].Split('[');
                            string[] TabFileName = post[1].Split(']');
                            string FileName = TabFileName[0];
                            FileName = Regex.Replace(FileName, @"[\\/|]", "|");

                            part[1] = part[1].Replace(" ", "");
                            string FileVersion = part[1];

                            if (CreateUpdateList.ContainsKey(FileName) == true)
                            {
                                //Console.WriteLine(FileName + " de " + filePath.ToString()  + " existe déjà");
                                MessageBox.Show(FileName + " de " + file.ToString() + " existe déjà", "Report");
                            }
                            else
                            {
                                CreateUpdateList.Add(FileName, FileVersion);
                            }
                            //recherche la version du ScriptsMod inscrit dans UTIL_Changelog.lua
                            //versionDCE.ScriptsMod = "20.58.205"
                            if (line.IndexOf("UTIL_Changelog.") > -1)
                            {
                                Divers.ScriptsMod = FileVersion;
                            }
                            breakLoop = true;
                            i++;
                        }
                    }
                    sr.Close();
                }
                //recense les fichiers images ou son png oog
                else
                {
                    DateTime dtFileLoc = File.GetLastWriteTime(file);
                    //CreateUpdateList[ext[0]] = dtFileLoc.ToString();

                    string nameOtherFile;
                    if (filePath[filePath.Length - 2] == "NG")
                    {
                        nameOtherFile = ext[0] + "." + ext[1];
                    }
                    else
                    {
                        nameOtherFile = filePath[filePath.Length - 2] + "|" + ext[0] + "." + ext[1];
                    }


                    CreateUpdateList.Add("\"" + nameOtherFile.ToString() + "\"", "\"" + dtFileLoc.ToString() + "\"");
                    //CreateUpdateList.Add( nameOtherFile.ToString() ,  dtFileLoc.ToString());

                    i++;
                }
            }

            //MessageBox.Show("Fichiers Controlé: " + nbFileToUpdat.ToString(), "Report");

            //***********************************************************************
            //************Creation dictionary des liens googleDrive*************
            //***********************************************************************

            var GooglLink = new Dictionary<string, string>();

            if (File.Exists(textBoxCreateFileUpdate.Text + @"\upgradeLink.txt"))
            {
                using (StreamReader reader = new StreamReader(textBoxCreateFileUpdate.Text + @"\upgradeLink.txt"))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if ((line.IndexOf("versionDCE[") > -1) && line.Substring(0, 2) != "--")                                                         //ne prend pas en compte les lignes commençant par --
                        {
                            string[] part = line.Split('=');

                            string[] post = part[0].Split('[');
                            string[] TabFileName = post[1].Split(']');
                            string FileName = TabFileName[0];

                            part[1] = part[1].Replace(" ", "");
                            string FileVersion = part[1];

                            GooglLink[FileName] = FileVersion;
                        }
                    }
                }
            }

            //***********************************************************************
            //************Creation ou mise à jour du fichier Upgrade.txt*************
            //***********************************************************************
          
            //cherche si ce GestionName existe deja

            Boolean foundInUpgradeTXT;
            Boolean foundVerScriptsMod = false;

            int NbLignModif = 0;
            int Nb_erreur = 0;
            string noFoundList = "";
            string pathFile = textBoxCreateFileUpdate.Text + @"\upgrade.txt";
            string tempPathFile = Path.Combine(Path.GetTempPath(), "upgrade_temp.txt");

            try
            {
                // Copier upgrade.txt vers un fichier temporaire pour éviter le verrouillage du fichier original
                File.Copy(pathFile, tempPathFile, overwrite: true);

                foreach (KeyValuePair<string, string> entry in CreateUpdateList)
                {
                    foundInUpgradeTXT = false;
                    try
                    {
                        // Utiliser un FileStream avec FileShare.Read pour permettre à d'autres processus de lire le fichier
                        using (FileStream fs = new FileStream(tempPathFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (StreamReader sr = new StreamReader(fs))
                        {
                            string line;
                            while ((line = sr.ReadLine()) != null)
                            {

                                if (line.Length >= 2 && line.Substring(0, 2) != "--")
                                {

                                    if (line.Contains(entry.Key) & GooglLink.ContainsKey(entry.Key))
                                    {
                                        int nlm = 0;
                                        int nb_e = 0;
                                        string ligneAModifier = "versionDCE[" + entry.Key + "] = " + entry.Value + " = " + GooglLink[entry.Key];

                                        FormUtils.LogRegister("DEV ligneAModifier " + ligneAModifier.ToString());

                                        (nlm, nb_e) = FormUtils.ModifierLigneBis(textBoxCreateFileUpdate.Text + @"\upgrade.txt", line, ligneAModifier);

                                        NbLignModif = NbLignModif + nlm;
                                        Nb_erreur = Nb_erreur + nb_e;

                                        FormUtils.LogRegister("DEV nlm " + nlm.ToString() + "nlmTotal" + NbLignModif.ToString());

                                        foundInUpgradeTXT = true;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FormUtils.ErrorGeneral_BoxOrLog(ex, "ButCreaUpdate_Click", pathFile, false, true);
                    }

                    //ne recherche que la ligne:
                    //versionDCE.ScriptsMod = "20.58.205" dans upgrade.txt
                    if (!foundVerScriptsMod)
                    {
                        try
                        {
                            // Utiliser un FileStream avec FileShare.Read pour permettre à d'autres processus de lire le fichier
                            using (FileStream fs = new FileStream(tempPathFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (StreamReader sr = new StreamReader(fs))
                            {
                                string line;
                                while ((line = sr.ReadLine()) != null)
                                {
                                    if (line.Length >= 2 && line.Substring(0, 2) != "--")
                                    {
                                        int nb_B = 0;
                                        int nb_e = 0;
                                        if (line.Contains("versionDCE.ScriptsMod"))
                                        {

                                            string ligneAModifier = "versionDCE.ScriptsMod" + " = " + Divers.ScriptsMod;

                                            (nb_B, nb_e) = FormUtils.ModifierLigneBis(pathFile, line, ligneAModifier);

                                            NbLignModif = NbLignModif + nb_B;
                                            Nb_erreur = Nb_erreur + nb_e;

                                            foundInUpgradeTXT = true;
                                            foundVerScriptsMod = true;

                                            if (nb_B == 0)
                                            {
                                                MessageBox.Show("in versionDCE.ScriptsMod no Change ligne: " + "\r\n"
                                                    + "|pathFile|" + pathFile + "\r\n"
                                                    + "|line|" + line + "\r\n"
                                                    + "|ligneAModifier|" + ligneAModifier + "\r\n"
                                                    , "foundInUpgradeTXT FALSE");
                                            }
                                            //else
                                            //{
                                            //    MessageBox.Show("in versionDCE.ScriptsMod  Change ligne OK: " + "\r\n"
                                            //        + "Nb de ligne modifiée: "+ nb_B.ToString() + "\r\n"
                                            //    + "|pathFile|" + pathFile + "\r\n"
                                            //    + "|line|" + line + "\r\n"
                                            //    + "|ligneAModifier|" + ligneAModifier + "\r\n"
                                            //    , "foundInUpgradeTXT TRUE");
                                            //}
                                        }

                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            FormUtils.ErrorGeneral_BoxOrLog(ex, "ButCreaUpdate_Click", pathFile, false, true);
                        }
                    }
                    if (foundInUpgradeTXT == false)
                    {
                        noFoundList = noFoundList + entry.Key + "\r\n";

                        MessageBox.Show("noFoundList: " + "|" + noFoundList.ToString() + "|", "foundInUpgradeTXT FALSE");

                    }
                    if (foundVerScriptsMod == false)
                    {
                        noFoundList = noFoundList + entry.Key + "\r\n";

                        MessageBox.Show("noFoundList: " + "|versionDCE.ScriptsMod|", "foundInUpgradeTXT FALSE");

                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors de la copie du fichier upgrade.txt vers un fichier temporaire", pathFile, false, true);
            }
            finally
            {
                // Suppression du fichier temporaire après lecture et modification
                if (File.Exists(tempPathFile))
                {
                    try
                    {
                        File.Delete(tempPathFile);
                    }
                    catch (Exception ex)
                    {
                        FormUtils.ErrorGeneral_BoxOrLog(ex, "Erreur lors de la suppression du fichier temporaire", tempPathFile, false, true);
                    }
                }
            }

            if (noFoundList == "" & Nb_erreur == 0)
            {
                MessageBox.Show("OK Mise à jour de update.txt effectué avec succes \r\n" + "Nb de lignes modifiées: " + NbLignModif.ToString() + "\r\n" 
                    + textBoxCreateFileUpdate.Text + @"\upgrade.txt", "Succes");
            }
            else
            {
                MessageBox.Show("Pb lors de la mise à jour" + "\r\n"
                    + noFoundList.ToString() + "\r\n"
                    + "Nb d'error?: " + Nb_erreur.ToString() + "\r\n"
                    + "Conclusion: regarde le log pour plus d'info ^^ "+ "\r\n"
                    , "Bug(s)?");
            }
            if (NbLignModif != i)
            {
                MessageBox.Show("Nb de ligne differentes AVANT: " + i.ToString() + " Apres: " + NbLignModif.ToString(), "Error???? ");
            }

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

        static Color GetPixel(Point position)
        {
            using (var bitmap = new Bitmap(1, 1))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(position, new Point(0, 0), new Size(1, 1));
                }
                return bitmap.GetPixel(0, 0);
            }
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

        private void textBoxCampEdit_TextChanged(object sender, EventArgs e)
        {

        }


        //if (tabControl1.SelectedTab == someTabPage)
        //{
        //// Do stuff here...
        //}

        void CampaignTab_Selected(object sender, TabControlEventArgs e)
        {
             if (e.TabPage == tabPage6 || groupBoxCampEdit.Text == "")
             {
                buttonSaveChgtCampaign.Visible = false;
                buttonSaveActive.Visible = false;
                buttonResetBackup.Visible = false;
             }
            if (groupBoxCampEdit.Text != "")
            {
                if (e.TabPage == tabPage7 || e.TabPage == tabPage8)
                {
                    buttonSaveChgtCampaign.Visible = true;
                    buttonSaveActive.Visible = false;
                    buttonResetBackup.Visible = true;
                }
                else if (e.TabPage == tabPage9 || e.TabPage == tabPage10 )
                {
                    buttonSaveChgtCampaign.Visible = false;
                    buttonSaveActive.Visible = true;
                    buttonResetBackup.Visible = false;
                }
            }
        }

        public void modifiedCampaign(string pathFile, string pathFileBackup, string initActive)
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

            foreach (TabPage tbp in CampaignTab.TabPages)
            {
                
                if (tbp.Name == "tabPage7" | tbp.Name == "tabPage8" | tbp.Name == "tabPage9" | tbp.Name == "tabPage10")
                {
                    int idCamp = 0;
                    if (tbp.Name == "tabPage7" | tbp.Name == "tabPage9")
                    {
                        idCamp = 1;
                    }
                    else if (tbp.Name == "tabPage8" | tbp.Name == "tabPage10")
                    {
                        idCamp = 2;
                    }

                    foreach (Control c in tbp.Controls)
                    {
                        if (c is CheckBox || c is RadioButton)
                        {
                            if (c.Name != "")
                            {
                                string[] words = c.Name.Split('|');

                                int idSquad = Int32.Parse((string)words[0]);
                                string varName = words[1];
                                string tableName = words[2];
                                //int nbVariable = Int32.Parse((string)c.Tag);
                                string[] wordsB = c.Tag.ToString().Split('|');
                                int nbVariable = Int32.Parse((string)wordsB[0]);
                                string keyName = wordsB[1];

                                string ValueChecked = "";

                                if (c is RadioButton)
                                {
                                    ValueChecked = ((RadioButton)c).Checked.ToString().ToLower();
                                }
                                else if (c is CheckBox)
                                {
                                    ValueChecked = ((CheckBox)c).Checked.ToString().ToLower();
                                }
                                
                                //if (keyName == "Active" || keyName == "Inactive")
                                if (keyName == "Active")
                                {
                                    keyName = "Inactive";
                                    if (ValueChecked == "false")
                                    {
                                        ValueChecked = "true";
                                    }
                                    else
                                    {
                                        ValueChecked = "false";
                                    }
                                }

                                if (ParamDivers.NewParseOobAir)
                                {
                                    //var squad = List_oob_air_Manager.List_oob_air.Where(s => s.IdSquad == idSquad);
                                    var squad = List_oob_air_Manager.List_oob_air.FirstOrDefault(s => s.IdSquad == idSquad);

                                    if (squad != null)
                                    {
                                        UpdateProperty(squad, keyName, tableName, ValueChecked);
                                    }
                                }                              
                            }
                        }
                        else if (c is ComboBox)
                        {
                            //MessageBox.Show("else if (c is ComboBox)");

                            if (c.Name != "")
                            {
                                //nameArg3 = args["item"].ToString();
                                //label1.Name = nameArg1 + "_" + nameArg2 + "_" + nameArg3;
                                //label1.Name = args["idSquad"].ToString() + "_" + Name + "_" + argsString["tablSup"];
                                string[] words = c.Name.Split('|');
                                int idSquad = Int32.Parse((string)words[0]);
                                string varName = words[1];
                                string tableName = words[2];

                                
                                //label1.Tag = args["idElement"].ToString() + "_" + argsString["key"];
                                string[] wordsB = c.Tag.ToString().Split('|');
                                int nbVariable = Int32.Parse((string)wordsB[0]);
                                string keyName = wordsB[1];

                                string ValueChecked = "";


                                if (((ComboBox)c).SelectedItem != null)
                                {
                                    ValueChecked = ((ComboBox)c).SelectedItem.ToString();
                                }
                                else
                                {
                                    //ValueChecked = varName.Replace(" ", "");
                                    //ValueChecked = varName;
                                }

                                if (keyName == "BaseAlternative")
                                { }

                                if (ParamDivers.NewParseOobAir)
                                {
                                    var squad = List_oob_air_Manager.List_oob_air.FirstOrDefault(s => s.IdSquad == idSquad);

                                    if (squad != null)
                                    {
                                        UpdateProperty(squad, keyName, tableName, ValueChecked);
                                    }
                                }
                             }
                        }

                        //NumericUpDown control = new NumericUpDown();
                        else if (c is NumericUpDown)
                        {
                            if (c.Name != "")
                            {
                                string[] words = c.Name.Split('|');

                                int idSquad = Int32.Parse((string)words[0]);
                                string varName = words[1];
                                string tableName = words[2];
                                //int nbVariable = Int32.Parse((string)c.Tag);
                                string[] wordsB = c.Tag.ToString().Split('|');
                                int nbVariable = Int32.Parse((string)wordsB[0]);
                                string keyName = wordsB[1];

                                string ValueChecked = "";

                                ValueChecked = ((NumericUpDown)c).Value.ToString();
                                
                                if (ParamDivers.NewParseOobAir)
                                {
                                    var squad = List_oob_air_Manager.List_oob_air.FirstOrDefault(s => s.IdSquad == idSquad);

                                    if (squad != null)
                                    {
                                        UpdateProperty(squad, keyName, tableName, ValueChecked);
                                    }
                                }
                            }
                        }
                    }
                }
            }

           
            //active l'unité du squad sélectionné
            foreach (var squad in List_oob_air_Manager.List_oob_air)
            {
                if (squad.Player)
                {
                    squad.Inactive = false;
                }
            }

            //ecrit les Class de tous les squad dans le fichier oob_air
            FormUtils.WriteListClassSquadsToFile(pathFile, initActive);

            MessageBox.Show("Changes saved.", "Report");
        }

        public void buttonSaveChgtCampaign_Click(object sender, EventArgs e)
        {
            string pathFileBackup = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Init\oob_air_init_backup_DTT.lua";
            string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Init\oob_air_init.lua";

            modifiedCampaign(pathFile, pathFileBackup, "Init");

            tabPage7.Controls.Clear();
            tabPage8.Controls.Clear();

            PublicTable.errorTable.Clear();
            textBox_Bugs.Text = "";
            tabPage12.Text = "Bugs";

            CampaignEdit1(sender, e, pathFile, groupBoxCampEdit.Text);

        }

        private void buttonSaveActive_Click(object sender, EventArgs e)
        {
            string pathFile = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text + @"\Active\oob_air.lua";
           
            modifiedCampaign(pathFile, null, "Active");

            tabPage9.Controls.Clear();
            tabPage10.Controls.Clear();

            PublicTable.errorTable.Clear();
            textBox_Bugs.Text = "";
            tabPage12.Text = "Bugs";

            CampaignEdit1(sender, e, pathFile, groupBoxCampEdit.Text);
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

            //string fileIMG = (textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + NameCamp);
            string path = textBox_SavedGames.Text + @"\Mods\tech\DCE\Missions\Campaigns\" + groupBoxCampEdit.Text;

            DCE_Manager.CampaignEdit EditCampaignForm = new DCE_Manager.CampaignEdit(this, groupBoxCampEdit.Text);
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

            txtBoxFolderCreateUpdate.Visible = false;
            textBoxCreateFileUpdate.Visible = false;
            butCreateUpdateBrowse.Visible = false;
            butCreaUpdate.Visible = false;
            ScriptsModUpdateButton.Visible = false;
            CampUpdateButton.Visible = false;
            C_DataMap.Visible = false;
            C_DataMapCity.Visible = false;
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

            txtBoxFolderCreateUpdate.Visible = false;
            textBoxCreateFileUpdate.Visible = false;
            butCreateUpdateBrowse.Visible = false;
            butCreaUpdate.Visible = false;
            ScriptsModUpdateButton.Visible = false;
            CampUpdateButton.Visible = false;
            C_DataMap.Visible = false;
            C_DataMapCity.Visible = false;
            textBox_id_client.Visible = false;
            LabelStatut.Text = "CampaignMaker";
            ScriptsModUpdateButton.Text = "Update";
            ParamManager.userLevel = 3;

        }

        private void VersionDceManager_Click(object sender, EventArgs e)
        {
            //Pour devenir DEV

            if (ButtonPreview == true ) {


                VersionLongDceManager();
                txtBoxFolderCreateUpdate.Visible = true;
                textBoxCreateFileUpdate.Visible = true;
                butCreateUpdateBrowse.Visible = true;
                butCreaUpdate.Visible = true;
                ScriptsModUpdateButton.Visible = true;
                CampUpdateButton.Visible = true;
                C_DataMap.Visible = true;
                C_DataMapCity.Visible = true;
                but_GPS_LL.Visible = true;
                //checkBoxSanitize.Visible = true;
                LabelStatut.Text = "DEV";
                ScriptsModUpdateButton.Text = "Update DEV";
                butCheckVersion.Visible = true;
                textBox_id_client.Visible = true;
            }

        }

        private void butCheckVersion_Click(object sender, EventArgs e)
        {
            CheckVersionScriptsModLocal();
        }

        private void textBox_id_client_TextChanged(object sender, EventArgs e)
        {

        }
    }

}