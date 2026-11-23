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
    // Un tracé du fichier mission (frontières, zones...) : coordonnées DCS absolues
    internal class Map_Drawing
    {
        public string Name = "";
        public string Group = "mission";    // mission / zones / spawn : chaque groupe a son bouton
        public string Label = "";           // texte écrit au milieu de la forme (nom de zone)
        public double RangeM;               // portée (m) : si > 0, la distance est ajoutée à l'étiquette dans l'unité choisie (km / Nm)

        public string LabelText
        {
            get { return RangeM > 0 && Label != "" && Label != "onsel" ? Label + " " + Map_Units.Dist(RangeM, "0") : Label; }
        }
        public string Kind = "";            // Line / Polygon / Circle / Text
        public List<double[]> Points = new List<double[]>();   // { x (nord), y (est) }
        public bool Closed;
        public bool Dashed;
        public float Thickness = 2;
        public Color Line = Color.White;
        public Color Fill = Color.Transparent;
        public double Radius;               // cercle, en mètres
        public string Text = "";
        public int Index;                   // marqueur de waypoint : numéro du waypoint dans son vol
        public int Alert;                   // tronçon de plan de vol : 0 normal, 1 vitesse tendue (orange), 2 impossible (rouge clignotant)
    }

    // Un élément au sol de la mission (véhicule, statique, navire) : position, camp, type d'icône ; la table Lua d'origine
    // est gardée (déjà en mémoire) pour afficher tous ses champs au clic, sans rien recopier au chargement.
    internal class Map_GroundUnit
    {
        public double X, Y;             // coordonnées DCS (x = nord, y = est)
        public bool Red;                // false = bleu
        public char Kind;               // icône : a blindé, u véhicule, m défense sol-air, r radar, g artillerie, i infanterie, b statique, p avion/hélico statique, f FARP, n navire, k porte-avions
        public float HalfM;             // demi-taille de l'icône en mètres (taille approximative réelle) : l'icône grossit avec le zoom au-delà de sa taille minimale
        public double Heading = double.NaN;     // cap en radians (0 = nord, sens horaire), NaN si absent
        public string Name = "", Type = "";
        public Dictionary<string, object> Src;
        public Map_GroundGroup Group;
    }

    // Taille approximative réelle des éléments au sol (pour que l'icône grossisse quand on zoome).
    // A MODIFIER ICI : motif cherché dans le type DCS -> longueur en mètres. Sinon valeur par famille d'icône.
    internal static class Map_GroundSize
    {
        private static readonly object[][] ShipLengths =
        {
            new object[] { "CVN|VINSON|STENNIS|NIMITZ|CARL|ROOSEVELT|WASHINGTON|TRUMAN|LINCOLN", 333.0 },
            new object[] { "KUZNECOW|KUZNETSOV|CV_1143|LIAONING", 305.0 },
            new object[] { "FORRESTAL|KITTY|SARATOGA|AMERICA", 325.0 },
            new object[] { "LHA|TARAWA|WASP|LHD", 250.0 },
            new object[] { "INVINCIBLE", 209.0 },
            new object[] { "PIOTR|PYOTR|VELIKIY|KIROV", 252.0 },
            new object[] { "071|LPD", 210.0 },
            new object[] { "MOSCOW|MOSKVA|SLAVA", 186.0 },
            new object[] { "TICONDEROG|CG_|CG-", 173.0 },
            new object[] { "ARLEIGH|BURKE|DDG", 155.0 },
            new object[] { "052B|052C|052D|LUYANG|LUZHOU", 155.0 },
            new object[] { "054A|JIANGKAI", 134.0 },
            new object[] { "PERRY|OLIVER|FFG", 136.0 },
            new object[] { "NEUSTRASH|REZKY|KRIVAK", 125.0 },
            new object[] { "ALBATROS|GRISHA", 72.0 },
            new object[] { "MOLNIYA|TARANTUL", 56.0 },
            new object[] { "BDK|ROPUCHA", 112.0 },
            new object[] { "KILO|SOM|SUB|SS_", 74.0 },
            new object[] { "SEAWISE", 458.0 },
            new object[] { "ELNYA|TANKER|BULKER|HANDY", 180.0 },
            new object[] { "DRY|CARGO|CONTAINER", 140.0 },
            new object[] { "LST|HIGGINS|LANDING", 60.0 },
            new object[] { "SPEEDBOAT|BOAT|ZWEZDNY|RIB", 12.0 },
        };

        // véhicules connus (longueur totale, m)
        private static readonly object[][] VehicleLengths =
        {
            new object[] { "ABRAMS|LEOPARD|T-90|T-80|T-72|CHALLENGER|LECLERC", 10.0 },
            new object[] { "PATRIOT|S-300|S300|5P85|SCUD|ISKANDER|HIMARS|MLRS|SMERCH|GRAD", 11.0 },
            new object[] { "KAMAZ|URAL|ZIL|TRUCK|M978|M939|HEMTT", 8.0 },
            new object[] { "HMMWV|M1043|M1045|UAZ|JEEP|LAND_ROVER|TIGR", 5.0 },
        };

        private static readonly List<KeyValuePair<System.Text.RegularExpressions.Regex, double>> RxShip = Compile(ShipLengths);
        private static readonly List<KeyValuePair<System.Text.RegularExpressions.Regex, double>> RxVehicle = Compile(VehicleLengths);
        private static readonly Dictionary<string, double> Cache = new Dictionary<string, double>();

        private static List<KeyValuePair<System.Text.RegularExpressions.Regex, double>> Compile(object[][] rows)
        {
            var list = new List<KeyValuePair<System.Text.RegularExpressions.Regex, double>>();
            foreach (object[] row in rows)
                list.Add(new KeyValuePair<System.Text.RegularExpressions.Regex, double>(
                    new System.Text.RegularExpressions.Regex((string)row[0], System.Text.RegularExpressions.RegexOptions.IgnoreCase), (double)row[1]));
            return list;
        }

        // Longueur par défaut selon la famille d'icône
        private static double DefaultLength(char kind)
        {
            switch (kind)
            {
                case 'a': return 8;
                case 'm': return 9;
                case 'r': return 8;
                case 'g': return 8;
                case 'i': return 2;
                case 'b': return 12;
                case 'p': return 16;
                case 'f': return 60;
                case 'n': return 120;
                case 'k': return 300;
                default: return 6;
            }
        }

        // Combien d'unités "h" l'icône mesure dans sa plus grande dimension (voir DrawGroundIcon)
        private static double ShapeUnits(char kind)
        {
            switch (kind)
            {
                case 'n': return 3.3;
                case 'k': return 4.8;
                case 'a': return 2.8;
                case 'm': return 2.2;
                case 'g': return 2.2;
                case 'b': return 2.4;
                case 'p': return 2.1;
                default: return 2.0;
            }
        }

        // Longueur approximative (m) d'un élément de cette famille / de ce type (ex. porte-avions 'k')
        public static double LengthMeters(char kind, string type)
        {
            return HalfMeters(kind, type) * ShapeUnits(kind);
        }

        // Demi-taille "h" de l'icône en mètres
        public static float HalfMeters(char kind, string type)
        {
            double len;
            string key = kind + "|" + type;
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out len))
                {
                    len = DefaultLength(kind);
                    if (kind == 'n' || kind == 'k' || kind == 'a' || kind == 'u' || kind == 'm' || kind == 'g' || kind == 'r')
                    {
                        foreach (KeyValuePair<System.Text.RegularExpressions.Regex, double> r in (kind == 'n' || kind == 'k') ? RxShip : RxVehicle)
                        {
                            if (!r.Key.IsMatch(type ?? "")) continue;
                            len = r.Value;
                            break;
                        }
                    }
                    Cache[key] = len;
                }
            }
            return (float)(len / ShapeUnits(kind));
        }
    }

    // Un groupe au sol (pour afficher son nom une seule fois, au centre du groupe)
    internal class Map_GroundGroup
    {
        public string Name = "", Country = "", Category = "";
        public double X, Y;
        public bool Red;
        public Dictionary<string, object> Src;
        public bool Hidden;             // ["hidden"] = true dans le fichier mission (DCE cache ainsi les groupes sur la carte F10)
        public List<Map_GroundUnit> Units = new List<Map_GroundUnit>();
    }

    // Mission en cours : plans de vol des joueurs + menaces sol-air (lus dans le .miz de la mission)
    internal class Map_MissionData
    {
        public string Source = "";      // fichier lu (vide = aucune mission trouvée)
        public string Error = "";
        public int PlayerFlights;
        public int PackageFlights;      // vols des mêmes packages que les joueurs (hors joueurs)
        public int OtherFlights;        // tous les autres vols du camp affiché
        public string BriefingBlue = "", BriefingRed = "", BriefingGeneral = "", BriefingNeutral = "";    // textes de briefing (descriptionBlueTask / RedTask, résolus dans le dictionary)
        public double StartTime;        // heure de début de mission (secondes depuis minuit)
        public bool Fallback;           // vrai = le .miz attendu n'avait aucun vol joueur, un autre a été pris
        public List<Map_Flight> Flights = new List<Map_Flight>();
        public int SamSites;
        public List<Map_Drawing> Plans = new List<Map_Drawing>();
        public List<Map_Drawing> Threats = new List<Map_Drawing>();
        public List<Map_GroundUnit> Ground = new List<Map_GroundUnit>();           // éléments sol non cachés
        public List<Map_GroundGroup> GroundGroups = new List<Map_GroundGroup>();
        public List<Map_GroundUnit> HiddenGround = new List<Map_GroundUnit>();               // éléments sol des groupes "hidden" : montrés seulement sur demande (debug / CampaignMaker)
        public List<Map_GroundGroup> HiddenGroundGroups = new List<Map_GroundGroup>();
        public List<Map_Drawing> Radars = new List<Map_Drawing>();                 // zones radar des EWR au sol (cercles)
        public List<Map_Drawing> ShipRoutes = new List<Map_Drawing>();             // trajets prévus des navires (pointillés) ; Text = camp ("blue" / "red")
    }
}
