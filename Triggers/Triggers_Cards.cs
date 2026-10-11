using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Panneau qui dessine les cadres sans scintillement.
    internal class CardCanvas : Panel
    {
        public CardCanvas()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
    }

    // La partie IF de la fenêtre des triggers : la condition en CARTES-PHRASES (partie de Triggers_Form).
    //   - une carte = une condition = une phrase à compléter ("The alive % of target [X] [is more than] [40]")
    //   - un cadre = un groupe : ALL (toutes vraies), AT LEAST ONE (une suffit) ou NOT (le contraire)
    //   - le menu du début de la carte change le TYPE de condition sans la refaire
    //   - "In plain words" résume tout ; le Lua est caché derrière un lien
    // Les cartes sont de vrais contrôles posés à la main (Layout) : une condition a rarement plus de 10 cartes.
    public partial class Triggers_Form
    {
        // ----- l'interface d'une carte ou d'un groupe -----
        private class NodeUi
        {
            public CondNode Node;                       // null = groupe de tête "virtuel" (la condition est une seule condition, ou "toujours")
            public bool IsGroup;
            public bool Virtual;
            public bool IsRoot;
            public CondKind Kind;                       // groupes
            public readonly List<Control> Parts = new List<Control>();      // carte : les morceaux de la phrase, de gauche à droite
            public Control Stretch;                     // le morceau qui prend la place restante (Lua brut)
            public ComboBox FuncCombo;                  // carte : "ce qu'on teste"
            public Button Remove, Up, Down;
            public Label Grip;                          // la poignée ⋮⋮ pour glisser-déposer
            public Rectangle Rect;                      // sa place dans le canvas (rempli par Layout)
            public ComboBox Mode;                       // groupe : ALL / AT LEAST ONE
            public Label Desc, Empty;
            public LinkLabel AddCond, AddGroup;
            public readonly List<NodeUi> Kids = new List<NodeUi>();
            public readonly List<Label> Chips = new List<Label>();          // Chips[i] = le AND / OR avant Kids[i + 1]
        }

        private class CardBox
        {
            public Rectangle Rect;
            public bool Group;
            public Color Bar;
        }

        private class CardOp
        {
            public string Op;
            public string Fixed;                        // valeur imposée (nil) : pas de zone de valeur
            public string Display = "";
            public override string ToString() { return Lang.T(Display); }
        }

        private static readonly CardOp[] CardOps =
        {
            new CardOp { Op = "==", Display = "is equal to  (=)" },
            new CardOp { Op = "~=", Display = "is different from  (≠)" },
            new CardOp { Op = "<", Display = "is less than  (<)" },
            new CardOp { Op = "<=", Display = "is less than or equal to  (≤)" },
            new CardOp { Op = ">", Display = "is more than  (>)" },
            new CardOp { Op = ">=", Display = "is more than or equal to  (≥)" }
        };

        private static readonly CardOp OpSet = new CardOp { Op = "~=", Fixed = "nil", Display = "is set" };
        private static readonly CardOp OpEmpty = new CardOp { Op = "==", Fixed = "nil", Display = "is not set" };
        private static readonly CardOp OpNone = new CardOp { Op = null, Display = "is true" };

        private static readonly Regex SentenceRx = new Regex(@"\{(\d+)\}");
        private static readonly Regex NumberOnlyRx = new Regex(@"^-?\d+(\.\d+)?$");
        private static readonly Regex WordOnlyRx = new Regex(@"^[A-Za-z_][A-Za-z_0-9]*$");

        private Panel _panelIf;
        private Panel _cardsHost;
        private CardCanvas _canvas;
        private Panel _panelPlain;
        private Label _labelPlain;
        private LinkLabel _linkLua;
        private TextBox _textLua;
        private bool _luaShown;
        private bool _ifUserHeight;                    // l'utilisateur a tiré le trait : on ne règle plus la hauteur tout seul
        private bool _cardsRebuildPending;
        private bool _layouting;
        private NodeUi _rootUi;
        private readonly List<Control> _rawParts = new List<Control>();
        private readonly List<CardBox> _boxes = new List<CardBox>();
        private readonly Dictionary<CondNode, NodeUi> _uiByNode = new Dictionary<CondNode, NodeUi>();
        private CondNode _focusLeaf;                   // la condition qu'on vient d'ajouter : sa liste "ce qu'on teste" s'ouvre
        private CondNode _focusGroup;                  // le groupe qu'on vient d'ajouter : le menu "que vérifier ?" s'ouvre
        private ContextMenuStrip _menuKinds, _menuNewGroup;
        private ToolStripMenuItem _menuMore;
        private bool _moreFilled;
        private NodeUi _dragUi;                        // la carte/le groupe qu'on est en train de glisser
        private bool _dragging;
        private Point _dragStart;
        private NodeUi _dropGroup;                     // où ça va tomber
        private int _dropIndex;
        private Rectangle _dropLine = Rectangle.Empty;
        private NodeUi _menuTarget;                    // le groupe pour lequel un menu est ouvert

        // ------------------------------------------------------------------------------------------
        //  Construction du panneau
        // ------------------------------------------------------------------------------------------

        private void BuildCardsPanel()
        {
            _panelIf = new Panel { Dock = DockStyle.Top, Height = 300 };

            var header = new Panel { Dock = DockStyle.Top, Height = 28 };
            header.Controls.Add(new Label { Text = Lang.T("IF"), Left = 0, Top = 6, Width = 28, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
            header.Controls.Add(new Label { Text = Lang.T("this trigger runs when:"), Left = 30, Top = 7, Width = 400, ForeColor = SystemColors.GrayText });

            _cardsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Control };
            _canvas = new CardCanvas { Left = 0, Top = 0, Width = 400, Height = 100, BackColor = SystemColors.Control };
            _canvas.Paint += Canvas_Paint;
            _cardsHost.Controls.Add(_canvas);
            _cardsHost.Resize += (s, e) => { if (_rootUi != null || _rawParts.Count > 0) LayoutCards(); };

            // "In plain words" + le Lua (caché)
            _panelPlain = new Panel { Dock = DockStyle.Bottom, Height = 66 };

            _textLua = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9),
                BackColor = ColorDetailBack,
                ForeColor = ColorDetailText,
                Visible = false
            };

            _linkLua = new LinkLabel { Dock = DockStyle.Top, Height = 20, Text = "[Lua ▾]", Padding = new Padding(8, 3, 0, 0), LinkBehavior = LinkBehavior.HoverUnderline };
            _toolTip.SetToolTip(_linkLua, Lang.T("Show or hide the Lua code of the condition"));
            _linkLua.LinkClicked += (s, e) =>
            {
                _luaShown = !_luaShown;
                ApplyLuaVisibility();
            };

            _labelPlain = new Label
            {
                Dock = DockStyle.Top,
                Height = 46,
                Padding = new Padding(8, 6, 8, 0),
                BackColor = SystemColors.Info,
                ForeColor = SystemColors.InfoText,
                AutoEllipsis = true
            };

            _panelPlain.Controls.Add(_textLua);
            _panelPlain.Controls.Add(_linkLua);
            _panelPlain.Controls.Add(_labelPlain);

            _toolTip.SetToolTip(_labelPlain, Lang.T("The whole condition, in plain words."));

            _panelIf.Controls.Add(_cardsHost);
            _panelIf.Controls.Add(_panelPlain);
            _panelIf.Controls.Add(header);
        }

        // La condition sur plusieurs lignes : un retour à la ligne avant chaque "and" / "or", décalé selon la profondeur des parenthèses.
        // Seul l'affichage change : le texte du trigger n'est pas touché.
        private static string FormatLuaCondition(string cond)
        {
            cond = cond.Replace("\r\n", "\n").Trim();

            var sb = new StringBuilder();
            int depth = 0;
            char quote = '\0';

            for (int i = 0; i < cond.Length; i++)
            {
                char c = cond[i];

                if (quote != '\0')
                {
                    sb.Append(c);

                    if (c == '\\' && i + 1 < cond.Length) sb.Append(cond[++i]);
                    else if (c == quote) quote = '\0';

                    continue;
                }

                if (c == '"' || c == '\'') { quote = c; sb.Append(c); continue; }
                if (c == '(' || c == '{') depth++;
                if (c == ')' || c == '}') depth = Math.Max(0, depth - 1);

                string keyword = null;

                if (string.CompareOrdinal(cond, i, " and ", 0, 5) == 0) keyword = "and";
                else if (string.CompareOrdinal(cond, i, " or ", 0, 4) == 0) keyword = "or";

                if (keyword != null)
                {
                    sb.Append("\n").Append(' ', depth * 4).Append(keyword).Append(' ');
                    i += keyword.Length + 1;
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString().Replace("\n", "\r\n");
        }

        private void ApplyLuaVisibility()
        {
            _textLua.Visible = _luaShown;
            _linkLua.Text = _luaShown ? "[Lua ▴]" : "[Lua ▾]";
            _panelPlain.Height = 66 + (_luaShown ? 130 : 0);
            FitIfHeight();
        }

        // ------------------------------------------------------------------------------------------
        //  (Re)construction des cartes
        // ------------------------------------------------------------------------------------------

        // Refait les cartes tout de suite (à ne pas appeler depuis un événement d'une carte : voir RebuildCardsLater).
        private void RefreshIfList(CondNode unused)
        {
            _cardsRebuildPending = false;
            ClearCards();

            CampTrigger t = _editing;

            if (t == null || _condRoot == null && !_condRaw)
            {
                RefreshPlain();
                return;
            }

            _canvas.SuspendLayout();

            try
            {
                if (_condRaw || _condRoot == null)
                    BuildRawCondition(t);
                else
                    _rootUi = BuildRoot(t);
            }
            finally
            {
                _canvas.ResumeLayout();
            }

            LayoutCards();
            RefreshPlain();

            // ce qu'on vient d'ajouter : on le montre, sans rien ouvrir
            CondNode added = _focusLeaf ?? _focusGroup;
            _focusLeaf = null;
            _focusGroup = null;

            NodeUi ui;

            if (added != null && _uiByNode.TryGetValue(added, out ui))
            {
                Control target = ui.FuncCombo != null ? (Control)ui.FuncCombo : (ui.Stretch != null ? ui.Stretch : ui.AddCond);
                bool raw = ui.FuncCombo == null && ui.Stretch is TextBox;      // une carte "Lua" qu'on vient d'ajouter : on peut taper tout de suite

                if (target != null)
                    BeginInvoke(new Action(() =>
                    {
                        if (target.IsDisposed || target.Parent == null)
                            return;

                        _cardsHost.ScrollControlIntoView(target);

                        if (raw)
                        {
                            target.Focus();
                            ((TextBox)target).SelectAll();
                        }
                    }));
            }
        }

        // Pour tout ce qui vient d'un événement d'une carte : la carte n'est pas supprimée pendant son propre événement.
        private void RebuildCardsLater(CondNode unused)
        {
            if (_cardsRebuildPending)
                return;

            _cardsRebuildPending = true;

            BeginInvoke(new Action(() =>
            {
                if (_cardsRebuildPending && !IsDisposed)
                    RefreshIfList(null);
            }));
        }

        private void ClearCards()
        {
            _rootUi = null;
            _boxes.Clear();
            _uiByNode.Clear();
            _rawParts.Clear();

            foreach (Control c in _canvas.Controls.Cast<Control>().ToList())
            {
                _canvas.Controls.Remove(c);
                c.Dispose();
            }
        }

        private T AddToCanvas<T>(T c) where T : Control
        {
            _canvas.Controls.Add(c);
            return c;
        }

        private NodeUi BuildRoot(CampTrigger t)
        {
            CondNode r = _condRoot;

            if (r.Kind != CondKind.Leaf)
                return MakeGroupUi(t, r, true);

            // une seule condition (ou "toujours") : on la montre dans un groupe ALL de tête, sans changer le fichier
            NodeUi root = MakeGroupUi(t, null, true);

            if (!IsAlways(r))
                root.Kids.Add(MakeNodeUi(t, r));

            FinishGroupUi(t, root);
            return root;
        }

        private static bool IsAlways(CondNode n)
        {
            return n != null && n.Kind == CondKind.Leaf && n.Lua.Trim() == "true";
        }

        private NodeUi MakeNodeUi(CampTrigger t, CondNode n)
        {
            return n.Kind == CondKind.Leaf ? MakeLeafUi(t, n) : MakeGroupUi(t, n, false);
        }

        // ----- un groupe -----

        private NodeUi MakeGroupUi(CampTrigger t, CondNode g, bool isRoot)
        {
            var ui = new NodeUi { Node = g, IsGroup = true, IsRoot = isRoot, Virtual = g == null, Kind = g != null ? g.Kind : CondKind.And };

            if (g != null)
                _uiByNode[g] = ui;

            ui.Mode = AddToCanvas(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 132, Font = new Font(Font, FontStyle.Bold) });
            ui.Mode.Items.Add(Lang.T("ALL"));
            ui.Mode.Items.Add(Lang.T("AT LEAST ONE"));
            ui.Mode.Items.Add(Lang.T("NONE"));
            ui.Mode.SelectedIndex = ui.Kind == CondKind.And ? 0 : (ui.Kind == CondKind.Or ? 1 : 2);
            ui.Mode.SelectionChangeCommitted += (s, e) => ChangeGroupKind(ui, ui.Mode.SelectedIndex == 0 ? CondKind.And : (ui.Mode.SelectedIndex == 1 ? CondKind.Or : CondKind.Not));
            _toolTip.SetToolTip(ui.Mode, Lang.T("ALL: every condition must be true (AND).\r\nAT LEAST ONE: one true condition is enough (OR).\r\nNONE: not a single one of them may be true (NOT)."));

            ui.Desc = AddToCanvas(new Label
            {
                AutoSize = false,
                Height = 20,
                Width = 360,
                BackColor = Color.Transparent,
                Text = ui.Kind == CondKind.Not ? Lang.T("of these conditions can be true") : Lang.T("of these conditions must be true"),
                Font = Font,
                ForeColor = SystemColors.GrayText
            });

            if (!isRoot)
            {
                ui.Grip = MakeGrip(ui);
                ui.Remove = MakeSmallButton("✕", Lang.T("Remove this group and what is inside"), () => RemoveNode(ui));
                ui.Up = MakeSmallButton("▲", Lang.T("Move up"), () => MoveNode(ui, -1));
                ui.Down = MakeSmallButton("▼", Lang.T("Move down"), () => MoveNode(ui, 1));
            }

            if (g != null)
            {
                foreach (CondNode c in g.Children)
                    ui.Kids.Add(MakeNodeUi(t, c));

                FinishGroupUi(t, ui);
            }

            return ui;
        }

        // Les AND / OR entre les cartes, le message "vide" et les liens "+ condition" / "+ group".
        private void FinishGroupUi(CampTrigger t, NodeUi ui)
        {
            for (int i = 1; i < ui.Kids.Count; i++)
            {
                ui.Chips.Add(AddToCanvas(new Label
                {
                    AutoSize = false,
                    Width = 60,
                    Height = 16,
                    BackColor = Color.Transparent,
                    ForeColor = SystemColors.GrayText,
                    Font = new Font(Font.FontFamily, 8, FontStyle.Bold),
                    Text = ui.Kind == CondKind.Or ? Lang.T("OR") : Lang.T("AND")
                }));
            }

            if (ui.Kids.Count == 0)
            {
                string text = ui.Virtual || ui.IsRoot
                    ? Lang.T("No condition: this trigger always runs. Add one with \"+ condition\".")
                    : Lang.T("Empty group: add a first condition with \"+ condition\". An empty group is ignored.");

                ui.Empty = AddToCanvas(new Label { AutoSize = false, Height = 22, BackColor = Color.Transparent, ForeColor = SystemColors.GrayText, Text = text });
            }

            ui.AddCond = MakeLink("+ condition", Lang.T("Adds a condition in this group."), () => ShowKindMenu(ui, ui.AddCond));
            ui.AddGroup = MakeLink(Lang.T("+ group"), Lang.T("Adds a group (ALL by default). You can change it to AT LEAST ONE or NONE afterwards."), () => AddEmptyGroup(ui, CondKind.And));
        }

        private LinkLabel MakeLink(string text, string tip, Action click)
        {
            var l = AddToCanvas(new LinkLabel
            {
                Text = text,
                AutoSize = true,
                BackColor = Color.Transparent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = new Font(Font.FontFamily, 8.5f)
            });

            l.LinkClicked += (s, e) => click();
            _toolTip.SetToolTip(l, tip);
            return l;
        }

        private Button MakeSmallButton(string text, string tip, Action click)
        {
            var b = AddToCanvas(new Button { Text = text, Width = 22, Height = 22, TabStop = false, Font = new Font(Font.FontFamily, 7.5f), Padding = new Padding(0) });
            b.Click += (s, e) => click();
            _toolTip.SetToolTip(b, tip);
            return b;
        }

        // ----- une carte (une condition) -----

        private Label MakeCardLabel(string text)
        {
            var l = AddToCanvas(new Label { AutoSize = false, Height = 20, BackColor = Color.Transparent, Text = text, TextAlign = ContentAlignment.MiddleLeft });
            l.Width = TextRenderer.MeasureText(text, l.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + 6;
            return l;
        }

        private NodeUi MakeLeafUi(CampTrigger t, CondNode leaf)
        {
            var ui = new NodeUi { Node = leaf };
            _uiByNode[leaf] = ui;

            LeafState st = ParseLeaf(leaf);

            if (st == null)
            {
                // pas une condition connue : le texte Lua, modifiable
                ui.Parts.Add(MakeCardLabel(Lang.T("Lua:")));
                TextBox raw = MakeRawLeafBox(t, leaf);
                ui.Parts.Add(raw);
                ui.Stretch = raw;
            }
            else
                BuildLeafParts(t, ui, leaf, st);

            ui.Grip = MakeGrip(ui);
            ui.Remove = MakeSmallButton("✕", Lang.T("Remove this condition"), () => RemoveNode(ui));
            ui.Up = MakeSmallButton("▲", Lang.T("Move up"), () => MoveNode(ui, -1));
            ui.Down = MakeSmallButton("▼", Lang.T("Move down"), () => MoveNode(ui, 1));

            return ui;
        }

        private static string[] SentenceParts(string sentence)
        {
            return SentenceRx.Split(sentence);           // texte, n°, texte, n°, texte...
        }

        private static string Capitalize(string s)
        {
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // Le début de la phrase, affiché dans la liste fermée : "The alive % of target", "Flag", "The mission number"...
        private static string ShortLabel(LeafState st)
        {
            if (st.Def == null)
            {
                string[] r = CondRefs.FirstOrDefault(x => x[1] == st.RefText);
                return Capitalize(r != null ? r[0] : st.RefText);
            }

            string head = SentenceParts(st.Def.Sentence)[0].Trim();
            return Capitalize(head.Length > 0 ? head : PlaceholderRx.Replace(st.Def.Sentence, "…"));
        }

        private ComboBox MakeFuncCombo(CampTrigger t, LeafState st)
        {
            CondItem current = CondItems().FirstOrDefault(x => st.Def != null ? x.Def == st.Def : x.RefText == st.RefText)
                ?? new CondItem { RefText = st.RefText, Display = st.RefText };

            string shortText = ShortLabel(st);

            var combo = AddToCanvas(new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                DropDownWidth = 560,
                Tag = shortText,
                Font = new Font(Font, FontStyle.Bold)
            });

            combo.ItemHeight = 18;
            combo.Items.Add(current);
            combo.SelectedIndex = 0;
            combo.Width = Math.Max(90, TextRenderer.MeasureText(shortText, combo.Font).Width + 34);

            // liste fermée : le début de la phrase ; liste ouverte : "Catégorie : phrase complète"
            combo.DrawItem += (s, e) =>
            {
                if (e.Index < 0)
                    return;

                var item = combo.Items[e.Index] as CondItem;
                bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
                string text = edit ? (string)combo.Tag : (item != null ? item.Display : "");

                e.DrawBackground();
                TextRenderer.DrawText(e.Graphics, text, edit ? combo.Font : Font, e.Bounds, e.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };

            bool loaded = false;

            combo.DropDown += (s, e) =>
            {
                if (loaded)
                    return;

                loaded = true;
                combo.BeginUpdate();
                combo.Items.Clear();
                combo.Items.AddRange(CondItems().ToArray());

                if (!CondItems().Contains(current))
                    combo.Items.Add(current);

                combo.SelectedItem = current;
                combo.EndUpdate();
            };

            combo.SelectionChangeCommitted += (s, e) => ChangeLeafFunction(t, st, combo.SelectedItem as CondItem);

            _toolTip.SetToolTip(combo, Lang.T("What this condition tests. Change it here: the card keeps its place."));
            return combo;
        }

        private static int EditorWidth(ParamDef p)
        {
            switch (p.Type)
            {
                case TrigParam.TargetTitle:
                case TrigParam.TargetName: return 230;
                case TrigParam.AirUnit: return 190;
                case TrigParam.Airbase: return 170;
                case TrigParam.Flag: return 110;
                case TrigParam.Number: return 64;
                case TrigParam.Choice: return 110;
                case TrigParam.Text: return 170;
                default: return 120;
            }
        }

        private void BuildLeafParts(CampTrigger t, NodeUi ui, CondNode leaf, LeafState st)
        {
            ComboBox combo = MakeFuncCombo(t, st);
            ui.FuncCombo = combo;
            ui.Parts.Add(combo);

            // les champs de la fonction, dans l'ordre de la phrase
            var row = new ActionRow { Index = -1, Def = st.Def };
            row.Args = st.Args;

            row.WriteBack = text =>
            {
                st.Node.Lua = st.Compose();
                CommitLight(t);
            };

            if (st.Def != null)
            {
                int count = Math.Max(st.Def.Params.Count, st.ArgNodes.Count);

                while (st.Args.Count < count)
                    st.Args.Add(null);

                var used = new HashSet<int>();
                string[] parts = SentenceParts(st.Def.Sentence);

                for (int i = 1; i < parts.Length; i += 2)
                {
                    int k = int.Parse(parts[i], CultureInfo.InvariantCulture);

                    if (k < count && used.Add(k))
                        AddParamEditor(t, ui, st, row, k);

                    string after = i + 1 < parts.Length ? parts[i + 1].Trim() : "";

                    if (after.Length > 0)
                        ui.Parts.Add(MakeCardLabel(after));
                }

                for (int k = 0; k < count; k++)
                {
                    if (used.Add(k))
                        AddParamEditor(t, ui, st, row, k);
                }
            }

            AddComparison(t, ui, st);
        }

        private void AddParamEditor(CampTrigger t, NodeUi ui, LeafState st, ActionRow row, int k)
        {
            ParamDef p = k < st.Def.Params.Count
                ? st.Def.Params[k]
                : new ParamDef { Label = Lang.T("extra (ignored by the engine)"), Type = TrigParam.Any, Optional = true };

            int height;
            Control editor = MakeEditor(t, row, k, p, k < st.ArgNodes.Count ? st.ArgNodes[k] : null, out height);

            if (!(editor is CheckBox))
                editor.Width = EditorWidth(p);

            _toolTip.SetToolTip(editor, p.Label + "\r\n" + ParamHint(p));
            ui.Parts.Add(AddToCanvas(editor));
        }

        // "is equal to [value]" (ou "is set", "is not set" pour un flag)
        private void AddComparison(CampTrigger t, NodeUi ui, LeafState st)
        {
            bool boolFunction = st.Def != null && st.Def.Returns == TrigValue.Bool;

            if (boolFunction && st.Op == null)
                return;

            bool anyValue = st.Def != null && st.Def.Returns == TrigValue.Any;
            bool nilRight = st.Right.Trim() == "nil";

            CardOp current;

            if (st.Op == null) current = OpNone;
            else if (nilRight && st.Op == "~=") current = OpSet;
            else if (nilRight && st.Op == "==") current = OpEmpty;
            else current = CardOps.FirstOrDefault(o => o.Op == st.Op) ?? CardOps[0];

            var items = new List<CardOp>();

            if (anyValue || current == OpSet || current == OpEmpty)
            {
                items.Add(OpSet);
                items.Add(OpEmpty);
            }

            items.AddRange(CardOps);

            if (current == OpNone)
                items.Insert(0, OpNone);

            var opCombo = AddToCanvas(new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 });
            opCombo.Items.AddRange(items.Cast<object>().ToArray());
            opCombo.SelectedItem = current;

            opCombo.SelectionChangeCommitted += (s, e) =>
            {
                var item = opCombo.SelectedItem as CardOp;

                if (item == null || ReferenceEquals(item, current))
                    return;

                st.Op = item.Op;

                if (item.Fixed != null)
                    st.Right = item.Fixed;
                else if (st.Op != null && (st.Right.Trim().Length == 0 || st.Right.Trim() == "nil"))
                    st.Right = DefaultRight(st.Def != null ? st.Def.Returns : TrigValue.Number);

                st.Node.Lua = st.Compose();
                CommitCondition(t, st.Node);
            };

            _toolTip.SetToolTip(opCombo, Lang.T("How the value is compared."));
            ui.Parts.Add(opCombo);

            if (st.Op == null || current.Fixed != null)
                return;

            bool textual = st.Def != null && (st.Def.Returns == TrigValue.Any || st.Def.Returns == TrigValue.Text);
            string shown = textual ? UnquoteLua(st.Right) : st.Right;

            var box = AddToCanvas(new TextBox { Width = textual ? 110 : 80, Text = shown });
            string last = shown;

            _toolTip.SetToolTip(box, textual
                ? Lang.T("A number, true / false, or a word or text (written between quotes for you).")
                : Lang.T("A number, or any Lua value (for example Return.CampFlag(801) + 5)."));

            box.Leave += (s, e) =>
            {
                string v = box.Text.Trim();

                if (v == last)
                    return;

                if (v.Length == 0)
                {
                    MessageBox.Show(Lang.T("A value is needed here."), "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    box.Text = last;
                    return;
                }

                string lua = textual ? ValueToLua(v) : v;
                string error = TriggerChecker.CheckLuaSyntax("return " + lua);

                if (error != null)
                {
                    MessageBox.Show(Lang.T("This is not valid Lua:\r\n") + error, "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    box.Text = last;
                    return;
                }

                last = v;
                st.Right = lua;
                st.Node.Lua = st.Compose();
                CommitLight(t);
            };

            ui.Parts.Add(box);
        }

        // Ce qu'on a tapé -> Lua : un nombre / true / false / nil restent tels quels, un mot devient un texte, le reste est du Lua.
        private static string ValueToLua(string typed)
        {
            string v = typed.Trim();

            if (NumberOnlyRx.IsMatch(v) || v == "true" || v == "false" || v == "nil")
                return v;

            if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0])
                return v;

            if (WordOnlyRx.IsMatch(v) || TriggerChecker.CheckLuaSyntax("return " + v) != null)
                return LuaText.Quote(v);

            return v;
        }

        private static string UnquoteLua(string lua)
        {
            string v = lua.Trim();

            if (v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0] && v.IndexOf(v[0], 1) == v.Length - 1)
                return v.Substring(1, v.Length - 2);

            return v;
        }

        // Une condition simple qu'on ne sait pas décomposer : son texte Lua.
        private TextBox MakeRawLeafBox(CampTrigger t, CondNode leaf)
        {
            var box = AddToCanvas(new TextBox { Width = 380, Font = new Font("Consolas", 9.5f), Text = leaf.Lua });
            string last = leaf.Lua;

            _toolTip.SetToolTip(box, Lang.T("This condition is not in the lists, so it is kept as Lua text. As soon as it can be read, it becomes a normal card."));

            box.Leave += (s, e) =>
            {
                string v = box.Text.Trim();

                if (v == last)
                    return;

                string error = v.Length == 0 ? Lang.T("A condition cannot be empty (use true for 'always').") : TriggerChecker.CheckLuaSyntax("return " + v);

                if (error != null)
                {
                    MessageBox.Show(Lang.T("This is not valid Lua, so it was not accepted:\r\n") + error, "Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    box.Text = last;
                    return;
                }

                last = v;
                leaf.Lua = v;
                CommitCondition(t, leaf);            // il est peut-être lisible maintenant : les cartes sont refaites
            };

            return box;
        }

        // La condition entière est illisible : on la montre en Lua, modifiable.
        private void BuildRawCondition(CampTrigger t)
        {
            Label help = AddToCanvas(new Label
            {
                AutoSize = false,
                Height = 36,
                BackColor = Color.Transparent,
                Text = Lang.T("This condition is not made of known pieces, so it is kept as Lua text. Correct it here: as soon as it can be read, the cards appear.")
            });

            Control box = AddToCanvas(MakeRawConditionBox(t, null));
            box.Dock = DockStyle.None;

            _rawParts.Add(help);
            _rawParts.Add(box);
        }

        // ------------------------------------------------------------------------------------------
        //  Mise en page et dessin
        // ------------------------------------------------------------------------------------------

        private void LayoutCards()
        {
            if (_layouting)
                return;

            _layouting = true;

            try
            {
                _boxes.Clear();

                int width = Math.Max(440, _cardsHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
                int y = 8;

                if (_rawParts.Count == 2)
                {
                    _rawParts[0].SetBounds(8, y, width - 16, 36);
                    _rawParts[1].SetBounds(8, y + 40, width - 16, 150);
                    y += 200;
                }
                else if (_rootUi != null)
                    y += LayoutNode(_rootUi, 6, y, width - 12) + 4;

                _canvas.SetBounds(0, 0, width, y + 6);
                _canvas.Invalidate();
            }
            finally
            {
                _layouting = false;
            }

            FitIfHeight();
        }

        // Place une carte ou un groupe ; renvoie sa hauteur.
        private int LayoutNode(NodeUi u, int x, int y, int w)
        {
            return u.IsGroup ? LayoutGroup(u, x, y, w) : LayoutCard(u, x, y, w);
        }

        private int LayoutCard(NodeUi u, int x, int y, int w)
        {
            const int pad = 22, lineH = 28, btn = 22;

            int right = x + w - 6 - 3 * (btn + 2) - 4;      // la place des boutons ▲ ▼ ✕ à droite
            int fx = x + pad, fy = y + 6;

            foreach (Control part in u.Parts)
            {
                int pw = part.Width;

                if (ReferenceEquals(part, u.Stretch))
                {
                    if (right - fx < 160 && fx > x + pad)
                    {
                        fx = x + pad;
                        fy += lineH;
                    }

                    pw = Math.Max(160, right - fx);
                    part.Width = pw;
                }
                else if (fx + pw > right && fx > x + pad)
                {
                    fx = x + pad;
                    fy += lineH;
                }

                part.Left = fx;
                part.Top = fy + (lineH - part.Height) / 2;
                fx += pw + 4;
            }

            int h = fy - y + lineH + 6;

            PlaceButtons(u, x + w - 6, y + 6);
            if (u.Grip != null) u.Grip.SetBounds(x + 4, y + 6, 14, 22);
            u.Rect = new Rectangle(x, y, w, h);
            _boxes.Add(new CardBox { Rect = u.Rect, Group = false });
            return h;
        }

        private void PlaceButtons(NodeUi u, int rightEdge, int top)
        {
            const int btn = 22;

            if (u.Remove != null) u.Remove.SetBounds(rightEdge - btn, top, btn, btn);
            if (u.Down != null) u.Down.SetBounds(rightEdge - 2 * btn - 2, top, btn, btn);
            if (u.Up != null) u.Up.SetBounds(rightEdge - 3 * btn - 4, top, btn, btn);

            // ▲ ▼ ne servent que s'il y a un voisin
            CondNode n = u.Node;

            if (n != null)
            {
                CondNode p = n.Parent;
                bool inGroup = p != null;
                int i = inGroup ? p.Children.IndexOf(n) : -1;

                if (u.Up != null) { u.Up.Visible = inGroup; u.Up.Enabled = i > 0; }
                if (u.Down != null) { u.Down.Visible = inGroup; u.Down.Enabled = inGroup && i < p.Children.Count - 1; }
            }
        }

        private int LayoutGroup(NodeUi u, int x, int y, int w)
        {
            int left = u.IsRoot ? 14 : 26;

            int boxIndex = _boxes.Count;
            _boxes.Add(null);                                 // le cadre du groupe est dessiné avant ce qu'il contient

            int cy = y + 8;
            int cx = x + left;

            if (u.Grip != null)
                u.Grip.SetBounds(x + 9, cy + 1, 14, 22);

            if (u.Mode != null)
            {
                u.Mode.Location = new Point(cx, cy);
                u.Desc.Location = new Point(u.Mode.Right + 8, cy + 4);
            }
            else
                u.Desc.Location = new Point(cx, cy + 3);

            u.Desc.Width = Math.Max(100, x + w - 100 - u.Desc.Left);

            if (u.Remove != null)
                PlaceButtons(u, x + w - 6, cy);

            cy += 32;

            if (u.Kids.Count == 0)
            {
                if (u.Empty != null)
                {
                    u.Empty.SetBounds(cx, cy, w - left - 12, 22);
                    cy += 26;
                }
            }
            else
            {
                for (int i = 0; i < u.Kids.Count; i++)
                {
                    if (i > 0)
                    {
                        u.Chips[i - 1].Location = new Point(cx + 10, cy);
                        cy += 18;
                    }

                    cy += LayoutNode(u.Kids[i], cx, cy, w - left - 8) + 4;
                }
            }

            if (u.AddCond != null)
            {
                u.AddCond.Location = new Point(cx, cy);

                if (u.AddGroup != null)
                    u.AddGroup.Location = new Point(u.AddCond.Right + 12, cy);

                cy += 22;
            }

            int h = cy - y + 4;
            Color bar = u.Kind == CondKind.Or ? SystemColors.ControlDarkDark : (u.Kind == CondKind.Not ? SystemColors.GrayText : SystemColors.Highlight);

            u.Rect = new Rectangle(x, y, w, h);
            _boxes[boxIndex] = new CardBox { Rect = u.Rect, Group = true, Bar = bar };
            return h;
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            using (var cardBack = new SolidBrush(SystemColors.Window))
            using (var groupBack = new SolidBrush(SystemColors.ControlLight))
            using (var border = new Pen(SystemColors.ControlDark))
            {
                foreach (CardBox b in _boxes)
                {
                    if (b == null || !b.Rect.IntersectsWith(e.ClipRectangle))
                        continue;

                    e.Graphics.FillRectangle(b.Group ? groupBack : cardBack, b.Rect);
                    e.Graphics.DrawRectangle(border, b.Rect.X, b.Rect.Y, b.Rect.Width - 1, b.Rect.Height - 1);

                    if (b.Group)
                    {
                        using (var bar = new SolidBrush(b.Bar))
                            e.Graphics.FillRectangle(bar, b.Rect.X, b.Rect.Y, 5, b.Rect.Height);
                    }
                }
            }

            if (_dragging)
            {
                using (var pen = new Pen(SystemColors.Highlight, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    if (_dragUi != null)
                        e.Graphics.DrawRectangle(pen, _dragUi.Rect.X + 1, _dragUi.Rect.Y + 1, _dragUi.Rect.Width - 3, _dragUi.Rect.Height - 3);
                }

                if (_dropGroup != null)
                {
                    using (var pen = new Pen(SystemColors.Highlight, 2))
                        e.Graphics.DrawRectangle(pen, _dropGroup.Rect.X + 1, _dropGroup.Rect.Y + 1, _dropGroup.Rect.Width - 3, _dropGroup.Rect.Height - 3);
                }

                if (!_dropLine.IsEmpty)
                {
                    using (var b = new SolidBrush(SystemColors.Highlight))
                        e.Graphics.FillRectangle(b, _dropLine);
                }
            }
        }

        // La hauteur de IF suit son contenu (sauf si on a tiré le trait nous-mêmes).
        private void FitIfHeight()
        {
            if (_ifUserHeight || _panelIf == null || _panelIf.Parent == null)
                return;

            int parentHeight = _panelIf.Parent.ClientSize.Height;

            if (parentHeight <= 0)
                return;

            int want = 28 + _canvas.Height + 4 + _panelPlain.Height + 4;
            int max = Math.Max(200, parentHeight - 190);        // on garde de la place pour THEN
            want = Math.Max(190, Math.Min(want, max));

            if (Math.Abs(_panelIf.Height - want) > 2)
                _panelIf.Height = want;
        }

        // ------------------------------------------------------------------------------------------
        //  "In plain words" et le Lua
        // ------------------------------------------------------------------------------------------

        private static string LowerFirst(string s)
        {
            if (s.Length > 1 && char.IsUpper(s[0]) && char.IsLower(s[1]))
                return char.ToLowerInvariant(s[0]) + s.Substring(1);

            return s;
        }

        private string PlainText(CondNode n, bool top)
        {
            if (n.Kind == CondKind.Leaf)
                return LowerFirst(DescribeLeaf(n.Lua));

            List<CondNode> kids = n.Children.Where(c => c.HasContent()).ToList();

            if (kids.Count == 0)
                return Lang.T("always");

            if (n.Kind == CondKind.Not)
                return kids.Count == 1 ? Lang.T("not (") + PlainText(kids[0], true) + ")" : Lang.T("none of: ") + string.Join(", ", kids.Select(c => PlainText(c, false)).ToArray());

            string text = string.Join(n.Kind == CondKind.And ? Lang.T(" and ") : Lang.T(" or "), kids.Select(c => PlainText(c, false)).ToArray());
            return top || kids.Count == 1 ? text : "(" + text + ")";
        }

        private void RefreshPlain()
        {
            if (_labelPlain == null)
                return;

            CampTrigger t = _editing;

            if (t == null)
            {
                _labelPlain.Text = "";
                _textLua.Text = "";
                return;
            }

            string text;

            if (_condRaw || _condRoot == null)
                text = Lang.T("The condition is kept as Lua text.");
            else
            {
                string plain = PlainText(_condRoot, true);
                text = plain == "always" ? Lang.T("Always runs (no condition).") : Lang.T("Runs when ") + plain + ".";
            }

            _labelPlain.Text = text;
            _textLua.Text = FormatLuaCondition(t.Condition ?? "");
        }

        // ------------------------------------------------------------------------------------------
        //  Écrire dans le trigger
        // ------------------------------------------------------------------------------------------

        // Le texte de la condition est réécrit ici seulement quand on a modifié quelque chose.
        private void WriteCondition(CampTrigger t)
        {
            t.HasCondition = true;
            t.Condition = _condRoot == null ? "true" : _condRoot.ToLua();

            MarkDirty();
            RefreshAfterEdit(t);
        }

        // Une valeur d'un champ a changé : rien à refaire dans les cartes.
        private void CommitLight(CampTrigger t)
        {
            WriteCondition(t);
            RefreshPlain();
        }

        // La forme a changé (ajout, suppression, type, groupe...) : les cartes sont refaites.
        private void CommitCondition(CampTrigger t, CondNode unused)
        {
            WriteCondition(t);
            RebuildCardsLater(null);
        }

        // ------------------------------------------------------------------------------------------
        //  Changer ce qu'une carte teste
        // ------------------------------------------------------------------------------------------

        private string DefaultArgSmart(FuncDef def, int k)
        {
            ParamDef p = def.Params[k];

            if (p.Optional)
                return null;

            if (p.Type == TrigParam.TargetTitle || p.Type == TrigParam.TargetName || p.Type == TrigParam.AirUnit || p.Type == TrigParam.Airbase)
            {
                List<string> names = NameChoices(def, k, p);

                if (names != null && names.Count > 0)
                    return LuaText.Quote(names[0]);
            }

            if (p.Type == TrigParam.Flag)
            {
                string flag = FlagChoices().FirstOrDefault();
                return flag != null ? FlagLua(flag) : "801";
            }

            return DefaultArg(p);
        }

        // Une nouvelle condition de ce genre (en reprenant les valeurs de la précédente quand c'est possible).
        private LeafState NewState(CondItem item, LeafState old)
        {
            var ns = new LeafState();

            if (item.Def != null)
            {
                FuncDef def = item.Def;
                ns.Def = def;

                for (int k = 0; k < def.Params.Count; k++)
                {
                    string carry = null;

                    if (old != null && old.Def != null && k < old.Def.Params.Count && k < old.Args.Count && SameKind(old.Def.Params[k], def.Params[k]))
                        carry = old.Args[k];

                    ns.Args.Add(carry ?? DefaultArgSmart(def, k));
                }

                if (def.Name == "DatePassed" || def.Name == "DateBefore")
                {
                    ns.Args.Clear();
                    ns.Args.AddRange(new[] { "1980", "1", "1" });
                }

                if (def.Returns == TrigValue.Bool)
                {
                    ns.Op = null;
                    ns.Right = "";
                }
                else if (def.Name == "CampFlag") { ns.Op = "~="; ns.Right = "nil"; }
                else if (def.Name == "Mission") { ns.Op = ">="; ns.Right = "1"; }
                else if (def.Name == "TargetAlive" || def.Name == "BaseAlive") { ns.Op = "<"; ns.Right = "50"; }
                else { ns.Op = "=="; ns.Right = DefaultRight(def.Returns); }
            }
            else
            {
                ns.RefText = item.RefText;
                ns.Op = "==";
                ns.Right = "1";
            }

            return ns;
        }

        private void ChangeLeafFunction(CampTrigger t, LeafState st, CondItem item)
        {
            if (item == null || _loadingEditor)
                return;

            if (item.Def != null && item.Def == st.Def)
                return;

            if (item.Def == null && item.RefText == st.RefText && st.Def == null)
                return;

            LeafState ns = NewState(item, st);
            st.Node.Lua = ns.Compose();
            CommitCondition(t, st.Node);
        }

        // ------------------------------------------------------------------------------------------
        //  Groupes : ALL / AT LEAST ONE, ajouter, supprimer, déplacer
        // ------------------------------------------------------------------------------------------

        // La condition de tête était une seule condition (ou "toujours") : elle devient un vrai groupe.
        private CondNode MaterializeRoot(CondKind kind)
        {
            CondNode old = _condRoot;
            CondNode g = CondNode.NewGroup(kind);

            if (!IsAlways(old))
                g.Adopt(old);

            _condRoot = g;
            g.Parent = null;
            return g;
        }

        private void ChangeGroupKind(NodeUi ui, CondKind kind)
        {
            CampTrigger t = _editing;

            if (t == null)
                return;

            if (ui.Virtual)
            {
                if (kind != CondKind.And)
                    MaterializeRoot(kind);
                else
                    return;
            }
            else
            {
                CondNode g = ui.Node;

                if (g.Kind == kind)
                    return;

                g.Kind = kind;
                CondNode up = g.Parent;

                // du même type que son parent : les parenthèses ne servent plus, on fusionne (sauf NONE dans NONE : ce n'est pas la même chose)
                if (up != null && up.Kind == g.Kind && g.Kind != CondKind.Not)
                {
                    int i = up.Children.IndexOf(g);
                    up.Children.RemoveAt(i);

                    foreach (CondNode c in g.Children)
                        c.Parent = up;

                    up.Children.InsertRange(i, g.Children);
                }
            }

            CommitCondition(t, null);
        }

        // Met une condition (ou un groupe) à la fin du groupe.
        private void AddInto(NodeUi gui, CondNode fresh)
        {
            if (gui.Virtual)
            {
                if (IsAlways(_condRoot))
                {
                    _condRoot = fresh;                         // elle devient toute la condition
                    fresh.Parent = null;
                }
                else
                    MaterializeRoot(CondKind.And).Adopt(fresh);
            }
            else
                gui.Node.Adopt(fresh);
        }

        private void AddLeaf(NodeUi gui, CondNode leaf)
        {
            CampTrigger t = _editing;

            if (t == null)
                return;

            AddInto(gui, leaf);
            _focusLeaf = leaf;
            CommitCondition(t, leaf);
        }

        private void AddEmptyGroup(NodeUi gui, CondKind kind)
        {
            CampTrigger t = _editing;

            if (t == null)
                return;

            CondNode g = CondNode.NewGroup(kind);
            AddInto(gui, g);
            _focusGroup = g;                                    // le menu "que vérifier ?" s'ouvre dessus
            CommitCondition(t, g);
        }

        private void RemoveNode(NodeUi ui)
        {
            CampTrigger t = _editing;

            if (t == null || ui.Node == null)
                return;

            CondNode n = ui.Node;

            if (n.HasContent())
            {
                string what = n.Kind == CondKind.Leaf ? DescribeLeaf(n.Lua) : Lang.T("this group and everything inside it");
                string question = Lang.T("Remove this condition?\r\n\r\n") + what + Lang.T("\r\n\r\nIt leaves the file only when you click Save changes.");

                if (MessageBox.Show(question, "Triggers", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }

            DeleteNode(n);
            CommitCondition(t, null);
        }

        // ------------------------------------------------------------------------------------------
        //  Glisser-déposer (poignée ⋮⋮) : on suit la souris à la main, une ligne bleue montre où ça tombera
        // ------------------------------------------------------------------------------------------

        private Label MakeGrip(NodeUi ui)
        {
            var g = AddToCanvas(new Label
            {
                Text = "⋮⋮",
                AutoSize = false,
                Width = 14,
                Height = 22,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                ForeColor = SystemColors.GrayText,
                Cursor = Cursors.SizeAll
            });

            _toolTip.SetToolTip(g, Lang.T("Drag to move.\r\nDrop it between two conditions, or into a group."));

            g.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left)
                    return;

                _dragUi = ui;
                _dragging = false;
                _dragStart = Cursor.Position;
            };
            g.MouseMove += (s, e) => DragMove();
            g.MouseUp += (s, e) => DragEnd(true);
            g.MouseCaptureChanged += (s, e) => { if (_dragUi != null && !((Control)s).Capture) DragEnd(false); };

            return g;
        }

        private void DragMove()
        {
            if (_dragUi == null)
                return;

            Point mouse = Cursor.Position;

            if (!_dragging)
            {
                if (Math.Abs(mouse.X - _dragStart.X) < 5 && Math.Abs(mouse.Y - _dragStart.Y) < 5)
                    return;

                _dragging = true;
            }

            // près du bord de la zone : ça défile
            Point inHost = _cardsHost.PointToClient(mouse);

            if (inHost.Y < 20 || inHost.Y > _cardsHost.ClientSize.Height - 20)
            {
                int dy = inHost.Y < 20 ? -24 : 24;
                _cardsHost.AutoScrollPosition = new Point(0, -_cardsHost.AutoScrollPosition.Y + dy);
            }

            FindDrop(_canvas.PointToClient(mouse));
            _canvas.Invalidate();
        }

        // Le groupe le plus profond sous la souris, puis la place entre ses cartes.
        private void FindDrop(Point p)
        {
            _dropGroup = null;
            _dropIndex = 0;
            _dropLine = Rectangle.Empty;

            if (_rootUi == null || _rootUi.Virtual)
                return;

            NodeUi best = DeepestGroup(_rootUi, p);

            if (best == null)
                return;

            _dropGroup = best;
            int n = best.Kids.Count;
            int index = n;

            for (int i = 0; i < n; i++)
            {
                Rectangle r = best.Kids[i].Rect;

                if (p.Y < r.Top + r.Height / 2)
                {
                    index = i;
                    break;
                }
            }

            _dropIndex = index;
            int x = best.Rect.X + 10, w = best.Rect.Width - 20;

            if (n == 0)
                _dropLine = new Rectangle(x, best.Rect.Bottom - 12, w, 3);
            else if (index < n)
                _dropLine = new Rectangle(x, best.Kids[index].Rect.Top - 3, w, 3);
            else
                _dropLine = new Rectangle(x, best.Kids[n - 1].Rect.Bottom + 1, w, 3);
        }

        private NodeUi DeepestGroup(NodeUi g, Point p)
        {
            if (!g.Rect.Contains(p) || ReferenceEquals(g, _dragUi))
                return null;

            foreach (NodeUi k in g.Kids)
            {
                if (k.IsGroup)
                {
                    NodeUi deeper = DeepestGroup(k, p);

                    if (deeper != null)
                        return deeper;
                }
            }

            return g;
        }

        private void DragEnd(bool drop)
        {
            if (_dragUi == null)
                return;

            NodeUi ui = _dragUi;
            NodeUi target = _dropGroup;
            int index = _dropIndex;
            bool was = _dragging;

            _dragUi = null;
            _dragging = false;
            _dropGroup = null;
            _dropLine = Rectangle.Empty;
            _canvas.Invalidate();

            if (!was || !drop || target == null || ui.Node == null || target.Node == null)
                return;

            CampTrigger t = _editing;
            CondNode n = ui.Node, to = target.Node;

            // pas dans soi-même ni dans ce qu'on contient
            for (CondNode a = to; a != null; a = a.Parent)
                if (ReferenceEquals(a, n))
                    return;

            CondNode from = n.Parent;

            if (from == null || t == null)
                return;

            int old = from.Children.IndexOf(n);
            from.Children.Remove(n);

            if (ReferenceEquals(from, to) && old < index)
                index--;

            index = Math.Max(0, Math.Min(index, to.Children.Count));
            to.Children.Insert(index, n);
            n.Parent = to;

            CommitCondition(t, n);
        }

        private void MoveNode(NodeUi ui, int dir)
        {
            CampTrigger t = _editing;
            CondNode n = ui.Node;

            if (t == null || n == null || n.Parent == null)
                return;

            List<CondNode> list = n.Parent.Children;
            int i = list.IndexOf(n), j = i + dir;

            if (j < 0 || j >= list.Count)
                return;

            list[i] = list[j];
            list[j] = n;

            CommitCondition(t, n);
        }

        // ------------------------------------------------------------------------------------------
        //  Menus "que vérifier ?" et "quel groupe ?"
        // ------------------------------------------------------------------------------------------

        private CondItem FindItem(string fullName)
        {
            return CondItems().FirstOrDefault(x => x.Def != null && x.Def.FullName == fullName);
        }

        private void AddKind(string kind)
        {
            NodeUi gui = _menuTarget;

            if (gui == null)
                return;

            CondItem item = null;

            switch (kind)
            {
                case "mission": item = FindItem("Return.Mission"); break;
                case "date": item = FindItem("Return.DatePassed"); break;
                case "flag": item = FindItem("Return.CampFlag"); break;
                case "target": item = FindItem("Return.TargetAlive"); break;
                case "air": item = FindItem("Return.AirUnitActive"); break;
                case "lua":
                    AddLeaf(gui, CondNode.NewLeaf("true == true"));            // une carte "Lua" (toujours vraie tant qu'on n'a rien écrit) : on écrit la condition soi-même
                    return;
                default: item = CondItems().FirstOrDefault(x => x.RefText == "MissionInstance"); break;     // première génération
            }

            if (item == null)
                return;

            AddLeaf(gui, CondNode.NewLeaf(NewState(item, null).Compose()));
        }

        private void AddFromItem(CondItem item)
        {
            if (_menuTarget != null)
                AddLeaf(_menuTarget, CondNode.NewLeaf(NewState(item, null).Compose()));
        }

        private void EnsureMenus()
        {
            if (_menuKinds != null)
                return;

            _menuKinds = new ContextMenuStrip();
            _menuKinds.Items.Add(MakeMenuItem(Lang.T("Mission number     (from mission 5, before mission 3...)"), () => AddKind("mission")));
            _menuKinds.Items.Add(MakeMenuItem(Lang.T("Date     (after 12 March 1975...)"), () => AddKind("date")));
            _menuKinds.Items.Add(MakeMenuItem(Lang.T("Flag     (a number or a word set by another trigger)"), () => AddKind("flag")));
            _menuKinds.Items.Add(MakeMenuItem(Lang.T("Target     (still alive, destroyed...)"), () => AddKind("target")));
            _menuKinds.Items.Add(MakeMenuItem(Lang.T("Air unit     (active, playable...)"), () => AddKind("air")));
            _menuKinds.Items.Add(MakeMenuItem(Lang.T("First generation     (only the very first mission)"), () => AddKind("first")));
            _menuKinds.Items.Add(new ToolStripSeparator());
            _menuKinds.Items.Add(MakeMenuItem(Lang.T("Lua code     (write the condition yourself)"), () => AddKind("lua")));

            _menuMore = new ToolStripMenuItem(Lang.T("More tests..."));
            _menuMore.DropDownItems.Add(new ToolStripMenuItem(Lang.T("(loading)")));
            _menuMore.DropDownOpening += (s, e) => FillMoreMenu();
            _menuKinds.Items.Add(_menuMore);

            _menuNewGroup = new ContextMenuStrip();
            _menuNewGroup.Items.Add(MakeMenuItem(Lang.T("ALL of...     (every condition must be true)"), () => AddEmptyGroup(_menuTarget, CondKind.And)));
            _menuNewGroup.Items.Add(MakeMenuItem(Lang.T("AT LEAST ONE of...     (one true condition is enough)"), () => AddEmptyGroup(_menuTarget, CondKind.Or)));
            _menuNewGroup.Items.Add(MakeMenuItem(Lang.T("NOT...     (what is inside must be false)"), () => AddEmptyGroup(_menuTarget, CondKind.Not)));
        }

        private void FillMoreMenu()
        {
            if (_moreFilled)
                return;

            _moreFilled = true;
            _menuMore.DropDownItems.Clear();

            foreach (var group in CondItems().GroupBy(x => x.Def != null ? x.Category : Lang.T("Values")))
            {
                var sub = new ToolStripMenuItem(group.Key);

                foreach (CondItem item in group)
                {
                    CondItem captured = item;
                    string text = item.Def != null ? item.Def.ListText(PlaceholderRx.Replace(item.Def.Sentence, "…")) : item.Display.Replace(Lang.T("Values : "), "");
                    ToolStripMenuItem entry = MakeMenuItem(Capitalize(text), () => AddFromItem(captured));

                    if (item.Def != null && item.Def.IsDeprecated)
                    {
                        entry.ForeColor = SystemColors.GrayText;
                        entry.Font = new Font(entry.Font, FontStyle.Italic);
                        entry.ToolTipText = item.Def.HelpFull;
                    }

                    sub.DropDownItems.Add(entry);
                }

                _menuMore.DropDownItems.Add(sub);
            }
        }

        private void ShowKindMenu(NodeUi gui, Control anchor)
        {
            EnsureMenus();
            _menuTarget = gui;
            _menuKinds.Show(anchor, new Point(0, anchor.Height));
        }

        private void ShowGroupMenu(NodeUi gui, Control anchor)
        {
            EnsureMenus();
            _menuTarget = gui;
            _menuNewGroup.Show(anchor, new Point(0, anchor.Height));
        }
    }
}
