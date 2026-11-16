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
        public List<Map_Runway> Runways = new List<Map_Runway>();   // pistes : l'icône de la base reprend leur nombre, cap et longueur
        public List<Map_ParkSpot> Parks = new List<Map_ParkSpot>();  // places "parkAlertSAR" de db_airbases (hélicoptères d'alerte / SAR)
    }

    // Une place d'hélicoptère de db_airbases (parkAlertSAR) : position DCS et cap en RADIANS (comme "heading" dans le fichier mission : 0 = nord, sens horaire)
    internal class Map_ParkSpot
    {
        public string Base = "";
        public double X, Y;             // x = vers le NORD, y = vers l'EST
        public double Hdg;              // radians
        public bool HasHdg;             // false = aucun cap dans le fichier : on l'affiche cap au nord
        public List<KeyValuePair<string, string>> Extra = new List<KeyValuePair<string, string>>();     // autres champs du fichier (reservedAR...), recopiés tels quels à l'écriture

        // La place telle qu'elle est dans le fichier : sert à retrouver les hélicoptères du .miz posés dessus quand elle bouge (carte de mission)
        public bool IsNew = true;               // true = ajoutée sur la carte, pas encore dans le fichier
        public double OrigX, OrigY, OrigHdg;
        public bool OrigHasHdg;

        public void Remember()
        {
            IsNew = false;
            OrigX = X; OrigY = Y; OrigHdg = Hdg; OrigHasHdg = HasHdg;
        }

        // Déplacée ou tournée depuis la lecture du fichier
        public bool Changed
        {
            get
            {
                if (IsNew) return false;
                if (Math.Abs(X - OrigX) > 0.01 || Math.Abs(Y - OrigY) > 0.01) return true;
                return HasHdg && Math.Abs(Hdg - (OrigHasHdg ? OrigHdg : 0.0)) > 1e-4;
            }
        }
    }

    // Une piste d'une base (cap en degrés, longueur en mètres, position DCS du centre si connue)
    internal class Map_Runway
    {
        public double Hdg, Length, X, Y;
    }

    // Carte "vectorielle" de toute la map DCS : un point par base, sans image de fond.
    // Les positions viennent de db_airbases.lua, donc aucun décalage possible.
    // Molette = zoom, glisser = déplacer, clic = choisir une base.
    internal class Map_UI_View : UserControl
    {
        // Envoyé quand on clique sur une base (nom de la base)
        public event Action<string> BaseSelected;

        // Silhouette d'avion à côté des bases à plusieurs squadrons : seulement sur la carte de la campagne
        // (sur la carte de mission elle se confond avec les plans de vol voisins)
        public bool ShowSilhouettes = true;

        // Dessin par-dessus la carte (juste avant la règle) : utilisé par la carte des événements du debrief
        public event Action<Graphics> OverlayPaint;

        // Envoyé quand on clique sur un vol (nom du groupe), ou "" pour désélectionner (carte de mission seulement)
        // (CTRL + clic : plusieurs vols ; la liste envoyée = tous les vols sélectionnés, vide = plus aucun)
        public event Action<List<string>> FlightsSelected;

        // Vol en surbrillance : les autres plans de vol sont estompés
        private string _selectedFlight = "";
        public string SelectedFlight
        {
            get { return _selectedFlight; }
            set
            {
                _selectedFlight = value ?? "";
                _selSet.Clear();
                if (_selectedFlight != "") _selSet.Add(_selectedFlight);
                Invalidate();
            }
        }

        // Tous les vols en surbrillance (sélection multiple) ; _selectedFlight = le dernier choisi
        private readonly HashSet<string> _selSet = new HashSet<string>();

        public void SetSelectedFlights(IEnumerable<string> names)
        {
            _selSet.Clear();
            foreach (string n in names) _selSet.Add(n);
            _selectedFlight = _selSet.Count > 0 ? _selSet.Last() : "";
            Invalidate();
        }

        // Clic sur un waypoint : (nom du vol, numéro du waypoint). Carte de mission seulement.
        public event Action<string, int> WaypointSelected;

        // Déplacement d'un waypoint à la souris (simulation) : pendant (vol, numéro, x, y) et au lâcher (vol, numéro)
        public event Action<string, int, double, double> WaypointMoving;
        public event Action<string, int> WaypointDropped;

        // Clic droit (mode crayon) : ajouter un waypoint sur un tronçon (vol, place du nouveau waypoint, x, y) / supprimer un waypoint (vol, numéro)
        public event Action<string, int, double, double> WaypointInsertRequested;
        public event Action<string, int> WaypointDeleteRequested;

        private bool _moveEnabled;
        private string _dragFlight = "";
        private int _dragIndex;
        private bool _dragMoved;
        private string _dragInfo = "";

        // Autorise à tirer les waypoints du vol sélectionné
        public bool AllowMove
        {
            get { return _moveEnabled; }
            set { _moveEnabled = value; Cursor = Cursors.Default; }
        }

        public void SetDragInfo(string text)
        {
            _dragInfo = text ?? "";
            Invalidate();
        }

        // ------------------------------------------------------------------
        //  Réglage des pistes à la souris (base choisie) : un bout se tire (l'autre reste en place), le carré du milieu déplace la piste
        // ------------------------------------------------------------------

        // Envoyé au lâcher : la base dont les pistes ont changé (ses Runways sont déjà à jour)
        public event Action<string> RunwaysEdited;

        private bool _runwayEdit;
        private Map_Base _rwPrepared;           // base dont la liste de pistes a été préparée (doublons regroupés)
        private Map_Runway _rwVirtual;          // base sans piste : une piste de départ proposée, ajoutée à la base au premier déplacement
        private Map_Runway _rwDrag;
        private int _rwPart;                    // 0 = bout du côté du cap, 1 = l'autre bout, 2 = milieu
        private double _rwFixX, _rwFixY;        // bout qui ne bouge pas
        private double _rwGrabX, _rwGrabY;      // milieu : écart entre la souris et le centre de la piste
        private bool _rwMoved;

        public bool RunwayEdit
        {
            get { return _runwayEdit; }
            set { _runwayEdit = value; _rwPrepared = null; _rwDrag = null; Invalidate(); }
        }

        public Map_Base GetBase(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Map_Base b in _bases)
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        private Map_Base EditBase()
        {
            Map_Base b = GetBase(_selectedName);
            return (b == null || b.IsFarp || IsCarrier(b)) ? null : b;
        }

        private List<Map_Runway> EditList(Map_Base b)
        {
            if (!ReferenceEquals(_rwPrepared, b))
            {
                b.Runways = Map_RunwayData.Merged(b);       // une piste = une ligne, avec sa position absolue
                _rwPrepared = b;
                _rwVirtual = null;
            }
            if (b.Runways.Count > 0) return b.Runways;

            if (_rwVirtual == null) _rwVirtual = new Map_Runway { Hdg = 0, Length = 2500, X = b.X, Y = b.Y };
            return new List<Map_Runway> { _rwVirtual };
        }

        // Les deux bouts d'une piste (A = côté du cap) en coordonnées DCS
        private static void RunwayEnds(Map_Runway r, out double ax, out double ay, out double bx, out double by)
        {
            double a = r.Hdg * Math.PI / 180.0, half = r.Length / 2;
            double ux = Math.Cos(a), uy = Math.Sin(a);
            ax = r.X + half * ux; ay = r.Y + half * uy;
            bx = r.X - half * ux; by = r.Y - half * uy;
        }

        private static float Dist2(PointF p, Point m)
        {
            return (p.X - m.X) * (p.X - m.X) + (p.Y - m.Y) * (p.Y - m.Y);
        }

        // Poignée sous la souris : les bouts d'abord, puis le milieu
        private bool HitRunwayHandle(Point mouse, out Map_Runway rw, out int part)
        {
            rw = null; part = -1;
            Map_Base b = EditBase();
            if (b == null) return false;

            float best = 11f * 11f;
            foreach (Map_Runway r in EditList(b))
            {
                double ax, ay, bx, by;
                RunwayEnds(r, out ax, out ay, out bx, out by);
                PointF pa = ToScreen(ax, ay), pb = ToScreen(bx, by);
                if (!Finite(pa) || !Finite(pb)) continue;

                float da = Dist2(pa, mouse), db = Dist2(pb, mouse);
                if (da < best) { best = da; rw = r; part = 0; }
                if (db < best) { best = db; rw = r; part = 1; }
            }
            if (rw != null) return true;

            best = 9f * 9f;
            foreach (Map_Runway r in EditList(b))
            {
                double ax, ay, bx, by;
                RunwayEnds(r, out ax, out ay, out bx, out by);
                PointF pa = ToScreen(ax, ay), pb = ToScreen(bx, by), pc = ToScreen(r.X, r.Y);
                if (!Finite(pa) || !Finite(pb) || !Finite(pc)) continue;
                if (Dist2(pa, new Point((int)pb.X, (int)pb.Y)) < 30f * 30f) continue;      // piste trop petite à l'écran : pas de poignée au milieu

                float dc = Dist2(pc, mouse);
                if (dc < best) { best = dc; rw = r; part = 2; }
            }
            return rw != null;
        }

        private void DragRunway(Point p)
        {
            if (!_rwMoved)
            {
                if (Math.Abs(p.X - _downPoint.X) <= 3 && Math.Abs(p.Y - _downPoint.Y) <= 3) return;
                _rwMoved = true;
                Map_Base eb = EditBase();
                if (eb != null && eb.Runways.Count == 0 && ReferenceEquals(_rwDrag, _rwVirtual)) eb.Runways.Add(_rwVirtual);
            }

            double wx, wy;
            ToWorld(p.X, p.Y, out wx, out wy);

            if (_rwPart == 2)
            {
                _rwDrag.X = wx - _rwGrabX;
                _rwDrag.Y = wy - _rwGrabY;
            }
            else
            {
                double dn = wx - _rwFixX, de = wy - _rwFixY;        // du bout fixe vers la souris (nord, est)
                double len = Math.Sqrt(dn * dn + de * de);
                if (len < 1) return;

                double hdg = Math.Atan2(de, dn) * 180.0 / Math.PI;
                if (_rwPart == 1) hdg += 180;                        // le bout tiré est du côté opposé au cap
                hdg = Math.Round(((hdg % 360) + 360) % 360);
                len = Math.Max(100, Math.Round(len / 10.0) * 10);

                double a = hdg * Math.PI / 180.0, ux = Math.Cos(a), uy = Math.Sin(a);
                double sign = _rwPart == 0 ? 1 : -1;                 // bout A : centre = bout fixe + moitié vers le cap ; bout B : l'inverse
                _rwDrag.Hdg = hdg;
                _rwDrag.Length = len;
                _rwDrag.X = _rwFixX + sign * (len / 2) * ux;
                _rwDrag.Y = _rwFixY + sign * (len / 2) * uy;
            }
            Invalidate();
        }

        private void DrawRunwayEdit(Graphics g, Map_Base b)
        {
            PointF bp = ToScreen(b.X, b.Y);
            if (Finite(bp))
            {
                // Le point de la base : petite croix
                using (var pen = new Pen(Color.White, 3.5f)) { g.DrawLine(pen, bp.X - 7, bp.Y, bp.X + 7, bp.Y); g.DrawLine(pen, bp.X, bp.Y - 7, bp.X, bp.Y + 7); }
                using (var pen = new Pen(Color.Black, 1.5f)) { g.DrawLine(pen, bp.X - 7, bp.Y, bp.X + 7, bp.Y); g.DrawLine(pen, bp.X, bp.Y - 7, bp.X, bp.Y + 7); }
            }

            foreach (Map_Runway r in EditList(b))
            {
                double ax, ay, bx, by;
                RunwayEnds(r, out ax, out ay, out bx, out by);
                PointF pa = ToScreen(ax, ay), pb = ToScreen(bx, by), pc = ToScreen(r.X, r.Y);
                if (!Finite(pa) || !Finite(pb) || Math.Abs(pa.X) > 1e6f || Math.Abs(pa.Y) > 1e6f || Math.Abs(pb.X) > 1e6f || Math.Abs(pb.Y) > 1e6f) continue;

                using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 7f)) g.DrawLine(pen, pa, pb);
                using (var pen = new Pen(Color.FromArgb(240, 230, 60, 40), 3f)) g.DrawLine(pen, pa, pb);

                foreach (PointF h in new[] { pa, pb })
                {
                    g.FillEllipse(Brushes.White, h.X - 6, h.Y - 6, 12, 12);
                    using (var pen = new Pen(Color.FromArgb(230, 60, 40), 2.5f)) g.DrawEllipse(pen, h.X - 6, h.Y - 6, 12, 12);
                }
                if (Dist2(pa, new Point((int)pb.X, (int)pb.Y)) >= 30f * 30f)
                {
                    g.FillRectangle(Brushes.White, pc.X - 5, pc.Y - 5, 10, 10);
                    using (var pen = new Pen(Color.FromArgb(230, 60, 40), 2.5f)) g.DrawRectangle(pen, pc.X - 5, pc.Y - 5, 10, 10);
                }

                // Valeurs de la piste, à côté du milieu
                double h1 = ((r.Hdg % 180) + 180) % 180;
                string txt = h1.ToString("000") + "° / " + (h1 + 180).ToString("000") + "°   " + Map_Units.Alt(r.Length);
                SizeF sz = g.MeasureString(txt, _fontBold);
                float tx = pc.X + 12, ty = pc.Y + 10;
                using (var back = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
                    g.FillRectangle(back, tx - 3, ty - 1, sz.Width + 6, sz.Height + 2);
                g.DrawString(txt, _fontBold, Brushes.White, tx, ty);
            }
        }

        // ------------------------------------------------------------------
        //  Places d'hélicoptère (parkAlertSAR de db_airbases) : silhouette de CH-46 dans son cercle de sécurité.
        //  On tire la silhouette pour la déplacer, la poignée ronde pour tourner le cap. Les changements sont gardés par la fenêtre (Save).
        // ------------------------------------------------------------------

        public const double ParkRadiusM = 16.0;         // cercle : le CH-46 (2 rotors de 15,2 m, 25,7 m rotors tournants) y tient avec de la marge
        public const double ParkMinPxPerM = 1.0;        // taille minimale à l'écran (pixels par mètre) : à ce plancher la silhouette fait environ 27 pixels

        public event Action<string> ParksEdited;                // base dont une place a été déplacée / tournée (au lâcher)
        public event Action<Map_ParkSpot> ParkSelected;         // place choisie (null = plus aucune)
        public event Action<Map_ParkSpot> ParkChanged;          // pendant le glissement (pour mettre à jour la fenêtre)
        public event Action<double, double> ParkAddClicked;     // clic de placement (x, y DCS) après "Add"
        public event Action ParkDeleteRequested;                // touche Suppr

        private bool _parkShow, _parkAdd;
        private Map_ParkSpot _parkSel, _parkDrag;
        private int _parkPart;                  // 0 = déplacer, 1 = tourner (poignée)
        private bool _parkMoved;
        private double _parkGrabX, _parkGrabY;

        public bool ParkShow
        {
            get { return _parkShow; }
            set { _parkShow = value; if (!value) { _parkAdd = false; _parkSel = null; _parkDrag = null; } Invalidate(); }
        }

        public bool ParkAddMode
        {
            get { return _parkAdd; }
            set { _parkAdd = value; Cursor = value ? Cursors.Cross : Cursors.Default; Invalidate(); }
        }

        public Map_ParkSpot SelectedPark
        {
            get { return _parkSel; }
            set { _parkSel = value; Invalidate(); }
        }

        // Taille à l'écran : réelle si le zoom le permet, sinon une taille minimale lisible
        private float ParkScale() { return (float)Math.Max(_scale, ParkMinPxPerM); }

        // Les places de la base choisie sont toujours montrées ; celles des autres bases seulement quand le zoom est assez grand
        private bool ParkBaseShown(Map_Base b)
        {
            return b.Parks.Count > 0 && (_scale >= 0.15 || string.Equals(b.Name, _selectedName, StringComparison.OrdinalIgnoreCase));
        }

        // Direction du cap à l'écran (vecteur unitaire) : calculée avec la même projection que la carte
        private bool ParkDir(Map_ParkSpot s, out PointF center, out PointF dir)
        {
            center = ToScreen(s.X, s.Y);
            PointF q = ToScreen(s.X + Math.Cos(s.Hdg), s.Y + Math.Sin(s.Hdg));
            float dx = q.X - center.X, dy = q.Y - center.Y, m = (float)Math.Sqrt(dx * dx + dy * dy);
            dir = m < 1e-6f ? new PointF(0, -1) : new PointF(dx / m, dy / m);
            return Finite(center);
        }

        private bool HitPark(Point mouse, out Map_ParkSpot hit, out int part)
        {
            hit = null; part = -1;
            if (!_parkShow) return false;

            // la poignée du cap de la place choisie
            if (_parkSel != null)
            {
                PointF c, d;
                if (ParkDir(_parkSel, out c, out d))
                {
                    float r = Math.Max(8f, (float)ParkRadiusM * ParkScale()) + 16f;
                    if (Dist2(new PointF(c.X + d.X * r, c.Y + d.Y * r), mouse) <= 9f * 9f) { hit = _parkSel; part = 1; return true; }
                }
            }

            float best = float.MaxValue;
            foreach (Map_Base b in _bases)
            {
                if (!ParkBaseShown(b)) continue;
                foreach (Map_ParkSpot s in b.Parks)
                {
                    PointF c, d;
                    if (!ParkDir(s, out c, out d)) continue;
                    float r = Math.Max(8f, (float)ParkRadiusM * ParkScale()) + 2f;
                    float dd = Dist2(c, mouse);
                    if (dd <= r * r && dd < best) { best = dd; hit = s; part = 0; }
                }
            }
            return hit != null;
        }

        private void DragPark(Point p)
        {
            if (!_parkMoved)
            {
                if (Math.Abs(p.X - _downPoint.X) <= 3 && Math.Abs(p.Y - _downPoint.Y) <= 3) return;
                _parkMoved = true;
            }

            double wx, wy;
            ToWorld(p.X, p.Y, out wx, out wy);
            if (_parkPart == 0)
            {
                _parkDrag.X = wx - _parkGrabX;
                _parkDrag.Y = wy - _parkGrabY;
            }
            else
            {
                double dn = wx - _parkDrag.X, de = wy - _parkDrag.Y;        // du centre vers la souris (nord, est)
                if (Math.Sqrt(dn * dn + de * de) < 0.5) return;
                double h = Math.Atan2(de, dn);
                if (h < 0) h += 2 * Math.PI;
                _parkDrag.Hdg = Math.Round(h, 4);
                _parkDrag.HasHdg = true;
            }
            ParkChanged?.Invoke(_parkDrag);
            Invalidate();
        }

        // Silhouette de CH-46 vue de dessus (mètres, nez vers le haut = y négatif) : fuselage, aileron arrière, deux rotors qui se recouvrent
        private static readonly PointF[] Ch46Body =
        {
            new PointF(0f, -7.4f), new PointF(1.2f, -6.6f), new PointF(1.5f, -4.8f), new PointF(1.5f, 3.2f), new PointF(1.25f, 5.0f),
            new PointF(1.0f, 7.2f), new PointF(-1.0f, 7.2f), new PointF(-1.25f, 5.0f), new PointF(-1.5f, 3.2f), new PointF(-1.5f, -4.8f), new PointF(-1.2f, -6.6f)
        };

        private void DrawParks(Graphics g, Map_Base selected)
        {
            foreach (Map_Base b in _bases)
            {
                if (!ParkBaseShown(b)) continue;
                foreach (Map_ParkSpot s in b.Parks)
                    DrawPark(g, s, ReferenceEquals(s, _parkSel));
            }
        }

        private void DrawPark(Graphics g, Map_ParkSpot s, bool selected)
        {
            PointF c, d;
            if (!ParkDir(s, out c, out d)) return;

            float sc = ParkScale();
            float r = (float)ParkRadiusM * sc;
            if (c.X < -60 - r || c.Y < -60 - r || c.X > Width + 60 + r || c.Y > Height + 60 + r) return;

            Color col = s.HasHdg ? Color.FromArgb(255, 190, 40) : Color.FromArgb(120, 200, 255);      // bleu clair : pas encore de cap dans le fichier
            float ang = (float)(Math.Atan2(d.X, -d.Y) * 180.0 / Math.PI);

            // cercle de sécurité (rayon ParkRadiusM) : pointillé tant que le cap n'est pas validé
            using (var br = new SolidBrush(Color.FromArgb(45, col))) g.FillEllipse(br, c.X - r, c.Y - r, 2 * r, 2 * r);
            using (var pen = new Pen(Color.FromArgb(230, col), selected ? 2.5f : 1.6f) { DashStyle = s.HasHdg ? DashStyle.Solid : DashStyle.Dash })
                g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);

            GraphicsState st = g.Save();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(ang);

            // les deux rotors (traits fins)
            float rr = 7.75f * sc;
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash })
            {
                g.DrawEllipse(pen, -rr, -5.5f * sc - rr, 2 * rr, 2 * rr);
                g.DrawEllipse(pen, -rr, 5.5f * sc - rr, 2 * rr, 2 * rr);
            }

            // fuselage
            var pts = new PointF[Ch46Body.Length];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(Ch46Body[i].X * sc, Ch46Body[i].Y * sc);
            using (var br = new SolidBrush(Color.FromArgb(235, 235, 235, 235))) g.FillPolygon(br, pts);
            using (var pen = new Pen(Color.FromArgb(220, 30, 30, 30), 1.2f)) g.DrawPolygon(pen, pts);

            // nez : petit repère rouge ; moyeux des rotors
            using (var br = new SolidBrush(Color.FromArgb(220, 40, 40))) g.FillEllipse(br, -1.3f * sc, -7.0f * sc, 2.6f * sc, 2.6f * sc);
            g.FillEllipse(Brushes.DimGray, -0.8f * sc, -5.9f * sc, 1.6f * sc, 1.6f * sc);
            g.FillEllipse(Brushes.DimGray, -0.8f * sc, 4.7f * sc, 1.6f * sc, 1.6f * sc);
            g.Restore(st);

            if (!selected) return;

            using (var pen = new Pen(Color.Gold, 2.5f)) g.DrawEllipse(pen, c.X - r - 4, c.Y - r - 4, 2 * (r + 4), 2 * (r + 4));

            // poignée du cap
            float hr = Math.Max(8f, r) + 16f;
            PointF hp = new PointF(c.X + d.X * hr, c.Y + d.Y * hr);
            using (var pen = new Pen(Color.FromArgb(230, 60, 40), 2f)) g.DrawLine(pen, c, hp);
            g.FillEllipse(Brushes.White, hp.X - 6, hp.Y - 6, 12, 12);
            using (var pen = new Pen(Color.FromArgb(230, 60, 40), 2.5f)) g.DrawEllipse(pen, hp.X - 6, hp.Y - 6, 12, 12);

            double deg = s.Hdg * 180.0 / Math.PI;
            string txt = s.Base + "   " + deg.ToString("000.0") + "°   " + s.Hdg.ToString("0.000") + " rad" + (s.HasHdg ? "" : "   (no heading in the file: north)");
            SizeF sz = g.MeasureString(txt, _fontBold);
            float tx = c.X + r + 8, ty = c.Y - sz.Height / 2;
            using (var back = new SolidBrush(Color.FromArgb(200, 20, 20, 20)))
                g.FillRectangle(back, tx - 3, ty - 1, sz.Width + 6, sz.Height + 2);
            g.DrawString(txt, _fontBold, Brushes.White, tx, ty);
        }

        // Remplace les tracés d'un seul vol (déplacement en direct : pas besoin de tout reconstruire)
        public void ReplaceFlightDrawings(string name, List<Map_Drawing> fresh)
        {
            List<Map_Drawing> list;
            if (!_drawingGroups.TryGetValue("plans", out list)) return;

            var merged = new List<Map_Drawing>(list.Count + fresh.Count);
            foreach (Map_Drawing d in list)
                if (d.Name != name) merged.Add(d);
            merged.AddRange(fresh);

            _drawingGroups["plans"] = merged;
            Invalidate();
        }

        // Pendant un déplacement : la scène (fond, bases, autres vols) est dessinée une seule fois dans une image,
        // seul le vol déplacé est redessiné à chaque mouvement de souris -> fluide
        private Bitmap _dragCache;
        private string _cacheSkip = "";

        private void BuildDragCache()
        {
            DropDragCache();
            if (Width < 2 || Height < 2) return;

            _dragCache = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            _cacheSkip = _dragFlight;
            try
            {
                using (Graphics g2 = Graphics.FromImage(_dragCache))
                    PaintScene(g2);
            }
            finally { _cacheSkip = ""; }
        }

        private void DropDragCache()
        {
            if (_dragCache != null) { _dragCache.Dispose(); _dragCache = null; }
        }

        // Waypoint entouré d'un anneau doré (le vol choisi dans la liste ou cliqué sur la carte)
        private string _selWpFlight = "";
        private int _selWpIndex = -1;
        private double _selWpX, _selWpY;

        public void SetSelectedWaypoint(string flight, int index)
        {
            _selWpFlight = flight ?? "";
            _selWpIndex = index;
            List<Map_Drawing> list;
            if (_selWpFlight != "" && index >= 0 && _drawingGroups.TryGetValue("plans", out list))
            {
                Map_Drawing m = list.FirstOrDefault(d => d.Kind == "Marker" && d.Name == _selWpFlight && d.Index == index);
                if (m != null) { _selWpX = m.Points[0][0]; _selWpY = m.Points[0][1]; }
                else { _selWpFlight = ""; _selWpIndex = -1; }
            }
            Invalidate();
        }

        // La carte se centre sur le waypoint choisi (liste de droite). Le zoom reste, sauf si la carte est trop dézoomée pour voir quelque chose.
        public void CenterOnSelectedWaypoint()
        {
            if (_selWpFlight == "" || _selWpIndex < 0 || Width < 20 || Height < 20) return;

            _scale = Math.Max(_scale, 0.01f);
            _ox = Width / 2f - (float)(_selWpY * _scale);
            _oy = Height / 2f + (float)(_selWpX * _scale);
            _userMoved = true;
            CloseSpread(true);
            Invalidate();
        }

        // Vols superposés : des clics successifs au même endroit passent de l'un à l'autre
        private string _cycleKey = "";
        private int _cycleIndex;
        private Point _cycleAt;

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
        private static readonly string[] GroupOrder = { "spawn", "zones", "mission", "radars", "threats", "aaa", "ships", "plans" };      // du dessous au dessus     // du dessous au dessus

        // Tuiles plus fines : on attend que la vue ne bouge plus avant de les demander
        private readonly Timer _detailTimer = new Timer { Interval = 300 };
        private readonly Timer _tilePaintTimer = new Timer { Interval = 150 };     // une tuile qui arrive ne redessine pas tout : au plus une fois toutes les 150 ms
        private int _forceZoom;                                                    // 0 = niveaux automatiques ; sinon le niveau de zoom forcé (jusqu'à ce que le zoom change)
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
            _tilePaintTimer.Tick += (s, e) => { _tilePaintTimer.Stop(); Invalidate(); };
            _spreadTimer.Tick += SpreadTick;
            _areaMsgTimer.Tick += (s, e) => { _areaMsgTimer.Stop(); _areaMsg = ""; Invalidate(); };
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
            _forceZoom = 0;
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

        // Bouton "charger la zone visible" : on garde la zone, tuiles grossières si elle est trop grande,
        // puis les tuiles fines arrivent toutes seules quand on zoome dans cette zone
        private string _areaMsg = "";
        private readonly Timer _areaMsgTimer = new Timer { Interval = 7000 };

        // Points de l'écran (coins, milieux des bords, centre) en coordonnées DCS
        private List<double[]> ScreenWorldPoints()
        {
            var pts = new List<double[]>();
            foreach (float fx in new[] { 0f, Width / 2f, Width })
            {
                foreach (float fy in new[] { 0f, Height / 2f, Height })
                {
                    double x, y;
                    ToWorld(fx, fy, out x, out y);
                    pts.Add(new double[] { x, y });
                }
            }
            return pts;
        }

        public void LoadVisibleArea()
        {
            if (_bg == null || _bg.Detail == null || !_showBg)
            {
                ShowAreaMessage("Turn the web map on first.");
                return;
            }

            bool coarse;
            int n = _bg.Detail.RequestArea(_scale, ScreenWorldPoints(), out coarse);

            string msg = coarse
                ? "Too many tiles for this zoom: " + n + " coarse tiles. Zoom in on this area to get more detail."
                : n + " tiles at full detail for this view.";
            if (!Main_WebAccess.Enabled) msg += " (Web access is OFF: only the tiles already on disk are shown.)";
            ShowAreaMessage(msg);

            _detailTimer.Stop();
            _detailTimer.Start();       // dans un instant : tuiles fines si besoin
            Invalidate();
        }

        private void ShowAreaMessage(string text)
        {
            _areaMsg = text;
            _areaMsgTimer.Stop();
            _areaMsgTimer.Start();
            Invalidate();
        }

        private void DrawAreaInfo(Graphics g)
        {
            if (_bg == null || _bg.Detail == null) return;

            string text = _areaMsg;
            int loaded, total;
            _bg.Detail.AreaProgress(out loaded, out total);
            if (total > 0 && loaded < total)
                text = "Loading area: " + loaded + " / " + total + " tiles" + (_areaMsg != "" ? "\n" + _areaMsg : "");
            if (text == "") return;

            SizeF sz = g.MeasureString(text, _fontBold);
            float bx = (Width - sz.Width) / 2f - 6, by = 8;
            using (var back = new SolidBrush(Color.FromArgb(215, 20, 20, 20)))
                g.FillRectangle(back, bx, by, sz.Width + 12, sz.Height + 6);
            g.DrawString(text, _fontBold, Brushes.White, bx + 6, by + 3);
        }

        // Une tuile fine vient d'arriver (thread de téléchargement) : on redessine
        private void OnDetailChanged()
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => { if (!_tilePaintTimer.Enabled) _tilePaintTimer.Start(); }));
            }
            catch { }
        }

        // Bouton "Detail now" : on veut les tuiles du niveau de zoom où l'on est, sans attendre un niveau "automatique".
        // Le choix dure tant que le niveau de zoom ne change pas (on peut se déplacer : les tuiles voisines suivent).
        public void ForceDetailHere()
        {
            if (_bg == null || _bg.Detail == null || !_showBg)
            {
                ShowAreaMessage("Turn the web map on first.");
                return;
            }

            int zd = _bg.Detail.ZoomFor(_scale);
            if (zd <= 0)
            {
                _forceZoom = 0;
                ShowAreaMessage("Nothing finer to load here: the base map is already the right level.");
                return;
            }

            _forceZoom = zd;
            string msg = "Detail tiles at zoom " + zd + " (finest of this style: " + _bg.Detail.MaxLevel + "). Kept until you change the zoom level.";
            if (!Main_WebAccess.Enabled) msg += " (Web access is OFF: only the tiles already on disk are shown.)";
            ShowAreaMessage(msg);
            DetailTick(null, EventArgs.Empty);
        }

        private void DetailTick(object sender, EventArgs e)
        {
            _detailTimer.Stop();
            if (!_showBg || _bg == null || _bg.Detail == null) return;

            // Niveau demandé : tous les niveaux n'est pas utile (chaque niveau = 4 fois plus de tuiles). Automatique = un niveau sur "AutoStep",
            // sauf le plus fin du style (toujours chargé). "Detail now" force le niveau exact tant que le zoom ne change pas.
            int zd = _bg.Detail.ZoomFor(_scale);
            if (_forceZoom != 0 && zd != _forceZoom) _forceZoom = 0;
            int want = _forceZoom != 0 ? zd : _bg.Detail.AutoZoom(zd);
            _bg.Detail.Update(want, ToScreen, ClientRectangle);

            // Zone demandée avec le bouton : tuiles fines si on est dedans
            double cwx, cwy;
            ToWorld(Width / 2f, Height / 2f, out cwx, out cwy);
            _bg.Detail.UpdateArea(_scale, ScreenWorldPoints(), cwx, cwy, ToScreen, ClientRectangle);
            Invalidate();
        }

        // Éléments au sol (véhicules, statiques, navires) : dessinés d'un coup, seulement ceux de l'écran
        private List<Map_GroundUnit> _ground = new List<Map_GroundUnit>();
        private List<Map_GroundGroup> _groundGroups = new List<Map_GroundGroup>();
        private bool _gBlue, _gRed, _gNames;
        private Map_GroundUnit _selGround;
        public event Action<Map_GroundUnit> GroundClicked;
        private readonly SolidBrush _gBlueBrush = new SolidBrush(Color.FromArgb(70, 150, 255));
        private readonly SolidBrush _gRedBrush = new SolidBrush(Color.FromArgb(240, 70, 70));
        private readonly Pen _gBluePen = new Pen(Color.FromArgb(70, 150, 255), 1.6f);
        private readonly Pen _gRedPen = new Pen(Color.FromArgb(240, 70, 70), 1.6f);
        private readonly Pen _gPen = new Pen(Color.FromArgb(170, 0, 0, 0), 1f);
        private readonly SolidBrush _gBlueText = new SolidBrush(Color.FromArgb(180, 212, 255));
        private readonly SolidBrush _gRedText = new SolidBrush(Color.FromArgb(255, 180, 170));
        private readonly Font[] _fontsGround = { new Font("Segoe UI", 9f), new Font("Segoe UI", 10.5f), new Font("Segoe UI", 12f), new Font("Segoe UI", 14f) };   // la police grandit avec le zoom

        public void SetGround(List<Map_GroundUnit> units, List<Map_GroundGroup> groups)
        {
            _ground = units ?? new List<Map_GroundUnit>();
            _groundGroups = groups ?? new List<Map_GroundGroup>();
            _carrierHdg.Clear();
            _selGround = null;
            Invalidate();
        }

        // blue / red = camps affichés ; names = noms de groupes
        public void SetGroundShown(bool blue, bool red, bool names)
        {
            _gBlue = blue; _gRed = red; _gNames = names;
            if (_selGround != null && (_selGround.Red ? !red : !blue)) _selGround = null;
            Invalidate();
        }

        // Demi-taille d'une icône : sa taille minimale en pixels, ou sa taille réelle (en mètres, selon le zoom) si elle est plus grande
        private float GroundIconHalf(Map_GroundUnit u, float minHalf)
        {
            if (u.HalfM <= 0) return minHalf;
            return Math.Max(minHalf, Math.Min(400f, u.HalfM * _scale));
        }

        private float GroundHalf()
        {
            return _scale > 0.08f ? 6f : (_scale > 0.02f ? 4.5f : (_scale > 0.006f ? 3.5f : 2.5f));
        }

        // Une icône par famille : voir Map_GroundUnit.Kind
        private void DrawGroundIcon(Graphics g, char kind, float sx, float sy, float h, bool red)
        {
            SolidBrush b = red ? _gRedBrush : _gBlueBrush;
            Pen pen = red ? _gRedPen : _gBluePen;
            switch (kind)
            {
                case 'a':   // blindé : rectangle couché
                    g.FillRectangle(b, sx - h * 1.4f, sy - h * 0.8f, h * 2.8f, h * 1.6f);
                    g.DrawRectangle(_gPen, sx - h * 1.4f, sy - h * 0.8f, h * 2.8f, h * 1.6f);
                    break;
                case 'm':   // défense sol-air : triangle pointe en haut
                    g.FillPolygon(b, new[] { new PointF(sx, sy - h * 1.2f), new PointF(sx + h * 1.1f, sy + h * 0.9f), new PointF(sx - h * 1.1f, sy + h * 0.9f) });
                    break;
                case 'r':   // radar : cercle creux avec un point
                    g.DrawEllipse(pen, sx - h, sy - h, 2 * h, 2 * h);
                    g.FillRectangle(b, sx - 1, sy - 1, 2, 2);
                    break;
                case 'g':   // artillerie : rond avec un tube
                    g.FillEllipse(b, sx - h * 0.8f, sy - h * 0.8f, h * 1.6f, h * 1.6f);
                    g.DrawLine(pen, sx, sy, sx + h * 1.4f, sy - h * 1.4f);
                    break;
                case 'i':   // infanterie : croix
                    g.DrawLine(pen, sx - h, sy - h, sx + h, sy + h);
                    g.DrawLine(pen, sx - h, sy + h, sx + h, sy - h);
                    break;
                case 'b':   // statique : losange
                    g.FillPolygon(b, new[] { new PointF(sx, sy - h * 1.2f), new PointF(sx + h * 1.2f, sy), new PointF(sx, sy + h * 1.2f), new PointF(sx - h * 1.2f, sy) });
                    break;
                case 'p':   // avion / hélico statique : triangle pointe en bas
                    g.FillPolygon(b, new[] { new PointF(sx, sy + h * 1.2f), new PointF(sx + h * 1.1f, sy - h * 0.9f), new PointF(sx - h * 1.1f, sy - h * 0.9f) });
                    break;
                case 'f':   // FARP : carré creux
                    g.DrawRectangle(pen, sx - h, sy - h, 2 * h, 2 * h);
                    break;
                case 'n':   // navire : coque pointue (vers la droite)
                    g.FillPolygon(b, new[] { new PointF(sx - h * 1.6f, sy - h * 0.7f), new PointF(sx + h * 0.8f, sy - h * 0.7f), new PointF(sx + h * 1.7f, sy),
                                             new PointF(sx + h * 0.8f, sy + h * 0.7f), new PointF(sx - h * 1.6f, sy + h * 0.7f) });
                    break;
                case 'k':   // porte-avions : grand rectangle avec une piste
                    g.FillRectangle(b, sx - h * 2.4f, sy - h * 0.9f, h * 4.8f, h * 1.8f);
                    g.DrawRectangle(_gPen, sx - h * 2.4f, sy - h * 0.9f, h * 4.8f, h * 1.8f);
                    g.DrawLine(Pens.White, sx - h * 1.8f, sy, sx + h * 1.8f, sy);
                    break;
                default:    // véhicule : carré
                    g.FillRectangle(b, sx - h, sy - h, 2 * h, 2 * h);
                    if (h >= 3f) g.DrawRectangle(_gPen, sx - h, sy - h, 2 * h, 2 * h);
                    break;
            }
        }

        private void DrawGround(Graphics g)
        {
            bool showBlue = _gBlue, showRed = _gRed;
            if ((!showBlue && !showRed) || _ground.Count == 0) return;

            int w = Width, h = Height;
            float half = GroundHalf();
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;               // petites icônes : inutile de lisser, bien plus rapide

            float lastX = -999, lastY = -999;
            foreach (Map_GroundUnit u in _ground)
            {
                if (u.Red ? !showRed : !showBlue) continue;
                float sx = _ox + (float)u.Y * _scale, sy = _oy - (float)u.X * _scale;
                float hu = GroundIconHalf(u, half);         // taille minimale, ou taille réelle quand elle est plus grande
                float margin = Math.Max(20f, hu * 5f);
                if (sx < -margin || sy < -margin || sx > w + margin || sy > h + margin) continue;
                if (Math.Abs(sx - lastX) < 1f && Math.Abs(sy - lastY) < 1f) continue;      // même pixel que le précédent
                lastX = sx; lastY = sy;

                // Navire / porte-avions à sa taille réelle : tourné dans le sens de son cap
                if ((u.Kind == 'n' || u.Kind == 'k') && hu > half * 1.5f && !double.IsNaN(u.Heading))
                {
                    float deg = (float)(Math.Atan2(-Math.Cos(u.Heading), Math.Sin(u.Heading)) * 180.0 / Math.PI);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TranslateTransform(sx, sy);
                    g.RotateTransform(deg);
                    DrawGroundIcon(g, u.Kind, 0, 0, hu, u.Red);
                    g.ResetTransform();
                    g.SmoothingMode = SmoothingMode.None;
                }
                else
                    DrawGroundIcon(g, u.Kind, sx, sy, hu, u.Red);
            }

            // Unité choisie : cercle doré
            if (_selGround != null)
            {
                float sx = _ox + (float)_selGround.Y * _scale, sy = _oy - (float)_selGround.X * _scale;
                float rh = Math.Max(half, GroundIconHalf(_selGround, half) * 2.5f);      // les icônes longues (navires) ont besoin d'un anneau plus grand
                using (var ring = new Pen(Color.Gold, 2.5f))
                    g.DrawEllipse(ring, sx - rh - 6, sy - rh - 6, 2 * rh + 12, 2 * rh + 12);
            }
            g.SmoothingMode = old;

            // Noms de groupes : seulement assez zoomé, police plus grande quand on zoome, max 200, pas deux noms l'un sur l'autre
            if (_gNames && _scale >= 0.004f)
            {
                int fi = _scale > 0.08f ? 3 : (_scale > 0.03f ? 2 : (_scale > 0.01f ? 1 : 0));
                Font font = _fontsGround[fi];
                float cellW = 10f * font.Size, cellH = 1.9f * font.Size;
                var used = new HashSet<long>();
                int count = 0;
                foreach (Map_GroundGroup gr in _groundGroups)
                {
                    if (gr.Red ? !showRed : !showBlue) continue;
                    float sx = _ox + (float)gr.Y * _scale, sy = _oy - (float)gr.X * _scale;
                    if (sx < -80 || sy < -20 || sx > w + 80 || sy > h + 20) continue;
                    long cell = ((long)Math.Floor(sx / cellW) << 32) + (long)Math.Floor(sy / cellH);
                    if (!used.Add(cell)) continue;
                    g.DrawString(gr.Name, font, gr.Red ? _gRedText : _gBlueText, sx + half + 3, sy - font.Size);
                    if (++count >= 200) break;
                }
            }
        }

        // Clic : l'unité sol affichée la plus proche (à 8 pixels maximum). Seulement au clic, jamais au survol.
        private Map_GroundUnit HitGround(Point p)
        {
            if ((!_gBlue && !_gRed) || _ground.Count == 0) return null;
            Map_GroundUnit best = null;
            float bestD = 64f;
            foreach (Map_GroundUnit u in _ground)
            {
                if (u.Red ? !_gRed : !_gBlue) continue;
                float dx = _ox + (float)u.Y * _scale - p.X, dy = _oy - (float)u.X * _scale - p.Y;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
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
                Refit();
            Invalidate();
        }

        // Tout remettre dans la fenêtre (le bouton ⛶ : toute la carte, on oublie le zoom sur les vols)
        public void ResetZoom()
        {
            _fitFlights = null;
            FitAll();
            Invalidate();
        }

        // Cadre la carte sur une boîte (x = nord, y = est), au moins 5 km de côté (carte des événements du debrief)
        public void ZoomToBox(double minX, double maxX, double minY, double maxY)
        {
            if (Width < 20 || Height < 20) return;

            double rangeX = Math.Max((maxX - minX) * 1.2, 5000);
            double rangeY = Math.Max((maxY - minY) * 1.2, 5000);
            const float margin = 30f;

            float scale = (float)Math.Min((Width - 2 * margin) / rangeY, (Height - 2 * margin) / rangeX);
            _scale = Math.Max(_fitScale * 0.4f, Math.Min(2f, scale));

            double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0;
            _ox = Width / 2f - (float)(cy * _scale);
            _oy = Height / 2f + (float)(cx * _scale);
            _userMoved = true;
            CloseSpread(true);
            Invalidate();
        }

        // Centre la carte sur un point, sans changer le zoom
        public void CenterOn(double x, double y)
        {
            if (Width < 20 || Height < 20) return;

            _ox = Width / 2f - (float)(y * _scale);
            _oy = Height / 2f + (float)(x * _scale);
            _userMoved = true;
            Invalidate();
        }

        // Cadre la carte juste autour de ces vols (leurs waypoints), avec une petite marge.
        // Tant que l'utilisateur ne zoome pas ni ne déplace, la vue garde ce cadrage (même si la fenêtre change de taille).
        public void ZoomToFlights(IEnumerable<string> names)
        {
            _fitFlights = new HashSet<string>(names ?? new string[0]);
            if (_fitFlights.Count == 0) _fitFlights = null;
            _userMoved = false;
            Refit();
            Invalidate();
        }

        private HashSet<string> _fitFlights;

        // Toute la carte, puis (s'il y a des vols à cadrer) le zoom sur ces vols
        private void Refit()
        {
            FitAll();
            if (_fitFlights != null) FitFlights();
        }

        private void FitFlights()
        {
            List<Map_Drawing> list;
            if (Width < 20 || Height < 20 || !_drawingGroups.TryGetValue("plans", out list))
                return;

            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (Map_Drawing d in list)
            {
                // Waypoints et circuits d'attente (AWACS, tanker...) : les circuits sont larges, ils comptent dans le cadrage
                if ((d.Kind != "Marker" && d.Kind != "Polygon") || d.Points.Count == 0 || !_fitFlights.Contains(d.Name)) continue;
                foreach (double[] pt in d.Points)
                {
                    if (pt[0] < minX) minX = pt[0];
                    if (pt[0] > maxX) maxX = pt[0];
                    if (pt[1] < minY) minY = pt[1];
                    if (pt[1] > maxY) maxY = pt[1];
                }
            }
            if (minX > maxX) return;       // aucun waypoint trouvé : on garde la vue sur toute la carte

            // Marge d'environ 12 % autour des vols, et au moins 20 km de côté (un vol très court ne doit pas zoomer à l'extrême)
            double rangeX = Math.Max((maxX - minX) * 1.24, 20000);
            double rangeY = Math.Max((maxY - minY) * 1.24, 20000);
            const float margin = 30f;

            float scale = (float)Math.Min((Width - 2 * margin) / rangeY, (Height - 2 * margin) / rangeX);
            _scale = Math.Max(_fitScale * 0.4f, Math.Min(2f, scale));

            double cx = (minX + maxX) / 2.0;
            double cy = (minY + maxY) / 2.0;
            _ox = Width / 2f - (float)(cy * _scale);
            _oy = Height / 2f + (float)(cx * _scale);
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

        // Publiques : la carte des événements du debrief s'en sert pour dessiner par-dessus (OverlayPaint)
        public PointF ToScreen(double x, double y)
        {
            return new PointF(_ox + (float)(y * _scale), _oy - (float)(x * _scale));
        }

        public void ToWorld(float sx, float sy, out double x, out double y)
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
            DropDragCache();
            if (!_userMoved)
                Refit();
        }

        // ------------------------------------------------------------------
        //  Dessin
        // ------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Déplacement de waypoint en cours : image mémorisée + vol déplacé seulement
            if (_dragCache != null && _dragCache.Width == Width && _dragCache.Height == Height)
            {
                g.DrawImageUnscaled(_dragCache, 0, 0);
                List<Map_Drawing> list;
                if (_shownGroups.Contains("plans") && _drawingGroups.TryGetValue("plans", out list))
                    DrawGroup(g, list.Where(x => x.Name == _dragFlight).ToList());
                DrawSelectionOverlay(g);
                return;
            }

            PaintScene(g);
        }

        private void PaintScene(Graphics g)
        {
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

            if (_parkShow) DrawParks(g, selected);

            if (_runwayEdit)
            {
                if (selected != null && !selected.IsFarp) DrawRunwayEdit(g, selected);
                else g.DrawString("Runway editing: click a base, then drag the round handles at the ends of its runway.", _fontBold, Brushes.Gold, 20, 44);
            }

            // Dessin externe (carte des événements du debrief) : par-dessus tout, sous la règle
            if (OverlayPaint != null)
            {
                try { OverlayPaint(g); }
                catch (Exception ex) { DCE_Manager.Utils.FormUtils.LogRegister("Map_UI_View | overlay : " + ex.Message); }
            }

            DrawRuler(g);
            DrawScaleBar(g, stepKm);
            DrawAreaInfo(g);
            DrawSpread(g);

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

            if (_bg.Detail != null) DrawTiles(g, _bg.Detail.AreaDrawList);      // zone demandée (dessous : le fond de la zone de combat passe par-dessus)

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
            DrawGround(g);
            foreach (string group in GroupOrder)
            {
                List<Map_Drawing> list;
                if (_shownGroups.Contains(group) && _drawingGroups.TryGetValue(group, out list))
                    DrawGroup(g, list);
            }

            if (_cacheSkip == "") DrawSelectionOverlay(g);
        }

        // Anneau doré du waypoint choisi + petite bulle d'info pendant le déplacement
        private void DrawSelectionOverlay(Graphics g)
        {
            if (_selWpFlight != "" && _shownGroups.Contains("plans"))
            {
                PointF sp = ToScreen(_selWpX, _selWpY);
                if (Finite(sp))
                    using (var ring = new Pen(Color.Gold, 2.5f))
                        g.DrawEllipse(ring, sp.X - 11, sp.Y - 11, 22, 22);
            }

            if (_dragInfo != "")
            {
                SizeF sz = g.MeasureString(_dragInfo, _fontBold);
                float bx = _mouse.X + 18, by = _mouse.Y + 18;
                if (bx + sz.Width + 10 > Width) bx = _mouse.X - sz.Width - 24;
                if (by + sz.Height + 8 > Height) by = _mouse.Y - sz.Height - 24;
                using (var back = new SolidBrush(Color.FromArgb(230, 20, 20, 20)))
                    g.FillRectangle(back, bx, by, sz.Width + 10, sz.Height + 6);
                g.DrawString(_dragInfo, _fontBold, Brushes.White, bx + 5, by + 3);
            }
        }

        // Cadenas fermé de 10 x 15 px (x, y = coin haut gauche) : anse noire, corps jaune
        private static void DrawPadlock(Graphics g, float x, float y)
        {
            using (var pen = new Pen(Color.Black, 3.2f))
                g.DrawArc(pen, x + 1.5f, y, 7f, 10f, 180, 180);
            using (var pen = new Pen(Color.FromArgb(235, 235, 235), 1.4f))
                g.DrawArc(pen, x + 1.5f, y, 7f, 10f, 180, 180);
            var body = new RectangleF(x, y + 5.5f, 10f, 8f);
            using (var br = new SolidBrush(Color.FromArgb(255, 205, 40))) g.FillRectangle(br, body);
            g.DrawRectangle(Pens.Black, body.X, body.Y, body.Width, body.Height);
            g.FillEllipse(Brushes.Black, x + 3.8f, y + 8f, 2.4f, 2.4f);
        }

        private void DrawGroup(Graphics g, List<Map_Drawing> list)
        {
            // Un vol est sélectionné : il passe en dernier (donc au-dessus), les autres sont estompés
            bool plans = _selSet.Count > 0 && list.Count > 0 && list[0].Group == "plans";
            if (plans)
                list = list.Where(x => !_selSet.Contains(x.Name)).Concat(list.Where(x => _selSet.Contains(x.Name))).ToList();

            foreach (Map_Drawing d in list)
            {
                if (d.Points.Count == 0) continue;
                if (_cacheSkip != "" && d.Group == "plans" && d.Name == _cacheSkip) continue;      // dessiné par-dessus l'image mémorisée

                bool dim = plans && !_selSet.Contains(d.Name);
                bool selected = plans && !dim;
                if (dim && (d.Kind == "Text" || d.Kind == "Arrow" || d.Kind == "Lock" || d.Kind == "Heli")) continue;   // pas de numéros ni de flèches pour les vols estompés

                if (d.Kind == "Heli")
                {
                    PointF hp = ToScreen(d.Points[0][0], d.Points[0][1]);
                    if (Finite(hp) && hp.X > -200 && hp.Y > -200 && hp.X < Width + 200 && hp.Y < Height + 200) DrawHeli(g, hp, d);
                    continue;
                }

                if (d.Kind == "Marker")
                {
                    // Marqueur de waypoint : la forme est dans d.Text, d.Thickness > 0 = gros marqueur (vol joueur)
                    PointF mp = ToScreen(d.Points[0][0], d.Points[0][1]);
                    if (Finite(mp) && mp.X > -20 && mp.Y > -20 && mp.X < Width + 20 && mp.Y < Height + 20)
                        DrawMarker(g, mp, d.Text, d.Line, MarkerPx(d), dim ? 90 : 255);
                    continue;
                }

                if (d.Kind == "Text")
                {
                    // Numéros des waypoints des vols non joueurs : seulement quand le vol est sélectionné
                    if (d.Label == "onsel" && !selected) continue;

                    PointF tp = ToScreen(d.Points[0][0], d.Points[0][1]);
                    if (d.Group == "plans") { tp.X += 8; tp.Y -= 8; }       // à côté du marqueur
                    g.DrawString(d.Text, _fontBold, Brushes.Black, tp.X + 1, tp.Y + 1);
                    using (var tb = new SolidBrush(Color.FromArgb(255, d.Line)))
                        g.DrawString(d.Text, _fontBold, tb, tp.X, tp.Y);
                    continue;
                }

                if (d.Kind == "Lock")
                {
                    // Heure verrouillée : petit cadenas fermé à gauche et au-dessus du marqueur
                    if (d.Label == "onsel" && !selected) continue;
                    PointF lp = ToScreen(d.Points[0][0], d.Points[0][1]);
                    if (Finite(lp)) DrawPadlock(g, lp.X - 17, lp.Y - 21);
                    continue;
                }

                if (d.Kind == "Arrow")
                {
                    if (d.Points.Count >= 2)
                        DrawArrowHead(g, ToScreen(d.Points[0][0], d.Points[0][1]), ToScreen(d.Points[1][0], d.Points[1][1]), d.Line);
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
                        string ctext = d.LabelText;
                        SizeF csz = g.MeasureString(ctext, _fontBold);
                        g.DrawString(ctext, _fontBold, Brushes.Black, c.X - csz.Width / 2 + 1, c.Y - csz.Height / 2 + 1);
                        g.DrawString(ctext, _fontBold, Brushes.White, c.X - csz.Width / 2, c.Y - csz.Height / 2);
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
                        using (var fb = new SolidBrush(dim ? Color.FromArgb(d.Fill.A / 3, d.Fill) : d.Fill))
                            g.FillPolygon(fb, clipped);
                }

                Color penColor = dim ? Color.FromArgb(55, d.Line) : d.Line;
                float penWidth = selected ? d.Thickness + 2f : d.Thickness;
                if (d.Alert == 1) { penColor = Color.FromArgb(255, 150, 0); penWidth = Math.Max(penWidth, 3f); }                                         // vitesse tendue : orange

                if (d.Alert == 2)
                {
                    // Impossible : jaune rayé de noir, comme la signalisation de danger au sol
                    float hw = Math.Max(penWidth, 3.5f) + 2.5f;
                    using (var black = new Pen(Color.Black, hw))
                        DrawClippedLines(g, black, pts, false);
                    using (var yellow = new Pen(Color.FromArgb(255, 215, 0), hw))
                    {
                        yellow.DashStyle = DashStyle.Custom;
                        yellow.DashPattern = new float[] { 1.2f, 1.2f };
                        DrawClippedLines(g, yellow, pts, false);
                    }
                }
                else
                    using (var pen = new Pen(penColor, penWidth))
                    {
                        if (d.Dashed) pen.DashStyle = DashStyle.Dash;
                        DrawClippedLines(g, pen, pts, d.Closed && pts.Length >= 3);
                    }

                // Nom de la zone au milieu (seulement si elle est assez grande à l'écran)
                if (d.Label != "" && !dim && pts.Length >= 3 && pts.All(Finite))
                {
                    float minX = pts.Min(q => q.X), maxX = pts.Max(q => q.X);
                    if (maxX - minX > 50)
                    {
                        PointF mid = new PointF(pts.Average(q => q.X), pts.Average(q => q.Y));
                        string ltext = d.LabelText;
                        SizeF sz = g.MeasureString(ltext, _fontBold);
                        float lx = mid.X - sz.Width / 2, ly = mid.Y - sz.Height / 2;
                        g.DrawString(ltext, _fontBold, Brushes.Black, lx + 1, ly + 1);
                        g.DrawString(ltext, _fontBold, Brushes.White, lx, ly);
                    }
                }
            }
        }


        // Marqueur de waypoint centré sur p : rond = standard, carré = IP / station, étoile = cible,
        // triangle = terrain (décollage / atterrissage), losange = point de passage du package (split, egress...)
        internal static void DrawMarker(Graphics g, PointF p, string shape, Color color, float r, int alpha)
        {
            PointF[] poly = null;
            switch (shape)
            {
                case "target":
                    {
                        float big = r * 1.5f, small = r * 0.65f;
                        poly = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double ang = -Math.PI / 2 + i * Math.PI / 5;
                            float rad = i % 2 == 0 ? big : small;
                            poly[i] = new PointF(p.X + (float)Math.Cos(ang) * rad, p.Y + (float)Math.Sin(ang) * rad);
                        }
                        break;
                    }
                case "airfield":
                    poly = new PointF[] { new PointF(p.X, p.Y - r * 1.2f), new PointF(p.X + r * 1.1f, p.Y + r * 0.85f), new PointF(p.X - r * 1.1f, p.Y + r * 0.85f) };
                    break;
                case "flow":
                    poly = new PointF[] { new PointF(p.X, p.Y - r * 1.25f), new PointF(p.X + r * 1.25f, p.Y), new PointF(p.X, p.Y + r * 1.25f), new PointF(p.X - r * 1.25f, p.Y) };
                    break;
            }

            using (var fill = new SolidBrush(Color.FromArgb(alpha, color)))
            using (var pen = new Pen(Color.FromArgb(alpha, 0, 0, 0), 1.4f) { LineJoin = LineJoin.Round })
            {
                if (poly != null) { g.FillPolygon(fill, poly); g.DrawPolygon(pen, poly); }
                else if (shape == "station") { g.FillRectangle(fill, p.X - r, p.Y - r, 2 * r, 2 * r); g.DrawRectangle(pen, p.X - r, p.Y - r, 2 * r, 2 * r); }
                else { g.FillEllipse(fill, p.X - r, p.Y - r, 2 * r, 2 * r); g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r); }
            }
        }

        // Silhouette d'hélicoptère vue de dessus (genre UH-1H), nez dans le sens du cap de l'avion.
        // Taille réelle (17 m) quand le zoom le permet, jamais plus petite que 26 pixels. u = vers le nez, v = vers la droite, en longueurs de fuselage.
        private static readonly float[][] HeliRight =
        {
            new[] { 0.46f, 0f }, new[] { 0.41f, 0.07f }, new[] { 0.28f, 0.105f }, new[] { 0.08f, 0.105f },
            new[] { -0.06f, 0.07f }, new[] { -0.14f, 0.028f }, new[] { -0.42f, 0.025f }, new[] { -0.50f, 0.05f }
        };

        private void DrawHeli(Graphics g, PointF p, Map_Drawing d)
        {
            double h = d.Radius;
            PointF q = ToScreen(d.Points[0][0] + Math.Cos(h) * 10, d.Points[0][1] + Math.Sin(h) * 10);
            if (!Finite(q)) return;
            float ang = (float)Math.Atan2(q.Y - p.Y, q.X - p.X);          // sens du nez à l'écran
            float fx = (float)Math.Cos(ang), fy = (float)Math.Sin(ang);    // vers le nez
            float rx = -fy, ry = fx;                                       // vers la droite
            float len = (float)Math.Min(400.0, Math.Max(26.0, 17.4 * _scale));

            Func<float, float, PointF> pt = (u, v) => new PointF(p.X + len * (u * fx + v * rx), p.Y + len * (u * fy + v * ry));

            // Rotor (disque transparent + deux pales)
            PointF hub = pt(0.02f, 0f);
            float rr = len * 0.42f;
            using (var disc = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
            using (var rim = new Pen(Color.FromArgb(150, 255, 255, 255), 1.2f))
            {
                g.FillEllipse(disc, hub.X - rr, hub.Y - rr, 2 * rr, 2 * rr);
                g.DrawEllipse(rim, hub.X - rr, hub.Y - rr, 2 * rr, 2 * rr);
            }

            // Patins
            using (var skid = new Pen(Color.FromArgb(230, 20, 20, 20), Math.Max(1.2f, len * 0.025f)))
            {
                foreach (float v in new[] { -0.15f, 0.15f }) g.DrawLine(skid, pt(0.25f, v), pt(-0.12f, v));
                g.DrawLine(skid, pt(0.12f, -0.15f), pt(0.12f, 0.15f));
                g.DrawLine(skid, pt(-0.05f, -0.15f), pt(-0.05f, 0.15f));
            }

            // Fuselage + poutre de queue, puis stabilisateur
            var body = new List<PointF>();
            foreach (float[] r in HeliRight) body.Add(pt(r[0], r[1]));
            for (int i = HeliRight.Length - 1; i >= 0; i--) body.Add(pt(HeliRight[i][0], -HeliRight[i][1]));
            PointF[] stab = { pt(-0.38f, -0.10f), pt(-0.38f, 0.10f), pt(-0.43f, 0.10f), pt(-0.43f, -0.10f) };

            using (var fill = new SolidBrush(Color.FromArgb(235, d.Line)))
            using (var pen = new Pen(Color.FromArgb(255, 0, 0, 0), 1.3f) { LineJoin = LineJoin.Round })
            {
                g.FillPolygon(fill, body.ToArray());
                g.DrawPolygon(pen, body.ToArray());
                g.FillPolygon(fill, stab);
                g.DrawPolygon(pen, stab);
            }

            // Pales (par-dessus) et moyeu
            using (var blade = new Pen(Color.FromArgb(200, 30, 30, 30), 1.6f))
            {
                double a1 = ang + 0.6, a2 = ang + 0.6 + Math.PI / 2;
                g.DrawLine(blade, hub.X - rr * (float)Math.Cos(a1), hub.Y - rr * (float)Math.Sin(a1), hub.X + rr * (float)Math.Cos(a1), hub.Y + rr * (float)Math.Sin(a1));
                g.DrawLine(blade, hub.X - rr * (float)Math.Cos(a2), hub.Y - rr * (float)Math.Sin(a2), hub.X + rr * (float)Math.Cos(a2), hub.Y + rr * (float)Math.Sin(a2));
            }
            g.FillEllipse(Brushes.Black, hub.X - 2.5f, hub.Y - 2.5f, 5f, 5f);

            // Numéro de l'avion dans le vol
            string num = d.Index.ToString();
            g.DrawString(num, _fontBold, Brushes.Black, p.X + len * 0.30f + 1, p.Y + len * 0.30f + 1);
            g.DrawString(num, _fontBold, Brushes.White, p.X + len * 0.30f, p.Y + len * 0.30f);
        }

        // Petite flèche pleine au milieu d'un tronçon a -> b (sens du vol). Rien si le tronçon est trop court à l'écran.
        private void DrawArrowHead(Graphics g, PointF a, PointF b, Color color)
        {
            if (!Finite(a) || !Finite(b)) return;

            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 40f) return;

            PointF mid = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
            if (!ClipArea.Contains(mid)) return;

            float ux = dx / len, uy = dy / len;     // direction
            float px = -uy, py = ux;                // perpendiculaire
            const float size = 8f;

            PointF[] tri =
            {
                new PointF(mid.X + ux * size, mid.Y + uy * size),
                new PointF(mid.X - ux * size + px * size * 0.6f, mid.Y - uy * size + py * size * 0.6f),
                new PointF(mid.X - ux * size - px * size * 0.6f, mid.Y - uy * size - py * size * 0.6f)
            };

            using (var fb = new SolidBrush(color))
            using (var op = new Pen(Color.Black, 1f))
            {
                g.FillPolygon(fb, tri);
                g.DrawPolygon(op, tri);
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
            g.DrawString(Map_Units.Dist(stepKm * 1000, "0.#"), _fontLabel, Brushes.Gainsboro, x + len + 6, y - 9);
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

            // Très gros zoom : le point de la base peut être hors écran alors que sa piste (très longue à l'écran) le traverse
            float extra = 0f;
            if (!b.IsFarp)
            {
                double maxLen = b.Runways.Count > 0 ? b.Runways.Max(q => q.Length) : 3000.0;
                extra = (float)Math.Min(1e6, (maxLen + 2500.0) * _scale / 2.0);
                if (extra < 0f) extra = 0f;
            }
            if (p.X < -150 - extra || p.Y < -30 - extra || p.X > Width + 150 + extra || p.Y > Height + 30 + extra)
                return;

            Color c = SideColor(b);
            float r = 11f;      // demi-taille de l'icône : sert aussi à décaler le texte

            List<RunwayDraw> strips = b.IsFarp ? null : BuildStrips(b, p);
            if (strips != null)
                foreach (RunwayDraw s in strips)
                    r = Math.Max(r, s.Len / 2f + (float)Math.Sqrt(s.Off.X * s.Off.X + s.Off.Y * s.Off.Y));
            r = Math.Min(r, strips != null && strips.Any(q => q.Real) ? 3000f : 45f);

            // FARP : empreinte réelle (carré creux) et ses 4 pads, quand le zoom la rend plus grande que l'icône
            if (b.IsFarp)
            {
                float half = (float)(Map_Drawings.FarpHalfM * _scale);
                if (half > 10f)
                {
                    DrawFarpFootprint(g, b, c);
                    r = Math.Max(r, Math.Min(half, 400f));
                }
            }

            if (selected)
            {
                float ring = Math.Min(r, 18f);
                using (var pen = new Pen(Color.Gold, 2.5f))
                    g.DrawEllipse(pen, p.X - ring - 5, p.Y - ring - 5, 2 * (ring + 5), 2 * (ring + 5));
            }

            // FARP = plateforme d'hélico (carré avec un H) ; base = toutes ses pistes, tournées et allongées comme les vraies.
            // Base inactive = contour seul.
            if (b.IsFarp) DrawHelipad(g, p, c, b.Inactive);
            else DrawRunways(g, p, c, b.Inactive, strips, Width, Height);

            // Plusieurs squadrons : petite silhouette d'avion à gauche, d'autant plus grande qu'il y a d'avions
            if (ShowSilhouettes && b.Squads >= 2)
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

        // Empreinte du FARP à l'échelle de la carte : carré creux (+ léger remplissage) et les 4 pads aux coins
        private void DrawFarpFootprint(Graphics g, Map_Base b, Color c)
        {
            double h = Map_Drawings.FarpHalfM;
            PointF[] sq =
            {
                ToScreen(b.X + h, b.Y - h), ToScreen(b.X + h, b.Y + h), ToScreen(b.X - h, b.Y + h), ToScreen(b.X - h, b.Y - h)
            };
            foreach (PointF q in sq) if (!Finite(q)) return;

            using (var fill = new SolidBrush(Color.FromArgb(b.Inactive ? 15 : 35, c)))
            using (var pen = new Pen(Color.FromArgb(220, c), 2f))
            {
                g.FillPolygon(fill, sq);
                g.DrawPolygon(pen, sq);
            }

            float padR = (float)(7.0 * _scale);        // un pad = environ 14 m
            if (padR < 4f) return;
            double po = Map_Drawings.FarpPadM;
            using (var pen = new Pen(Color.FromArgb(200, c), 1.6f))
                foreach (double[] o in new[] { new[] { po, -po }, new[] { po, po }, new[] { -po, po }, new[] { -po, -po } })
                {
                    PointF q = ToScreen(b.X + o[0], b.Y + o[1]);
                    if (Finite(q)) g.DrawEllipse(pen, q.X - padR, q.Y - padR, 2 * padR, 2 * padR);
                }
        }

        // Une piste à dessiner : cap, longueur à l'écran, décalage du centre par rapport au point de la base
        private class RunwayDraw
        {
            public float Hdg, Len;
            public float Wid = 7f;  // largeur (px) : fixe pour une piste, à l'échelle pour un porte-avions
            public bool Real;       // vrai = longueur réelle à l'échelle de la carte (zoom avant)
            public PointF Off;
        }

        // Une piste (les deux sens d'une même piste comptent pour une seule), avant conversion à l'écran
        private class StripWorld
        {
            public double Hdg, Length, X, Y;
        }

        // Toutes les pistes de la base : deux sens d'une même piste = une seule bande ; pistes parallèles = bandes côte à côte.
        // Dézoomé : icône de 14.7 px (piste courte) à 29.3 px (3500 m et plus), soit un tiers de moins qu'avant.
        // Zoomé : dès que la piste réelle (mètres x échelle) dépasse l'icône, on la dessine à sa vraie longueur. La largeur ne change jamais.
        // Les porte-avions de la liste des bases : pas de vraie piste, on dessine le pont à l'échelle (longueur selon le type)
        // Porte-avions (ou LHA...) : pas de vraie piste, la règle de position des pistes ne s'applique pas
        internal static bool IsCarrier(Map_Base b)
        {
            return b != null && RxCarrierBase.IsMatch((b.Name ?? "") + " " + (b.Alias ?? ""));
        }

        private static readonly System.Text.RegularExpressions.Regex RxCarrierBase = new System.Text.RegularExpressions.Regex(
            @"^\s*(CVN|CVV|CVL|CV|LHA|LHD)[-_ ]?\d|KUZNETSOV|KUZNECOW|STENNIS|TARAWA|FORRESTAL|INVINCIBLE|VINSON|NIMITZ|TRUMAN|LINCOLN|ROOSEVELT|CARRIER",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Cap (degrés) du porte-avions d'une base, repris du navire de la mission : même nom, sinon le porte-avions le plus proche (5 km max).
        // NaN = inconnu (carte sans éléments au sol). Mémorisé par base.
        private readonly Dictionary<string, double> _carrierHdg = new Dictionary<string, double>();

        private double CarrierHeading(Map_Base b)
        {
            double hdg;
            if (_carrierHdg.TryGetValue(b.Name, out hdg)) return hdg;

            hdg = double.NaN;
            Map_GroundUnit best = null;
            double bestD = 25e6;        // 5 km au carré
            string name = (b.Name ?? "").Trim();
            foreach (Map_GroundUnit u in _ground)
            {
                if (u.Kind != 'k' || double.IsNaN(u.Heading)) continue;
                if (name != "" && (u.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                    || (u.Group != null && u.Group.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)))
                { best = u; break; }

                double dx = u.X - b.X, dy = u.Y - b.Y, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = u; }
            }
            if (best != null) hdg = ((best.Heading * 180.0 / Math.PI) % 360 + 360) % 360;

            _carrierHdg[b.Name] = hdg;
            return hdg;
        }

        private List<RunwayDraw> BuildStrips(Map_Base b, PointF p)
        {
            var world = new List<StripWorld>();
            foreach (Map_Runway rw in b.Runways)
            {
                double h = ((rw.Hdg % 180) + 180) % 180;
                bool known = !(rw.X == 0 && rw.Y == 0);
                bool merged = false;

                foreach (StripWorld s in world)
                {
                    double dh = Math.Abs(s.Hdg - h);
                    if (dh > 90) dh = 180 - dh;
                    if (dh >= 8) continue;

                    double perp = 0;
                    if (known && !(s.X == 0 && s.Y == 0))
                    {
                        double a = s.Hdg * Math.PI / 180.0;
                        perp = Math.Abs(-Math.Sin(a) * (rw.X - s.X) + Math.Cos(a) * (rw.Y - s.Y));     // écart entre les deux axes (mètres)
                    }
                    if (perp < 60)
                    {
                        s.Length = Math.Max(s.Length, rw.Length);
                        merged = true;
                        break;
                    }
                }
                if (!merged && world.Count < 6)
                    world.Add(new StripWorld { Hdg = h, Length = rw.Length, X = rw.X, Y = rw.Y });
            }
            if (world.Count == 0) world.Add(new StripWorld());      // aucune piste connue : une piste nord-sud courte

            string shipText = (b.Name ?? "") + " " + (b.Alias ?? "");
            bool carrier = RxCarrierBase.IsMatch(shipText);
            double carrierLen = 0;
            if (carrier)
            {
                carrierLen = Map_GroundSize.LengthMeters('k', shipText);
                double h0 = world[0].Hdg;
                double ch = CarrierHeading(b);
                if (!double.IsNaN(ch)) h0 = ch;             // cap du navire dans la mission
                world.Clear();
                world.Add(new StripWorld { Hdg = h0, Length = carrierLen });         // un seul pont, à la longueur du bâtiment
            }

            var result = new List<RunwayDraw>();
            foreach (StripWorld s in world)
            {
                var d = new RunwayDraw { Hdg = (float)s.Hdg };
                float icon = (22f + 22f * (float)Math.Max(0.0, Math.Min(1.0, (s.Length - 1200.0) / 2300.0))) * 2f / 3f;
                float real = (float)(s.Length * _scale);
                d.Real = real > icon;          // plus de limite haute : DrawRunways ne dessine que la partie visible
                d.Len = d.Real ? real : icon;
                if (carrier && d.Real) d.Wid = Math.Max(7f, (float)(carrierLen * 0.22 * _scale));      // pont d'envol : environ 22 % de la longueur

                if (!(s.X == 0 && s.Y == 0))
                {
                    PointF q = ToScreen(s.X, s.Y);
                    float ox = q.X - p.X, oy = q.Y - p.Y;
                    float mag = (float)Math.Sqrt(ox * ox + oy * oy);
                    if (Finite(q) && !d.Real && mag > 40f) { ox *= 40f / mag; oy *= 40f / mag; }
                    if (Finite(q)) d.Off = new PointF(ox, oy);
                }
                result.Add(d);
            }

            // Pistes parallèles trop proches à l'écran : on les écarte (9 px minimum) pour qu'on voie qu'il y en a plusieurs
            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    double dh = Math.Abs(result[i].Hdg - result[j].Hdg);
                    if (dh > 90) dh = 180 - dh;
                    if (dh >= 8) continue;

                    double a = result[i].Hdg * Math.PI / 180.0;
                    float px = (float)Math.Cos(a), py = (float)Math.Sin(a);         // perpendiculaire à la piste, à l'écran
                    float sp = (result[j].Off.X - result[i].Off.X) * px + (result[j].Off.Y - result[i].Off.Y) * py;
                    if (!result[i].Real && !result[j].Real && Math.Abs(sp) < 9f)
                    {
                        float push = (sp >= 0 ? 1f : -1f) * (9f - Math.Abs(sp));
                        result[j].Off = new PointF(result[j].Off.X + px * push, result[j].Off.Y + py * push);
                    }
                }
            }
            return result;
        }

        // Piste vue de dessus : bande de la couleur du camp, liseré blanc, ligne centrale en pointillés.
        // Portion de la droite o + d*t qui reste entre min et max (réduit t0 / t1) ; false = tout est dehors
        private static bool ClipAxis(double o, double d, double min, double max, ref double t0, ref double t1)
        {
            if (Math.Abs(d) < 1e-9) return o >= min && o <= max;
            double ta = (min - o) / d, tb = (max - o) / d;
            if (ta > tb) { double k = ta; ta = tb; tb = k; }
            t0 = Math.Max(t0, ta);
            t1 = Math.Min(t1, tb);
            return t0 < t1;
        }

        private static void DrawRunways(Graphics g, PointF p, Color c, bool inactive, List<RunwayDraw> strips, int vw, int vh)
        {
            foreach (RunwayDraw s in strips)
            {
                float cx = p.X + s.Off.X, cy = p.Y + s.Off.Y, len = s.Len;

                // Piste très longue à l'écran (gros zoom) : on ne dessine que le morceau qui passe dans la fenêtre (le GDI+ n'aime pas les millions de pixels)
                if (s.Real && len > 3000f)
                {
                    double a = s.Hdg * Math.PI / 180.0, ax = -Math.Sin(a), ay = Math.Cos(a);       // sens de la piste à l'écran (rotation GDI)
                    double m = s.Wid / 2.0 + 40.0, t0 = -len / 2.0, t1 = len / 2.0;
                    if (!ClipAxis(cx, ax, -m, vw + m, ref t0, ref t1) || !ClipAxis(cy, ay, -m, vh + m, ref t0, ref t1)) continue;
                    double mid = (t0 + t1) / 2.0;
                    cx += (float)(ax * mid);
                    cy += (float)(ay * mid);
                    len = (float)(t1 - t0);
                }

                GraphicsState st = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(s.Hdg);

                var rect = new RectangleF(-s.Wid / 2f, -len / 2f, s.Wid, len);
                using (var shadow = new Pen(Color.FromArgb(150, 0, 0, 0), 4f))
                    g.DrawRectangle(shadow, rect.X, rect.Y, rect.Width, rect.Height);

                if (inactive)
                {
                    using (var pen = new Pen(c, 2f)) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
                else
                {
                    using (var br = new SolidBrush(c)) g.FillRectangle(br, rect);
                    using (var pen = new Pen(Color.White, 1.4f)) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    using (var dash = new Pen(Color.White, 1f) { DashStyle = DashStyle.Dash })
                        g.DrawLine(dash, 0, -len / 2f + 3f, 0, len / 2f - 3f);
                }
                g.Restore(st);
            }
        }

        // FARP : carré avec un "H" (plateforme d'hélicoptère)
        private static void DrawHelipad(Graphics g, PointF p, Color c, bool inactive)
        {
            var rect = new RectangleF(p.X - 8f, p.Y - 8f, 16f, 16f);
            using (var shadow = new Pen(Color.FromArgb(150, 0, 0, 0), 4f))
                g.DrawRectangle(shadow, rect.X, rect.Y, rect.Width, rect.Height);

            if (inactive)
            {
                using (var pen = new Pen(c, 2f)) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
            else
            {
                using (var br = new SolidBrush(c)) g.FillRectangle(br, rect);
                using (var pen = new Pen(Color.White, 1.4f)) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }

            Color hc = inactive ? c : Color.White;
            using (var pen = new Pen(hc, 2f))
            {
                g.DrawLine(pen, p.X - 3.5f, p.Y - 4.5f, p.X - 3.5f, p.Y + 4.5f);
                g.DrawLine(pen, p.X + 3.5f, p.Y - 4.5f, p.X + 3.5f, p.Y + 4.5f);
                g.DrawLine(pen, p.X - 3.5f, p.Y, p.X + 3.5f, p.Y);
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
            string text = Map_Units.Dist(meters);

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
                if (ShowSilhouettes && b.Squads >= 2)
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

        private static float DistToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 < 0.0001f ? 0f : ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
            t = Math.Max(0f, Math.Min(1f, t));
            float cx = a.X + t * dx - p.X, cy = a.Y + t * dy - p.Y;
            return (float)Math.Sqrt(cx * cx + cy * cy);
        }

        // Noms des vols dont un tronçon passe à moins de 6 px du clic, celui du dessus en premier
        // Marqueur de waypoint sous la souris (9 px). Un marqueur de terrain n'est pris que si son vol est déjà sélectionné
        // (sinon on ne pourrait plus cliquer sur la base qui est dessous). Calcul fait au clic seulement, pas au déplacement.
        // Rayon d'un marqueur de waypoint à l'écran : taille minimale en pixels, ou taille réelle (d.Radius en mètres) quand elle est plus grande
        private float MarkerPx(Map_Drawing d)
        {
            float r = d.Thickness > 0 ? 6.5f : 5.5f;
            if (d.Radius > 0) r = Math.Max(r, Math.Min(150f, (float)(d.Radius * _scale)));
            return r;
        }

        private Map_Drawing HitWaypoint(Point p)
        {
            List<Map_Drawing> list;
            if (!_shownGroups.Contains("plans") || !_drawingGroups.TryGetValue("plans", out list))
                return null;

            Map_Drawing best = null;
            float bestDist = float.MaxValue;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Map_Drawing d = list[i];
                if (d.Kind != "Marker" || d.Points.Count == 0) continue;
                bool mine = _selSet.Contains(d.Name);
                if (d.Text == "airfield" && !mine) continue;

                PointF q = ToScreen(d.Points[0][0], d.Points[0][1]);
                if (!Finite(q)) continue;
                float dx = q.X - p.X, dy = q.Y - p.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy) - (mine ? 3f : 0f);
                if (dist < Math.Max(9f, MarkerPx(d)) && dist < bestDist) { bestDist = dist; best = d; }
            }
            return best;
        }

        private List<string> HitFlights(Point p)
        {
            var result = new List<string>();
            List<Map_Drawing> list;
            if (!_shownGroups.Contains("plans") || !_drawingGroups.TryGetValue("plans", out list))
                return result;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                Map_Drawing d = list[i];
                if (d.Kind != "Line" || d.Points.Count < 2 || result.Contains(d.Name)) continue;

                PointF a = ToScreen(d.Points[0][0], d.Points[0][1]);
                PointF b = ToScreen(d.Points[1][0], d.Points[1][1]);
                if (!Finite(a) || !Finite(b)) continue;

                if (DistToSegment(p, a, b) <= 6f)
                    result.Add(d.Name);
            }
            return result;
        }

        // Nom affiché au survol d'un élément éclaté (donné par la fenêtre : titre, joueur / package) ; sinon le nom du groupe
        public Func<string, string> FlightLabel;

        // Nom d'un waypoint (vol, numéro) pour le survol des éléments éclatés ; donné par la fenêtre
        public Func<string, int, string> WaypointLabel;

        // ------------------------------------------------------------------
        //  Éclatement des éléments superposés
        //  Un clic sur un tas d'icônes (waypoints de plusieurs vols, base comprise) les écarte en cercle autour du tas.
        //  Une petite ligne relie chaque icône à sa vraie position. Les places sont toujours les mêmes pour un même tas
        //  (tri par nom, angle selon le rang). Un clic sur une icône la choisit, un clic ailleurs / Échap referme.
        // ------------------------------------------------------------------

        private class SpreadItem
        {
            public string Flight = "";          // "" = c'est la base
            public int Index = -1;              // numéro de waypoint (-1 = pas de waypoint précis : terrain)
            public int Num = -1;                // numéro du marqueur dans son vol (même pour un terrain), pour afficher son nom
            public Map_Base Base;
            public double WX, WY;               // vraie position (monde)
            public string Shape = "";
            public Color Color = Color.White;
            public float TX, TY;                // place finale à l'écran
        }

        private List<SpreadItem> _spread;
        private int _spreadHover = -1;
        private DateTime _spreadStart;
        private bool _spreadPreview;            // la sélection a été changée pour l'aperçu (survol)
        private string _spreadPrevFlight = "";
        private List<string> _spreadPrevSet = new List<string>();
        private readonly Timer _spreadTimer = new Timer { Interval = 15 };
        private const float SpreadMs = 160f;
        private const float SpreadHitPx = 26f;

        private float SpreadEase()
        {
            float t = (float)((DateTime.Now - _spreadStart).TotalMilliseconds / SpreadMs);
            if (t >= 1f) return 1f;
            t = 1f - t;
            return 1f - t * t * t;              // part vite, ralentit à l'arrivée
        }

        private PointF SpreadPos(SpreadItem it, float k)
        {
            PointF r = ToScreen(it.WX, it.WY);
            return new PointF(r.X + (it.TX - r.X) * k, r.Y + (it.TY - r.Y) * k);
        }

        // Zone d'écran à redessiner pendant l'animation (seulement elle, pas toute la carte)
        private Rectangle SpreadBounds()
        {
            if (_spread == null || _spread.Count == 0) return Rectangle.Empty;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (SpreadItem it in _spread)
            {
                PointF r = ToScreen(it.WX, it.WY);
                x0 = Math.Min(x0, Math.Min(r.X, it.TX)); x1 = Math.Max(x1, Math.Max(r.X, it.TX));
                y0 = Math.Min(y0, Math.Min(r.Y, it.TY)); y1 = Math.Max(y1, Math.Max(r.Y, it.TY));
            }
            return Rectangle.Inflate(Rectangle.FromLTRB((int)x0, (int)y0, (int)x1, (int)y1), 280, 60);
        }

        private void SpreadTick(object sender, EventArgs e)
        {
            if (_spread == null) { _spreadTimer.Stop(); return; }
            Invalidate(SpreadBounds());
            if (SpreadEase() >= 1f) _spreadTimer.Stop();
        }

        private int SpreadHit(Point p)
        {
            if (_spread == null) return -1;
            float k = SpreadEase();
            int best = -1;
            float bestD = SpreadHitPx * SpreadHitPx;
            for (int i = 0; i < _spread.Count; i++)
            {
                PointF q = SpreadPos(_spread[i], k);
                float dx = q.X - p.X, dy = q.Y - p.Y;
                float d = dx * dx + dy * dy;
                if (d <= bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // Plusieurs icônes sous le clic : on les éclate. false = rien d'ambigu (ou un waypoint d'un vol déjà choisi :
        // c'est lui qu'on règle), le clic suit son cours normal.
        private bool TryPickSpread(Point p)
        {
            List<Map_Drawing> list;
            if (!_shownGroups.Contains("plans") || !_drawingGroups.TryGetValue("plans", out list)) return false;

            Map_Drawing own = HitWaypoint(p);
            if (own != null && own.Text != "airfield" && _selSet.Contains(own.Name)) return false;

            // Un élément par vol : son waypoint s'il y en a un sous le clic, sinon son marqueur de terrain
            var found = new Dictionary<string, SpreadItem>();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Map_Drawing d = list[i];
                if (d.Kind != "Marker" || d.Points.Count == 0) continue;

                PointF q = ToScreen(d.Points[0][0], d.Points[0][1]);
                if (!Finite(q)) continue;
                float dx = q.X - p.X, dy = q.Y - p.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > Math.Max(9f, MarkerPx(d))) continue;

                bool wp = d.Text != "airfield";
                SpreadItem old;
                if (found.TryGetValue(d.Name, out old) && (old.Index >= 0 || !wp)) continue;

                found[d.Name] = new SpreadItem
                {
                    Flight = d.Name,
                    Index = wp ? d.Index : -1,
                    Num = d.Index,
                    WX = d.Points[0][0],
                    WY = d.Points[0][1],
                    Shape = d.Text,
                    Color = d.Line
                };
            }

            Map_Base hb = HitTest(p);
            if (found.Count + (hb != null ? 1 : 0) < 2) return false;

            var items = new List<SpreadItem>();
            if (hb != null) items.Add(new SpreadItem { Base = hb, WX = hb.X, WY = hb.Y });
            foreach (string n in found.Keys.OrderBy(x => x, StringComparer.Ordinal).Take(24))
                items.Add(found[n]);

            // Centre du tas (moyenne des vraies positions), cercle de rayon : 3 icônes au minimum, plus s'il y en a beaucoup
            float cx = 0, cy = 0;
            foreach (SpreadItem it in items) { PointF r = ToScreen(it.WX, it.WY); cx += r.X; cy += r.Y; }
            cx /= items.Count; cy /= items.Count;

            float radius = Math.Max(150f, items.Count * 95f / (float)(2 * Math.PI));
            for (int i = 0; i < items.Count; i++)
            {
                double ang = -Math.PI / 2 + 2 * Math.PI * i / items.Count;       // la base (ou le 1er) en haut, puis dans le sens horaire
                items[i].TX = Math.Max(30f, Math.Min(Width - 30f, cx + radius * (float)Math.Cos(ang)));
                items[i].TY = Math.Max(30f, Math.Min(Height - 30f, cy + radius * (float)Math.Sin(ang)));
            }

            _spreadPrevFlight = _selectedFlight;
            _spreadPrevSet = new List<string>(_selSet);
            _spreadPreview = false;
            _spreadHover = -1;
            _spread = items;
            _spreadStart = DateTime.Now;
            _spreadTimer.Start();
            Invalidate(SpreadBounds());
            return true;
        }

        // restore = true : on remet la sélection d'avant (si le survol l'a changée pour l'aperçu)
        private void CloseSpread(bool restore)
        {
            if (_spread == null) return;
            Rectangle r = SpreadBounds();
            _spread = null;
            _spreadHover = -1;
            _spreadTimer.Stop();

            bool full = false;
            if (_spreadPreview && restore)
            {
                _selectedFlight = _spreadPrevFlight;
                _selSet.Clear();
                foreach (string n in _spreadPrevSet) _selSet.Add(n);
                full = true;
            }
            _spreadPreview = false;
            if (full) Invalidate(); else Invalidate(r);
        }

        private void SpreadChoose(SpreadItem it)
        {
            if (it.Base != null)
            {
                string bn = it.Base.Name;
                CloseSpread(true);
                _selectedName = bn;
                Invalidate();
                BaseSelected?.Invoke(bn);
                return;
            }

            string name = it.Flight;
            CloseSpread(false);
            _selectedFlight = name;
            _selSet.Clear();
            _selSet.Add(name);
            if (it.Index >= 0)
            {
                SetSelectedWaypoint(name, it.Index);
                WaypointSelected?.Invoke(name, it.Index);
            }
            else
            {
                if (name != _selWpFlight) { _selWpFlight = ""; _selWpIndex = -1; }
                Invalidate();
                FlightsSelected?.Invoke(_selSet.ToList());
            }
        }

        // Survol : l'élément sous la souris est entouré, son nom s'affiche et son vol est mis en évidence sur la carte (aperçu)
        private void UpdateSpreadHover(Point p)
        {
            int h = SpreadHit(p);
            if (h == _spreadHover) return;
            _spreadHover = h;
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;

            if (h >= 0 && _spread[h].Base == null)
            {
                _selectedFlight = _spread[h].Flight;
                _selSet.Clear();
                _selSet.Add(_selectedFlight);
                _spreadPreview = true;
                Invalidate();
            }
            else if (_spreadPreview)
            {
                _selectedFlight = _spreadPrevFlight;
                _selSet.Clear();
                foreach (string n in _spreadPrevSet) _selSet.Add(n);
                _spreadPreview = false;
                Invalidate();
            }
            else Invalidate(SpreadBounds());
        }

        private void DrawSpread(Graphics g)
        {
            if (_spread == null) return;
            float k = SpreadEase();

            using (var leg = new Pen(Color.FromArgb(210, 255, 255, 255), 1.5f))
                foreach (SpreadItem it in _spread)
                {
                    PointF r = ToScreen(it.WX, it.WY);
                    PointF q = SpreadPos(it, k);
                    g.DrawLine(leg, r, q);
                    g.FillEllipse(Brushes.White, r.X - 2f, r.Y - 2f, 4f, 4f);
                }

            for (int i = 0; i < _spread.Count; i++)
            {
                SpreadItem it = _spread[i];
                PointF q = SpreadPos(it, k);
                bool hot = i == _spreadHover;

                using (var back = new SolidBrush(Color.FromArgb(215, 15, 15, 15)))
                    g.FillEllipse(back, q.X - 24f, q.Y - 24f, 48f, 48f);
                using (var ring = new Pen(hot ? Color.Gold : Color.White, hot ? 2.5f : 1.5f))
                    g.DrawEllipse(ring, q.X - 24f, q.Y - 24f, 48f, 48f);

                if (it.Base != null)
                {
                    using (var bigFont = new Font(_fontBold.FontFamily, 18f, FontStyle.Bold))
                    {
                        SizeF sz = g.MeasureString("B", bigFont);
                        g.DrawString("B", bigFont, Brushes.Gold, q.X - sz.Width / 2f, q.Y - sz.Height / 2f);
                    }
                }
                else DrawMarker(g, q, it.Shape, it.Color, 15f, 255);
            }

            // Nom de l'élément survolé, à côté de lui
            if (_spreadHover >= 0 && _spreadHover < _spread.Count)
            {
                SpreadItem it = _spread[_spreadHover];
                PointF q = SpreadPos(it, k);
                string text;
                if (it.Base != null) text = "Base: " + (string.IsNullOrEmpty(it.Base.Alias) ? it.Base.Name : it.Base.Name + " (" + it.Base.Alias + ")");
                else
                {
                    text = FlightLabel != null ? FlightLabel(it.Flight) : it.Flight;
                    string wl = WaypointLabel != null ? WaypointLabel(it.Flight, it.Num) : "";
                    if (wl != "") text += "\n" + wl;
                }

                SizeF sz = g.MeasureString(text, _fontBold);
                float bx = q.X + 32f, by = q.Y - sz.Height / 2f;
                if (bx + sz.Width + 10f > Width) bx = q.X - sz.Width - 42f;
                using (var back = new SolidBrush(Color.FromArgb(235, 20, 20, 20)))
                    g.FillRectangle(back, bx, by, sz.Width + 10f, sz.Height + 4f);
                g.DrawString(text, _fontBold, Brushes.White, bx + 5f, by + 2f);
            }
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

                // Mode déplacement : un waypoint du vol sélectionné sous la souris se tire (pas les terrains : décollage / atterrissage)
                _dragFlight = "";
                _dragMoved = false;

                // Places d'hélicoptère : une silhouette (ou sa poignée de cap) sous la souris se tire
                _parkDrag = null;
                if (_parkShow && !_parkAdd && !_rulerEnabled)
                {
                    Map_ParkSpot ps; int pp;
                    if (HitPark(e.Location, out ps, out pp))
                    {
                        _parkDrag = ps;
                        _parkPart = pp;
                        _parkMoved = false;
                        double gx, gy;
                        ToWorld(e.X, e.Y, out gx, out gy);
                        _parkGrabX = gx - ps.X; _parkGrabY = gy - ps.Y;
                        if (!ReferenceEquals(_parkSel, ps)) { _parkSel = ps; ParkSelected?.Invoke(ps); }
                        Invalidate();
                        return;
                    }
                }

                // Mode pistes : une poignée sous la souris se tire
                _rwDrag = null;
                if (_runwayEdit && !_rulerEnabled)
                {
                    Map_Runway hr; int hp;
                    if (HitRunwayHandle(e.Location, out hr, out hp))
                    {
                        _rwDrag = hr;
                        _rwPart = hp;
                        _rwMoved = false;

                        double ax, ay, bx, by;
                        RunwayEnds(hr, out ax, out ay, out bx, out by);
                        if (hp == 0) { _rwFixX = bx; _rwFixY = by; }
                        else if (hp == 1) { _rwFixX = ax; _rwFixY = ay; }
                        else
                        {
                            double wx, wy;
                            ToWorld(e.X, e.Y, out wx, out wy);
                            _rwGrabX = wx - hr.X; _rwGrabY = wy - hr.Y;
                        }
                        return;
                    }
                }

                if (_moveEnabled && !_rulerEnabled && _spread == null && _drawingGroups.ContainsKey("plans"))
                {
                    Map_Drawing wp = HitWaypoint(e.Location);
                    if (wp != null && _selSet.Contains(wp.Name) && wp.Text != "airfield")
                    {
                        _dragFlight = wp.Name;
                        _dragIndex = wp.Index;
                    }
                }
            }
            else if (e.Button == MouseButtons.Right && _rulerEnabled)
            {
                ClearRuler();
            }
            else if (e.Button == MouseButtons.Right && _moveEnabled && _spread == null && _drawingGroups.ContainsKey("plans"))
            {
                ShowPlanMenu(e.Location);
            }
        }

        // Clic droit sur un waypoint du vol choisi : le supprimer. Sur un tronçon du vol choisi : y ajouter un waypoint (le point
        // est posé sur le trait lui-même, à l'endroit du clic).
        private void ShowPlanMenu(Point p)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false };

            Map_Drawing wp = HitWaypoint(p);
            if (wp != null && _selSet.Contains(wp.Name) && wp.Text != "airfield")
            {
                string fl = wp.Name;
                int ix = wp.Index;
                var del = new ToolStripMenuItem("Delete waypoint " + ix);
                del.Click += (s, e) => { if (WaypointDeleteRequested != null) WaypointDeleteRequested(fl, ix); };
                menu.Items.Add(del);
            }
            else
            {
                string name;
                int idx;
                double wx, wy;
                if (HitLeg(p, out name, out idx, out wx, out wy))
                {
                    var add = new ToolStripMenuItem("Add a waypoint here (new waypoint " + idx + ")");
                    add.Click += (s, e) => { if (WaypointInsertRequested != null) WaypointInsertRequested(name, idx, wx, wy); };
                    menu.Items.Add(add);
                }
            }

            if (menu.Items.Count == 0) { menu.Dispose(); return; }
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(this, p);
        }

        // Tronçon d'un vol choisi sous le clic : nom du vol, place du nouveau waypoint (= numéro du waypoint d'arrivée du tronçon), point du trait le plus proche (monde)
        private bool HitLeg(Point p, out string flight, out int index, out double wx, out double wy)
        {
            flight = ""; index = -1; wx = 0; wy = 0;
            List<Map_Drawing> list;
            if (!_drawingGroups.TryGetValue("plans", out list)) return false;

            float best = 9f;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Map_Drawing d = list[i];
                if (d.Kind != "Line" || d.Index < 1 || d.Points.Count < 2 || !_selSet.Contains(d.Name)) continue;

                PointF a = ToScreen(d.Points[0][0], d.Points[0][1]);
                PointF b = ToScreen(d.Points[1][0], d.Points[1][1]);
                if (!Finite(a) || !Finite(b)) continue;

                float dist = DistToSegment(p, a, b);
                if (dist >= best) continue;

                float vx = b.X - a.X, vy = b.Y - a.Y, len2 = vx * vx + vy * vy;
                double t = len2 < 1e-6f ? 0.5 : ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2;
                t = Math.Max(0.05, Math.Min(0.95, t));                  // pas collé à un waypoint existant

                best = dist;
                flight = d.Name;
                index = d.Index;
                wx = d.Points[0][0] + t * (d.Points[1][0] - d.Points[0][0]);
                wy = d.Points[0][1] + t * (d.Points[1][1] - d.Points[0][1]);
            }
            return index >= 1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = e.Location;

            if (_spread != null && e.Button == MouseButtons.None)
            {
                UpdateSpreadHover(e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left && _parkDrag != null)
            {
                DragPark(e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left && _rwDrag != null)
            {
                DragRunway(e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left && _dragFlight != "")
            {
                if (!_dragMoved && (Math.Abs(e.X - _downPoint.X) > 3 || Math.Abs(e.Y - _downPoint.Y) > 3))
                {
                    _dragMoved = true;
                    BuildDragCache();
                }

                if (_dragMoved)
                {
                    double wx, wy;
                    ToWorld(e.X, e.Y, out wx, out wy);
                    WaypointMoving?.Invoke(_dragFlight, _dragIndex, wx, wy);
                }
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (!_panning && (Math.Abs(e.X - _downPoint.X) > 4 || Math.Abs(e.Y - _downPoint.Y) > 4))
                {
                    _panning = true;
                    CloseSpread(true);
                }

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
                Map_Runway hr; int hp;
                Map_ParkSpot hs; int hpart;
                if (_parkAdd) Cursor = Cursors.Cross;
                else if (_parkShow && HitPark(e.Location, out hs, out hpart)) Cursor = hpart == 1 ? Cursors.Cross : Cursors.SizeAll;
                else if (_runwayEdit && HitRunwayHandle(e.Location, out hr, out hp)) Cursor = Cursors.SizeAll;
                else Cursor = HitTest(e.Location) != null ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Left && _parkDrag != null)
            {
                Map_ParkSpot ps = _parkDrag;
                bool changed = _parkMoved;
                _parkDrag = null;
                _parkMoved = false;
                Invalidate();
                if (changed) ParksEdited?.Invoke(ps.Base);
                return;
            }

            if (e.Button == MouseButtons.Left && _rwDrag != null)
            {
                bool changed = _rwMoved;
                _rwDrag = null;
                _rwMoved = false;
                Invalidate();
                if (changed && _rwPrepared != null) RunwaysEdited?.Invoke(_rwPrepared.Name);
                return;
            }

            if (e.Button == MouseButtons.Left && _dragFlight != "")
            {
                bool moved = _dragMoved;
                string fl = _dragFlight;
                int ix = _dragIndex;
                _dragFlight = "";
                _dragMoved = false;
                _dragInfo = "";
                DropDragCache();
                if (moved)
                {
                    WaypointDropped?.Invoke(fl, ix);
                    Invalidate();
                    return;
                }
            }

            if (e.Button != MouseButtons.Left || _panning)
                return;

            // Après "Add" : ce clic place la nouvelle place d'hélicoptère
            if (_parkAdd)
            {
                double px, py;
                ToWorld(e.X, e.Y, out px, out py);
                ParkAddMode = false;
                ParkAddClicked?.Invoke(px, py);
                return;
            }
            if (_parkSel != null) { _parkSel = null; ParkSelected?.Invoke(null); Invalidate(); }

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

            // Un tas est déjà éclaté : un clic sur une icône la choisit, ailleurs il se referme
            if (_spread != null)
            {
                int si = SpreadHit(e.Location);
                if (si >= 0) SpreadChoose(_spread[si]); else CloseSpread(true);
                return;
            }

            // Carte de mission : plusieurs icônes superposées sous le clic (waypoints de plusieurs vols, base) -> elles s'écartent (CTRL + clic : sélection multiple, pas d'éclatement)
            if (_drawingGroups.ContainsKey("plans") && (ModifierKeys & Keys.Control) != Keys.Control && TryPickSpread(e.Location))
                return;

            // Carte de mission : un waypoint sous le clic passe avant le reste
            if (_drawingGroups.ContainsKey("plans"))
            {
                Map_Drawing wp = HitWaypoint(e.Location);
                if (wp != null)
                {
                    _selectedFlight = wp.Name;
                    _selSet.Clear();
                    _selSet.Add(wp.Name);
                    SetSelectedWaypoint(wp.Name, wp.Index);
                    WaypointSelected?.Invoke(wp.Name, wp.Index);
                    return;
                }
            }

            // Unité au sol sous le clic (si elles sont affichées)
            Map_GroundUnit gu = HitGround(e.Location);
            if (gu != null && HitTest(e.Location) == null)
            {
                _selGround = gu;
                Invalidate();
                GroundClicked?.Invoke(gu);
                return;
            }
            if (_selGround != null) { _selGround = null; Invalidate(); }

            Map_Base hit = HitTest(e.Location);
            if (hit != null)
            {
                _selectedName = hit.Name;
                Invalidate();
                BaseSelected?.Invoke(hit.Name);
                return;
            }

            // Carte de mission : sinon on cherche un plan de vol sous le clic (clics répétés = vol suivant)
            if (_drawingGroups.ContainsKey("plans"))
            {
                List<string> names = HitFlights(e.Location);
                string chosen = "";

                // CTRL + clic : on ajoute / retire le vol du dessus à la sélection
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    if (names.Count > 0)
                    {
                        string n = names[0];
                        if (!_selSet.Remove(n)) _selSet.Add(n);
                        _selectedFlight = _selSet.Contains(n) ? n : (_selSet.Count > 0 ? _selSet.Last() : "");
                        Invalidate();
                        FlightsSelected?.Invoke(_selSet.ToList());
                    }
                    return;
                }

                if (names.Count > 0)
                {
                    string key = string.Join("|", names);
                    bool same = key == _cycleKey && Math.Abs(e.X - _cycleAt.X) <= 8 && Math.Abs(e.Y - _cycleAt.Y) <= 8;
                    _cycleIndex = same ? (_cycleIndex + 1) % names.Count : 0;
                    _cycleKey = key;
                    _cycleAt = e.Location;
                    chosen = names[_cycleIndex];
                }
                else
                {
                    _cycleKey = "";
                }

                if (chosen != _selectedFlight || chosen != "")
                {
                    if (chosen != _selWpFlight) { _selWpFlight = ""; _selWpIndex = -1; }
                    _selectedFlight = chosen;
                    _selSet.Clear();
                    if (chosen != "") _selSet.Add(chosen);
                    Invalidate();
                    FlightsSelected?.Invoke(_selSet.ToList());
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            CloseSpread(true);

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
            {
                CloseSpread(true);
                ClearRuler();
                if (_parkAdd) ParkAddMode = false;
            }
            else if (e.KeyCode == Keys.Delete && _parkShow && _parkSel != null)
            {
                ParkDeleteRequested?.Invoke();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _detailTimer.Stop();
                _detailTimer.Dispose();
                _tilePaintTimer.Stop();
                _tilePaintTimer.Dispose();
                _spreadTimer.Stop();
                _spreadTimer.Dispose();
                _areaMsgTimer.Stop();
                _areaMsgTimer.Dispose();
                DropDragCache();
                _dim.Dispose();
                _dimBrush.Dispose();
                _fontLabel.Dispose();
                _fontBold.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
