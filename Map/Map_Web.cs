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
    // Fond de carte pris sur le web, calé automatiquement sur les bases qui ont un code ICAO.
    //  1) liste des aéroports du monde (OurAirports, domaine public) : téléchargée une seule fois
    //  2) on retrouve chaque base par son code ICAO -> position réelle (lat/lon)
    //  3) on cherche la projection qui superpose le mieux positions réelles et positions DCS
    //  4) on télécharge les tuiles OpenStreetMap utiles (peu nombreuses, gardées en cache)
    // Tout est gardé dans Documents\DCE_Manager\MapCache : rien n'est retéléchargé ensuite.
    internal static partial class Map_Web
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
    }
}
