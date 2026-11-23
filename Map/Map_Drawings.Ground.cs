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
    // Partie de Map_Drawings (voir Map_Drawings.cs) : éléments au sol, SAM, EWR, navires
    internal static partial class Map_Drawings
    {
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
                    if (units == null) continue;
                    bool hidden = Str(group, "hidden") == "true";

                    if (cat == "ship" && !hidden) AddShipRoute(data, group, red);

                    var gg = new Map_GroundGroup { Name = Str(group, "name"), Red = red, Country = countryName, Category = cat, Src = group, Hidden = hidden };
                    double sx = 0, sy = 0;
                    foreach (object uo in units.Values)
                    {
                        var unit = uo as Dictionary<string, object>;
                        if (unit == null || Str(unit, "dead") == "true") continue;
                        string type = Str(unit, "type");
                        var u = new Map_GroundUnit { X = Num(unit, "x"), Y = Num(unit, "y"), Red = red, Kind = GroundKind(cat, type, unit), Name = Str(unit, "name"), Type = type, Src = unit, Group = gg };
                        u.HalfM = Map_GroundSize.HalfMeters(u.Kind, type);
                        if (unit.ContainsKey("heading")) u.Heading = Num(unit, "heading");
                        (hidden ? data.HiddenGround : data.Ground).Add(u);
                        gg.Units.Add(u);
                        sx += u.X; sy += u.Y;
                    }
                    if (gg.Units.Count > 0)
                    {
                        gg.X = sx / gg.Units.Count; gg.Y = sy / gg.Units.Count;
                        (hidden ? data.HiddenGroundGroups : data.GroundGroups).Add(gg);
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
    }
}
