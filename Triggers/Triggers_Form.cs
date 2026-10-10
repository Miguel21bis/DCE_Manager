using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Fenêtre des triggers d'une campagne (partie 1/2 ; la vue en blocs IF / THEN est dans Triggers_Blocks.cs).
    // À gauche la liste regroupée et filtrable, à droite le trigger choisi : IF (conditions), THEN (actions) et DETAILS.
    // Onglet CODE : le Lua complet. On peut modifier : le nom, Active, Once, les conditions et les actions
    // (texte Lua brut pour ce qui n'est pas reconnu). Rien n'est écrit tant qu'on
    // n'a pas cliqué sur "Save changes" (copies de sécurité .bak / .prev, voir CampTriggersFile.Save).
    // Construite en code (comme ConfModForm), donc pas de .Designer.cs.
    public partial class Triggers_Form : Form
    {
        // Couleurs : uniquement des couleurs système (comme les autres Forms de DCE_Manager),
        // jamais de blanc pur. Tout est ici, au même endroit : le jour où DCE_Manager aura des
        // thèmes (clair / sombre), il suffira de changer ces 4 lignes.
        private static readonly Color ColorWindow = SystemColors.Control;
        private static readonly Color ColorList = SystemColors.Control;
        private static readonly Color ColorInput = SystemColors.ControlLight;   // zone de filtre
        private static readonly Color ColorDetailBack = SystemColors.Control;
        private static readonly Color ColorDetailText = SystemColors.ControlText;

        private readonly string _campaignName;
        private CampTriggersFile _file;

        private Label _labelSummary;
        private Button _buttonNew;
        private Label _labelNotice;
        private Label _labelFilter;
        private TextBox _textFilter;                        // recherche par NOM (approchante)
        private ComboBox _textFunc;                          // recherche par fonction DCE (Action.TargetActive...)
        private Panel _panelProblems;
        private Button _buttonPrevProblem, _buttonNextProblem;
        private List<CampTrigger> _displayOrder = new List<CampTrigger>();   // les triggers trouvés, dans l'ordre de la liste (groupes repliés compris)
        private object _tipKey;                             // ce que l'infobulle "problèmes" montre en ce moment
        private int _funcSourceCount = -1;
        private DateTime _proposedExpires = new DateTime(1970, 1, 1);
        private CheckBox _checkOnlyProblems;
        private ListBox _listTriggers;
        private TextBox _textDetail;
        private Button _buttonScanAll;
        private Button _buttonProblems;
        private Button _buttonFlags;
        private Button _buttonClose;
        private Button _buttonSave;
        private Button _buttonDiscard;
        private Panel _panelEdit;
        private TextBox _textName;
        private CheckBox _checkActive;
        private CheckBox _checkOnce;
        private CheckBox _checkExpires;             // "Active until" : la clé expires du trigger
        private DateTimePicker _dateExpires;
        private Label _labelExpires;
        private Label _labelStatus;                // "✔ Valid" / "⚠ 2 warnings" du trigger affiché
        private readonly Dictionary<string, List<string>> _namesCache = new Dictionary<string, List<string>>();
        private System.Windows.Forms.Timer _filterTimer;     // le filtre attend 300 ms sans frappe avant de se lancer
        private bool _refilling;                             // true pendant qu'on remplit la liste de gauche
        private int _busy;                                   // > 0 : sablier affiché
        private Image _iconClone;
        private Image _iconCloneSelected;
        private const int CloneZone = 26;                    // largeur (à droite de chaque ligne) où se trouve l'icône de clonage
        private const int DeleteZone = 22;                   // juste à gauche du clonage : la petite poubelle
        private Image _iconDelete;
        private Image _iconDeleteSelected;
        private int _overClone;                          // 0 = rien, 1 = icône de clonage, 2 = poubelle
        private SplitContainer _split;                       // l'onglet LIST
        private TextBox _textCode;                           // l'onglet CODE (le Lua complet, lecture seule)
        private Panel _findBar;                              // la recherche (Ctrl+F) de l'onglet CODE
        private TextBox _textFind;
        private Label _labelFind;
        private Button _buttonFind;
        private string _codeText = "";                       // le texte affiché dans l'onglet CODE (copie, pour chercher vite)
        private ComboBox _comboGroup;                        // regroupement de la liste de gauche
        private readonly HashSet<string> _collapsed = new HashSet<string>();   // groupes repliés (par clé)
        private bool _headerMouse;                           // un clic souris vient d'arriver sur un titre de groupe
        private int _lastIndex = -1;                         // dernière ligne trigger sélectionnée (pour sauter les titres au clavier)
        private Button _buttonAddAction;
        private ContextMenuStrip _menuAdd;
        private List<FuncItem> _funcItems;
        private CampTrigger _editing;          // le trigger affiché dans la zone d'édition
        private bool _loadingEditor;           // true pendant qu'on remplit la zone d'édition (évite les événements)
        private bool _dirty;
        private bool _confirmedThisSession;
        private readonly ToolTip _toolTip = new ToolTip();

        public Triggers_Form(string campaignName)
        {
            _campaignName = campaignName;

            Text = "Triggers - " + campaignName;
            Width = 1050;
            Height = 680;
            MinimumSize = new Size(760, 460);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9);
            BackColor = ColorWindow;

            TriggerCatalog.Refresh();           // les fonctions ajoutées dans ScriptsMod depuis la dernière fois
            BuildForm();
            LoadTriggers();
        }

        private void BuildForm()
        {
            // ----- bandeau du haut : résumé + avertissement -----
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 6, 10, 0) };

            _labelNotice = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                ForeColor = Color.FromArgb(140, 90, 0),
                AutoEllipsis = true
            };

            _labelSummary = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoEllipsis = true
            };

            topPanel.Controls.Add(_labelSummary);
            topPanel.Controls.Add(_labelNotice);
            BuildLanguageBox(topPanel);

            // ----- bas : boutons -----
            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10, 6, 10, 0)
            };

            _buttonClose = new Button { Text = Lang.T("Close"), Width = 90 };
            _buttonClose.Click += (s, e) => Close();

            _buttonScanAll = new Button { Text = Lang.T("Scan all campaigns"), Width = 150 };
            _buttonScanAll.Click += ButtonScanAll_Click;

            _toolTip.SetToolTip(_buttonScanAll,
                Lang.T("Test tool: reads the triggers of every campaign and lists the counts and the errors on the right. Nothing is written."));

            _buttonProblems = new Button { Text = Lang.T("All problems"), Width = 110 };
            _buttonProblems.Click += (s, e) =>
            {
                _listTriggers.ClearSelected();       // affiche d'abord la vue d'ensemble, qu'on remplace juste après
                _textDetail.Text = BuildOverview();
            };

            _toolTip.SetToolTip(_buttonProblems, Lang.T("Lists the errors, warnings and notes found in all the triggers of this campaign."));

            _buttonFlags = new Button { Text = "Flags", Width = 80 };
            _buttonFlags.Click += (s, e) =>
            {
                _listTriggers.ClearSelected();
                _textDetail.Text = TriggerGrouper.FlagReport(_file);
            };

            _toolTip.SetToolTip(_buttonFlags, Lang.T("Lists every flag (number or name) with the triggers that read it and the triggers that set it."));

            _buttonSave = new Button { Text = Lang.T("Save changes"), Width = 110, Enabled = false };
            _buttonSave.Click += ButtonSave_Click;
            _toolTip.SetToolTip(_buttonSave, Lang.T("Writes Init\\camp_triggers_init.lua (as a list). Backups: .bak = the very first original, .prev = the version before this save."));

            _buttonDiscard = new Button { Text = Lang.T("Discard changes"), Width = 120, Enabled = false };
            _buttonDiscard.Click += ButtonDiscard_Click;

            bottomPanel.Controls.Add(_buttonClose);
            bottomPanel.Controls.Add(_buttonScanAll);
            bottomPanel.Controls.Add(_buttonProblems);
            bottomPanel.Controls.Add(_buttonFlags);
            bottomPanel.Controls.Add(_buttonDiscard);
            bottomPanel.Controls.Add(_buttonSave);

            CancelButton = _buttonClose;

            // ----- centre : liste à gauche, détail à droite -----
            var split = _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };

            _listTriggers = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                HorizontalScrollbar = false,
                BackColor = ColorList,
                BorderStyle = BorderStyle.FixedSingle,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 18
            };
            _listTriggers.SelectedIndexChanged += ListTriggers_SelectedIndexChanged;
            _listTriggers.DrawItem += ListTriggers_DrawItem;
            _listTriggers.MouseDown += ListTriggers_MouseDown;
            _listTriggers.MouseMove += ListTriggers_MouseMove;
            _listTriggers.MouseLeave += (s, e) => { SetOverClone(0); HideProblemTip(); };

            _iconClone = MakeCloneIcon(SystemColors.ControlText, SystemColors.ControlLight);
            _iconCloneSelected = MakeCloneIcon(SystemColors.HighlightText, SystemColors.Highlight);
            _iconDelete = MakeDeleteIcon(SystemColors.ControlText);
            _iconDeleteSelected = MakeDeleteIcon(SystemColors.HighlightText);

            _filterTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _filterTimer.Tick += (s, e) =>
            {
                _filterTimer.Stop();
                RefillList();
            };

            _textFilter = new TextBox { Dock = DockStyle.Top, BackColor = ColorInput };
            _textFilter.TextChanged += (s, e) =>
            {
                _filterTimer.Stop();
                _filterTimer.Start();
            };
            _textFilter.HandleCreated += (s, e) => UpdateCues();
            _toolTip.SetToolTip(_textFilter, Lang.T("Finds a trigger by its name. Several words: all must be there, in any order. Small typos and accents are forgiven. (Ctrl+F)"));

            // zone modifiable ET liste : on tape un bout de nom, ou on déroule la liste de toutes les fonctions du catalogue
            _textFunc = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown, BackColor = ColorInput, MaxDropDownItems = 20 };
            _textFunc.TextChanged += (s, e) =>
            {
                _filterTimer.Stop();
                _filterTimer.Start();
            };
            _textFunc.HandleCreated += (s, e) => UpdateCues();
            _toolTip.SetToolTip(_textFunc, Lang.T("Finds the triggers that use a DCE function in their condition or actions, e.g. Action.TargetActive. Several words: all must be there. (Ctrl+Shift+F)"));

            _checkOnlyProblems = new CheckBox { Dock = DockStyle.Fill, Text = Lang.T("Only triggers with problems (\u26A0)") };
            _checkOnlyProblems.CheckedChanged += (s, e) => { RefillList(); _listThen.Invalidate(); };

            _buttonPrevProblem = new Button { Dock = DockStyle.Right, Width = 28, Text = "\u25B2" };
            _buttonNextProblem = new Button { Dock = DockStyle.Right, Width = 28, Text = "\u25BC" };
            _buttonPrevProblem.Click += (s, e) => GoToProblem(-1);
            _buttonNextProblem.Click += (s, e) => GoToProblem(1);
            _toolTip.SetToolTip(_buttonPrevProblem, Lang.T("Previous trigger with a problem (Shift+F8)"));
            _toolTip.SetToolTip(_buttonNextProblem, Lang.T("Next trigger with a problem (F8)"));

            _panelProblems = new Panel { Dock = DockStyle.Top, Height = 26 };
            _panelProblems.Controls.Add(_checkOnlyProblems);
            _panelProblems.Controls.Add(_buttonPrevProblem);
            _panelProblems.Controls.Add(_buttonNextProblem);

            _labelFilter = new Label { Dock = DockStyle.Top, Height = 20, Padding = new Padding(0, 3, 0, 0) };

            // Les Dock = Top s'empilent dans l'ordre inverse de l'ajout : le Label sera tout en haut.
            _comboGroup = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            _comboGroup.Items.AddRange(new object[] { Lang.T("Group by: main condition (auto)"), Lang.T("Group by: mission"), Lang.T("Group by: date"), Lang.T("Group by: flag"), Lang.T("No groups (file order)") });
            _comboGroup.SelectedIndex = 0;
            _comboGroup.SelectedIndexChanged += (s, e) => RefillList(_editing);
            _toolTip.SetToolTip(_comboGroup, Lang.T("How the list is grouped. Click a group title to fold or unfold it."));

            split.Panel1.Controls.Add(_listTriggers);

            _buttonNew = new Button { Dock = DockStyle.Bottom, Height = 28, Text = Lang.T("+ New trigger") };
            _buttonNew.Click += (s, e) => NewTrigger();
            _toolTip.SetToolTip(_buttonNew, Lang.T("Adds an empty trigger (always true, no action) just after the selected one.\r\nIt is written to the file only when you click Save changes."));
            split.Panel1.Controls.Add(_buttonNew);
            split.Panel1.Controls.Add(_comboGroup);
            split.Panel1.Controls.Add(_panelProblems);
            split.Panel1.Controls.Add(_textFunc);
            split.Panel1.Controls.Add(_textFilter);
            split.Panel1.Controls.Add(_labelFilter);
            split.Panel1.Padding = new Padding(8, 0, 4, 8);

            _textDetail = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9.5f),
                BackColor = ColorDetailBack,
                ForeColor = ColorDetailText,
                BorderStyle = BorderStyle.FixedSingle
            };

            // ----- zone d'édition (au-dessus du détail) : nom, Active, Once -----
            _panelEdit = new Panel { Dock = DockStyle.Top, Height = 84, Enabled = false };

            var labelName = new Label { Left = 0, Top = 6, Width = 50, Text = Lang.T("Name") };
            _textName = new TextBox { Left = 52, Top = 3, Width = 300 };
            _textName.Leave += (s, e) => CommitName();

            _checkActive = new CheckBox { Left = 52, Top = 30, Width = 90, Text = Lang.T("Active") };
            _checkActive.CheckedChanged += (s, e) => CommitFlags();

            _checkOnce = new CheckBox { Left = 150, Top = 30, Width = 260, Text = Lang.T("Once (plays one time only)") };
            _checkOnce.CheckedChanged += (s, e) => CommitFlags();

            // ligne 3 : expires = { year, month, day } -> case + calendrier
            _checkExpires = new CheckBox { Left = 52, Top = 58, Width = 130, Text = Lang.T("Active until") };
            _dateExpires = new DateTimePicker { Left = 186, Top = 55, Width = 130, Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy", Enabled = false, Visible = false };
            _labelExpires = new Label { Left = 322, Top = 59, Width = 300, Visible = false, ForeColor = SystemColors.GrayText, Text = Lang.T("(included) then switched off") };
            _checkExpires.CheckedChanged += (s, e) =>
            {
                if (!_checkExpires.Enabled)
                    return;

                if (_checkExpires.Checked)
                {
                    ShowExpiresControls(true);

                    if (!_loadingEditor)
                        _dateExpires.Value = _proposedExpires;      // la date n'apparaît que maintenant qu'on la veut
                }
                else
                {
                    _proposedExpires = _dateExpires.Value;
                    ShowExpiresControls(false);
                }

                CommitExpires();
            };
            _dateExpires.ValueChanged += (s, e) => CommitExpires();
            _toolTip.SetToolTip(_checkExpires, Lang.T("After this day the engine switches the trigger off (active = false). The trigger still works on this day."));
            _toolTip.SetToolTip(_dateExpires, Lang.T("After this day the engine switches the trigger off (active = false). The trigger still works on this day."));

            _labelStatus = new Label { Left = 420, Top = 32, Width = 200, AutoEllipsis = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            _panelEdit.Resize += (s, e) =>
            {
                _labelStatus.Width = Math.Max(100, _panelEdit.ClientSize.Width - _labelStatus.Left - 8);
            };

            _panelEdit.Controls.Add(_labelStatus);
            _panelEdit.Controls.Add(labelName);
            _panelEdit.Controls.Add(_textName);
            _panelEdit.Controls.Add(_checkActive);
            _panelEdit.Controls.Add(_checkOnce);
            _panelEdit.Controls.Add(_checkExpires);
            _panelEdit.Controls.Add(_dateExpires);
            _panelEdit.Controls.Add(_labelExpires);

            // ----- IF / THEN / DETAILS (voir Triggers_Blocks.cs) -----
            BuildBlocksView();

            split.Panel2.Padding = new Padding(4, 20, 8, 8);
            split.Panel2.Controls.Add(_textDetail);          // la vue d'ensemble (aucun trigger choisi)
            split.Panel2.Controls.Add(_panelBlocks);         // le trigger choisi
            split.Panel2.Controls.Add(_panelEdit);

            // ----- onglets : LIST (la liste + l'édition) | CODE (le Lua complet) -----
            _textCode = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                MaxLength = int.MaxValue,
                Font = new Font("Consolas", 9.5f),
                BackColor = ColorDetailBack,
                ForeColor = ColorDetailText,
                BorderStyle = BorderStyle.FixedSingle,
                HideSelection = false,          // la sélection (trigger, résultat de recherche) reste visible même sans le focus
                Visible = false
            };

            _findBar = new Panel { Dock = DockStyle.Top, Height = 32, Visible = false };

            var labelFindTitle = new Label { Left = 10, Top = 7, Width = 40, Text = Lang.T("Find") };
            _textFind = new TextBox { Left = 52, Top = 4, Width = 280 };
            var buttonPrev = new Button { Left = 340, Top = 3, Width = 80, Height = 25, Text = Lang.T("Previous") };
            var buttonNext = new Button { Left = 424, Top = 3, Width = 80, Height = 25, Text = Lang.T("Next") };
            _labelFind = new Label { Left = 512, Top = 7, Width = 200, ForeColor = SystemColors.GrayText };
            var buttonCloseFind = new Button { Left = 716, Top = 3, Width = 28, Height = 25, Text = "\u2716" };

            _textFind.TextChanged += (s, e) => FindText(true, true);
            _textFind.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    FindText(!e.Shift, false);
                }
            };
            buttonNext.Click += (s, e) => FindText(true, false);
            buttonPrev.Click += (s, e) => FindText(false, false);
            buttonCloseFind.Click += (s, e) => HideFindBar();

            _toolTip.SetToolTip(buttonNext, Lang.T("Next match (F3)"));
            _toolTip.SetToolTip(buttonPrev, Lang.T("Previous match (Shift+F3)"));
            _toolTip.SetToolTip(buttonCloseFind, Lang.T("Close the search (Esc)"));

            _findBar.Controls.Add(labelFindTitle);
            _findBar.Controls.Add(_textFind);
            _findBar.Controls.Add(buttonPrev);
            _findBar.Controls.Add(buttonNext);
            _findBar.Controls.Add(_labelFind);
            _findBar.Controls.Add(buttonCloseFind);

            var tabBar = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(10, 4, 10, 2) };

            _tabList = new RadioButton { Appearance = Appearance.Button, Text = Lang.T("LIST"), Left = 10, Top = 4, Width = 90, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Checked = true };
            _tabGraph = new RadioButton { Appearance = Appearance.Button, Text = Lang.T("GRAPH"), Left = 104, Top = 4, Width = 90, Height = 26, TextAlign = ContentAlignment.MiddleCenter };
            _tabCode = new RadioButton { Appearance = Appearance.Button, Text = "CODE", Left = 198, Top = 4, Width = 90, Height = 26, TextAlign = ContentAlignment.MiddleCenter };

            _tabList.CheckedChanged += (s, e) => { if (_tabList.Checked) ShowTab("list"); };
            _tabGraph.CheckedChanged += (s, e) => { if (_tabGraph.Checked) ShowTab("graph"); };
            _tabCode.CheckedChanged += (s, e) => { if (_tabCode.Checked) ShowTab("code"); };

            _toolTip.SetToolTip(_tabList, Lang.T("The triggers, grouped, with their editor."));
            _toolTip.SetToolTip(_tabGraph, Lang.T("The whole campaign at a glance: conditions, triggers and what they change (read only)."));
            _toolTip.SetToolTip(_tabCode, Lang.T("The Lua text of the whole file, as it would be written (read only)."));

            _buttonFind = new Button { Left = 298, Top = 4, Width = 110, Height = 26, Text = Lang.T("Find (Ctrl+F)"), Visible = false };
            _buttonFind.Click += (s, e) => ShowFindBar();

            tabBar.Controls.Add(_tabList);
            tabBar.Controls.Add(_tabGraph);
            tabBar.Controls.Add(_tabCode);
            tabBar.Controls.Add(_buttonFind);

            // Ordre d'ajout : d'abord le Fill, ensuite Top/Bottom (même règle que ConfModForm).
            BuildGraphPanel();

            Controls.Add(split);
            Controls.Add(_textCode);
            Controls.Add(_panelGraph);
            Controls.Add(_findBar);
            Controls.Add(tabBar);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);

            Load += (s, e) =>
            {
                split.Panel1MinSize = 220;
                split.SplitterDistance = 320;
            };

            FormClosing += (s, e) =>
            {
                if (!_dirty)
                    return;

                DialogResult answer = MessageBox.Show(Lang.T("There are changes that are not saved. Close anyway?"), "Triggers",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                    e.Cancel = true;
            };
        }

        private void LoadTriggers()
        {
            BeginBusy();

            try
            {
                _file = CampTriggersFile.Load(_campaignName);
                _namesCache.Clear();

                UpdateSummary();

                _labelNotice.Text = _file.ActiveCopyExists
                    ? Lang.T("Active\\camp_triggers.lua exists: it is the working copy of the running campaign. Changes made to the Init file only reach it after a restart (First Mission).")
                    : "";

                RefillList();
            }
            finally
            {
                EndBusy();
            }
        }

        // Sablier pendant un travail long (appels imbriqués possibles).
        private void BeginBusy()
        {
            if (_busy++ == 0)
            {
                UseWaitCursor = true;
                Cursor.Current = Cursors.WaitCursor;
            }
        }

        private void EndBusy()
        {
            if (_busy > 0 && --_busy == 0)
            {
                UseWaitCursor = false;
                Cursor.Current = Cursors.Default;
            }
        }

        // Le format du fichier, dans la langue choisie ("list", "named table ...").
        private string FormatName()
        {
            string f = _file.Format;
            return f == "list" || f == "empty" ? Lang.W(f) : Lang.T(f);
        }

        private void UpdateSummary()
        {
            if (_file.LoadError != null)
            {
                _labelSummary.Text = Lang.T("Could not read the triggers of '") + _campaignName + "'.";
            }
            else
            {
                _labelSummary.Text = _file.Triggers.Count + Lang.T(" trigger(s)  |  ")
                    + _file.ErrorCount + Lang.T(" error(s), ") + _file.WarningCount + Lang.T(" warning(s)  |  ")
                    + _file.RawStringCount + Lang.T(" text(s) kept as raw Lua  |  format: ")
                    + FormatName();
            }
        }

        private string CurrentGroupMode()
        {
            int i = _comboGroup != null ? _comboGroup.SelectedIndex : 0;
            return i >= 0 && i < TriggerGrouper.Modes.Length ? TriggerGrouper.Modes[i] : "Auto";
        }

        // Remplit la liste de gauche (filtre + groupes) ; garde la sélection si possible.
        // Pendant le remplissage la sélection ne déclenche rien (_refilling) : le détail n'est reconstruit qu'une seule fois, à la fin.
        private void RefillList(CampTrigger preferred = null)
        {
            if (_file == null)
                return;

            _filterTimer.Stop();
            BeginBusy();

            try
            {
                _labelFilter.Text = Lang.T("Working...");
                _labelFilter.Update();

                CampTrigger previous = _listTriggers.SelectedItem as CampTrigger;
                string nameQuery = _textFilter.Text.Trim();
                string funcQuery = _textFunc.Text.Trim();
                bool searching = nameQuery.Length > 0 || funcQuery.Length > 0;
                int withProblems = 0;
                UpdateCues();
                bool onlyProblems = _checkOnlyProblems.Checked;
                string mode = CurrentGroupMode();
                bool headers = mode != "None";

                var found = new List<CampTrigger>();
                var groups = new Dictionary<string, GroupHeader>();
                var members = new Dictionary<string, List<CampTrigger>>();
                var order = new List<GroupHeader>();

                foreach (CampTrigger t in _file.Triggers)
                {
                    if (onlyProblems && t.ErrorCount + t.WarningCount == 0)
                        continue;

                    if (nameQuery.Length > 0 && !t.MatchesName(nameQuery))
                        continue;

                    if (funcQuery.Length > 0 && !t.MatchesFunction(funcQuery))
                        continue;

                    found.Add(t);
                    bool bad = t.ErrorCount + t.WarningCount > 0;

                    if (bad)
                        withProblems++;

                    TriggerGroupKey k = TriggerGrouper.Classify(t, mode);
                    GroupHeader h;

                    if (!groups.TryGetValue(k.Id, out h))
                    {
                        h = new GroupHeader { Key = k.Id, Label = Lang.Group(k.Label), Rank = k.Rank, Sort = k.Sort };
                        groups[k.Id] = h;
                        members[k.Id] = new List<CampTrigger>();
                        order.Add(h);
                    }

                    members[k.Id].Add(t);
                    h.Count++;

                    if (bad)
                        h.Bad.Add(t);
                }

                // le trigger qu'on veut montrer (copie, suivant d'une suppression) ne doit pas être caché dans un groupe replié
                if (preferred != null)
                    _collapsed.Remove(TriggerGrouper.Classify(preferred, mode).Id);

                order = order.OrderBy(h => h.Rank).ThenBy(h => h.Sort).ThenBy(h => h.Label, StringComparer.OrdinalIgnoreCase).ToList();

                var items = new List<object>();
                var visible = new List<CampTrigger>();
                _displayOrder = order.SelectMany(h => members[h.Key]).ToList();
                HideProblemTip();

                foreach (GroupHeader h in order)
                {
                    h.Collapsed = headers && _collapsed.Contains(h.Key);

                    if (headers)
                        items.Add(h);

                    if (h.Collapsed)
                        continue;

                    foreach (CampTrigger t in members[h.Key])
                    {
                        items.Add(t);
                        visible.Add(t);
                    }
                }

                CampTrigger target = null;
                bool keepEditor = false;

                if (preferred != null && visible.Contains(preferred))
                    target = preferred;
                else if (previous != null && visible.Contains(previous))
                    target = previous;
                else if (_editing != null && visible.Contains(_editing))
                    target = _editing;
                else if (_editing != null && found.Contains(_editing))
                    keepEditor = true;                  // son groupe est replié : l'éditeur reste tel quel
                else if (visible.Count > 0)
                    target = visible[0];

                _refilling = true;

                try
                {
                    _listTriggers.BeginUpdate();
                    _listTriggers.Items.Clear();
                    _listTriggers.Items.AddRange(items.ToArray());

                    if (target != null)
                        _listTriggers.SelectedItem = target;

                    _listTriggers.EndUpdate();
                }
                finally
                {
                    _refilling = false;
                }

                _lastIndex = _listTriggers.SelectedIndex;
                _labelFilter.Text = Lang.T("Triggers   ") + found.Count + " / " + _file.Triggers.Count
                    + (withProblems > 0 ? "   \u26A0 " + withProblems : "")
                    + (searching && headers ? "   (" + order.Count + Lang.T(" block(s)") + ")" : "");

                if (!keepEditor && (target == null || !ReferenceEquals(target, _editing)))
                    ShowSelected();
            }
            finally
            {
                EndBusy();
            }
        }

        private void ShowSelected()
        {
            if (_loadingEditor || _refilling || _listTriggers.SelectedItem is GroupHeader)
                return;

            BeginBusy();

            try
            {
                CampTrigger t = _listTriggers.SelectedItem as CampTrigger;

                if (t == null)
                    _textDetail.Text = BuildOverview();

                FillEditor(t);
            }
            finally
            {
                EndBusy();
            }
        }

        // ----- liste de gauche : titres de groupe -----

        private void ListTriggers_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_refilling)
                return;

            if (_listTriggers.SelectedItem is GroupHeader)
            {
                // clic = replier / déplier ; flèches du clavier = on saute le titre. On attend la fin du message (BeginInvoke).
                BeginInvoke(new Action(SkipHeaderWithKeyboard));
                return;
            }

            if (_listTriggers.SelectedIndex >= 0)
                _lastIndex = _listTriggers.SelectedIndex;

            ShowSelected();
        }

        private void SkipHeaderWithKeyboard()
        {
            if (_headerMouse || IsDisposed)
                return;                         // c'est un clic : HandleHeaderClick s'en occupe

            int index = _listTriggers.SelectedIndex;

            if (index < 0 || !(_listTriggers.Items[index] is GroupHeader))
                return;

            int dir = index >= _lastIndex ? 1 : -1;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int j = index + dir; j >= 0 && j < _listTriggers.Items.Count; j += dir)
                {
                    if (_listTriggers.Items[j] is CampTrigger)
                    {
                        _listTriggers.SelectedIndex = j;
                        return;
                    }
                }

                dir = -dir;
            }
        }

        private void HandleHeaderClick(GroupHeader h)
        {
            if (!_headerMouse || IsDisposed)
                return;

            _headerMouse = false;

            if (!_collapsed.Remove(h.Key))
                _collapsed.Add(h.Key);

            RefillList();
        }

        // ----- onglets LIST / CODE -----

        private void ShowTab(string tab)
        {
            bool code = tab == "code";

            _split.Visible = tab == "list";
            _textCode.Visible = code;
            _panelGraph.Visible = tab == "graph";
            _buttonFind.Visible = code;

            if (!code)
            {
                _findBar.Visible = false;

                if (tab == "graph")
                    ShowGraph();

                return;
            }

            BeginBusy();

            int start = 0, length = 0;

            try
            {
                string header = _dirty ? Lang.T("-- (not saved yet: this is the text that Save changes would write)\r\n") : "";

                if (_file == null)
                    _codeText = "";
                else if (_file.LoadError != null)
                    _codeText = Lang.T("READ ERROR\r\n") + _file.LoadError;
                else
                {
                    _codeText = header + _file.ToLuaText(_editing, out start, out length);
                    start += header.Length;
                }
            }
            catch (Exception ex)
            {
                _codeText = Lang.T("Could not build the Lua text: ") + ex.Message;
                start = 0;
                length = 0;
                FormUtils.LogRegister("Triggers_Form | CODE : " + ex);
            }
            finally
            {
                EndBusy();
            }

            _textCode.Text = _codeText;

            // on arrive sur le trigger qui était choisi dans LIST : son bloc est sélectionné, son début en haut
            _textCode.SelectionLength = 0;
            _textCode.SelectionStart = Math.Min(start + length, _codeText.Length);
            _textCode.ScrollToCaret();
            _textCode.SelectionStart = Math.Min(start, _codeText.Length);
            _textCode.ScrollToCaret();
            _textCode.SelectionLength = Math.Min(length, _codeText.Length - _textCode.SelectionStart);
        }

        // ----- recherche (Ctrl+F) dans l'onglet CODE -----

        private void ShowFindBar()
        {
            if (!_textCode.Visible)
                return;

            _findBar.Visible = true;
            _textFind.Focus();
            _textFind.SelectAll();
        }

        private void HideFindBar()
        {
            _findBar.Visible = false;
            _textCode.Focus();
        }

        private int CountMatches(string term)
        {
            int count = 0, i = 0;

            while ((i = _codeText.IndexOf(term, i, StringComparison.OrdinalIgnoreCase)) >= 0 && count < 100000)
            {
                count++;
                i += Math.Max(1, term.Length);
            }

            return count;
        }

        // forward : vers la fin ; inclusive : peut retrouver le résultat déjà sélectionné (utile quand on tape).
        private void FindText(bool forward, bool inclusive)
        {
            string term = _textFind.Text;

            if (term.Length == 0 || _codeText.Length == 0)
            {
                _labelFind.Text = "";
                return;
            }

            int total = CountMatches(term);

            if (total == 0)
            {
                _labelFind.Text = Lang.T("No match");
                return;
            }

            int index;
            int caret = _textCode.SelectionStart;

            if (forward)
            {
                int from = inclusive ? caret : caret + 1;
                index = from <= _codeText.Length ? _codeText.IndexOf(term, from, StringComparison.OrdinalIgnoreCase) : -1;

                if (index < 0)
                    index = _codeText.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase);       // on repart du début
            }
            else
            {
                int from = caret + term.Length - 2;
                index = from >= 0 ? _codeText.LastIndexOf(term, Math.Min(from, _codeText.Length - 1), StringComparison.OrdinalIgnoreCase) : -1;

                if (index < 0)
                    index = _codeText.LastIndexOf(term, _codeText.Length - 1, StringComparison.OrdinalIgnoreCase);   // on repart de la fin
            }

            if (index < 0)
            {
                _labelFind.Text = Lang.T("No match");
                return;
            }

            _textCode.Select(index, term.Length);
            _textCode.ScrollToCaret();
            _labelFind.Text = total + Lang.T(" match(es)");
        }

        // Ctrl+F : recherche dans CODE, ou recherche par nom dans LIST (Ctrl+Maj+F : par fonction DCE ; F8 / Maj+F8 : problème suivant / précédent). F3 / Maj+F3 : résultat suivant / précédent. Echap : ferme la recherche.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                if (_textCode.Visible)
                {
                    ShowFindBar();
                }
                else
                {
                    _textFilter.Focus();
                    _textFilter.SelectAll();
                }

                return true;
            }

            if (!_textCode.Visible)
            {
                if (keyData == (Keys.Control | Keys.Shift | Keys.F))
                {
                    _textFunc.Focus();
                    _textFunc.SelectAll();
                    return true;
                }

                if (keyData == Keys.F8 || keyData == (Keys.Shift | Keys.F8))
                {
                    GoToProblem(keyData == Keys.F8 ? 1 : -1);
                    return true;
                }
            }

            if (_textCode.Visible && _findBar.Visible)
            {
                if (keyData == Keys.F3) { FindText(true, false); return true; }
                if (keyData == (Keys.Shift | Keys.F3)) { FindText(false, false); return true; }
                if (keyData == Keys.Escape) { HideFindBar(); return true; }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------------------------------------
        //  Édition (étape 3)
        // ------------------------------------------------------------------------------------------

        private void FillEditor(CampTrigger t)
        {
            _loadingEditor = true;

            try
            {
                _editing = t;
                _panelEdit.Enabled = t != null;

                if (t == null)
                {
                    _textName.Text = "";
                    _checkActive.Checked = false;
                    _checkOnce.Checked = false;
                    LoadExpires(null);
                    FillBlocks(null);
                    UpdateStatus(null);
                    return;
                }

                _textName.Text = t.Name;
                _checkActive.Checked = t.HasActive && t.Active;
                _checkOnce.Checked = t.Once == true;
                LoadExpires(t);

                FillBlocks(t);
                UpdateStatus(t);
            }
            finally
            {
                _loadingEditor = false;
            }
        }

        // Le même verdict que dans la liste, en clair, mis à jour après chaque modification.
        private void UpdateStatus(CampTrigger t)
        {
            if (t == null)
            {
                _labelStatus.Text = "";
                _toolTip.SetToolTip(_labelStatus, "");
                return;
            }

            int e = t.ErrorCount, w = t.WarningCount, n = t.NoteCount;

            if (e > 0)
                _labelStatus.Text = "\u2716 " + e + Lang.T(" error(s)") + (w > 0 ? ", " + w + Lang.T(" warning(s)") : "");
            else if (w > 0)
                _labelStatus.Text = "\u26A0 " + w + Lang.T(" warning(s)");
            else
                _labelStatus.Text = Lang.T("\u2714 Valid") + (n > 0 ? " (" + n + Lang.T(" note(s))") : "");

            _toolTip.SetToolTip(_labelStatus, t.Problems.Count > 0 ? string.Join("\r\n", t.Problems.Select(x => Lang.Problem(x)).ToArray()) : Lang.T("No problem found in this trigger."));
        }

        // ------------------------------------------------------------------------------------------
        //  Éditeur d'actions (étape 4)
        //  Une action reconnue = une fonction du catalogue + un champ par paramètre. On ne réécrit le texte de
        //  l'action que si on touche à l'une de ses lignes ; les paramètres non touchés gardent leur texte d'origine.
        //  Une action non reconnue reste du Lua brut, modifiable à la main (syntaxe vérifiée avant d'accepter).
        // ------------------------------------------------------------------------------------------

        // Un titre de groupe dans la liste de gauche (pas un trigger).
        private class GroupHeader
        {
            public string Key = "";
            public string Label = "";
            public int Rank;
            public double Sort;
            public int Count;
            public bool Collapsed;
            public List<CampTrigger> Bad = new List<CampTrigger>();     // les triggers du groupe qui ont un problème
            public override string ToString() { return Label; }
        }

        private class FuncItem
        {
            public FuncDef Def;                 // null = "(raw Lua)"
            public string Display = "";
            public string Short = "";
            public string Category = "";        // le thème sous lequel cette ligne apparaît (une fonction peut en avoir plusieurs)
            public override string ToString() { return Display; }
        }

        private class ChoiceItem
        {
            public string Value;                // null = paramètre absent
            public string Display = "";
            public override string ToString() { return Display; }
        }

        // L'état d'une ligne d'action : sa position, sa fonction et le texte Lua de chaque argument (null = absent).
        private class ActionRow
        {
            public int Index;
            public FuncDef Def;
            public List<string> Args = new List<string>();
            public Action<string> WriteBack;        // si non null : sert à écrire le résultat ailleurs que dans t.Actions (conditions)
        }

        private static readonly Regex PlaceholderRx = new Regex(@"\{\d+\}");

        private List<FuncItem> FuncItems()
        {
            if (_funcItems == null)
            {
                _funcItems = new List<FuncItem> { new FuncItem { Def = null, Display = Lang.T("(raw Lua)"), Short = Lang.T("Raw Lua text") } };

                // une fonction apparaît dans chacun de ses thèmes
                var pairs = new List<KeyValuePair<string, FuncDef>>();

                foreach (FuncDef f in TriggerCatalog.All.Where(x => x.Kind == "Action"))
                    foreach (string c in f.AllCategories)
                        pairs.Add(new KeyValuePair<string, FuncDef>(c, f));

                foreach (KeyValuePair<string, FuncDef> pair in pairs
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .ThenBy(x => x.Value.IsDeprecated)                  // les fonctions obsolètes en fin de thème
                    .ThenBy(x => x.Value.Sentence, StringComparer.Ordinal))
                {
                    string sentence = pair.Value.ListText(PlaceholderRx.Replace(pair.Value.Sentence, "…"));
                    _funcItems.Add(new FuncItem { Def = pair.Value, Category = pair.Key, Display = pair.Key + " : " + sentence, Short = sentence });
                }
            }

            return _funcItems;
        }

        // Menu "Add an action" : un sous-menu par thème.
        private ContextMenuStrip BuildAddMenu()
        {
            var menu = new ContextMenuStrip();

            foreach (IGrouping<string, FuncItem> group in FuncItems().Where(x => x.Def != null).GroupBy(x => x.Category))
            {
                var sub = new ToolStripMenuItem(group.Key);

                foreach (FuncItem item in group)
                {
                    FuncDef def = item.Def;
                    var entry = new ToolStripMenuItem(item.Short) { ToolTipText = def.HelpFull };

                    if (def.IsDeprecated)
                    {
                        entry.ForeColor = SystemColors.GrayText;
                        entry.Font = new Font(menu.Font, FontStyle.Italic);
                    }

                    entry.Click += (s, e) => AddAction(def);
                    sub.DropDownItems.Add(entry);
                }

                menu.Items.Add(sub);
            }

            menu.Items.Add(new ToolStripSeparator());
            var raw = new ToolStripMenuItem(Lang.T("Raw Lua text (for anything not in the lists)"));
            raw.Click += (s, e) => AddAction(null);
            menu.Items.Add(raw);

            return menu;
        }

        // Une phrase courte pour la ligne repliée : "3.  Play sound 'xxx'".
        private string ActionSummary(CampTrigger t, int index)
        {
            LuaNode node = index < t.ActionNodes.Count ? t.ActionNodes[index] : null;
            string text;

            if (node != null && node.Kind == NodeKind.Call && node.Def != null && node.Def.Kind == "Action")
            {
                text = PlaceholderRx.Replace(node.Def.Sentence, m =>
                {
                    int k = int.Parse(m.Value.Substring(1, m.Value.Length - 2), CultureInfo.InvariantCulture);

                    if (k >= node.Items.Count)
                        return Lang.T("(not set)");

                    LuaNode arg = node.Items[k];
                    return arg.Kind == NodeKind.Text ? "'" + arg.Name + "'" : arg.Source;
                });
            }
            else
            {
                text = Lang.T("[raw Lua] ") + t.Actions[index];
            }

            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (text.Length > 300)
                text = text.Substring(0, 300) + "...";

            return (index + 1) + ".  " + text;
        }

        // La liste des fonctions d'une action (la liste complète n'est mise dans la liste déroulante qu'à l'ouverture).
        private Control MakeActionHeader(CampTrigger t, ActionRow state)
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 28 };

            var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 560 };

            FuncItem current = FuncItems().FirstOrDefault(x => x.Def == state.Def) ?? FuncItems()[0];
            combo.Items.Add(current);
            combo.SelectedIndex = 0;

            bool loaded = false;
            combo.DropDown += (s, e) =>
            {
                if (loaded)
                    return;

                loaded = true;
                combo.BeginUpdate();
                combo.Items.Clear();
                combo.Items.AddRange(FuncItems().ToArray());
                combo.SelectedItem = current;
                combo.EndUpdate();
            };

            combo.SelectionChangeCommitted += (s, e) => ChangeFunction(t, state, combo.SelectedItem as FuncItem);

            if (state.Def != null)
                _toolTip.SetToolTip(combo, state.Def.HelpFull);

            header.Controls.Add(combo);

            return header;
        }

        // Une ligne = le nom du paramètre à gauche + le champ à droite.
        private Control MakeParamLine(CampTrigger t, ActionRow state, int k, ParamDef p, LuaNode arg)
        {
            int height;
            Control editor = MakeEditor(t, state, k, p, arg, out height);

            var line = new Panel { Dock = DockStyle.Top, Height = height + 4, Padding = new Padding(0, 2, 18, 2) };
            var label = new Label
            {
                Dock = DockStyle.Left,
                Width = 124,
                Text = p.Label + (p.Optional ? Lang.T(" (optional)") : ""),
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 3, 0, 0)
            };

            string hint = ParamHint(p);

            if (p.Optional)
                hint += Lang.T("\r\nOptional.") + (p.Default.Length > 0 ? Lang.T(" If empty: ") + p.Default : "");

            if (p.Note.Length > 0)
                hint += "\r\n" + p.Note;                // texte --@param écrit dans ScriptsMod

            _toolTip.SetToolTip(editor, hint);
            _toolTip.SetToolTip(label, hint);

            // Action.RestrictedLoadout : sous le nom du fichier, ce que ce .miz interdit (lu comme le fait le moteur)
            if (state.Def != null && state.Def.FullName == "Action.RestrictedLoadout" && k == 0 && _file != null && _file.Lists != null)
            {
                TriggerLists lists = _file.Lists;
                var note = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    BackColor = SystemColors.Control,
                    ForeColor = SystemColors.GrayText,
                    BorderStyle = BorderStyle.FixedSingle
                };

                Control picker = editor;
                Action refresh = () => note.Text = lists.RestrictedSummary(picker.Text);

                picker.TextChanged += (s, e) => refresh();
                refresh();

                var host = new Panel { Dock = DockStyle.Fill };
                picker.Dock = DockStyle.Top;
                host.Controls.Add(note);
                host.Controls.Add(picker);
                line.Height = height + 4 + 96;
                line.Controls.Add(host);
                line.Controls.Add(label);

                return line;
            }

            // un champ court ne s'étire pas sur toute la largeur
            int width = PreferredWidth(editor, p);

            if (width > 0)
            {
                editor.Dock = DockStyle.Left;
                editor.Width = width;
            }
            else
            {
                editor.Dock = DockStyle.Fill;
            }

            line.Controls.Add(editor);
            line.Controls.Add(label);

            return line;
        }

        // Largeur d'un champ d'après ce qu'on y met (0 = tout l'espace disponible).
        private static int PreferredWidth(Control editor, ParamDef p)
        {
            if (editor is Panel)
                return "compact".Equals(editor.Tag as string) ? editor.Width : 0;

            var box = editor as TextBox;

            if (box != null)
                return box.Multiline ? 0 : (p.Type == TrigParam.Number ? 90 : 260);

            var combo = editor as ComboBox;

            if (combo != null)
            {
                if (combo.DropDownStyle == ComboBoxStyle.DropDownList)
                    return 240;

                return p.Type == TrigParam.Flag ? 220 : 380;
            }

            return 0;
        }

        // Les flags déjà utilisés dans la campagne (en liste déroulante). Les noms viennent sans guillemets, comme on les tape.
        private List<string> FlagChoices()
        {
            List<string> cached;

            if (_namesCache.TryGetValue("flags", out cached))
                return cached;

            cached = _file == null ? new List<string>() : TriggerGrouper.KnownFlags(_file);
            _namesCache["flags"] = cached;

            return cached;
        }

        // Ce qu'on a tapé -> le texte Lua : un nombre reste un nombre (802), tout le reste devient un texte ("zoneA").
        private static string FlagLua(string typed)
        {
            double number;

            if (double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && Regex.IsMatch(typed, @"^-?\d+(\.\d+)?$"))
                return typed;

            if (typed.Length >= 2 && typed[0] == '"' && typed[typed.Length - 1] == '"')
                typed = typed.Substring(1, typed.Length - 2);       // "802" tapé avec ses guillemets = le texte 802

            return LuaText.Quote(typed);
        }

        // Ce qu'on attend dans un champ, d'après son type (affiché en infobulle).
        private static string ParamHint(ParamDef p)
        {
            switch (p.Type)
            {
                case TrigParam.TargetTitle: return Lang.T("Title of a target (titleName), chosen from the target list of the campaign.");
                case TrigParam.TargetName: return Lang.T("Name of a target (the key in targetlist_init.lua), chosen from the target list.");
                case TrigParam.AirUnit: return Lang.T("Name of an air unit (squadron), chosen from oob_air_init.lua.");
                case TrigParam.Airbase: return Lang.T("Name of an airbase.");
                case TrigParam.Number: return Lang.T("A number.");
                case TrigParam.Bool: return Lang.T("true or false.");
                case TrigParam.Flag: return Lang.T("A campaign flag: a number (802) or a name (zoneA). Pick one already used, or type a new one.");
                case TrigParam.Choice: return Lang.T("One value from the list: ") + string.Join(", ", (p.Choices ?? new string[0]).Where(c => c.Length > 0).ToArray()) + ".";
                case TrigParam.Text: return Lang.T("A text.");
                case TrigParam.Weather: return Lang.T("Tick the settings to change. Unticked settings keep their current value.");
                case TrigParam.ShipRoute: return Lang.T("The route of the ship group, as reference point names.");
                case TrigParam.ShipZone: return Lang.T("The area, as reference point names.");
                case TrigParam.ShipGroup: return Lang.T("Name of a ship group, read from base_mission.miz and oob_ground.");
                case TrigParam.Time: return Lang.T("Time since the start of the campaign, as hours and minutes. The engine currently uses the current campaign time.");
                case TrigParam.ShipSpeed: return Lang.T("Speed in metres per second (1 m/s = 1.94 knots).");
                default: return Lang.T("A Lua value, written as it is in the file.");
            }
        }

        private Control MakeEditor(CampTrigger t, ActionRow state, int k, ParamDef p, LuaNode arg, out int height)
        {
            height = 22;

            bool absent = arg == null;
            bool isText = absent || arg.Kind == NodeKind.Text;
            bool isList = p.Type == TrigParam.TargetTitle || p.Type == TrigParam.TargetName || p.Type == TrigParam.AirUnit || p.Type == TrigParam.Airbase || p.Type == TrigParam.ShipGroup;

            // ----- éditeurs dédiés (null = forme inattendue -> zone Lua brute) -----
            if (p.Type == TrigParam.Weather && isText)
            {
                Control w = MakeWeatherEditor(t, state, k, p, arg, out height);
                if (w != null) return w;
            }
            if ((p.Type == TrigParam.ShipRoute || p.Type == TrigParam.ShipZone) && (absent || arg.Kind == NodeKind.Table || (isText && arg.Name.Trim().Length == 0)))
            {
                Control w = MakeRouteEditor(t, state, k, p, arg, p.Type == TrigParam.ShipZone, NameChoices(state.Def, k, p), out height);
                if (w != null) return w;
            }
            if (p.Type == TrigParam.Time)
            {
                Control w = MakeTimeEditor(t, state, k, p, arg, out height);
                if (w != null) return w;
            }
            if (p.Type == TrigParam.ShipSpeed)
            {
                Control w = MakeSpeedEditor(t, state, k, p, arg, k == 2 ? 10m : 8m, out height);
                if (w != null) return w;
            }

            // ----- true / false -----
            if (p.Type == TrigParam.Bool && (absent || arg.Kind == NodeKind.Bool))
            {
                var cb = new CheckBox { Checked = !absent && arg.Bool };
                cb.Text = cb.Checked ? "true" : "false";
                cb.CheckedChanged += (s, e) =>
                {
                    cb.Text = cb.Checked ? "true" : "false";
                    SetArg(t, state, k, cb.Checked ? "true" : "false");
                };
                return cb;
            }

            // ----- une valeur parmi une courte liste -----
            if (p.Type == TrigParam.Choice && isText)
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

                if (p.Optional)
                    combo.Items.Add(new ChoiceItem { Value = null, Display = p.Default.Length > 0 ? Lang.T("(not set: ") + p.Default + ")" : Lang.T("(not set)") });

                foreach (string c in p.Choices.Where(x => x.Length > 0))
                    combo.Items.Add(new ChoiceItem { Value = c, Display = c });

                if (!absent)
                {
                    ChoiceItem current = combo.Items.Cast<ChoiceItem>().FirstOrDefault(x => x.Value == arg.Name);

                    if (current == null)
                    {
                        // une valeur hors liste (ou ""): on l'affiche quand même, rien n'est perdu
                        current = new ChoiceItem { Value = arg.Name, Display = arg.Name.Length == 0 ? Lang.T("(empty text)") : arg.Name + Lang.T(" (not in the list)") };
                        combo.Items.Add(current);
                    }

                    combo.SelectedItem = current;
                }
                else if (p.Optional)
                {
                    combo.SelectedIndex = 0;
                }

                combo.SelectionChangeCommitted += (s, e) =>
                {
                    var item = combo.SelectedItem as ChoiceItem;

                    if (item != null)
                        SetArg(t, state, k, item.Value == null ? null : LuaText.Quote(item.Value));
                };

                return combo;
            }

            // ----- liste de valeurs écrite dans ScriptsMod : --@param clear | source=true/false | ... -----
            string[] literal = TriggerLists.LiteralChoices(p.Source);

            if (literal != null && (absent || arg.Kind != NodeKind.Table))
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

                if (p.Optional)
                    combo.Items.Add(new ChoiceItem { Value = null, Display = p.Default.Length > 0 ? Lang.T("(not set: ") + p.Default + ")" : Lang.T("(not set)") });

                foreach (string c in literal)
                    combo.Items.Add(new ChoiceItem { Value = TriggerLists.LuaLiteral(c), Display = c });

                if (!absent)
                {
                    string written = arg.Source.Trim();
                    ChoiceItem current = combo.Items.Cast<ChoiceItem>().FirstOrDefault(x => x.Value == written);

                    if (current == null)
                    {
                        current = new ChoiceItem { Value = written, Display = written + Lang.T(" (not in the list)") };
                        combo.Items.Add(current);
                    }

                    combo.SelectedItem = current;
                }
                else if (p.Optional)
                {
                    combo.SelectedIndex = 0;
                }

                combo.SelectionChangeCommitted += (s, e) =>
                {
                    var item = combo.SelectedItem as ChoiceItem;

                    if (item != null)
                        SetArg(t, state, k, item.Value);
                };

                return combo;
            }

            // ----- flag : un numéro (802) ou un nom ("zoneA"), choisi parmi ceux déjà utilisés ou tapé -----
            if (p.Type == TrigParam.Flag && (absent || arg.Kind == NodeKind.Number || arg.Kind == NodeKind.Text))
            {
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown,
                    AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                    AutoCompleteSource = AutoCompleteSource.ListItems,
                    DropDownWidth = 320
                };

                bool filled = false;
                Action fill = () =>
                {
                    if (filled)
                        return;

                    filled = true;
                    string keepText = combo.Text;
                    combo.BeginUpdate();
                    combo.Items.AddRange(FlagChoices().ToArray());
                    combo.EndUpdate();
                    combo.Text = keepText;
                };

                combo.Text = absent ? "" : (arg.Kind == NodeKind.Number ? arg.Source.Trim() : arg.Name);
                combo.Enter += (s, e) => fill();
                combo.DropDown += (s, e) => fill();

                string last = combo.Text;

                Action<string> commit = value =>
                {
                    value = value.Trim();

                    if (value == last)
                        return;

                    if (value.Length == 0)
                    {
                        MessageBox.Show(Lang.T("A flag number or name is needed here."), "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        combo.Text = last;
                        return;
                    }

                    last = value;
                    SetArg(t, state, k, FlagLua(value));
                };

                combo.Leave += (s, e) => commit(combo.Text);
                combo.SelectionChangeCommitted += (s, e) => commit(combo.SelectedItem as string ?? combo.Text);

                return combo;
            }

            // ----- nom pris dans une liste de la campagne (cible, escadrille, base, image, template) -----
            List<string> names = isText ? NameChoices(state.Def, k, p) : null;

            if (names != null)
            {
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDown,
                    AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                    AutoCompleteSource = AutoCompleteSource.ListItems,
                    DropDownWidth = 420
                };

                // La liste n'est remplie qu'au moment où on s'en sert (beaucoup de lignes = beaucoup de listes).
                bool filled = false;
                Action fill = () =>
                {
                    if (filled)
                        return;

                    filled = true;
                    string keepText = combo.Text;

                    combo.BeginUpdate();
                    combo.Items.AddRange(names.ToArray());
                    combo.EndUpdate();
                    combo.Text = keepText;
                };

                // l'auto-complétion est lente avec une très longue liste : on la garde pour les listes raisonnables
                if (names.Count > 2500)
                    combo.AutoCompleteMode = AutoCompleteMode.None;

                combo.Text = absent ? "" : arg.Name;
                combo.Enter += (s, e) => fill();
                combo.DropDown += (s, e) => fill();

                string last = combo.Text;

                Action<string> commit = value =>
                {
                    if (value == last)
                        return;

                    last = value;
                    SetArg(t, state, k, value.Length == 0 && p.Optional ? null : LuaText.Quote(value));
                };

                combo.Leave += (s, e) => commit(combo.Text);
                combo.SelectionChangeCommitted += (s, e) => commit(combo.SelectedItem as string ?? combo.Text);

                return combo;
            }

            // ----- nombre avec bornes et unité (type=percent...) -----
            if (p.Type == TrigParam.Number && (p.Max.HasValue || p.Min.HasValue || p.Unit.Length > 0))
            {
                Control w = MakeRangeEditor(t, state, k, p, arg, out height);
                if (w != null) return w;
            }

            // ----- nombre -----
            if (p.Type == TrigParam.Number && (absent || arg.Kind == NodeKind.Number))
            {
                var box = new TextBox { Text = absent ? "" : arg.Source };
                string last = box.Text;

                box.Leave += (s, e) =>
                {
                    string value = box.Text.Trim();

                    if (value == last)
                        return;

                    if (value.Length == 0)
                    {
                        if (p.Optional)
                        {
                            last = value;
                            SetArg(t, state, k, null);
                        }
                        else
                        {
                            MessageBox.Show(Lang.T("A number is needed here."), "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            box.Text = last;
                        }

                        return;
                    }

                    double number;

                    if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    {
                        MessageBox.Show("'" + value + "' is not a number.", "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        box.Text = last;
                        return;
                    }

                    last = number.ToString("R", CultureInfo.InvariantCulture);
                    box.Text = last;
                    SetArg(t, state, k, last);
                };

                return box;
            }

            // ----- texte -----
            bool isTextParam = p.Type == TrigParam.Text || isList;

            if (isTextParam && isText)
            {
                bool multi = state.Def != null && (state.Def.Name == "Text" || state.Def.Name == "TextPlayMission") && k == 0;
                string original = absent ? "" : arg.Name;

                var box = new TextBox
                {
                    Multiline = multi,
                    ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None,
                    Text = original.Replace("\r\n", "\n").Replace("\n", "\r\n")
                };

                if (multi)
                    height = 66;

                string last = original.Replace("\r\n", "\n");

                box.Leave += (s, e) =>
                {
                    string value = box.Text.Replace("\r\n", "\n");

                    if (value == last)
                        return;

                    last = value;
                    SetArg(t, state, k, value.Length == 0 && p.Optional ? null : LuaText.Quote(value));
                };

                return box;
            }

            // ----- tout le reste (flag, table, expression...) : le texte Lua tel quel -----
            var raw = new TextBox { Font = new Font("Consolas", 9.5f), Text = absent ? "" : arg.Source };
            string lastRaw = raw.Text;

            raw.Leave += (s, e) =>
            {
                string value = raw.Text.Trim();

                if (value == lastRaw)
                    return;

                if (value.Length == 0)
                {
                    if (p.Optional)
                    {
                        lastRaw = value;
                        SetArg(t, state, k, null);
                    }
                    else
                    {
                        MessageBox.Show(Lang.T("This value cannot be empty."), "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        raw.Text = lastRaw;
                    }

                    return;
                }

                string error = TriggerChecker.CheckLuaSyntax("return " + value);

                if (error != null)
                {
                    MessageBox.Show(Lang.T("This is not valid Lua:\r\n") + error, "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    raw.Text = lastRaw;
                    return;
                }

                lastRaw = value;
                SetArg(t, state, k, value);
            };

            return raw;
        }

        // Une action qu'on ne sait pas décomposer : son texte Lua, modifiable.
        private Control MakeRawActionLine(CampTrigger t, ActionRow state)
        {
            string original = t.Actions[state.Index].Replace("\r\n", "\n");

            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9.5f),
                Text = original.Replace("\n", "\r\n")
            };

            string last = original;

            box.Leave += (s, e) =>
            {
                string value = box.Text.Replace("\r\n", "\n");

                if (value == last)
                    return;

                string error = value.Trim().Length == 0 ? null : TriggerChecker.CheckLuaSyntax(value);

                if (error != null)
                {
                    MessageBox.Show(Lang.T("This is not valid Lua, so it was not accepted:\r\n") + error, "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    box.Text = last.Replace("\n", "\r\n");
                    return;
                }

                last = value;
                t.Actions[state.Index] = value;
                MarkDirty();
                RefreshAfterEdit(t);
            };

            _toolTip.SetToolTip(box, Lang.T("Raw Lua: this action is not in the lists, so it is kept as text. Choose a function above to replace it."));

            var line = new Panel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(30, 2, 0, 2) };
            line.Controls.Add(box);

            return line;
        }

        // Liste de noms à proposer pour un paramètre (null = simple zone de texte). Les listes sont gardées en mémoire.
        private List<string> NameChoices(FuncDef def, int k, ParamDef p)
        {
            TriggerLists lists = _file != null ? _file.Lists : null;
            string key = p.Type + "|" + p.Source + "|" + (def != null && k == 0 ? def.FullName : "");

            List<string> cached;

            if (_namesCache.TryGetValue(key, out cached))
                return cached;

            IEnumerable<string> source = null;

            // la liste demandée par l'annotation --@param ... | source=... de ScriptsMod passe avant le reste
            if (!string.IsNullOrEmpty(p.Source))
            {
                source = SourceChoices(p.Source, lists);
            }
            else switch (p.Type)
            {
                case TrigParam.TargetTitle:
                    if (lists != null && (lists.HasTargets || lists.ActiveTargetTitles.Count > 0))
                        source = lists.TargetTitles.Union(lists.ActiveTargetTitles);
                    break;
                case TrigParam.TargetName:
                    if (lists != null && lists.HasTargets) source = lists.TargetNames;
                    break;
                case TrigParam.AirUnit:
                    if (lists != null && lists.HasAirUnits) source = lists.AirUnits;
                    break;
                case TrigParam.Airbase:
                    if (lists != null && lists.HasAirbases) source = lists.Airbases;
                    break;
                case TrigParam.ShipGroup:
                    if (lists != null) { lists.EnsureMissionLoaded(); if (lists.HasShipGroups) source = lists.ShipGroups; }
                    break;
                case TrigParam.ShipRoute:
                case TrigParam.ShipZone:
                    if (lists != null) { lists.EnsureMissionLoaded(); if (lists.HasZones) source = lists.Zones; }
                    break;
                default:
                    source = FileChoices(def, k);
                    break;
            }

            List<string> result = source == null || !source.Any() ? null : source.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            _namesCache[key] = result;

            return result;
        }

        // Liste demandée par "source=" : les flags viennent du fichier ouvert, le reste de TriggerLists.
        private IEnumerable<string> SourceChoices(string sourceName, TriggerLists lists)
        {
            if (sourceName.Trim().Equals("flags", StringComparison.OrdinalIgnoreCase))
                return FlagChoices();

            return lists == null ? null : lists.ListFromSource(sourceName);
        }

        // Les fichiers du dossier de la campagne qui conviennent à ce paramètre (images, templates).
        private IEnumerable<string> FileChoices(FuncDef def, int k)
        {
            if (def == null || k != 0)
                return null;

            string folder, pattern;

            switch (def.FullName)
            {
                case "Action.AddImage": folder = "Images"; pattern = "*.*"; break;
                case "Action.TemplateActive":
                case "Action.TemplateDeactivate": folder = "Templates"; pattern = "*.stm"; break;
                default: return null;
            }

            try
            {
                string dir = Path.Combine(DcemLua.CampaignPath(_campaignName), folder);

                if (!Directory.Exists(dir))
                    return null;

                return Directory.GetFiles(dir, pattern).Select(f => Path.GetFileName(f)).ToList();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ----- écriture dans le modèle -----

        // "Action.Nom(arg1, arg2)" : les arguments absents à la fin sont omis, ceux du milieu deviennent nil.
        private static string ComposeAction(FuncDef def, List<string> args)
        {
            int last = args.Count - 1;

            while (last >= 0 && args[last] == null)
                last--;

            var parts = new List<string>();

            for (int k = 0; k <= last; k++)
                parts.Add(args[k] ?? "nil");

            return def.FullName + "(" + string.Join(", ", parts) + ")";
        }

        private void SetArg(CampTrigger t, ActionRow row, int k, string lua)
        {
            if (_loadingEditor || row.Def == null)
                return;

            if (row.WriteBack == null && (row.Index < 0 || row.Index >= t.Actions.Count))
                return;

            while (row.Args.Count <= k)
                row.Args.Add(null);

            row.Args[k] = lua;

            if (row.WriteBack != null)
            {
                row.WriteBack(ComposeAction(row.Def, row.Args));
                return;
            }

            t.Actions[row.Index] = ComposeAction(row.Def, row.Args);

            MarkDirty();
            RefreshAfterEdit(t);
        }

        // Valeur de départ d'un paramètre quand on crée une action ou qu'on change de fonction.
        private static string DefaultArg(ParamDef p)
        {
            if (p.Optional)
                return null;

            switch (p.Type)
            {
                case TrigParam.Number: return "0";
                case TrigParam.Bool: return "true";
                case TrigParam.Choice: return LuaText.Quote(p.Choices.FirstOrDefault(c => c.Length > 0) ?? "");
                case TrigParam.Any: return "0";
                case TrigParam.Weather: return LuaText.Quote("weather = { }");
                case TrigParam.ShipRoute: return "{}";
                case TrigParam.ShipZone: return "{}";
                default: return "\"\"";
            }
        }

        private static List<string> DefaultArgs(FuncDef def)
        {
            return def.Params.Select(DefaultArg).ToList();
        }

        private static bool SameKind(ParamDef a, ParamDef b)
        {
            if (a.Type != b.Type)
                return false;

            return a.Type != TrigParam.Choice || (a.Choices ?? new string[0]).SequenceEqual(b.Choices ?? new string[0]);
        }

        private void ChangeFunction(CampTrigger t, ActionRow row, FuncItem item)
        {
            if (item == null || _loadingEditor || row.Index >= t.Actions.Count)
                return;

            FuncDef newDef = item.Def;

            if (newDef == row.Def)
                return;

            if (newDef == null)
            {
                // vers "(raw Lua)" : le texte reste le même, il devient simplement modifiable à la main
                ShowDetailsLater();
                return;
            }

            if (row.Def == null && t.Actions[row.Index].Trim().Length > 0)
            {
                string question = Lang.T("This replaces the raw Lua of this action with:\r\n") + item.Display + Lang.T("\r\n\r\nThe current text will be lost (Discard changes brings it back). Continue?");

                if (MessageBox.Show(question, "Triggers", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    ShowDetailsLater();      // remet la liste de fonctions sur son ancienne valeur
                    return;
                }
            }

            // On garde les valeurs déjà saisies quand le paramètre est du même genre (ex. un nom de cible).
            var args = new List<string>();

            for (int k = 0; k < newDef.Params.Count; k++)
            {
                string carry = null;

                if (row.Def != null && k < row.Def.Params.Count && k < row.Args.Count && SameKind(row.Def.Params[k], newDef.Params[k]))
                    carry = row.Args[k];

                args.Add(carry ?? DefaultArg(newDef.Params[k]));
            }

            t.Actions[row.Index] = ComposeAction(newDef, args);

            MarkDirty();
            RefreshAfterEdit(t);
            ShowDetailsLater();
        }

        private void AddAction(FuncDef def)
        {
            CampTrigger t = _editing;

            if (t == null)
                return;

            t.HasAction = true;
            t.Actions.Add(def == null ? "" : ComposeAction(def, DefaultArgs(def)));

            MarkDirty();
            RefreshAfterEdit(t);
            SelectAction(t.Actions.Count - 1);        // la nouvelle action est choisie : ses champs sont dans DETAILS
        }

        private void DeleteAction(CampTrigger t, int index)
        {
            if (index < 0 || index >= t.Actions.Count)
                return;

            string question = Lang.T("Delete this action?\r\n\r\n") + ActionSummary(t, index) + Lang.T("\r\n\r\nIt leaves the file only when you click Save changes.");

            if (MessageBox.Show(question, "Triggers", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            t.Actions.RemoveAt(index);

            MarkDirty();
            RefreshAfterEdit(t);
            SelectAction(Math.Min(index, t.Actions.Count - 1));
        }

        private void MoveAction(CampTrigger t, int index, int delta)
        {
            int other = index + delta;

            if (index < 0 || index >= t.Actions.Count || other < 0 || other >= t.Actions.Count)
                return;

            string tmp = t.Actions[index];
            t.Actions[index] = t.Actions[other];
            t.Actions[other] = tmp;

            MarkDirty();
            RefreshAfterEdit(t);
            SelectAction(other);
        }

        // Petite icône "deux feuilles" dessinée en code (pas de fichier image à ajouter au projet).
        private static Image MakeCloneIcon(Color line, Color fill)
        {
            var bmp = new Bitmap(16, 16);

            using (Graphics g = Graphics.FromImage(bmp))
            using (var pen = new Pen(line))
            using (var brush = new SolidBrush(fill))
            {
                g.Clear(Color.Transparent);
                g.DrawRectangle(pen, 2, 1, 8, 9);
                g.FillRectangle(brush, 6, 5, 8, 9);
                g.DrawRectangle(pen, 6, 5, 8, 9);
            }

            return bmp;
        }

        // ----- liste de gauche : dessin des lignes (avec l'icône de clonage à droite) -----

        private void ListTriggers_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _listTriggers.Items.Count)
                return;

            var header = _listTriggers.Items[e.Index] as GroupHeader;

            if (header != null)
            {
                using (var brush = new SolidBrush(SystemColors.ControlLight))
                    e.Graphics.FillRectangle(brush, e.Bounds);

                string title = (header.Collapsed ? "\u25B8 " : "\u25BE ") + header.Label;
                int badWidth = header.Bad.Count > 0 ? 50 : 0;
                var titleRect = new Rectangle(e.Bounds.X + 2, e.Bounds.Y, Math.Max(10, e.Bounds.Width - 44 - badWidth), e.Bounds.Height);

                using (var bold = new Font(_listTriggers.Font, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, title, bold, titleRect, SystemColors.ControlText,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                if (header.Bad.Count > 0)
                {
                    // le bloc contient des triggers en défaut : visible même quand il est replié
                    var badRect = new Rectangle(e.Bounds.Right - 42 - badWidth, e.Bounds.Y, badWidth, e.Bounds.Height);

                    using (var bold2 = new Font(_listTriggers.Font, FontStyle.Bold))
                        TextRenderer.DrawText(e.Graphics, "\u26A0 " + header.Bad.Count.ToString(CultureInfo.InvariantCulture), bold2, badRect, SystemColors.ControlText,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                var countRect = new Rectangle(e.Bounds.Right - 42, e.Bounds.Y, 38, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, header.Count.ToString(CultureInfo.InvariantCulture), _listTriggers.Font, countRect, SystemColors.GrayText,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                return;
            }

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back = selected ? SystemColors.Highlight : _listTriggers.BackColor;
            Color fore = selected ? SystemColors.HighlightText : _listTriggers.ForeColor;

            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);

            var triggerRow = _listTriggers.Items[e.Index] as CampTrigger;

            if (triggerRow != null && triggerRow.ErrorCount + triggerRow.WarningCount > 0)
            {
                // le trigger a un problème : \u26A0 devant la ligne (le nom peut être coupé à droite, pas ici)
                using (var bold = new Font(_listTriggers.Font, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, "\u26A0", bold, new Rectangle(e.Bounds.X + 1, e.Bounds.Y, 17, e.Bounds.Height), fore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            var textRect = new Rectangle(e.Bounds.X + 18, e.Bounds.Y, Math.Max(10, e.Bounds.Width - CloneZone - DeleteZone - 18), e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics, _listTriggers.Items[e.Index].ToString(), _listTriggers.Font, textRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            e.Graphics.DrawImage(selected ? _iconCloneSelected : _iconClone,
                e.Bounds.Right - CloneZone + 5, e.Bounds.Y + (e.Bounds.Height - 16) / 2);

            e.Graphics.DrawImage(selected ? _iconDeleteSelected : _iconDelete,
                e.Bounds.Right - CloneZone - DeleteZone + 4, e.Bounds.Y + (e.Bounds.Height - 16) / 2);
        }

        private bool IsCloneZone(Point p)
        {
            return p.X >= _listTriggers.ClientSize.Width - CloneZone;
        }

        private bool IsDeleteZone(Point p)
        {
            int right = _listTriggers.ClientSize.Width - CloneZone;
            return p.X >= right - DeleteZone && p.X < right;
        }

        // Petite poubelle dessinée en code.
        private static Image MakeDeleteIcon(Color line)
        {
            var bmp = new Bitmap(16, 16);

            using (Graphics g = Graphics.FromImage(bmp))
            using (var pen = new Pen(line))
            {
                g.Clear(Color.Transparent);
                g.DrawLine(pen, 2, 3, 13, 3);           // le couvercle
                g.DrawRectangle(pen, 6, 1, 3, 2);       // sa poignée
                g.DrawLine(pen, 3, 5, 4, 14);           // le corps
                g.DrawLine(pen, 12, 5, 11, 14);
                g.DrawLine(pen, 4, 14, 11, 14);
                g.DrawLine(pen, 6, 6, 6, 12);           // les rainures
                g.DrawLine(pen, 9, 6, 9, 12);
            }

            return bmp;
        }

        private void ListTriggers_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            int index = _listTriggers.IndexFromPoint(e.Location);

            if (index < 0)
                return;

            var header = _listTriggers.Items[index] as GroupHeader;

            if (header != null)
            {
                _headerMouse = true;
                BeginInvoke(new Action(() => HandleHeaderClick(header)));
                return;
            }

            var t = _listTriggers.Items[index] as CampTrigger;

            if (t == null)
                return;

            if (IsCloneZone(e.Location))
            {
                BeginInvoke(new Action(() => CloneTrigger(t)));
            }
            else if (IsDeleteZone(e.Location))
            {
                // on choisit d'abord la ligne : DeleteTrigger travaille sur le trigger affiché
                _listTriggers.SelectedItem = t;
                BeginInvoke(new Action(DeleteTrigger));
            }
        }

        private void ListTriggers_MouseMove(object sender, MouseEventArgs e)
        {
            int index = _listTriggers.IndexFromPoint(e.Location);
            bool isTrigger = index >= 0 && _listTriggers.Items[index] is CampTrigger;

            int over = isTrigger && IsCloneZone(e.Location) ? 1 : isTrigger && IsDeleteZone(e.Location) ? 2 : 0;
            SetOverClone(over);

            if (over != 0 || index < 0)
            {
                HideProblemTip();
                return;
            }

            object item = _listTriggers.Items[index];

            if (ReferenceEquals(item, _tipKey))
                return;

            string tip = ProblemTip(item);

            if (tip.Length == 0)
            {
                HideProblemTip();
                return;
            }

            _tipKey = item;
            _toolTip.Show(tip, _listTriggers, e.X + 16, e.Y + 20, 12000);
        }

        private void HideProblemTip()
        {
            if (_tipKey == null)
                return;

            _tipKey = null;
            _toolTip.Hide(_listTriggers);
        }

        // Le texte de l'infobulle d'une ligne : pour un trigger ses problèmes, pour un groupe les noms de ses triggers en défaut.
        private static string ProblemTip(object item)
        {
            var t = item as CampTrigger;

            if (t != null)
            {
                if (t.Problems.Count == 0 || t.ErrorCount + t.WarningCount == 0)
                    return "";

                return string.Join("\r\n", t.Problems.Where(p => !p.StartsWith(TriggerChecker.NoteTag, StringComparison.Ordinal)).Take(8).Select(x => Lang.Problem(x)).ToArray());
            }

            var h = item as GroupHeader;

            if (h == null || h.Bad.Count == 0)
                return "";

            var sb = new StringBuilder();
            sb.Append(h.Bad.Count).Append(Lang.T(" trigger(s) with a problem in this block:"));

            foreach (CampTrigger b in h.Bad.Take(15))
                sb.Append("\r\n  \u26A0 ").Append(b.Name);

            if (h.Bad.Count > 15)
                sb.Append("\r\n  ... +").Append(h.Bad.Count - 15);

            return sb.ToString();
        }

        // F8 / Maj+F8 et les deux boutons : va au trigger suivant / précédent qui a un problème (déplie son groupe).
        private void GoToProblem(int direction)
        {
            if (_file == null || _displayOrder.Count == 0)
                return;

            // d'abord les actions fautives du trigger affiché, ensuite le trigger suivant
            if (GoToBadAction(direction, false))
                return;

            int n = _displayOrder.Count;
            int start = _editing != null ? _displayOrder.IndexOf(_editing) : -1;

            for (int step = 1; step <= n; step++)
            {
                int i = start < 0 ? (direction > 0 ? step - 1 : n - step) : (((start + direction * step) % n) + n) % n;
                CampTrigger t = _displayOrder[i];

                if (ReferenceEquals(t, _editing) || t.ErrorCount + t.WarningCount == 0)
                    continue;

                RefillList(t);

                if (ReferenceEquals(_editing, t) && t.ActionProblems.Count > 0)
                {
                    _listThen.SelectedIndex = direction > 0 ? t.ActionProblems.Keys.Min() : t.ActionProblems.Keys.Max();
                    _listThen.Focus();
                }
                else
                    _listTriggers.Focus();

                return;
            }

            _labelFilter.Text = Lang.T("No other trigger with a problem");
        }

        private bool PassesSearch(CampTrigger t)
        {
            string a = _textFilter.Text.Trim();
            string b = _textFunc.Text.Trim();
            return (a.Length == 0 || t.MatchesName(a)) && (b.Length == 0 || t.MatchesFunction(b));
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        // Texte gris dans les champs vides (indication) + liste de complétion des fonctions DCE.
        private void UpdateCues()
        {
            if (_textFilter.IsHandleCreated)
                SendMessage(_textFilter.Handle, 0x1501, (IntPtr)1, Lang.T("Search a trigger by name..."));

            if (_textFunc.IsHandleCreated)
                SendMessage(_textFunc.Handle, 0x1703, IntPtr.Zero, Lang.T("Search a DCE function (type or pick in the list)"));

            int count = TriggerCatalog.All.Count();

            if (count != _funcSourceCount)
            {
                _funcSourceCount = count;
                string typed = _textFunc.Text;

                _textFunc.BeginUpdate();
                _textFunc.Items.Clear();

                foreach (FuncDef f in TriggerCatalog.All.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                    _textFunc.Items.Add(f.FullName);

                _textFunc.EndUpdate();
                _textFunc.Text = typed;
            }
        }

        private void SetOverClone(int over)
        {
            if (over == _overClone)
                return;

            _overClone = over;
            _listTriggers.Cursor = over != 0 ? Cursors.Hand : Cursors.Default;
            _toolTip.SetToolTip(_listTriggers, over == 1 ? Lang.T("Clone this trigger (a copy is added just after it, with a new name)")
                : over == 2 ? Lang.T("Delete this trigger (it leaves the file only when you click Save changes)") : "");
        }

        // Supprime le trigger choisi (confirmation). Le fichier n'est touché qu'au "Save changes".
        private void DeleteTrigger()
        {
            CampTrigger t = _editing;

            if (t == null || _file == null)
                return;

            string question = Lang.T("Delete the trigger '") + t.Name + "' (" + t.Actions.Count + " action(s))?\r\n\r\n"
                + Lang.T("It leaves the file only when you click Save changes. Discard changes brings it back.");

            if (MessageBox.Show(question, Lang.T("Triggers - delete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            int pos = _file.Triggers.IndexOf(t);
            _file.Triggers.Remove(t);

            MarkDirty();

            BeginBusy();

            try { _file.Recheck(); }
            finally { EndBusy(); }

            UpdateSummary();

            CampTrigger next = null;

            if (_file.Triggers.Count > 0)
                next = _file.Triggers[Math.Min(pos, _file.Triggers.Count - 1)];

            _editing = null;        // force l'affichage du suivant
            RefillList(next);
        }

        // Copie le trigger choisi juste après lui, avec un nom libre ("... - copy", "... - copy 2"...).
        // Un trigger neuf : toujours vrai, sans action. On le place après le trigger choisi (ou à la fin).
        private void NewTrigger()
        {
            if (_file == null || _file.LoadError != null)
                return;

            string name = Lang.T("New trigger");
            int n = 2;

            while (_file.Triggers.Any(x => x.Name == name))
                name = Lang.T("New trigger ") + n++;

            var t = new CampTrigger
            {
                Name = name,
                HasActive = true,
                Active = true,
                HasCondition = true,
                Condition = "true",
                HasAction = true
            };

            int at = _editing != null ? _file.Triggers.IndexOf(_editing) : -1;
            _file.Triggers.Insert(at >= 0 ? at + 1 : _file.Triggers.Count, t);

            MarkDirty();

            BeginBusy();

            try { _file.Recheck(); }
            finally { EndBusy(); }

            UpdateSummary();

            if (!PassesSearch(t))
                {
                    _textFilter.Text = "";
                    _textFunc.Text = "";
                }

            if (_checkOnlyProblems.Checked && t.ErrorCount + t.WarningCount == 0)
                _checkOnlyProblems.Checked = false;

            RefillList(t);

            _textName.Focus();
            _textName.SelectAll();                  // on tape le nom tout de suite
        }

        private void CloneTrigger(CampTrigger source)
        {
            if (source == null || _file == null)
                return;

            string baseName = source.Name + Lang.T(" - copy");
            string name = baseName;
            int n = 2;

            while (_file.Triggers.Any(x => x.Name == name))
                name = baseName + " " + n++;

            string ask = Lang.T("Clone the trigger '") + source.Name + Lang.T("'?\r\n\r\nA copy called '") + name + Lang.T("' is added just after it (you can rename it right away).\r\nIt is written to the file only when you click Save changes.");

            if (MessageBox.Show(ask, Lang.T("Triggers - clone"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            CampTrigger copy = source.Clone(name);
            _file.Triggers.Insert(_file.Triggers.IndexOf(source) + 1, copy);

            MarkDirty();

            BeginBusy();

            try { _file.Recheck(); }
            finally { EndBusy(); }

            UpdateSummary();

            // le filtre pourrait cacher la copie : on l'enlève dans ce cas
            if (!PassesSearch(copy))
                {
                    _textFilter.Text = "";
                    _textFunc.Text = "";
                }

            if (_checkOnlyProblems.Checked && copy.ErrorCount + copy.WarningCount == 0)
                _checkOnlyProblems.Checked = false;

            RefillList(copy);

            _textName.Focus();
            _textName.SelectAll();      // on peut taper le nouveau nom tout de suite
        }

        private void MarkDirty()
        {
            _dirty = true;
            _buttonSave.Enabled = true;
            _buttonDiscard.Enabled = true;
            Text = "Triggers * - " + _campaignName;
        }

        private void ClearDirty()
        {
            _dirty = false;
            _buttonSave.Enabled = false;
            _buttonDiscard.Enabled = false;
            Text = "Triggers - " + _campaignName;
        }

        // Après un changement : met à jour la ligne de la liste (nom, coche) et le détail.
        private void RefreshAfterEdit(CampTrigger t)
        {
            BeginBusy();

            try { _file.Recheck(); }        // les remarques sont refaites avec les nouvelles valeurs
            finally { EndBusy(); }

            UpdateSummary();

            _loadingEditor = true;

            try
            {
                int index = _listTriggers.Items.IndexOf(t);

                if (index >= 0)
                    _listTriggers.Items[index] = t;     // force le rafraîchissement du texte de la ligne (réécrit aussi la sélection)

                if (index >= 0 && _listTriggers.SelectedIndex != index)
                    _listTriggers.SelectedIndex = index;

                _textInfo.Text = BuildDetail(t);
                _listThen.Invalidate();
                UpdateThenBad();
                UpdateThenLua();
                RefreshPlain();
                UpdateStatus(t);
            }
            finally
            {
                _loadingEditor = false;
            }
        }

        private void CommitName()
        {
            CampTrigger t = _editing;

            if (_loadingEditor || t == null)
                return;

            string name = _textName.Text.Trim();

            if (name == t.Name)
                return;

            string problem = null;

            if (name.Length == 0)
                problem = Lang.T("The name cannot be empty.");
            else if (_file.Triggers.Any(x => !ReferenceEquals(x, t) && x.Name == name))
                problem = Lang.T("Another trigger is already called '") + name + Lang.T("'. Names must be unique.");

            if (problem != null)
            {
                MessageBox.Show(problem, "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _textName.Text = t.Name;
                return;
            }

            t.Name = name;
            t.NameGenerated = false;
            MarkDirty();
            RefreshAfterEdit(t);
        }

        // ----- expires : "Active until" -----
        private static readonly Regex ExpiresPartRx = new Regex(@"\b(year|month|day)\s*=\s*(\d+)");
        private static readonly Regex ConditionDateRx = new Regex(@"Return\.Date(?:Passed|Before)\(\s*(\d{4})\s*,\s*(\d{1,2})\s*,\s*(\d{1,2})");

        // Lit expires du trigger (table ou ancien texte) dans la case et le calendrier. Une valeur étrangère reste intacte, non modifiable.
        private void LoadExpires(CampTrigger t)
        {
            _checkExpires.Enabled = true;
            _labelExpires.Text = Lang.T("(included) then switched off");

            if (t == null || t.ExpiresText == null)
            {
                // pas d'expires : ni date ni texte. La date proposée quand on coche = celle de la condition du trigger si elle en a une
                Match m = t != null ? ConditionDateRx.Match(t.Condition ?? "") : Match.Empty;
                DateTime proposed = new DateTime(1970, 1, 1);

                if (m.Success)
                    TryMakeDate(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), ref proposed);

                _proposedExpires = proposed;
                _checkExpires.Checked = false;
                ShowExpiresControls(false);
                return;
            }

            int year = 0, month = 0, day = 0;

            foreach (Match part in ExpiresPartRx.Matches(t.ExpiresText))
            {
                int v = int.Parse(part.Groups[2].Value);

                switch (part.Groups[1].Value)
                {
                    case "year": year = v; break;
                    case "month": month = v; break;
                    case "day": day = v; break;
                }
            }

            DateTime date = new DateTime(1970, 1, 1);

            if (!TryMakeDate(year, month, day, ref date))
            {
                // pas une date lisible (variable, valeur impossible...) : on ne touche à rien
                _checkExpires.Checked = true;
                _checkExpires.Enabled = false;
                _dateExpires.Visible = false;
                _labelExpires.Visible = true;
                _labelExpires.Text = Lang.T("kept as it is: ") + Regex.Replace(t.ExpiresText, @"\s+", " ");
                return;
            }

            _dateExpires.Value = date;
            _checkExpires.Checked = true;
            ShowExpiresControls(true);
        }

        private void ShowExpiresControls(bool on)
        {
            _dateExpires.Visible = on;
            _labelExpires.Visible = on;
            _dateExpires.Enabled = on;
        }

        private static bool TryMakeDate(int year, int month, int day, ref DateTime date)
        {
            if (year < 1753 || year > 9998 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
                return false;

            date = new DateTime(year, month, day);
            return true;
        }

        // Écrit expires = { year = .., month = .., day = .. } (ou le retire quand la case est décochée).
        private void CommitExpires()
        {
            CampTrigger t = _editing;

            if (_loadingEditor || t == null || !_checkExpires.Enabled)
                return;

            DateTime d = _dateExpires.Value;
            string text = _checkExpires.Checked
                ? "{ year = " + d.Year.ToString(CultureInfo.InvariantCulture) + ", month = " + d.Month.ToString(CultureInfo.InvariantCulture) + ", day = " + d.Day.ToString(CultureInfo.InvariantCulture) + " }"
                : null;

            if (text == t.ExpiresText)
                return;

            t.ExpiresText = text;
            t.Problems.RemoveAll(p => p.Contains("'expires'"));       // les remarques sur l'ancienne valeur ne valent plus
            MarkDirty();
            RefreshAfterEdit(t);
        }

        private void CommitFlags()
        {
            CampTrigger t = _editing;

            if (_loadingEditor || t == null)
                return;

            bool changed = false;

            if (_checkActive.Checked != (t.HasActive && t.Active))
            {
                t.HasActive = true;
                t.Active = _checkActive.Checked;
                changed = true;
            }

            if (_checkOnce.Checked != (t.Once == true))
            {
                if (_checkOnce.Checked)
                    t.Once = true;
                else if (t.Once != null)
                    t.Once = false;     // le champ existait : on le garde, à false

                changed = true;
            }

            if (!changed)
                return;

            MarkDirty();
            RefreshAfterEdit(t);
        }

        private void ButtonSave_Click(object sender, EventArgs e)
        {
            string reason;

            if (!_file.CanSave(out reason))
            {
                MessageBox.Show(reason, Lang.T("Triggers - cannot save"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_confirmedThisSession)
            {
                string text = Lang.T("This writes ") + _file.FilePath + Lang.T(" again, as a list.\r\n\r\n")
                    + Lang.T("Kept as they are: names, active, once, conditions, actions, expires and unknown keys.\r\n")
                    + Lang.T("Not kept: comments and any code outside the camp_triggers table")
                    + (_file.CommentLineCount > 0 ? Lang.T(" (about ") + _file.CommentLineCount + Lang.T(" line(s) with comments here)") : "") + ".\r\n\r\n"
                    + Lang.T("Backups: .bak (the very first original, never overwritten) and .prev (the version before this save).\r\n\r\n")
                    + Lang.T("Tip: test on a clone of the campaign first.\r\n\r\nContinue?");

                if (MessageBox.Show(text, Lang.T("Triggers - save"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                _confirmedThisSession = true;
            }

            string error;

            if (!_file.Save(out error))
            {
                MessageBox.Show(error, Lang.T("Triggers - not saved"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string keep = _editing != null ? _editing.Name : null;

            ClearDirty();
            LoadTriggers();          // relit le fichier écrit : tout est revérifié depuis le disque
            SelectByName(keep);

            _labelNotice.Text = Lang.T("Saved at ") + DateTime.Now.ToString("HH:mm:ss") + Lang.T(". Backups: camp_triggers_init.lua.bak (first original) and .prev.")
                + (_file.ActiveCopyExists ? Lang.T(" Active\\camp_triggers.lua is not changed: the new triggers apply after a restart (First Mission).") : "");
        }

        private void ButtonDiscard_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Lang.T("Throw away the changes made since the last save?"), "Triggers", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            string keep = _editing != null ? _editing.Name : null;

            ClearDirty();
            LoadTriggers();
            SelectByName(keep);
        }

        private void SelectByName(string name)
        {
            if (name == null)
                return;

            for (int i = 0; i < _listTriggers.Items.Count; i++)
            {
                var t = _listTriggers.Items[i] as CampTrigger;

                if (t != null && t.Name == name)
                {
                    _listTriggers.SelectedIndex = i;
                    return;
                }
            }
        }

        // Ordre d'affichage des remarques : erreurs, puis avertissements, puis notes.
        private static int ProblemRank(string line)
        {
            if (line.Contains(TriggerChecker.ErrorTag)) return 0;
            if (line.Contains(TriggerChecker.WarnTag)) return 1;
            return 2;
        }

        // Texte affiché quand aucun trigger n'est sélectionné : erreur de lecture, ou toutes les remarques du fichier.
        private string BuildOverview()
        {
            var sb = new StringBuilder();

            if (_file.LoadError != null)
            {
                sb.AppendLine("READ ERROR");
                sb.AppendLine(_file.LoadError);
                return sb.ToString();
            }

            sb.AppendLine(Lang.T("File   : ") + _file.FilePath);
            sb.AppendLine(Lang.T("Format : ") + FormatName());

            if (_file.Lists != null)
            {
                sb.AppendLine(Lang.T("Names checked against : ")
                    + (_file.Lists.HasTargets ? "targetlist_init.lua  " : "")
                    + (_file.Lists.HasAirUnits ? "oob_air_init.lua  " : "")
                    + (_file.Lists.HasAirbases ? "db_airbases.lua" : ""));
            }

            sb.AppendLine();

            List<string> problems = _file.AllProblems(true).OrderBy(ProblemRank).ToList();

            if (problems.Count == 0)
            {
                sb.AppendLine(Lang.T("No problem found in this file."));
            }
            else
            {
                sb.AppendLine(Lang.T("ERROR = the engine fails or ignores it.  Warning = suspect.  Note = information."));
                sb.AppendLine();
                sb.AppendLine(Lang.T("Problems (") + problems.Count + ") :");

                foreach (string p in problems)
                    sb.AppendLine("  - " + Lang.Problem(p));
            }

            sb.AppendLine();
            sb.AppendLine(Lang.T("Select a trigger on the left to see it."));

            return sb.ToString();
        }

        private static string Indent(string text, int spaces)
        {
            string pad = new string(' ', spaces);
            return pad + text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine + pad);
        }

        // Le texte Lua sur une seule ligne (pour la ligne "Lua:" sous une phrase).
        private static string OneLine(string text)
        {
            return string.Join(" ", text.Replace("\r", " ").Split(new[] { '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
        }

        private static string BuildDetail(CampTrigger t)
        {
            var sb = new StringBuilder();

            sb.AppendLine(Lang.T("Name   : ") + t.Name);

            sb.AppendLine(Lang.T("Active : ") + (!t.HasActive ? Lang.T("no (field missing)") : Lang.T(t.Active ? "yes" : "no")));

            if (t.Once == null)
                sb.AppendLine(Lang.T("Once   : ") + Lang.T("not set (plays again at every mission)"));
            else
                sb.AppendLine(Lang.T("Once   : ") + Lang.T(t.Once.Value ? "yes (plays one time only)" : "no (plays again at every mission)"));

            sb.AppendLine();

            // ----- CONDITIONS -----
            sb.AppendLine("CONDITIONS");

            if (!t.HasCondition)
            {
                sb.AppendLine(Lang.T("    (none)"));
            }
            else if (t.ConditionIsRaw)
            {
                sb.AppendLine(Lang.T("    [raw Lua - not split into fields]"));
                sb.AppendLine(Indent(t.Condition, 6));
            }
            else
            {
                sb.AppendLine(Indent(TriggerText.DescribeCondition(t.ConditionNode), 4));
                sb.AppendLine("      Lua: " + OneLine(t.Condition));
            }

            sb.AppendLine();

            // ----- ACTIONS -----
            sb.AppendLine("ACTIONS (" + t.Actions.Count + ")" + (t.ActionWasSingleString ? Lang.T("   [written as a single text, not a list]") : ""));

            for (int i = 0; i < t.Actions.Count; i++)
            {
                if (t.ActionIsRaw(i))
                {
                    sb.AppendLine("  " + (i + 1) + Lang.T(". [raw Lua - not split into fields]"));
                    sb.AppendLine(Indent(t.Actions[i], 7));
                }
                else
                {
                    sb.AppendLine("  " + (i + 1) + ". " + TriggerText.Describe(t.ActionNodes[i]));
                    sb.AppendLine("       Lua: " + OneLine(t.Actions[i]));
                }
            }

            if (t.ExpiresText != null)
            {
                sb.AppendLine();
                sb.AppendLine(Lang.T("EXPIRES (kept as is)"));
                sb.AppendLine(Indent(t.ExpiresText, 4));
            }

            if (t.OtherKeys.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Lang.T("OTHER KEYS (unknown, kept as is)"));

                foreach (var kv in t.OtherKeys)
                    sb.AppendLine("    " + kv.Key + " = " + kv.Value);
            }

            if (t.Problems.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Lang.T("PROBLEMS"));

                foreach (string p in t.Problems.OrderBy(ProblemRank))
                    sb.AppendLine("  - " + Lang.Problem(p));
            }

            return sb.ToString();
        }

        // Outil de test : lit les triggers de TOUTES les campagnes (aucune écriture) et affiche le compte-rendu
        // dans la zone de droite (texte sélectionnable : Ctrl+A puis Ctrl+C pour le copier).
        // En bas : les formes de texte non décomposées et les remarques les plus fréquentes, pour savoir
        // quoi ajouter au catalogue ou quoi corriger.
        private void ButtonScanAll_Click(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            var rawShapes = new Dictionary<string, ShapeStat>();
            var problemShapes = new Dictionary<string, ShapeStat>();
            int campaigns = 0, readErrors = 0, triggers = 0, raw = 0, errors = 0, warnings = 0, notes = 0;
            var noteShapes = new Dictionary<string, ShapeStat>();

            Cursor = Cursors.WaitCursor;

            try
            {
                string root = DcemLua.CampaignsPath;

                if (!Directory.Exists(root))
                {
                    MessageBox.Show(Lang.T("Campaigns folder not found:\r\n") + root, "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                foreach (string dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileName(dir);

                    if (!File.Exists(DcemLua.CampaignInitFile(name, "camp_triggers_init.lua")))
                        continue; // pas une campagne complète : la grille la signale déjà

                    campaigns++;

                    CampTriggersFile f = CampTriggersFile.Load(name);

                    if (f.LoadError != null)
                    {
                        readErrors++;
                        sb.AppendLine("READ ERROR  " + name + "  ->  " + f.LoadError);
                        continue;
                    }

                    triggers += f.Triggers.Count;
                    raw += f.RawStringCount;
                    errors += f.ErrorCount;
                    warnings += f.WarningCount;
                    notes += f.NoteCount;

                    sb.AppendLine(
                        (f.ErrorCount > 0 ? "ERROR " : f.WarningCount > 0 ? "WARN  " : f.NoteCount > 0 ? "note  " : "ok    ")
                        + name.PadRight(45)
                        + f.Triggers.Count.ToString().PadLeft(4) + " trigger(s)  "
                        + f.RawStringCount.ToString().PadLeft(4) + " raw  "
                        + f.ErrorCount.ToString().PadLeft(4) + " error(s)  "
                        + f.WarningCount.ToString().PadLeft(4) + Lang.T(" warning(s)")
                        + f.NoteCount.ToString().PadLeft(4) + " note(s)");

                    // le détail de chaque problème de cette campagne (30 lignes au maximum)
                    List<string> details = f.AllProblems(true);

                    foreach (string line in details.Take(30))
                        sb.AppendLine("          - " + line);

                    if (details.Count > 30)
                        sb.AppendLine("          ... " + (details.Count - 30) + " more");

                    foreach (CampTrigger t in f.Triggers)
                    {
                        if (t.ConditionIsRaw)
                            Count(rawShapes, "CONDITION " + TriggerText.Shape(t.Condition), name, "'" + t.Name + "'");

                        for (int i = 0; i < t.Actions.Count; i++)
                        {
                            if (t.Actions[i].Trim().Length > 0 && t.ActionIsRaw(i))
                                Count(rawShapes, "ACTION " + TriggerText.Shape(t.Actions[i]), name, "'" + t.Name + "'");
                        }

                        foreach (string p in t.Problems)
                        {
                            if (p.StartsWith(TriggerChecker.NoteTag, StringComparison.Ordinal))
                                Count(noteShapes, TriggerText.Shape(p), name, "'" + t.Name + "' : " + p);
                            else
                                Count(problemShapes, TriggerText.Shape(p), name, "'" + t.Name + "' : " + p);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.ErrorGeneral_BoxOrLog(ex, "Scan all campaigns", "", true, true);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            var report = new StringBuilder();

            report.AppendLine("SCAN: " + campaigns + " campaign(s), " + readErrors + " read error(s), " + triggers + " trigger(s), "
                + errors + Lang.T(" error(s), ") + warnings + " warning(s), " + notes + " note(s), " + raw + " raw text(s)");
            report.AppendLine("The lists at the end count every occurrence in all campaigns. Cloned campaigns share the same triggers, so the same problem is counted once per clone.");
            report.AppendLine();
            report.Append(sb);
            report.AppendLine();
            report.AppendLine("MOST FREQUENT RAW TEXTS (S = a text, N = a number)");

            AppendShapes(report, rawShapes);

            report.AppendLine();
            report.AppendLine("MOST FREQUENT ERRORS AND WARNINGS");

            AppendShapes(report, problemShapes);

            report.AppendLine();
            report.AppendLine("MOST FREQUENT NOTES");

            AppendShapes(report, noteShapes);

            _listTriggers.ClearSelected(); // affiche d'abord la vue d'ensemble, qu'on écrase juste après
            _textDetail.Text = report.ToString();
        }

        // Pour chaque "forme" de texte ou de remarque : combien de fois, dans quelles campagnes, et un exemple.
        private class ShapeStat
        {
            public int Total;
            public string Example = "";
            public Dictionary<string, int> Campaigns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private static void Count(Dictionary<string, ShapeStat> shapes, string key, string campaign, string example)
        {
            ShapeStat stat;

            if (!shapes.TryGetValue(key, out stat))
            {
                stat = new ShapeStat { Example = campaign + " > " + example };
                shapes[key] = stat;
            }

            stat.Total++;

            int n;
            stat.Campaigns.TryGetValue(campaign, out n);
            stat.Campaigns[campaign] = n + 1;
        }

        private static void AppendShapes(StringBuilder report, Dictionary<string, ShapeStat> shapes)
        {
            foreach (KeyValuePair<string, ShapeStat> kv in shapes.OrderByDescending(x => x.Value.Total).Take(40))
            {
                ShapeStat stat = kv.Value;

                report.AppendLine(stat.Total.ToString().PadLeft(5) + "  " + kv.Key + "   [" + stat.Campaigns.Count + " campaign(s)]");

                List<string> names = stat.Campaigns
                    .OrderByDescending(c => c.Value)
                    .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(30)
                    .Select(c => c.Key + " (" + c.Value + ")")
                    .ToList();

                report.AppendLine("         in: " + string.Join(", ", names) + (stat.Campaigns.Count > 30 ? ", ... +" + (stat.Campaigns.Count - 30) + " more" : ""));
                report.AppendLine("         e.g. " + stat.Example);
            }
        }
    }
}
