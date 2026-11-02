using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Une base à dessiner sur la carte (données déjà prêtes, pas de lecture de fichier ici)
    internal class Map_Base
    {
        public string Name;
        public double X;        // coordonnée DCS x = vers le NORD
        public double Y;        // coordonnée DCS y = vers l'EST
        public string Side;     // "blue" / "red" / autre
        public bool Inactive;
        public bool IsFarp;
        public int Squads;      // nombre de squadrons basés ici
        public int Aircraft;    // nombre d'avions : donne la taille de la silhouette
        public string Alias = "";   // nom de remplacement (various_table.lua), vide si aucun
        public string ZoneSide = "";    // camp qui tient la zone wargame où se trouve la base ("blue" / "red" / "other"), vide = pas de zone : alors on prend Side
    }

    // Carte "vectorielle" de toute la map DCS : un point par base, sans image de fond.
    // Les positions viennent de db_airbases.lua, donc aucun décalage possible.
    // Molette = zoom, glisser = déplacer, clic = choisir une base.
    internal class Map_UI_View : UserControl
    {
        // Envoyé quand on clique sur une base (nom de la base)
        public event Action<string> BaseSelected;

        private List<Map_Base> _bases = new List<Map_Base>();
        private string _selectedName = "";

        // Transformation monde -> écran :  sx = _ox + y * _scale   et   sy = _oy - x * _scale
        // (DCS : x = nord, y = est ; à l'écran le nord est en haut)
        private float _scale = 0.002f;
        private float _fitScale = 0.002f;
        private float _ox, _oy;
        private bool _userMoved;        // vrai dès qu'on zoome ou déplace : on ne recadre plus tout seul

        // Déplacement à la souris
        private Point _lastMouse;
        private Point _downPoint;
        private bool _panning;

        // Règle (comme la carte F10 de DCS)
        private bool _rulerEnabled;
        private bool _hasA, _hasB;
        private double _ax, _ay, _bx, _by;
        private Point _mouse = Point.Empty;

        // Fond de carte pris sur le web (tuiles OpenStreetMap), assombri pour garder les noms lisibles
        private Map_Background _bg;
        private bool _showBg;
        private readonly Dictionary<string, List<Map_Drawing>> _drawingGroups = new Dictionary<string, List<Map_Drawing>>();
        private readonly HashSet<string> _shownGroups = new HashSet<string>();
        private static readonly string[] GroupOrder = { "spawn", "zones", "mission", "threats", "plans" };      // du dessous au dessus

        // Tuiles plus fines : on attend que la vue ne bouge plus avant de les demander
        private readonly Timer _detailTimer = new Timer { Interval = 300 };
        private float _seenScale, _seenOx, _seenOy;
        private int _seenW, _seenH;
        private Map_Detail _hookedDetail;
        private ImageAttributes _dim;                                       // juste le mode de bord (pas de lignes entre les tuiles)
        private readonly SolidBrush _dimBrush = new SolidBrush(Color.FromArgb(115, 0, 0, 0));      // voile qui assombrit le fond

        private readonly Font _fontLabel = new Font("Segoe UI", 8.5f);
        private readonly Font _fontAlias = new Font("Segoe UI", 12f, FontStyle.Bold);
        private readonly Font _fontBold = new Font("Segoe UI", 9f, FontStyle.Bold);

        private static readonly Color ColBlue = Color.FromArgb(70, 140, 255);
        private static readonly Color ColRed = Color.FromArgb(235, 70, 70);
        private static readonly Color ColOther = Color.FromArgb(170, 170, 170);

        public Map_UI_View()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(26, 34, 46);

            _detailTimer.Tick += DetailTick;
            _dim = new ImageAttributes();
            _dim.SetWrapMode(WrapMode.TileFlipXY);      // évite les fines lignes entre les tuiles
        }

        // ------------------------------------------------------------------
        //  API publique
        // ------------------------------------------------------------------

        public string SelectedName
        {
            get { return _selectedName; }
            set { _selectedName = value ?? ""; Invalidate(); }
        }

        // Le fond appartient à la fenêtre (c'est elle qui le libère)
        public void SetBackground(Map_Background bg)
        {
            if (_hookedDetail != null) _hookedDetail.Changed -= OnDetailChanged;
            _bg = bg;
            _hookedDetail = bg != null ? bg.Detail : null;
            if (_hookedDetail != null) _hookedDetail.Changed += OnDetailChanged;

            _seenW = -1;            // force une nouvelle demande de tuiles
            Invalidate();

            if (bg != null && bg.Mosaic == null)
                StartMosaic(bg);
        }

        // Construit la mosaïque en arrière-plan ; en attendant, les tuiles sont dessinées une à une (plus lent)
        private void StartMosaic(Map_Background bg)
        {
            Task.Run(() =>
            {
                try
                {
                    double minX, maxX, minY, maxY, pix;
                    Bitmap mosaic = Map_Web.BuildMosaic(bg, out minX, out maxX, out minY, out maxY, out pix);

                    if (IsDisposed || !IsHandleCreated || bg.Disposed)
                    {
                        mosaic.Dispose();
                        return;
                    }
                    BeginInvoke(new Action(() =>
                    {
                        bg.AttachMosaic(mosaic, minX, maxX, minY, maxY, pix);
                        Invalidate();
                    }));
                }
                catch (Exception ex)
                {
                    DCE_Manager.Utils.FormUtils.LogRegister("Map_UI_View | mosaïque : " + ex.Message);
                }
            });
        }

        // Une tuile fine vient d'arriver (thread de téléchargement) : on redessine
        private void OnDetailChanged()
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(Invalidate));
            }
            catch { }
        }

        private void DetailTick(object sender, EventArgs e)
        {
            _detailTimer.Stop();
            if (!_showBg || _bg == null || _bg.Detail == null) return;

            _bg.Detail.Update(_bg.Detail.ZoomFor(_scale), ToScreen, ClientRectangle);
            Invalidate();
        }

        // Un groupe de tracés (mission / zones / spawn)
        public void SetDrawings(string group, List<Map_Drawing> list)
        {
            _drawingGroups[group] = list ?? new List<Map_Drawing>();
            Invalidate();
        }

        public void SetDrawingsVisible(string group, bool visible)
        {
            if (visible) _shownGroups.Add(group);
            else _shownGroups.Remove(group);
            Invalidate();
        }

        public bool ShowBackground
        {
            get { return _showBg; }
            set { _showBg = value; _seenW = -1; Invalidate(); }
        }

        public bool RulerEnabled
        {
            get { return _rulerEnabled; }
            set
            {
                _rulerEnabled = value;
                ClearRuler();
                Cursor = value ? Cursors.Cross : Cursors.Default;
            }
        }

        // Remplace les bases affichées. On ne recadre que si l'utilisateur n'a pas encore zoomé.
        public void SetBases(List<Map_Base> bases)
        {
            _bases = bases ?? new List<Map_Base>();
            if (!_userMoved)
                FitAll();
            Invalidate();
        }

        // Tout remettre dans la fenêtre
        public void ResetZoom()
        {
            FitAll();
            Invalidate();
        }

        public void ClearRuler()
        {
            _hasA = false;
            _hasB = false;
            Invalidate();
        }

        // ------------------------------------------------------------------
        //  Transformations
        // ------------------------------------------------------------------

        private PointF ToScreen(double x, double y)
        {
            return new PointF(_ox + (float)(y * _scale), _oy - (float)(x * _scale));
        }

        private void ToWorld(float sx, float sy, out double x, out double y)
        {
            y = (sx - _ox) / _scale;
            x = (_oy - sy) / _scale;
        }

        private void FitAll()
        {
            if (Width < 20 || Height < 20)
                return;

            if (_bases.Count == 0)
            {
                _scale = _fitScale = 0.002f;
                _ox = Width / 2f;
                _oy = Height / 2f;
                _userMoved = false;
                return;
            }

            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (Map_Base b in _bases)
            {
                if (b.X < minX) minX = b.X;
                if (b.X > maxX) maxX = b.X;
                if (b.Y < minY) minY = b.Y;
                if (b.Y > maxY) maxY = b.Y;
            }

            double rangeX = Math.Max(maxX - minX, 20000);   // au moins 20 km, pour une seule base
            double rangeY = Math.Max(maxY - minY, 20000);
            const float margin = 70f;

            _scale = (float)Math.Min((Width - 2 * margin) / rangeY, (Height - 2 * margin) / rangeX);
            _fitScale = _scale;

            double cx = (minX + maxX) / 2.0;
            double cy = (minY + maxY) / 2.0;
            _ox = Width / 2f - (float)(cy * _scale);
            _oy = Height / 2f + (float)(cx * _scale);
            _userMoved = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_userMoved)
                FitAll();
        }

        // ------------------------------------------------------------------
        //  Dessin
        // ------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            // La vue a bougé (zoom, déplacement, taille) : on redemandera les tuiles fines dans un instant
            if (_scale != _seenScale || _ox != _seenOx || _oy != _seenOy || Width != _seenW || Height != _seenH)
            {
                _seenScale = _scale; _seenOx = _ox; _seenOy = _oy; _seenW = Width; _seenH = Height;
                _detailTimer.Stop();
                _detailTimer.Start();
            }

            bool bgDrawn = DrawBackground(g);
            double stepKm = DrawGrid(g);
            try { DrawDrawings(g); }
            catch (Exception ex) { DCE_Manager.Utils.FormUtils.LogRegister("Map_UI_View | tracés : " + ex.Message); }

            // La base choisie est dessinée en dernier, pour rester visible
            Map_Base selected = null;
            foreach (Map_Base b in _bases)
            {
                if (string.Equals(b.Name, _selectedName, StringComparison.OrdinalIgnoreCase))
                {
                    selected = b;
                    continue;
                }
                DrawBase(g, b, false);
            }
            if (selected != null)
                DrawBase(g, selected, true);

            DrawRuler(g);
            DrawScaleBar(g, stepKm);

            if (bgDrawn)
            {
                string attr = _bg != null && _bg.Attribution != "" ? _bg.Attribution : "© OpenStreetMap contributors";
                SizeF sz = g.MeasureString(attr, _fontLabel);
                g.DrawString(attr, _fontLabel, Brushes.Gainsboro, Width - sz.Width - 6, Height - 20);
            }

            if (_bases.Count == 0)
                g.DrawString("No airbase with a position found for this campaign.", _fontBold, Brushes.Gainsboro, 20, 20);
        }

        // Tuiles du fond : chaque tuile est un petit parallélogramme (ses 3 coins sont déjà en coordonnées DCS)
        private bool DrawBackground(Graphics g)
        {
            if (!_showBg || _bg == null || (_bg.Mosaic == null && _bg.Tiles.Count == 0))
                return false;

            // Pendant un déplacement / zoom, on dessine en qualité réduite : bien plus fluide
            bool moving = _detailTimer.Enabled;
            g.InterpolationMode = moving ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            if (_bg.Mosaic != null) DrawMosaic(g);
            else DrawTiles(g, _bg.Tiles);

            if (_bg.Detail != null) DrawTiles(g, _bg.Detail.DrawList);

            g.FillRectangle(_dimBrush, ClientRectangle);        // assombrit le fond pour garder les noms lisibles
            return true;
        }

        // La mosaïque est alignée sur le nord : seule la partie visible est dessinée, en une seule opération
        private void DrawMosaic(Graphics g)
        {
            Map_Background bg = _bg;
            double pix = bg.MosaicPix;
            int w = bg.Mosaic.Width, h = bg.Mosaic.Height;

            // Partie visible de l'écran, en pixels de la mosaïque
            double left = ((0 - _ox) / _scale - bg.MosaicMinY) / pix;
            double right = ((Width - _ox) / _scale - bg.MosaicMinY) / pix;
            double top = (bg.MosaicMaxX - (_oy - 0) / _scale) / pix;
            double bottom = (bg.MosaicMaxX - (_oy - Height) / _scale) / pix;

            left = Math.Max(0, Math.Floor(left)); top = Math.Max(0, Math.Floor(top));
            right = Math.Min(w, Math.Ceiling(right)); bottom = Math.Min(h, Math.Ceiling(bottom));
            if (right <= left || bottom <= top) return;

            // Où ce morceau tombe à l'écran
            float dl = _ox + (float)((bg.MosaicMinY + left * pix) * _scale);
            float dt = _oy - (float)((bg.MosaicMaxX - top * pix) * _scale);
            float dw = (float)((right - left) * pix * _scale);
            float dh = (float)((bottom - top) * pix * _scale);

            g.DrawImage(bg.Mosaic, new PointF[] { new PointF(dl, dt), new PointF(dl + dw, dt), new PointF(dl, dt + dh) },
                new RectangleF((float)left, (float)top, (float)(right - left), (float)(bottom - top)), GraphicsUnit.Pixel, _dim);
        }

        // Frontières et zones dessinées dans l'éditeur de mission
        private void DrawDrawings(Graphics g)
        {
            foreach (string group in GroupOrder)
            {
                List<Map_Drawing> list;
                if (_shownGroups.Contains(group) && _drawingGroups.TryGetValue(group, out list))
                    DrawGroup(g, list);
            }
        }

        private void DrawGroup(Graphics g, List<Map_Drawing> list)
        {
            foreach (Map_Drawing d in list)
            {
                if (d.Points.Count == 0) continue;

                if (d.Kind == "Text")
                {
                    PointF tp = ToScreen(d.Points[0][0], d.Points[0][1]);
                    g.DrawString(d.Text, _fontBold, Brushes.Black, tp.X + 1, tp.Y + 1);
                    g.DrawString(d.Text, _fontBold, new SolidBrush(Color.FromArgb(255, d.Line)), tp.X, tp.Y);
                    continue;
                }

                if (d.Kind == "Circle")
                {
                    PointF c = ToScreen(d.Points[0][0], d.Points[0][1]);
                    float rr = (float)(d.Radius * _scale);
                    if (rr < 1f || rr > 20000f || !Finite(c) || Math.Abs(c.X) > 1e6f || Math.Abs(c.Y) > 1e6f) continue;
                    if (d.Fill.A > 0)
                        using (var fb = new SolidBrush(d.Fill))
                            g.FillEllipse(fb, c.X - rr, c.Y - rr, rr * 2, rr * 2);
                    using (var cp = new Pen(d.Line, d.Thickness))
                    {
                        if (d.Dashed) cp.DashStyle = DashStyle.Dash;
                        g.DrawEllipse(cp, c.X - rr, c.Y - rr, rr * 2, rr * 2);
                    }
                    if (d.Label != "" && rr > 25)
                    {
                        SizeF csz = g.MeasureString(d.Label, _fontBold);
                        g.DrawString(d.Label, _fontBold, Brushes.Black, c.X - csz.Width / 2 + 1, c.Y - csz.Height / 2 + 1);
                        g.DrawString(d.Label, _fontBold, Brushes.White, c.X - csz.Width / 2, c.Y - csz.Height / 2);
                    }
                    continue;
                }

                var pts = new PointF[d.Points.Count];
                for (int i = 0; i < pts.Length; i++)
                    pts[i] = ToScreen(d.Points[i][0], d.Points[i][1]);

                if (d.Closed && d.Fill.A > 0 && pts.Length >= 3 && pts.All(Finite))
                {
                    PointF[] clipped = ClipPolygon(pts, ClipArea);
                    if (clipped.Length >= 3)
                        using (var fb = new SolidBrush(d.Fill))
                            g.FillPolygon(fb, clipped);
                }

                using (var pen = new Pen(d.Line, d.Thickness))
                {
                    if (d.Dashed) pen.DashStyle = DashStyle.Dash;
                    DrawClippedLines(g, pen, pts, d.Closed && pts.Length >= 3);
                }

                // Nom de la zone au milieu (seulement si elle est assez grande à l'écran)
                if (d.Label != "" && pts.Length >= 3 && pts.All(Finite))
                {
                    float minX = pts.Min(q => q.X), maxX = pts.Max(q => q.X);
                    if (maxX - minX > 50)
                    {
                        PointF mid = new PointF(pts.Average(q => q.X), pts.Average(q => q.Y));
                        SizeF sz = g.MeasureString(d.Label, _fontBold);
                        float lx = mid.X - sz.Width / 2, ly = mid.Y - sz.Height / 2;
                        g.DrawString(d.Label, _fontBold, Brushes.Black, lx + 1, ly + 1);
                        g.DrawString(d.Label, _fontBold, Brushes.White, lx, ly);
                    }
                }
            }
        }


        // ------------------------------------------------------------------
        //  Découpe des tracés : GDI+ refuse (ArgumentException) les coordonnées géantes,
        //  ce qui arrive dès qu'on zoome sur une frontière de 800 km. On ne dessine donc que ce qui touche l'écran.
        // ------------------------------------------------------------------

        private RectangleF ClipArea
        {
            get { return new RectangleF(-60, -60, Width + 120, Height + 120); }
        }

        private static bool Finite(PointF p)
        {
            return !(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y));
        }

        // Liang-Barsky : garde la partie du segment qui est dans le rectangle
        private static bool ClipSegment(PointF a, PointF b, RectangleF r, out PointF ca, out PointF cb)
        {
            ca = a; cb = b;
            double dx = b.X - a.X, dy = b.Y - a.Y, t0 = 0, t1 = 1;
            double[] p = { -dx, dx, -dy, dy };
            double[] q = { a.X - r.Left, r.Right - a.X, a.Y - r.Top, r.Bottom - a.Y };

            for (int i = 0; i < 4; i++)
            {
                if (p[i] == 0)
                {
                    if (q[i] < 0) return false;
                    continue;
                }
                double t = q[i] / p[i];
                if (p[i] < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            ca = new PointF((float)(a.X + t0 * dx), (float)(a.Y + t0 * dy));
            cb = new PointF((float)(a.X + t1 * dx), (float)(a.Y + t1 * dy));
            return true;
        }

        private void DrawClippedLines(Graphics g, Pen pen, PointF[] pts, bool closed)
        {
            RectangleF r = ClipArea;
            var run = new List<PointF>();
            int n = pts.Length;
            int segments = closed ? n : n - 1;

            for (int i = 0; i < segments; i++)
            {
                PointF a = pts[i], b = pts[(i + 1) % n];
                PointF ca, cb;

                if (!Finite(a) || !Finite(b) || !ClipSegment(a, b, r, out ca, out cb))
                {
                    FlushRun(g, pen, run);
                    continue;
                }

                if (ca == a && cb == b)
                {
                    if (run.Count == 0) run.Add(a);
                    run.Add(b);
                }
                else
                {
                    FlushRun(g, pen, run);
                    g.DrawLine(pen, ca, cb);
                }
            }
            FlushRun(g, pen, run);
        }

        private static void FlushRun(Graphics g, Pen pen, List<PointF> run)
        {
            if (run.Count >= 2) g.DrawLines(pen, run.ToArray());
            run.Clear();
        }

        // Sutherland-Hodgman : polygone découpé aux bords du rectangle (pour le remplissage)
        private static PointF[] ClipPolygon(PointF[] poly, RectangleF r)
        {
            List<PointF> cur = new List<PointF>(poly);
            for (int edge = 0; edge < 4 && cur.Count > 0; edge++)
            {
                var next = new List<PointF>();
                for (int i = 0; i < cur.Count; i++)
                {
                    PointF a = cur[i], b = cur[(i + 1) % cur.Count];
                    bool ina = Inside(a, edge, r), inb = Inside(b, edge, r);
                    if (ina) next.Add(a);
                    if (ina != inb) next.Add(Cross(a, b, edge, r));
                }
                cur = next;
            }
            return cur.ToArray();
        }

        private static bool Inside(PointF p, int edge, RectangleF r)
        {
            switch (edge)
            {
                case 0: return p.X >= r.Left;
                case 1: return p.X <= r.Right;
                case 2: return p.Y >= r.Top;
                default: return p.Y <= r.Bottom;
            }
        }

        private static PointF Cross(PointF a, PointF b, int edge, RectangleF r)
        {
            float x = edge == 0 ? r.Left : r.Right;
            float y = edge == 2 ? r.Top : r.Bottom;
            if (edge <= 1)
            {
                float t = (x - a.X) / (b.X - a.X);
                return new PointF(x, a.Y + t * (b.Y - a.Y));
            }
            else
            {
                float t = (y - a.Y) / (b.Y - a.Y);
                return new PointF(a.X + t * (b.X - a.X), y);
            }
        }

        private void DrawTiles(Graphics g, List<Map_Tile> tiles)
        {
            foreach (Map_Tile t in tiles)
            {
                PointF tl = ToScreen(t.TlX, t.TlY);
                PointF tr = ToScreen(t.TrX, t.TrY);
                PointF bl = ToScreen(t.BlX, t.BlY);

                // Hors de l'écran : on ne dessine pas
                float minX = Math.Min(tl.X, Math.Min(tr.X, bl.X));
                float maxX = Math.Max(tl.X, Math.Max(tr.X, bl.X + (tr.X - tl.X)));
                float minY = Math.Min(tl.Y, Math.Min(tr.Y, bl.Y));
                float maxY = Math.Max(tl.Y, Math.Max(tr.Y, bl.Y + (tr.Y - tl.Y)));
                if (maxX < 0 || minX > Width || maxY < 0 || minY > Height)
                    continue;

                g.DrawImage(t.Image, new PointF[] { tl, tr, bl }, t.Src, GraphicsUnit.Pixel, _dim);
            }
        }

        // Quadrillage léger. Renvoie le pas choisi (en km), réutilisé par l'échelle.
        private double DrawGrid(Graphics g)
        {
            double[] steps = { 5, 10, 20, 50, 100, 200, 500 };
            double stepKm = steps[steps.Length - 1];
            foreach (double s in steps)
            {
                if (s * 1000 * _scale >= 70) { stepKm = s; break; }
            }
            double stepM = stepKm * 1000;

            double xTop, yLeft, xBottom, yRight;
            ToWorld(0, 0, out xTop, out yLeft);
            ToWorld(Width, Height, out xBottom, out yRight);

            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            {
                for (double y = Math.Floor(yLeft / stepM) * stepM; y <= yRight; y += stepM)
                {
                    float sx = ToScreen(0, y).X;
                    g.DrawLine(pen, sx, 0, sx, Height);
                }
                for (double x = Math.Floor(xBottom / stepM) * stepM; x <= xTop; x += stepM)
                {
                    float sy = ToScreen(x, 0).Y;
                    g.DrawLine(pen, 0, sy, Width, sy);
                }
            }
            return stepKm;
        }

        private void DrawScaleBar(Graphics g, double stepKm)
        {
            float len = (float)(stepKm * 1000 * _scale);
            float x = 14, y = Height - 22;
            g.DrawLine(Pens.Gainsboro, x, y, x + len, y);
            g.DrawLine(Pens.Gainsboro, x, y - 4, x, y + 4);
            g.DrawLine(Pens.Gainsboro, x + len, y - 4, x + len, y + 4);
            g.DrawString(stepKm + " km", _fontLabel, Brushes.Gainsboro, x + len + 6, y - 9);
        }

        private static Color SideColor(Map_Base b)
        {
            // La zone wargame prime sur le camp inscrit dans db_airbases (base capturée = couleur du vainqueur, même inactive)
            string s = string.IsNullOrEmpty(b.ZoneSide) ? (b.Side ?? "").ToLowerInvariant() : b.ZoneSide;
            if (s == "blue") return ColBlue;
            if (s == "red") return ColRed;
            return ColOther;
        }

        private void DrawBase(Graphics g, Map_Base b, bool selected)
        {
            PointF p = ToScreen(b.X, b.Y);
            if (p.X < -150 || p.Y < -30 || p.X > Width + 150 || p.Y > Height + 30)
                return;

            Color c = SideColor(b);
            float r = selected ? 8f : 6f;

            if (selected)
            {
                using (var pen = new Pen(Color.Gold, 2.5f))
                    g.DrawEllipse(pen, p.X - r - 5, p.Y - r - 5, 2 * (r + 5), 2 * (r + 5));
            }

            // FARP = carré, base normale = rond. Base inactive = contour seul.
            RectangleF rect = new RectangleF(p.X - r, p.Y - r, 2 * r, 2 * r);
            if (b.Inactive)
            {
                using (var pen = new Pen(c, 2f))
                {
                    if (b.IsFarp) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    else g.DrawEllipse(pen, rect);
                }
            }
            else
            {
                using (var br = new SolidBrush(c))
                {
                    if (b.IsFarp) g.FillRectangle(br, rect);
                    else g.FillEllipse(br, rect);
                }
                g.DrawEllipse(Pens.White, rect.X, rect.Y, rect.Width, rect.Height);
            }

            // Plusieurs squadrons : petite silhouette d'avion à gauche, d'autant plus grande qu'il y a d'avions
            if (b.Squads >= 2)
                DrawSilhouette(g, p.X - r - 8, p.Y, b.Aircraft, c);

            string counts = b.Squads > 0 ? "  [" + b.Squads + " sq / " + b.Aircraft + " ac]" : "";
            float tx = p.X + r + 5;
            var fmt = StringFormat.GenericTypographic;

            if (b.Alias != "")
            {
                // L'alias (nom affiché dans le jeu) est le titre : grand et blanc. Le vrai nom passe en petit, gris.
                float ty = p.Y - 11;
                tx += DrawText(g, b.Alias, _fontAlias, Brushes.White, tx, ty, fmt);
                tx += DrawText(g, "  (" + b.Name + ")", _fontLabel, Brushes.Silver, tx, ty + 3, fmt);
                if (counts != "") DrawText(g, counts, _fontBold, Brushes.White, tx, ty + 2, fmt);
            }
            else
            {
                Font f = b.Squads > 0 ? _fontBold : _fontLabel;
                Brush br = b.Squads > 0 ? Brushes.White : Brushes.Silver;
                DrawText(g, b.Name + counts, f, br, tx, p.Y - 8, fmt);
            }
        }

        // Contour d'un avion vu de dessus, nez en haut (coordonnées de -1 à +1)
        private static readonly PointF[] JetShape =
        {
            new PointF(0f, -1f), new PointF(0.12f, -0.55f), new PointF(0.12f, -0.15f), new PointF(0.95f, 0.35f),
            new PointF(0.95f, 0.5f), new PointF(0.12f, 0.3f), new PointF(0.1f, 0.7f), new PointF(0.4f, 0.9f),
            new PointF(0.4f, 1f), new PointF(0f, 0.92f), new PointF(-0.4f, 1f), new PointF(-0.4f, 0.9f),
            new PointF(-0.1f, 0.7f), new PointF(-0.12f, 0.3f), new PointF(-0.95f, 0.5f), new PointF(-0.95f, 0.35f),
            new PointF(-0.12f, -0.15f), new PointF(-0.12f, -0.55f)
        };

        // La taille grandit avec la racine du nombre d'avions : 4 avions = petit, 100 avions = grand
        // Texte avec ombre noire ; renvoie sa largeur
        private static float DrawText(Graphics g, string text, Font f, Brush brush, float x, float y, StringFormat fmt)
        {
            g.DrawString(text, f, Brushes.Black, x + 1, y + 1, fmt);
            g.DrawString(text, f, brush, x, y, fmt);
            return g.MeasureString(text, f, 2000, fmt).Width;
        }

        private static float SilhouetteHalf(int aircraft)
        {
            float size = 12f + 4.5f * (float)Math.Sqrt(Math.Max(0, aircraft));
            if (size > 60f) size = 60f;
            return size / 2f;
        }

        private static void DrawSilhouette(Graphics g, float cx, float cy, int aircraft, Color color)
        {
            float half = SilhouetteHalf(aircraft);

            var pts = new PointF[JetShape.Length];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new PointF(cx - half + JetShape[i].X * half, cy + JetShape[i].Y * half);

            using (var br = new SolidBrush(Color.FromArgb(215, color)))
                g.FillPolygon(br, pts);
            g.DrawPolygon(Pens.White, pts);
        }

        private void DrawRuler(Graphics g)
        {
            if (!_rulerEnabled || !_hasA)
                return;

            double bx, by;
            if (_hasB)
            {
                bx = _bx; by = _by;
            }
            else
            {
                if (_mouse == Point.Empty) return;
                ToWorld(_mouse.X, _mouse.Y, out bx, out by);
            }

            PointF a = ToScreen(_ax, _ay);
            PointF b = ToScreen(bx, by);

            using (var pen = new Pen(Color.Yellow, 2f) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, a, b);
            g.FillEllipse(Brushes.Yellow, a.X - 4, a.Y - 4, 8, 8);
            g.FillEllipse(Brushes.Yellow, b.X - 4, b.Y - 4, 8, 8);

            double dx = bx - _ax, dy = by - _ay;
            double meters = Math.Sqrt(dx * dx + dy * dy);
            string text = (meters / 1000.0).ToString("0.0") + " km   |   " + (meters / 1852.0).ToString("0.0") + " Nm";

            SizeF size = g.MeasureString(text, _fontBold);
            float tx = b.X + 14, ty = b.Y - 26;
            if (tx + size.Width + 8 > Width) tx = b.X - size.Width - 22;
            if (ty < 2) ty = b.Y + 14;

            using (var back = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.FillRectangle(back, tx - 4, ty - 2, size.Width + 8, size.Height + 4);
            g.DrawString(text, _fontBold, Brushes.Yellow, tx, ty);
        }

        // ------------------------------------------------------------------
        //  Souris et clavier
        // ------------------------------------------------------------------

        // Base la plus proche du point écran (rayon 14 px), sinon null
        private Map_Base HitTest(Point p)
        {
            Map_Base best = null;
            float bestD = 14f * 14f;
            foreach (Map_Base b in _bases)
            {
                PointF s = ToScreen(b.X, b.Y);
                float dx = s.X - p.X, dy = s.Y - p.Y;
                float d = dx * dx + dy * dy;

                // La silhouette (2 squadrons ou plus) compte aussi : un clic dessus choisit la base
                if (b.Squads >= 2)
                {
                    float half = SilhouetteHalf(b.Aircraft);
                    float sx = s.X - 6f - 8f - half;
                    if (Math.Abs(p.X - sx) <= half + 2 && Math.Abs(p.Y - s.Y) <= half + 2)
                        d = Math.Min(d, 1f);
                }

                if (d <= bestD) { bestD = d; best = b; }
            }
            return best;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button == MouseButtons.Left)
            {
                _downPoint = e.Location;
                _lastMouse = e.Location;
                _panning = false;
            }
            else if (e.Button == MouseButtons.Right && _rulerEnabled)
            {
                ClearRuler();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = e.Location;

            if (e.Button == MouseButtons.Left)
            {
                if (!_panning && (Math.Abs(e.X - _downPoint.X) > 4 || Math.Abs(e.Y - _downPoint.Y) > 4))
                    _panning = true;

                if (_panning)
                {
                    _ox += e.X - _lastMouse.X;
                    _oy += e.Y - _lastMouse.Y;
                    _userMoved = true;
                    Invalidate();
                }
                _lastMouse = e.Location;
                return;
            }

            if (_rulerEnabled)
            {
                if (_hasA && !_hasB)
                    Invalidate();           // le trait suit la souris
            }
            else
            {
                Cursor = HitTest(e.Location) != null ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || _panning)
                return;

            if (_rulerEnabled)
            {
                double x, y;
                ToWorld(e.X, e.Y, out x, out y);
                if (!_hasA || _hasB)
                {
                    _ax = x; _ay = y;
                    _hasA = true; _hasB = false;
                }
                else
                {
                    _bx = x; _by = y;
                    _hasB = true;
                }
                Invalidate();
                return;
            }

            Map_Base hit = HitTest(e.Location);
            if (hit != null)
            {
                _selectedName = hit.Name;
                Invalidate();
                BaseSelected?.Invoke(hit.Name);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            float factor = e.Delta > 0 ? 1.25f : 0.8f;
            float newScale = Math.Max(_fitScale * 0.4f, Math.Min(2f, _scale * factor));
            factor = newScale / _scale;

            // Le point sous la souris reste sous la souris
            _ox = e.X - (e.X - _ox) * factor;
            _oy = e.Y - (e.Y - _oy) * factor;
            _scale = newScale;
            _userMoved = true;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
                ClearRuler();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _detailTimer.Stop();
                _detailTimer.Dispose();
                _dim.Dispose();
                _dimBrush.Dispose();
                _fontLabel.Dispose();
                _fontBold.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
