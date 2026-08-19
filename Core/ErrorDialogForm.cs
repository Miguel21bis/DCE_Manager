using System;
using System.Drawing;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Fenêtre d'erreur générique, utilisée par le gestionnaire d'exceptions global
    // (voir Program.cs). Remplace la boîte de dialogue .NET par défaut, qui montre une
    // pile d'appel illisible et ne permet pas de copier le texte proprement.
    // Texte 100% anglais : c'est une fenêtre destinée à l'utilisateur final.
    public class ErrorDialogForm : Form
    {
        private ErrorDialogForm(string message, string details)
        {
            Text = "DCE_Manager - Unexpected error";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 640;
            Height = 420;
            MinimumSize = new Size(420, 260);

            var iconBox = new PictureBox
            {
                Image = SystemIcons.Error.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(15, 15)
            };

            var lblMessage = new Label
            {
                Text = message,
                AutoSize = false,
                Location = new Point(60, 15),
                Size = new Size(550, 45),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };

            var txtDetails = new TextBox
            {
                Text = details,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                Location = new Point(15, 65),
                Size = new Size(600, 260),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            var btnCopy = new Button
            {
                Text = "Copy details",
                Location = new Point(15, 335),
                Size = new Size(110, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnCopy.Click += (s, e) =>
            {
                try { Clipboard.SetText(details); }
                catch { /* presse-papier parfois verrouillé par une autre appli, tant pis */ }
            };

            var btnClose = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Location = new Point(525, 335),
                Size = new Size(90, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            Controls.Add(iconBox);
            Controls.Add(lblMessage);
            Controls.Add(txtDetails);
            Controls.Add(btnCopy);
            Controls.Add(btnClose);

            AcceptButton = btnClose;
            CancelButton = btnClose;

            // Le texte doit être copiable (sélectionnable), mais SANS présélection à
            // l'ouverture : on ne met jamais le focus sur txtDetails, ni SelectAll().
            ActiveControl = btnClose;
        }

        public static void Show(string message, Exception ex)
        {
            string details = ex?.ToString() ?? "(no exception details)";

            using (var frm = new ErrorDialogForm(message, details))
            {
                frm.ShowDialog();
            }
        }
    }
}