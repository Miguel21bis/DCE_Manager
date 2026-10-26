using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // ==========================================================================================
    // Trois outils autour du "pont" entre le wargame et l'ancien DCE :
    //
    //   WargameOldDce        : détruit les éléments de l'ancien DCE (oob_ground, bâtiments de la targetlist)
    //                          - zone capturée : tout ce qui est au sol est détruit, définitivement
    //                          - pertes simulées de la Preview en première ligne
    //   WargamePreviewExport : écrit l'état d'un tour de la Preview dans la campagne (Active), avec sauvegarde
    //                          et restauration, pour lancer ensuite SkipMission sur cette situation
    //   WargameLogistics     : points de ravitaillement avancés posés dans DCS, ordres et résultats des convois (scriptsMod)
    //   WargameLog           : journal du wargame, un petit fichier par action, dans <campagne>\Debug\Wargame (fichiers wargame_*.log + wargame_preview_log.txt de la Preview)
    //   DcsLogbook           : fait croire à DCS que la 1re mission de la campagne a été jouée
    //
    // Les fichiers DCE sont modifiés LIGNE PAR LIGNE (rien n'est réécrit en entier) : on repère les tableaux
    // par leur indentation (tabulations), exactement comme le sérialiseur de DCE les écrit.
    // ==========================================================================================

    // Index des lignes d'un fichier Lua écrit par DCE (une clé par ligne, tableaux indentés par tabulations).
    // Chemin d'un tableau = ses clés depuis la racine, séparées par "/" (ex. "blue/2/vehicle/group/1/units/3").
    internal class LuaLineIndex
    {
        public List<string> Lines;

        // tableau -> (clé de champ -> numéro de ligne) ; seulement les champs "simples" (pas les sous-tableaux)
        public Dictionary<string, Dictionary<string, int>> Fields = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        // tableau -> ligne de sa "}" fermante, et profondeur (nombre de tabulations) de sa clé
        public Dictionary<string, int> Close = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Depth = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<string> Tables = new List<string>();

        // [""clé""] = ..., [12] = ... ou clé = ... (les fichiers Init écrivent souvent les clés sans crochets)
        private static readonly Regex KeyRx = new Regex(@"^(\t*)(?:\[(?:""([^""]*)""|(-?\d+))\]|([A-Za-z_][A-Za-z0-9_]*))\s*=\s*(.*?)\s*$");
        private static readonly Regex CloseRx = new Regex(@"^(\t*)\}\s*,?\s*(--.*)?$");   // DCS ajoute parfois "-- end of [2]"

        public static LuaLineIndex Build(List<string> lines)
        {
            var idx = new LuaLineIndex { Lines = lines };
            var keys = new string[64];

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                Match m = KeyRx.Match(line);
                if (m.Success)
                {
                    int d = m.Groups[1].Length;
                    if (d < 1 || d >= 63) continue;

                    string key = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
                    string value = m.Groups[5].Value;

                    if (value.Length == 0 || value == "{")
                    {
                        keys[d] = key;
                        for (int k = d + 1; k < keys.Length; k++) keys[k] = null;

                        string path = PathOf(keys, d);
                        idx.Tables.Add(path);
                        idx.Depth[path] = d;
                    }
                    else
                    {
                        string parent = PathOf(keys, d - 1);
                        Dictionary<string, int> fields;
                        if (!idx.Fields.TryGetValue(parent, out fields))
                        {
                            fields = new Dictionary<string, int>(StringComparer.Ordinal);
                            idx.Fields[parent] = fields;
                        }
                        fields[key] = i;
                    }
                    continue;
                }

                Match c = CloseRx.Match(line);
                if (c.Success)
                {
                    int d = c.Groups[1].Length;
                    if (d >= 1 && d < 63 && keys[d] != null)
                        idx.Close[PathOf(keys, d)] = i;
                }
            }

            return idx;
        }

        private static string PathOf(string[] keys, int d)
        {
            var sb = new StringBuilder();
            for (int k = 1; k <= d; k++)
            {
                if (k > 1) sb.Append('/');
                sb.Append(keys[k] ?? "?");
            }
            return sb.ToString();
        }

        // Valeur brute d'un champ ("true", "12.5", "\"MapObject\""...), null si absent
        public string Value(string table, string key)
        {
            Dictionary<string, int> fields;
            int line;
            if (!Fields.TryGetValue(table, out fields) || !fields.TryGetValue(key, out line)) return null;

            Match m = KeyRx.Match(Lines[line]);
            if (!m.Success) return null;
            return m.Groups[5].Value.TrimEnd(',').Trim();
        }

        public double Number(string table, string key, double fallback)
        {
            string v = Value(table, key);
            double d;
            if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }

        public string Text(string table, string key)
        {
            string v = Value(table, key);
            if (v == null) return null;
            return v.Trim('"');
        }
    }

    // Modifications d'un fichier : remplacer une ligne, ou insérer avant une ligne. Appliquées de la fin vers le début.
    internal class LuaLineEdits
    {
        private readonly List<Tuple<int, int, string>> _edits = new List<Tuple<int, int, string>>();   // ligne, 0 = remplace / 1 = insère, texte
        private int _order;

        public int Count { get { return _edits.Count; } }

        public void Replace(int line, string text) { _edits.Add(Tuple.Create(line, 0, text)); _order++; }
        public void InsertBefore(int line, string text) { _edits.Add(Tuple.Create(line, 1, text)); _order++; }

        // Met un champ à une valeur : remplace sa ligne s'il existe, sinon l'ajoute avant la "}" du tableau
        public void SetField(LuaLineIndex idx, string table, string key, string luaValue)
        {
            Dictionary<string, int> fields;
            int line;
            int depth;
            if (!idx.Depth.TryGetValue(table, out depth)) return;

            string text = new string('\t', depth + 1) + "[\"" + key + "\"] = " + luaValue + ",";

            if (idx.Fields.TryGetValue(table, out fields) && fields.TryGetValue(key, out line))
            {
                Replace(line, text);
                return;
            }

            int close;
            if (idx.Close.TryGetValue(table, out close))
                InsertBefore(close, text);
        }

        public List<string> Apply(List<string> lines)
        {
            var result = new List<string>(lines);

            // De la fin vers le début ; sur une même ligne, les insertions gardent leur ordre d'ajout
            var ordered = _edits.Select((e, n) => new { e, n })
                .OrderByDescending(x => x.e.Item1).ThenByDescending(x => x.n).ToList();

            var replaced = new HashSet<int>();
            foreach (var x in ordered)
            {
                if (x.e.Item2 == 0)
                {
                    if (replaced.Add(x.e.Item1)) result[x.e.Item1] = x.e.Item3;
                }
                else
                {
                    result.Insert(x.e.Item1, x.e.Item3);
                }
            }

            return result;
        }
    }

    // ==========================================================================================
    // Ancien DCE : destruction des unités au sol (oob_ground) et des bâtiments de la carte (targetlist)
    // ==========================================================================================
    internal static class WargameOldDce
    {
        // NB : les groupes du wargame (wargameFormationId dans oob_ground) ne sont JAMAIS touchés ici : ils sont gérés par
        // DCE_Manager (repose) et scriptsMod (DC_UpdateWargame). Seul l'ancien DCE est concerné.
        // Une unité au sol de oob_ground (véhicule ou statique ; les navires ne sont jamais touchés)
        private class OobUnit
        {
            public string Path;          // chemin du tableau de l'unité
            public string GroupPath;     // chemin du tableau de son groupe
            public string Side;          // camp PROPRIETAIRE (oob_ground est rangé par propriétaire)
            public string Category;      // vehicle / static
            public PointF Position;
            public bool Dead;
        }

        private static List<OobUnit> ReadUnits(LuaLineIndex idx)
        {
            var units = new List<OobUnit>();

            foreach (string path in idx.Tables)
            {
                string[] p = path.Split('/');
                if (p.Length != 7) continue;
                if (p[3] != "group" || p[5] != "units") continue;
                if (p[2] != "vehicle" && p[2] != "static") continue;
                if (p[0] != "blue" && p[0] != "red") continue;
                if (idx.Value(string.Join("/", p, 0, 5), "wargameFormationId") != null) continue;   // groupe wargame : pas l'ancien DCE

                double x = idx.Number(path, "x", double.NaN);
                double y = idx.Number(path, "y", double.NaN);
                if (double.IsNaN(x) || double.IsNaN(y)) continue;

                units.Add(new OobUnit
                {
                    Path = path,
                    GroupPath = string.Join("/", p, 0, 5),
                    Side = p[0],
                    Category = p[2],
                    Position = new PointF((float)x, (float)y),
                    Dead = idx.Value(path, "dead") == "true",
                });
            }

            return units;
        }

        // Comme KillTarget de scriptsMod : unit.dead + CheckDay ; un statique : group.dead + group.hidden en plus
        private static void Kill(LuaLineEdits edits, LuaLineIndex idx, OobUnit u, double checkDay, HashSet<string> staticGroupsDone)
        {
            edits.SetField(idx, u.Path, "dead", "true");
            edits.SetField(idx, u.Path, "CheckDay", checkDay.ToString(CultureInfo.InvariantCulture));

            if (u.Category == "static" && staticGroupsDone.Add(u.GroupPath))
            {
                edits.SetField(idx, u.GroupPath, "dead", "true");
                edits.SetField(idx, u.GroupPath, "hidden", "true");
            }
        }

        // Tue les unités choisies par "shouldKill" (camp propriétaire, position) dans Active/oob_ground.lua. Renvoie le nombre tué.
        public static int KillGroundUnits(string campaignFolder, Func<string, PointF, bool> shouldKill, double checkDay)
        {
            string path = Path.Combine(campaignFolder, "Active", "oob_ground.lua");
            if (!File.Exists(path)) return 0;

            List<string> lines = File.ReadAllLines(path).ToList();
            LuaLineIndex idx = LuaLineIndex.Build(lines);

            var edits = new LuaLineEdits();
            var staticDone = new HashSet<string>(StringComparer.Ordinal);
            int killed = 0;

            foreach (OobUnit u in ReadUnits(idx))
            {
                if (u.Dead) continue;
                if (!shouldKill(u.Side, u.Position)) continue;

                Kill(edits, idx, u, checkDay, staticDone);
                killed++;
            }

            if (edits.Count > 0)
                File.WriteAllLines(path, edits.Apply(lines));

            return killed;
        }

        // Bâtiments de la carte (éléments "MapObject" de la targetlist) : element.dead = true.
        //   shouldKill(table de la targetlist, nom de la cible, position) -> true = détruit
        //   fractionByTarget (facultatif) : nom de cible -> part de ses éléments à détruire (objectifs abîmés dans la Preview)
        public static int KillMapObjects(string campaignFolder, Func<string, string, PointF, bool> shouldKill,
            Dictionary<string, double> fractionByTarget, double checkDay)
        {
            return KillMapObjectsInFile(Path.Combine(campaignFolder, "Active", "targetlist.lua"), shouldKill, fractionByTarget, checkDay);
        }

        // Même chose sur un fichier donné (Active/targetlist.lua ou Init/targetlist_init.lua)
        public static int KillMapObjectsInFile(string path, Func<string, string, PointF, bool> shouldKill,
            Dictionary<string, double> fractionByTarget, double checkDay)
        {
            if (!File.Exists(path)) return 0;

            List<string> lines = File.ReadAllLines(path).ToList();
            LuaLineIndex idx = LuaLineIndex.Build(lines);

            var edits = new LuaLineEdits();
            int killed = 0;

            // éléments regroupés par cible, dans l'ordre du fichier
            var byTarget = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string table in idx.Tables)
            {
                string[] p = table.Split('/');
                if (p.Length != 4 || p[2] != "elements") continue;
                if (idx.Text(table, "class") != "MapObject") continue;

                string target = p[0] + "/" + p[1];
                List<string> list;
                if (!byTarget.TryGetValue(target, out list)) { list = new List<string>(); byTarget[target] = list; }
                list.Add(table);
            }

            foreach (KeyValuePair<string, List<string>> kv in byTarget)
            {
                string side = kv.Key.Split('/')[0];
                string name = idx.Text(kv.Key, "titleName") ?? idx.Text(kv.Key, "name") ?? "";

                double fraction = 0;
                if (fractionByTarget != null) fractionByTarget.TryGetValue(name, out fraction);
                int toKillByFraction = (int)Math.Round(kv.Value.Count * Math.Max(0, Math.Min(1, fraction)));

                for (int n = 0; n < kv.Value.Count; n++)
                {
                    string element = kv.Value[n];
                    if (idx.Value(element, "dead") == "true") continue;

                    double x = idx.Number(element, "x", double.NaN);
                    double y = idx.Number(element, "y", double.NaN);
                    if (double.IsNaN(x) || double.IsNaN(y)) continue;

                    bool kill = n < toKillByFraction
                        || (shouldKill != null && shouldKill(side, name, new PointF((float)x, (float)y)));
                    if (!kill) continue;

                    edits.SetField(idx, element, "dead", "true");
                    edits.SetField(idx, element, "CheckDay", checkDay.ToString(CultureInfo.InvariantCulture));
                    killed++;
                }
            }

            if (edits.Count > 0)
                File.WriteAllLines(path, edits.Apply(lines));

            return killed;
        }

        // Zones capturées : tout ce qui est au sol et appartenait au camp qui a perdu la zone est détruit, définitivement
        // (une reprise plus tard ne le ressuscite pas). Les navires et les bases aériennes ne sont pas touchés.
        //   captured : zone -> camp qui l'a perdue
        public static List<string> DestroyInCapturedZones(string campaignFolder, List<WargameZoneData> zones,
            Dictionary<WargameZoneData, string> captured, double checkDay)
        {
            var report = new List<string>();
            if (captured == null || captured.Count == 0) return report;

            Func<PointF, WargameZoneData> zoneOf = p => WargameEngineCombat.FindZoneContaining(zones, p);

            try
            {
                int units = KillGroundUnits(campaignFolder, (side, pos) =>
                {
                    WargameZoneData z = zoneOf(pos);
                    string loser;
                    return z != null && captured.TryGetValue(z, out loser) && string.Equals(loser, side, StringComparison.OrdinalIgnoreCase);
                }, checkDay);

                // Bâtiments : une cible du perdant est rangée dans la table de l'AUTRE camp (celui qui la vise)
                int buildings = KillMapObjects(campaignFolder, (table, name, pos) =>
                {
                    WargameZoneData z = zoneOf(pos);
                    string loser;
                    return z != null && captured.TryGetValue(z, out loser)
                        && string.Equals(table, Saver_TargetList_Wargame.TargetTableSide(loser), StringComparison.OrdinalIgnoreCase);
                }, null, checkDay);

                report.Add("captured zones (" + string.Join(", ", captured.Keys.Select(z => z.Id)) + ") : "
                    + units + " ground unit(s) and " + buildings + " building part(s) destroyed for good");
            }
            catch (Exception ex)
            {
                report.Add("captured zones : old DCE units NOT destroyed (" + ex.Message + ")");
                WargameLog.Line("WargameOldDce | destruction en zone capturée impossible : " + ex.Message);
            }

            return report;
        }

        // ---- Zones dont le camp a été choisi à la main dans l'éditeur ----
        // Un élément de l'ancien DCE posé dans une zone tenue par l'AUTRE camp n'a rien à y faire : il est détruit,
        // comme dans une zone capturée.

        // Vue Active : destruction directe dans Active/oob_ground.lua et Active/targetlist.lua (bâtiments)
        public static List<string> DestroyEnemyInZones(string campaignFolder, List<WargameZoneData> zones)
        {
            var report = new List<string>();
            Func<PointF, WargameZoneData> zoneOf = p => WargameEngineCombat.FindZoneContaining(zones, p);
            double checkDay = ReadCampTotalTimeS(campaignFolder);

            try
            {
                int units = KillGroundUnits(campaignFolder, (side, pos) =>
                {
                    WargameZoneData z = zoneOf(pos);
                    return z != null && IsEnemyOf(side, z);
                }, checkDay);

                // une cible rangée dans la table d'un camp appartient à l'autre : dans une zone de CE camp, elle est en territoire ennemi
                int buildings = KillMapObjects(campaignFolder, (table, name, pos) =>
                {
                    WargameZoneData z = zoneOf(pos);
                    return z != null && !WargameEngineCombat.IsOffMapId(z.Id)
                        && string.Equals(table, z.Control, StringComparison.OrdinalIgnoreCase);
                }, null, checkDay);

                if (units + buildings > 0)
                    report.Add(units + " old DCE ground unit(s) and " + buildings + " building part(s) standing in a zone held by the other side were destroyed (Active files).");
            }
            catch (Exception ex)
            {
                report.Add("Old DCE units in zones of the other side NOT checked: " + ex.Message);
            }

            return report;
        }

        // Vue Init : oob_ground est recréé par scriptsMod au FirstMission à partir de Init/base_mission.miz, on ne peut donc
        // pas l'écrire ici. On liste les groupes en territoire ennemi dans Init/wargame_init_kills.lua (lu par scriptsMod au
        // FirstMission, qui les détruit avec KillTarget). Les bâtiments de la carte, eux, sont marqués directement dans
        // Init/targetlist_init.lua (recopié tel quel dans l'Active au FirstMission).
        public static List<string> PrepareInitEnemyKills(string campaignFolder, List<WargameZoneData> zones)
        {
            var report = new List<string>();
            string listPath = Path.Combine(campaignFolder, "Init", "wargame_init_kills.lua");

            try
            {
                var groups = new List<string>();
                var details = new List<string>();

                string miz = Path.Combine(campaignFolder, "Init", "base_mission.miz");
                if (File.Exists(miz))
                {
                    foreach (Tuple<string, string, PointF> g in ReadMizGroundGroups(miz))
                    {
                        WargameZoneData z = WargameEngineCombat.FindZoneContaining(zones, g.Item3);
                        if (z == null || !IsEnemyOf(g.Item2, z)) continue;

                        groups.Add(g.Item1);
                        details.Add(g.Item1 + " (" + g.Item2 + ") in " + z.Id);
                    }
                }

                var sb = new StringBuilder();
                sb.AppendLine("-- Written by DCE_Manager (wargame editor). Do not edit by hand: rewritten at each save of the Init zones.");
                sb.AppendLine("-- Old DCE ground groups standing, at campaign start, in a wargame zone held by the other side.");
                sb.AppendLine("-- scriptsMod destroys them at FirstMission (KillTarget), right after oob_ground is rebuilt.");
                sb.AppendLine("wargame_init_kills = {");
                foreach (string name in groups.Distinct())
                    sb.AppendLine("\t\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",");
                sb.AppendLine("}");
                File.WriteAllText(listPath, sb.ToString(), new UTF8Encoding(false));

                int buildings = KillMapObjectsInFile(Path.Combine(campaignFolder, "Init", "targetlist_init.lua"), (table, name, pos) =>
                {
                    WargameZoneData z = WargameEngineCombat.FindZoneContaining(zones, pos);
                    return z != null && !WargameEngineCombat.IsOffMapId(z.Id)
                        && string.Equals(table, z.Control, StringComparison.OrdinalIgnoreCase);
                }, null, 0);

                if (groups.Count > 0)
                    report.Add(groups.Count + " old DCE ground group(s) stand in a zone held by the other side; they will be destroyed at FirstMission:\n  - "
                        + string.Join("\n  - ", details));
                if (buildings > 0)
                    report.Add(buildings + " building part(s) in a zone held by the other side marked destroyed in Init/targetlist_init.lua.");
            }
            catch (Exception ex)
            {
                report.Add("Old DCE units in zones of the other side NOT checked: " + ex.Message);
            }

            return report;
        }

        private static bool IsEnemyOf(string side, WargameZoneData zone)
        {
            if (WargameEngineCombat.IsOffMapId(zone.Id)) return false;

            bool sideBlue = string.Equals(side, WargameSide.Blue, StringComparison.OrdinalIgnoreCase);
            bool sideRed = string.Equals(side, WargameSide.Red, StringComparison.OrdinalIgnoreCase);
            bool zoneBlue = string.Equals(zone.Control, WargameSide.Blue, StringComparison.OrdinalIgnoreCase);
            bool zoneRed = string.Equals(zone.Control, WargameSide.Red, StringComparison.OrdinalIgnoreCase);

            return (sideBlue && zoneRed) || (sideRed && zoneBlue);
        }

        // Groupes au sol (véhicules et statiques) de la mission de base : nom, camp, centre des unités
        private static List<Tuple<string, string, PointF>> ReadMizGroundGroups(string mizPath)
        {
            var result = new List<Tuple<string, string, PointF>>();

            string text;
            using (ZipArchive zip = ZipFile.OpenRead(mizPath))
            {
                ZipArchiveEntry entry = zip.GetEntry("mission");
                if (entry == null) return result;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    text = reader.ReadToEnd();
            }

            using (Lua lua = new Lua())
            {
                lua.DoString("os = nil\nio = nil\nfile = nil\ndebug = nil");
                lua.DoString(text);

                LuaTable mission = lua["mission"] as LuaTable;
                LuaTable coalitions = mission == null ? null : mission["coalition"] as LuaTable;
                if (coalitions == null) return result;

                foreach (string side in new[] { "blue", "red" })
                {
                    LuaTable coalition = coalitions[side] as LuaTable;
                    LuaTable countries = coalition == null ? null : coalition["country"] as LuaTable;
                    if (countries == null) continue;

                    foreach (object c in countries.Values)
                    {
                        LuaTable country = c as LuaTable;
                        if (country == null) continue;

                        foreach (string category in new[] { "vehicle", "static" })
                        {
                            LuaTable cat = country[category] as LuaTable;
                            LuaTable groups = cat == null ? null : cat["group"] as LuaTable;
                            if (groups == null) continue;

                            foreach (object g in groups.Values)
                            {
                                LuaTable group = g as LuaTable;
                                LuaTable units = group == null ? null : group["units"] as LuaTable;
                                string name = group == null ? null : group["name"] as string;
                                if (units == null || string.IsNullOrEmpty(name)) continue;

                                double sx = 0, sy = 0;
                                int n = 0;
                                foreach (object u in units.Values)
                                {
                                    LuaTable unit = u as LuaTable;
                                    if (unit == null) continue;
                                    sx += Convert.ToDouble(unit["x"] ?? 0.0, CultureInfo.InvariantCulture);
                                    sy += Convert.ToDouble(unit["y"] ?? 0.0, CultureInfo.InvariantCulture);
                                    n++;
                                }

                                if (n > 0)
                                    result.Add(Tuple.Create(name, side, new PointF((float)(sx / n), (float)(sy / n))));
                            }
                        }
                    }
                }
            }

            return result;
        }

        // Temps de campagne écoulé (secondes) lu dans Active/camp_status.lua : sert de CheckDay, comme scriptsMod.
        // Dernière valeur "CampTotalTimeS" du fichier ; 0 si absente.
        public static double ReadCampTotalTimeS(string campaignFolder)
        {
            try
            {
                string path = Path.Combine(campaignFolder, "Active", "camp_status.lua");
                if (!File.Exists(path)) return 0;

                double last = 0;
                foreach (Match m in Regex.Matches(File.ReadAllText(path), @"\[""CampTotalTimeS""\]\s*=\s*(-?[\d\.]+)"))
                {
                    double v;
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) last = v;
                }
                return last;
            }
            catch
            {
                return 0;
            }
        }
    }

    // ==========================================================================================
    // Preview -> campagne : écrit la situation d'un tour de la Preview dans Active, pour la jouer dans DCS
    // ==========================================================================================
    internal static class WargamePreviewExport
    {
        public static string BackupRoot(string campaignName)
        {
            return Path.Combine(WargameZoneRepository.GetCampaignFolder(campaignName), "Active_Backups");
        }

        // Copie complète du dossier Active dans Active_Backups\<nom>. Renvoie le dossier créé.
        public static string BackupActive(string campaignName, string label)
        {
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);
            string active = Path.Combine(campaignFolder, "Active");
            string target = Path.Combine(BackupRoot(campaignName), label + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            CopyFolder(active, target);
            return target;
        }

        // Remet une sauvegarde en place. L'Active actuel n'est pas effacé : il part lui-même en sauvegarde ("BeforeRestore").
        public static void RestoreActive(string campaignName, string backupFolder)
        {
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);
            string active = Path.Combine(campaignFolder, "Active");

            if (Directory.Exists(active))
            {
                string aside = Path.Combine(BackupRoot(campaignName), "BeforeRestore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(BackupRoot(campaignName));
                Directory.Move(active, aside);
            }

            CopyFolder(backupFolder, active);
        }

        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);

            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);

            foreach (string dir in Directory.GetDirectories(from))
                CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        // Ecrit la situation du tour actuel de la session dans la campagne. Aucune mission n'est lancée ici.
        // Renvoie le compte rendu (une ligne par étape).
        public static List<string> Export(string campaignName, WargameCombatSession session, WargameCampClock clock, Random rng)
        {
            var report = new List<string>();
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);
            int turn = Math.Max(1, session.Turn);
            DateTime when = clock != null ? clock.At(turn) : DateTime.Now;

            // Temps de campagne écoulé (CheckDay des destructions) : depuis le début de la campagne, à minuit
            double checkDay = clock != null ? Math.Max(0, (when - clock.Start.Date).TotalSeconds) : WargameOldDce.ReadCampTotalTimeS(campaignFolder);

            // 1. Zones et formations du wargame (état de la Preview)
            Saver_WargameZoneActive.Save(WargameZoneRepository.GetActiveWargameZonesPath(campaignName), session.Zones);
            report.Add("wargame zones and formations saved (turn " + turn + ")");

            // 2. Formations reposées dans la targetlist, d'après l'état qu'on vient d'écrire (définitions complètes de l'Init)
            report.Add(WargameEngineLosses.RepostAllFormations(campaignName, session.Facing));

            // Zones rechargées comme le fera la campagne (Init + Active)
            List<WargameZoneData> zones = WargameZoneRepository.LoadOrGenerateInit(campaignName);
            new WargameZoneActiveLoader().ApplyActiveState(WargameZoneRepository.GetActiveWargameZonesPath(campaignName), zones);

            // Zones capturées depuis le début de la Preview : camp de départ (SupplyOwner) différent du camp actuel
            var captured = new Dictionary<WargameZoneData, string>();
            var flipped = new List<WargameZoneData>();
            foreach (WargameZoneData simZone in session.Zones)
            {
                if (WargameEngineCombat.IsOffMapId(simZone.Id)) continue;
                if (!IsFighting(simZone.SupplyOwner) || !IsFighting(simZone.Control)) continue;
                if (string.Equals(simZone.SupplyOwner, simZone.Control, StringComparison.OrdinalIgnoreCase)) continue;

                WargameZoneData z = zones.FirstOrDefault(x => string.Equals(x.Id, simZone.Id, StringComparison.OrdinalIgnoreCase));
                if (z == null) continue;
                captured[z] = simZone.SupplyOwner;
                flipped.Add(z);
            }

            // 3. Objectifs : changent de table dans les zones capturées, et ceux abîmés dans la Preview perdent leurs bâtiments
            if (flipped.Count > 0)
            {
                string tl = Path.Combine(campaignFolder, "Active", "targetlist.lua");
                WargameEngineLosses.MoveObjectivesOfFlippedZones(tl, zones, flipped);
                int zeroed = WargameLogistics.ZeroCapturedSupply(tl, zones, flipped);
                if (zeroed > 0) report.Add(zeroed + " captured supply source(s) set to 0 for good");
            }

            // Points de ravitaillement avancés posés dans DCS + ordres de convois / aérien pour scriptsMod
            report.AddRange(WargameLogistics.PostPointsAndOrders(campaignName, zones, session.Convoys, turn));

            Dictionary<string, double> damaged = session.ObjectiveDamage();
            if (damaged.Count > 0)
            {
                int parts = WargameOldDce.KillMapObjects(campaignFolder, null, damaged, checkDay);
                report.Add("objectives damaged in the preview : " + damaged.Count + " (" + parts + " building part(s) destroyed)");
            }

            // 4. Ancien DCE : zones capturées (tout détruit) puis pertes simulées en première ligne
            report.AddRange(WargameOldDce.DestroyInCapturedZones(campaignFolder, zones, captured, checkDay));

            Dictionary<string, double> killChance = session.FrontKillChance();   // "zone|camp" -> probabilité d'être détruit
            if (killChance.Count > 0)
            {
                int lost = WargameOldDce.KillGroundUnits(campaignFolder, (side, pos) =>
                {
                    WargameZoneData z = WargameEngineCombat.FindZoneContaining(zones, pos);
                    if (z == null || captured.ContainsKey(z)) return false;

                    double p;
                    return killChance.TryGetValue(z.Id + "|" + side, out p) && rng.NextDouble() < p;
                }, checkDay);
                report.Add("front line losses (old DCE ground units) : " + lost + " unit(s) destroyed");
            }

            // 5. camp_status : mission, heure, fichier de mission, flags
            report.Add(WriteCampStatus(campaignFolder, campaignName, turn, when, session.Flags));

            // 6. conf_mod : la date du tour, appliquée par scriptsMod au lancement (saut dans le temps + triggers rejoués)
            report.Add(WriteConfModDate(campaignFolder, when));

            // 7. Marqueur : au lancement de SkipMission, le tour du wargame est DÉJÀ joué (ProcessBeforeMission ne le rejoue pas)
            File.WriteAllText(Path.Combine(campaignFolder, "Active", WargameEngineLosses.ExportedFlagFile),
                "turn " + turn + " written by the wargame preview on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            report.Add("next SkipMission will NOT play an extra wargame turn (" + WargameEngineLosses.ExportedFlagFile + ")");

            WargameLog.Line("---- export summary (turn " + turn + ") ----");
            WargameLog.Lines(report, "  ");
            return report;
        }

        private static bool IsFighting(string side)
        {
            return string.Equals(side, WargameSide.Blue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(side, WargameSide.Red, StringComparison.OrdinalIgnoreCase);
        }

        // camp.mission = tour, camp.time = heure du tour, camp.MissionFilename = ..._ongoing.miz, camp.flag = flags de la Preview.
        // Les flags à valeur numérique (compteurs) déjà présents sont gardés tels quels.
        private static string WriteCampStatus(string campaignFolder, string campaignName, int turn, DateTime when, HashSet<string> flags)
        {
            string path = Path.Combine(campaignFolder, "Active", "camp_status.lua");
            if (!File.Exists(path)) return "camp_status.lua not found : mission number, time and flags NOT written";

            List<string> lines = File.ReadAllLines(path).ToList();
            int seconds = (int)when.TimeOfDay.TotalSeconds;

            for (int i = 0; i < lines.Count; i++)
            {
                lines[i] = Regex.Replace(lines[i], @"^(\t\[""mission""\]\s*=\s*)-?\d+(\s*,)", "${1}" + turn + "${2}");
                lines[i] = Regex.Replace(lines[i], @"^(\t\[""time""\]\s*=\s*)-?[\d\.]+(\s*,)", "${1}" + seconds + "${2}");

                Match mf = Regex.Match(lines[i], @"^(\t\[""MissionFilename""\]\s*=\s*"")(.*)(""\s*,.*)$");
                if (mf.Success)
                    lines[i] = mf.Groups[1].Value + Regex.Replace(mf.Groups[2].Value, "_first\\.miz$", "_ongoing.miz", RegexOptions.IgnoreCase) + mf.Groups[3].Value;
            }

            // Bloc des flags (profondeur 1)
            int start = lines.FindIndex(l => Regex.IsMatch(l, @"^\t\[""flag""\]\s*=\s*$"));
            var block = new List<string>();
            block.Add("\t[\"flag\"] = ");
            block.Add("\t{");

            int end = -1;
            var kept = new List<string>();
            if (start >= 0)
            {
                for (int j = start + 1; j < lines.Count; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^\t\}\s*,?\s*$")) { end = j; break; }

                    // compteurs (valeur numérique) : gardés
                    Match fm = Regex.Match(lines[j], @"^\t\t(\[[^\]]+\])\s*=\s*(-?[\d\.]+)\s*,");
                    if (fm.Success) kept.Add(lines[j]);
                }
            }

            block.AddRange(kept);
            foreach (string flag in flags.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string key = Regex.IsMatch(flag, @"^\d+$") ? "[" + flag + "]" : "[\"" + flag.Replace("\"", "\\\"") + "\"]";
                if (kept.Any(k => k.Contains(key))) continue;
                block.Add("\t\t" + key + " = true,");
            }
            block.Add("\t},");

            if (start >= 0 && end > start)
            {
                lines.RemoveRange(start, end - start + 1);
                lines.InsertRange(start, block);
            }
            else
            {
                // pas de bloc : on l'ajoute juste avant la dernière accolade du fichier
                int last = lines.FindLastIndex(l => l.Trim() == "}");
                if (last > 0) lines.InsertRange(last, block);
            }

            File.WriteAllLines(path, lines);
            return "camp_status : mission " + turn + ", time " + when.ToString("HH:mm") + ", mission file _ongoing, " + flags.Count + " flag(s)";
        }

        // Init/conf_mod.lua : mission_ini.current_date = date du tour, setDateInNextMission = true (lu par BAT_SkipMission.lua)
        private static string WriteConfModDate(string campaignFolder, DateTime when)
        {
            string path = Path.Combine(campaignFolder, "Init", "conf_mod.lua");
            if (!File.Exists(path)) return "Init/conf_mod.lua not found : date NOT written";

            List<string> lines = File.ReadAllLines(path).ToList();
            int start = lines.FindIndex(l => Regex.IsMatch(l, @"^\s*current_date\s*=\s*\{"));
            if (start < 0) return "conf_mod : current_date block not found, date NOT written";

            for (int i = start + 1; i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"^\s*\}")) break;

                lines[i] = Regex.Replace(lines[i], @"(\bsetDateInNextMission\s*=\s*)(true|false)", "${1}true");
                lines[i] = Regex.Replace(lines[i], @"(\bday\s*=\s*)\d+", "${1}" + when.Day);
                lines[i] = Regex.Replace(lines[i], @"(\bmonth\s*=\s*)\d+", "${1}" + when.Month);
                lines[i] = Regex.Replace(lines[i], @"(\byear\s*=\s*)\d+", "${1}" + when.Year);
            }

            File.WriteAllLines(path, lines);
            return "conf_mod : date of the mission set to " + when.ToString("dd/MM/yyyy") + " (applied by scriptsMod, triggers checked again)";
        }
    }


    // ==========================================================================================
    // Journal du wargame (partie DCE_Manager) : UN PETIT FICHIER PAR ACTION, dans <campagne>\Debug\Wargame (fichiers wargame_*.log + wargame_preview_log.txt de la Preview)
    //     wargame_M001_FirstMission_122427.log, wargame_M015_PreviewExport_122655.log, wargame_M016_Turn_130512.log...
    // Chaque fichier se lit seul (en-tête + tout le détail de l'action) : c'est lui qu'on partage pour un problème.
    // On garde les 20 derniers ; une FirstMission les efface tous (nouvelle partie). Seuls les fichiers wargame_*.log sont concernés.
    // Hors action (aucun fichier ouvert), Line() écrit dans le log général de DCE_Manager comme avant.
    // ==========================================================================================
    internal static class WargameLog
    {
        private const int KeepFiles = 20;

        // Tous les fichiers du journal commencent par là : ils se repèrent dans Debug à côté des fichiers de scriptsMod,
        // et le nettoyage ne touche QUE eux (jamais WargameTrace.log ni les DCEM_*.lua de scriptsMod)
        private const string FilePrefix = "wargame_";

        private static readonly object _lock = new object();
        private static string _file;                                  // fichier de l'action en cours (null = aucune)
        private static readonly StringBuilder _buffer = new StringBuilder();
        private static int _bufferedLines;

        // <campagne>\Debug\Wargame : tout le wargame au même endroit (journaux d'action + wargame_preview_log.txt)
        public static string Folder(string campaignName)
        {
            return Path.Combine(WargameZoneRepository.GetCampaignFolder(campaignName), "Debug", "Wargame");
        }

        // Ouvre le fichier d'une action. clearFolder = true : vide d'abord le dossier (FirstMission)
        public static void Begin(string campaignName, string action, int mission, bool clearFolder = false)
        {
            lock (_lock)
            {
                try
                {
                    if (_file != null) EndLocked();   // action précédente pas refermée : on la ferme proprement

                    string folder = Folder(campaignName);
                    Directory.CreateDirectory(folder);

                    List<FileInfo> old = new DirectoryInfo(folder).GetFiles(FilePrefix + "*.log").OrderByDescending(fi => fi.LastWriteTime).ToList();
                    for (int i = 0; i < old.Count; i++)
                        if (clearFolder || i >= KeepFiles - 1)
                            try { old[i].Delete(); } catch { }

                    DateTime now = DateTime.Now;
                    _file = Path.Combine(folder, FilePrefix + "M" + Math.Max(0, mission).ToString("000") + "_" + action + "_" + now.ToString("HHmmss") + ".log");

                    _buffer.Clear();
                    _bufferedLines = 0;
                    _buffer.AppendLine("##########################################################################");
                    _buffer.AppendLine("# WARGAME LOG (DCE_Manager) | " + action + " | campaign " + campaignName + " | mission " + mission);
                    _buffer.AppendLine("# " + now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        + " | DCE_Manager " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                    _buffer.AppendLine("##########################################################################");
                }
                catch (Exception ex)
                {
                    _file = null;
                    FormUtils.LogRegister("WargameLog | journal wargame impossible à ouvrir : " + ex.Message);
                }
            }
        }

        public static void Line(string text)
        {
            lock (_lock)
            {
                if (_file == null)
                {
                    FormUtils.LogRegister(text);
                    return;
                }

                _buffer.Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(' ').AppendLine(text);
                if (++_bufferedLines >= 200) FlushLocked();
            }
        }

        public static void Lines(IEnumerable<string> lines, string prefix = "")
        {
            if (lines == null) return;
            foreach (string l in lines) Line(prefix + l);
        }

        // Ferme le fichier de l'action ; une seule ligne part dans le log général (où trouver le détail)
        public static void End()
        {
            lock (_lock) { EndLocked(); }
        }

        private static void EndLocked()
        {
            if (_file == null) return;
            string file = _file;
            FlushLocked();
            _file = null;
            FormUtils.LogRegister("WargameLog | détail écrit dans " + file);
        }

        private static void FlushLocked()
        {
            if (_file == null || _buffer.Length == 0) return;
            try { File.AppendAllText(_file, _buffer.ToString(), new UTF8Encoding(false)); }
            catch (Exception ex) { FormUtils.LogRegister("WargameLog | écriture impossible : " + ex.Message); }
            _buffer.Clear();
            _bufferedLines = 0;
        }

        // FirstMission : nouvelle partie -> dossier vidé, puis les formations de départ posées dans l'Init (même appel qu'avant)
        public static List<string> FirstMission(string campaignName)
        {
            Begin(campaignName, "FirstMission", 1, true);
            try
            {
                List<string> failures = Saver_TargetList_Wargame.WriteInitialFormations(campaignName);
                Line("Init/targetlist_init.lua : starting formations written" + (failures.Count > 0 ? ", " + failures.Count + " could not be placed :" : ""));
                Lines(failures, "   NOT PLACED : ");
                return failures;
            }
            catch (Exception ex)
            {
                Line("ERROR : " + ex);
                throw;
            }
            finally
            {
                End();
            }
        }
    }

    // ==========================================================================================
    // Logistique du wargame dans les missions (convois, points de ravitaillement avancés, aérien)
    //
    //   AVANT le tour (ReadResults)      : état des points posés (alive dans la targetlist) et résultats des convois / de
    //                                      l'aérien rendus par scriptsMod (Active/wargame_logistics_result.lua)
    //   APRES le tour (PostPointsAndOrders) : points avancés posés dans la targetlist (WG_POINT_BLUE_1...) et ordres pour
    //                                      scriptsMod (Active/wargame_logistics.lua : convois à fabriquer, aérien demandé)
    //
    // Fichier rendu par scriptsMod (bloc "logistique B", pas encore fait) :
    //   wargame_logistics_result = {
    //       ["convoys"] = { ["BLUE_1"] = 60, ... },   -- part du convoi ARRIVEE ou encore vivante en fin de mission, en %
    //       ["air"]     = { ["BLUE_1"] = 100, ... },  -- part des livraisons aériennes réussies, en %
    //   }
    // Absent : les valeurs par défaut du moteur s'appliquent (convois arrivés en entier, pas d'aérien).
    // ==========================================================================================
    internal static class WargameLogistics
    {
        // wargameFormationId réservés aux points avancés (jamais une vraie formation) : bleu 900001..., rouge 910001...
        private static int PointId(string side, int slot)
        {
            return (string.Equals(side, WargameSide.Blue, StringComparison.OrdinalIgnoreCase) ? 900000 : 910000) + slot;
        }

        public static string PointName(string side, int slot)
        {
            return "WG_POINT_" + side.ToUpperInvariant() + "_" + slot;
        }

        private static readonly Regex LogisticRx = new Regex(@"wargameLogistic""?\]?\s*=\s*""point""");

        // ---- Avant le tour : ce que la mission jouée a fait aux points et aux convois ----
        public static List<string> ReadResults(string campaignFolder, List<WargameZoneData> zones, WargameCombatConfig config)
        {
            var report = new List<string>();

            // 1. Points avancés posés à la mission d'avant : alive dans la targetlist
            try
            {
                string path = Path.Combine(campaignFolder, "Active", "targetlist.lua");
                if (File.Exists(path))
                {
                    LuaLineIndex idx = LuaLineIndex.Build(File.ReadAllLines(path).ToList());

                    foreach (string table in idx.Tables)
                    {
                        if (table.Split('/').Length != 2) continue;
                        if (idx.Text(table, "wargameLogistic") != "point") continue;

                        double alive = idx.Number(table, "alive", 100);
                        string zoneId = idx.Text(table, "wargameZone") ?? "";
                        string side = idx.Text(table, "wargameSide") ?? "";

                        WargameZoneData zone = zones.FirstOrDefault(z => string.Equals(z.Id, zoneId, StringComparison.OrdinalIgnoreCase));
                        if (zone == null || !string.Equals(zone.ForwardSide, side, StringComparison.OrdinalIgnoreCase)) continue;

                        if (alive < 10)
                        {
                            zone.ForwardReserve = 0;
                            zone.ForwardDestroyed = true;
                            report.Add("supply point in " + zone.Id + " (" + side + ") DESTROYED : stock lost");
                        }
                        else if (alive < 100)
                        {
                            zone.ForwardReserve = zone.ForwardReserve * alive / 100.0;
                            report.Add("supply point in " + zone.Id + " (" + side + ") damaged (" + alive.ToString("0") + "%) : stock "
                                + zone.ForwardReserve.ToString("0.#") + " turn(s)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                report.Add("supply points NOT read : " + ex.Message);
            }

            // 2. Résultats des convois et de l'aérien rendus par scriptsMod
            config.ConvoyDeliveredBySlot = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            config.AirDeliveredBySlot = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            string resultPath = Path.Combine(campaignFolder, "Active", "wargame_logistics_result.lua");
            if (File.Exists(resultPath))
            {
                try
                {
                    using (Lua lua = new Lua())
                    {
                        lua.DoString("os = nil\nio = nil\nfile = nil\ndebug = nil");
                        lua.DoFile(resultPath);

                        LuaTable root = lua["wargame_logistics_result"] as LuaTable;
                        ReadPercentTable(root == null ? null : root["convoys"] as LuaTable, config.ConvoyDeliveredBySlot);
                        ReadPercentTable(root == null ? null : root["air"] as LuaTable, config.AirDeliveredBySlot);
                    }

                    report.Add("convoy results read : " + config.ConvoyDeliveredBySlot.Count + " convoy(s), " + config.AirDeliveredBySlot.Count + " air delivery(ies)");

                    // Consommé : on le met de côté pour ne pas le relire au tour suivant
                    string used = Path.Combine(campaignFolder, "Active", "wargame_logistics_result_used.lua");
                    if (File.Exists(used)) File.Delete(used);
                    File.Move(resultPath, used);
                }
                catch (Exception ex)
                {
                    report.Add("convoy results NOT read : " + ex.Message);
                }
            }

            return report;
        }

        private static void ReadPercentTable(LuaTable table, Dictionary<string, double> into)
        {
            if (table == null) return;

            foreach (object key in table.Keys)
            {
                double v;
                if (double.TryParse(Convert.ToString(table[key], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    into[key.ToString()] = Math.Max(0, Math.Min(1, v / 100.0));
            }
        }

        // ---- Après le tour : points avancés posés dans DCS + ordres pour scriptsMod ----
        public static List<string> PostPointsAndOrders(string campaignName, List<WargameZoneData> zones,
            List<WargameEngineCombat.WargameConvoy> convoys, int mission)
        {
            var report = new List<string>();
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);
            string activePath = Path.Combine(campaignFolder, "Active", "targetlist.lua");
            if (!File.Exists(activePath)) return report;

            try
            {
                WargameCampaignInfo campaignInfo = WargameCampaignInfo.Load(campaignName);
                WargameTemplateCatalog catalog = WargameTemplateCatalog.LoadAndSync(campaignName, campaignInfo);
                WargameSpawnAreas spawnAreas = Saver_TargetList_Wargame.LoadSpawnAreas(campaignName);
                var rng = new Random();

                List<string> lines = File.ReadAllLines(activePath).ToList();

                // Les anciens points sont retirés, les points actifs reposés (ils suivent le front)
                List<TargetListBlock> blocks = TargetListBlockScanner.ScanBlocks(lines,
                    (l, start, end) => Enumerable.Range(start, end - start + 1).Any(k => LogisticRx.IsMatch(l[k])),
                    out Dictionary<string, int> sideCloseLine);

                var linesToRemove = new HashSet<int>();
                foreach (TargetListBlock b in blocks.Where(b => b.Matched))
                    for (int i = b.Start; i <= b.End; i++) linesToRemove.Add(i);

                var newBlocksBySide = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    { WargameSide.Blue, new List<string>() },
                    { WargameSide.Red, new List<string>() },
                };
                Dictionary<string, int> nextIndexBySide = Saver_TargetList_Wargame.FindNextNumericIndexBySide(lines);

                // position du point posé (pour donner une destination aux convois)
                var pointPosition = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
                var pointLines = new List<string>();   // section "supplyPoints" des ordres : où DCE en jeu doit poser chaque point
                var missingTemplate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int posted = 0, failed = 0;

                foreach (WargameZoneData zone in zones)
                {
                    if (zone.ForwardReserve <= 0 || zone.ForwardSlot <= 0) continue;
                    if (WargameEngineCombat.IsOffMapId(zone.Id)) continue;

                    string side = zone.ForwardSide;
                    if (side != WargameSide.Blue && side != WargameSide.Red) continue;

                    WargameTemplateEntry entry = catalog.OfType(WargameUnitType.SupplyPoint, side).FirstOrDefault();
                    if (entry == null) { missingTemplate.Add(side); continue; }

                    List<WargameTemplateUnit> layout = new Parser_WargameTemplateLayout().LoadLayout(campaignInfo.GetTemplateFilePath(entry.Template));
                    if (layout.Count == 0) { failed++; continue; }

                    // Rangé derrière les formations du camp dans la zone (dernier secteur)
                    int mates = zone.Formations.Count(f => f.ForcePower > 0 && f.Side == side);
                    PointF? threat = Saver_TargetList_Wargame.FindNearestEnemyZoneCenter(zone, side, zones);
                    List<WargamePlacedUnit> placed = WargameSpawnSolver.PlaceTemplate(zone, layout, spawnAreas, rng, threat, mates, mates + 1,
                        side, zones, campaignInfo.ZoneEdgeMarginMeters);
                    if (placed == null || placed.Count == 0) { failed++; continue; }

                    string name = PointName(side, zone.ForwardSlot);
                    var fake = new WargameFormation
                    {
                        FormationId = PointId(side, zone.ForwardSlot),
                        Name = name,
                        Template = entry.Template,
                        Side = side,
                        Priority = entry.Priority,
                        Attributes = new List<string>(entry.Attributes),
                        FirepowerMin = entry.FirepowerMin,
                        FirepowerMax = entry.FirepowerMax,
                    };

                    string targetSide = Saver_TargetList_Wargame.TargetTableSide(side);
                    int index = nextIndexBySide.TryGetValue(targetSide, out int n) ? n : 1;
                    nextIndexBySide[targetSide] = index + 1;

                    List<string> block = Saver_TargetList_Wargame.BuildActiveBlockLines(index, fake, placed, name);

                    // Le point est dans la targetlist pour ses DONNÉES (alive, réserve...), pas comme cible : l'ATO adverse ne le
                    // propose pas (sa vraie position est décidée en jeu, sur une route, et n'est pas connue ici)
                    int ato = block.FindIndex(l => l.Trim() == "[\"ATO\"] = true,");
                    if (ato >= 0) block[ato] = "\t\t\t[\"ATO\"] = false,";

                    // Marqueurs logistiques, juste avant la "}" finale du bloc
                    block.InsertRange(block.Count - 1, new[]
                    {
                        "\t\t\t[\"wargameLogistic\"] = \"point\",",
                        "\t\t\t[\"wargameSlot\"] = \"" + side.ToUpperInvariant() + "_" + zone.ForwardSlot + "\",",
                        "\t\t\t[\"wargameZone\"] = \"" + zone.Id.Replace("\"", "\\\"") + "\",",
                        "\t\t\t[\"wargameSide\"] = \"" + side + "\",",
                        "\t\t\t[\"wargameReserve\"] = " + zone.ForwardReserve.ToString("0.##", CultureInfo.InvariantCulture) + ",",
                    });

                    newBlocksBySide[targetSide].AddRange(block);

                    float sx = 0, sy = 0;
                    foreach (WargamePlacedUnit u in placed) { sx += u.Position.X; sy += u.Position.Y; }
                    pointPosition[zone.Id] = new PointF(sx / placed.Count, sy / placed.Count);
                    posted++;

                    // Véhicules, pays : lus dans le template du point (scriptsMod en a besoin pour fabriquer le point en jeu)
                    string pointCountry, pointCountryId;
                    List<string> pointVehicles = ReadVehicleTypes(campaignInfo.GetTemplateFilePath(entry.Template), out pointCountry, out pointCountryId);
                    if (pointCountry.Length == 0 || pointVehicles.Count == 0)
                        WargameLog.Line("WargameLogistics | supply point template '" + entry.Template + "' : country='" + pointCountry + "', "
                            + pointVehicles.Count + " vehicle(s) found (check the .stm of the template)");

                    // Ordre de placement en jeu : sur une route, DANS le polygone de la zone (hint = position calculée ici, à défaut)
                    pointLines.Add("\t\t[" + posted + "] = ");
                    pointLines.Add("\t\t{");
                    pointLines.Add("\t\t\t[\"slot\"] = \"" + side.ToUpperInvariant() + "_" + zone.ForwardSlot + "\",");
                    pointLines.Add("\t\t\t[\"name\"] = \"" + name + "\",");
                    pointLines.Add("\t\t\t[\"side\"] = \"" + side + "\",");
                    pointLines.Add("\t\t\t[\"zone\"] = \"" + Esc(zone.Id) + "\",");
                    pointLines.Add("\t\t\t[\"template\"] = \"" + Esc(entry.Template) + "\",");
                    pointLines.Add("\t\t\t[\"country\"] = \"" + Esc(pointCountry) + "\",");              // pays du template (country.name du .stm)
                    if (pointCountryId.Length > 0)
                        pointLines.Add("\t\t\t[\"countryId\"] = " + pointCountryId + ",");               // country.id du .stm
                    pointLines.Add("\t\t\t[\"vehicles\"] = { " + string.Join(", ", pointVehicles.Select(v => "\"" + Esc(v) + "\"")) + " },");
                    pointLines.Add("\t\t\t[\"hint\"] = { [\"x\"] = " + Num(pointPosition[zone.Id].X) + ", [\"y\"] = " + Num(pointPosition[zone.Id].Y) + " },");
                    pointLines.Add("\t\t\t[\"polygon\"] = ");
                    pointLines.Add("\t\t\t{");
                    for (int pi = 0; pi < zone.DcsPoints.Count; pi++)
                        pointLines.Add("\t\t\t\t[" + (pi + 1) + "] = { [\"x\"] = " + Num(zone.DcsPoints[pi].X) + ", [\"y\"] = " + Num(zone.DcsPoints[pi].Y) + " },");
                    pointLines.Add("\t\t\t},");
                    pointLines.Add("\t\t},");
                }

                if (linesToRemove.Count > 0 || posted > 0)
                {
                    List<string> rewritten = TargetListBlockScanner.RemoveAndInsert(lines, linesToRemove, sideCloseLine, newBlocksBySide);
                    rewritten = TargetListBlockScanner.RenumberBlocks(rewritten);
                    File.WriteAllLines(activePath, rewritten);
                }

                if (posted > 0 || failed > 0) report.Add("supply points posted in DCS : " + posted + (failed > 0 ? " (" + failed + " could not be placed)" : ""));
                foreach (string side in missingTemplate)
                    report.Add("no 'supplypoint' template for " + side + " in the template catalog : its supply points are NOT posted in DCS");

                report.Add(WriteOrders(campaignFolder, zones, convoys, mission, catalog, campaignInfo, pointPosition, pointLines, rng, report));
            }
            catch (Exception ex)
            {
                report.Add("supply points / logistics orders NOT written : " + ex.Message);
                WargameLog.Line("WargameLogistics | " + ex);
            }

            return report;
        }

        // Ordres pour scriptsMod : convois à fabriquer en jeu (itinéraire, véhicules, destination) et aérien demandé
        private static string WriteOrders(string campaignFolder, List<WargameZoneData> zones, List<WargameEngineCombat.WargameConvoy> convoys,
            int mission, WargameTemplateCatalog catalog, WargameCampaignInfo campaignInfo, Dictionary<string, PointF> pointPosition,
            List<string> pointLines, Random rng, List<string> report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- Written by DCE_Manager before each mission (wargame logistics). Read by scriptsMod.");
            sb.AppendLine("-- supplyPoints : forward supply points, to be placed IN GAME on a road inside 'polygon' (hint = fallback position).");
            sb.AppendLine("--                Keep the unit names of the targetlist block (alive is read by name). Not an ATO target (ATO = false).");
            sb.AppendLine("--                template / country / countryId / vehicles : what to build (read from the template .stm).");
            sb.AppendLine("-- convoys : road convoys to build in game, from 'from' to 'to', along 'route' (zone centers),");
            sb.AppendLine("--           from = xy of the center of the start zone (= route[1], fromZone = its name),");
            sb.AppendLine("--           to   = xy of the destination supply point 'supplyPoint' (toZone = its zone),");
            sb.AppendLine("--           ending ON the road at the in-game position of the supply point ('destination' = hint only).");
            sb.AppendLine("-- air     : supply drops wanted for each sector (the only way when byRoad = false).");
            sb.AppendLine("-- Result expected after the mission in Active/wargame_logistics_result.lua (see DCE_Manager WargameLogistics).");
            sb.AppendLine("wargame_logistics = ");
            sb.AppendLine("{");
            sb.AppendLine("\t[\"mission\"] = " + mission + ",");

            var convoyLines = new List<string>();
            var airLines = new List<string>();
            var missingConvoyTemplate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nc = 0, na = 0;

            foreach (WargameEngineCombat.WargameConvoy c in convoys ?? new List<WargameEngineCombat.WargameConvoy>())
            {
                if (c.Lost) continue;

                WargameZoneData target = zones.FirstOrDefault(z => string.Equals(z.Id, c.TargetId, StringComparison.OrdinalIgnoreCase));
                if (target == null) continue;

                PointF dest;
                if (!pointPosition.TryGetValue(target.Id, out dest)) dest = target.Center;

                string pointName = PointName(c.Side, c.SlotIndex);

                if (c.ByRoad && c.Route.Count >= 2)
                {
                    nc++;
                    convoyLines.Add("\t\t[" + nc + "] = ");
                    convoyLines.Add("\t\t{");
                    convoyLines.Add("\t\t\t[\"slot\"] = \"" + c.Slot + "\",");
                    convoyLines.Add("\t\t\t[\"name\"] = \"WG_CONVOY_" + c.Slot + "\",");
                    convoyLines.Add("\t\t\t[\"side\"] = \"" + c.Side + "\",");
                    // from : centre de la zone de départ (= route[1]) ; to : position du point de ravitaillement de destination
                    WargameZoneData sourceZone = zones.FirstOrDefault(z => string.Equals(z.Id, c.SourceId, StringComparison.OrdinalIgnoreCase));
                    PointF fromPos = sourceZone != null ? sourceZone.Center : target.Center;
                    convoyLines.Add("\t\t\t[\"from\"] = { [\"x\"] = " + Num(fromPos.X) + ", [\"y\"] = " + Num(fromPos.Y) + " },");
                    convoyLines.Add("\t\t\t[\"fromZone\"] = \"" + Esc(c.SourceId) + "\",");
                    convoyLines.Add("\t\t\t[\"to\"] = { [\"x\"] = " + Num(dest.X) + ", [\"y\"] = " + Num(dest.Y) + " },");
                    convoyLines.Add("\t\t\t[\"toZone\"] = \"" + Esc(c.TargetId) + "\",");
                    convoyLines.Add("\t\t\t[\"supplyPoint\"] = \"" + pointName + "\",");
                    convoyLines.Add("\t\t\t[\"destination\"] = { [\"x\"] = " + Num(dest.X) + ", [\"y\"] = " + Num(dest.Y) + " },");
                    convoyLines.Add("\t\t\t[\"route\"] = ");
                    convoyLines.Add("\t\t\t{");
                    int k = 0;
                    foreach (string id in c.Route)
                    {
                        WargameZoneData z = zones.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                        if (z == null) continue;
                        k++;
                        convoyLines.Add("\t\t\t\t[" + k + "] = { [\"zone\"] = \"" + Esc(z.Id) + "\", [\"x\"] = " + Num(z.Center.X) + ", [\"y\"] = " + Num(z.Center.Y) + " },");
                    }
                    convoyLines.Add("\t\t\t},");

                    // Véhicules : pris dans un template "convoy" du camp (le convoi lui-même est fabriqué en jeu)
                    List<WargameTemplateEntry> convoyTemplates = catalog.OfType(WargameUnitType.Convoy, c.Side);
                    WargameTemplateEntry chosen = convoyTemplates.Count > 0 ? convoyTemplates[rng.Next(convoyTemplates.Count)] : null;
                    string country = "", countryId = "";
                    List<string> vehicles = new List<string>();
                    if (chosen != null)
                        vehicles = ReadVehicleTypes(campaignInfo.GetTemplateFilePath(chosen.Template), out country, out countryId);
                    else if (missingConvoyTemplate.Add(c.Side))
                        report.Add("no 'convoy' template for " + c.Side + " in the template catalog : its road convoys will have an empty template / country / vehicles");

                    convoyLines.Add("\t\t\t[\"template\"] = \"" + Esc(chosen != null ? chosen.Template : "") + "\",");
                    convoyLines.Add("\t\t\t[\"country\"] = \"" + Esc(country) + "\",");             // pays du template (country.name du .stm)
                    if (countryId.Length > 0)
                        convoyLines.Add("\t\t\t[\"countryId\"] = " + countryId + ",");              // country.id du .stm
                    convoyLines.Add("\t\t\t[\"vehicles\"] = { " + string.Join(", ", vehicles.Select(v => "\"" + Esc(v) + "\"")) + " },");
                    if (chosen != null)
                    {
                        convoyLines.Add("\t\t\t[\"priority\"] = " + chosen.Priority + ",");
                        convoyLines.Add("\t\t\t[\"attributes\"] = { " + string.Join(", ", chosen.Attributes.Select(a => "\"" + Esc(a) + "\"")) + " },");
                    }
                    convoyLines.Add("\t\t},");
                }

                na++;
                airLines.Add("\t\t[" + na + "] = { [\"slot\"] = \"" + c.Slot + "\", [\"side\"] = \"" + c.Side + "\", [\"to\"] = \"" + Esc(c.TargetId)
                    + "\", [\"supplyPoint\"] = \"" + pointName + "\", [\"x\"] = " + Num(dest.X) + ", [\"y\"] = " + Num(dest.Y)
                    + ", [\"byRoad\"] = " + (c.ByRoad ? "true" : "false") + " },");
            }

            sb.AppendLine("\t[\"supplyPoints\"] = ");
            sb.AppendLine("\t{");
            foreach (string l in pointLines ?? new List<string>()) sb.AppendLine(l);
            sb.AppendLine("\t},");
            sb.AppendLine("\t[\"convoys\"] = ");
            sb.AppendLine("\t{");
            foreach (string l in convoyLines) sb.AppendLine(l);
            sb.AppendLine("\t},");
            sb.AppendLine("\t[\"air\"] = ");
            sb.AppendLine("\t{");
            foreach (string l in airLines) sb.AppendLine(l);
            sb.AppendLine("\t},");
            sb.AppendLine("}");

            File.WriteAllText(Path.Combine(campaignFolder, "Active", "wargame_logistics.lua"), sb.ToString(), new UTF8Encoding(false));
            return "logistics orders for scriptsMod : " + (pointLines == null ? 0 : pointLines.Count(l => l == "\t\t{")) + " supply point(s) to place, "
                + nc + " road convoy(s), " + na + " air request(s) (Active/wargame_logistics.lua)";
        }

        // Types DCS des véhicules d'un template .stm (pour fabriquer le convoi en jeu), et le pays qui les porte
        // (country.name / country.id du 1er pays qui a des véhicules ; à défaut, du 1er pays du template)
        public static List<string> ReadVehicleTypes(string stmPath, out string country, out string countryId)
        {
            var result = new List<string>();
            country = "";
            countryId = "";
            if (string.IsNullOrEmpty(stmPath) || !File.Exists(stmPath)) return result;

            // Repli : 1er pays du template, au cas où aucun pays n'a de véhicules (template de statiques)
            string firstName = "", firstId = "";

            try
            {
                using (Lua lua = new Lua())
                {
                    lua.DoString("os = nil\nio = nil\nfile = nil\ndebug = nil");
                    lua.DoFile(stmPath);

                    LuaTable root = lua["staticTemplate"] as LuaTable;
                    LuaTable coalitions = root == null ? null : root["coalition"] as LuaTable;
                    if (coalitions == null) return result;

                    foreach (object co in coalitions.Values)
                    {
                        LuaTable countries = (co as LuaTable) == null ? null : (co as LuaTable)["country"] as LuaTable;
                        if (countries == null) continue;

                        foreach (object cn in countries.Values)
                        {
                            LuaTable countryTable = cn as LuaTable;
                            if (countryTable == null) continue;

                            if (firstName.Length == 0)
                            {
                                firstName = Convert.ToString(countryTable["name"], CultureInfo.InvariantCulture) ?? "";
                                firstId = Convert.ToString(countryTable["id"], CultureInfo.InvariantCulture) ?? "";
                            }

                            LuaTable vehicle = countryTable["vehicle"] as LuaTable;
                            LuaTable groups = vehicle == null ? null : vehicle["group"] as LuaTable;
                            if (groups == null) continue;

                            if (country.Length == 0)
                            {
                                country = Convert.ToString(countryTable["name"], CultureInfo.InvariantCulture) ?? "";
                                countryId = Convert.ToString(countryTable["id"], CultureInfo.InvariantCulture) ?? "";
                            }

                            foreach (object g in groups.Values)
                            {
                                LuaTable units = (g as LuaTable) == null ? null : (g as LuaTable)["units"] as LuaTable;
                                if (units == null) continue;

                                foreach (object u in units.Values)
                                {
                                    string type = (u as LuaTable) == null ? null : (u as LuaTable)["type"] as string;
                                    if (!string.IsNullOrEmpty(type)) result.Add(type);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WargameLog.Line("WargameLogistics | types de véhicules illisibles dans " + stmPath + " : " + ex.Message);
            }

            if (country.Length == 0)
            {
                country = firstName;
                countryId = firstId;
            }

            return result;
        }

        // ---- Source capturée : un objectif-source dans une zone qui change de main passe à 0, définitivement ----
        // (le propriétaire le détruit avant de céder le terrain ; le preneur n'en tire rien, et une reprise ne le remet pas)
        public static int ZeroCapturedSupply(string targetlistPath, List<WargameZoneData> zones, ICollection<WargameZoneData> captured)
        {
            if (captured == null || captured.Count == 0 || !File.Exists(targetlistPath)) return 0;

            // Objectifs-sources situés dans une zone capturée (même lecture que le moteur : Parser_WargameObjectives)
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameObjective obj in new Parser_WargameObjectives().Load(targetlistPath))
            {
                if (obj.SupplyValue <= 0 || string.IsNullOrEmpty(obj.Name)) continue;
                WargameZoneData zone = WargameEngineCombat.FindZoneContaining(zones, obj.Position);
                if (zone != null && captured.Contains(zone)) names.Add(obj.Name);
            }
            if (names.Count == 0) return 0;

            List<string> lines = File.ReadAllLines(targetlistPath).ToList();
            LuaLineIndex idx = LuaLineIndex.Build(lines);
            var edits = new LuaLineEdits();
            int zeroed = 0;

            foreach (string table in idx.Tables)
            {
                string[] p = table.Split('/');
                if (p.Length != 2) continue;

                string name = idx.Text(table, "titleName") ?? idx.Text(table, "name") ?? p[1];
                if (!names.Contains(name)) continue;

                edits.SetField(idx, table, "wargameSupply", "0");
                edits.SetField(idx, table, "wargameSupplyLost", "true");
                zeroed++;
            }

            if (edits.Count > 0)
                File.WriteAllLines(targetlistPath, edits.Apply(lines));

            return zeroed;
        }

        // Numéro de mission courant (camp.mission dans camp_status.lua), 0 si illisible
        public static int ReadMission(string campaignFolder)
        {
            try
            {
                string path = Path.Combine(campaignFolder, "Active", "camp_status.lua");
                if (!File.Exists(path)) return 0;
                Match m = Regex.Match(File.ReadAllText(path), @"(?m)^\t\[""mission""\]\s*=\s*(\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }
        private static string Num(double v) { return v.ToString(CultureInfo.InvariantCulture); }
    }

    // ==========================================================================================
    // DCS : progression de la campagne (Saved Games\DCS\MissionEditor\logbook.lua)
    //
    // Vérifié sur un vrai logbook.lua : logbook.players[n].games[k] = { campaign = "<chemin>/Qeshm-Blue.cmp", history = { ... } }.
    // Chaque entrée de history = une mission ; une mission JOUEE a datetime / result / kills ; la DERNIERE entrée donne la
    // prochaine mission à lancer : stage (1 = _first, 2 = _ongoing) et mission (fichier).
    // Pour sauter la 1re mission : l'entrée stage 1 non jouée devient "jouée" (result 51), et une entrée stage 2 est ajoutée.
    // DCS réécrit ce fichier en quittant : il doit être FERME pendant la modification.
    // ==========================================================================================
    internal static class DcsLogbook
    {
        // Dossier Saved Games de DCS : camp.path dans Active/camp_status.lua (ex. "C:/Users/miguel/Saved Games/DCS/")
        public static string ReadSavedGamesPath(string campaignFolder)
        {
            try
            {
                string path = Path.Combine(campaignFolder, "Active", "camp_status.lua");
                if (!File.Exists(path)) return null;

                Match m = Regex.Match(File.ReadAllText(path), @"(?m)^\t\[""path""\]\s*=\s*""([^""]*)""");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        // logbook.lua possibles : camp.path, puis les dossiers Saved Games habituels (DCS et DCS.openbeta)
        private static List<string> CandidateLogbooks(string savedGamesPath)
        {
            var dirs = new List<string>();
            if (!string.IsNullOrEmpty(savedGamesPath)) dirs.Add(savedGamesPath);

            string userSaved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
            dirs.Add(Path.Combine(userSaved, "DCS"));
            dirs.Add(Path.Combine(userSaved, "DCS.openbeta"));

            var result = new List<string>();
            foreach (string d in dirs)
            {
                string p = Path.GetFullPath(Path.Combine(d.Replace('/', '\\'), "MissionEditor", "logbook.lua"));
                if (File.Exists(p) && !result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
            }
            return result;
        }

        public static bool TrySkipFirstMission(string savedGamesPath, string campaignName, out string message)
        {
            message = "";
            List<string> candidates = CandidateLogbooks(savedGamesPath);
            if (candidates.Count == 0) { message = "logbook.lua not found (DCS Saved Games\\MissionEditor) : NOT changed"; return false; }

            var notes = new List<string>();
            foreach (string path in candidates)
            {
                string m;
                bool found;
                bool ok = TrySkipIn(path, campaignName, out m, out found);
                if (found) { message = m + " (" + path + ")"; return ok; }
                notes.Add(m);
            }

            message = string.Join(" / ", notes);
            return false;
        }

        // found = la campagne existe dans ce logbook (on s'arrête là, réussi ou non)
        private static bool TrySkipIn(string path, string campaignName, out string message, out bool found)
        {
            message = "";
            found = false;
            try
            {
                List<string> lines = File.ReadAllLines(path).ToList();
                LuaLineIndex idx = LuaLineIndex.Build(lines);

                // Entrée de la campagne : "campaign" se termine par /<nom>.cmp (la plus récente s'il y en a plusieurs)
                string game = null;
                int gameKey = int.MinValue;
                string suffix = campaignName + ".cmp";
                foreach (string table in idx.Tables)
                {
                    string[] p = table.Split('/');
                    if (p.Length < 2 || p[p.Length - 2] != "games") continue;

                    string campaign = (idx.Text(table, "campaign") ?? "").Replace('\\', '/');
                    if (!campaign.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase) && !string.Equals(campaign, suffix, StringComparison.OrdinalIgnoreCase)) continue;

                    int k;
                    if (!int.TryParse(p[p.Length - 1], out k)) k = 0;
                    if (k > gameKey) { gameKey = k; game = table; }
                }

                if (game == null) { message = "campaign '" + campaignName + "' not found in " + path + " : start it once in DCS (then quit) and try again"; return false; }
                found = true;

                string history = game + "/history";
                int historyDepth;
                if (!idx.Depth.TryGetValue(history, out historyDepth)) { message = "no history for '" + campaignName + "' in logbook.lua"; return false; }

                // Dernière entrée
                string last = null;
                int lastN = 0;
                foreach (string table in idx.Tables)
                {
                    if (!table.StartsWith(history + "/", StringComparison.Ordinal)) continue;
                    string rest = table.Substring(history.Length + 1);
                    int n;
                    if (rest.Contains("/") || !int.TryParse(rest, out n)) continue;
                    if (n > lastN) { lastN = n; last = table; }
                }

                string ongoing = campaignName + "_ongoing.miz";
                var edits = new LuaLineEdits();

                if (last != null && idx.Number(last, "stage", 1) >= 2)
                {
                    // Déjà à l'étape 2 : on vérifie juste le nom de la mission
                    edits.SetField(idx, last, "mission", "\"" + ongoing + "\"");
                    message = "logbook.lua : '" + campaignName + "' already at stage 2 (" + ongoing + ")";
                }
                else
                {
                    // L'entrée de la 1re mission (non jouée) devient jouée, puis une entrée stage 2 est ajoutée
                    if (last != null && idx.Value(last, "result") == null)
                    {
                        edits.SetField(idx, last, "datetime", "\"" + DateTime.Now.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture) + "\"");
                        edits.SetField(idx, last, "result", "51");
                        edits.SetField(idx, last, "aaKills", "0");
                        edits.SetField(idx, last, "agKills", "0");
                        edits.SetField(idx, last, "deathsCount", "0");
                    }

                    int close;
                    if (!idx.Close.TryGetValue(history, out close)) { message = "logbook.lua : history block not understood, NOT changed"; return false; }

                    int next = lastN + 1;
                    string t = new string('\t', historyDepth + 1);
                    edits.InsertBefore(close, t + "[" + next + "] = ");
                    edits.InsertBefore(close, t + "{");
                    edits.InsertBefore(close, t + "\t[\"stage\"] = 2,");
                    edits.InsertBefore(close, t + "\t[\"mission\"] = \"" + ongoing + "\",");
                    edits.InsertBefore(close, t + "}, -- end of [" + next + "]");

                    message = "logbook.lua : '" + campaignName + "' moved to stage 2 (" + ongoing + "), the first mission counts as played";
                }

                File.Copy(path, path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"), true);
                File.WriteAllLines(path, edits.Apply(lines));
                return true;
            }
            catch (Exception ex)
            {
                message = "logbook.lua NOT changed : " + ex.Message;
                return false;
            }
        }
    }
}
