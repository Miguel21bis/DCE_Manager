using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DCE_Manager.Utils;
using DCE_Manager.Parameters;

namespace DCE_Manager.UserControls
{
    // Regroupe tout l'onglet "About" : bloc contributeurs en haut, les 2 changelogs en bas.
    //
    // - contributors.txt et changelog_dcemanager.txt sont embarqués dans le projet
    //   (Data\About\, copiés dans le dossier de sortie) : ils décrivent DCE_Manager lui-même.
    // - le changelog ScriptsMod, lui, est lu directement depuis l'installation DCS du joueur
    //   (Mods\tech\DCE\ScriptsMod.NG\UTIL_Changelog.lua).
    //
    // VERSION DE CE FICHIER : v1.0
    // (cause du bug d'affichage trouvee : dans CreateSection, "header" (Dock=Top) etait ajoute
    // AVANT "contentHost" (Dock=Fill) -> header ne recevait jamais sa vraie largeur. Regle a
    // retenir : toujours ajouter le Dock=Fill AVANT les Dock=Top/Bottom/Left/Right du meme parent.)
    public class ucAbout : UserControl
    {
        private const string FileVersion = "ucAbout.cs v1.0";

        private FlowLayoutPanel flowLayoutPanelContributors;
        private ChangelogPanel changelogPanelDceManager;
        private ChangelogPanel changelogPanelScriptsMod;
        private Label labelBadgeDceManager;
        private Label labelBadgeScriptsMod;

        private readonly string dataFolder;

        private class SectionParts
        {
            public Panel Outer;
            public Panel ContentHost;
            public Label BadgeLabel;
        }

        public ucAbout()
        {
            //FormUtils.LogRegister("[ucAbout] ctor START - " + FileVersion + "\r\n");

            dataFolder = Path.Combine(Application.StartupPath, "Data", "About");

            this.Dock = DockStyle.Fill;

            // --- Zone contributeurs (en haut) ---
            var contributorsSection = CreateSection(
                "icons8-conference-50.png", Color.RoyalBlue,
                "CONTRIBUTORS", "Thanks to all the contributors who make DCE possible.", false);
            contributorsSection.Outer.Dock = DockStyle.Top;
            contributorsSection.Outer.Height = 250;

            flowLayoutPanelContributors = new FlowLayoutPanel();
            flowLayoutPanelContributors.Dock = DockStyle.Fill;
            flowLayoutPanelContributors.AutoScroll = true;
            flowLayoutPanelContributors.WrapContents = true;
            flowLayoutPanelContributors.FlowDirection = FlowDirection.LeftToRight;
            contributorsSection.ContentHost.Controls.Add(flowLayoutPanelContributors);

            // --- Zone changelogs (en bas, 2 colonnes) ---
            var tableLayoutChangelogs = new TableLayoutPanel();
            tableLayoutChangelogs.Dock = DockStyle.Fill;
            tableLayoutChangelogs.ColumnCount = 2;
            tableLayoutChangelogs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tableLayoutChangelogs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var dceManagerSection = CreateSection(
                "icons8-vue-de-gauche-48.png", Color.RoyalBlue, "DCE_MANAGER CHANGELOG", null, true);
            dceManagerSection.Outer.Dock = DockStyle.Fill;
            labelBadgeDceManager = dceManagerSection.BadgeLabel;

            changelogPanelDceManager = new ChangelogPanel();
            changelogPanelDceManager.Dock = DockStyle.Fill;
            changelogPanelDceManager.AccentColor = Color.RoyalBlue;
            dceManagerSection.ContentHost.Controls.Add(changelogPanelDceManager);

            var scriptsModSection = CreateSection(
                "icons8-puzzle-64.png", Color.MediumPurple, "SCRIPTSMOD CHANGELOG", null, true);
            scriptsModSection.Outer.Dock = DockStyle.Fill;
            labelBadgeScriptsMod = scriptsModSection.BadgeLabel;

            changelogPanelScriptsMod = new ChangelogPanel();
            changelogPanelScriptsMod.Dock = DockStyle.Fill;
            changelogPanelScriptsMod.AccentColor = Color.MediumPurple;
            scriptsModSection.ContentHost.Controls.Add(changelogPanelScriptsMod);

            tableLayoutChangelogs.Controls.Add(dceManagerSection.Outer, 0, 0);
            tableLayoutChangelogs.Controls.Add(scriptsModSection.Outer, 1, 0);

            this.Controls.Add(tableLayoutChangelogs);
            this.Controls.Add(contributorsSection.Outer);

            LoadAllData();

            //FormUtils.LogRegister("[ucAbout] ctor END - " + FileVersion + "\r\n");
        }

