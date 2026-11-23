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
