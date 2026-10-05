using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Panneau d'édition des axes de progression (WargameState.Axes).
    // A placer dans Wargame\UI\.
    //
    // Un axe = une zone de départ (A) + une zone objectif (B). Le chemin entre les
    // deux est calculé tout seul (WargameAxisPathFinder), le campaignMaker ne le
    // saisit jamais. Un axe devient actif en jeu quand le flag de campagne qu'on lui
    // donne ici est posé par un trigger de camp_triggers_init.lua (bouton "Copy trigger").
    //
    // Le panneau modifie directement les objets WargameAxis de la liste reçue dans
    // Bind() ; l'écriture disque est gérée par la Form (Save -> Init).
    internal class ucWargameAxisPanel : UserControl
    {
        private List<WargameAxis> _axes = new List<WargameAxis>();
        private List<WargameZoneData> _zones = new List<WargameZoneData>();
        private bool _editable = true;

        // true pendant qu'on remplit les champs depuis l'axe, pour ne pas
        // redéclencher les handlers de changement
        private bool _loading;

        // Un axe a été modifié par l'utilisateur (la Form passe en "dirty" et redessine)
        public event Action AxesChanged;

        // L'axe sélectionné dans la liste a changé (la carte le met en évidence)
        public event Action<WargameAxis> SelectedAxisChanged;

        // true = l'utilisateur veut choisir la zone de départ (A) sur la carte, false = l'arrivée (B)
        public event Action<bool> PickZoneRequested;

        private Label _lblNote;
        private ListBox _lstAxes;
        private Button _btnAdd, _btnRemove, _btnPickStart, _btnPickEnd, _btnCopyTrigger;
        private TextBox _txtName, _txtStart, _txtEnd, _txtFlag;
        private Label _lblInfo;

        public ucWargameAxisPanel()
        {
            BuildUi();
            Resize += (s, e) => ApplyLayout();
            ApplyLayout();
            LoadAxisFields();
        }

        // -------------------- API pour la Form --------------------

        // axes : la liste vivante (WargameZoneRepository.State.Axes). editable = false
        // en vue Active : les axes se définissent dans l'Init.
        public void Bind(List<WargameAxis> axes, List<WargameZoneData> zones, bool editable)
        {
            _axes = axes ?? new List<WargameAxis>();
            _zones = zones ?? new List<WargameZoneData>();
            _editable = editable;

            RefreshList(_axes.Count > 0 ? 0 : -1);
        }

        // A appeler quand les voisins des zones ont pu changer : les chemins sont recalculés.
        public void RefreshPaths()
        {
            foreach (WargameAxis axis in _axes)
                axis.Path = WargameAxisPathFinder.FindPath(_zones, axis.StartZoneId, axis.EndZoneId);

            int keep = _lstAxes.SelectedIndex;
            RefreshList(keep);
        }

        // Réponse à PickZoneRequested : la zone cliquée sur la carte.
        public void SetPickedZone(bool isStart, string zoneId)
        {
            WargameAxis axis = CurrentAxis;
            if (axis == null || !_editable) return;

            if (isStart) axis.StartZoneId = zoneId;
            else axis.EndZoneId = zoneId;

            axis.Path = WargameAxisPathFinder.FindPath(_zones, axis.StartZoneId, axis.EndZoneId);

            UpdateListItem(_lstAxes.SelectedIndex);
            LoadAxisFields();
            AxesChanged?.Invoke();
        }

        private WargameAxis CurrentAxis
        {
            get
            {
                int index = _lstAxes.SelectedIndex;
                return index >= 0 && index < _axes.Count ? _axes[index] : null;
            }
        }

        // -------------------- Liste --------------------

        private static string AxisLabel(WargameAxis axis)
        {
            string from = string.IsNullOrEmpty(axis.StartZoneId) ? "?" : axis.StartZoneId;
            string to = string.IsNullOrEmpty(axis.EndZoneId) ? "?" : axis.EndZoneId;
            string state = axis.IsValid ? axis.Path.Count + " zones" : "no path";

            return axis.Name + "   (" + from + " -> " + to + ", " + state + ")";
        }

        private void RefreshList(int selectIndex)
        {
            _loading = true;
            try
            {
                _lstAxes.Items.Clear();

                foreach (WargameAxis axis in _axes)
                    _lstAxes.Items.Add(AxisLabel(axis));

                if (selectIndex >= 0 && selectIndex < _lstAxes.Items.Count)
                    _lstAxes.SelectedIndex = selectIndex;
            }
            finally
            {
                _loading = false;
            }

            LoadAxisFields();
            SelectedAxisChanged?.Invoke(CurrentAxis);
        }

        private void UpdateListItem(int index)
        {
            if (index < 0 || index >= _axes.Count) return;

            _loading = true;
            try
            {
                _lstAxes.Items[index] = AxisLabel(_axes[index]);
                _lstAxes.SelectedIndex = index;
            }
            finally
            {
                _loading = false;
            }
        }

        private void LstAxes_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            LoadAxisFields();
            SelectedAxisChanged?.Invoke(CurrentAxis);
        }

        // -------------------- Champs --------------------

        private void LoadAxisFields()
        {
            WargameAxis axis = CurrentAxis;
            bool hasAxis = axis != null;
            bool canEdit = hasAxis && _editable;

            _loading = true;
            try
            {
                _txtName.Text = hasAxis ? axis.Name : "";
                _txtStart.Text = hasAxis ? axis.StartZoneId : "";
                _txtEnd.Text = hasAxis ? axis.EndZoneId : "";
                _txtFlag.Text = hasAxis ? axis.ActivationFlag : "";
            }
            finally
            {
                _loading = false;
            }

            _txtName.Enabled = canEdit;
            _txtFlag.Enabled = canEdit;
            _btnPickStart.Enabled = canEdit;
            _btnPickEnd.Enabled = canEdit;
            _btnAdd.Enabled = _editable;
            _btnRemove.Enabled = canEdit;
            _btnCopyTrigger.Enabled = hasAxis;

            _lblNote.Text = _editable
                ? "Pick A and B on the map: the path between them is computed from the zone neighbors."
                : "Axes are defined in the Init view. Switch to Init to edit them.";

            _lblInfo.Text = BuildInfoText(axis);
        }

        private static string BuildInfoText(WargameAxis axis)
        {
            if (axis == null)
                return "No axis selected.";

            var sb = new StringBuilder();

            if (string.IsNullOrEmpty(axis.StartZoneId) || string.IsNullOrEmpty(axis.EndZoneId))
                sb.AppendLine("Pick the start (A) and the end (B) zones on the map.");
            else if (axis.IsValid)
                sb.AppendLine("Path (" + axis.Path.Count + " zones): " + string.Join(" > ", axis.Path));
            else
                sb.AppendLine("NO PATH between " + axis.StartZoneId + " and " + axis.EndZoneId
                    + ". Check that the zones in between are linked as neighbors.");

            sb.AppendLine();

            if (string.IsNullOrWhiteSpace(axis.ActivationFlag))
                sb.AppendLine("No flag: this axis will never be active.");
            else
                sb.AppendLine("Active when a trigger sets the flag \"" + axis.ActivationFlag.Trim()
                    + "\". Use Copy trigger, then paste it in Init\\camp_triggers_init.lua.");

            return sb.ToString();
        }

        private void TxtName_TextChanged(object sender, EventArgs e)
        {
            WargameAxis axis = CurrentAxis;
            if (_loading || axis == null) return;

            axis.Name = _txtName.Text;
            UpdateListItem(_lstAxes.SelectedIndex);
            AxesChanged?.Invoke();
        }

        private void TxtFlag_TextChanged(object sender, EventArgs e)
        {
            WargameAxis axis = CurrentAxis;
            if (_loading || axis == null) return;

            axis.ActivationFlag = _txtFlag.Text.Trim();
            _lblInfo.Text = BuildInfoText(axis);
            AxesChanged?.Invoke();
        }

        // -------------------- Boutons --------------------

        private void ButtonAdd_Click(object sender, EventArgs e)
        {
            if (!_editable) return;

            int n = 1;
            while (_axes.Any(a => string.Equals(a.Name, "Axis " + n, StringComparison.OrdinalIgnoreCase)))
                n++;

            _axes.Add(new WargameAxis { Name = "Axis " + n, ActivationFlag = "axis_" + n });

            RefreshList(_axes.Count - 1);
            AxesChanged?.Invoke();
        }

        private void ButtonRemove_Click(object sender, EventArgs e)
        {
            WargameAxis axis = CurrentAxis;
            if (axis == null || !_editable) return;

            DialogResult answer = MessageBox.Show("Remove the axis '" + axis.Name + "'?",
                "Wargame", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            int index = _lstAxes.SelectedIndex;
            _axes.Remove(axis);

            RefreshList(Math.Min(index, _axes.Count - 1));
            AxesChanged?.Invoke();
        }

        private void ButtonPick_Click(bool isStart)
        {
            if (CurrentAxis == null || !_editable) return;

            _lblInfo.Text = "Click the " + (isStart ? "START (A)" : "END (B)")
                + " zone on the map. Right-click on the map to cancel.";

            PickZoneRequested?.Invoke(isStart);
        }

        // Prépare dans le presse-papiers le trigger qui pose le flag de l'axe. Le
        // campaignMaker le colle dans camp_triggers_init.lua et règle la condition.
        private void ButtonCopyTrigger_Click(object sender, EventArgs e)
        {
            WargameAxis axis = CurrentAxis;
            if (axis == null) return;

            string name = CleanForLua(axis.Name);
            string flag = CleanForLua(axis.ActivationFlag);

            if (flag.Length == 0)
            {
                MessageBox.Show("Type a trigger flag name first.", "Wargame",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("\t[\"Wargame Axis " + name + "\"] = {");
            sb.AppendLine("\t\tactive = true,");
            sb.AppendLine("\t\tonce = true,");
            sb.AppendLine("\t\tcondition = 'Return.Mission() >= 1',");
            sb.AppendLine("\t\taction = {");
            sb.AppendLine("\t\t\t'Action.SetCampFlag(\"" + flag + "\", true)',");
            sb.AppendLine("\t\t},");
            sb.AppendLine("\t},");

            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not use the clipboard: " + ex.Message, "Wargame",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                "Trigger copied.\n\nPaste it inside camp_triggers = { ... } in Init\\camp_triggers_init.lua, "
                + "then change its condition (by default the axis starts at mission 1).\n\n"
                + "Any condition works: Return.Mission(), Return.TargetAlive(...), Return.CampFlag(...)...",
                "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Guillemets retirés : le flag et le nom sont insérés dans une chaîne Lua
        private static string CleanForLua(string text)
        {
            return (text ?? "").Replace("\"", "").Replace("'", "").Trim();
        }

        // -------------------- Construction de l'UI --------------------

        private void BuildUi()
        {
            AutoScroll = true;
            Padding = new Padding(10);

            var title = new Label
            {
                Text = "Axes of progression",
                Location = new Point(10, 10),
                AutoSize = true,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            };

            _lblNote = new Label { Location = new Point(10, 38), Width = 260, Height = 34, ForeColor = Color.DimGray };

            _lstAxes = new ListBox { Location = new Point(10, 76), Width = 260, Height = 100, IntegralHeight = false };
            _lstAxes.SelectedIndexChanged += LstAxes_SelectedIndexChanged;

            _btnAdd = new Button { Text = "Add axis", Location = new Point(10, 182), Width = 125, Height = 28 };
            _btnAdd.Click += ButtonAdd_Click;

            _btnRemove = new Button { Text = "Remove axis", Location = new Point(145, 182), Width = 125, Height = 28 };
            _btnRemove.Click += ButtonRemove_Click;

            var lblName = new Label { Text = "Name", Location = new Point(10, 222), AutoSize = true };
            _txtName = new TextBox { Location = new Point(10, 240), Width = 260 };
            _txtName.TextChanged += TxtName_TextChanged;

            var lblStart = new Label { Text = "From (A)", Location = new Point(10, 274), AutoSize = true };
            _txtStart = new TextBox { Location = new Point(10, 292), Width = 150, ReadOnly = true };
            _btnPickStart = new Button { Text = "Pick on map", Location = new Point(170, 290), Width = 100, Height = 26 };
            _btnPickStart.Click += (s, e) => ButtonPick_Click(true);

            var lblEnd = new Label { Text = "To (B)", Location = new Point(10, 326), AutoSize = true };
            _txtEnd = new TextBox { Location = new Point(10, 344), Width = 150, ReadOnly = true };
            _btnPickEnd = new Button { Text = "Pick on map", Location = new Point(170, 342), Width = 100, Height = 26 };
            _btnPickEnd.Click += (s, e) => ButtonPick_Click(false);

            var lblFlag = new Label { Text = "Trigger flag (set by a camp trigger)", Location = new Point(10, 378), AutoSize = true };
            _txtFlag = new TextBox { Location = new Point(10, 396), Width = 260 };
            _txtFlag.TextChanged += TxtFlag_TextChanged;

            _lblInfo = new Label { Location = new Point(10, 430), Width = 260, Height = 110 };

            _btnCopyTrigger = new Button { Text = "Copy trigger", Location = new Point(10, 546), Width = 260, Height = 30 };
            _btnCopyTrigger.Click += ButtonCopyTrigger_Click;

            Controls.Add(title);
            Controls.Add(_lblNote);
            Controls.Add(_lstAxes);
            Controls.Add(_btnAdd);
            Controls.Add(_btnRemove);
            Controls.Add(lblName);
            Controls.Add(_txtName);
            Controls.Add(lblStart);
            Controls.Add(_txtStart);
            Controls.Add(_btnPickStart);
            Controls.Add(lblEnd);
            Controls.Add(_txtEnd);
            Controls.Add(_btnPickEnd);
            Controls.Add(lblFlag);
            Controls.Add(_txtFlag);
            Controls.Add(_lblInfo);
            Controls.Add(_btnCopyTrigger);
        }

        // Pas d'Anchor : on recalcule les largeurs quand le panneau change de taille
        // (même méthode que ucWargameZoneEditPanel, plus sûre que les ancres ici).
        private void ApplyLayout()
        {
            int inner = ClientSize.Width - 20;
            if (inner < 240) inner = 240;

            _lblNote.Width = inner;
            _lstAxes.Width = inner;
            _txtName.Width = inner;
            _txtFlag.Width = inner;
            _lblInfo.Width = inner;
            _btnCopyTrigger.Width = inner;

            _btnAdd.Width = (inner - 10) / 2;
            _btnRemove.Width = (inner - 10) / 2;
            _btnRemove.Left = 10 + _btnAdd.Width + 10;

            _txtStart.Width = inner - 110;
            _txtEnd.Width = inner - 110;
            _btnPickStart.Left = 10 + inner - 100;
            _btnPickEnd.Left = 10 + inner - 100;
        }
    }
}
