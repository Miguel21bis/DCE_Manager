using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    // Popup "Gérer la famille" : ouverte depuis la colonne Family de la grid
    // (icône ⋯), pour corriger à la main un classement maître/fille automatique
    // qui se serait trompé (voir CampaignHierarchy.ClassifyUnknown).
    //
    // Deux façons d'agir, indépendantes :
    // - "Attach as child of" : cette campagne rejoint une autre famille.
    // - "Add existing campaigns as children" : on rattache d'autres campagnes à
    //   celle-ci. Désactivé si cette campagne est elle-même une fille (le modèle
    //   est à 2 niveaux, pas d'arbre - voir CampaignHierarchy).
    public class ManageFamily_Form : Form
    {
        private readonly string _campaignName;

        private Label _statusLabel;
        private ComboBox _masterCombo;
        private Button _attachButton;
        private Button _detachButton;

        private Label _addChildrenLabel;
        private CheckedListBox _addChildrenList;
        private Button _addChildrenButton;

        private Button _closeButton;

        public ManageFamily_Form(string campaignName)
        {
            _campaignName = campaignName;

            Text = "Manage family - " + campaignName;
            Width = 460;
            Height = 440;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            BuildControls();
            RefreshStatus();
        }

        private void BuildControls()
        {
            var nameLabel = new Label
            {
                Text = _campaignName,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(15, 15)
            };
            Controls.Add(nameLabel);

            _statusLabel = new Label
            {
                AutoSize = false,
                Width = 410,
                Height = 50,
                Location = new Point(15, 40)
            };
            Controls.Add(_statusLabel);

            // ----- Section 1 : s'attacher soi-même à une autre campagne -----

            var attachToLabel = new Label
            {
                Text = "Attach this campaign as child of :",
                AutoSize = true,
                Location = new Point(15, 100)
            };
            Controls.Add(attachToLabel);

            _masterCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 290,
                Location = new Point(15, 120)
            };
            Controls.Add(_masterCombo);

            _attachButton = new Button
            {
                Text = "Attach",
                Width = 100,
                Location = new Point(315, 119)
            };
            _attachButton.Click += AttachButton_Click;
            Controls.Add(_attachButton);

            _detachButton = new Button
            {
                Text = "Detach / make standalone",
                Width = 220,
                Location = new Point(15, 155)
            };
            _detachButton.Click += DetachButton_Click;
            Controls.Add(_detachButton);

            // ----- Section 2 : ajouter des filles à celle-ci -----

            _addChildrenLabel = new Label
            {
                Text = "Add existing campaigns as children of this one :",
                AutoSize = true,
                Location = new Point(15, 200)
            };
            Controls.Add(_addChildrenLabel);

            _addChildrenList = new CheckedListBox
            {
                Width = 410,
                Height = 150,
                Location = new Point(15, 220),
                CheckOnClick = true
            };
            Controls.Add(_addChildrenList);

            _addChildrenButton = new Button
            {
                Text = "Add checked as children",
                Width = 200,
                Location = new Point(15, 378)
            };
            _addChildrenButton.Click += AddChildrenButton_Click;
            Controls.Add(_addChildrenButton);

            _closeButton = new Button
            {
                Text = "Close",
                Width = 90,
                Location = new Point(345, 378)
            };
            _closeButton.Click += (s, e) => Close();
            Controls.Add(_closeButton);

            PopulateMasterCombo();
            PopulateAddChildrenList();
        }

        private List<string> GetAllCampaignNames()
        {
            string campaignsRoot = ParamConf.PATH_SavedGames_DCS + @"\Mods\tech\DCE\Missions\Campaigns";

            if (!Directory.Exists(campaignsRoot))
                return new List<string>();

            return Directory.GetDirectories(campaignsRoot).Select(Path.GetFileName).ToList();
        }

        // Candidats pour "s'attacher à" : tout le monde sauf soi-même et ses propres
        // filles (s'y attacher créerait un cycle immédiat - CampaignHierarchy.SetChild
        // s'en protège déjà, mais autant ne pas les proposer du tout).
        private void PopulateMasterCombo()
        {
            var ownChildren = new HashSet<string>(CampaignHierarchy.GetChildren(_campaignName), StringComparer.OrdinalIgnoreCase);

            var candidates = GetAllCampaignNames()
                .Where(n => n != _campaignName && !ownChildren.Contains(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            _masterCombo.Items.Clear();
            foreach (string name in candidates)
                _masterCombo.Items.Add(name);
        }

        // Candidats pour "ajouter comme fille" : tout le monde sauf soi-même et ceux
        // déjà rattachés ici. Désactivé entièrement si _campaignName est elle-même une
        // fille (pas de 3e niveau possible).
        private void PopulateAddChildrenList()
        {
            bool isChild = CampaignHierarchy.IsChild(_campaignName);

            _addChildrenLabel.Text = isChild
                ? "Add existing campaigns as children of this one : (disabled - this campaign is itself a child)"
                : "Add existing campaigns as children of this one :";

            _addChildrenList.Enabled = !isChild;
            _addChildrenButton.Enabled = !isChild;

            var currentChildren = new HashSet<string>(CampaignHierarchy.GetChildren(_campaignName), StringComparer.OrdinalIgnoreCase);

            var candidates = GetAllCampaignNames()
                .Where(n => n != _campaignName && !currentChildren.Contains(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            _addChildrenList.Items.Clear();
            foreach (string name in candidates)
                _addChildrenList.Items.Add(name);
        }

        private void RefreshStatus()
        {
            List<string> children = CampaignHierarchy.GetChildren(_campaignName);
            bool isChild = CampaignHierarchy.IsChild(_campaignName);

            if (children.Count > 0)
            {
                _statusLabel.Text = "Master of : " + string.Join(", ", children);
            }
            else if (isChild)
            {
                _statusLabel.Text = "Child of : " + CampaignHierarchy.ResolveMaster(_campaignName);
            }
            else
            {
                _statusLabel.Text = "Standalone (no family link).";
            }

            _detachButton.Enabled = isChild; // rien à détacher pour un maître ou une campagne seule

            PopulateMasterCombo();
            PopulateAddChildrenList();
        }

        private void AttachButton_Click(object sender, EventArgs e)
        {
            if (_masterCombo.SelectedItem == null)
            {
                MessageBox.Show("Pick a campaign to attach to first.", "Manage family", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            CampaignHierarchy.SetChild(_campaignName, _masterCombo.SelectedItem.ToString());
            RefreshStatus();
        }

        private void DetachButton_Click(object sender, EventArgs e)
        {
            CampaignHierarchy.Detach(_campaignName);
            RefreshStatus();
        }

        private void AddChildrenButton_Click(object sender, EventArgs e)
        {
            var checkedNames = _addChildrenList.CheckedItems.Cast<object>().Select(o => o.ToString()).ToList();

            if (checkedNames.Count == 0)
            {
                MessageBox.Show("Check at least one campaign to add first.", "Manage family", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            foreach (string childName in checkedNames)
                CampaignHierarchy.SetChild(childName, _campaignName);

            RefreshStatus();
        }
    }
}
