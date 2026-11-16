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
        public double Lat;      // position réelle de référence (OurAirports : point de l'aéroport, ou milieu / bout d'une piste)
        public double Lon;
        public double ArpLat, ArpLon;   // point de l'aéroport (OurAirports), pour revenir dessus si aucune piste ne colle
        public List<Map_Runway> DbRunways;      // pistes de db_airbases (position absolue, 0 / 0 = pas de position) : sert à mesurer la règle de position des pistes
        public string Ref = "";         // ce que représente le point de la base : "runway middle" / "runway end" / "airport point"
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
        public string AnchorRule = "";      // règle déduite de la mesure : middle / start / east / west ("" = pas de règle claire)
        public string AnchorInfo = "";      // mesure : où se trouve le point de db_airbases sur les vraies pistes (voir Map_Web.MeasureAnchor)
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
        private const string CalibVersion = "5";   // changer ce numéro force un nouveau téléchargement chez tout le monde
        private const double K0 = 0.9996;

        // Styles proposés. Chaque style a son dossier de cache (l'ancien cache OpenStreetMap est gardé tel quel).
        public static readonly List<Map_Style> Styles = new List<Map_Style>
        {
            new Map_Style { Id = "osm", Label = "Standard - OpenStreetMap (local names)", MaxZoom = 18,
                Url = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
                Attribution = "© OpenStreetMap contributors" },
            // Les tuiles Carto sont devenues payantes (filigrane "API KEY REQUIRED") : remplacées par Esri, accessible sans clé, noms en lettres latines
            new Map_Style { Id = "esri_street", Label = "Streets - Esri World Street Map (Latin letters)", MaxZoom = 17,
                Url = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}",
                Attribution = "Tiles © Esri, HERE, Garmin, OpenStreetMap contributors" },
            new Map_Style { Id = "esri_topo", Label = "Topographic - Esri World Topo Map (Latin letters)", MaxZoom = 17,
                Url = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}",
                Attribution = "Tiles © Esri, HERE, Garmin, OpenStreetMap contributors" },
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
        private static string _styleCampaign = "";       // campagne dont on affiche le style

        // Style de la campagne en cours : un petit fichier par campagne (MapCache\Style_<campagne>.txt)
        private static string StylePath
        {
            get { return Path.Combine(CacheRoot, "Style_" + _styleCampaign + ".txt"); }
        }

        // Ancien réglage commun à toutes les campagnes (il n'est plus écrit, il sert seulement à reprendre l'existant)
        private static string OldStylePath
        {
            get { return Path.Combine(CacheRoot, "style.txt"); }
        }

        // A appeler avant de lire ou changer le style : chaque campagne garde SON style
        public static void UseCampaign(string campaign)
        {
            campaign = campaign ?? "";
            if (campaign == _styleCampaign && _currentStyleId != null) return;

            _styleCampaign = campaign;
            _currentStyleId = null;         // relu au prochain accès
        }

        // Style choisi pour la campagne en cours (gardé d'une fois sur l'autre, propre à chaque campagne)
        public static Map_Style CurrentStyle
        {
            get
            {
                if (_currentStyleId == null)
                {
                    string id = "osm";
                    bool save = false;
                    try
                    {
                        if (File.Exists(StylePath))
                        {
                            id = File.ReadAllText(StylePath).Trim();
                        }
                        else if (HasCalibration(_styleCampaign) && File.Exists(OldStylePath))
                        {
                            // Campagne déjà calée avant ce changement : elle garde le style qu'elle montrait jusqu'ici
                            id = File.ReadAllText(OldStylePath).Trim();
                            save = true;
                        }
                    }
                    catch { id = "osm"; }

                    // Ancien choix Carto (devenu payant) : on passe sur Esri
                    if (id.StartsWith("carto_", StringComparison.OrdinalIgnoreCase))
                    {
                        id = "esri_street";
                        save = true;
                    }

                    _currentStyleId = id;
                    if (save) SaveStyle();
                    DeleteCartoCache();
                }
                return Styles.FirstOrDefault(x => x.Id == _currentStyleId) ?? Styles[0];
            }
            set
            {
                _currentStyleId = value.Id;
                SaveStyle();
            }
        }

        private static void SaveStyle()
        {
            try
            {
                Directory.CreateDirectory(CacheRoot);
                File.WriteAllText(StylePath, _currentStyleId);
            }
            catch { }
        }

        // Tuiles Carto déjà téléchargées : elles portent le filigrane, inutile de les garder
        private static void DeleteCartoCache()
        {
            try
            {
                if (!Directory.Exists(CacheRoot)) return;
                foreach (string dir in Directory.GetDirectories(CacheRoot, "Tiles_carto_*"))
                    Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Web | cache Carto non effacé : " + ex.Message);
            }
        }

        internal static string CacheRoot
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
            UseCampaign(campaign);
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
                bg.AnchorInfo = d.ContainsKey("anchor") ? d["anchor"] : "";
                bg.AnchorRule = d.ContainsKey("anchor_rule") ? d["anchor_rule"] : "";
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

        // tiles : reçoit { tuiles traitées, total } pendant le téléchargement (barre de progression de la fenêtre)
        public static Map_Background BuildBackground(string campaign, List<Map_Match> inputs, List<double[]> allPoints, IProgress<string> progress, IProgress<int[]> tiles = null)
        {
            UseCampaign(campaign);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Directory.CreateDirectory(CacheRoot);

            // 1) Aéroports réels reconnus par leur code ICAO (s'il y en a)
            var matches = new List<Map_Match>();
            var notFound = new List<string>();

            // Bases avec code ICAO : cherchées dans la liste des aéroports. Bases sans code : rattachées à une piste réelle proche (étape 1b).
            List<Map_Match> withIcao = inputs.Where(m => !string.IsNullOrWhiteSpace(m.Icao)).ToList();
            List<Map_Match> noIcao = inputs.Where(m => string.IsNullOrWhiteSpace(m.Icao)).ToList();

            if (withIcao.Count > 0)
            {
                string csv = Path.Combine(CacheRoot, "airports.csv");
                if (!File.Exists(csv))
                {
                    progress.Report("Downloading the airport list (about 12 MB, only once)...");
                    Download(AirportsUrl, csv);
                }

                progress.Report("Looking for the airbases by ICAO code...");
                var wanted = new HashSet<string>(withIcao.Select(m => m.Icao), StringComparer.OrdinalIgnoreCase);
                Dictionary<string, double[]> coords = ReadAirports(csv, wanted);

                foreach (Map_Match m in withIcao)
                {
                    double[] c;
                    if (coords.TryGetValue(m.Icao, out c))
                    {
                        m.Lat = m.ArpLat = c[0];
                        m.Lon = m.ArpLon = c[1];
                        m.Ref = "airport point";
                        matches.Add(m);
                    }
                    else
                    {
                        notFound.Add(m.Name);
                    }
                }
            }

            // 1b) Le point d'une base DCS est tantôt le milieu de la piste principale, tantôt un bout de piste, tantôt ailleurs.
            //     On compare donc chaque base aux vraies pistes (milieu et deux bouts) et on garde le point le plus proche comme référence du calage.
            //     Les bases sans code ICAO entrent aussi dans le calage si une piste réelle passe tout près.
            string anchorInfo = "", anchorRule = "";
            try { anchorInfo = RefineWithRunways(campaign, matches, noIcao, progress, out anchorRule); }
            catch (Exception ex) { FormUtils.LogRegister("Map_Web | pistes réelles : " + ex.Message); }

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
                        + (theatre != "" ? " (" + theatre + ")" : "") + " is not in the list of known maps. Cannot place the web map."
                        + " Add a line for it in the [theatres] section of " + Map_Data.UserFile);
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
                    if (tiles != null) tiles.Report(new[] { done, total });
                    string file = TileFile(z, tx, ty);
                    if (File.Exists(file)) continue;

                    progress.Report("Downloading the map: tile " + done + " / " + total + " (" + (100 * done / total) + " %)...");
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

            int nMid = used.Count(m => m.Ref == "runway middle"), nEnd = used.Count(m => m.Ref == "runway end"), nArp = used.Count(m => m.Ref == "airport point");
            if (nMid + nEnd > 0)
                report.Append(" Base point matched to the real runways: " + nMid + " at a runway middle, " + nEnd + " at a runway end, " + nArp + " at the airport point.");

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
                "report=" + report.ToString().Replace("\r", " ").Replace("\n", " "),
                "anchor=" + anchorInfo.Replace("\r", " ").Replace("\n", " "),
                "anchor_rule=" + anchorRule
            };
            File.WriteAllLines(CalibPath(campaign), lines, Encoding.UTF8);

            Map_Background bg = LoadTiles(proj, z, x0, x1, y0, y1);
            bg.Report = report.ToString();
            bg.AnchorInfo = anchorInfo;
            bg.AnchorRule = anchorRule;
            return bg;
        }

        internal static string TileUrl(Map_Style st, int z, int x, int y)
        {
            return st.Url.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
        }

        internal static void Download(string url, string dest)
        {
            // Interrupteur "Web data" de la fenêtre principale : OFF = aucun accès réseau, jamais
            if (!Main_WebAccess.Enabled)
                throw new WebException("Web access is turned off (Web data : OFF).");

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
        internal static List<string> ParseCsvLine(string line)
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
        // Les valeurs viennent de la section [theatres] de Data\dce_data.ini (+ %AppData%\DCE_Manager\dce_data_user.ini) : voir Map_Data.

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
            double cm, fe, fn;
            if (theatre != "" && Map_Data.TheatreProjection(theatre, out cm, out fe, out fn))
                return new Map_Projection { Cm = cm, Fe = fe, Fn = fn };
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

        // Pour chaque base, choisit comme référence réelle le point de piste le plus proche du point DCS (milieu ou bout d'une piste).
        // matches : bases avec ICAO (on ne regarde que les pistes de leur aéroport, sinon on garde le point de l'aéroport).
        // noIcao : bases sans ICAO (on regarde toutes les pistes à moins de 1 km) ; celles qui trouvent une piste sont ajoutées à matches.
        // Renvoie aussi la mesure de la règle de position des pistes de db_airbases (texte, vide si impossible)
        private static string RefineWithRunways(string campaign, List<Map_Match> matches, List<Map_Match> noIcao, IProgress<string> progress, out string anchorRule)
        {
            anchorRule = "";
            if (matches.Count == 0 && noIcao.Count == 0) return "";
            if (!Map_RunwayData.EnsureFile(progress)) return "";

            List<Map_RunwayData.PubRunway> all = Map_RunwayData.All();
            if (all.Count == 0) return "";

            // Première projection : celle du théâtre si elle existe et colle, sinon le calage sur les points d'aéroport
            string theatre;
            Map_Projection known = KnownProjection(campaign, out theatre);
            Map_Projection p = known;
            bool fitted = false;
            if (matches.Count >= 3)
            {
                Map_Projection fit = FitRobust(matches, new List<Map_Match>());
                if (known == null) { p = fit; fitted = true; }
                else
                {
                    double eFit = Median(matches.Select(m => ErrorOf(fit, m)));
                    double eKnown = Median(matches.Select(m => ErrorOf(known, m)));
                    if (eKnown > eFit * 1.25 + 150) { p = fit; fitted = true; }
                }
            }
            if (p == null) return "";

            progress.Report("Matching the airbases to the real runways...");
            var byIdent = new Dictionary<string, List<Map_RunwayData.PubRunway>>(StringComparer.OrdinalIgnoreCase);
            foreach (Map_RunwayData.PubRunway r in all)
            {
                if (r.Ident == "") continue;
                List<Map_RunwayData.PubRunway> l;
                if (!byIdent.TryGetValue(r.Ident, out l)) byIdent[r.Ident] = l = new List<Map_RunwayData.PubRunway>();
                l.Add(r);
            }

            var extra = new List<Map_Match>();
            for (int pass = 0; pass < (fitted ? 2 : 1); pass++)
            {
                foreach (Map_Match m in matches)
                {
                    List<Map_RunwayData.PubRunway> l;
                    byIdent.TryGetValue(m.Icao, out l);
                    if (!PickReference(p, m, l, 1500))
                    {
                        m.Lat = m.ArpLat; m.Lon = m.ArpLon; m.Ref = "airport point";
                    }
                }

                extra.Clear();
                foreach (Map_Match m in noIcao)
                {
                    double lat, lon;
                    DcsToLatLon(p, m.X, m.Y, out lat, out lon);
                    var near = new List<Map_RunwayData.PubRunway>();
                    foreach (Map_RunwayData.PubRunway r in all)
                        if (Math.Abs((r.LatA + r.LatB) / 2 - lat) < 0.05 && Math.Abs((r.LonA + r.LonB) / 2 - lon) < 0.07) near.Add(r);
                    if (near.Count > 0 && PickReference(p, m, near, 1000)) extra.Add(m);
                }

                if (fitted)
                {
                    var pool = new List<Map_Match>(matches);
                    pool.AddRange(extra);
                    if (pool.Count >= 3) p = FitRobust(pool, new List<Map_Match>());
                }
            }
            matches.AddRange(extra);

            return MeasureAnchor(p, matches, byIdent, out anchorRule);
        }

        // Où se trouve le point (x, y) des pistes de db_airbases sur la vraie piste ? (milieu, bout est, bout ouest, début de chaque piste)
        // Pour chaque point (piste de db_airbases ; à défaut le point de la base), on cherche la vraie piste de même cap la plus proche (moins de 3 km),
        // et on mesure la position le long de la piste : -1 = bout ouest, 0 = milieu, +1 = bout est (nord / sud si la piste va du nord au sud).
        // 2e mesure : dans le sens du cap de la piste de db_airbases : -1 = le point est au début de la piste (seuil), +1 = à la fin.
        private static string MeasureAnchor(Map_Projection p, List<Map_Match> matches, Dictionary<string, List<Map_RunwayData.PubRunway>> byIdent, out string rule)
        {
            rule = "";
            var east = new List<double>();
            var own = new List<double>();

            foreach (Map_Match m in matches)
            {
                List<Map_RunwayData.PubRunway> real;
                if (string.IsNullOrEmpty(m.Icao) || !byIdent.TryGetValue(m.Icao, out real)) continue;

                // Vraies pistes en coordonnées DCS : milieu (x, y), direction unitaire vers l'est (nord, est), demi-longueur
                var rr = new List<double[]>();
                foreach (Map_RunwayData.PubRunway r in real)
                {
                    double ax, ay, bx, by;
                    LatLonToDcs(p, r.LatA, r.LonA, out ax, out ay);
                    LatLonToDcs(p, r.LatB, r.LonB, out bx, out by);
                    double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    if (len < 200) continue;

                    double un = (bx - ax) / len, ue = (by - ay) / len;
                    if (ue < 0 || (Math.Abs(ue) < 1e-9 && un < 0)) { un = -un; ue = -ue; }
                    rr.Add(new[] { (ax + bx) / 2, (ay + by) / 2, un, ue, len / 2 });
                }
                if (rr.Count == 0) continue;

                // Points à mesurer : x, y, cap dans db_airbases (NaN = c'est le point de la base)
                var pts = new List<double[]>();
                if (m.DbRunways != null)
                    foreach (Map_Runway e in m.DbRunways)
                        pts.Add(new[] { (e.X == 0 && e.Y == 0) ? m.X : e.X, (e.X == 0 && e.Y == 0) ? m.Y : e.Y, e.Hdg });
                if (pts.Count == 0) pts.Add(new[] { m.X, m.Y, double.NaN });

                foreach (double[] pt in pts)
                {
                    double[] best = null;
                    double bestD = 3000;
                    foreach (double[] r in rr)
                    {
                        double d = Math.Sqrt((pt[0] - r[0]) * (pt[0] - r[0]) + (pt[1] - r[1]) * (pt[1] - r[1]));
                        if (d >= bestD) continue;

                        if (!double.IsNaN(pt[2]))
                        {
                            double hr = Math.Atan2(r[3], r[2]) * 180.0 / Math.PI;           // cap de la vraie piste (0 a 180)
                            double dh = Math.Abs((((pt[2] - hr) % 180) + 180) % 180);
                            if (dh > 90) dh = 180 - dh;
                            if (dh > 20) continue;
                        }
                        best = r;
                        bestD = d;
                    }
                    if (best == null) continue;

                    double dn = pt[0] - best[0], de = pt[1] - best[1];
                    east.Add((dn * best[2] + de * best[3]) / best[4]);
                    if (!double.IsNaN(pt[2]))
                    {
                        double a = pt[2] * Math.PI / 180.0;
                        own.Add((dn * Math.Cos(a) + de * Math.Sin(a)) / best[4]);
                    }
                }
            }
            if (east.Count < 3) return "";

            double mE = Median(east);
            double mO = own.Count >= 3 ? Median(own) : double.NaN;

            string verdict;
            if (Math.Abs(mE) < 0.3 && (double.IsNaN(mO) || Math.Abs(mO) < 0.3)) { verdict = "in the middle of the runway"; rule = "middle"; }
            else if (!double.IsNaN(mO) && mO < -0.7) { verdict = "at the start of each runway (threshold)"; rule = "start"; }
            else if (mE > 0.7) { verdict = "at the east end of the runway"; rule = "east"; }
            else if (mE < -0.7) { verdict = "at the west end of the runway"; rule = "west"; }
            else verdict = "no clear rule (mixed)";

            CultureInfo c = CultureInfo.InvariantCulture;
            return "Measured on " + east.Count + " points of db_airbases against the real runways: median position " + (mE * 100).ToString("+0;-0;0", c)
                + " % (-100 = west end, 0 = middle, +100 = east end)"
                + (double.IsNaN(mO) ? "" : "; along the heading of the runway " + (mO * 100).ToString("+0;-0;0", c) + " % (-100 = start of the runway)")
                + ". It looks like the point is " + verdict + ".";
        }

        // Milieu ou bout de piste le plus proche du point DCS de la base (dans la limite de maxDist mètres) ; met à jour m.Lat / m.Lon / m.Ref
        private static bool PickReference(Map_Projection p, Map_Match m, List<Map_RunwayData.PubRunway> runways, double maxDist)
        {
            if (runways == null) return false;

            double best = double.MaxValue, bLat = 0, bLon = 0;
            string kind = "";
            foreach (Map_RunwayData.PubRunway r in runways)
            {
                double[,] pts =
                {
                    { r.LatA, r.LonA, 1 },
                    { r.LatB, r.LonB, 1 },
                    { (r.LatA + r.LatB) / 2, (r.LonA + r.LonB) / 2, 0 }
                };
                for (int i = 0; i < 3; i++)
                {
                    double x, y;
                    LatLonToDcs(p, pts[i, 0], pts[i, 1], out x, out y);
                    double d = Math.Sqrt((x - m.X) * (x - m.X) + (y - m.Y) * (y - m.Y));
                    if (d < best) { best = d; bLat = pts[i, 0]; bLon = pts[i, 1]; kind = pts[i, 2] > 0 ? "runway end" : "runway middle"; }
                }
            }
            if (best > maxDist) return false;

            m.Lat = bLat; m.Lon = bLon; m.Ref = kind;
            return true;
        }

        private static double Median(IEnumerable<double> values)
        {
            List<double> l = values.OrderBy(v => v).ToList();
            return l.Count == 0 ? 0 : l[l.Count / 2];
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

        internal static int TileX(double lon, int z)
        {
            return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << z));
        }

        internal static int TileY(double lat, int z)
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

    // Un tracé du fichier mission (frontières, zones...) : coordonnées DCS absolues
    internal class Map_Drawing
    {
        public string Name = "";
        public string Group = "mission";    // mission / zones / spawn : chaque groupe a son bouton
        public string Label = "";           // texte écrit au milieu de la forme (nom de zone)
        public double RangeM;               // portée (m) : si > 0, la distance est ajoutée à l'étiquette dans l'unité choisie (km / Nm)

        public string LabelText
        {
            get { return RangeM > 0 && Label != "" && Label != "onsel" ? Label + " " + Map_Units.Dist(RangeM, "0") : Label; }
        }
        public string Kind = "";            // Line / Polygon / Circle / Text
        public List<double[]> Points = new List<double[]>();   // { x (nord), y (est) }
        public bool Closed;
        public bool Dashed;
        public float Thickness = 2;
        public Color Line = Color.White;
        public Color Fill = Color.Transparent;
        public double Radius;               // cercle, en mètres
        public string Text = "";
        public int Index;                   // marqueur de waypoint : numéro du waypoint dans son vol
        public int Alert;                   // tronçon de plan de vol : 0 normal, 1 vitesse tendue (orange), 2 impossible (rouge clignotant)
    }

    // Un élément au sol de la mission (véhicule, statique, navire) : position, camp, type d'icône ; la table Lua d'origine
    // est gardée (déjà en mémoire) pour afficher tous ses champs au clic, sans rien recopier au chargement.
    internal class Map_GroundUnit
    {
        public double X, Y;             // coordonnées DCS (x = nord, y = est)
        public bool Red;                // false = bleu
        public char Kind;               // icône : a blindé, u véhicule, m défense sol-air, r radar, g artillerie, i infanterie, b statique, p avion/hélico statique, f FARP, n navire, k porte-avions
        public float HalfM;             // demi-taille de l'icône en mètres (taille approximative réelle) : l'icône grossit avec le zoom au-delà de sa taille minimale
        public double Heading = double.NaN;     // cap en radians (0 = nord, sens horaire), NaN si absent
        public string Name = "", Type = "";
        public Dictionary<string, object> Src;
        public Map_GroundGroup Group;
    }

    // Taille approximative réelle des éléments au sol (pour que l'icône grossisse quand on zoome).
    // A MODIFIER ICI : motif cherché dans le type DCS -> longueur en mètres. Sinon valeur par famille d'icône.
    internal static class Map_GroundSize
    {
        private static readonly object[][] ShipLengths =
        {
            new object[] { "CVN|VINSON|STENNIS|NIMITZ|CARL|ROOSEVELT|WASHINGTON|TRUMAN|LINCOLN", 333.0 },
            new object[] { "KUZNECOW|KUZNETSOV|CV_1143|LIAONING", 305.0 },
            new object[] { "FORRESTAL|KITTY|SARATOGA|AMERICA", 325.0 },
            new object[] { "LHA|TARAWA|WASP|LHD", 250.0 },
            new object[] { "INVINCIBLE", 209.0 },
            new object[] { "PIOTR|PYOTR|VELIKIY|KIROV", 252.0 },
            new object[] { "071|LPD", 210.0 },
            new object[] { "MOSCOW|MOSKVA|SLAVA", 186.0 },
            new object[] { "TICONDEROG|CG_|CG-", 173.0 },
            new object[] { "ARLEIGH|BURKE|DDG", 155.0 },
            new object[] { "052B|052C|052D|LUYANG|LUZHOU", 155.0 },
            new object[] { "054A|JIANGKAI", 134.0 },
            new object[] { "PERRY|OLIVER|FFG", 136.0 },
            new object[] { "NEUSTRASH|REZKY|KRIVAK", 125.0 },
            new object[] { "ALBATROS|GRISHA", 72.0 },
            new object[] { "MOLNIYA|TARANTUL", 56.0 },
            new object[] { "BDK|ROPUCHA", 112.0 },
            new object[] { "KILO|SOM|SUB|SS_", 74.0 },
            new object[] { "SEAWISE", 458.0 },
            new object[] { "ELNYA|TANKER|BULKER|HANDY", 180.0 },
            new object[] { "DRY|CARGO|CONTAINER", 140.0 },
            new object[] { "LST|HIGGINS|LANDING", 60.0 },
            new object[] { "SPEEDBOAT|BOAT|ZWEZDNY|RIB", 12.0 },
        };

        // véhicules connus (longueur totale, m)
        private static readonly object[][] VehicleLengths =
        {
            new object[] { "ABRAMS|LEOPARD|T-90|T-80|T-72|CHALLENGER|LECLERC", 10.0 },
            new object[] { "PATRIOT|S-300|S300|5P85|SCUD|ISKANDER|HIMARS|MLRS|SMERCH|GRAD", 11.0 },
            new object[] { "KAMAZ|URAL|ZIL|TRUCK|M978|M939|HEMTT", 8.0 },
            new object[] { "HMMWV|M1043|M1045|UAZ|JEEP|LAND_ROVER|TIGR", 5.0 },
        };

        private static readonly List<KeyValuePair<System.Text.RegularExpressions.Regex, double>> RxShip = Compile(ShipLengths);
        private static readonly List<KeyValuePair<System.Text.RegularExpressions.Regex, double>> RxVehicle = Compile(VehicleLengths);
        private static readonly Dictionary<string, double> Cache = new Dictionary<string, double>();

        private static List<KeyValuePair<System.Text.RegularExpressions.Regex, double>> Compile(object[][] rows)
        {
            var list = new List<KeyValuePair<System.Text.RegularExpressions.Regex, double>>();
            foreach (object[] row in rows)
                list.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, double>(
                    new System.Text.RegularExpressions.Regex((string)row[0], System.Text.RegularExpressions.RegexOptions.IgnoreCase), (double)row[1]));
            return list;
        }

        // Longueur par défaut selon la famille d'icône
        private static double DefaultLength(char kind)
        {
            switch (kind)
            {
                case 'a': return 8;
                case 'm': return 9;
                case 'r': return 8;
                case 'g': return 8;
                case 'i': return 2;
                case 'b': return 12;
                case 'p': return 16;
                case 'f': return 60;
                case 'n': return 120;
                case 'k': return 300;
                default: return 6;
            }
        }

        // Combien d'unités "h" l'icône mesure dans sa plus grande dimension (voir DrawGroundIcon)
        private static double ShapeUnits(char kind)
        {
            switch (kind)
            {
                case 'n': return 3.3;
                case 'k': return 4.8;
                case 'a': return 2.8;
                case 'm': return 2.2;
                case 'g': return 2.2;
                case 'b': return 2.4;
                case 'p': return 2.1;
                default: return 2.0;
            }
        }

        // Longueur approximative (m) d'un élément de cette famille / de ce type (ex. porte-avions 'k')
        public static double LengthMeters(char kind, string type)
        {
            return HalfMeters(kind, type) * ShapeUnits(kind);
        }

        // Demi-taille "h" de l'icône en mètres
        public static float HalfMeters(char kind, string type)
        {
            double len;
            string key = kind + "|" + type;
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out len))
                {
                    len = DefaultLength(kind);
                    if (kind == 'n' || kind == 'k' || kind == 'a' || kind == 'u' || kind == 'm' || kind == 'g' || kind == 'r')
                    {
                        foreach (KeyValuePair<System.Text.RegularExpressions.Regex, double> r in (kind == 'n' || kind == 'k') ? RxShip : RxVehicle)
                        {
                            if (!r.Key.IsMatch(type ?? "")) continue;
                            len = r.Value;
                            break;
                        }
                    }
                    Cache[key] = len;
                }
            }
            return (float)(len / ShapeUnits(kind));
        }
    }

    // Un groupe au sol (pour afficher son nom une seule fois, au centre du groupe)
    internal class Map_GroundGroup
    {
        public string Name = "", Country = "", Category = "";
        public double X, Y;
        public bool Red;
        public Dictionary<string, object> Src;
        public List<Map_GroundUnit> Units = new List<Map_GroundUnit>();
    }

    // Mission en cours : plans de vol des joueurs + menaces sol-air (lus dans le .miz de la mission)
    internal class Map_MissionData
    {
        public string Source = "";      // fichier lu (vide = aucune mission trouvée)
        public string Error = "";
        public int PlayerFlights;
        public int PackageFlights;      // vols des mêmes packages que les joueurs (hors joueurs)
        public int OtherFlights;        // tous les autres vols du camp affiché
        public string BriefingBlue = "", BriefingRed = "", BriefingGeneral = "", BriefingNeutral = "";    // textes de briefing (descriptionBlueTask / RedTask, résolus dans le dictionary)
        public double StartTime;        // heure de début de mission (secondes depuis minuit)
        public bool Fallback;           // vrai = le .miz attendu n'avait aucun vol joueur, un autre a été pris
        public List<Map_Flight> Flights = new List<Map_Flight>();
        public int SamSites;
        public List<Map_Drawing> Plans = new List<Map_Drawing>();
        public List<Map_Drawing> Threats = new List<Map_Drawing>();
        public List<Map_GroundUnit> Ground = new List<Map_GroundUnit>();           // éléments sol non cachés
        public List<Map_GroundGroup> GroundGroups = new List<Map_GroundGroup>();
        public List<Map_Drawing> Radars = new List<Map_Drawing>();                 // zones radar des EWR au sol (cercles)
        public List<Map_Drawing> ShipRoutes = new List<Map_Drawing>();             // trajets prévus des navires (pointillés) ; Text = camp ("blue" / "red")
    }

    // Lecture des dessins de l'éditeur de mission (drawings/layers) dans Init\base_mission.miz.
    // Un .miz est un zip : le fichier "mission" est une table Lua, lue ici par un petit lecteur maison.
    internal static partial class Map_Drawings
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

        // Le .miz de la mission affichée (jamais Active\last_Mission.lua : on veut pouvoir modifier ce fichier plus tard).
        //  - justGenerated = true (juste après une génération) : le .miz le plus récent
        //  - sinon selon le numéro de mission (Active\camp_status.lua) : <= 1 -> le fichier "first", sinon le fichier "ongoing"
        // Si le fichier voulu n'existe pas, on prend l'autre.
        public static string FindMissionSource(string campaign, bool justGenerated = false)
        {
            List<string> list = FindMissionSources(campaign, justGenerated);
            return list.Count > 0 ? list[0] : null;
        }

        // Les .miz candidats, le plus attendu d'abord. Ils sont rangés À CÔTÉ du dossier de la campagne (pas dedans) :
        //   ...\Missions\Campaigns\<campagne>_first.miz   et   ...\Missions\Campaigns\<campagne>_ongoing.miz
        public static List<string> FindMissionSources(string campaign, bool justGenerated = false)
        {
            string campaigns = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns");
            string firstFile = Path.Combine(campaigns, campaign + "_first.miz");
            string ongoingFile = Path.Combine(campaigns, campaign + "_ongoing.miz");

            var first = new List<string>();
            var ongoing = new List<string>();
            if (File.Exists(firstFile)) first.Add(firstFile);
            if (File.Exists(ongoingFile)) ongoing.Add(ongoingFile);

            // Nom un peu différent (espaces, casse, version dans le nom du dossier...) : on cherche aussi par ressemblance
            if ((first.Count == 0 || ongoing.Count == 0) && Directory.Exists(campaigns))
            {
                string key = campaign.Trim();
                foreach (string f in Directory.GetFiles(campaigns, "*.miz"))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    bool isFirst = n.EndsWith("_first", StringComparison.OrdinalIgnoreCase);
                    bool isOngoing = n.EndsWith("_ongoing", StringComparison.OrdinalIgnoreCase);
                    if (!isFirst && !isOngoing) continue;

                    string prefix = n.Substring(0, n.LastIndexOf('_')).Trim();
                    bool similar = prefix.Equals(key, StringComparison.OrdinalIgnoreCase)
                                || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                || prefix.StartsWith(key, StringComparison.OrdinalIgnoreCase);
                    if (!similar) continue;

                    if (isFirst && !first.Contains(f)) first.Add(f);
                    if (isOngoing && !ongoing.Contains(f)) ongoing.Add(f);
                }
            }
            FormUtils.LogRegister("Map_Drawings | .miz of '" + campaign + "' in " + campaigns + " : " + first.Count + " first, " + ongoing.Count + " ongoing");

            var all = new List<string>(first);
            all.AddRange(ongoing);
            all.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));     // du plus récent au plus ancien

            var result = new List<string>();
            if (!justGenerated)
            {
                int mission = WargameLogistics.ReadMission(Path.Combine(campaigns, campaign));
                string pick = Newest(mission <= 1 ? first : ongoing);
                if (pick != null) result.Add(pick);
            }
            foreach (string f in all)
                if (!result.Contains(f)) result.Add(f);

            return result;
        }

        private static string Newest(List<string> files)
        {
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string f in files)
            {
                DateTime t = File.GetLastWriteTime(f);
                if (t > bestTime) { best = f; bestTime = t; }
            }
            return best;
        }

        // Table "last_Mission" (fichier .lua) ou "mission" (dans le .miz)
        // Le fichier mission est lu par les vols ET par le panneau météo : on garde le dernier résultat pour ne l'analyser qu'une fois
        private static string _rootKey = "";
        private static Dictionary<string, object> _rootCache;

        internal static Dictionary<string, object> ReadMissionRoot(string file)
        {
            string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks;
            lock (typeof(Map_Drawings))
            {
                if (key == _rootKey && _rootCache != null) return _rootCache;
            }

            Dictionary<string, object> root = ReadMissionRootNow(file);
            lock (typeof(Map_Drawings))
            {
                _rootKey = key;
                _rootCache = root;
            }
            return root;
        }

        private static Dictionary<string, object> ReadMissionRootNow(string file)
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

        // ------------------------------------------------------------------
        //  Écriture : positions des waypoints déplacés dans la carte
        //  On ne touche qu'aux nombres x / y (le reste du fichier mission reste identique, caractère pour caractère).
        //  Départ en l'air : le premier waypoint entraîne aussi la position du groupe et de ses avions.
        //  La première fois, l'original est copié en "<fichier>.wpt_backup".
        // ------------------------------------------------------------------
        internal static bool SaveWaypoints(string file, IEnumerable<Map_Flight> flights, out string message)
        {
            message = "";
            try
            {
                bool isMiz = file.EndsWith(".miz", StringComparison.OrdinalIgnoreCase);
                string text;
                if (isMiz)
                {
                    using (ZipArchive zip = ZipFile.OpenRead(file))
                    {
                        ZipArchiveEntry entry = zip.GetEntry("mission");
                        if (entry == null) { message = "No 'mission' file inside the .miz."; return false; }
                        using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                            text = sr.ReadToEnd();
                    }
                }
                else text = File.ReadAllText(file);

                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                Dictionary<string, object> coalitions = root == null ? null : Sub(root, "coalition");
                if (coalitions == null) { message = "The mission file has no coalition table."; return false; }

                // Tous les groupes avion / hélico, par nom
                var groups = new Dictionary<string, Dictionary<string, object>>();
                foreach (object co in coalitions.Values)
                {
                    Dictionary<string, object> countries = Sub(co as Dictionary<string, object>, "country");
                    if (countries == null) continue;
                    foreach (object cy in countries.Values)
                    {
                        foreach (string cat in new[] { "plane", "helicopter" })
                        {
                            Dictionary<string, object> gl = Sub(Sub(cy as Dictionary<string, object>, cat), "group");
                            if (gl == null) continue;
                            foreach (object go in gl.Values)
                            {
                                var g = go as Dictionary<string, object>;
                                string n = g == null ? "" : Str(g, "name");
                                if (n != "" && !groups.ContainsKey(n)) groups[n] = g;
                            }
                        }
                    }
                }

                var changes = new List<KeyValuePair<int[], string>>();      // (début, fin) -> nouveau texte
                int nbWp = 0, nbFlights = 0;
                // Remplace la valeur d'une clé ; si la clé n'existe pas dans la table, elle est ajoutée au début de la table
                Func<Dictionary<string, object>, string, string, bool> setRaw = (table, key, raw) =>
                {
                    Dictionary<string, int[]> ks;
                    int[] span;
                    if (table == null || !spans.TryGetValue(table, out ks)) return false;
                    if (ks.TryGetValue(key, out span)) changes.Add(new KeyValuePair<int[], string>(span, raw));
                    else if (ks.TryGetValue("{", out span)) changes.Add(new KeyValuePair<int[], string>(new[] { span[0], span[0] }, " [\"" + key + "\"] = " + raw + ","));
                    else return false;
                    return true;
                };
                Func<Dictionary<string, object>, string, double, bool> setNum = (table, key, value) =>
                    setRaw(table, key, Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture));

                foreach (Map_Flight fl in flights)
                {
                    Dictionary<string, object> g;
                    if (!fl.Edited || !groups.TryGetValue(fl.Name, out g)) continue;

                    Dictionary<string, object> points = Sub(Sub(g, "route"), "points");
                    if (points == null) continue;

                    int first = fl.Waypoints.Where(q => !q.IsNew).Select(q => q.Number).DefaultIfEmpty(0).Min();
                    bool any = false;
                    for (int wi = 0; wi < fl.Waypoints.Count; wi++)
                    {
                        Map_Waypoint wp = fl.Waypoints[wi];
                        if (wp.IsNew) { nbWp++; any = true; continue; }          // waypoint ajouté : il est écrit en entier plus bas (RebuildRoutePoints)
                        double dx = wp.X - wp.OrigX, dy = wp.Y - wp.OrigY;
                        bool moved = Math.Abs(dx) >= 0.01 || Math.Abs(dy) >= 0.01;

                        object po;
                        var p = points.TryGetValue(wp.Number.ToString(CultureInfo.InvariantCulture), out po) ? po as Dictionary<string, object> : null;
                        if (p == null || (moved && (!setNum(p, "x", wp.X) || !setNum(p, "y", wp.Y)))) { message = "Waypoint " + wp.Number + " of '" + fl.Name + "' not found in the file."; return false; }

                        // Heures et vitesses recalculées pour TOUS les waypoints du vol (ce que la carte affiche)
                        if (wi < fl.Times.Length) setNum(p, "ETA", fl.Times[wi]);
                        // La vitesse d'un waypoint est celle du tronçon qui le suit : on écrit la vitesse calculée du tronçon wi -> wi+1
                        // (pas pour un point dont la vitesse du plan est ~0 : décollage / fin, sauf si elle a été changée à la main)
                        if (wp.Dirty.Contains("speed")) setNum(p, "speed", wp.Speed);
                        else if (wi + 1 < fl.LegSpeed.Length && fl.LegSpeed[wi + 1] > 1 && wp.OrigSpeed >= 10) setNum(p, "speed", fl.LegSpeed[wi + 1]);
                        if (wp.Dirty.Contains("alt")) setNum(p, "alt", wp.Alt);
                        if (wp.Dirty.Contains("speed_locked")) setRaw(p, "speed_locked", wp.SpeedLocked ? "true" : "false");
                        if (wp.Dirty.Contains("ETA_locked")) setRaw(p, "ETA_locked", wp.EtaLocked ? "true" : "false");
                        nbWp++;
                        any = true;

                        // Type de départ changé dans la fiche : seulement ["type"] et ["action"] du point (les "parking" / "parking_id" des avions restent)
                        bool startAir = wp.Number == first && fl.StartAir;
                        if (wp.Dirty.Contains("start"))
                        {
                            setRaw(p, "type", LuaText(wp.Type));
                            setRaw(p, "action", LuaText(wp.Action));
                            bool ground = wp.Type.StartsWith("TakeOffGround", StringComparison.Ordinal);
                            if (ground) setRaw(p, "alt_type", LuaText("RADIO"));
                            if (ground || startAir)
                            {
                                // Plus de terrain / piste / navire rattaché au point (comme GroundStartPoint)
                                Dictionary<string, int[]> pks;
                                if (spans.TryGetValue(p, out pks))
                                    foreach (string idKey in new[] { "linkUnit", "helipadId", "airdromeId" })
                                    {
                                        TextEdit rem = KeyRemoval(text, pks, idKey);
                                        if (rem != null) changes.Add(new KeyValuePair<int[], string>(new[] { rem.S, rem.E }, ""));
                                    }
                            }
                            if (startAir) setNum(g, "start_time", 0);       // le groupe est actif dès le début de la mission
                        }

                        // Départ en l'air : le groupe et ses avions suivent le premier waypoint (en vol choisi à la main : ils viennent sur le point, à son altitude et sa vitesse)
                        if (wp.Number == first && ((moved && wp.Role != "airfield") || (startAir && wp.Dirty.Contains("start"))))
                        {
                            double sx = startAir && wp.Dirty.Contains("start") ? wp.X - Num(g, "x") : dx;
                            double sy = startAir && wp.Dirty.Contains("start") ? wp.Y - Num(g, "y") : dy;
                            setNum(g, "x", Num(g, "x") + sx);
                            setNum(g, "y", Num(g, "y") + sy);
                            Dictionary<string, object> units = Sub(g, "units");
                            if (units != null)
                                foreach (object uo in units.Values)
                                {
                                    var u = uo as Dictionary<string, object>;
                                    if (u == null) continue;
                                    setNum(u, "x", Num(u, "x") + sx);
                                    setNum(u, "y", Num(u, "y") + sy);
                                    if (startAir && wp.Dirty.Contains("start"))
                                    {
                                        setNum(u, "alt", wp.Alt);
                                        setNum(u, "speed", wp.Speed);
                                    }
                                }
                        }
                    }
                    if (any) nbFlights++;
                }

                if (nbWp == 0) { message = "Nothing to write."; return false; }

                // Du dernier au premier : les positions du texte restent valables
                changes.Sort((a, b) => b.Key[0].CompareTo(a.Key[0]));
                var sb = new StringBuilder(text);
                foreach (KeyValuePair<int[], string> ch in changes)
                {
                    sb.Remove(ch.Key[0], ch.Key[1] - ch.Key[0]);
                    sb.Insert(ch.Key[0], ch.Value);
                }
                string newText = sb.ToString();

                // Waypoints ajoutés / supprimés : la table "points" des vols concernés est refaite avec les nouveaux numéros
                int nbAdded = 0, nbRemoved = 0;
                foreach (Map_Flight fl in flights)
                {
                    if (!fl.Edited || !fl.Structural) continue;
                    string err;
                    int ad, rm;
                    newText = RebuildRoutePoints(newText, fl, out err, out ad, out rm);
                    if (newText == null) { message = err; return false; }
                    nbAdded += ad; nbRemoved += rm;
                }

                // Contrôle : le nouveau texte doit se relire, avec les mêmes groupes
                Dictionary<string, object> check = ParseMissionText(newText, null);
                if (check == null || Sub(check, "coalition") == null) { message = "The new text could not be read back: nothing was written."; return false; }

                string backup = file + ".wpt_backup";
                if (!File.Exists(backup)) File.Copy(file, backup);

                if (isMiz)
                {
                    string tmp = file + ".tmp_wpt";
                    File.Copy(file, tmp, true);
                    using (ZipArchive zip = ZipFile.Open(tmp, ZipArchiveMode.Update))
                    {
                        ZipArchiveEntry old = zip.GetEntry("mission");
                        if (old != null) old.Delete();
                        ZipArchiveEntry created = zip.CreateEntry("mission", CompressionLevel.Optimal);
                        using (var sw = new StreamWriter(created.Open(), new UTF8Encoding(false)))
                            sw.Write(newText);
                    }
                    File.Copy(tmp, file, true);
                    File.Delete(tmp);
                }
                else File.WriteAllText(file, newText, new UTF8Encoding(false));

                lock (typeof(Map_Drawings)) { _rootKey = ""; _rootCache = null; }
                message = "Mission saved: " + Path.GetFileName(file) + " (" + nbWp + " waypoint(s), " + nbFlights + " flight(s)"
                    + (nbAdded > 0 ? ", " + nbAdded + " waypoint(s) added" : "") + (nbRemoved > 0 ? ", " + nbRemoved + " deleted" : "") + ").";
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | save waypoints : " + ex);
                message = "Could not write the mission file: " + ex.Message;
                return false;
            }
        }

        // Refait la table ["points"] de la route d'un vol : même ordre que la carte, numéros 1, 2, 3...
        // Les points qui existent déjà gardent leur texte tel quel (déjà mis à jour : x, y, ETA, vitesse...). Les nouveaux sont écrits en entier.
        private static string RebuildRoutePoints(string text, Map_Flight fl, out string error, out int added, out int removed)
        {
            error = ""; added = 0; removed = 0;

            var spans = new Dictionary<object, Dictionary<string, int[]>>();
            Dictionary<string, object> root = ParseMissionText(text, spans);
            Dictionary<string, object> group = FindAirGroupByName(root, fl.Name);
            if (group == null) { error = "Flight '" + fl.Name + "' not found in the file."; return null; }

            Dictionary<string, object> route = Sub(group, "route");
            Dictionary<string, object> points = Sub(route, "points");
            Dictionary<string, int[]> routeSpans, pointSpans;
            int[] tableSpan;
            if (route == null || points == null || !spans.TryGetValue(route, out routeSpans) || !routeSpans.TryGetValue("points", out tableSpan) || !spans.TryGetValue(points, out pointSpans))
            { error = "Route of '" + fl.Name + "' not readable in the file."; return null; }

            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            string ind0 = LineIndent(text, tableSpan[0]);
            string ind1 = ind0 + "    ";

            var sb = new StringBuilder();
            sb.Append("{").Append(nl);
            int kept = 0;
            for (int k = 0; k < fl.Waypoints.Count; k++)
            {
                Map_Waypoint w = fl.Waypoints[k];
                string raw;
                if (w.IsNew)
                {
                    raw = NewRoutePointText(fl, k, w, ind1, nl);
                    added++;
                }
                else
                {
                    int[] sp;
                    if (!pointSpans.TryGetValue(w.Number.ToString(CultureInfo.InvariantCulture), out sp)) { error = "Waypoint " + w.Number + " of '" + fl.Name + "' not found in the file."; return null; }
                    raw = text.Substring(sp[0], sp[1] - sp[0]);
                    kept++;
                }
                sb.Append(ind1).Append("[").Append(k + 1).Append("] = ").Append(nl)
                  .Append(ind1).Append(raw).Append(", -- end of [").Append(k + 1).Append("]").Append(nl);
            }
            sb.Append(ind0).Append("}");
            removed = Math.Max(0, points.Count - kept);

            return text.Substring(0, tableSpan[0]) + sb + text.Substring(tableSpan[1]);
        }

        // Un waypoint "Turning Point" tout simple (pas de tâche) : altitude, vitesse, heure et position calculées par la carte
        private static string NewRoutePointText(Map_Flight fl, int k, Map_Waypoint w, string ind, string nl)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Func<double, string> num = v => Math.Round(v, 3).ToString("0.###", c);

            double speed = k + 1 < fl.LegSpeed.Length && fl.LegSpeed[k + 1] > 1 ? fl.LegSpeed[k + 1] : w.Speed;
            double eta = k < fl.Times.Length ? fl.Times[k] : 0;
            string altType = "BARO";
            object at;
            if (w.Source != null && w.Source.TryGetValue("alt_type", out at) && at is string && (string)at != "") altType = (string)at;

            string i1 = ind + "    ", i2 = i1 + "    ", i3 = i2 + "    ";
            var sb = new StringBuilder();
            sb.Append("{").Append(nl);
            sb.Append(i1).Append("[\"alt\"] = ").Append(num(w.Alt)).Append(",").Append(nl);
            sb.Append(i1).Append("[\"action\"] = \"Turning Point\",").Append(nl);
            sb.Append(i1).Append("[\"alt_type\"] = \"").Append(altType).Append("\",").Append(nl);
            sb.Append(i1).Append("[\"speed\"] = ").Append(num(speed)).Append(",").Append(nl);
            sb.Append(i1).Append("[\"task\"] = ").Append(nl);
            sb.Append(i1).Append("{").Append(nl);
            sb.Append(i2).Append("[\"id\"] = \"ComboTask\",").Append(nl);
            sb.Append(i2).Append("[\"params\"] = ").Append(nl);
            sb.Append(i2).Append("{").Append(nl);
            sb.Append(i3).Append("[\"tasks\"] = ").Append(nl);
            sb.Append(i3).Append("{").Append(nl);
            sb.Append(i3).Append("}, -- end of [\"tasks\"]").Append(nl);
            sb.Append(i2).Append("}, -- end of [\"params\"]").Append(nl);
            sb.Append(i1).Append("}, -- end of [\"task\"]").Append(nl);
            sb.Append(i1).Append("[\"type\"] = \"Turning Point\",").Append(nl);
            sb.Append(i1).Append("[\"ETA\"] = ").Append(num(eta)).Append(",").Append(nl);
            sb.Append(i1).Append("[\"ETA_locked\"] = false,").Append(nl);
            sb.Append(i1).Append("[\"y\"] = ").Append(num(w.Y)).Append(",").Append(nl);
            sb.Append(i1).Append("[\"x\"] = ").Append(num(w.X)).Append(",").Append(nl);
            sb.Append(i1).Append("[\"formation_template\"] = \"\",").Append(nl);
            sb.Append(i1).Append("[\"speed_locked\"] = true,").Append(nl);
            sb.Append(ind).Append("}");
            return sb.ToString();
        }

        private static Dictionary<string, object> FindAirGroupByName(Dictionary<string, object> root, string name)
        {
            Dictionary<string, object> coalitions = root == null ? null : Sub(root, "coalition");
            if (coalitions == null) return null;
            foreach (object co in coalitions.Values)
            {
                Dictionary<string, object> countries = Sub(co as Dictionary<string, object>, "country");
                if (countries == null) continue;
                foreach (object cy in countries.Values)
                    foreach (string cat in new[] { "plane", "helicopter" })
                    {
                        Dictionary<string, object> gl = Sub(Sub(cy as Dictionary<string, object>, cat), "group");
                        if (gl == null) continue;
                        foreach (object go in gl.Values)
                        {
                            var g = go as Dictionary<string, object>;
                            if (g != null && Str(g, "name") == name) return g;
                        }
                    }
            }
            return null;
        }

        private static Dictionary<string, object> ParseMissionText(string text, Dictionary<object, Dictionary<string, int[]>> spans)
        {
            foreach (string name in new[] { "last_Mission", "mission" })
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^\s*" + name + @"\s*=\s*\{");
                if (!m.Success) continue;
                return new LuaReader(text, m.Index + m.Length - 1, spans).ReadValue() as Dictionary<string, object>;
            }
            return null;
        }

        // Fichier l10n\DEFAULT\dictionary du .miz : "clé" -> texte
        private static Dictionary<string, object> ReadDictionary(string file)
        {
            if (!file.EndsWith(".miz", StringComparison.OrdinalIgnoreCase)) return null;
            string text;
            using (ZipArchive zip = ZipFile.OpenRead(file))
            {
                ZipArchiveEntry entry = zip.GetEntry("l10n/DEFAULT/dictionary");
                if (entry == null) return null;
                using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    text = sr.ReadToEnd();
            }
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^\s*dictionary\s*=\s*\{");
            if (!m.Success) return null;
            return new LuaReader(text, m.Index + m.Length - 1).ReadValue() as Dictionary<string, object>;
        }

        // Texte d'un champ du briefing : soit la valeur elle-même, soit la clé "DictKey_..." à chercher dans le dictionary
        private static string BriefingText(Dictionary<string, object> root, Dictionary<string, object> dict, string field)
        {
            string v = Str(root, field);
            if (v == "") return "";
            object t;
            string result = v.StartsWith("DictKey_") ? "" : v;
            if (dict != null && dict.TryGetValue(v, out t) && t is string && (string)t != v) result = (string)t;
            return result.Replace("\\\r\n", "\n").Replace("\\\n", "\n").Replace("\r\n", "\n");     // fin de ligne Lua "\" + saut de ligne
        }

        // Lit le .miz attendu ; s'il ne contient aucun vol joueur (mauvais fichier ?), on essaie les autres .miz de la campagne.
        public static Map_MissionData LoadMission(string campaign, bool justGenerated = false)
        {
            List<string> files = FindMissionSources(campaign, justGenerated);
            if (files.Count == 0)
                return new Map_MissionData { Error = "No mission file found for '" + campaign + "' in "
                    + Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns") + " (expected <campaign>_first.miz or <campaign>_ongoing.miz)." };

            Map_MissionData firstResult = null;
            foreach (string file in files)
            {
                Map_MissionData data = LoadMissionFile(file);
                FormUtils.LogRegister("Map_Drawings | mission " + file + " : " + data.PlayerFlights + " player flight(s), " + data.PackageFlights
                    + " package flight(s)" + (data.Error != "" ? " | " + data.Error : ""));

                if (firstResult == null) firstResult = data;
                if (data.Error == "" && data.PlayerFlights > 0)
                {
                    data.Fallback = data != firstResult;
                    return data;
                }
            }
            return firstResult;
        }

        private static Map_MissionData LoadMissionFile(string file)
        {
            var data = new Map_MissionData();
            try
            {
                data.Source = file;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Dictionary<string, object> root = ReadMissionRoot(file);
                long tRead = watch.ElapsedMilliseconds;
                Dictionary<string, object> coalitions = root == null ? null : Sub(root, "coalition");
                if (coalitions == null)
                {
                    data.Error = "The mission file has no coalition table: " + Path.GetFileName(file);
                    return data;
                }

                var flights = new List<Map_Flight>();
                data.StartTime = Num(root, "start_time");

                try
                {
                    Dictionary<string, object> dict = ReadDictionary(file);
                    data.BriefingBlue = BriefingText(root, dict, "descriptionBlueTask");
                    data.BriefingRed = BriefingText(root, dict, "descriptionRedTask");
                    data.BriefingNeutral = BriefingText(root, dict, "descriptionNeutralsTask");
                    data.BriefingGeneral = BriefingText(root, dict, "descriptionText");
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("Map_Drawings | dictionary : " + ex.Message);
                }

                foreach (string side in new[] { "blue", "red" })
                {
                    Dictionary<string, object> countries = Sub(Sub(coalitions, side) ?? new Dictionary<string, object>(), "country");
                    if (countries == null) continue;

                    Color samColor = side == "blue" ? Color.FromArgb(70, 140, 255) : Color.FromArgb(235, 70, 70);

                    foreach (object co in countries.Values)
                    {
                        var country = co as Dictionary<string, object>;
                        if (country == null) continue;

                        Map_Flights.Collect(country, side, flights);

                        AddSamThreats(data, Sub(Sub(country, "vehicle") ?? new Dictionary<string, object>(), "group"), samColor);
                        AddEwrRadars(data, Sub(Sub(country, "vehicle") ?? new Dictionary<string, object>(), "group"), side == "red");
                        AddGround(data, country, side == "red");
                    }
                }

                // Vols joueurs + vols de leur package (Pack N), couleurs et segments
                long tFlights = watch.ElapsedMilliseconds;
                Map_Flights.Finish(data, flights);

                // Hors CampaignMaker, seul le camp du joueur est montré (les éléments sol de l'autre camp restent inconnus)
                if (ParamConf.UserLevel != UserLevel.CampaignMaker)
                {
                    var mySides = new HashSet<string>(data.Flights.Where(fl => fl.IsPlayer).Select(fl => fl.Side));
                    if (mySides.Count > 0)
                    {
                        data.Ground.RemoveAll(u => !mySides.Contains(u.Red ? "red" : "blue"));
                        data.GroundGroups.RemoveAll(u => !mySides.Contains(u.Red ? "red" : "blue"));
                        data.ShipRoutes.RemoveAll(r => !mySides.Contains(r.Text));
                    }
                }
                FormUtils.LogRegister("Map_Drawings | timing " + Path.GetFileName(file) + " : read+parse " + tRead + " ms, flights+SAM " + (tFlights - tRead)
                    + " ms, plans " + (watch.ElapsedMilliseconds - tFlights) + " ms, total " + watch.ElapsedMilliseconds + " ms");
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

        // Choix de l'icône d'après le type (une fois par type, le résultat est gardé)
        private static readonly Dictionary<string, char> KindCache = new Dictionary<string, char>();
        private static readonly System.Text.RegularExpressions.Regex RxInf = new System.Text.RegularExpressions.Regex(@"soldier|infantry|paratrooper|insurgent|manpad|\bAK\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex RxRadar = new System.Text.RegularExpressions.Regex(@"\b(sr|tr|str)\b|radar|\bewr\b|dog ear|snow drift|flat face|big bird|clam shell|land roll|1l13|55g6|fps-|p-19", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex RxArty = new System.Text.RegularExpressions.Regex(@"mlrs|smerch|grad|uragan|urgan|\b2s\d|m-?109|spgh|plz|msta|akatsiya|gvozdika|nona|paladin|howitzer|mortar|hurricane|\bbm-|9k57|hl_b|\bpzh|dana|as-90|t155|scud|iskander|tos-1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex RxAd = new System.Text.RegularExpressions.Regex(@"s-300|s-200|s-75|s-125|s_75|\bsa-?\d|patriot|hawk|hq-|\btor\b|9k\d+|2s6|tunguska|\bosa\b|buk|\bkub\b|2k12|strela|\bigla\b|stinger|avenger|roland|rapier|nasams|gepard|shilka|zsu|zu-?23|vulcan|m163|linebacker|pantsir|\baaa\b|flak|s-60|ks-19|bofors|chaparral|mistral|crotale|\bsnr|\bln\b|launcher|phalanx|2k22|5p85", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex RxArmor = new System.Text.RegularExpressions.Regex(@"t-?55|t-?72|t-?80|t-?90|\bm-?1\b|m1a|abrams|m-?60|leopard|challenger|merkava|chieftain|\btank|bmp|bmd|btr|brdm|\bm-?2\b|bradley|lav-|stryker|marder|warrior|\bmcv|aav|m-?113|mt-?lb|sprut|boxer|\bvab|amx|centauro|ztz|zbd|ztl|tpz|fuchs|wiesel|pt-76|jagdpanzer|\bpz|tigr|cobra", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex RxCarrier = new System.Text.RegularExpressions.Regex(@"cvn|\bcv[_-]|kuznetsov|stennis|vinson|lha|lhd|tarawa|carrier|forrestal|nimitz|essex|truman", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static char GroundKind(string cat, string type, Dictionary<string, object> unit)
        {
            if (cat == "static")
            {
                string c = Str(unit, "category");
                if (c.IndexOf("plane", StringComparison.OrdinalIgnoreCase) >= 0 || c.IndexOf("helicopter", StringComparison.OrdinalIgnoreCase) >= 0) return 'p';
                if (c.IndexOf("heliport", StringComparison.OrdinalIgnoreCase) >= 0 || type.IndexOf("FARP", StringComparison.OrdinalIgnoreCase) >= 0) return 'f';
                return 'b';
            }

            char k;
            string key = cat + "|" + type;
            if (KindCache.TryGetValue(key, out k)) return k;

            if (cat == "ship") k = RxCarrier.IsMatch(type) ? 'k' : 'n';
            else if (RxInf.IsMatch(type)) k = 'i';
            else if (RxRadar.IsMatch(type)) k = 'r';
            else if (RxArty.IsMatch(type)) k = 'g';
            else if (RxAd.IsMatch(type)) k = 'm';
            else if (RxArmor.IsMatch(type)) k = 'a';
            else k = 'u';
            KindCache[key] = k;
            return k;
        }

        // Véhicules, statiques et navires d'un pays : une position par unité, un nom par groupe. Groupes cachés et unités mortes ignorés.
        private static void AddGround(Map_MissionData data, Dictionary<string, object> country, bool red)
        {
            string countryName = Str(country, "name");
            foreach (string cat in new[] { "vehicle", "static", "ship" })
            {
                Dictionary<string, object> groups = Sub(Sub(country, cat) ?? new Dictionary<string, object>(), "group");
                if (groups == null) continue;

                foreach (object go in groups.Values)
                {
                    var group = go as Dictionary<string, object>;
                    Dictionary<string, object> units = group == null ? null : Sub(group, "units");
                    if (units == null || Str(group, "hidden") == "true") continue;

                    if (cat == "ship") AddShipRoute(data, group, red);

                    var gg = new Map_GroundGroup { Name = Str(group, "name"), Red = red, Country = countryName, Category = cat, Src = group };
                    double sx = 0, sy = 0;
                    foreach (object uo in units.Values)
                    {
                        var unit = uo as Dictionary<string, object>;
                        if (unit == null || Str(unit, "dead") == "true") continue;
                        string type = Str(unit, "type");
                        var u = new Map_GroundUnit { X = Num(unit, "x"), Y = Num(unit, "y"), Red = red, Kind = GroundKind(cat, type, unit), Name = Str(unit, "name"), Type = type, Src = unit, Group = gg };
                        u.HalfM = Map_GroundSize.HalfMeters(u.Kind, type);
                        if (unit.ContainsKey("heading")) u.Heading = Num(unit, "heading");
                        data.Ground.Add(u);
                        gg.Units.Add(u);
                        sx += u.X; sy += u.Y;
                    }
                    if (gg.Units.Count > 0)
                    {
                        gg.X = sx / gg.Units.Count; gg.Y = sy / gg.Units.Count;
                        data.GroundGroups.Add(gg);
                    }
                }
            }
        }

        // Trajet prévu d'un groupe de navires : les points de sa route (route.points), en pointillés, avec une flèche de sens
        private static void AddShipRoute(Map_MissionData data, Dictionary<string, object> group, bool red)
        {
            Dictionary<string, object> points = Sub(Sub(group, "route") ?? new Dictionary<string, object>(), "points");
            if (points == null) return;

            var wps = new List<KeyValuePair<int, Dictionary<string, object>>>();
            foreach (KeyValuePair<string, object> kv in points)
            {
                int n;
                var p = kv.Value as Dictionary<string, object>;
                if (p != null && int.TryParse(kv.Key, out n)) wps.Add(new KeyValuePair<int, Dictionary<string, object>>(n, p));
            }
            if (wps.Count < 2) return;
            wps.Sort((a, b) => a.Key.CompareTo(b.Key));

            string name = Str(group, "name");
            Color c = red ? Color.FromArgb(255, 140, 130) : Color.FromArgb(130, 200, 255);
            var line = new Map_Drawing { Name = name, Group = "ships", Kind = "Line", Dashed = true, Thickness = 1.8f, Line = c, Text = red ? "red" : "blue" };

            int longest = 0;
            double longestLen = -1;
            for (int i = 0; i < wps.Count; i++)
            {
                line.Points.Add(new double[] { Num(wps[i].Value, "x"), Num(wps[i].Value, "y") });
                if (i == 0) continue;

                double dx = line.Points[i][0] - line.Points[i - 1][0], dy = line.Points[i][1] - line.Points[i - 1][1];
                double len = dx * dx + dy * dy;
                if (len > longestLen) { longestLen = len; longest = i - 1; }
            }
            data.ShipRoutes.Add(line);

            // Une flèche de sens, au milieu du tronçon le plus long
            var arrow = new Map_Drawing { Name = name, Group = "ships", Kind = "Arrow", Line = c, Text = line.Text };
            arrow.Points.Add(line.Points[longest]);
            arrow.Points.Add(line.Points[longest + 1]);
            data.ShipRoutes.Add(arrow);
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
                    Label = bestName, RangeM = best,
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

        // Portée radar (m) des EWR au sol, d'après les données de DCS (120 km dans le jeu). A MODIFIER ICI si besoin.
        private static readonly object[][] EwrTable =
        {
            new object[] { "1L13 EWR",  "EWR 1L13",  120000.0 },
            new object[] { "55G6 EWR",  "EWR 55G6",  120000.0 },
            new object[] { "FPS-117",   "EWR FPS-117", 120000.0 },
        };

        // Un cercle par radar EWR (unités vivantes, groupes non cachés), couleur du camp
        private static void AddEwrRadars(Map_MissionData data, Dictionary<string, object> groups, bool red)
        {
            if (groups == null) return;
            Color side = red ? Color.FromArgb(255, 140, 130) : Color.FromArgb(130, 200, 255);

            foreach (object go in groups.Values)
            {
                var group = go as Dictionary<string, object>;
                Dictionary<string, object> units = group == null ? null : Sub(group, "units");
                if (units == null || Str(group, "hidden") == "true") continue;

                foreach (object uo in units.Values)
                {
                    var unit = uo as Dictionary<string, object>;
                    if (unit == null || Str(unit, "dead") == "true") continue;

                    string type = Str(unit, "type");
                    foreach (object[] row in EwrTable)
                    {
                        if (type.IndexOf((string)row[0], StringComparison.OrdinalIgnoreCase) < 0) continue;

                        double range = (double)row[2];
                        var circle = new Map_Drawing
                        {
                            Name = Str(group, "name"), Group = "radars", Kind = "Circle", Radius = range, Dashed = true,
                            Label = (string)row[1], RangeM = range,
                            Line = Color.FromArgb(200, side), Fill = Color.FromArgb(14, side), Thickness = 1.4f
                        };
                        circle.Points.Add(new double[] { Num(unit, "x"), Num(unit, "y") });
                        data.Radars.Add(circle);
                        break;
                    }
                }
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
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
            if (v is bool) return (bool)v ? "true" : "false";
            return System.Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v is double) return (double)v;
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
        // Il avance TOUJOURS d'au moins un caractère par tour : un caractère inattendu ne peut plus le bloquer
        // (avant, un "]" ou un texte [[...]] isolé le faisait tourner à l'infini jusqu'à saturer la mémoire).
        private class LuaReader
        {
            private readonly string _s;
            private int _i;
            private readonly Dictionary<object, Dictionary<string, int[]>> _spans;     // (facultatif) pour chaque table : début / fin de la valeur de chaque clé dans le texte

            public LuaReader(string s, int start, Dictionary<object, Dictionary<string, int[]>> spans = null) { _s = s; _i = start; _spans = spans; }

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

            // "[[" ou "[=[" : début d'un texte long Lua (et non d'une clé)
            private bool IsLongString()
            {
                return _s[_i] == '[' && _i + 1 < _s.Length && (_s[_i + 1] == '[' || _s[_i + 1] == '=');
            }

            public object ReadValue()
            {
                Skip();
                if (_i >= _s.Length) return null;

                char c = _s[_i];
                if (c == '{') return ReadTable();
                if (c == '"' || c == '\'') return ReadString(c);
                if (IsLongString()) return ReadLongString();

                int start = _i;
                while (_i < _s.Length && ",;}]\r\n \t".IndexOf(_s[_i]) < 0) _i++;
                if (_i == start) { _i++; return null; }       // caractère inattendu : on le saute

                string w = _s.Substring(start, _i - start);
                if (w == "true") return true;
                if (w == "false") return false;
                double d;
                if (double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                return null;
            }

            private string ReadString(char quote)
            {
                var sb = new StringBuilder();
                _i++;       // guillemet ouvrant
                while (_i < _s.Length && _s[_i] != quote)
                {
                    if (_s[_i] == '\\' && _i + 1 < _s.Length) _i++;
                    sb.Append(_s[_i]);
                    _i++;
                }
                _i++;       // guillemet fermant
                return sb.ToString();
            }

            // [[texte]] ou [==[texte]==]
            private string ReadLongString()
            {
                int j = _i + 1, eq = 0;
                while (j < _s.Length && _s[j] == '=') { eq++; j++; }
                if (j >= _s.Length || _s[j] != '[') { _i++; return ""; }

                string close = "]" + new string('=', eq) + "]";
                int end = _s.IndexOf(close, j + 1, StringComparison.Ordinal);
                if (end < 0) { _i = _s.Length; return ""; }

                string text = _s.Substring(j + 1, end - j - 1);
                _i = end + close.Length;
                return text;
            }

            private Dictionary<string, object> ReadTable()
            {
                var t = new Dictionary<string, object>();
                _i++;       // {
                if (_spans != null) _spans[t] = new Dictionary<string, int[]> { { "{", new[] { _i, _i } } };      // où insérer une clé qui n'existe pas
                int auto = 1;
                while (true)
                {
                    Skip();
                    if (_i >= _s.Length) return t;
                    if (_s[_i] == '}') { _i++; return t; }
                    if (_s[_i] == ',' || _s[_i] == ';') { _i++; continue; }

                    int before = _i;
                    string key;
                    if (_s[_i] == '[' && !IsLongString())
                    {
                        _i++;
                        Skip();
                        object k = ReadValue();
                        key = k is double ? ((double)k).ToString(CultureInfo.InvariantCulture) : (k as string ?? "");
                        Skip();
                        if (_i < _s.Length && _s[_i] == ']') _i++;       // ]
                        Skip();
                        if (_i < _s.Length && _s[_i] == '=') _i++;       // =
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
                    Skip();
                    int valueStart = _i;
                    t[key] = ReadValue();
                    if (_spans != null)
                    {
                        Dictionary<string, int[]> keySpans;
                        if (!_spans.TryGetValue(t, out keySpans)) { keySpans = new Dictionary<string, int[]>(); _spans[t] = keySpans; }
                        keySpans[key] = new[] { valueStart, _i };
                    }

                    if (_i <= before) _i = before + 1;      // garde-fou : rien n'a avancé, on force
                }
            }
        }
    }

    // Pistes des bases pour la carte. Ordre : 1) réglage de l'utilisateur (section [runways] de dce_data_user.ini), 2) db_airbases,
    // 3) données publiques OurAirports (runways.csv, téléchargé une fois dans le cache) : les pistes qui passent à moins de 2 km du point de la base.
    // Les pistes publiques ont une position réelle : elles sont donc bien placées que le point de la base soit le milieu de la piste, un bout, ou autre chose.
    internal static class Map_RunwayData
    {
        private const string RunwaysUrl = "https://davidmegginson.github.io/ourairports-data/runways.csv";
        private const double MatchRadiusM = 2000;

        internal class PubRunway { public string Ident = ""; public double LatA, LonA, LatB, LonB, LengthM; }

        private static readonly object _lock = new object();
        private static List<PubRunway> _public;
        private static bool _downloading, _downloadFailed;
        private static readonly Dictionary<string, Map_Projection> _proj = new Dictionary<string, Map_Projection>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<Map_Runway>> _effective = new Dictionary<string, List<Map_Runway>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _theatre = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Changements faits sur la carte mais pas encore écrits : ils ne le sont qu'au clic sur Save (CommitPending)
        private class PendingEntry { public double BX, BY; public List<Map_Runway> List; }       // List = null : retour aux pistes d'origine
        private static readonly Dictionary<string, PendingEntry> _pending = new Dictionary<string, PendingEntry>(StringComparer.OrdinalIgnoreCase);
        private static bool _anchorPending, _sourcePending;
        private static string _pendingAnchorTheatre = "", _pendingAnchor = "auto";
        private static string _pendingSourceTheatre = "", _pendingSource = "db";
        private static readonly Dictionary<string, string> _measured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Le fichier public vient d'être téléchargé (appelé depuis un thread de fond) : la carte peut recalculer ses pistes
        public static event Action Ready;

        internal static string RunwaysFile { get { return Path.Combine(Map_Web.CacheRoot, "runways.csv"); } }

        // Pistes retenues pour une base (après Fill) et leur origine : "your setting" / "db_airbases" / "public data (OurAirports)"
        public static List<Map_Runway> Get(string baseName, out string source)
        {
            source = "";
            List<Map_Runway> l;
            if (baseName != null && _effective.TryGetValue(baseName, out l))
            {
                source = _source[baseName];
                return l;
            }
            return null;
        }

        private static void Remember(Map_Base b, string source)
        {
            _effective[b.Name] = b.Runways;
            _source[b.Name] = source;
        }

        // Nom du théâtre de la campagne (lu une seule fois)
        public static string TheatreOf(string campaign)
        {
            string t;
            if (_theatre.TryGetValue(campaign, out t)) return t;

            Map_Projection p = Map_Web.KnownProjection(campaign, out t);
            t = t ?? "";
            _theatre[campaign] = t;
            _proj[campaign] = p;
            return t;
        }

        public static bool HasPending { get { return _pending.Count > 0 || _anchorPending || _sourcePending; } }

        // Règle mesurée pendant le calage de la carte web (utilisée quand rien n'est réglé pour ce théâtre)
        public static void SetMeasured(string theatre, string rule)
        {
            if (!string.IsNullOrEmpty(theatre)) _measured[theatre] = rule ?? "";
        }

        // D'où viennent les pistes de la carte : "db" (db_airbases, avec la règle de position) ou "web" (données publiques d'abord, db_airbases en secours)
        public static string GetSource(string theatre)
        {
            theatre = theatre ?? "";
            if (_sourcePending && string.Equals(_pendingSourceTheatre, theatre, StringComparison.OrdinalIgnoreCase)) return _pendingSource;

            string v;
            if (theatre != "" && Map_Data.Section("runway_source").TryGetValue(theatre, out v) && (v ?? "").Trim().ToLowerInvariant() == "web") return "web";
            return "db";
        }

        public static void SetPendingSource(string theatre, string source)
        {
            _sourcePending = true;
            _pendingSourceTheatre = theatre ?? "";
            _pendingSource = source == "web" ? "web" : "db";
        }

        // Bouton "pistes du web" de la carte : web = on ignore db_airbases et les réglages à la main (au prochain Save, ils sont remplacés par le web) ;
        // web = false : retour aux données locales (les retours en attente demandés par le bouton sont annulés)
        public static void SetWebView(string theatre, bool web, IEnumerable<string> baseNames)
        {
            SetPendingSource(theatre, web ? "web" : "db");
            if (web) { ResetSettingsOf(baseNames); return; }
            foreach (string n in baseNames)
            {
                PendingEntry pe;
                if (_pending.TryGetValue(n, out pe) && pe.List == null) _pending.Remove(n);
            }
        }

        // Retour aux pistes d'origine pour ces bases (réglages enregistrés ou en attente : tous annulés au prochain Save)
        public static void ResetSettingsOf(IEnumerable<string> baseNames)
        {
            Dictionary<string, string> saved = Map_Data.Section("runways");
            foreach (string n in baseNames)
                if (saved.ContainsKey(n) || _pending.ContainsKey(n))
                    _pending[n] = new PendingEntry { List = null };
        }

        // list = null : on veut revenir aux pistes d'origine. La liste est copiée (position absolue).
        public static void SetPending(string baseName, double baseX, double baseY, List<Map_Runway> list)
        {
            _pending[baseName] = new PendingEntry { BX = baseX, BY = baseY, List = list == null ? null : Copy(list) };
        }

        public static void SetPendingAnchor(string theatre, string anchor)
        {
            _anchorPending = true;
            _pendingAnchorTheatre = theatre ?? "";
            _pendingAnchor = anchor ?? "auto";
        }

        public static void DiscardPending()
        {
            _pending.Clear();
            _anchorPending = false;
            _sourcePending = false;
        }

        // Écrit tout ce qui est en attente dans dce_data_user.ini (clic sur Save)
        public static void CommitPending()
        {
            foreach (KeyValuePair<string, PendingEntry> kv in new List<KeyValuePair<string, PendingEntry>>(_pending))
                SaveUser(kv.Key, kv.Value.BX, kv.Value.BY, kv.Value.List);
            _pending.Clear();

            if (_anchorPending)
            {
                if (_pendingAnchorTheatre != "")
                    Map_Data.SetUser("runway_anchor", _pendingAnchorTheatre, _pendingAnchor == "auto" ? null : _pendingAnchor);
                _anchorPending = false;
            }
            if (_sourcePending)
            {
                if (_pendingSourceTheatre != "")
                    Map_Data.SetUser("runway_source", _pendingSourceTheatre, _pendingSource == "web" ? "web" : null);
                _sourcePending = false;
            }
        }

        private static List<Map_Runway> Copy(List<Map_Runway> src)
        {
            return src.Select(r => new Map_Runway { Hdg = r.Hdg, Length = r.Length, X = r.X, Y = r.Y }).ToList();
        }

        // Réglage fait pour ce théâtre : "auto" (rien de réglé) ou middle / start / east / west
        public static string GetAnchorSetting(string theatre)
        {
            theatre = theatre ?? "";
            if (_anchorPending && string.Equals(_pendingAnchorTheatre, theatre, StringComparison.OrdinalIgnoreCase)) return _pendingAnchor;

            string v;
            if (theatre != "" && Map_Data.Section("runway_anchor").TryGetValue(theatre, out v))
            {
                v = (v ?? "").Trim().ToLowerInvariant();
                if (v == "middle" || v == "start" || v == "east" || v == "west") return v;
            }
            return "auto";
        }

        // Règle de position du point (x, y) de db_airbases sur la piste, pour tout le théâtre : middle, start, east ou west.
        // Ordre : réglage (fichier de données ou en attente) > mesure faite pendant le calage de la carte web > middle
        public static string GetAnchor(string theatre)
        {
            string s = GetAnchorSetting(theatre);
            if (s != "auto") return s;

            string m;
            if (!string.IsNullOrEmpty(theatre) && _measured.TryGetValue(theatre, out m) && m != "") return m;
            return "middle";
        }

        // Phrase pour la fenêtre des pistes : règle appliquée en ce moment et d'où elle vient
        public static string AnchorStatus(string theatre)
        {
            string s = GetAnchorSetting(theatre);
            if (s != "auto") return "Applied now: " + s + " (data file or your choice).";

            string m;
            if (!string.IsNullOrEmpty(theatre) && _measured.TryGetValue(theatre, out m) && m != "") return "Applied now: " + m + " (measured on the web map calibration).";
            return "Applied now: middle (nothing set, nothing measured yet).";
        }

        // Pistes de db_airbases dont le point (x, y) n'est pas le milieu : on retrouve le milieu.
        // start : le point est le début de la piste (la piste part de là dans le sens de son cap) ; east / west : bout est / ouest (cap ramené entre 0 et 180).
        // Une piste sans position (0 / 0) part du point de la base.
        private static void ApplyAnchor(Map_Base b, string anchor)
        {
            foreach (Map_Runway r in b.Runways)
            {
                double px = (r.X == 0 && r.Y == 0) ? b.X : r.X, py = (r.X == 0 && r.Y == 0) ? b.Y : r.Y;
                double half = r.Length / 2;
                double hRaw = r.Hdg * Math.PI / 180.0;
                double hMod = (((r.Hdg % 180) + 180) % 180) * Math.PI / 180.0;

                if (anchor == "start") { r.X = px + half * Math.Cos(hRaw); r.Y = py + half * Math.Sin(hRaw); }
                else if (anchor == "east") { r.X = px - half * Math.Cos(hMod); r.Y = py - half * Math.Sin(hMod); }
                else if (anchor == "west") { r.X = px + half * Math.Cos(hMod); r.Y = py + half * Math.Sin(hMod); }
            }
        }

        // proj : projection de la carte de fond si elle existe (sinon celle du théâtre)
        // Ordre pour chaque base : 1) son réglage (en attente ou enregistré)  2) selon la source choisie pour la carte :
        //   "db"  : db_airbases avec la règle de position, sinon les données publiques ;
        //   "web" : les données publiques d'abord, sinon db_airbases avec la règle de position.
        public static void Fill(string campaign, List<Map_Base> bases, Map_Projection proj)
        {
            _effective.Clear();
            _source.Clear();
            Dictionary<string, string> user = Map_Data.Section("runways");
            string theatre = TheatreOf(campaign);
            string anchor = GetAnchor(theatre);
            bool web = GetSource(theatre) == "web";
            var rest = new List<Map_Base>();

            foreach (Map_Base b in bases)
            {
                PendingEntry pe;
                string text;
                if (_pending.TryGetValue(b.Name, out pe))
                {
                    if (pe.List != null && pe.List.Count > 0) { b.Runways = Copy(pe.List); Remember(b, "your setting (not saved yet: click Save)"); continue; }
                    // retour aux pistes d'origine demandé : on ignore le réglage enregistré
                }
                else if (user.TryGetValue(b.Name, out text))
                {
                    List<Map_Runway> u = ParseUser(text, b);
                    if (u.Count > 0) { b.Runways = u; Remember(b, "your setting"); continue; }
                }
                rest.Add(b);
            }

            bool needPublic = rest.Any(b => !IsShipOrFarp(b) && (web || b.Runways.Count == 0));
            List<PubRunway> pub = null;
            if (needPublic)
            {
                if (!File.Exists(RunwaysFile)) StartDownload();
                else pub = All();
                if (proj == null) _proj.TryGetValue(campaign, out proj);
            }
            bool pubOk = pub != null && pub.Count > 0 && proj != null;

            foreach (Map_Base b in rest)
            {
                if (IsShipOrFarp(b))        // porte-avions, FARP : ni règle de position, ni pistes du web
                {
                    if (b.Runways.Count > 0) Remember(b, "db_airbases");
                    continue;
                }
                if (web && pubOk && FillFromPublic(b, pub, proj)) continue;

                if (b.Runways.Count > 0)
                {
                    if (anchor != "middle") ApplyAnchor(b, anchor);
                    Remember(b, "db_airbases");
                    continue;
                }
                if (pubOk) FillFromPublic(b, pub, proj);
            }
        }

        private static bool IsShipOrFarp(Map_Base b)
        {
            return b.IsFarp || Map_UI_View.IsCarrier(b);
        }

        // Pistes publiques qui passent à moins de 2 km du point de la base (les 4 plus proches). false = aucune.
        private static bool FillFromPublic(Map_Base b, List<PubRunway> pub, Map_Projection proj)
        {
            double lat, lon;
            Map_Web.DcsToLatLon(proj, b.X, b.Y, out lat, out lon);

            var found = new List<KeyValuePair<double, Map_Runway>>();
            foreach (PubRunway r in pub)
            {
                if (Math.Abs((r.LatA + r.LatB) / 2 - lat) > 0.06 || Math.Abs((r.LonA + r.LonB) / 2 - lon) > 0.09) continue;     // trop loin (tri rapide)

                double ax, ay, bx, by;
                Map_Web.LatLonToDcs(proj, r.LatA, r.LonA, out ax, out ay);
                Map_Web.LatLonToDcs(proj, r.LatB, r.LonB, out bx, out by);
                double dist = SegmentDistance(b.X, b.Y, ax, ay, bx, by);
                if (dist > MatchRadiusM) continue;

                double hdg = Math.Atan2(by - ay, bx - ax) * 180.0 / Math.PI;        // x = nord, y = est : cap = atan2(est, nord)
                if (hdg < 0) hdg += 360;
                double len = r.LengthM > 100 ? r.LengthM : Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                found.Add(new KeyValuePair<double, Map_Runway>(dist, new Map_Runway { Hdg = hdg, Length = len, X = (ax + bx) / 2, Y = (ay + by) / 2 }));
            }
            if (found.Count == 0) return false;

            found.Sort((p, q) => p.Key.CompareTo(q.Key));
            b.Runways = found.Take(4).Select(p => p.Value).ToList();
            Remember(b, "public data (OurAirports)");
            return true;
        }

        private static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay;
            double l2 = vx * vx + vy * vy;
            double t = l2 < 1 ? 0 : ((px - ax) * vx + (py - ay) * vy) / l2;
            t = Math.Max(0, Math.Min(1, t));
            double cx = ax + t * vx, cy = ay + t * vy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        // Pistes de la base avec une position absolue (centre) ; les doublons (les deux sens d'une même piste) sont regroupés.
        // Cap ramené entre 0 et 180 : une piste n'a pas de sens.
        public static List<Map_Runway> Merged(Map_Base b)
        {
            var list = new List<Map_Runway>();
            foreach (Map_Runway rw in b.Runways)
            {
                double x = rw.X, y = rw.Y;
                if (x == 0 && y == 0) { x = b.X; y = b.Y; }
                double h = ((rw.Hdg % 180) + 180) % 180;

                bool merged = false;
                foreach (Map_Runway s in list)
                {
                    double dh = Math.Abs(s.Hdg - h);
                    if (dh > 90) dh = 180 - dh;
                    if (dh >= 8) continue;

                    double a = s.Hdg * Math.PI / 180.0;
                    double perp = Math.Abs(-Math.Sin(a) * (x - s.X) + Math.Cos(a) * (y - s.Y));
                    if (perp < 60)
                    {
                        if (rw.Length > s.Length) s.Length = rw.Length;
                        merged = true;
                        break;
                    }
                }
                if (!merged && list.Count < 6)
                    list.Add(new Map_Runway { Hdg = h, Length = rw.Length, X = x, Y = y });
            }
            return list;
        }

        // Écrit dans le texte du fichier : "cap ; longueur ; décalage nord ; décalage est | ..."  (décalage = du point de la base au milieu de la piste, en mètres)
        // -> pistes à position absolue. Le décalage est facultatif.
        private static List<Map_Runway> ParseUser(string text, Map_Base b)
        {
            var list = new List<Map_Runway>();
            foreach (string part in text.Split('|'))
            {
                string[] p = part.Split(';');
                if (p.Length < 2) continue;

                double h, l, dx = 0, dy = 0;
                if (!Num(p[0], out h) || !Num(p[1], out l) || l <= 0) continue;
                if (p.Length >= 4) { Num(p[2], out dx); Num(p[3], out dy); }
                list.Add(new Map_Runway { Hdg = h, Length = l, X = b.X + dx, Y = b.Y + dy });
            }
            return list;
        }

        // Réglage de l'utilisateur ; liste vide ou null = on retire le réglage (retour aux données d'origine).
        // runways : position absolue (X, Y = milieu de la piste) ; on enregistre le décalage par rapport au point de la base.
        public static void SaveUser(string baseName, double baseX, double baseY, List<Map_Runway> runways)
        {
            string value = null;
            if (runways != null && runways.Count > 0)
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                value = string.Join(" | ", runways.Select(r =>
                    r.Hdg.ToString("0.#", c) + " ; " + r.Length.ToString("0", c) + " ; "
                    + ((r.X == 0 && r.Y == 0) ? 0 : r.X - baseX).ToString("0", c) + " ; "
                    + ((r.X == 0 && r.Y == 0) ? 0 : r.Y - baseY).ToString("0", c)).ToArray());
            }
            Map_Data.SetUser("runways", baseName, value);

            if (value != null)
            {
                _effective[baseName] = runways;
                _source[baseName] = "your setting";
            }
        }

        // Toutes les pistes publiques (liste vide si le fichier n'est pas là)
        internal static List<PubRunway> All()
        {
            lock (_lock)
            {
                LoadPublic();
                return _public ?? new List<PubRunway>();
            }
        }

        private static void LoadPublic()
        {
            if (_public != null || !File.Exists(RunwaysFile)) return;

            var list = new List<PubRunway>();
            try
            {
                using (var sr = new StreamReader(RunwaysFile, Encoding.UTF8))
                {
                    string header = sr.ReadLine();
                    if (header == null) return;
                    List<string> cols = Map_Web.ParseCsvLine(header);
                    int iLen = cols.IndexOf("length_ft"), iClosed = cols.IndexOf("closed"), iId = cols.IndexOf("airport_ident");
                    int iLa = cols.IndexOf("le_latitude_deg"), iLo = cols.IndexOf("le_longitude_deg");
                    int iHa = cols.IndexOf("he_latitude_deg"), iHo = cols.IndexOf("he_longitude_deg");
                    if (iLa < 0 || iLo < 0 || iHa < 0 || iHo < 0) return;
                    int max = Math.Max(Math.Max(iLa, iLo), Math.Max(iHa, iHo));

                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        List<string> f = Map_Web.ParseCsvLine(line);
                        if (f.Count <= max) continue;
                        if (iClosed >= 0 && iClosed < f.Count && f[iClosed] == "1") continue;

                        double la, lo, ha, ho, ft = 0;
                        if (!Num(f[iLa], out la) || !Num(f[iLo], out lo) || !Num(f[iHa], out ha) || !Num(f[iHo], out ho)) continue;
                        if (iLen >= 0 && iLen < f.Count) Num(f[iLen], out ft);
                        list.Add(new PubRunway
                        {
                            Ident = (iId >= 0 && iId < f.Count) ? f[iId].Trim() : "",
                            LatA = la, LonA = lo, LatB = ha, LonB = ho, LengthM = ft * 0.3048
                        });
                    }
                }
                _public = list;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_RunwayData | lecture : " + ex.Message);
            }
        }

        private static bool Num(string s, out double v)
        {
            return double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // Télécharge runways.csv s'il manque (appelé depuis le thread de fond du calage de la carte). false = pas de fichier.
        internal static bool EnsureFile(IProgress<string> progress)
        {
            if (File.Exists(RunwaysFile)) return true;
            try
            {
                if (progress != null) progress.Report("Downloading the runway list (only once)...");
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                Directory.CreateDirectory(Map_Web.CacheRoot);
                Map_Web.Download(RunwaysUrl, RunwaysFile);
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_RunwayData | téléchargement : " + ex.Message);
                return false;
            }
        }

        private static void StartDownload()
        {
            if (_downloading || _downloadFailed) return;
            _downloading = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (!EnsureFile(null)) { _downloadFailed = true; return; }
                    Action r = Ready;
                    if (r != null) r();
                }
                finally
                {
                    _downloading = false;
                }
            });
        }
    }
}
