using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Données de référence modifiables sans toucher au programme (cartes DCS pour l'instant, d'autres sections plus tard).
    // Trois couches, la dernière gagne (par section et par nom) :
    //   1) BuiltIn          : copie de secours dans le code, si le fichier livré manque
    //   2) Data\dce_data.ini        : à côté du .exe, livré et remplacé à chaque mise à jour
    //   3) %AppData%\DCE_Manager\dce_data_user.ini : à toi, créé au premier lancement, jamais écrasé
    // Format : [section] puis  nom = valeur  ; les lignes qui commencent par # sont des commentaires.
    internal static class Map_Data
    {
        private const string BuiltIn = @"
[theatres]
# nom = méridien central ; décalage est ; décalage nord ; fuseau horaire (heures par rapport à UTC, sans heure d'été)
Caucasus       = 33   ; -99516.9999999732   ; -4998114.999999984  ; 4
Nevada         = -117 ; -193996.80999964548 ; -4410028.063999966  ; -8
PersianGulf    = 57   ; 75755.99999999645   ; -2894933.0000000377 ; 4
Syria          = 39   ; 282801.00000000003  ; -3879865.9999999935 ; 3
Normandy       = -3   ; -195526.00000000303 ; -5484812.99999999   ; 0
TheChannel     = 3    ; 99376.00000003197   ; -5636889.00000003   ; 0
MarianaIslands = 147  ; 238417.99999999846  ; -1491840.9999999985 ; 10
SouthAtlantic  = -57  ; 147639.99999999158  ; 5815417.000000032   ; -3
Falklands      = -57  ; 147639.99999999158  ; 5815417.000000032   ; -3
SinaiMap       = 33   ; 169221.0000000007   ; -3325312.0000000224 ; 2
Kola           = 21   ; -62702.00000000019  ; -7543624.999999983  ; 3
Afghanistan    = 63   ; -300149.99999864185 ; -3759657.000000045  ; 4.5

[runway_anchor]
# Où se trouve le point (x, y) des pistes de db_airbases sur la piste, pour toute une carte :
#   middle = milieu de la piste ; start = DEBUT de la piste (seuil : la piste part de ce point dans le sens de son cap) ; east / west = bout est / ouest
# Sans ligne pour une carte : règle mesurée pendant le calage de la carte web, sinon middle.
# Constaté sur Persian Gulf (Abu Dhabi, Bandar Abbas, Lar) : chaque piste est dessinée une demi-longueur trop en arrière, donc le point est le début de la piste.
PersianGulf = start
";

        private const string UserTemplate = @"# DCE_Manager - tes données personnelles (ce fichier n'est jamais écrasé par une mise à jour).
# Même format que Data\dce_data.ini : une ligne ici remplace la ligne du même nom du fichier livré.
# Exemple : une nouvelle carte DCS. Le nom est celui du fichier 'theatre' du .miz (voir le message d'erreur du programme).
#
# [theatres]
# MaCarte = 33 ; -99517.0 ; -4998115.0 ; 4
";

        private static Dictionary<string, Dictionary<string, string>> _data;
        private static readonly object _lock = new object();

        public static string DefaultFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "dce_data.ini"); }
        }

        public static string UserFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DCE_Manager", "dce_data_user.ini"); }
        }

        // Relit les fichiers à la prochaine demande (après une modification à la main)
        public static void Reload()
        {
            lock (_lock) { _data = null; }
        }

        // Écrit (ou remplace) une ligne dans le fichier utilisateur ; value = null : supprime la ligne. Les autres lignes et les commentaires sont gardés.
        public static void SetUser(string section, string key, string value)
        {
            EnsureUserFile();
            var lines = new List<string>(File.ReadAllText(UserFile).Replace("\r\n", "\n").Split('\n'));

            int secStart = -1, secEnd = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.Length < 2 || t[0] != '[' || t[t.Length - 1] != ']') continue;
                if (secStart >= 0) { secEnd = i; break; }
                if (string.Equals(t.Substring(1, t.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase)) secStart = i;
            }

            int keyLine = -1;
            if (secStart >= 0)
            {
                for (int i = secStart + 1; i < secEnd; i++)
                {
                    string t = lines[i].Trim();
                    if (t == "" || t[0] == '#') continue;
                    int eq = t.IndexOf('=');
                    if (eq > 0 && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) { keyLine = i; break; }
                }
            }

            string newLine = key + " = " + value;
            if (value == null)
            {
                if (keyLine >= 0) lines.RemoveAt(keyLine);
            }
            else if (keyLine >= 0)
            {
                lines[keyLine] = newLine;
            }
            else if (secStart >= 0)
            {
                int ins = secEnd;
                while (ins - 1 > secStart && lines[ins - 1].Trim() == "") ins--;       // juste après la dernière ligne de la section
                lines.Insert(ins, newLine);
            }
            else
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim() != "") lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(newLine);
            }

            File.WriteAllText(UserFile, string.Join("\r\n", lines.ToArray()), new System.Text.UTF8Encoding(true));
            Reload();
        }

        // Toutes les lignes d'une section : nom -> texte de la valeur
        public static Dictionary<string, string> Section(string name)
        {
            Load();
            Dictionary<string, string> s;
            return _data.TryGetValue(name, out s) ? s : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // [theatres] : projection de la carte (méridien central, décalages est / nord)
        public static bool TheatreProjection(string theatre, out double cm, out double fe, out double fn)
        {
            double[] v = TheatreValues(theatre);
            cm = fe = fn = 0;
            if (v == null || v.Length < 3) return false;
            cm = v[0]; fe = v[1]; fn = v[2];
            return true;
        }

        // [theatres] : fuseau horaire de la carte (4e valeur, facultatif)
        public static bool TheatreTimeZone(string theatre, out double tz)
        {
            double[] v = TheatreValues(theatre);
            tz = 0;
            if (v == null || v.Length < 4) return false;
            tz = v[3];
            return true;
        }

        private static double[] TheatreValues(string theatre)
        {
            if (string.IsNullOrEmpty(theatre)) return null;

            string text;
            if (!Section("theatres").TryGetValue(theatre, out text)) return null;

            string[] parts = text.Split(';');
            var list = new List<double>();
            foreach (string p in parts)
            {
                double d;
                if (!double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) break;
                list.Add(d);
            }
            return list.ToArray();
        }

        private static void Load()
        {
            lock (_lock)
            {
                if (_data != null) return;

                _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                Parse(BuiltIn, "built-in");
                ParseFile(DefaultFile);
                EnsureUserFile();
                ParseFile(UserFile);
            }
        }

        private static void EnsureUserFile()
        {
            try
            {
                if (File.Exists(UserFile)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(UserFile));
                File.WriteAllText(UserFile, UserTemplate.Replace("\r\n", "\n").Replace("\n", "\r\n"), new System.Text.UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Data | fichier utilisateur non créé : " + ex.Message);
            }
        }

        private static void ParseFile(string file)
        {
            try
            {
                if (File.Exists(file)) Parse(File.ReadAllText(file), file);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("Map_Data | " + file + " illisible : " + ex.Message);
            }
        }

        private static void Parse(string text, string source)
        {
            string section = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line == "" || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0 || section == "") continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1);
                int hash = value.IndexOf('#');                   // commentaire en fin de ligne
                if (hash >= 0) value = value.Substring(0, hash);
                value = value.Trim();
                if (key == "" || value == "") continue;

                Dictionary<string, string> s;
                if (!_data.TryGetValue(section, out s))
                {
                    s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _data[section] = s;
                }
                s[key] = value;                                  // la couche suivante remplace la précédente
            }
        }
    }

    // Unités d'affichage de TOUTES les fenêtres carte : métrique "russe" (m, km, km/h, m/s, hPa) ou anglo-saxon (ft, Nm, kt, inHg).
    // Les valeurs du jeu restent en unités DCS (mètres, m/s, mmHg) : seul l'affichage change. Le choix est gardé d'une session à l'autre.
    // Changed est lancé à chaque bascule : les fenêtres se redessinent tout de suite.
    internal static class Map_Units
    {
        public const double FtPerM = 3.28084, MPerNm = 1852.0, KtPerMs = 1.943844, InHgPerMmHg = 0.0393701, HPaPerMmHg = 1.33322;
        // Pression en mode métrique : false = hectopascal (ICAO), true = mm de mercure (comme les avions russes, et comme le fichier mission)
        public const bool MmHgInMetric = false;

        private static bool _metric = true;
        public static event EventHandler Changed;

        static Map_Units()
        {
            try
            {
                string f = FilePath;
                if (File.Exists(f)) _metric = File.ReadAllText(f).Trim().ToLowerInvariant() != "imperial";
            }
            catch { }
        }

        private static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DCE_Manager", "map_units.txt"); }
        }

        public static bool Metric { get { return _metric; } }

        public static void Set(bool metric)
        {
            if (metric == _metric) return;
            _metric = metric;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, metric ? "metric" : "imperial");
            }
            catch { }
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        private static CultureInfo Inv { get { return CultureInfo.InvariantCulture; } }

        // ---- Altitude et longueurs courtes (mètres <-> pieds) ----
        public static string AltUnit { get { return _metric ? "m" : "ft"; } }
        public static double AltVal(double m) { return _metric ? m : m * FtPerM; }
        public static double AltToM(double v) { return _metric ? v : v / FtPerM; }
        public static string Alt(double m) { return AltVal(m).ToString("0", Inv) + " " + AltUnit; }
        public static string AltRange(double a, double b) { return AltVal(a).ToString("0", Inv) + " - " + AltVal(b).ToString("0", Inv) + " " + AltUnit; }

        // Petite étiquette de circuit (tanker) : "A24" (angels 24 = 24 000 ft) ou "7.3 km"
        public static string AltTag(double m)
        {
            return _metric ? (m / 1000.0).ToString("0.0", Inv) + " km" : "A" + Math.Round(m * FtPerM / 1000).ToString("0", Inv);
        }

        // Niveau de vol : "FL240" ou, en métrique, l'altitude
        public static string FlightLevel(double m)
        {
            return _metric ? Alt(m) : "FL" + (m * FtPerM / 100).ToString("000", Inv);
        }

        // ---- Distances (mètres <-> km / Nm) ----
        public static string DistUnit { get { return _metric ? "km" : "Nm"; } }
        public static double DistVal(double m) { return _metric ? m / 1000.0 : m / MPerNm; }
        public static string Dist(double m, string fmt = "0.0") { return DistVal(m).ToString(fmt, Inv) + " " + DistUnit; }

        // ---- Vitesse des avions (m/s <-> km/h / kt) ----
        public static string SpeedUnit { get { return _metric ? "km/h" : "kt"; } }
        public static double SpeedVal(double ms) { return _metric ? ms * 3.6 : ms * KtPerMs; }
        public static double SpeedToMs(double v) { return _metric ? v / 3.6 : v / KtPerMs; }
        public static string Speed(double ms) { return SpeedVal(ms).ToString("0", Inv) + " " + SpeedUnit; }

        // ---- Vent (m/s <-> m/s / kt) : les météorologues russes comptent le vent en m/s ----
        public static string WindUnit { get { return _metric ? "m/s" : "kt"; } }
        public static double WindVal(double ms) { return _metric ? ms : ms * KtPerMs; }
        public static string Wind(double ms) { return WindVal(ms).ToString(_metric ? "0.0" : "0", Inv) + " " + WindUnit; }

        // ---- Pression (le fichier mission donne le QNH en mmHg) ----
        public static string Pressure(double mmHg)
        {
            if (!_metric) return (mmHg * InHgPerMmHg).ToString("0.00", Inv) + " inHg";
            return MmHgInMetric ? mmHg.ToString("0", Inv) + " mmHg" : (mmHg * HPaPerMmHg).ToString("0", Inv) + " hPa";
        }

        // ---- Texte pour les info-bulles ----
        public static string AltName { get { return _metric ? "metres" : "feet"; } }
        public static string SpeedName { get { return _metric ? "km/h" : "knots"; } }
    }
}
