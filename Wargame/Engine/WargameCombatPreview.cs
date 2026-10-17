using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DCE_Manager
{
    // Simulation "à blanc" du combat : rejoue des tours (pertes simulées -> combat -> ravitaillement,
    // dans le même ordre que WargameEngineLosses) sur une COPIE des zones, avec un peu de hasard.
    // Rien n'est écrit sur le disque.
    //
    // Deux usages :
    //  - Run(...)                : N tours x R passes d'un coup, rapport texte avec des pourcentages
    //  - WargameCombatSession    : un tour à la fois, pour le suivre sur la carte (Preview_Form)
    internal static class WargameCombatPreview
    {
        public static string Run(
            List<WargameZoneData> zones,
            List<WargameAxis> axes,
            HashSet<string> activeFlags,
            WargameTemplateCatalog catalog,
            WargameCampaignInfo info,
            int turns, int runs, double variance, double blueLossPercent, double redLossPercent,
            string airSide, double airBonus, double retreatLossPercent, int maxPerZone, double disbandPercent)
        {
            // Copie des réglages de la campagne : on change le hasard et on coupe le journal
            // (sinon 1000 combats = des milliers de lignes dans log.txt)
            WargameCombatConfig config = info.CombatConfig.Clone();
            config.RandomVariance = variance;
            config.AirSuperioritySide = airSide;
            config.AirSuperiorityBonus = airBonus;
            config.RetreatLossFraction = retreatLossPercent / 100.0;
            config.MaxFormationsPerZone = maxPerZone;
            config.DisbandBelowFraction = disbandPercent / 100.0;
            config.Quiet = true;

            var rng = new Random();
            Dictionary<string, List<string>> links = WargameEngineCombat.BuildLinks(zones);

            var startControl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in zones)
                startControl[z.Id] = z.Control;

            double startBlue = SideForce(zones, WargameSide.Blue);
            double startRed = SideForce(zones, WargameSide.Red);

            var endBlue = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var endRed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var changedRuns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var reachedTurns = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            double sumLastChange = 0, sumBlueForce = 0, sumRedForce = 0;

            for (int run = 0; run < runs; run++)
            {
                List<WargameZoneData> work = CloneZones(zones);
                var byId = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
                foreach (WargameZoneData z in work)
                    byId[z.Id] = z;

                var reachedThisRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int lastChange = 0;

                for (int turn = 1; turn <= turns; turn++)
                {
                    ApplyAttrition(work, byId, links, blueLossPercent, redLossPercent, rng, catalog);

                    WargameCombatResult result = WargameEngineCombat.Resolve(work, axes, activeFlags, config, rng, catalog);
                    WargameEngineCombat.RemoveBrokenFormations(work, catalog, config, result);
                    WargameEngineCombat.ApplySupply(work, catalog, config, result);

                    if (result.FlippedZones.Count > 0 || result.MovedFormationIds.Count > 0)
                        lastChange = turn;

                    foreach (string flag in result.ReachedFlags)
                    {
                        if (!reachedThisRun.Add(flag)) continue;

                        if (!reachedTurns.ContainsKey(flag)) reachedTurns[flag] = new List<int>();
                        reachedTurns[flag].Add(turn);
                    }
                }

                sumLastChange += lastChange;
                sumBlueForce += SideForce(work, WargameSide.Blue);
                sumRedForce += SideForce(work, WargameSide.Red);

                foreach (WargameZoneData z in work)
                {
                    if (z.Control == WargameSide.Blue) Add(endBlue, z.Id);
                    else if (z.Control == WargameSide.Red) Add(endRed, z.Id);

                    if (!string.Equals(z.Control, startControl[z.Id], StringComparison.OrdinalIgnoreCase))
                        Add(changedRuns, z.Id);
                }
            }

            return BuildReport(zones, axes, info, turns, runs, variance, blueLossPercent, redLossPercent,
                startControl, endBlue, endRed, changedRuns, reachedTurns,
                sumLastChange / runs, startBlue, startRed, sumBlueForce / runs, sumRedForce / runs);
        }

        private static string BuildReport(
            List<WargameZoneData> zones, List<WargameAxis> axes, WargameCampaignInfo info,
            int turns, int runs, double variance, double blueLossPercent, double redLossPercent,
            Dictionary<string, string> startControl,
            Dictionary<string, int> endBlue, Dictionary<string, int> endRed,
            Dictionary<string, int> changedRuns, Dictionary<string, List<int>> reachedTurns,
            double avgLastChange, double startBlue, double startRed, double endBlueForce, double endRedForce)
        {
            var sb = new StringBuilder();

            sb.AppendLine("PREVIEW - nothing was written to disk");
            sb.AppendLine(turns + " turn(s) x " + runs + " run(s), dice variance " + (variance * 100).ToString("0") + "%, "
                + "losses per turn (% of one posted copy): " + info.BlueLabel + " " + blueLossPercent.ToString("0") + "% / " + info.RedLabel + " " + redLossPercent.ToString("0") + "%");
            sb.AppendLine();

            int blueZones = zones.Count(z => z.Control == WargameSide.Blue);
            int redZones = zones.Count(z => z.Control == WargameSide.Red);
            sb.AppendLine("Start: " + info.BlueLabel + " " + blueZones + " zone(s), " + info.RedLabel + " " + redZones + " zone(s)");
            sb.AppendLine();

            // Zones qui ont changé de main au moins une fois (dans au moins une passe)
            List<WargameZoneData> moved = zones
                .Where(z => changedRuns.ContainsKey(z.Id))
                .OrderByDescending(z => changedRuns[z.Id])
                .ThenBy(z => z.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (moved.Count == 0)
            {
                sb.AppendLine("No zone changed hands.");
                sb.AppendLine("(Check that at least one axis has a flag, and that its start zone is held by blue or red.)");
            }
            else
            {
                sb.AppendLine("Zones that changed hands (final result):");
                foreach (WargameZoneData z in moved)
                {
                    double blue = Percent(endBlue, z.Id, runs);
                    double red = Percent(endRed, z.Id, runs);

                    sb.AppendLine("  " + z.Id.PadRight(14)
                        + "was " + info.GetLabel(startControl[z.Id]).PadRight(10)
                        + "-> " + info.BlueLabel + " " + blue.ToString("0").PadLeft(3) + "%  /  "
                        + info.RedLabel + " " + red.ToString("0").PadLeft(3) + "%");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Axis objectives:");

            bool anyAxis = false;
            foreach (WargameAxis axis in axes)
            {
                if (string.IsNullOrWhiteSpace(axis.ActivationFlag)) continue;

                anyAxis = true;
                List<int> turnsList;
                if (reachedTurns.TryGetValue(axis.ObjectiveFlag, out turnsList))
                {
                    sb.AppendLine("  " + axis.Name + " : reached in " + turnsList.Count + "/" + runs
                        + " run(s), average turn " + turnsList.Average().ToString("0.0"));
                }
                else
                {
                    sb.AppendLine("  " + axis.Name + " : never reached");
                }
            }

            if (!anyAxis)
                sb.AppendLine("  (no axis with a flag)");

            sb.AppendLine();
            sb.AppendLine("Front last moved around turn " + avgLastChange.ToString("0.0") + " (average)"
                + (avgLastChange >= turns - 0.5 ? " - still moving at the end, try more turns" : " - then it was stable"));
            sb.AppendLine();
            sb.AppendLine("Total force, start -> end (average):");
            sb.AppendLine("  " + info.BlueLabel + " : " + startBlue.ToString("0") + " -> " + endBlueForce.ToString("0"));
            sb.AppendLine("  " + info.RedLabel + " : " + startRed.ToString("0") + " -> " + endRedForce.ToString("0"));

            return sb.ToString();
        }

        // ---- Outils (partagés avec WargameCombatSession) ----

        private static void Add(Dictionary<string, int> counter, string key)
        {
            int value;
            counter.TryGetValue(key, out value);
            counter[key] = value + 1;
        }

        private static double Percent(Dictionary<string, int> counter, string key, int runs)
        {
            int value;
            counter.TryGetValue(key, out value);
            return 100.0 * value / runs;
        }

        internal static double SideForce(List<WargameZoneData> zones, string side)
        {
            double total = 0;

            foreach (WargameZoneData z in zones)
            {
                foreach (WargameFormation f in z.Formations)
                {
                    if (string.Equals(f.Side, side, StringComparison.OrdinalIgnoreCase))
                        total += f.ForcePower;
                }
            }

            return total;
        }

        // Pertes simulées : elles imitent les missions du joueur, avec la même loi que la production
        // (WargameEngineLosses) : chaque formation de première ligne perd, à chaque tour, une part d'UN SEUL
        // exemplaire posé de son template (jamais plus : une formation x5 ne perd pas 5 exemplaires d'un coup).
        // Le pourcentage est PAR CAMP (celui qui a la maîtrise du ciel perd peu) : 100 = l'exemplaire est
        // entièrement détruit, 20 = un cinquième de l'exemplaire. Tirage entre 0.5 et 1.5 fois la valeur demandée.
        internal static void ApplyAttrition(
            List<WargameZoneData> zones, Dictionary<string, WargameZoneData> byId,
            Dictionary<string, List<string>> links, double bluePercent, double redPercent, Random rng,
            WargameTemplateCatalog catalog)
        {
            foreach (WargameZoneData zone in zones)
            {
                if (!WargameEngineCombat.IsFightingSide(zone.Control)) continue;

                double percent = string.Equals(zone.Control, WargameSide.Blue, StringComparison.OrdinalIgnoreCase)
                    ? bluePercent : redPercent;
                if (percent <= 0) continue;

                bool isFront = links[zone.Id].Any(id => WargameEngineCombat.IsFightingSide(byId[id].Control)
                    && !string.Equals(byId[id].Control, zone.Control, StringComparison.OrdinalIgnoreCase));

                if (!isFront) continue;

                foreach (WargameFormation f in zone.Formations)
                {
                    if (f.ForcePower <= 0) continue;
                    if (!string.Equals(f.Side, zone.Control, StringComparison.OrdinalIgnoreCase)) continue;

                    // Force d'UN exemplaire posé (10 si le template est inconnu, comme en production)
                    double exemplar = catalog != null ? catalog.GetPower(f.Template) : 0;
                    if (exemplar <= 0) exemplar = 10.0;

                    double posted = Math.Min(f.ForcePower, exemplar);
                    double share = Math.Min(1.0, percent / 100.0 * (0.5 + rng.NextDouble()));
                    f.ForcePower = Math.Max(0, f.ForcePower - posted * share);
                }
            }
        }

        // Copie des zones et de leurs formations (la géométrie est partagée : elle n'est jamais modifiée)
        internal static List<WargameZoneData> CloneZones(List<WargameZoneData> zones)
        {
            var copy = new List<WargameZoneData>();

            foreach (WargameZoneData z in zones)
            {
                var c = new WargameZoneData
                {
                    Id = z.Id,
                    Shape = z.Shape,
                    Center = z.Center,
                    Radius = z.Radius,
                    DcsPoints = z.DcsPoints,
                    Control = z.Control,
                    ResupplyModifier = z.ResupplyModifier,
                    Neighbors = new List<string>(z.Neighbors),
                    SupplySource = z.SupplySource,
                    Terrain = z.Terrain,
                    SupplyRoute = z.SupplyRoute,
                };

                foreach (WargameFormation f in z.Formations)
                {
                    c.Formations.Add(new WargameFormation
                    {
                        FormationId = f.FormationId,
                        Name = f.Name,
                        Template = f.Template,
                        Side = f.Side,
                        Multiplier = f.Multiplier,
                        ForcePower = f.ForcePower,
                        SpawnGeneration = f.SpawnGeneration,
                        Priority = f.Priority,
                    });
                }

                copy.Add(c);
            }

            return copy;
        }
    }

    // Une partie jouée UN TOUR A LA FOIS sur une copie des zones. La Form la montre sur la carte :
    // Zones = les copies (c'est elles que la carte dessine), Step() joue un tour et renvoie le
    // journal du tour (zones prises, formations déplacées, objectifs atteints).
    internal class WargameCombatSession
    {
        public readonly List<WargameZoneData> Zones;

        public int Turn;

        // Modifiables en cours de partie : relus à chaque tour
        public double Variance;
        public double BlueLossPercent;
        public double RedLossPercent;

        // Supériorité aérienne ("blue", "red" ou "" = aucune) et son bonus (0.3 = +30 % de force au combat)
        public string AirSide = "";
        public double AirBonus = 0.3;

        // Perte de force d'une formation à chaque repli (en %) et nombre normal de formations par zone
        public double RetreatLossPercent = 5;
        public int MaxPerZone = 3;

        // Une formation sous ce % de sa force nominale est dissoute
        public double DisbandPercent = 25;

        // Zones qui ont changé de main au dernier tour (la carte les entoure)
        public readonly HashSet<string> LastFlippedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Nombre de tours de suite sans aucun mouvement (sert à arrêter la lecture auto)
        public int QuietTurns;

        private readonly Dictionary<string, WargameZoneData> _byId = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _links;
        private readonly List<WargameAxis> _axes;
        private readonly HashSet<string> _flags;
        private readonly WargameTemplateCatalog _catalog;
        private readonly WargameCampaignInfo _info;
        private readonly WargameCombatConfig _config;
        private readonly Random _rng = new Random();
        private readonly HashSet<string> _reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Lignes de jugement envoyées par le moteur pendant le tour (voir WargameCombatConfig.Trace)
        private readonly List<string> _traceLines = new List<string>();

        // Journal COMPLET (fichier texte) : en-tête, état de la carte, événements du moteur dans l'ordre,
        // et le détail de chaque formation à chaque tour. Voir WriteHeader et SaveFullLog.
        public readonly List<string> FullLog = new List<string>();

        // Tout ce que le moteur dit pendant un tour, dans l'ordre (Trace + Detail)
        private readonly List<string> _timeline = new List<string>();

        public WargameCombatSession(
            List<WargameZoneData> zones, List<WargameAxis> axes, HashSet<string> activeFlags,
            WargameTemplateCatalog catalog, WargameCampaignInfo info)
        {
            Zones = WargameCombatPreview.CloneZones(zones);
            foreach (WargameZoneData z in Zones)
                _byId[z.Id] = z;

            _axes = axes;
            _flags = activeFlags;
            _catalog = catalog;
            _info = info;

            _config = info.CombatConfig.Clone();
            _config.Quiet = true;
            _config.Trace = line => { _traceLines.Add(line); _timeline.Add(line); };
            _config.Detail = line => _timeline.Add(line);

            _links =WargameEngineCombat.BuildLinks(Zones);
        }

        // Force de chaque formation, par identifiant (pour savoir ce qui l'a fait varier pendant un tour)
        private Dictionary<int, double> SnapshotPower()
        {
            var snap = new Dictionary<int, double>();
            foreach (WargameZoneData z in Zones)
                foreach (WargameFormation f in z.Formations)
                    snap[f.FormationId] = f.ForcePower;
            return snap;
        }

        // Joue un tour. Renvoie les lignes du journal (le journal complet est dans FullLog).
        public List<string> Step()
        {
            var log = new List<string>();

            Turn++;
            log.Add("--- Turn " + Turn + " ---");

            // Etat avant : camp de chaque zone, zone de chaque formation
            var controlBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var zoneBefore = new Dictionary<int, string>();

            foreach (WargameZoneData z in Zones)
            {
                controlBefore[z.Id] = z.Control;
                foreach (WargameFormation f in z.Formations)
                    zoneBefore[f.FormationId] = z.Id;
            }

            _config.RandomVariance = Variance;
            _config.AirSuperioritySide = AirSide;
            _config.AirSuperiorityBonus = AirBonus;
            _config.RetreatLossFraction = RetreatLossPercent / 100.0;
            _config.MaxFormationsPerZone = MaxPerZone;
            _config.DisbandBelowFraction = DisbandPercent / 100.0;

            FullLog.Add("");
            FullLog.Add("==================== TURN " + Turn + " ====================");
            FullLog.Add("Settings this turn: luck " + (Variance * 100).ToString("0") + "% | test losses (% of one posted copy) blue " + BlueLossPercent.ToString("0.#")
                + "% / red " + RedLossPercent.ToString("0.#") + "% | air " + (string.IsNullOrEmpty(AirSide) ? "none" : AirSide + " +" + (AirBonus * 100).ToString("0") + "%")
                + " | retreat loss " + RetreatLossPercent.ToString("0.#") + "% | formations per zone " + MaxPerZone + " | disband below " + DisbandPercent.ToString("0.#") + "%");
            FullLog.Add("");
            FullLog.Add("-- State at the start of the turn --");
            FullLog.AddRange(WargameEngineCombat.DescribeState(Zones, _catalog, _config));

            Dictionary<int, double> power0 = SnapshotPower();

            WargameCombatPreview.ApplyAttrition(Zones, _byId, _links, BlueLossPercent, RedLossPercent, _rng, _catalog);
            Dictionary<int, double> power1 = SnapshotPower();

            _traceLines.Clear();
            _timeline.Clear();
            WargameCombatResult result = WargameEngineCombat.Resolve(Zones, _axes, _flags, _config, _rng, _catalog);
            WargameEngineCombat.RemoveBrokenFormations(Zones, _catalog, _config, result);
            Dictionary<int, double> power2 = SnapshotPower();

            WargameEngineCombat.ApplySupply(Zones, _catalog, _config, result);
            Dictionary<int, double> power3 = SnapshotPower();

            // Pourquoi ça bouge (ou pas) : le jugement de chaque combat, tel que le moteur l'a fait
            if (_traceLines.Count == 0)
                log.Add("  no fight judged: no active axis has a blue/red front (check axis flags and zones)");
            else
                log.AddRange(_traceLines);

            // Zones qui ont changé de main
            LastFlippedIds.Clear();
            foreach (WargameZoneData z in Zones)
            {
                if (string.Equals(z.Control, controlBefore[z.Id], StringComparison.OrdinalIgnoreCase))
                    continue;

                LastFlippedIds.Add(z.Id);
                log.Add("  ZONE " + z.Id + " : " + _info.GetLabel(controlBefore[z.Id]) + " -> " + _info.GetLabel(z.Control));
            }

            // Formations déplacées (avance après conquête, repli, redressement du front)
            int moves = 0;
            var kindById = new Dictionary<int, string>();
            foreach (WargameZoneData z in Zones)
            {
                foreach (WargameFormation f in z.Formations)
                {
                    string from;
                    if (!zoneBefore.TryGetValue(f.FormationId, out from)) continue;
                    if (string.Equals(from, z.Id, StringComparison.OrdinalIgnoreCase)) continue;

                    bool advance = LastFlippedIds.Contains(z.Id)
                        && string.Equals(f.Side, z.Control, StringComparison.OrdinalIgnoreCase);

                    string kind = result.ReinforcedFormationIds.Contains(f.FormationId) ? "reinforce "
                        : advance ? "advance " : "retreat ";

                    kindById[f.FormationId] = kind.Trim();
                    log.Add("  " + kind + f.Name + " : " + from + " -> " + z.Id);
                    moves++;
                }
            }

            if (result.Destroyed > 0)
                log.Add("  " + result.Destroyed + " formation(s) lost (no room to retreat)");

            foreach (string note in result.DisbandedNotes)
                log.Add("  disbanded " + note);

            // Objectifs d'axe atteints
            bool newObjective = false;
            foreach (string flag in result.ReachedFlags)
            {
                if (!_reached.Add(flag)) continue;

                WargameAxis axis = _axes.FirstOrDefault(a => string.Equals(a.ObjectiveFlag, flag, StringComparison.OrdinalIgnoreCase));
                log.Add("  OBJECTIVE REACHED : " + (axis != null ? axis.Name : flag) + "  (flag " + flag + ")");
                newObjective = true;
            }

            bool changed = LastFlippedIds.Count > 0 || moves > 0 || result.Destroyed > 0 || result.Disbanded > 0 || newObjective;

            if (!changed)
                log.Add("  nothing moved");

            QuietTurns = changed ? 0 : QuietTurns + 1;

            // ---- Journal complet ----

            FullLog.Add("");
            FullLog.Add("-- Test losses applied by the preview (these stand for the player's missions) --");
            int testLines = 0;
            foreach (KeyValuePair<int, double> kv in power1)
            {
                double before = power0[kv.Key];
                if (before - kv.Value < 0.05) continue;
                FullLog.Add("  " + FormationName(kv.Key) + " : " + before.ToString("0.0") + " -> " + kv.Value.ToString("0.0"));
                testLines++;
            }
            if (testLines == 0) FullLog.Add("  none");

            FullLog.Add("");
            FullLog.Add("-- Engine events, in the order they happened --");
            if (_timeline.Count == 0) FullLog.Add("  (nothing)");
            else FullLog.AddRange(_timeline);

            FullLog.Add("");
            FullLog.Add("-- Formation ledger (only formations that moved or changed strength) --");
            FullLog.Add("   start | after test losses | after combat+retreat+wear+disband | after supply = end / nominal (%)");

            int ledger = 0;
            foreach (WargameZoneData z in Zones)
            {
                foreach (WargameFormation f in z.Formations)
                {
                    string from;
                    zoneBefore.TryGetValue(f.FormationId, out from);
                    bool movedZone = from != null && !string.Equals(from, z.Id, StringComparison.OrdinalIgnoreCase);

                    double p0, p1, p2, p3;
                    if (!power0.TryGetValue(f.FormationId, out p0)) continue;
                    p1 = power1[f.FormationId]; p2 = power2[f.FormationId]; p3 = power3[f.FormationId];

                    bool changedPower = Math.Abs(p3 - p0) >= 0.05 || Math.Abs(p1 - p0) >= 0.05 || Math.Abs(p2 - p1) >= 0.05;
                    if (!movedZone && !changedPower) continue;

                    double nominal = f.GetNominalForcePower(_catalog);
                    string kind;
                    var tags = new List<string>();
                    if (kindById.TryGetValue(f.FormationId, out kind)) tags.Add(kind);
                    if (result.EngagedFormationIds.Contains(f.FormationId)) tags.Add("engaged");
                    if (p0 > 0 && p2 <= 0) tags.Add(result.DisbandedNotes.Any(n => n.StartsWith(f.Name + " ")) ? "DISBANDED" : "LOST");

                    FullLog.Add("  " + f.Name + " (" + f.Side + ") " + (movedZone ? from + " -> " + z.Id : "stays in " + z.Id) + " | "
                        + p0.ToString("0.0") + " | " + p1.ToString("0.0") + " | " + p2.ToString("0.0") + " | " + p3.ToString("0.0")
                        + " / " + nominal.ToString("0.0") + (nominal > 0 ? " (" + (100.0 * p3 / nominal).ToString("0") + "%)" : "")
                        + (tags.Count > 0 ? "  [" + string.Join(", ", tags) + "]" : ""));
                    ledger++;
                }
            }
            if (ledger == 0) FullLog.Add("  none");

            FullLog.Add("");
            FullLog.Add("-- Turn summary --");
            FullLog.Add("  zones changed hands: " + (LastFlippedIds.Count == 0 ? "none" : string.Join(", ", LastFlippedIds.OrderBy(x => x)))
                + " | moves " + moves + " | advances " + result.Advanced + " | worn " + result.Worn + " | lost " + result.Destroyed
                + " | disbanded " + result.Disbanded + " | objectives " + (result.ReachedFlags.Count == 0 ? "none" : string.Join(", ", result.ReachedFlags)));
            FullLog.Add("  force blue " + WargameCombatPreview.SideForce(Zones, WargameSide.Blue).ToString("0.0")
                + " / red " + WargameCombatPreview.SideForce(Zones, WargameSide.Red).ToString("0.0")
                + " | zones blue " + Zones.Count(z => z.Control == WargameSide.Blue) + " / red " + Zones.Count(z => z.Control == WargameSide.Red));

            return log;
        }

        // Nom d'une formation à partir de son identifiant
        private string FormationName(int id)
        {
            foreach (WargameZoneData z in Zones)
                foreach (WargameFormation f in z.Formations)
                    if (f.FormationId == id) return f.Name + " (" + f.Side + ", " + z.Id + ")";
            return "#" + id;
        }

        // En-tête du journal complet : comment le lire, tous les réglages, les axes, l'état de départ.
        // A appeler une fois, quand les réglages de test sont posés (juste après la création de la session).
        public void WriteHeader(string campaignName)
        {
            FullLog.Clear();

            FullLog.Add("WARGAME PREVIEW - FULL LOG");
            FullLog.Add("Campaign: " + campaignName + " | created " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            FullLog.Add("Nothing here was written to the campaign: this is a simulation on a copy.");
            FullLog.Add("");
            FullLog.Add("HOW TO READ THIS LOG");
            FullLog.Add("  Order of a turn: test losses (stand for player missions) -> judge each fight on active axes -> wear -> flips/retreats/advances");
            FullLog.Add("                   -> front alignment -> reinforcements -> empty zones follow influence -> disband weak formations in contact -> supply.");
            FullLog.Add("  Judgement: attack = force x supply(route combat x) x air x luck ; defense = force x supply x terrain x air.");
            FullLog.Add("             delta = (attack - defense) / (attack + defense). The zone changes hands if |delta| > Conquest difficulty.");
            FullLog.Add("             delta < 0 means the DEFENDER counter-attacks and wins: the attacker's zone flips instead.");
            FullLog.Add("  'allocation Z (side)': how zone Z splits its formations between its fronts. [attack] = main force on the axis target,");
            FullLog.Add("             [guard, needs N] = a formation kept back to hold a flank against N of enemy threat. 'missing' = force it lacks.");
            FullLog.Add("  Zone tags: FRONT = touches an enemy zone | CUT-OFF = no road to a supply source | DCS = played as units in DCS");
            FullLog.Add("             SOURCE = supply source | EMPTY = no formation alive.");
            FullLog.Add("  Test losses: like the real campaign, a front-line formation loses at most ONE posted copy of its template per mission.");
            FullLog.Add("             The % is the share of that copy destroyed (100 = the whole copy, random between half and one and a half times the value).");
            FullLog.Add("  Formation ledger columns: strength at the start, after the preview's test losses, after combat/retreat/wear/disband, after supply.");
            FullLog.Add("");

            FullLog.Add("PRODUCTION SETTINGS (from camp_init, what the real campaign uses)");
            FullLog.AddRange(DescribeConfig(_info.CombatConfig));
            FullLog.Add("");

            FullLog.Add("TEST SETTINGS (preview only, they stand for the missions the player would fly)");
            FullLog.Add("  luck " + (Variance * 100).ToString("0") + "% | test losses (% of one posted copy) blue " + BlueLossPercent.ToString("0.#") + "% / red " + RedLossPercent.ToString("0.#")
                + "% | air " + (string.IsNullOrEmpty(AirSide) ? "none" : AirSide + " +" + (AirBonus * 100).ToString("0") + "%")
                + " | retreat loss " + RetreatLossPercent.ToString("0.#") + "% | formations per zone " + MaxPerZone + " | disband below " + DisbandPercent.ToString("0.#") + "%");
            FullLog.Add("  (these test values replace the production ones for variance, retreat loss, formations per zone, disband and air)");
            FullLog.Add("");

            FullLog.Add("AXES");
            foreach (WargameAxis a in _axes)
            {
                FullLog.Add("  " + a.Name + " : " + (a.IsActive(_flags) ? "ACTIVE" : "inactive") + " | flag '" + a.ActivationFlag + "' | objective flag '" + a.ObjectiveFlag
                    + "' | attacker " + (string.IsNullOrEmpty(a.AttackerSide) ? "(not set)" : a.AttackerSide)
                    + " | path " + (a.IsValid ? string.Join(" > ", a.Path) : "NO PATH"));
            }
            FullLog.Add("  active flags: " + (_flags.Count == 0 ? "none" : string.Join(", ", _flags.OrderBy(x => x))));
            FullLog.Add("");

            FullLog.Add("STATE AT START");
            FullLog.Add("  force blue " + WargameCombatPreview.SideForce(Zones, WargameSide.Blue).ToString("0.0")
                + " / red " + WargameCombatPreview.SideForce(Zones, WargameSide.Red).ToString("0.0")
                + " | zones blue " + Zones.Count(z => z.Control == WargameSide.Blue) + " / red " + Zones.Count(z => z.Control == WargameSide.Red));
            FullLog.AddRange(WargameEngineCombat.DescribeState(Zones, _catalog, _config));
        }

        private static List<string> DescribeConfig(WargameCombatConfig c)
        {
            var lines = new List<string>();
            lines.Add("  conquest difficulty (control_flip_threshold) " + c.ControlFlipThreshold.ToString("0.00") + " | luck " + c.RandomVariance.ToString("0.00")
                + " | retreat loss " + c.RetreatLossFraction.ToString("0.00") + " | wear in combat " + c.CombatAttrition.ToString("0.00")
                + " | straighten front " + c.FrontAlignmentEnabled);
            lines.Add("  formations per zone " + c.MaxFormationsPerZone + " (hard max " + c.MaxFormationsHard + ") | disband below " + c.DisbandBelowFraction.ToString("0.00")
                + " | reinforcement speed " + c.SupplyRecoveryRate.ToString("0.00") + " | front-line recovery x" + c.FrontLineRecoveryFactor.ToString("0.00")
                + " | engaged recovery x" + c.EngagedRecoveryFactor.ToString("0.00") + " | cut-off supply x" + c.CutOffSupplyFactor.ToString("0.00"));
            lines.Add("  reinforcements follow the front " + c.AxisReinforcement + " | empty zones follow influence " + c.EmptyZoneInfluence
                + " (decay " + c.InfluenceDecay.ToString("0.00") + ") | advance on conquest " + c.AdvanceOnConquest + " | zones played in DCS depth " + c.DcsZoneDepth);
            lines.Add("  terrain defense: " + string.Join(", ", c.TerrainDefense.OrderBy(k => k.Key).Select(k => k.Key + " x" + k.Value.ToString("0.00"))));
            lines.Add("  route recovery:  " + string.Join(", ", c.RouteRecovery.OrderBy(k => k.Key).Select(k => k.Key + " x" + k.Value.ToString("0.00"))));
            lines.Add("  route combat:    " + string.Join(", ", c.RouteCombat.OrderBy(k => k.Key).Select(k => k.Key + " x" + k.Value.ToString("0.00"))));
            return lines;
        }

        // Ecrit le journal complet dans un fichier texte (UTF-8)
        public void SaveFullLog(string path)
        {
            File.WriteAllLines(path, FullLog, new UTF8Encoding(false));
        }

        // Texte affiché sous le nom de chaque zone : force restante, puis % de la force nominale.
        // Une étoile marque les zones jouées dans DCS (voir WargameEngineCombat.FindDcsZones).
        public Dictionary<string, string> BuildCaptions()
        {
            var captions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> dcsZones = WargameEngineCombat.FindDcsZones(Zones, _config.DcsZoneDepth);

            foreach (WargameZoneData z in Zones)
            {
                if (z.Formations.Count == 0)
                {
                    captions[z.Id] = "empty";
                    continue;
                }

                double now = 0, nominal = 0;
                foreach (WargameFormation f in z.Formations)
                {
                    now += f.ForcePower;
                    nominal += f.GetNominalForcePower(_catalog);
                }

                captions[z.Id] = Math.Round(now).ToString("0")
                    + (nominal > 0 ? " (" + (100.0 * now / nominal).ToString("0") + "%)" : "")
                    + (dcsZones.Contains(z.Id) ? " *" : "");
            }

            return captions;
        }

        // Un point par zone occupée : le nombre de formations s'écrit dedans, la taille suit la force
        public Dictionary<string, WargameZoneDot> BuildDots()
        {
            var dots = new Dictionary<string, WargameZoneDot>(StringComparer.OrdinalIgnoreCase);

            foreach (WargameZoneData z in Zones)
            {
                int count = 0;
                double force = 0;

                foreach (WargameFormation f in z.Formations)
                {
                    if (f.ForcePower <= 0) continue;
                    count++;
                    force += f.ForcePower;
                }

                if (count == 0) continue;

                dots[z.Id] = new WargameZoneDot { Count = count, Force = force, Side = z.Control };
            }

            return dots;
        }

        public string BuildStatus(int maxTurns)
        {
            int blueZones = Zones.Count(z => z.Control == WargameSide.Blue);
            int redZones = Zones.Count(z => z.Control == WargameSide.Red);

            HashSet<string> dcsZones = WargameEngineCombat.FindDcsZones(Zones, _config.DcsZoneDepth);
            int inDcs = 0, total = 0;
            foreach (WargameZoneData z in Zones)
            {
                foreach (WargameFormation f in z.Formations)
                {
                    if (f.ForcePower <= 0) continue;
                    total++;
                    if (dcsZones.Contains(z.Id)) inDcs++;
                }
            }

            return "Turn " + Turn + " / " + maxTurns + "\r\n"
                + _info.BlueLabel + " : " + blueZones + " zone(s), force " + WargameCombatPreview.SideForce(Zones, WargameSide.Blue).ToString("0")
                + "   |   "
                + _info.RedLabel + " : " + redZones + " zone(s), force " + WargameCombatPreview.SideForce(Zones, WargameSide.Red).ToString("0")
                + "\r\nPlayed in DCS (*) : " + inDcs + " / " + total + " formation(s)";
        }
    }

    // Ce que la carte dessine dans une zone : un point (nombre de formations dedans, taille = force)
    internal class WargameZoneDot
    {
        public int Count;
        public double Force;
        public string Side;
    }

    // Fenêtre de la prévisualisation : la carte à gauche (tour par tour), les réglages et le
    // journal à droite. Un onglet garde le rapport texte "N parties d'un coup".
    internal class WargameCombatPreview_Form : Form
    {
        private readonly string _campaignName;
        private readonly List<WargameZoneData> _zones;
        private readonly List<WargameAxis> _axes;
        private readonly WargameTemplateCatalog _catalog;
        private readonly WargameCampaignInfo _info;
        private readonly Image _mapImage;
        private readonly WargameMapCalibration _calibration;
        private readonly List<WargameObjective> _objectives;

        private WargameCombatSession _session;

        private ucWargameMapView _mapView;
        private Timer _timer;

        private NumericUpDown _numTurns;
        private NumericUpDown _numVariance;
        private NumericUpDown _numLossBlue;
        private NumericUpDown _numLossRed;
        private NumericUpDown _numAirBonus;
        private NumericUpDown _numRetreatLoss;
        private NumericUpDown _numMaxPerZone;
        private NumericUpDown _numDisband;
        private CheckBox _checkShowAxes;
        private NumericUpDown _numAxisOpacity;
        private ComboBox _comboAir;
        private NumericUpDown _numDelay;
        private NumericUpDown _numRuns;
        private CheckBox _checkAllAxes;
        private Button _buttonPlay;
        private Label _labelStatus;
        private TextBox _txtJournal;
        private TextBox _txtReport;

        public WargameCombatPreview_Form(
            string campaignName, List<WargameZoneData> zones, List<WargameAxis> axes,
            WargameTemplateCatalog catalog, WargameCampaignInfo info,
            Image mapImage, WargameMapCalibration calibration, List<WargameObjective> objectives)
        {
            _campaignName = campaignName;
            _zones = zones;
            _axes = axes;
            _catalog = catalog;
            _info = info;
            _mapImage = mapImage;       // appartient à l'éditeur : on ne le libère pas ici
            _calibration = calibration;
            _objectives = objectives;

            Text = "Wargame - Preview combat";
            Width = 1300;
            Height = 800;
            StartPosition = FormStartPosition.CenterParent;

            BuildUi();
            ResetSimulation();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        // -------------------- Construction de l'UI --------------------

        private void BuildUi()
        {
            // Carte (lecture seule : zoom molette et déplacement restent actifs)
            _mapView = new ucWargameMapView { ReadOnly = true, ShowAxes = true, ShowMissionUnits = false };

            var mapPanel = new NoAutoScrollPanel { Dock = DockStyle.Fill, AutoScroll = true };
            mapPanel.Controls.Add(_mapView);

            // Panneau de droite : réglages en haut, onglets (journal / rapport) en dessous
            var right = new Panel { Dock = DockStyle.Right, Width = 400 };

            var tabs = new TabControl { Dock = DockStyle.Fill };

            var tabJournal = new TabPage("Journal");
            _txtJournal = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
            };
            tabJournal.Controls.Add(_txtJournal);

            var tabReport = new TabPage("Report (many games)");
            _txtReport = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                Text = "Plays the whole thing several times, without the map, and gives percentages.\r\n\r\n"
                    + "Runs : how many games to play.\r\n"
                    + "Uses the Max turns, Dice %, losses %, air superiority, retreat loss, max per zone and disband threshold set above.\r\n"
                    + "Nothing is saved.",
            };

            var reportTop = new Panel { Dock = DockStyle.Top, Height = 40 };
            reportTop.Controls.Add(new Label { Text = "Runs", Location = new Point(8, 13), AutoSize = true });
            _numRuns = new NumericUpDown { Location = new Point(50, 10), Width = 60, Minimum = 1, Maximum = 1000, Value = 50 };
            reportTop.Controls.Add(_numRuns);
            var buttonReport = new Button { Text = "Run report", Location = new Point(125, 8), Width = 110, Height = 26 };
            buttonReport.Click += (s, e) => RunReport();
            reportTop.Controls.Add(buttonReport);

            tabReport.Controls.Add(_txtReport);   // Fill d'abord, puis le Top
            tabReport.Controls.Add(reportTop);

            tabs.TabPages.Add(tabJournal);
            tabs.TabPages.Add(tabReport);

            var settings = new Panel { Dock = DockStyle.Top, Height = 344 };

            // Hasard : on part du réglage de la campagne, ou 10 % si elle n'en a pas
            decimal variance = _info.CombatConfig.RandomVariance > 0 ? (decimal)(_info.CombatConfig.RandomVariance * 100) : 10;

            AddLabel(settings, "Max turns", 10, 13);
            _numTurns = AddNumber(settings, 110, 10, 60, 20, 1, 500);

            AddLabel(settings, "Dice %", 200, 13);
            _numVariance = AddNumber(settings, 270, 10, 60, variance, 0, 50);
            _numVariance.ValueChanged += (s, e) => { if (_session != null) _session.Variance = (double)_numVariance.Value / 100.0; };

            // Pertes de première ligne par tour, PAR CAMP : celui qui a la maîtrise du ciel perd peu
            AddLabel(settings, _info.BlueLabel + " losses %", 10, 43);
            _numLossBlue = AddNumber(settings, 110, 40, 60, 20, 0, 100);
            _numLossBlue.ValueChanged += (s, e) => { if (_session != null) _session.BlueLossPercent = (double)_numLossBlue.Value; };

            AddLabel(settings, _info.RedLabel + " losses %", 200, 43);
            _numLossRed = AddNumber(settings, 290, 40, 60, 60, 0, 100);
            _numLossRed.ValueChanged += (s, e) => { if (_session != null) _session.RedLossPercent = (double)_numLossRed.Value; };

            // Explication au survol : cette valeur imite les missions du joueur, avec la loi de la vraie campagne
            var lossTip = new ToolTip { AutoPopDelay = 12000 };
            string lossHelp = "Stands for your missions. Each turn, every front-line formation of this side loses this % of ONE posted copy of its template, "
                + "never more (like the real campaign). 100 = the whole copy destroyed. Random between half and one and a half times this value.";
            lossTip.SetToolTip(_numLossBlue, lossHelp);
            lossTip.SetToolTip(_numLossRed, lossHelp);

            AddLabel(settings, "Delay (ms)", 10, 73);
            _numDelay = AddNumber(settings, 110, 70, 70, 800, 100, 5000);

            // Supériorité aérienne : camp qui a la maîtrise du ciel + bonus de force qu'elle lui donne
            AddLabel(settings, "Air superiority", 200, 73);
            _comboAir = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(290, 69), Width = 95 };
            _comboAir.Items.Add("none");
            _comboAir.Items.Add(_info.BlueLabel);
            _comboAir.Items.Add(_info.RedLabel);

            string airSide = _info.CombatConfig.AirSuperioritySide;
            _comboAir.SelectedIndex = airSide == WargameSide.Blue ? 1 : airSide == WargameSide.Red ? 2 : 0;
            _comboAir.SelectedIndexChanged += (s, e) => ApplyAirToSession();
            settings.Controls.Add(_comboAir);

            AddLabel(settings, "Air bonus %", 10, 103);
            _numAirBonus = AddNumber(settings, 110, 100, 60, (decimal)(_info.CombatConfig.AirSuperiorityBonus * 100), 0, 200);
            _numAirBonus.ValueChanged += (s, e) => ApplyAirToSession();
            _numDelay.ValueChanged += (s, e) => _timer.Interval = (int)_numDelay.Value;

            // Nombre normal de formations par zone (une zone de front en reçoit plus si l'ennemi d'en face est plus fort)
            AddLabel(settings, "Max / zone", 200, 103);
            _numMaxPerZone = AddNumber(settings, 290, 100, 60, _info.CombatConfig.MaxFormationsPerZone, 1, 30);
            _numMaxPerZone.ValueChanged += (s, e) => { if (_session != null) _session.MaxPerZone = (int)_numMaxPerZone.Value; };

            // Force perdue par une formation à chaque repli
            decimal retreatLoss = _info.CombatConfig.RetreatLossFraction > 0 ? (decimal)(_info.CombatConfig.RetreatLossFraction * 100) : 5;
            AddLabel(settings, "Retreat loss %", 10, 133);
            _numRetreatLoss = AddNumber(settings, 110, 130, 60, retreatLoss, 0, 90);
            _numRetreatLoss.ValueChanged += (s, e) => { if (_session != null) _session.RetreatLossPercent = (double)_numRetreatLoss.Value; };

            // Une formation qui tombe sous ce % de sa force nominale est dissoute
            AddLabel(settings, "Disband below %", 200, 133);
            _numDisband = AddNumber(settings, 310, 130, 60, (decimal)(_info.CombatConfig.DisbandBelowFraction * 100), 0, 90);
            _numDisband.ValueChanged += (s, e) => { if (_session != null) _session.DisbandPercent = (double)_numDisband.Value; };

            _checkAllAxes = new CheckBox
            {
                Text = "Treat all axes as active",
                Checked = true,
                Location = new Point(10, 164),
                AutoSize = true,
            };
            _checkAllAxes.CheckedChanged += (s, e) => ResetSimulation();
            settings.Controls.Add(_checkAllAxes);

            var buttonNext = new Button { Text = "Next turn", Location = new Point(10, 194), Width = 110, Height = 30 };
            buttonNext.Click += (s, e) => { StopPlay(); DoStep(); };

            _buttonPlay = new Button { Text = "Play", Location = new Point(130, 194), Width = 110, Height = 30 };
            _buttonPlay.Click += (s, e) => { if (_timer.Enabled) StopPlay(); else StartPlay(); };

            var buttonReset = new Button { Text = "Reset", Location = new Point(250, 194), Width = 110, Height = 30 };
            buttonReset.Click += (s, e) => ResetSimulation();

            settings.Controls.Add(buttonNext);
            settings.Controls.Add(_buttonPlay);
            settings.Controls.Add(buttonReset);

            // Affichage des axes : on peut les masquer ou les rendre transparents pour voir les points de force
            var buttonSaveLog = new Button { Text = "Save full log", Location = new Point(300, 232), Width = 90, Height = 28 };
            buttonSaveLog.Click += (s, e) => SaveFullLog();
            settings.Controls.Add(buttonSaveLog);

            _checkShowAxes = new CheckBox { Text = "Show axes", Checked = true, Location = new Point(10, 236), AutoSize = true };
            _checkShowAxes.CheckedChanged += (s, e) => _mapView.ShowAxes = _checkShowAxes.Checked;
            settings.Controls.Add(_checkShowAxes);

            AddLabel(settings, "Axes opacity %", 130, 239);
            _numAxisOpacity = AddNumber(settings, 235, 235, 60, 35, 0, 100);
            _numAxisOpacity.ValueChanged += (s, e) => _mapView.AxisOpacity = (float)_numAxisOpacity.Value / 100f;
            _mapView.AxisOpacity = 0.35f;

            _labelStatus = new Label
            {
                Location = new Point(10, 272),
                Size = new Size(375, 64),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };
            settings.Controls.Add(_labelStatus);

            right.Controls.Add(tabs);       // Fill d'abord, puis le Top
            right.Controls.Add(settings);

            Controls.Add(mapPanel);         // Fill d'abord, puis le Right
            Controls.Add(right);

            _timer = new Timer { Interval = (int)_numDelay.Value };
            _timer.Tick += (s, e) =>
            {
                DoStep();

                if (_session.Turn >= (int)_numTurns.Value)
                {
                    Append("Max turns reached.");
                    StopPlay();
                }
                else if (_session.QuietTurns >= 3)
                {
                    Append("Front stable for 3 turns, stopped.");
                    StopPlay();
                }
            };
        }

        private static void AddLabel(Panel panel, string text, int x, int y)
        {
            panel.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
        }

        private static NumericUpDown AddNumber(Panel panel, int x, int y, int width, decimal value, decimal min, decimal max)
        {
            var num = new NumericUpDown
            {
                Location = new Point(x, y),
                Width = width,
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
            };

            panel.Controls.Add(num);
            return num;
        }

        // -------------------- Simulation tour par tour --------------------

        private HashSet<string> ComputeFlags()
        {
            if (_checkAllAxes.Checked)
            {
                return new HashSet<string>(
                    _axes.Where(a => !string.IsNullOrWhiteSpace(a.ActivationFlag)).Select(a => a.ActivationFlag),
                    StringComparer.OrdinalIgnoreCase);
            }

            string statusPath = Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Active", "camp_status.lua");
            return WargameCampFlags.LoadActiveFlags(statusPath);
        }

        // Repart de l'état des zones tel qu'il est dans l'éditeur (copie neuve)
        private void ResetSimulation()
        {
            if (_timer != null) StopPlay();

            WargameAxisPathFinder.RecomputeAll(_axes, _zones);
            WargameAxisPathFinder.RememberAttackers(_axes, _zones);   // attaquant = camp qui tient la zone de départ MAINTENANT

            _session = new WargameCombatSession(_zones, _axes, ComputeFlags(), _catalog, _info);
            _session.Variance = (double)_numVariance.Value / 100.0;
            _session.BlueLossPercent = (double)_numLossBlue.Value;
            _session.RedLossPercent = (double)_numLossRed.Value;
            _session.RetreatLossPercent = (double)_numRetreatLoss.Value;
            _session.MaxPerZone = (int)_numMaxPerZone.Value;
            _session.DisbandPercent = (double)_numDisband.Value;
            ApplyAirToSession();
            _session.WriteHeader(_campaignName);

            _txtJournal.Text = "Start. Click \"Next turn\" or \"Play\".\r\n";

            _mapView.LoadMap(_mapImage, _calibration, _session.Zones);
            _mapView.SetAxes(_axes);
            _mapView.SetObjectives(_objectives);

            RefreshMap();
        }

        private string AirSideFromCombo()
        {
            switch (_comboAir.SelectedIndex)
            {
                case 1: return WargameSide.Blue;
                case 2: return WargameSide.Red;
                default: return "";
            }
        }

        private void ApplyAirToSession()
        {
            if (_session == null) return;

            _session.AirSide = AirSideFromCombo();
            _session.AirBonus = (double)_numAirBonus.Value / 100.0;
        }

        // Ecrit le journal complet à côté de la campagne et affiche où il se trouve
        private void SaveFullLog()
        {
            try
            {
                string path = Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "wargame_preview_log.txt");
                _session.SaveFullLog(path);
                Append("Full log saved: " + path);
            }
            catch (Exception ex)
            {
                Append("Full log NOT saved: " + ex.Message);
            }
        }

        private void DoStep()
        {
            List<string> lines = _session.Step();
            Append(string.Join("\r\n", lines));
            RefreshMap();
        }

        private void RefreshMap()
        {
            _mapView.SetZoneCaptions(_session.BuildCaptions());
            _mapView.SetZoneDots(_session.BuildDots());
            _mapView.SetFlashZones(_session.LastFlippedIds);
            _labelStatus.Text = _session.BuildStatus((int)_numTurns.Value);
        }

        private void Append(string text)
        {
            _txtJournal.AppendText(text + "\r\n");
        }

        private void StartPlay()
        {
            _timer.Interval = (int)_numDelay.Value;
            _timer.Start();
            _buttonPlay.Text = "Pause";
        }

        private void StopPlay()
        {
            _timer.Stop();
            _buttonPlay.Text = "Play";
        }

        // -------------------- Rapport texte (N parties d'un coup) --------------------

        private void RunReport()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                WargameAxisPathFinder.RecomputeAll(_axes, _zones);
            WargameAxisPathFinder.RememberAttackers(_axes, _zones);   // attaquant = camp qui tient la zone de départ MAINTENANT

                _txtReport.Text = WargameCombatPreview.Run(
                    _zones, _axes, ComputeFlags(), _catalog, _info,
                    (int)_numTurns.Value, (int)_numRuns.Value,
                    (double)_numVariance.Value / 100.0, (double)_numLossBlue.Value, (double)_numLossRed.Value,
                    AirSideFromCombo(), (double)_numAirBonus.Value / 100.0,
                    (double)_numRetreatLoss.Value, (int)_numMaxPerZone.Value, (double)_numDisband.Value);
            }
            catch (Exception ex)
            {
                _txtReport.Text = "Report failed: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}
