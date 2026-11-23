using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Tuiles plus détaillées, chargées seulement pour la partie de la carte que l'utilisateur regarde.
    // Quand on zoome, la vue demande le bon niveau de zoom : les tuiles déjà en cache s'affichent tout de suite,
    // les autres sont téléchargées une par une en arrière-plan (jamais plus de MaxWanted tuiles à la fois).
    // Le fond de base reste dessiné dessous : si une tuile manque ou n'est pas encore arrivée, on voit la version floue.
    internal class Map_Detail : IDisposable
    {
        private const int MaxWanted = 150;      // tuiles visibles demandées en même temps
        private const int MaxCache = 480;       // tuiles gardées en mémoire (environ 125 Mo au maximum)
        private const int ExtraZoom = 11;       // niveaux de zoom au-dessus du fond de base
        private const int AbsMaxZoom = 19;      // plafond absolu ; le vrai plafond est celui du style (osm 18, esri 17...)
        private const int AutoStep = 2;         // détail automatique : un niveau de zoom sur AutoStep (1 = tous les niveaux, 3 = un sur trois). Le niveau le plus fin est toujours chargé.

        private class Entry
        {
            public Bitmap Image;
            public List<Map_Tile> Pieces;
            public int Zoom;
            public long Stamp;
        }

        private readonly Map_Projection _p;
        private readonly int _z, _x0, _x1, _y0, _y1;
        private readonly Map_Style _style;
        private readonly double _refLat;
        private readonly object _lock = new object();
        private readonly Dictionary<long, Entry> _cache = new Dictionary<long, Entry>();
        private readonly HashSet<long> _failed = new HashSet<long>();
        private HashSet<long> _wanted = new HashSet<long>();
        private List<long> _missing = new List<long>();
        private List<long> _missingArea = new List<long>();     // tuiles de la zone demandée (traitées après celles de l'écran)
        private int _wantedZoom;
        private bool _working, _disposed;
        private long _stamp;
        private volatile List<Map_Tile> _drawList = new List<Map_Tile>();

        // Appelé depuis le thread de téléchargement quand une tuile arrive
        public event Action Changed;

        public List<Map_Tile> DrawList { get { return _drawList; } }

        public Map_Detail(Map_Projection p, int z, int x0, int x1, int y0, int y1, Map_Style style)
        {
            _p = p; _z = z; _x0 = x0; _x1 = x1; _y0 = y0; _y1 = y1; _style = style;
            _refLat = Map_Web.TileLat((y0 + y1 + 1) / 2.0, z);
        }

        private static long Key(int zoom, int tx, int ty)
        {
            return ((long)zoom << 50) | ((long)tx << 25) | (long)ty;
        }

        // Niveau de zoom utile pour l'échelle actuelle de la vue (pixels écran par mètre). 0 = le fond de base suffit.
        public int ZoomFor(double pixelsPerMeter)
        {
            double m = 156543.03392 * Math.Cos(_refLat * Math.PI / 180.0) * pixelsPerMeter;
            if (m <= 0) return 0;

            int zd = (int)Math.Ceiling(Math.Log(m, 2) - 0.5);
            int max = Math.Min(Math.Min(_style.MaxZoom, AbsMaxZoom), _z + ExtraZoom);
            if (zd > max) zd = max;
            return zd > _z ? zd : 0;
        }

        // Niveau le plus fin que ce style peut donner ici
        public int MaxLevel { get { return Math.Min(Math.Min(_style.MaxZoom, AbsMaxZoom), _z + ExtraZoom); } }

        // Niveau chargé automatiquement pour le niveau demandé zd (0 = le fond de base suffit) :
        // on saute des niveaux pour ne pas recharger des séries de tuiles à chaque coup de molette (le bouton "Detail now" force le niveau exact).
        // On prend le niveau inférieur (un peu plus flou, 4 fois moins de tuiles) ; le niveau le plus fin du style est toujours pris.
        public int AutoZoom(int zd)
        {
            if (zd <= _z) return 0;
            int cap = MaxLevel;
            if (zd >= cap) return cap;
            int z = _z + ((zd - _z) / AutoStep) * AutoStep;
            return z > _z ? z : 0;
        }

        // Quelles tuiles de ce zoom touchent l'écran ? (liste: clé + distance au centre de l'écran)
        // On descend de la grande tuile du fond de base vers les petites : une tuile qui ne touche pas l'écran
        // est abandonnée avec toutes ses filles (avec ExtraZoom élevé, tout tester une par une prendrait plusieurs secondes).
        private void CollectVisible(int zd, Func<double, double, PointF> toScreen, Rectangle screen, List<KeyValuePair<long, double>> result)
        {
            var area = new RectangleF(screen.X - 20, screen.Y - 20, screen.Width + 40, screen.Height + 40);
            float cx = screen.X + screen.Width / 2f, cy = screen.Y + screen.Height / 2f;

            for (int bx = _x0; bx <= _x1; bx++)
                for (int by = _y0; by <= _y1; by++)
                    Descend(bx, by, _z, zd, toScreen, area, cx, cy, result);
        }

        private void Descend(int tx, int ty, int zoom, int zd, Func<double, double, PointF> toScreen, RectangleF area, float cx, float cy, List<KeyValuePair<long, double>> result)
        {
            RectangleF box;
            if (!Touches(tx, ty, 1, zoom, toScreen, area, out box)) return;

            if (zoom >= zd)
            {
                double dx = box.X + box.Width / 2 - cx, dy = box.Y + box.Height / 2 - cy;
                result.Add(new KeyValuePair<long, double>(Key(zd, tx, ty), dx * dx + dy * dy));
                return;
            }
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    Descend(tx * 2 + i, ty * 2 + j, zoom + 1, zd, toScreen, area, cx, cy, result);
        }

        private bool Touches(int tx, int ty, int span, int zoom, Func<double, double, PointF> toScreen, RectangleF area, out RectangleF box)
        {
            double[] lat = { Map_Web.TileLat(ty, zoom), Map_Web.TileLat(ty + span, zoom) };
            double[] lon = { Map_Web.TileLon(tx, zoom), Map_Web.TileLon(tx + span, zoom) };
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            for (int a = 0; a < 2; a++)
            {
                for (int b = 0; b < 2; b++)
                {
                    double x, y;
                    Map_Web.LatLonToDcs(_p, lat[a], lon[b], out x, out y);
                    PointF s = toScreen(x, y);
                    if (s.X < minX) minX = s.X;
                    if (s.X > maxX) maxX = s.X;
                    if (s.Y < minY) minY = s.Y;
                    if (s.Y > maxY) maxY = s.Y;
                }
            }
            box = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            return box.IntersectsWith(area);
        }

        // À appeler (thread de l'interface) quand la vue s'est arrêtée de bouger
        public void Update(int zd, Func<double, double, PointF> toScreen, Rectangle screen)
        {
            var visible = new List<KeyValuePair<long, double>>();

            while (zd > _z)
            {
                visible.Clear();
                CollectVisible(zd, toScreen, screen, visible);
                if (visible.Count <= MaxWanted) break;
                zd--;               // trop de tuiles : on prend un niveau de moins
            }
            if (zd <= _z) visible.Clear();

            visible.Sort((a, b) => a.Value.CompareTo(b.Value));     // les plus proches du centre d'abord

            bool start = false;
            lock (_lock)
            {
                if (_disposed) return;

                _wanted = new HashSet<long>(visible.Select(v => v.Key));
                _wantedZoom = zd;
                _stamp++;

                _missing = new List<long>();
                foreach (KeyValuePair<long, double> v in visible)
                {
                    Entry e;
                    if (_cache.TryGetValue(v.Key, out e)) e.Stamp = _stamp;
                    else if (!_failed.Contains(v.Key)) _missing.Add(v.Key);
                }

                // Trop de tuiles en mémoire : on libère les plus anciennes qui ne sont pas à l'écran
                if (_cache.Count > MaxCache)
                {
                    foreach (long k in _cache.Where(c => !_wanted.Contains(c.Key) && !_aWanted.Contains(c.Key)).OrderBy(c => c.Value.Stamp).Select(c => c.Key).ToList())
                    {
                        if (_cache.Count <= MaxCache) break;
                        _cache[k].Image.Dispose();
                        _cache.Remove(k);
                    }
                }

                RebuildDrawList();

                if (_missing.Count > 0 && !_working)
                {
                    _working = true;
                    start = true;
                }
            }

            if (start)
                ThreadPool.QueueUserWorkItem(_ => Work());
        }

        // (dans le verrou) Les morceaux à dessiner : TOUTES les tuiles déjà en mémoire au zoom demandé (pas seulement celles de l'écran).
        // Quand on fait glisser la carte, les tuiles déjà vues réapparaissent donc tout de suite, sans attendre la fin du mouvement.
        // (le dessin ignore ce qui est hors de l'écran) ; les tuiles de la "zone demandée" ont leur propre liste.
        private void RebuildDrawList()
        {
            var list = new List<Map_Tile>();
            if (_wantedZoom > _z)
            {
                foreach (KeyValuePair<long, Entry> kv in _cache)
                {
                    if (kv.Value.Zoom != _wantedZoom) continue;
                    if (_wanted.Contains(kv.Key) || !_aWanted.Contains(kv.Key))
                        list.AddRange(kv.Value.Pieces);
                }
            }
            _drawList = list;
        }

        // Thread de téléchargement : une tuile à la fois, comme pour le fond de base
        private void Work()
        {
            ThreadPriority oldPriority = Thread.CurrentThread.Priority;
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;     // l'interface passe avant le chargement des tuiles
                while (true)
                {
                    long key;
                    lock (_lock)
                    {
                        if (_disposed || (_missing.Count == 0 && _missingArea.Count == 0)) return;
                        if (_missing.Count > 0) { key = _missing[0]; _missing.RemoveAt(0); }
                        else { key = _missingArea[0]; _missingArea.RemoveAt(0); }
                    }

                    int zoom = (int)(key >> 50);
                    int tx = (int)((key >> 25) & 0x1FFFFFF);
                    int ty = (int)(key & 0x1FFFFFF);

                    try
                    {
                        string file = Map_Web.TileFile(_style, zoom, tx, ty);

                        // Web interdit et tuile absente du disque : on saute, sans la marquer "échouée" (elle reviendra si le web est réautorisé)
                        if (!File.Exists(file) && !Main_WebAccess.Enabled)
                            continue;

                        if (!File.Exists(file))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(file));
                            Map_Web.Download(Map_Web.TileUrl(_style, zoom, tx, ty), file);
                            Thread.Sleep(80);
                        }

                        Bitmap img = Map_Web.LoadBitmap(file);
                        List<Map_Tile> pieces = Map_Web.MakePieces(_p, zoom, tx, ty, img, 2);

                        lock (_lock)
                        {
                            if (_disposed) { img.Dispose(); return; }
                            _cache[key] = new Entry { Image = img, Pieces = pieces, Zoom = zoom, Stamp = _stamp };
                            if (_wanted.Contains(key) || zoom == _wantedZoom) RebuildDrawList();
                            if (_aWanted.Contains(key)) RebuildAreaList();
                        }

                        Action changed = Changed;
                        if (changed != null) changed();
                        Thread.Sleep(4);        // laisse respirer l'interface entre deux tuiles
                    }
                    catch (Exception ex)
                    {
                        lock (_lock) { _failed.Add(key); }
                        FormUtils.LogRegister("Map_Detail | tuile " + zoom + "/" + tx + "/" + ty + " : " + ex.Message);
                    }
                }
            }
            finally
            {
                try { Thread.CurrentThread.Priority = oldPriority; } catch { }
                lock (_lock) { _working = false; }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                foreach (Entry e in _cache.Values) e.Image.Dispose();
                _cache.Clear();
                _drawList = new List<Map_Tile>();
                _areaDraw = new List<Map_Tile>();
            }
        }

        // ------------------------------------------------------------------
        //  Zone demandée par l'utilisateur (bouton "charger la zone visible")
        //  Hors zone de combat si besoin. Plus de AreaMaxTiles tuiles : on prend un zoom plus grossier.
        //  Ensuite, tant que l'utilisateur zoome DANS cette zone, les tuiles plus fines sont chargées toutes seules ;
        //  s'il regarde ailleurs, on ne fait rien (il doit recliquer sur le bouton).
        // ------------------------------------------------------------------

        private const int AreaMaxTiles = 100;
        private const int AreaMinZoom = 3;

        private bool _areaOn;
        private double _aMinLat, _aMaxLat, _aMinLon, _aMaxLon;
        private int _aZoom;
        private List<long> _aCoarse = new List<long>();
        private List<long> _aFine = new List<long>();
        private HashSet<long> _aWanted = new HashSet<long>();
        private volatile List<Map_Tile> _areaDraw = new List<Map_Tile>();

        // À dessiner AVANT le fond de base (les tuiles de la zone de combat passent par-dessus)
        public List<Map_Tile> AreaDrawList { get { return _areaDraw; } }

        // Pour la petite info "x / y tuiles" pendant le chargement
        public void AreaProgress(out int loaded, out int total)
        {
            lock (_lock)
            {
                total = _aWanted.Count;
                loaded = 0;
                foreach (long k in _aWanted)
                    if (_cache.ContainsKey(k)) loaded++;
            }
        }

        private int ZoomAt(double pixelsPerMeter, double lat)
        {
            double m = 156543.03392 * Math.Cos(lat * Math.PI / 180.0) * pixelsPerMeter;
            if (m <= 0) return AreaMinZoom;
            int zd = (int)Math.Ceiling(Math.Log(m, 2) - 0.5);
            int max = Math.Min(_style.MaxZoom, AbsMaxZoom);
            if (zd > max) zd = max;
            return Math.Max(zd, AreaMinZoom);
        }

        private void WorldBox(List<double[]> worldPts, out double minLat, out double maxLat, out double minLon, out double maxLon)
        {
            minLat = 90; maxLat = -90; minLon = 180; maxLon = -180;
            foreach (double[] pt in worldPts)
            {
                double lat, lon;
                Map_Web.DcsToLatLon(_p, pt[0], pt[1], out lat, out lon);
                if (lat < minLat) minLat = lat;
                if (lat > maxLat) maxLat = lat;
                if (lon < minLon) minLon = lon;
                if (lon > maxLon) maxLon = lon;
            }
        }

        // Clic sur le bouton. worldPts = points de l'écran (coins, milieux, centre) en coordonnées DCS.
        // Renvoie le nombre de tuiles et si le zoom a dû être réduit (zone grossière).
        public int RequestArea(double pixelsPerMeter, List<double[]> worldPts, out bool coarse)
        {
            double minLat, maxLat, minLon, maxLon;
            WorldBox(worldPts, out minLat, out maxLat, out minLon, out maxLon);

            int best = ZoomAt(pixelsPerMeter, (minLat + maxLat) / 2);
            int z = best, x0, x1, y0, y1;
            while (true)
            {
                x0 = Map_Web.TileX(minLon, z); x1 = Map_Web.TileX(maxLon, z);
                y0 = Map_Web.TileY(maxLat, z); y1 = Map_Web.TileY(minLat, z);
                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) <= AreaMaxTiles || z <= AreaMinZoom) break;
                z--;
            }
            coarse = z < best;

            var keys = new List<KeyValuePair<long, double>>();
            double cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0;
            for (int tx = x0; tx <= x1; tx++)
                for (int ty = y0; ty <= y1; ty++)
                    keys.Add(new KeyValuePair<long, double>(Key(z, tx, ty), (tx - cx) * (tx - cx) + (ty - cy) * (ty - cy)));
            keys.Sort((a, b) => a.Value.CompareTo(b.Value));        // du centre vers les bords

            bool start = false;
            lock (_lock)
            {
                if (_disposed) return 0;

                _areaOn = true;
                _aMinLat = minLat; _aMaxLat = maxLat; _aMinLon = minLon; _aMaxLon = maxLon;
                _aZoom = z;
                _aCoarse = keys.Select(k => k.Key).ToList();
                _aFine = new List<long>();
                start = RefreshAreaWanted();
            }

            if (start) ThreadPool.QueueUserWorkItem(_ => Work());
            return keys.Count;
        }

        // À appeler (thread de l'interface) quand la vue s'est arrêtée de bouger : tuiles fines si on est dans la zone
        public void UpdateArea(double pixelsPerMeter, List<double[]> worldPts, double centerX, double centerY, Func<double, double, PointF> toScreen, Rectangle screen)
        {
            if (!_areaOn) return;

            double clat, clon;
            Map_Web.DcsToLatLon(_p, centerX, centerY, out clat, out clon);
            bool inside = clat >= _aMinLat && clat <= _aMaxLat && clon >= _aMinLon && clon <= _aMaxLon;

            var fine = new List<long>();
            int zd = ZoomAt(pixelsPerMeter, clat);

            if (inside && zd > _aZoom)
            {
                double minLat, maxLat, minLon, maxLon;
                WorldBox(worldPts, out minLat, out maxLat, out minLon, out maxLon);
                minLat = Math.Max(minLat, _aMinLat); maxLat = Math.Min(maxLat, _aMaxLat);
                minLon = Math.Max(minLon, _aMinLon); maxLon = Math.Min(maxLon, _aMaxLon);

                var area = new RectangleF(screen.X - 20, screen.Y - 20, screen.Width + 40, screen.Height + 40);
                float cx = screen.X + screen.Width / 2f, cy = screen.Y + screen.Height / 2f;
                var found = new List<KeyValuePair<long, double>>();

                for (; zd > _aZoom; zd--)
                {
                    found.Clear();
                    int x0 = Map_Web.TileX(minLon, zd), x1 = Map_Web.TileX(maxLon, zd);
                    int y0 = Map_Web.TileY(maxLat, zd), y1 = Map_Web.TileY(minLat, zd);
                    if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > AreaMaxTiles * 4) continue;       // beaucoup trop : inutile de tester une par une

                    for (int tx = x0; tx <= x1; tx++)
                    {
                        for (int ty = y0; ty <= y1; ty++)
                        {
                            RectangleF box;
                            if (!Touches(tx, ty, 1, zd, toScreen, area, out box)) continue;
                            double dx = box.X + box.Width / 2 - cx, dy = box.Y + box.Height / 2 - cy;
                            found.Add(new KeyValuePair<long, double>(Key(zd, tx, ty), dx * dx + dy * dy));
                        }
                    }
                    if (found.Count <= AreaMaxTiles) break;
                }

                if (zd > _aZoom)
                {
                    found.Sort((a, b) => a.Value.CompareTo(b.Value));
                    fine = found.Select(k => k.Key).ToList();
                }
            }

            bool start;
            lock (_lock)
            {
                if (_disposed || !_areaOn) return;
                _aFine = fine;
                start = RefreshAreaWanted();
            }
            if (start) ThreadPool.QueueUserWorkItem(_ => Work());
        }

        // (dans le verrou) Recalcule les tuiles voulues / manquantes de la zone. Renvoie true s'il faut lancer le thread.
        private bool RefreshAreaWanted()
        {
            _aWanted = new HashSet<long>(_aCoarse);
            foreach (long k in _aFine) _aWanted.Add(k);

            _stamp++;
            _missingArea = new List<long>();
            foreach (long k in _aFine.Concat(_aCoarse).Distinct())      // les fines (celles qu'on regarde) d'abord
            {
                Entry e;
                if (_cache.TryGetValue(k, out e)) e.Stamp = _stamp;
                else if (!_failed.Contains(k)) _missingArea.Add(k);
            }

            RebuildAreaList();

            if ((_missingArea.Count > 0) && !_working)
            {
                _working = true;
                return true;
            }
            return false;
        }

        // (dans le verrou) Tuiles grossières d'abord, puis les fines par-dessus
        private void RebuildAreaList()
        {
            var list = new List<Map_Tile>();
            foreach (long k in _aCoarse)
            {
                Entry e;
                if (_cache.TryGetValue(k, out e)) list.AddRange(e.Pieces);
            }
            foreach (long k in _aFine)
            {
                Entry e;
                if (_cache.TryGetValue(k, out e)) list.AddRange(e.Pieces);
            }
            _areaDraw = list;
        }
    }
}
