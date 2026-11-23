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
    // Partie de Map_Drawings (voir Map_Drawings.cs) : écriture des waypoints dans le .miz
    internal static partial class Map_Drawings
    {
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
                int nbWp = 0, nbFlights = 0, nbHold = 0;
                var holdErrors = new List<string>();
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

                        // Départ en vol en avance sur l'heure fixe : orbite d'attente (fonction DCE OrbitPosition) sur ce waypoint (jamais le 0)
                        if (fl.StartAir && fl.HoldWp == wi && fl.HoldS >= Map_Flight.MinHoldS && SetHoldOrbit(p, spans, changes, fl, wi, holdErrors)) nbHold++;

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
                    + (nbAdded > 0 ? ", " + nbAdded + " waypoint(s) added" : "") + (nbRemoved > 0 ? ", " + nbRemoved + " deleted" : "")
                    + (nbHold > 0 ? ", " + nbHold + " hold orbit(s)" : "") + ").";
                if (holdErrors.Count > 0) message += " WARNING - hold orbit NOT written: " + string.Join("; ", holdErrors.ToArray()) + ".";
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Drawings | save waypoints : " + ex);
                message = "Could not write the mission file: " + ex.Message;
                return false;
            }
        }

        private static readonly System.Text.RegularExpressions.Regex RegexHoldOrbit = new System.Text.RegularExpressions.Regex(
            @"OrbitPosition\s*\(\s*(['""]).*?\1\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)", System.Text.RegularExpressions.RegexOptions.Singleline);

        // La table Lua qui contient la commande Script "OrbitPosition(...)" (fonction DCE), cherchée dans les tâches imbriquées
        private static Dictionary<string, object> FindOrbitCommandTable(Dictionary<string, object> node, int depth, out System.Text.RegularExpressions.Match match)
        {
            match = null;
            if (node == null || depth > 10) return null;
            foreach (KeyValuePair<string, object> kv in node)
            {
                var child = kv.Value as Dictionary<string, object>;
                if (child != null)
                {
                    Dictionary<string, object> found = FindOrbitCommandTable(child, depth + 1, out match);
                    if (found != null) return found;
                    continue;
                }
                string cmd = kv.Value as string;
                if (kv.Key == "command" && cmd != null)
                {
                    System.Text.RegularExpressions.Match m = RegexHoldOrbit.Match(cmd);
                    if (m.Success) { match = m; return node; }
                }
            }
            return null;
        }

        // Départ en vol : écrit l'attente sur le waypoint wi du vol.
        //  - une commande OrbitPosition existe déjà sur ce waypoint : seule son heure de fin change
        //  - sinon : une tâche Script OrbitPosition est ajoutée à la liste des tâches du waypoint (même forme que celle de DCE)
        // false = rien écrit (la raison est ajoutée à "errors")
        private static bool SetHoldOrbit(Dictionary<string, object> p, Dictionary<object, Dictionary<string, int[]>> spans,
            List<KeyValuePair<int[], string>> changes, Map_Flight fl, int wi, List<string> errors)
        {
            double until = fl.HoldUntil();
            if (until <= 0) return false;

            Dictionary<string, object> task = Sub(p, "task");
            System.Text.RegularExpressions.Match om;
            Dictionary<string, object> cmdTable = FindOrbitCommandTable(task, 0, out om);
            Dictionary<string, int[]> ks;

            if (cmdTable != null)
            {
                string cmd = Str(cmdTable, "command");
                var g4 = om.Groups[4];
                string newCmd = cmd.Substring(0, g4.Index) + LuaNum(until) + cmd.Substring(g4.Index + g4.Length);
                if (!spans.TryGetValue(cmdTable, out ks) || !ks.ContainsKey("command")) { errors.Add("WP " + wi + " of '" + fl.Name + "': orbit command not found in the text"); return false; }
                changes.Add(new KeyValuePair<int[], string>(ks["command"], LuaText(newCmd)));
                return true;
            }

            Dictionary<string, object> tasks = Sub(Sub(task, "params"), "tasks");
            if (tasks == null || !spans.TryGetValue(tasks, out ks) || !ks.ContainsKey("{")) { errors.Add("WP " + wi + " of '" + fl.Name + "': no task list"); return false; }
            if (fl.Name.IndexOfAny(new[] { '\'', '"', '\\' }) >= 0) { errors.Add("'" + fl.Name + "': quote in the flight name"); return false; }

            int num = 1;
            foreach (string k in tasks.Keys)
            {
                int kn;
                if (int.TryParse(k, out kn) && kn >= num) num = kn + 1;
            }
            Map_Waypoint wp = fl.Waypoints[wi];
            double speed = wp.Speed >= 50 ? wp.Speed : fl.NavSpeed();
            string command = "OrbitPosition('" + fl.Name + "', " + LuaNum(wp.Alt) + ", " + LuaNum(speed) + ", " + LuaNum(until) + ")";
            string entry = " [" + num + "] = { [\"number\"] = " + num + ", [\"auto\"] = false, [\"id\"] = \"WrappedAction\", [\"name\"] = \"orbit with DCE_Manager air start (hold)\", [\"enabled\"] = true, "
                + "[\"params\"] = { [\"action\"] = { [\"id\"] = \"Script\", [\"params\"] = { [\"command\"] = " + LuaText(command) + ", }, }, }, },";
            changes.Add(new KeyValuePair<int[], string>(new[] { ks["{"][0], ks["{"][0] }, entry));
            return true;
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
    }
}
