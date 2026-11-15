using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Sonde le relief réel (tuiles d'altitude Terrarium, zoom 12 = environ 35 m par pixel) pour dire si un point
    // est dans l'eau ou sur une pente trop forte. Sert à placer les formations, les points de ravitaillement
    // (WargameSpawnSolver) et les FARP (WargameFarpStore).
    //
    // Règles :
    //  - les zones et polygones du campaignMaker passent TOUJOURS en premier : ici on ne fait qu'affiner.
    //  - le web ne bloque jamais rien : délai court, 2 échecs de suite = web coupé pour la session,
    //    et sans réponse on dit "inconnu" (donc on accepte le point).
    //  - l'interrupteur de la barre du haut (Main_WebAccess) interdit tout accès réseau ;
    //    les tuiles déjà téléchargées (Documents\DCE_Manager\MapCache\Terrain) restent utilisées.
    //
    // Limite connue : c'est le relief du monde réel, pas celui de DCS (le calage peut avoir 1 km d'écart).
    // On s'en sert donc pour écarter l'évident (mer, gros lac, forte pente), pas pour du détail.
    internal static class WargameRelief
    {
        public const double MaxSlope = 0.15;        // 15 % : véhicules
        public const double MaxSlopeFarp = 0.08;    // 8 % : FARP

        private const int Zoom = 12;
        private const int TimeoutMs = 3000;         // par tuile
        private const int MaxDownloads = 300;       // garde-fou par session
        private const int MaxFailsInRow = 2;        // 2 échecs réseau de suite = web coupé
        private const string TileUrl = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{0}/{1}/{2}.png";
        private const string UserAgent = "DCE_Manager/1.0 (DCS campaign manager; few tiles, cached)";

        public struct Info
        {
            public bool Known;         // false = pas de donnée (web coupé, théâtre inconnu...) : on ne juge pas
            public double Elevation;   // mètres
            public double Slope;       // 0.15 = 15 %
            public bool Water;         // mer, ou grand plan d'eau plat
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, float[]> Tiles = new Dictionary<string, float[]>();
        private static readonly HashSet<string> NoTile = new HashSet<string>();

        private static string _campaign = "";
        private static Map_Projection _proj;
        private static bool _projTried;
        private static bool _webDown;
        private static int _failsInRow;
        private static int _downloads;
        private static int _checked;
        private static int _rejected;

        // Facultatif : un appelant qui connaît la campagne peut la donner ici.
        // Sinon on prend la campagne sélectionnée dans l'interface.
        public static string Campaign = "";

        static WargameRelief()
        {
            // L'utilisateur vient de ré-autoriser le web : on retente (même si il était "tombé")
            Main_WebAccess.Changed += delegate
            {
                lock (Sync) { _webDown = false; _failsInRow = 0; NoTile.Clear(); }
            };
        }

        // ---------------------------------------------------------------- API ----------------------------------------------------------------

        // Vrai si TOUS les points sont acceptables (ou inconnus). terrainClass = zone.Terrain ("coastal"... ) :
        // en zone côtière on ne rejette pas l'eau (plages, calage imprécis), seulement la pente.
        public static bool PlacementOk(IEnumerable<PointF> points, string terrainClass)
        {
            bool coastal = string.Equals(terrainClass, "coastal", StringComparison.OrdinalIgnoreCase);

            lock (Sync)
            {
                try
                {
                    foreach (PointF p in points)
                    {
                        Info info = ProbeLocked(p.X, p.Y);
                        if (!info.Known) continue;

                        _checked++;
                        bool bad = info.Slope > MaxSlope || (info.Water && !coastal);
                        if (bad)
                        {
                            _rejected++;
                            if (_rejected == 1 || _rejected % 25 == 0)
                                FormUtils.LogRegister("WargameRelief | point refusé (" + (info.Water ? "eau" : "pente " + (info.Slope * 100).ToString("0") + " %")
                                    + ") ; refus cumulés " + _rejected + " / " + _checked + " points sondés");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("WargameRelief | erreur ignorée : " + ex.Message);
                }
            }

            return true;
        }

        // FARP : si le point de départ est dans l'eau ou trop pentu, cherche le point acceptable le plus proche
        // DANS le polygone de la plage. null = rien à corriger (ou rien de mieux trouvé) : on garde le point d'origine.
        public static PointF? FindFarpPoint(PointF start, List<PointF> polygon)
        {
            lock (Sync)
            {
                try
                {
                    Info first = ProbeLocked(start.X, start.Y);
                    if (!first.Known || FarpOk(first)) return null;

                    for (double r = 100; r <= 3000; r += 100)
                    {
                        int samples = Math.Max(12, (int)(2 * Math.PI * r / 100));
                        for (int i = 0; i < samples; i++)
                        {
                            double a = 2 * Math.PI * i / samples;
                            var p = new PointF((float)(start.X + r * Math.Cos(a)), (float)(start.Y + r * Math.Sin(a)));

                            if (polygon != null && polygon.Count >= 3 && !InsidePolygon(p, polygon)) continue;

                            Info info = ProbeLocked(p.X, p.Y);
                            if (info.Known && FarpOk(info))
                            {
                                FormUtils.LogRegister("WargameRelief | FARP déplacée de " + r.ToString("0") + " m (départ : "
                                    + (first.Water ? "eau" : "pente " + (first.Slope * 100).ToString("0") + " %") + ")");
                                return p;
                            }
                        }
                    }

                    FormUtils.LogRegister("WargameRelief | FARP : point de départ mauvais mais rien de mieux dans la plage, position gardée");
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("WargameRelief | erreur ignorée : " + ex.Message);
                }
            }

            return null;
        }

        // ---------------------------------------------------------------- Drop zone ----------------------------------------------------------------

        public const double MaxSlopeDrop = 0.06;    // 6 % : zone de largage (caisses posées au sol)

        // Cherche un terrain dégagé (disque de 'radius' m : ni eau, ni pente) en ARRIERE de 'anchor'.
        // rearDir = direction de l'arrière (vecteur unitaire, x = nord, y = est). On teste des distances croissantes
        // (minDist -> maxDist) et, à chaque distance, des angles de plus en plus écartés de l'arrière (jusqu'à +/- 100 degrés) :
        // le premier endroit valable est donc le plus proche et le plus "derrière".
        // allowed : filtre facultatif sur le centre (ex. rester dans la zone). null = pas de filtre.
        // Retourne null si rien de bon, ou si le relief est inconnu (web coupé...) : l'appelant garde alors son comportement d'origine.
        public static PointF? FindClearArea(PointF anchor, PointF rearDir, double radius, double minDist, double maxDist, Func<PointF, bool> allowed)
        {
            lock (Sync)
            {
                try
                {
                    if (!ProbeLocked(anchor.X, anchor.Y).Known) return null;

                    double baseAngle = Math.Atan2(rearDir.Y, rearDir.X);
                    double[] offsets = { 0, 25, -25, 50, -50, 75, -75, 100, -100 };

                    for (double d = minDist; d <= maxDist; d += 100)
                    {
                        foreach (double off in offsets)
                        {
                            double a = baseAngle + off * Math.PI / 180.0;
                            var c = new PointF((float)(anchor.X + d * Math.Cos(a)), (float)(anchor.Y + d * Math.Sin(a)));

                            if (allowed != null && !allowed(c)) continue;
                            if (ClearDisk(c, radius, allowed)) return c;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("WargameTerrain | erreur ignorée : " + ex.Message);
                }
            }

            return null;
        }

        // Centre + 2 anneaux (rayon / 2 et rayon) : tous connus, hors de l'eau, pente faible
        private static bool ClearDisk(PointF c, double radius, Func<PointF, bool> allowed)
        {
            for (int ring = 0; ring <= 2; ring++)
            {
                double r = ring * radius / 2.0;
                int n = ring == 0 ? 1 : 8;
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    var p = new PointF((float)(c.X + r * Math.Cos(a)), (float)(c.Y + r * Math.Sin(a)));

                    if (ring == 2 && allowed != null && !allowed(p)) return false;

                    Info info = ProbeLocked(p.X, p.Y);
                    if (!info.Known || info.Water || info.Slope > MaxSlopeDrop) return false;
                }
            }
            return true;
        }

        // Relief d'un point (coordonnées DCS : x = nord, y = est)
        public static Info Probe(double x, double y)
        {
            lock (Sync)
            {
                try { return ProbeLocked(x, y); }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("WargameRelief | erreur ignorée : " + ex.Message);
                    return new Info();
                }
            }
        }

        private static bool FarpOk(Info i)
        {
            return !i.Water && i.Slope <= MaxSlopeFarp;
        }

        // ---------------------------------------------------------------- Sonde ----------------------------------------------------------------

        private static Info ProbeLocked(double x, double y)
        {
            var result = new Info();
            if (!Prepare()) return result;

            bool web = Main_WebAccess.Enabled;

            double lat, lon;
            Map_Web.DcsToLatLon(_proj, x, y, out lat, out lon);

            double n = (1 << Zoom) * 256.0;
            double latRad = lat * Math.PI / 180.0;
            double gxf = (lon + 180.0) / 360.0 * n;
            double gyf = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n;
            int gx = (int)Math.Floor(gxf), gy = (int)Math.Floor(gyf);

            float h0 = PixelHeight(gx, gy, web);
            if (float.IsNaN(h0)) return result;

            float hE = PixelHeight(gx + 1, gy, web), hW = PixelHeight(gx - 1, gy, web);
            float hN = PixelHeight(gx, gy - 1, web), hS = PixelHeight(gx, gy + 1, web);
            if (float.IsNaN(hE) || float.IsNaN(hW) || float.IsNaN(hN) || float.IsNaN(hS)) return result;

            double pixelMeters = 40075016.686 * Math.Cos(latRad) / n;
            double dzx = (hE - hW) / (2.0 * pixelMeters);
            double dzy = (hS - hN) / (2.0 * pixelMeters);

            result.Known = true;
            result.Elevation = h0;
            result.Slope = Math.Sqrt(dzx * dzx + dzy * dzy);
            // le test "5 x 5 identiques" ne se fait que si le point est déjà plat (économie de calcul)
            result.Water = h0 < 0.5f || (result.Slope < 0.001 && IsFlatBlock(gx, gy, h0, web));
            return result;
        }

        // Un plan d'eau est plat au centimètre près : 5 x 5 pixels identiques = eau (la terre n'est presque jamais aussi plate)
        private static bool IsFlatBlock(int gx, int gy, float h0, bool web)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    float h = PixelHeight(gx + dx, gy + dy, web);
                    if (float.IsNaN(h) || Math.Abs(h - h0) > 0.02f) return false;
                }
            }
            return true;
        }

        // Prépare la campagne : projection du théâtre. Faux = théâtre inconnu, on ne juge rien.
        private static bool Prepare()
        {
            string camp = !string.IsNullOrEmpty(Campaign) ? Campaign : ParamCampaignSelected.NameCampaign;
            if (string.IsNullOrEmpty(camp)) return false;

            if (camp != _campaign)
            {
                _campaign = camp;
                _proj = null;
                _projTried = false;
                _webDown = false;
                _failsInRow = 0;
                _downloads = 0;
                Tiles.Clear();
                NoTile.Clear();
            }

            if (!_projTried)
            {
                _projTried = true;
                string theatre;
                _proj = Map_Web.KnownProjection(camp, out theatre);
                FormUtils.LogRegister("WargameRelief | campagne " + camp + " : théâtre '" + theatre + "' "
                    + (_proj == null ? "inconnu, relief non sondé" : "connu, relief sondé (web " + (Main_WebAccess.Enabled ? "autorisé" : "interdit") + ")"));
            }

            return _proj != null;
        }

        // ---------------------------------------------------------------- Tuiles ----------------------------------------------------------------

        private static float PixelHeight(int gx, int gy, bool web)
        {
            int max = (1 << Zoom) * 256;
            if (gx < 0 || gy < 0 || gx >= max || gy >= max) return float.NaN;

            float[] t = GetTile(gx >> 8, gy >> 8, web);
            if (t == null) return float.NaN;
            return t[(gy & 255) * 256 + (gx & 255)];
        }

        private static float[] GetTile(int tx, int ty, bool web)
        {
            string key = tx + "/" + ty;

            float[] cached;
            if (Tiles.TryGetValue(key, out cached)) return cached;
            if (NoTile.Contains(key)) return null;

            string file = Path.Combine(ParamManager.pathManager, "MapCache", "Terrain", Zoom.ToString(), tx.ToString(), ty + ".png");

            if (!File.Exists(file))
            {
                // pas sur le disque : web interdit, coupé, ou garde-fou atteint = "inconnu", sans rien mémoriser de définitif
                if (!web || _webDown || _downloads >= MaxDownloads) return null;

                _downloads++;
                if (!Fetch(string.Format(TileUrl, Zoom, tx, ty), file))
                {
                    NoTile.Add(key);
                    return null;
                }
            }

            float[] heights = Decode(file);
            if (heights == null)
            {
                try { File.Delete(file); } catch { }   // fichier abîmé : sera retéléchargé plus tard
                NoTile.Add(key);
                return null;
            }

            if (Tiles.Count > 60) Tiles.Clear();      // mémoire : environ 256 Ko par tuile
            Tiles[key] = heights;
            return heights;
        }

        // Télécharge une tuile avec un délai court. Ne lève jamais d'exception.
        private static bool Fetch(string url, string dest)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest));

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;
                req.UserAgent = UserAgent;

                string tmp = dest + ".tmp";
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (FileStream fs = File.Create(tmp))
                    s.CopyTo(fs);

                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);

                _failsInRow = 0;
                if (_downloads % 25 == 0)
                    FormUtils.LogRegister("WargameRelief | " + _downloads + " tuile(s) de relief téléchargée(s)");
                return true;
            }
            catch (WebException ex)
            {
                // 404 = cette tuile n'existe pas (pas un problème de réseau)
                var r = ex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) return false;

                NetworkFailed(ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                NetworkFailed(ex.Message);
                return false;
            }
        }

        private static void NetworkFailed(string message)
        {
            _failsInRow++;
            if (_failsInRow >= MaxFailsInRow && !_webDown)
            {
                _webDown = true;
                FormUtils.LogRegister("WargameRelief | web indisponible (" + message + ") : relief web coupé pour cette session, "
                    + "le placement continue avec les zones seules");
            }
        }

        // Altitude Terrarium : R*256 + G + B/256 - 32768 (mètres)
        private static float[] Decode(string file)
        {
            try
            {
                using (Bitmap bmp = Map_Web.LoadBitmap(file))
                {
                    if (bmp.Width != 256 || bmp.Height != 256) return null;

                    BitmapData d = bmp.LockBits(new Rectangle(0, 0, 256, 256), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        byte[] buf = new byte[Math.Abs(d.Stride) * 256];
                        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);

                        var h = new float[256 * 256];
                        for (int y = 0; y < 256; y++)
                        {
                            for (int x = 0; x < 256; x++)
                            {
                                int i = y * d.Stride + x * 4;      // ordre en mémoire : B, G, R, A
                                h[y * 256 + x] = buf[i + 2] * 256f + buf[i + 1] + buf[i] / 256f - 32768f;
                            }
                        }
                        return h;
                    }
                    finally
                    {
                        bmp.UnlockBits(d);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool InsidePolygon(PointF p, List<PointF> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; i++)
            {
                PointF a = polygon[i], b = polygon[j];
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    float xCross = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < xCross) inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}
