using System;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // ÉTAPE 5 - La vue "blocs" d'un trigger : IF (conditions en arbre ALL / ANY / NOT) puis THEN (actions),
    // avec un panneau DETAILS à droite pour modifier le bloc choisi.
    // Même principe que les actions : on ne réécrit le texte Lua de la condition que si on la modifie.
    // Une condition qu'on ne sait pas lire reste du texte Lua, modifiable à la main.

    public enum CondKind { Leaf, And, Or, Not }

    // Un morceau de condition : une condition simple (Leaf, son texte Lua) ou un groupe (And / Or / Not).
    public class CondNode
    {
        public CondKind Kind;
        public string Lua = "";                         // Leaf : le texte Lua de la condition simple
        public List<CondNode> Children = new List<CondNode>();
        public CondNode Parent;

        public static CondNode NewLeaf(string lua)
        {
            return new CondNode { Kind = CondKind.Leaf, Lua = lua };
        }

        public static CondNode NewGroup(CondKind kind, params CondNode[] kids)
        {
            var g = new CondNode { Kind = kind };

            foreach (CondNode k in kids)
                g.Adopt(k);

            return g;
        }

        public void Adopt(CondNode child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public void FixParents()
        {
            foreach (CondNode c in Children)
            {
                c.Parent = this;
                c.FixParents();
            }
        }

        // L'arbre à partir de la lecture d'une condition. Les "and" / "or" enchaînés sont mis à plat.
        public static CondNode FromLua(LuaNode n)
        {
            while (n.Kind == NodeKind.Paren && n.Items.Count == 1)
                n = n.Items[0];

            if (n.Kind == NodeKind.Binary && (n.Name == "and" || n.Name == "or") && n.Items.Count == 2)
            {
                CondKind kind = n.Name == "and" ? CondKind.And : CondKind.Or;
                var g = new CondNode { Kind = kind };

                foreach (LuaNode part in n.Items)
                {
                    CondNode c = FromLua(part);

                    if (c.Kind == kind)
                        g.Children.AddRange(c.Children);
                    else
                        g.Children.Add(c);
                }

                return g;
            }

            if (n.Kind == NodeKind.Not && n.Items.Count == 1)
            {
                var g = new CondNode { Kind = CondKind.Not };
                CondNode inner = FromLua(n.Items[0]);

                // not (a or b) = NONE de a, b : on montre directement a et b dans le groupe NONE
                if (inner.Kind == CondKind.Or)
                    g.Children.AddRange(inner.Children);
                else
                    g.Children.Add(inner);

                return g;
            }

            return NewLeaf(n.Source.Trim());
        }

        // Le texte Lua de la condition (les parenthèses nécessaires sont ajoutées).
        public string ToLua()
        {
            return Compose(this, null);
        }

        // Une condition simple qui contient un "or" au premier niveau, dans un ALL : Lua l'écrit entre parenthèses.
        // Faux pour un groupe sans aucune condition dedans : il est ignoré (sinon un OR vide serait toujours vrai).
        public bool HasContent()
        {
            if (Kind == CondKind.Leaf)
                return true;

            foreach (CondNode c in Children)
            {
                if (c.HasContent())
                    return true;
            }

            return false;
        }

        public bool LeafNeedsParens()
        {
            return Kind == CondKind.Leaf && Parent != null && Parent.Kind == CondKind.And && TopLevelIsOr(Lua.Trim());
        }

        private static bool TopLevelIsOr(string lua)
        {
            LuaNode n = LuaExprParser.ParseCondition(lua);
            return n != null && n.Kind == NodeKind.Binary && n.Name == "or";
        }

        private static string Compose(CondNode n, CondNode parent)
        {
            switch (n.Kind)
            {
                case CondKind.Leaf:
                {
                    string l = n.Lua.Trim();

                    if (l.Length == 0)
                        l = "true";

                    if (parent != null && parent.Kind == CondKind.And && TopLevelIsOr(l))
                        return "(" + l + ")";

                    return l;
                }

                case CondKind.Not:
                {
                    // NONE : aucune des conditions n'est vraie  =  not (a or b or c)
                    List<CondNode> none = n.Children.Where(c => c.HasContent()).ToList();

                    if (none.Count == 0)
                        return "true";

                    if (none.Count == 1)
                        return "not (" + Compose(none[0], null) + ")";

                    return "not (" + string.Join(" or ", none.Select(c => Compose(c, n)).ToArray()) + ")";
                }

                default:
                {
                    List<CondNode> kids = n.Children.Where(c => c.HasContent()).ToList();

                    if (kids.Count == 0)
                        return "true";

                    if (kids.Count == 1)
                        return Compose(kids[0], parent);

                    string joiner = n.Kind == CondKind.And ? " and " : " or ";
                    string text = string.Join(joiner, kids.Select(c => Compose(c, n)).ToArray());

                    return parent != null ? "(" + text + ")" : text;
                }
            }
        }
    }

    public partial class Triggers_Form
    {
        private static readonly Color ColorIfBar = SystemColors.Highlight;
        private static readonly Color ColorThenBar = SystemColors.ControlDarkDark;
        private const string DefaultLeafLua = "MissionInstance == 1";

        private Panel _panelBlocks;                    // IF + THEN + DETAILS
        private Panel _panelDetails;
        private ListBox _listThen;
        private static readonly Color ColorBadAction = Color.Firebrick;     // la seule couleur "hors système" : une action en défaut doit se voir
        private Font _fontBadAction;                    // gras et un peu plus grand (créée une fois)
        private int _thenWarnRow = -1;                  // l'action dont l'infobulle de problèmes est affichée
        private Label _labelThenBad;                    // "\u26A0 3 actions with a problem" dans l'en-tête THEN
        private const int ThenGripWidth = 28;           // la zone de la poignée ⋮⋮ à gauche de chaque action
        private int _thenDragFrom = -1;                 // l'action qu'on glisse (-1 = rien)
        private int _thenDropAt = -1;                   // la ligne bleue : avant cette action (Count = tout à la fin)
        private bool _thenDragging;
        private Point _thenDragStart;
        private TextBox _textInfo;                     // DETAILS quand aucun bloc n'est choisi : le détail du trigger et ses remarques
        private Splitter _splitterIf;
        private Splitter _splitterDetails;
        private CondNode _condRoot;                    // l'arbre de la condition du trigger affiché
        private bool _condRaw;                         // la condition n'a pas pu être lue : elle reste du Lua brut
        private bool _blocksUpdating;                  // true pendant qu'on remplit les listes (évite les événements)
        private bool _detailsPending;

        private Button _buttonActDup, _buttonActUp, _buttonActDown, _buttonActDelete;
        private Panel _thenFoot;
        private LinkLabel _linkThenLua;
        private TextBox _textThenLua;
        private const int ThenBtnW = 22;                // largeur d'un petit bouton (⧉ ▲ ▼ ✕) à droite de chaque action
        private const int ThenBtnZone = ThenBtnW * 4 + 6;
        private int _thenOverSlot = -1;

        private class CondItem
        {
            public FuncDef Def;                        // une fonction Return.*
            public string RefText;                     // ou une valeur (MissionInstance...)
            public string Display = "";
            public string Category = "";               // thème sous lequel cette ligne apparaît (une fonction peut en avoir plusieurs)
            public override string ToString() { return Display; }
        }

        // Une condition simple décomposée : [fonction ou valeur] [opérateur] [valeur].
        private class LeafState
        {
            public CondNode Node;
            public FuncDef Def;
            public string RefText;
            public List<string> Args = new List<string>();
            public List<LuaNode> ArgNodes = new List<LuaNode>();
            public string Op;
            public string Right = "";
            public Label Preview;

            public string Compose()
            {
                string left = Def != null ? ComposeAction(Def, Args) : RefText;
                return Op == null ? left : left + " " + Op + " " + Right;
            }
        }

        // (une propriété : le texte suit la langue choisie)
        private static string[][] CondRefs
        {
            get
            {
                return new[]
                {
            new[] { Lang.T("the mission instance (1 = first generation)"), "MissionInstance" },
            new[] { Lang.T("the campaign year"), "camp.date.year" },
            new[] { Lang.T("the campaign month"), "camp.date.month" },
            new[] { Lang.T("the campaign day"), "camp.date.day" },
            new[] { Lang.T("the ground targets percent (blue list)"), "GroundTarget[\"blue\"].percent" },
            new[] { Lang.T("the ground targets percent (red list)"), "GroundTarget[\"red\"].percent" },
            new[] { Lang.T("the 'repair minimum destroyed' setting"), "campMod.RepairMinimumDestroyed" }
                };
            }
        }

        private List<CondItem> _condItems;

        // ------------------------------------------------------------------------------------------
        //  Construction de la vue
        // ------------------------------------------------------------------------------------------

        private ListBox MakeBlockList()
        {
            return new ListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 26,
                IntegralHeight = false,
                HorizontalScrollbar = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ColorList
            };
        }

        private Button MakeBarButton(string text, int width, string tip)
        {
            var b = new Button { Text = text, Width = width, Height = 26, Margin = new Padding(0, 0, 4, 0) };
            _toolTip.SetToolTip(b, tip);
            return b;
        }

        private void BuildBlocksView()
        {
            _panelBlocks = new Panel { Dock = DockStyle.Fill, Visible = false };
            var center = new Panel { Dock = DockStyle.Fill };

            // ----- THEN -----
            _listThen = MakeBlockList();
            _listThen.Dock = DockStyle.Fill;
            _listThen.DrawItem += ListThen_DrawItem;
            _listThen.MouseDown += ListThen_MouseDown;
            _listThen.MouseMove += ListThen_MouseMove;
            _listThen.MouseUp += (s, e) => { _thenDragFrom = -1; };
            _listThen.MouseLeave += (s, e) => { if (_thenWarnRow >= 0) { _thenWarnRow = -1; _toolTip.Hide(_listThen); } };
            _listThen.AllowDrop = true;
            _listThen.DragOver += ListThen_DragOver;
            _listThen.DragDrop += ListThen_DragDrop;
            _listThen.DragLeave += (s, e) => ThenDragCancel();
            _listThen.SelectedIndexChanged += (s, e) =>
            {
                if (_blocksUpdating || _loadingEditor)
                    return;

                UpdateThenButtons();
                ShowDetailsLater();
            };

            // En-tête comme celui du IF ; les boutons de la barre sont maintenant sur chaque ligne (⧉ ▲ ▼ ✕) et le lien "+ action" sous la liste.
            var thenBar = new Panel { Dock = DockStyle.Top, Height = 28 };
            thenBar.Controls.Add(new Label { Text = Lang.T("THEN"), Left = 0, Top = 6, Width = 50, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
            thenBar.Controls.Add(new Label { Text = Lang.T("this trigger then does:"), Left = 52, Top = 7, Width = 400, ForeColor = SystemColors.GrayText });

            _labelThenBad = new Label { Left = 250, Top = 7, Width = 420, Visible = false, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            _labelThenBad.Click += (s, e) => GoToBadAction(1, true);
            _toolTip.SetToolTip(_labelThenBad, Lang.T("Click: go to the next action with a problem (F8)"));
            thenBar.Controls.Add(_labelThenBad);

            // les anciens boutons existent toujours (le code les met à jour) mais ne sont plus affichés
            _buttonAddAction = MakeBarButton(Lang.T("Add an action ▾"), 128, Lang.T("Adds an action at the end of the list."));
            _buttonActDup = MakeBarButton(Lang.T("Duplicate"), 80, Lang.T("Copies the chosen action just after it."));
            _buttonActUp = MakeBarButton("▲", 32, Lang.T("Move the chosen action up"));
            _buttonActDown = MakeBarButton("▼", 32, Lang.T("Move the chosen action down"));
            _buttonActDelete = MakeBarButton(Lang.T("Delete"), 70, Lang.T("Delete the chosen action"));

            _thenFoot = new Panel { Dock = DockStyle.Bottom, Height = 24 };

            var linkAdd = new LinkLabel { Left = 6, Top = 4, AutoSize = true, Text = Lang.T("+ action"), LinkBehavior = LinkBehavior.HoverUnderline };
            linkAdd.LinkClicked += (s, e) => _menuAdd.Show(linkAdd, new Point(0, linkAdd.Height));

            _linkThenLua = new LinkLabel { Left = 90, Top = 4, AutoSize = true, Text = "[Lua ▾]", LinkBehavior = LinkBehavior.HoverUnderline };
            _toolTip.SetToolTip(_linkThenLua, Lang.T("Show or hide the Lua code of the actions"));
            _linkThenLua.LinkClicked += (s, e) =>
            {
                _textThenLua.Visible = !_textThenLua.Visible;
                _linkThenLua.Text = _textThenLua.Visible ? "[Lua ▴]" : "[Lua ▾]";
                UpdateThenLua();
            };

            _thenFoot.Controls.Add(linkAdd);
            _thenFoot.Controls.Add(_linkThenLua);

            _textThenLua = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 120,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9),
                BackColor = ColorDetailBack,
                ForeColor = ColorDetailText,
                Visible = false
            };

            // ----- IF : des cartes-phrases (voir Triggers_Cards.cs) -----
            BuildCardsPanel();

            // Ordre d'ajout : le Fill d'abord, puis le trait mobile juste avant le panneau qu'il redimensionne.
            _splitterIf = new Splitter { Dock = DockStyle.Top, Height = 5 };
            _splitterIf.SplitterMoved += (s, e) => _ifUserHeight = true;

            center.Controls.Add(_listThen);
            center.Controls.Add(_thenFoot);
            center.Controls.Add(_textThenLua);
            center.Controls.Add(thenBar);
            center.Controls.Add(_splitterIf);
            center.Controls.Add(_panelIf);

            // ----- DETAILS -----
            _textInfo = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9),
                BackColor = ColorDetailBack,
                ForeColor = ColorDetailText,
                BorderStyle = BorderStyle.FixedSingle
            };

            _panelDetails = new Panel { Dock = DockStyle.Right, Width = 600, AutoScroll = true, Padding = new Padding(6, 0, 0, 0) };
            _splitterDetails = new Splitter { Dock = DockStyle.Right, Width = 5, MinExtra = 260, MinSize = 240 };

            _panelBlocks.Controls.Add(center);
            _panelBlocks.Controls.Add(_splitterDetails);
            _panelBlocks.Controls.Add(_panelDetails);

            _menuAdd = BuildAddMenu();
        }

        private Font BadActionFont()
        {
            if (_fontBadAction == null)
                _fontBadAction = new Font(_listThen.Font.FontFamily, _listThen.Font.Size + 1.5f, FontStyle.Bold);

            return _fontBadAction;
        }

        // "\u26A0 3 actions with a problem" dans l'en-tête THEN (cliquable = action fautive suivante).
        private void UpdateThenBad()
        {
            int n = _editing != null ? _editing.ActionProblems.Count : 0;
            _labelThenBad.Visible = n > 0;

            if (n > 0)
                _labelThenBad.Text = "\u26A0 " + n + Lang.T(n == 1 ? " action with a problem  (click or F8)" : " actions with a problem  (click or F8)");
        }

        // Sélectionne l'action fautive suivante (ou précédente) du trigger affiché. wrap = recommencer au début si on est au bout.
        // Renvoie false s'il n'y en a pas.
        private bool GoToBadAction(int direction, bool wrap)
        {
            if (_editing == null || _editing.ActionProblems.Count == 0)
                return false;

            int n = _editing.Actions.Count;
            int from = _listThen.SelectedIndex;

            for (int step = 1; step <= n; step++)
            {
                int i = from + direction * step;

                if (i < 0 || i >= n)
                {
                    if (!wrap)
                        return false;

                    i = ((i % n) + n) % n;
                }

                if (_editing.ActionProblems.ContainsKey(i))
                {
                    _listThen.SelectedIndex = i;
                    return true;
                }
            }

            return false;
        }

        private void UpdateThenButtons()
        {
            int i = _listThen.SelectedIndex;
            int n = _editing != null ? _editing.Actions.Count : 0;

            _buttonActDup.Enabled = i >= 0;
            _buttonActUp.Enabled = i > 0;
            _buttonActDown.Enabled = i >= 0 && i < n - 1;
            _buttonActDelete.Enabled = i >= 0;
        }

        // ------------------------------------------------------------------------------------------
        //  Remplissage
        // ------------------------------------------------------------------------------------------

        // Appelé quand on choisit un trigger (ou aucun).
        private void FillBlocks(CampTrigger t)
        {
            _panelBlocks.Visible = t != null;
            _textDetail.Visible = t == null;

            if (t == null)
            {
                _condRoot = null;
                _condRaw = false;
                return;
            }

            LoadCondition(t);
            _ifUserHeight = false;                // un autre trigger : la hauteur de IF se règle de nouveau toute seule

            _blocksUpdating = true;

            try
            {
                RefreshIfList(null);
                RefreshThenList(-1);
            }
            finally
            {
                _blocksUpdating = false;
            }

            UpdateThenButtons();
            ShowDetails();
        }

        private void LoadCondition(CampTrigger t)
        {
            _condRaw = false;
            _condRoot = null;

            string text = t.HasCondition ? (t.Condition ?? "") : "";

            if (text.Trim().Length == 0)
            {
                _condRoot = CondNode.NewLeaf("true");      // le moteur veut toujours une condition : "true" = toujours
                return;
            }

            LuaNode n = LuaExprParser.ParseCondition(text);

            if (n == null)
            {
                _condRaw = true;
                return;
            }

            _condRoot = CondNode.FromLua(n);
            _condRoot.FixParents();
        }

        private static string DescribeLeaf(string lua)
        {
            LuaNode n = LuaExprParser.ParseCondition(lua);

            if (n == null)
                return "(raw Lua) " + OneLine(lua);

            string text = TriggerText.DescribeCondition(n);
            return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
        }

        private void RefreshThenList(int select)
        {
            bool was = _blocksUpdating;
            _blocksUpdating = true;

            try
            {
                int n = _editing != null ? _editing.Actions.Count : 0;

                _listThen.BeginUpdate();
                _listThen.Items.Clear();
                _listThen.Items.AddRange(Enumerable.Range(0, n).Select(i => (object)i).ToArray());

                if (select >= 0 && select < n)
                    _listThen.SelectedIndex = select;

                _listThen.EndUpdate();
                UpdateThenLua();
                UpdateThenBad();
            }
            finally
            {
                _blocksUpdating = was;
            }
        }

        // Les actions telles qu'elles sont écrites dans le fichier : une par ligne.
        private void UpdateThenLua()
        {
            if (_textThenLua == null || !_textThenLua.Visible)
                return;

            var sb = new StringBuilder();

            if (_editing != null)
            {
                sb.Append("action =\r\n{\r\n");

                foreach (string a in _editing.Actions)
                    sb.Append("\t" + LuaText.Quote(a) + ",\r\n");

                sb.Append("}");
            }

            _textThenLua.Text = sb.ToString();
        }

        // Après un ajout / suppression / déplacement d'action : la liste est refaite et l'action choisie reste affichée dans DETAILS.
        private void SelectAction(int index)
        {
            RefreshThenList(index);
            UpdateThenButtons();
            ShowDetails();
        }

        // ------------------------------------------------------------------------------------------
        //  Dessin des lignes
        // ------------------------------------------------------------------------------------------

        private void DrawBlockBack(DrawItemEventArgs e, bool selected, Color bar, Color back)
        {
            using (var brush = new SolidBrush(selected ? SystemColors.Highlight : back))
                e.Graphics.FillRectangle(brush, e.Bounds);

            using (var pen = new Pen(SystemColors.ControlLight))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            using (var brush = new SolidBrush(bar))
                e.Graphics.FillRectangle(brush, e.Bounds.X, e.Bounds.Y + 3, 4, e.Bounds.Height - 7);
        }

        private void ListThen_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || _editing == null || e.Index >= _editing.Actions.Count)
                return;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            DrawBlockBack(e, selected, ColorThenBar, _listThen.BackColor);

            bool raw = _editing.ActionIsRaw(e.Index);
            bool bad = _editing.ActionProblems.ContainsKey(e.Index);
            bool anyBad = _editing.ActionProblems.Count > 0;
            bool fade = anyBad && !bad && _checkOnlyProblems.Checked;      // "only problems" : les actions sans problème s'effacent
            Color fore = selected ? SystemColors.HighlightText : (bad ? ColorBadAction : raw || fade ? SystemColors.GrayText : SystemColors.ControlText);
            var grip = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, ThenGripWidth - 8, e.Bounds.Height);

            if (bad)
            {
                // action en défaut : le \u26A0 prend la place de la poignée (qui reste active : on peut toujours glisser par là)
                TextRenderer.DrawText(e.Graphics, "\u26A0", BadActionFont(), new Rectangle(e.Bounds.X + 4, e.Bounds.Y, ThenGripWidth - 4, e.Bounds.Height), fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, "⋮⋮", _listThen.Font, grip, selected ? SystemColors.HighlightText : SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            var rect = new Rectangle(e.Bounds.X + ThenGripWidth + 4, e.Bounds.Y, Math.Max(10, e.Bounds.Width - ThenGripWidth - ThenBtnZone - 8), e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics, ActionSummary(_editing, e.Index), bad ? BadActionFont() : _listThen.Font, rect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // les petits boutons : dupliquer, monter, descendre, supprimer
            int zoneX = e.Bounds.Right - ThenBtnZone;
            int n = _editing.Actions.Count;
            Color dim = selected ? SystemColors.HighlightText : SystemColors.GrayText;

            e.Graphics.DrawImage(selected ? _iconCloneSelected : _iconClone, zoneX + 3, e.Bounds.Y + (e.Bounds.Height - 16) / 2);

            DrawThenGlyph(e, "▲", zoneX + ThenBtnW, e.Index > 0 ? fore : Color.Empty, dim);
            DrawThenGlyph(e, "▼", zoneX + ThenBtnW * 2, e.Index < n - 1 ? fore : Color.Empty, dim);
            DrawThenGlyph(e, "✕", zoneX + ThenBtnW * 3, fore, dim);

            // la ligne bleue du glisser-déposer
            if (_thenDragging && _thenDropAt >= 0)
            {
                using (var b = new SolidBrush(SystemColors.Highlight))
                {
                    if (_thenDropAt == e.Index)
                        e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y, e.Bounds.Width, 3);
                    else if (_thenDropAt == e.Index + 1 && e.Index == _editing.Actions.Count - 1)
                        e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3);
                }
            }
        }

        // ----- glisser-déposer des actions (on suit la souris à la main) -----

        private void DrawThenGlyph(DrawItemEventArgs e, string glyph, int x, Color color, Color dim)
        {
            var r = new Rectangle(x, e.Bounds.Y, ThenBtnW, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, glyph, _listThen.Font, r, color.IsEmpty ? Color.FromArgb(90, dim) : color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // Quel petit bouton est sous la souris ? 0 dupliquer, 1 monter, 2 descendre, 3 supprimer ; -1 = aucun.
        private int ThenSlotAt(Point p)
        {
            int zoneX = _listThen.ClientSize.Width - ThenBtnZone;

            if (p.X < zoneX || p.X >= zoneX + ThenBtnW * 4)
                return -1;

            return (p.X - zoneX) / ThenBtnW;
        }

        private void ListThen_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _editing == null)
                return;

            // un petit bouton à droite de la ligne
            int slot = ThenSlotAt(e.Location);
            int row = _listThen.IndexFromPoint(e.Location);

            if (slot >= 0 && row >= 0 && row < _editing.Actions.Count)
            {
                CampTrigger target = _editing;
                int count = _editing.Actions.Count;
                _thenDragFrom = -1;

                BeginInvoke(new Action(() =>
                {
                    if (slot == 0) { _listThen.SelectedIndex = row; DuplicateAction(); }
                    else if (slot == 1 && row > 0) MoveAction(target, row, -1);
                    else if (slot == 2 && row < count - 1) MoveAction(target, row, 1);
                    else if (slot == 3) DeleteAction(target, row);
                }));

                return;
            }

            // on ne glisse que par la poignée ⋮⋮ (comme les cartes du IF) ; ailleurs, c'est un simple clic
            int i = e.X < ThenGripWidth ? _listThen.IndexFromPoint(e.Location) : -1;

            _thenDragFrom = i;
            _thenDragging = false;
            _thenDragStart = e.Location;
        }

        private void ListThen_MouseMove(object sender, MouseEventArgs e)
        {
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                int slot = _listThen.IndexFromPoint(e.Location) >= 0 ? ThenSlotAt(e.Location) : -1;
                _listThen.Cursor = slot >= 0 ? Cursors.Hand : e.X < ThenGripWidth && _listThen.IndexFromPoint(e.Location) >= 0 ? Cursors.SizeAll : Cursors.Default;

                if (slot != _thenOverSlot)
                {
                    _thenOverSlot = slot;
                    _toolTip.SetToolTip(_listThen, slot == 0 ? Lang.T("Copies the chosen action just after it.")
                        : slot == 1 ? Lang.T("Move the chosen action up")
                        : slot == 2 ? Lang.T("Move the chosen action down")
                        : slot == 3 ? Lang.T("Delete the chosen action") : "");
                }
            }

            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                // infobulle : les problèmes de l'action sous la souris (sauf sur les petits boutons)
                int hit = _listThen.IndexFromPoint(e.Location);
                int warnRow = hit >= 0 && _editing != null && ThenSlotAt(e.Location) < 0 && _editing.ActionProblems.ContainsKey(hit) ? hit : -1;

                if (warnRow != _thenWarnRow)
                {
                    _thenWarnRow = warnRow;

                    if (warnRow >= 0)
                        _toolTip.Show(_editing.ActionProblemText(warnRow), _listThen, e.X + 16, e.Y + 20, 12000);
                    else
                        _toolTip.Hide(_listThen);
                }
            }

            if (_thenDragFrom < 0 || _editing == null || (Control.MouseButtons & MouseButtons.Left) == 0)
                return;

            if (Math.Abs(e.X - _thenDragStart.X) < 5 && Math.Abs(e.Y - _thenDragStart.Y) < 5)
                return;

            int from = _thenDragFrom;
            _thenDragFrom = -1;

            if (from < 0 || from >= _editing.Actions.Count)
                return;

            _thenDragging = true;
            _thenDropAt = -1;

            // DoDragDrop garde la main jusqu'au relâchement de la souris (DragOver / DragDrop ci-dessous)
            DragDropEffects result = _listThen.DoDragDrop("action:" + from, DragDropEffects.Move);

            if (result == DragDropEffects.None)
                ThenDragCancel();
        }

        private void ListThen_DragOver(object sender, DragEventArgs e)
        {
            if (_editing == null || !e.Data.GetDataPresent(typeof(string)))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;
            _thenDragging = true;

            Point p = _listThen.PointToClient(new Point(e.X, e.Y));

            // près du bord : la liste défile (DragOver est rappelé régulièrement)
            if (p.Y < 14 && _listThen.TopIndex > 0)
                _listThen.TopIndex--;
            else if (p.Y > _listThen.ClientSize.Height - 14 && _listThen.TopIndex < _listThen.Items.Count - 1)
                _listThen.TopIndex++;

            int count = _editing.Actions.Count;
            int at = _listThen.IndexFromPoint(p.X, Math.Max(0, Math.Min(p.Y, _listThen.ClientSize.Height - 1)));

            if (at < 0)
                at = count;
            else if (p.Y > _listThen.GetItemRectangle(at).Top + _listThen.ItemHeight / 2)
                at++;

            if (at != _thenDropAt)
            {
                _thenDropAt = at;
                _listThen.Invalidate();
            }
        }

        private void ListThen_DragDrop(object sender, DragEventArgs e)
        {
            CampTrigger t = _editing;
            int at = _thenDropAt;
            string data = e.Data.GetDataPresent(typeof(string)) ? (string)e.Data.GetData(typeof(string)) : "";

            ThenDragCancel();

            int from;

            if (t == null || !data.StartsWith("action:") || !int.TryParse(data.Substring(7), out from))
                return;

            if (from < 0 || from >= t.Actions.Count || at < 0)
                return;

            if (at == from || at == from + 1)
                return;                                         // il reste à la même place

            string action = t.Actions[from];
            t.Actions.RemoveAt(from);

            if (at > from)
                at--;

            t.Actions.Insert(at, action);

            MarkDirty();
            RefreshAfterEdit(t);
            SelectAction(at);
        }

        private void ThenDragCancel()
        {
            bool was = _thenDragging;

            _thenDragFrom = -1;
            _thenDropAt = -1;
            _thenDragging = false;
            _listThen.Cursor = Cursors.Default;

            if (was)
                _listThen.Invalidate();
        }

        // ------------------------------------------------------------------------------------------
        //  DETAILS
        // ------------------------------------------------------------------------------------------

        private void ShowDetailsLater()
        {
            if (_detailsPending)
                return;

            _detailsPending = true;

            BeginInvoke(new Action(() =>
            {
                _detailsPending = false;

                if (!IsDisposed)
                    ShowDetails();
            }));
        }

        private void ClearDetails()
        {
            foreach (Control c in _panelDetails.Controls.Cast<Control>().ToList())
            {
                _panelDetails.Controls.Remove(c);

                if (!ReferenceEquals(c, _textInfo))
                    c.Dispose();
            }
        }

        private void ShowDetails()
        {
            CampTrigger t = _editing;
            bool wasLoading = _loadingEditor;

            _loadingEditor = true;
            _panelDetails.SuspendLayout();

            try
            {
                ClearDetails();
                _detailsNeed = 0;
                _panelDetails.AutoScrollMinSize = Size.Empty;

                if (t == null)
                    return;

                var controls = new List<Control>();          // du haut vers le bas
                int ai = _listThen.SelectedIndex;

                if (ai >= 0 && ai < t.Actions.Count)
                    BuildActionDetails(t, ai, controls);
                else
                {
                    _textInfo.Text = BuildDetail(t);
                    _panelDetails.Controls.Add(_textInfo);
                    return;
                }

                for (int i = controls.Count - 1; i >= 0; i--)        // Dock = Top s'empile à l'envers
                    _panelDetails.Controls.Add(controls[i]);

                // une seule barre de défilement horizontale, pour tout le panneau, et seulement si un champ ne tient pas
                if (_detailsNeed > 0)
                    _panelDetails.AutoScrollMinSize = new Size(_detailsNeed, 0);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Triggers_Form | ShowDetails : " + ex);
            }
            finally
            {
                _panelDetails.ResumeLayout();
                _loadingEditor = wasLoading;
            }
        }

        private Control MakeDetailsTitle(string text)
        {
            return new Label { Dock = DockStyle.Top, Height = 26, Text = text, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        }

        private Control MakeHelpLabel(string text)
        {
            int lines = text.Length / 48 + 1;

            return new Label
            {
                Dock = DockStyle.Top,
                Height = 8 + lines * 16,
                Text = text,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, 4, 0, 0)
            };
        }

        // Un avertissement en gras sous l'aide.
        private Control MakeNotice(string text)
        {
            int lines = text.Length / 52 + 1;

            return new Label
            {
                Dock = DockStyle.Top,
                Height = 8 + lines * 17,
                Text = text,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Padding = new Padding(0, 4, 0, 0)
            };
        }

        private Control MakeSpacer(int height)
        {
            return new Panel { Dock = DockStyle.Top, Height = height };
        }

        // ----- une action -----

        private void BuildActionDetails(CampTrigger t, int index, List<Control> controls)
        {
            LuaNode node = index < t.ActionNodes.Count ? t.ActionNodes[index] : null;
            FuncDef def = (node != null && node.Kind == NodeKind.Call && node.Def != null && node.Def.Kind == "Action") ? node.Def : null;

            var state = new ActionRow { Index = index, Def = def };

            controls.Add(MakeDetailsTitle("Action " + (index + 1)));
            controls.Add(MakeActionHeader(t, state));

            if (def != null && def.Help.Length > 0)
                controls.Add(MakeHelpLabel(def.Help));

            // fonction obsolète : on la laisse utilisable, mais on le dit clairement
            if (def != null && def.IsDeprecated)
                controls.Add(MakeNotice(Lang.T("DEPRECATED: ") + def.Deprecated + " " + Lang.T("You can still use it.")));

            if (def == null)
            {
                Control raw = MakeRawActionLine(t, state);
                raw.Height = 170;
                controls.Add(raw);
                return;
            }

            int count = Math.Max(def.Params.Count, node.Items.Count);

            for (int k = 0; k < count; k++)
                state.Args.Add(k < node.Items.Count ? node.Items[k].Source : null);

            for (int k = 0; k < count; k++)
            {
                ParamDef p = k < def.Params.Count
                    ? def.Params[k]
                    : new ParamDef { Label = Lang.T("extra (ignored by the engine)"), Type = TrigParam.Any, Optional = true };

                controls.Add(MakeParamLine(t, state, k, p, k < node.Items.Count ? node.Items[k] : null));
            }
        }

        // ----- une condition -----

        private Control MakeRawConditionBox(CampTrigger t, CondNode leaf)
        {
            string original = (leaf != null ? leaf.Lua : t.Condition ?? "").Replace("\r\n", "\n");

            var box = new TextBox
            {
                Dock = DockStyle.Top,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 150,
                Font = new Font("Consolas", 9.5f),
                Text = original.Replace("\n", "\r\n")
            };

            string last = original;

            box.Leave += (s, e) =>
            {
                string value = box.Text.Replace("\r\n", "\n");

                if (value == last)
                    return;

                string error = value.Trim().Length == 0 ? Lang.T("A condition cannot be empty (use true for 'always').") : TriggerChecker.CheckLuaSyntax("return " + value);

                if (error != null)
                {
                    MessageBox.Show(Lang.T("This is not valid Lua, so it was not accepted:\r\n") + error, "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    box.Text = last.Replace("\n", "\r\n");
                    return;
                }

                last = value;

                if (leaf != null)
                {
                    leaf.Lua = value.Trim();
                    CommitCondition(t, leaf);
                    return;
                }

                // la condition entière : on essaie de la relire en blocs
                t.HasCondition = true;
                t.Condition = value.Trim();
                MarkDirty();
                RefreshAfterEdit(t);
                LoadCondition(t);
                RebuildCardsLater(null);          // la zone de texte est dans les cartes : on ne la supprime pas pendant son propre événement
            };

            return box;
        }

        private List<CondItem> CondItems()
        {
            if (_condItems == null)
            {
                _condItems = new List<CondItem>();

                foreach (string[] r in CondRefs)
                    _condItems.Add(new CondItem { RefText = r[1], Display = Lang.T("Values : ") + r[0] });

                var pairs = new List<KeyValuePair<string, FuncDef>>();

                foreach (FuncDef f in TriggerCatalog.All.Where(x => x.Kind == "Return"))
                    foreach (string c in f.AllCategories)
                        pairs.Add(new KeyValuePair<string, FuncDef>(c, f));

                foreach (KeyValuePair<string, FuncDef> pair in pairs
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .ThenBy(x => x.Value.IsDeprecated)                  // les fonctions obsolètes en fin de thème
                    .ThenBy(x => x.Value.Sentence, StringComparer.Ordinal))
                {
                    _condItems.Add(new CondItem { Def = pair.Value, Category = pair.Key, Display = pair.Key + " : " + pair.Value.ListText(PlaceholderRx.Replace(pair.Value.Sentence, "…")) });
                }
            }

            return _condItems;
        }

        private static bool IsCompareOp(string op)
        {
            return op == "==" || op == "~=" || op == "<" || op == "<=" || op == ">" || op == ">=";
        }

        // null = on ne sait pas la décomposer (elle reste du Lua brut).
        private LeafState ParseLeaf(CondNode leaf)
        {
            LuaNode n = LuaExprParser.ParseCondition(leaf.Lua);

            if (n == null)
                return null;

            LuaNode left = n;
            LuaNode right = null;
            string op = null;

            if (n.Kind == NodeKind.Binary && IsCompareOp(n.Name) && n.Items.Count == 2)
            {
                left = n.Items[0];
                op = n.Name;
                right = n.Items[1];
            }

            var st = new LeafState { Node = leaf, Op = op, Right = right != null ? right.Source : "" };

            if (left.Kind == NodeKind.Call && left.Def != null && left.Def.Kind == "Return")
            {
                st.Def = left.Def;

                foreach (LuaNode item in left.Items)
                {
                    st.Args.Add(item.Source);
                    st.ArgNodes.Add(item);
                }
            }
            else if (left.Kind == NodeKind.Ref)
            {
                st.RefText = left.Name;
            }
            else
            {
                return null;
            }

            return st;
        }

        private static string DefaultRight(TrigValue returns)
        {
            return returns == TrigValue.Text ? "\"\"" : "0";
        }

        // ------------------------------------------------------------------------------------------
        //  Modifier l'arbre des conditions
        // ------------------------------------------------------------------------------------------

        private void ReplaceNode(CondNode old, CondNode repl)
        {
            CondNode parent = old.Parent;

            if (parent == null)
            {
                _condRoot = repl;
                repl.Parent = null;
                return;
            }

            int i = parent.Children.IndexOf(old);
            parent.Children[i] = repl;
            repl.Parent = parent;
        }

        // ------------------------------------------------------------------------------------------
        //  Ajouter avec AND / OR / AND NOT (les groupes et les parenthèses se font tout seuls)
        // ------------------------------------------------------------------------------------------

        private static ToolStripMenuItem MakeMenuItem(string text, Action click)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => click();
            return item;
        }

        private static void Info(string text)
        {
            MessageBox.Show(text, "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ----- souris dans la liste IF : clic sur AND / OR = bascule du groupe ; clic droit = menu -----

        private void DeleteNode(CondNode n)
        {
            CondNode parent = n.Parent;

            if (parent == null)
            {
                _condRoot = CondNode.NewLeaf("true");       // plus de condition = toujours
                return;
            }

            parent.Children.Remove(n);
            n.Parent = null;

            if (parent.Children.Count == 0)
            {
                DeleteNode(parent);
                return;
            }

            if ((parent.Kind == CondKind.And || parent.Kind == CondKind.Or) && parent.Children.Count == 1)
                ReplaceNode(parent, parent.Children[0]);        // un groupe d'un seul élément n'a plus de sens
        }

        // ------------------------------------------------------------------------------------------
        //  Actions : copier
        // ------------------------------------------------------------------------------------------

        private void DuplicateAction()
        {
            CampTrigger t = _editing;
            int i = _listThen.SelectedIndex;

            if (t == null || i < 0 || i >= t.Actions.Count)
                return;

            t.Actions.Insert(i + 1, t.Actions[i]);

            MarkDirty();
            RefreshAfterEdit(t);
            SelectAction(i + 1);
        }
    }
}
