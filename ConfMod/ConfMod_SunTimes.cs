using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NLua;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Met à jour "dawn" et "dusk" de conf_mod.lua d'après le vrai lever / coucher du soleil :
    //   date  = camp_init.lua (camp.date)
    //   lieu  = base du joueur (squad player = true dans oob_air) -> position dans db_airbases -> latitude / longitude
    //   heure = heure LOCALE du théâtre (DCS compte les missions en heure locale, fuseau fixe par carte)
    // Calcul local (formule NOAA), sans internet : précis à 1-2 minutes.
    // Appelé une fois par session et par campagne (voir EnsureCampaignFilesUpToDate).
    internal static class ConfMod_SunTimes
    {
        // Fuseau horaire utilisé par DCS pour chaque carte (heures par rapport à UTC, pas d'heure d'été).
        // Si une valeur est fausse pour ta carte, c'est ici qu'il faut la corriger.
        private static readonly Dictionary<string, double> TimeZones = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "Caucasus", 4 }, { "Nevada", -8 }, { "PersianGulf", 4 }, { "Syria", 3 }, { "Normandy", 0 },
            { "TheChannel", 0 }, { "MarianaIslands", 10 }, { "SouthAtlantic", -3 }, { "SinaiMap", 2 },
            { "Kola", 3 }, { "Afghanistan", 4.5 }
        };

        // Renvoie une ligne de compte-rendu (aussi écrite dans le journal). Ne lève jamais d'exception.
        public static string UpdateConfMod(string campaign)
        {
            try
            {
                string root = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", campaign);
                string campInit = Path.Combine(root, "Init", "camp_init.lua");
                if (!File.Exists(campInit)) return Log(campaign, "camp_init.lua not found");

                DateTime date;
                if (!ReadDate(campInit, out date)) return Log(campaign, "date not readable in camp_init.lua");

                string theatre;
                Map_Projection proj = Map_Web.KnownProjection(campaign, out theatre);
                if (proj == null) return Log(campaign, "theatre '" + theatre + "' unknown: dawn/dusk unchanged");

                double tz;
                if (!TimeZones.TryGetValue(theatre, out tz)) return Log(campaign, "no time zone known for '" + theatre + "'");

                // Position : base du joueur, sinon centre des bases connues
                string place;
                double x, y;
                if (!PlayerBasePosition(campaign, root, out x, out y, out place)) return Log(campaign, "no airbase position found");

                double lat, lon;
                Map_Web.DcsToLatLon(proj, x, y, out lat, out lon);

                double rise, set;
                if (!SunTimes(lat, lon, date, tz, out rise, out set))
                    return Log(campaign, "no sunrise/sunset at this latitude on this date: dawn/dusk unchanged");

                int dawn = (int)Math.Round(rise), dusk = (int)Math.Round(set);

                int changed = 0;
                foreach (string file in new[] { Path.Combine(root, "Init", "conf_mod.lua"), Path.Combine(root, "Active", "conf_mod.lua") })
                    if (File.Exists(file) && PatchConfMod(file, dawn, dusk)) changed++;

                return Log(campaign, "sun at " + place + " (" + lat.ToString("0.00") + ", " + lon.ToString("0.00") + ") on " + date.ToString("yyyy-MM-dd")
                    + ", " + theatre + " UTC" + (tz >= 0 ? "+" : "") + tz + " : dawn " + Hm(dawn) + " (" + dawn + "), dusk " + Hm(dusk) + " (" + dusk + ")"
                    + (changed > 0 ? " -> conf_mod.lua updated" : " -> conf_mod.lua already correct"));
            }
            catch (Exception ex)
            {
                return Log(campaign, "failed : " + ex.Message);
            }
        }

        private static string Log(string campaign, string text)
        {
            string line = "ConfMod_SunTimes | " + campaign + " | " + text;
            FormUtils.LogRegister(line);
            return line;
        }

        private static string Hm(int seconds)
        {
            return (seconds / 3600).ToString("00") + ":" + ((seconds % 3600) / 60).ToString("00");
        }

        // camp.date = { year, month, day }
        private static bool ReadDate(string campInit, out DateTime date)
        {
            date = DateTime.MinValue;
            using (Lua lua = new Lua())
            {
                lua.DoString("os = nil io = nil debug = nil dofile = nil loadfile = nil require = nil");
                lua.DoFile(campInit);

                LuaTable camp = lua["camp"] as LuaTable;
                LuaTable d = camp != null ? camp["date"] as LuaTable : null;
                if (d == null) return false;

                int year = (int)Num(d["year"], 0), month = (int)Num(d["month"], 0), day = (int)Num(d["day"], 0);
                if (year < 1753 || year > 9998 || month < 1 || month > 12) return false;

                date = new DateTime(year, month, Math.Max(1, Math.Min(DateTime.DaysInMonth(year, month), day)));
                return true;
            }
        }

        private static double Num(object v, double fallback)
        {
            if (v is double) return (double)v;
            if (v is long) return (long)v;
            if (v is int) return (int)v;
            return fallback;
        }

        // Position DCS de la base du joueur ; sinon moyenne des bases placées sur la carte
        private static bool PlayerBasePosition(string campaign, string root, out double x, out double y, out string place)
        {
            x = y = 0;
            place = "";

            Dictionary<string, AirbaseInfo> bases;
            try
            {
                bases = new Dictionary<string, AirbaseInfo>(new Parser_db_airbases().Load_db_airbases(campaign), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }

            string baseName = PlayerBaseName(root);
            AirbaseInfo a;
            if (baseName != "" && bases.TryGetValue(baseName, out a) && (a.X != 0 || a.Y != 0) && Math.Abs(a.X) < 1e7 && Math.Abs(a.Y) < 1e7)
            {
                x = a.X; y = a.Y; place = baseName;
                return true;
            }

            // Base du joueur sur un porte-avions / inconnue : on prend le centre des bases du théâtre
            double sx = 0, sy = 0; int n = 0;
            foreach (AirbaseInfo b in bases.Values)
            {
                if ((b.X == 0 && b.Y == 0) || Math.Abs(b.X) >= 1e7 || Math.Abs(b.Y) >= 1e7) continue;
                sx += b.X; sy += b.Y; n++;
            }
            if (n == 0) return false;

            x = sx / n; y = sy / n;
            place = "map centre (player base '" + baseName + "' has no position)";
            return true;
        }

        // Nom de la base du squadron joueur : Active\oob_air.lua s'il existe, sinon Init\oob_air_init.lua
        private static string PlayerBaseName(string root)
        {
            string file = Path.Combine(root, "Active", "oob_air.lua");
            if (!File.Exists(file)) file = Path.Combine(root, "Init", "oob_air_init.lua");
            if (!File.Exists(file)) return "";

            string content = File.ReadAllText(file);
            content = Regex.Replace(content, @"--\[\[.*?\]\]", "", RegexOptions.Singleline);
            content = Regex.Replace(content, @"--.*?$", "", RegexOptions.Multiline);

            Match pm = Regex.Match(content, @"(?:\[\s*[""']player[""']\s*\]|(?<!\w)player)\s*=\s*true", RegexOptions.IgnoreCase);
            if (!pm.Success) return "";

            // Remonte au { du bloc du squadron, puis redescend à son }
            int level = 0, start = -1;
            for (int i = pm.Index; i >= 0; i--)
            {
                if (content[i] == '}') level++;
                else if (content[i] == '{')
                {
                    if (level == 0) { start = i; break; }
                    level--;
                }
            }
            if (start < 0) return "";

            level = 1;
            int end = -1;
            for (int i = start + 1; i < content.Length; i++)
            {
                if (content[i] == '{') level++;
                else if (content[i] == '}') level--;
                if (level == 0) { end = i; break; }
            }
            if (end <= start) return "";

            string block = content.Substring(start, end - start + 1);
            Match bm = Regex.Match(block, @"(?:\[\s*[""']base[""']\s*\]|(?<!\w)base)\s*=\s*([""'])(.*?)\1", RegexOptions.IgnoreCase);
            return bm.Success ? bm.Groups[2].Value : "";
        }

        // Lever / coucher du soleil en secondes depuis minuit, heure locale (formule NOAA, soleil au ras de l'horizon).
        // false = pas de lever ou de coucher ce jour-là (jour ou nuit polaire).
        public static bool SunTimes(double lat, double lon, DateTime date, double tzHours, out double riseSec, out double setSec)
        {
            riseSec = setSec = 0;

            double gamma = 2.0 * Math.PI / (DateTime.IsLeapYear(date.Year) ? 366.0 : 365.0) * (date.DayOfYear - 1);

            double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                      - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
            double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                        - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                        - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

            double latR = lat * Math.PI / 180.0;
            double cosHa = Math.Cos(90.833 * Math.PI / 180.0) / (Math.Cos(latR) * Math.Cos(decl)) - Math.Tan(latR) * Math.Tan(decl);
            if (cosHa < -1.0 || cosHa > 1.0) return false;

            double ha = Math.Acos(cosHa) * 180.0 / Math.PI;

            double riseUtcMin = 720.0 - 4.0 * (lon + ha) - eqTime;
            double setUtcMin = 720.0 - 4.0 * (lon - ha) - eqTime;

            riseSec = Wrap((riseUtcMin + tzHours * 60.0) * 60.0);
            setSec = Wrap((setUtcMin + tzHours * 60.0) * 60.0);
            return true;
        }

        private static double Wrap(double seconds)
        {
            while (seconds < 0) seconds += 86400;
            while (seconds >= 86400) seconds -= 86400;
            return seconds;
        }

        // Remplace les valeurs de dawn et dusk en gardant tout le reste de la ligne (commentaires @ui compris).
        // true = le fichier a été modifié.
        private static bool PatchConfMod(string file, int dawn, int dusk)
        {
            byte[] raw = File.ReadAllBytes(file);
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));

            string patched = Regex.Replace(text, @"(?m)^(\s*dawn\s*=\s*)-?\d+(?:\.\d+)?", m => m.Groups[1].Value + dawn, RegexOptions.None);
            patched = Regex.Replace(patched, @"(?m)^(\s*dusk\s*=\s*)-?\d+(?:\.\d+)?", m => m.Groups[1].Value + dusk, RegexOptions.None);

            if (patched == text) return false;

            File.WriteAllText(file, patched, new UTF8Encoding(bom));
            return true;
        }
    }
}
