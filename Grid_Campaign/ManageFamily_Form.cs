using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    // Popup "Campaign family" : ouverte depuis la colonne Family de la grid (icône ⋯).
    //
    // Trois parties, une seule active à la fois (les 3 RadioButton sont liés à la main,
    // car ils sont dans 3 Panels différents donc WinForms ne les groupe pas tout seul) :
    // - Independent : la campagne n'a plus de famille.
    // - Main        : la campagne est la mère, on coche ses filles.
    // - Child       : la campagne est une fille, on choisit sa mère.
    //
    // IMPORTANT : rien n'est écrit dans CampaignHierarchy avant le clic sur OK. Pendant
    // l'édition, les choix vivent en mémoire (_selectedChildren, _pickedMaster), ce qui
    // permet aussi de filtrer les listes avec la recherche sans perdre les coches.
    //
    // Le modèle reste à 2 niveaux (voir CampaignHierarchy). Si on choisit comme mère une
    // campagne qui est elle-même fille, SetChild redirige vers SA mère : le texte en bas de
    // la partie Child l'annonce avant le OK.
    public class ManageFamily_Form : Form
    {
        private const string MessageTitle = "Campaign family";
        private const int PanelLeft = 15;
        private const int PanelWidth = 480;
        private const int PartHeaderHeight = 40;

        private enum FamilyMode { Independent, Main, Child }

        // Ligne d'une liste : Name = vrai nom de dossier, Text = ce qu'on affiche
        // (nom + petite info sur sa famille actuelle).
        private class CampaignItem
        {
            public string Name;
            public string Text;
            public override string ToString() { return Text; }
        }

        private readonly string _campaignName;
        private readonly List<string> _allNames;

        // Choix en cours (pas encore appliqués)
        private readonly HashSet<string> _selectedChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _pickedMaster;

        // Vrai pendant qu'on remplit les contrôles par code, pour ne pas réagir
        // comme si c'était l'utilisateur qui cliquait.
        private bool _loading;

        private Label _statusLabel;

        private Panel _partIndependent, _partMain, _partChild;
        private Panel _stripIndependent, _stripMain, _stripChild;
        private RadioButton _radioIndependent, _radioMain, _radioChild;

        private Label _independentHint;

        private Label _mainLabel, _mainSearchLabel, _mainCountLabel;
        private TextBox _mainSearch;
        private CheckedListBox _mainList;

        private Label _childLabel, _childSearchLabel, _childResultLabel;
        private TextBox _childSearch;
        private ListBox _childList;

        private Button _okButton;
        private Button _cancelButton;

        public ManageFamily_Form(string campaignName)
        {
            _campaignName = campaignName;
            _allNames = GetAllCampaignNames();

            Text = MessageTitle + " - " + campaignName;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // Point de départ des choix = la situation actuelle
            foreach (string child in CampaignHierarchy.GetChildren(_campaignName))
                _selectedChildren.Add(child);

            if (CampaignHierarchy.IsChild(_campaignName))
                _pickedMaster = CampaignHierarchy.ResolveMaster(_campaignName);

            BuildControls();

            FamilyMode startMode = CampaignHierarchy.IsChild(_campaignName) ? FamilyMode.Child
                                 : (_selectedChildren.Count > 0 ? FamilyMode.Main : FamilyMode.Independent);

            _loading = true;
            _radioIndependent.Checked = startMode == FamilyMode.Independent;
            _radioMain.Checked = startMode == FamilyMode.Main;
            _radioChild.Checked = startMode == FamilyMode.Child;
            _loading = false;

            PopulateMainList();
            PopulateChildList();
            UpdateLayout();
        }

        // ------------------------------------------------------------------
        // Construction de la fenêtre
        // ------------------------------------------------------------------

        private void BuildControls()
        {
            var nameLabel = new Label
            {
                Text = _campaignName,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = false,
                AutoEllipsis = true,
                Width = PanelWidth,
                Height = 24,
                Location = new Point(PanelLeft, 12)
            };
            Controls.Add(nameLabel);

            // Encadré "situation actuelle"
            var statusPanel = new Panel
            {
                BackColor = Color.FromArgb(235, 243, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Width = PanelWidth,
                Height = 40,
                Location = new Point(PanelLeft, 42)
            };
            Controls.Add(statusPanel);

            _statusLabel = new Label
            {
                AutoSize = false,
                Width = PanelWidth - 16,
                Height = 30,
                Location = new Point(8, 5),
                Text = DescribeCurrentSituation()
            };
            statusPanel.Controls.Add(_statusLabel);

            var helpLabel = new Label
            {
                Text = "Choose what this campaign should be, then click OK.\r\nNothing changes until you click OK.",
                ForeColor = Color.DimGray,
                AutoSize = false,
                Width = PanelWidth,
                Height = 36,
                Location = new Point(PanelLeft, 90)
            };
            Controls.Add(helpLabel);

            // ----- Partie 1 : Independent -----

            _radioIndependent = new RadioButton();
            _partIndependent = CreatePart(_radioIndependent, "Independent campaign", out _stripIndependent);

            _independentHint = new Label
            {
                AutoSize = false,
                Width = 450,
                Height = 40,
                Location = new Point(18, 42),
                Text = BuildIndependentHint()
            };
            _partIndependent.Controls.Add(_independentHint);

            // ----- Partie 2 : Main -----

            _radioMain = new RadioButton();
            _partMain = CreatePart(_radioMain, "Main campaign (the parent)", out _stripMain);

            _mainLabel = new Label { Text = "Tick the campaigns that will be its children:", AutoSize = true, Location = new Point(18, 42) };
            _partMain.Controls.Add(_mainLabel);

            _mainSearchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(18, 72) };
            _partMain.Controls.Add(_mainSearchLabel);

            _mainSearch = new TextBox { Width = 385, Location = new Point(80, 68) };
            _mainSearch.TextChanged += (s, e) => PopulateMainList();
            _partMain.Controls.Add(_mainSearch);

            _mainList = new CheckedListBox
            {
                Width = 450,
                Height = 130,
                Location = new Point(18, 96),
                CheckOnClick = true
            };
            _mainList.ItemCheck += MainList_ItemCheck;
            _partMain.Controls.Add(_mainList);

            _mainCountLabel = new Label { AutoSize = true, ForeColor = Color.DimGray, Location = new Point(18, 232) };
            _partMain.Controls.Add(_mainCountLabel);

            // ----- Partie 3 : Child -----

            _radioChild = new RadioButton();
            _partChild = CreatePart(_radioChild, "Child campaign", out _stripChild);

            _childLabel = new Label { Text = "Pick its main campaign:", AutoSize = true, Location = new Point(18, 42) };
            _partChild.Controls.Add(_childLabel);

            _childSearchLabel = new Label { Text = "Search:", AutoSize = true, Location = new Point(18, 72) };
            _partChild.Controls.Add(_childSearchLabel);

            _childSearch = new TextBox { Width = 385, Location = new Point(80, 68) };
            _childSearch.TextChanged += (s, e) => PopulateChildList();
            _partChild.Controls.Add(_childSearch);

            _childList = new ListBox
            {
                Width = 450,
                Height = 130,
                Location = new Point(18, 96),
                SelectionMode = SelectionMode.One
            };
            _childList.SelectedIndexChanged += ChildList_SelectedIndexChanged;
            _partChild.Controls.Add(_childList);

            _childResultLabel = new Label
            {
                AutoSize = false,
                Width = 450,
                Height = 54,
                Location = new Point(18, 232),
                ForeColor = Color.FromArgb(0, 90, 170)
            };
            _partChild.Controls.Add(_childResultLabel);

            // ----- OK / Cancel (la position est donnée par UpdateLayout) -----

            _okButton = new Button { Text = "OK", Width = 90, Height = 28 };
            _okButton.Click += OkButton_Click;
            Controls.Add(_okButton);

            _cancelButton = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(_cancelButton);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        // Crée le cadre d'une partie : un Panel avec une barre colorée à gauche (bleue si
        // la partie est active, grise sinon) et le RadioButton qui sert de titre.
        private Panel CreatePart(RadioButton radio, string radioText, out Panel strip)
        {
            var panel = new Panel
            {
                Width = PanelWidth,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            strip = new Panel { Width = 6, Dock = DockStyle.Left, BackColor = Color.Silver };
            panel.Controls.Add(strip);

            radio.Text = radioText;
            radio.AutoSize = true;
            radio.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            radio.Location = new Point(18, 8);
            radio.CheckedChanged += Radio_CheckedChanged;
            panel.Controls.Add(radio);

            Controls.Add(panel);
            return panel;
        }

        private List<string> GetAllCampaignNames()
        {
            string campaignsRoot = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns";

            if (!Directory.Exists(campaignsRoot))
                return new List<string>();

            return Directory.GetDirectories(campaignsRoot).Select(Path.GetFileName).ToList();
        }

        // ------------------------------------------------------------------
        // Textes
        // ------------------------------------------------------------------

        private string DescribeCurrentSituation()
        {
            List<string> children = CampaignHierarchy.GetChildren(_campaignName);

            if (children.Count > 0)
                return "Now: this is a MAIN campaign with " + children.Count + " child" + (children.Count > 1 ? "ren" : "") + ".";

            if (CampaignHierarchy.IsChild(_campaignName))
                return "Now: this is a CHILD campaign.\r\nIts main campaign is: " + CampaignHierarchy.ResolveMaster(_campaignName);

            return "Now: this campaign is independent (no family).";
        }

        private string BuildIndependentHint()
        {
            List<string> kids = CampaignHierarchy.GetChildren(_campaignName);

            if (kids.Count > 0)
                return "Not linked to any other campaign.\r\nIts " + kids.Count + " child(ren) stay together: '" + kids[0] + "' becomes their new main campaign.";

            return "Not linked to any other campaign.\r\nAny link it has now will be removed.";
        }

        // Texte gris après le nom : où se trouve cette campagne dans sa famille actuelle.
        // Sert à voir, avant le OK, ce qu'on va "voler" à une autre famille.
        private CampaignItem MakeItem(string name)
        {
            string text = name;

            if (CampaignHierarchy.IsChild(name))
            {
                text += "   (child of " + CampaignHierarchy.ResolveMaster(name) + ")";
            }
            else
            {
                int childCount = CampaignHierarchy.GetChildren(name).Count;
                if (childCount > 0)
                    text += "   (main, " + childCount + " child" + (childCount > 1 ? "ren" : "") + ")";
            }

            return new CampaignItem { Name = name, Text = text };
        }

        // ------------------------------------------------------------------
        // Les 3 parties liées entre elles + mise en page
        // ------------------------------------------------------------------

        private FamilyMode CurrentMode()
        {
            if (_radioMain.Checked) return FamilyMode.Main;
            if (_radioChild.Checked) return FamilyMode.Child;
            return FamilyMode.Independent;
        }

        // Un RadioButton ne se décoche pas tout seul quand ses voisins sont dans un autre
        // Panel : on le fait à la main. Un RadioButton ne peut pas être décoché par un clic,
        // donc il y a toujours exactement une partie active.
        private void Radio_CheckedChanged(object sender, EventArgs e)
        {
            if (_loading)
                return;

            var radio = (RadioButton)sender;

            if (!radio.Checked)
                return;

            _loading = true;
            foreach (RadioButton other in new[] { _radioIndependent, _radioMain, _radioChild })
            {
                if (other != radio)
                    other.Checked = false;
            }
            _loading = false;

            UpdateLayout();
        }

        // Déplie la partie active, replie les deux autres, empile les trois, place les
        // boutons et ajuste la hauteur de la fenêtre.
        private void UpdateLayout()
        {
            FamilyMode mode = CurrentMode();

            LayoutPart(_partIndependent, _stripIndependent, _radioIndependent, mode == FamilyMode.Independent, 88,
                _independentHint);

            LayoutPart(_partMain, _stripMain, _radioMain, mode == FamilyMode.Main, 262,
                _mainLabel, _mainSearchLabel, _mainSearch, _mainList, _mainCountLabel);

            LayoutPart(_partChild, _stripChild, _radioChild, mode == FamilyMode.Child, 292,
                _childLabel, _childSearchLabel, _childSearch, _childList, _childResultLabel);

            int y = 135;
            foreach (Panel part in new[] { _partIndependent, _partMain, _partChild })
            {
                part.Location = new Point(PanelLeft, y);
                y += part.Height + 8;
            }

            _okButton.Location = new Point(PanelLeft + PanelWidth - 190, y + 6);
            _cancelButton.Location = new Point(PanelLeft + PanelWidth - 90, y + 6);

            ClientSize = new Size(PanelLeft * 2 + PanelWidth, y + 6 + 28 + 15);

            UpdateMainCount();
            UpdateChildResult();
            UpdateOkButton();
        }

        private void LayoutPart(Panel panel, Panel strip, RadioButton radio, bool active, int activeHeight, params Control[] content)
        {
            panel.Height = active ? activeHeight : PartHeaderHeight;
            panel.BackColor = active ? Color.White : Color.FromArgb(245, 245, 245);
            strip.BackColor = active ? Color.FromArgb(0, 120, 215) : Color.Silver;
            radio.ForeColor = active ? Color.Black : Color.Gray;

            foreach (Control c in content)
                c.Visible = active;
        }

        // ------------------------------------------------------------------
        // Partie Main : cases à cocher
        // ------------------------------------------------------------------

        // Toutes les autres campagnes, sauf la mère actuelle de celle-ci (la cocher n'aurait
        // pas de sens clair). L'état coché vient de _selectedChildren, jamais de la liste
        // elle-même : le filtre de recherche ne fait donc jamais perdre un choix.
        private void PopulateMainList()
        {
            _loading = true;

            int topIndex = _mainList.TopIndex;
            string filter = _mainSearch.Text.Trim();
            string ownMaster = CampaignHierarchy.IsChild(_campaignName) ? CampaignHierarchy.ResolveMaster(_campaignName) : null;

            var candidates = _allNames
                .Where(n => n != _campaignName && !string.Equals(n, ownMaster, StringComparison.OrdinalIgnoreCase))
                .Where(n => filter.Length == 0 || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            _mainList.BeginUpdate();
            _mainList.Items.Clear();
            foreach (string name in candidates)
                _mainList.Items.Add(MakeItem(name), _selectedChildren.Contains(name));
            _mainList.EndUpdate();

            if (topIndex < _mainList.Items.Count)
                _mainList.TopIndex = topIndex;

            _loading = false;
        }

        // ItemCheck est appelé AVANT que la case change : on se base sur e.NewValue.
        private void MainList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading)
                return;

            var item = (CampaignItem)_mainList.Items[e.Index];

            if (e.NewValue == CheckState.Checked)
                _selectedChildren.Add(item.Name);
            else
                _selectedChildren.Remove(item.Name);

            UpdateMainCount();
        }

        private void UpdateMainCount()
        {
            _mainCountLabel.Text = _selectedChildren.Count + " child campaign(s) selected";
        }

        // ------------------------------------------------------------------
        // Partie Child : choix de la mère
        // ------------------------------------------------------------------

        // Toutes les autres campagnes, sauf ses propres filles (la rattacher à l'une d'elles
        // créerait une boucle).
        private void PopulateChildList()
        {
            _loading = true;

            string filter = _childSearch.Text.Trim();
            var ownChildren = new HashSet<string>(CampaignHierarchy.GetChildren(_campaignName), StringComparer.OrdinalIgnoreCase);

            var candidates = _allNames
                .Where(n => n != _campaignName && !ownChildren.Contains(n))
                .Where(n => filter.Length == 0 || n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            _childList.BeginUpdate();
            _childList.Items.Clear();

            int selectedIndex = -1;
            foreach (string name in candidates)
            {
                int index = _childList.Items.Add(MakeItem(name));

                if (string.Equals(name, _pickedMaster, StringComparison.OrdinalIgnoreCase))
                    selectedIndex = index;
            }

            _childList.EndUpdate();

            if (selectedIndex >= 0)
                _childList.SelectedIndex = selectedIndex;

            _loading = false;
        }

        private void ChildList_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loading)
                return;

            var item = _childList.SelectedItem as CampaignItem;

            if (item == null)
                return;

            _pickedMaster = item.Name;
            UpdateChildResult();
            UpdateOkButton();
        }

        // Annonce clairement le résultat : si on a cliqué sur une fille, c'est sa mère
        // qui sera utilisée (CampaignHierarchy.SetChild fait cette redirection).
        private void UpdateChildResult()
        {
            if (string.IsNullOrEmpty(_pickedMaster))
            {
                _childResultLabel.Text = "Pick a campaign in the list.";
                return;
            }

            string target = CampaignHierarchy.ResolveMaster(_pickedMaster);

            string text = "This campaign will be a CHILD of: " + target;

            if (!string.Equals(target, _pickedMaster, StringComparison.OrdinalIgnoreCase))
                text += "\r\n('" + _pickedMaster + "' is already a child of it, so its main campaign is used.)";

            int ownChildCount = CampaignHierarchy.GetChildren(_campaignName).Count;
            if (ownChildCount > 0)
                text += "\r\nIts " + ownChildCount + " child(ren) will move with it.";

            _childResultLabel.Text = text;
        }

        // En mode Child, OK n'a pas de sens tant qu'aucune mère n'est choisie.
        private void UpdateOkButton()
        {
            _okButton.Enabled = !(CurrentMode() == FamilyMode.Child && string.IsNullOrEmpty(_pickedMaster));
        }

        // ------------------------------------------------------------------
        // OK : c'est ici, et seulement ici, qu'on écrit dans CampaignHierarchy
        // ------------------------------------------------------------------

        private void OkButton_Click(object sender, EventArgs e)
        {
            switch (CurrentMode())
            {
                case FamilyMode.Independent:
                    ApplyIndependent();
                    break;

                case FamilyMode.Main:
                    ApplyMain();
                    break;

                case FamilyMode.Child:
                    // SetChild redirige tout seul vers le vrai maître si la campagne choisie
                    // est elle-même une fille, et emmène les filles de celle-ci avec elle.
                    CampaignHierarchy.SetChild(_campaignName, _pickedMaster);
                    break;
            }

            DialogResult = DialogResult.OK; // ferme la fenêtre
        }

        private void ApplyIndependent()
        {
            List<string> kids = CampaignHierarchy.GetChildren(_campaignName);

            if (kids.Count > 0)
            {
                // Elle était mère : sa famille continue sans elle. La 1ère fille (ordre
                // alphabétique, comme CampaignHierarchy.OnCampaignDeleted) devient la mère
                // des autres.
                string newMain = kids[0];
                CampaignHierarchy.Detach(newMain);

                for (int i = 1; i < kids.Count; i++)
                    CampaignHierarchy.SetChild(kids[i], newMain);
            }
            else
            {
                CampaignHierarchy.Detach(_campaignName); // sans effet si elle n'avait aucun lien
            }
        }

        private void ApplyMain()
        {
            // Elle quitte sa mère d'abord, sinon SetChild redirigerait les nouvelles filles
            // vers cette mère-là.
            if (CampaignHierarchy.IsChild(_campaignName))
                CampaignHierarchy.Detach(_campaignName);

            var currentChildren = new HashSet<string>(CampaignHierarchy.GetChildren(_campaignName), StringComparer.OrdinalIgnoreCase);

            // Décochées : redeviennent indépendantes
            foreach (string name in currentChildren)
            {
                if (!_selectedChildren.Contains(name))
                    CampaignHierarchy.Detach(name);
            }

            // Nouvellement cochées : SetChild écrase leur ancienne mère (changement automatique)
            foreach (string name in _selectedChildren)
            {
                if (!currentChildren.Contains(name))
                    CampaignHierarchy.SetChild(name, _campaignName);
            }
        }
    }
}
