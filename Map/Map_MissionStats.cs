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
    // Chiffres de la mission affichés dans l'encart "Mission content" de la carte de mission.
    //  - groupes / unités : comptés dans le fichier "mission" du .miz
    //  - modules requis : table requiredModules du même fichier
    //  - éléments supprimés par DC_Prune : écrits par la fenêtre ScriptsMod dans l10n/DEFAULT/camp_status.lua du .miz (entrée ["dcem_stats"] de campL)
    internal class Map_StatsData
    {
        public int[] Plane = new int[2], Heli = new int[2], Ship = new int[2], Vehicle = new int[2], Static = new int[2];     // { groupes, unités }
        public List<string> Modules = new List<string>();
        public int Pruned = -1, Kept = -1;                                              // -1 = inconnu
        public int StatPlaneBefore = -1, StatPlaneAfter = -1, StatHeliBefore = -1, StatHeliAfter = -1;
        public int TotalGroups { get { return Plane[0] + Heli[0] + Ship[0] + Vehicle[0] + Static[0]; } }
        public int TotalUnits { get { return Plane[1] + Heli[1] + Ship[1] + Vehicle[1] + Static[1]; } }
    }

    internal static class Map_MissionStats
    {
        private const string CampStatusFileName = "camp_status.lua";
        private const string StatsKey = "dcem_stats";

        private static Dictionary<string, object> SubD(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        // Le camp_status.lua du .miz (n'importe où dans le zip, avec / ou \), sinon null
        private static ZipArchiveEntry FindCampStatus(ZipArchive zip)
        {
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                string n = e.FullName.Replace('\\', '/');
                if (n.Equals(CampStatusFileName, StringComparison.OrdinalIgnoreCase) || n.EndsWith("/" + CampStatusFileName, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        public static Map_StatsData Load(string miz)
        {
            var d = new Map_StatsData();
            if (string.IsNullOrEmpty(miz) || !File.Exists(miz)) return d;

            try
            {
                Dictionary<string, object> root = Map_Drawings.ReadMissionRoot(miz);      // déjà en cache si la carte vient de lire ce fichier
                Dictionary<string, object> coalitions = SubD(root, "coalition");
                if (coalitions != null)
                {
                    foreach (object co in coalitions.Values)
                    {
                        Dictionary<string, object> countries = SubD(co as Dictionary<string, object>, "country");
                        if (countries == null) continue;
                        foreach (object cy in countries.Values)
                        {
                            var country = cy as Dictionary<string, object>;
                            Count(country, "plane", d.Plane);
                            Count(country, "helicopter", d.Heli);
                            Count(country, "ship", d.Ship);
                            Count(country, "vehicle", d.Vehicle);
                            Count(country, "static", d.Static);
                        }
                    }
                }

                Dictionary<string, object> mods = SubD(root, "requiredModules");
                if (mods != null)
                    foreach (KeyValuePair<string, object> kv in mods)
                    {
                        string name = kv.Value as string;
                        if (string.IsNullOrEmpty(name)) name = kv.Key;
                        if (!d.Modules.Contains(name)) d.Modules.Add(name);
                    }
                d.Modules.Sort(StringComparer.OrdinalIgnoreCase);

                ReadPrune(miz, d);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_MissionStats | Load | " + ex.Message);
            }
            return d;
        }

        private static void Count(Dictionary<string, object> country, string category, int[] total)
        {
            Dictionary<string, object> groups = SubD(SubD(country, category), "group");
            if (groups == null) return;
            foreach (object go in groups.Values)
            {
                var g = go as Dictionary<string, object>;
                if (g == null) continue;
                total[0]++;
                Dictionary<string, object> units = SubD(g, "units");
                total[1] += units == null ? 0 : units.Count;
            }
        }

        private static void ReadPrune(string miz, Map_StatsData d)
        {
            using (ZipArchive zip = ZipFile.OpenRead(miz))
            {
                ZipArchiveEntry entry = FindCampStatus(zip);
                if (entry == null) return;
                string text;
                using (var sr = new StreamReader(entry.Open(), Encoding.UTF8)) text = sr.ReadToEnd();

                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, "\\[\"" + StatsKey + "\"\\]\\s*=\\s*\\{([^{}]*)\\}");
                if (!m.Success) return;
                foreach (System.Text.RegularExpressions.Match kv in System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, "\\[\"(\\w+)\"\\]\\s*=\\s*(-?\\d+)"))
                {
                    int v = int.Parse(kv.Groups[2].Value, CultureInfo.InvariantCulture);
                    switch (kv.Groups[1].Value)
                    {
                        case "pruned": d.Pruned = v; break;
                        case "kept": d.Kept = v; break;
                        case "static_planes_before": d.StatPlaneBefore = v; break;
                        case "static_planes_after": d.StatPlaneAfter = v; break;
                        case "static_helis_before": d.StatHeliBefore = v; break;
                        case "static_helis_after": d.StatHeliAfter = v; break;
                    }
                }
            }
        }

        // ---- Debug de la mission : ["debug"] de la table campL du camp_status.lua du .miz ----

        // Cherche une clé au 1er niveau de campL (pas dans les sous-tables). Rend la place de sa valeur (vStart..vEnd) ou, sinon, l'endroit où l'ajouter.
        private static bool FindCampLKey(string text, string key, out int vStart, out int vEnd, out int insertAt)
        {
            vStart = vEnd = insertAt = -1;
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, "campL\\s*=\\s*\\{");
            if (!m.Success) return false;
            insertAt = m.Index + m.Length;

            string quoted = "[\"" + key + "\"]";
            int depth = 1, i = insertAt, n = text.Length;
            while (i < n && depth > 0)
            {
                char c = text[i];
                if (c == '-' && i + 1 < n && text[i + 1] == '-') { while (i < n && text[i] != '\n') i++; continue; }          // commentaire
                if (c == '"' || c == '\'') { i++; while (i < n && text[i] != c) { if (text[i] == '\\') i++; i++; } i++; continue; }       // texte
                if (c == '{') { depth++; i++; continue; }
                if (c == '}') { depth--; i++; continue; }

                if (depth == 1)
                {
                    int after = -1;
                    if (c == '[' && string.CompareOrdinal(text, i, quoted, 0, quoted.Length) == 0) after = i + quoted.Length;
                    else if (char.IsLetter(c) && (i == 0 || !(char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')) && string.CompareOrdinal(text, i, key, 0, key.Length) == 0
                             && (i + key.Length >= n || !(char.IsLetterOrDigit(text[i + key.Length]) || text[i + key.Length] == '_')))
                        after = i + key.Length;

                    if (after > 0)
                    {
                        int j = after;
                        while (j < n && char.IsWhiteSpace(text[j])) j++;
                        if (j < n && text[j] == '=')
                        {
                            j++;
                            while (j < n && char.IsWhiteSpace(text[j])) j++;
                            vStart = j;
                            while (j < n && (char.IsLetterOrDigit(text[j]) || text[j] == '.' || text[j] == '-' || text[j] == '_')) j++;
                            vEnd = j;
                            return true;
                        }
                    }
                }
                i++;
            }
            return false;
        }

        // true / false = valeur écrite ; null = pas de camp_status.lua ou pas de clé "debug" (alors considéré comme faux)
        // Noms des variables de campL lues par DCE_InGame (à adapter ici si les noms réels sont différents)
        public const string KeyDebug = "debug";
        public const string KeyTracePlayers = "debugTracePlayers";
        public const string KeyTraceAI = "debugTraceAI";

        public static bool? ReadCampDebug(string miz, out bool hasFile) { return ReadCampBool(miz, KeyDebug, out hasFile); }
        public static bool WriteCampDebug(string miz, bool on) { return WriteCampBool(miz, KeyDebug, on); }

        public static bool? ReadCampBool(string miz, string key, out bool hasFile)
        {
            hasFile = false;
            try
            {
                if (string.IsNullOrEmpty(miz) || !File.Exists(miz)) return null;
                using (ZipArchive zip = ZipFile.OpenRead(miz))
                {
                    ZipArchiveEntry entry = FindCampStatus(zip);
                    if (entry == null) return null;
                    hasFile = true;
                    string text;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8)) text = sr.ReadToEnd();
                    int vs, ve, ins;
                    if (!FindCampLKey(text, key, out vs, out ve, out ins)) return null;
                    return text.Substring(vs, ve - vs) == "true";
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_MissionStats | ReadCampBool " + key + " | " + ex.Message);
                return null;
            }
        }

        public static bool WriteCampBool(string miz, string key, bool on)
        {
            try
            {
                using (var fs = new FileStream(miz, FileMode.Open, FileAccess.ReadWrite))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
                {
                    ZipArchiveEntry entry = FindCampStatus(zip);
                    if (entry == null) return false;
                    string name = entry.FullName, text;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8)) text = sr.ReadToEnd();

                    int vs, ve, ins;
                    string val = on ? "true" : "false";
                    if (FindCampLKey(text, key, out vs, out ve, out ins)) text = text.Substring(0, vs) + val + text.Substring(ve);
                    else if (ins >= 0) text = text.Insert(ins, (text.Contains("\r\n") ? "\r\n" : "\n") + "\t[\"" + key + "\"] = " + val + ",");
                    else return false;

                    entry.Delete();
                    ZipArchiveEntry created = zip.CreateEntry(name);
                    using (var sw = new StreamWriter(created.Open(), new UTF8Encoding(false))) sw.Write(text);
                }
                FormUtils.LogRegister("Map_MissionStats | " + key + " = " + on + " written in camp_status.lua of " + Path.GetFileName(miz));
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_MissionStats | WriteCampBool " + key + " | " + ex.Message);
                return false;
            }
        }

        // Écrit (ou remplace) ["dcem_stats"] dans la table campL du camp_status.lua du .miz. Appelé par la fenêtre ScriptsMod.
        // Seul ce fichier du zip est réécrit ; la mission elle-même n'est pas touchée.
        public static bool WritePrune(string miz, int pruned, int kept, int planesBefore, int planesAfter, int helisBefore, int helisAfter)
        {
            if (string.IsNullOrEmpty(miz) || !File.Exists(miz) || pruned < 0) return false;
            try
            {
                using (var fs = new FileStream(miz, FileMode.Open, FileAccess.ReadWrite))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
                {
                    ZipArchiveEntry entry = FindCampStatus(zip);
                    if (entry == null) { FormUtils.LogRegister("Map_MissionStats | no " + CampStatusFileName + " in " + Path.GetFileName(miz)); return false; }

                    string name = entry.FullName, text;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8)) text = sr.ReadToEnd();

                    string nl = text.Contains("\r\n") ? "\r\n" : "\n";
                    // l'ancienne entrée (d'une mission précédente, campL est repris d'une mission à l'autre) disparaît d'abord
                    text = System.Text.RegularExpressions.Regex.Replace(text, "[ \\t]*\\[\"" + StatsKey + "\"\\]\\s*=\\s*\\{[^{}]*\\},?[ \\t]*\\r?\\n?", "");

                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, "campL\\s*=\\s*\\{");
                    if (!m.Success) { FormUtils.LogRegister("Map_MissionStats | campL table not found in " + CampStatusFileName); return false; }

                    string block = nl + "\t[\"" + StatsKey + "\"] = {[\"pruned\"] = " + pruned + ", [\"kept\"] = " + kept
                        + (planesBefore >= 0 ? ", [\"static_planes_before\"] = " + planesBefore + ", [\"static_planes_after\"] = " + planesAfter : "")
                        + (helisBefore >= 0 ? ", [\"static_helis_before\"] = " + helisBefore + ", [\"static_helis_after\"] = " + helisAfter : "")
                        + "},";
                    text = text.Insert(m.Index + m.Length, block);

                    entry.Delete();
                    ZipArchiveEntry created = zip.CreateEntry(name);
                    using (var sw = new StreamWriter(created.Open(), new UTF8Encoding(false))) sw.Write(text);
                }
                FormUtils.LogRegister("Map_MissionStats | prune stats written in " + Path.GetFileName(miz) + " : " + pruned + " removed, " + kept + " kept");
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_MissionStats | WritePrune | " + ex.Message);
                return false;
            }
        }
    }
}
