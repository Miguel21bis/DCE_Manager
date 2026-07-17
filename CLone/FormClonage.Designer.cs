namespace DCE_Manager
{
    partial class FormClonage
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        public void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormClonage));
            this.button_clone = new System.Windows.Forms.Button();
            this.CampaignName = new System.Windows.Forms.TextBox();
            this.comboPlaneChoice = new System.Windows.Forms.ComboBox();
            this.SquadName = new System.Windows.Forms.TextBox();
            this.BaseName = new System.Windows.Forms.TextBox();
            this.planeFIX = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // button_clone
            // 
            this.button_clone.Location = new System.Drawing.Point(609, 236);
            this.button_clone.Name = "button_clone";
            this.button_clone.Size = new System.Drawing.Size(75, 23);
            this.button_clone.TabIndex = 0;
            this.button_clone.Tag = "NewdNameCamp";
            this.button_clone.Text = "Clone";
            this.button_clone.UseVisualStyleBackColor = true;
            this.button_clone.Click += new System.EventHandler(this.button_clone_Click);
            // 
            // CampaignName
            // 
            this.CampaignName.Location = new System.Drawing.Point(12, 91);
            this.CampaignName.Name = "CampaignName";
            this.CampaignName.Size = new System.Drawing.Size(272, 20);
            this.CampaignName.TabIndex = 1;
            this.CampaignName.TextChanged += new System.EventHandler(this.CampaignName_TextChanged);
            // 
            // comboPlaneChoice
            // 
            this.comboPlaneChoice.FormattingEnabled = true;
            this.comboPlaneChoice.Location = new System.Drawing.Point(306, 21);
            this.comboPlaneChoice.Name = "comboPlaneChoice";
            this.comboPlaneChoice.Size = new System.Drawing.Size(378, 21);
            this.comboPlaneChoice.TabIndex = 3;
            this.comboPlaneChoice.SelectedIndexChanged += new System.EventHandler(this.comboPlaneChoice_SelectedIndexChanged);
            // 
            // SquadName
            // 
            this.SquadName.Location = new System.Drawing.Point(393, 91);
            this.SquadName.Name = "SquadName";
            this.SquadName.ReadOnly = true;
            this.SquadName.Size = new System.Drawing.Size(144, 20);
            this.SquadName.TabIndex = 4;
            // 
            // BaseName
            // 
            this.BaseName.Location = new System.Drawing.Point(543, 91);
            this.BaseName.Name = "BaseName";
            this.BaseName.ReadOnly = true;
            this.BaseName.Size = new System.Drawing.Size(141, 20);
            this.BaseName.TabIndex = 5;
            // 
            // planeFIX
            // 
            this.planeFIX.Location = new System.Drawing.Point(306, 91);
            this.planeFIX.Name = "planeFIX";
            this.planeFIX.ReadOnly = true;
            this.planeFIX.Size = new System.Drawing.Size(81, 20);
            this.planeFIX.TabIndex = 8;
            this.planeFIX.TextChanged += new System.EventHandler(this.planeFIX_TextChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(169, 24);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(115, 13);
            this.label2.TabIndex = 9;
            this.label2.Text = "1/ Choose an airplane:";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(9, 66);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(173, 13);
            this.label3.TabIndex = 10;
            this.label3.Text = "2/ Edit the name of your campaign:";
            // 
            // FormClonage
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(692, 271);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.planeFIX);
            this.Controls.Add(this.BaseName);
            this.Controls.Add(this.SquadName);
            this.Controls.Add(this.comboPlaneChoice);
            this.Controls.Add(this.CampaignName);
            this.Controls.Add(this.button_clone);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FormClonage";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Clone";
            this.Load += new System.EventHandler(this.Form3_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button button_clone;
        private System.Windows.Forms.TextBox CampaignName;
        private System.Windows.Forms.ComboBox comboPlaneChoice;
        private System.Windows.Forms.TextBox SquadName;
        private System.Windows.Forms.TextBox BaseName;
        private System.Windows.Forms.TextBox planeFIX;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
    }
}