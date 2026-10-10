using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Onglet GRAPH : toute la campagne en un coup d'oeil, en trois colonnes
    //     WHEN (conditions)  ->  TRIGGER  ->  THEN (ce qui change : flags, cibles, escadrilles, bases...)
    // Un clic sur un élément surligne sa chaîne ; les flags sont suivis d'un trigger à l'autre
    // (le trigger qui positionne un flag -> ceux qui le lisent). Lecture seule : on édite dans LIST.

    // Un élément du graphe (une condition, un trigger ou une conséquence).
    public class GraphNode
    {
        public int Col;                 // 0 = conditions, 1 = triggers, 2 = conséquences
        public string Id = "";          // clé : "flag:802", "mission:==3", "target:xxx", "t:12" ...
        public string Label = "";
        public string Tag = "";         // petite lettre devant le texte : M mission, D date, F flag, T cible, A escadrille, B base, E fin
        public int Rank;
        public double Sort;
        public int Order;
        public int Row;                 // ligne dans sa colonne
        public CampTrigger Trigger;     // colonne 1 seulement
        public List<GraphNode> Links = new List<GraphNode>();

        public bool IsFlag
        {
            get { return Id.StartsWith("flag:", StringComparison.Ordinal); }
        }
    }

    // Ce que fait un trigger (une conséquence) avant d'en faire un GraphNode.
    internal class ResultKey
    {
        public string Id = "";
        public string Label = "";
        public string Tag = "";
        public int Kind;                // ordre d'affichage : 0 flag, 1 cible, 2 escadrille, 3 base, 4 fin, 5 catégorie, 6 Lua brut
    }

    public class GraphModel
    {
        public List<GraphNode> Conditions = new List<GraphNode>();
        public List<GraphNode> Triggers = new List<GraphNode>();
        public List<GraphNode> Results = new List<GraphNode>();
        public List<GraphNode[]> Edges = new List<GraphNode[]>();           // { gauche, droite }
        public Dictionary<string, GraphNode> ReadFlags = new Dictionary<string, GraphNode>();   // flags lus (colonne 0)
        public Dictionary<string, GraphNode> SetFlags = new Dictionary<string, GraphNode>();    // flags positionnés (colonne 2)
        public int ReadOnlyFlags;       // lus mais positionnés par aucun trigger
        public int SetOnlyFlags;        // positionnés mais lus par aucun trigger

        public int Rows
        {
            get { return Math.Max(Conditions.Count, Math.Max(Triggers.Count, Results.Count)); }
        }

        private static void Link(GraphModel m, GraphNode left, GraphNode right)
        {
            if (left.Links.Contains(right))
                return;

            left.Links.Add(right);
            right.Links.Add(left);
            m.Edges.Add(new[] { left, right });
        }

        private static string CondTag(TriggerGroupKey k)
        {
            if (k.Id.StartsWith("mission:", StringComparison.Ordinal)) return "M";
            if (k.Rank == 1) return "D";
            if (k.Rank == 2) return "F";
            return "";
        }

        public static GraphModel Build(CampTriggersFile file)
        {
            var m = new GraphModel();
            var condById = new Dictionary<string, GraphNode>();
            var resById = new Dictionary<string, GraphNode>();

            // même ordre que la liste (regroupement automatique) : les liens vont à peu près tout droit
            var sorted = file.Triggers
                .Select((t, i) => new { t, i, k = TriggerGrouper.Classify(t, "Auto") })
                .OrderBy(x => x.k.Rank).ThenBy(x => x.k.Sort).ThenBy(x => x.k.Label, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.i)
                .ToList();

            int order = 0;

            foreach (var x in sorted)
            {
                var tn = new GraphNode { Col = 1, Id = "t:" + x.i, Label = x.t.Name, Trigger = x.t, Row = m.Triggers.Count };
                m.Triggers.Add(tn);

                foreach (TriggerGroupKey k in TriggerGrouper.AtomKeys(x.t))
                {
                    GraphNode c;

                    if (!condById.TryGetValue(k.Id, out c))
                    {
                        c = new GraphNode { Col = 0, Id = k.Id, Label = Lang.Group(k.Label), Rank = k.Rank, Sort = k.Sort, Tag = CondTag(k) };
                        condById[k.Id] = c;
                        m.Conditions.Add(c);
                    }

                    Link(m, c, tn);
                }

                foreach (ResultKey r in ResultsOf(x.t))
                {
                    GraphNode res;

                    if (!resById.TryGetValue(r.Id, out res))
                    {
                        res = new GraphNode { Col = 2, Id = r.Id, Label = r.Label, Tag = r.Tag, Rank = r.Kind, Order = order++ };
                        resById[r.Id] = res;
                        m.Results.Add(res);
                    }

                    Link(m, tn, res);
                }
            }

            m.Conditions = m.Conditions.OrderBy(n => n.Rank).ThenBy(n => n.Sort).ThenBy(n => n.Label, StringComparer.OrdinalIgnoreCase).ToList();
            m.Results = m.Results.OrderBy(n => n.Rank).ThenBy(n => n.Order).ToList();

            for (int i = 0; i < m.Conditions.Count; i++)
            {
                m.Conditions[i].Row = i;

                if (m.Conditions[i].IsFlag)
                    m.ReadFlags[m.Conditions[i].Id] = m.Conditions[i];
            }

            for (int i = 0; i < m.Results.Count; i++)
            {
                m.Results[i].Row = i;

                if (m.Results[i].IsFlag)
                    m.SetFlags[m.Results[i].Id] = m.Results[i];
            }

            m.ReadOnlyFlags = m.ReadFlags.Keys.Count(id => !m.SetFlags.ContainsKey(id));
            m.SetOnlyFlags = m.SetFlags.Keys.Count(id => !m.ReadFlags.ContainsKey(id));

            return m;
        }

        private static ResultKey FlagResult(string flag)
        {
            return new ResultKey { Id = "flag:" + flag, Label = "Flag " + flag, Tag = "F", Kind = 0 };
        }

        private static IEnumerable<string> TextValues(LuaNode n)
        {
            if (n.Kind == NodeKind.Text)
                yield return n.Name;
            else if (n.Kind == NodeKind.Table)
            {
                foreach (LuaNode i in n.Items)
                {
                    if (i.Kind == NodeKind.Text)
                        yield return i.Name;
                }
            }
        }

        // Ce que fait un trigger : les flags qu'il change, les cibles / escadrilles / bases qu'il touche, sinon la catégorie de l'action.
        private static List<ResultKey> ResultsOf(CampTrigger t)
        {
            var list = new List<ResultKey>();
            var seen = new HashSet<string>();

            Action<ResultKey> add = r =>
            {
                if (seen.Add(r.Id))
                    list.Add(r);
            };

            for (int i = 0; i < t.Actions.Count; i++)
            {
                LuaNode n = i < t.ActionNodes.Count ? t.ActionNodes[i] : null;

                if (n == null || n.Kind != NodeKind.Call)
                {
                    bool any = false;

                    foreach (string f in TriggerGrouper.SetFlagsInText(t.Actions[i]))
                    {
                        add(FlagResult(f));
                        any = true;
                    }

                    if (!any)
                        add(new ResultKey { Id = "cat:raw", Label = Lang.T("Raw Lua"), Tag = "", Kind = 6 });

                    continue;
                }

                if (n.Name == "Action.SetCampFlag" || n.Name == "Action.AddCampFlag")
                {
                    add(FlagResult(TriggerGrouper.FlagName(n)));
                    continue;
                }

                bool specific = false;

                if (n.Def != null)
                {
                    for (int p = 0; p < n.Def.Params.Count && p < n.Items.Count; p++)
                    {
                        string prefix, title, tag;
                        int kind;

                        switch (n.Def.Params[p].Type)
                        {
                            case TrigParam.TargetTitle:
                            case TrigParam.TargetName: prefix = "target:"; title = Lang.T("Target "); tag = "T"; kind = 1; break;
                            case TrigParam.AirUnit: prefix = "air:"; title = Lang.T("Air unit "); tag = "A"; kind = 2; break;
                            case TrigParam.Airbase: prefix = "base:"; title = "Base "; tag = "B"; kind = 3; break;
                            default: continue;
                        }

                        foreach (string v in TextValues(n.Items[p]))
                        {
                            add(new ResultKey { Id = prefix + v, Label = title + v, Tag = tag, Kind = kind });
                            specific = true;
                        }
                    }
                }

                if (specific)
                    continue;

                if (n.Name == "Action.CampaignEnd" && n.Items.Count > 0 && n.Items[0].Kind == NodeKind.Text)
                    add(new ResultKey { Id = "end:" + n.Items[0].Name, Label = Lang.T("End of campaign: ") + n.Items[0].Name, Tag = "E", Kind = 4 });
                else
                {
                    string cat = n.Def != null ? n.Def.Category : Lang.T("Other");
                    add(new ResultKey { Id = "cat:" + cat, Label = cat, Tag = "", Kind = 5 });
                }
            }

            return list;
        }
    }

    // Le dessin du graphe. Contrôle maison : seules les lignes visibles sont dessinées (rapide même avec 200 triggers).
    public class TriggerGraphView : Control
    {
        private const int RowH = 24;
        private const int HeadH = 30;
        private const int Pad = 12;
        private const int Gap = 90;
        private const int MinColW = 200;
        private const int TagW = 20;
        private const int CountW = 34;

        private GraphModel _model;
        private GraphNode _selected;
        private readonly Dictionary<GraphNode, int> _state = new Dictionary<GraphNode, int>();   // 1 choisi, 2 voisin direct, 3 lié par un flag
        private readonly List<GraphNode> _unlocks = new List<GraphNode>();     // triggers qui lisent un flag positionné par la sélection
        private readonly List<GraphNode> _needs = new List<GraphNode>();       // triggers qui positionnent un flag lu par la sélection
        private readonly VScrollBar _vbar = new VScrollBar { Dock = DockStyle.Right, Visible = false };
        private readonly HScrollBar _hbar = new HScrollBar { Dock = DockStyle.Bottom, Visible = false };
        private Font _fontBold;
        private Font _fontItalic;

        public event EventHandler SelectionChanged;
        public event EventHandler<GraphNode> NodeActivated;

        public TriggerGraphView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

            BackColor = SystemColors.Window;
            TabStop = true;

            Controls.Add(_vbar);
            Controls.Add(_hbar);

            _vbar.ValueChanged += (s, e) => Invalidate();
            _hbar.ValueChanged += (s, e) => Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontBold != null) _fontBold.Dispose();
                if (_fontItalic != null) _fontItalic.Dispose();
            }

            base.Dispose(disposing);
        }

        public GraphNode Selected
        {
            get { return _selected; }
        }

        public void SetModel(GraphModel model)
        {
            _model = model;
            _selected = null;
            _state.Clear();
            _unlocks.Clear();
            _needs.Clear();
            _vbar.Value = 0;
            _hbar.Value = 0;
            UpdateScroll();
            Invalidate();
        }

        // Choisit le nœud d'un trigger et le met au milieu de l'écran.
        public void SelectTrigger(CampTrigger t)
        {
            if (_model == null || t == null)
                return;

            GraphNode n = _model.Triggers.FirstOrDefault(x => ReferenceEquals(x.Trigger, t));

            if (n == null)
                return;

            Select(n);
            ScrollToRow(n.Row);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
        }

        private void EnsureFonts()
        {
            if (_fontBold == null)
                _fontBold = new Font(Font, FontStyle.Bold);

            if (_fontItalic == null)
                _fontItalic = new Font(Font, FontStyle.Italic);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);

            if (_fontBold != null) { _fontBold.Dispose(); _fontBold = null; }
            if (_fontItalic != null) { _fontItalic.Dispose(); _fontItalic = null; }
        }

        // ----- défilement -----

        private void UpdateScroll()
        {
            if (_model == null)
            {
                _vbar.Visible = false;
                _hbar.Visible = false;
                return;
            }

            int contentH = HeadH + _model.Rows * RowH + Pad;
            int contentW = 3 * MinColW + 2 * Gap + 2 * Pad;

            bool needV = contentH > ClientSize.Height;
            bool needH = contentW > ClientSize.Width - (needV ? _vbar.Width : 0);

            if (needH)
                needV = contentH > ClientSize.Height - _hbar.Height;

            needH = contentW > ClientSize.Width - (needV ? _vbar.Width : 0);

            int viewW = ClientSize.Width - (needV ? _vbar.Width : 0);
            int viewH = ClientSize.Height - (needH ? _hbar.Height : 0);

            if (needV)
            {
                int v = Math.Max(0, Math.Min(_vbar.Value, contentH - viewH));
                _vbar.Maximum = contentH - 1;
                _vbar.LargeChange = Math.Max(1, viewH);
                _vbar.SmallChange = RowH * 2;
                _vbar.Value = Math.Max(0, Math.Min(v, _vbar.Maximum - _vbar.LargeChange + 1));
            }
            else
                _vbar.Value = 0;

            if (needH)
            {
                int v = Math.Max(0, Math.Min(_hbar.Value, contentW - viewW));
                _hbar.Maximum = contentW - 1;
                _hbar.LargeChange = Math.Max(1, viewW);
                _hbar.SmallChange = 40;
                _hbar.Value = Math.Max(0, Math.Min(v, _hbar.Maximum - _hbar.LargeChange + 1));
            }
            else
                _hbar.Value = 0;

            _vbar.Visible = needV;
            _hbar.Visible = needH;
        }

        private int ViewW { get { return ClientSize.Width - (_vbar.Visible ? _vbar.Width : 0); } }
        private int ViewH { get { return ClientSize.Height - (_hbar.Visible ? _hbar.Height : 0); } }
        private int OffX { get { return _hbar.Visible ? _hbar.Value : 0; } }
        private int OffY { get { return _vbar.Visible ? _vbar.Value : 0; } }

        private int ColW()
        {
            return Math.Max(MinColW, (ViewW - 2 * Pad - 2 * Gap) / 3);
        }

        private void ScrollToRow(int row)
        {
            if (!_vbar.Visible)
                return;

            int target = HeadH + row * RowH - ViewH / 2;
            int max = _vbar.Maximum - _vbar.LargeChange + 1;
            _vbar.Value = Math.Max(0, Math.Min(target, max));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (!_vbar.Visible)
                return;

            int max = _vbar.Maximum - _vbar.LargeChange + 1;
            _vbar.Value = Math.Max(0, Math.Min(_vbar.Value - e.Delta / 120 * RowH * 3, max));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);

            if (!Focused && FindForm() != null && FindForm() == Form.ActiveForm)
                Focus();                // pour que la molette agisse ici
        }

        // ----- sélection -----

        private void Mark(GraphNode n, int st)
        {
            if (!_state.ContainsKey(n))
                _state[n] = st;
        }

        private void Compute()
        {
            _state.Clear();
            _unlocks.Clear();
            _needs.Clear();

            if (_selected == null || _model == null)
                return;

            _state[_selected] = 1;

            var flagNodes = new List<GraphNode>();

            if (_selected.Col == 1)
            {
                foreach (GraphNode l in _selected.Links)
                {
                    Mark(l, 2);

                    if (l.IsFlag)
                        flagNodes.Add(l);
                }
            }
            else
            {
                if (_selected.IsFlag)
                    flagNodes.Add(_selected);

                foreach (GraphNode t in _selected.Links)
                {
                    Mark(t, 2);

                    foreach (GraphNode l in t.Links)
                    {
                        if (l.Col != _selected.Col)
                            Mark(l, 2);
                    }
                }
            }

            // un flag positionné (colonne 2) est lu ailleurs (colonne 0) : on suit le fil d'un trigger à l'autre
            foreach (GraphNode f in flagNodes)
            {
                GraphNode other;
                bool set = f.Col == 2;

                if (!(set ? _model.ReadFlags : _model.SetFlags).TryGetValue(f.Id, out other))
                    continue;

                Mark(other, 3);

                foreach (GraphNode t in other.Links)
                {
                    if (ReferenceEquals(t, _selected))
                        continue;

                    Mark(t, 3);
                    List<GraphNode> into = set ? _unlocks : _needs;

                    if (!into.Contains(t))
                        into.Add(t);
                }
            }
        }

        private void Select(GraphNode n)
        {
            _selected = n;
            Compute();
            Invalidate();

            if (SelectionChanged != null)
                SelectionChanged(this, EventArgs.Empty);
        }

        private GraphNode HitTest(Point p)
        {
            if (_model == null || p.Y < HeadH)
                return null;

            int colW = ColW();
            int x = p.X + OffX;
            int row = (p.Y + OffY - HeadH) / RowH;

            for (int col = 0; col < 3; col++)
            {
                int cx = Pad + col * (colW + Gap);

                if (x < cx || x >= cx + colW)
                    continue;

                List<GraphNode> list = col == 0 ? _model.Conditions : col == 1 ? _model.Triggers : _model.Results;
                return row >= 0 && row < list.Count ? list[row] : null;
            }

            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button != MouseButtons.Left || e.Clicks > 1)
                return;

            GraphNode n = HitTest(e.Location);
            Select(ReferenceEquals(n, _selected) ? null : n);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            GraphNode n = HitTest(e.Location);

            if (n != null && n.Col == 1 && NodeActivated != null)
                NodeActivated(this, n);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = HitTest(e.Location) != null ? Cursors.Hand : Cursors.Default;
        }

        // ----- texte d'information (sous le graphe) -----

        private static string Names(IEnumerable<GraphNode> nodes)
        {
            const int max = 8;
            List<GraphNode> list = nodes.ToList();

            if (list.Count == 0)
                return "-";

            string text = string.Join(", ", list.Take(max).Select(n => n.Label).ToArray());
            return list.Count > max ? text + "  (+" + (list.Count - max) + Lang.T(" more)") : text;
        }

        public string Describe()
        {
            if (_model == null)
                return "";

            var sb = new StringBuilder();

            if (_selected == null)
            {
                sb.AppendLine(_model.Triggers.Count + Lang.T(" triggers  |  ") + _model.Conditions.Count + Lang.T(" different conditions  |  ") + _model.Results.Count + Lang.T(" different results"));
                sb.AppendLine(Lang.T("Flags read but set by no trigger : ") + _model.ReadOnlyFlags + Lang.T("   |   set but read by no trigger : ") + _model.SetOnlyFlags);
                sb.Append(Lang.T("Click an item to follow its links."));
                return sb.ToString();
            }

            GraphNode s = _selected;

            if (s.Col == 1)
            {
                sb.AppendLine(Lang.T("TRIGGER  ") + s.Label);
                sb.AppendLine(Lang.T("When : ") + Names(s.Links.Where(l => l.Col == 0)));
                sb.AppendLine(Lang.T("Then : ") + Names(s.Links.Where(l => l.Col == 2)));
            }
            else
            {
                sb.AppendLine(Lang.T(s.Col == 0 ? "CONDITION  " : "RESULT  ") + s.Label + "   (" + s.Links.Count + Lang.T(" trigger") + (s.Links.Count == 1 ? "" : "s") + ")");
                sb.AppendLine(Lang.T("Triggers : ") + Names(s.Links));
            }

            if (_unlocks.Count > 0)
                sb.AppendLine(Lang.T("Unlocks (they read a flag set here) : ") + Names(_unlocks));

            if (_needs.Count > 0)
                sb.AppendLine(Lang.T("Needs (they set a flag read here) : ") + Names(_needs));

            if (s.IsFlag && _unlocks.Count == 0 && _needs.Count == 0)
                sb.AppendLine(Lang.T("No other trigger is linked to this flag (the mission or the engine may set or read it)."));

            if (s.Col == 1)
                sb.Append(Lang.T("Double-click to edit it in LIST."));

            return sb.ToString().TrimEnd();
        }

        // ----- dessin -----

        private static Color Blend(Color a, Color b, double part)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * part),
                (int)(a.G + (b.G - a.G) * part),
                (int)(a.B + (b.B - a.B) * part));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            if (_model == null)
                return;

            if (_model.Triggers.Count == 0)
            {
                TextRenderer.DrawText(g, Lang.T("No trigger in this campaign."), Font, ClientRectangle, SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            EnsureFonts();

            int offX = OffX, offY = OffY, viewW = ViewW, viewH = ViewH, colW = ColW();
            int x0 = Pad - offX, x1 = Pad + colW + Gap - offX, x2 = Pad + 2 * (colW + Gap) - offX;

            // --- liens ---
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool sel = _selected != null;
            var hot = new List<GraphNode[]>();

            using (var faint = new Pen(Color.FromArgb(sel ? 30 : 80, SystemColors.ControlDarkDark), 1f))
            {
                foreach (GraphNode[] ed in _model.Edges)
                {
                    int ya = HeadH + ed[0].Row * RowH + RowH / 2 - offY;
                    int yb = HeadH + ed[1].Row * RowH + RowH / 2 - offY;

                    if (Math.Max(ya, yb) < HeadH || Math.Min(ya, yb) > viewH)
                        continue;

                    if (sel && _state.ContainsKey(ed[0]) && _state.ContainsKey(ed[1]))
                    {
                        hot.Add(ed);
                        continue;
                    }

                    DrawEdge(g, faint, ed, x0, x1, x2, colW, offY);
                }
            }

            if (hot.Count > 0)
            {
                using (var bright = new Pen(SystemColors.Highlight, 2f))
                {
                    foreach (GraphNode[] ed in hot)
                        DrawEdge(g, bright, ed, x0, x1, x2, colW, offY);
                }
            }

            g.SmoothingMode = SmoothingMode.Default;

            // --- éléments (seulement ceux qu'on voit) ---
            DrawColumn(g, _model.Conditions, x0, colW, offY, viewH);
            DrawColumn(g, _model.Triggers, x1, colW, offY, viewH);
            DrawColumn(g, _model.Results, x2, colW, offY, viewH);

            // --- titres (fixes) ---
            using (var back = new SolidBrush(SystemColors.Control))
            using (var line = new Pen(SystemColors.ControlDark))
            {
                g.FillRectangle(back, 0, 0, viewW, HeadH);
                g.DrawLine(line, 0, HeadH - 1, viewW, HeadH - 1);
            }

            DrawHeader(g, Lang.T("WHEN  (") + _model.Conditions.Count + ")", x0, colW);
            DrawHeader(g, "TRIGGER  (" + _model.Triggers.Count + ")", x1, colW);
            DrawHeader(g, Lang.T("THEN  (") + _model.Results.Count + ")", x2, colW);
        }

        private void DrawHeader(Graphics g, string text, int x, int colW)
        {
            TextRenderer.DrawText(g, text, _fontBold, new Rectangle(x, 0, colW, HeadH), SystemColors.ControlText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        private void DrawEdge(Graphics g, Pen pen, GraphNode[] ed, int x0, int x1, int x2, int colW, int offY)
        {
            // ed[0] est dans la colonne 0 (liens vers un trigger) ou 1 (liens vers une conséquence)
            int xa = (ed[0].Col == 0 ? x0 : x1) + colW;
            int xb = ed[1].Col == 1 ? x1 : x2;
            float ya = HeadH + ed[0].Row * RowH + RowH / 2f - offY;
            float yb = HeadH + ed[1].Row * RowH + RowH / 2f - offY;
            float mid = (xb - xa) / 2f;

            g.DrawBezier(pen, xa, ya, xa + mid, ya, xb - mid, yb, xb, yb);
        }

        private void DrawColumn(Graphics g, List<GraphNode> list, int x, int colW, int offY, int viewH)
        {
            int first = Math.Max(0, (offY - HeadH) / RowH);
            int last = Math.Min(list.Count - 1, (offY + viewH - HeadH) / RowH);

            for (int i = first; i <= last; i++)
                DrawNode(g, list[i], new Rectangle(x, HeadH + i * RowH - offY + 1, colW, RowH - 2));
        }

        private void DrawNode(Graphics g, GraphNode n, Rectangle r)
        {
            int st;
            _state.TryGetValue(n, out st);

            bool dimmed = _selected != null && st == 0;
            Color back = SystemColors.Window, border = SystemColors.ControlDark, fore = SystemColors.WindowText, tagColor = SystemColors.HotTrack;
            Font font = Font;

            if (st == 1)
            {
                back = SystemColors.Highlight;
                border = SystemColors.Highlight;
                fore = SystemColors.HighlightText;
                tagColor = SystemColors.HighlightText;
                font = _fontBold;
            }
            else if (st == 2)
            {
                back = Blend(SystemColors.Window, SystemColors.Highlight, 0.25);
                border = SystemColors.Highlight;
            }
            else if (st == 3)
            {
                back = Blend(SystemColors.Window, SystemColors.Highlight, 0.10);
                border = SystemColors.Highlight;
            }
            else if (dimmed)
                fore = SystemColors.GrayText;

            CampTrigger t = n.Trigger;
            bool inactive = t != null && t.HasActive && !t.Active;

            if (inactive && st != 1)
            {
                font = _fontItalic;
                fore = SystemColors.GrayText;
            }

            using (var brush = new SolidBrush(back))
                g.FillRectangle(brush, r);

            using (var pen = new Pen(border))
            {
                if (st == 3)
                    pen.DashStyle = DashStyle.Dash;

                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            string tag = n.Tag;

            if (t != null)
                tag = t.ErrorCount > 0 ? "!" : t.WarningCount > 0 ? "?" : "";

            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            if (tag.Length > 0)
                TextRenderer.DrawText(g, tag, _fontBold, new Rectangle(r.X + 4, r.Y, TagW, r.Height), tagColor, flags);

            int right = n.Col == 1 ? 0 : CountW;

            TextRenderer.DrawText(g, n.Label, font, new Rectangle(r.X + TagW + 2, r.Y, r.Width - TagW - 4 - right, r.Height), fore, flags);

            if (n.Col != 1)
                TextRenderer.DrawText(g, n.Links.Count.ToString(), Font, new Rectangle(r.Right - CountW - 2, r.Y, CountW, r.Height),
                    st == 1 ? SystemColors.HighlightText : SystemColors.GrayText,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    // ------------------------------------------------------------------------------------------
    //  Logigramme des flags : "ce trigger pose le flag 802 -> ces triggers le lisent".
    //  De gauche à droite : les triggers qui démarrent tout seuls, puis ceux qu'ils débloquent, etc.
    //  Un clic sur un trigger allume toute sa chaîne (ce qui le débloque, ce qu'il débloque). Lecture seule.
    // ------------------------------------------------------------------------------------------

    public class FlowNode
    {
        public CampTrigger Trigger;             // null = un flag posé / lu "ailleurs" (la mission, le moteur) ou par personne
        public string Label = "";
        public int Kind;                        // 0 trigger, 1 flag lu mais posé par aucun trigger, 2 flag posé mais lu par personne
        public int Layer;
        public int Row;
        public string Flag = "";                // Kind 1 et 2 : le nom du flag
        public List<FlowEdge> In = new List<FlowEdge>();
        public List<FlowEdge> Out = new List<FlowEdge>();
        public List<string> Reads = new List<string>();
        public List<string> Sets = new List<string>();
    }

    public class FlowEdge
    {
        public FlowNode From, To;
        public string Flag = "";
        public bool Back;                       // retour en arrière (boucle) : dessiné en pointillé
    }

    public class FlowModel
    {
        public List<FlowNode> Nodes = new List<FlowNode>();
        public List<FlowEdge> Edges = new List<FlowEdge>();
        public int Layers;
        public int Rows;                        // la colonne la plus longue
        public int Loops;
        public int Unlinked;                    // triggers qui n'utilisent aucun flag
        public int ReadOnly, SetOnly;           // flags lus mais jamais posés / posés mais jamais lus

        // "802" ou "zoneA" : la clé du flag d'un appel SetCampFlag / CampFlag ; null = pas une valeur fixe
        private static string FlagName(LuaNode arg)
        {
            if (arg == null)
                return null;

            if (arg.Kind == NodeKind.Text)
                return arg.Name;

            if (arg.Kind == NodeKind.Number)
                return arg.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

            return null;
        }

        public static FlowModel Build(CampTriggersFile file)
        {
            var m = new FlowModel();
            var byTrigger = new Dictionary<CampTrigger, FlowNode>();
            var readers = new Dictionary<string, List<FlowNode>>(StringComparer.Ordinal);
            var setters = new Dictionary<string, List<FlowNode>>(StringComparer.Ordinal);

            foreach (CampTrigger t in file.Triggers)
            {
                var reads = new List<string>();
                var sets = new List<string>();

                if (t.ConditionNode != null)
                {
                    foreach (LuaNode n in t.ConditionNode.Walk())
                    {
                        if (n.Kind == NodeKind.Call && n.Name == "Return.CampFlag" && n.Items.Count > 0)
                        {
                            string f = FlagName(n.Items[0]);

                            if (f != null && !reads.Contains(f))
                                reads.Add(f);
                        }
                    }
                }

                foreach (LuaNode root in t.ActionNodes)
                {
                    if (root == null)
                        continue;

                    foreach (LuaNode n in root.Walk())
                    {
                        if (n.Kind == NodeKind.Call && (n.Name == "Action.SetCampFlag" || n.Name == "Action.AddCampFlag") && n.Items.Count > 0)
                        {
                            string f = FlagName(n.Items[0]);

                            if (f != null && !sets.Contains(f))
                                sets.Add(f);
                        }
                    }
                }

                if (reads.Count == 0 && sets.Count == 0)
                {
                    m.Unlinked++;
                    continue;
                }

                var node = new FlowNode { Trigger = t, Label = t.Name, Reads = reads, Sets = sets };
                byTrigger[t] = node;
                m.Nodes.Add(node);

                foreach (string f in reads)
                {
                    List<FlowNode> list;

                    if (!readers.TryGetValue(f, out list))
                        readers[f] = list = new List<FlowNode>();

                    list.Add(node);
                }

                foreach (string f in sets)
                {
                    List<FlowNode> list;

                    if (!setters.TryGetValue(f, out list))
                        setters[f] = list = new List<FlowNode>();

                    list.Add(node);
                }
            }

            // les liens : celui qui pose -> celui qui lit
            foreach (string f in readers.Keys.Union(setters.Keys).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList())
            {
                List<FlowNode> r, s;
                readers.TryGetValue(f, out r);
                setters.TryGetValue(f, out s);

                if (r != null && s == null)
                {
                    m.ReadOnly++;
                    var src = new FlowNode { Kind = 1, Flag = f, Label = Lang.T("Flag ") + f + Lang.T(" (set elsewhere)") };
                    m.Nodes.Add(src);

                    foreach (FlowNode reader in r)
                        m.AddEdge(src, reader, f);
                }
                else if (s != null && r == null)
                {
                    m.SetOnly++;
                    var sink = new FlowNode { Kind = 2, Flag = f, Label = Lang.T("Flag ") + f + Lang.T(" (read by nobody)") };
                    m.Nodes.Add(sink);

                    foreach (FlowNode setter in s)
                        m.AddEdge(setter, sink, f);
                }
                else if (r != null && s != null)
                {
                    foreach (FlowNode setter in s)
                    {
                        foreach (FlowNode reader in r)
                        {
                            if (!ReferenceEquals(setter, reader))
                                m.AddEdge(setter, reader, f);
                        }
                    }
                }
            }

            m.Layout();
            return m;
        }

        private void AddEdge(FlowNode from, FlowNode to, string flag)
        {
            // un seul lien par paire de triggers ; les flags en plus s'ajoutent à son étiquette
            FlowEdge same = from.Out.FirstOrDefault(e => ReferenceEquals(e.To, to));

            if (same != null)
            {
                if (!same.Flag.Split(',').Select(x => x.Trim()).Contains(flag))
                    same.Flag += ", " + flag;

                return;
            }

            var edge = new FlowEdge { From = from, To = to, Flag = flag };
            from.Out.Add(edge);
            to.In.Add(edge);
            Edges.Add(edge);
        }

        // Colonnes = le plus long chemin depuis un départ ; les boucles (retour en arrière) sont repérées et laissées de côté.
        private void Layout()
        {
            // 1) les retours en arrière : un parcours en profondeur, un lien vers un nœud encore "en cours" est une boucle
            var state = new Dictionary<FlowNode, int>();     // 1 en cours, 2 fini

            foreach (FlowNode start in Nodes.Where(n => n.In.Count == 0).Concat(Nodes))
                MarkBack(start, state);

            Loops = Edges.Count(e => e.Back);

            // 2) la colonne de chaque nœud (en ignorant les boucles)
            var layer = new Dictionary<FlowNode, int>();

            foreach (FlowNode n in Nodes)
                n.Layer = LayerOf(n, layer);

            Layers = Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Layer) + 1;

            // 3) l'ordre dans chaque colonne : on suit la moyenne des lignes des prédécesseurs (les liens se croisent moins)
            Rows = 0;

            for (int l = 0; l < Layers; l++)
            {
                List<FlowNode> col = Nodes.Where(n => n.Layer == l).ToList();

                col = col.OrderBy(n =>
                {
                    var preds = n.In.Where(e => !e.Back && e.From.Layer < l).Select(e => (double)e.From.Row).ToList();
                    return preds.Count == 0 ? double.MaxValue : preds.Average();
                }).ThenBy(n => n.Kind).ThenBy(n => n.Label, StringComparer.OrdinalIgnoreCase).ToList();

                for (int i = 0; i < col.Count; i++)
                    col[i].Row = i;

                Rows = Math.Max(Rows, col.Count);
            }
        }

        private static void MarkBack(FlowNode n, Dictionary<FlowNode, int> state)
        {
            int st;

            if (state.TryGetValue(n, out st))
                return;

            state[n] = 1;

            foreach (FlowEdge e in n.Out)
            {
                int to;

                if (state.TryGetValue(e.To, out to))
                {
                    if (to == 1)
                        e.Back = true;

                    continue;
                }

                MarkBack(e.To, state);
            }

            state[n] = 2;
        }

        private static int LayerOf(FlowNode n, Dictionary<FlowNode, int> memo)
        {
            int v;

            if (memo.TryGetValue(n, out v))
                return v;

            memo[n] = 0;
            int best = 0;

            foreach (FlowEdge e in n.In)
            {
                if (!e.Back)
                    best = Math.Max(best, LayerOf(e.From, memo) + 1);
            }

            memo[n] = best;
            return best;
        }
    }

    // Le dessin du logigramme (même principe que TriggerGraphView : seules les cases visibles sont dessinées).
    public class FlagFlowView : Control
    {
        private const int RowH = 28;
        private const int Pad = 12;
        private const int Gap = 90;
        private const int ColW = 300;

        private FlowModel _model;
        private FlowNode _selected;
        private readonly Dictionary<FlowNode, int> _state = new Dictionary<FlowNode, int>();   // 1 choisi, 2 débloque (en aval), 3 est nécessaire (en amont)
        private readonly VScrollBar _vbar = new VScrollBar { Dock = DockStyle.Right, Visible = false };
        private readonly HScrollBar _hbar = new HScrollBar { Dock = DockStyle.Bottom, Visible = false };
        private Font _fontBold;
        private Font _fontSmall;
        private readonly ToolTip _tip = new ToolTip();
        private FlowNode _tipNode;

        public event EventHandler SelectionChanged;
        public event EventHandler<FlowNode> NodeActivated;

        public FlagFlowView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

            BackColor = SystemColors.Window;
            TabStop = true;
            Controls.Add(_vbar);
            Controls.Add(_hbar);
            _vbar.ValueChanged += (s, e) => Invalidate();
            _hbar.ValueChanged += (s, e) => Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontBold != null) _fontBold.Dispose();
                if (_fontSmall != null) _fontSmall.Dispose();
                _tip.Dispose();
            }

            base.Dispose(disposing);
        }

        public FlowNode Selected { get { return _selected; } }

        public void SetModel(FlowModel model)
        {
            _model = model;
            _selected = null;
            _state.Clear();
            _vbar.Value = 0;
            _hbar.Value = 0;
            UpdateScroll();
            Invalidate();
        }

        public void SelectTrigger(CampTrigger t)
        {
            if (_model == null || t == null)
                return;

            FlowNode n = _model.Nodes.FirstOrDefault(x => ReferenceEquals(x.Trigger, t));

            if (n == null)
                return;

            Select(n);

            if (_vbar.Visible)
            {
                int target = HeadH + n.Row * RowH - ViewH / 2;
                _vbar.Value = Math.Max(0, Math.Min(target, _vbar.Maximum - _vbar.LargeChange + 1));
            }
        }

        private const int HeadH = 26;

        private int ViewW { get { return ClientSize.Width - (_vbar.Visible ? _vbar.Width : 0); } }
        private int ViewH { get { return ClientSize.Height - (_hbar.Visible ? _hbar.Height : 0); } }
        private int OffX { get { return _hbar.Visible ? _hbar.Value : 0; } }
        private int OffY { get { return _vbar.Visible ? _vbar.Value : 0; } }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_fontBold != null) { _fontBold.Dispose(); _fontBold = null; }
            if (_fontSmall != null) { _fontSmall.Dispose(); _fontSmall = null; }
        }

        private void EnsureFonts()
        {
            if (_fontBold == null) _fontBold = new Font(Font, FontStyle.Bold);
            if (_fontSmall == null) _fontSmall = new Font(Font.FontFamily, Math.Max(7f, Font.Size - 1.5f));
        }

        private void UpdateScroll()
        {
            if (_model == null || _model.Nodes.Count == 0)
            {
                _vbar.Visible = false;
                _hbar.Visible = false;
                return;
            }

            int contentH = HeadH + _model.Rows * RowH + Pad;
            int contentW = _model.Layers * (ColW + Gap) - Gap + 2 * Pad;

            bool needV = contentH > ClientSize.Height;
            bool needH = contentW > ClientSize.Width - (needV ? _vbar.Width : 0);

            if (needH)
                needV = contentH > ClientSize.Height - _hbar.Height;

            needH = contentW > ClientSize.Width - (needV ? _vbar.Width : 0);

            int viewW = ClientSize.Width - (needV ? _vbar.Width : 0);
            int viewH = ClientSize.Height - (needH ? _hbar.Height : 0);

            if (needV)
            {
                int v = Math.Max(0, Math.Min(_vbar.Value, contentH - viewH));
                _vbar.Maximum = contentH - 1;
                _vbar.LargeChange = Math.Max(1, viewH);
                _vbar.SmallChange = RowH * 2;
                _vbar.Value = Math.Max(0, Math.Min(v, _vbar.Maximum - _vbar.LargeChange + 1));
            }
            else
                _vbar.Value = 0;

            if (needH)
            {
                int v = Math.Max(0, Math.Min(_hbar.Value, contentW - viewW));
                _hbar.Maximum = contentW - 1;
                _hbar.LargeChange = Math.Max(1, viewW);
                _hbar.SmallChange = 40;
                _hbar.Value = Math.Max(0, Math.Min(v, _hbar.Maximum - _hbar.LargeChange + 1));
            }
            else
                _hbar.Value = 0;

            _vbar.Visible = needV;
            _hbar.Visible = needH;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            if (!_vbar.Visible)
                return;

            int max = _vbar.Maximum - _vbar.LargeChange + 1;
            _vbar.Value = Math.Max(0, Math.Min(_vbar.Value - e.Delta / 120 * RowH * 3, max));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);

            if (!Focused && FindForm() != null && FindForm() == Form.ActiveForm)
                Focus();
        }

        // ----- sélection : toute la chaîne, en amont et en aval -----

        private void Select(FlowNode n)
        {
            _selected = n;
            _state.Clear();

            if (n != null)
            {
                _state[n] = 1;
                Walk(n, true);       // en aval : ce qu'il débloque
                Walk(n, false);      // en amont : ce qui le débloque
            }

            Invalidate();

            if (SelectionChanged != null)
                SelectionChanged(this, EventArgs.Empty);
        }

        private void Walk(FlowNode start, bool down)
        {
            var todo = new Stack<FlowNode>();
            todo.Push(start);

            while (todo.Count > 0)
            {
                FlowNode n = todo.Pop();

                foreach (FlowEdge e in down ? n.Out : n.In)
                {
                    FlowNode next = down ? e.To : e.From;

                    if (_state.ContainsKey(next))
                        continue;

                    _state[next] = down ? 2 : 3;
                    todo.Push(next);
                }
            }
        }

        private FlowNode HitTest(Point p)
        {
            if (_model == null || p.Y < HeadH)
                return null;

            int x = p.X + OffX - Pad;
            int layer = x / (ColW + Gap);

            if (x < 0 || x % (ColW + Gap) >= ColW)
                return null;

            int row = (p.Y + OffY - HeadH) / RowH;
            return _model.Nodes.FirstOrDefault(n => n.Layer == layer && n.Row == row);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button != MouseButtons.Left || e.Clicks > 1)
                return;

            FlowNode n = HitTest(e.Location);
            Select(ReferenceEquals(n, _selected) ? null : n);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            FlowNode n = HitTest(e.Location);

            if (n != null && n.Trigger != null && NodeActivated != null)
                NodeActivated(this, n);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            FlowNode n = HitTest(e.Location);
            Cursor = n != null ? Cursors.Hand : Cursors.Default;

            if (ReferenceEquals(n, _tipNode))
                return;

            _tipNode = n;

            if (n == null)
            {
                _tip.Hide(this);
                return;
            }

            // le nom en entier (les cases le coupent) + les flags lus / posés
            string text = n.Label;

            if (n.Reads.Count > 0)
                text += "\r\n" + Lang.T("Reads flags : ") + string.Join(", ", n.Reads.ToArray());

            if (n.Sets.Count > 0)
                text += "\r\n" + Lang.T("Sets flags : ") + string.Join(", ", n.Sets.ToArray());

            _tip.Show(text, this, e.X + 16, e.Y + 20, 8000);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _tipNode = null;
            _tip.Hide(this);
        }

        // ----- texte d'information (sous le logigramme) -----

        private static string Names(IEnumerable<FlowNode> nodes, bool withFlags, bool incoming)
        {
            const int max = 6;
            List<FlowNode> list = nodes.ToList();

            if (list.Count == 0)
                return "-";

            string text = string.Join(", ", list.Take(max).Select(n => n.Label).ToArray());
            return list.Count > max ? text + Lang.T("  (+") + (list.Count - max) + Lang.T(" more)") : text;
        }

        public string Describe()
        {
            if (_model == null)
                return "";

            var sb = new StringBuilder();

            if (_selected == null)
            {
                sb.AppendLine(_model.Nodes.Count(n => n.Kind == 0) + Lang.T(" triggers linked by flags  |  ") + _model.Edges.Count + Lang.T(" links  |  ") + _model.Unlinked + Lang.T(" triggers use no flag (not shown)"));
                sb.AppendLine(_model.ReadOnly + Lang.T(" flags read but set by no trigger (grey boxes on the left)  |  ") + _model.SetOnly + Lang.T(" flags set but read by no trigger (grey boxes on the right)"));

                if (_model.Loops > 0)
                    sb.AppendLine(_model.Loops + Lang.T(" loop(s): a chain that comes back on itself (dashed links)."));

                sb.Append(Lang.T("Click a box to light up its whole chain."));
                return sb.ToString();
            }

            FlowNode s = _selected;

            if (s.Trigger != null)
            {
                sb.AppendLine(Lang.T("TRIGGER  ") + s.Label);
                sb.AppendLine(Lang.T("Reads flags : ") + (s.Reads.Count == 0 ? "-" : string.Join(", ", s.Reads.ToArray())) + Lang.T("   |   sets flags : ") + (s.Sets.Count == 0 ? "-" : string.Join(", ", s.Sets.ToArray())));
            }
            else
            {
                sb.AppendLine(s.Label);
            }

            sb.AppendLine(Lang.T("Needs, directly (they set a flag it reads) : ") + Names(s.In.Select(e => e.From), true, true));
            sb.AppendLine(Lang.T("Unlocks, directly (they read a flag it sets) : ") + Names(s.Out.Select(e => e.To), true, false));
            sb.AppendLine(Lang.T("Whole chain : ") + _state.Count(k => k.Value == 3) + Lang.T(" before, ") + _state.Count(k => k.Value == 2) + Lang.T(" after"));

            if (s.Trigger != null)
                sb.Append(Lang.T("Double-click to edit it in LIST."));

            return sb.ToString().TrimEnd();
        }

        // ----- dessin -----

        private static Color Blend(Color a, Color b, double part)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * part), (int)(a.G + (b.G - a.G) * part), (int)(a.B + (b.B - a.B) * part));
        }

        private Rectangle BoxOf(FlowNode n, int offX, int offY)
        {
            return new Rectangle(Pad + n.Layer * (ColW + Gap) - offX, HeadH + n.Row * RowH - offY + 2, ColW, RowH - 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);

            if (_model == null)
                return;

            if (_model.Nodes.Count == 0)
            {
                TextRenderer.DrawText(g, Lang.T("No trigger uses a flag in this campaign."), Font, ClientRectangle, SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            EnsureFonts();
            int offX = OffX, offY = OffY, viewW = ViewW, viewH = ViewH;
            bool sel = _selected != null;

            // --- liens ---
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var hot = new List<FlowEdge>();

            using (var faint = new Pen(Color.FromArgb(sel ? 12 : 70, SystemColors.ControlDarkDark), 1f))
            using (var faintBack = new Pen(Color.FromArgb(sel ? 12 : 70, SystemColors.ControlDarkDark), 1f) { DashStyle = DashStyle.Dash })
            {
                foreach (FlowEdge ed in _model.Edges)
                {
                    if (sel && _state.ContainsKey(ed.From) && _state.ContainsKey(ed.To))
                    {
                        hot.Add(ed);
                        continue;
                    }

                    DrawEdge(g, ed.Back ? faintBack : faint, ed, offX, offY, viewH);
                }
            }

            using (var bright = new Pen(SystemColors.Highlight, 2f))
            using (var brightBack = new Pen(SystemColors.Highlight, 2f) { DashStyle = DashStyle.Dash })
            {
                foreach (FlowEdge ed in hot)
                    DrawEdge(g, ed.Back ? brightBack : bright, ed, offX, offY, viewH);
            }

            g.SmoothingMode = SmoothingMode.Default;

            // --- étiquettes des flags sur les liens allumés : UNE par case d'arrivée (les mêmes flags ne s'empilent plus) ---
            foreach (var grp in hot.GroupBy(ed => ed.To))
            {
                Rectangle b = BoxOf(grp.Key, offX, offY);
                float ym = b.Y + b.Height / 2f;

                if (ym < HeadH || ym > viewH || b.X < 0 || b.X > viewW)
                    continue;

                string text = string.Join(", ", grp.SelectMany(ed => ed.Flag.Split(',')).Select(x => x.Trim()).Distinct().ToArray());
                var size = TextRenderer.MeasureText(text, _fontSmall);
                var r = new Rectangle(b.X - size.Width - 8, (int)ym - size.Height / 2, size.Width + 4, size.Height);

                using (var back = new SolidBrush(SystemColors.Window))
                    g.FillRectangle(back, r);

                TextRenderer.DrawText(g, text, _fontSmall, r, SystemColors.HotTrack, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // --- cases (seulement celles qu'on voit) ---
            foreach (FlowNode n in _model.Nodes)
            {
                Rectangle r = BoxOf(n, offX, offY);

                if (r.Bottom < HeadH || r.Y > viewH || r.Right < 0 || r.X > viewW)
                    continue;

                DrawNode(g, n, r);
            }

            // --- titres de colonnes (fixes) ---
            using (var back = new SolidBrush(SystemColors.Control))
            using (var line = new Pen(SystemColors.ControlDark))
            {
                g.FillRectangle(back, 0, 0, viewW, HeadH);
                g.DrawLine(line, 0, HeadH - 1, viewW, HeadH - 1);
            }

            for (int l = 0; l < _model.Layers; l++)
            {
                int x = Pad + l * (ColW + Gap) - offX;

                if (x + ColW < 0 || x > viewW)
                    continue;

                string head = l == 0 ? Lang.T("START") : Lang.T("STEP ") + (l + 1);
                TextRenderer.DrawText(g, head, _fontBold, new Rectangle(x, 0, ColW, HeadH), SystemColors.ControlText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        private void DrawEdge(Graphics g, Pen pen, FlowEdge ed, int offX, int offY, int viewH)
        {
            Rectangle a = BoxOf(ed.From, offX, offY), b = BoxOf(ed.To, offX, offY);
            float ya = a.Y + a.Height / 2f, yb = b.Y + b.Height / 2f;

            if (Math.Max(ya, yb) < HeadH || Math.Min(ya, yb) > viewH)
                return;

            if (ed.Back || b.X <= a.X)
            {
                // un retour en arrière : on sort par la droite de la case, on rentre par sa gauche, en faisant un grand arrondi
                float xa = a.Right, xb = b.X;
                g.DrawBezier(pen, xa, ya, xa + Gap, ya, xb - Gap, yb, xb, yb);
                return;
            }

            float xs = a.Right, xe = b.X, mid = (xe - xs) / 2f;
            g.DrawBezier(pen, xs, ya, xs + mid, ya, xe - mid, yb, xe, yb);
        }

        private void DrawNode(Graphics g, FlowNode n, Rectangle r)
        {
            int st;
            _state.TryGetValue(n, out st);

            Color back = SystemColors.Window, border = SystemColors.ControlDark, fore = SystemColors.WindowText;
            Font font = Font;

            if (n.Kind != 0)
            {
                back = SystemColors.Control;                // les flags posés / lus "ailleurs" : cases grises
                fore = SystemColors.GrayText;
            }

            if (st == 1)
            {
                back = SystemColors.Highlight; border = SystemColors.Highlight; fore = SystemColors.HighlightText; font = _fontBold;
            }
            else if (st == 2)
            {
                back = Blend(SystemColors.Window, SystemColors.Highlight, 0.25); border = SystemColors.Highlight;
            }
            else if (st == 3)
            {
                back = Blend(SystemColors.Window, SystemColors.Highlight, 0.10); border = SystemColors.Highlight;
            }
            else if (_selected != null)
                fore = SystemColors.GrayText;

            using (var brush = new SolidBrush(back))
                g.FillRectangle(brush, r);

            using (var pen = new Pen(border))
            {
                if (n.Kind != 0)
                    pen.DashStyle = DashStyle.Dash;

                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            string tag = n.Trigger != null ? (n.Trigger.ErrorCount > 0 ? "!" : n.Trigger.WarningCount > 0 ? "?" : "") : "";
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            if (tag.Length > 0)
                TextRenderer.DrawText(g, tag, _fontBold, new Rectangle(r.X + 4, r.Y, 16, r.Height), st == 1 ? SystemColors.HighlightText : SystemColors.HotTrack, flags);

            TextRenderer.DrawText(g, n.Label, font, new Rectangle(r.X + 20, r.Y, r.Width - 24, r.Height), fore, flags);
        }
    }

    // L'onglet GRAPH de la fenêtre (partie de Triggers_Form).
    public partial class Triggers_Form
    {
        private Panel _panelGraph;
        private TriggerGraphView _graphView;
        private FlagFlowView _flowView;                  // le logigramme des flags (l'autre vue du même onglet)
        private ComboBox _comboGraphMode;
        private Label _labelGraphHint;
        private Label _labelGraphInfo;
        private RadioButton _tabList;
        private RadioButton _tabCode;
        private RadioButton _tabGraph;

        private void BuildGraphPanel()
        {
            _panelGraph = new Panel { Dock = DockStyle.Fill, Visible = false };

            // en haut : le choix de la vue + une phrase d'aide
            var top = new Panel { Dock = DockStyle.Top, Height = 30 };

            _comboGraphMode = new ComboBox { Left = 10, Top = 3, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            _comboGraphMode.Items.AddRange(new object[] { Lang.T("View: conditions > triggers > results"), Lang.T("View: flag flowchart") });
            _comboGraphMode.SelectedIndex = 0;
            _comboGraphMode.SelectedIndexChanged += (s, e) => ShowGraph();
            _toolTip.SetToolTip(_comboGraphMode, Lang.T("Conditions > triggers > results: what each trigger reads and changes. Flag flowchart: which trigger sets a flag that another one reads, step by step."));

            var hint = new Label
            {
                Left = 270,
                Top = 7,
                Height = 20,
                Width = 600,
                ForeColor = SystemColors.GrayText,
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = Lang.T("Click an item to follow its links (flags are followed from one trigger to the next). Double-click a trigger to edit it. M mission, D date, F flag, T target, A air unit, B base, E end, ! error.")
            };

            _labelGraphHint = hint;
            top.Controls.Add(_comboGraphMode);
            top.Controls.Add(hint);
            top.Resize += (s, e) => hint.Width = Math.Max(100, top.ClientSize.Width - hint.Left - 8);

            _labelGraphInfo = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 92,
                Padding = new Padding(10, 6, 10, 4),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Control
            };

            _graphView = new TriggerGraphView { Dock = DockStyle.Fill };
            _graphView.SelectionChanged += (s, e) => _labelGraphInfo.Text = _graphView.Describe();
            _graphView.NodeActivated += (s, n) =>
            {
                CampTrigger t = n.Trigger;
                BeginInvoke(new Action(() => OpenTriggerInList(t)));
            };

            _flowView = new FlagFlowView { Dock = DockStyle.Fill, Visible = false };
            _flowView.SelectionChanged += (s, e) => _labelGraphInfo.Text = _flowView.Describe();
            _flowView.NodeActivated += (s, n) =>
            {
                CampTrigger t = n.Trigger;
                BeginInvoke(new Action(() => OpenTriggerInList(t)));
            };

            // même règle que partout : d'abord le Fill, ensuite Bottom / Top
            _panelGraph.Controls.Add(_graphView);
            _panelGraph.Controls.Add(_flowView);
            _panelGraph.Controls.Add(_labelGraphInfo);
            _panelGraph.Controls.Add(top);
        }

        private void ShowGraph()
        {
            BeginBusy();

            try
            {
                bool flow = _comboGraphMode.SelectedIndex == 1;
                _graphView.Visible = !flow;
                _flowView.Visible = flow;
                _labelGraphHint.Text = flow
                    ? Lang.T("Each box is a trigger. An arrow goes from the trigger that sets a flag to the ones that read it. Click a box to light up its chain. Double-click to edit it.")
                    : Lang.T("Click an item to follow its links (flags are followed from one trigger to the next). Double-click a trigger to edit it. M mission, D date, F flag, T target, A air unit, B base, E end, ! error.");

                if (_file == null || _file.LoadError != null)
                {
                    _graphView.SetModel(null);
                    _flowView.SetModel(null);
                    _labelGraphInfo.Text = _file == null ? "" : Lang.T("READ ERROR\r\n") + _file.LoadError;
                    return;
                }

                if (flow)
                {
                    _flowView.SetModel(FlowModel.Build(_file));
                    _flowView.SelectTrigger(_editing);
                    _labelGraphInfo.Text = _flowView.Describe();
                }
                else
                {
                    _graphView.SetModel(GraphModel.Build(_file));
                    _graphView.SelectTrigger(_editing);               // le trigger choisi dans LIST l'est aussi ici
                    _labelGraphInfo.Text = _graphView.Describe();
                }
            }
            catch (Exception ex)
            {
                _graphView.SetModel(null);
                _flowView.SetModel(null);
                _labelGraphInfo.Text = Lang.T("Could not build the graph: ") + ex.Message;
                FormUtils.LogRegister("Triggers_Form | GRAPH : " + ex);
            }
            finally
            {
                EndBusy();
            }
        }

        // Double-clic dans le graphe : retour à LIST sur ce trigger.
        private void OpenTriggerInList(CampTrigger t)
        {
            if (t == null || _file == null)
                return;

            _tabList.Checked = true;
            RefillList(t);

            // caché par le filtre ? on l'enlève pour pouvoir le montrer
            if (!ReferenceEquals(_listTriggers.SelectedItem, t))
            {
                _textFilter.Text = "";
                _textFunc.Text = "";
                _checkOnlyProblems.Checked = false;
                _filterTimer.Stop();
                RefillList(t);
            }
        }
    }
}
