using System;
using System.Drawing;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Compte-rendu d'export affiche dans une zone de texte selectionnable/copiable, plutot
    // qu'un MessageBox : les chemins complets sont longs et l'utilisateur doit pouvoir les
    // recuperer pour aller verifier ses dossiers de livrees.
    internal class CampaignExportReport_Form : Form
    {
        public CampaignExportReport_Form(string title, string reportText)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ClientSize = new Size(720, 420);
            MinimumSize = new Size(500, 300);

            // Reprend l'icone de l'executable : evite d'avoir a embarquer une ressource
            // dediee, et la fenetre ne montre plus l'icone WinForms par defaut.
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception ex)
            {
                // Purement cosmetique : jamais bloquant si l'icone n'est pas recuperable.
                Utils.FormUtils.LogRegister("CampaignExportReport_Form | icone non recuperee : " + ex.Message);
            }

            var textBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                Text = reportText,
                Dock = DockStyle.Fill
            };

            // FlowLayoutPanel en flux droite-a-gauche : les boutons se placent tout seuls a
            // droite, sans coordonnees en dur (celles-ci se decalaient au docking du panel).
            var panelBottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 8, 0)
            };

            var buttonClose = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Width = 90
            };

            var buttonCopy = new Button
            {
                Text = "Copy all",
                Width = 90,
                Margin = new Padding(8, 0, 0, 0)
            };
            buttonCopy.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(textBox.Text))
                    Clipboard.SetText(textBox.Text);
            };

            // Ordre d'ajout = ordre d'affichage de droite a gauche : Close le plus a droite.
            panelBottom.Controls.Add(buttonClose);
            panelBottom.Controls.Add(buttonCopy);

            // Le control Dock.Fill doit etre ajoute AVANT les bords dockes, pour que ceux-ci
            // reservent leur place en premier et que le Fill occupe le reste.
            Controls.Add(textBox);
            Controls.Add(panelBottom);

            AcceptButton = buttonClose;
            CancelButton = buttonClose;   // Echap ferme aussi la fenetre

            // Le TextBox selectionne tout son contenu en prenant le focus (fond bleu) : on
            // remet le curseur au debut et on donne le focus initial au bouton Close.
            Shown += (s, e) =>
            {
                textBox.SelectionStart = 0;
                textBox.SelectionLength = 0;
                buttonClose.Focus();
            };
        }
    }
}