        private SectionParts CreateSection(string iconFileName, Color accentColor, string title, string subtitle, bool withBadge)
        {
            var outer = new Panel();
            outer.BorderStyle = BorderStyle.FixedSingle;
            outer.BackColor = SystemColors.Control;

            var header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 56;

            // --- panneau de gauche (icone + titre [+ sous-titre]), Dock=Fill ---
            var leftHost = new Panel();
            leftHost.Dock = DockStyle.Fill;

            var iconBox = new PictureBox();
            iconBox.Width = 26;
            iconBox.Height = 26;
            iconBox.Left = 12;
            iconBox.Top = 14;
            iconBox.SizeMode = PictureBoxSizeMode.Zoom;
            iconBox.Image = LoadIconOrFallback(iconFileName, accentColor);
            leftHost.Controls.Add(iconBox);

            var titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Font = new Font(this.Font.FontFamily, 10f, FontStyle.Bold);
            titleLabel.AutoSize = true;
            titleLabel.Left = iconBox.Right + 8;
            titleLabel.Top = subtitle != null ? 10 : 18;
            leftHost.Controls.Add(titleLabel);

            if (subtitle != null)
            {
                var subtitleLabel = new Label();
                subtitleLabel.Text = subtitle;
                subtitleLabel.ForeColor = Color.DimGray;
                subtitleLabel.AutoSize = true;
                subtitleLabel.Left = titleLabel.Left;
                subtitleLabel.Top = titleLabel.Bottom + 2;
                leftHost.Controls.Add(subtitleLabel);
            }

            Label badgeLabel = null;
            if (withBadge)
            {
                // panneau a largeur FIXE docke a droite : se cale toujours correctement,
                // quelle que soit la largeur finale du header.
                var badgeHost = new Panel();
                badgeHost.Dock = DockStyle.Right;
                badgeHost.Width = 100;

                badgeLabel = new Label();
                badgeLabel.Text = ""; // rempli apres le chargement des donnees
                badgeLabel.ForeColor = Color.White;
                badgeLabel.BackColor = accentColor;
                badgeLabel.TextAlign = ContentAlignment.MiddleCenter;
                badgeLabel.Left = 6;
                badgeLabel.Top = 16;
                badgeLabel.Width = 88;
                badgeLabel.Height = 24;
                badgeHost.Controls.Add(badgeLabel);

                // IMPORTANT : Fill (leftHost) doit etre ajoute AVANT les autres directions de Dock
                header.Controls.Add(leftHost);
                header.Controls.Add(badgeHost);
            }
            else
            {
                header.Controls.Add(leftHost);
            }

            // IMPORTANT (la cause du bug precedent) : contentHost (Fill) doit etre ajoute AVANT
            // header (Top), sinon header ne recoit jamais sa vraie largeur.
            var contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;
            outer.Controls.Add(contentHost);
            outer.Controls.Add(header);

            return new SectionParts { Outer = outer, ContentHost = contentHost, BadgeLabel = badgeLabel };
        }

        private Image LoadIconOrFallback(string iconFileName, Color color)
        {
            string iconPath = Path.Combine(dataFolder, "Icons", iconFileName);

            if (File.Exists(iconPath))
            {
                try { return Image.FromFile(iconPath); }
                catch { /* fichier illisible : on retombe sur le rond de couleur */ }
            }

            var bmp = new Bitmap(26, 26);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, 1, 1, 24, 24);
            }
            return bmp;
        }

        public void LoadAllData()
        {
            LoadContributors();

            changelogPanelDceManager.LoadEntries(Path.Combine(dataFolder, "changelog_dcemanager.txt"));
            if (labelBadgeDceManager != null)
                labelBadgeDceManager.Text = changelogPanelDceManager.LatestVersion;

            string scriptsModChangelogPath = Path.Combine(
                ParamConf.PATH_SavedGames_DCS, "Mods", "tech", "DCE", "ScriptsMod.NG", "UTIL_Changelog.lua");

            changelogPanelScriptsMod.LoadEntriesFromLuaFile(scriptsModChangelogPath, "ScriptsMod");
            if (labelBadgeScriptsMod != null)
                labelBadgeScriptsMod.Text = changelogPanelScriptsMod.LatestVersion;

            //FormUtils.LogRegister("[ucAbout] LoadAllData OK - DCE_Manager=" + changelogPanelDceManager.LatestVersion
            //    + " ScriptsMod=" + changelogPanelScriptsMod.LatestVersion + "\r\n");
        }

        private void LoadContributors()
        {
            flowLayoutPanelContributors.Controls.Clear();

            string contributorsFile = Path.Combine(dataFolder, "contributors.txt");
            string iconsFolder = Path.Combine(dataFolder, "Icons");

            var list = ContributorEntry.LoadFromFile(contributorsFile);

            foreach (var entry in list)
            {
                var card = new ContributorCard(entry, iconsFolder);
                flowLayoutPanelContributors.Controls.Add(card);
            }
        }
    }
}
