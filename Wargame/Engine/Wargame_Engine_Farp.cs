using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DCE_Manager
{
    // FARP d'une plage de débarquement : future base de ravitaillement du camp qui débarque (hélicos, convois).
    // Une FARP par PLAGE (zone visée par un ou plusieurs plans LANDING_n), posée par défaut au milieu de la zone.
    // Elle est enregistrée dans Init/db_airbases.lua, dans un bloc délimité par deux lignes de commentaire :
    // tout le reste du fichier n'est jamais touché. Les identifiants airdromeId / helipadId restent vides :
    // ils dépendent des unités posées dans la mission, donc scriptsMod les remplit quand il place le template "farp".
    internal class WargameFarp
    {
        public string Name = "";      // nom de la base dans db_airbases : "FARP-<plage>"
        public string Beach = "";     // zone-plage (WargameZoneData.Id)
        public string Plan = "";      // un des plans qui visent cette plage (information)
        public string Side = WargameSide.Blue;
        public double X, Y;           // coordonnées DCS
    }

    internal static class WargameFarpStore
    {
        private const string BeginMark = "-- >>> DCE_Manager wargame FARPs (automatic block, edited from the wargame map) >>>";
        private const string EndMark = "-- <<< DCE_Manager wargame FARPs <<<";

        public static string GetInitPath(string campaignName)
        {
            return Path.Combine(WargameZoneRepository.GetCampaignFolder(campaignName), "Init", "db_airbases.lua");
        }

        public static string NameFor(string beachId)
        {
            return "FARP-" + (beachId ?? "").Replace("'", "").Replace("\"", "").Trim();
        }

        // ---------------- Lecture ----------------

        private static readonly Regex NameRegex = new Regex(@"^\s*\['(?<n>[^']*)'\]\s*=\s*\{?\s*$");
        private static readonly Regex NumRegex = new Regex(@"^\s*(?<k>x|y)\s*=\s*(?<v>-?[0-9.eE+\-]+)\s*,");
        private static readonly Regex StrRegex = new Regex(@"^\s*(?<k>side|wargamePlan|wargameBeach)\s*=\s*'(?<v>[^']*)'\s*,");

        public static List<WargameFarp> Load(string campaignName)
        {
            return LoadFile(GetInitPath(campaignName));
        }

        // Même chose depuis un chemin de dossier de campagne déjà connu
        public static List<WargameFarp> LoadFromFolder(string campaignFolder)
        {
            return LoadFile(Path.Combine(campaignFolder, "Init", "db_airbases.lua"));
        }

        private static List<WargameFarp> LoadFile(string path)
        {
            var list = new List<WargameFarp>();

            try
            {
                if (!File.Exists(path)) return list;

                bool inside = false;
                WargameFarp current = null;

                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.Trim() == BeginMark) { inside = true; continue; }
                    if (line.Trim() == EndMark) { inside = false; current = null; continue; }
                    if (!inside) continue;

                    Match m = NameRegex.Match(line);
                    if (m.Success)
                    {
                        current = new WargameFarp { Name = m.Groups["n"].Value };
                        list.Add(current);
                        continue;
                    }

                    if (current == null) continue;

                    m = NumRegex.Match(line);
                    if (m.Success)
                    {
                        double v;
                        if (double.TryParse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        {
                            if (m.Groups["k"].Value == "x") current.X = v; else current.Y = v;
                        }
                        continue;
                    }

                    m = StrRegex.Match(line);
                    if (m.Success)
                    {
                        string k = m.Groups["k"].Value, v = m.Groups["v"].Value;
                        if (k == "side") current.Side = v;
                        else if (k == "wargamePlan") current.Plan = v;
                        else if (k == "wargameBeach") current.Beach = v;
                    }
                }

                // Une entrée sans plage (fichier modifié à la main) ne peut pas être suivie : on la retrouve par son nom
                foreach (WargameFarp f in list)
                    if (f.Beach.Length == 0 && f.Name.StartsWith("FARP-", StringComparison.OrdinalIgnoreCase))
                        f.Beach = f.Name.Substring(5);
            }
            catch (Exception ex)
            {
                WargameLog.Line("WargameFarp | db_airbases.lua NOT read : " + ex.Message);
            }

            return list;
        }

        // ---------------- Ecriture ----------------

        private static string Num(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        // Fréquence ATC distincte par FARP (bleu 128.x, rouge 129.x) : le numéro suit l'ordre de la liste
        private static string Frequency(WargameFarp f, int index)
        {
            double mhz = (string.Equals(f.Side, WargameSide.Red, StringComparison.OrdinalIgnoreCase) ? 129.0 : 128.0) + 0.1 * ((index % 9) + 1);
            return mhz.ToString("0.000", CultureInfo.InvariantCulture);
        }

        // La FARP est utilisable (inactive = false) dès que sa plage est tenue par son camp
        public static bool IsHeld(WargameFarp farp, List<WargameZoneData> zones)
        {
            if (zones == null) return false;
            WargameZoneData beach = zones.FirstOrDefault(z => string.Equals(z.Id, farp.Beach, StringComparison.OrdinalIgnoreCase));
            return beach != null && string.Equals(beach.Control, farp.Side, StringComparison.OrdinalIgnoreCase);
        }

        // zones : état à écrire pour "inactive" (plage tenue = false). null = toutes inactives.
        public static void Save(string campaignName, List<WargameFarp> farps, List<WargameZoneData> zones = null)
        {
            try
            {
                string path = GetInitPath(campaignName);
                if (!File.Exists(path))
                {
                    WargameLog.Line("WargameFarp | Init/db_airbases.lua not found : FARPs NOT saved (" + path + ")");
                    return;
                }

                List<string> lines = File.ReadAllLines(path).ToList();

                // 1. on enlève l'ancien bloc automatique
                int begin = lines.FindIndex(l => l.Trim() == BeginMark);
                if (begin >= 0)
                {
                    int end = lines.FindIndex(begin, l => l.Trim() == EndMark);
                    if (end < 0) end = begin;
                    lines.RemoveRange(begin, end - begin + 1);
                }

                // 2. on écrit le nouveau juste avant l'accolade finale de la table db_airbases
                if (farps != null && farps.Count > 0)
                {
                    int close = lines.FindLastIndex(l => l.Trim() == "}");
                    if (close < 0)
                    {
                        WargameLog.Line("WargameFarp | closing brace of db_airbases not found : FARPs NOT saved");
                        return;
                    }

                    var block = new List<string>();
                    block.Add("\t" + BeginMark);
                    block.Add("\t-- airdromeId / helipadId are NOT set here : scriptsMod fills them when it places the 'farp' template of the side.");

                    for (int i = 0; i < farps.Count; i++)
                    {
                        WargameFarp f = farps[i];
                        block.Add("\t['" + f.Name + "'] = {");
                        block.Add("\t\tx = " + Num(f.X) + ",");
                        block.Add("\t\ty = " + Num(f.Y) + ",");
                        block.Add("\t\televation = 0,");
                        block.Add("\t\tATC_frequency = \"" + Frequency(f, i) + "\",");
                        block.Add("\t\tside = '" + f.Side + "',");
                        block.Add("\t\tdivert = false,");
                        block.Add("\t\tLimitedParkNb = 4,");
                        block.Add("\t\tinactive = " + (IsHeld(f, zones) ? "false" : "true") + ",");
                        WargameFarpTrace.Note(campaignName, "Save Init db_airbases : " + f.Name + " inactive = " + (IsHeld(f, zones) ? "false" : "true") + (zones == null ? " (no zones given : all inactive)" : ""));
                        block.Add("\t\twargameFarp = true,");
                        block.Add("\t\twargamePlan = '" + f.Plan + "',");
                        block.Add("\t\twargameBeach = '" + f.Beach + "',");
                        block.Add("\t},");
                    }

                    block.Add("\t" + EndMark);
                    lines.InsertRange(close, block);
                }

                File.WriteAllLines(path, lines, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                WargameLog.Line("WargameFarp | db_airbases.lua NOT written : " + ex.Message);
            }
        }

        // ---------------- Active/db_airbases.lua ----------------
        // Le fichier Active est écrit par DCE (["clé"] = valeur). On y met à jour ce qui change en cours de campagne,
        // sans dépendre de scriptsMod : la position (déplacement dans l'éditeur) et, si updateInactive, l'état
        // (inactive = false dès que la plage est tenue par le camp de la FARP). Une FARP absente du fichier y est ajoutée.
        // Renvoie un texte pour le journal ("" = rien à dire).
        public static string SyncActive(string campaignName, List<WargameFarp> farps, List<WargameZoneData> zones, bool updateInactive)
        {
            if (farps == null || farps.Count == 0) return "";

            try
            {
                string path = Path.Combine(WargameZoneRepository.GetCampaignFolder(campaignName), "Active", "db_airbases.lua");
                if (!File.Exists(path)) return "";   // campagne pas encore lancée : l'Init sera copié au premier lancement

                List<string> lines = File.ReadAllLines(path).ToList();
                int changed = 0, added = 0, active = 0;

                foreach (WargameFarp f in farps)
                {
                    bool inactive = !IsHeld(f, zones);
                    if (!inactive) active++;

                    WargameZoneData beachZone = zones == null ? null : zones.FirstOrDefault(z => string.Equals(z.Id, f.Beach, StringComparison.OrdinalIgnoreCase));
                    WargameFarpTrace.Note(campaignName, "SyncActive " + f.Name + " : beach " + f.Beach + " control " + (beachZone == null ? "zone not found" : beachZone.Control)
                        + " / farp side " + f.Side + " -> inactive = " + (inactive ? "true" : "false") + (updateInactive ? "" : " (NOT written : position only)"));

                    int start = lines.FindIndex(l => l.Trim() == "[\"" + f.Name + "\"] =" || l.Trim() == "['" + f.Name + "'] =");

                    if (start < 0)
                    {
                        int close = lines.FindLastIndex(l => l.Trim() == "}");
                        if (close < 0) continue;

                        var block = new List<string>
                        {
                            "\t[\"" + f.Name + "\"] = ",
                            "\t{",
                            "\t\t[\"wargameFarp\"] = true,",
                            "\t\t[\"side\"] = \"" + f.Side + "\",",
                            "\t\t[\"elevation\"] = 0,",
                            "\t\t[\"wargameBeach\"] = \"" + f.Beach + "\",",
                            "\t\t[\"runwayAlive\"] = 100,",
                            "\t\t[\"inactive\"] = " + (inactive ? "true" : "false") + ",",
                            "\t\t[\"y\"] = " + Num(f.Y) + ",",
                            "\t\t[\"x\"] = " + Num(f.X) + ",",
                            "\t\t[\"name\"] = \"" + f.Name + "\",",
                            "\t\t[\"wargamePlan\"] = \"" + f.Plan + "\",",
                            "\t\t[\"baseAlive\"] = 100,",
                            "\t\t[\"LimitedParkNb\"] = 4,",
                            "\t\t[\"divert\"] = false,",
                            "\t\t[\"ATC_frequency\"] = \"" + Frequency(f, farps.IndexOf(f)) + "\",",
                            "\t},",
                        };
                        lines.InsertRange(close, block);
                        added++;
                        continue;
                    }

                    // fin du bloc : première ligne "}," à la même indentation que l'ouverture
                    string indent = lines[start].Substring(0, lines[start].Length - lines[start].TrimStart().Length);
                    int end = lines.FindIndex(start + 1, l => l == indent + "}," || l == indent + "}");
                    if (end < 0) continue;

                    bool touched = SetField(lines, start, ref end, "x", Num(f.X));
                    touched |= SetField(lines, start, ref end, "y", Num(f.Y));
                    if (updateInactive) touched |= SetField(lines, start, ref end, "inactive", inactive ? "true" : "false");
                    if (touched) changed++;
                }

                if (changed > 0 || added > 0)
                    File.WriteAllLines(path, lines, new UTF8Encoding(false));

                return "FARPs in Active/db_airbases.lua : " + farps.Count + " (" + active + " active, " + added + " added, " + changed + " updated)";
            }
            catch (Exception ex)
            {
                return "FARPs in Active/db_airbases.lua NOT updated : " + ex.Message;
            }
        }

        // Change la valeur du champ ["clé"] = ... dans le bloc [start..end] (ajoute la ligne si absente). true = le fichier change.
        private static bool SetField(List<string> lines, int start, ref int end, string key, string value)
        {
            for (int i = start + 1; i < end; i++)
            {
                string t = lines[i].Trim();
                if (!t.StartsWith("[\"" + key + "\"]")) continue;

                string indent = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                string next = indent + "[\"" + key + "\"] = " + value + ",";
                if (lines[i] == next) return false;

                lines[i] = next;
                return true;
            }

            lines.Insert(end, "\t\t[\"" + key + "\"] = " + value + ",");
            end++;
            return true;
        }

        // ---------------- Règles ----------------

        // Une FARP par plage visée par au moins un plan de débarquement ; les FARP dont la plage n'est plus visée disparaissent.
        // Une FARP qui existe déjà garde sa position (le campaignMaker a pu la déplacer). Renvoie true si la liste a changé.
        public static bool Sync(List<WargameFarp> farps, List<WargameZoneData> zones)
        {
            bool changed = false;
            var wanted = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);   // plage -> plan

            foreach (WargameZoneData plan in zones.Where(z => WargameEngineCombat.IsLandingZone(z)))
            {
                if (!WargameEngineCombat.IsFightingSide(plan.Control) || string.IsNullOrEmpty(plan.LandingTarget)) continue;
                if (!wanted.ContainsKey(plan.LandingTarget)) wanted[plan.LandingTarget] = plan;
            }

            changed |= farps.RemoveAll(f => !wanted.ContainsKey(f.Beach)) > 0;

            foreach (KeyValuePair<string, WargameZoneData> kvp in wanted)
            {
                WargameZoneData beach = zones.FirstOrDefault(z => string.Equals(z.Id, kvp.Key, StringComparison.OrdinalIgnoreCase));
                if (beach == null) continue;

                WargameFarp farp = farps.FirstOrDefault(f => string.Equals(f.Beach, kvp.Key, StringComparison.OrdinalIgnoreCase));

                if (farp == null)
                {
                    farps.Add(new WargameFarp
                    {
                        Name = NameFor(beach.Id),
                        Beach = beach.Id,
                        Plan = kvp.Value.Id,
                        Side = kvp.Value.Control,
                        X = beach.Center.X,
                        Y = beach.Center.Y,
                    });
                    changed = true;
                }
                else if (farp.Side != kvp.Value.Control || farp.Plan != kvp.Value.Id)
                {
                    farp.Side = kvp.Value.Control;
                    farp.Plan = kvp.Value.Id;
                    changed = true;
                }
            }

            return changed;
        }

        // Remet une FARP au milieu de sa plage
        public static void Recenter(WargameFarp farp, List<WargameZoneData> zones)
        {
            WargameZoneData beach = zones.FirstOrDefault(z => string.Equals(z.Id, farp.Beach, StringComparison.OrdinalIgnoreCase));
            if (beach == null) return;

            farp.X = beach.Center.X;
            farp.Y = beach.Center.Y;
        }
    }

    // ==========================================================================================
    // Trace des FARP du wargame : pour trouver QUI remet "inactive" à true.
    // Fichier unique, jamais effacé : <campagne>\Debug\Wargame\wargame_farp_trace.txt (on y AJOUTE des lignes).
    // Chaque "Snapshot" liste, pour chaque FARP : inactive dans Init et dans Active, la plage, son camp actuel, et l'heure
    // de dernière écriture de Active\db_airbases.lua (un scriptsMod qui réécrit le fichier change cette heure).
    // En comparant deux snapshots à la suite, on voit entre lesquels l'état a changé.
    // ==========================================================================================
    internal static class WargameFarpTrace
    {
        private static readonly object _lock = new object();

        public static string FilePath(string campaignName)
        {
            return Path.Combine(WargameLog.Folder(campaignName), "wargame_farp_trace.txt");
        }

        // Une ligne dans le fichier de trace (et dans le journal wargame en cours, s'il y en a un)
        public static void Note(string campaignName, string text)
        {
            try
            {
                lock (_lock)
                {
                    string path = FilePath(campaignName);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + text + Environment.NewLine, new UTF8Encoding(false));
                }
                WargameLog.Line("FARPTRACE " + text);
            }
            catch { }
        }

        // État de tous les FARP du wargame à cet instant. step = où on en est (texte libre).
        public static void Snapshot(string campaignName, string step)
        {
            try
            {
                string folder = WargameZoneRepository.GetCampaignFolder(campaignName);
                string initPath = Path.Combine(folder, "Init", "db_airbases.lua");
                string activePath = Path.Combine(folder, "Active", "db_airbases.lua");

                Note(campaignName, "==== " + step + " | mission " + WargameLogistics.ReadMission(folder)
                    + " | Active db_airbases.lua written " + (File.Exists(activePath) ? File.GetLastWriteTime(activePath).ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "(missing)") + " ====");

                var init = ReadFarps(initPath);
                var active = ReadFarps(activePath);

                List<WargameZoneData> zones = null;
                try
                {
                    zones = WargameZoneRepository.LoadOrGenerateInit(campaignName);
                    new WargameZoneActiveLoader().ApplyActiveState(WargameZoneRepository.GetActiveWargameZonesPath(campaignName), zones);
                }
                catch { zones = null; }

                var names = new SortedSet<string>(init.Keys.Concat(active.Keys), StringComparer.OrdinalIgnoreCase);
                if (names.Count == 0) Note(campaignName, "   no wargameFarp entry in Init nor Active db_airbases.lua");

                foreach (string name in names)
                {
                    string i, a;
                    init.TryGetValue(name, out i);
                    active.TryGetValue(name, out a);

                    string beach = ExtractField(a ?? i, "beach");
                    string side = ExtractField(a ?? i, "side");
                    string control = "?";
                    if (zones != null)
                    {
                        WargameZoneData z = zones.FirstOrDefault(x => string.Equals(x.Id, beach, StringComparison.OrdinalIgnoreCase));
                        control = z == null ? "zone not found" : z.Control;
                    }

                    bool held = string.Equals(control, side, StringComparison.OrdinalIgnoreCase);
                    Note(campaignName, "   " + name + " | Init " + (i == null ? "(absent)" : "inactive=" + ExtractField(i, "inactive"))
                        + " | Active " + (a == null ? "(absent)" : "inactive=" + ExtractField(a, "inactive") + " airdromeId=" + ExtractField(a, "airdromeId"))
                        + " | side " + side + " beach " + beach + " control " + control + " -> should be " + (held ? "ACTIVE" : "inactive"));
                }

                // Triggers qui touchent à des bases (Init et Active) : un trigger peut remettre inactive
                foreach (string file in new[] { "Init\\camp_triggers_init.lua", "Active\\camp_triggers.lua" })
                {
                    string tp = Path.Combine(folder, file);
                    if (!File.Exists(tp)) continue;

                    foreach (string line in File.ReadAllLines(tp))
                        if (Regex.IsMatch(line, @"ActivateBaseAndItsUnits|SideBase|moveToAnotherBaseOrDeactivate|AirUnitBase"))
                            Note(campaignName, "   trigger line in " + file + " : " + line.Trim());
                }
            }
            catch (Exception ex)
            {
                Note(campaignName, "snapshot failed : " + ex.Message);
            }
        }

        // nom de FARP -> texte brut de son bloc (seulement les entrées wargameFarp = true)
        private static Dictionary<string, string> ReadFarps(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;

            string text = File.ReadAllText(path);
            var lua = new WargameBaseCapture.LuaText(text);
            foreach (var b in lua.Blocks.Where(x => x.DepthBefore == 1))
            {
                bool farp;
                if (!lua.BoolField(b, "wargameFarp", out farp) || !farp) continue;

                string key = lua.KeyOf(b);
                if (key == null) continue;
                result[key] = text.Substring(b.Open, b.Close - b.Open + 1);
            }
            return result;
        }

        // Valeur d'un champ dans le texte d'un bloc (niveau direct : les FARP n'ont pas de sous-table utile ici)
        private static string ExtractField(string block, string field)
        {
            if (block == null) return "?";

            string key = field == "beach" ? "wargameBeach" : field;
            Match m = Regex.Match(block, @"(?:\[\s*[""']" + key + @"[""']\s*\]|\b" + key + @")\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s,}]+))");
            if (!m.Success) return "(absent)";
            if (m.Groups[1].Success) return m.Groups[1].Value;
            return m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        }
    }
}
