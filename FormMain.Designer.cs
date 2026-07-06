using System;

namespace DCE_Manager
{
    partial class Form1
    {
        /// <summary>
        /// Variable nécessaire au concepteur.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Nettoyage des ressources utilisées.
        /// </summary>
        /// <param name="disposing">true si les ressources managées doivent être supprimées ; sinon, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Code généré par le Concepteur Windows Form

        /// <summary>
        /// Méthode requise pour la prise en charge du concepteur - ne modifiez pas
        /// le contenu de cette méthode avec l'éditeur de code.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.m_ButtonDcsPath = new System.Windows.Forms.Button();
            this.m_ButtonSavedGames = new System.Windows.Forms.Button();
            this.textBox_DCS = new System.Windows.Forms.TextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.checkBoxSanitize = new System.Windows.Forms.CheckBox();
            this.checkBox_OvwNGfolder = new System.Windows.Forms.CheckBox();
            this.linkLabelCampaign = new System.Windows.Forms.LinkLabel();
            this.checkBoxActiveFolder = new System.Windows.Forms.CheckBox();
            this.button_InstallCampaign = new System.Windows.Forms.Button();
            this.Label_Campaign = new System.Windows.Forms.Label();
            this.textBox_Campaign = new System.Windows.Forms.TextBox();
            this.Button_choiceCampaign = new System.Windows.Forms.Button();
            this.linkLabelOvGME = new System.Windows.Forms.LinkLabel();
            this.Label_OvGME = new System.Windows.Forms.Label();
            this.textBox_OvGME = new System.Windows.Forms.TextBox();
            this.button2 = new System.Windows.Forms.Button();
            this.Label_SavedGames = new System.Windows.Forms.Label();
            this.Label_DCS = new System.Windows.Forms.Label();
            this.textBox_SavedGames = new System.Windows.Forms.TextBox();
            this.button1 = new System.Windows.Forms.Button();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.label1 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.pictureBox3 = new System.Windows.Forms.PictureBox();
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.tabControl = new System.Windows.Forms.TabControl();
            this.tabPageLeft_Install = new System.Windows.Forms.TabPage();
            this.groupBox7 = new System.Windows.Forms.GroupBox();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.groupBox8 = new System.Windows.Forms.GroupBox();
            this.comboBox_Config = new System.Windows.Forms.ComboBox();
            this.m_Button_Config_Del = new System.Windows.Forms.Button();
            this.button6 = new System.Windows.Forms.Button();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.textBox_Config = new System.Windows.Forms.TextBox();
            this.m_Button_AddConfig = new System.Windows.Forms.Button();
            this.groupBoxCampEdit = new System.Windows.Forms.GroupBox();
            this.label4 = new System.Windows.Forms.Label();
            this.tabPageLeft_Campaigns = new System.Windows.Forms.TabPage();
            this.dataGridViewCampaigns = new System.Windows.Forms.DataGridView();
            this.tabPageLeft_Update = new System.Windows.Forms.TabPage();
            this.groupBox6 = new System.Windows.Forms.GroupBox();
            this.label15 = new System.Windows.Forms.Label();
            this.label16 = new System.Windows.Forms.Label();
            this.label_DCEM_Status = new System.Windows.Forms.Label();
            this.pictureBox_DCE_Manager_Status = new System.Windows.Forms.PictureBox();
            this.pictureBox_Update_DCE_Manager = new System.Windows.Forms.PictureBox();
            this.DCEManagerStatusLabel = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.DCEManagerInstalledVersion = new System.Windows.Forms.Label();
            this.DCEManagerAvailableVersion = new System.Windows.Forms.Label();
            this.DCEManagerUpdateButton = new System.Windows.Forms.Button();
            this.groupBox_DwlCampaign = new System.Windows.Forms.GroupBox();
            this.pictureBoxCampaignDownload = new System.Windows.Forms.PictureBox();
            this.labelCampaignDld_Pct = new System.Windows.Forms.Label();
            this.labelCampaignTitle = new System.Windows.Forms.Label();
            this.buttonCampaignCancel = new System.Windows.Forms.Button();
            this.labelCampaignDownload = new System.Windows.Forms.Label();
            this.progressBarCampaignDownload = new System.Windows.Forms.ProgressBar();
            this.CampaignDataGridView = new System.Windows.Forms.DataGridView();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.label_ScriptsMod_B = new System.Windows.Forms.Label();
            this.label_ScriptsMod_A = new System.Windows.Forms.Label();
            this.label_SM_Status = new System.Windows.Forms.Label();
            this.pictureBox_ScriptsMod_Status = new System.Windows.Forms.PictureBox();
            this.pictureBox_Update_ScriptsMod = new System.Windows.Forms.PictureBox();
            this.ScriptsModStatusLabel = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.ScriptModInstalledVersion = new System.Windows.Forms.Label();
            this.ScriptsModUpdateButton = new System.Windows.Forms.Button();
            this.ScriptsModAvailableVersion = new System.Windows.Forms.Label();
            this.tabPageLeft_About = new System.Windows.Forms.TabPage();
            this.linkLabel_Icons8 = new System.Windows.Forms.LinkLabel();
            this.label9 = new System.Windows.Forms.Label();
            this.textBox_ChangelogScriptsMod = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.textBox_changelog = new System.Windows.Forms.TextBox();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.tabPageLeftNews = new System.Windows.Forms.TabPage();
            this.panel_News = new System.Windows.Forms.Panel();
            this.textBox_News = new System.Windows.Forms.TextBox();
            this.tabPageLeft_Tools = new System.Windows.Forms.TabPage();
            this.textBox_ASTI = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.but_ASTI = new System.Windows.Forms.Button();
            this.button_theWay = new System.Windows.Forms.Button();
            this.tabPageLeft_Options = new System.Windows.Forms.TabPage();
            this.checkBox_Stat_anonym = new System.Windows.Forms.CheckBox();
            this.buttonDocFolder = new System.Windows.Forms.Button();
            this.button_Log = new System.Windows.Forms.Button();
            this.VersionDceManager = new System.Windows.Forms.Label();
            this.LabelStatut = new System.Windows.Forms.Label();
            this.pictureBox5 = new System.Windows.Forms.PictureBox();
            this.pictureBoxOvGME = new System.Windows.Forms.PictureBox();
            this.groupBoxDroiteAccueil = new System.Windows.Forms.GroupBox();
            this.textBox_id_client = new System.Windows.Forms.TextBox();
            this.butCampMaker = new System.Windows.Forms.Button();
            this.but_Expert = new System.Windows.Forms.Button();
            this.butClient = new System.Windows.Forms.Button();
            this.CampaignTab = new System.Windows.Forms.TabControl();
            this.tabPage6 = new System.Windows.Forms.TabPage();
            this.pictureBoxCampImage = new System.Windows.Forms.PictureBox();
            this.textBoxCampBriefing = new System.Windows.Forms.TextBox();
            this.tabPage14 = new System.Windows.Forms.TabPage();
            this.dataGridViewBlue = new System.Windows.Forms.DataGridView();
            this.tabPage15 = new System.Windows.Forms.TabPage();
            this.dataGridViewRed = new System.Windows.Forms.DataGridView();
            this.tabPage11 = new System.Windows.Forms.TabPage();
            this.tabPage12 = new System.Windows.Forms.TabPage();
            this.textBox_Bugs = new System.Windows.Forms.TextBox();
            this.radioButton_OOB_INIT = new System.Windows.Forms.RadioButton();
            this.radioButton_OOB_ACTIVE = new System.Windows.Forms.RadioButton();
            this.buttonResetBackup = new System.Windows.Forms.Button();
            this.buttonSaveChgtCampaign = new System.Windows.Forms.Button();
            this.toolTip3 = new System.Windows.Forms.ToolTip(this.components);
            this.groupBox_staticTemplate = new System.Windows.Forms.GroupBox();
            this.but_GPS_LL = new System.Windows.Forms.Button();
            this.but_ASTI_Open_templateFolder = new System.Windows.Forms.Button();
            this.but_ASTI_Process = new System.Windows.Forms.Button();
            this.but_ASTI_Browse_MissionFile = new System.Windows.Forms.Button();
            this.but_ASTI_Browse_Template = new System.Windows.Forms.Button();
            this.textBox_ASTI_MissionFile = new System.Windows.Forms.TextBox();
            this.textBox_ASTI_importTemplateFolder = new System.Windows.Forms.TextBox();
            this.panel2 = new System.Windows.Forms.Panel();
            this.panel_Droite = new System.Windows.Forms.Panel();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox3)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.tabControl.SuspendLayout();
            this.tabPageLeft_Install.SuspendLayout();
            this.groupBox7.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.groupBox8.SuspendLayout();
            this.groupBox5.SuspendLayout();
            this.groupBoxCampEdit.SuspendLayout();
            this.tabPageLeft_Campaigns.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewCampaigns)).BeginInit();
            this.tabPageLeft_Update.SuspendLayout();
            this.groupBox6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_DCE_Manager_Status)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Update_DCE_Manager)).BeginInit();
            this.groupBox_DwlCampaign.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampaignDownload)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.CampaignDataGridView)).BeginInit();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_ScriptsMod_Status)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Update_ScriptsMod)).BeginInit();
            this.tabPageLeft_About.SuspendLayout();
            this.tabPageLeftNews.SuspendLayout();
            this.tabPageLeft_Tools.SuspendLayout();
            this.tabPageLeft_Options.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox5)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxOvGME)).BeginInit();
            this.groupBoxDroiteAccueil.SuspendLayout();
            this.CampaignTab.SuspendLayout();
            this.tabPage6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampImage)).BeginInit();
            this.tabPage14.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewBlue)).BeginInit();
            this.tabPage15.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRed)).BeginInit();
            this.tabPage12.SuspendLayout();
            this.groupBox_staticTemplate.SuspendLayout();
            this.panel2.SuspendLayout();
            this.panel_Droite.SuspendLayout();
            this.SuspendLayout();
            // 
            // m_ButtonDcsPath
            // 
            this.m_ButtonDcsPath.Location = new System.Drawing.Point(475, 14);
            this.m_ButtonDcsPath.Name = "m_ButtonDcsPath";
            this.m_ButtonDcsPath.Size = new System.Drawing.Size(110, 28);
            this.m_ButtonDcsPath.TabIndex = 1;
            this.m_ButtonDcsPath.Text = "Browse...";
            this.m_ButtonDcsPath.UseVisualStyleBackColor = true;
            this.m_ButtonDcsPath.Click += new System.EventHandler(this.m_ButtonDcsPath_Click);
            // 
            // m_ButtonSavedGames
            // 
            this.m_ButtonSavedGames.Location = new System.Drawing.Point(475, 57);
            this.m_ButtonSavedGames.Name = "m_ButtonSavedGames";
            this.m_ButtonSavedGames.Size = new System.Drawing.Size(110, 28);
            this.m_ButtonSavedGames.TabIndex = 2;
            this.m_ButtonSavedGames.Text = "Browse...";
            this.m_ButtonSavedGames.UseVisualStyleBackColor = true;
            this.m_ButtonSavedGames.Click += new System.EventHandler(this.m_ButtonSavedGame_Click);
            // 
            // textBox_DCS
            // 
            this.textBox_DCS.Location = new System.Drawing.Point(147, 19);
            this.textBox_DCS.Name = "textBox_DCS";
            this.textBox_DCS.Size = new System.Drawing.Size(306, 20);
            this.textBox_DCS.TabIndex = 4;
            // 
            // groupBox1
            // 
            this.groupBox1.BackColor = System.Drawing.SystemColors.Control;
            this.groupBox1.Controls.Add(this.checkBoxSanitize);
            this.groupBox1.Controls.Add(this.checkBox_OvwNGfolder);
            this.groupBox1.Controls.Add(this.linkLabelCampaign);
            this.groupBox1.Controls.Add(this.checkBoxActiveFolder);
            this.groupBox1.Controls.Add(this.button_InstallCampaign);
            this.groupBox1.Controls.Add(this.Label_Campaign);
            this.groupBox1.Controls.Add(this.textBox_Campaign);
            this.groupBox1.Controls.Add(this.Button_choiceCampaign);
            this.groupBox1.Location = new System.Drawing.Point(86, 178);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(611, 118);
            this.groupBox1.TabIndex = 6;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Add a DCE campaign";
            // 
            // checkBoxSanitize
            // 
            this.checkBoxSanitize.AutoSize = true;
            this.checkBoxSanitize.Location = new System.Drawing.Point(14, 92);
            this.checkBoxSanitize.Name = "checkBoxSanitize";
            this.checkBoxSanitize.Size = new System.Drawing.Size(344, 17);
            this.checkBoxSanitize.TabIndex = 20;
            this.checkBoxSanitize.TabStop = false;
            this.checkBoxSanitize.Text = "MissionScripting Mod (incompatible Integrity Check)(don\'t work in C)";
            this.checkBoxSanitize.UseVisualStyleBackColor = true;
            this.checkBoxSanitize.Visible = false;
            this.checkBoxSanitize.CheckedChanged += new System.EventHandler(this.checkBoxSanitize_CheckedChanged);
            // 
            // checkBox_OvwNGfolder
            // 
            this.checkBox_OvwNGfolder.AutoSize = true;
            this.checkBox_OvwNGfolder.Location = new System.Drawing.Point(14, 69);
            this.checkBox_OvwNGfolder.Name = "checkBox_OvwNGfolder";
            this.checkBox_OvwNGfolder.Size = new System.Drawing.Size(226, 17);
            this.checkBox_OvwNGfolder.TabIndex = 18;
            this.checkBox_OvwNGfolder.Text = "Overwrite the NG folder (if already present)";
            this.checkBox_OvwNGfolder.UseVisualStyleBackColor = true;
            this.checkBox_OvwNGfolder.Visible = false;
            // 
            // linkLabelCampaign
            // 
            this.linkLabelCampaign.AutoSize = true;
            this.linkLabelCampaign.Location = new System.Drawing.Point(78, 22);
            this.linkLabelCampaign.Name = "linkLabelCampaign";
            this.linkLabelCampaign.Size = new System.Drawing.Size(54, 13);
            this.linkLabelCampaign.TabIndex = 15;
            this.linkLabelCampaign.TabStop = true;
            this.linkLabelCampaign.Text = "Campaign";
            this.linkLabelCampaign.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabelCampaign_LinkClicked);
            // 
            // checkBoxActiveFolder
            // 
            this.checkBoxActiveFolder.AutoSize = true;
            this.checkBoxActiveFolder.Location = new System.Drawing.Point(14, 46);
            this.checkBoxActiveFolder.Name = "checkBoxActiveFolder";
            this.checkBoxActiveFolder.Size = new System.Drawing.Size(316, 17);
            this.checkBoxActiveFolder.TabIndex = 17;
            this.checkBoxActiveFolder.Text = "Do not erase the progress of the campaign (if already present)";
            this.checkBoxActiveFolder.UseVisualStyleBackColor = true;
            this.checkBoxActiveFolder.Visible = false;
            // 
            // button_InstallCampaign
            // 
            this.button_InstallCampaign.Location = new System.Drawing.Point(475, 86);
            this.button_InstallCampaign.Name = "button_InstallCampaign";
            this.button_InstallCampaign.Size = new System.Drawing.Size(110, 23);
            this.button_InstallCampaign.TabIndex = 7;
            this.button_InstallCampaign.Text = "Install Campaign";
            this.button_InstallCampaign.UseVisualStyleBackColor = true;
            this.button_InstallCampaign.Click += new System.EventHandler(this.button_InstallCampaign_Click);
            // 
            // Label_Campaign
            // 
            this.Label_Campaign.AutoSize = true;
            this.Label_Campaign.Location = new System.Drawing.Point(11, 22);
            this.Label_Campaign.Name = "Label_Campaign";
            this.Label_Campaign.Size = new System.Drawing.Size(61, 13);
            this.Label_Campaign.TabIndex = 10;
            this.Label_Campaign.Text = "Choose Zip";
            // 
            // textBox_Campaign
            // 
            this.textBox_Campaign.Location = new System.Drawing.Point(147, 19);
            this.textBox_Campaign.Name = "textBox_Campaign";
            this.textBox_Campaign.Size = new System.Drawing.Size(306, 20);
            this.textBox_Campaign.TabIndex = 9;
            this.textBox_Campaign.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // Button_choiceCampaign
            // 
            this.Button_choiceCampaign.Location = new System.Drawing.Point(475, 14);
            this.Button_choiceCampaign.Name = "Button_choiceCampaign";
            this.Button_choiceCampaign.Size = new System.Drawing.Size(110, 28);
            this.Button_choiceCampaign.TabIndex = 8;
            this.Button_choiceCampaign.Text = "Browse...";
            this.Button_choiceCampaign.UseVisualStyleBackColor = true;
            this.Button_choiceCampaign.Click += new System.EventHandler(this.Button_choiceCampaign_Click);
            // 
            // linkLabelOvGME
            // 
            this.linkLabelOvGME.AutoSize = true;
            this.linkLabelOvGME.Location = new System.Drawing.Point(11, 108);
            this.linkLabelOvGME.Name = "linkLabelOvGME";
            this.linkLabelOvGME.Size = new System.Drawing.Size(45, 13);
            this.linkLabelOvGME.TabIndex = 14;
            this.linkLabelOvGME.TabStop = true;
            this.linkLabelOvGME.Text = "OvGME";
            this.linkLabelOvGME.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabelOvGME_LinkClicked);
            // 
            // Label_OvGME
            // 
            this.Label_OvGME.AutoSize = true;
            this.Label_OvGME.Location = new System.Drawing.Point(62, 108);
            this.Label_OvGME.Name = "Label_OvGME";
            this.Label_OvGME.Size = new System.Drawing.Size(60, 13);
            this.Label_OvGME.TabIndex = 13;
            this.Label_OvGME.Text = "Mod Folder";
            // 
            // textBox_OvGME
            // 
            this.textBox_OvGME.Location = new System.Drawing.Point(147, 105);
            this.textBox_OvGME.Name = "textBox_OvGME";
            this.textBox_OvGME.Size = new System.Drawing.Size(306, 20);
            this.textBox_OvGME.TabIndex = 12;
            this.textBox_OvGME.TextChanged += new System.EventHandler(this.textBox4_TextChanged);
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(475, 100);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(110, 28);
            this.button2.TabIndex = 11;
            this.button2.Text = "Browse...";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.m_buttonOvGME_Click);
            // 
            // Label_SavedGames
            // 
            this.Label_SavedGames.AutoSize = true;
            this.Label_SavedGames.Location = new System.Drawing.Point(11, 65);
            this.Label_SavedGames.Name = "Label_SavedGames";
            this.Label_SavedGames.Size = new System.Drawing.Size(131, 13);
            this.Label_SavedGames.TabIndex = 7;
            this.Label_SavedGames.Text = "DCS Saved Games Folder";
            // 
            // Label_DCS
            // 
            this.Label_DCS.AutoSize = true;
            this.Label_DCS.Location = new System.Drawing.Point(11, 22);
            this.Label_DCS.Name = "Label_DCS";
            this.Label_DCS.Size = new System.Drawing.Size(87, 13);
            this.Label_DCS.TabIndex = 6;
            this.Label_DCS.Text = "DCS Root Folder";
            // 
            // textBox_SavedGames
            // 
            this.textBox_SavedGames.Location = new System.Drawing.Point(147, 62);
            this.textBox_SavedGames.Name = "textBox_SavedGames";
            this.textBox_SavedGames.Size = new System.Drawing.Size(306, 20);
            this.textBox_SavedGames.TabIndex = 5;
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(345, 348);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 8;
            this.button1.Text = "Exit";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // toolTip1
            // 
            this.toolTip1.Popup += new System.Windows.Forms.PopupEventHandler(this.toolTip1_Popup);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(134, 193);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(61, 13);
            this.label1.TabIndex = 13;
            this.label1.Text = "Modified by";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(123, 222);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(94, 13);
            this.label3.TabIndex = 14;
            this.label3.Text = "CEF and Miguel21";
            // 
            // pictureBox3
            // 
            this.pictureBox3.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox3.Image")));
            this.pictureBox3.Location = new System.Drawing.Point(16, 193);
            this.pictureBox3.Name = "pictureBox3";
            this.pictureBox3.Size = new System.Drawing.Size(54, 51);
            this.pictureBox3.TabIndex = 12;
            this.pictureBox3.TabStop = false;
            // 
            // pictureBox2
            // 
            this.pictureBox2.Image = global::DCE_Manager.Properties.Resources.SPA3_tissue50b;
            this.pictureBox2.Location = new System.Drawing.Point(255, 184);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(50, 51);
            this.pictureBox2.TabIndex = 11;
            this.pictureBox2.TabStop = false;
            this.pictureBox2.Click += new System.EventHandler(this.pictureBox2_Click);
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Image = global::DCE_Manager.Properties.Resources.DCE_logo;
            this.pictureBox1.Location = new System.Drawing.Point(44, 36);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(232, 145);
            this.pictureBox1.TabIndex = 9;
            this.pictureBox1.TabStop = false;
            // 
            // tabControl
            // 
            this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.tabControl.Controls.Add(this.tabPageLeft_Install);
            this.tabControl.Controls.Add(this.tabPageLeft_Campaigns);
            this.tabControl.Controls.Add(this.tabPageLeft_Update);
            this.tabControl.Controls.Add(this.tabPageLeft_About);
            this.tabControl.Controls.Add(this.tabPageLeftNews);
            this.tabControl.Controls.Add(this.tabPageLeft_Tools);
            this.tabControl.Controls.Add(this.tabPageLeft_Options);
            this.tabControl.Location = new System.Drawing.Point(3, 11);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(816, 558);
            this.tabControl.TabIndex = 18;
            // 
            // tabPageLeft_Install
            // 
            this.tabPageLeft_Install.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeft_Install.Controls.Add(this.groupBox7);
            this.tabPageLeft_Install.Controls.Add(this.groupBox4);
            this.tabPageLeft_Install.Controls.Add(this.groupBox1);
            this.tabPageLeft_Install.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Install.Name = "tabPageLeft_Install";
            this.tabPageLeft_Install.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageLeft_Install.Size = new System.Drawing.Size(808, 532);
            this.tabPageLeft_Install.TabIndex = 0;
            this.tabPageLeft_Install.Text = "Install";
            // 
            // groupBox7
            // 
            this.groupBox7.Controls.Add(this.Label_DCS);
            this.groupBox7.Controls.Add(this.m_ButtonSavedGames);
            this.groupBox7.Controls.Add(this.m_ButtonDcsPath);
            this.groupBox7.Controls.Add(this.textBox_DCS);
            this.groupBox7.Controls.Add(this.linkLabelOvGME);
            this.groupBox7.Controls.Add(this.textBox_SavedGames);
            this.groupBox7.Controls.Add(this.Label_OvGME);
            this.groupBox7.Controls.Add(this.Label_SavedGames);
            this.groupBox7.Controls.Add(this.button2);
            this.groupBox7.Controls.Add(this.textBox_OvGME);
            this.groupBox7.Location = new System.Drawing.Point(86, 14);
            this.groupBox7.Name = "groupBox7";
            this.groupBox7.Size = new System.Drawing.Size(611, 140);
            this.groupBox7.TabIndex = 25;
            this.groupBox7.TabStop = false;
            this.groupBox7.Text = "Path required";
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.groupBox8);
            this.groupBox4.Controls.Add(this.groupBox5);
            this.groupBox4.Controls.Add(this.groupBoxCampEdit);
            this.groupBox4.Location = new System.Drawing.Point(242, 318);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(323, 104);
            this.groupBox4.TabIndex = 22;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "Config Name (optional)";
            this.groupBox4.Visible = false;
            // 
            // groupBox8
            // 
            this.groupBox8.Controls.Add(this.comboBox_Config);
            this.groupBox8.Controls.Add(this.m_Button_Config_Del);
            this.groupBox8.Controls.Add(this.button6);
            this.groupBox8.Location = new System.Drawing.Point(6, 57);
            this.groupBox8.Name = "groupBox8";
            this.groupBox8.Size = new System.Drawing.Size(276, 40);
            this.groupBox8.TabIndex = 27;
            this.groupBox8.TabStop = false;
            // 
            // comboBox_Config
            // 
            this.comboBox_Config.FormattingEnabled = true;
            this.comboBox_Config.Location = new System.Drawing.Point(6, 12);
            this.comboBox_Config.Name = "comboBox_Config";
            this.comboBox_Config.Size = new System.Drawing.Size(121, 21);
            this.comboBox_Config.TabIndex = 18;
            this.comboBox_Config.SelectedIndexChanged += new System.EventHandler(this.comboBox_Config_SelectedIndexChanged);
            // 
            // m_Button_Config_Del
            // 
            this.m_Button_Config_Del.Location = new System.Drawing.Point(200, 11);
            this.m_Button_Config_Del.Name = "m_Button_Config_Del";
            this.m_Button_Config_Del.Size = new System.Drawing.Size(63, 20);
            this.m_Button_Config_Del.TabIndex = 21;
            this.m_Button_Config_Del.Text = "Delete";
            this.m_Button_Config_Del.UseVisualStyleBackColor = true;
            this.m_Button_Config_Del.Click += new System.EventHandler(this.m_Button_Config_Del_Click);
            // 
            // button6
            // 
            this.button6.Location = new System.Drawing.Point(133, 11);
            this.button6.Name = "button6";
            this.button6.Size = new System.Drawing.Size(63, 20);
            this.button6.TabIndex = 22;
            this.button6.Text = "Edit/Save";
            this.button6.UseVisualStyleBackColor = true;
            this.button6.Click += new System.EventHandler(this.but_EditConfig_Click);
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.textBox_Config);
            this.groupBox5.Controls.Add(this.m_Button_AddConfig);
            this.groupBox5.Location = new System.Drawing.Point(6, 19);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(200, 40);
            this.groupBox5.TabIndex = 26;
            this.groupBox5.TabStop = false;
            // 
            // textBox_Config
            // 
            this.textBox_Config.Location = new System.Drawing.Point(6, 12);
            this.textBox_Config.Name = "textBox_Config";
            this.textBox_Config.Size = new System.Drawing.Size(121, 20);
            this.textBox_Config.TabIndex = 19;
            // 
            // m_Button_AddConfig
            // 
            this.m_Button_AddConfig.Location = new System.Drawing.Point(133, 12);
            this.m_Button_AddConfig.Name = "m_Button_AddConfig";
            this.m_Button_AddConfig.Size = new System.Drawing.Size(63, 20);
            this.m_Button_AddConfig.TabIndex = 20;
            this.m_Button_AddConfig.Text = "New";
            this.m_Button_AddConfig.UseVisualStyleBackColor = true;
            this.m_Button_AddConfig.Click += new System.EventHandler(this.m_Button_AddConfig_Click);
            // 
            // groupBoxCampEdit
            // 
            this.groupBoxCampEdit.Controls.Add(this.label4);
            this.groupBoxCampEdit.Location = new System.Drawing.Point(319, 82);
            this.groupBoxCampEdit.Name = "groupBoxCampEdit";
            this.groupBoxCampEdit.Size = new System.Drawing.Size(280, 263);
            this.groupBoxCampEdit.TabIndex = 24;
            this.groupBoxCampEdit.TabStop = false;
            this.groupBoxCampEdit.Visible = false;
            this.groupBoxCampEdit.Enter += new System.EventHandler(this.groupBoxCampEdit_Enter);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(184, 377);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(0, 13);
            this.label4.TabIndex = 10;
            // 
            // tabPageLeft_Campaigns
            // 
            this.tabPageLeft_Campaigns.Controls.Add(this.dataGridViewCampaigns);
            this.tabPageLeft_Campaigns.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Campaigns.Margin = new System.Windows.Forms.Padding(2);
            this.tabPageLeft_Campaigns.Name = "tabPageLeft_Campaigns";
            this.tabPageLeft_Campaigns.Padding = new System.Windows.Forms.Padding(2);
            this.tabPageLeft_Campaigns.Size = new System.Drawing.Size(808, 532);
            this.tabPageLeft_Campaigns.TabIndex = 6;
            this.tabPageLeft_Campaigns.Text = "Campaigns";
            this.tabPageLeft_Campaigns.UseVisualStyleBackColor = true;
            // 
            // dataGridViewCampaigns
            // 
            this.dataGridViewCampaigns.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.dataGridViewCampaigns.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridViewCampaigns.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewCampaigns.Location = new System.Drawing.Point(2, 2);
            this.dataGridViewCampaigns.Margin = new System.Windows.Forms.Padding(2);
            this.dataGridViewCampaigns.MultiSelect = false;
            this.dataGridViewCampaigns.Name = "dataGridViewCampaigns";
            this.dataGridViewCampaigns.ReadOnly = true;
            this.dataGridViewCampaigns.RowHeadersWidth = 51;
            this.dataGridViewCampaigns.RowTemplate.Height = 80;
            this.dataGridViewCampaigns.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dataGridViewCampaigns.Size = new System.Drawing.Size(806, 528);
            this.dataGridViewCampaigns.TabIndex = 0;
            // 
            // tabPageLeft_Update
            // 
            this.tabPageLeft_Update.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeft_Update.Controls.Add(this.groupBox6);
            this.tabPageLeft_Update.Controls.Add(this.groupBox_DwlCampaign);
            this.tabPageLeft_Update.Controls.Add(this.groupBox2);
            this.tabPageLeft_Update.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Update.Name = "tabPageLeft_Update";
            this.tabPageLeft_Update.Size = new System.Drawing.Size(808, 532);
            this.tabPageLeft_Update.TabIndex = 2;
            this.tabPageLeft_Update.Text = "Update";
            // 
            // groupBox6
            // 
            this.groupBox6.Controls.Add(this.label15);
            this.groupBox6.Controls.Add(this.label16);
            this.groupBox6.Controls.Add(this.label_DCEM_Status);
            this.groupBox6.Controls.Add(this.pictureBox_DCE_Manager_Status);
            this.groupBox6.Controls.Add(this.pictureBox_Update_DCE_Manager);
            this.groupBox6.Controls.Add(this.DCEManagerStatusLabel);
            this.groupBox6.Controls.Add(this.label13);
            this.groupBox6.Controls.Add(this.label14);
            this.groupBox6.Controls.Add(this.DCEManagerInstalledVersion);
            this.groupBox6.Controls.Add(this.DCEManagerAvailableVersion);
            this.groupBox6.Controls.Add(this.DCEManagerUpdateButton);
            this.groupBox6.Location = new System.Drawing.Point(15, 114);
            this.groupBox6.Name = "groupBox6";
            this.groupBox6.Size = new System.Drawing.Size(775, 84);
            this.groupBox6.TabIndex = 4;
            this.groupBox6.TabStop = false;
            this.groupBox6.Enter += new System.EventHandler(this.groupBox6_Enter);
            // 
            // label15
            // 
            this.label15.AutoSize = true;
            this.label15.Font = new System.Drawing.Font("Segoe UI Variable Display", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label15.Location = new System.Drawing.Point(104, 47);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(68, 16);
            this.label15.TabIndex = 13;
            this.label15.Text = "Application";
            // 
            // label16
            // 
            this.label16.AutoSize = true;
            this.label16.Font = new System.Drawing.Font("Segoe UI Variable Display", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label16.Location = new System.Drawing.Point(92, 22);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(105, 20);
            this.label16.TabIndex = 12;
            this.label16.Text = "DCE Manager";
            // 
            // label_DCEM_Status
            // 
            this.label_DCEM_Status.AutoSize = true;
            this.label_DCEM_Status.Font = new System.Drawing.Font("Segoe UI Variable Display", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_DCEM_Status.Location = new System.Drawing.Point(500, 27);
            this.label_DCEM_Status.Name = "label_DCEM_Status";
            this.label_DCEM_Status.Size = new System.Drawing.Size(42, 15);
            this.label_DCEM_Status.TabIndex = 10;
            this.label_DCEM_Status.Text = "Status";
            // 
            // pictureBox_DCE_Manager_Status
            // 
            this.pictureBox_DCE_Manager_Status.Image = global::DCE_Manager.Properties.Resources.icons8_warning_blue_30;
            this.pictureBox_DCE_Manager_Status.Location = new System.Drawing.Point(503, 49);
            this.pictureBox_DCE_Manager_Status.Name = "pictureBox_DCE_Manager_Status";
            this.pictureBox_DCE_Manager_Status.Size = new System.Drawing.Size(28, 20);
            this.pictureBox_DCE_Manager_Status.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox_DCE_Manager_Status.TabIndex = 9;
            this.pictureBox_DCE_Manager_Status.TabStop = false;
            // 
            // pictureBox_Update_DCE_Manager
            // 
            this.pictureBox_Update_DCE_Manager.Image = global::DCE_Manager.Properties.Resources.DCE_logo;
            this.pictureBox_Update_DCE_Manager.Location = new System.Drawing.Point(16, 19);
            this.pictureBox_Update_DCE_Manager.Name = "pictureBox_Update_DCE_Manager";
            this.pictureBox_Update_DCE_Manager.Size = new System.Drawing.Size(57, 57);
            this.pictureBox_Update_DCE_Manager.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox_Update_DCE_Manager.TabIndex = 7;
            this.pictureBox_Update_DCE_Manager.TabStop = false;
            // 
            // DCEManagerStatusLabel
            // 
            this.DCEManagerStatusLabel.AutoSize = true;
            this.DCEManagerStatusLabel.Location = new System.Drawing.Point(537, 53);
            this.DCEManagerStatusLabel.Name = "DCEManagerStatusLabel";
            this.DCEManagerStatusLabel.Size = new System.Drawing.Size(53, 13);
            this.DCEManagerStatusLabel.TabIndex = 6;
            this.DCEManagerStatusLabel.Text = "Unknown";
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Font = new System.Drawing.Font("Segoe UI Variable Display", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label13.Location = new System.Drawing.Point(246, 27);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(52, 15);
            this.label13.TabIndex = 5;
            this.label13.Text = "Installed";
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Font = new System.Drawing.Font("Segoe UI Variable Display", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label14.Location = new System.Drawing.Point(380, 27);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(55, 15);
            this.label14.TabIndex = 4;
            this.label14.Text = "Available";
            // 
            // DCEManagerInstalledVersion
            // 
            this.DCEManagerInstalledVersion.AutoSize = true;
            this.DCEManagerInstalledVersion.Location = new System.Drawing.Point(246, 53);
            this.DCEManagerInstalledVersion.Name = "DCEManagerInstalledVersion";
            this.DCEManagerInstalledVersion.Size = new System.Drawing.Size(16, 13);
            this.DCEManagerInstalledVersion.TabIndex = 3;
            this.DCEManagerInstalledVersion.Text = "...";
            // 
            // DCEManagerAvailableVersion
            // 
            this.DCEManagerAvailableVersion.AutoSize = true;
            this.DCEManagerAvailableVersion.Location = new System.Drawing.Point(380, 53);
            this.DCEManagerAvailableVersion.Name = "DCEManagerAvailableVersion";
            this.DCEManagerAvailableVersion.Size = new System.Drawing.Size(16, 13);
            this.DCEManagerAvailableVersion.TabIndex = 2;
            this.DCEManagerAvailableVersion.Text = "...";
            this.DCEManagerAvailableVersion.Click += new System.EventHandler(this.labelUpdateDceManager_Click);
            // 
            // DCEManagerUpdateButton
            // 
            this.DCEManagerUpdateButton.BackColor = System.Drawing.Color.White;
            this.DCEManagerUpdateButton.FlatAppearance.BorderColor = System.Drawing.Color.Silver;
            this.DCEManagerUpdateButton.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightGray;
            this.DCEManagerUpdateButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Gainsboro;
            this.DCEManagerUpdateButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.DCEManagerUpdateButton.ForeColor = System.Drawing.Color.Black;
            this.DCEManagerUpdateButton.Location = new System.Drawing.Point(660, 46);
            this.DCEManagerUpdateButton.Name = "DCEManagerUpdateButton";
            this.DCEManagerUpdateButton.Size = new System.Drawing.Size(110, 30);
            this.DCEManagerUpdateButton.TabIndex = 0;
            this.DCEManagerUpdateButton.Text = "Update && Install";
            this.DCEManagerUpdateButton.UseVisualStyleBackColor = false;
            this.DCEManagerUpdateButton.Visible = false;
            this.DCEManagerUpdateButton.Click += new System.EventHandler(this.DCEManagerUpdateButton_Click);
            // 
            // groupBox_DwlCampaign
            // 
            this.groupBox_DwlCampaign.Controls.Add(this.pictureBoxCampaignDownload);
            this.groupBox_DwlCampaign.Controls.Add(this.labelCampaignDld_Pct);
            this.groupBox_DwlCampaign.Controls.Add(this.labelCampaignTitle);
            this.groupBox_DwlCampaign.Controls.Add(this.buttonCampaignCancel);
            this.groupBox_DwlCampaign.Controls.Add(this.labelCampaignDownload);
            this.groupBox_DwlCampaign.Controls.Add(this.progressBarCampaignDownload);
            this.groupBox_DwlCampaign.Controls.Add(this.CampaignDataGridView);
            this.groupBox_DwlCampaign.Location = new System.Drawing.Point(15, 217);
            this.groupBox_DwlCampaign.Name = "groupBox_DwlCampaign";
            this.groupBox_DwlCampaign.Size = new System.Drawing.Size(775, 300);
            this.groupBox_DwlCampaign.TabIndex = 3;
            this.groupBox_DwlCampaign.TabStop = false;
            this.groupBox_DwlCampaign.Text = "Campaign";
            this.groupBox_DwlCampaign.Enter += new System.EventHandler(this.groupBox_UpdateCampaign_Enter);
            // 
            // pictureBoxCampaignDownload
            // 
            this.pictureBoxCampaignDownload.Image = global::DCE_Manager.Properties.Resources.icons8_download_cloud_64;
            this.pictureBoxCampaignDownload.Location = new System.Drawing.Point(5, 237);
            this.pictureBoxCampaignDownload.Name = "pictureBoxCampaignDownload";
            this.pictureBoxCampaignDownload.Size = new System.Drawing.Size(57, 40);
            this.pictureBoxCampaignDownload.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxCampaignDownload.TabIndex = 6;
            this.pictureBoxCampaignDownload.TabStop = false;
            this.pictureBoxCampaignDownload.Visible = false;
            // 
            // labelCampaignDld_Pct
            // 
            this.labelCampaignDld_Pct.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.labelCampaignDld_Pct.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labelCampaignDld_Pct.Location = new System.Drawing.Point(605, 219);
            this.labelCampaignDld_Pct.Name = "labelCampaignDld_Pct";
            this.labelCampaignDld_Pct.Size = new System.Drawing.Size(35, 13);
            this.labelCampaignDld_Pct.TabIndex = 5;
            this.labelCampaignDld_Pct.Visible = false;
            // 
            // labelCampaignTitle
            // 
            this.labelCampaignTitle.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.labelCampaignTitle.Location = new System.Drawing.Point(69, 219);
            this.labelCampaignTitle.Name = "labelCampaignTitle";
            this.labelCampaignTitle.Size = new System.Drawing.Size(350, 13);
            this.labelCampaignTitle.TabIndex = 4;
            this.labelCampaignTitle.Visible = false;
            this.labelCampaignTitle.Click += new System.EventHandler(this.labelCampaignTitle_Click);
            // 
            // buttonCampaignCancel
            // 
            this.buttonCampaignCancel.BackColor = System.Drawing.Color.White;
            this.buttonCampaignCancel.FlatAppearance.BorderColor = System.Drawing.Color.Silver;
            this.buttonCampaignCancel.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightGray;
            this.buttonCampaignCancel.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Gainsboro;
            this.buttonCampaignCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.buttonCampaignCancel.ForeColor = System.Drawing.Color.Black;
            this.buttonCampaignCancel.Location = new System.Drawing.Point(660, 238);
            this.buttonCampaignCancel.Margin = new System.Windows.Forms.Padding(2);
            this.buttonCampaignCancel.Name = "buttonCampaignCancel";
            this.buttonCampaignCancel.Size = new System.Drawing.Size(110, 30);
            this.buttonCampaignCancel.TabIndex = 3;
            this.buttonCampaignCancel.Text = "Cancel";
            this.buttonCampaignCancel.UseVisualStyleBackColor = false;
            this.buttonCampaignCancel.Visible = false;
            this.buttonCampaignCancel.Click += new System.EventHandler(this.buttonCampaignCancel_Click);
            // 
            // labelCampaignDownload
            // 
            this.labelCampaignDownload.AutoSize = true;
            this.labelCampaignDownload.Location = new System.Drawing.Point(139, 282);
            this.labelCampaignDownload.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.labelCampaignDownload.Name = "labelCampaignDownload";
            this.labelCampaignDownload.Size = new System.Drawing.Size(0, 13);
            this.labelCampaignDownload.TabIndex = 2;
            this.labelCampaignDownload.Visible = false;
            // 
            // progressBarCampaignDownload
            // 
            this.progressBarCampaignDownload.Location = new System.Drawing.Point(69, 249);
            this.progressBarCampaignDownload.Margin = new System.Windows.Forms.Padding(2);
            this.progressBarCampaignDownload.Name = "progressBarCampaignDownload";
            this.progressBarCampaignDownload.Size = new System.Drawing.Size(571, 19);
            this.progressBarCampaignDownload.TabIndex = 1;
            this.progressBarCampaignDownload.Visible = false;
            // 
            // CampaignDataGridView
            // 
            this.CampaignDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.CampaignDataGridView.Location = new System.Drawing.Point(5, 18);
            this.CampaignDataGridView.Margin = new System.Windows.Forms.Padding(2);
            this.CampaignDataGridView.Name = "CampaignDataGridView";
            this.CampaignDataGridView.RowHeadersWidth = 51;
            this.CampaignDataGridView.RowTemplate.Height = 24;
            this.CampaignDataGridView.Size = new System.Drawing.Size(765, 195);
            this.CampaignDataGridView.TabIndex = 0;
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.label_ScriptsMod_B);
            this.groupBox2.Controls.Add(this.label_ScriptsMod_A);
            this.groupBox2.Controls.Add(this.label_SM_Status);
            this.groupBox2.Controls.Add(this.pictureBox_ScriptsMod_Status);
            this.groupBox2.Controls.Add(this.pictureBox_Update_ScriptsMod);
            this.groupBox2.Controls.Add(this.ScriptsModStatusLabel);
            this.groupBox2.Controls.Add(this.label12);
            this.groupBox2.Controls.Add(this.label11);
            this.groupBox2.Controls.Add(this.ScriptModInstalledVersion);
            this.groupBox2.Controls.Add(this.ScriptsModUpdateButton);
            this.groupBox2.Controls.Add(this.ScriptsModAvailableVersion);
            this.groupBox2.Location = new System.Drawing.Point(15, 15);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(775, 93);
            this.groupBox2.TabIndex = 2;
            this.groupBox2.TabStop = false;
            // 
            // label_ScriptsMod_B
            // 
            this.label_ScriptsMod_B.AutoSize = true;
            this.label_ScriptsMod_B.Font = new System.Drawing.Font("Segoe UI Variable Display", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_ScriptsMod_B.Location = new System.Drawing.Point(104, 48);
            this.label_ScriptsMod_B.Name = "label_ScriptsMod_B";
            this.label_ScriptsMod_B.Size = new System.Drawing.Size(114, 16);
            this.label_ScriptsMod_B.TabIndex = 11;
            this.label_ScriptsMod_B.Text = "Core mission engine";
            // 
            // label_ScriptsMod_A
            // 
            this.label_ScriptsMod_A.AutoSize = true;
            this.label_ScriptsMod_A.Font = new System.Drawing.Font("Segoe UI Variable Display", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_ScriptsMod_A.Location = new System.Drawing.Point(92, 23);
            this.label_ScriptsMod_A.Name = "label_ScriptsMod_A";
            this.label_ScriptsMod_A.Size = new System.Drawing.Size(90, 20);
            this.label_ScriptsMod_A.TabIndex = 10;
            this.label_ScriptsMod_A.Text = "ScriptsMod";
            // 
            // label_SM_Status
            // 
            this.label_SM_Status.AutoSize = true;
            this.label_SM_Status.Font = new System.Drawing.Font("Segoe UI Variable Display", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_SM_Status.Location = new System.Drawing.Point(500, 28);
            this.label_SM_Status.Name = "label_SM_Status";
            this.label_SM_Status.Size = new System.Drawing.Size(42, 15);
            this.label_SM_Status.TabIndex = 9;
            this.label_SM_Status.Text = "Status";
            // 
            // pictureBox_ScriptsMod_Status
            // 
            this.pictureBox_ScriptsMod_Status.Image = global::DCE_Manager.Properties.Resources.icons8_warning_blue_30;
            this.pictureBox_ScriptsMod_Status.Location = new System.Drawing.Point(503, 51);
            this.pictureBox_ScriptsMod_Status.Name = "pictureBox_ScriptsMod_Status";
            this.pictureBox_ScriptsMod_Status.Size = new System.Drawing.Size(28, 20);
            this.pictureBox_ScriptsMod_Status.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox_ScriptsMod_Status.TabIndex = 8;
            this.pictureBox_ScriptsMod_Status.TabStop = false;
            // 
            // pictureBox_Update_ScriptsMod
            // 
            this.pictureBox_Update_ScriptsMod.Image = global::DCE_Manager.Properties.Resources.icons8_engrenages_50;
            this.pictureBox_Update_ScriptsMod.Location = new System.Drawing.Point(16, 23);
            this.pictureBox_Update_ScriptsMod.Name = "pictureBox_Update_ScriptsMod";
            this.pictureBox_Update_ScriptsMod.Size = new System.Drawing.Size(57, 57);
            this.pictureBox_Update_ScriptsMod.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox_Update_ScriptsMod.TabIndex = 7;
            this.pictureBox_Update_ScriptsMod.TabStop = false;
            // 
            // ScriptsModStatusLabel
            // 
            this.ScriptsModStatusLabel.AutoSize = true;
            this.ScriptsModStatusLabel.Location = new System.Drawing.Point(537, 54);
            this.ScriptsModStatusLabel.Name = "ScriptsModStatusLabel";
            this.ScriptsModStatusLabel.Size = new System.Drawing.Size(53, 13);
            this.ScriptsModStatusLabel.TabIndex = 5;
            this.ScriptsModStatusLabel.Text = "Unknown";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Font = new System.Drawing.Font("Segoe UI Variable Text", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label12.Location = new System.Drawing.Point(380, 28);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(55, 15);
            this.label12.TabIndex = 4;
            this.label12.Text = "Available";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Font = new System.Drawing.Font("Segoe UI Variable Display", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label11.Location = new System.Drawing.Point(246, 28);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(52, 15);
            this.label11.TabIndex = 3;
            this.label11.Text = "Installed";
            this.label11.Click += new System.EventHandler(this.label11_Click);
            // 
            // ScriptModInstalledVersion
            // 
            this.ScriptModInstalledVersion.AutoSize = true;
            this.ScriptModInstalledVersion.Cursor = System.Windows.Forms.Cursors.Hand;
            this.ScriptModInstalledVersion.Location = new System.Drawing.Point(246, 53);
            this.ScriptModInstalledVersion.Name = "ScriptModInstalledVersion";
            this.ScriptModInstalledVersion.Size = new System.Drawing.Size(16, 13);
            this.ScriptModInstalledVersion.TabIndex = 2;
            this.ScriptModInstalledVersion.Text = "...";
            this.ScriptModInstalledVersion.Click += new System.EventHandler(this.ScriptModInstalledVersion_Click);
            // 
            // ScriptsModUpdateButton
            // 
            this.ScriptsModUpdateButton.BackColor = System.Drawing.Color.White;
            this.ScriptsModUpdateButton.FlatAppearance.BorderColor = System.Drawing.Color.Silver;
            this.ScriptsModUpdateButton.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightGray;
            this.ScriptsModUpdateButton.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Gainsboro;
            this.ScriptsModUpdateButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.ScriptsModUpdateButton.ForeColor = System.Drawing.Color.Black;
            this.ScriptsModUpdateButton.Location = new System.Drawing.Point(660, 48);
            this.ScriptsModUpdateButton.Name = "ScriptsModUpdateButton";
            this.ScriptsModUpdateButton.Size = new System.Drawing.Size(110, 30);
            this.ScriptsModUpdateButton.TabIndex = 0;
            this.ScriptsModUpdateButton.Text = "Update && Install";
            this.ScriptsModUpdateButton.UseVisualStyleBackColor = false;
            this.ScriptsModUpdateButton.Visible = false;
            this.ScriptsModUpdateButton.Click += new System.EventHandler(this.ScriptsModUpdateButton_Click);
            // 
            // ScriptsModAvailableVersion
            // 
            this.ScriptsModAvailableVersion.AutoSize = true;
            this.ScriptsModAvailableVersion.Location = new System.Drawing.Point(380, 53);
            this.ScriptsModAvailableVersion.Name = "ScriptsModAvailableVersion";
            this.ScriptsModAvailableVersion.Size = new System.Drawing.Size(16, 13);
            this.ScriptsModAvailableVersion.TabIndex = 1;
            this.ScriptsModAvailableVersion.Text = "...";
            this.ScriptsModAvailableVersion.Click += new System.EventHandler(this.label5_Click);
            // 
            // tabPageLeft_About
            // 
            this.tabPageLeft_About.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeft_About.Controls.Add(this.linkLabel_Icons8);
            this.tabPageLeft_About.Controls.Add(this.label9);
            this.tabPageLeft_About.Controls.Add(this.textBox_ChangelogScriptsMod);
            this.tabPageLeft_About.Controls.Add(this.label8);
            this.tabPageLeft_About.Controls.Add(this.textBox_changelog);
            this.tabPageLeft_About.Controls.Add(this.textBox1);
            this.tabPageLeft_About.Controls.Add(this.label5);
            this.tabPageLeft_About.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_About.Name = "tabPageLeft_About";
            this.tabPageLeft_About.Size = new System.Drawing.Size(808, 532);
            this.tabPageLeft_About.TabIndex = 3;
            this.tabPageLeft_About.Text = "About";
            // 
            // linkLabel_Icons8
            // 
            this.linkLabel_Icons8.AutoSize = true;
            this.linkLabel_Icons8.Location = new System.Drawing.Point(46, 457);
            this.linkLabel_Icons8.Name = "linkLabel_Icons8";
            this.linkLabel_Icons8.Size = new System.Drawing.Size(92, 13);
            this.linkLabel_Icons8.TabIndex = 6;
            this.linkLabel_Icons8.TabStop = true;
            this.linkLabel_Icons8.Text = "Icônes par Icons8";
            this.linkLabel_Icons8.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel_Icons8_LinkClicked);
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(43, 232);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(139, 13);
            this.label9.TabIndex = 5;
            this.label9.Text = "Changelog (ScriptsMod.NG)";
            this.label9.Click += new System.EventHandler(this.label9_Click);
            // 
            // textBox_ChangelogScriptsMod
            // 
            this.textBox_ChangelogScriptsMod.Location = new System.Drawing.Point(45, 248);
            this.textBox_ChangelogScriptsMod.Multiline = true;
            this.textBox_ChangelogScriptsMod.Name = "textBox_ChangelogScriptsMod";
            this.textBox_ChangelogScriptsMod.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_ChangelogScriptsMod.Size = new System.Drawing.Size(731, 153);
            this.textBox_ChangelogScriptsMod.TabIndex = 4;
            this.textBox_ChangelogScriptsMod.TextChanged += new System.EventHandler(this.textBox_ChangelogScriptsMod_TextChanged);
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(42, 123);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(157, 13);
            this.label8.TabIndex = 3;
            this.label8.Text = "Changelog (DCE_Manager.exe)";
            // 
            // textBox_changelog
            // 
            this.textBox_changelog.Location = new System.Drawing.Point(45, 139);
            this.textBox_changelog.Multiline = true;
            this.textBox_changelog.Name = "textBox_changelog";
            this.textBox_changelog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_changelog.Size = new System.Drawing.Size(731, 90);
            this.textBox_changelog.TabIndex = 2;
            // 
            // textBox1
            // 
            this.textBox1.Location = new System.Drawing.Point(45, 20);
            this.textBox1.Multiline = true;
            this.textBox1.Name = "textBox1";
            this.textBox1.Size = new System.Drawing.Size(488, 97);
            this.textBox1.TabIndex = 1;
            this.textBox1.Text = resources.GetString("textBox1.Text");
            this.textBox1.TextChanged += new System.EventHandler(this.textBox1_TextChanged_2);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(43, 4);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(45, 13);
            this.label5.TabIndex = 0;
            this.label5.Text = "Credits :";
            // 
            // tabPageLeftNews
            // 
            this.tabPageLeftNews.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeftNews.Controls.Add(this.panel_News);
            this.tabPageLeftNews.Controls.Add(this.textBox_News);
            this.tabPageLeftNews.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeftNews.Name = "tabPageLeftNews";
            this.tabPageLeftNews.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageLeftNews.Size = new System.Drawing.Size(808, 532);
            this.tabPageLeftNews.TabIndex = 4;
            this.tabPageLeftNews.Text = "News";
            // 
            // panel_News
            // 
            this.panel_News.AutoScroll = true;
            this.panel_News.BackColor = System.Drawing.SystemColors.Window;
            this.panel_News.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel_News.Location = new System.Drawing.Point(35, 14);
            this.panel_News.Name = "panel_News";
            this.panel_News.Size = new System.Drawing.Size(692, 377);
            this.panel_News.TabIndex = 4;
            // 
            // textBox_News
            // 
            this.textBox_News.Location = new System.Drawing.Point(46, 29);
            this.textBox_News.Multiline = true;
            this.textBox_News.Name = "textBox_News";
            this.textBox_News.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_News.Size = new System.Drawing.Size(672, 343);
            this.textBox_News.TabIndex = 3;
            // 
            // tabPageLeft_Tools
            // 
            this.tabPageLeft_Tools.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeft_Tools.Controls.Add(this.textBox_ASTI);
            this.tabPageLeft_Tools.Controls.Add(this.label6);
            this.tabPageLeft_Tools.Controls.Add(this.label2);
            this.tabPageLeft_Tools.Controls.Add(this.but_ASTI);
            this.tabPageLeft_Tools.Controls.Add(this.button_theWay);
            this.tabPageLeft_Tools.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Tools.Name = "tabPageLeft_Tools";
            this.tabPageLeft_Tools.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageLeft_Tools.Size = new System.Drawing.Size(808, 532);
            this.tabPageLeft_Tools.TabIndex = 5;
            this.tabPageLeft_Tools.Text = "Tools";
            // 
            // textBox_ASTI
            // 
            this.textBox_ASTI.Location = new System.Drawing.Point(239, 174);
            this.textBox_ASTI.Multiline = true;
            this.textBox_ASTI.Name = "textBox_ASTI";
            this.textBox_ASTI.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textBox_ASTI.Size = new System.Drawing.Size(563, 235);
            this.textBox_ASTI.TabIndex = 4;
            this.textBox_ASTI.Text = resources.GetString("textBox_ASTI.Text");
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(236, 32);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(272, 13);
            this.label6.TabIndex = 3;
            this.label6.Text = "transforms LatLon positions into The Way compatbile file";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(236, 157);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(136, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "All Static Template Importer";
            // 
            // but_ASTI
            // 
            this.but_ASTI.Location = new System.Drawing.Point(29, 152);
            this.but_ASTI.Name = "but_ASTI";
            this.but_ASTI.Size = new System.Drawing.Size(158, 23);
            this.but_ASTI.TabIndex = 1;
            this.but_ASTI.Text = "ASTI";
            this.but_ASTI.UseVisualStyleBackColor = true;
            // 
            // button_theWay
            // 
            this.button_theWay.Location = new System.Drawing.Point(29, 29);
            this.button_theWay.Name = "button_theWay";
            this.button_theWay.Size = new System.Drawing.Size(158, 23);
            this.button_theWay.TabIndex = 0;
            this.button_theWay.Text = "The Way (futur)";
            this.button_theWay.UseVisualStyleBackColor = true;
            // 
            // tabPageLeft_Options
            // 
            this.tabPageLeft_Options.Controls.Add(this.checkBox_Stat_anonym);
            this.tabPageLeft_Options.Controls.Add(this.buttonDocFolder);
            this.tabPageLeft_Options.Controls.Add(this.button_Log);
            this.tabPageLeft_Options.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Options.Margin = new System.Windows.Forms.Padding(2);
            this.tabPageLeft_Options.Name = "tabPageLeft_Options";
            this.tabPageLeft_Options.Padding = new System.Windows.Forms.Padding(2);
            this.tabPageLeft_Options.Size = new System.Drawing.Size(808, 532);
            this.tabPageLeft_Options.TabIndex = 7;
            this.tabPageLeft_Options.Text = "Options";
            this.tabPageLeft_Options.UseVisualStyleBackColor = true;
            // 
            // checkBox_Stat_anonym
            // 
            this.checkBox_Stat_anonym.AutoSize = true;
            this.checkBox_Stat_anonym.Location = new System.Drawing.Point(35, 46);
            this.checkBox_Stat_anonym.Margin = new System.Windows.Forms.Padding(2);
            this.checkBox_Stat_anonym.Name = "checkBox_Stat_anonym";
            this.checkBox_Stat_anonym.Size = new System.Drawing.Size(183, 17);
            this.checkBox_Stat_anonym.TabIndex = 34;
            this.checkBox_Stat_anonym.Text = "Send anonymous usage statistics";
            this.checkBox_Stat_anonym.UseVisualStyleBackColor = true;
            // 
            // buttonDocFolder
            // 
            this.buttonDocFolder.Location = new System.Drawing.Point(35, 134);
            this.buttonDocFolder.Name = "buttonDocFolder";
            this.buttonDocFolder.Size = new System.Drawing.Size(65, 23);
            this.buttonDocFolder.TabIndex = 32;
            this.buttonDocFolder.Text = "DocFolder";
            this.buttonDocFolder.UseVisualStyleBackColor = true;
            // 
            // button_Log
            // 
            this.button_Log.Location = new System.Drawing.Point(35, 87);
            this.button_Log.Name = "button_Log";
            this.button_Log.Size = new System.Drawing.Size(63, 23);
            this.button_Log.TabIndex = 31;
            this.button_Log.Text = "Log";
            this.button_Log.UseVisualStyleBackColor = true;
            // 
            // VersionDceManager
            // 
            this.VersionDceManager.AutoSize = true;
            this.VersionDceManager.Location = new System.Drawing.Point(146, 301);
            this.VersionDceManager.Name = "VersionDceManager";
            this.VersionDceManager.Size = new System.Drawing.Size(42, 13);
            this.VersionDceManager.TabIndex = 19;
            this.VersionDceManager.Text = "Version";
            this.VersionDceManager.Click += new System.EventHandler(this.VersionDceManager_Click);
            // 
            // LabelStatut
            // 
            this.LabelStatut.AutoSize = true;
            this.LabelStatut.Location = new System.Drawing.Point(342, 14);
            this.LabelStatut.Name = "LabelStatut";
            this.LabelStatut.Size = new System.Drawing.Size(29, 13);
            this.LabelStatut.TabIndex = 20;
            this.LabelStatut.Text = "User";
            // 
            // pictureBox5
            // 
            this.pictureBox5.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pictureBox5.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox5.Image")));
            this.pictureBox5.Location = new System.Drawing.Point(20, 264);
            this.pictureBox5.Name = "pictureBox5";
            this.pictureBox5.Size = new System.Drawing.Size(50, 50);
            this.pictureBox5.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox5.TabIndex = 21;
            this.pictureBox5.TabStop = false;
            this.pictureBox5.Click += new System.EventHandler(this.pictureBox5_Click);
            // 
            // pictureBoxOvGME
            // 
            this.pictureBoxOvGME.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pictureBoxOvGME.Image = ((System.Drawing.Image)(resources.GetObject("pictureBoxOvGME.Image")));
            this.pictureBoxOvGME.Location = new System.Drawing.Point(255, 269);
            this.pictureBoxOvGME.Name = "pictureBoxOvGME";
            this.pictureBoxOvGME.Size = new System.Drawing.Size(50, 50);
            this.pictureBoxOvGME.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBoxOvGME.TabIndex = 22;
            this.pictureBoxOvGME.TabStop = false;
            this.pictureBoxOvGME.Click += new System.EventHandler(this.pictureBoxOvGME_Click);
            // 
            // groupBoxDroiteAccueil
            // 
            this.groupBoxDroiteAccueil.Controls.Add(this.textBox_id_client);
            this.groupBoxDroiteAccueil.Controls.Add(this.butCampMaker);
            this.groupBoxDroiteAccueil.Controls.Add(this.but_Expert);
            this.groupBoxDroiteAccueil.Controls.Add(this.butClient);
            this.groupBoxDroiteAccueil.Controls.Add(this.pictureBoxOvGME);
            this.groupBoxDroiteAccueil.Controls.Add(this.LabelStatut);
            this.groupBoxDroiteAccueil.Controls.Add(this.pictureBox1);
            this.groupBoxDroiteAccueil.Controls.Add(this.button1);
            this.groupBoxDroiteAccueil.Controls.Add(this.pictureBox5);
            this.groupBoxDroiteAccueil.Controls.Add(this.pictureBox3);
            this.groupBoxDroiteAccueil.Controls.Add(this.label1);
            this.groupBoxDroiteAccueil.Controls.Add(this.VersionDceManager);
            this.groupBoxDroiteAccueil.Controls.Add(this.label3);
            this.groupBoxDroiteAccueil.Controls.Add(this.pictureBox2);
            this.groupBoxDroiteAccueil.Location = new System.Drawing.Point(13, 8);
            this.groupBoxDroiteAccueil.Name = "groupBoxDroiteAccueil";
            this.groupBoxDroiteAccueil.Size = new System.Drawing.Size(447, 400);
            this.groupBoxDroiteAccueil.TabIndex = 23;
            this.groupBoxDroiteAccueil.TabStop = false;
            // 
            // textBox_id_client
            // 
            this.textBox_id_client.BackColor = System.Drawing.SystemColors.Control;
            this.textBox_id_client.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBox_id_client.Location = new System.Drawing.Point(53, 327);
            this.textBox_id_client.Margin = new System.Windows.Forms.Padding(2);
            this.textBox_id_client.Name = "textBox_id_client";
            this.textBox_id_client.Size = new System.Drawing.Size(75, 13);
            this.textBox_id_client.TabIndex = 27;
            this.textBox_id_client.TabStop = false;
            this.textBox_id_client.Visible = false;
            this.textBox_id_client.TextChanged += new System.EventHandler(this.textBox_id_client_TextChanged);
            // 
            // butCampMaker
            // 
            this.butCampMaker.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.butCampMaker.Location = new System.Drawing.Point(345, 92);
            this.butCampMaker.Name = "butCampMaker";
            this.butCampMaker.Size = new System.Drawing.Size(75, 23);
            this.butCampMaker.TabIndex = 25;
            this.butCampMaker.Text = "CampaignMaker";
            this.butCampMaker.UseVisualStyleBackColor = true;
            this.butCampMaker.Click += new System.EventHandler(this.butCampMaker_Click);
            // 
            // but_Expert
            // 
            this.but_Expert.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.but_Expert.Location = new System.Drawing.Point(345, 63);
            this.but_Expert.Name = "but_Expert";
            this.but_Expert.Size = new System.Drawing.Size(75, 23);
            this.but_Expert.TabIndex = 24;
            this.but_Expert.Text = "Expert";
            this.but_Expert.UseVisualStyleBackColor = true;
            this.but_Expert.Click += new System.EventHandler(this.but_Expert_Click);
            // 
            // butClient
            // 
            this.butClient.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.butClient.Location = new System.Drawing.Point(345, 36);
            this.butClient.Name = "butClient";
            this.butClient.Size = new System.Drawing.Size(75, 23);
            this.butClient.TabIndex = 23;
            this.butClient.Text = "User";
            this.butClient.UseVisualStyleBackColor = true;
            this.butClient.Click += new System.EventHandler(this.butClient_Click);
            // 
            // CampaignTab
            // 
            this.CampaignTab.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CampaignTab.Controls.Add(this.tabPage6);
            this.CampaignTab.Controls.Add(this.tabPage14);
            this.CampaignTab.Controls.Add(this.tabPage15);
            this.CampaignTab.Controls.Add(this.tabPage11);
            this.CampaignTab.Controls.Add(this.tabPage12);
            this.CampaignTab.Location = new System.Drawing.Point(20, 25);
            this.CampaignTab.Name = "CampaignTab";
            this.CampaignTab.SelectedIndex = 0;
            this.CampaignTab.Size = new System.Drawing.Size(534, 525);
            this.CampaignTab.TabIndex = 11;
            this.CampaignTab.Visible = false;
            // 
            // tabPage6
            // 
            this.tabPage6.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage6.Controls.Add(this.pictureBoxCampImage);
            this.tabPage6.Controls.Add(this.textBoxCampBriefing);
            this.tabPage6.Location = new System.Drawing.Point(4, 22);
            this.tabPage6.Name = "tabPage6";
            this.tabPage6.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage6.Size = new System.Drawing.Size(526, 499);
            this.tabPage6.TabIndex = 0;
            this.tabPage6.Text = "Intro";
            // 
            // pictureBoxCampImage
            // 
            this.pictureBoxCampImage.Location = new System.Drawing.Point(19, 297);
            this.pictureBoxCampImage.Name = "pictureBoxCampImage";
            this.pictureBoxCampImage.Size = new System.Drawing.Size(491, 187);
            this.pictureBoxCampImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBoxCampImage.TabIndex = 1;
            this.pictureBoxCampImage.TabStop = false;
            // 
            // textBoxCampBriefing
            // 
            this.textBoxCampBriefing.Location = new System.Drawing.Point(19, 12);
            this.textBoxCampBriefing.Multiline = true;
            this.textBoxCampBriefing.Name = "textBoxCampBriefing";
            this.textBoxCampBriefing.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxCampBriefing.Size = new System.Drawing.Size(491, 179);
            this.textBoxCampBriefing.TabIndex = 0;
            // 
            // tabPage14
            // 
            this.tabPage14.Controls.Add(this.dataGridViewBlue);
            this.tabPage14.Location = new System.Drawing.Point(4, 22);
            this.tabPage14.Margin = new System.Windows.Forms.Padding(2);
            this.tabPage14.Name = "tabPage14";
            this.tabPage14.Padding = new System.Windows.Forms.Padding(2);
            this.tabPage14.Size = new System.Drawing.Size(526, 499);
            this.tabPage14.TabIndex = 7;
            this.tabPage14.Text = "OOB Blue";
            this.tabPage14.UseVisualStyleBackColor = true;
            // 
            // dataGridViewBlue
            // 
            this.dataGridViewBlue.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridViewBlue.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewBlue.Location = new System.Drawing.Point(2, 2);
            this.dataGridViewBlue.Margin = new System.Windows.Forms.Padding(2);
            this.dataGridViewBlue.Name = "dataGridViewBlue";
            this.dataGridViewBlue.RowHeadersWidth = 51;
            this.dataGridViewBlue.RowTemplate.Height = 24;
            this.dataGridViewBlue.Size = new System.Drawing.Size(557, 500);
            this.dataGridViewBlue.TabIndex = 14;
            this.dataGridViewBlue.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridViewBlue_CellContentClick);
            // 
            // tabPage15
            // 
            this.tabPage15.Controls.Add(this.dataGridViewRed);
            this.tabPage15.Location = new System.Drawing.Point(4, 22);
            this.tabPage15.Margin = new System.Windows.Forms.Padding(2);
            this.tabPage15.Name = "tabPage15";
            this.tabPage15.Padding = new System.Windows.Forms.Padding(2);
            this.tabPage15.Size = new System.Drawing.Size(526, 499);
            this.tabPage15.TabIndex = 8;
            this.tabPage15.Text = "OOB Red";
            this.tabPage15.UseVisualStyleBackColor = true;
            // 
            // dataGridViewRed
            // 
            this.dataGridViewRed.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridViewRed.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewRed.Location = new System.Drawing.Point(2, 2);
            this.dataGridViewRed.Margin = new System.Windows.Forms.Padding(2);
            this.dataGridViewRed.Name = "dataGridViewRed";
            this.dataGridViewRed.RowHeadersWidth = 51;
            this.dataGridViewRed.RowTemplate.Height = 24;
            this.dataGridViewRed.Size = new System.Drawing.Size(561, 498);
            this.dataGridViewRed.TabIndex = 15;
            this.dataGridViewRed.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dataGridViewRed_CellContentClick);
            // 
            // tabPage11
            // 
            this.tabPage11.Location = new System.Drawing.Point(4, 22);
            this.tabPage11.Name = "tabPage11";
            this.tabPage11.Size = new System.Drawing.Size(526, 499);
            this.tabPage11.TabIndex = 5;
            this.tabPage11.Text = "Options";
            // 
            // tabPage12
            // 
            this.tabPage12.Controls.Add(this.textBox_Bugs);
            this.tabPage12.Location = new System.Drawing.Point(4, 22);
            this.tabPage12.Name = "tabPage12";
            this.tabPage12.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage12.Size = new System.Drawing.Size(526, 499);
            this.tabPage12.TabIndex = 6;
            this.tabPage12.Text = "Bugs";
            this.tabPage12.UseVisualStyleBackColor = true;
            // 
            // textBox_Bugs
            // 
            this.textBox_Bugs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBox_Bugs.Location = new System.Drawing.Point(13, 3);
            this.textBox_Bugs.Multiline = true;
            this.textBox_Bugs.Name = "textBox_Bugs";
            this.textBox_Bugs.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_Bugs.Size = new System.Drawing.Size(584, 488);
            this.textBox_Bugs.TabIndex = 1;
            // 
            // radioButton_OOB_INIT
            // 
            this.radioButton_OOB_INIT.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.radioButton_OOB_INIT.AutoSize = true;
            this.radioButton_OOB_INIT.Location = new System.Drawing.Point(97, 6);
            this.radioButton_OOB_INIT.Margin = new System.Windows.Forms.Padding(2);
            this.radioButton_OOB_INIT.Name = "radioButton_OOB_INIT";
            this.radioButton_OOB_INIT.Size = new System.Drawing.Size(65, 17);
            this.radioButton_OOB_INIT.TabIndex = 16;
            this.radioButton_OOB_INIT.TabStop = true;
            this.radioButton_OOB_INIT.Text = "OOB Init";
            this.radioButton_OOB_INIT.UseVisualStyleBackColor = true;
            this.radioButton_OOB_INIT.Visible = false;
            this.radioButton_OOB_INIT.CheckedChanged += new System.EventHandler(this.radioButton_OOB_INIT_CheckedChanged);
            // 
            // radioButton_OOB_ACTIVE
            // 
            this.radioButton_OOB_ACTIVE.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.radioButton_OOB_ACTIVE.AutoSize = true;
            this.radioButton_OOB_ACTIVE.Location = new System.Drawing.Point(183, 6);
            this.radioButton_OOB_ACTIVE.Margin = new System.Windows.Forms.Padding(2);
            this.radioButton_OOB_ACTIVE.Name = "radioButton_OOB_ACTIVE";
            this.radioButton_OOB_ACTIVE.Size = new System.Drawing.Size(81, 17);
            this.radioButton_OOB_ACTIVE.TabIndex = 17;
            this.radioButton_OOB_ACTIVE.TabStop = true;
            this.radioButton_OOB_ACTIVE.Text = "OOB Active";
            this.radioButton_OOB_ACTIVE.UseVisualStyleBackColor = true;
            this.radioButton_OOB_ACTIVE.Visible = false;
            this.radioButton_OOB_ACTIVE.CheckedChanged += new System.EventHandler(this.radioButton_OOB_ACTIVE_CheckedChanged);
            // 
            // buttonResetBackup
            // 
            this.buttonResetBackup.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.buttonResetBackup.Location = new System.Drawing.Point(407, 5);
            this.buttonResetBackup.Name = "buttonResetBackup";
            this.buttonResetBackup.Size = new System.Drawing.Size(75, 23);
            this.buttonResetBackup.TabIndex = 14;
            this.buttonResetBackup.Text = "Reset Init";
            this.buttonResetBackup.UseVisualStyleBackColor = true;
            this.buttonResetBackup.Visible = false;
            this.buttonResetBackup.Click += new System.EventHandler(this.buttonResetBackup_Click);
            // 
            // buttonSaveChgtCampaign
            // 
            this.buttonSaveChgtCampaign.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.buttonSaveChgtCampaign.Location = new System.Drawing.Point(282, 5);
            this.buttonSaveChgtCampaign.Name = "buttonSaveChgtCampaign";
            this.buttonSaveChgtCampaign.Size = new System.Drawing.Size(110, 23);
            this.buttonSaveChgtCampaign.TabIndex = 12;
            this.buttonSaveChgtCampaign.Text = "Save Campaign";
            this.buttonSaveChgtCampaign.UseVisualStyleBackColor = true;
            this.buttonSaveChgtCampaign.Visible = false;
            this.buttonSaveChgtCampaign.Click += new System.EventHandler(this.buttonSaveChgtCampaign_Click);
            // 
            // toolTip3
            // 
            this.toolTip3.Popup += new System.Windows.Forms.PopupEventHandler(this.toolTip3_Popup);
            // 
            // groupBox_staticTemplate
            // 
            this.groupBox_staticTemplate.Controls.Add(this.but_GPS_LL);
            this.groupBox_staticTemplate.Controls.Add(this.but_ASTI_Open_templateFolder);
            this.groupBox_staticTemplate.Controls.Add(this.but_ASTI_Process);
            this.groupBox_staticTemplate.Controls.Add(this.but_ASTI_Browse_MissionFile);
            this.groupBox_staticTemplate.Controls.Add(this.but_ASTI_Browse_Template);
            this.groupBox_staticTemplate.Controls.Add(this.textBox_ASTI_MissionFile);
            this.groupBox_staticTemplate.Controls.Add(this.textBox_ASTI_importTemplateFolder);
            this.groupBox_staticTemplate.Location = new System.Drawing.Point(13, 71);
            this.groupBox_staticTemplate.Name = "groupBox_staticTemplate";
            this.groupBox_staticTemplate.Size = new System.Drawing.Size(420, 377);
            this.groupBox_staticTemplate.TabIndex = 2;
            this.groupBox_staticTemplate.TabStop = false;
            this.groupBox_staticTemplate.Text = "ASTI";
            this.groupBox_staticTemplate.Visible = false;
            // 
            // but_GPS_LL
            // 
            this.but_GPS_LL.Location = new System.Drawing.Point(44, 242);
            this.but_GPS_LL.Name = "but_GPS_LL";
            this.but_GPS_LL.Size = new System.Drawing.Size(97, 25);
            this.but_GPS_LL.TabIndex = 8;
            this.but_GPS_LL.Text = "Test_LL";
            this.but_GPS_LL.UseVisualStyleBackColor = true;
            this.but_GPS_LL.Visible = false;
            // 
            // but_ASTI_Open_templateFolder
            // 
            this.but_ASTI_Open_templateFolder.Location = new System.Drawing.Point(252, 34);
            this.but_ASTI_Open_templateFolder.Name = "but_ASTI_Open_templateFolder";
            this.but_ASTI_Open_templateFolder.Size = new System.Drawing.Size(114, 25);
            this.but_ASTI_Open_templateFolder.TabIndex = 7;
            this.but_ASTI_Open_templateFolder.Text = "Open folder";
            this.but_ASTI_Open_templateFolder.UseVisualStyleBackColor = true;
            this.but_ASTI_Open_templateFolder.Visible = false;
            // 
            // but_ASTI_Process
            // 
            this.but_ASTI_Process.Location = new System.Drawing.Point(185, 326);
            this.but_ASTI_Process.Name = "but_ASTI_Process";
            this.but_ASTI_Process.Size = new System.Drawing.Size(181, 25);
            this.but_ASTI_Process.TabIndex = 4;
            this.but_ASTI_Process.Text = "Process";
            this.but_ASTI_Process.UseVisualStyleBackColor = true;
            // 
            // but_ASTI_Browse_MissionFile
            // 
            this.but_ASTI_Browse_MissionFile.Location = new System.Drawing.Point(44, 140);
            this.but_ASTI_Browse_MissionFile.Name = "but_ASTI_Browse_MissionFile";
            this.but_ASTI_Browse_MissionFile.Size = new System.Drawing.Size(181, 25);
            this.but_ASTI_Browse_MissionFile.TabIndex = 3;
            this.but_ASTI_Browse_MissionFile.Text = "Select Mission file (.miz)";
            this.but_ASTI_Browse_MissionFile.UseVisualStyleBackColor = true;
            // 
            // but_ASTI_Browse_Template
            // 
            this.but_ASTI_Browse_Template.Location = new System.Drawing.Point(46, 34);
            this.but_ASTI_Browse_Template.Name = "but_ASTI_Browse_Template";
            this.but_ASTI_Browse_Template.Size = new System.Drawing.Size(181, 25);
            this.but_ASTI_Browse_Template.TabIndex = 2;
            this.but_ASTI_Browse_Template.Text = "Select Templates folder";
            this.but_ASTI_Browse_Template.UseVisualStyleBackColor = true;
            // 
            // textBox_ASTI_MissionFile
            // 
            this.textBox_ASTI_MissionFile.Location = new System.Drawing.Point(44, 178);
            this.textBox_ASTI_MissionFile.Name = "textBox_ASTI_MissionFile";
            this.textBox_ASTI_MissionFile.Size = new System.Drawing.Size(322, 20);
            this.textBox_ASTI_MissionFile.TabIndex = 1;
            // 
            // textBox_ASTI_importTemplateFolder
            // 
            this.textBox_ASTI_importTemplateFolder.Location = new System.Drawing.Point(44, 70);
            this.textBox_ASTI_importTemplateFolder.Name = "textBox_ASTI_importTemplateFolder";
            this.textBox_ASTI_importTemplateFolder.Size = new System.Drawing.Size(322, 20);
            this.textBox_ASTI_importTemplateFolder.TabIndex = 0;
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.radioButton_OOB_INIT);
            this.panel2.Controls.Add(this.radioButton_OOB_ACTIVE);
            this.panel2.Controls.Add(this.buttonSaveChgtCampaign);
            this.panel2.Controls.Add(this.buttonResetBackup);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel2.Location = new System.Drawing.Point(0, 556);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(557, 37);
            this.panel2.TabIndex = 25;
            this.panel2.Paint += new System.Windows.Forms.PaintEventHandler(this.panel2_Paint);
            // 
            // panel_Droite
            // 
            this.panel_Droite.Controls.Add(this.panel2);
            this.panel_Droite.Controls.Add(this.CampaignTab);
            this.panel_Droite.Controls.Add(this.groupBoxDroiteAccueil);
            this.panel_Droite.Controls.Add(this.groupBox_staticTemplate);
            this.panel_Droite.Dock = System.Windows.Forms.DockStyle.Right;
            this.panel_Droite.Location = new System.Drawing.Point(855, 0);
            this.panel_Droite.Name = "panel_Droite";
            this.panel_Droite.Size = new System.Drawing.Size(557, 593);
            this.panel_Droite.TabIndex = 26;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.ClientSize = new System.Drawing.Size(1412, 593);
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.panel_Droite);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.Name = "Form1";
            this.Text = "DCE_Manager";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Form1_KeyDown);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.Form1_KeyUp);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox3)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.tabControl.ResumeLayout(false);
            this.tabPageLeft_Install.ResumeLayout(false);
            this.groupBox7.ResumeLayout(false);
            this.groupBox7.PerformLayout();
            this.groupBox4.ResumeLayout(false);
            this.groupBox8.ResumeLayout(false);
            this.groupBox5.ResumeLayout(false);
            this.groupBox5.PerformLayout();
            this.groupBoxCampEdit.ResumeLayout(false);
            this.groupBoxCampEdit.PerformLayout();
            this.tabPageLeft_Campaigns.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewCampaigns)).EndInit();
            this.tabPageLeft_Update.ResumeLayout(false);
            this.groupBox6.ResumeLayout(false);
            this.groupBox6.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_DCE_Manager_Status)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Update_DCE_Manager)).EndInit();
            this.groupBox_DwlCampaign.ResumeLayout(false);
            this.groupBox_DwlCampaign.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampaignDownload)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.CampaignDataGridView)).EndInit();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_ScriptsMod_Status)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Update_ScriptsMod)).EndInit();
            this.tabPageLeft_About.ResumeLayout(false);
            this.tabPageLeft_About.PerformLayout();
            this.tabPageLeftNews.ResumeLayout(false);
            this.tabPageLeftNews.PerformLayout();
            this.tabPageLeft_Tools.ResumeLayout(false);
            this.tabPageLeft_Tools.PerformLayout();
            this.tabPageLeft_Options.ResumeLayout(false);
            this.tabPageLeft_Options.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox5)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxOvGME)).EndInit();
            this.groupBoxDroiteAccueil.ResumeLayout(false);
            this.groupBoxDroiteAccueil.PerformLayout();
            this.CampaignTab.ResumeLayout(false);
            this.tabPage6.ResumeLayout(false);
            this.tabPage6.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampImage)).EndInit();
            this.tabPage14.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewBlue)).EndInit();
            this.tabPage15.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRed)).EndInit();
            this.tabPage12.ResumeLayout(false);
            this.tabPage12.PerformLayout();
            this.groupBox_staticTemplate.ResumeLayout(false);
            this.groupBox_staticTemplate.PerformLayout();
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            this.panel_Droite.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        //private void textBoxCampEdit_TextChanged(object sender, EventArgs e)
        //{
        //    throw new NotImplementedException();
        //}

        #endregion
        public System.Windows.Forms.Button m_ButtonDcsPath;
        public System.Windows.Forms.Button m_ButtonSavedGames;
        public System.Windows.Forms.TextBox textBox_DCS;
        public System.Windows.Forms.GroupBox groupBox1;
        public System.Windows.Forms.TextBox textBox_SavedGames;
        private System.Windows.Forms.Label Label_SavedGames;
        private System.Windows.Forms.Label Label_DCS;
        private System.Windows.Forms.Button button_InstallCampaign;
        private System.Windows.Forms.Label Label_Campaign;
        public System.Windows.Forms.TextBox textBox_Campaign;
        private System.Windows.Forms.Button Button_choiceCampaign;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label Label_OvGME;
        private System.Windows.Forms.TextBox textBox_OvGME;
        private System.Windows.Forms.Button button2;
        public System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.LinkLabel linkLabelOvGME;
        private System.Windows.Forms.LinkLabel linkLabelCampaign;
        private System.Windows.Forms.PictureBox pictureBox2;
        private System.Windows.Forms.PictureBox pictureBox3;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label3;
        public System.Windows.Forms.CheckBox checkBoxActiveFolder;
        public System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabPageLeft_Install;
        private System.Windows.Forms.TabPage tabPageLeft_Update;
        private System.Windows.Forms.TabPage tabPageLeft_About;
        public System.Windows.Forms.Button ScriptsModUpdateButton;
        public System.Windows.Forms.Label ScriptsModAvailableVersion;
        private System.Windows.Forms.GroupBox groupBox_DwlCampaign;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label5;
        public System.Windows.Forms.ComboBox comboBox_Config;
        private System.Windows.Forms.Button m_Button_AddConfig;
        private System.Windows.Forms.TextBox textBox_Config;
        private System.Windows.Forms.Button m_Button_Config_Del;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.Label VersionDceManager;
        public System.Windows.Forms.Label LabelStatut;
        private System.Windows.Forms.TextBox textBox_changelog;
        private System.Windows.Forms.GroupBox groupBox6;
        public System.Windows.Forms.Button DCEManagerUpdateButton;
        private System.Windows.Forms.Label label8;
        public System.Windows.Forms.Label DCEManagerAvailableVersion;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TextBox textBox_ChangelogScriptsMod;
        private System.Windows.Forms.PictureBox pictureBox5;
        private System.Windows.Forms.PictureBox pictureBoxOvGME;
        private System.Windows.Forms.TabPage tabPageLeftNews;
        private System.Windows.Forms.TextBox textBox_News;
        private System.Windows.Forms.CheckBox checkBox_OvwNGfolder;
        public System.Windows.Forms.Label ScriptModInstalledVersion;
        public System.Windows.Forms.Label DCEManagerInstalledVersion;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.CheckBox checkBoxSanitize;
        public System.Windows.Forms.GroupBox groupBoxDroiteAccueil;
        public System.Windows.Forms.TabControl CampaignTab;
        public System.Windows.Forms.TabPage tabPage6;
        private System.Windows.Forms.Button buttonSaveChgtCampaign;
        private System.Windows.Forms.Button buttonResetBackup;
        private System.Windows.Forms.ToolTip toolTip3;
        public System.Windows.Forms.TextBox textBoxCampBriefing;
        public System.Windows.Forms.PictureBox pictureBoxCampImage;
        public System.Windows.Forms.TabPage tabPage11;
        public System.Windows.Forms.TextBox textBox_Bugs;
        public System.Windows.Forms.TabPage tabPage12;
        private System.Windows.Forms.Panel panel_News;
        private System.Windows.Forms.GroupBox groupBox7;
        private System.Windows.Forms.TabPage tabPageLeft_Tools;

        private System.Windows.Forms.Button button_theWay;
        public System.Windows.Forms.GroupBox groupBox_staticTemplate;
        

        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label2;

        
        private System.Windows.Forms.Button button6;
        private System.Windows.Forms.Button butClient;
        private System.Windows.Forms.Button but_Expert;
        private System.Windows.Forms.GroupBox groupBox8;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.Button butCampMaker;

        public System.Windows.Forms.Button but_ASTI;
        public System.Windows.Forms.Button but_ASTI_Browse_Template;
        public System.Windows.Forms.Button but_ASTI_Open_templateFolder;
        public System.Windows.Forms.Button but_ASTI_Browse_MissionFile;
        private System.Windows.Forms.Button but_ASTI_Process;

        private System.Windows.Forms.Button but_GPS_LL;

        public System.Windows.Forms.TextBox textBox_ASTI_MissionFile;
        public System.Windows.Forms.TextBox textBox_ASTI_importTemplateFolder;
        private System.Windows.Forms.TextBox textBox_ASTI;
        private System.Windows.Forms.TextBox textBox_id_client;
        public System.Windows.Forms.DataGridView dataGridViewBlue;
        public System.Windows.Forms.DataGridView dataGridViewRed;
        public System.Windows.Forms.TabPage tabPage14;
        public System.Windows.Forms.TabPage tabPage15;
        public System.Windows.Forms.TabPage tabPageLeft_Campaigns;
        public System.Windows.Forms.DataGridView dataGridViewCampaigns;
        public System.Windows.Forms.RadioButton radioButton_OOB_ACTIVE;
        public System.Windows.Forms.RadioButton radioButton_OOB_INIT;
        public System.Windows.Forms.Label ScriptsModStatusLabel;
        private System.Windows.Forms.Label label11;
        public System.Windows.Forms.Panel panel2;
        public System.Windows.Forms.GroupBox groupBoxCampEdit;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Panel panel_Droite;
        private System.Windows.Forms.TabPage tabPageLeft_Options;
        private System.Windows.Forms.CheckBox checkBox_Stat_anonym;
        private System.Windows.Forms.Button buttonDocFolder;
        private System.Windows.Forms.Button button_Log;
        public System.Windows.Forms.Label DCEManagerStatusLabel;
        public System.Windows.Forms.DataGridView CampaignDataGridView;
        public System.Windows.Forms.Label labelCampaignDownload;
        public System.Windows.Forms.ProgressBar progressBarCampaignDownload;
        public System.Windows.Forms.Button buttonCampaignCancel;
        public System.Windows.Forms.Label labelCampaignTitle;
        public System.Windows.Forms.Label labelCampaignDld_Pct;
        public System.Windows.Forms.PictureBox pictureBoxCampaignDownload;
        public System.Windows.Forms.PictureBox pictureBox_Update_DCE_Manager;
        public System.Windows.Forms.PictureBox pictureBox_Update_ScriptsMod;
        public System.Windows.Forms.PictureBox pictureBox_DCE_Manager_Status;
        public System.Windows.Forms.PictureBox pictureBox_ScriptsMod_Status;
        public System.Windows.Forms.LinkLabel linkLabel_Icons8;
        private System.Windows.Forms.Label label_DCEM_Status;
        private System.Windows.Forms.Label label_SM_Status;
        private System.Windows.Forms.Label label_ScriptsMod_B;
        private System.Windows.Forms.Label label_ScriptsMod_A;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.Label label16;
    }
}

