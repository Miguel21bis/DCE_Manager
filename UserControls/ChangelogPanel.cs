using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DCE_Manager.UserControls
{
    public class ChangelogLine
    {
        public string Type; // ADD, MOD, FIX, NOTE
        public string Text;
    }

    public class ChangelogEntry
    {
        public string Version;
        public string Date;
        public List<ChangelogLine> Lines = new List<ChangelogLine>();
    }

    public static class ChangelogData
    {
        private static readonly HashSet<string> knownTypeWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "add", "added", "fix", "fixed", "mod", "modified", "changed", "change", "chg", "removed", "remove"
        };

        // Format réel du changelog DCE_Manager, écrit à la main au fil du temps :
        //
        //   V10.16.51
        //   MOD: revamp of the PATH/Campaign Installation page
        //   MOD: revamp of configuration management
        //
        //   V9.15
        //   ADD Statistics
        //   ADD: ability to add and clone squads
        //
        // - une ligne "V<numero>" toute seule démarre une nouvelle version (pas de date : normal,
        //   ce changelog n'en a jamais eu)
        // - une ligne "TYPE: texte" (ou "TYPE texte" sans ":") démarre une nouvelle puce
        // - une ligne sans TYPE reconnu est ajoutée à la puce précédente (suite du même point,
        //   souvent une ligne indentée avec une tabulation dans le fichier d'origine)
        public static List<ChangelogEntry> LoadFromFile(string filePath)
        {
            var result = new List<ChangelogEntry>();

            if (!File.Exists(filePath))
                return result;

            var regexVersion = new System.Text.RegularExpressions.Regex(@"^[Vv]\s*(\d+(?:\.\d+){0,2})\s*$");
            var regexTypeColon = new System.Text.RegularExpressions.Regex(@"^([A-Za-z]+)\s*:\s*(.*)$");
            var regexTypeNoColon = new System.Text.RegularExpressions.Regex(@"^([A-Za-z]+)\s+(.*)$");

            ChangelogEntry current = null;
            ChangelogLine currentBullet = null;

            foreach (string rawLine in File.ReadAllLines(filePath))
            {
                string trimmed = rawLine.Trim();

                if (trimmed.Length == 0)
                    continue; // les lignes vides séparent juste visuellement, on ignore

                var mVersion = regexVersion.Match(trimmed);
                if (mVersion.Success)
                {
                    current = new ChangelogEntry { Version = "v" + mVersion.Groups[1].Value };
                    result.Add(current);
                    currentBullet = null;
                    continue;
                }

                if (current == null)
                    continue; // texte avant la toute première version : on ignore

                string typeWord = null;
                string text = null;

                var mColon = regexTypeColon.Match(trimmed);
                if (mColon.Success && knownTypeWords.Contains(mColon.Groups[1].Value))
                {
                    typeWord = mColon.Groups[1].Value;
                    text = mColon.Groups[2].Value.Trim();
                }
                else
                {
                    var mNoColon = regexTypeNoColon.Match(trimmed);
                    if (mNoColon.Success && knownTypeWords.Contains(mNoColon.Groups[1].Value))
                    {
                        typeWord = mNoColon.Groups[1].Value;
                        text = mNoColon.Groups[2].Value.Trim();
                    }
                }

                if (typeWord != null)
                {
                    currentBullet = new ChangelogLine { Type = typeWord.ToUpperInvariant(), Text = text };
                    current.Lines.Add(currentBullet);
                }
                else if (currentBullet != null)
                {
                    // suite du point précédent (ligne indentée dans le fichier d'origine)
                    currentBullet.Text = currentBullet.Text + " " + trimmed;
                }
                else
                {
                    currentBullet = new ChangelogLine { Type = "NOTE", Text = trimmed };
                    current.Lines.Add(currentBullet);
                }
            }

            return result;
        }

        // Le fichier UTIL_Changelog.lua a été écrit à la main pendant des années et mélange
        // 3 façons différentes de noter une version dans le même fichier :
        //
        //   1) format récent :   ##  Version 22.108.627
        //                            ###  Fixed
        //                                texte...
        //
        //   2) format intermédiaire : ==:20.92.572:==
        //                              572 add   [tag]   texte...
        //
        //   3) format ancien :   -- M42 -Added- texte...
        //                        28 -fixed- texte...
        //
        // On détecte les 3 types de "début de version" et on range tout le texte qui suit
        // (jusqu'à la version suivante) dans cette entrée. Les catégories (Fixed/Added/Changed/...)
        // sont gardées telles quelles, pas limitées à ADD/MOD/FIX.
        public static List<ChangelogEntry> LoadFromLuaChangelogFile(string filePath, string versionLabel)
        {
            var result = new List<ChangelogEntry>();

            if (!File.Exists(filePath))
                return result;

            var regexVersionRecent = new System.Text.RegularExpressions.Regex(@"^##\s*Version\s+(.+)$");
            var regexVersionMid = new System.Text.RegularExpressions.Regex(@"^==:(.+?):==$");
            var regexVersionOld = new System.Text.RegularExpressions.Regex(@"^--\s*M(\d+)\b(.*)$");
            var regexCategory = new System.Text.RegularExpressions.Regex(@"^###\s*(.+)$");
            var regexNumberedLine = new System.Text.RegularExpressions.Regex(@"^\d+\s*-?\s*([A-Za-z]+)\s*-?\s*(.*)$");
            var knownCategoryWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "add", "added", "fix", "fixed", "mod", "modified", "modfied",
                "changed", "change", "chg", "removed", "remove", "wip", "loadout"
            };

            ChangelogEntry current = null;
            string currentCategory = "NOTE";

            foreach (string rawLine in File.ReadAllLines(filePath))
            {
                string line = rawLine.TrimEnd();
                string trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed == "--[[" || trimmed == "]]--")
                    continue;

                var mRecent = regexVersionRecent.Match(trimmed);
                var mMid = regexVersionMid.Match(trimmed);
                var mOld = regexVersionOld.Match(trimmed);

                if (mRecent.Success)
                {
                    current = new ChangelogEntry { Version = "v" + mRecent.Groups[1].Value.Trim() };
                    result.Add(current);
                    currentCategory = "NOTE";
                    continue;
                }
                if (mMid.Success)
                {
                    current = new ChangelogEntry { Version = mMid.Groups[1].Value.Trim() };
                    result.Add(current);
                    currentCategory = "NOTE";
                    continue;
                }
                if (mOld.Success)
                {
                    current = new ChangelogEntry { Version = "M" + mOld.Groups[1].Value.Trim() };
                    result.Add(current);
                    currentCategory = "NOTE";

                    string rest = mOld.Groups[2].Value.Trim(' ', '-');
                    if (rest.Length > 0)
                        current.Lines.Add(new ChangelogLine { Type = currentCategory, Text = rest });
                    continue;
                }

                var mCategory = regexCategory.Match(trimmed);
                if (mCategory.Success && current != null)
                {
                    currentCategory = mCategory.Groups[1].Value.Trim();
                    continue;
                }

                if (current == null)
                    continue; // texte d'intro avant la toute première version : on l'ignore

                // enlève un éventuel "--" de commentaire Lua en tête de ligne
                string content = trimmed;
                if (content.StartsWith("--"))
                    content = content.Substring(2).Trim();

                if (content.Length == 0)
                    continue;

                var mNum = regexNumberedLine.Match(content);
                if (mNum.Success && knownCategoryWords.Contains(mNum.Groups[1].Value))
                {
                    current.Lines.Add(new ChangelogLine
                    {
                        Type = mNum.Groups[1].Value,
                        Text = mNum.Groups[2].Value.Trim()
                    });
                }
                else
                {
                    current.Lines.Add(new ChangelogLine { Type = currentCategory, Text = content });
                }
            }

            return result;
        }
    }

    // Panneau réutilisable : liste des versions à gauche, détail coloré (ADD/MOD/FIX/NOTE) à droite.
    // Une seule classe sert pour les deux changelogs (DCE_Manager et ScriptsMod), seule la couleur
    // d'accent (AccentColor) et le fichier chargé changent.
    public class ChangelogPanel : UserControl
    {
        // Nombre de versions affichées par défaut dans la liste ; "View all versions" ouvre
        // une fenêtre à part avec le texte brut complet (zéro reformattage, donc zéro perte).
        private const int DefaultVisibleCount = 15;

        private ListBox listBoxVersions;
        private RichTextBox richTextBoxDetail;
        private LinkLabel linkLabelViewAll;
        private List<ChangelogEntry> entries = new List<ChangelogEntry>();
        private Color accentColor = Color.RoyalBlue;

        // Chemin du fichier chargé (texte brut ré-affiché tel quel par "View all versions")
        private string sourceFilePath;

        public Color AccentColor
        {
            get { return accentColor; }
            set
            {
                accentColor = value;
                if (linkLabelViewAll != null)
                    linkLabelViewAll.LinkColor = value;
            }
        }

        // Version de la toute première entrée (la plus récente, le fichier étant trié
        // du plus récent au plus ancien) : utile pour afficher un badge "v10.17.52" en en-tête.
        public string LatestVersion
        {
            get { return entries.Count > 0 ? entries[0].Version : ""; }
        }

        public ChangelogPanel()
        {
            var panelVersionsSide = new Panel();
            panelVersionsSide.Dock = DockStyle.Left;
            panelVersionsSide.Width = 140;

            listBoxVersions = new ListBox();
            listBoxVersions.Dock = DockStyle.Fill;
            listBoxVersions.SelectedIndexChanged += ListBoxVersions_SelectedIndexChanged;

            linkLabelViewAll = new LinkLabel();
            linkLabelViewAll.Dock = DockStyle.Bottom;
            linkLabelViewAll.Height = 26;
            linkLabelViewAll.TextAlign = ContentAlignment.MiddleLeft;
            linkLabelViewAll.LinkColor = accentColor;
            linkLabelViewAll.Click += (s, e) => { ShowFullTextWindow(); };

            // IMPORTANT : Fill (listBoxVersions) doit être ajouté AVANT Bottom (linkLabelViewAll)
            panelVersionsSide.Controls.Add(listBoxVersions);
            panelVersionsSide.Controls.Add(linkLabelViewAll);

            richTextBoxDetail = new RichTextBox();
            richTextBoxDetail.Dock = DockStyle.Fill;
            richTextBoxDetail.ReadOnly = true;
            richTextBoxDetail.BorderStyle = BorderStyle.None;

            // IMPORTANT : Fill (richTextBoxDetail) doit être ajouté AVANT Left (panelVersionsSide)
            this.Controls.Add(richTextBoxDetail);
            this.Controls.Add(panelVersionsSide);
        }

        // Ouvre une petite fenêtre à part qui affiche le fichier source tel quel (texte brut,
        // aucun reformattage) : c'est la version "sans perte" de l'historique complet.
        private void ShowFullTextWindow()
        {
            string content;
            try
            {
                content = File.Exists(sourceFilePath)
                    ? File.ReadAllText(sourceFilePath)
                    : "(fichier introuvable : " + sourceFilePath + ")";
            }
            catch (System.Exception ex)
            {
                content = "(erreur de lecture : " + ex.Message + ")";
            }

            var viewerForm = new Form();
            viewerForm.Text = "Historique complet";
            viewerForm.Width = 700;
            viewerForm.Height = 600;
            viewerForm.StartPosition = FormStartPosition.CenterParent;

            var textBox = new TextBox();
            textBox.Multiline = true;
            textBox.ReadOnly = true;
            textBox.ScrollBars = ScrollBars.Vertical;
            textBox.Dock = DockStyle.Fill;
            textBox.Font = new Font("Consolas", 9f);
            textBox.Text = content.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            textBox.SelectionStart = 0;

            viewerForm.Controls.Add(textBox);
            viewerForm.ShowDialog(this.FindForm());
        }

        // Charge (ou recharge) les entrées depuis un fichier texte
        public void LoadEntries(string filePath)
        {
            sourceFilePath = filePath;
            entries = ChangelogData.LoadFromFile(filePath);
            RefreshListBox();
        }

        // Variante pour le fichier UTIL_Changelog.lua (format brut, pas notre .txt maison)
        public void LoadEntriesFromLuaFile(string filePath, string versionLabel)
        {
            sourceFilePath = filePath;
            entries = ChangelogData.LoadFromLuaChangelogFile(filePath, versionLabel);
            RefreshListBox();
        }

        // Remplit la ListBox avec les DefaultVisibleCount versions les plus récentes (le fichier
        // est trié du plus récent au plus ancien). "View all versions" ouvre le fichier source
        // en entier dans une fenêtre à part plutôt que de rallonger cette liste.
        private void RefreshListBox()
        {
            listBoxVersions.Items.Clear();

            int countToShow = System.Math.Min(DefaultVisibleCount, entries.Count);
            for (int i = 0; i < countToShow; i++)
            {
                var entry = entries[i];
                listBoxVersions.Items.Add(entry.Version + (string.IsNullOrEmpty(entry.Date) ? "" : "   " + entry.Date));
            }

            linkLabelViewAll.Visible = entries.Count > 0;
            linkLabelViewAll.Text = "\u2261  View all versions (" + entries.Count + ")";

            if (listBoxVersions.Items.Count > 0)
                listBoxVersions.SelectedIndex = 0;
            else
                richTextBoxDetail.Clear();
        }

        private void ListBoxVersions_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = listBoxVersions.SelectedIndex;
            if (index < 0 || index >= entries.Count)
                return;

            ShowEntry(entries[index]);
        }

        private static readonly Dictionary<string, Color> categoryColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "ADD", Color.Green },
            { "ADDED", Color.Green },
            { "FIX", Color.Red },
            { "FIXED", Color.Red },
            { "MOD", Color.DarkOrange },
            { "MODIFIED", Color.DarkOrange },
            { "CHANGED", Color.DarkOrange },
            { "REMOVED", Color.Gray },
            { "LOADOUT", Color.Teal },
            { "WIP", Color.Purple },
            { "NOTE", Color.Black },
        };

        private static Color GetCategoryColor(string category)
        {
            Color color;
            if (categoryColors.TryGetValue(category, out color))
                return color;
            return Color.Black; // catégorie inconnue : pas de couleur particulière
        }

        private void ShowEntry(ChangelogEntry entry)
        {
            richTextBoxDetail.Clear();

            AppendLine(entry.Version + (string.IsNullOrEmpty(entry.Date) ? "" : "  (" + entry.Date + ")"), AccentColor, true);
            richTextBoxDetail.AppendText("\n");

            // Affiche chaque catégorie réellement présente dans l'entrée, dans l'ordre où
            // elle apparaît (au lieu d'une liste figée ADD/MOD/FIX) : le fichier ScriptsMod
            // utilise des catégories variées (Fixed, Added, Changed, Loadout, Removed, WIP...).
            var categoriesSeen = new List<string>();
            foreach (var line in entry.Lines)
            {
                if (!categoriesSeen.Contains(line.Type))
                    categoriesSeen.Add(line.Type);
            }

            foreach (var category in categoriesSeen)
                AppendGroup(entry, category, GetCategoryColor(category));
        }

        private void AppendGroup(ChangelogEntry entry, string type, Color color)
        {
            bool headerWritten = false;

            foreach (var line in entry.Lines)
            {
                if (line.Type != type)
                    continue;

                if (!headerWritten)
                {
                    AppendLine(type, color, true);
                    headerWritten = true;
                }

                if (type.Equals("NOTE", StringComparison.OrdinalIgnoreCase))
                    richTextBoxDetail.AppendText(line.Text + "\n");
                else
                    richTextBoxDetail.AppendText("  \u2022 " + line.Text + "\n");
            }

            if (headerWritten)
                richTextBoxDetail.AppendText("\n");
        }

        private void AppendLine(string text, Color color, bool bold)
        {
            richTextBoxDetail.SelectionStart = richTextBoxDetail.TextLength;
            richTextBoxDetail.SelectionLength = 0;
            richTextBoxDetail.SelectionColor = color;
            richTextBoxDetail.SelectionFont = new Font(richTextBoxDetail.Font, bold ? FontStyle.Bold : FontStyle.Regular);
            richTextBoxDetail.AppendText(text + "\n");
        }
    }
}
