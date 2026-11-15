using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Briefing d'un vol : le texte de la mission (dictionary) contient une "BRIEFING Part" par vol joueur, mises bout à bout.
    // Ici on retrouve la partie du vol choisi, et on la découpe en sections pour la datacard.
    internal static class Map_Briefing
    {
        private static readonly Regex RegexPart = new Regex(@"_{10,}\s*BRIEFING Part:\s*_{10,}", RegexOptions.IgnoreCase);
        private static readonly Regex RegexFlightName = new Regex(@"^\s*Pack\s+(\d+)\s+-\s+(.+)\s+-\s+(.+?)\s*$", RegexOptions.IgnoreCase);   // " - " avec espaces : le nom d'escadron peut contenir des tirets (VFA-106, 450 M-E/P-2)
        private static readonly Regex RegexPackage = new Regex(@"Package:\s*(\d+)", RegexOptions.IgnoreCase);

        // Titres de sections connus d'une partie de briefing (dans l'ordre où DCE les écrit)
        public static readonly string[] Sections = { "Target:", "Package:", "Flight:", "Bullseye:", "Flightplan:", "Radio Navigation:", "Communication:", "Meteo:" };

        // La partie du briefing qui concerne ce vol ("" si le texte n'en a pas pour lui).
        // Reconnaissance : nom du vol "Pack 1 - 450 M-E/P-2 - Strike 1" -> package 1, escadron "450 M-E/P-2", mission "Strike".
        public static string PartFor(Map_Flight f, string text)
        {
            if (f == null || string.IsNullOrEmpty(text)) return "";

            Match nm = RegexFlightName.Match(f.Name);
            if (!nm.Success) return "";
            string pack = nm.Groups[1].Value;
            string squad = nm.Groups[2].Value.Trim();
            string task = Regex.Replace(nm.Groups[3].Value, @"\s*\d+$", "").Trim();      // "Strike 1" -> "Strike"

            string[] parts = RegexPart.Split(text);
            var found = new List<string>();
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i].Trim('\n', '\r', ' ');

                // 1re ligne utile = titre : "____ SA342M 450 M-E/P-2 Strike ____"
                string title = part.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Replace("_", "").Trim() != "") ?? "";
                if (title.IndexOf(squad, StringComparison.OrdinalIgnoreCase) < 0 || title.IndexOf(task, StringComparison.OrdinalIgnoreCase) < 0) continue;

                Match pm = RegexPackage.Match(part);
                if (!pm.Success || pm.Groups[1].Value != pack) continue;
                found.Add(CleanPart(part));
            }
            return string.Join("\n\n", found);
        }

        // Ligne du vol dans le tableau "Air Tasking Order" (Pack / Sorties / Type / Mission / TOT), avec son en-tête.
        // Sert pour les vols sans partie de briefing (vols IA). "" si le vol n'y est pas.
        public static string AtoLine(Map_Flight f, string text)
        {
            if (f == null || f.Package < 0 || string.IsNullOrEmpty(text)) return "";

            string header = "", samePack = "", sameType = "";
            bool inTable = false;
            foreach (string raw in text.Split('\n'))
            {
                string l = raw.TrimEnd('\r');
                if (l.TrimStart().StartsWith("Pack", StringComparison.OrdinalIgnoreCase) && l.IndexOf("Sorties", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    header = l.Trim();
                    inTable = true;
                    continue;
                }
                if (!inTable) continue;
                if (l.Trim() == "") { inTable = false; continue; }

                Match m = Regex.Match(l, @"^\s*(\d+)\s+\d+\s+(\S+)");
                if (!m.Success || m.Groups[1].Value != f.Package.ToString()) continue;
                if (samePack == "") samePack = l.Trim();
                if (sameType == "" && m.Groups[2].Value.Equals(f.Type, StringComparison.OrdinalIgnoreCase)) sameType = l.Trim();
            }
            string line = sameType != "" ? sameType : samePack;
            return line == "" ? "" : header + "\n" + line;
        }

        // Enlève les lignes de soulignés, garde le titre
        private static string CleanPart(string part)
        {
            var lines = new List<string>();
            foreach (string raw in part.Split('\n'))
            {
                string l = raw.TrimEnd('\r');
                string bare = l.Replace("_", "").Trim();
                if (l.Trim().Length > 5 && bare == "") continue;                // ligne de soulignés
                if (l.Trim().StartsWith("_") && bare != "") l = bare.ToUpperInvariant();   // ligne de titre
                lines.Add(l.TrimEnd());
            }
            return string.Join("\n", lines).Trim('\n');
        }

        // Texte d'une section (de son titre jusqu'au titre suivant), ou "" ; sans la ligne du titre
        public static string Section(string part, string title)
        {
            if (string.IsNullOrEmpty(part)) return "";
            string[] lines = part.Split('\n');
            var result = new List<string>();
            bool inside = false;
            foreach (string raw in lines)
            {
                string l = raw.TrimEnd('\r');
                string t = l.Trim();
                bool isTitle = Sections.Any(s => t.Equals(s, StringComparison.OrdinalIgnoreCase));
                if (isTitle) { inside = t.Equals(title, StringComparison.OrdinalIgnoreCase); continue; }
                if (inside) result.Add(l);
            }
            return string.Join("\n", result).Trim('\n', ' ');
        }
    }

    // Armement lu dans la charge (payload) d'un avion du vol : stations -> CLSID, carburant, leurres, canon
    internal class Map_Loadout
    {
        public string Name = "";        // nom du loadout, s'il est resté dans le fichier mission (sinon vide)
        public SortedDictionary<int, string> Pylons = new SortedDictionary<int, string>();
        public double Fuel = -1, Flare = -1, Chaff = -1, Gun = -1;

        public static Map_Loadout Of(Map_Flight f)
        {
            Dictionary<string, object> units = Sub(f.Source, "units");
            if (units == null) return null;

            // L'avion du joueur d'abord, sinon le premier
            Dictionary<string, object> pick = null;
            foreach (object uo in units.Values)
            {
                var u = uo as Dictionary<string, object>;
                if (u == null) continue;
                if (pick == null) pick = u;
                object sk;
                if (u.TryGetValue("skill", out sk) && (string.Equals(sk as string, "Player", StringComparison.OrdinalIgnoreCase) || string.Equals(sk as string, "Client", StringComparison.OrdinalIgnoreCase)))
                { pick = u; break; }
            }
            Dictionary<string, object> payload = Sub(pick, "payload");
            if (payload == null) return null;

            var lo = new Map_Loadout();
            Dictionary<string, object> pylons = Sub(payload, "pylons");
            if (pylons != null)
                foreach (KeyValuePair<string, object> kv in pylons)
                {
                    int n;
                    var st = kv.Value as Dictionary<string, object>;
                    object cl;
                    if (st == null || !int.TryParse(kv.Key, out n) || !st.TryGetValue("CLSID", out cl)) continue;
                    string id = cl as string;
                    if (!string.IsNullOrEmpty(id)) lo.Pylons[n] = id;
                }
            lo.Name = FindName(payload, pick, f.Source);
            lo.Fuel = Num(payload, "fuel");
            lo.Flare = Num(payload, "flare");
            lo.Chaff = Num(payload, "chaff");
            lo.Gun = Num(payload, "gun");
            return lo;
        }

        // DCS n'écrit normalement pas le nom du loadout dans la mission : on regarde les clés possibles (charge, avion, groupe)
        private static readonly string[] NameKeys = { "name", "loadout", "loadoutName", "payloadName", "payload_name", "Loadout", "pylonsName" };

        private static string FindName(Dictionary<string, object> payload, Dictionary<string, object> unit, Dictionary<string, object> group)
        {
            foreach (Dictionary<string, object> d in new[] { payload, unit, group })
            {
                if (d == null) continue;
                foreach (string k in NameKeys)
                {
                    if (k == "name" && d != payload) continue;          // avion / groupe : "name" = nom de l'unité, pas du loadout
                    object v;
                    string t = d.TryGetValue(k, out v) ? v as string : null;
                    if (!string.IsNullOrWhiteSpace(t)) return t.Trim();
                }
            }
            // Rien trouvé : on note les clés de la charge dans le journal (une fois) pour savoir où chercher
            if (!_logged)
            {
                _logged = true;
                DCE_Manager.Utils.FormUtils.LogRegister("Map_Loadout | no loadout name found. payload keys: " + string.Join(",", payload.Keys)
                    + " | unit keys: " + (unit == null ? "-" : string.Join(",", unit.Keys)));
            }
            return "";
        }
        private static bool _logged;

        private static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        private static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v is double ? (double)v : -1;
        }

        // Nom lisible d'un CLSID. Les codes de type GUID ne sont pas traduisibles sans la base de données de DCS : on garde un code court.
        public static string Pretty(string clsid)
        {
            string s = clsid.Trim().Trim('{', '}');
            if (Regex.IsMatch(s, @"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$"))
                return "store " + s.Substring(0, 8);
            int star = s.LastIndexOf('*');
            if (star >= 0 && star + 1 < s.Length) s = s.Substring(star + 1);         // "LAU-115_2*LAU-127_AIM-120C" -> "LAU-127_AIM-120C"
            s = Regex.Replace(s, @"^LAU-\d+[A-Z]?(/[A-Z])?_", "");
            return s.Replace('_', ' ').Trim();
        }

        // Famille de l'emport : décide de la forme dessinée
        public static string Kind(string clsid)
        {
            string u = clsid.ToUpperInvariant();
            if (u.Contains("TANK") || u.Contains("PTB") || u.Contains("FUEL") || u.Contains("DROP")) return "tank";
            if (u.Contains("POD") || u.Contains("ECM") || u.Contains("LANTIRN") || u.Contains("TGP") || u.Contains("ATFLIR") || u.Contains("LITENING")
                || u.Contains("SNIPER") || u.Contains("AN/") || u.Contains("JAMMER") || u.Contains("DEFLECTOR") || u.Contains("SMOKE")) return "pod";
            if (u.Contains("GBU") || u.Contains("CBU") || u.Contains("BLU") || u.Contains("MK-8") || u.Contains("MK_8") || u.Contains("MK8") || u.Contains("FAB")
                || u.Contains("BETAB") || u.Contains("ROCKEYE") || u.Contains("M117") || u.Contains("BDU") || u.Contains("BOMB") || u.Contains("KAB") || u.Contains("SAB")) return "bomb";
            if (u.Contains("S-8") || u.Contains("S-5") || u.Contains("S-13") || u.Contains("S-24") || u.Contains("UB-") || u.Contains("HYDRA") || u.Contains("ROCKET")
                || u.Contains("LAU-68") || u.Contains("LAU-61") || u.Contains("B-8") || u.Contains("B-13")) return "rocket";
            if (u.Contains("AIM") || u.Contains("AGM") || u.Contains("R-") || u.Contains("HOT") || u.Contains("MISSILE") || u.Contains("PHOENIX") || u.Contains("MAVERICK")
                || u.Contains("HELLFIRE") || u.Contains("MICA") || u.Contains("MAGIC") || u.Contains("KH-") || u.Contains("X-") || u.Contains("ATAKA") || u.Contains("VIKHR")) return "missile";
            return "other";
        }
    }

    // Datacard : une image (type carte de vol imprimable) qui résume le vol : package, communications, radio-navigation,
    // plan de vol, armement et météo. Les textes viennent du briefing de la mission, le plan de vol et l'armement de la mission elle-même.
    internal static class Map_Datacard
    {
        private static readonly Color Bar = Color.FromArgb(200, 200, 200), Grid = Color.FromArgb(40, 40, 40), Soft = Color.FromArgb(240, 240, 240);
        private const int W = 1100, M = 16;

        public static Bitmap Render(Map_Flight f, double missionStart, string part, Map_Projection proj)
        {
            int height;
            using (var dry = new Bitmap(10, 10))
            using (Graphics g = Graphics.FromImage(dry))
                height = Draw(g, f, missionStart, part, proj);

            var bmp = new Bitmap(W, height + M);
            using (Graphics g = Graphics.FromImage(bmp))
                Draw(g, f, missionStart, part, proj);
            return bmp;
        }

        private static string[] Lines(string part, string title, int max)
        {
            string body = Map_Briefing.Section(part, title);
            if (body == "") return new string[0];
            return body.Split('\n').Select(l => l.TrimEnd('\r', ' ')).Where(l => l.Trim() != "").Take(max).ToArray();
        }

        // Une ligne de la radio-navigation : "Base: Larnaca Airbase VOR: 112.80 NDB: 432 ILS: RWY 228/110.80"
        private class NavLine { public string Role = "", Name = "", Tacan = "", Vor = "", Ndb = "", Ils = ""; }

        private static readonly Regex RegexNavKey = new Regex(@"\b(TACAN|VOR|NDB|ILS|ICLS):\s*", RegexOptions.IgnoreCase);

        private static List<NavLine> ParseNav(string[] lines)
        {
            var list = new List<NavLine>();
            foreach (string raw in lines)
            {
                int colon = raw.IndexOf(':');
                if (colon <= 0) continue;
                var n = new NavLine { Role = raw.Substring(0, colon).Trim() };
                string rest = raw.Substring(colon + 1).Trim();

                if (n.Role.Equals("Tanker", StringComparison.OrdinalIgnoreCase) || n.Role.Equals("AWACS", StringComparison.OrdinalIgnoreCase))
                {
                    // "17Y S-3B Tanker Shell41" : le TACAN est le premier mot
                    Match tm = Regex.Match(rest, @"^(\d{1,3}[XY])\s+(.*)$", RegexOptions.IgnoreCase);
                    if (tm.Success) { n.Tacan = tm.Groups[1].Value.ToUpperInvariant(); n.Name = tm.Groups[2].Value.Trim(); }
                    else n.Name = rest;
                    list.Add(n);
                    continue;
                }

                MatchCollection keys = RegexNavKey.Matches(rest);
                n.Name = (keys.Count > 0 ? rest.Substring(0, keys[0].Index) : rest).Trim();
                for (int i = 0; i < keys.Count; i++)
                {
                    int s = keys[i].Index + keys[i].Length;
                    int e = i + 1 < keys.Count ? keys[i + 1].Index : rest.Length;
                    string v = rest.Substring(s, e - s).Trim();
                    switch (keys[i].Groups[1].Value.ToUpperInvariant())
                    {
                        case "TACAN": n.Tacan = v; break;
                        case "VOR": n.Vor = v; break;
                        case "NDB": n.Ndb = v; break;
                        default: n.Ils = (n.Ils == "" ? "" : n.Ils + "  ") + keys[i].Groups[1].Value.ToUpperInvariant() + " " + v; break;
                    }
                }
                list.Add(n);
            }
            return list;
        }

        // Coupe une ligne de tableau texte sur les groupes de 2 espaces ou plus
        private static string[] Cells(string line)
        {
            return Regex.Split(line.Trim(), @"\s{2,}").Where(s => s != "").ToArray();
        }

        // Coordonnées au format DCS : N 34°52.1'  E 033°37.5'
        private static string LatLon(Map_Projection proj, Map_Waypoint w)
        {
            if (proj == null) return "-";
            try
            {
                double lat, lon;
                Map_Web.DcsToLatLon(proj, w.X, w.Y, out lat, out lon);
                return Dm(lat, 'N', 'S', 2) + "  " + Dm(lon, 'E', 'W', 3);
            }
            catch { return "-"; }
        }

        private static string Dm(double v, char pos, char neg, int degDigits)
        {
            char h = v >= 0 ? pos : neg;
            v = Math.Abs(v);
            int d = (int)v;
            double m = (v - d) * 60.0;
            if (m >= 59.95) { d++; m = 0; }
            return h + " " + d.ToString(new string('0', degDigits), CultureInfo.InvariantCulture) + "°" + m.ToString("00.0", CultureInfo.InvariantCulture) + "'";
        }

        // Tableau à grille : en-têtes sur fond gris, une ligne par entrée ; widths = largeurs relatives des colonnes
        private static int Table(Graphics g, string[] heads, List<string[]> rows, float[] widths, int x, int y, int width,
            Font fHead, Font fCell, Pen grid, StringFormat clip, bool boldFirstRow)
        {
            const int rowH = 20;
            float total = widths.Sum();
            var xs = new float[widths.Length + 1];
            xs[0] = x;
            for (int i = 0; i < widths.Length; i++) xs[i + 1] = xs[i] + width * widths[i] / total;

            int top = y;
            using (var hb = new SolidBrush(Soft)) g.FillRectangle(hb, x, y, width, rowH);
            for (int i = 0; i < heads.Length; i++)
                g.DrawString(heads[i], fHead, Brushes.Black, new RectangleF(xs[i] + 4, y + 2, xs[i + 1] - xs[i] - 6, rowH), clip);
            y += rowH;
            foreach (string[] r in rows)
            {
                g.DrawLine(Pens.LightGray, x, y, x + width, y);
                for (int i = 0; i < widths.Length; i++)
                {
                    string v = i < r.Length ? r[i] : "";
                    g.DrawString(v, fCell, Brushes.Black, new RectangleF(xs[i] + 4, y + 2, xs[i + 1] - xs[i] - 6, rowH), clip);
                }
                y += rowH;
            }
            for (int i = 1; i < widths.Length; i++) g.DrawLine(grid, xs[i], top, xs[i], y);   // séparateurs verticaux
            g.DrawLine(grid, x, top + rowH, x + width, top + rowH);
            g.DrawRectangle(grid, x, top, width, y - top);
            return y;
        }

        // Dessine toute la carte ; renvoie la hauteur utilisée (premier passage = mesure)
        private static int Draw(Graphics g, Map_Flight f, double missionStart, string part, Map_Projection proj)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            f.Compute();
            List<KeyValuePair<string, string>> facts = Map_Flights.Describe(f, missionStart);
            Map_Loadout lo = Map_Loadout.Of(f);

            using (var fTitle = new Font("Segoe UI", 17f, FontStyle.Bold))
            using (var fSub = new Font("Segoe UI", 9.5f))
            using (var fHead = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (var fRow = new Font("Consolas", 9.5f))
            using (var fMono = new Font("Consolas", 8.5f))
            using (var fMonoB = new Font("Consolas", 8.5f, FontStyle.Bold))
            using (var black = new SolidBrush(Color.Black))
            using (var gray = new SolidBrush(Color.FromArgb(90, 90, 90)))
            using (var gridPen = new Pen(Grid, 1.2f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.White);

                var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var clip = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                int full = W - 2 * M;

                int y = M;

                // ---- Titre ----
                g.DrawRectangle(gridPen, M, y, full, 54);
                using (var accent = new SolidBrush(f.Color)) g.FillRectangle(accent, M + 1, y + 1, 8, 53);
                g.DrawString(f.Title, fTitle, black, new RectangleF(M, y, full, 38), center);
                string sub = (f.Side ?? "").ToUpperInvariant() + "  -  " + f.Units + " aircraft" + (f.IsPlayer ? "  -  PLAYER" : "");
                g.DrawString(sub, fSub, gray, new RectangleF(M, y + 32, full, 20), center);
                y += 54;

                // ---- Radio-navigation (lue une fois, servira aussi à la communication) ----
                List<NavLine> nav = ParseNav(Lines(part, "Radio Navigation:", 40));

                // ---- PACKAGE : tableau Package + tableau Flight à gauche, chiffres clés à droite ----
                y = HeaderBar(g, "PACKAGE", y, fHead, gridPen, center);
                int leftW = 700, gap = 0;
                int yTop = y;

                // Package: Task | Num | Type | Base | Callsign
                var packRows = new List<string[]>();
                string[] packLines = Lines(part, "Package:", 12);
                for (int i = 1; i < packLines.Length; i++)             // ligne 0 = en-tête du tableau
                {
                    string[] cells = Cells(packLines[i]);
                    if (cells.Length > 0) packRows.Add(cells);
                }
                var flightRows = new List<string[]>();
                string[] flLines = Lines(part, "Flight:", 12);
                for (int i = 1; i < flLines.Length; i++)
                {
                    string[] cells = Cells(flLines[i]);
                    if (cells.Length > 0) flightRows.Add(cells);
                }

                int yL = y;
                if (packRows.Count > 0)
                    yL = Table(g, new[] { "Task", "Num", "Type", "Base", "Callsign" }, packRows, new float[] { 1.1f, 0.6f, 1f, 2f, 1.6f }, M, yL, leftW, fHead, fRow, gridPen, clip, false);
                if (flightRows.Count > 0)
                    yL = Table(g, new[] { "Role", "Callsign", "Aircraft #" }, flightRows, new float[] { 1f, 1.5f, 1.5f }, M, yL, leftW, fHead, fRow, gridPen, clip, false);
                if (packRows.Count == 0 && flightRows.Count == 0)
                {
                    g.DrawRectangle(gridPen, M, yL, leftW, 26);
                    g.DrawString("No briefing table for this flight (usually only the player flights have one).", fMono, black, M + 8, yL + 6);
                    yL += 26;
                }

                // Boîte de droite : chiffres clés
                var keyRows = new List<string[]>();
                foreach (string key in new[] { "Start", "End", "Duration", "Distance", "Average speed", "Altitude" })
                {
                    string v = facts.Where(r => r.Key == key).Select(r => r.Value.Replace("\n", "   ")).FirstOrDefault();
                    if (v != null) keyRows.Add(new[] { key, v });
                }
                if (lo != null)
                {
                    if (lo.Name != "") keyRows.Add(new[] { "Loadout", lo.Name });
                    if (lo.Fuel >= 0) keyRows.Add(new[] { "Fuel", lo.Fuel.ToString("0", c) + " kg" });
                    if (lo.Flare >= 0 || lo.Chaff >= 0) keyRows.Add(new[] { "Flares / Chaff", Math.Max(0, lo.Flare).ToString("0", c) + " / " + Math.Max(0, lo.Chaff).ToString("0", c) });
                    if (lo.Gun >= 0) keyRows.Add(new[] { "Gun", lo.Gun.ToString("0", c) + " %" });
                }
                int boxX = M + leftW + gap, boxW = full - leftW - gap;
                int yR = Table(g, new[] { "Data", "Value" }, keyRows, new float[] { 1f, 1.6f }, boxX, yTop, boxW, fHead, fMonoB, gridPen, clip, false);
                y = Math.Max(yL, yR);

                // ---- COMMUNICATION : Freq | Use | Name | TACAN | ILS / VOR ----
                var commRows = new List<string[]>();
                string radioName = "";
                foreach (string l in Lines(part, "Communication:", 60))
                {
                    if (l.TrimStart().StartsWith("Radio:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (radioName != "") break;                        // un seul poste radio (le premier)
                        radioName = l.Substring(l.IndexOf(':') + 1).Trim();
                        continue;
                    }
                    // "ATC:   CVN-71 Theodore Roosevelt   127.500 MHz   Channel 2"  ->  cases séparées par 2 espaces ou plus
                    string[] cells = Cells(l);
                    string use = cells.Length > 0 ? cells[0] : "", name = "", freq = "", chan = "";
                    var mid = new List<string>();
                    for (int i = 1; i < cells.Length; i++)
                    {
                        Match mf = Regex.Match(cells[i], @"^(\d+(?:\.\d+)?)\s*(MHz|kHz)$", RegexOptions.IgnoreCase);
                        Match mc = Regex.Match(cells[i], @"^Channel\s+(\S+)$", RegexOptions.IgnoreCase);
                        if (mf.Success) freq = mf.Groups[1].Value + " " + mf.Groups[2].Value;
                        else if (mc.Success) chan = mc.Groups[1].Value;
                        else mid.Add(cells[i]);
                    }
                    int colon = use.IndexOf(':');
                    if (colon >= 0)                                  // "Tanker: 59Y S-3B Tanker Shell41" -> Use = Tanker, le reste passe dans le nom
                    {
                        string after = use.Substring(colon + 1).Trim();
                        use = use.Substring(0, colon).Trim();
                        if (after != "") mid.Insert(0, after);
                    }
                    name = string.Join(" ", mid);

                    string tacan = "", ils = "";
                    Match tm = Regex.Match(name, @"^(\d{1,3}[XY])\s+(.*)$", RegexOptions.IgnoreCase);
                    if (tm.Success) { tacan = tm.Groups[1].Value.ToUpperInvariant(); name = tm.Groups[2].Value; }      // ravitailleur : TACAN en tête du nom
                    NavLine hit = name == "" ? null : nav.FirstOrDefault(n => n.Name != "" && (name.IndexOf(n.Name, StringComparison.OrdinalIgnoreCase) >= 0 || n.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0));
                    if (hit != null)
                    {
                        if (tacan == "") tacan = hit.Tacan;
                        ils = hit.Ils != "" ? hit.Ils : (hit.Vor != "" ? "VOR " + hit.Vor : "");
                    }
                    commRows.Add(new[] { freq, chan, use, name, tacan, ils });
                }
                if (commRows.Count > 0)
                {
                    y = HeaderBar(g, "COMMUNICATION" + (radioName != "" ? "   -   " + radioName : ""), y, fHead, gridPen, center);
                    y = Table(g, new[] { "Frequency", "Ch", "Use", "Name", "TACAN", "ILS / VOR" }, commRows, new float[] { 1.3f, 0.45f, 0.8f, 2.6f, 0.9f, 2.4f }, M, y, full, fHead, fRow, gridPen, clip, false);
                }

                // ---- RADIO NAVIGATION : base, déroutements, ravitailleurs ----
                if (nav.Count > 0)
                {
                    y = HeaderBar(g, "RADIO NAVIGATION   -   base, diverts, tankers", y, fHead, gridPen, center);
                    var navRows = nav.Select(n => new[] { n.Role, n.Name, n.Tacan, n.Vor, n.Ndb, n.Ils }).ToList();
                    y = Table(g, new[] { "Type", "Name", "TACAN", "VOR", "NDB", "ILS / ICLS" }, navRows, new float[] { 0.8f, 2.4f, 1f, 0.9f, 0.7f, 1.8f }, M, y, full, fHead, fRow, gridPen, clip, false);
                }

                // ---- FLIGHT PLAN : WP | Name | Coordinates | Altitude | Speed | ETA | Hdg | Leg ----
                y = HeaderBar(g, "FLIGHT PLAN", y, fHead, gridPen, center);
                var wpRows = new List<string[]>();
                for (int i = 0; i < f.Waypoints.Count; i++)
                {
                    Map_Waypoint w = f.Waypoints[i];
                    string hdg = "-", leg = "-";
                    if (i > 0)
                    {
                        Map_Waypoint pw = f.Waypoints[i - 1];
                        double hh = (Math.Atan2(w.Y - pw.Y, w.X - pw.X) * 180.0 / Math.PI + 360.0) % 360.0;
                        hdg = hh.ToString("000", c);
                        leg = (f.LegM[i] / 1000.0).ToString("0.0", c) + " km";
                    }
                    wpRows.Add(new[]
                    {
                        "", i.ToString(c) + "  " + (w.ShortName == "" ? "-" : w.ShortName), LatLon(proj, w),
                        w.Alt.ToString("0", c) + " m / " + (w.Alt * 3.28084).ToString("0", c) + " ft",
                        w.Speed > 0 ? (w.Speed * 3.6).ToString("0", c) + " km/h / " + (w.Speed * 1.943844).ToString("0", c) + " kt" : "-",
                        Map_Flights.Clock(missionStart, f.Times[i]), hdg, leg
                    });
                }
                int wpTop = y;
                y = Table(g, new[] { "", "Waypoint", "Coordinates", "Altitude", "Speed", "ETA", "Hdg", "Leg" }, wpRows,
                    new float[] { 0.35f, 2.3f, 2.6f, 1.8f, 2f, 0.9f, 0.6f, 1f }, M, y, full, fHead, fRow, gridPen, clip, false);
                // symbole de chaque waypoint dans la première colonne
                float symX = M + 16;
                for (int i = 0; i < f.Waypoints.Count; i++)
                    Map_UI_View.DrawMarker(g, new PointF(symX, wpTop + 20 + i * 20 + 10), f.Waypoints[i].Role, f.Color, 4.5f, 255);

                // ---- Armement : silhouette de face ----
                if (lo != null && lo.Pylons.Count > 0)
                {
                    y = HeaderBar(g, "LOADOUT" + (lo.Name != "" ? "   -   " + lo.Name : "") + "   (front view, station 1 = left wing tip)", y, fHead, gridPen, center);
                    y = DrawLoadout(g, lo, y, fMono, fHead, gridPen, black);
                }

                // ---- Cible et météo ----
                string tasked = TaskedLine(part);
                var tgt = new List<string>();
                if (tasked != "") tgt.Add(tasked);
                tgt.AddRange(Lines(part, "Target:", 6));
                if (tgt.Count > 0)
                {
                    y = HeaderBar(g, "TARGET", y, fHead, gridPen, center);
                    y = TextBox(g, tgt, y, fMono, fMonoB, gridPen, black, clip, false);
                }
                string[] meteo = Lines(part, "Meteo:", 8);
                if (meteo.Length > 0)
                {
                    y = HeaderBar(g, "METEO", y, fHead, gridPen, center);
                    y = TextBox(g, meteo.ToList(), y, fMono, fMonoB, gridPen, black, clip, false);
                }

                return y;
            }
        }

        // "You are tasked to strike OBJ_Towla with a Time On Target of 06h58."
        private static string TaskedLine(string part)
        {
            if (string.IsNullOrEmpty(part)) return "";
            foreach (string l in part.Split('\n'))
                if (l.TrimStart().StartsWith("You are tasked", StringComparison.OrdinalIgnoreCase)) return l.Trim();
            return "";
        }

        private static int HeaderBar(Graphics g, string title, int y, Font f, Pen grid, StringFormat center)
        {
            using (var b = new SolidBrush(Bar)) g.FillRectangle(b, M, y, W - 2 * M, 22);
            g.DrawRectangle(grid, M, y, W - 2 * M, 22);
            g.DrawString(title, f, Brushes.Black, new RectangleF(M, y, W - 2 * M, 22), center);
            return y + 22;
        }

        // Cadre de texte en police fixe ; boldBase = les lignes "Base:" en gras (radio-navigation)
        private static int TextBox(Graphics g, List<string> lines, int y, Font mono, Font monoBold, Pen grid, Brush black, StringFormat clip, bool boldBase)
        {
            int h = lines.Count * 15 + 12;
            g.DrawRectangle(grid, M, y, W - 2 * M, h);
            for (int i = 0; i < lines.Count; i++)
            {
                bool bold = boldBase && lines[i].TrimStart().StartsWith("Base:", StringComparison.OrdinalIgnoreCase);
                g.DrawString(lines[i], bold ? monoBold : mono, black, new RectangleF(M + 8, y + 6 + i * 15, W - 2 * M - 16, 16), clip);
            }
            return y + h;
        }

        // Avion vu de face (fuselage, ailes, dérive) avec les stations numérotées de gauche à droite, un symbole par emport, légende à droite
        private static int DrawLoadout(Graphics g, Map_Loadout lo, int y, Font mono, Font head, Pen grid, Brush black)
        {
            int stations = Math.Max(lo.Pylons.Keys.Max(), 5);
            int boxH = Math.Max(210, lo.Pylons.Count * 16 + 20);
            g.DrawRectangle(grid, M, y, W - 2 * M, boxH);

            // silhouette
            float cx = M + 290, cy = y + 70, half = 250;
            using (var body = new SolidBrush(Color.FromArgb(215, 220, 228)))
            using (var line = new Pen(Color.FromArgb(120, 130, 145), 1.5f))
            {
                g.FillEllipse(body, cx - 22, cy - 38, 44, 76);                       // fuselage vu de face
                g.DrawEllipse(line, cx - 22, cy - 38, 44, 76);
                g.FillRectangle(body, cx - half, cy - 4, 2 * half, 9);               // ailes
                g.DrawRectangle(line, cx - half, cy - 4, 2 * half, 9);
                g.FillRectangle(body, cx - 3, cy - 70, 6, 36);                       // dérive
                g.DrawRectangle(line, cx - 3, cy - 70, 6, 36);
                g.FillEllipse(Brushes.SlateGray, cx - 9, cy - 28, 18, 22);            // verrière
            }

            foreach (KeyValuePair<int, string> st in lo.Pylons)
            {
                float t = stations <= 1 ? 0.5f : (st.Key - 1) / (float)(stations - 1);
                float x = cx - half + t * 2 * half;
                string kind = Map_Loadout.Kind(st.Value);

                g.DrawLine(Pens.DimGray, x, cy + 5, x, cy + 16);                      // pylône
                RectangleF r;
                Color col;
                switch (kind)
                {
                    case "missile": r = new RectangleF(x - 4.5f, cy + 16, 9, 9); col = Color.FromArgb(200, 60, 60); break;
                    case "rocket": r = new RectangleF(x - 8, cy + 16, 16, 16); col = Color.FromArgb(220, 140, 40); break;
                    case "bomb": r = new RectangleF(x - 8, cy + 16, 16, 16); col = Color.FromArgb(90, 90, 90); break;
                    case "tank": r = new RectangleF(x - 11, cy + 16, 22, 16); col = Color.FromArgb(70, 150, 210); break;
                    case "pod": r = new RectangleF(x - 6, cy + 16, 12, 12); col = Color.FromArgb(120, 90, 170); break;
                    default: r = new RectangleF(x - 6, cy + 16, 12, 12); col = Color.FromArgb(150, 150, 150); break;
                }
                using (var b = new SolidBrush(col)) g.FillEllipse(b, r);
                g.DrawEllipse(Pens.Black, r.X, r.Y, r.Width, r.Height);
                if (kind == "missile")
                {
                    g.DrawLine(Pens.Black, r.X - 2, r.Y + r.Height / 2, r.Right + 2, r.Y + r.Height / 2);     // ailerons en croix
                    g.DrawLine(Pens.Black, r.X + r.Width / 2, r.Y - 2, r.X + r.Width / 2, r.Bottom + 2);
                }
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(st.Key.ToString(), head, black, x, cy + 40, sf);
            }

            // légende : numéro de station -> nom
            int lx = M + 600, ly = y + 8;
            foreach (KeyValuePair<int, string> st in lo.Pylons)
            {
                g.DrawString(st.Key.ToString().PadLeft(2) + "   " + Map_Loadout.Pretty(st.Value) + "   [" + Map_Loadout.Kind(st.Value) + "]", mono, black,
                    new RectangleF(lx, ly, W - M - lx - 8, 16), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                ly += 16;
            }
            return y + boxH;
        }
    }

    // Petite fenêtre (non modale) avec le texte d'un élément au sol : nom du groupe / de l'unité, type, position et tous ses champs
    internal class Map_UI_GroundInfo : Form
    {
        private readonly TextBox _box;

        public Map_UI_GroundInfo()
        {
            Text = "Ground element";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(430, 520);
            ShowInTaskbar = false;

            var btnCopy = new Button { Text = "Copy all", Dock = DockStyle.Top, Height = 26 };
            btnCopy.Click += (s, e) => { try { Clipboard.SetText(_box.Text); } catch (Exception ex) { MessageBox.Show(ex.Message, "Copy"); } };
            _box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f), BackColor = Color.White };
            Controls.Add(_box);
            Controls.Add(btnCopy);
        }

        public void ShowUnit(Map_GroundUnit u, Map_Projection proj)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Map_GroundGroup g = u.Group;
            var sb = new StringBuilder();
            Text = (u.Red ? "Red" : "Blue") + " - " + (g != null ? g.Name : u.Name);

            if (g != null)
            {
                sb.AppendLine("GROUP : " + g.Name);
                sb.AppendLine("Side     : " + (g.Red ? "red" : "blue") + "   Country : " + g.Country + "   Kind : " + g.Category + "   Units : " + g.Units.Count);
                AppendFields(sb, g.Src, "units", "route");
                sb.AppendLine();
            }

            sb.AppendLine("UNIT : " + u.Name);
            sb.AppendLine("Type     : " + u.Type);
            sb.AppendLine("Position : x " + u.X.ToString("0", c) + "  y " + u.Y.ToString("0", c) + "  (x = north, y = east)");
            if (proj != null)
            {
                try
                {
                    double lat, lon;
                    Map_Web.DcsToLatLon(proj, u.X, u.Y, out lat, out lon);
                    sb.AppendLine("Lat/Lon  : " + lat.ToString("0.00000", c) + "  " + lon.ToString("0.00000", c));
                }
                catch { }
            }
            AppendFields(sb, u.Src, "x", "y", "name", "type");

            if (g != null && g.Units.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("OTHER UNITS OF THE GROUP");
                foreach (Map_GroundUnit o in g.Units)
                    if (o != u) sb.AppendLine("  " + o.Name + "   [" + o.Type + "]");
            }

            _box.Text = sb.ToString();
            _box.SelectionStart = 0;
            _box.SelectionLength = 0;
        }

        // Tous les champs simples (texte, nombre, oui / non) d'une table Lua ; "heading" est en radians dans le fichier, on ajoute les degrés
        private static void AppendFields(StringBuilder sb, Dictionary<string, object> d, params string[] skip)
        {
            if (d == null) return;
            CultureInfo c = CultureInfo.InvariantCulture;
            foreach (KeyValuePair<string, object> kv in d.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (skip.Contains(kv.Key) || kv.Value is Dictionary<string, object> || kv.Value == null) continue;
                string v;
                if (kv.Value is double)
                {
                    double n = (double)kv.Value;
                    v = n.ToString("0.###", c);
                    if (kv.Key == "heading") v += "  (" + ((n * 180.0 / Math.PI + 360.0) % 360.0).ToString("0", c) + " deg)";
                }
                else v = Convert.ToString(kv.Value, c);
                sb.AppendLine(kv.Key.PadRight(9) + ": " + v);
            }
        }
    }

    // Fenêtre d'aperçu de la datacard : Save (PNG) et Copy (presse-papiers)
    internal class Map_UI_DatacardForm : Form
    {
        private readonly Bitmap _bmp;
        private readonly string _name;

        public Map_UI_DatacardForm(Bitmap bmp, string flightName)
        {
            _bmp = bmp;
            _name = flightName;
            Text = "Datacard - " + flightName;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Math.Min(1180, bmp.Width + 40), Math.Min(900, bmp.Height + 110));

            var buttons = new Panel { Dock = DockStyle.Top, Height = 38 };
            var btnSave = new Button { Text = "Save as PNG...", Location = new Point(8, 6), Size = new Size(120, 26) };
            var btnCopy = new Button { Text = "Copy", Location = new Point(136, 6), Size = new Size(80, 26) };
            btnSave.Click += (s, e) => Save();
            btnCopy.Click += (s, e) => { try { Clipboard.SetImage(_bmp); } catch (Exception ex) { MessageBox.Show(ex.Message, "Datacard"); } };
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnCopy);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var pic = new PictureBox { Image = bmp, SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(0, 0) };
            scroll.Controls.Add(pic);

            Controls.Add(scroll);
            Controls.Add(buttons);
        }

        private void Save()
        {
            string safe = new string(_name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            using (var dlg = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "Datacard - " + safe + ".png" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { _bmp.Save(dlg.FileName, ImageFormat.Png); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Datacard"); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _bmp != null) _bmp.Dispose();
            base.Dispose(disposing);
        }
    }
}
