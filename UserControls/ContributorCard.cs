using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DCE_Manager.UserControls
{
    // Représente une ligne du fichier contributors.txt
    public class ContributorEntry
    {
        public string IconFileName;
        public string Pseudo;
        public string Role;

        // Lit le fichier texte et retourne la liste des contributeurs.
        // Format d'une ligne : icone.png|PSEUDO|Role
        // Les lignes vides ou commençant par # sont ignorées.
        public static List<ContributorEntry> LoadFromFile(string filePath)
        {
            var list = new List<ContributorEntry>();

            if (!File.Exists(filePath))
                return list;

            foreach (string rawLine in File.ReadAllLines(filePath))
            {
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                string[] parts = line.Split('|');
                if (parts.Length < 3)
                    continue; // ligne mal formée, on l'ignore plutôt que de planter

                list.Add(new ContributorEntry
                {
                    IconFileName = parts[0].Trim(),
                    Pseudo = parts[1].Trim(),
                    Role = parts[2].Trim()
                });
            }

            return list;
        }
    }

    // Carte visuelle d'un contributeur (icône + pseudo + rôle), construite entièrement en code
    // (pas besoin de fichier .Designer.cs séparé).
    public class ContributorCard : UserControl
    {
        private PictureBox pictureBoxIcon;
        private Label labelPseudo;
        private Label labelRole;

        public ContributorCard(ContributorEntry entry, string iconsFolder)
        {
            this.Width = 165;
            this.Height = 190;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.BackColor = Color.White;
            this.Margin = new Padding(8);

            pictureBoxIcon = new PictureBox();
            pictureBoxIcon.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBoxIcon.Width = 90;
            pictureBoxIcon.Height = 90;
            pictureBoxIcon.Left = (this.Width - pictureBoxIcon.Width) / 2;
            pictureBoxIcon.Top = 15;

            string iconPath = Path.Combine(iconsFolder, entry.IconFileName);
            if (File.Exists(iconPath))
            {
                try { pictureBoxIcon.Image = Image.FromFile(iconPath); }
                catch { /* image illisible ou corrompue : on laisse vide */ }
            }

            labelPseudo = new Label();
            labelPseudo.Text = "\u2605 " + entry.Pseudo; // étoile + pseudo
            labelPseudo.Font = new Font(this.Font, FontStyle.Bold);
            labelPseudo.ForeColor = Color.RoyalBlue;
            labelPseudo.TextAlign = ContentAlignment.MiddleCenter;
            labelPseudo.Width = this.Width - 10;
            labelPseudo.Left = 5;
            labelPseudo.Top = pictureBoxIcon.Bottom + 10;

            labelRole = new Label();
            labelRole.Text = entry.Role;
            labelRole.ForeColor = Color.DimGray;
            labelRole.TextAlign = ContentAlignment.MiddleCenter;
            labelRole.Width = this.Width - 10;
            labelRole.Left = 5;
            labelRole.Top = labelPseudo.Bottom + 5;
            labelRole.Height = 40;

            this.Controls.Add(pictureBoxIcon);
            this.Controls.Add(labelPseudo);
            this.Controls.Add(labelRole);
        }
    }
}
