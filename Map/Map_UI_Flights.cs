using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    // Un waypoint d'un vol, tel que lu dans le fichier mission (coordonnées DCS : x = nord, y = est).
    // "Source" garde la table Lua d'origine : plus tard, pour déplacer un waypoint à la souris,
    // on modifiera X/Y ici puis on réécrira cette table dans le fichier mission.
    internal class Map_Waypoint
    {
        public int Number;
        public double X, Y;
        public string Name = "";
        public double Alt;          // mètres
        public double Speed;        // m/s
        public double Eta;          // secondes depuis le début de la mission (0 si absent)
        public string Type = "";    // TakeOff / Turning Point / Land ...
        public string Action = "";  // From Parking Area / Fly Over Point ...
        public string Role = "standard";    // standard / airfield / target / station / flow : décide de la forme du marqueur
        public string OrbitPattern = "";    // "" = pas d'orbite ici ; "Circle" ou "Race-Track" (tâche Orbit du waypoint : AWACS, tanker, CAP...)
        public double OrbitSpeed;           // m/s (vitesse demandée dans la tâche Orbit, 0 si absente)
        public double OrbitAlt;             // mètres (altitude demandée dans la tâche Orbit, 0 si absente)
        public double OrbitUntil;           // secondes depuis le début de la mission : fin de l'orbite (script DCE OrbitPosition), 0 si inconnue
        public double OrigX, OrigY;         // position d'origine (gardée au premier déplacement, pour le Reset)
        public bool SpeedLocked, EtaLocked; // "speed_locked" / "ETA_locked" du fichier mission (cases à cocher de DCS)
        public HashSet<string> Dirty = new HashSet<string>();   // champs changés à la main, à écrire dans la mission : speed, ETA, alt, speed_locked, ETA_locked
        public double ForcedTime = double.NaN;                  // heure imposée à la main (secondes depuis le début de la mission), NaN = aucune
        public double OrigSpeed, OrigAlt;
        public bool OrigSpeedLocked, OrigEtaLocked;
        public Dictionary<string, object> Source;
        public bool IsNew;                  // waypoint ajouté à la carte (pas encore dans le fichier mission : Number = -1)
        public string OrigType = "", OrigAction = "", OrigRole = "";    // type de départ d'origine (pour le Reset)
        public bool StartAuto;              // altitude / vitesse mises par la carte au passage en départ en vol (rendues si on change d'avis)

        // Les noms de waypoints DCE contiennent parfois le cap / la distance ("Assemble307  / 11 /  2 mn") : on les enlève à l'affichage
        public string ShortName
        {
            get { return Regex.Replace(Name ?? "", @"\s*\d{1,3}\s*/.*$", "").Trim(); }
        }
    }

    // Un vol (= un groupe avion/hélico du fichier mission)
    internal class Map_Flight
    {
        public string Name = "";
        public string Type = "";            // type d'avion
        public string Side = "";            // blue / red
        public int Units;
        public bool IsPlayer;               // contient un Client / Player
        public bool IsHeli;                 // groupe "helicopter" du fichier mission
        public List<double[]> UnitPos = new List<double[]>();   // position de départ de chaque avion : { x, y, cap en radians }
        public bool StartAir;               // départ en vol choisi à la main : au début de la mission (heure 0), altitude et vitesse non nulles
        public bool GroundStart;            // le 1er waypoint est un départ du sol (TakeOffGround) : les silhouettes des hélicos sont alors dessinées
        public List<double[]> PendingUnitPos;   // positions prévues pour le nombre d'avions demandé (pas encore écrit) : liste vide = places de parking, null = aucun changement demandé
        public bool IsPackage;              // même "Pack N" qu'un vol joueur (hors joueur)
        public int Package = -1;            // numéro du "Pack N", -1 si le nom n'en a pas
        public string Task = "";            // tâche du groupe dans le fichier mission : CAP, AWACS, Refueling...
        public string Callsign = "";        // indicatif radio (tanker...) : "Texaco 11"
        public double Freq;                 // fréquence radio du groupe, MHz (0 si absente)
        public string Modulation = "AM";
        public string Tacan = "";           // "21X" (balise du tanker), "" si aucune
        public Color Color = Color.White;
        public List<Map_Waypoint> Waypoints = new List<Map_Waypoint>();
        public Dictionary<string, object> Source;

        // Nom affiché : type d'avion (alias de Init\various_table.lua s'il existe) + nom du vol
        public string Title
        {
            get { return Type == "" ? Name : Map_Flights.TypeName(Type) + " - " + Name; }
        }

        public double StartDelay;           // "start_time" du groupe : retard au décollage (secondes)

        // Départ en vol : si le vol arrive trop tôt à son heure fixe, le temps en trop est mis dans une orbite d'attente
        // (fonction DCE OrbitPosition) sur un waypoint d'après le 0, au lieu de ralentir tout le vol. Calculé par ApplyPlanTimes.
        public const double MinHoldS = 30;  // en dessous de cette durée : pas d'orbite, on corrige par la vitesse
        public int HoldWp = -1;             // waypoint qui porte l'orbite (jamais le 0), -1 = aucune
        public double HoldS;                // secondes ajoutées à l'orbite
        public bool HoldNew;                // vrai : orbite à créer sur ce waypoint ; faux : on rallonge celle qui est dans le fichier

        // Calculés par Compute()
        public double[] Times = new double[0];      // heure de chaque waypoint (secondes depuis le début de la mission)
        public double[] LegM = new double[0];       // longueur du tronçon qui MÈNE à chaque waypoint (mètres)
        public double DistanceM;
        public double MinAlt, MaxAlt;               // mètres
        public bool EtaFromFile;                    // faux = heures estimées à partir des vitesses

        // Distances (plan horizontal DCS) et heures de passage. Appelé avant d'afficher la fiche du vol.
        public void Compute()
        {
            int n = Waypoints.Count;
            Times = new double[n];
            LegM = new double[n];
            DistanceM = 0;
            EtaFromFile = false;
            HoldWp = -1; HoldS = 0; HoldNew = false;
            MinAlt = double.MaxValue;
            MaxAlt = 0;
            if (n == 0) { MinAlt = 0; return; }

            double lastSpeed = 150;     // m/s, au cas où un waypoint n'aurait aucune vitesse
            Times[0] = Waypoints[0].Eta > 0 ? Waypoints[0].Eta : StartDelay;

            for (int i = 0; i < n; i++)
            {
                Map_Waypoint w = Waypoints[i];
                if (w.Alt > 0)
                {
                    if (w.Alt < MinAlt) MinAlt = w.Alt;
                    if (w.Alt > MaxAlt) MaxAlt = w.Alt;
                }
                if (i == 0) continue;

                Map_Waypoint p = Waypoints[i - 1];
                double dx = w.X - p.X, dy = w.Y - p.Y;
                LegM[i] = Math.Sqrt(dx * dx + dy * dy);
                DistanceM += LegM[i];

                // La vitesse d'un waypoint vaut pour le tronçon QUI SUIT (p -> w) : c'est celle du waypoint précédent
                if (p.Speed > 1) lastSpeed = p.Speed;

                if (w.Eta > Times[i - 1])
                {
                    Times[i] = w.Eta;
                    EtaFromFile = true;
                }
                else
                {
                    Times[i] = Times[i - 1] + LegM[i] / lastSpeed;
                }
            }
            if (MinAlt == double.MaxValue) MinAlt = 0;

            // Après un déplacement de waypoint : heures du plan gardées jusqu'à la cible, décalées ensuite
            if (PlanTimes != null && PlanTimes.Length == n && OrigLegM != null) ApplyPlanTimes();
        }

        // Tous les tronçons gardent la vitesse du plan (leur durée suit leur longueur), sauf ceux dont la vitesse a été changée à la main.
        // Heures FIXES : le cadenas (AnchorIndex : par défaut la cible = TOT) et les ETA imposées à la main (ForcedTime).
        //  - Avant la première heure fixe : c'est l'heure de décollage qui bouge pour la tenir, sans passer sous le plancher
        //    (5 min après le début de la mission pour un départ au sol, 0 pour un départ en l'air).
        //    Si le plancher est atteint, le trajet est comprimé : les vitesses montent (Check() juge ça avec la règle des 25 %).
        //  - Entre deux heures fixes : les durées sont étirées / comprimées pour tomber juste.
        //  - Après la dernière heure fixe : rien n'est tenu, l'heure d'arrivée se décale.
        private void ApplyPlanTimes()
        {
            int n = Waypoints.Count;
            TakeoffLimited = false;

            var dt = new double[n];
            for (int i = 1; i < n; i++)
            {
                double dt0 = PlanTimes[i] - PlanTimes[i - 1];
                dt[i] = OrigLegM[i] > 1 ? dt0 * LegM[i] / OrigLegM[i] : dt0;       // même vitesse que le plan sur ce tronçon

                // Les ETA du fichier ne sont pas toujours cohérentes avec les distances (tronçon très court de quelques secondes, circuit d'attente...) :
                // la vitesse "impliquée" est alors absurde (833 kt, 1735 kt, 47 kt...) et la proportionnalité la garderait à tout jamais.
                // Dans ce cas on garde la durée du plan et on ajoute / retire seulement la distance en plus, à la vitesse prévue du tronçon.
                double implied = dt0 > 1 ? OrigLegM[i] / dt0 : 0;
                bool absurd = dt0 < 10 || (VHi > 0 && (implied > VHi * 1.2 || implied < VLo * 0.8));
                if (absurd)
                {
                    double vLeg = Waypoints[i - 1].Speed >= 10 ? Waypoints[i - 1].Speed : (Waypoints[i].Speed >= 10 ? Waypoints[i].Speed : 200);
                    dt[i] = Math.Max(1, dt0 + (LegM[i] - OrigLegM[i]) / vLeg);
                }
                if (Waypoints[i - 1].Dirty.Contains("speed") && Waypoints[i - 1].Speed > 1 && LegM[i] > 1)
                    dt[i] = LegM[i] / Waypoints[i - 1].Speed;                       // vitesse choisie à la main (valable pour le tronçon qui suit le waypoint)
            }

            // Départ en vol : le premier tronçon se vole à la vitesse du WP 0 (au moins la vitesse de nav), pas à la vitesse du décollage du plan
            if (StartAir && n > 1 && LegM[1] > 1 && Waypoints[0].Speed > 1) dt[1] = LegM[1] / Waypoints[0].Speed;

            // Diagnostic (voir DumpState) : ce que fait ApplyPlanTimes, pas à pas
            var dbg = DebugLog ? new System.Text.StringBuilder() : null;

            var fixedT = new SortedDictionary<int, double>();
            int t = AnchorIndex;
            if (t >= 1 && t < n) fixedT[t] = AnchorTime;
            for (int i = 1; i < n; i++)
                if (!double.IsNaN(Waypoints[i].ForcedTime)) fixedT[i] = Waypoints[i].ForcedTime;
            _fixed = fixedT.Keys.ToList();

            double t0 = PlanTimes[0];
            if (_fixed.Count > 0)
            {
                int first = _fixed[0];
                double sum = 0;
                for (int i = 1; i <= first; i++) sum += dt[i];
                t0 = fixedT[first] - sum;

                double floor = Waypoints[0].Role == "airfield" ? Math.Min(TakeoffFloor, PlanTimes[0]) : 0;
                if (dbg != null)
                    dbg.AppendLine("  bloc 0 > " + first + " : heure fixe " + fixedT[first].ToString("0") + " s, somme des durees au plan " + sum.ToString("0") + " s, decollage voulu " + t0.ToString("0")
                        + " s, plancher " + floor.ToString("0") + " s (PlanTimes[0] = " + PlanTimes[0].ToString("0") + ")");
                if (t0 < floor)
                {
                    t0 = floor;
                    TakeoffLimited = true;
                    double avail = fixedT[first] - t0;
                    if (sum > 1 && avail > 1)
                    {
                        double k = avail / sum;                                     // < 1 : il faut aller plus vite
                        for (int i = 1; i <= first; i++) dt[i] *= k;
                        if (dbg != null) dbg.AppendLine("  decollage au plancher : duree dispo " + avail.ToString("0") + " s pour " + sum.ToString("0") + " s -> facteur k = " + k.ToString("0.000"));
                    }
                }
            }

            // Départ en vol choisi à la main : le vol commence au début de la mission (heure 0), jamais plus tard.
            // Les vitesses du plan sont gardées. Si le vol est trop en avance sur son heure fixe, le temps en trop va dans une orbite
            // (celle du fichier si elle existe, sinon une nouvelle) sur un waypoint d'après le 0. S'il est en retard : les tronçons sont comprimés.
            if (StartAir && t0 > 0.5)
            {
                t0 = 0;
                if (_fixed.Count > 0)
                {
                    int f1 = _fixed[0];
                    double sum1 = 0;
                    for (int i = 1; i <= f1; i++) sum1 += dt[i];
                    double avail1 = fixedT[f1];
                    double surplus = avail1 - sum1;
                    int h = surplus >= MinHoldS ? PickHoldWp(f1) : -1;
                    if (h >= 1)
                    {
                        HoldWp = h;
                        HoldS = surplus;
                        HoldNew = Waypoints[h].OrbitUntil <= 0;
                        dt[h + 1] += surplus;                                       // l'attente est entre le waypoint h et le suivant
                        if (dbg != null) dbg.AppendLine("  depart en vol : attente de " + surplus.ToString("0") + " s sur le WP " + h + (HoldNew ? " (orbite a creer)" : " (orbite du fichier rallongee)"));
                    }
                    else if (sum1 > 1 && avail1 > 1)
                    {
                        double k1 = avail1 / sum1;                                  // pas d'orbite possible : on ajuste les durees
                        for (int i = 1; i <= f1; i++) dt[i] *= k1;
                    }
                }
            }

            for (int j = 1; j < _fixed.Count; j++)
            {
                int a = _fixed[j - 1], b = _fixed[j];
                double sum = 0;
                for (int i = a + 1; i <= b; i++) sum += dt[i];
                double avail = fixedT[b] - fixedT[a];
                if (sum > 1 && avail > 1)
                {
                    double k = avail / sum;
                    for (int i = a + 1; i <= b; i++) dt[i] *= k;
                    if (dbg != null) dbg.AppendLine("  bloc " + a + " > " + b + " : dispo " + avail.ToString("0") + " s pour " + sum.ToString("0") + " s -> k = " + k.ToString("0.000"));
                }
            }

            Times[0] = t0;
            for (int i = 1; i < n; i++) Times[i] = Times[i - 1] + dt[i];
            if (dbg != null) DebugApply = dbg.ToString();
        }

        // Waypoint (entre 1 et f1 - 1, donc avant l'heure fixe) qui portera l'attente d'un départ en vol : d'abord une orbite OrbitPosition
        // déjà dans le fichier, sinon le premier waypoint sans orbite (le 1, en général). Jamais le 0, jamais un waypoint ajouté à la carte.
        private int PickHoldWp(int f1)
        {
            for (int i = 1; i < f1 && i < Waypoints.Count - 1; i++)
                if (!Waypoints[i].IsNew && Waypoints[i].OrbitUntil > 0) return i;
            for (int i = 1; i < f1 && i < Waypoints.Count - 1; i++)
                if (!Waypoints[i].IsNew && Waypoints[i].OrbitPattern == "" && Waypoints[i].Role != "airfield") return i;
            return -1;
        }

        // Heure de fin de l'orbite d'attente (secondes depuis le début de la mission), 0 si pas d'attente.
        // Orbite du fichier : sa durée d'origine (fin - heure du waypoint au plan) est gardée, on ajoute le temps en trop.
        public double HoldUntil()
        {
            if (HoldWp < 1 || HoldWp >= Waypoints.Count || HoldS <= 0 || PlanTimes == null || HoldWp >= PlanTimes.Length) return 0;
            double d = HoldNew ? 0 : Math.Max(0, Waypoints[HoldWp].OrbitUntil - PlanTimes[HoldWp]);
            return Times[HoldWp] + d + HoldS;
        }

        // Vitesse de croisière du vol (m/s) : celle du premier tronçon de transit du plan, sinon une valeur par défaut
        public double NavSpeed()
        {
            double min = IsHeli ? 30 : 100;
            for (int i = 1; i < Waypoints.Count; i++)
                if (Waypoints[i].Speed >= min) return Waypoints[i].Speed;
            return IsHeli ? 60 : 200;
        }

        // ------------------------------------------------------------------------------------------
        //  DIAGNOSTIC : un fichier texte (%TEMP%\DCE_Manager_waypoints.log) où chaque waypoint lâché ou modifié ajoute
        //  l'état complet du vol : valeurs du fichier mission, vitesse que les ETA du fichier impliquent sur chaque tronçon,
        //  ce que calcule la carte. Mettre DebugLog = false pour l'arrêter.
        // ------------------------------------------------------------------------------------------
        public static bool DebugLog = true;
        public string DebugApply = "";

        public static string DebugFile { get { return Path.Combine(Path.GetTempPath(), "DCE_Manager_waypoints.log"); } }

        private static string Kt(double ms) { return (ms * 1.943844).ToString("0"); }

        public string DumpState(double missionStart, string why)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==== " + DateTime.Now.ToString("HH:mm:ss") + "  " + Name + "  -  " + why);
            sb.AppendLine("mission start " + missionStart.ToString("0") + " s | VLo " + Kt(VLo) + " kt, VHi " + Kt(VHi) + " kt | ancre (cadenas) WP " + AnchorIndex + " a " + AnchorTime.ToString("0")
                + " s | heures fixes : " + string.Join(",", _fixed.Select(x => x.ToString()).ToArray()) + " | TakeoffLimited " + TakeoffLimited + " | LandingShift " + LandingShift.ToString("0") + " s");
            sb.AppendLine(DebugApply == "" ? "  (ApplyPlanTimes : rien)" : DebugApply.TrimEnd());
            sb.AppendLine("  i role    spdFile ETAfile  lockS lockE | legNow  legOrig dtPlan implied(kt) | spd[i-1] spd[i] | Times    dt    LegSpeed status");
            for (int i = 0; i < Waypoints.Count; i++)
            {
                Map_Waypoint w = Waypoints[i];
                double legNow = i < LegM.Length ? LegM[i] : 0;
                double legOrig = OrigLegM != null && i < OrigLegM.Length ? OrigLegM[i] : 0;
                double dtPlan = PlanTimes != null && i > 0 && i < PlanTimes.Length ? PlanTimes[i] - PlanTimes[i - 1] : 0;
                double implied = dtPlan > 1 ? legOrig / dtPlan : 0;                    // vitesse que les ETA du fichier donnent sur ce tronçon
                double dt = i > 0 && i < Times.Length ? Times[i] - Times[i - 1] : 0;
                sb.AppendLine("  " + i.ToString().PadLeft(2) + " " + (w.Role ?? "").PadRight(8)
                    + Kt(w.Speed).PadLeft(7) + w.Eta.ToString("0").PadLeft(8) + (w.SpeedLocked ? "    S" : "    -") + (w.EtaLocked ? "    E" : "    -")
                    + " | " + legNow.ToString("0").PadLeft(6) + legOrig.ToString("0").PadLeft(8) + dtPlan.ToString("0").PadLeft(7) + Kt(implied).PadLeft(9)
                    + " | " + (i > 0 ? Kt(Waypoints[i - 1].Speed) : "-").PadLeft(8) + Kt(w.Speed).PadLeft(7)
                    + " | " + (i < Times.Length ? Times[i].ToString("0") : "").PadLeft(6) + dt.ToString("0").PadLeft(6)
                    + (i < LegSpeed.Length ? Kt(LegSpeed[i]) : "").PadLeft(8) + " " + (LegStatus != null && i < LegStatus.Length ? LegStatus[i].ToString() : ""));
            }
            sb.AppendLine("  alertes : " + (Alerts == null || Alerts.Count == 0 ? "aucune" : string.Join(" | ", Alerts.ToArray())));
            return sb.ToString();
        }

        public void WriteDebug(double missionStart, string why)
        {
            if (!DebugLog) return;
            try { File.AppendAllText(DebugFile, DumpState(missionStart, why)); }
            catch { }
        }

        private List<int> _fixed = new List<int>();         // waypoints à heure fixe (dans l'ordre)

        // Vrai si l'heure de ce waypoint ne bouge pas (cadenas ou ETA imposée)
        public bool IsFixed(int i)
        {
            return PlanTimes == null ? i == AnchorIndex : _fixed.Contains(i);
        }

        // ---- Déplacement de waypoints : simulation en mémoire, rien n'est écrit dans la mission ----

        public double[] PlanTimes;                  // heures d'origine de chaque waypoint (null tant qu'on n'a rien déplacé)
        public double VLo, VHi;                     // vitesses mini / maxi du plan (m/s), sans les vitesses presque nulles du sol ; 0 = inconnues
        public int[] LegStatus = new int[0];        // par waypoint : état du tronçon qui MÈNE à ce waypoint (0 normal, 1 tendu, 2 impossible)
        public double[] LegSpeed = new double[0];   // par waypoint : vitesse nécessaire sur ce tronçon (m/s)
        public List<string> Alerts = new List<string>();
        public bool Edited;
        public const double TakeoffFloor = 300;     // décollage au sol : pas avant 5 min après le début de la mission (s)
        public bool TakeoffLimited;                 // le décollage est au plancher : les vitesses doivent compenser
        public double AnchorTime;                   // heure (s) tenue par le cadenas

        // Waypoint dont l'heure est verrouillée (cadenas). Par défaut l'IP (le premier), sinon la cible (TOT), sinon la station ; -1 = aucun
        private int _anchor = -2;
        public int AnchorIndex
        {
            get
            {
                if (_anchor == -2)
                {
                    // Par défaut : l'IP (le premier s'il y en a plusieurs), sinon la cible, sinon la station
                    _anchor = Waypoints.FindIndex(w => Map_Flights.RegexIp.IsMatch((w.Name ?? "").Trim()));
                    if (_anchor < 1) _anchor = Waypoints.FindIndex(w => w.Role == "target");
                    if (_anchor < 0) _anchor = Waypoints.FindIndex(w => w.Role == "station");
                }
                return _anchor;
            }
            set { _anchor = value; }
        }
        public double[] OrigLegM;                   // longueur d'origine des tronçons

        // Décalage de l'heure d'arrivée (dernier waypoint) par rapport au plan, en secondes
        public double LandingShift
        {
            get { return PlanTimes == null || Times.Length == 0 || PlanTimes.Length != Times.Length ? 0 : Times[Times.Length - 1] - PlanTimes[PlanTimes.Length - 1]; }
        }

        // Avant le premier déplacement : on garde les positions, les heures et les vitesses du plan d'origine
        private void BeginEdit()
        {
            if (PlanTimes != null) return;

            Compute();
            PlanTimes = (double[])Times.Clone();
            foreach (Map_Waypoint w in Waypoints) { w.OrigX = w.X; w.OrigY = w.Y; }

            OrigLegM = (double[])LegM.Clone();

            _snapWps = new List<Map_Waypoint>(Waypoints);           // pour "Reset" : la liste d'origine (avant ajouts / suppressions)
            _snapPlan = (double[])PlanTimes.Clone();
            _snapLeg = (double[])OrigLegM.Clone();

            foreach (Map_Waypoint w in Waypoints)
            {
                w.OrigSpeed = w.Speed;
                w.OrigAlt = w.Alt;
                w.OrigType = w.Type; w.OrigAction = w.Action; w.OrigRole = w.Role;
                w.OrigSpeedLocked = w.SpeedLocked;
                w.OrigEtaLocked = w.EtaLocked;
            }

            int ai = AnchorIndex;
            AnchorTime = ai >= 1 && ai < Waypoints.Count ? PlanTimes[ai] : 0;

            VLo = double.MaxValue;
            VHi = 0;
            foreach (Map_Waypoint w in Waypoints)
            {
                if (w.Speed < 10) continue;         // le 0 m/s du décollage / de l'atterrissage ne compte pas
                if (w.Speed < VLo) VLo = w.Speed;
                if (w.Speed > VHi) VHi = w.Speed;
            }
            if (VHi <= 0) VLo = 0;
        }

        public void MoveWaypoint(int index, double x, double y)
        {
            if (index < 0 || index >= Waypoints.Count) return;
            BeginEdit();
            Waypoints[index].X = x;
            Waypoints[index].Y = y;
            Edited = true;
            Check();
        }

        // Vitesse, ETA ou altitude changées à la main (NaN = inchangé). Une ETA imposée devient une heure fixe : les autres s'adaptent.
        public void EditWaypoint(int index, double speed, double forcedTime, double alt)
        {
            if (index < 0 || index >= Waypoints.Count) return;
            BeginEdit();
            Map_Waypoint w = Waypoints[index];
            if (!double.IsNaN(speed)) { w.Speed = speed; w.Dirty.Add("speed"); }
            if (!double.IsNaN(forcedTime)) { w.ForcedTime = forcedTime; w.Dirty.Add("ETA"); }
            if (!double.IsNaN(alt)) { w.Alt = alt; w.Dirty.Add("alt"); }
            Edited = true;
            Check();
        }

        public void SetSpeedLocked(int index, bool locked)
        {
            if (index < 0 || index >= Waypoints.Count) return;
            BeginEdit();
            Waypoints[index].SpeedLocked = locked;
            Waypoints[index].Dirty.Add("speed_locked");
            Edited = true;
        }

        public void SetEtaLocked(int index, bool locked)
        {
            if (index < 0 || index >= Waypoints.Count) return;
            BeginEdit();
            Waypoints[index].EtaLocked = locked;
            Waypoints[index].Dirty.Add("ETA_locked");
            Edited = true;
        }

        // Cadenas de la carte : l'heure de ce waypoint reste fixe quand on déplace les autres
        public void SetAnchor(int index, bool locked)
        {
            if (index < 1 || index >= Waypoints.Count) return;
            BeginEdit();
            if (locked) { AnchorTime = Times[index]; AnchorIndex = index; }
            else
            {
                if (AnchorIndex == index) AnchorIndex = -1;
                Waypoints[index].ForcedTime = double.NaN;           // une heure imposée à la main se déverrouille aussi
            }
            Check();
        }

        // ---- Type de départ du 1er waypoint (liste déroulante de la fiche) ----
        // Deux champs du fichier mission : ["type"] et ["action"] du point 1. Les champs "parking" / "parking_id" des avions ne sont PAS touchés.

        public static readonly string[] StartNames = { "Parking - cold", "Parking - hot", "Runway", "Ground - cold", "Ground - hot", "Air start (at mission start)" };
        private static readonly string[] StartTypes = { "TakeOffParking", "TakeOffParkingHot", "TakeOff", "TakeOffGround", "TakeOffGroundHot", "Turning Point" };
        private static readonly string[] StartActions = { "From Parking Area", "From Parking Area Hot", "From Runway", "From Ground Area", "From Ground Area Hot", "Turning Point" };

        // Départ actuel : indice dans StartNames, -1 si le fichier a un type inconnu
        public int StartKind()
        {
            if (Waypoints.Count == 0) return -1;
            Map_Waypoint w = Waypoints[0];
            for (int k = 0; k < StartTypes.Length; k++)
                if (string.Equals(w.Type, StartTypes[k], StringComparison.OrdinalIgnoreCase) && string.Equals(w.Action, StartActions[k], StringComparison.OrdinalIgnoreCase)) return k;
            for (int k = 0; k < StartTypes.Length; k++)
                if (string.Equals(w.Type, StartTypes[k], StringComparison.OrdinalIgnoreCase)) return k;
            return -1;
        }

        // Change le type de départ. false = refusé, "why" dit pourquoi.
        public bool SetStartType(int kind, out string why)
        {
            why = "";
            if (kind < 0 || kind >= StartTypes.Length || Waypoints.Count < 2) return false;
            Map_Waypoint w = Waypoints[0];
            if (w.IsNew) return false;
            if (kind == StartKind()) return true;

            bool air = kind == 5, ground = kind == 3 || kind == 4;
            if (kind <= 2 && (w.Source == null || !(w.Source.ContainsKey("airdromeId") || w.Source.ContainsKey("helipadId") || w.Source.ContainsKey("linkUnit"))))
            {
                why = "This flight does not start from an airfield (no airfield id in the first waypoint): Parking and Runway starts are not possible here. Use Ground or Air start.";
                return false;
            }

            BeginEdit();

            // On quitte un départ en vol : altitude et vitesse mises par la carte sont rendues
            if (w.StartAuto && !air)
            {
                w.Alt = w.OrigAlt; w.Speed = w.OrigSpeed;
                w.Dirty.Remove("alt"); w.Dirty.Remove("speed");
                w.StartAuto = false;
            }

            w.Type = StartTypes[kind];
            w.Action = StartActions[kind];
            w.Dirty.Add("start");
            StartAir = air;

            if (air)
            {
                // Départ en vol : altitude non nulle (1500 m) et vitesse au moins égale à la vitesse de nav du vol
                if (w.Alt < 300) { w.Alt = 1500; w.Dirty.Add("alt"); w.StartAuto = true; }
                double nav = NavSpeed();
                if (w.Speed < nav)
                {
                    w.Speed = nav;
                    w.Dirty.Add("speed");
                    w.StartAuto = true;
                }
            }
            else if (ground)
            {
                w.Alt = 0;                      // comme GroundStartPoint : départ du sol = altitude 0 (RADIO)
                w.Dirty.Add("alt");
            }
            else
            {
                w.Alt = w.OrigAlt;              // parking / piste : l'altitude du terrain d'origine
                w.Dirty.Remove("alt");
            }

            w.Role = Map_Flights.Classify(w);
            GroundStart = ground;
            Edited = true;
            Check();
            return true;
        }

        // ---- Ajout / suppression d'un waypoint (simulation en mémoire ; écrit au clic sur Save modification) ----
        // Les heures du plan sont gardées : un nouveau waypoint reçoit une heure proportionnelle à sa place sur le tronçon, un waypoint
        // supprimé fusionne ses deux tronçons. Ensuite Compute / Check refont les heures et les vitesses comme pour un déplacement.

        public bool Structural;                         // des waypoints ont été ajoutés / supprimés : la table "points" du fichier est refaite à l'écriture
        private List<Map_Waypoint> _snapWps;
        private double[] _snapPlan, _snapLeg;

        // index = place du nouveau waypoint (1 à n-1) : il s'insère sur le tronçon (index-1 -> index)
        public bool CanInsert(int index, out string why)
        {
            why = "";
            if (index < 1 || index >= Waypoints.Count) { why = "No leg here."; return false; }
            if (Waypoints[index - 1].OrbitPattern == "Race-Track") { why = "This leg is the race-track of an orbit: a waypoint can not be added on it."; return false; }
            return true;
        }

        public bool CanDelete(int index, out string why)
        {
            why = "";
            if (index < 0 || index >= Waypoints.Count) { why = "No waypoint selected."; return false; }
            if (Waypoints.Count <= 2) { why = "A flight needs at least 2 waypoints."; return false; }
            if (index == 0 || index == Waypoints.Count - 1) { why = "The first waypoint (start) and the last one (landing) can not be deleted."; return false; }
            if (Waypoints[index].Role == "airfield") { why = "A waypoint on an airfield can not be deleted."; return false; }
            if (Waypoints[index - 1].OrbitPattern == "Race-Track") { why = "This waypoint is the end of a race-track: delete the orbit waypoint before it instead, or keep both."; return false; }
            return true;
        }

        public bool InsertWaypoint(int index, double x, double y)
        {
            string why;
            if (!CanInsert(index, out why)) return false;
            BeginEdit();

            Map_Waypoint a = Waypoints[index - 1], b = Waypoints[index];
            var w = new Map_Waypoint { Number = -1, IsNew = true, X = x, Y = y, OrigX = x, OrigY = y, Role = "standard", Type = "Turning Point", Action = "Turning Point" };
            w.Alt = (a.Alt + b.Alt) / 2.0;                                                   // moyenne des deux voisins
            w.Speed = a.Speed >= 10 ? a.Speed : (b.Speed >= 10 ? b.Speed : 200);          // le tronçon qui suit garde la vitesse du tronçon coupé
            w.OrigSpeed = w.Speed; w.OrigAlt = w.Alt;
            w.Source = new Dictionary<string, object>();
            object at;
            if (a.Source != null && a.Source.TryGetValue("alt_type", out at)) w.Source["alt_type"] = at;
            w.Dirty.Add("speed"); w.Dirty.Add("alt");                                         // un nouveau point écrit toutes ses valeurs

            double dA = Math.Sqrt((x - a.X) * (x - a.X) + (y - a.Y) * (y - a.Y));
            double dB = Math.Sqrt((x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y));
            double frac = dA + dB > 0 ? dA / (dA + dB) : 0.5;

            var plan = new List<double>(PlanTimes);
            plan.Insert(index, PlanTimes[index - 1] + frac * (PlanTimes[index] - PlanTimes[index - 1]));
            var leg = new List<double>(OrigLegM);
            double old = OrigLegM[index];
            leg[index] = old * (1 - frac);                       // tronçon qui MÈNE au waypoint d'après (devient index + 1)
            leg.Insert(index, old * frac);
            PlanTimes = plan.ToArray();
            OrigLegM = leg.ToArray();

            int ai = AnchorIndex;
            Waypoints.Insert(index, w);
            if (ai >= index) _anchor = ai + 1;

            Structural = true;
            Edited = true;
            Check();
            return true;
        }

        public bool DeleteWaypoint(int index)
        {
            string why;
            if (!CanDelete(index, out why)) return false;
            BeginEdit();

            int ai = AnchorIndex;
            var plan = new List<double>(PlanTimes);
            plan.RemoveAt(index);
            var leg = new List<double>(OrigLegM);
            leg[index + 1] += leg[index];                        // le tronçon fusionné garde la longueur du chemin d'origine : même vitesse
            leg.RemoveAt(index);
            PlanTimes = plan.ToArray();
            OrigLegM = leg.ToArray();

            Waypoints.RemoveAt(index);
            if (ai == index) _anchor = -2;                       // le waypoint verrouillé disparaît : on reprend la règle par défaut
            else if (ai > index) _anchor = ai - 1;
            int na = AnchorIndex;
            AnchorTime = na >= 1 && na < PlanTimes.Length ? PlanTimes[na] : 0;

            Structural = true;
            Edited = true;
            Check();
            return true;
        }

        public void ResetEdit()
        {
            if (PlanTimes == null) return;
            if (Structural && _snapWps != null)
            {
                Waypoints.Clear();
                Waypoints.AddRange(_snapWps);
                PlanTimes = (double[])_snapPlan.Clone();
                OrigLegM = (double[])_snapLeg.Clone();
                Structural = false;
            }
            foreach (Map_Waypoint w in Waypoints)
            {
                w.X = w.OrigX; w.Y = w.OrigY;
                w.Speed = w.OrigSpeed; w.Alt = w.OrigAlt;
                w.SpeedLocked = w.OrigSpeedLocked; w.EtaLocked = w.OrigEtaLocked;
                w.Type = w.OrigType; w.Action = w.OrigAction; w.Role = w.OrigRole; w.StartAuto = false;
                w.Dirty.Clear();
                w.ForcedTime = double.NaN;
            }
            StartAir = false;
            GroundStart = Waypoints.Count > 0 && Waypoints[0].Type.StartsWith("TakeOffGround", StringComparison.Ordinal);
            _anchor = -2;
            int ai = AnchorIndex;
            AnchorTime = ai >= 1 && ai < Waypoints.Count ? PlanTimes[ai] : 0;
            Edited = false;
            Check();
        }

        // Recalcule les distances, puis la vitesse qu'il faut sur chaque tronçon AVANT la cible pour tenir son heure.
        // Un circuit d'attente (et la fin de sa piste) absorbe du temps : on juge alors le bloc entier, et seulement "trop vite".
        // Après la cible : rien à juger, les vitesses du plan sont gardées et l'heure d'arrivée se décale.
        public void Check()
        {
            Compute();
            int n = Waypoints.Count;
            LegStatus = new int[n];
            LegSpeed = new double[n];
            Alerts = new List<string>();
            if (PlanTimes == null || OrigLegM == null || n < 2) return;

            int last = _fixed.Count > 0 ? _fixed[_fixed.Count - 1] : -1;       // dernier waypoint à heure fixe
            for (int b = 1, a = 0; b <= last && b < n; b++)
            {
                // Un bloc va d'un waypoint "ferme" au suivant ; entre les deux : les waypoints souples (circuit d'attente et fin de piste)
                bool soft = Waypoints[b].OrbitPattern != "" || Waypoints[b - 1].OrbitPattern == "Race-Track" || b == HoldWp;
                if (soft && b < last && !_fixed.Contains(b)) continue;

                CheckBlock(a, b);
                a = b;
            }

            if (TakeoffLimited)
                Alerts.Add("Takeoff is at its earliest (5 min after mission start): the speeds must make up the difference");

            // Après la cible : vitesse réellement utilisée sur chaque tronçon (pour l'affichage)
            for (int k = Math.Max(1, last + 1); k < n; k++)
            {
                double dt = Times[k] - Times[k - 1];
                if (dt > 1) LegSpeed[k] = LegM[k] / dt;
            }
        }

        private void CheckBlock(int a, int b)
        {
            double dt = Times[b] - Times[a];
            if (HoldS > 0 && HoldWp >= a && HoldWp < b) dt -= HoldS;       // l'attente ne compte pas dans la vitesse de vol
            double d = 0, d0 = 0;
            for (int k = a + 1; k <= b; k++) { d += LegM[k]; d0 += OrigLegM[k]; }
            if (dt < 5 || d <= 0) return;

            double v = d / dt;
            for (int k = a + 1; k <= b; k++) LegSpeed[k] = v;

            bool timeChanged = Math.Abs(dt - (PlanTimes[b] - PlanTimes[a])) > 1;
            if (VHi <= 0 || (Math.Abs(d - d0) < 1 && !timeChanged)) return;       // pas de vitesses de référence, ou rien n'a bougé ici

            // Bloc qui part du terrain : le roulage et la montée font baisser la vitesse moyenne, on prend donc celle du plan comme plancher
            double lo = VLo;
            if (Waypoints[a].Role == "airfield")
            {
                double dtPlan = PlanTimes[b] - PlanTimes[a];
                if (dtPlan > 5) lo = Math.Min(VLo, d0 / dtPlan);
            }

            bool absorb = b - a > 1;                            // un circuit d'attente est dans le bloc : il avale le temps en trop (pas de "trop lent")
            int st = 0;
            if (v > VHi * 1.25) st = 2;
            else if (v > VHi) st = 1;
            else if (!absorb && v < lo * 0.75) st = 2;
            else if (!absorb && v < lo) st = 1;
            if (st == 0) return;

            for (int k = a + 1; k <= b; k++) LegStatus[k] = st;

            CultureInfo c = CultureInfo.InvariantCulture;
            Alerts.Add("Leg " + a + " > " + b + ": " + (v > VHi ? "too fast" : "too slow") + ", needs " + Map_Units.Speed(v)
                + " (plan " + Map_Units.SpeedVal(lo).ToString("0", c) + " - " + Map_Units.Speed(VHi) + ")"
                + (st == 2 ? " - IMPOSSIBLE" : " - stretched") + (absorb && v > VHi ? " (orbit time already used)" : ""));
        }
    }

    // Lecture des vols joueurs + de leur package, et fabrication des tracés de la carte
    // (un segment par tronçon entre deux waypoints, une flèche de sens, un numéro par waypoint).
    internal static class Map_Flights
    {
        private static readonly Regex RegexPack = new Regex(@"^\s*Pack\s+(\d+)\b", RegexOptions.IgnoreCase);

        // Une couleur par vol (cyclique). Un seul camp affiché : jamais de couleur proche de l'autre camp.
        // Les deux camps (CampaignMaker) : des nuances de bleu pour les bleus, des nuances de rouge pour les rouges.
        private static readonly Color[] PaletteBlueSide =
        {
            Color.FromArgb(0, 210, 255),   Color.FromArgb(120, 230, 90),  Color.FromArgb(255, 235, 60),
            Color.FromArgb(60, 220, 180),  Color.FromArgb(235, 235, 235), Color.FromArgb(190, 255, 90),
            Color.FromArgb(120, 170, 255), Color.FromArgb(170, 210, 170)
        };
        private static readonly Color[] PaletteRedSide =
        {
            Color.FromArgb(255, 150, 0),   Color.FromArgb(120, 230, 90),  Color.FromArgb(255, 235, 60),
            Color.FromArgb(60, 220, 180),  Color.FromArgb(235, 235, 235), Color.FromArgb(190, 255, 90),
            Color.FromArgb(255, 90, 90),   Color.FromArgb(255, 170, 120)
        };
        private static readonly Color[] ShadesBlue =
        {
            Color.FromArgb(0, 210, 255), Color.FromArgb(70, 140, 255), Color.FromArgb(130, 190, 255),
            Color.FromArgb(40, 100, 230), Color.FromArgb(150, 160, 255), Color.FromArgb(0, 165, 200)
        };
        private static readonly Color[] ShadesRed =
        {
            Color.FromArgb(235, 60, 60), Color.FromArgb(255, 110, 40), Color.FromArgb(200, 30, 70),
            Color.FromArgb(255, 130, 130), Color.FromArgb(180, 40, 40), Color.FromArgb(255, 90, 140)
        };

        // Lit tous les groupes avion/hélico d'un pays et les ajoute à "into"
        public static void Collect(Dictionary<string, object> country, string side, List<Map_Flight> into)
        {
            foreach (string cat in new[] { "plane", "helicopter" })
            {
                Dictionary<string, object> groups = Sub(Sub(country, cat), "group");
                if (groups == null) continue;

                foreach (object go in groups.Values)
                {
                    var group = go as Dictionary<string, object>;
                    Dictionary<string, object> units = group == null ? null : Sub(group, "units");
                    if (units == null) continue;

                    var flight = new Map_Flight { Name = Str(group, "name"), Side = side, Source = group, StartDelay = Num(group, "start_time"), Task = Str(group, "task"), IsHeli = cat == "helicopter" };

                    foreach (object uo in units.Values)
                    {
                        var unit = uo as Dictionary<string, object>;
                        if (unit == null) continue;
                        flight.Units++;
                        flight.UnitPos.Add(new[] { Num(unit, "x"), Num(unit, "y"), Num(unit, "heading") });

                        string skill = Str(unit, "skill");
                        bool player = skill.Equals("Client", StringComparison.OrdinalIgnoreCase)
                                   || skill.Equals("Player", StringComparison.OrdinalIgnoreCase);
                        if (player) flight.IsPlayer = true;
                        if (flight.Type == "" || player) flight.Type = Str(unit, "type");

                        if (flight.Callsign == "")
                        {
                            Dictionary<string, object> cs = Sub(unit, "callsign");
                            if (cs != null) flight.Callsign = Regex.Replace(Str(cs, "name"), @"(\D)(\d)", "$1 $2");      // Texaco11 -> Texaco 11
                        }
                    }

                    flight.Freq = Num(group, "frequency");
                    flight.Modulation = Num(group, "modulation") == 1 ? "FM" : "AM";

                    Match m = RegexPack.Match(flight.Name);
                    if (m.Success) flight.Package = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);

                    Dictionary<string, object> points = Sub(Sub(group, "route"), "points");
                    if (points == null) continue;

                    foreach (KeyValuePair<string, object> kv in points)
                    {
                        int n;
                        var p = kv.Value as Dictionary<string, object>;
                        if (p == null || !int.TryParse(kv.Key, out n)) continue;

                        flight.Waypoints.Add(new Map_Waypoint
                        {
                            Number = n,
                            X = Num(p, "x"),
                            Y = Num(p, "y"),
                            Name = Str(p, "name"),
                            Alt = Num(p, "alt"),
                            Speed = Num(p, "speed"),
                            Eta = Num(p, "ETA"),
                            SpeedLocked = Str(p, "speed_locked") == "true",
                            EtaLocked = Str(p, "ETA_locked") == "true",
                            Type = Str(p, "type"),
                            Action = Str(p, "action"),
                            Source = p
                        });
                        flight.Waypoints[flight.Waypoints.Count - 1].Role = Classify(flight.Waypoints[flight.Waypoints.Count - 1]);
                        ReadOrbit(flight.Waypoints[flight.Waypoints.Count - 1]);
                    }
                    flight.Waypoints.Sort((a, b) => a.Number.CompareTo(b.Number));
                    flight.GroundStart = flight.Waypoints.Count > 0 && flight.Waypoints[0].Type.StartsWith("TakeOffGround", StringComparison.Ordinal);

                    // Balise TACAN (tanker) : action ActivateBeacon dans les tâches d'un waypoint
                    foreach (Map_Waypoint wp in flight.Waypoints)
                    {
                        Dictionary<string, object> beacon = FindTask(Sub(wp.Source, "task"), "ActivateBeacon", 0);
                        Dictionary<string, object> bp = beacon == null ? null : Sub(beacon, "params");
                        if (bp == null || Num(bp, "channel") <= 0) continue;

                        string mode = Str(bp, "modeChannel");
                        flight.Tacan = ((int)Num(bp, "channel")) + (mode == "" ? "X" : mode);
                        break;
                    }

                    if (flight.Waypoints.Count > 0) into.Add(flight);
                }
            }
        }

        // Taille réelle supposée d'un waypoint (rayon, en mètres) : le marqueur garde sa taille minimale en pixels quand on dézoome,
        // puis grossit avec la carte quand cette taille dépasse la taille minimale. A MODIFIER ICI si besoin.
        internal static double WaypointRadiusM(string role)
        {
            // Chaque icône mesure environ 100 m de large au sol
            switch (role)
            {
                case "station": return 50;          // IP, station, orbite (carré : côté 100 m)
                case "airfield": return 45;         // triangle
                case "target": return 33;           // l'étoile fait 1,5 fois ce rayon
                case "flow": return 40;             // le losange fait 1,25 fois ce rayon
                default: return 50;                 // point de passage (rond : 100 m)
            }
        }

        // Alias des types d'avion (TypeAlias de Init\\various_table.lua), rempli par la fenêtre carte
        public static Dictionary<string, string> TypeAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string TypeName(string type)
        {
            string a;
            return type != null && TypeAlias.TryGetValue(type, out a) && a != "" ? a : type;
        }

        // ---- forme des marqueurs : rond = standard, carré = IP / station, étoile = cible, triangle = terrain, losange = point de passage du package ----

        internal static readonly Regex RegexIp = new Regex(@"^IP(?![A-Z])", RegexOptions.IgnoreCase);

        private static readonly string[] StationNames = { "STATION", "CAP", "ORBIT", "HOLD", "ANCHOR", "ASSEMBLE", "RACETRACK", "LOITER", "TANKER", "AWACS" };
        private static readonly string[] FlowNames = { "SPLIT", "EGRESS", "JOIN", "REJOIN", "ATTACK", "PUSH" };

        // Cherche dans le nom du waypoint (noms DCE : Departure, Assemble, Join, IP, Attack, Target, Egress, Split, Land, CAP-...),
        // puis dans les tâches du waypoint (Bombing, Orbit...) pour les missions qui n'utilisent pas ces noms.
        internal static string Classify(Map_Waypoint w)
        {
            string n = (w.Name ?? "").Trim().ToUpperInvariant();
            string t = w.Type ?? "", a = w.Action ?? "";

            if (t.StartsWith("TakeOff", StringComparison.OrdinalIgnoreCase) || t.Equals("Land", StringComparison.OrdinalIgnoreCase)
                || a.StartsWith("From ", StringComparison.OrdinalIgnoreCase) || a.Equals("Landing", StringComparison.OrdinalIgnoreCase))
                return "airfield";

            if (n.StartsWith("TARGET") || n.StartsWith("TGT") || n.StartsWith("DMPI")) return "target";
            if (RegexIp.IsMatch(n)) return "station";
            foreach (string s in StationNames) if (n.StartsWith(s)) return "station";
            foreach (string s in FlowNames) if (n.StartsWith(s)) return "flow";
            if (n.StartsWith("LAND")) return "airfield";

            var ids = new List<string>();
            CollectIds(Sub(w.Source, "task"), ids, 0);
            foreach (string id in ids)
            {
                if (id.StartsWith("Bombing") || id.StartsWith("CarpetBombing") || id.StartsWith("AttackGroup")
                    || id.StartsWith("AttackUnit") || id.StartsWith("Strike")) return "target";
                if (id == "Orbit" || id == "Tanker" || id == "AWACS" || id == "Refueling" || id.StartsWith("OrbitPosition")) return "station";
            }
            return "standard";
        }

        // Orbite du waypoint (AWACS, tanker, CAP...) : tâche "Orbit", avec son schéma (cercle ou hippodrome) et sa vitesse
        private static void ReadOrbit(Map_Waypoint w)
        {
            Dictionary<string, object> orbit = FindTask(Sub(w.Source, "task"), "Orbit", 0);
            if (orbit == null)
            {
                // Orbite demandée par la fonction DCE (tâche "Script") : OrbitPosition('vol', altitude m, vitesse m/s, heure de fin s).
                // Cercle autour de la position du waypoint.
                Match om = FindOrbitPosition(Sub(w.Source, "task"), 0);
                if (om == null) return;

                CultureInfo ic = CultureInfo.InvariantCulture;
                w.OrbitPattern = "Circle";
                double.TryParse(om.Groups[2].Value, NumberStyles.Float, ic, out w.OrbitAlt);
                double.TryParse(om.Groups[3].Value, NumberStyles.Float, ic, out w.OrbitSpeed);
                double.TryParse(om.Groups[4].Value, NumberStyles.Float, ic, out w.OrbitUntil);
                return;
            }

            Dictionary<string, object> prm = Sub(orbit, "params");
            string pattern = prm == null ? "" : Str(prm, "pattern");
            w.OrbitPattern = pattern.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0 ? "Race-Track" : "Circle";   // sans schéma : cercle (défaut de DCS)
            w.OrbitSpeed = prm == null ? 0 : Num(prm, "speed");
            w.OrbitAlt = prm == null ? 0 : Num(prm, "altitude");
        }

        private static readonly Regex RegexOrbitPosition = new Regex(@"OrbitPosition\s*\(\s*(['""]).*?\1\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)", RegexOptions.Singleline);

        // Cherche, dans les tâches imbriquées, une commande Script "OrbitPosition(...)" (fonction DCE, pas DCS)
        private static Match FindOrbitPosition(Dictionary<string, object> node, int depth)
        {
            if (node == null || depth > 10) return null;
            foreach (KeyValuePair<string, object> kv in node)
            {
                var child = kv.Value as Dictionary<string, object>;
                if (child != null)
                {
                    Match found = FindOrbitPosition(child, depth + 1);
                    if (found != null) return found;
                    continue;
                }
                string cmd = kv.Value as string;
                if (kv.Key == "command" && cmd != null)
                {
                    Match m = RegexOrbitPosition.Match(cmd);
                    if (m.Success) return m;
                }
            }
            return null;
        }

        // La table de la tâche qui a cet "id", dans les tâches imbriquées (ComboTask, ControlledTask...)
        private static Dictionary<string, object> FindTask(Dictionary<string, object> node, string id, int depth)
        {
            if (node == null || depth > 10) return null;
            if (Str(node, "id") == id) return node;

            foreach (object o in node.Values)
            {
                var child = o as Dictionary<string, object>;
                if (child == null) continue;
                Dictionary<string, object> found = FindTask(child, id, depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        // Tous les "id" (et "command") des tâches d'un waypoint, y compris les tâches imbriquées
        private static void CollectIds(Dictionary<string, object> node, List<string> ids, int depth)
        {
            if (node == null || depth > 10) return;
            foreach (KeyValuePair<string, object> kv in node)
            {
                var child = kv.Value as Dictionary<string, object>;
                if (child != null) { CollectIds(child, ids, depth + 1); continue; }
                if (kv.Key == "id" || kv.Key == "command")
                {
                    string v = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(v)) ids.Add(v);
                }
            }
        }

        // Garde les vols du (ou des) camp(s) affiché(s), leur donne une couleur et remplit data.Plans / data.Flights.
        // Tous les avions sont gardés (joueurs, package des joueurs, autres) ; seul le camp du joueur est affiché,
        // sauf en mode CampaignMaker (les deux camps, l'utilisateur les masque à la demande dans la fenêtre).
        public static void Finish(Map_MissionData data, List<Map_Flight> all)
        {
            bool bothSides = ParamConf.UserLevel == UserLevel.CampaignMaker;
            var packs = new HashSet<int>(all.Where(f => f.IsPlayer && f.Package >= 0).Select(f => f.Package));
            var playerSides = new HashSet<string>(all.Where(f => f.IsPlayer).Select(f => f.Side));

            foreach (Map_Flight f in all)
                f.IsPackage = !f.IsPlayer && f.Package >= 0 && packs.Contains(f.Package);

            // Un vol sans trajet (moins de 2 waypoints) n'a rien à montrer, sauf s'il est joueur (sa fiche reste utile)
            List<Map_Flight> kept = all.Where(f => (f.IsPlayer || f.Waypoints.Count >= 2)
                                                   && (bothSides || playerSides.Contains(f.Side))).ToList();

            // Couleurs : joueurs d'abord, puis le package, puis les autres
            bool playerIsRed = playerSides.Contains("red") && !playerSides.Contains("blue");
            int nBlue = 0, nRed = 0, nAny = 0;
            foreach (Map_Flight f in kept.OrderBy(k => k.IsPlayer ? 0 : (k.IsPackage ? 1 : 2)))
            {
                if (bothSides)
                {
                    f.Color = f.Side == "red" ? ShadesRed[nRed++ % ShadesRed.Length] : ShadesBlue[nBlue++ % ShadesBlue.Length];
                }
                else
                {
                    Color[] pal = playerIsRed ? PaletteRedSide : PaletteBlueSide;
                    f.Color = pal[nAny++ % pal.Length];
                }
            }

            data.Flights = kept;
            data.PlayerFlights = kept.Count(f => f.IsPlayer);
            data.PackageFlights = kept.Count(f => f.IsPackage);
            data.OtherFlights = kept.Count - data.PlayerFlights - data.PackageFlights;

            data.Plans.AddRange(BuildPlans(kept));
        }

        // Tracés des vols donnés : un segment par tronçon, une flèche de sens, un marqueur + un numéro par waypoint.
        // Appelé aussi quand le CampaignMaker masque / affiche un camp.
        public static List<Map_Drawing> BuildPlans(IEnumerable<Map_Flight> flights)
        {
            var lines = new List<Map_Drawing>();
            var orbits = new List<Map_Drawing>();
            var arrows = new List<Map_Drawing>();
            var marks = new List<Map_Drawing>();
            var heliUnits = new List<Map_Drawing>();
            var texts = new List<Map_Drawing>();

            // Les vols joueurs en dernier : ils restent visibles par-dessus les autres
            foreach (Map_Flight f in flights.OrderBy(k => k.IsPlayer ? 2 : (k.IsPackage ? 1 : 0)))
            {
                string label = f.Title;
                float thick = f.IsPlayer ? 3.2f : (f.IsPackage ? 2.6f : 1.8f);

                // Une seule flèche de sens par vol, au milieu du tronçon le plus long (le plus lisible à l'écran)
                int longest = -1;
                double longestLen = -1;
                for (int i = 0; i + 1 < f.Waypoints.Count; i++)
                {
                    double ddx = f.Waypoints[i + 1].X - f.Waypoints[i].X, ddy = f.Waypoints[i + 1].Y - f.Waypoints[i].Y;
                    double len = ddx * ddx + ddy * ddy;
                    if (len > longestLen) { longestLen = len; longest = i; }
                }

                for (int i = 0; i + 1 < f.Waypoints.Count; i++)
                {
                    Map_Waypoint a = f.Waypoints[i], b = f.Waypoints[i + 1];

                    var leg = new Map_Drawing { Name = f.Name, Label = label, Group = "plans", Kind = "Line", Line = f.Color, Thickness = thick, Index = i + 1,
                        Alert = f.LegStatus != null && i + 1 < f.LegStatus.Length ? f.LegStatus[i + 1] : 0 };
                    leg.Points.Add(new double[] { a.X, a.Y });
                    leg.Points.Add(new double[] { b.X, b.Y });
                    lines.Add(leg);

                    if (i == longest)
                    {
                        var arrow = new Map_Drawing { Name = f.Name, Group = "plans", Kind = "Arrow", Line = f.Color };
                        arrow.Points.Add(new double[] { a.X, a.Y });
                        arrow.Points.Add(new double[] { b.X, b.Y });
                        arrows.Add(arrow);
                    }
                }

                AddOrbits(f, orbits, arrows);

                // Hélicoptères : une silhouette par avion à sa position de départ (dessinée seulement quand le vol est sélectionné)
                List<double[]> shownUnits = f.PendingUnitPos != null ? f.PendingUnitPos : (f.GroundStart ? f.UnitPos : null);
                if (f.IsHeli && shownUnits != null)
                    for (int u = 0; u < shownUnits.Count; u++)
                    {
                        var hu = new Map_Drawing { Name = f.Name, Group = "plans", Kind = "Heli", Line = f.Color, Index = u + 1, Radius = shownUnits[u][2] };
                        hu.Points.Add(new double[] { shownUnits[u][0], shownUnits[u][1] });
                        heliUnits.Add(hu);
                    }

                for (int i = 0; i < f.Waypoints.Count; i++)
                {
                    Map_Waypoint w = f.Waypoints[i];

                    var m = new Map_Drawing { Name = f.Name, Group = "plans", Kind = "Marker", Line = f.Color, Text = w.Role, Thickness = f.IsPlayer ? 1f : 0f, Index = i, Radius = WaypointRadiusM(w.Role) };
                    m.Points.Add(new double[] { w.X, w.Y });
                    marks.Add(m);

                    // Numéro (+ nom du waypoint pour les joueurs). Les autres vols : numéro seulement quand le vol est sélectionné ("onsel")
                    string text = i + (f.IsPlayer && w.ShortName != "" ? " " + w.ShortName : "");
                    var t = new Map_Drawing { Name = f.Name, Group = "plans", Kind = "Text", Line = f.Color, Text = text, Label = f.IsPlayer ? "" : "onsel" };
                    t.Points.Add(new double[] { w.X, w.Y });
                    texts.Add(t);

                    // Cadenas fermé à côté du waypoint dont l'heure est verrouillée (dessiné par Map_UI_View)
                    if (i >= 1 && f.IsFixed(i))
                    {
                        var lk = new Map_Drawing { Name = f.Name, Group = "plans", Kind = "Lock", Line = f.Color, Label = f.IsPlayer ? "" : "onsel" };
                        lk.Points.Add(new double[] { w.X, w.Y });
                        texts.Add(lk);
                    }
                }
            }

            var all = new List<Map_Drawing>(lines);
            all.AddRange(orbits);
            all.AddRange(arrows);
            all.AddRange(marks);
            all.AddRange(heliUnits);
            all.AddRange(texts);
            return all;
        }

        // Circuits d'attente (AWACS, tanker, CAP...) : cercle autour du waypoint, ou hippodrome entre ce waypoint et le suivant
        // (comme DCS : la piste va du waypoint portant la tâche Orbit au waypoint d'après). Tracé largement : ces avions tournent large.
        // Rayon = rayon de virage à 25 degrés d'inclinaison pour la vitesse demandée (entre 1,5 et 30 km). C'est une approximation.
        private static void AddOrbits(Map_Flight f, List<Map_Drawing> shapes, List<Map_Drawing> arrows)
        {
            int usedAsEnd = -1;         // waypoint déjà pris comme bout d'hippodrome : il ne redémarre pas un autre circuit
            bool tanker = f.Task == "Refueling";

            for (int i = 0; i < f.Waypoints.Count; i++)
            {
                Map_Waypoint w = f.Waypoints[i];
                if (w.OrbitPattern == "" || i == usedAsEnd) continue;

                double v = w.OrbitSpeed > 1 ? w.OrbitSpeed : (w.Speed > 1 ? w.Speed : 200);
                double r = Math.Max(1500, Math.Min(30000, v * v / (9.81 * Math.Tan(25 * Math.PI / 180))));

                var shape = new Map_Drawing
                {
                    Name = f.Name, Group = "plans", Kind = "Polygon", Closed = true, Dashed = true,
                    Line = f.Color, Thickness = f.IsPlayer ? 2.4f : 1.6f,
                    Label = tanker ? TankerLabel(f, w) : (f.Task != "" ? f.Task + " " : "") + "orbit"
                };
                if (tanker) shape.Fill = Color.FromArgb(70, f.Color);       // le circuit du tanker est rempli : on le repère de loin

                bool race = false;
                if (w.OrbitPattern == "Race-Track" && i + 1 < f.Waypoints.Count)
                {
                    Map_Waypoint b = f.Waypoints[i + 1];
                    double dx = b.X - w.X, dy = b.Y - w.Y, len = Math.Sqrt(dx * dx + dy * dy);
                    race = b.Role != "airfield" && len >= 2000 && len <= 300000;     // sinon : on ne devine pas, on dessine un cercle

                    if (race)
                    {
                        usedAsEnd = i + 1;
                        double ux = dx / len, uy = dy / len;        // du début vers la fin (x = nord, y = est)
                        double mx = uy, my = -ux;                   // côté gauche : on part à gauche, on tourne à droite (sens des aiguilles d'une montre)
                        const int N = 16;

                        // Aller (côté gauche) puis demi-cercle autour de la fin, retour (côté droit) puis demi-cercle autour du début
                        for (int k = 0; k <= N; k++)
                        {
                            double a = Math.PI * k / N;
                            shape.Points.Add(new double[] { b.X + r * (mx * Math.Cos(a) + ux * Math.Sin(a)), b.Y + r * (my * Math.Cos(a) + uy * Math.Sin(a)) });
                        }
                        for (int k = 0; k <= N; k++)
                        {
                            double a = Math.PI * k / N;
                            shape.Points.Add(new double[] { w.X - r * (mx * Math.Cos(a) + ux * Math.Sin(a)), w.Y - r * (my * Math.Cos(a) + uy * Math.Sin(a)) });
                        }

                        // Flèche de sens sur le trajet aller
                        var arrow = new Map_Drawing { Name = f.Name, Group = "plans", Kind = "Arrow", Line = f.Color };
                        arrow.Points.Add(new double[] { w.X + r * mx, w.Y + r * my });
                        arrow.Points.Add(new double[] { b.X + r * mx, b.Y + r * my });
                        arrows.Add(arrow);
                    }
                }

                if (!race)
                {
                    const int N = 48;
                    for (int k = 0; k < N; k++)
                    {
                        double a = 2 * Math.PI * k / N;
                        shape.Points.Add(new double[] { w.X + r * Math.Cos(a), w.Y + r * Math.Sin(a) });
                    }
                }
                shapes.Add(shape);
            }
        }

        // Légende du circuit d'un tanker, courte : "Texaco 11  A24" puis "251.0 AM - TACAN 21X"  (A24 = angels 24 = 24 000 ft)
        private static string TankerLabel(Map_Flight f, Map_Waypoint w)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            double alt = w.OrbitAlt > 0 ? w.OrbitAlt : w.Alt;      // mètres

            string line1 = f.Callsign != "" ? f.Callsign : f.Name;
            if (alt > 0) line1 += "  " + Map_Units.AltTag(alt);

            var parts = new List<string>();
            if (f.Freq > 0) parts.Add(f.Freq.ToString("0.0##", c) + " " + f.Modulation);
            if (f.Tacan != "") parts.Add("TACAN " + f.Tacan);
            return parts.Count == 0 ? line1 : line1 + "\n" + string.Join(" - ", parts);
        }

        // Portée radar des AWACS (m), indicative : deux familles. Le motif est cherché dans le type de l'avion.
        // A MODIFIER ICI si tu connais mieux les valeurs (nom affiché, portée en mètres).
        private static readonly object[][] AwacsTable =
        {
            new object[] { "E-3",     "E-3C",    400000.0 },      // grands radars
            new object[] { "EC-121",  "EC-121",  250000.0 },      // guerre du Vietnam (Warning Star)
            new object[] { "A-50",    "A-50",    350000.0 },
            new object[] { "KJ-2000", "KJ-2000", 350000.0 },
            new object[] { "E-2",     "E-2D",    250000.0 },      // Hawkeye (E-2C / E-2D) : radar plus court
        };

        private static bool AwacsRange(Map_Flight f, out string name, out double range)
        {
            name = "AWACS";
            range = 0;
            foreach (object[] row in AwacsTable)
            {
                if ((f.Type ?? "").IndexOf((string)row[0], StringComparison.OrdinalIgnoreCase) < 0) continue;
                name = (string)row[1];
                range = (double)row[2];
                return true;
            }
            if (f.Task == "AWACS") { range = 250000; return true; }     // AWACS d'un type inconnu : valeur moyenne
            return false;
        }

        // Zone couverte par le radar de chaque AWACS : un ovale qui suit son circuit (hippodrome : capsule autour des deux
        // points de la piste ; cercle : disque autour du point). Couleur du camp, pas du vol.
        public static List<Map_Drawing> BuildRadars(IEnumerable<Map_Flight> flights)
        {
            var list = new List<Map_Drawing>();

            foreach (Map_Flight f in flights)
            {
                string name;
                double range;
                if (!AwacsRange(f, out name, out range)) continue;

                // Le circuit : premier waypoint avec une tâche Orbit, sinon premier waypoint "station"
                int i = f.Waypoints.FindIndex(w => w.OrbitPattern != "");
                if (i < 0) i = f.Waypoints.FindIndex(w => w.Role == "station");
                if (i < 0) continue;

                Map_Waypoint a = f.Waypoints[i], b = null;
                if (a.OrbitPattern == "Race-Track" && i + 1 < f.Waypoints.Count)
                {
                    Map_Waypoint n = f.Waypoints[i + 1];
                    double len = Math.Sqrt((n.X - a.X) * (n.X - a.X) + (n.Y - a.Y) * (n.Y - a.Y));
                    if (n.Role != "airfield" && len >= 2000 && len <= 300000) b = n;       // mêmes règles que le dessin du circuit
                }

                Color side = f.Side == "red" ? Color.FromArgb(255, 140, 130) : Color.FromArgb(130, 200, 255);
                string label = name + " radar";
                var shape = new Map_Drawing
                {
                    Name = f.Name, Group = "radars", Kind = "Polygon", Closed = true, Dashed = true,
                    Line = Color.FromArgb(200, side), Fill = Color.FromArgb(14, side), Thickness = 1.6f, Label = label, RangeM = range
                };

                if (b == null)
                {
                    const int N = 72;
                    for (int k = 0; k < N; k++)
                    {
                        double ang = 2 * Math.PI * k / N;
                        shape.Points.Add(new double[] { a.X + range * Math.Cos(ang), a.Y + range * Math.Sin(ang) });
                    }
                }
                else
                {
                    double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                    double ux = dx / len, uy = dy / len, mx = uy, my = -ux;
                    const int N = 36;
                    for (int k = 0; k <= N; k++)
                    {
                        double ang = Math.PI * k / N;
                        shape.Points.Add(new double[] { b.X + range * (mx * Math.Cos(ang) + ux * Math.Sin(ang)), b.Y + range * (my * Math.Cos(ang) + uy * Math.Sin(ang)) });
                    }
                    for (int k = 0; k <= N; k++)
                    {
                        double ang = Math.PI * k / N;
                        shape.Points.Add(new double[] { a.X - range * (mx * Math.Cos(ang) + ux * Math.Sin(ang)), a.Y - range * (my * Math.Cos(ang) + uy * Math.Sin(ang)) });
                    }
                }
                list.Add(shape);
            }
            return list;
        }

        // ---- texte de la fiche d'un vol ----

        internal static string Clock(double missionStart, double t)
        {
            double sec = (missionStart + t) % 86400;
            if (sec < 0) sec += 86400;
            return TimeSpan.FromSeconds(Math.Round(sec)).ToString(@"hh\:mm\:ss");
        }

        private static string Duration(double t)
        {
            var ts = TimeSpan.FromSeconds(Math.Round(t));
            return ts.TotalHours >= 1 ? ((int)ts.TotalHours) + " h " + ts.Minutes.ToString("00") + " min" : ts.Minutes + " min " + ts.Seconds.ToString("00") + " s";
        }

        // Lignes "titre / valeur" de la fiche d'un vol (temps = heure de début de mission + heure du waypoint)
        public static List<KeyValuePair<string, string>> Describe(Map_Flight f, double missionStart)
        {
            f.Compute();
            var rows = new List<KeyValuePair<string, string>>();
            CultureInfo c = CultureInfo.InvariantCulture;
            int n = f.Waypoints.Count;
            if (n == 0) return rows;

            double start = f.Times[0], end = f.Times[n - 1], dur = end - start;

            rows.Add(new KeyValuePair<string, string>("Start", Clock(missionStart, start)));
            rows.Add(new KeyValuePair<string, string>("End", Clock(missionStart, end)));
            rows.Add(new KeyValuePair<string, string>("Duration", Duration(dur)));
            rows.Add(new KeyValuePair<string, string>("Distance", Map_Units.Dist(f.DistanceM)));
            rows.Add(new KeyValuePair<string, string>("Waypoints", n.ToString(c)));

            if (dur > 1)
            {
                double ms = f.DistanceM / dur;
                rows.Add(new KeyValuePair<string, string>("Average speed", Map_Units.Speed(ms)));
            }
            if (f.MaxAlt > 0)
                rows.Add(new KeyValuePair<string, string>("Altitude", Map_Units.AltRange(f.MinAlt, f.MaxAlt)));
            if (f.Edited)
                rows.Add(new KeyValuePair<string, string>("Plan check", (f.Alerts.Count == 0 ? "OK: TOT kept, speeds in range" : string.Join("\n", f.Alerts))
                    + (f.AnchorIndex < 0 ? "\nNo target: nothing locked, times follow the plan speeds" : "")
                    + "\nTakeoff " + Clock(missionStart, f.Times[0]) + "  (" + ShiftText(f.Times[0] - f.PlanTimes[0]) + ")"
                    + "\nLanding " + Clock(missionStart, f.Times[f.Times.Length - 1]) + "  (" + ShiftText(f.LandingShift) + ")"));
            if (!f.EtaFromFile)
                rows.Add(new KeyValuePair<string, string>("Note", "Times are estimated from the speeds."));
            return rows;
        }

        public static string RoleLabel(string role)
        {
            switch (role)
            {
                case "airfield": return "Airfield (take-off / landing)";
                case "target": return "Target";
                case "station": return "IP / station";
                case "flow": return "Package point (split, egress, join, attack)";
                default: return "Standard";
            }
        }

        // Noms lisibles des tâches d'un waypoint (Orbit, Bombing, CustomRejoin...), sans les mots génériques
        public static string TaskNames(Map_Waypoint w)
        {
            var ids = new List<string>();
            CollectIds(Sub(w.Source, "task"), ids, 0);
            var names = new List<string>();
            foreach (string id in ids)
            {
                string n = id;
                int k = n.IndexOf('(');
                if (k > 0) n = n.Substring(0, k);
                n = n.Trim();
                if (n == "" || n == "ComboTask" || n == "WrappedAction" || n == "Option" || n == "Script" || n == "Start" || names.Contains(n)) continue;
                names.Add(n);
            }
            if (names.Count == 0) return "-";
            string txt = string.Join(", ", names.Take(5));
            return names.Count > 5 ? txt + " (+" + (names.Count - 5) + ")" : txt;
        }

        // Cap vrai (0 = nord, 90 = est) du point a vers le point b ; DCS : x = nord, y = est
        private static double Heading(Map_Waypoint a, Map_Waypoint b)
        {
            double h = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
            return (h + 360.0) % 360.0;
        }

        // Fiche d'un waypoint (lecture seule pour l'instant ; "Source" garde la table Lua pour l'éditer plus tard)
        public static List<KeyValuePair<string, string>> DescribeWaypoint(Map_Flight f, int index, double missionStart)
        {
            f.Compute();
            var rows = new List<KeyValuePair<string, string>>();
            if (index < 0 || index >= f.Waypoints.Count) return rows;

            CultureInfo c = CultureInfo.InvariantCulture;
            Map_Waypoint w = f.Waypoints[index];

            rows.Add(new KeyValuePair<string, string>("Number", index + "  (of 0 - " + (f.Waypoints.Count - 1) + ")"));
            rows.Add(new KeyValuePair<string, string>("Name", w.ShortName == "" ? "-" : w.ShortName));
            // 1er waypoint : le nom compréhensible du type de départ (Parking - cold...) ; les autres : type / action du fichier
            int sk = index == 0 ? f.StartKind() : -1;
            if (index == 0) rows.Add(new KeyValuePair<string, string>("Start type", sk >= 0 ? Map_Flight.StartNames[sk] : (w.Type == "" ? "-" : w.Type)));
            else rows.Add(new KeyValuePair<string, string>("Type / action", (w.Type == "" ? "-" : w.Type) + (w.Action != "" ? "\n" + w.Action : "")));
            rows.Add(new KeyValuePair<string, string>("Time", Clock(missionStart, f.Times[index]) + (f.Edited ? (f.IsFixed(index) ? "  (kept)" : "  (" + ShiftText(f.Times[index] - f.PlanTimes[index]) + ")") : "")));

            string altType = Str(w.Source, "alt_type");
            rows.Add(new KeyValuePair<string, string>("Altitude", Map_Units.Alt(w.Alt) + (altType != "" ? "  (" + altType + ")" : "")));
            // Comme dans DCS : la vitesse d'un waypoint est celle du tronçon qui SUIT ce waypoint (vers le suivant)
            string speedText = w.Speed > 0 ? Map_Units.Speed(w.Speed) : "-";
            int nx = index + 1;
            if (f.Edited && nx < f.LegSpeed.Length && f.LegSpeed[nx] > 0)
                speedText = Map_Units.Speed(f.LegSpeed[nx]) + " needed to WP " + nx + "\nplan: " + Map_Units.Speed(w.Speed)
                    + (f.LegStatus[nx] == 2 ? "\nIMPOSSIBLE" : (f.LegStatus[nx] == 1 ? "\nstretched" : ""));
            rows.Add(new KeyValuePair<string, string>("Speed", speedText));
            if (f.HoldWp == index && f.HoldS > 0)
                rows.Add(new KeyValuePair<string, string>("Hold", (f.HoldNew ? "new orbit here: " : "orbit extended by ") + Duration(f.HoldS)
                    + "\nuntil " + Clock(missionStart, f.HoldUntil())));

            if (w.OrbitPattern != "")
                rows.Add(new KeyValuePair<string, string>("Orbit", w.OrbitPattern
                    + (w.OrbitAlt > 0 ? "  " + Map_Units.FlightLevel(w.OrbitAlt) : "")
                    + (w.OrbitSpeed > 0 ? "  " + Map_Units.Speed(w.OrbitSpeed) : "")
                    + (w.OrbitUntil > 0 ? "\nuntil " + Clock(missionStart, w.OrbitUntil) : "")));

            rows.Add(new KeyValuePair<string, string>("Tasks", TaskNames(w)));
            return rows;
        }

        // Petit texte près de la souris pendant qu'on déplace un waypoint : heure gardée, longueur et vitesse nécessaire des deux tronçons touchés
        public static string DragText(Map_Flight f, int index, double missionStart)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("WP " + index + "   " + Clock(missionStart, f.Times[index]) + (f.IsFixed(index) ? " (kept)" : " (" + ShiftText(f.Times[index] - f.PlanTimes[index]) + ")"));

            foreach (int k in new[] { index, index + 1 })
            {
                if (k < 1 || k >= f.Waypoints.Count || k >= f.LegStatus.Length) continue;
                string state = f.LegStatus[k] == 2 ? "  IMPOSSIBLE" : (f.LegStatus[k] == 1 ? "  stretched" : "");
                sb.Append("\nleg " + (k - 1) + " > " + k + "   " + Map_Units.Dist(f.LegM[k]) + "   "
                    + (f.LegSpeed[k] > 0 ? Map_Units.Speed(f.LegSpeed[k]) : "-") + state);
            }
            sb.Append("\ntakeoff " + Clock(missionStart, f.Times[0]) + " (" + ShiftText(f.Times[0] - f.PlanTimes[0]) + ")");
            sb.Append("\nlanding " + Clock(missionStart, f.Times[f.Times.Length - 1]) + " (" + ShiftText(f.LandingShift) + ")");
            return sb.ToString();
        }

        // "+3 min", "-1 min", "same"
        private static string ShiftText(double seconds)
        {
            int m = (int)Math.Round(seconds / 60.0);
            return m == 0 ? "same" : (m > 0 ? "+" : "") + m + " min";
        }

        // ---- petits outils de lecture des tables Lua (Dictionary<string, object>) ----

        private static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return "";
            if (v is bool) return (bool)v ? "true" : "false";
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v is double) return (double)v;
            return 0;
        }
    }

    // Petite "fiche" jolie : bande de couleur à gauche, titre, sous-titre, puis lignes titre / valeur.
    // Sert pour les bases et pour les vols (lecture seule). La hauteur s'adapte au contenu.
    internal class Map_UI_Card : Control
    {
        private string _title = "", _subtitle = "";
        private Color _accent = Color.SteelBlue;
        private List<KeyValuePair<string, string>> _rows = new List<KeyValuePair<string, string>>();

        private readonly Font _fTitle = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        private readonly Font _fSub = new Font("Segoe UI", 9f);
        private readonly Font _fLabel = new Font("Segoe UI", 9f);
        private readonly Font _fValue = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        private const int Pad = 14, HeaderH = 58, LineH = 18, RowPad = 8;

        // ---- Édition en place (fiche d'un waypoint) : on clique la valeur Time / Altitude / Speed pour la changer ----
        // Cadenas à gauche de Time = heure verrouillée sur la carte ; "locked" à droite = cases DCS ETA_locked / speed_locked
        public int WantedHeight = 70;                                   // hauteur voulue par le contenu (le parent peut la réduire s'il manque de place)
        public bool Editable;
        public ComboBox Embedded;                                       // liste posée dans la fiche, à la place de la valeur de la ligne EmbeddedKey
        public string EmbeddedKey = "";
        public bool LockTot, LockEta, LockSpeed;
        public bool LockEnabled = true;                                 // le cadenas de la carte ne change que si le mode crayon est actif
        public string EditTime = "", EditAlt = "", EditSpeed = "";      // texte proposé quand on clique la valeur
        public event Action<string, string> ValueEdited;                // champ ("time"/"alt"/"speed"), texte saisi
        public event Action<string, bool> LockToggled;                  // "tot" / "eta" / "speed", nouvel état

        private readonly TextBox _box = new TextBox { Visible = false, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly ToolTip _tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 350, ShowAlways = true };
        private readonly List<KeyValuePair<string, Rectangle>> _hits = new List<KeyValuePair<string, Rectangle>>();   // zones cliquables
        private string _editing = "", _hover = "";

        public Map_UI_Card()
        {
            Controls.Add(_box);
            _box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EndEdit(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndEdit(false); }
            };
            _box.Leave += (s, e) => EndEdit(true);

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = HeaderH + 12;
        }

        public void SetContent(string title, string subtitle, Color accent, List<KeyValuePair<string, string>> rows)
        {
            _title = title ?? "";
            _subtitle = subtitle ?? "";
            _accent = accent;
            _rows = rows ?? new List<KeyValuePair<string, string>>();
            EndEdit(false);

            int h = HeaderH + 8;
            foreach (KeyValuePair<string, string> r in _rows)
                h += RowHeight(r.Value);
            WantedHeight = h + 8;
            Height = WantedHeight;
            Invalidate();
        }

        private float RowX { get { return 6 + Pad + (Editable ? 12 : 0); } }

        private void EndEdit(bool commit)
        {
            if (_editing == "") return;
            string field = _editing, text = _box.Text;
            _editing = "";
            _box.Visible = false;
            if (commit) { if (ValueEdited != null) ValueEdited(field, text); }
            Invalidate();
        }

        private void StartEdit(string field, Rectangle r)
        {
            EndEdit(true);
            _editing = field;
            _box.Text = field == "time" ? EditTime : field == "alt" ? EditAlt : EditSpeed;
            _box.SetBounds(r.X, r.Y, 84, r.Height);
            _box.Visible = true;
            _box.BringToFront();
            _box.Focus();
            _box.SelectAll();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            string h = "";
            foreach (var z in _hits) if (z.Value.Contains(e.Location)) { h = z.Key; break; }
            if (h != _hover)
            {
                _hover = h;
                Cursor = h == "" ? Cursors.Default : Cursors.Hand;
                string tip = "";
                if (h == "tot") tip = LockEnabled
                    ? "LOCK THIS TIME (map only)\nWhen you move waypoints, the time of this waypoint stays fixed and the other waypoints adapt. Default: the first IP (else the target)."
                    : "Time lock (map only). Switch the pencil mode on to change it.";
                else if (h == "eta") tip = "ETA_locked (DCS)\nThe arrival time is kept when DCS recalculates the flight plan.";
                else if (h == "speed") tip = "speed_locked (DCS)\nThe speed is kept when DCS recalculates the flight plan.";
                else if (h == "time") tip = "Click to change the time (HH:MM:SS). The other waypoints adapt.";
                else if (h == "alt") tip = "Click to change the altitude (" + Map_Units.AltName + "). Change the units with the button at the top.";
                else if (h == "spd") tip = "Click to change the speed (" + Map_Units.SpeedName + ") of the leg that follows this waypoint (towards the next one). Change the units with the button at the top.";
                _tip.SetToolTip(this, tip);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = "";
            Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Editable || e.Button != MouseButtons.Left) return;
            foreach (var z in _hits)
            {
                if (!z.Value.Contains(e.Location)) continue;
                if (z.Key == "tot") { if (!LockEnabled) return; LockTot = !LockTot; if (LockToggled != null) LockToggled("tot", LockTot); }
                else if (z.Key == "eta") { LockEta = !LockEta; if (LockToggled != null) LockToggled("eta", LockEta); }
                else if (z.Key == "speed") { LockSpeed = !LockSpeed; if (LockToggled != null) LockToggled("speed", LockSpeed); }
                else if (z.Key == "time") StartEdit("time", new Rectangle(z.Value.X, z.Value.Y, 84, z.Value.Height));
                else if (z.Key == "alt") StartEdit("alt", new Rectangle(z.Value.X, z.Value.Y, 84, z.Value.Height));
                else if (z.Key == "spd") StartEdit("speed", new Rectangle(z.Value.X, z.Value.Y, 84, z.Value.Height));
                Invalidate();
                return;
            }
        }

        private static int RowHeight(string value)
        {
            int lines = 1;
            foreach (char ch in value ?? "") if (ch == '\n') lines++;
            return lines * LineH + RowPad;
        }

        private static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var gp = new GraphicsPath();
            float d = rad * 2;
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);

            var box = new RectangleF(1, 1, Width - 3, Height - 3);
            using (GraphicsPath gp = Rounded(box, 8))
            {
                using (var fill = new SolidBrush(Color.FromArgb(250, 251, 253)))
                    g.FillPath(fill, gp);

                // Bande de couleur à gauche, découpée dans la forme arrondie
                var state = g.Save();
                g.SetClip(gp);
                using (var accent = new SolidBrush(_accent))
                    g.FillRectangle(accent, box.X, box.Y, 6, box.Height);
                g.Restore(state);

                using (var border = new Pen(Color.FromArgb(214, 219, 228), 1f))
                    g.DrawPath(border, gp);
            }

            float x = 6 + Pad;
            float w = Width - x - Pad;
            var trim = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

            using (var tb = new SolidBrush(Color.FromArgb(30, 36, 48)))
                g.DrawString(_title, _fTitle, tb, new RectangleF(x, 10, w, 26), trim);
            using (var sb = new SolidBrush(Color.FromArgb(110, 118, 132)))
                g.DrawString(_subtitle, _fSub, sb, new RectangleF(x, 34, w, 20), trim);

            float y = HeaderH;
            using (var sep = new Pen(Color.FromArgb(226, 230, 238), 1f))
                g.DrawLine(sep, x, y - 2, Width - Pad, y - 2);

            _hits.Clear();
            bool embedShown = false;
            x = RowX;
            w = Width - x - Pad;
            float labelW = Math.Min(110f, w * 0.34f);
            using (var lb = new SolidBrush(Color.FromArgb(120, 128, 142)))
            using (var vb = new SolidBrush(Color.FromArgb(34, 40, 54)))
            using (var line = new Pen(Color.FromArgb(238, 241, 246), 1f))
            using (var dot = new Pen(Color.FromArgb(150, 160, 180), 1f) { DashStyle = DashStyle.Dot })
            using (var symF = new Font("Segoe UI Symbol", 9f))
            {
                foreach (KeyValuePair<string, string> r in _rows)
                {
                    int rh = RowHeight(r.Value);
                    float vx = x + labelW;
                    float vw = w - labelW;
                    string key = Editable ? (r.Key == "Time" ? "time" : r.Key == "Altitude" ? "alt" : r.Key == "Speed" ? "spd" : "") : "";
                    bool hasLock = key == "time" || key == "spd";

                    if (hasLock)
                    {
                        // Case DCS à droite de la ligne : "locked" + carré coché ou non
                        bool on = key == "time" ? LockEta : LockSpeed;
                        var cb = new Rectangle((int)(Width - Pad - 62), (int)y + 3, 62, LineH);
                        var sq = new Rectangle(cb.X, cb.Y + 2, 13, 13);
                        g.SmoothingMode = SmoothingMode.None;
                        g.FillRectangle(on ? Brushes.SteelBlue : Brushes.White, sq);
                        g.DrawRectangle(Pens.Gray, sq);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        if (on) TextRenderer.DrawText(g, "\u2713", symF, new Rectangle(sq.X - 1, sq.Y - 1, 15, 15), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        g.DrawString("locked", _fLabel, lb, cb.X + 16, y + 4);
                        _hits.Add(new KeyValuePair<string, Rectangle>(key == "time" ? "eta" : "speed", cb));
                        vw -= 66;
                    }
                    if (key == "time")
                    {
                        // Cadenas de la carte, dans la marge à gauche de Time
                        var pr = new Rectangle((int)(x - 14), (int)y + 2, 16, LineH + 2);
                        using (var pf = new Font("Segoe UI Emoji", 9f))
                            TextRenderer.DrawText(g, LockTot ? "\U0001F512" : "\U0001F513", pf, pr, LockTot ? (LockEnabled ? Color.FromArgb(30, 36, 48) : Color.FromArgb(120, 125, 135)) : Color.FromArgb(185, 190, 200), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        _hits.Add(new KeyValuePair<string, Rectangle>("tot", pr));
                    }

                    g.DrawString(r.Key, _fLabel, lb, new RectangleF(x, y + 4, labelW, LineH), trim);
                    if (Embedded != null && Editable && r.Key == EmbeddedKey)
                    {
                        // La liste prend la place de la valeur
                        embedShown = true;
                        var eb = new Rectangle((int)vx - 3, (int)y + Math.Max(0, (rh - Embedded.Height) / 2), (int)Math.Min(vw, 230), Embedded.Height);
                        if (Embedded.Bounds != eb) Embedded.Bounds = eb;
                    }
                    else
                        g.DrawString(r.Value ?? "", _fValue, vb, new RectangleF(vx, y + 3, vw, rh), new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
                    if (key != "")
                    {
                        // Valeur modifiable : zone cliquable, soulignée en pointillés
                        var vr = new Rectangle((int)vx, (int)y + 2, (int)Math.Min(vw, 150), LineH + 2);
                        _hits.Add(new KeyValuePair<string, Rectangle>(key, vr));
                        g.DrawLine(dot, vr.X, y + LineH + 3, vr.X + Math.Min(vr.Width, TextRenderer.MeasureText((r.Value ?? "").Split('\n')[0], _fValue).Width), y + LineH + 3);
                    }
                    y += rh;
                    g.DrawLine(line, x, y, Width - Pad, y);
                }
            }
            if (Embedded != null && Embedded.Visible != embedShown) Embedded.Visible = embedShown;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fTitle.Dispose(); _fSub.Dispose(); _fLabel.Dispose(); _fValue.Dispose(); _box.Dispose(); _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Bouton qui montre / cache la colonne de gauche ou de droite d'une carte : un petit écran avec la colonne dessinée pleine (visible) ou vide (cachée).
    // Les trois cartes (Map, mission, événements) ont les deux mêmes boutons, aux mêmes endroits : tout à gauche et tout à droite de la barre du haut.
    internal class Map_UI_SideToggle : CheckBox
    {
        public bool LeftSide;

        public Map_UI_SideToggle(bool leftSide)
        {
            LeftSide = leftSide;
            Appearance = Appearance.Button;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Size = new Size(34, 34);
            Checked = true;
            TabStop = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);

            Color body = Checked ? Color.FromArgb(40, 110, 200) : Color.FromArgb(160, 163, 172);
            var r = new RectangleF(4.5f, 8.5f, Width - 10f, Height - 17f);
            float w = r.Width * 0.38f;
            RectangleF col = LeftSide ? new RectangleF(r.X, r.Y, w, r.Height) : new RectangleF(r.Right - w, r.Y, w, r.Height);

            if (Checked) using (var br = new SolidBrush(body)) g.FillRectangle(br, col);          // colonne visible : pleine
            using (var pen = new Pen(body, 1.8f)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            using (var pen = new Pen(body, 1.2f))
            {
                float lx = LeftSide ? col.Right : col.X;
                g.DrawLine(pen, lx, r.Y, lx, r.Bottom);
            }
        }

        // Barre du haut commune : le bouton de gauche tout à gauche, celui de droite tout à droite, le reste (inner) au milieu.
        // outer = la barre à ajouter à la fenêtre (Dock Top) ; inner = là où la fenêtre pose ses propres contrôles.
        public static Panel Wrap(int height, out Panel inner, out Map_UI_SideToggle left, out Map_UI_SideToggle right)
        {
            var outer = new Panel { Dock = DockStyle.Top, Height = height };
            var pl = new Panel { Dock = DockStyle.Left, Width = 40 };
            var pr = new Panel { Dock = DockStyle.Right, Width = 40 };
            inner = new Panel { Dock = DockStyle.Fill };
            left = new Map_UI_SideToggle(true) { Location = new Point(3, 2) };
            right = new Map_UI_SideToggle(false) { Location = new Point(3, 2) };
            pl.Controls.Add(left);
            pr.Controls.Add(right);
            outer.Controls.Add(inner);          // Fill d'abord, puis les deux côtés (le dernier ajouté s'accroche en premier)
            outer.Controls.Add(pl);
            outer.Controls.Add(pr);
            return outer;
        }
    }

    // Liste des waypoints d'un vol : marqueur (même forme que sur la carte), numéro, nom, altitude, heure
    internal class Map_UI_WaypointList : ComboBox
    {
        public Map_Flight Flight;
        public double MissionStart;

        private readonly Font _small = new Font("Segoe UI", 8.5f);

        public Map_UI_WaypointList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            ItemHeight = 20;
            IntegralHeight = false;
            DropDownHeight = 320;
        }

        public void Fill(Map_Flight f, double missionStart)
        {
            Flight = f;
            MissionStart = missionStart;
            BeginUpdate();
            Items.Clear();
            if (f != null)
            {
                f.Compute();
                foreach (Map_Waypoint w in f.Waypoints) Items.Add(w);
            }
            EndUpdate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0)
            {
                TextRenderer.DrawText(e.Graphics, "Pick a waypoint...", _small, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                    SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                return;
            }
            var w = e.Index < Items.Count ? Items[e.Index] as Map_Waypoint : null;
            if (w == null || Flight == null) return;

            Graphics g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Color fore = sel ? SystemColors.HighlightText : SystemColors.ControlText;
            int cy = e.Bounds.Y + e.Bounds.Height / 2;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Map_UI_View.DrawMarker(g, new PointF(e.Bounds.X + 11, cy), w.Role, Flight.Color, 4.5f, 255);
            g.SmoothingMode = old;

            string time = e.Index < Flight.Times.Length ? Map_Flights.Clock(MissionStart, Flight.Times[e.Index]) : "";
            string alt = w.Alt > 0 ? Map_Units.Alt(w.Alt) : "";
            string spd = w.Speed > 0 ? Map_Units.Speed(w.Speed) : "";
            const TextFormatFlags fl = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

            int right = e.Bounds.Right - 4;
            TextRenderer.DrawText(g, time, _small, new Rectangle(right - 62, e.Bounds.Y, 62, e.Bounds.Height), fore, fl | TextFormatFlags.Right);
            TextRenderer.DrawText(g, spd, _small, new Rectangle(right - 62 - 62, e.Bounds.Y, 62, e.Bounds.Height), fore, fl | TextFormatFlags.Right);
            TextRenderer.DrawText(g, alt, _small, new Rectangle(right - 62 - 62 - 56, e.Bounds.Y, 56, e.Bounds.Height), fore, fl | TextFormatFlags.Right);
            TextRenderer.DrawText(g, e.Index.ToString(), _small, new Rectangle(e.Bounds.X + 22, e.Bounds.Y, 22, e.Bounds.Height), fore, fl);
            int nameW = Math.Max(20, right - 62 - 62 - 56 - (e.Bounds.X + 46) - 4);
            TextRenderer.DrawText(g, w.ShortName == "" ? "-" : w.ShortName, _small, new Rectangle(e.Bounds.X + 46, e.Bounds.Y, nameW, e.Bounds.Height), fore, fl);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _small.Dispose();
            base.Dispose(disposing);
        }
    }

    // Liste des vols : un carré de la couleur du vol (la même que sur la carte) + le nom
    internal class Map_UI_FlightList : ListBox
    {
        public Map_UI_FlightList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            SelectionMode = SelectionMode.MultiExtended;        // CTRL / MAJ + clic : plusieurs vols
            ItemHeight = 24;
            IntegralHeight = false;
            BorderStyle = BorderStyle.FixedSingle;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= Items.Count) return;

            var f = Items[e.Index] as Map_Flight;
            if (f == null) return;

            Graphics g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;

            var sw = new Rectangle(e.Bounds.X + 6, e.Bounds.Y + (e.Bounds.Height - 12) / 2, 12, 12);
            using (var b = new SolidBrush(f.Color))
                g.FillRectangle(b, sw);
            g.DrawRectangle(Pens.Black, sw);

            Font font = f.IsPlayer ? new Font(Font, FontStyle.Bold) : Font;
            string text = f.Title + (f.IsPlayer ? "   (player)" : "");
            TextRenderer.DrawText(g, text, font, new Rectangle(e.Bounds.X + 26, e.Bounds.Y, e.Bounds.Width - 28, e.Bounds.Height),
                sel ? SystemColors.HighlightText : SystemColors.ControlText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
            if (f.IsPlayer) font.Dispose();
        }
    }
}
