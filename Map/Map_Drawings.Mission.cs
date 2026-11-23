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
    // Partie de Map_Drawings (voir Map_Drawings.cs) : choix et lecture du .miz de la mission
    internal static partial class Map_Drawings
    {
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
                        data.HiddenGround.RemoveAll(u => !mySides.Contains(u.Red ? "red" : "blue"));
                        data.HiddenGroundGroups.RemoveAll(u => !mySides.Contains(u.Red ? "red" : "blue"));
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
    }
}
