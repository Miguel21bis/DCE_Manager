namespace DCE_Manager
{
    partial class FormSquadEdit
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
        private void InitializeComponent()
        {
            this.textBoxName = new System.Windows.Forms.TextBox();
            this.textBoxType = new System.Windows.Forms.TextBox();
            this.textBoxBase = new System.Windows.Forms.TextBox();
            this.numericNumber = new System.Windows.Forms.NumericUpDown();
            this.numericReserve = new System.Windows.Forms.NumericUpDown();
            this.checkBoxPlayer = new System.Windows.Forms.CheckBox();
            this.buttonOK = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.label_SquadName = new System.Windows.Forms.Label();
            this.label_PlaneType = new System.Windows.Forms.Label();
            this.label_Base = new System.Windows.Forms.Label();
            this.label_Number = new System.Windows.Forms.Label();
            this.label_Reserve = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numericNumber)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericReserve)).BeginInit();
            this.SuspendLayout();
            // 
            // textBoxName
            // 
            this.textBoxName.Location = new System.Drawing.Point(529, 186);
            this.textBoxName.Name = "textBoxName";
            this.textBoxName.Size = new System.Drawing.Size(100, 22);
            this.textBoxName.TabIndex = 0;
            // 
            // textBoxType
            // 
            this.textBoxType.Location = new System.Drawing.Point(529, 241);
            this.textBoxType.Name = "textBoxType";
            this.textBoxType.Size = new System.Drawing.Size(100, 22);
            this.textBoxType.TabIndex = 1;
            // 
            // textBoxBase
            // 
            this.textBoxBase.Location = new System.Drawing.Point(529, 289);
            this.textBoxBase.Name = "textBoxBase";
            this.textBoxBase.Size = new System.Drawing.Size(100, 22);
            this.textBoxBase.TabIndex = 2;
            // 
            // numericNumber
            // 
            this.numericNumber.Location = new System.Drawing.Point(529, 356);
            this.numericNumber.Name = "numericNumber";
            this.numericNumber.Size = new System.Drawing.Size(120, 22);
            this.numericNumber.TabIndex = 3;
            // 
            // numericReserve
            // 
            this.numericReserve.Location = new System.Drawing.Point(529, 410);
            this.numericReserve.Name = "numericReserve";
            this.numericReserve.Size = new System.Drawing.Size(120, 22);
            this.numericReserve.TabIndex = 4;
            // 
            // checkBoxPlayer
            // 
            this.checkBoxPlayer.AutoSize = true;
            this.checkBoxPlayer.Location = new System.Drawing.Point(529, 457);
            this.checkBoxPlayer.Name = "checkBoxPlayer";
            this.checkBoxPlayer.Size = new System.Drawing.Size(68, 20);
            this.checkBoxPlayer.TabIndex = 5;
            this.checkBoxPlayer.Text = "Player";
            this.checkBoxPlayer.UseVisualStyleBackColor = true;
            // 
            // buttonOK
            // 
            this.buttonOK.Location = new System.Drawing.Point(949, 572);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(75, 23);
            this.buttonOK.TabIndex = 6;
            this.buttonOK.Text = "OK";
            this.buttonOK.UseVisualStyleBackColor = true;
            // 
            // buttonCancel
            // 
            this.buttonCancel.Location = new System.Drawing.Point(779, 572);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(75, 23);
            this.buttonCancel.TabIndex = 7;
            this.buttonCancel.Text = "Cancel";
            this.buttonCancel.UseVisualStyleBackColor = true;
            // 
            // label_SquadName
            // 
            this.label_SquadName.AutoSize = true;
            this.label_SquadName.Location = new System.Drawing.Point(424, 186);
            this.label_SquadName.Name = "label_SquadName";
            this.label_SquadName.Size = new System.Drawing.Size(87, 16);
            this.label_SquadName.TabIndex = 8;
            this.label_SquadName.Text = "Squad Name";
            // 
            // label_PlaneType
            // 
            this.label_PlaneType.AutoSize = true;
            this.label_PlaneType.Location = new System.Drawing.Point(424, 247);
            this.label_PlaneType.Name = "label_PlaneType";
            this.label_PlaneType.Size = new System.Drawing.Size(79, 16);
            this.label_PlaneType.TabIndex = 9;
            this.label_PlaneType.Text = "Plane TYpe";
            // 
            // label_Base
            // 
            this.label_Base.AutoSize = true;
            this.label_Base.Location = new System.Drawing.Point(424, 295);
            this.label_Base.Name = "label_Base";
            this.label_Base.Size = new System.Drawing.Size(39, 16);
            this.label_Base.TabIndex = 10;
            this.label_Base.Text = "Base";
            // 
            // label_Number
            // 
            this.label_Number.AutoSize = true;
            this.label_Number.Location = new System.Drawing.Point(424, 358);
            this.label_Number.Name = "label_Number";
            this.label_Number.Size = new System.Drawing.Size(55, 16);
            this.label_Number.TabIndex = 11;
            this.label_Number.Text = "Number";
            // 
            // label_Reserve
            // 
            this.label_Reserve.AutoSize = true;
            this.label_Reserve.Location = new System.Drawing.Point(424, 416);
            this.label_Reserve.Name = "label_Reserve";
            this.label_Reserve.Size = new System.Drawing.Size(59, 16);
            this.label_Reserve.TabIndex = 12;
            this.label_Reserve.Text = "Reserve";
            // 
            // FormSquadEdit
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1389, 673);
            this.Controls.Add(this.label_Reserve);
            this.Controls.Add(this.label_Number);
            this.Controls.Add(this.label_Base);
            this.Controls.Add(this.label_PlaneType);
            this.Controls.Add(this.label_SquadName);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOK);
            this.Controls.Add(this.checkBoxPlayer);
            this.Controls.Add(this.numericReserve);
            this.Controls.Add(this.numericNumber);
            this.Controls.Add(this.textBoxBase);
            this.Controls.Add(this.textBoxType);
            this.Controls.Add(this.textBoxName);
            this.Name = "FormSquadEdit";
            this.Text = "Squad Edit";
            ((System.ComponentModel.ISupportInitialize)(this.numericNumber)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericReserve)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox textBoxName;
        private System.Windows.Forms.TextBox textBoxType;
        private System.Windows.Forms.TextBox textBoxBase;
        private System.Windows.Forms.NumericUpDown numericNumber;
        private System.Windows.Forms.NumericUpDown numericReserve;
        private System.Windows.Forms.CheckBox checkBoxPlayer;
        private System.Windows.Forms.Button buttonOK;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.Label label_SquadName;
        private System.Windows.Forms.Label label_PlaneType;
        private System.Windows.Forms.Label label_Base;
        private System.Windows.Forms.Label label_Number;
        private System.Windows.Forms.Label label_Reserve;
    }
}