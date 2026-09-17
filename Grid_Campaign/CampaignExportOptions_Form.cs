using System;
using System.Drawing;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Petite fenêtre de choix affichée avant un Export : quoi inclure en plus du cœur de
    // la campagne (Init/, .miz, .cmp, .png - toujours inclus). Écrite entièrement en code :
    // assez simple pour ne pas avoir besoin d'un Designer.cs séparé.
    internal class CampaignExportOptions_Form : Form
    {
        public bool IncludeLiveries => checkBoxLiveries.Checked;
        public bool IncludeDoc => checkBoxDoc.Checked;

        private readonly CheckBox checkBoxLiveries;
        private readonly CheckBox checkBoxDoc;

        public CampaignExportOptions_Form(string campaignName)
        {
            Text = "Export options — " + campaignName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(400, 160);

            var label = new Label
            {
                Text = "What should be included in the exported package?",
                AutoSize = true,
                Location = new Point(12, 12)
            };

            checkBoxLiveries = new CheckBox
            {
                Text = "Liveries (skins) used by this campaign, if found on this PC",
                AutoSize = true,
                Checked = true,
                Location = new Point(15, 45)
            };

            checkBoxDoc = new CheckBox
            {
                Text = "Doc folder",
                AutoSize = true,
                Checked = true,
                Location = new Point(15, 75)
            };

            var buttonExport = new Button
            {
                Text = "Export",
                DialogResult = DialogResult.OK,
                Location = new Point(210, 120),
                Width = 80
            };

            var buttonCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(300, 120),
                Width = 80
            };

            Controls.Add(label);
            Controls.Add(checkBoxLiveries);
            Controls.Add(checkBoxDoc);
            Controls.Add(buttonExport);
            Controls.Add(buttonCancel);

            AcceptButton = buttonExport;
            CancelButton = buttonCancel;
        }
    }
}