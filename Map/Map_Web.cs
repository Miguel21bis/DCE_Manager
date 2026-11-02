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
    // Une base utilisée pour caler la carte (nom + code ICAO + position DCS)
    internal class Map_Match
    {
        public string Name;
        public string Icao;
        public double X;        // DCS x (nord)
        public double Y;        // DCS y (est)
        public double Lat;      // position réelle (OurAirports)
        public double Lon;
        public double ErrorM;   // écart après calage, en mètres
    }

    // Une tuile de fond, avec ses 3 coins exprimés en coordonnées DCS
    // (haut-gauche, haut-droite, bas-gauche) : la vue n'a plus qu'à les passer à DrawImage.
    internal class Map_Tile
    {
        public Bitmap Image;
        public double TlX, TlY, TrX, TrY, BlX, BlY;
        public RectangleF Src;      // partie de l'image utilisée (une tuile est découpée en petits morceaux)
    }

    // Un style de fond de carte (serveur de tuiles). {z} {x} {y} sont remplacés dans l'adresse.
    internal class Map_Style
    {
        public string Id;
        public string Label;
        public string Url;
        public string Attribution;
        public int MaxZoom;
    }

    internal class Map_Background : IDisposable
    {
        public List<Map_Tile> Tiles = new List<Map_Tile>();
        public string Report = "";
        public string Attribution = "";
        public Map_Detail Detail;       // tuiles plus fines, chargées selon le zoom de l'utilisateur

        // Données pour construire la mosaïque (voir Map_Web.BuildMosaic)
        public Map_Projection Proj;
        public Map_Style Style;
        public int Z, X0, X1, Y0, Y1;

        // Mosaïque : toutes les tuiles du fond collées en UNE image alignée sur le nord DCS.
        // Dessiner une seule image est bien plus rapide que des centaines de petits morceaux inclinés.
        public Bitmap Mosaic;
        public double MosaicMinX, MosaicMaxX, MosaicMinY, MosaicMaxY, MosaicPix;
        public bool Disposed;

        // Appelé dans le thread de l'interface : la mosaïque est prête, on libère les tuiles d'origine
        public void AttachMosaic(Bitmap img, double minX, double maxX, double minY, double maxY, double pix)
        {
            if (Disposed) { img.Dispose(); return; }

            Mosaic = img;
            MosaicMinX = minX; MosaicMaxX = maxX; MosaicMinY = minY; MosaicMaxY = maxY; MosaicPix = pix;

            var done = new HashSet<Bitmap>();
            foreach (Map_Tile t in Tiles)
                if (t.Image != null && done.Add(t.Image)) t.Image.Dispose();
            Tiles.Clear();
        }

        public void Dispose()
        {
            Disposed = true;
            if (Detail != null) Detail.Dispose();
            if (Mosaic != null) { Mosaic.Dispose(); Mosaic = null; }
            // Les morceaux d'une même tuile partagent la même image : on ne la libère qu'une fois
            var done = new HashSet<Bitmap>();
            foreach (Map_Tile t in Tiles)
                if (t.Image != null && done.Add(t.Image)) t.Image.Dispose();
            Tiles.Clear();
        }
    }

    // Passage latitude/longitude -> coordonnées DCS : projection Transverse Mercator,
    // avec méridien central (Cm) et décalages (Fe, Fn) retrouvés par le calage.
    //   DCS y (est)  = Easting  + Fe
    //   DCS x (nord) = Northing + Fn
    internal class Map_Projection
    {
        public double Cm, Fe, Fn;
    }

    // Fond de carte pris sur le web, calé automatiquement sur les bases qui ont un code ICAO.
    //  1) liste des aéroports du monde (OurAirports, domaine public) : téléchargée une seule fois
    //  2) on retrouve chaque base par son code ICAO -> position réelle (lat/lon)
    //  3) on cherche la projection qui superpose le mieux positions réelles et positions DCS
    //  4) on télécharge les tuiles OpenStreetMap utiles (peu nombreuses, gardées en cache)
    // Tout est gardé dans Documents\DCE_Manager\MapCache : rien n'est retéléchargé ensuite.
    internal static class Map_Web
    {
        private const string AirportsUrl = "https://davidmegginson.github.io/ourairports-data/airports.csv";
        private const string UserAgent = "DCE_Manager/1.0 (DCS campaign manager; few tiles, cached)";
        private const int Cut = 4;             // une tuile = 4 x 4 morceaux (voir LoadTiles)
        private const int MaxTiles = 200;      // zoom 9 pour la zone du Golfe (environ 140 tuiles, 3 Mo)
        private const string CalibVersion = "2";   // changer ce numéro force un nouveau téléchargement chez tout le monde
        private const double K0 = 0.9996;

        // Styles proposés. Chaque style a son dossier de cache (l'ancien cache OpenStreetMap est gardé tel quel).
        public static readonly List<Map_Style> Styles = new List<Map_Style>
        {
            new Map_Style { Id = "osm", Label = "Standard - OpenStreetMap (local names)", MaxZoom = 18,
                Url = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
                Attribution = "© OpenStreetMap contributors" },
            new Map_Style { Id = "carto_voyager", Label = "Clean - Carto Voyager (Latin letters)", MaxZoom = 18,
                Url = "https://a.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png",
                Attribution = "© OpenStreetMap contributors © CARTO" },
            new Map_Style { Id = "carto_light", Label = "Light grey - Carto Positron (Latin letters)", MaxZoom = 18,
                Url = "https://a.basemaps.cartocdn.com/light_all/{z}/{x}/{y}.png",
                Attribution = "© OpenStreetMap contributors © CARTO" },
            new Map_Style { Id = "topo", Label = "Relief and contour lines - OpenTopoMap", MaxZoom = 17,
                Url = "https://a.tile.opentopomap.org/{z}/{x}/{y}.png",
                Attribution = "© OpenStreetMap contributors, SRTM | © OpenTopoMap (CC-BY-SA)" },
            new Map_Style { Id = "esri_shaded", Label = "Shaded relief, no names - Esri", MaxZoom = 13,
                Url = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Shaded_Relief/MapServer/tile/{z}/{y}/{x}",
                Attribution = "Tiles © Esri" },
            new Map_Style { Id = "esri_sat", Label = "Satellite, no names - Esri World Imagery", MaxZoom = 17,
                Url = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                Attribution = "Tiles © Esri, Maxar, Earthstar Geographics" }
        };

        private static string _currentStyleId;

        private static string StylePath
        {
            get { return Path.Combine(CacheRoot, "style.txt"); }
        }

        // Style choisi (gardé d'une fois sur l'autre, commun à toutes les campagnes)
        public static Map_Style CurrentStyle
        {
            get
            {
                if (_currentStyleId == null)
                {
                    try { _currentStyleId = File.Exists(StylePath) ? File.ReadAllText(StylePath).Trim() : "osm"; }
                    catch { _currentStyleId = "osm"; }
                }
                return Styles.FirstOrDefault(x => x.Id == _currentStyleId) ?? Styles[0];
            }
            set
            {
                _currentStyleId = value.Id;
                try
                {
                    Directory.CreateDirectory(CacheRoot);
                    File.WriteAllText(StylePath, value.Id);
                }
                catch { }
            }
        }

        private static string CacheRoot
        {
            get { return Path.Combine(ParamManager.pathManager, "MapCache"); }
        }

        private static string CalibPath(string campaign)
        {
            return Path.Combine(CacheRoot, "Calib_" + campaign + ".txt");
        }

        public static bool HasCalibration(string campaign)
        {
            return File.Exists(CalibPath(campaign));
        }

        // Efface le calage de la campagne (les tuiles et la liste d'aéroports restent en cache)
        public static void ForgetCalibration(string campaign)
        {
            try { File.Delete(CalibPath(campaign)); } catch { }
        }

        // ------------------------------------------------------------------
        //  Lecture du cache (aucun accès réseau)
        // ------------------------------------------------------------------

        public static Map_Background TryLoadCached(string campaign)
        {
            try
            {
                string path = CalibPath(campaign);
                if (!File.Exists(path))
                    return null;

                var d = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }

                // Calage fait avec une ancienne version (tuiles moins détaillées) : on le refait
                if (!d.ContainsKey("v") || d["v"] != CalibVersion)
                    return null;

                var p = new Map_Projection
                {
                    Cm = Dbl(d["cm"]),
                    Fe = Dbl(d["fe"]),
                    Fn = Dbl(d["fn"])
                };
                int z = int.Parse(d["z"], CultureInfo.InvariantCulture);
                int x0 = int.Parse(d["x0"], CultureInfo.InvariantCulture);
                int x1 = int.Parse(d["x1"], CultureInfo.InvariantCulture);
                int y0 = int.Parse(d["y0"], CultureInfo.InvariantCulture);
                int y1 = int.Parse(d["y1"], CultureInfo.InvariantCulture);

                Map_Background bg = LoadTiles(p, z, x0, x1, y0, y1);
                bg.Report = d.ContainsKey("report") ? d["report"] : "";
                return bg.Tiles.Count > 0 ? bg : null;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Web | TryLoadCached : " + ex.Message);
                return null;
            }
        }

        private static double Dbl(string s)
        {
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        //  Construction complète (réseau) : à appeler dans un thread de fond
        // ------------------------------------------------------------------

        public static Map_Background BuildBackground(string campaign, List<Map_Match> inputs, List<double[]> allPoints, IProgress<string> progress)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Directory.CreateDirectory(CacheRoot);

            // 1) Aéroports réels reconnus par leur code ICAO (s'il y en a)
            var matches = new List<Map_Match>();
            var notFound = new List<string>();

            if (inputs.Count > 0)
            {
                string csv = Path.Combine(CacheRoot, "airports.csv");
                if (!File.Exists(csv))
                {
                    progress.Report("Downloading the airport list (about 12 MB, only once)...");
                    Download(AirportsUrl, csv);
                }

                progress.Report("Looking for the airbases by ICAO code...");
                var wanted = new HashSet<string>(inputs.Select(m => m.Icao), StringComparer.OrdinalIgnoreCase);
                Dictionary<string, double[]> coords = ReadAirports(csv, wanted);

                foreach (Map_Match m in inputs)
                {
                    double[] c;
                    if (coords.TryGetValue(m.Icao, out c))
                    {
                        m.Lat = c[0];
                        m.Lon = c[1];
                        matches.Add(m);
                    }
                    else
                    {
                        notFound.Add(m.Name);
                    }
                }
            }

            // 2) Projection : calage sur les bases (si au moins 3), et/ou projection connue du théâtre (Nevada, Caucase...).
            //    Si les deux existent, on garde celle qui colle le mieux aux bases.
            var used = new List<Map_Match>();
            Map_Projection fit = null;
            if (matches.Count >= 3)
            {
                progress.Report("Calibrating on " + matches.Count + " airbases...");
                fit = FitRobust(matches, used);
            }

            string theatre;
            Map_Projection known = KnownProjection(campaign, out theatre);

            Map_Projection proj;
            bool useKnown;
            if (fit == null)
            {
                if (known == null)
                    throw new Exception("Only " + matches.Count + " airbase(s) with a known ICAO code, and the theatre of this campaign"
                        + (theatre != "" ? " (" + theatre + ")" : "") + " is not in the list of known maps. Cannot place the web map.");
                useKnown = true;
            }
            else if (known == null)
            {
                useKnown = false;
            }
            else
            {
                double eFit = used.Average(m => m.ErrorM);
                double eKnown = used.Average(m => ErrorOf(known, m));
                useKnown = eKnown <= eFit * 1.25 + 150;
            }

            double minLat, maxLat, minLon, maxLon;
            if (useKnown)
            {
                proj = known;
                foreach (Map_Match m in matches) m.ErrorM = ErrorOf(known, m);
                used = matches.Where(m => m.ErrorM < 5000).ToList();

                // Emprise : toutes les bases posées sur la carte, ramenées en latitude / longitude
                minLat = minLon = double.MaxValue;
                maxLat = maxLon = double.MinValue;
                foreach (double[] pt in allPoints)
                {
                    double lat, lon;
                    DcsToLatLon(known, pt[0], pt[1], out lat, out lon);
                    if (lat < minLat) minLat = lat;
                    if (lat > maxLat) maxLat = lat;
                    if (lon < minLon) minLon = lon;
                    if (lon > maxLon) maxLon = lon;
                }
                if (minLat > maxLat) throw new Exception("No airbase with a position on the map.");
                minLat -= 0.9; maxLat += 0.9; minLon -= 0.9; maxLon += 0.9;
            }
            else
            {
                proj = fit;
                minLat = used.Min(m => m.Lat) - 0.9; maxLat = used.Max(m => m.Lat) + 0.9;
                minLon = used.Min(m => m.Lon) - 0.9; maxLon = used.Max(m => m.Lon) + 0.9;
            }

            // 3) Tuiles nécessaires
            int z, x0, x1, y0, y1;
            for (z = Math.Min(10, CurrentStyle.MaxZoom); z > 4; z--)
            {
                x0 = TileX(minLon, z); x1 = TileX(maxLon, z);
                y0 = TileY(maxLat, z); y1 = TileY(minLat, z);
                if ((x1 - x0 + 1) * (y1 - y0 + 1) <= MaxTiles) break;
            }
            x0 = TileX(minLon, z); x1 = TileX(maxLon, z);
            y0 = TileY(maxLat, z); y1 = TileY(minLat, z);

            int total = (x1 - x0 + 1) * (y1 - y0 + 1);
            int done = 0, failed = 0;
            for (int tx = x0; tx <= x1; tx++)
            {
                for (int ty = y0; ty <= y1; ty++)
                {
                    done++;
                    string file = TileFile(z, tx, ty);
                    if (File.Exists(file)) continue;

                    progress.Report("Downloading the map: tile " + done + " / " + total + "...");
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        Download(TileUrl(CurrentStyle, z, tx, ty), file);
                        Thread.Sleep(80);       // un seul téléchargement à la fois, sans brusquer le serveur
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        FormUtils.LogRegister("Map_Web | tile " + z + "/" + tx + "/" + ty + " : " + ex.Message);
                    }
                }
            }
            if (failed > total / 3)
                throw new Exception(failed + " map tiles out of " + total + " could not be downloaded. Check the internet connection.");

            // 4) Rapport + sauvegarde du calage
            var report = new StringBuilder();
            string how = useKnown ? "Map placed with the known projection of the " + theatre + " map" : "Map placed using the real airports (ICAO codes)";
            if (used.Count > 0)
            {
                double mean = used.Average(m => m.ErrorM);
                Map_Match worst = used.OrderByDescending(m => m.ErrorM).First();
                report.Append(how + ", checked on " + used.Count + " airbases. Average gap: " + Math.Round(mean)
                    + " m, largest: " + worst.Name + " " + Math.Round(worst.ErrorM) + " m. Zoom " + z + ".");
            }
            else
            {
                report.Append(how + " (no airbase with an ICAO code to check it). Zoom " + z + ".");
            }

            var skipped = matches.Where(m => !used.Contains(m)).Select(m => m.Name + " (" + (m.ErrorM / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km)").ToList();
            if (skipped.Count > 0) report.Append(" Left out of the placement (too far from the others, probably moved in DCS): " + string.Join(", ", skipped) + ".");
            if (notFound.Count > 0) report.Append(" Not used (no ICAO code or not found online): " + string.Join(", ", notFound.Take(6)) + (notFound.Count > 6 ? "..." : "") + ".");

            var lines = new List<string>
            {
                "v=" + CalibVersion,
                "cm=" + proj.Cm.ToString("R", CultureInfo.InvariantCulture),
                "fe=" + proj.Fe.ToString("R", CultureInfo.InvariantCulture),
                "fn=" + proj.Fn.ToString("R", CultureInfo.InvariantCulture),
                "z=" + z, "x0=" + x0, "x1=" + x1, "y0=" + y0, "y1=" + y1,
                "report=" + report.ToString().Replace("\r", " ").Replace("\n", " ")
            };
            File.WriteAllLines(CalibPath(campaign), lines, Encoding.UTF8);

            Map_Background bg = LoadTiles(proj, z, x0, x1, y0, y1);
            bg.Report = report.ToString();
            return bg;
        }

        internal static string TileUrl(Map_Style st, int z, int x, int y)
        {
            return st.Url.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
        }

        internal static void Download(string url, string dest)
        {
            string tmp = dest + ".tmp";
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                wc.DownloadFile(url, tmp);
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }

        // ------------------------------------------------------------------
        //  Liste des aéroports (OurAirports)
        // ------------------------------------------------------------------

        private static Dictionary<string, double[]> ReadAirports(string csvPath, HashSet<string> wanted)
        {
            var result = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

            using (var sr = new StreamReader(csvPath, Encoding.UTF8))
            {
                string header = sr.ReadLine();
                if (header == null) return result;

                List<string> cols = ParseCsvLine(header);
                int iIdent = cols.IndexOf("ident");
                int iIcao = cols.IndexOf("icao_code");
                int iLat = cols.IndexOf("latitude_deg");
                int iLon = cols.IndexOf("longitude_deg");
                if (iIdent < 0 || iLat < 0 || iLon < 0)
                    throw new Exception("The airport list has an unexpected format.");

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    List<string> f = ParseCsvLine(line);
                    if (f.Count <= Math.Max(iLat, iLon)) continue;

                    string key = null;
                    if (wanted.Contains(f[iIdent])) key = f[iIdent];
                    else if (iIcao >= 0 && iIcao < f.Count && f[iIcao] != "" && wanted.Contains(f[iIcao])) key = f[iIcao];
                    if (key == null) continue;

                    double lat, lon;
                    if (double.TryParse(f[iLat], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
                        && double.TryParse(f[iLon], NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
                        result[key] = new[] { lat, lon };
                }
            }
            return result;
        }

        // Découpe une ligne CSV (champs entre guillemets possibles)
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }

        // ------------------------------------------------------------------
        //  Calage : projection Transverse Mercator (WGS84)
        // ------------------------------------------------------------------

        // Easting / Northing (sans décalage) d'un point lat/lon pour un méridien central donné
        private static void Tm(double latDeg, double lonDeg, double cmDeg, out double easting, out double northing)
        {
            const double a = 6378137.0;
            const double f = 1 / 298.257223563;
            double e2 = f * (2 - f);
            double ep2 = e2 / (1 - e2);

            double phi = latDeg * Math.PI / 180;
            double lam = (lonDeg - cmDeg) * Math.PI / 180;

            double sin = Math.Sin(phi), cos = Math.Cos(phi), tan = Math.Tan(phi);
            double N = a / Math.Sqrt(1 - e2 * sin * sin);
            double T = tan * tan;
            double C = ep2 * cos * cos;
            double A = lam * cos;

            double e4 = e2 * e2, e6 = e4 * e2;
            double M = a * ((1 - e2 / 4 - 3 * e4 / 64 - 5 * e6 / 256) * phi
                          - (3 * e2 / 8 + 3 * e4 / 32 + 45 * e6 / 1024) * Math.Sin(2 * phi)
                          + (15 * e4 / 256 + 45 * e6 / 1024) * Math.Sin(4 * phi)
                          - (35 * e6 / 3072) * Math.Sin(6 * phi));

            easting = K0 * N * (A + (1 - T + C) * Math.Pow(A, 3) / 6
                              + (5 - 18 * T + T * T + 72 * C - 58 * ep2) * Math.Pow(A, 5) / 120);
            northing = K0 * (M + N * tan * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * Math.Pow(A, 4) / 24
                              + (61 - 58 * T + T * T + 600 * C - 330 * ep2) * Math.Pow(A, 6) / 720));
        }

        public static void LatLonToDcs(Map_Projection p, double lat, double lon, out double x, out double y)
        {
            double e, n;
            Tm(lat, lon, p.Cm, out e, out n);
            y = e + p.Fe;
            x = n + p.Fn;
        }

        // Coût d'un méridien central donné : somme des écarts carrés, après avoir calé Fe / Fn sur la moyenne
        private static double Cost(List<Map_Match> list, double cm, out double fe, out double fn)
        {
            int n = list.Count;
            var e = new double[n];
            var nn = new double[n];
            double sumY = 0, sumX = 0;

            for (int i = 0; i < n; i++)
            {
                Tm(list[i].Lat, list[i].Lon, cm, out e[i], out nn[i]);
                sumY += list[i].Y - e[i];
                sumX += list[i].X - nn[i];
            }
            fe = sumY / n;
            fn = sumX / n;

            double cost = 0;
            for (int i = 0; i < n; i++)
            {
                double dy = list[i].Y - e[i] - fe;
                double dx = list[i].X - nn[i] - fn;
                cost += dx * dx + dy * dy;
            }
            return cost;
        }

        private static double BestCm(List<Map_Match> list, double from, double to, double step)
        {
            double best = from, bestCost = double.MaxValue;
            for (double cm = from; cm <= to; cm += step)
            {
                double fe, fn;
                double c = Cost(list, cm, out fe, out fn);
                if (c < bestCost) { bestCost = c; best = cm; }
            }
            return best;
        }

        private static Map_Projection Fit(List<Map_Match> list)
        {
            double lonMean = list.Average(m => m.Lon);
            double cm = BestCm(list, lonMean - 9, lonMean + 9, 0.5);
            cm = BestCm(list, cm - 0.5, cm + 0.5, 0.05);
            cm = BestCm(list, cm - 0.05, cm + 0.05, 0.005);

            double fe, fn;
            Cost(list, cm, out fe, out fn);
            return new Map_Projection { Cm = cm, Fe = fe, Fn = fn };
        }

        // Projections des cartes DCS (Transverse Mercator, k0 = 0.9996) : méridien central, décalages est / nord.
        // DCS y = Easting + Fe ; DCS x = Northing + Fn
        private static readonly Dictionary<string, double[]> Theatres = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Caucasus",       new double[] { 33,   -99516.9999999732,   -4998114.999999984 } },
            { "Nevada",         new double[] { -117, -193996.80999964548, -4410028.063999966 } },
            { "PersianGulf",    new double[] { 57,   75755.99999999645,   -2894933.0000000377 } },
            { "Syria",          new double[] { 39,   282801.00000000003,  -3879865.9999999935 } },
            { "Normandy",       new double[] { -3,   -195526.00000000303, -5484812.99999999 } },
            { "TheChannel",     new double[] { 3,    99376.00000003197,   -5636889.00000003 } },
            { "MarianaIslands", new double[] { 147,  238417.99999999846,  -1491840.9999999985 } },
            { "SouthAtlantic",  new double[] { -57,  147639.99999999158,  5815417.000000032 } },
            { "SinaiMap",       new double[] { 33,   169221.0000000007,   -3325312.0000000224 } },
            { "Kola",           new double[] { 21,   -62702.00000000019,  -7543624.999999983 } },
            { "Afghanistan",    new double[] { 63,   -300149.99999864185, -3759657.000000045 } }
        };

        // Nom du théâtre (fichier "theatre" d'un .miz de la campagne : Init\base_mission.miz, sinon <campagne>_first.miz)
        private static string DetectTheatre(string campaign)
        {
            string root = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns");
            string[] candidates =
            {
                Path.Combine(root, campaign, "Init", "base_mission.miz"),
                Path.Combine(root, campaign + "_first.miz"),
                Path.Combine(root, campaign + "_ongoing.miz")
            };

            foreach (string miz in candidates)
            {
                try
                {
                    if (!File.Exists(miz)) continue;
                    using (ZipArchive zip = ZipFile.OpenRead(miz))
                    {
                        ZipArchiveEntry e = zip.GetEntry("theatre");
                        if (e == null) continue;
                        using (var sr = new StreamReader(e.Open(), Encoding.UTF8))
                        {
                            string name = sr.ReadToEnd().Trim();
                            if (name != "") return name;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("Map_Web | théâtre illisible dans " + miz + " : " + ex.Message);
                }
            }
            return "";
        }

        internal static Map_Projection KnownProjection(string campaign, out string theatre)
        {
            theatre = DetectTheatre(campaign);
            double[] v;
            if (theatre != "" && Theatres.TryGetValue(theatre, out v))
                return new Map_Projection { Cm = v[0], Fe = v[1], Fn = v[2] };
            return null;
        }

        // Inverse de LatLonToDcs (Newton sur la projection directe) : position DCS -> latitude / longitude
        internal static void DcsToLatLon(Map_Projection p, double x, double y, out double lat, out double lon)
        {
            lat = (x - p.Fn) / 111000.0;
            lon = p.Cm + (y - p.Fe) / (111320.0 * Math.Max(0.1, Math.Cos(lat * Math.PI / 180.0)));

            const double h = 1e-4;
            for (int i = 0; i < 12; i++)
            {
                double fx, fy, ax, ay, bx, by;
                LatLonToDcs(p, lat, lon, out fx, out fy);
                double ex = x - fx, ey = y - fy;
                if (Math.Abs(ex) < 1 && Math.Abs(ey) < 1) break;

                LatLonToDcs(p, lat + h, lon, out ax, out ay);
                LatLonToDcs(p, lat, lon + h, out bx, out by);
                double a = (ax - fx) / h, b = (bx - fx) / h, c = (ay - fy) / h, d = (by - fy) / h;
                double det = a * d - b * c;
                if (Math.Abs(det) < 1e-9) break;

                lat += (ex * d - b * ey) / det;
                lon += (a * ey - c * ex) / det;
            }
        }

        private static double ErrorOf(Map_Projection p, Map_Match m)
        {
            double x, y;
            LatLonToDcs(p, m.Lat, m.Lon, out x, out y);
            double dx = m.X - x, dy = m.Y - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Calage qui écarte les bases trop éloignées du reste (mauvais code ICAO, point DCS décalé...).
        // "used" reçoit les bases gardées ; ErrorM est rempli pour toutes.
        private static Map_Projection FitRobust(List<Map_Match> all, List<Map_Match> used)
        {
            used.Clear();
            used.AddRange(all);

            for (int pass = 0; pass < 3; pass++)
            {
                Map_Projection p = Fit(used);
                foreach (Map_Match m in all) m.ErrorM = ErrorOf(p, m);

                List<double> errs = used.Select(m => m.ErrorM).OrderBy(e => e).ToList();
                double median = errs[errs.Count / 2];
                double limit = Math.Max(3000, 3 * median);

                List<Map_Match> next = all.Where(m => m.ErrorM <= limit).ToList();
                if (next.Count < 3 || next.Count == used.Count) break;

                used.Clear();
                used.AddRange(next);
            }

            Map_Projection final = Fit(used);
            foreach (Map_Match m in all) m.ErrorM = ErrorOf(final, m);
            return final;
        }

        // ------------------------------------------------------------------
        //  Tuiles
        // ------------------------------------------------------------------

        private static int TileX(double lon, int z)
        {
            return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << z));
        }

        private static int TileY(double lat, int z)
        {
            double r = Math.Max(-85, Math.Min(85, lat)) * Math.PI / 180;
            return (int)Math.Floor((1 - Math.Log(Math.Tan(r) + 1 / Math.Cos(r)) / Math.PI) / 2 * (1 << z));
        }

        internal static double TileLon(double tx, int z)
        {
            return tx / (double)(1 << z) * 360.0 - 180.0;
        }

        internal static double TileLat(double ty, int z)
        {
            return Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * ty / (1 << z)))) * 180.0 / Math.PI;
        }

        private static string TileFile(int z, int x, int y)
        {
            return TileFile(CurrentStyle, z, x, y);
        }

        internal static string TileFile(Map_Style st, int z, int x, int y)
        {
            string root = st.Id == "osm" ? Path.Combine(CacheRoot, "Tiles") : Path.Combine(CacheRoot, "Tiles_" + st.Id);
            return Path.Combine(root, z.ToString(), x.ToString(), y + ".png");
        }

        // Charge les tuiles déjà présentes sur le disque et calcule leurs coins en coordonnées DCS
        // Image lue en mémoire : le fichier n'est pas verrouillé
        internal static Bitmap LoadBitmap(string file)
        {
            using (var fs = File.OpenRead(file))
            using (var tmp = Image.FromStream(fs))
                return new Bitmap(tmp);
        }

        // Une tuile OSM est carrée en Web Mercator, mais en coordonnées DCS c'est un trapèze
        // (convergence des méridiens, surtout au nord). 3 coins ne suffisent donc pas : on la découpe
        // en cut x cut morceaux, chacun avec ses vrais coins. Les voisins partagent les mêmes coins,
        // donc plus de bandes noires entre les tuiles.
        internal static List<Map_Tile> MakePieces(Map_Projection p, int z, int tx, int ty, Bitmap img, int cut)
        {
            var list = new List<Map_Tile>();
            float w = img.Width / (float)cut, h = img.Height / (float)cut;
            for (int i = 0; i < cut; i++)
            {
                for (int j = 0; j < cut; j++)
                {
                    var t = new Map_Tile { Image = img, Src = new RectangleF(i * w, j * h, w, h) };
                    double a0 = tx + i / (double)cut, a1 = tx + (i + 1) / (double)cut;
                    double b0 = ty + j / (double)cut, b1 = ty + (j + 1) / (double)cut;
                    LatLonToDcs(p, TileLat(b0, z), TileLon(a0, z), out t.TlX, out t.TlY);
                    LatLonToDcs(p, TileLat(b0, z), TileLon(a1, z), out t.TrX, out t.TrY);
                    LatLonToDcs(p, TileLat(b1, z), TileLon(a0, z), out t.BlX, out t.BlY);
                    list.Add(t);
                }
            }
            return list;
        }

        // Colle toutes les tuiles du fond dans une seule image (thread de fond : relit les fichiers lui-même,
        // pour ne jamais toucher aux images que l'interface est en train de dessiner).
        internal static Bitmap BuildMosaic(Map_Background bg, out double minX, out double maxX, out double minY, out double maxY, out double pix)
        {
            var tiles = new List<int[]>();
            minX = minY = double.MaxValue;
            maxX = maxY = double.MinValue;
            double tileMeters = double.MaxValue;

            for (int tx = bg.X0; tx <= bg.X1; tx++)
            {
                for (int ty = bg.Y0; ty <= bg.Y1; ty++)
                {
                    if (!File.Exists(TileFile(bg.Style, bg.Z, tx, ty))) continue;
                    tiles.Add(new int[] { tx, ty });

                    double[] lat = { TileLat(ty, bg.Z), TileLat(ty + 1, bg.Z) };
                    double[] lon = { TileLon(tx, bg.Z), TileLon(tx + 1, bg.Z) };
                    double tlX = 0, tlY = 0, trX = 0, trY = 0;
                    for (int a = 0; a < 2; a++)
                    {
                        for (int b = 0; b < 2; b++)
                        {
                            double x, y;
                            LatLonToDcs(bg.Proj, lat[a], lon[b], out x, out y);
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                            if (a == 0 && b == 0) { tlX = x; tlY = y; }
                            if (a == 0 && b == 1) { trX = x; trY = y; }
                        }
                    }
                    // taille d'un pixel de tuile, en mètres (côté haut de la tuile / 256)
                    double side = Math.Sqrt((tlX - trX) * (tlX - trX) + (tlY - trY) * (tlY - trY)) / 256.0;
                    if (side > 0 && side < tileMeters) tileMeters = side;
                }
            }
            if (tiles.Count == 0) throw new Exception("no tile");

            // Finesse : celle des tuiles, mais pas plus de 3600 pixels de côté (environ 50 Mo)
            double extent = Math.Max(maxX - minX, maxY - minY);
            pix = Math.Max(tileMeters, extent / 3600.0);
            int w = (int)Math.Ceiling((maxY - minY) / pix) + 1;
            int h = (int)Math.Ceiling((maxX - minX) / pix) + 1;

            var mosaic = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(mosaic))
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                double mnX = minX, mxX = maxX, mnY = minY, p = pix;
                Func<double, double, PointF> P = (x, y) => new PointF((float)((y - mnY) / p), (float)((mxX - x) / p));

                foreach (int[] t in tiles)
                {
                    try
                    {
                        using (Bitmap img = LoadBitmap(TileFile(bg.Style, bg.Z, t[0], t[1])))
                        {
                            foreach (Map_Tile piece in MakePieces(bg.Proj, bg.Z, t[0], t[1], img, Cut))
                                g.DrawImage(img, new PointF[] { P(piece.TlX, piece.TlY), P(piece.TrX, piece.TrY), P(piece.BlX, piece.BlY) },
                                    piece.Src, GraphicsUnit.Pixel, ia);
                        }
                    }
                    catch (Exception ex)
                    {
                        FormUtils.LogRegister("Map_Web | mosaïque, tuile illisible : " + ex.Message);
                    }
                }
            }
            return mosaic;
        }

        private static Map_Background LoadTiles(Map_Projection p, int z, int x0, int x1, int y0, int y1)
        {
            var bg = new Map_Background { Attribution = CurrentStyle.Attribution, Proj = p, Style = CurrentStyle, Z = z, X0 = x0, X1 = x1, Y0 = y0, Y1 = y1 };

            for (int tx = x0; tx <= x1; tx++)
            {
                for (int ty = y0; ty <= y1; ty++)
                {
                    string file = TileFile(z, tx, ty);
                    if (!File.Exists(file)) continue;

                    try
                    {
                        Bitmap img = LoadBitmap(file);
                        bg.Tiles.AddRange(MakePieces(p, z, tx, ty, img, Cut));
                    }
                    catch (Exception ex)
                    {
                        FormUtils.LogRegister("Map_Web | tile illisible " + file + " : " + ex.Message);
                        try { File.Delete(file); } catch { }    // tuile abîmée : elle sera retéléchargée
                    }
                }
            }

            bg.Detail = new Map_Detail(p, z, x0, x1, y0, y1, CurrentStyle);
            return bg;
        }
    }


    // Tuiles plus détaillées, chargées seulement pour la partie de la carte que l'utilisateur regarde.
    // Quand on zoome, la vue demande le bon niveau de zoom : les tuiles déjà en cache s'affichent tout de suite,
    // les autres sont téléchargées une par une en arrière-plan (jamais plus de MaxWanted tuiles à la fois).
    // Le fond de base reste dessiné dessous : si une tuile manque ou n'est pas encore arrivée, on voit la version floue.
    internal class Map_Detail : IDisposable
    {
        private const int MaxWanted = 150;      // tuiles visibles demandées en même temps
        private const int MaxCache = 260;       // tuiles gardées en mémoire (environ 60 Mo au maximum)
        private const int ExtraZoom = 3;        // niveaux de zoom au-dessus du fond de base
        private const int AbsMaxZoom = 13;      // on ne descend jamais plus fin (pas de téléchargement massif)

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

        // Quelles tuiles de ce zoom touchent l'écran ? (liste: clé + distance au centre de l'écran)
        private void CollectVisible(int zd, Func<double, double, PointF> toScreen, Rectangle screen, List<KeyValuePair<long, double>> result)
        {
            int f = 1 << (zd - _z);
            var area = new RectangleF(screen.X - 20, screen.Y - 20, screen.Width + 40, screen.Height + 40);
            float cx = screen.X + screen.Width / 2f, cy = screen.Y + screen.Height / 2f;

            for (int bx = _x0; bx <= _x1; bx++)
            {
                for (int by = _y0; by <= _y1; by++)
                {
                    // 1) test rapide sur la grande tuile du fond de base
                    RectangleF baseBox;
                    if (!Touches(bx, by, 1, _z, toScreen, area, out baseBox)) continue;

                    // 2) puis ses petites tuiles au zoom demandé
                    for (int i = 0; i < f; i++)
                    {
                        for (int j = 0; j < f; j++)
                        {
                            int tx = bx * f + i, ty = by * f + j;
                            RectangleF box;
                            if (!Touches(tx, ty, 1, zd, toScreen, area, out box)) continue;
                            double dx = box.X + box.Width / 2 - cx, dy = box.Y + box.Height / 2 - cy;
                            result.Add(new KeyValuePair<long, double>(Key(zd, tx, ty), dx * dx + dy * dy));
                        }
                    }
                }
            }
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
                    foreach (long k in _cache.Where(c => !_wanted.Contains(c.Key)).OrderBy(c => c.Value.Stamp).Select(c => c.Key).ToList())
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

        // (dans le verrou) Les morceaux à dessiner : tuiles voulues et déjà chargées
        private void RebuildDrawList()
        {
            var list = new List<Map_Tile>();
            foreach (long k in _wanted)
            {
                Entry e;
                if (_cache.TryGetValue(k, out e) && e.Zoom == _wantedZoom)
                    list.AddRange(e.Pieces);
            }
            _drawList = list;
        }

        // Thread de téléchargement : une tuile à la fois, comme pour le fond de base
        private void Work()
        {
            try
            {
                while (true)
                {
                    long key;
                    lock (_lock)
                    {
                        if (_disposed || _missing.Count == 0) return;
                        key = _missing[0];
                        _missing.RemoveAt(0);
                    }

                    int zoom = (int)(key >> 50);
                    int tx = (int)((key >> 25) & 0x1FFFFFF);
                    int ty = (int)(key & 0x1FFFFFF);

                    try
                    {
                        string file = Map_Web.TileFile(_style, zoom, tx, ty);
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
                            if (_wanted.Contains(key)) RebuildDrawList();
                        }

                        Action changed = Changed;
                        if (changed != null) changed();
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
            }
        }
    }

    // Un tracé du fichier mission (frontières, zones...) : coordonnées DCS absolues
    internal class Map_Drawing
    {
        public string Name = "";
        public string Group = "mission";    // mission / zones / spawn : chaque groupe a son bouton
        public string Label = "";           // texte écrit au milieu de la forme (nom de zone)
        public string Kind = "";            // Line / Polygon / Circle / Text
        public List<double[]> Points = new List<double[]>();   // { x (nord), y (est) }
        public bool Closed;
        public bool Dashed;
        public float Thickness = 2;
        public Color Line = Color.White;
        public Color Fill = Color.Transparent;
        public double Radius;               // cercle, en mètres
        public string Text = "";
    }

    // Mission en cours : plans de vol des joueurs + menaces sol-air (lus dans last_Mission.lua ou dans le .miz)
    internal class Map_MissionData
    {
        public string Source = "";      // fichier lu (vide = aucune mission trouvée)
        public string Error = "";
        public int PlayerFlights;
        public int SamSites;
        public List<Map_Drawing> Plans = new List<Map_Drawing>();
        public List<Map_Drawing> Threats = new List<Map_Drawing>();
    }

    // Lecture des dessins de l'éditeur de mission (drawings/layers) dans Init\base_mission.miz.
    // Un .miz est un zip : le fichier "mission" est une table Lua, lue ici par un petit lecteur maison.
    internal static class Map_Drawings
    {
        public static List<Map_Drawing> Load(string campaign)
        {
            var list = new List<Map_Drawing>();
            try
            {
                string miz = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", campaign, "Init", "base_mission.miz");
                if (!File.Exists(miz)) return list;

                string text;
                using (ZipArchive zip = ZipFile.OpenRead(miz))
                {
                    ZipArchiveEntry entry = zip.GetEntry("mission");
                    if (entry == null) return list;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                        text = sr.ReadToEnd();
                }

                int i = text.IndexOf("[\"drawings\"]", StringComparison.Ordinal);
                if (i < 0) return list;
                i = text.IndexOf('{', i);
                if (i < 0) return list;

                var reader = new LuaReader(text, i);
                var drawings = reader.ReadValue() as Dictionary<string, object>;
                if (drawings == null) return list;

                var layers = Sub(drawings, "layers");
                if (layers == null) return list;

                foreach (object layerObj in layers.Values)
                {
                    var layer = layerObj as Dictionary<string, object>;
                    if (layer == null || !IsVisible(layer)) continue;

                    var objects = Sub(layer, "objects");
                    if (objects == null) continue;

                    foreach (object o in objects.Values)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null || !IsVisible(d)) continue;
                        Map_Drawing md = Convert(d);
                        if (md != null) list.Add(md);
                    }
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | " + ex.Message);
            }
            return list;
        }

        // Zones du wargame : Init\Wargame\DCE_Generated\wargame_zones_init.lua (déjà lu par l'éditeur wargame).
        // Couleur = camp qui tient la zone. Les plans de débarquement (LANDING...) sont en pointillés.
        //   active : true = on applique par-dessus l'état de Active\wargame_zones.lua (camp actuel de chaque zone)
        public static List<WargameZoneData> LoadWargameZoneData(string campaign, bool active)
        {
            try
            {
                string file = WargameZoneRepository.GetInitLuaPath(campaign);
                if (!File.Exists(file)) return new List<WargameZoneData>();

                List<WargameZoneData> zones = new WargameZoneInitLoader().Load(file);

                if (active)
                {
                    string activePath = WargameZoneRepository.GetActiveWargameZonesPath(campaign);
                    if (File.Exists(activePath))
                        new WargameZoneActiveLoader().ApplyActiveState(activePath, zones);
                }
                return zones;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | wargame zone data : " + ex.Message);
                return new List<WargameZoneData>();
            }
        }

        public static List<Map_Drawing> LoadWargameZones(string campaign, bool active)
        {
            var list = new List<Map_Drawing>();
            try
            {
                List<WargameZoneData> zones = LoadWargameZoneData(campaign, active);

                foreach (WargameZoneData z in zones)
                {
                    Color c = z.Control == WargameSide.Blue ? Color.FromArgb(70, 140, 255)
                            : z.Control == WargameSide.Red ? Color.FromArgb(235, 70, 70)
                            : Color.FromArgb(240, 170, 40);

                    var md = new Map_Drawing { Name = z.Id, Label = z.Id, Group = "zones", Line = c, Fill = Color.FromArgb(45, c), Thickness = 2f };

                    if (z.Id.StartsWith("LANDING", StringComparison.OrdinalIgnoreCase))
                    {
                        md.Line = Color.Cyan;
                        md.Fill = Color.FromArgb(40, Color.Cyan);
                        md.Dashed = true;
                    }

                    if (z.Shape == WargameZoneShape.Circle)
                    {
                        md.Kind = "Circle";
                        md.Radius = z.Radius;
                        md.Points.Add(new double[] { z.Center.X, z.Center.Y });
                    }
                    else
                    {
                        md.Kind = "Polygon";
                        md.Closed = true;
                        foreach (PointF pt in z.DcsPoints)
                            md.Points.Add(new double[] { pt.X, pt.Y });
                        if (md.Points.Count < 3) continue;
                    }
                    list.Add(md);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | wargame zones : " + ex.Message);
            }
            return list;
        }

        // Zones de spawn du wargame : Init\Wargame\wargame_spawn.miz (routes = lignes, surfaces tagguées town / forest / mountain...)
        public static List<Map_Drawing> LoadWargameSpawn(string campaign)
        {
            var list = new List<Map_Drawing>();
            try
            {
                string file = WargameZoneRepository.GetSpawnMizPath(campaign);
                if (!File.Exists(file)) return list;

                WargameSpawnAreas areas = new Parser_WargameSpawnAreas().LoadFromMiz(file);

                foreach (WargameSpawnLine l in areas.Lines)
                {
                    var md = new Map_Drawing { Name = l.Name, Kind = "Line", Group = "spawn", Line = Color.FromArgb(230, 225, 200, 60), Thickness = 1.5f };
                    foreach (PointF pt in l.Points) md.Points.Add(new double[] { pt.X, pt.Y });
                    if (md.Points.Count >= 2) list.Add(md);
                }

                foreach (WargameSpawnPolygon p in areas.Polygons)
                {
                    string tag = (p.Tag ?? "").ToLowerInvariant();
                    Color c = tag.Contains("forest") ? Color.FromArgb(80, 190, 90)
                            : tag.Contains("mountain") ? Color.FromArgb(190, 130, 80)
                            : Color.FromArgb(190, 190, 190);

                    var md = new Map_Drawing { Name = p.Name, Label = p.Tag ?? "", Kind = "Polygon", Group = "spawn", Closed = true,
                                               Line = c, Fill = Color.FromArgb(50, c), Thickness = 1.5f };
                    foreach (PointF pt in p.Points) md.Points.Add(new double[] { pt.X, pt.Y });
                    if (md.Points.Count >= 3) list.Add(md);
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | wargame spawn : " + ex.Message);
            }
            return list;
        }

        private static Map_Drawing Convert(Dictionary<string, object> d)
        {
            string type = Str(d, "primitiveType");
            double mx = Num(d, "mapX"), my = Num(d, "mapY");

            var md = new Map_Drawing { Name = Str(d, "name"), Kind = type };
            md.Closed = Str(d, "closed") == "true";
            md.Dashed = Str(d, "style") != "solid" && Str(d, "style") != "";
            md.Thickness = (float)Math.Max(1, Math.Min(5, Num(d, "thickness") / 4));
            md.Line = Col(Str(d, "colorString"), Color.White);
            md.Fill = Col(Str(d, "fillColorString"), Color.Transparent);

            if (type == "Line" || type == "Polygon")
            {
                var pts = Sub(d, "points");
                string mode = Str(d, "polygonMode");

                if (type == "Polygon" && mode == "circle")
                {
                    md.Kind = "Circle";
                    md.Radius = Num(d, "radius");
                    md.Points.Add(new double[] { mx, my });
                    return md;
                }
                if (pts == null) return null;

                // les clés sont des numéros (parfois dans le désordre) : on trie
                var keys = new List<int>();
                foreach (string k in pts.Keys) { int n; if (int.TryParse(k, out n)) keys.Add(n); }
                keys.Sort();
                foreach (int k in keys)
                {
                    var p = pts[k.ToString()] as Dictionary<string, object>;
                    if (p == null) continue;
                    md.Points.Add(new double[] { mx + Num(p, "x"), my + Num(p, "y") });
                }
                if (type == "Polygon") md.Closed = true;
                return md.Points.Count >= 2 ? md : null;
            }

            if (type == "TextBox")
            {
                md.Text = Str(d, "text");
                md.Points.Add(new double[] { mx, my });
                return md.Text != "" ? md : null;
            }
            return null;        // icônes, flèches... : pas gérées
        }

        // ------------------------------------------------------------------
        //  Mission en cours : plans de vol des joueurs (skill Client / Player) et rayons de menace des SAM
        // ------------------------------------------------------------------

        // Portée maximale (m) par lanceur, d'après les données connues de DCS (valeurs arrondies, indicatives).
        // Le motif est cherché dans le type de l'unité ; le premier qui correspond gagne (les plus précis d'abord).
        private static readonly object[][] SamTable =
        {
            new object[] { "5P85",            "SA-10 / S-300PS",   75000.0 },
            new object[] { "9A82",            "SA-12 / S-300V",    75000.0 },
            new object[] { "9A83",            "SA-12 / S-300V",    75000.0 },
            new object[] { "Patriot ln",      "Patriot",           80000.0 },
            new object[] { "Hawk ln",         "Hawk",              40000.0 },
            new object[] { "S_75M",           "SA-2",              45000.0 },
            new object[] { "5p73",            "SA-3",              25000.0 },
            new object[] { "9A310",           "SA-11 Buk",         32000.0 },
            new object[] { "2P25",            "SA-6 Kub",          24000.0 },
            new object[] { "NASAMS_LN",       "NASAMS",            30000.0 },
            new object[] { "IRIST",           "IRIS-T SLM",        40000.0 },
            new object[] { "Tor 9A331",       "SA-15 Tor",         12000.0 },
            new object[] { "HQ-7",            "HQ-7",              12000.0 },
            new object[] { "Osa 9A33",        "SA-8 Osa",          10000.0 },
            new object[] { "Roland ADS",      "Roland",             8000.0 },
            new object[] { "2S6",             "SA-19 Tunguska",     8000.0 },
            new object[] { "rapier",          "Rapier",             7000.0 },
            new object[] { "M1097",           "Avenger",            5500.0 },
            new object[] { "Strela-10M3",     "SA-13 Strela-10",    5000.0 },
            new object[] { "Igla",            "SA-18 Igla",         5000.0 },
            new object[] { "M6 Linebacker",   "Linebacker",         4800.0 },
            new object[] { "Stinger",         "Stinger",            4800.0 },
            new object[] { "Strela-1 9P31",   "SA-9 Strela-1",      4200.0 },
        };

        private static bool SamRange(string type, out string name, out double range)
        {
            name = "";
            range = 0;
            if (string.IsNullOrEmpty(type)) return false;

            foreach (object[] row in SamTable)
            {
                if (type.IndexOf((string)row[0], StringComparison.OrdinalIgnoreCase) < 0) continue;
                name = (string)row[1];
                range = (double)row[2];
                return true;
            }
            return false;
        }

        // Le fichier de la mission en cours : Active\last_Mission.lua s'il existe, sinon le .miz le plus récent
        // (_ongoing ou FirstMission) dans le dossier de la campagne ou dans Active.
        public static string FindMissionSource(string campaign)
        {
            string root = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", campaign);

            string last = Path.Combine(root, "Active", "last_Mission.lua");
            if (File.Exists(last)) return last;

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string dir in new[] { root, Path.Combine(root, "Active") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "*.miz"))
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    if (!n.Contains("ongoing") && !n.Contains("firstmission")) continue;

                    DateTime t = File.GetLastWriteTime(f);
                    if (t > bestTime) { best = f; bestTime = t; }
                }
            }
            return best;
        }

        // Table "last_Mission" (fichier .lua) ou "mission" (dans le .miz)
        private static Dictionary<string, object> ReadMissionRoot(string file)
        {
            string text;
            if (file.EndsWith(".miz", StringComparison.OrdinalIgnoreCase))
            {
                using (ZipArchive zip = ZipFile.OpenRead(file))
                {
                    ZipArchiveEntry entry = zip.GetEntry("mission");
                    if (entry == null) return null;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                        text = sr.ReadToEnd();
                }
            }
            else
            {
                text = File.ReadAllText(file);
            }

            foreach (string name in new[] { "last_Mission", "mission" })
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^\s*" + name + @"\s*=\s*\{");
                if (!m.Success) continue;

                var reader = new LuaReader(text, m.Index + m.Length - 1);
                return reader.ReadValue() as Dictionary<string, object>;
            }
            return null;
        }

        public static Map_MissionData LoadMission(string campaign)
        {
            var data = new Map_MissionData();
            try
            {
                string file = FindMissionSource(campaign);
                if (file == null)
                {
                    data.Error = "No mission found yet (Active\\last_Mission.lua, _ongoing.miz or FirstMission.miz).";
                    return data;
                }

                data.Source = file;
                Dictionary<string, object> root = ReadMissionRoot(file);
                Dictionary<string, object> coalitions = root == null ? null : Sub(root, "coalition");
                if (coalitions == null)
                {
                    data.Error = "The mission file has no coalition table: " + Path.GetFileName(file);
                    return data;
                }

                foreach (string side in new[] { "blue", "red" })
                {
                    Dictionary<string, object> countries = Sub(Sub(coalitions, side) ?? new Dictionary<string, object>(), "country");
                    if (countries == null) continue;

                    Color planColor = side == "blue" ? Color.FromArgb(0, 210, 255) : Color.FromArgb(255, 150, 0);
                    Color samColor = side == "blue" ? Color.FromArgb(70, 140, 255) : Color.FromArgb(235, 70, 70);

                    foreach (object co in countries.Values)
                    {
                        var country = co as Dictionary<string, object>;
                        if (country == null) continue;

                        foreach (string cat in new[] { "plane", "helicopter" })
                            AddPlayerPlans(data, Sub(Sub(country, cat) ?? new Dictionary<string, object>(), "group"), planColor);

                        AddSamThreats(data, Sub(Sub(country, "vehicle") ?? new Dictionary<string, object>(), "group"), samColor);
                    }
                }
            }
            catch (Exception ex)
            {
                data.Error = ex.Message;
                FormUtils.LogRegister("Map_Drawings | mission : " + ex);
            }
            return data;
        }

        private static void AddPlayerPlans(Map_MissionData data, Dictionary<string, object> groups, Color color)
        {
            if (groups == null) return;

            foreach (object go in groups.Values)
            {
                var group = go as Dictionary<string, object>;
                Dictionary<string, object> units = group == null ? null : Sub(group, "units");
                if (units == null) continue;

                bool player = false;
                string type = "";
                foreach (object uo in units.Values)
                {
                    var unit = uo as Dictionary<string, object>;
                    if (unit == null) continue;
                    string skill = Str(unit, "skill");
                    if (skill.Equals("Client", StringComparison.OrdinalIgnoreCase) || skill.Equals("Player", StringComparison.OrdinalIgnoreCase))
                    {
                        player = true;
                        type = Str(unit, "type");
                    }
                }
                if (!player) continue;

                Dictionary<string, object> points = Sub(Sub(group, "route") ?? new Dictionary<string, object>(), "points");
                if (points == null) continue;

                var wps = new List<KeyValuePair<int, Dictionary<string, object>>>();
                foreach (KeyValuePair<string, object> kv in points)
                {
                    int n;
                    var wp = kv.Value as Dictionary<string, object>;
                    if (wp != null && int.TryParse(kv.Key, out n)) wps.Add(new KeyValuePair<int, Dictionary<string, object>>(n, wp));
                }
                wps.Sort((p, q) => p.Key.CompareTo(q.Key));
                if (wps.Count == 0) continue;

                string name = Str(group, "name");
                var line = new Map_Drawing { Name = name, Label = name + (type != "" ? " (" + type + ")" : ""), Group = "plans", Kind = "Line", Line = color, Thickness = 2.5f };

                int index = 0;
                foreach (KeyValuePair<int, Dictionary<string, object>> p in wps)
                {
                    double x = Num(p.Value, "x"), y = Num(p.Value, "y");
                    line.Points.Add(new double[] { x, y });

                    string wpName = Str(p.Value, "name");
                    var txt = new Map_Drawing { Name = name, Group = "plans", Kind = "Text", Line = color,
                                                Text = "\u25CF" + index + (wpName != "" ? " " + wpName : "") };
                    txt.Points.Add(new double[] { x, y });
                    data.Plans.Add(txt);
                    index++;
                }

                data.Plans.Insert(0, line);     // les traits d'abord, les numéros par-dessus
                data.PlayerFlights++;
            }
        }

        private static void AddSamThreats(Map_MissionData data, Dictionary<string, object> groups, Color color)
        {
            if (groups == null) return;

            foreach (object go in groups.Values)
            {
                var group = go as Dictionary<string, object>;
                Dictionary<string, object> units = group == null ? null : Sub(group, "units");
                if (units == null) continue;
                if (Str(group, "hidden") == "true") continue;       // groupe caché : pas de menace affichée

                // Un site = un cercle, autour du lanceur qui porte le plus loin
                double best = 0, bx = 0, by = 0;
                string bestName = "";
                foreach (object uo in units.Values)
                {
                    var unit = uo as Dictionary<string, object>;
                    if (unit == null || Str(unit, "dead") == "true") continue;

                    string sam;
                    double range;
                    if (SamRange(Str(unit, "type"), out sam, out range) && range > best)
                    {
                        best = range; bestName = sam;
                        bx = Num(unit, "x"); by = Num(unit, "y");
                    }
                }
                if (best <= 0) continue;

                var circle = new Map_Drawing
                {
                    Name = Str(group, "name"),
                    Label = bestName + " " + (best / 1000).ToString("0") + " km",
                    Group = "threats",
                    Kind = "Circle",
                    Radius = best,
                    Line = Color.FromArgb(220, color),
                    Fill = Color.FromArgb(28, color),
                    Thickness = 1.5f
                };
                circle.Points.Add(new double[] { bx, by });
                data.Threats.Add(circle);
                data.SamSites++;
            }
        }

        private static bool IsVisible(Dictionary<string, object> d)
        {
            object v;
            return !d.TryGetValue("visible", out v) || !(v is bool) || (bool)v;
        }

        private static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return "";
            if (v is bool) return (bool)v ? "true" : "false";
            return System.Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v is double) return (double)v;
            return 0;
        }

        // "0xRRGGBBAA" -> Color
        private static Color Col(string s, Color fallback)
        {
            if (s == null || s.Length != 10 || !s.StartsWith("0x")) return fallback;
            try
            {
                int r = System.Convert.ToInt32(s.Substring(2, 2), 16);
                int g = System.Convert.ToInt32(s.Substring(4, 2), 16);
                int b = System.Convert.ToInt32(s.Substring(6, 2), 16);
                int a = System.Convert.ToInt32(s.Substring(8, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }
            catch { return fallback; }
        }

        // Petit lecteur de table Lua : tables, textes, nombres, true/false. Les clés deviennent des textes.
        private class LuaReader
        {
            private readonly string _s;
            private int _i;

            public LuaReader(string s, int start) { _s = s; _i = start; }

            private void Skip()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (char.IsWhiteSpace(c)) { _i++; continue; }
                    if (c == '-' && _i + 1 < _s.Length && _s[_i + 1] == '-')
                    {
                        while (_i < _s.Length && _s[_i] != '\n') _i++;
                        continue;
                    }
                    break;
                }
            }

            public object ReadValue()
            {
                Skip();
                char c = _s[_i];
                if (c == '{') return ReadTable();
                if (c == '"') return ReadString();

                int start = _i;
                while (_i < _s.Length && ",}]\r\n \t".IndexOf(_s[_i]) < 0) _i++;
                string w = _s.Substring(start, _i - start);
                if (w == "true") return true;
                if (w == "false") return false;
                double d;
                if (double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                return null;
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                _i++;       // guillemet ouvrant
                while (_i < _s.Length && _s[_i] != '"')
                {
                    if (_s[_i] == '\\' && _i + 1 < _s.Length) _i++;
                    sb.Append(_s[_i]);
                    _i++;
                }
                _i++;       // guillemet fermant
                return sb.ToString();
            }

            private Dictionary<string, object> ReadTable()
            {
                var t = new Dictionary<string, object>();
                _i++;       // {
                int auto = 1;
                while (true)
                {
                    Skip();
                    if (_i >= _s.Length) return t;
                    if (_s[_i] == '}') { _i++; return t; }
                    if (_s[_i] == ',') { _i++; continue; }

                    string key;
                    if (_s[_i] == '[')
                    {
                        _i++;
                        Skip();
                        object k = ReadValue();
                        key = k is double ? ((double)k).ToString(CultureInfo.InvariantCulture) : (k as string ?? "");
                        Skip();
                        _i++;       // ]
                        Skip();
                        _i++;       // =
                    }
                    else
                    {
                        // clé sans crochets (nom = valeur) ou valeur seule
                        int save = _i;
                        while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                        string name = _s.Substring(save, _i - save);
                        Skip();
                        if (name != "" && _i < _s.Length && _s[_i] == '=') { _i++; key = name; }
                        else { _i = save; key = (auto++).ToString(); }
                    }
                    t[key] = ReadValue();
                }
            }
        }
    }
}
