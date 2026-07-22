using System;

namespace DCE_Manager
{
    partial class Main_Form
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
        public void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main_Form));
            this.m_But_Install_Browse_DcsPath = new System.Windows.Forms.Button();
            this.m_But_Install_Browse_SavedGame = new System.Windows.Forms.Button();
            this.textBox_PATH_DCS_Root = new System.Windows.Forms.TextBox();
            this.button_InstallCampaign = new System.Windows.Forms.Button();
            this.textBox_OvGME = new System.Windows.Forms.TextBox();
            this.m_but_Install_Browse_OVGME = new System.Windows.Forms.Button();
            this.Label_DCS = new System.Windows.Forms.Label();
            this.textBox_SavedGames = new System.Windows.Forms.TextBox();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.tabControl_LEFT = new System.Windows.Forms.TabControl();
            this.tabPageLeft_Install = new System.Windows.Forms.TabPage();
            this.panel_install_campaign = new System.Windows.Forms.Panel();
            this.label_install_campaign = new System.Windows.Forms.Label();
            this.panel_PATH = new System.Windows.Forms.Panel();
            this.but_PATH_CANCEL = new System.Windows.Forms.Button();
            this.but_PATH_SAVE = new System.Windows.Forms.Button();
            this.box_OVGME = new System.Windows.Forms.GroupBox();
            this.label_sub_OVGME = new System.Windows.Forms.Label();
            this.pic_OVGME = new System.Windows.Forms.PictureBox();
            this.label_OVGME_b = new System.Windows.Forms.Label();
            this.Box_DCS_SavedGame = new System.Windows.Forms.GroupBox();
            this.label_subLabel_SavedGame_Folder = new System.Windows.Forms.Label();
            this.pic_SavedGame = new System.Windows.Forms.PictureBox();
            this.label_SavedGame_Folder = new System.Windows.Forms.Label();
            this.label_DCS_INSTALLATION = new System.Windows.Forms.Label();
            this.but_PATH_Modify = new System.Windows.Forms.Button();
            this.Box_DCS_Root = new System.Windows.Forms.GroupBox();
            this.Label_subLabel_DCS = new System.Windows.Forms.Label();
            this.pic_DCS_Root = new System.Windows.Forms.PictureBox();
            this.tabPageLeft_Campaigns = new System.Windows.Forms.TabPage();
            this.dataGridViewCampaigns = new System.Windows.Forms.DataGridView();
            this.tabPageLeft_Update = new System.Windows.Forms.TabPage();
            this.groupBox_Update_DCE_M = new System.Windows.Forms.GroupBox();
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
            this.groupBox_Update_ScriptMod = new System.Windows.Forms.GroupBox();
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
            this.pictureBox4 = new System.Windows.Forms.PictureBox();
            this.pic_preferences = new System.Windows.Forms.PictureBox();
            this.panel_OpenDoc = new System.Windows.Forms.Panel();
            this.pic_OpenDoc_ArrowLog = new System.Windows.Forms.PictureBox();
            this.lbl_OpenDocTitle = new System.Windows.Forms.Label();
            this.pic_OpenDocIcon = new System.Windows.Forms.PictureBox();
            this.lbl_OpenDocDesc = new System.Windows.Forms.Label();
            this.label_tolls = new System.Windows.Forms.Label();
            this.label_preference = new System.Windows.Forms.Label();
            this.panel_ViewLog = new System.Windows.Forms.Panel();
            this.pic_ViewLog_ArrowLog = new System.Windows.Forms.PictureBox();
            this.lbl_ViewLogTitle = new System.Windows.Forms.Label();
            this.pic_ViewLogIcon = new System.Windows.Forms.PictureBox();
            this.lbl_ViewLogDesc = new System.Windows.Forms.Label();
            this.panel_preferences = new System.Windows.Forms.Panel();
            this.pictureBox8 = new System.Windows.Forms.PictureBox();
            this.checkBox_Stat_anonym = new System.Windows.Forms.CheckBox();
            this.label_statistics_explain = new System.Windows.Forms.Label();
            this.Readme = new System.Windows.Forms.LinkLabel();
            this.tabPageLeft_About = new System.Windows.Forms.TabPage();
            this.linkLabel_Icons8 = new System.Windows.Forms.LinkLabel();
            this.label9 = new System.Windows.Forms.Label();
            this.textBox_ChangelogScriptsMod = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.textBox_changelog = new System.Windows.Forms.TextBox();
            this.textBox_Credits = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.comboBox_Config = new System.Windows.Forms.ComboBox();
            this.toolTip3 = new System.Windows.Forms.ToolTip(this.components);
            this.panel_top = new System.Windows.Forms.Panel();
            this.but_Level_DEV = new System.Windows.Forms.Button();
            this.but_level_User = new System.Windows.Forms.Button();
            this.but_Level_CampMaker = new System.Windows.Forms.Button();
            this.label_UserLevel = new System.Windows.Forms.Label();
            this.but_Configuration_Edit = new System.Windows.Forms.Button();
            this.label_Config = new System.Windows.Forms.Label();
            this.panelRightView = new System.Windows.Forms.Panel();
            this.panel_Down = new System.Windows.Forms.Panel();
            this.button_EXIT = new System.Windows.Forms.Button();
            this.dropZoneControl1 = new DCE_Manager.Controls.DropZoneControl();
            this.tabControl_LEFT.SuspendLayout();
            this.tabPageLeft_Install.SuspendLayout();
            this.panel_install_campaign.SuspendLayout();
            this.panel_PATH.SuspendLayout();
            this.box_OVGME.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_OVGME)).BeginInit();
            this.Box_DCS_SavedGame.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_SavedGame)).BeginInit();
            this.Box_DCS_Root.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_DCS_Root)).BeginInit();
            this.tabPageLeft_Campaigns.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewCampaigns)).BeginInit();
            this.tabPageLeft_Update.SuspendLayout();
            this.groupBox_Update_DCE_M.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_DCE_Manager_Status)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Update_DCE_Manager)).BeginInit();
            this.groupBox_DwlCampaign.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampaignDownload)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.CampaignDataGridView)).BeginInit();
            this.groupBox_Update_ScriptMod.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_ScriptsMod_Status)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Update_ScriptsMod)).BeginInit();
            this.tabPageLeftNews.SuspendLayout();
            this.tabPageLeft_Tools.SuspendLayout();
            this.tabPageLeft_Options.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pic_preferences)).BeginInit();
            this.panel_OpenDoc.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_OpenDoc_ArrowLog)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pic_OpenDocIcon)).BeginInit();
            this.panel_ViewLog.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_ViewLog_ArrowLog)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pic_ViewLogIcon)).BeginInit();
            this.panel_preferences.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox8)).BeginInit();
            this.tabPageLeft_About.SuspendLayout();
            this.panel_top.SuspendLayout();
            this.panel_Down.SuspendLayout();
            this.SuspendLayout();
            // 
            // m_But_Install_Browse_DcsPath
            // 
            this.m_But_Install_Browse_DcsPath.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.m_But_Install_Browse_DcsPath.Location = new System.Drawing.Point(566, 11);
            this.m_But_Install_Browse_DcsPath.Name = "m_But_Install_Browse_DcsPath";
            this.m_But_Install_Browse_DcsPath.Size = new System.Drawing.Size(55, 28);
            this.m_But_Install_Browse_DcsPath.TabIndex = 1;
            this.m_But_Install_Browse_DcsPath.Text = "Browse...";
            this.m_But_Install_Browse_DcsPath.UseVisualStyleBackColor = true;
            this.m_But_Install_Browse_DcsPath.Visible = false;
            this.m_But_Install_Browse_DcsPath.Click += new System.EventHandler(this.m_But_Install_Browse_DcsPath_Click);
            // 
            // m_But_Install_Browse_SavedGame
            // 
            this.m_But_Install_Browse_SavedGame.Location = new System.Drawing.Point(566, 15);
            this.m_But_Install_Browse_SavedGame.Name = "m_But_Install_Browse_SavedGame";
            this.m_But_Install_Browse_SavedGame.Size = new System.Drawing.Size(55, 28);
            this.m_But_Install_Browse_SavedGame.TabIndex = 2;
            this.m_But_Install_Browse_SavedGame.Text = "Browse...";
            this.m_But_Install_Browse_SavedGame.UseVisualStyleBackColor = true;
            this.m_But_Install_Browse_SavedGame.Visible = false;
            this.m_But_Install_Browse_SavedGame.Click += new System.EventHandler(this.m_But_Install_Browse_SavedGame_Click);
            // 
            // textBox_PATH_DCS_Root
            // 
            this.textBox_PATH_DCS_Root.Location = new System.Drawing.Point(268, 17);
            this.textBox_PATH_DCS_Root.Name = "textBox_PATH_DCS_Root";
            this.textBox_PATH_DCS_Root.Size = new System.Drawing.Size(294, 20);
            this.textBox_PATH_DCS_Root.TabIndex = 4;
            this.textBox_PATH_DCS_Root.Visible = false;
            // 
            // button_InstallCampaign
            // 
            this.button_InstallCampaign.Font = new System.Drawing.Font("Segoe UI", 10.2F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button_InstallCampaign.Location = new System.Drawing.Point(173, 131);
            this.button_InstallCampaign.Name = "button_InstallCampaign";
            this.button_InstallCampaign.Size = new System.Drawing.Size(323, 41);
            this.button_InstallCampaign.TabIndex = 7;
            this.button_InstallCampaign.Text = "Install Campaign";
            this.button_InstallCampaign.UseVisualStyleBackColor = true;
            this.button_InstallCampaign.Click += new System.EventHandler(this.button_InstallCampaign_Click);
            // 
            // textBox_OvGME
            // 
            this.textBox_OvGME.Location = new System.Drawing.Point(268, 18);
            this.textBox_OvGME.Name = "textBox_OvGME";
            this.textBox_OvGME.Size = new System.Drawing.Size(294, 20);
            this.textBox_OvGME.TabIndex = 12;
            this.textBox_OvGME.Visible = false;
            // 
            // m_but_Install_Browse_OVGME
            // 
            this.m_but_Install_Browse_OVGME.Location = new System.Drawing.Point(566, 19);
            this.m_but_Install_Browse_OVGME.Name = "m_but_Install_Browse_OVGME";
            this.m_but_Install_Browse_OVGME.Size = new System.Drawing.Size(55, 28);
            this.m_but_Install_Browse_OVGME.TabIndex = 11;
            this.m_but_Install_Browse_OVGME.Text = "Browse...";
            this.m_but_Install_Browse_OVGME.UseVisualStyleBackColor = true;
            this.m_but_Install_Browse_OVGME.Visible = false;
            this.m_but_Install_Browse_OVGME.Click += new System.EventHandler(this.m_but_Install_Browse_OVGME_Click);
            // 
            // Label_DCS
            // 
            this.Label_DCS.AutoSize = true;
            this.Label_DCS.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Label_DCS.Location = new System.Drawing.Point(62, 17);
            this.Label_DCS.Name = "Label_DCS";
            this.Label_DCS.Size = new System.Drawing.Size(94, 15);
            this.Label_DCS.TabIndex = 6;
            this.Label_DCS.Text = "DCS Root Folder";
            // 
            // textBox_SavedGames
            // 
            this.textBox_SavedGames.Location = new System.Drawing.Point(268, 18);
            this.textBox_SavedGames.Name = "textBox_SavedGames";
            this.textBox_SavedGames.Size = new System.Drawing.Size(294, 20);
            this.textBox_SavedGames.TabIndex = 5;
            this.textBox_SavedGames.Visible = false;
            // 
            // tabControl_LEFT
            // 
            this.tabControl_LEFT.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.tabControl_LEFT.Controls.Add(this.tabPageLeft_Install);
            this.tabControl_LEFT.Controls.Add(this.tabPageLeft_Campaigns);
            this.tabControl_LEFT.Controls.Add(this.tabPageLeft_Update);
            this.tabControl_LEFT.Controls.Add(this.tabPageLeftNews);
            this.tabControl_LEFT.Controls.Add(this.tabPageLeft_Tools);
            this.tabControl_LEFT.Controls.Add(this.tabPageLeft_Options);
            this.tabControl_LEFT.Controls.Add(this.tabPageLeft_About);
            this.tabControl_LEFT.Location = new System.Drawing.Point(3, 53);
            this.tabControl_LEFT.Name = "tabControl_LEFT";
            this.tabControl_LEFT.SelectedIndex = 0;
            this.tabControl_LEFT.Size = new System.Drawing.Size(816, 627);
            this.tabControl_LEFT.TabIndex = 18;
            // 
            // tabPageLeft_Install
            // 
            this.tabPageLeft_Install.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeft_Install.Controls.Add(this.panel_install_campaign);
            this.tabPageLeft_Install.Controls.Add(this.panel_PATH);
            this.tabPageLeft_Install.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Install.Name = "tabPageLeft_Install";
            this.tabPageLeft_Install.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageLeft_Install.Size = new System.Drawing.Size(808, 601);
            this.tabPageLeft_Install.TabIndex = 0;
            this.tabPageLeft_Install.Text = "Install";
            // 
            // panel_install_campaign
            // 
            this.panel_install_campaign.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel_install_campaign.Controls.Add(this.label_install_campaign);
            this.panel_install_campaign.Controls.Add(this.dropZoneControl1);
            this.panel_install_campaign.Controls.Add(this.button_InstallCampaign);
            this.panel_install_campaign.Location = new System.Drawing.Point(74, 299);
            this.panel_install_campaign.Name = "panel_install_campaign";
            this.panel_install_campaign.Size = new System.Drawing.Size(658, 186);
            this.panel_install_campaign.TabIndex = 28;
            // 
            // label_install_campaign
            // 
            this.label_install_campaign.AutoSize = true;
            this.label_install_campaign.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_install_campaign.Location = new System.Drawing.Point(8, 9);
            this.label_install_campaign.Name = "label_install_campaign";
            this.label_install_campaign.Size = new System.Drawing.Size(176, 21);
            this.label_install_campaign.TabIndex = 28;
            this.label_install_campaign.Text = "INSTALL A CAMPAIGN";
            // 
            // panel_PATH
            // 
            this.panel_PATH.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel_PATH.Controls.Add(this.but_PATH_CANCEL);
            this.panel_PATH.Controls.Add(this.but_PATH_SAVE);
            this.panel_PATH.Controls.Add(this.box_OVGME);
            this.panel_PATH.Controls.Add(this.Box_DCS_SavedGame);
            this.panel_PATH.Controls.Add(this.label_DCS_INSTALLATION);
            this.panel_PATH.Controls.Add(this.but_PATH_Modify);
            this.panel_PATH.Controls.Add(this.Box_DCS_Root);
            this.panel_PATH.Location = new System.Drawing.Point(74, 26);
            this.panel_PATH.Margin = new System.Windows.Forms.Padding(2);
            this.panel_PATH.Name = "panel_PATH";
            this.panel_PATH.Size = new System.Drawing.Size(658, 250);
            this.panel_PATH.TabIndex = 26;
            // 
            // but_PATH_CANCEL
            // 
            this.but_PATH_CANCEL.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.but_PATH_CANCEL.Location = new System.Drawing.Point(527, 8);
            this.but_PATH_CANCEL.Name = "but_PATH_CANCEL";
            this.but_PATH_CANCEL.Size = new System.Drawing.Size(55, 28);
            this.but_PATH_CANCEL.TabIndex = 11;
            this.but_PATH_CANCEL.Text = "Cancel";
            this.but_PATH_CANCEL.UseVisualStyleBackColor = true;
            this.but_PATH_CANCEL.Visible = false;
            this.but_PATH_CANCEL.Click += new System.EventHandler(this.but_PATH_CANCEL_Click);
            // 
            // but_PATH_SAVE
            // 
            this.but_PATH_SAVE.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.but_PATH_SAVE.Location = new System.Drawing.Point(466, 8);
            this.but_PATH_SAVE.Name = "but_PATH_SAVE";
            this.but_PATH_SAVE.Size = new System.Drawing.Size(55, 28);
            this.but_PATH_SAVE.TabIndex = 10;
            this.but_PATH_SAVE.Text = "Save";
            this.but_PATH_SAVE.UseVisualStyleBackColor = true;
            this.but_PATH_SAVE.Visible = false;
            this.but_PATH_SAVE.Click += new System.EventHandler(this.but_PATH_SAVE_Click);
            // 
            // box_OVGME
            // 
            this.box_OVGME.Controls.Add(this.label_sub_OVGME);
            this.box_OVGME.Controls.Add(this.m_but_Install_Browse_OVGME);
            this.box_OVGME.Controls.Add(this.textBox_OvGME);
            this.box_OVGME.Controls.Add(this.pic_OVGME);
            this.box_OVGME.Controls.Add(this.label_OVGME_b);
            this.box_OVGME.Location = new System.Drawing.Point(12, 172);
            this.box_OVGME.Margin = new System.Windows.Forms.Padding(2);
            this.box_OVGME.Name = "box_OVGME";
            this.box_OVGME.Padding = new System.Windows.Forms.Padding(2);
            this.box_OVGME.Size = new System.Drawing.Size(631, 66);
            this.box_OVGME.TabIndex = 9;
            this.box_OVGME.TabStop = false;
            // 
            // label_sub_OVGME
            // 
            this.label_sub_OVGME.AutoSize = true;
            this.label_sub_OVGME.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_sub_OVGME.Location = new System.Drawing.Point(62, 34);
            this.label_sub_OVGME.Name = "label_sub_OVGME";
            this.label_sub_OVGME.Size = new System.Drawing.Size(16, 13);
            this.label_sub_OVGME.TabIndex = 10;
            this.label_sub_OVGME.Text = "...";
            // 
            // pic_OVGME
            // 
            this.pic_OVGME.Image = global::DCE_Manager.Properties.Resources.icons8_warning_blue_30;
            this.pic_OVGME.Location = new System.Drawing.Point(5, 15);
            this.pic_OVGME.Name = "pic_OVGME";
            this.pic_OVGME.Size = new System.Drawing.Size(28, 20);
            this.pic_OVGME.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pic_OVGME.TabIndex = 9;
            this.pic_OVGME.TabStop = false;
            // 
            // label_OVGME_b
            // 
            this.label_OVGME_b.AutoSize = true;
            this.label_OVGME_b.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_OVGME_b.Location = new System.Drawing.Point(62, 17);
            this.label_OVGME_b.Name = "label_OVGME_b";
            this.label_OVGME_b.Size = new System.Drawing.Size(153, 15);
            this.label_OVGME_b.TabIndex = 6;
            this.label_OVGME_b.Text = "PATH_OVGME_MOD Folder";
            // 
            // Box_DCS_SavedGame
            // 
            this.Box_DCS_SavedGame.Controls.Add(this.m_But_Install_Browse_SavedGame);
            this.Box_DCS_SavedGame.Controls.Add(this.textBox_SavedGames);
            this.Box_DCS_SavedGame.Controls.Add(this.label_subLabel_SavedGame_Folder);
            this.Box_DCS_SavedGame.Controls.Add(this.pic_SavedGame);
            this.Box_DCS_SavedGame.Controls.Add(this.label_SavedGame_Folder);
            this.Box_DCS_SavedGame.Location = new System.Drawing.Point(12, 102);
            this.Box_DCS_SavedGame.Margin = new System.Windows.Forms.Padding(2);
            this.Box_DCS_SavedGame.Name = "Box_DCS_SavedGame";
            this.Box_DCS_SavedGame.Padding = new System.Windows.Forms.Padding(2);
            this.Box_DCS_SavedGame.Size = new System.Drawing.Size(631, 66);
            this.Box_DCS_SavedGame.TabIndex = 8;
            this.Box_DCS_SavedGame.TabStop = false;
            // 
            // label_subLabel_SavedGame_Folder
            // 
            this.label_subLabel_SavedGame_Folder.AutoSize = true;
            this.label_subLabel_SavedGame_Folder.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_subLabel_SavedGame_Folder.Location = new System.Drawing.Point(62, 40);
            this.label_subLabel_SavedGame_Folder.Name = "label_subLabel_SavedGame_Folder";
            this.label_subLabel_SavedGame_Folder.Size = new System.Drawing.Size(16, 13);
            this.label_subLabel_SavedGame_Folder.TabIndex = 10;
            this.label_subLabel_SavedGame_Folder.Text = "...";
            // 
            // pic_SavedGame
            // 
            this.pic_SavedGame.Image = global::DCE_Manager.Properties.Resources.icons8_warning_blue_30;
            this.pic_SavedGame.Location = new System.Drawing.Point(5, 15);
            this.pic_SavedGame.Name = "pic_SavedGame";
            this.pic_SavedGame.Size = new System.Drawing.Size(28, 20);
            this.pic_SavedGame.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pic_SavedGame.TabIndex = 9;
            this.pic_SavedGame.TabStop = false;
            // 
            // label_SavedGame_Folder
            // 
            this.label_SavedGame_Folder.AutoSize = true;
            this.label_SavedGame_Folder.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_SavedGame_Folder.Location = new System.Drawing.Point(62, 17);
            this.label_SavedGame_Folder.Name = "label_SavedGame_Folder";
            this.label_SavedGame_Folder.Size = new System.Drawing.Size(135, 15);
            this.label_SavedGame_Folder.TabIndex = 6;
            this.label_SavedGame_Folder.Text = "DCS Saved Game Folder";
            // 
            // label_DCS_INSTALLATION
            // 
            this.label_DCS_INSTALLATION.AutoSize = true;
            this.label_DCS_INSTALLATION.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_DCS_INSTALLATION.Location = new System.Drawing.Point(8, 8);
            this.label_DCS_INSTALLATION.Name = "label_DCS_INSTALLATION";
            this.label_DCS_INSTALLATION.Size = new System.Drawing.Size(156, 21);
            this.label_DCS_INSTALLATION.TabIndex = 7;
            this.label_DCS_INSTALLATION.Text = "DCS INSTALLATION";
            // 
            // but_PATH_Modify
            // 
            this.but_PATH_Modify.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.but_PATH_Modify.Location = new System.Drawing.Point(588, 8);
            this.but_PATH_Modify.Name = "but_PATH_Modify";
            this.but_PATH_Modify.Size = new System.Drawing.Size(55, 28);
            this.but_PATH_Modify.TabIndex = 2;
            this.but_PATH_Modify.Text = "Modify";
            this.but_PATH_Modify.UseVisualStyleBackColor = true;
            this.but_PATH_Modify.Click += new System.EventHandler(this.but_PATH_Modify_Click);
            // 
            // Box_DCS_Root
            // 
            this.Box_DCS_Root.Controls.Add(this.Label_subLabel_DCS);
            this.Box_DCS_Root.Controls.Add(this.pic_DCS_Root);
            this.Box_DCS_Root.Controls.Add(this.Label_DCS);
            this.Box_DCS_Root.Controls.Add(this.textBox_PATH_DCS_Root);
            this.Box_DCS_Root.Controls.Add(this.m_But_Install_Browse_DcsPath);
            this.Box_DCS_Root.Location = new System.Drawing.Point(12, 35);
            this.Box_DCS_Root.Margin = new System.Windows.Forms.Padding(2);
            this.Box_DCS_Root.Name = "Box_DCS_Root";
            this.Box_DCS_Root.Padding = new System.Windows.Forms.Padding(2);
            this.Box_DCS_Root.Size = new System.Drawing.Size(631, 66);
            this.Box_DCS_Root.TabIndex = 0;
            this.Box_DCS_Root.TabStop = false;
            // 
            // Label_subLabel_DCS
            // 
            this.Label_subLabel_DCS.AutoSize = true;
            this.Label_subLabel_DCS.Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Label_subLabel_DCS.Location = new System.Drawing.Point(62, 41);
            this.Label_subLabel_DCS.Name = "Label_subLabel_DCS";
            this.Label_subLabel_DCS.Size = new System.Drawing.Size(16, 13);
            this.Label_subLabel_DCS.TabIndex = 10;
            this.Label_subLabel_DCS.Text = "...";
            // 
            // pic_DCS_Root
            // 
            this.pic_DCS_Root.Image = global::DCE_Manager.Properties.Resources.icons8_warning_blue_30;
            this.pic_DCS_Root.Location = new System.Drawing.Point(5, 15);
            this.pic_DCS_Root.Name = "pic_DCS_Root";
            this.pic_DCS_Root.Size = new System.Drawing.Size(28, 20);
            this.pic_DCS_Root.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pic_DCS_Root.TabIndex = 9;
            this.pic_DCS_Root.TabStop = false;
            // 
            // tabPageLeft_Campaigns
            // 
            this.tabPageLeft_Campaigns.Controls.Add(this.dataGridViewCampaigns);
            this.tabPageLeft_Campaigns.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Campaigns.Margin = new System.Windows.Forms.Padding(2);
            this.tabPageLeft_Campaigns.Name = "tabPageLeft_Campaigns";
            this.tabPageLeft_Campaigns.Padding = new System.Windows.Forms.Padding(2);
            this.tabPageLeft_Campaigns.Size = new System.Drawing.Size(808, 601);
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
            this.dataGridViewCampaigns.Size = new System.Drawing.Size(806, 559);
            this.dataGridViewCampaigns.TabIndex = 0;
            // 
            // tabPageLeft_Update
            // 
            this.tabPageLeft_Update.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeft_Update.Controls.Add(this.groupBox_Update_DCE_M);
            this.tabPageLeft_Update.Controls.Add(this.groupBox_DwlCampaign);
            this.tabPageLeft_Update.Controls.Add(this.groupBox_Update_ScriptMod);
            this.tabPageLeft_Update.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Update.Name = "tabPageLeft_Update";
            this.tabPageLeft_Update.Size = new System.Drawing.Size(808, 601);
            this.tabPageLeft_Update.TabIndex = 2;
            this.tabPageLeft_Update.Text = "Update";
            // 
            // groupBox_Update_DCE_M
            // 
            this.groupBox_Update_DCE_M.Controls.Add(this.label15);
            this.groupBox_Update_DCE_M.Controls.Add(this.label16);
            this.groupBox_Update_DCE_M.Controls.Add(this.label_DCEM_Status);
            this.groupBox_Update_DCE_M.Controls.Add(this.pictureBox_DCE_Manager_Status);
            this.groupBox_Update_DCE_M.Controls.Add(this.pictureBox_Update_DCE_Manager);
            this.groupBox_Update_DCE_M.Controls.Add(this.DCEManagerStatusLabel);
            this.groupBox_Update_DCE_M.Controls.Add(this.label13);
            this.groupBox_Update_DCE_M.Controls.Add(this.label14);
            this.groupBox_Update_DCE_M.Controls.Add(this.DCEManagerInstalledVersion);
            this.groupBox_Update_DCE_M.Controls.Add(this.DCEManagerAvailableVersion);
            this.groupBox_Update_DCE_M.Controls.Add(this.DCEManagerUpdateButton);
            this.groupBox_Update_DCE_M.Location = new System.Drawing.Point(15, 114);
            this.groupBox_Update_DCE_M.Name = "groupBox_Update_DCE_M";
            this.groupBox_Update_DCE_M.Size = new System.Drawing.Size(775, 84);
            this.groupBox_Update_DCE_M.TabIndex = 4;
            this.groupBox_Update_DCE_M.TabStop = false;
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
            this.pictureBox_Update_DCE_Manager.Cursor = System.Windows.Forms.Cursors.Hand;
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
            // groupBox_Update_ScriptMod
            // 
            this.groupBox_Update_ScriptMod.Controls.Add(this.label_ScriptsMod_B);
            this.groupBox_Update_ScriptMod.Controls.Add(this.label_ScriptsMod_A);
            this.groupBox_Update_ScriptMod.Controls.Add(this.label_SM_Status);
            this.groupBox_Update_ScriptMod.Controls.Add(this.pictureBox_ScriptsMod_Status);
            this.groupBox_Update_ScriptMod.Controls.Add(this.pictureBox_Update_ScriptsMod);
            this.groupBox_Update_ScriptMod.Controls.Add(this.ScriptsModStatusLabel);
            this.groupBox_Update_ScriptMod.Controls.Add(this.label12);
            this.groupBox_Update_ScriptMod.Controls.Add(this.label11);
            this.groupBox_Update_ScriptMod.Controls.Add(this.ScriptModInstalledVersion);
            this.groupBox_Update_ScriptMod.Controls.Add(this.ScriptsModUpdateButton);
            this.groupBox_Update_ScriptMod.Controls.Add(this.ScriptsModAvailableVersion);
            this.groupBox_Update_ScriptMod.Location = new System.Drawing.Point(15, 15);
            this.groupBox_Update_ScriptMod.Name = "groupBox_Update_ScriptMod";
            this.groupBox_Update_ScriptMod.Size = new System.Drawing.Size(775, 93);
            this.groupBox_Update_ScriptMod.TabIndex = 2;
            this.groupBox_Update_ScriptMod.TabStop = false;
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
            this.pictureBox_Update_ScriptsMod.Cursor = System.Windows.Forms.Cursors.Hand;
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
            // 
            // tabPageLeftNews
            // 
            this.tabPageLeftNews.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeftNews.Controls.Add(this.panel_News);
            this.tabPageLeftNews.Controls.Add(this.textBox_News);
            this.tabPageLeftNews.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeftNews.Name = "tabPageLeftNews";
            this.tabPageLeftNews.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageLeftNews.Size = new System.Drawing.Size(808, 601);
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
            this.tabPageLeft_Tools.Size = new System.Drawing.Size(808, 601);
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
            this.but_ASTI.Click += new System.EventHandler(this.but_ASTI_Click);
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
            this.tabPageLeft_Options.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeft_Options.Controls.Add(this.pictureBox4);
            this.tabPageLeft_Options.Controls.Add(this.pic_preferences);
            this.tabPageLeft_Options.Controls.Add(this.panel_OpenDoc);
            this.tabPageLeft_Options.Controls.Add(this.label_tolls);
            this.tabPageLeft_Options.Controls.Add(this.label_preference);
            this.tabPageLeft_Options.Controls.Add(this.panel_ViewLog);
            this.tabPageLeft_Options.Controls.Add(this.panel_preferences);
            this.tabPageLeft_Options.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_Options.Margin = new System.Windows.Forms.Padding(2);
            this.tabPageLeft_Options.Name = "tabPageLeft_Options";
            this.tabPageLeft_Options.Padding = new System.Windows.Forms.Padding(2);
            this.tabPageLeft_Options.Size = new System.Drawing.Size(808, 601);
            this.tabPageLeft_Options.TabIndex = 7;
            this.tabPageLeft_Options.Text = "Options";
            // 
            // pictureBox4
            // 
            this.pictureBox4.Image = global::DCE_Manager.Properties.Resources.icons8_outils_50;
            this.pictureBox4.Location = new System.Drawing.Point(26, 190);
            this.pictureBox4.Name = "pictureBox4";
            this.pictureBox4.Size = new System.Drawing.Size(31, 32);
            this.pictureBox4.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox4.TabIndex = 43;
            this.pictureBox4.TabStop = false;
            // 
            // pic_preferences
            // 
            this.pic_preferences.Image = global::DCE_Manager.Properties.Resources.icons8_roue_dentée_50;
            this.pic_preferences.Location = new System.Drawing.Point(26, 29);
            this.pic_preferences.Name = "pic_preferences";
            this.pic_preferences.Size = new System.Drawing.Size(31, 32);
            this.pic_preferences.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pic_preferences.TabIndex = 42;
            this.pic_preferences.TabStop = false;
            // 
            // panel_OpenDoc
            // 
            this.panel_OpenDoc.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel_OpenDoc.Controls.Add(this.pic_OpenDoc_ArrowLog);
            this.panel_OpenDoc.Controls.Add(this.lbl_OpenDocTitle);
            this.panel_OpenDoc.Controls.Add(this.pic_OpenDocIcon);
            this.panel_OpenDoc.Controls.Add(this.lbl_OpenDocDesc);
            this.panel_OpenDoc.Cursor = System.Windows.Forms.Cursors.Hand;
            this.panel_OpenDoc.Location = new System.Drawing.Point(26, 334);
            this.panel_OpenDoc.Name = "panel_OpenDoc";
            this.panel_OpenDoc.Size = new System.Drawing.Size(752, 100);
            this.panel_OpenDoc.TabIndex = 41;
            // 
            // pic_OpenDoc_ArrowLog
            // 
            this.pic_OpenDoc_ArrowLog.Image = global::DCE_Manager.Properties.Resources.icons8_forward_50;
            this.pic_OpenDoc_ArrowLog.Location = new System.Drawing.Point(681, 17);
            this.pic_OpenDoc_ArrowLog.Name = "pic_OpenDoc_ArrowLog";
            this.pic_OpenDoc_ArrowLog.Size = new System.Drawing.Size(68, 50);
            this.pic_OpenDoc_ArrowLog.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pic_OpenDoc_ArrowLog.TabIndex = 41;
            this.pic_OpenDoc_ArrowLog.TabStop = false;
            // 
            // lbl_OpenDocTitle
            // 
            this.lbl_OpenDocTitle.AutoSize = true;
            this.lbl_OpenDocTitle.Font = new System.Drawing.Font("Segoe UI", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lbl_OpenDocTitle.Location = new System.Drawing.Point(132, 23);
            this.lbl_OpenDocTitle.Name = "lbl_OpenDocTitle";
            this.lbl_OpenDocTitle.Size = new System.Drawing.Size(207, 20);
            this.lbl_OpenDocTitle.TabIndex = 40;
            this.lbl_OpenDocTitle.Text = "Open Documentation Folder";
            // 
            // pic_OpenDocIcon
            // 
            this.pic_OpenDocIcon.Image = global::DCE_Manager.Properties.Resources.icons8_dossier_ouvert_72;
            this.pic_OpenDocIcon.Location = new System.Drawing.Point(14, 23);
            this.pic_OpenDocIcon.Name = "pic_OpenDocIcon";
            this.pic_OpenDocIcon.Size = new System.Drawing.Size(100, 51);
            this.pic_OpenDocIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pic_OpenDocIcon.TabIndex = 39;
            this.pic_OpenDocIcon.TabStop = false;
            // 
            // lbl_OpenDocDesc
            // 
            this.lbl_OpenDocDesc.AutoSize = true;
            this.lbl_OpenDocDesc.Location = new System.Drawing.Point(133, 57);
            this.lbl_OpenDocDesc.Name = "lbl_OpenDocDesc";
            this.lbl_OpenDocDesc.Size = new System.Drawing.Size(223, 13);
            this.lbl_OpenDocDesc.TabIndex = 38;
            this.lbl_OpenDocDesc.Text = "Open the folder containing the DCE Manager.\r\n";
            // 
            // label_tolls
            // 
            this.label_tolls.AutoSize = true;
            this.label_tolls.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_tolls.Location = new System.Drawing.Point(63, 194);
            this.label_tolls.Name = "label_tolls";
            this.label_tolls.Size = new System.Drawing.Size(235, 25);
            this.label_tolls.TabIndex = 40;
            this.label_tolls.Text = "TOOLS && MAINTENANCE";
            // 
            // label_preference
            // 
            this.label_preference.AutoSize = true;
            this.label_preference.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_preference.Location = new System.Drawing.Point(63, 34);
            this.label_preference.Name = "label_preference";
            this.label_preference.Size = new System.Drawing.Size(136, 25);
            this.label_preference.TabIndex = 39;
            this.label_preference.Text = "PREFERENCES";
            // 
            // panel_ViewLog
            // 
            this.panel_ViewLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel_ViewLog.Controls.Add(this.pic_ViewLog_ArrowLog);
            this.panel_ViewLog.Controls.Add(this.lbl_ViewLogTitle);
            this.panel_ViewLog.Controls.Add(this.pic_ViewLogIcon);
            this.panel_ViewLog.Controls.Add(this.lbl_ViewLogDesc);
            this.panel_ViewLog.Cursor = System.Windows.Forms.Cursors.Hand;
            this.panel_ViewLog.Location = new System.Drawing.Point(26, 228);
            this.panel_ViewLog.Name = "panel_ViewLog";
            this.panel_ViewLog.Size = new System.Drawing.Size(752, 100);
            this.panel_ViewLog.TabIndex = 38;
            // 
            // pic_ViewLog_ArrowLog
            // 
            this.pic_ViewLog_ArrowLog.Image = global::DCE_Manager.Properties.Resources.icons8_forward_50;
            this.pic_ViewLog_ArrowLog.Location = new System.Drawing.Point(681, 28);
            this.pic_ViewLog_ArrowLog.Name = "pic_ViewLog_ArrowLog";
            this.pic_ViewLog_ArrowLog.Size = new System.Drawing.Size(68, 50);
            this.pic_ViewLog_ArrowLog.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pic_ViewLog_ArrowLog.TabIndex = 40;
            this.pic_ViewLog_ArrowLog.TabStop = false;
            // 
            // lbl_ViewLogTitle
            // 
            this.lbl_ViewLogTitle.AutoSize = true;
            this.lbl_ViewLogTitle.Font = new System.Drawing.Font("Segoe UI", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lbl_ViewLogTitle.Location = new System.Drawing.Point(132, 28);
            this.lbl_ViewLogTitle.Name = "lbl_ViewLogTitle";
            this.lbl_ViewLogTitle.Size = new System.Drawing.Size(73, 20);
            this.lbl_ViewLogTitle.TabIndex = 39;
            this.lbl_ViewLogTitle.Text = "View Log";
            // 
            // pic_ViewLogIcon
            // 
            this.pic_ViewLogIcon.Image = global::DCE_Manager.Properties.Resources.icons8_document_72;
            this.pic_ViewLogIcon.Location = new System.Drawing.Point(14, 28);
            this.pic_ViewLogIcon.Name = "pic_ViewLogIcon";
            this.pic_ViewLogIcon.Size = new System.Drawing.Size(100, 50);
            this.pic_ViewLogIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pic_ViewLogIcon.TabIndex = 38;
            this.pic_ViewLogIcon.TabStop = false;
            // 
            // lbl_ViewLogDesc
            // 
            this.lbl_ViewLogDesc.AutoSize = true;
            this.lbl_ViewLogDesc.Location = new System.Drawing.Point(133, 58);
            this.lbl_ViewLogDesc.Name = "lbl_ViewLogDesc";
            this.lbl_ViewLogDesc.Size = new System.Drawing.Size(228, 26);
            this.lbl_ViewLogDesc.TabIndex = 37;
            this.lbl_ViewLogDesc.Text = "Open the application log file for troubleshooting\nand diagnostics.";
            // 
            // panel_preferences
            // 
            this.panel_preferences.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel_preferences.Controls.Add(this.pictureBox8);
            this.panel_preferences.Controls.Add(this.checkBox_Stat_anonym);
            this.panel_preferences.Controls.Add(this.label_statistics_explain);
            this.panel_preferences.Controls.Add(this.Readme);
            this.panel_preferences.Location = new System.Drawing.Point(26, 72);
            this.panel_preferences.Name = "panel_preferences";
            this.panel_preferences.Size = new System.Drawing.Size(752, 100);
            this.panel_preferences.TabIndex = 37;
            // 
            // pictureBox8
            // 
            this.pictureBox8.Image = global::DCE_Manager.Properties.Resources.icons8_lien_externe_30;
            this.pictureBox8.Location = new System.Drawing.Point(712, 29);
            this.pictureBox8.Name = "pictureBox8";
            this.pictureBox8.Size = new System.Drawing.Size(34, 21);
            this.pictureBox8.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox8.TabIndex = 37;
            this.pictureBox8.TabStop = false;
            // 
            // checkBox_Stat_anonym
            // 
            this.checkBox_Stat_anonym.AutoSize = true;
            this.checkBox_Stat_anonym.Checked = true;
            this.checkBox_Stat_anonym.CheckState = System.Windows.Forms.CheckState.Checked;
            this.checkBox_Stat_anonym.Font = new System.Drawing.Font("Segoe UI Semibold", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkBox_Stat_anonym.Location = new System.Drawing.Point(59, 23);
            this.checkBox_Stat_anonym.Margin = new System.Windows.Forms.Padding(2);
            this.checkBox_Stat_anonym.Name = "checkBox_Stat_anonym";
            this.checkBox_Stat_anonym.Size = new System.Drawing.Size(229, 21);
            this.checkBox_Stat_anonym.TabIndex = 34;
            this.checkBox_Stat_anonym.Text = "Send anonymous usage statistics";
            this.checkBox_Stat_anonym.UseVisualStyleBackColor = true;
            // 
            // label_statistics_explain
            // 
            this.label_statistics_explain.AutoSize = true;
            this.label_statistics_explain.Location = new System.Drawing.Point(78, 50);
            this.label_statistics_explain.Name = "label_statistics_explain";
            this.label_statistics_explain.Size = new System.Drawing.Size(250, 26);
            this.label_statistics_explain.TabIndex = 36;
            this.label_statistics_explain.Text = "Help improve DCE Manager by sending anonymous\r\nusage data. No personal informatio" +
    "n is collected.";
            // 
            // Readme
            // 
            this.Readme.AutoSize = true;
            this.Readme.ForeColor = System.Drawing.SystemColors.Control;
            this.Readme.Location = new System.Drawing.Point(662, 31);
            this.Readme.Name = "Readme";
            this.Readme.Size = new System.Drawing.Size(47, 13);
            this.Readme.TabIndex = 35;
            this.Readme.TabStop = true;
            this.Readme.Text = "Readme";
            this.Readme.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.Readme_LinkClicked);
            // 
            // tabPageLeft_About
            // 
            this.tabPageLeft_About.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageLeft_About.Controls.Add(this.linkLabel_Icons8);
            this.tabPageLeft_About.Controls.Add(this.label9);
            this.tabPageLeft_About.Controls.Add(this.textBox_ChangelogScriptsMod);
            this.tabPageLeft_About.Controls.Add(this.label8);
            this.tabPageLeft_About.Controls.Add(this.textBox_changelog);
            this.tabPageLeft_About.Controls.Add(this.textBox_Credits);
            this.tabPageLeft_About.Controls.Add(this.label5);
            this.tabPageLeft_About.Location = new System.Drawing.Point(4, 22);
            this.tabPageLeft_About.Name = "tabPageLeft_About";
            this.tabPageLeft_About.Size = new System.Drawing.Size(808, 601);
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
            this.label9.Location = new System.Drawing.Point(46, 275);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(139, 13);
            this.label9.TabIndex = 5;
            this.label9.Text = "Changelog (ScriptsMod.NG)";
            // 
            // textBox_ChangelogScriptsMod
            // 
            this.textBox_ChangelogScriptsMod.Location = new System.Drawing.Point(48, 291);
            this.textBox_ChangelogScriptsMod.Multiline = true;
            this.textBox_ChangelogScriptsMod.Name = "textBox_ChangelogScriptsMod";
            this.textBox_ChangelogScriptsMod.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_ChangelogScriptsMod.Size = new System.Drawing.Size(731, 153);
            this.textBox_ChangelogScriptsMod.TabIndex = 4;
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(45, 166);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(157, 13);
            this.label8.TabIndex = 3;
            this.label8.Text = "Changelog (DCE_Manager.exe)";
            // 
            // textBox_changelog
            // 
            this.textBox_changelog.Location = new System.Drawing.Point(48, 182);
            this.textBox_changelog.Multiline = true;
            this.textBox_changelog.Name = "textBox_changelog";
            this.textBox_changelog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_changelog.Size = new System.Drawing.Size(731, 90);
            this.textBox_changelog.TabIndex = 2;
            // 
            // textBox_Credits
            // 
            this.textBox_Credits.Location = new System.Drawing.Point(45, 20);
            this.textBox_Credits.Multiline = true;
            this.textBox_Credits.Name = "textBox_Credits";
            this.textBox_Credits.Size = new System.Drawing.Size(488, 122);
            this.textBox_Credits.TabIndex = 1;
            this.textBox_Credits.Text = resources.GetString("textBox_Credits.Text");
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
            // comboBox_Config
            // 
            this.comboBox_Config.FormattingEnabled = true;
            this.comboBox_Config.Location = new System.Drawing.Point(1051, 8);
            this.comboBox_Config.Name = "comboBox_Config";
            this.comboBox_Config.Size = new System.Drawing.Size(145, 21);
            this.comboBox_Config.TabIndex = 18;
            this.comboBox_Config.SelectedIndexChanged += new System.EventHandler(this.comboBox_Config_SelectedIndexChanged);
            // 
            // panel_top
            // 
            this.panel_top.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel_top.Controls.Add(this.but_Level_DEV);
            this.panel_top.Controls.Add(this.but_level_User);
            this.panel_top.Controls.Add(this.but_Level_CampMaker);
            this.panel_top.Controls.Add(this.label_UserLevel);
            this.panel_top.Controls.Add(this.but_Configuration_Edit);
            this.panel_top.Controls.Add(this.comboBox_Config);
            this.panel_top.Controls.Add(this.label_Config);
            this.panel_top.Location = new System.Drawing.Point(11, 11);
            this.panel_top.Margin = new System.Windows.Forms.Padding(2);
            this.panel_top.Name = "panel_top";
            this.panel_top.Size = new System.Drawing.Size(1347, 37);
            this.panel_top.TabIndex = 28;
            this.panel_top.Paint += new System.Windows.Forms.PaintEventHandler(this.panel_configuration_Paint);
            // 
            // but_Level_DEV
            // 
            this.but_Level_DEV.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.but_Level_DEV.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.but_Level_DEV.Location = new System.Drawing.Point(272, 7);
            this.but_Level_DEV.Name = "but_Level_DEV";
            this.but_Level_DEV.Size = new System.Drawing.Size(85, 23);
            this.but_Level_DEV.TabIndex = 32;
            this.but_Level_DEV.Text = "DEV";
            this.but_Level_DEV.UseVisualStyleBackColor = true;
            this.but_Level_DEV.Visible = false;
            // 
            // but_level_User
            // 
            this.but_level_User.BackColor = System.Drawing.Color.DodgerBlue;
            this.but_level_User.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.but_level_User.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.but_level_User.ForeColor = System.Drawing.Color.White;
            this.but_level_User.Location = new System.Drawing.Point(100, 7);
            this.but_level_User.Name = "but_level_User";
            this.but_level_User.Size = new System.Drawing.Size(75, 23);
            this.but_level_User.TabIndex = 31;
            this.but_level_User.Text = "Player";
            this.but_level_User.UseVisualStyleBackColor = false;
            this.but_level_User.Click += new System.EventHandler(this.but_level_User_Click);
            // 
            // but_Level_CampMaker
            // 
            this.but_Level_CampMaker.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.but_Level_CampMaker.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.but_Level_CampMaker.Location = new System.Drawing.Point(181, 7);
            this.but_Level_CampMaker.Name = "but_Level_CampMaker";
            this.but_Level_CampMaker.Size = new System.Drawing.Size(85, 23);
            this.but_Level_CampMaker.TabIndex = 30;
            this.but_Level_CampMaker.Text = "CampMaker";
            this.but_Level_CampMaker.UseVisualStyleBackColor = true;
            this.but_Level_CampMaker.Click += new System.EventHandler(this.but_Level_CampMaker_Click);
            // 
            // label_UserLevel
            // 
            this.label_UserLevel.AutoSize = true;
            this.label_UserLevel.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_UserLevel.Location = new System.Drawing.Point(42, 12);
            this.label_UserLevel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label_UserLevel.Name = "label_UserLevel";
            this.label_UserLevel.Size = new System.Drawing.Size(30, 13);
            this.label_UserLevel.TabIndex = 29;
            this.label_UserLevel.Text = "User";
            // 
            // but_Configuration_Edit
            // 
            this.but_Configuration_Edit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.but_Configuration_Edit.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.but_Configuration_Edit.Location = new System.Drawing.Point(1211, 6);
            this.but_Configuration_Edit.Name = "but_Configuration_Edit";
            this.but_Configuration_Edit.Size = new System.Drawing.Size(75, 23);
            this.but_Configuration_Edit.TabIndex = 27;
            this.but_Configuration_Edit.Text = "Edit";
            this.but_Configuration_Edit.UseVisualStyleBackColor = true;
            this.but_Configuration_Edit.Click += new System.EventHandler(this.but_EditConfig_Click);
            // 
            // label_Config
            // 
            this.label_Config.AutoSize = true;
            this.label_Config.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label_Config.Location = new System.Drawing.Point(913, 11);
            this.label_Config.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label_Config.Name = "label_Config";
            this.label_Config.Size = new System.Drawing.Size(122, 13);
            this.label_Config.TabIndex = 26;
            this.label_Config.Text = "Current Configuration";
            // 
            // panelRightView
            // 
            this.panelRightView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelRightView.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panelRightView.Location = new System.Drawing.Point(862, 53);
            this.panelRightView.Name = "panelRightView";
            this.panelRightView.Size = new System.Drawing.Size(496, 593);
            this.panelRightView.TabIndex = 29;
            // 
            // panel_Down
            // 
            this.panel_Down.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.panel_Down.Controls.Add(this.button_EXIT);
            this.panel_Down.Location = new System.Drawing.Point(862, 651);
            this.panel_Down.Margin = new System.Windows.Forms.Padding(2);
            this.panel_Down.Name = "panel_Down";
            this.panel_Down.Size = new System.Drawing.Size(496, 37);
            this.panel_Down.TabIndex = 30;
            // 
            // button_EXIT
            // 
            this.button_EXIT.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.button_EXIT.Location = new System.Drawing.Point(416, 5);
            this.button_EXIT.Name = "button_EXIT";
            this.button_EXIT.Size = new System.Drawing.Size(75, 23);
            this.button_EXIT.TabIndex = 39;
            this.button_EXIT.Text = "Exit";
            this.button_EXIT.UseVisualStyleBackColor = true;
            // 
            // dropZoneControl1
            // 
            this.dropZoneControl1.AllowDrop = true;
            this.dropZoneControl1.BackColor = System.Drawing.SystemColors.Control;
            this.dropZoneControl1.Cursor = System.Windows.Forms.Cursors.Hand;
            this.dropZoneControl1.CustomIcon = global::DCE_Manager.Properties.Resources.icons8_zip_64;
            this.dropZoneControl1.FileFilter = "Fichiers ZIP (*.zip)|*.zip";
            this.dropZoneControl1.Location = new System.Drawing.Point(12, 51);
            this.dropZoneControl1.MainText = "Drag & drop ZIP here";
            this.dropZoneControl1.Margin = new System.Windows.Forms.Padding(2);
            this.dropZoneControl1.Name = "dropZoneControl1";
            this.dropZoneControl1.Size = new System.Drawing.Size(631, 66);
            this.dropZoneControl1.SubText = "or click to browse";
            this.dropZoneControl1.TabIndex = 27;
            // 
            // Main_Form
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.ClientSize = new System.Drawing.Size(1377, 699);
            this.Controls.Add(this.panel_Down);
            this.Controls.Add(this.panelRightView);
            this.Controls.Add(this.panel_top);
            this.Controls.Add(this.tabControl_LEFT);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.Name = "Main_Form";
            this.Text = "DCE_Manager";
            this.tabControl_LEFT.ResumeLayout(false);
            this.tabPageLeft_Install.ResumeLayout(false);
            this.panel_install_campaign.ResumeLayout(false);
            this.panel_install_campaign.PerformLayout();
            this.panel_PATH.ResumeLayout(false);
            this.panel_PATH.PerformLayout();
            this.box_OVGME.ResumeLayout(false);
            this.box_OVGME.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_OVGME)).EndInit();
            this.Box_DCS_SavedGame.ResumeLayout(false);
            this.Box_DCS_SavedGame.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_SavedGame)).EndInit();
            this.Box_DCS_Root.ResumeLayout(false);
            this.Box_DCS_Root.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_DCS_Root)).EndInit();
            this.tabPageLeft_Campaigns.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewCampaigns)).EndInit();
            this.tabPageLeft_Update.ResumeLayout(false);
            this.groupBox_Update_DCE_M.ResumeLayout(false);
            this.groupBox_Update_DCE_M.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_DCE_Manager_Status)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Update_DCE_Manager)).EndInit();
            this.groupBox_DwlCampaign.ResumeLayout(false);
            this.groupBox_DwlCampaign.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCampaignDownload)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.CampaignDataGridView)).EndInit();
            this.groupBox_Update_ScriptMod.ResumeLayout(false);
            this.groupBox_Update_ScriptMod.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_ScriptsMod_Status)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox_Update_ScriptsMod)).EndInit();
            this.tabPageLeftNews.ResumeLayout(false);
            this.tabPageLeftNews.PerformLayout();
            this.tabPageLeft_Tools.ResumeLayout(false);
            this.tabPageLeft_Tools.PerformLayout();
            this.tabPageLeft_Options.ResumeLayout(false);
            this.tabPageLeft_Options.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pic_preferences)).EndInit();
            this.panel_OpenDoc.ResumeLayout(false);
            this.panel_OpenDoc.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_OpenDoc_ArrowLog)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pic_OpenDocIcon)).EndInit();
            this.panel_ViewLog.ResumeLayout(false);
            this.panel_ViewLog.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pic_ViewLog_ArrowLog)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pic_ViewLogIcon)).EndInit();
            this.panel_preferences.ResumeLayout(false);
            this.panel_preferences.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox8)).EndInit();
            this.tabPageLeft_About.ResumeLayout(false);
            this.tabPageLeft_About.PerformLayout();
            this.panel_top.ResumeLayout(false);
            this.panel_top.PerformLayout();
            this.panel_Down.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        //private void textBoxCampEdit_TextChanged(object sender, EventArgs e)
        //{
        //    throw new NotImplementedException();
        //}

        #endregion
        public System.Windows.Forms.Button m_But_Install_Browse_DcsPath;
        public System.Windows.Forms.Button m_But_Install_Browse_SavedGame;
        public System.Windows.Forms.TextBox textBox_PATH_DCS_Root;
        public System.Windows.Forms.TextBox textBox_SavedGames;
        private System.Windows.Forms.Label Label_DCS;
        private System.Windows.Forms.Button button_InstallCampaign;
        private System.Windows.Forms.TextBox textBox_OvGME;
        private System.Windows.Forms.Button m_but_Install_Browse_OVGME;
        public System.Windows.Forms.ToolTip toolTip1;
        public System.Windows.Forms.TabControl tabControl_LEFT;
        private System.Windows.Forms.TabPage tabPageLeft_Install;
        public System.Windows.Forms.TabPage tabPageLeft_Update;
        private System.Windows.Forms.TabPage tabPageLeft_About;
        public System.Windows.Forms.Button ScriptsModUpdateButton;
        public System.Windows.Forms.Label ScriptsModAvailableVersion;
        public System.Windows.Forms.GroupBox groupBox_DwlCampaign;
        private System.Windows.Forms.GroupBox groupBox_Update_ScriptMod;
        private System.Windows.Forms.TextBox textBox_Credits;
        private System.Windows.Forms.Label label5;
        public System.Windows.Forms.ComboBox comboBox_Config;
        private System.Windows.Forms.TextBox textBox_changelog;
        private System.Windows.Forms.GroupBox groupBox_Update_DCE_M;
        public System.Windows.Forms.Button DCEManagerUpdateButton;
        private System.Windows.Forms.Label label8;
        public System.Windows.Forms.Label DCEManagerAvailableVersion;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.TextBox textBox_ChangelogScriptsMod;
        private System.Windows.Forms.TabPage tabPageLeftNews;
        private System.Windows.Forms.TextBox textBox_News;
        public System.Windows.Forms.Label ScriptModInstalledVersion;
        public System.Windows.Forms.Label DCEManagerInstalledVersion;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.ToolTip toolTip3;
        private System.Windows.Forms.Panel panel_News;
        private System.Windows.Forms.TabPage tabPageLeft_Tools;

        private System.Windows.Forms.Button button_theWay;
        

        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label2;

        public System.Windows.Forms.Button but_ASTI;
        private System.Windows.Forms.TextBox textBox_ASTI;
        public System.Windows.Forms.TabPage tabPageLeft_Campaigns;
        public System.Windows.Forms.DataGridView dataGridViewCampaigns;
        public System.Windows.Forms.Label ScriptsModStatusLabel;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.TabPage tabPageLeft_Options;
        private System.Windows.Forms.CheckBox checkBox_Stat_anonym;
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
        private System.Windows.Forms.LinkLabel Readme;
        private System.Windows.Forms.Label label_statistics_explain;
        private System.Windows.Forms.Label label_tolls;
        private System.Windows.Forms.Label label_preference;
        private System.Windows.Forms.Panel panel_ViewLog;
        private System.Windows.Forms.Label lbl_ViewLogDesc;
        private System.Windows.Forms.Panel panel_preferences;
        private System.Windows.Forms.Panel panel_OpenDoc;
        private System.Windows.Forms.PictureBox pic_OpenDocIcon;
        private System.Windows.Forms.Label lbl_OpenDocDesc;
        private System.Windows.Forms.PictureBox pic_ViewLogIcon;
        private System.Windows.Forms.Label lbl_ViewLogTitle;
        private System.Windows.Forms.PictureBox pic_ViewLog_ArrowLog;
        private System.Windows.Forms.Label lbl_OpenDocTitle;
        private System.Windows.Forms.PictureBox pictureBox8;
        private System.Windows.Forms.PictureBox pic_OpenDoc_ArrowLog;
        private System.Windows.Forms.PictureBox pic_preferences;
        private System.Windows.Forms.PictureBox pictureBox4;
        private System.Windows.Forms.Panel panel_PATH;
        private System.Windows.Forms.GroupBox Box_DCS_Root;
        public System.Windows.Forms.Button but_PATH_Modify;
        private System.Windows.Forms.Label label_DCS_INSTALLATION;
        public System.Windows.Forms.PictureBox pic_DCS_Root;
        private System.Windows.Forms.Label Label_subLabel_DCS;
        private System.Windows.Forms.GroupBox Box_DCS_SavedGame;
        private System.Windows.Forms.Label label_subLabel_SavedGame_Folder;
        public System.Windows.Forms.PictureBox pic_SavedGame;
        private System.Windows.Forms.Label label_SavedGame_Folder;
        private System.Windows.Forms.GroupBox box_OVGME;
        private System.Windows.Forms.Label label_sub_OVGME;
        public System.Windows.Forms.PictureBox pic_OVGME;
        private System.Windows.Forms.Label label_OVGME_b;
        public System.Windows.Forms.Button but_PATH_CANCEL;
        public System.Windows.Forms.Button but_PATH_SAVE;
        private Controls.DropZoneControl dropZoneControl1;
        private System.Windows.Forms.Panel panel_install_campaign;
        private System.Windows.Forms.Label label_install_campaign;
        private System.Windows.Forms.Panel panel_top;
        private System.Windows.Forms.Button but_Configuration_Edit;
        private System.Windows.Forms.Label label_Config;
        private System.Windows.Forms.Panel panelRightView;
        private System.Windows.Forms.Button but_level_User;
        private System.Windows.Forms.Button but_Level_CampMaker;
        private System.Windows.Forms.Label label_UserLevel;
        private System.Windows.Forms.Panel panel_Down;
        public System.Windows.Forms.Button button_EXIT;
        public System.Windows.Forms.Button but_Level_DEV;
    }
}

