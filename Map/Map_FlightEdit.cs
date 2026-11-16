using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Modifications d'un vol dans le fichier mission (partie de Map_Drawings : elle utilise son lecteur Lua) :
    //  1) AuditFlight  : lecture seule, liste tout ce qui touche un vol (ids, noms, triggers, autres vols, scripts)
    //  2) ApplyUnits : avions d'un vol (1 à 4 : ajout = copie du dernier avion, retrait, noms / indicatifs / numéros de queue)
    //  3) ApplyFlightFreq, 3b) ReadPresets / ApplyPresets (presets radio des avions joueur) : fréquence "flight" (DCE_FreqFlight) d'un vol
    // Même méthode que SaveWaypoints : le texte du fichier est modifié sur place (pas de réécriture complète), copie "<fichier>.wpt_backup" la première fois.
    internal static partial class Map_Drawings
    {
        private class AirGroup
        {
            public Dictionary<string, object> G;
            public string Side = "", Country = "", Category = "", Name = "";
            public int[] Span;
        }

        // Tous les identifiants / noms de la mission (tous les groupes : avions, sol, navires, statiques)
        private class MissionIndex
        {
            public Dictionary<long, int> GroupIds = new Dictionary<long, int>();
            public Dictionary<long, int> UnitIds = new Dictionary<long, int>();
            public Dictionary<string, int> GroupNames = new Dictionary<string, int>(StringComparer.Ordinal);
            public Dictionary<string, int> UnitNames = new Dictionary<string, int>(StringComparer.Ordinal);
            public Dictionary<string, int> Onboard = new Dictionary<string, int>(StringComparer.Ordinal);
            public long MaxGroupId, MaxUnitId;
        }

        private static string ReadMissionText(string file, out bool isMiz)
        {
            isMiz = file.EndsWith(".miz", StringComparison.OrdinalIgnoreCase);
            if (!isMiz) return File.ReadAllText(file);

            using (ZipArchive zip = ZipFile.OpenRead(file))
            {
                ZipArchiveEntry entry = zip.GetEntry("mission");
                if (entry == null) throw new InvalidDataException("No 'mission' file inside the .miz.");
                using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
        }

        // Écrit le nouveau texte (copie de sécurité la première fois), même méthode que SaveWaypoints
        private static void WriteMissionText(string file, string newText, bool isMiz)
        {
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
        }

        private static List<AirGroup> ListAirGroups(Dictionary<string, object> root, Dictionary<object, Dictionary<string, int[]>> spans)
        {
            var result = new List<AirGroup>();
            Dictionary<string, object> coalitions = Sub(root, "coalition");
            if (coalitions == null) return result;

            foreach (KeyValuePair<string, object> co in coalitions)
            {
                var coalition = co.Value as Dictionary<string, object>;
                Dictionary<string, object> countries = Sub(coalition, "country");
                if (countries == null) continue;

                foreach (object cyo in countries.Values)
                {
                    var country = cyo as Dictionary<string, object>;
                    foreach (string cat in new[] { "plane", "helicopter" })
                    {
                        Dictionary<string, object> gl = Sub(Sub(country, cat), "group");
                        Dictionary<string, int[]> ks;
                        if (gl == null || !spans.TryGetValue(gl, out ks)) continue;

                        foreach (KeyValuePair<string, object> kv in gl)
                        {
                            var g = kv.Value as Dictionary<string, object>;
                            int[] sp;
                            if (g == null || !ks.TryGetValue(kv.Key, out sp)) continue;
                            result.Add(new AirGroup { G = g, Side = co.Key, Country = Str(country, "name"), Category = cat, Name = Str(g, "name"), Span = sp });
                        }
                    }
                }
            }
            return result;
        }

        private static MissionIndex BuildIndex(Dictionary<string, object> root)
        {
            var ix = new MissionIndex();
            Dictionary<string, object> coalitions = Sub(root, "coalition");
            if (coalitions == null) return ix;

            foreach (object co in coalitions.Values)
            {
                Dictionary<string, object> countries = Sub(co as Dictionary<string, object>, "country");
                if (countries == null) continue;
                foreach (object cyo in countries.Values)
                {
                    var country = cyo as Dictionary<string, object>;
                    if (country == null) continue;
                    foreach (object cato in country.Values)
                    {
                        Dictionary<string, object> gl = Sub(cato as Dictionary<string, object>, "group");
                        if (gl == null) continue;
                        foreach (object go in gl.Values)
                        {
                            var g = go as Dictionary<string, object>;
                            if (g == null) continue;

                            long gid = (long)Num(g, "groupId");
                            Bump(ix.GroupIds, gid);
                            if (gid > ix.MaxGroupId) ix.MaxGroupId = gid;
                            Bump(ix.GroupNames, Str(g, "name"));

                            Dictionary<string, object> units = Sub(g, "units");
                            if (units == null) continue;
                            foreach (object uo in units.Values)
                            {
                                var u = uo as Dictionary<string, object>;
                                if (u == null) continue;
                                long uid = (long)Num(u, "unitId");
                                Bump(ix.UnitIds, uid);
                                if (uid > ix.MaxUnitId) ix.MaxUnitId = uid;
                                Bump(ix.UnitNames, Str(u, "name"));
                                string ob = Str(u, "onboard_num");
                                if (ob != "") Bump(ix.Onboard, ob);
                            }
                        }
                    }
                }
            }
            return ix;
        }

        private static void Bump<T>(Dictionary<T, int> d, T key)
        {
            int n;
            d[key] = d.TryGetValue(key, out n) ? n + 1 : 1;
        }

        private static string LuaText(string s)
        {
            return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string LuaNum(double v)
        {
            return Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
        }

        // Début de la ligne qui contient pos : espaces et tabulations du début
        private static string LineIndent(string text, int pos)
        {
            int ls = pos > 0 ? text.LastIndexOf('\n', pos - 1) + 1 : 0;
            int e = ls;
            while (e < text.Length && (text[e] == '\t' || text[e] == ' ')) e++;
            return text.Substring(ls, e - ls);
        }

        private static bool IsPlayerSkill(string skill)
        {
            return skill.Equals("Client", StringComparison.OrdinalIgnoreCase) || skill.Equals("Player", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        //  1) AUDIT : lecture seule
        // ------------------------------------------------------------------
        internal static string AuditFlight(string file, string flightName)
        {
            try
            {
                bool isMiz;
                string text = ReadMissionText(file, out isMiz);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                if (root == null || Sub(root, "coalition") == null) return "The mission file has no coalition table.";

                List<AirGroup> air = ListAirGroups(root, spans);
                AirGroup ag = air.FirstOrDefault(a => a.Name == flightName);
                if (ag == null) return "Flight '" + flightName + "' not found in the file.";

                MissionIndex ix = BuildIndex(root);
                long gid = (long)Num(ag.G, "groupId");
                var sb = new StringBuilder();

                // --- le groupe ---
                Dictionary<string, object> points = Sub(Sub(ag.G, "route"), "points");
                Dictionary<string, object> p1 = Sub(points, "1");
                sb.AppendLine("FLIGHT  " + ag.Name);
                sb.AppendLine("  side " + ag.Side + " / country " + ag.Country + " / " + ag.Category + " / task " + Str(ag.G, "task"));
                sb.AppendLine("  groupId " + gid + "    start_time " + LuaNum(Num(ag.G, "start_time")) + " s    late activation " + (Str(ag.G, "lateActivation") == "true" ? "yes" : "no")
                    + "    uncontrolled " + (Str(ag.G, "uncontrolled") == "true" ? "yes" : "no"));
                sb.AppendLine("  radio " + Str(ag.G, "frequency") + " MHz    first waypoint: " + Str(p1, "type") + " / " + Str(p1, "action") + "    waypoints: " + (points == null ? 0 : points.Count));
                sb.AppendLine();

                // --- les avions ---
                Dictionary<string, object> units = Sub(ag.G, "units");
                int nUnits = units == null ? 0 : units.Count;
                sb.AppendLine("AIRCRAFT (" + nUnits + ")");
                bool fixedParking = false, hasPlayer = false;
                if (units != null)
                    foreach (KeyValuePair<string, object> kv in units)
                    {
                        var u = kv.Value as Dictionary<string, object>;
                        if (u == null) continue;
                        object cso;
                        string cs = "";
                        if (u.TryGetValue("callsign", out cso))
                        {
                            var cst = cso as Dictionary<string, object>;
                            cs = cst != null ? Str(cst, "name") : Str(u, "callsign");
                        }
                        string parking = u.ContainsKey("parking") ? "parking slot " + Str(u, "parking") : "parking automatic";
                        if (u.ContainsKey("parking")) fixedParking = true;
                        if (IsPlayerSkill(Str(u, "skill"))) hasPlayer = true;
                        sb.AppendLine("  " + kv.Key + "  " + Str(u, "name") + "   unitId " + Str(u, "unitId") + "   " + Str(u, "type") + "   skill " + Str(u, "skill")
                            + "   onboard " + Str(u, "onboard_num") + "   callsign " + cs + "   " + parking);
                    }
                sb.AppendLine();

                // --- les triggers (règles lisibles + texte compilé que DCS exécute) ---
                sb.AppendLine("TRIGGERS THAT USE THIS FLIGHT");
                int nRules = 0;
                Dictionary<string, object> rules = Sub(root, "trigrules");
                Dictionary<string, object> compiled = Sub(Sub(root, "trig"), "actions");
                if (rules != null)
                    foreach (KeyValuePair<string, object> kv in rules)
                    {
                        var rule = kv.Value as Dictionary<string, object>;
                        Dictionary<string, object> actions = Sub(rule, "actions");
                        if (actions == null) continue;

                        var found = new List<string>();
                        foreach (object ao in actions.Values)
                        {
                            var a = ao as Dictionary<string, object>;
                            if (a == null) continue;
                            object go;
                            if (a.TryGetValue("group", out go) && go is double && (long)(double)go == gid) found.Add(Str(a, "predicate"));
                            else if (a.TryGetValue("set_ai_task", out go))
                            {
                                var st = go as Dictionary<string, object>;
                                if (st != null && (long)Num(st, "1") == gid) found.Add(Str(a, "predicate") + " (task " + Str(st, "2") + ")");
                            }
                        }
                        if (found.Count == 0) continue;

                        nRules++;
                        string cond = "";
                        Dictionary<string, object> conds = Sub(rule, "rules");
                        if (conds != null)
                            foreach (object co in conds.Values)
                            {
                                var c = co as Dictionary<string, object>;
                                if (c == null) continue;
                                string one = Str(c, "predicate");
                                foreach (KeyValuePair<string, object> f in c)
                                    if (f.Key != "predicate" && (f.Value is double || f.Value is string)) one += " " + f.Key + "=" + Str(c, f.Key);
                                cond += (cond == "" ? "" : " AND ") + one;
                            }

                        object cmp;
                        string inCompiled = "";
                        if (compiled != null && compiled.TryGetValue(kv.Key, out cmp) && cmp is string)
                            inCompiled = ((string)cmp).IndexOf("(" + gid + ",", StringComparison.Ordinal) >= 0 || ((string)cmp).IndexOf("(" + gid + ")", StringComparison.Ordinal) >= 0 ? "   (also in the compiled text: yes)" : "   (also in the compiled text: NO)";
                        sb.AppendLine("  rule " + kv.Key + ": " + string.Join(", ", found) + "   when: " + (cond == "" ? "(no condition)" : cond) + "   type " + Str(rule, "predicate") + inCompiled);
                    }
                if (nRules == 0) sb.AppendLine("  none");
                sb.AppendLine();

                // --- les autres vols qui le citent par son identifiant (escorte, suivi...) ---
                sb.AppendLine("OTHER FLIGHTS THAT POINT AT THIS ONE (by groupId)");
                var idRx = new Regex("\\[\"groupId\"\\]\\s*=\\s*" + gid + "(?![\\d.])");
                int nPoint = 0;
                foreach (AirGroup o in air)
                {
                    if (o == ag) continue;
                    string slice = text.Substring(o.Span[0], o.Span[1] - o.Span[0]);
                    Match m = idRx.Match(slice);
                    if (!m.Success) continue;
                    var ids = Regex.Matches(slice.Substring(0, m.Index), "\\[\"id\"\\]\\s*=\\s*\"(\\w+)\"");
                    sb.AppendLine("  " + o.Name + "   (task: " + (ids.Count > 0 ? ids[ids.Count - 1].Groups[1].Value : "?") + ")");
                    nPoint++;
                }
                if (nPoint == 0) sb.AppendLine("  none");
                sb.AppendLine();

                // --- les scripts et textes qui contiennent son nom ---
                sb.AppendLine("SCRIPT LINES AND TRIGGER TEXTS THAT CONTAIN ITS NAME");
                int own = 0, other = 0, inTrig = 0;
                var otherNames = new List<string>();
                int[] sTrigRules, sTrig;
                Dictionary<string, int[]> rootSpans = spans[root];
                rootSpans.TryGetValue("trigrules", out sTrigRules);
                rootSpans.TryGetValue("trig", out sTrig);
                foreach (Match m in Regex.Matches(text, Regex.Escape(ag.Name)))
                {
                    int p = m.Index;
                    bool quoted = p > 0 && text[p - 1] == '\'' && p + ag.Name.Length < text.Length && text[p + ag.Name.Length] == '\'';
                    if (p >= ag.Span[0] && p < ag.Span[1]) { if (quoted) own++; continue; }
                    if ((sTrigRules != null && p >= sTrigRules[0] && p < sTrigRules[1]) || (sTrig != null && p >= sTrig[0] && p < sTrig[1])) { inTrig++; continue; }
                    AirGroup og = air.FirstOrDefault(a => p >= a.Span[0] && p < a.Span[1]);
                    if (og != null && quoted) { other++; if (!otherNames.Contains(og.Name)) otherNames.Add(og.Name); }
                }
                sb.AppendLine("  in its own route scripts: " + own + "    in other flights' scripts: " + other + (otherNames.Count > 0 ? " (" + string.Join("; ", otherNames) + ")" : "") + "    in triggers: " + inTrig);
                sb.AppendLine();

                // --- contrôles de cohérence ---
                sb.AppendLine("CHECKS");
                int cg; ix.GroupIds.TryGetValue(gid, out cg);
                sb.AppendLine("  groupId unique: " + (cg == 1 ? "OK" : "PROBLEM (" + cg + " groups use it)"));
                int cn; ix.GroupNames.TryGetValue(ag.Name, out cn);
                sb.AppendLine("  group name unique: " + (cn == 1 ? "OK" : "PROBLEM (" + cn + " groups use it)"));
                var bad = new List<string>();
                if (units != null)
                    foreach (object uo in units.Values)
                    {
                        var u = uo as Dictionary<string, object>;
                        if (u == null) continue;
                        int cu, cun, cob = 0;
                        ix.UnitIds.TryGetValue((long)Num(u, "unitId"), out cu);
                        ix.UnitNames.TryGetValue(Str(u, "name"), out cun);
                        if (Str(u, "onboard_num") != "") ix.Onboard.TryGetValue(Str(u, "onboard_num"), out cob);
                        if (cu != 1) bad.Add("unitId of " + Str(u, "name") + " (" + cu + " uses)");
                        if (cun != 1) bad.Add("name " + Str(u, "name") + " (" + cun + " uses)");
                        if (cob > 1) bad.Add("onboard number " + Str(u, "onboard_num") + " (" + cob + " aircraft)");
                    }
                sb.AppendLine("  unitIds, unit names, onboard numbers: " + (bad.Count == 0 ? "OK, all unique" : "PROBLEM: " + string.Join("; ", bad)));
                sb.AppendLine("  highest groupId in the mission " + ix.MaxGroupId + ", highest unitId " + ix.MaxUnitId);
                sb.AppendLine();

                // --- ce que le changement d'effectif ferait ---
                sb.AppendLine("CHANGING THE NUMBER OF AIRCRAFT");
                Dictionary<string, object> padA = fixedParking ? FindHelipad(root, p1) : null;
                if (fixedParking && padA == null) sb.AppendLine("  Adding aircraft: NOT possible (fixed parking slot on an airbase or a ship). Removing: possible.");
                else if (fixedParking) sb.AppendLine("  Start on " + Str(padA, "type") + " '" + Str(padA, "name") + "' (" + PadCapacity(Str(padA, "type")) + " parking slot(s)). Adding aircraft: they take the free slots; above that number the whole flight starts on the ground around the pad.");
                else sb.AppendLine("  Adding aircraft (up to 4): possible. The new ones are copies of the last aircraft (new unitId, name, onboard number, callsign number, formation spot).");
                sb.AppendLine("  Removing aircraft: " + (hasPlayer ? "the player's aircraft can not be removed." : "possible (the last ones go)."));
                sb.AppendLine("  Not changed: briefing text, Air Tasking Order table, the squadron roster of DCE.");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | audit : " + ex);
                return "Audit failed: " + ex.Message;
            }
        }

        // ------------------------------------------------------------------
        //  2) CHANGER LES AVIONS D'UN VOL (nombre 1 à 4, noms, indicatifs, numéros de queue)
        //  "rows" = la liste voulue, dans l'ordre : Src = numéro (1...) de l'avion d'origine, 0 = nouvel avion (copie du dernier).
        //  Un avion d'origine absent de la liste est supprimé. Les noms doivent rester uniques dans la mission.
        //  Vol parti d'un FARP / pad : les avions prennent les places du pad (parking_id) tant qu'il y en a assez ;
        //  au-delà, tout le vol part du sol autour du FARP (points SAR de db_airbases s'il y en a, sinon un cercle), cap vers son centre.
        //  "finder" donne les points SAR (x, y) de la base : (nom du pad, x, y) -> liste, ou null.
        // ------------------------------------------------------------------
        internal class UnitEdit
        {
            public int Src;
            public string Name = "", Callsign = "", Onboard = "";
        }

        // Change le nom / l'indicatif / le numéro de queue d'un avion (seulement ce qui est donné et qui change)
        private static string EditUnitText(string raw, string name, string callsign, string onboard)
        {
            var sp = new Dictionary<object, Dictionary<string, int[]>>();
            var t = new LuaReader(raw, 0, sp).ReadValue() as Dictionary<string, object>;
            if (t == null) return raw;

            Dictionary<string, int[]> ks = sp[t];
            var edits = new List<TextEdit>();
            int[] s;
            if (name != "" && name != Str(t, "name") && ks.TryGetValue("name", out s)) edits.Add(new TextEdit(s[0], s[1], LuaText(name)));
            if (onboard != "" && onboard != Str(t, "onboard_num") && ks.TryGetValue("onboard_num", out s)) edits.Add(new TextEdit(s[0], s[1], LuaText(onboard)));

            Dictionary<string, object> cs = Sub(t, "callsign");
            Dictionary<string, int[]> cks;
            if (callsign != "" && cs != null && callsign != Str(cs, "name") && sp.TryGetValue(cs, out cks))
            {
                if (cks.TryGetValue("name", out s)) edits.Add(new TextEdit(s[0], s[1], LuaText(callsign)));
                // "Colt91" : avant-dernier chiffre = n° du vol (2e case), dernier = n° de l'avion (3e case)
                Match m = Regex.Match(callsign, @"^.*\D(\d)(\d)$");
                if (m.Success)
                {
                    if (cks.TryGetValue("2", out s)) edits.Add(new TextEdit(s[0], s[1], m.Groups[1].Value));
                    if (cks.TryGetValue("3", out s)) edits.Add(new TextEdit(s[0], s[1], m.Groups[2].Value));
                }
            }
            return edits.Count == 0 ? raw : ApplyEdits(raw, edits);
        }

        internal static bool ApplyUnits(string file, string flightName, List<UnitEdit> rows, Func<string, double, double, List<double[]>> finder, out string message)
        {
            message = "";
            try
            {
                int newCount = rows == null ? 0 : rows.Count;
                if (newCount < 1 || newCount > 4) { message = "A flight can have 1 to 4 aircraft."; return false; }

                bool isMiz;
                string text = ReadMissionText(file, out isMiz);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                if (root == null || Sub(root, "coalition") == null) { message = "The mission file has no coalition table."; return false; }

                List<AirGroup> air = ListAirGroups(root, spans);
                AirGroup ag = air.FirstOrDefault(a => a.Name == flightName);
                if (ag == null) { message = "Flight '" + flightName + "' not found in the file."; return false; }

                Dictionary<string, object> units = Sub(ag.G, "units");
                Dictionary<string, int[]> groupSpans = spans[ag.G];
                int[] unitsSpan;
                Dictionary<string, int[]> unitSpans;
                if (units == null || !groupSpans.TryGetValue("units", out unitsSpan) || !spans.TryGetValue(units, out unitSpans))
                { message = "The units table of '" + flightName + "' could not be located."; return false; }

                // Les avions d'origine, dans l'ordre 1, 2, 3...
                var list = new List<Dictionary<string, object>>();
                var listSpan = new List<int[]>();
                for (int i = 1; ; i++)
                {
                    object o;
                    int[] sp;
                    if (!units.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out o) || !unitSpans.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out sp)) break;
                    var u = o as Dictionary<string, object>;
                    if (u == null) break;
                    list.Add(u);
                    listSpan.Add(sp);
                }
                int n = list.Count;
                if (n == 0 || n != units.Count) { message = "The units of '" + flightName + "' are not numbered 1, 2, 3... : not changed."; return false; }

                // Liste voulue : sources valides, joueur conservé, noms uniques
                MissionIndex ix = BuildIndex(root);
                var own = new HashSet<string>(list.Select(u => Str(u, "name")));
                var seenSrc = new HashSet<int>();
                var seenName = new HashSet<string>();
                foreach (UnitEdit e in rows)
                {
                    if (e.Src < 0 || e.Src > n || (e.Src > 0 && !seenSrc.Add(e.Src))) { message = "Bad aircraft list for '" + flightName + "': nothing was written."; return false; }
                    string nm = (e.Name ?? "").Trim();
                    if (nm != "")
                    {
                        if (!seenName.Add(nm)) { message = "Two aircraft are named '" + nm + "': names must be unique."; return false; }
                        if (ix.UnitNames.ContainsKey(nm) && !own.Contains(nm)) { message = "A unit named '" + nm + "' already exists in the mission: names must be unique."; return false; }
                    }
                }
                for (int i = 1; i <= n; i++)
                    if (IsPlayerSkill(Str(list[i - 1], "skill")) && !seenSrc.Contains(i)) { message = "Aircraft " + i + " is the player's aircraft: it can not be removed."; return false; }

                bool wasParking = list.Any(u => u.ContainsKey("parking"));
                LayoutPlan lay = MakeLayout(root, ag.G, newCount, finder);
                if (newCount > n && wasParking && lay == null)
                { message = "Some aircraft of this flight have a fixed parking slot on an airbase or a ship: adding aircraft is not supported there (only FARP / helipad starts)."; return false; }

                string nl = text.Contains("\r\n") ? "\r\n" : "\n";
                long nextId = ix.MaxUnitId;
                var onboard = new HashSet<string>(ix.Onboard.Keys);

                // Formation (sans FARP) : on prolonge l'écart entre les deux derniers avions (sinon 15 m / 15 m)
                var last = list[n - 1];
                double dx = 15, dy = 15;
                if (n >= 2)
                {
                    double ddx = Num(last, "x") - Num(list[n - 2], "x"), ddy = Num(last, "y") - Num(list[n - 2], "y");
                    if (Math.Abs(ddx) + Math.Abs(ddy) > 0.5) { dx = ddx; dy = ddy; }
                }
                double px = Num(last, "x"), py = Num(last, "y");
                string lastRaw = text.Substring(listSpan[n - 1][0], listSpan[n - 1][1] - listSpan[n - 1][0]);

                var raws = new List<string>();
                for (int r = 0; r < rows.Count; r++)
                {
                    UnitEdit e = rows[r];
                    string nm = (e.Name ?? "").Trim(), cs = (e.Callsign ?? "").Trim(), ob = (e.Onboard ?? "").Trim();
                    string rawU;
                    if (e.Src > 0)
                        rawU = text.Substring(listSpan[e.Src - 1][0], listSpan[e.Src - 1][1] - listSpan[e.Src - 1][0]);
                    else
                    {
                        string uname = nm != "" ? nm : ag.Name + "-" + (r + 1);
                        if (nm == "" && ix.UnitNames.ContainsKey(uname)) { message = "A unit named '" + uname + "' already exists in the mission: give the new aircraft a name."; return false; }
                        if (ob == "" || ix.Onboard.ContainsKey(ob)) ob = NextOnboard(Str(last, "onboard_num"), onboard);
                        else onboard.Add(ob);
                        nextId++;
                        px += dx; py += dy;
                        rawU = CloneUnit(lastRaw, uname, nextId, ob, px, py, r + 1, null);
                        nm = uname;
                    }
                    raws.Add(EditUnitText(rawU, nm, cs, ob));
                }

                // Places / positions / caps des avions (FARP) et 1er waypoint si le vol part maintenant du sol
                TextEdit p1Edit = null;
                if (lay != null)
                {
                    for (int i = 0; i < raws.Count; i++) raws[i] = PlaceUnit(raws[i], lay.Units[i]);

                    if (lay.Ground)
                    {
                        Dictionary<string, object> pts = Sub(Sub(ag.G, "route"), "points");
                        Dictionary<string, int[]> pspans;
                        int[] p1sp;
                        if (pts == null || !spans.TryGetValue(pts, out pspans) || !pspans.TryGetValue("1", out p1sp)) { message = "The first waypoint of '" + flightName + "' could not be located."; return false; }
                        p1Edit = new TextEdit(p1sp[0], p1sp[1], GroundStartPoint(text.Substring(p1sp[0], p1sp[1] - p1sp[0]), lay.Units[0].X, lay.Units[0].Y));
                    }
                }

                // La nouvelle table "units" remplace l'ancienne
                string ind0 = LineIndent(text, unitsSpan[0]);
                string ind1 = LineIndent(text, listSpan[0][0]);
                var tb = new StringBuilder();
                tb.Append("{").Append(nl);
                for (int i = 0; i < raws.Count; i++)
                {
                    tb.Append(ind1).Append("[").Append(i + 1).Append("] = ").Append(nl);
                    tb.Append(ind1).Append(raws[i]).Append(",").Append(nl);
                }
                tb.Append(ind0).Append("}");

                var finalEdits = new List<TextEdit> { new TextEdit(unitsSpan[0], unitsSpan[1], tb.ToString()) };
                if (p1Edit != null) finalEdits.Add(p1Edit);
                string newText = ApplyEdits(text, finalEdits);

                // Contrôle : le nouveau texte se relit, le vol a le bon nombre d'avions, tous les ids sont uniques
                var checkSpans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> check = ParseMissionText(newText, checkSpans);
                if (check == null || Sub(check, "coalition") == null) { message = "The new text could not be read back: nothing was written."; return false; }
                AirGroup ag2 = ListAirGroups(check, checkSpans).FirstOrDefault(a => a.Name == flightName);
                Dictionary<string, object> units2 = ag2 == null ? null : Sub(ag2.G, "units");
                if (units2 == null || units2.Count != newCount) { message = "Check failed (aircraft count after the change): nothing was written."; return false; }
                MissionIndex ix2 = BuildIndex(check);
                if (ix2.UnitIds.Values.Any(c => c > 1) || ix2.GroupIds.Values.Any(c => c > 1)) { message = "Check failed (duplicate ids after the change): nothing was written."; return false; }

                WriteMissionText(file, newText, isMiz);
                message = "'" + flightName + "': " + newCount + " aircraft" + (newCount != n ? " (was " + n + ")" : "") + "."
                    + (lay != null && lay.Ground && wasParking ? " The pad has only " + lay.Capacity + " parking slot" + (lay.Capacity > 1 ? "s" : "") + ": the whole flight now starts on the ground around it." : "")
                    + " The briefing text and the DCE squadron roster are not changed.";
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | apply units : " + ex);
                message = "Could not change the flight: " + ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  Fréquence "flight" d'un vol (clé DCE_FreqFlight du groupe : la radio entre les avions du vol, pas celle du package)
        //  Même méthode : seul ce nombre est remplacé dans le texte (ou ajouté si le groupe n'en a pas), puis le fichier est relu pour contrôle.
        // ------------------------------------------------------------------
        internal static bool ApplyFlightFreq(string file, string flightName, double freq, out string message)
        {
            message = "";
            try
            {
                if (freq < 1 || freq > 500) { message = "Bad frequency: " + freq.ToString(CultureInfo.InvariantCulture) + " MHz."; return false; }

                bool isMiz;
                string text = ReadMissionText(file, out isMiz);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                if (root == null || Sub(root, "coalition") == null) { message = "The mission file has no coalition table."; return false; }

                AirGroup ag = ListAirGroups(root, spans).FirstOrDefault(a => a.Name == flightName);
                if (ag == null) { message = "Flight '" + flightName + "' not found in the file."; return false; }

                string val = freq.ToString("0.###", CultureInfo.InvariantCulture);
                Dictionary<string, int[]> gs = spans[ag.G];
                int[] s;
                TextEdit ed;
                if (gs.TryGetValue("DCE_FreqFlight", out s)) ed = new TextEdit(s[0], s[1], val);
                else if (gs.TryGetValue("{", out s)) ed = new TextEdit(s[0], s[0], " [\"DCE_FreqFlight\"] = " + val + ",");
                else { message = "The group of '" + flightName + "' could not be located."; return false; }

                string newText = ApplyEdits(text, new List<TextEdit> { ed });

                var checkSpans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> check = ParseMissionText(newText, checkSpans);
                AirGroup ag2 = check == null ? null : ListAirGroups(check, checkSpans).FirstOrDefault(a => a.Name == flightName);
                if (ag2 == null || Math.Abs(Num(ag2.G, "DCE_FreqFlight") - freq) > 0.0005) { message = "Check failed (flight frequency): nothing was written."; return false; }

                WriteMissionText(file, newText, isMiz);
                FormUtils.LogRegister("Map radio | flight radio written | " + flightName + " | " + val + " MHz | " + (gs.ContainsKey("DCE_FreqFlight") ? "value replaced" : "key added"));
                message = "'" + flightName + "': flight radio " + val + " MHz. Briefing text and player radio presets are not changed.";
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | flight frequency : " + ex);
                message = "Could not change the flight frequency: " + ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  Presets radio des avions "joueur" / "client" : Radio[n].channels de chaque avion du vol
        //  Même méthode : seuls les nombres changés sont remplacés dans le texte (jamais de nouvelle table), puis le fichier est relu pour contrôle.
        // ------------------------------------------------------------------

        // Les tables "channels" des radios d'un avion (Radio[1], Radio[2]...) ; null si une radio n'a pas de channels
        private static List<Dictionary<string, object>> UnitRadioTables(Dictionary<string, object> u)
        {
            var res = new List<Dictionary<string, object>>();
            Dictionary<string, object> radio = Sub(u, "Radio");
            if (radio == null) return res;
            for (int n = 1; ; n++)
            {
                Dictionary<string, object> r = Sub(radio, n.ToString(CultureInfo.InvariantCulture));
                if (r == null) break;
                res.Add(Sub(r, "channels"));
            }
            return res;
        }

        // Presets du vol : une liste de fréquences par radio, prise sur le premier avion qui a une table Radio (liste vide = pas de presets).
        // Lu sur le groupe déjà chargé en mémoire (Map_Flight.Source) : instantané, le fichier n'est pas relu.
        // unitsWithRadio = nombre d'avions du vol qui seront modifiés ; firstKeys = numéro du 1er canal de chaque radio
        // (1 en général, mais 0 pour certains avions russes comme le MiG-21).
        internal static List<List<double>> ReadPresetsOf(Dictionary<string, object> group, out int unitsWithRadio, out List<int> firstKeys)
        {
            unitsWithRadio = 0;
            firstKeys = new List<int>();
            var res = new List<List<double>>();
            Dictionary<string, object> units = Sub(group, "units");
            if (units == null) return res;

            for (int i = 1; i <= units.Count; i++)
            {
                List<Dictionary<string, object>> tables = UnitRadioTables(Sub(units, i.ToString(CultureInfo.InvariantCulture)));
                if (tables.Count == 0) continue;
                unitsWithRadio++;
                if (res.Count > 0) continue;
                foreach (Dictionary<string, object> ch in tables)
                {
                    var list = new List<double>();
                    int b0 = ch != null && ch.ContainsKey("0") ? 0 : 1;
                    if (ch != null)
                        for (int k = b0; ch.ContainsKey(k.ToString(CultureInfo.InvariantCulture)); k++) list.Add(Num(ch, k.ToString(CultureInfo.InvariantCulture)));
                    res.Add(list);
                    firstKeys.Add(b0);
                }
            }
            return res;
        }

        // Écrit les presets voulus (une liste par radio) dans TOUS les avions du vol qui ont une table Radio.
        // Le nombre de canaux ne change jamais : on remplace seulement la fréquence des canaux qui existent (le 1er canal est 0 ou 1 selon l'avion).
        internal static bool ApplyPresets(string file, string flightName, List<List<double>> wanted, out string message)
        {
            message = "";
            try
            {
                bool isMiz;
                string text = ReadMissionText(file, out isMiz);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                if (root == null || Sub(root, "coalition") == null) { message = "The mission file has no coalition table."; return false; }

                AirGroup ag = ListAirGroups(root, spans).FirstOrDefault(a => a.Name == flightName);
                if (ag == null) { message = "Flight '" + flightName + "' not found in the file."; return false; }
                Dictionary<string, object> units = Sub(ag.G, "units");
                if (units == null) { message = "The units table of '" + flightName + "' could not be located."; return false; }

                var edits = new List<TextEdit>();
                int nUnits = 0;
                for (int i = 1; i <= units.Count; i++)
                {
                    List<Dictionary<string, object>> tables = UnitRadioTables(Sub(units, i.ToString(CultureInfo.InvariantCulture)));
                    bool touched = false;
                    for (int n = 0; n < tables.Count && n < wanted.Count; n++)
                    {
                        Dictionary<string, object> ch = tables[n];
                        Dictionary<string, int[]> cs;
                        if (ch == null || !spans.TryGetValue(ch, out cs)) continue;
                        int b0 = ch.ContainsKey("0") ? 0 : 1;
                        for (int k = 0; k < wanted[n].Count; k++)
                        {
                            string key = (b0 + k).ToString(CultureInfo.InvariantCulture);
                            int[] sp;
                            if (!ch.ContainsKey(key) || !cs.TryGetValue(key, out sp)) continue;
                            if (Math.Abs(Num(ch, key) - wanted[n][k]) <= 0.0004) continue;
                            if (wanted[n][k] < 1 || wanted[n][k] > 500) { message = "Bad frequency in the presets: " + wanted[n][k].ToString(CultureInfo.InvariantCulture) + " MHz."; return false; }
                            edits.Add(new TextEdit(sp[0], sp[1], wanted[n][k].ToString("0.######", CultureInfo.InvariantCulture)));
                            touched = true;
                        }
                    }
                    if (touched) nUnits++;
                }
                if (edits.Count == 0) { message = "'" + flightName + "': radio presets already up to date."; return true; }

                string newText = ApplyEdits(text, edits);

                // Contrôle : on relit le texte, chaque canal voulu doit y être
                var checkSpans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> check = ParseMissionText(newText, checkSpans);
                AirGroup ag2 = check == null ? null : ListAirGroups(check, checkSpans).FirstOrDefault(a => a.Name == flightName);
                Dictionary<string, object> units2 = ag2 == null ? null : Sub(ag2.G, "units");
                if (units2 == null || units2.Count != units.Count) { message = "Check failed (radio presets): nothing was written."; return false; }
                for (int i = 1; i <= units2.Count; i++)
                {
                    List<Dictionary<string, object>> tables = UnitRadioTables(Sub(units2, i.ToString(CultureInfo.InvariantCulture)));
                    for (int n = 0; n < tables.Count && n < wanted.Count; n++)
                    {
                        if (tables[n] == null) continue;
                        int b0 = tables[n].ContainsKey("0") ? 0 : 1;
                        for (int k = 0; k < wanted[n].Count; k++)
                        {
                            string key = (b0 + k).ToString(CultureInfo.InvariantCulture);
                            if (tables[n].ContainsKey(key) && Math.Abs(Num(tables[n], key) - wanted[n][k]) > 0.0004) { message = "Check failed (radio presets): nothing was written."; return false; }
                        }
                    }
                }

                WriteMissionText(file, newText, isMiz);
                FormUtils.LogRegister("Map radio | presets written | " + flightName + " | " + edits.Count + " channel(s) in " + nUnits + " aircraft");
                message = "'" + flightName + "': " + edits.Count + " radio preset(s) changed in " + nUnits + " aircraft.";
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | radio presets : " + ex);
                message = "Could not change the radio presets: " + ex.Message;
                return false;
            }
        }

        // Même chose que PreviewUnits ci-dessous, mais sans relire le fichier à chaque clic : le groupe est déjà en mémoire (Map_Flight.Source).
        // Le texte de la mission n'est analysé (une fois, gardé en mémoire tant que le fichier ne change pas) que si le vol part d'un FARP / d'un pad :
        // sinon il n'y a rien à prévoir et la réponse est immédiate.
        internal static List<double[]> PreviewUnits(string file, Dictionary<string, object> group, int count, Func<string, double, double, List<double[]>> finder)
        {
            try
            {
                Dictionary<string, object> p1 = Sub(Sub(Sub(group, "route"), "points"), "1");
                if (p1 == null) return null;
                bool pad = (long)Num(p1, "linkUnit") != 0 || (long)Num(p1, "helipadId") != 0;
                bool ground = Str(p1, "type").StartsWith("TakeOffGround", StringComparison.Ordinal);
                if (!pad && !ground) return null;           // départ d'un terrain normal : rien à prévoir

                Dictionary<string, object> root = ReadMissionRoot(file);      // cache existant, relu seulement si le fichier a changé
                if (root == null) return null;

                LayoutPlan lay = MakeLayout(root, group, count, finder);
                if (lay == null) return null;
                var res = new List<double[]>();
                if (lay.Ground)
                    foreach (UnitPlan up in lay.Units) res.Add(new[] { up.X, up.Y, up.Hdg });
                return res;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | preview units : " + ex);
                return null;
            }
        }

        // ------------------------------------------------------------------
        //  Places d'hélicoptère "parkAlertSAR" de Init\db_airbases.lua ou Active\db_airbases.lua (carte Map et carte de mission)
        //  Lecture : x, y, heading (radians, facultatif) + les autres champs de chaque place, gardés tels quels.
        //  Écriture : seule la table parkAlertSAR des bases changées est remplacée dans le texte ; le reste du fichier n'est pas touché.
        // ------------------------------------------------------------------

        // Fichier .lua : UTF-8 si possible, sinon Latin-1 (aucun octet perdu à la réécriture)
        private static string ReadLuaFile(string file, out Encoding enc)
        {
            byte[] b = File.ReadAllBytes(file);
            bool bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF;
            int skip = bom ? 3 : 0;
            try
            {
                string s = new UTF8Encoding(false, true).GetString(b, skip, b.Length - skip);
                enc = new UTF8Encoding(bom);
                return s;
            }
            catch (Exception)
            {
                enc = Encoding.GetEncoding(28591);
                return enc.GetString(b);
            }
        }

        private static Dictionary<string, object> ParseDbAirbases(string text, Dictionary<object, Dictionary<string, int[]>> spans)
        {
            Match m = Regex.Match(text, @"(?m)^\s*db_airbases\s*=\s*\{");
            if (!m.Success) return null;
            return new LuaReader(text, m.Index + m.Length - 1, spans).ReadValue() as Dictionary<string, object>;
        }

        private static List<Map_ParkSpot> ParkSpotsOf(string baseName, Dictionary<string, object> baseTable, string text, Dictionary<object, Dictionary<string, int[]>> spans)
        {
            var list = new List<Map_ParkSpot>();
            Dictionary<string, object> park = Sub(baseTable, "parkAlertSAR");
            if (park == null) return list;

            var keys = park.Keys.Select(k => { double n; return new KeyValuePair<double, string>(double.TryParse(k, NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 1e9, k); })
                .OrderBy(k => k.Key).Select(k => k.Value).ToList();
            foreach (string k in keys)
            {
                Dictionary<string, object> sd = park[k] as Dictionary<string, object>;
                if (sd == null) continue;

                var s = new Map_ParkSpot { Base = baseName, X = Num(sd, "x"), Y = Num(sd, "y") };
                object h;
                if (sd.TryGetValue("heading", out h) && h is double) { s.Hdg = (double)h; s.HasHdg = true; }
                s.Remember();       // état du fichier : sert à retrouver les hélicos du .miz posés sur la place si elle bouge

                Dictionary<string, int[]> ss;
                if (spans != null && spans.TryGetValue(sd, out ss))
                    foreach (string key in sd.Keys)
                    {
                        int[] sp;
                        if (key == "x" || key == "y" || key == "heading" || !ss.TryGetValue(key, out sp)) continue;
                        s.Extra.Add(new KeyValuePair<string, string>(key, text.Substring(sp[0], sp[1] - sp[0]).Trim()));
                    }
                list.Add(s);
            }
            return list;
        }

        // Toutes les places de la campagne : nom de la base -> liste (les bases sans place n'y sont pas)
        internal static Dictionary<string, List<Map_ParkSpot>> ReadParkSpots(string file)
        {
            var res = new Dictionary<string, List<Map_ParkSpot>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(file)) return res;
                Encoding enc;
                string text = ReadLuaFile(file, out enc);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseDbAirbases(text, spans);
                if (root == null) return res;

                foreach (KeyValuePair<string, object> kv in root)
                {
                    Dictionary<string, object> bt = kv.Value as Dictionary<string, object>;
                    if (bt == null) continue;
                    List<Map_ParkSpot> spots = ParkSpotsOf(kv.Key, bt, text, spans);
                    if (spots.Count > 0) res[kv.Key] = spots;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | park spots read : " + ex);
            }
            return res;
        }

        private static string ParkTableText(List<Map_ParkSpot> spots, string ind, bool bracket, string nl)
        {
            Func<string, string> K = n => bracket ? "[\"" + n + "\"]" : n;
            var sb = new StringBuilder();
            sb.Append("{").Append(nl);
            for (int i = 0; i < spots.Count; i++)
            {
                Map_ParkSpot s = spots[i];
                double h = ((s.Hdg % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
                sb.Append(ind).Append("\t[").Append(i + 1).Append("] = { ")
                    .Append(K("x")).Append(" = ").Append(LuaNum(s.X)).Append(", ")
                    .Append(K("y")).Append(" = ").Append(LuaNum(s.Y)).Append(", ")
                    .Append(K("heading")).Append(" = ").Append(Math.Round(h, 5).ToString("0.#####", CultureInfo.InvariantCulture));
                foreach (KeyValuePair<string, string> x in s.Extra)
                    sb.Append(", ").Append(K(x.Key)).Append(" = ").Append(x.Value);
                sb.Append(" },").Append(nl);
            }
            sb.Append(ind).Append("}");
            return sb.ToString();
        }

        // Remplace la table parkAlertSAR de chaque base de "wanted" (liste vide = la clé est retirée). Copie de sécurité : <fichier>.park_backup (la première fois).
        internal static bool ApplyParkSpots(string file, Dictionary<string, List<Map_ParkSpot>> wanted, out string message)
        {
            message = "";
            try
            {
                if (!File.Exists(file)) { message = "The file does not exist:\n" + file; return false; }
                Encoding enc;
                string text = ReadLuaFile(file, out enc);
                string nl = text.Contains("\r\n") ? "\r\n" : "\n";
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseDbAirbases(text, spans);
                if (root == null) { message = "No db_airbases table found in the file."; return false; }

                var edits = new List<TextEdit>();
                int nBases = 0, nSpots = 0;
                foreach (KeyValuePair<string, List<Map_ParkSpot>> kv in wanted)
                {
                    string name = root.Keys.FirstOrDefault(k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase));
                    Dictionary<string, object> bt = name == null ? null : root[name] as Dictionary<string, object>;
                    Dictionary<string, int[]> bs;
                    if (bt == null || !spans.TryGetValue(bt, out bs)) { message = "Base '" + kv.Key + "' not found in " + Path.GetFileName(Path.GetDirectoryName(file)) + "\\db_airbases.lua."; return false; }

                    // indentation des champs de la base et style des clés (["x"] ou x)
                    int[] first = bs.Where(p => p.Key != "{").Select(p => p.Value).OrderBy(v => v[0]).FirstOrDefault();
                    string ind = first == null ? "\t\t" : LineIndent(text, first[0]);
                    int bEnd = bs.Where(p => p.Key != "{").Select(p => p.Value[1]).DefaultIfEmpty(bs["{"][0]).Max();
                    bool bracket = text.IndexOf("[\"", bs["{"][0], Math.Max(0, bEnd - bs["{"][0])) >= 0;

                    int[] sp;
                    bool has = bs.TryGetValue("parkAlertSAR", out sp);
                    if (kv.Value.Count == 0)
                    {
                        if (has)
                        {
                            // on retire toute la ligne de la clé (début de ligne -> fin de ligne après la virgule)
                            int ks = text.LastIndexOf("parkAlertSAR", sp[0], StringComparison.Ordinal);
                            if (ks < 0) { message = "Could not locate parkAlertSAR of '" + kv.Key + "'."; return false; }
                            int ls = text.LastIndexOf('\n', ks) + 1;
                            int e = sp[1];
                            while (e < text.Length && (text[e] == ' ' || text[e] == '\t')) e++;
                            if (e < text.Length && text[e] == ',') e++;
                            while (e < text.Length && (text[e] == ' ' || text[e] == '\t' || text[e] == '\r')) e++;
                            if (e < text.Length && text[e] == '\n') e++;
                            edits.Add(new TextEdit(ls, e, ""));
                            nBases++;
                        }
                        continue;
                    }

                    string table = ParkTableText(kv.Value, ind, bracket, nl);
                    if (has) edits.Add(new TextEdit(sp[0], sp[1], table));
                    else edits.Add(new TextEdit(bs["{"][0], bs["{"][0], nl + ind + (bracket ? "[\"parkAlertSAR\"]" : "parkAlertSAR") + " = " + table + ","));
                    nBases++;
                    nSpots += kv.Value.Count;
                }
                if (edits.Count == 0) { message = "Park spots: nothing to write."; return true; }

                string newText = ApplyEdits(text, edits);

                // Contrôle : on relit le texte, chaque base voulue doit contenir exactement les places voulues
                var cs = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> check = ParseDbAirbases(newText, cs);
                if (check == null || check.Count != root.Count) { message = "Check failed (park spots): nothing was written."; return false; }
                foreach (KeyValuePair<string, List<Map_ParkSpot>> kv in wanted)
                {
                    string name = check.Keys.FirstOrDefault(k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase));
                    List<Map_ParkSpot> back = name == null ? new List<Map_ParkSpot>() : ParkSpotsOf(name, check[name] as Dictionary<string, object>, newText, cs);
                    bool ok = back.Count == kv.Value.Count;
                    for (int i = 0; ok && i < back.Count; i++)
                        ok = Math.Abs(back[i].X - kv.Value[i].X) < 0.01 && Math.Abs(back[i].Y - kv.Value[i].Y) < 0.01 && back[i].HasHdg;
                    if (!ok) { message = "Check failed (park spots of '" + kv.Key + "'): nothing was written."; return false; }
                }

                string backup = file + ".park_backup";
                if (!File.Exists(backup)) File.Copy(file, backup);
                File.WriteAllText(file, newText, enc);
                FormUtils.LogRegister("Map park | written | " + file + " | " + nBases + " base(s), " + nSpots + " spot(s)");
                message = "Park spots written: " + nBases + " base(s), " + nSpots + " spot(s) (copy kept in " + Path.GetFileName(backup) + ").";
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | park spots write : " + ex);
                message = "Could not write the park spots: " + ex.Message;
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  Hélicoptères du fichier mission posés sur une place parkAlertSAR qui a bougé (carte de mission)
        //  Un hélico est "sur" une place si son point de départ est à moins de ParkMatchM mètres de l'ANCIENNE position de la place.
        //  Changés : x, y de l'hélico (et son cap : heading et psi si la place a un cap), x, y du groupe, x, y des waypoints qui étaient sur la place.
        //  Seuls les groupes d'hélicoptères sont touchés. Dry run : on compte seulement, rien n'est écrit.
        // ------------------------------------------------------------------
        internal class ParkMove
        {
            public double OldX, OldY, NewX, NewY, NewHdg;       // NewHdg en radians (0 = nord, sens horaire)
            public bool SetHdg;
        }

        private const double ParkMatchM = 3.0;

        private static ParkMove FindParkMove(List<ParkMove> moves, double x, double y)
        {
            ParkMove best = null;
            double bestD = ParkMatchM;
            foreach (ParkMove m in moves)
            {
                double d = Math.Sqrt((m.OldX - x) * (m.OldX - x) + (m.OldY - y) * (m.OldY - y));
                if (d <= bestD) { bestD = d; best = m; }
            }
            return best;
        }

        internal static bool MoveParkUnits(string file, List<ParkMove> moves, bool dryRun, out string message)
        {
            message = "";
            try
            {
                if (moves == null || moves.Count == 0) return true;
                if (!File.Exists(file)) { message = "The mission file does not exist:\n" + file; return false; }

                bool isMiz;
                string text = ReadMissionText(file, out isMiz);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                if (root == null) { message = "The mission file could not be read."; return false; }

                var edits = new List<TextEdit>();
                Func<Dictionary<string, object>, ParkMove, bool> moveXY = (t, m) =>
                {
                    Dictionary<string, int[]> ks;
                    int[] sx, sy;
                    if (t == null || !spans.TryGetValue(t, out ks) || !ks.TryGetValue("x", out sx) || !ks.TryGetValue("y", out sy)) return false;
                    edits.Add(new TextEdit(sx[0], sx[1], LuaNum(m.NewX)));
                    edits.Add(new TextEdit(sy[0], sy[1], LuaNum(m.NewY)));
                    return true;
                };

                int nUnits = 0, nGroups = 0, nPoints = 0;
                List<AirGroup> air = ListAirGroups(root, spans);
                foreach (AirGroup ag in air)
                {
                    if (ag.Category != "helicopter") continue;
                    Dictionary<string, object> us = Sub(ag.G, "units");
                    if (us == null) continue;

                    bool any = false;
                    foreach (object uo in us.Values)
                    {
                        var u = uo as Dictionary<string, object>;
                        if (u == null) continue;
                        ParkMove m = FindParkMove(moves, Num(u, "x"), Num(u, "y"));
                        if (m == null || !moveXY(u, m)) continue;

                        if (m.SetHdg)
                        {
                            Dictionary<string, int[]> uk = spans[u];
                            int[] sh, sp;
                            if (uk.TryGetValue("heading", out sh)) edits.Add(new TextEdit(sh[0], sh[1], LuaNum(m.NewHdg)));
                            if (uk.TryGetValue("psi", out sp)) edits.Add(new TextEdit(sp[0], sp[1], LuaNum(-m.NewHdg)));
                        }
                        nUnits++;
                        any = true;
                    }
                    if (!any) continue;
                    nGroups++;

                    ParkMove gm = FindParkMove(moves, Num(ag.G, "x"), Num(ag.G, "y"));
                    if (gm != null) moveXY(ag.G, gm);

                    Dictionary<string, object> pts = Sub(Sub(ag.G, "route"), "points");
                    if (pts == null) continue;
                    foreach (object po in pts.Values)
                    {
                        var p = po as Dictionary<string, object>;
                        if (p == null) continue;
                        ParkMove pm = FindParkMove(moves, Num(p, "x"), Num(p, "y"));
                        if (pm != null && moveXY(p, pm)) nPoints++;
                    }
                }

                if (nUnits == 0)
                {
                    message = "Mission: no helicopter was standing on a moved spot (nothing to change in the .miz).";
                    return true;
                }
                message = "Mission: " + nUnits + " helicopter(s) of " + nGroups + " group(s) moved with their spot (" + nPoints + " waypoint(s) too).";
                if (dryRun) return true;

                string newText = ApplyEdits(text, edits);

                // Contrôle : le texte se relit, et il y a toujours autant de groupes d'avions
                var cs = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> check = ParseMissionText(newText, cs);
                if (check == null || ListAirGroups(check, cs).Count != air.Count)
                {
                    message = "Check failed (helicopters on the spots): the mission file was not changed.";
                    return false;
                }

                WriteMissionText(file, newText, isMiz);
                FormUtils.LogRegister("Map park | mission | " + file + " | " + nUnits + " unit(s), " + nGroups + " group(s), " + nPoints + " waypoint(s)");
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | park units : " + ex);
                message = "Could not move the helicopters in the mission: " + ex.Message;
                return false;
            }
        }

        // Pour la carte : où seraient les avions si le vol avait "count" avions ? null = rien de particulier (pas de FARP),
        // liste vide = départ sur les places du pad (pas de silhouette), sinon un { x, y, cap } par avion (départ du sol autour du FARP).
        internal static List<double[]> PreviewUnits(string file, string flightName, int count, Func<string, double, double, List<double[]>> finder)
        {
            try
            {
                bool isMiz;
                string text = ReadMissionText(file, out isMiz);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                if (root == null) return null;
                AirGroup ag = ListAirGroups(root, spans).FirstOrDefault(a => a.Name == flightName);
                if (ag == null) return null;

                LayoutPlan lay = MakeLayout(root, ag.G, count, finder);
                if (lay == null) return null;
                var res = new List<double[]>();
                if (lay.Ground)
                    foreach (UnitPlan up in lay.Units) res.Add(new[] { up.X, up.Y, up.Hdg });
                return res;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | preview units : " + ex);
                return null;
            }
        }

        // ------------------------------------------------------------------
        //  Départ sur un FARP / une hélistation
        //  Le 1er waypoint pointe l'unité statique du FARP (linkUnit / helipadId). Places : FARP = 4 pads ; Invisible FARP et pad simple = 1.
        //  Départ "du sol" : type TakeOffGround / action "From Ground Area" (comme les vols SAR de DCE), sans linkUnit ni parking.
        //  Dimensions approximatives (non officielles) : empreinte du FARP = carré de 2 x FarpHalfM, pads aux coins à FarpPadM du centre.
        // ------------------------------------------------------------------
        internal const double FarpHalfM = 35;
        internal const double FarpPadM = 20;
        private const double GroundRingM = 40;      // rayon du cercle de départ autour du FARP : au moins 30 m entre deux hélicos

        private class UnitPlan
        {
            public double X, Y, Hdg;
            public int Slot;            // place de parking (1...), 0 = départ du sol
        }

        private class LayoutPlan
        {
            public bool Ground;
            public string PadName = "", PadType = "";
            public int Capacity;
            public double PadX, PadY, PadHdg;
            public List<UnitPlan> Units = new List<UnitPlan>();
        }

        private static int PadCapacity(string type)
        {
            return string.Equals(type, "FARP", StringComparison.OrdinalIgnoreCase) ? 4 : 1;
        }

        // Décalage (nord, est) du pad numéro "slot" par rapport au centre du FARP
        private static double[] PadOffset(int slot, int capacity)
        {
            if (capacity <= 1) return new[] { 0.0, 0.0 };
            switch (slot)
            {
                case 1: return new[] { FarpPadM, -FarpPadM };
                case 2: return new[] { FarpPadM, FarpPadM };
                case 3: return new[] { -FarpPadM, FarpPadM };
                default: return new[] { -FarpPadM, -FarpPadM };
            }
        }

        private static IEnumerable<Dictionary<string, object>> HelipadUnits(Dictionary<string, object> root)
        {
            Dictionary<string, object> coalitions = Sub(root, "coalition");
            if (coalitions == null) yield break;
            foreach (object co in coalitions.Values)
            {
                Dictionary<string, object> countries = Sub(co as Dictionary<string, object>, "country");
                if (countries == null) continue;
                foreach (object cyo in countries.Values)
                {
                    Dictionary<string, object> groups = Sub(Sub(cyo as Dictionary<string, object>, "static"), "group");
                    if (groups == null) continue;
                    foreach (object go in groups.Values)
                    {
                        Dictionary<string, object> us = Sub(go as Dictionary<string, object>, "units");
                        if (us == null) continue;
                        foreach (object uo in us.Values)
                        {
                            var u = uo as Dictionary<string, object>;
                            if (u != null && Str(u, "category") == "Heliports") yield return u;
                        }
                    }
                }
            }
        }

        private static Dictionary<string, object> FindHelipad(Dictionary<string, object> root, Dictionary<string, object> p1)
        {
            if (p1 == null) return null;
            long id = (long)Num(p1, "linkUnit");
            if (id == 0) id = (long)Num(p1, "helipadId");
            if (id == 0) return null;
            return HelipadUnits(root).FirstOrDefault(u => (long)Num(u, "unitId") == id);
        }

        private static Dictionary<string, object> FindPadNear(Dictionary<string, object> root, double x, double y, double maxM)
        {
            Dictionary<string, object> best = null;
            double bestD = maxM;
            foreach (Dictionary<string, object> u in HelipadUnits(root))
            {
                double d = Math.Sqrt((Num(u, "x") - x) * (Num(u, "x") - x) + (Num(u, "y") - y) * (Num(u, "y") - y));
                if (d <= bestD) { bestD = d; best = u; }
            }
            return best;
        }

        // Où vont les "count" avions d'un vol qui part d'un FARP ? null = le vol ne part pas d'un FARP (rien de spécial à faire)
        private static LayoutPlan MakeLayout(Dictionary<string, object> root, Dictionary<string, object> g, int count, Func<string, double, double, List<double[]>> finder)
        {
            Dictionary<string, object> p1 = Sub(Sub(Sub(g, "route"), "points"), "1");
            Dictionary<string, object> units = Sub(g, "units");
            if (p1 == null || units == null) return null;

            bool parkingStart = units.Values.Any(uo => { var u = uo as Dictionary<string, object>; return u != null && u.ContainsKey("parking"); });
            Dictionary<string, object> pad = null;
            bool groundStart = false;
            if (parkingStart) pad = FindHelipad(root, p1);
            else if (Str(p1, "type").StartsWith("TakeOffGround", StringComparison.Ordinal))
            {
                Dictionary<string, object> u1 = Sub(units, "1");
                if (u1 != null) pad = FindPadNear(root, Num(u1, "x"), Num(u1, "y"), 150);
                groundStart = pad != null;
            }
            if (pad == null) return null;

            var lp = new LayoutPlan { PadName = Str(pad, "name"), PadType = Str(pad, "type"), PadX = Num(pad, "x"), PadY = Num(pad, "y"), PadHdg = Num(pad, "heading") };
            lp.Capacity = PadCapacity(lp.PadType);

            if (!groundStart && count <= lp.Capacity)
            {
                for (int s = 1; s <= count; s++)
                {
                    double[] off = PadOffset(s, lp.Capacity);
                    lp.Units.Add(new UnitPlan { Slot = s, X = lp.PadX + off[0], Y = lp.PadY + off[1], Hdg = lp.PadHdg });
                }
                return lp;
            }

            // Départ du sol : points SAR de la base s'il y en a assez (à moins de 400 m), sinon cercle autour du centre
            lp.Ground = true;
            List<double[]> spots = finder == null ? null : finder(lp.PadName, lp.PadX, lp.PadY);
            List<double[]> near = spots == null ? new List<double[]>()
                : spots.Where(s => Math.Sqrt((s[0] - lp.PadX) * (s[0] - lp.PadX) + (s[1] - lp.PadY) * (s[1] - lp.PadY)) <= 400)
                       .OrderBy(s => (s[0] - lp.PadX) * (s[0] - lp.PadX) + (s[1] - lp.PadY) * (s[1] - lp.PadY)).ToList();

            for (int i = 0; i < count; i++)
            {
                double ux, uy;
                if (near.Count >= count) { ux = near[i][0]; uy = near[i][1]; }
                else
                {
                    double ang = Math.PI / 4 + 2 * Math.PI * i / count;
                    ux = lp.PadX + GroundRingM * Math.Cos(ang);
                    uy = lp.PadY + GroundRingM * Math.Sin(ang);
                }

                double hdg = lp.PadHdg;
                if (Math.Abs(lp.PadX - ux) + Math.Abs(lp.PadY - uy) > 1) hdg = Math.Atan2(lp.PadY - uy, lp.PadX - ux);      // nez vers le centre du FARP
                if (hdg < 0) hdg += 2 * Math.PI;
                lp.Units.Add(new UnitPlan { Slot = 0, X = ux, Y = uy, Hdg = hdg });
            }
            return lp;
        }

        // Retire la clé "key" (ligne entière) d'une table de premier niveau dont on a les positions
        private static TextEdit KeyRemoval(string raw, Dictionary<string, int[]> ks, string key)
        {
            int[] s;
            if (!ks.TryGetValue(key, out s)) return null;
            int a = raw.LastIndexOf("[\"" + key + "\"]", s[0], StringComparison.Ordinal);
            if (a < 0) return null;

            int e = s[1];
            while (e < raw.Length && (raw[e] == ' ' || raw[e] == '\t')) e++;
            if (e < raw.Length && raw[e] == ',') e++;

            int ls = a;
            while (ls > 0 && (raw[ls - 1] == ' ' || raw[ls - 1] == '\t')) ls--;
            if (ls == 0 || raw[ls - 1] == '\n')
            {
                int e2 = e;
                while (e2 < raw.Length && (raw[e2] == ' ' || raw[e2] == '\t')) e2++;
                if (e2 < raw.Length && raw[e2] == '\r') e2++;
                if (e2 < raw.Length && raw[e2] == '\n') { a = ls; e = e2 + 1; }
            }
            return new TextEdit(a, e, "");
        }

        // Un avion à sa place : position, cap (heading et psi), et place de parking ou, pour un départ du sol, plus de parking
        private static string PlaceUnit(string raw, UnitPlan up)
        {
            var sp = new Dictionary<object, Dictionary<string, int[]>>();
            var t = new LuaReader(raw, 0, sp).ReadValue() as Dictionary<string, object>;
            if (t == null) return raw;

            Dictionary<string, int[]> ks = sp[t];
            var edits = new List<TextEdit>();
            Action<string, string> put = (key, value) =>
            {
                int[] s;
                if (ks.TryGetValue(key, out s)) edits.Add(new TextEdit(s[0], s[1], value));
            };
            put("x", LuaNum(up.X));
            put("y", LuaNum(up.Y));
            put("heading", LuaNum(up.Hdg));
            put("psi", LuaNum(-up.Hdg));
            if (up.Slot > 0)
            {
                put("parking", LuaText(up.Slot.ToString(CultureInfo.InvariantCulture)));
                put("parking_id", LuaText(up.Slot.ToString(CultureInfo.InvariantCulture)));
            }
            else
                foreach (string key in new[] { "parking", "parking_id" })
                {
                    TextEdit r = KeyRemoval(raw, ks, key);
                    if (r != null) edits.Add(r);
                }
            return ApplyEdits(raw, edits);
        }

        // 1er waypoint en départ du sol (à chaud si c'était à chaud)
        private static string GroundStartPoint(string raw, double x, double y)
        {
            var sp = new Dictionary<object, Dictionary<string, int[]>>();
            var t = new LuaReader(raw, 0, sp).ReadValue() as Dictionary<string, object>;
            if (t == null) return raw;

            Dictionary<string, int[]> ks = sp[t];
            var edits = new List<TextEdit>();
            bool hot = Str(t, "action").IndexOf("Hot", StringComparison.OrdinalIgnoreCase) >= 0;
            Action<string, string> put = (key, value) =>
            {
                int[] s;
                if (ks.TryGetValue(key, out s)) edits.Add(new TextEdit(s[0], s[1], value));
            };
            put("type", LuaText(hot ? "TakeOffGroundHot" : "TakeOffGround"));
            put("action", LuaText(hot ? "From Ground Area Hot" : "From Ground Area"));
            put("x", LuaNum(x));
            put("y", LuaNum(y));
            put("alt", "0");
            put("alt_type", LuaText("RADIO"));
            foreach (string key in new[] { "linkUnit", "helipadId", "airdromeId" })
            {
                TextEdit r = KeyRemoval(raw, ks, key);
                if (r != null) edits.Add(r);
            }
            return ApplyEdits(raw, edits);
        }

        // Prochain numéro de queue libre après celui du modèle (même nombre de chiffres)
        private static string NextOnboard(string model, HashSet<string> used)
        {
            int digits = model.Length >= 1 && model.All(char.IsDigit) ? model.Length : 3;
            int start;
            if (!int.TryParse(model, NumberStyles.None, CultureInfo.InvariantCulture, out start)) start = 100;
            int max = (int)Math.Pow(10, digits) - 1;
            for (int i = 1; i <= max; i++)
            {
                int v = ((start - 1 + i) % max) + 1;
                string s = v.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
                if (used.Add(s)) return s;
            }
            return model;
        }

        // Copie du texte d'un avion avec son nom, unitId, numéro de queue, position et numéro d'indicatif changés
        private static string CloneUnit(string raw, string name, long unitId, string onboard, double x, double y, int k, string parkingSlot)
        {
            var sp = new Dictionary<object, Dictionary<string, int[]>>();
            var t = new LuaReader(raw, 0, sp).ReadValue() as Dictionary<string, object>;
            if (t == null) return raw;

            var edits = new List<KeyValuePair<int[], string>>();
            Dictionary<string, int[]> ks = sp[t];
            Action<string, string> put = (key, value) =>
            {
                int[] s;
                if (ks.TryGetValue(key, out s)) edits.Add(new KeyValuePair<int[], string>(s, value));
            };

            put("name", LuaText(name));
            put("unitId", unitId.ToString(CultureInfo.InvariantCulture));
            put("onboard_num", LuaText(onboard));
            put("x", LuaNum(x));
            put("y", LuaNum(y));
            if (parkingSlot != null) { put("parking", LuaText(parkingSlot)); put("parking_id", LuaText(parkingSlot)); }
            if (IsPlayerSkill(Str(t, "skill"))) put("skill", LuaText("High"));      // le joueur reste unique : la copie est un avion IA

            Dictionary<string, object> cs = Sub(t, "callsign");
            Dictionary<string, int[]> cks;
            if (cs != null && sp.TryGetValue(cs, out cks))
            {
                // Indicatif "Colt91" = nom "Colt" + n° du vol (2e chiffre) + n° de l'avion (3e chiffre)
                string prefix = Regex.Replace(Str(cs, "name"), "\\d+$", "");
                int flightNo = (int)Num(cs, "2");
                int[] s;
                if (cks.TryGetValue("3", out s)) edits.Add(new KeyValuePair<int[], string>(s, k.ToString(CultureInfo.InvariantCulture)));
                if (cks.TryGetValue("name", out s)) edits.Add(new KeyValuePair<int[], string>(s, LuaText(prefix + flightNo + k)));
            }

            edits.Sort((a, b) => b.Key[0].CompareTo(a.Key[0]));
            var sb = new StringBuilder(raw);
            foreach (KeyValuePair<int[], string> e in edits)
            {
                sb.Remove(e.Key[0], e.Key[1] - e.Key[0]);
                sb.Insert(e.Key[0], e.Value);
            }
            return sb.ToString();
        }
        // ------------------------------------------------------------------
        //  3) SUPPRIMER / DÉSACTIVER DES VOLS
        //  Supprimer : le groupe disparaît, les triggers qui le citent sont nettoyés, les vols qui le suivent (Follow / escorte)
        //              sont re-pointés sur un autre vol du même package (sinon laissés tels quels : ils pointent dans le vide).
        //  Désactiver : le groupe reste dans le fichier (ids, noms) mais passe en "late activation" et plus rien ne l'active.
        //  dryRun = true : ne modifie rien, renvoie seulement ce que ça ferait.
        // ------------------------------------------------------------------
        private class TextEdit
        {
            public int S, E;
            public string T;
            public TextEdit(int s, int e, string t) { S = s; E = e; T = t; }
        }

        private static readonly Regex PackRx = new Regex(@"^(?:Recovery )?Pack (\d+) - (.+) - (\D+?) ?(\d+)$");
        private static readonly Regex PackNoRx = new Regex(@"^(?:Recovery )?Pack (\d+) - ");

        private static string ApplyEdits(string text, List<TextEdit> edits)
        {
            edits.Sort((a, b) => b.S.CompareTo(a.S));
            for (int i = 1; i < edits.Count; i++)
                if (edits[i].E > edits[i - 1].S) throw new InvalidOperationException("Overlapping edits in the mission text.");
            var sb = new StringBuilder(text);
            foreach (TextEdit e in edits) { sb.Remove(e.S, e.E - e.S); sb.Insert(e.S, e.T); }
            return sb.ToString();
        }

        // Tous les vols du même package que celui-ci (y compris "Recovery Pack N")
        internal static List<string> FlightsOfPackage(string file, string flightName)
        {
            var result = new List<string>();
            try
            {
                Match pm = PackNoRx.Match(flightName);
                if (!pm.Success) { result.Add(flightName); return result; }
                bool isMiz;
                string text = ReadMissionText(file, out isMiz);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                if (root == null) { result.Add(flightName); return result; }
                foreach (AirGroup a in ListAirGroups(root, spans))
                {
                    Match m = PackNoRx.Match(a.Name);
                    if (m.Success && m.Groups[1].Value == pm.Groups[1].Value) result.Add(a.Name);
                }
                if (!result.Contains(flightName)) result.Add(flightName);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | flights of package : " + ex);
                result.Clear();
                result.Add(flightName);
            }
            return result;
        }

        // Vol qui prend la suite d'un vol supprimé : même escadron et même tâche d'abord (n° suivant, puis précédent), puis le reste du package
        private static long FollowReplacement(string delName, long selfId, List<AirGroup> air, HashSet<string> deleted)
        {
            Match dm = PackRx.Match(delName);
            if (!dm.Success || delName.StartsWith("Recovery", StringComparison.Ordinal)) return -1;
            int k0 = int.Parse(dm.Groups[4].Value, CultureInfo.InvariantCulture);

            var order = new List<Tuple<int, int, string, long>>();
            foreach (AirGroup a in air)
            {
                if (deleted.Contains(a.Name) || a.Name.StartsWith("Recovery", StringComparison.Ordinal)) continue;
                Match m = PackRx.Match(a.Name);
                if (!m.Success || m.Groups[1].Value != dm.Groups[1].Value) continue;
                long id = (long)Num(a.G, "groupId");
                if (id == selfId) continue;
                bool same = m.Groups[2].Value == dm.Groups[2].Value && m.Groups[3].Value == dm.Groups[3].Value;
                int k = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                order.Add(Tuple.Create(same ? (k > k0 ? 0 : 1) : 2, Math.Abs(k - k0), a.Name, id));
            }
            if (order.Count == 0) return -1;
            order.Sort((x, y) =>
            {
                int c = x.Item1.CompareTo(y.Item1);
                if (c == 0) c = x.Item2.CompareTo(y.Item2);
                if (c == 0) c = string.CompareOrdinal(x.Item3, y.Item3);
                return c;
            });
            return order[0].Item4;
        }

        internal static bool RemoveFlights(string file, List<string> names, bool disableOnly, bool dryRun, out string message)
        {
            message = "";
            try
            {
                if (names == null || names.Count == 0) { message = "No flight to change."; return false; }

                bool isMiz;
                string text = ReadMissionText(file, out isMiz);
                var spans = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> root = ParseMissionText(text, spans);
                if (root == null || Sub(root, "coalition") == null) { message = "The mission file has no coalition table."; return false; }

                List<AirGroup> air = ListAirGroups(root, spans);
                var targets = new List<AirGroup>();
                foreach (string n in names.Distinct())
                {
                    AirGroup ag = air.FirstOrDefault(a => a.Name == n);
                    if (ag == null) { message = "Flight '" + n + "' not found in the file."; return false; }
                    Dictionary<string, object> us = Sub(ag.G, "units");
                    if (us != null)
                        foreach (object uo in us.Values)
                        {
                            var u = uo as Dictionary<string, object>;
                            if (u != null && IsPlayerSkill(Str(u, "skill"))) { message = "'" + n + "' is a player flight: it can not be deleted or disabled."; return false; }
                        }
                    targets.Add(ag);
                }

                var gids = new HashSet<long>(targets.Select(a => (long)Num(a.G, "groupId")));
                var deleted = new HashSet<string>(targets.Select(a => a.Name));
                int nAircraft = targets.Sum(a => Sub(a.G, "units") == null ? 0 : Sub(a.G, "units").Count);
                int nFollowMoved = 0, nFollowLost = 0, nRules = 0, nCompiled = 0;
                string nl = text.Contains("\r\n") ? "\r\n" : "\n";

                // --- A) les vols qui suivent / escortent un vol supprimé (seulement en suppression) ---
                if (!disableOnly)
                {
                    var idRx = new Regex("\\[\"groupId\"\\]\\s*=\\s*(\\d+)(?![\\d.])");
                    var byId = targets.ToDictionary(a => (long)Num(a.G, "groupId"), a => a.Name);
                    var editsA = new List<TextEdit>();
                    foreach (AirGroup o in air)
                    {
                        if (deleted.Contains(o.Name)) continue;
                        long selfId = (long)Num(o.G, "groupId");
                        string slice = text.Substring(o.Span[0], o.Span[1] - o.Span[0]);
                        foreach (Match m in idRx.Matches(slice))
                        {
                            long v = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                            string dn;
                            if (!byId.TryGetValue(v, out dn)) continue;
                            long r = FollowReplacement(dn, selfId, air, deleted);
                            if (r < 0) { nFollowLost++; continue; }
                            editsA.Add(new TextEdit(o.Span[0] + m.Groups[1].Index, o.Span[0] + m.Groups[1].Index + m.Groups[1].Length, r.ToString(CultureInfo.InvariantCulture)));
                            nFollowMoved++;
                        }
                    }
                    if (editsA.Count > 0)
                    {
                        text = ApplyEdits(text, editsA);
                        spans = new Dictionary<object, Dictionary<string, int[]>>();
                        root = ParseMissionText(text, spans);
                        air = ListAirGroups(root, spans);
                        targets = air.Where(a => deleted.Contains(a.Name)).ToList();
                    }
                }

                // --- B) triggers (règles lisibles + texte compilé) et, pour "désactiver", late activation ---
                var editsB = new List<TextEdit>();
                Dictionary<string, object> rules = Sub(root, "trigrules");
                if (rules != null)
                    foreach (KeyValuePair<string, object> kv in rules)
                    {
                        var rule = kv.Value as Dictionary<string, object>;
                        Dictionary<string, object> actions = Sub(rule, "actions");
                        Dictionary<string, int[]> rs, acs;
                        int[] aSpan;
                        if (actions == null || !spans.TryGetValue(rule, out rs) || !rs.TryGetValue("actions", out aSpan) || !spans.TryGetValue(actions, out acs)) continue;

                        var keep = new List<string>();
                        int removed = 0, counted = 0;
                        string ind1 = null;
                        for (int i = 1; ; i++)
                        {
                            object ao;
                            int[] sp;
                            string key = i.ToString(CultureInfo.InvariantCulture);
                            if (!actions.TryGetValue(key, out ao) || !acs.TryGetValue(key, out sp)) break;
                            counted++;
                            if (ind1 == null) ind1 = LineIndent(text, sp[0]);
                            var a = ao as Dictionary<string, object>;
                            bool hit = false;
                            if (a != null)
                            {
                                object go;
                                if (a.TryGetValue("group", out go) && go is double && gids.Contains((long)(double)go)) hit = true;
                                else if (a.TryGetValue("set_ai_task", out go))
                                {
                                    var st = go as Dictionary<string, object>;
                                    if (st != null && gids.Contains((long)Num(st, "1"))) hit = true;
                                }
                            }
                            if (hit) removed++; else keep.Add(text.Substring(sp[0], sp[1] - sp[0]));
                        }
                        if (removed == 0 || counted != actions.Count) continue;

                        string ind0 = LineIndent(text, aSpan[0]);
                        if (ind1 == null) ind1 = ind0 + "\t";
                        var tb = new StringBuilder();
                        tb.Append("{").Append(nl);
                        for (int i = 0; i < keep.Count; i++)
                        {
                            tb.Append(ind1).Append("[").Append(i + 1).Append("] = ").Append(nl);
                            tb.Append(ind1).Append(keep[i]).Append(",").Append(nl);
                        }
                        tb.Append(ind0).Append("}");
                        editsB.Add(new TextEdit(aSpan[0], aSpan[1], tb.ToString()));
                        nRules++;
                    }

                Dictionary<string, object> compiled = Sub(Sub(root, "trig"), "actions");
                Dictionary<string, int[]> cps;
                if (compiled != null && spans.TryGetValue(compiled, out cps))
                    foreach (KeyValuePair<string, object> kv in compiled)
                    {
                        int[] sp;
                        if (!(kv.Value is string) || !cps.TryGetValue(kv.Key, out sp)) continue;
                        string slice = text.Substring(sp[0], sp[1] - sp[0]);
                        string cleaned = slice;
                        foreach (long gid in gids)
                            cleaned = Regex.Replace(cleaned, "a_(?:\\w*group\\w*|set_ai_task)\\(" + gid + "(?:,[^)]*)?\\);\\s?", "");
                        if (cleaned == slice) continue;
                        editsB.Add(new TextEdit(sp[0], sp[1], cleaned));
                        nCompiled++;
                    }

                if (disableOnly)
                    foreach (AirGroup ag in targets)
                    {
                        Dictionary<string, int[]> gs = spans[ag.G];
                        int[] sp;
                        if (gs.TryGetValue("lateActivation", out sp)) editsB.Add(new TextEdit(sp[0], sp[1], "true"));
                        else if (gs.TryGetValue("{", out sp)) editsB.Add(new TextEdit(sp[0], sp[0], " [\"lateActivation\"] = true,"));
                        else { message = "Could not locate '" + ag.Name + "' in the file."; return false; }
                    }

                if (editsB.Count > 0)
                {
                    text = ApplyEdits(text, editsB);
                    spans = new Dictionary<object, Dictionary<string, int[]>>();
                    root = ParseMissionText(text, spans);
                    air = ListAirGroups(root, spans);
                }

                // --- C) retrait des groupes (la liste "group" est renumérotée 1, 2, 3...) ---
                if (!disableOnly)
                {
                    var editsC = new List<TextEdit>();
                    foreach (KeyValuePair<string, object> co in Sub(root, "coalition"))
                    {
                        Dictionary<string, object> countries = Sub(co.Value as Dictionary<string, object>, "country");
                        if (countries == null) continue;
                        foreach (object cyo in countries.Values)
                        {
                            var country = cyo as Dictionary<string, object>;
                            foreach (string cat in new[] { "plane", "helicopter" })
                            {
                                Dictionary<string, object> catT = Sub(country, cat);
                                Dictionary<string, object> gl = Sub(catT, "group");
                                Dictionary<string, int[]> catSpans, gls;
                                int[] glSpan;
                                if (gl == null || !spans.TryGetValue(catT, out catSpans) || !catSpans.TryGetValue("group", out glSpan) || !spans.TryGetValue(gl, out gls)) continue;

                                var keep = new List<string>();
                                int removed = 0, counted = 0;
                                string ind1 = null;
                                for (int i = 1; ; i++)
                                {
                                    object go;
                                    int[] sp;
                                    string key = i.ToString(CultureInfo.InvariantCulture);
                                    if (!gl.TryGetValue(key, out go) || !gls.TryGetValue(key, out sp)) break;
                                    counted++;
                                    if (ind1 == null) ind1 = LineIndent(text, sp[0]);
                                    var g = go as Dictionary<string, object>;
                                    if (g != null && deleted.Contains(Str(g, "name"))) removed++;
                                    else keep.Add(text.Substring(sp[0], sp[1] - sp[0]));
                                }
                                if (removed == 0) continue;
                                if (counted != gl.Count) { message = "The group list of a country is not numbered 1, 2, 3... : nothing was written."; return false; }

                                string ind0 = LineIndent(text, glSpan[0]);
                                if (ind1 == null) ind1 = ind0 + "\t";
                                var tb = new StringBuilder();
                                tb.Append("{").Append(nl);
                                for (int i = 0; i < keep.Count; i++)
                                {
                                    tb.Append(ind1).Append("[").Append(i + 1).Append("] = ").Append(nl);
                                    tb.Append(ind1).Append(keep[i]).Append(",").Append(nl);
                                }
                                tb.Append(ind0).Append("}");
                                editsC.Add(new TextEdit(glSpan[0], glSpan[1], tb.ToString()));
                            }
                        }
                    }
                    if (editsC.Count == 0) { message = "The flights could not be located in the file."; return false; }
                    text = ApplyEdits(text, editsC);
                }

                // --- Contrôle : le texte se relit, les vols sont partis (ou désactivés), plus aucun trigger ne cite leurs ids, ids toujours uniques ---
                var cs2 = new Dictionary<object, Dictionary<string, int[]>>();
                Dictionary<string, object> check = ParseMissionText(text, cs2);
                if (check == null || Sub(check, "coalition") == null) { message = "The new text could not be read back: nothing was written."; return false; }
                List<AirGroup> air2 = ListAirGroups(check, cs2);
                foreach (string n in deleted)
                {
                    AirGroup still = air2.FirstOrDefault(a => a.Name == n);
                    if (!disableOnly && still != null) { message = "Check failed ('" + n + "' is still there): nothing was written."; return false; }
                    if (disableOnly && (still == null || Str(still.G, "lateActivation") != "true")) { message = "Check failed ('" + n + "' is not disabled): nothing was written."; return false; }
                }
                MissionIndex ix2 = BuildIndex(check);
                if (ix2.UnitIds.Values.Any(c => c > 1) || ix2.GroupIds.Values.Any(c => c > 1)) { message = "Check failed (duplicate ids): nothing was written."; return false; }
                Dictionary<string, object> rules2 = Sub(check, "trigrules");
                if (rules2 != null)
                    foreach (object ro in rules2.Values)
                    {
                        Dictionary<string, object> acts = Sub(ro as Dictionary<string, object>, "actions");
                        if (acts == null) continue;
                        foreach (object ao in acts.Values)
                        {
                            var a = ao as Dictionary<string, object>;
                            object go;
                            if (a != null && a.TryGetValue("group", out go) && go is double && gids.Contains((long)(double)go)) { message = "Check failed (a trigger still uses a removed group): nothing was written."; return false; }
                        }
                    }

                string what = disableOnly ? "disabled" : "deleted";
                string sum = targets.Count + " flight" + (targets.Count > 1 ? "s" : "") + " (" + nAircraft + " aircraft) " + (dryRun ? "would be " : "") + what + ". "
                    + nRules + " trigger rule" + (nRules == 1 ? "" : "s") + " cleaned.";
                if (!disableOnly) sum += " Follow / escort re-pointed to another flight of the package: " + nFollowMoved + "; left pointing at nothing: " + nFollowLost + ".";
                sum += " Not changed: briefing text, radio presets, Air Tasking Order, the DCE squadron roster.";

                if (!dryRun) WriteMissionText(file, text, isMiz);
                message = sum;
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | remove flights : " + ex);
                message = "Could not change the flights: " + ex.Message;
                return false;
            }
        }
    }
}
