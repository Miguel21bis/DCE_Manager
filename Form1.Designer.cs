namespace WindowsFormsDCE
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
            this.linkLabelCampaign = new System.Windows.Forms.LinkLabel();
            this.linkLabelOvGME = new System.Windows.Forms.LinkLabel();
            this.Label_OvGME = new System.Windows.Forms.Label();
            this.textBox_OvGME = new System.Windows.Forms.TextBox();
            this.button2 = new System.Windows.Forms.Button();
            this.Label_Campaign = new System.Windows.Forms.Label();
            this.textBox_Campaign = new System.Windows.Forms.TextBox();
            this.m_Button_Campaign = new System.Windows.Forms.Button();
            this.Label_SavedGames = new System.Windows.Forms.Label();
            this.Label_DCS = new System.Windows.Forms.Label();
            this.textBox_SavedGames = new System.Windows.Forms.TextBox();
            this.m_buttonNext = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.label1 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.pictureBox3 = new System.Windows.Forms.PictureBox();
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.label2 = new System.Windows.Forms.Label();
            this.checkBoxSM = new System.Windows.Forms.CheckBox();
            this.checkBoxActiveFolder = new System.Windows.Forms.CheckBox();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox3)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // m_ButtonDcsPath
            // 
            this.m_ButtonDcsPath.Location = new System.Drawing.Point(417, 60);
            this.m_ButtonDcsPath.Name = "m_ButtonDcsPath";
            this.m_ButtonDcsPath.Size = new System.Drawing.Size(110, 28);
            this.m_ButtonDcsPath.TabIndex = 1;
            this.m_ButtonDcsPath.Text = "Browse...";
            this.m_ButtonDcsPath.UseVisualStyleBackColor = true;
            this.m_ButtonDcsPath.Click += new System.EventHandler(this.m_ButtonDcsPath_Click);
            // 
            // m_ButtonSavedGames
            // 
            this.m_ButtonSavedGames.Location = new System.Drawing.Point(417, 109);
            this.m_ButtonSavedGames.Name = "m_ButtonSavedGames";
            this.m_ButtonSavedGames.Size = new System.Drawing.Size(110, 28);
            this.m_ButtonSavedGames.TabIndex = 2;
            this.m_ButtonSavedGames.Text = "Browse...";
            this.m_ButtonSavedGames.UseVisualStyleBackColor = true;
            this.m_ButtonSavedGames.Click += new System.EventHandler(this.m_ButtonSavedGame_Click);
            // 
            // textBox_DCS
            // 
            this.textBox_DCS.Location = new System.Drawing.Point(147, 65);
            this.textBox_DCS.Name = "textBox_DCS";
            this.textBox_DCS.Size = new System.Drawing.Size(264, 20);
            this.textBox_DCS.TabIndex = 4;
            this.textBox_DCS.TextChanged += new System.EventHandler(this.textBox2_TextChanged);
            // 
            // groupBox1
            // 
            this.groupBox1.BackColor = System.Drawing.SystemColors.Control;
            this.groupBox1.Controls.Add(this.linkLabelCampaign);
            this.groupBox1.Controls.Add(this.linkLabelOvGME);
            this.groupBox1.Controls.Add(this.Label_OvGME);
            this.groupBox1.Controls.Add(this.textBox_OvGME);
            this.groupBox1.Controls.Add(this.button2);
            this.groupBox1.Controls.Add(this.Label_Campaign);
            this.groupBox1.Controls.Add(this.textBox_Campaign);
            this.groupBox1.Controls.Add(this.m_Button_Campaign);
            this.groupBox1.Controls.Add(this.Label_SavedGames);
            this.groupBox1.Controls.Add(this.Label_DCS);
            this.groupBox1.Controls.Add(this.textBox_SavedGames);
            this.groupBox1.Controls.Add(this.textBox_DCS);
            this.groupBox1.Controls.Add(this.m_ButtonDcsPath);
            this.groupBox1.Controls.Add(this.m_ButtonSavedGames);
            this.groupBox1.Location = new System.Drawing.Point(25, 51);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(534, 195);
            this.groupBox1.TabIndex = 6;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Path";
            this.groupBox1.Enter += new System.EventHandler(this.groupBox1_Enter);
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
            // linkLabelOvGME
            // 
            this.linkLabelOvGME.AutoSize = true;
            this.linkLabelOvGME.Location = new System.Drawing.Point(11, 164);
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
            this.Label_OvGME.Location = new System.Drawing.Point(62, 164);
            this.Label_OvGME.Name = "Label_OvGME";
            this.Label_OvGME.Size = new System.Drawing.Size(60, 13);
            this.Label_OvGME.TabIndex = 13;
            this.Label_OvGME.Text = "Mod Folder";
            this.Label_OvGME.Click += new System.EventHandler(this.label4_Click);
            // 
            // textBox_OvGME
            // 
            this.textBox_OvGME.Location = new System.Drawing.Point(147, 161);
            this.textBox_OvGME.Name = "textBox_OvGME";
            this.textBox_OvGME.Size = new System.Drawing.Size(264, 20);
            this.textBox_OvGME.TabIndex = 12;
            this.textBox_OvGME.TextChanged += new System.EventHandler(this.textBox4_TextChanged);
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(417, 156);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(110, 28);
            this.button2.TabIndex = 11;
            this.button2.Text = "Browse...";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.m_buttonOvGME_Click);
            // 
            // Label_Campaign
            // 
            this.Label_Campaign.AutoSize = true;
            this.Label_Campaign.Location = new System.Drawing.Point(11, 22);
            this.Label_Campaign.Name = "Label_Campaign";
            this.Label_Campaign.Size = new System.Drawing.Size(61, 13);
            this.Label_Campaign.TabIndex = 10;
            this.Label_Campaign.Text = "Choose Zip";
            this.Label_Campaign.Click += new System.EventHandler(this.label1_Click);
            // 
            // textBox_Campaign
            // 
            this.textBox_Campaign.Location = new System.Drawing.Point(147, 19);
            this.textBox_Campaign.Name = "textBox_Campaign";
            this.textBox_Campaign.Size = new System.Drawing.Size(264, 20);
            this.textBox_Campaign.TabIndex = 9;
            this.textBox_Campaign.TextChanged += new System.EventHandler(this.textBox1_TextChanged);
            // 
            // m_Button_Campaign
            // 
            this.m_Button_Campaign.Location = new System.Drawing.Point(417, 14);
            this.m_Button_Campaign.Name = "m_Button_Campaign";
            this.m_Button_Campaign.Size = new System.Drawing.Size(110, 28);
            this.m_Button_Campaign.TabIndex = 8;
            this.m_Button_Campaign.Text = "Browse...";
            this.m_Button_Campaign.UseVisualStyleBackColor = true;
            this.m_Button_Campaign.Click += new System.EventHandler(this.m_ButtonCampaign_Click);
            // 
            // Label_SavedGames
            // 
            this.Label_SavedGames.AutoSize = true;
            this.Label_SavedGames.Location = new System.Drawing.Point(11, 117);
            this.Label_SavedGames.Name = "Label_SavedGames";
            this.Label_SavedGames.Size = new System.Drawing.Size(131, 13);
            this.Label_SavedGames.TabIndex = 7;
            this.Label_SavedGames.Text = "DCS Saved Games Folder";
            this.Label_SavedGames.Click += new System.EventHandler(this.label3_Click);
            // 
            // Label_DCS
            // 
            this.Label_DCS.AutoSize = true;
            this.Label_DCS.Location = new System.Drawing.Point(11, 68);
            this.Label_DCS.Name = "Label_DCS";
            this.Label_DCS.Size = new System.Drawing.Size(87, 13);
            this.Label_DCS.TabIndex = 6;
            this.Label_DCS.Text = "DCS Root Folder";
            this.Label_DCS.Click += new System.EventHandler(this.label2_Click);
            // 
            // textBox_SavedGames
            // 
            this.textBox_SavedGames.Location = new System.Drawing.Point(147, 114);
            this.textBox_SavedGames.Name = "textBox_SavedGames";
            this.textBox_SavedGames.Size = new System.Drawing.Size(264, 20);
            this.textBox_SavedGames.TabIndex = 5;
            this.textBox_SavedGames.TextChanged += new System.EventHandler(this.textBox3_TextChanged);
            // 
            // m_buttonNext
            // 
            this.m_buttonNext.Location = new System.Drawing.Point(681, 313);
            this.m_buttonNext.Name = "m_buttonNext";
            this.m_buttonNext.Size = new System.Drawing.Size(75, 23);
            this.m_buttonNext.TabIndex = 7;
            this.m_buttonNext.Text = "Install";
            this.m_buttonNext.UseVisualStyleBackColor = true;
            this.m_buttonNext.Visible = false;
            this.m_buttonNext.Click += new System.EventHandler(this.m_buttonInstall_Click);
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(39, 313);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 8;
            this.button1.Text = "Exit";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(691, 350);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(38, 13);
            this.label4.TabIndex = 10;
            this.label4.Text = "V 1.04";
            this.label4.Click += new System.EventHandler(this.labelInc01_Click);
            // 
            // toolTip1
            // 
            this.toolTip1.Popup += new System.Windows.Forms.PopupEventHandler(this.toolTip1_Popup);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(649, 192);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(61, 13);
            this.label1.TabIndex = 13;
            this.label1.Text = "Modified by";
            this.label1.Click += new System.EventHandler(this.label1_Click_1);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(638, 205);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(94, 13);
            this.label3.TabIndex = 14;
            this.label3.Text = "CEF and Miguel21";
            this.label3.Click += new System.EventHandler(this.label3_Click_1);
            // 
            // pictureBox3
            // 
            this.pictureBox3.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox3.Image")));
            this.pictureBox3.Location = new System.Drawing.Point(578, 182);
            this.pictureBox3.Name = "pictureBox3";
            this.pictureBox3.Size = new System.Drawing.Size(54, 51);
            this.pictureBox3.TabIndex = 12;
            this.pictureBox3.TabStop = false;
            this.pictureBox3.Click += new System.EventHandler(this.pictureBox3_Click);
            // 
            // pictureBox2
            // 
            this.pictureBox2.Image = global::Test.Properties.Resources.SPA3_tissue50b;
            this.pictureBox2.Location = new System.Drawing.Point(738, 177);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(50, 51);
            this.pictureBox2.TabIndex = 11;
            this.pictureBox2.TabStop = false;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pictureBox1.Image = global::Test.Properties.Resources.DCE_logo;
            this.pictureBox1.Location = new System.Drawing.Point(565, 21);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(232, 145);
            this.pictureBox1.TabIndex = 9;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.Click += new System.EventHandler(this.pictureBox1_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(25, 21);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(462, 13);
            this.label2.TabIndex = 15;
            this.label2.Text = "Only compatible with DCE campaigns in ZIP format with ScriptsMod superior or equa" +
    "l to 20.38.01";
            // 
            // checkBoxSM
            // 
            this.checkBoxSM.AutoSize = true;
            this.checkBoxSM.Location = new System.Drawing.Point(39, 253);
            this.checkBoxSM.Name = "checkBoxSM";
            this.checkBoxSM.Size = new System.Drawing.Size(285, 17);
            this.checkBoxSM.TabIndex = 16;
            this.checkBoxSM.Text = "do not overwrite the \"scriptsMod\" (default: unchecked)";
            this.checkBoxSM.UseVisualStyleBackColor = true;
            this.checkBoxSM.CheckedChanged += new System.EventHandler(this.checkBoxSM_CheckedChanged);
            // 
            // checkBoxActiveFolder
            // 
            this.checkBoxActiveFolder.AutoSize = true;
            this.checkBoxActiveFolder.Location = new System.Drawing.Point(39, 276);
            this.checkBoxActiveFolder.Name = "checkBoxActiveFolder";
            this.checkBoxActiveFolder.Size = new System.Drawing.Size(326, 17);
            this.checkBoxActiveFolder.TabIndex = 17;
            this.checkBoxActiveFolder.Text = "do not erase the progress of the campaign (default: unchecked)";
            this.checkBoxActiveFolder.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.ClientSize = new System.Drawing.Size(800, 372);
            this.Controls.Add(this.checkBoxActiveFolder);
            this.Controls.Add(this.checkBoxSM);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.pictureBox3);
            this.Controls.Add(this.pictureBox2);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.m_buttonNext);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.pictureBox1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.Text = "Dynamique Campagne Engine (Installer)";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox3)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button m_ButtonDcsPath;
        private System.Windows.Forms.Button m_ButtonSavedGames;
        private System.Windows.Forms.TextBox textBox_DCS;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox textBox_SavedGames;
        private System.Windows.Forms.Label Label_SavedGames;
        private System.Windows.Forms.Label Label_DCS;
        private System.Windows.Forms.Button m_buttonNext;
        private System.Windows.Forms.Label Label_Campaign;
        private System.Windows.Forms.TextBox textBox_Campaign;
        private System.Windows.Forms.Button m_Button_Campaign;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label Label_OvGME;
        private System.Windows.Forms.TextBox textBox_OvGME;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.LinkLabel linkLabelOvGME;
        private System.Windows.Forms.LinkLabel linkLabelCampaign;
        private System.Windows.Forms.PictureBox pictureBox2;
        private System.Windows.Forms.PictureBox pictureBox3;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox checkBoxSM;
        private System.Windows.Forms.CheckBox checkBoxActiveFolder;
    }
}

