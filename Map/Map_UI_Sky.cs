using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Une couche de nuages (altitudes en mètres au-dessus du niveau de la mer)
    internal class Map_CloudLayer
    {
        public double Min, Max;
        public double Density;      // couverture 0 à 1 (nom gardé : utilisé par le calcul JVN)
        public double Thick = 0.7;  // "density" DCS = épaisseur optique, ~0.4 à 1.2
    }

    // Tout ce que le panneau "ciel" affiche : heure, soleil, lune, météo
    internal class Map_SkyData
    {
        public string Error = "";
        public string Place = "", Theatre = "";
        public double Lat, Lon, Tz;
        public DateTime Date;                 // jour de la mission (heure locale)
        public double StartSec, DurationSec;  // départ de la mission (secondes depuis minuit local) et durée
        public double DawnSec = -1, DuskSec = -1;
        public string TimeSource = "mission file";

        public double[] WindSpeed = new double[3];   // m/s : sol, 2000 m, 8000 m
        public double[] WindDir = new double[3];     // degrés, tel que dans le fichier mission
        public double Temperature = 15, QnhMmHg = 760, VisibilityM = 80000, Turbulence;
        public bool Fog, Dust;
        public double FogVisM, DustDensity;
        public string WeatherName = "", CloudPreset = "";
        public int Precipitation;                    // 0 aucune, 1 pluie, 2 neige / orage...
        public List<Map_CloudLayer> Clouds = new List<Map_CloudLayer>();
        public string CloudNote = "";
        public double PresetAltMin, PresetAltMax, PrecipPower;
        public bool BaseClamped;                     // la base de la mission sort de la plage du preset
        public double BaseAsked;
    }

    // Lecture de la mission en cours + calculs du soleil et de la lune (formules simples, précision ~1 degré)
    internal static class Map_Sky
    {
        // Dans le fichier mission de DCS, "dir" du vent = direction VERS laquelle il souffle (comme dans MOOSE).
        // Mettre false si ton briefing DCS montre l'inverse.
        public const bool WindDirIsTo = true;

        public static Map_SkyData Load(string campaign, bool newest = false, string sourceFile = null)
        {
            var d = new Map_SkyData();
            try
            {
                string file = sourceFile ?? Map_Drawings.FindMissionSource(campaign, newest);
                if (file == null) { d.Error = "No mission found yet."; return d; }

                Dictionary<string, object> root = Map_Drawings.ReadMissionRoot(file);
                if (root == null) { d.Error = "Mission file not readable."; return d; }

                // Date et heure (heure LOCALE de la carte)
                Dictionary<string, object> date = Sub(root, "date");
                int y = 2006, m = 7, day = 1;
                if (date != null)
                {
                    y = (int)Num(Get(date, "Year"), y);
                    m = (int)Num(Get(date, "Month"), m);
                    day = (int)Num(Get(date, "Day"), day);
                }
                if (y < 1753 || y > 9998 || m < 1 || m > 12) { y = 2006; m = 7; }
                d.Date = new DateTime(y, m, Math.Max(1, Math.Min(DateTime.DaysInMonth(y, m), day)));
                d.StartSec = Num(Get(root, "start_time"), 28800);

                // En campagne, l'heure et la date courantes sont dans Active\camp_status.lua (camp.time / camp.date) : elles priment
                ReadCampStatusTime(campaign, d);

                d.Theatre = Get(root, "theatre") as string ?? "";

                // Lieu et fuseau : base du joueur (même méthode que dawn / dusk)
                double lat, lon, tz;
                string place, theatre;
                if (ConfMod_SunTimes.Locate(campaign, out lat, out lon, out tz, out place, out theatre))
                {
                    d.Lat = lat; d.Lon = lon; d.Tz = tz; d.Place = place;
                    if (d.Theatre == "") d.Theatre = theatre;

                    double rise, set;
                    int polar;
                    ConfMod_SunTimes.SunTimesPolar(lat, lon, d.Date, tz, out rise, out set, out polar);
                    d.DawnSec = rise; d.DuskSec = set;
                }
                else
                {
                    d.Error = "Position of the player base not found: no sun / moon.";
                }

                d.DurationSec = ReadMissionDuration(campaign);
                ReadWeather(d, Sub(root, "weather"));
            }
            catch (Exception ex)
            {
                d.Error = ex.Message;
                FormUtils.LogRegister("Map_Sky | " + ex);
            }
            return d;
        }

        private static void ReadCampStatusTime(string campaign, Map_SkyData d)
        {
            try
            {
                string f = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", campaign, "Active", "camp_status.lua");
                if (!File.Exists(f)) return;

                using (Lua lua = new Lua())
                {
                    lua.DoString("os = nil io = nil debug = nil dofile = nil loadfile = nil require = nil");
                    lua.DoFile(f);

                    LuaTable camp = lua["camp"] as LuaTable;
                    if (camp == null) return;

                    double t = Num(camp["time"], -1);
                    if (t < 0 || t > 86400) return;

                    LuaTable date = camp["date"] as LuaTable;
                    if (date != null)
                    {
                        int y = (int)Num(date["year"], 0), m = (int)Num(date["month"], 0), day = (int)Num(date["day"], 0);
                        if (y >= 1753 && y <= 9998 && m >= 1 && m <= 12)
                            d.Date = new DateTime(y, m, Math.Max(1, Math.Min(DateTime.DaysInMonth(y, m), day)));
                    }

                    d.StartSec = t;
                    d.TimeSource = "camp_status.lua";
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Sky | camp_status : " + ex.Message);
            }
        }

        private static double ReadMissionDuration(string campaign)
        {
            try
            {
                string f = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", campaign, "Init", "conf_mod.lua");
                if (!File.Exists(f)) return 0;
                Match mt = Regex.Match(File.ReadAllText(f), @"(?m)^\s*mission_duration\s*=\s*(\d+)");
                return mt.Success ? double.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            }
            catch { return 0; }
        }

        private static void ReadWeather(Map_SkyData d, Dictionary<string, object> w)
        {
            if (w == null) { d.CloudNote = "no weather in the mission file"; return; }

            d.WeatherName = Get(w, "name") as string ?? "";

            Dictionary<string, object> wind = Sub(w, "wind");
            string[] keys = { "atGround", "at2000", "at8000" };
            for (int i = 0; i < 3; i++)
            {
                Dictionary<string, object> k = wind != null ? Sub(wind, keys[i]) : null;
                if (k == null) continue;
                d.WindSpeed[i] = Num(Get(k, "speed"), 0);
                d.WindDir[i] = Num(Get(k, "dir"), 0);
            }

            Dictionary<string, object> season = Sub(w, "season");
            if (season != null) d.Temperature = Num(Get(season, "temperature"), 15);
            d.QnhMmHg = Num(Get(w, "qnh"), 760);
            Dictionary<string, object> vis = Sub(w, "visibility");
            if (vis != null) d.VisibilityM = Num(Get(vis, "distance"), 80000);
            d.Turbulence = Num(Get(w, "groundTurbulence"), 0);

            d.Fog = Get(w, "enable_fog") is bool && (bool)Get(w, "enable_fog");
            Dictionary<string, object> fog = Sub(w, "fog");
            if (fog != null) d.FogVisM = Num(Get(fog, "visibility"), 0) * 1.0;
            d.Dust = Get(w, "enable_dust") is bool && (bool)Get(w, "enable_dust");
            d.DustDensity = Num(Get(w, "dust_density"), 0);

            // Nuages : ancien format (base / épaisseur / densité 0-10) ou préréglage ("Preset7")
            Dictionary<string, object> c = Sub(w, "clouds");
            if (c == null) { d.CloudNote = "no clouds table"; return; }

            d.Precipitation = (int)Num(Get(c, "iprecptns"), 0);
            double baseAlt = Num(Get(c, "base"), 0);
            string preset = Get(c, "preset") as string ?? "";
            d.CloudPreset = preset;

            if (preset != "")
            {
                if (!ReadPresetLayers(d, preset, baseAlt))
                {
                    // Fichier des préréglages illisible : une couche moyenne à l'altitude "base"
                    d.Clouds.Add(new Map_CloudLayer { Min = baseAlt, Max = baseAlt + 800, Density = 0.6 });
                    d.CloudNote = (d.CloudNote != "" ? d.CloudNote + " - " : "") + "rough picture";
                }
                return;
            }

            double dens = Num(Get(c, "density"), 0);          // 0 à 10
            double thick = Num(Get(c, "thickness"), 0);
            if (dens > 0)
                d.Clouds.Add(new Map_CloudLayer { Min = baseAlt, Max = baseAlt + Math.Max(150, thick), Density = Math.Min(1.0, dens / 10.0) });
        }

        // Couches d'un préréglage : Config\Effects\clouds.lua de l'installation DCS (clouds.presets.PresetN.layers)
        // En cas d'échec, la raison est écrite dans d.CloudNote (affichée sous le dessin des nuages)
        private static bool ReadPresetLayers(Map_SkyData d, string preset, double baseAlt)
        {
            try
            {
                if (string.IsNullOrEmpty(ParamConf.PATH_DCS_Root)) { d.CloudNote = "DCS folder is empty in the configuration"; return false; }

                string f = Path.Combine(ParamConf.PATH_DCS_Root, @"Config\Effects\clouds.lua");
                if (!File.Exists(f)) { d.CloudNote = "file not found: " + f; return false; }

                using (Lua lua = new Lua())
                {
                    lua.DoString("os = nil io = nil debug = nil dofile = nil loadfile = nil require = nil");
                    // clouds.lua écrit ses noms avec _('texte') : la fonction de traduction de DCS. Ici on garde le texte tel quel.
                    lua.DoString("function _(s) return s end");
                    lua.DoFile(f);

                    LuaTable clouds = lua["clouds"] as LuaTable;
                    LuaTable presets = clouds != null ? clouds["presets"] as LuaTable : null;
                    if (presets == null) { d.CloudNote = "no clouds.presets table in " + f; return false; }

                    LuaTable p = presets[preset] as LuaTable;
                    if (p == null) { d.CloudNote = "preset '" + preset + "' not found in clouds.lua"; return false; }

                    string readable = p["readableName"] as string;
                    if (!string.IsNullOrEmpty(readable)) d.CloudPreset = preset + " - " + Regex.Replace(readable, @"^\*+|\s+", " ").Trim();

                    d.PresetAltMin = Num(p["presetAltMin"], 0);
                    d.PresetAltMax = Num(p["presetAltMax"], 0);
                    d.PrecipPower = Num(p["precipitationPower"], 0);

                    LuaTable layers = p["layers"] as LuaTable;
                    if (layers == null) { d.CloudNote = "preset '" + preset + "' has no layers"; return false; }

                    double first = double.MaxValue;
                    var tmp = new List<Map_CloudLayer>();
                    foreach (object key in layers.Keys)
                    {
                        LuaTable l = layers[key] as LuaTable;
                        if (l == null) continue;

                        double a = Num(l["altitudeMin"], double.NaN), b = Num(l["altitudeMax"], double.NaN);
                        if (double.IsNaN(a) || double.IsNaN(b)) continue;
                        first = Math.Min(first, a);

                        double cov = Num(l["coverage"], 0);
                        if (cov < 0.02) continue;                       // couche "vide" du preset : on ne la dessine pas

                        tmp.Add(new Map_CloudLayer
                        {
                            Min = a, Max = b,
                            Density = Math.Min(1.0, cov),
                            Thick = Num(l["density"], 0.7)
                        });
                    }
                    if (first == double.MaxValue) { d.CloudNote = "layers of '" + preset + "' not readable (altitudes missing)"; return false; }
                    tmp.Sort((x, y) => x.Min.CompareTo(y.Min));

                    // Le "base" de la mission décale tout le préréglage (la plus basse couche démarre à base),
                    // mais il doit rester dans la plage du preset (presetAltMin / presetAltMax)
                    double used = baseAlt;
                    if (baseAlt > 0 && d.PresetAltMax > d.PresetAltMin)
                    {
                        used = Math.Max(d.PresetAltMin, Math.Min(d.PresetAltMax, baseAlt));
                        if (Math.Abs(used - baseAlt) > 1) { d.BaseClamped = true; d.BaseAsked = baseAlt; }      // le texte est fait au dessin (unités)
                    }
                    double shift = baseAlt > 0 ? used - first : 0;
                    foreach (Map_CloudLayer l in tmp) { l.Min += shift; l.Max += shift; d.Clouds.Add(l); }
                    return true;
                }
            }
            catch (Exception ex)
            {
                d.CloudNote = "clouds.lua error: " + ex.Message;
                FormUtils.LogRegister("Map_Sky | nuages : " + ex);
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  Astronomie
        // ------------------------------------------------------------------

        private const double Rad = Math.PI / 180.0;

        // Soleil : altitude et azimut (degrés, azimut depuis le nord vers l'est) à l'heure locale donnée
        public static void SunPosition(double lat, double lon, double tz, DateTime date, double localSec, out double alt, out double az)
        {
            double hour = localSec / 3600.0;
            double gamma = 2.0 * Math.PI / 365.0 * (date.DayOfYear - 1 + (hour - 12.0) / 24.0);

            double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
                                      - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
            double decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma)
                        - 0.006758 * Math.Cos(2 * gamma) + 0.000907 * Math.Sin(2 * gamma)
                        - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);

            double tst = localSec / 60.0 + eqTime + 4.0 * lon - 60.0 * tz;      // heure solaire vraie, minutes
            double ha = (tst / 4.0 - 180.0) * Rad;                              // angle horaire
            double la = lat * Rad;

            double sinAlt = Math.Sin(la) * Math.Sin(decl) + Math.Cos(la) * Math.Cos(decl) * Math.Cos(ha);
            alt = Math.Asin(Math.Max(-1, Math.Min(1, sinAlt))) / Rad;

            double azR = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(la) - Math.Tan(decl) * Math.Cos(la)) + Math.PI;
            az = Norm360(azR / Rad);
        }

        // Lune : altitude, azimut, et élongation (0 = nouvelle lune, 180 = pleine lune, degrés)
        public static void MoonPosition(double lat, double lon, double tz, DateTime date, double localSec,
                                        out double alt, out double az, out double elongation)
        {
            // Jours depuis le 31/12/1999 0h UT
            DateTime utc = date.Date.AddSeconds(localSec - tz * 3600.0);
            double d = (utc - new DateTime(1999, 12, 31, 0, 0, 0)).TotalDays;

            // Soleil (éléments moyens)
            double ws = 282.9404 + 4.70935e-5 * d;
            double Ms = Norm360(356.0470 + 0.9856002585 * d);
            double Ls = Norm360(ws + Ms);

            // Lune
            double N = Norm360(125.1228 - 0.0529538083 * d);
            double inc = 5.1454;
            double w = Norm360(318.0634 + 0.1643573223 * d);
            double a = 60.2666, e = 0.0549;
            double Mm = Norm360(115.3654 + 13.0649929509 * d);

            double E = Mm + e / Rad * Math.Sin(Mm * Rad) * (1.0 + e * Math.Cos(Mm * Rad));
            for (int i = 0; i < 5; i++)
                E = E - (E - e / Rad * Math.Sin(E * Rad) - Mm) / (1.0 - e * Math.Cos(E * Rad));

            double xv = a * (Math.Cos(E * Rad) - e);
            double yv = a * Math.Sqrt(1.0 - e * e) * Math.Sin(E * Rad);
            double v = Math.Atan2(yv, xv) / Rad;
            double r = Math.Sqrt(xv * xv + yv * yv);

            double vw = (v + w) * Rad, Nr = N * Rad, ir = inc * Rad;
            double xh = r * (Math.Cos(Nr) * Math.Cos(vw) - Math.Sin(Nr) * Math.Sin(vw) * Math.Cos(ir));
            double yh = r * (Math.Sin(Nr) * Math.Cos(vw) + Math.Cos(Nr) * Math.Sin(vw) * Math.Cos(ir));
            double zh = r * Math.Sin(vw) * Math.Sin(ir);

            double lonMoon = Math.Atan2(yh, xh) / Rad;
            double latMoon = Math.Atan2(zh, Math.Sqrt(xh * xh + yh * yh)) / Rad;

            // Principales perturbations
            double Lm = Norm360(N + w + Mm);
            double D = Lm - Ls;
            double F = Lm - N;
            lonMoon += -1.274 * Math.Sin((Mm - 2 * D) * Rad) + 0.658 * Math.Sin(2 * D * Rad) - 0.186 * Math.Sin(Ms * Rad)
                     - 0.059 * Math.Sin((2 * Mm - 2 * D) * Rad) - 0.057 * Math.Sin((Mm - 2 * D + Ms) * Rad)
                     + 0.053 * Math.Sin((Mm + 2 * D) * Rad) + 0.046 * Math.Sin((2 * D - Ms) * Rad) + 0.041 * Math.Sin((Mm - Ms) * Rad)
                     - 0.035 * Math.Sin(D * Rad) - 0.031 * Math.Sin((Mm + Ms) * Rad);
            latMoon += -0.173 * Math.Sin((F - 2 * D) * Rad) - 0.055 * Math.Sin((Mm - F - 2 * D) * Rad)
                     - 0.046 * Math.Sin((Mm + F - 2 * D) * Rad) + 0.033 * Math.Sin((F + 2 * D) * Rad) + 0.017 * Math.Sin((2 * Mm + F) * Rad);

            elongation = Norm360(lonMoon - Ls);

            // Écliptique -> équatorial
            double ecl = (23.4393 - 3.563e-7 * d) * Rad;
            double lm = lonMoon * Rad, bm = latMoon * Rad;
            double xe = Math.Cos(lm) * Math.Cos(bm);
            double ye = Math.Sin(lm) * Math.Cos(bm) * Math.Cos(ecl) - Math.Sin(bm) * Math.Sin(ecl);
            double ze = Math.Sin(lm) * Math.Cos(bm) * Math.Sin(ecl) + Math.Sin(bm) * Math.Cos(ecl);
            double ra = Math.Atan2(ye, xe) / Rad;
            double dec = Math.Atan2(ze, Math.Sqrt(xe * xe + ye * ye));

            // Temps sidéral local
            double ut = (utc - utc.Date).TotalHours;
            double lst = Norm360(Ls + 180.0 + 15.0 * ut + lon);
            double ha = (lst - ra) * Rad;
            double la = lat * Rad;

            double sinAlt = Math.Sin(la) * Math.Sin(dec) + Math.Cos(la) * Math.Cos(dec) * Math.Cos(ha);
            double altR = Math.Asin(Math.Max(-1, Math.Min(1, sinAlt)));
            double azR = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(la) - Math.Tan(dec) * Math.Cos(la)) + Math.PI;

            // Parallaxe (la Lune est proche : environ 1 degré)
            altR -= Math.Asin(1.0 / Math.Max(30.0, r)) * Math.Cos(altR);

            alt = altR / Rad;
            az = Norm360(azR / Rad);
        }

        // ------------------------------------------------------------------
        //  Lumière de nuit (pour les JVN / NVG)
        //  Éclairement du sol en lux = crépuscule + lune + étoiles, le tout réduit par les nuages.
        //  Repères : pleine lune haute = 0,2 lux ; quartier = 0,02 ; nuit sans lune = 0,001 (étoiles) ;
        //  fin du crépuscule civil (soleil -6°) = 3 lux.
        // ------------------------------------------------------------------

        public const double StarLux = 0.001;

        // Éclairement dû au crépuscule (lux), interpolé en log entre quelques points connus
        public static double TwilightLux(double sunAlt)
        {
            if (sunAlt >= 0) return 400;
            if (sunAlt <= -18) return 0;

            double[] alt = { 0, -6, -12, -18 };
            double[] lux = { 400, 3.4, 0.008, 0.0001 };
            for (int i = 0; i < 3; i++)
            {
                if (sunAlt <= alt[i] && sunAlt >= alt[i + 1])
                {
                    double t = (alt[i] - sunAlt) / (alt[i] - alt[i + 1]);
                    return Math.Pow(10, Math.Log10(lux[i]) + t * (Math.Log10(lux[i + 1]) - Math.Log10(lux[i])));
                }
            }
            return 0;
        }

        // Éclairement de la lune sur un sol horizontal (lux) : phase (Krisciunas & Schaefer), extinction de l'atmosphère, hauteur
        public static double MoonLux(double moonAlt, double elongation)
        {
            if (moonAlt <= 0) return 0;

            double phase = Math.Abs(180.0 - elongation);                        // 0 = pleine lune
            double mag = -12.73 + 0.026 * phase + 4e-9 * Math.Pow(phase, 4);
            double e = Math.Pow(10, -0.4 * (mag + 14.18));                      // lux hors atmosphère, plein face

            double sinH = Math.Sin(moonAlt * Math.PI / 180.0);
            double airmass = 1.0 / (sinH + 0.025 * Math.Exp(-11.0 * sinH));
            double ext = Math.Pow(10, -0.4 * 0.2 * airmass);                    // extinction ~0,2 mag par masse d'air

            return e * ext * sinH;
        }

        // Part de la lumière céleste qui traverse les nuages (1 = ciel clair). Une couche épaisse et couvrante laisse passer ~5 %.
        public static double CloudTransmission(Map_SkyData d)
        {
            double t = 1.0;
            foreach (Map_CloudLayer l in d.Clouds)
            {
                double thick = Math.Max(0, l.Max - l.Min);
                double overcast = 0.05 + 0.30 * Math.Exp(-thick / 700.0);
                t *= 1.0 - Math.Max(0, Math.Min(1, l.Density)) * (1.0 - overcast);
            }
            if (d.Precipitation > 0) t *= 0.8;
            return t;
        }

        // Éclairement total du sol à l'heure locale "sec" (lux) ; moonAlt / moonLux pour le détail
        public static double GroundLux(Map_SkyData d, double sec, out double moonAlt, out double moonOnly, out double sunAlt)
        {
            double az, el;
            SunPosition(d.Lat, d.Lon, d.Tz, d.Date, sec, out sunAlt, out az);
            MoonPosition(d.Lat, d.Lon, d.Tz, d.Date, sec, out moonAlt, out az, out el);

            double cloud = CloudTransmission(d);
            moonOnly = MoonLux(moonAlt, el) * cloud;

            if (sunAlt >= -0.8) return TwilightLux(0) * cloud;                  // jour (on ne cherche pas plus loin)
            return (TwilightLux(sunAlt) + MoonLux(moonAlt, el) + StarLux) * cloud;
        }

        // Classe d'éclairement pour les JVN (seuils simples, en milli-lux)
        public static string NvgClass(double lux, out Color color)
        {
            double mlux = lux * 1000.0;
            if (lux >= 1) { color = Color.FromArgb(235, 200, 90); return "Daylight / twilight"; }
            if (mlux >= 100) { color = Color.FromArgb(90, 215, 120); return "Excellent"; }
            if (mlux >= 30) { color = Color.FromArgb(150, 220, 90); return "Good"; }
            if (mlux >= 5) { color = Color.FromArgb(230, 200, 60); return "Fair"; }
            if (mlux >= 1) { color = Color.FromArgb(240, 140, 50); return "Poor"; }
            color = Color.FromArgb(235, 70, 60);
            return "Very dark";
        }

        public static string PhaseName(double elongation)
        {
            if (elongation < 12 || elongation >= 348) return "New moon";
            if (elongation < 78) return "Waxing crescent";
            if (elongation < 102) return "First quarter";
            if (elongation < 168) return "Waxing gibbous";
            if (elongation < 192) return "Full moon";
            if (elongation < 258) return "Waning gibbous";
            if (elongation < 282) return "Last quarter";
            return "Waning crescent";
        }

        public static double Norm360(double x)
        {
            x %= 360.0;
            return x < 0 ? x + 360.0 : x;
        }

        public static string Compass(double deg)
        {
            string[] n = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
            return n[(int)Math.Round(Norm360(deg) / 22.5) % 16];
        }

        // ------------------------------------------------------------------
        //  Petits utilitaires de lecture
        // ------------------------------------------------------------------

        private static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static double Num(object v, double fallback)
        {
            if (v is double) return (double)v;
            if (v is long) return (long)v;
            if (v is int) return (int)v;
            return fallback;
        }
    }

    // Panneau "ciel" de la carte de mission : heure, soleil / lune (au-dessus de l'horizon seulement), période de jour,
    // lumière pour les JVN, nuages, vent. Le graphique est ZOOMÉ autour du vol (4 à 12 h), la barre "jour" montre la journée entière.
    // La lune n'est affichée que si le vol se passe (au moins en partie) de nuit.
    internal class Map_UI_Sky : System.Windows.Forms.Control
    {
        private Map_SkyData _d = new Map_SkyData { Error = "Loading..." };
        private double _sel, _selRaw;        // heure de départ choisie (secondes depuis minuit) : celle de la mission, ou celle qu'on glisse
        private double _dur, _t0, _win;      // durée du vol, début et largeur (secondes) de la fenêtre du graphique
        private bool _night, _allDay;        // une partie du vol est de nuit / tout le vol est de jour
        private string _nvgText = "";
        private Color _nvgColor = Color.White;

        private Rectangle _plot, _dayBar, _resetRect;
        private enum DragKind { None, Plot, Day }
        private DragKind _drag = DragKind.None;
        private int _lastX;

        private static readonly Color Back = Color.FromArgb(24, 30, 44);
        private static readonly Color PanelCol = Color.FromArgb(33, 41, 60);
        private static readonly Color PanelBorder = Color.FromArgb(62, 78, 112);
        private static readonly Color Txt = Color.FromArgb(225, 230, 240);
        private static readonly Color Dim = Color.FromArgb(140, 150, 170);
        private static readonly Color Green = Color.FromArgb(120, 225, 150);
        private static readonly Color Sun = Color.FromArgb(255, 205, 60);

        private readonly Font _fBold = new Font("Segoe UI", 8f, FontStyle.Bold);
        private readonly Font _fMid = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font _fBig = new Font("Segoe UI", 20f, FontStyle.Bold);

        private static CultureInfo Inv { get { return CultureInfo.InvariantCulture; } }

        public Map_UI_Sky()
        {
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer
                     | System.Windows.Forms.ControlStyles.UserPaint | System.Windows.Forms.ControlStyles.ResizeRedraw, true);
            Font = new Font("Segoe UI", 8f);
            BackColor = Back;
            MouseEnter += (s2, e2) => Focus();       // pour que la molette marche sans clic
            Map_Units.Changed += UnitsChanged;
        }

        private void UnitsChanged(object sender, EventArgs e)
        {
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Map_Units.Changed -= UnitsChanged; _fBold.Dispose(); _fMid.Dispose(); _fBig.Dispose(); }
            base.Dispose(disposing);
        }

        public void Reload(string campaign, bool newest = false, string sourceFile = null)
        {
            _d = Map_Sky.Load(campaign, newest, sourceFile);
            _sel = _selRaw = _d.StartSec;
            Recompute();
            ApplyHeight();
            Invalidate();
        }

        private bool HasData { get { return _d.Error == "" || _d.Place != ""; } }

        // ------------------------------------------------------------------
        //  Calculs : fenêtre du graphique, jour / nuit sur la durée du vol, résumé JVN
        // ------------------------------------------------------------------

        private static double SecOf(double t)
        {
            return Math.Min(86399.0, ((t % 86400.0) + 86400.0) % 86400.0);
        }

        private void Recompute()
        {
            Map_SkyData d = _d;
            _night = false; _allDay = true; _nvgText = ""; _nvgColor = Txt;
            if (!HasData) return;

            _dur = d.DurationSec > 0 ? d.DurationSec : 1800;
            _win = Math.Min(12 * 3600.0, Math.Max(4 * 3600.0, _dur * 2 + 2 * 3600.0));      // 4 h pour un vol d'1 h
            _t0 = _sel + _dur / 2 - _win / 2;

            double sum = 0, min = double.MaxValue, max = 0;
            int n = 0, moonUp = 0, nightCount = 0;
            double mo, mn, sa;
            for (double t = 0; t <= _dur + 0.1; t += 600)
            {
                double lux = Map_Sky.GroundLux(d, SecOf(_sel + t), out mo, out mn, out sa);
                sum += lux; min = Math.Min(min, lux); max = Math.Max(max, lux);
                if (mo > 0) moonUp++;
                if (sa < 0) nightCount++;
                n++;
            }
            _night = nightCount > 0;
            _allDay = !_night;

            if (_allDay)
            {
                _nvgText = "Daylight during the whole mission";
                _nvgColor = Sun;
                return;
            }

            Color c;
            double mean = sum / Math.Max(1, n);
            string cls = Map_Sky.NvgClass(mean, out c);
            double cloud = Map_Sky.CloudTransmission(d);
            _nvgColor = c;
            _nvgText = (nightCount == n ? "Night  -  " : "Day / night  -  ") + "NVG " + cls + "  " + MLux(mean) + " mLux  (" + MLux(min) + " - " + MLux(max) + ")"
                + "   moon up " + (100 * moonUp / Math.Max(1, n)).ToString("0", Inv) + " %   clouds " + (cloud * 100).ToString("0", Inv) + " %";
        }

        // Hauteurs des blocs (voir DrawAll)
        private const int M = 6, Gap = 6, RowA = 108, RowB = 50, RowC = 118, RowW = 88, RowE = 80, RowD = 46, Foot = 30;

        private int DesiredHeight()
        {
            if (!HasData) return 40;
            return RowA + Gap + RowB + Gap + RowC + Gap + RowW + Gap + RowE + Gap + (_night ? RowD + Gap : 0) + Foot;
        }

        private void ApplyHeight()
        {
            int h = DesiredHeight();
            if (Height != h) Height = h;
        }

        // ------------------------------------------------------------------
        //  Souris : glisser sur le graphique = faire défiler l'heure ; clic / glisser sur la barre du jour = choisir l'heure ;
        //  molette = +/- 15 min ; "reset" = heure de la mission
        // ------------------------------------------------------------------

        private void SetSel(double sec)
        {
            sec = Math.Round(Math.Max(0, Math.Min(86399, sec)) / 300.0) * 300.0;      // pas de 5 minutes
            if (Math.Abs(sec - _sel) < 0.5) return;
            _sel = sec;
            Recompute();
            ApplyHeight();
            Invalidate();
        }

        private void SetFromDayBar(int mouseX)
        {
            if (_dayBar.Width <= 0) return;
            double sec = (mouseX - _dayBar.Left) * 86400.0 / _dayBar.Width;
            _selRaw = sec;
            SetSel(sec);
        }

        protected override void OnMouseDown(System.Windows.Forms.MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != System.Windows.Forms.MouseButtons.Left || !HasData) return;

            if (_resetRect.Contains(e.Location)) { _selRaw = _d.StartSec; SetSel(_d.StartSec); return; }

            if (_plot.Contains(e.Location)) { _drag = DragKind.Plot; _lastX = e.X; _selRaw = _sel; }
            else if (_dayBar.Contains(e.Location)) { _drag = DragKind.Day; SetFromDayBar(e.X); }
        }

        protected override void OnMouseMove(System.Windows.Forms.MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_drag == DragKind.Plot && _plot.Width > 0)
            {
                _selRaw -= (e.X - _lastX) * _win / _plot.Width;       // le graphique suit la souris
                _lastX = e.X;
                SetSel(_selRaw);
            }
            else if (_drag == DragKind.Day)
            {
                SetFromDayBar(e.X);
            }

            Cursor = _resetRect.Contains(e.Location) ? System.Windows.Forms.Cursors.Hand
                   : (_plot.Contains(e.Location) || _dayBar.Contains(e.Location)) ? System.Windows.Forms.Cursors.SizeWE : System.Windows.Forms.Cursors.Default;
        }

        protected override void OnMouseUp(System.Windows.Forms.MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = DragKind.None;
        }

        protected override void OnMouseWheel(System.Windows.Forms.MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _selRaw = _sel + (e.Delta > 0 ? 900 : -900);
            SetSel(_selRaw);
        }

        // ------------------------------------------------------------------
        //  Dessin
        // ------------------------------------------------------------------

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            try
            {
                if (!HasData)
                {
                    DrawText(g, _d.Error, 6, 6, Dim, Font);
                    return;
                }
                DrawAll(g);
            }
            catch (Exception ex)
            {
                DrawText(g, "Sky panel: " + ex.Message, 6, 6, Color.Salmon, Font);
            }
        }

        private void DrawAll(Graphics g)
        {
            Map_SkyData d = _d;
            int W = Width;
            int y = 0;

            // Ligne A : bloc "Mission" (heure) + graphique soleil / lune zoomé sur le vol
            var pMission = new Rectangle(M, y, 124, RowA);
            var pGraph = new Rectangle(pMission.Right + Gap, y, W - M - (pMission.Right + Gap), RowA);
            DrawMissionBlock(g, pMission);
            DrawSunGraph(g, pGraph);
            y += RowA + Gap;

            // Ligne B : la journée entière (nuit / jour) avec le vol repéré, et le résumé jour / nuit + JVN
            DrawDayBar(g, new Rectangle(M, y, W - 2 * M, RowB));
            y += RowB + Gap;

            // Ligne C : nuages (pleine largeur) puis vents
            DrawClouds(g, new Rectangle(M, y, W - 2 * M, RowC));
            y += RowC + Gap;
            DrawWindBlock(g, new Rectangle(M, y, W - 2 * M, RowW));
            y += RowW + Gap;

            // Ligne E : manche à air (vent au sol)
            DrawWindsock(g, new Rectangle(M, y, W - 2 * M, RowE));
            y += RowE + Gap;

            // Ligne D : la lune, seulement si le vol se passe de nuit
            if (_night)
            {
                DrawMoonBlock(g, new Rectangle(M, y, W - 2 * M, RowD));
                y += RowD + Gap;
            }

            string foot = "Temp " + d.Temperature.ToString("0", Inv) + " C"
                + "   QNH " + Map_Units.Pressure(d.QnhMmHg)
                + "   Vis " + Map_Units.Dist(d.VisibilityM, "0.#")
                + (d.Fog ? "   FOG" : "") + (d.Dust ? "   DUST" : "")
                + (d.Turbulence > 0 ? "   Turb " + d.Turbulence.ToString("0", Inv) : "");
            DrawText(g, "time from " + d.TimeSource + "  -  drag the graph or the day bar, or use the wheel", 8, y, Color.FromArgb(100, 110, 130), Font);
            DrawText(g, foot, 8, y + 14, Dim, Font);
        }

        private static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var gp = new GraphicsPath();
            int d = rad * 2;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        private static void Panel(Graphics g, Rectangle r)
        {
            using (GraphicsPath gp = Rounded(r, 6))
            using (var fill = new SolidBrush(PanelCol))
            using (var pen = new Pen(PanelBorder, 1f))
            {
                g.FillPath(fill, gp);
                g.DrawPath(pen, gp);
            }
        }

        // Bloc "Mission" : heure de départ en grand, date, fuseau, heure de fin
        private void DrawMissionBlock(Graphics g, Rectangle r)
        {
            Map_SkyData d = _d;
            Panel(g, r);

            DrawText(g, "Mission", r.Left + 10, r.Top + 6, Dim, _fBold);

            _resetRect = Rectangle.Empty;
            if (Math.Abs(_sel - d.StartSec) > 0.5)
            {
                _resetRect = new Rectangle(r.Right - 46, r.Top + 5, 40, 15);
                using (var br = new SolidBrush(Color.FromArgb(60, 90, 140))) g.FillRectangle(br, _resetRect);
                DrawText(g, "reset", _resetRect.Left + 5, _resetRect.Top + 1, Txt, Font);
            }

            DrawText(g, Hms(_sel), r.Left + 8, r.Top + 20, Txt, _fBig);
            DrawText(g, d.Date.ToString("dd MMM yyyy", Inv), r.Left + 10, r.Top + 54, Txt, Font);
            DrawTextIn(g, "UTC" + (d.Tz >= 0 ? "+" : "") + d.Tz.ToString("0.#", Inv) + "  " + d.Theatre,
                new Rectangle(r.Left + 10, r.Top + 67, r.Width - 16, 14), Dim);

            using (var pen = new Pen(PanelBorder)) g.DrawLine(pen, r.Left + 10, r.Top + 84, r.Right - 10, r.Top + 84);
            DrawText(g, Hms(_sel + _dur) + (d.DurationSec > 0 ? "  (" + Duration(d.DurationSec) + ")" : ""), r.Left + 10, r.Top + 88, Green, _fMid);
        }

        private float XT(double t)
        {
            return _plot.Left + (float)(_plot.Width * (t - _t0) / _win);
        }

        // Soleil (jaune) et lune (grise, de nuit seulement) AU-DESSUS de l'horizon, sur une fenêtre autour du vol.
        // Le vol est en vert ; en dessous : les heures, puis la bande de lumière pour les JVN (même échelle).
        private void DrawSunGraph(Graphics g, Rectangle R)
        {
            Map_SkyData d = _d;
            Panel(g, R);

            _plot = new Rectangle(R.Left + 10, R.Top + 26, R.Width - 20, R.Height - 26 - 32);
            Rectangle P = _plot;

            // Altitudes échantillonnées toutes les ~10 minutes
            int N = Math.Max(6, (int)Math.Ceiling(_win / 600.0));
            var sunA = new double[N + 1];
            var moonA = new double[N + 1];
            double top = 20, a, z, el;
            for (int i = 0; i <= N; i++)
            {
                double sec = SecOf(_t0 + _win * i / N);
                Map_Sky.SunPosition(d.Lat, d.Lon, d.Tz, d.Date, sec, out a, out z);
                sunA[i] = a;
                if (a > top) top = a;
                if (_night)
                {
                    Map_Sky.MoonPosition(d.Lat, d.Lon, d.Tz, d.Date, sec, out a, out z, out el);
                    moonA[i] = a;
                    if (a > top) top = a;
                }
            }
            double maxAlt = Math.Min(90, Math.Ceiling(top * 1.1 / 10.0) * 10.0);

            using (var br = new SolidBrush(Color.FromArgb(46, 66, 108))) g.FillRectangle(br, P);

            // Nuit : le soleil est sous l'horizon
            using (var br = new SolidBrush(Color.FromArgb(150, 4, 6, 18)))
                for (int i = 0; i < N; i++)
                    if ((sunA[i] + sunA[i + 1]) / 2 < 0)
                    {
                        float x1 = P.Left + (float)P.Width * i / N, x2 = P.Left + (float)P.Width * (i + 1) / N;
                        g.FillRectangle(br, x1, P.Top, x2 - x1 + 0.6f, P.Height);
                    }

            // Heures
            double step = _win <= 6 * 3600 ? 3600 : 7200;
            for (double t = Math.Ceiling(_t0 / step) * step; t < _t0 + _win; t += step)
            {
                float x = XT(t);
                using (var pen = new Pen(Color.FromArgb(50, 255, 255, 255))) g.DrawLine(pen, x, P.Top, x, P.Bottom);
                DrawText(g, ((int)(SecOf(t) / 3600)).ToString("00"), (int)x - 6, P.Bottom + 1, Dim, Font);
            }

            // Le vol
            float fx1 = XT(_sel), fx2 = d.DurationSec > 0 ? XT(_sel + _dur) : fx1 + 3;
            using (var br = new SolidBrush(Color.FromArgb(75, 80, 220, 130)))
                g.FillRectangle(br, fx1, P.Top, Math.Max(3, fx2 - fx1), P.Height);
            using (var pen = new Pen(Color.FromArgb(230, 120, 235, 160), 1.5f))
            {
                g.DrawLine(pen, fx1, P.Top, fx1, P.Bottom);
                g.DrawLine(pen, fx2, P.Top, fx2, P.Bottom);
            }

            // Courbes (au-dessus de l'horizon seulement)
            if (_night) DrawAltCurve(g, moonA, maxAlt, Color.FromArgb(215, 190, 196, 210), Color.Empty, 1.8f);
            DrawAltCurve(g, sunA, maxAlt, Color.FromArgb(255, 255, 205, 60), Color.FromArgb(55, 255, 190, 40), 2.2f);

            using (var pen = new Pen(Color.FromArgb(150, 170, 190, 220))) g.DrawLine(pen, P.Left, P.Bottom, P.Right, P.Bottom);   // horizon

            // Astres à l'heure de départ du vol
            double sa, sz, ma = -90, mz = 0, me = 0;
            Map_Sky.SunPosition(d.Lat, d.Lon, d.Tz, d.Date, SecOf(_sel), out sa, out sz);
            if (_night) Map_Sky.MoonPosition(d.Lat, d.Lon, d.Tz, d.Date, SecOf(_sel), out ma, out mz, out me);
            if (sa > 0)
                using (var br = new SolidBrush(Sun)) g.FillEllipse(br, fx1 - 5, AltY(sa, maxAlt) - 5, 10, 10);
            if (_night && ma > 0)
                using (var br = new SolidBrush(Color.FromArgb(225, 228, 238))) g.FillEllipse(br, fx1 - 4, AltY(ma, maxAlt) - 4, 8, 8);

            // Heures de début et de fin du vol
            string t1 = Hms(_sel), t2 = Hms(_sel + _dur);
            float w1 = g.MeasureString(t1, _fBold).Width, w2 = g.MeasureString(t2, _fBold).Width;
            if (fx2 - fx1 > w1 + w2 + 10)
            {
                DrawText(g, t1, (int)fx1 + 3, P.Top + 1, Green, _fBold);
                DrawText(g, t2, (int)(fx2 - w2 - 3), P.Top + 1, Green, _fBold);
            }
            else
            {
                DrawText(g, t1, (int)Math.Max(P.Left + 2, fx1 - w1 - 3), P.Top + 1, Green, _fBold);
                DrawText(g, t2, (int)Math.Min(P.Right - w2 - 2, fx2 + 3), P.Top + 1, Green, _fBold);
            }

            using (var pen = new Pen(Color.FromArgb(110, 130, 170))) g.DrawRectangle(pen, P);

            // Bande JVN (même échelle de temps que le graphique)
            int by = P.Bottom + 15;
            double mo, mn, sun;
            Color c;
            for (int i = 0; i < N; i++)
            {
                double tm = _t0 + _win * (i + 0.5) / N;
                double lux = Map_Sky.GroundLux(d, SecOf(tm), out mo, out mn, out sun);
                Map_Sky.NvgClass(lux, out c);
                float x1 = P.Left + (float)P.Width * i / N, x2 = P.Left + (float)P.Width * (i + 1) / N;
                using (var br = new SolidBrush(c)) g.FillRectangle(br, x1, by, x2 - x1 + 0.6f, 7);
            }

            // Titre : position du soleil au départ, aube / crépuscule
            DrawText(g, "Sun  " + sa.ToString("0", Inv) + "°  " + Map_Sky.Compass(sz) + (sa < 0 ? "  (below horizon)" : ""), R.Left + 10, R.Top + 5, Sun, _fBold);
            if (d.DawnSec >= 0)
            {
                string dd = "dawn " + Hms(d.DawnSec) + "   dusk " + Hms(d.DuskSec);
                float dw = g.MeasureString(dd, Font).Width;
                DrawText(g, dd, (int)(R.Right - dw - 8), R.Top + 6, Dim, Font);
            }
        }

        private float AltY(double alt, double maxAlt)
        {
            double k = Math.Max(0, Math.Min(1, alt / maxAlt));
            return _plot.Bottom - (float)(k * _plot.Height);
        }

        // Courbe d'altitude : seulement les parties au-dessus de l'horizon (avec remplissage léger sous la courbe)
        private void DrawAltCurve(Graphics g, double[] alt, double maxAlt, Color line, Color fill, float width)
        {
            int N = alt.Length - 1;
            float baseY = _plot.Bottom;
            var run = new List<PointF>();

            for (int i = 0; i <= N; i++)
            {
                float x = _plot.Left + (float)_plot.Width * i / N;
                float xPrev = _plot.Left + (float)_plot.Width * (i - 1) / N;

                if (alt[i] > 0)
                {
                    if (run.Count == 0 && i > 0)
                    {
                        double f = -alt[i - 1] / (alt[i] - alt[i - 1]);
                        run.Add(new PointF(xPrev + (x - xPrev) * (float)f, baseY));
                    }
                    run.Add(new PointF(x, AltY(alt[i], maxAlt)));
                }
                else if (run.Count > 0)
                {
                    double f = alt[i - 1] / (alt[i - 1] - alt[i]);
                    run.Add(new PointF(xPrev + (x - xPrev) * (float)f, baseY));
                    FlushCurve(g, run, line, fill, width);
                }
            }
            FlushCurve(g, run, line, fill, width);
        }

        private void FlushCurve(Graphics g, List<PointF> run, Color line, Color fill, float width)
        {
            if (run.Count >= 2)
            {
                if (fill != Color.Empty)
                {
                    var poly = new List<PointF>(run);
                    poly.Insert(0, new PointF(run[0].X, _plot.Bottom));
                    poly.Add(new PointF(run[run.Count - 1].X, _plot.Bottom));
                    using (var br = new SolidBrush(fill)) g.FillPolygon(br, poly.ToArray());
                }
                using (var pen = new Pen(line, width)) g.DrawLines(pen, run.ToArray());
            }
            run.Clear();
        }

        // La journée entière : nuit sombre, jour jaune (aube -> crépuscule), le vol en vert, puis le résumé jour / nuit + JVN
        private void DrawDayBar(Graphics g, Rectangle P)
        {
            Map_SkyData d = _d;
            Panel(g, P);

            int x0 = P.Left + 16, bw = P.Width - 32, barY = P.Top + 22, bh = 8;
            _dayBar = new Rectangle(x0, barY - 8, bw, bh + 16);

            using (var br = new SolidBrush(Color.FromArgb(14, 18, 34))) g.FillRectangle(br, x0, barY, bw, bh);

            if (d.DawnSec >= 0 && d.DuskSec >= 0)
            {
                float xd = x0 + (float)(bw * d.DawnSec / 86400.0), xk = x0 + (float)(bw * d.DuskSec / 86400.0);
                using (var br = new SolidBrush(Color.FromArgb(240, 190, 40)))
                    g.FillRectangle(br, xd, barY, Math.Max(1, xk - xd), bh);

                DrawDayMark(g, xd, P, barY, "dawn " + Hms(d.DawnSec));
                DrawDayMark(g, xk, P, barY, "dusk " + Hms(d.DuskSec));
            }

            // Le vol (peut passer minuit : deux morceaux)
            double s1 = SecOf(_sel), s2 = s1 + _dur;
            using (var br = new SolidBrush(Color.FromArgb(235, 90, 225, 140)))
            using (var pen = new Pen(Color.White, 1f))
            {
                double[][] parts = s2 <= 86400 ? new[] { new[] { s1, s2 } } : new[] { new[] { s1, 86400.0 }, new[] { 0.0, s2 - 86400 } };
                foreach (double[] part in parts)
                {
                    float xa = x0 + (float)(bw * part[0] / 86400.0), xb = x0 + (float)(bw * part[1] / 86400.0);
                    var rect = new RectangleF(xa, barY - 3, Math.Max(5, xb - xa), bh + 6);
                    g.FillRectangle(br, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }

            using (var br = new SolidBrush(_nvgColor)) g.FillRectangle(br, P.Left + 10, P.Top + 35, 4, 11);
            DrawTextIn(g, _nvgText, new Rectangle(P.Left + 18, P.Top + 34, P.Width - 24, 14), _nvgColor);
        }

        private void DrawDayMark(Graphics g, float x, Rectangle P, int barY, string text)
        {
            float w = g.MeasureString(text, Font).Width;
            float tx = Math.Max(P.Left + 4, Math.Min(P.Right - w - 4, x - w / 2));
            DrawText(g, text, (int)tx, P.Top + 3, Color.FromArgb(255, 215, 80), Font);

            var tri = new[] { new PointF(x - 3.5f, barY - 6), new PointF(x + 3.5f, barY - 6), new PointF(x, barY - 1) };
            using (var br = new SolidBrush(Color.FromArgb(255, 215, 80))) g.FillPolygon(br, tri);
        }

        private static string MLux(double lux)
        {
            double m = lux * 1000.0;
            return m >= 100 ? m.ToString("0", Inv) : m >= 10 ? m.ToString("0.#", Inv) : m.ToString("0.##", Inv);
        }

        // Nuages : coupe verticale (altitude MSL). Chaque couche = une bande de nuages, plus ou moins remplie selon la
        // couverture, plus ou moins sombre selon l'épaisseur (density DCS). Détail chiffré à droite.
        private void DrawClouds(Graphics g, Rectangle R)
        {
            Map_SkyData d = _d;
            Panel(g, R);
            DrawText(g, "Clouds", R.Left + 10, R.Top + 4, Dim, _fBold);
            if (d.CloudPreset != "")
                DrawTextIn(g, d.CloudPreset, new Rectangle(R.Left + 58, R.Top + 5, R.Width - 66, 14), Dim);

            // Échelle : jusqu'au sommet de la couche la plus haute (4 km mini, 14 km maxi)
            double top = 4000;
            foreach (Map_CloudLayer l in d.Clouds) top = Math.Max(top, l.Max);
            double maxAlt = Math.Min(14000, Math.Ceiling(top * 1.05 / 1000.0) * 1000.0);

            // Graduations : km (1, 2 ou 4) ou milliers de pieds (5 ou 10)
            double maxU = Map_Units.AltVal(maxAlt) / 1000.0;
            double stepU = Map_Units.Metric ? (maxU <= 6 ? 1 : maxU <= 10 ? 2 : 4) : (maxU <= 20 ? 5 : 10);
            double unitM = Map_Units.AltToM(1000.0);                      // un "km" ou un "kft" en mètres

            int cw = Math.Max(110, Math.Min(260, R.Width / 2 - 40));
            var C = new Rectangle(R.Left + 40, R.Top + 22, cw, R.Height - 22 - 8);

            using (var br = new LinearGradientBrush(C, Color.FromArgb(30, 52, 98), Color.FromArgb(78, 120, 170), 90f))
                g.FillRectangle(br, C);

            for (double u = 0; u * unitM <= maxAlt + 1; u += stepU)
            {
                float y = C.Bottom - (float)(C.Height * u * unitM / maxAlt);
                using (var pen = new Pen(Color.FromArgb(55, 255, 255, 255))) g.DrawLine(pen, C.Left, y, C.Right, y);
                if (y > R.Top + 19) DrawText(g, u.ToString("0", Inv) + (Map_Units.Metric ? " km" : " kft"), R.Left + 5, (int)y - 7, Dim, Font);
            }

            GraphicsState st = g.Save();
            g.SetClip(C);
            for (int i = 0; i < d.Clouds.Count; i++)
            {
                Map_CloudLayer l = d.Clouds[i];
                float yTop = C.Bottom - (float)(C.Height * Math.Min(maxAlt, l.Max) / maxAlt);
                float yBot = C.Bottom - (float)(C.Height * Math.Min(maxAlt, l.Min) / maxAlt);
                float bandH = Math.Max(5f, yBot - yTop);
                double cover = Math.Max(0, Math.Min(1, l.Density));

                // voile léger sur toute la hauteur de la couche
                using (var br = new SolidBrush(Color.FromArgb((int)(25 + 70 * cover), 240, 242, 248)))
                    g.FillRectangle(br, C.Left, yTop, C.Width, bandH);

                // petits nuages : nombre = couverture, teinte = épaisseur (graine fixe : le dessin ne bouge pas)
                int grey = (int)(246 - 90 * Math.Max(0, Math.Min(1, (l.Thick - 0.4) / 0.8)));
                var rnd = new Random(11 + i * 7);
                int n = (int)(cover * C.Width / 5.0) + 3;
                using (var br = new SolidBrush(Color.FromArgb(160, grey, grey, grey)))
                    for (int k = 0; k < n; k++)
                    {
                        float bw = 10 + rnd.Next(22);
                        float bh = Math.Max(4f, bandH * (0.45f + 0.5f * (float)rnd.NextDouble()));
                        float bx = C.Left + (float)(rnd.NextDouble() * C.Width) - bw / 2;
                        float by = yTop + (bandH - bh) * (float)rnd.NextDouble();
                        g.FillEllipse(br, bx, by, bw, bh);
                    }
            }
            g.Restore(st);
            using (var pen = new Pen(Color.FromArgb(110, 130, 170))) g.DrawRectangle(pen, C);

            // Détail chiffré
            int tx = C.Right + 14, ty = R.Top + 24;
            if (d.Clouds.Count == 0)
            {
                DrawText(g, "Clear sky", tx, ty, Txt, _fBold);
            }
            else
            {
                for (int i = 0; i < d.Clouds.Count && ty < R.Bottom - 26; i++)
                {
                    Map_CloudLayer l = d.Clouds[i];
                    DrawText(g, "L" + (i + 1) + "  " + Map_Units.AltRange(l.Min, l.Max), tx, ty, Txt, _fBold);
                    DrawText(g, "cover " + (l.Density * 100).ToString("0", Inv) + " %   thickness " + l.Thick.ToString("0.00", Inv), tx + 14, ty + 12, Dim, Font);
                    ty += 27;
                }
            }
            if ((d.Precipitation > 0 || d.PrecipPower > 0) && ty < R.Bottom - 12)
            {
                DrawText(g, "Rain / snow" + (d.PrecipPower > 0 ? "  (power " + d.PrecipPower.ToString("0.##", Inv) + ")" : ""), tx, ty, Color.FromArgb(120, 190, 240), Font);
                ty += 13;
            }
            string note = d.CloudNote;
            if (d.BaseClamped)
                note = (note != "" ? note + " - " : "") + "mission base " + Map_Units.Alt(d.BaseAsked) + " is outside the preset range ("
                    + Map_Units.AltRange(d.PresetAltMin, d.PresetAltMax) + "): clamped";
            if (note != "" && ty < R.Bottom - 12)
                using (var br = new SolidBrush(Color.FromArgb(240, 180, 90)))
                    g.DrawString(note, Font, br, new RectangleF(tx, ty, R.Right - tx - 6, R.Bottom - ty - 4));
        }

        // Vent : trois petits cadrans (sol, 2000 m, 8000 m), flèche = vers où souffle le vent
        private void DrawWindBlock(Graphics g, Rectangle R)
        {
            Map_SkyData d = _d;
            Panel(g, R);
            DrawText(g, "Winds", R.Left + 10, R.Top + 4, Dim, _fBold);

            string[] names = { "Ground", "2000 m", "8000 m" };
            int cell = (R.Width - 8) / 3;

            for (int i = 0; i < 3; i++)
            {
                float cx = R.Left + 8 + i * cell + 17, cy = R.Top + 54, r = 15;
                double kt = d.WindSpeed[i] * 1.94384;
                double toDeg = Map_Sky.WindDirIsTo ? d.WindDir[i] : d.WindDir[i] + 180;
                double fromDeg = Map_Sky.Norm360(toDeg + 180);

                using (var pen = new Pen(Color.FromArgb(120, 200, 205, 220))) g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                DrawText(g, "N", (int)cx - 4, (int)(cy - r) - 12, Dim, Font);

                Color c = kt < 10 ? Color.FromArgb(120, 220, 140) : kt < 20 ? Color.FromArgb(240, 200, 60) : Color.FromArgb(245, 100, 80);

                if (kt >= 0.5)
                {
                    double a = toDeg * Math.PI / 180.0;
                    float len = r - 2;
                    float ex = cx + (float)Math.Sin(a) * len, ey = cy - (float)Math.Cos(a) * len;
                    float bx = cx - (float)Math.Sin(a) * len, by = cy + (float)Math.Cos(a) * len;
                    using (var pen = new Pen(c, 2.5f) { EndCap = LineCap.ArrowAnchor }) g.DrawLine(pen, bx, by, ex, ey);
                }
                else
                {
                    using (var br = new SolidBrush(c)) g.FillEllipse(br, cx - 3, cy - 3, 6, 6);
                }

                int tx = (int)(cx + r + 5);
                DrawText(g, names[i], tx, R.Top + 34, Dim, Font);
                DrawText(g, Map_Units.Wind(d.WindSpeed[i]), tx, R.Top + 47, c, _fBold);
                if (kt >= 0.5) DrawText(g, "from " + fromDeg.ToString("000", Inv) + "°", tx, R.Top + 61, Dim, Font);
            }
        }

        // Manche à air (vent au sol seulement), dessin fixe : le nombre de bandes qui flottent à l'horizontale dépend du vent.
        // Règle de la réalité : manche de 3 m, une bande de plus tous les 3 kt, toute la manche à l'horizontale à 15 kt.
        // Les bandes qui ne sont pas portées par le vent pendent vers le bas. Le vent est en général indiqué par les cadrans ci-dessus.
        private void DrawWindsock(Graphics g, Rectangle R)
        {
            Panel(g, R);
            DrawText(g, "Windsock (ground wind)", R.Left + 10, R.Top + 4, Dim, _fBold);

            double kt = _d.WindSpeed[0] * 1.94384;
            int up = (int)Math.Min(5, Math.Ceiling(kt / 3.0 - 1e-6));       // bandes portées par le vent
            if (kt < 0.5) up = 0;

            // Mât à gauche, manche vers la droite
            float px = R.Left + 24, top = R.Top + 20, bottom = R.Bottom - 6;
            using (var mast = new Pen(Color.FromArgb(170, 175, 190), 3f))
                g.DrawLine(mast, px, top - 4, px, bottom);
            using (var ring = new Pen(Color.FromArgb(200, 205, 215), 2f))
                g.DrawLine(ring, px, top - 2, px + 3, top - 2);

            float segLen = 11f, w0 = 13f, w1 = 6f;          // longueur d'une bande, diamètre à l'entrée / à la pointe
            float x = px + 2, y = top;
            for (int i = 0; i < 5; i++)
            {
                bool carried = i < up;
                // bande portée : horizontale ; bande non portée : tournée vers le bas (de plus en plus si le vent est faible)
                float ang = carried ? 0f : (float)(Math.PI / 2 * (up == 0 ? 1.0 : 0.85));
                float wA = w0 + (w1 - w0) * i / 5f, wB = w0 + (w1 - w0) * (i + 1) / 5f;
                float dx = (float)Math.Cos(ang) * segLen, dy = (float)Math.Sin(ang) * segLen;
                float nx = -(float)Math.Sin(ang), ny = (float)Math.Cos(ang);       // perpendiculaire

                var quad = new PointF[]
                {
                    new PointF(x + nx * wA / 2, y + ny * wA / 2),
                    new PointF(x + dx + nx * wB / 2, y + dy + ny * wB / 2),
                    new PointF(x + dx - nx * wB / 2, y + dy - ny * wB / 2),
                    new PointF(x - nx * wA / 2, y - ny * wA / 2)
                };
                using (var br = new SolidBrush(i % 2 == 0 ? Color.FromArgb(240, 120, 40) : Color.FromArgb(240, 240, 240)))
                    g.FillPolygon(br, quad);
                using (var pen = new Pen(Color.FromArgb(90, 0, 0, 0), 1f))
                    g.DrawPolygon(pen, quad);
                x += dx; y += dy;
            }

            int tx = R.Left + 130;
            DrawText(g, Map_Units.Wind(_d.WindSpeed[0]) + " now: " + up + " of 5 bands up", tx, R.Top + 28, Txt, _fBold);
            DrawText(g, "1 band = " + Map_Units.Wind(3 / Map_Units.KtPerMs) + ", 5 bands = " + Map_Units.Wind(15 / Map_Units.KtPerMs), tx, R.Top + 46, Dim, Font);
        }

        // Lune (vol de nuit seulement) : disque avec sa phase + altitude / azimut au départ du vol
        private void DrawMoonBlock(Graphics g, Rectangle R)
        {
            Map_SkyData d = _d;
            Panel(g, R);

            double alt, az, el;
            Map_Sky.MoonPosition(d.Lat, d.Lon, d.Tz, d.Date, SecOf(_sel), out alt, out az, out el);

            float r = 16f;
            float cx = R.Left + 12 + r, cy = R.Top + R.Height / 2f;
            bool flip = d.Lat < 0;                       // hémisphère sud : la lune se voit "à l'envers"
            bool waxing = el < 180;
            double e = el * Math.PI / 180.0;
            double lit = (1 - Math.Cos(e)) / 2;

            using (var dark = new SolidBrush(Color.FromArgb(50, 55, 70)))
            using (var light = new SolidBrush(Color.FromArgb(235, 232, 215)))
            {
                g.FillEllipse(dark, cx - r, cy - r, 2 * r, 2 * r);

                bool rightLit = waxing != flip;
                float k = (float)Math.Cos(e);
                float sx = rightLit ? 1 : -1;

                GraphicsState st = g.Save();
                g.TranslateTransform(cx, cy);
                g.ScaleTransform(sx, 1);
                g.FillPie(light, -r, -r, 2 * r, 2 * r, -90, 180);
                float w = Math.Abs(k) * r;
                if (w > 0.5f)
                    g.FillEllipse(k > 0 ? dark : light, -w, -r, 2 * w, 2 * r);
                g.Restore(st);
            }
            using (var pen = new Pen(Color.FromArgb(120, 200, 205, 220))) g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);

            int tx = (int)(cx + r + 12);
            DrawText(g, "Moon  -  " + Map_Sky.PhaseName(el) + "   " + (lit * 100).ToString("0", Inv) + " % lit", tx, R.Top + 7, Txt, _fBold);
            DrawText(g, alt >= 0 ? "alt " + alt.ToString("0", Inv) + "°  " + Map_Sky.Compass(az) + "  (at the start of the flight)" : "below the horizon at the start of the flight",
                tx, R.Top + 24, Dim, Font);
        }

        private static void DrawText(Graphics g, string text, int x, int y, Color color, Font font)
        {
            using (var br = new SolidBrush(color)) g.DrawString(text, font, br, x, y);
        }

        // Texte dans un cadre, coupé avec "..." s'il est trop long
        private void DrawTextIn(Graphics g, string text, Rectangle r, Color color)
        {
            using (var br = new SolidBrush(color))
            using (var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, Font, br, r, fmt);
        }

        private static string Hms(double sec)
        {
            sec = ((sec % 86400) + 86400) % 86400;
            return ((int)(sec / 3600)).ToString("00") + ":" + ((int)(sec % 3600) / 60).ToString("00");
        }

        private static string Duration(double sec)
        {
            int m = (int)Math.Round(sec / 60.0);
            return (m / 60) + "h" + (m % 60).ToString("00");
        }
    }
}
