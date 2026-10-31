using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DCE_Manager.Utils;
using Newtonsoft.Json;

namespace DCE_Manager
{
    // Événement programmé de la Preview : à un tour donné, un objectif est détruit ou réparé.
    // Enregistré dans Init/Wargame/preview_events.json (utilisé par la Preview seulement, jamais par les missions).
    internal class PreviewEvent
    {
        public const string Destroy = "destroy";
        public const string Repair = "repair";

        public int Turn;
        public string Action = Destroy;
        public string Target = "";   // nom de l'objectif
    }

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
            string airSide, double airBonus, double retreatLossPercent, int maxPerZone, double disbandPercent,
            WargameTriggerSim triggerSim = null,
            List<PreviewEvent> events = null, List<WargameObjective> objectives = null)
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

                // Resolve retire les flags qu'il consomme (renforts) : chaque partie travaille sur sa copie
                var runFlags = new HashSet<string>(activeFlags, StringComparer.OrdinalIgnoreCase);

                // Triggers de la campagne, rejoués à chaque partie : déjà lus et compilés, on remet juste l'état à zéro
                if (triggerSim != null) triggerSim.Reset();

                // Objectifs : état de départ de cette partie (les événements les détruisent ou les réparent en cours de route)
                var alive = new Dictionary<WargameObjective, double>();
                if (objectives != null)
                {
                    foreach (WargameObjective o in objectives) alive[o] = o.Alive;
                    WargameEngineCombat.ApplyObjectivesToSupply(work, objectives, config, alive);
                }

                for (int turn = 1; turn <= turns; turn++)
                {
                    if (events != null && objectives != null)
                        ApplyEventsTo(events, turn, objectives, alive);

                    if (objectives != null)
                        WargameEngineCombat.ApplyObjectivesToSupply(work, objectives, config, alive);

                    if (triggerSim != null)
                        triggerSim.Run(turn, work, runFlags);

                    ApplyAttrition(work, byId, links, blueLossPercent, redLossPercent, rng, catalog);

                    WargameCombatResult result = WargameEngineCombat.Resolve(work, axes, runFlags, config, rng, catalog);
                    WargameEngineCombat.RemoveBrokenFormations(work, catalog, config, result);

                    if (objectives != null)
                        WargameEngineCombat.ApplyObjectivesToSupply(work, objectives, config, alive);
                    WargameEngineCombat.ApplyLandingSupply(work, runFlags, config);
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

        // Ce qui manque au catalogue de templates pour que le wargame fonctionne, une entrée par manque ("convoy (Blue)").
        //   convoysOn    : les templates "supplypoint" et "convoy" de chaque camp ne sont exigés que si les convois sont actifs
        //   includeUnits : vérifie aussi qu'il y a au moins un template de formation par camp
        // Liste vide = rien ne manque. Sert à l'éditeur (étape Templates) et à la Preview (case des convois).
        public static List<string> MissingTemplates(WargameTemplateCatalog catalog, WargameCampaignInfo info, bool convoysOn, bool includeUnits)
        {
            var missing = new List<string>();
            if (catalog == null || info == null) return missing;

            foreach (string side in new[] { WargameSide.Blue, WargameSide.Red })
            {
                string label = info.GetLabel(side);

                if (includeUnits && !catalog.Entries.Any(e => WargameUnitType.IsFormationType(e.Type)
                    && string.Equals(e.Side, side, StringComparison.OrdinalIgnoreCase)))
                    missing.Add("units (" + label + ")");

                if (!convoysOn) continue;

                if (catalog.OfType(WargameUnitType.SupplyPoint, side).Count == 0)
                    missing.Add("supplypoint (" + label + ")");

                if (catalog.OfType(WargameUnitType.Convoy, side).Count == 0)
                    missing.Add("convoy (" + label + ")");
            }

            return missing;
        }

        // Applique les événements d'un tour sur l'état de vie des objectifs. Renvoie les lignes de journal,
        // ou null si rien n'a changé (inutile de recalculer le ravitaillement).
        public static List<string> ApplyEventsTo(List<PreviewEvent> events, int turn, List<WargameObjective> objectives, Dictionary<WargameObjective, double> alive)
        {
            List<string> lines = null;

            foreach (PreviewEvent ev in events)
            {
                if (ev.Turn != turn) continue;

                if (lines == null) lines = new List<string>();

                WargameObjective obj = objectives.FirstOrDefault(o => string.Equals(o.Name, ev.Target, StringComparison.OrdinalIgnoreCase));
                if (obj == null)
                {
                    lines.Add("EVENT turn " + turn + " : target '" + ev.Target + "' not found among the objectives (ignored)");
                    continue;
                }

                bool destroy = ev.Action == PreviewEvent.Destroy;
                alive[obj] = destroy ? 0.0 : 100.0;

                string kind = (obj.IsBridge ? " [bridge]" : "") + (obj.SupplyValue > 0 ? " [supply source " + obj.SupplyValue.ToString("0") + "]" : "");
                lines.Add("EVENT : " + obj.Name + kind + (destroy ? " DESTROYED" : " REPAIRED")
                    + (kind == "" ? "   *** WARNING: this objective is neither a Bridge nor a Supply source, so it has NO effect on supply. Tick 'Bridge' (or set a Supply value) in the wargame editor. ***" : ""));
            }

            return lines;
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

            int blueZones = zones.Count(z => !WargameEngineCombat.IsReserveZone(z) && z.Control == WargameSide.Blue);
            int redZones = zones.Count(z => !WargameEngineCombat.IsReserveZone(z) && z.Control == WargameSide.Red);
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
                    SupplyFlag = z.SupplyFlag,
                    NavalSupport = z.NavalSupport,
                    NavalFlag = z.NavalFlag,
                    SupplyOwner = z.Control,   // camp de départ : un objectif-source capturé passe à 0
                    Terrain = z.Terrain,
                    SupplyRoute = z.SupplyRoute,
                    LandingTarget = z.LandingTarget,
                    LandingWait = z.LandingWait,
                    LandingRetries = z.LandingRetries,
                    LandingBeach = z.LandingBeach,
                    CutOffTurns = z.CutOffTurns,
                    CutOffSide = z.CutOffSide,
                    ForwardReserve = z.ForwardReserve,
                    ForwardSide = z.ForwardSide,
                    ForwardSlot = z.ForwardSlot,
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
                        Wave = f.Wave,
                        Attributes = new List<string>(f.Attributes ?? new List<string>()),
                        FirepowerMin = f.FirepowerMin,
                        FirepowerMax = f.FirepowerMax,
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

        // Test du ravitaillement : convois activés ou non, et part de la cargaison PERDUE (route) ou LIVREE (air), en %
        public bool ConvoysOn;
        public double BlueConvoyLossPercent, RedConvoyLossPercent;
        public double BlueAirPercent, RedAirPercent;

        // Convois du dernier tour (journal, carte)
        public List<WargameEngineCombat.WargameConvoy> Convoys { get { return _config.Convoys; } }

        // Zones qui ont changé de main au dernier tour (la carte les entoure)
        public readonly HashSet<string> LastFlippedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Nombre de tours de suite sans aucun mouvement (sert à arrêter la lecture auto)
        public int QuietTurns;

        // Événements programmés (détruire / réparer un objectif à un tour donné). Liste partagée avec la fenêtre :
        // un événement ajouté pendant la lecture compte dès que son tour arrive.
        public List<PreviewEvent> Events = new List<PreviewEvent>();

        private List<WargameObjective> _objectives = new List<WargameObjective>();
        private readonly Dictionary<WargameObjective, double> _alive = new Dictionary<WargameObjective, double>();

        // Objectifs de la campagne, avec leur état de départ. La session garde son propre état de vie (_alive) :
        // les objectifs eux-mêmes ne sont jamais modifiés.
        public void SetObjectives(List<WargameObjective> objectives)
        {
            _objectives = objectives ?? new List<WargameObjective>();
            _alive.Clear();
            foreach (WargameObjective o in _objectives)
                _alive[o] = o.Alive;

            WargameEngineCombat.ApplyObjectivesToSupply(Zones, _objectives, _config, _alive);
        }

        // Niveau de ravitaillement de chaque zone dans l'état actuel de la simulation (pour le calque de la carte)
        public Dictionary<string, WargameEngineCombat.WargameSupplyInfo> BuildSupplyMap()
        {
            WargameEngineCombat.ApplyObjectivesToSupply(Zones, _objectives, _config, _alive);
            WargameEngineCombat.ApplyLandingSupply(Zones, _flags, _config);
            return WargameEngineCombat.ComputeSupplyMap(Zones, _config);
        }

        // ---- Pour l'export vers la campagne (bouton "Jouer ce tour dans DCS") ----

        // Flags actuels de la simulation (axes, déploiements, débarquements, triggers)
        public HashSet<string> Flags { get { return _flags; } }

        // Formation -> zone ennemie vers laquelle elle est tournée (fin du dernier tour)
        public Dictionary<int, string> Facing { get { return _facing; } }

        // Objectifs abîmés par les événements de la Preview : nom -> part détruite (0 à 1)
        public Dictionary<string, double> ObjectiveDamage()
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<WargameObjective, double> kv in _alive)
            {
                if (kv.Value >= 100 || string.IsNullOrEmpty(kv.Key.Name)) continue;
                result[kv.Key.Name] = Math.Max(0, Math.Min(1, 1.0 - kv.Value / 100.0));
            }
            return result;
        }

        // Ancien DCE : chance qu'une unité au sol soit détruite, par "zone|camp", d'après les tours passés en première ligne
        // (à chaque tour : % de pertes de test du camp, doublé si l'ennemi a la maîtrise du ciel)
        private readonly Dictionary<string, double> _frontSurvival = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> FrontKillChance()
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> kv in _frontSurvival)
                if (kv.Value < 1.0) result[kv.Key] = 1.0 - kv.Value;
            return result;
        }

        private void RecordFrontLosses()
        {
            foreach (WargameZoneData z in Zones)
            {
                if (WargameEngineCombat.IsOffMapId(z.Id)) continue;
                if (z.Control != WargameSide.Blue && z.Control != WargameSide.Red) continue;

                string enemy = z.Control == WargameSide.Blue ? WargameSide.Red : WargameSide.Blue;

                List<string> neighbors;
                if (!_links.TryGetValue(z.Id, out neighbors)) continue;
                bool front = neighbors.Any(id => _byId.ContainsKey(id) && _byId[id].Control == enemy);
                if (!front) continue;

                double percent = z.Control == WargameSide.Blue ? BlueLossPercent : RedLossPercent;
                double p = percent / 100.0 * (string.Equals(AirSide, enemy, StringComparison.OrdinalIgnoreCase) ? 2.0 : 1.0);
                p = Math.Max(0, Math.Min(1, p));

                string key = z.Id + "|" + z.Control;
                double survival;
                if (!_frontSurvival.TryGetValue(key, out survival)) survival = 1.0;
                _frontSurvival[key] = survival * (1.0 - p);
            }
        }

        public HashSet<WargameObjective> DestroyedObjectives()
        {
            return new HashSet<WargameObjective>(_objectives.Where(o => _alive.ContainsKey(o) && _alive[o] <= 0));
        }

        // true quand tous les axes actifs (au moins un) ont atteint leur objectif
        public bool AllObjectivesReached()
        {
            bool any = false;
            foreach (WargameAxis axis in _axes)
            {
                if (!axis.IsActive(_flags) || string.IsNullOrEmpty(axis.ObjectiveFlag)) continue;

                any = true;
                if (!_reached.Contains(axis.ObjectiveFlag)) return false;
            }

            return any;
        }

        // true tant qu'un plan de débarquement garde des unités à mettre à terre : ni "objectifs atteints" ni "front stable"
        // ne doivent arrêter la lecture, l'invasion n'a pas encore eu lieu (ou n'est pas finie).
        public bool LandingPending()
        {
            foreach (WargameZoneData z in _byId.Values)
            {
                if (WargameEngineCombat.IsLandingZone(z) && z.Formations.Any(f => f.ForcePower > 0 && f.Wave > 0))
                    return true;
            }

            return false;
        }

        // Pour chaque formation au contact : id de la zone ennemie vers laquelle elle est tournée (fin du dernier tour).
        // Sert aux flèches de la carte. Vide avant le premier tour.
        private Dictionary<int, string> _facing = new Dictionary<int, string>();

        // Triggers de la campagne (camp_triggers_init.lua), rejoués au début de chaque tour. Peut rester null.
        public WargameTriggerSim Triggers;

        // Date et heure des missions, pour l'affichage et les journaux (les triggers ont leur propre référence)
        public WargameCampClock Clock;

        private string TimeText(int turn)
        {
            return Clock == null ? "" : Clock.At(Math.Max(1, turn)).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Pourquoi les triggers ne sont pas chargés (affiché dans le journal complet)
        public string TriggersNote = "";

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

            // Événements programmés de ce tour : appliqués AVANT l'état de départ, pour que le ravitaillement en tienne compte
            List<string> eventLines = WargameCombatPreview.ApplyEventsTo(Events, Turn, _objectives, _alive);

            // Toujours recalculé (pas seulement après un événement) : les zones ont pu changer de main au tour précédent,
            // et une source capturée doit tomber à 0 tout de suite
            WargameEngineCombat.ApplyObjectivesToSupply(Zones, _objectives, _config, _alive);

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
            _config.SupplyConvoys = ConvoysOn;
            _config.ConvoyDeliveredBlue = 1.0 - BlueConvoyLossPercent / 100.0;
            _config.ConvoyDeliveredRed = 1.0 - RedConvoyLossPercent / 100.0;
            _config.AirDeliveredBlue = BlueAirPercent / 100.0;
            _config.AirDeliveredRed = RedAirPercent / 100.0;

            FullLog.Add("");
            FullLog.Add("==================== TURN " + Turn + (Clock == null ? "" : " | " + TimeText(Turn)) + " ====================");
            FullLog.Add("Settings this turn: luck " + (Variance * 100).ToString("0") + "% | test losses (% of one posted copy) blue " + BlueLossPercent.ToString("0.#")
                + "% / red " + RedLossPercent.ToString("0.#") + "% | air " + (string.IsNullOrEmpty(AirSide) ? "none" : AirSide + " +" + (AirBonus * 100).ToString("0") + "%")
                + " | retreat loss " + RetreatLossPercent.ToString("0.#") + "% | formations per zone " + MaxPerZone + " | disband below " + DisbandPercent.ToString("0.#") + "%");
            FullLog.Add("Supply test: convoys " + (ConvoysOn ? "ON" : "off") + " | road convoys lost blue " + BlueConvoyLossPercent.ToString("0") + "% / red " + RedConvoyLossPercent.ToString("0")
                + "% | air supply delivered blue " + BlueAirPercent.ToString("0") + "% / red " + RedAirPercent.ToString("0") + "%");
            FullLog.Add("");
            if (eventLines != null)
            {
                FullLog.Add("-- Scheduled events --");
                foreach (string line in eventLines)
                {
                    FullLog.Add("  " + line);
                    log.Add("  " + line);
                }
                FullLog.Add("");
            }

            FullLog.Add("-- State at the start of the turn --");
            FullLog.AddRange(WargameEngineCombat.DescribeState(Zones, _catalog, _config));

            // Triggers de la campagne : ils passent à la génération de la mission, donc avant les pertes et le combat
            if (Triggers != null)
            {
                List<string> triggerLines = Triggers.Run(Turn, Zones, _flags);
                if (triggerLines.Count > 0)
                {
                    FullLog.Add("");
                    FullLog.Add("-- Campaign triggers --");
                    foreach (string line in triggerLines)
                    {
                        FullLog.Add(line);
                        log.Add("  " + line);
                    }
                }
            }

            Dictionary<int, double> power0 = SnapshotPower();

            RecordFrontLosses();   // pour l'export : pertes de l'ancien DCE en première ligne
            WargameCombatPreview.ApplyAttrition(Zones, _byId, _links, BlueLossPercent, RedLossPercent, _rng, _catalog);
            Dictionary<int, double> power1 = SnapshotPower();

            _traceLines.Clear();
            _timeline.Clear();
            WargameCombatResult result = WargameEngineCombat.Resolve(Zones, _axes, _flags, _config, _rng, _catalog);
            _facing = new Dictionary<int, string>(result.FacingZoneByFormationId);   // vers quelle zone ennemie chaque formation est tournée
            WargameEngineCombat.RemoveBrokenFormations(Zones, _catalog, _config, result);
            Dictionary<int, double> power2 = SnapshotPower();

            // Les zones ont pu changer de main pendant le combat : les sources sont recalculées avant la récupération
            WargameEngineCombat.ApplyObjectivesToSupply(Zones, _objectives, _config, _alive);
            WargameEngineCombat.ApplyLandingSupply(Zones, _flags, _config);
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

                    string kind = result.DeployedFormationIds.Contains(f.FormationId) ? "deploy "
                        : result.ReinforcedFormationIds.Contains(f.FormationId) ? "reinforce "
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

            foreach (string note in result.DeployNotes)
                log.Add("  " + note);

            // Objectifs d'axe atteints
            bool newObjective = false;

            // Un objectif n'est "atteint" que s'il l'est ENCORE : le moteur refait ce calcul à chaque tour
            // (sinon un axe "atteint" avant le débarquement, ou perdu depuis, fausserait l'arrêt de la Lecture)
            _reached.RemoveWhere(x => !result.ReachedFlags.Contains(x));

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

            // Besoin en ravitaillement de ce tour, en tonnes (1 tonne = 1000 kg). Affichage seulement : rien n'est consommé pour l'instant.
            List<WargameEngineCombat.WargameSupplyDemand> demand = WargameEngineCombat.ComputeSupplyDemand(Zones, _catalog, _config, result);
            FullLog.Add("");
            FullLog.Add("-- Supply demand (tons per turn, 1 ton = 1000 kg) --");
            FullLog.Add("   catalog consumption is per day; one turn = " + (_config.TurnDayFraction * 100).ToString("0.#") + "% of a day ("
                + (1.0 / Math.Max(0.001, _config.TurnDayFraction)).ToString("0.#") + " turns per day, from the conf_mod.lua timing)");
            FullLog.Add("   coefficients: rear x" + _config.SupplyUseRear.ToString("0.##") + " | front x" + _config.SupplyUseFront.ToString("0.##")
                + " | attacking x" + _config.SupplyUseAttack.ToString("0.##") + " | defending x" + _config.SupplyUseDefend.ToString("0.##"));

            foreach (string demandSide in new[] { WargameSide.Blue, WargameSide.Red })
            {
                FullLog.Add("  " + demandSide.ToUpperInvariant() + " (" + _info.GetLabel(demandSide) + ")");

                foreach (var zoneGroup in demand.Where(d => d.Side == demandSide).GroupBy(d => d.ZoneId).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    FullLog.Add("    " + zoneGroup.Key + " : " + zoneGroup.Sum(d => d.Tons).ToString("0.0") + " t");

                    foreach (var item in zoneGroup.OrderBy(x => x.Formation.Name, StringComparer.OrdinalIgnoreCase))
                        FullLog.Add("        " + item.Formation.Name + " : " + item.Base.ToString("0.0") + " t/turn (strength " + (item.Strength * 100).ToString("0") + "%) x " + item.Coef.ToString("0.##") + " (" + item.State + ") = " + item.Tons.ToString("0.0") + " t");
                }

                double sideTons = demand.Where(d => d.Side == demandSide).Sum(d => d.Tons);
                FullLog.Add("    TOTAL " + demandSide.ToUpperInvariant() + " : " + sideTons.ToString("0.0") + " t");
            }

            log.Add("  supply demand: blue " + demand.Where(d => d.Side == WargameSide.Blue).Sum(d => d.Tons).ToString("0") + " t / red "
                + demand.Where(d => d.Side == WargameSide.Red).Sum(d => d.Tons).ToString("0") + " t per turn (details in the full journal)");

            FullLog.Add("");
            FullLog.Add("-- Turn summary --");
            FullLog.Add("  zones changed hands: " + (LastFlippedIds.Count == 0 ? "none" : string.Join(", ", LastFlippedIds.OrderBy(x => x)))
                + " | moves " + moves + " | advances " + result.Advanced + " | worn " + result.Worn + " | lost " + result.Destroyed
                + " | disbanded " + result.Disbanded + " | objectives " + (result.ReachedFlags.Count == 0 ? "none" : string.Join(", ", result.ReachedFlags)));
            FullLog.Add("  force blue " + WargameCombatPreview.SideForce(Zones, WargameSide.Blue).ToString("0.0")
                + " / red " + WargameCombatPreview.SideForce(Zones, WargameSide.Red).ToString("0.0")
                + " | zones blue " + Zones.Count(z => !WargameEngineCombat.IsReserveZone(z) && z.Control == WargameSide.Blue) + " / red " + Zones.Count(z => !WargameEngineCombat.IsReserveZone(z) && z.Control == WargameSide.Red));

            return log;
        }

        // ---- Renforts (voir WargameEngineCombat.DeployReinforcements) ----

        // Formations vivantes rangées dans une zone de réserve
        public List<string> ReserveFormationNames()
        {
            var names = new List<string>();

            foreach (WargameZoneData z in Zones)
            {
                if (!WargameEngineCombat.IsReserveZone(z) || WargameEngineCombat.IsLandingZone(z)) continue;   // les plans de débarquement ont leur propre ordre

                foreach (WargameFormation f in z.Formations)
                    if (f.ForcePower > 0) names.Add(f.Name);
            }

            return names;
        }

        // Zones où un renfort peut être envoyé (toutes sauf les réserves)
        public List<string> DeployableZoneIds()
        {
            return Zones.Where(z => !WargameEngineCombat.IsReserveZone(z)).Select(z => z.Id)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Lève le flag d'ordre, comme le ferait un trigger de campagne : il sera traité au début du prochain tour
        public void QueueDeploy(string formationName, string zoneId)
        {
            _flags.Add(WargameEngineCombat.DeployFlagName(formationName, zoneId));
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

            FullLog.Add("PRODUCTION SETTINGS (code defaults, then camp_init, then wargame_balance.lua: " + (_info.BalanceFileFound ? "file FOUND" : "no file, code values") + ")");
            FullLog.AddRange(DescribeConfig(_info.CombatConfig));
            FullLog.Add("");

            FullLog.Add("TEST SETTINGS (preview only, they stand for the missions the player would fly)");
            FullLog.Add("  luck " + (Variance * 100).ToString("0") + "% | test losses (% of one posted copy) blue " + BlueLossPercent.ToString("0.#") + "% / red " + RedLossPercent.ToString("0.#")
                + "% | air " + (string.IsNullOrEmpty(AirSide) ? "none" : AirSide + " +" + (AirBonus * 100).ToString("0") + "%")
                + " | retreat loss " + RetreatLossPercent.ToString("0.#") + "% | formations per zone " + MaxPerZone + " | disband below " + DisbandPercent.ToString("0.#") + "%");
            FullLog.Add("  (these test values replace the production ones for variance, retreat loss, formations per zone, disband and air)");
            FullLog.Add("");

            if (Events.Count > 0)
            {
                FullLog.Add("SCHEDULED EVENTS (Events tab of the Preview, saved in Init/Wargame/preview_events.json)");
                foreach (PreviewEvent ev in Events.OrderBy(x => x.Turn))
                    FullLog.Add("  turn " + ev.Turn + " : " + (ev.Action == PreviewEvent.Destroy ? "destroy " : "repair ") + ev.Target);
                FullLog.Add("");
            }

            FullLog.Add("AXES");
            foreach (WargameAxis a in _axes)
            {
                FullLog.Add("  " + a.Name + " : " + (a.IsActive(_flags) ? "ACTIVE" : "inactive") + " | flag '" + a.ActivationFlag + "' | objective flag '" + a.ObjectiveFlag
                    + "' | attacker " + (string.IsNullOrEmpty(a.AttackerSide) ? "(not set)" : a.AttackerSide)
                    + " | path " + (a.IsValid ? string.Join(" > ", a.Path) : "NO PATH"));
            }
            FullLog.Add("  active flags: " + (_flags.Count == 0 ? "none" : string.Join(", ", _flags.OrderBy(x => x))));
            FullLog.Add("");

            if (Clock != null)
            {
                FullLog.Add("CAMPAIGN CLOCK (start from camp_init.lua, durations from conf_mod.lua; used by the date triggers): mission 1 on " + TimeText(1) + " | " + Clock.Describe());
                FullLog.Add("  (approximation: next mission = start + mission duration + AVERAGE pause between missions)");
                FullLog.Add("");
            }

            if (Triggers == null)
            {
                FullLog.Add("CAMPAIGN TRIGGERS: not loaded - " + (string.IsNullOrEmpty(TriggersNote) ? "unknown reason" : TriggersNote));
                FullLog.Add("");
            }
            else if (Triggers.Triggers.Count > 0)
            {
                FullLog.Add("CAMPAIGN TRIGGERS (camp_triggers_init.lua, replayed by the preview; conditions it cannot simulate wait until you force them)");
                FullLog.AddRange(Triggers.Describe());
                FullLog.Add("");
            }

            FullLog.Add("STATE AT START");
            FullLog.Add("  force blue " + WargameCombatPreview.SideForce(Zones, WargameSide.Blue).ToString("0.0")
                + " / red " + WargameCombatPreview.SideForce(Zones, WargameSide.Red).ToString("0.0")
                + " | zones blue " + Zones.Count(z => !WargameEngineCombat.IsReserveZone(z) && z.Control == WargameSide.Blue) + " / red " + Zones.Count(z => !WargameEngineCombat.IsReserveZone(z) && z.Control == WargameSide.Red));
            FullLog.AddRange(WargameEngineCombat.DescribeState(Zones, _catalog, _config));
        }

        private static List<string> DescribeConfig(WargameCombatConfig c)
        {
            var lines = new List<string>();
            lines.Add("  conquest difficulty (control_flip_threshold) " + c.ControlFlipThreshold.ToString("0.00") + " | luck " + c.RandomVariance.ToString("0.00")
                + " | retreat loss " + c.RetreatLossFraction.ToString("0.00") + " | wear in combat " + c.CombatAttrition.ToString("0.00")
                + " | straighten front " + c.FrontAlignmentEnabled + " | cut-off zones can attack " + c.CutOffCanAttack + " (after " + c.CutOffGraceTurns.ToString("0") + " turn(s) of grace)");
            lines.Add("  formations per zone " + c.MaxFormationsPerZone + " (hard max " + c.MaxFormationsHard + ") | disband below " + c.DisbandBelowFraction.ToString("0.00")
                + " | reinforcement speed " + c.SupplyRecoveryRate.ToString("0.00") + " | front-line recovery x" + c.FrontLineRecoveryFactor.ToString("0.00")
                + " | engaged recovery x" + c.EngagedRecoveryFactor.ToString("0.00") + " | cut-off supply x" + c.CutOffSupplyFactor.ToString("0.00"));
            lines.Add("  supply: full at capacity " + c.SupplySourceFull.ToString("0") + " | starving -" + (c.CutOffAttrition * 100).ToString("0.#") + "% per turn down to "
                + (c.CutOffAttritionFloor * 100).ToString("0") + "% | convoys " + c.SupplyConvoys + " (max " + c.ConvoyMax.ToString("0") + " per side, needed below "
                + (c.ConvoyNeedLevel * 100).ToString("0") + "%, forward point capacity " + c.ForwardPointCapacity.ToString("0") + ", max stock " + c.ForwardPointStockTons.ToString("0") + " t, convoy payload " + c.ConvoyPayloadTons.ToString("0") + " t, sources reach " + c.ConvoyFreeRange.ToString("0.#") + " without convoy)");
            lines.Add("  landings: " + c.LandingWaveGap + " turn(s) between waves | a repulsed wave loses " + (c.LandingFailLoss * 100).ToString("0.#") + "% and retries up to "
                + c.LandingMaxRetries.ToString("0") + " time(s), then gives up | a lost beach re-embarks by sea (fleet present) losing " + (c.ReembarkLoss * 100).ToString("0") + "%");
            lines.Add("  army follows the axes (surplus pull) " + c.AxisPullSurplus + " | flexible axis path: " + (c.AxisFlexDetour > 0 ? "up to +" + c.AxisFlexDetour + " zone(s)" : "off") + " | axis paths follow terrain and roads: " + (c.AxisUseTerrain ? "on" : "off"));
            lines.Add("  reinforcements follow the front " + c.AxisReinforcement + " | empty zones follow influence " + c.EmptyZoneInfluence
                + " (decay " + c.InfluenceDecay.ToString("0.00") + ") | advance on conquest " + c.AdvanceOnConquest + " | zones played in DCS depth " + c.DcsZoneDepth + " | objective garrison " + c.ObjectiveGarrison);
            lines.Add("  terrain defense: " + string.Join(", ", c.TerrainDefense.OrderBy(k => k.Key).Select(k => k.Key + " x" + k.Value.ToString("0.00"))));
            lines.Add("  route recovery:  " + string.Join(", ", c.RouteRecovery.OrderBy(k => k.Key).Select(k => k.Key + " x" + k.Value.ToString("0.00"))));
            lines.Add("  route combat:    " + string.Join(", ", c.RouteCombat.OrderBy(k => k.Key).Select(k => k.Key + " x" + k.Value.ToString("0.00"))));
            lines.Add("  supply network " + c.SupplyNetwork + " | range " + c.SupplyRange.ToString("0.0") + " | cut-off level " + c.SupplyMinLevel.ToString("0.00")
                + " | bridge down x" + c.BridgeDownFactor.ToString("0.00") + " | source full at " + c.SupplySourceFull.ToString("0"));
            lines.Add("  terrain supply cost: " + string.Join(", ", c.TerrainMove.OrderBy(k => k.Key).Select(k => k.Key + " x" + k.Value.ToString("0.00"))));
            lines.Add("  route supply flow:   " + string.Join(", ", c.RouteFlow.OrderBy(k => k.Key).Select(k => k.Key + " x" + k.Value.ToString("0.00"))));
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
                    captions[z.Id] = "vide";
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

        // Deux points par zone : les formations ENGAGEES (zone au contact d'une zone ennemie : les MaxPerZone plus fortes
        // se battent, comme dans le moteur) et le SURPLUS (le reste : formations en trop dans une zone au contact, ou
        // formations d'une zone qui ne touche aucune zone ennemie : en marche ou en attente).
        // La taille de chaque point suit sa force.
        public Dictionary<string, WargameZoneDot> BuildDots()
        {
            var dots = new Dictionary<string, WargameZoneDot>(StringComparer.OrdinalIgnoreCase);

            foreach (WargameZoneData z in Zones)
            {
                List<WargameFormation> live = z.Formations.Where(f => f.ForcePower > 0).OrderByDescending(f => f.ForcePower).ToList();
                if (live.Count == 0) continue;

                int engagedCount = TouchesEnemy(z) ? Math.Min(live.Count, Math.Max(1, MaxPerZone)) : 0;

                var dot = new WargameZoneDot { Side = z.Control };

                for (int i = 0; i < live.Count; i++)
                {
                    if (i < engagedCount) { dot.Count++; dot.Force += live[i].ForcePower; }
                    else { dot.Surplus++; dot.SurplusForce += live[i].ForcePower; }
                }

                dots[z.Id] = dot;
            }

            return dots;
        }

        // Vrai si la zone (tenue par un camp) touche une zone tenue par l'autre camp
        private bool TouchesEnemy(WargameZoneData zone)
        {
            if (!WargameEngineCombat.IsFightingSide(zone.Control)) return false;

            List<string> neighbors;
            if (!_links.TryGetValue(zone.Id, out neighbors)) return false;

            foreach (string id in neighbors)
            {
                WargameZoneData n;
                if (_byId.TryGetValue(id, out n)
                    && WargameEngineCombat.IsFightingSide(n.Control)
                    && !string.Equals(n.Control, zone.Control, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // Les flèches de combat : pour chaque zone, une flèche vers chaque zone ennemie à laquelle elle fait face.
        // Grosse = la force principale engagée de ce côté, petite = une garde de flanc (épaisseur = part de la force).
        public List<WargameZoneArrow> BuildArrows()
        {
            var arrows = new List<WargameZoneArrow>();
            if (_facing.Count == 0) return arrows;

            foreach (WargameZoneData z in Zones)
            {
                var byTarget = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                double total = 0;

                foreach (WargameFormation f in z.Formations)
                {
                    if (f.ForcePower <= 0) continue;

                    string targetId;
                    if (!_facing.TryGetValue(f.FormationId, out targetId)) continue;

                    // la cible doit être une zone ENNEMIE à l'instant présent
                    WargameZoneData target;
                    if (!_byId.TryGetValue(targetId, out target)
                        || !WargameEngineCombat.IsFightingSide(target.Control)
                        || string.Equals(target.Control, z.Control, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double old;
                    byTarget.TryGetValue(targetId, out old);
                    byTarget[targetId] = old + f.ForcePower;
                    total += f.ForcePower;
                }

                if (total <= 0) continue;

                foreach (KeyValuePair<string, double> kv in byTarget)
                    arrows.Add(new WargameZoneArrow { FromId = z.Id, ToId = kv.Key, Force = kv.Value, Share = kv.Value / total, Side = z.Control });
            }

            return arrows;
        }

        public string BuildStatus(int maxTurns)
        {
            int blueZones = Zones.Count(z => !WargameEngineCombat.IsReserveZone(z) && z.Control == WargameSide.Blue);
            int redZones = Zones.Count(z => !WargameEngineCombat.IsReserveZone(z) && z.Control == WargameSide.Red);

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

            return "Tour " + Turn + " / " + maxTurns + (Clock == null ? "" : "  -  " + TimeText(Turn)) + "\r\n"
                + _info.BlueLabel + " : " + blueZones + " zone(s), force " + WargameCombatPreview.SideForce(Zones, WargameSide.Blue).ToString("0")
                + "   |   "
                + _info.RedLabel + " : " + redZones + " zone(s), force " + WargameCombatPreview.SideForce(Zones, WargameSide.Red).ToString("0")
                + "\r\nJouées dans DCS (*) : " + inDcs + " / " + total + " formation(s)";
        }
    }

    // Ce que la carte dessine dans une zone : un point (nombre de formations dedans, taille = force)
    internal class WargameZoneDot
    {
        public int Count;            // formations ENGAGEES : dans une zone au contact de l'ennemi
        public double Force;
        public int Surplus;          // formations pas au contact (en marche, en attente)
        public double SurplusForce;
        public string Side;
    }

    // Une flèche de combat : de la zone FromId vers la zone ennemie ToId. Share = part de la force de la zone
    // tournée vers cette voisine (1 = tout, petit = une garde de flanc) : sert à l'épaisseur.
    internal class WargameZoneArrow
    {
        public string FromId;
        public string ToId;
        public double Force;
        public double Share;
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
        private ComboBox _comboStop;

        // Événements programmés (onglet "Événements")
        private TabControl _tabs;
        private TabPage _tabEvents;
        private List<PreviewEvent> _events = new List<PreviewEvent>();
        private ComboBox _comboEvTarget;
        private NumericUpDown _numEvTurn;
        private ComboBox _comboEvAction;
        private ListView _listEvents;
        private NumericUpDown _numVariance;
        private NumericUpDown _numLossBlue;
        private NumericUpDown _numLossRed;
        private NumericUpDown _numAirBonus;
        private NumericUpDown _numRetreatLoss;
        private NumericUpDown _numMaxPerZone;
        private NumericUpDown _numDisband;
        private CheckBox _checkShowAxes;
        private CheckBox _checkShowArrows;
        private CheckBox _checkShowSupply;
        private NumericUpDown _numAxisOpacity;
        private ComboBox _comboAir;
        private NumericUpDown _numDelay;
        private NumericUpDown _numRuns;
        private CheckBox _checkAllAxes;
        private Button _buttonPlay;
        private Button _buttonStop;
        private CheckBox _checkConvoys;
        private Label _labelConvoyWarn;                                                           // avertissement : templates convoy / supplypoint manquants
        private NumericUpDown _numConvoyLossBlue, _numConvoyLossRed, _numAirBlue, _numAirRed;   // % DETRUIT (route, air)
        private CheckBox _checkAirBlue, _checkAirRed;                                             // ravitaillement aérien prévu ou non
        private ListView _listTriggers;
        private Label _labelTriggers;
        private WargameTriggerSim _triggerSim;
        private string _triggerError = "";
        private Label _labelStatus;

        // Date et heure des missions (voir WargameCampClock) : départ = camp_init.lua, durées = conf_mod.lua (jamais modifiées ici)
        private WargameCampClock _clock = new WargameCampClock();
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

            // Ravitaillement : sources et ponts viennent des objectifs de l'éditeur (en Init, tous intacts)
            WargameEngineCombat.ApplyObjectivesToSupply(_zones, _objectives, _info.CombatConfig);

            Text = "Wargame - Aperçu du combat";
            Width = 1450;
            Height = 900;
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterParent;

            LoadEvents();
            BuildUi();
            LoadTriggers();
            ResetSimulation();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            if (_triggerSim != null) _triggerSim.Dispose();
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
            var right = new Panel { Dock = DockStyle.Right, Width = 510 };

            var tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs = tabs;
            var tip0 = new ToolTip { AutoPopDelay = 15000 };

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

            var tabReport = new TabPage("Rapport (plusieurs parties)");
            _txtReport = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                Text = "Joue toute la simulation plusieurs fois, sans la carte, et donne des pourcentages.\r\n\r\n"
                    + "Parties : nombre de parties à jouer.\r\n"
                    + "Utilise les réglages ci-dessus : tours max, hasard, pertes, maîtrise du ciel, perte au repli, max par zone et seuil de dissolution.\r\n"
                    + "Rien n'est enregistré.",
            };

            var reportTop = new Panel { Dock = DockStyle.Top, Height = 40 };
            reportTop.Controls.Add(new Label { Text = "Parties", Location = new Point(8, 13), AutoSize = true });
            _numRuns = new NumericUpDown { Location = new Point(65, 10), Width = 60, Minimum = 1, Maximum = 1000, Value = 50 };
            reportTop.Controls.Add(_numRuns);
            var buttonReport = new Button { Text = "Lancer le rapport", Location = new Point(140, 8), Width = 130, Height = 26 };
            buttonReport.Click += (s, e) => RunReport();
            reportTop.Controls.Add(buttonReport);

            tabReport.Controls.Add(_txtReport);   // Fill d'abord, puis le Top
            tabReport.Controls.Add(reportTop);

            // Triggers de la campagne : liste, état, bouton "Forcer"
            var tabTriggers = new TabPage("Triggers");
            _listTriggers = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
            _listTriggers.Columns.Add("Trigger", 270);
            _listTriggers.Columns.Add("État", 200);

            var triggersTop = new Panel { Dock = DockStyle.Top, Height = 62 };
            _labelTriggers = new Label { Location = new Point(8, 6), Size = new Size(480, 30), Text = "" };
            triggersTop.Controls.Add(_labelTriggers);
            var buttonForce = new Button { Text = "Forcer au prochain tour", Location = new Point(8, 32), Width = 170, Height = 26 };
            buttonForce.Click += (s, e) => ForceSelectedTrigger();
            triggersTop.Controls.Add(buttonForce);
            var buttonReload = new Button { Text = "Relire le fichier", Location = new Point(186, 32), Width = 130, Height = 26 };
            buttonReload.Click += (s, e) => { StopPlay(); LoadTriggers(); ResetSimulation(); };
            triggersTop.Controls.Add(buttonReload);
            tip0.SetToolTip(buttonReload, "Relit camp_triggers_init.lua (si tu l'as modifié) et recommence la simulation.");
            tip0.SetToolTip(buttonForce, "Pour une condition que la preview ne sait pas simuler (cible détruite, etc.) : on suppose qu'elle est vraie et on lance les actions.");

            tabTriggers.Controls.Add(_listTriggers);   // Fill d'abord, puis le Top
            tabTriggers.Controls.Add(triggersTop);

            tabs.TabPages.Add(tabJournal);
            tabs.TabPages.Add(tabTriggers);
            BuildEventsTab(tip0);
            tabs.TabPages.Add(_tabEvents);
            tabs.TabPages.Add(tabReport);

            // Réglages : 4 petits cadres (même logique que le moteur), puis les boutons, puis l'état
            var settings = new Panel { Dock = DockStyle.Top, Height = 700 };
            var tip = new ToolTip { AutoPopDelay = 15000 };

            // Hasard : on part du réglage de la campagne, ou 10 % si elle n'en a pas
            decimal variance = _info.CombatConfig.RandomVariance > 0 ? (decimal)(_info.CombatConfig.RandomVariance * 100) : 10;

            // ---- Cadre 1 : simulation ----
            GroupBox boxSim = AddGroup(settings, "Simulation", 4, 86);

            AddLabel(boxSim, "Tours max", 12, 26);
            _numTurns = AddNumber(boxSim, 150, 22, 60, 20, 1, 500);

            AddLabel(boxSim, "Délai (ms)", 225, 26);
            _numDelay = AddNumber(boxSim, 290, 22, 60, 800, 100, 5000);
            _numDelay.ValueChanged += (s, e) => _timer.Interval = (int)_numDelay.Value;

            _comboStop = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(362, 22), Width = 122 };
            _comboStop.Items.Add("Arrêt : tours max");
            _comboStop.Items.Add("Arrêt : front stable");
            _comboStop.Items.Add("Arrêt : objectifs");
            _comboStop.Items.Add("Arrêt : les deux");
            _comboStop.SelectedIndex = 2;   // par défaut : arrêt quand tous les objectifs sont atteints
            boxSim.Controls.Add(_comboStop);
            tip.SetToolTip(_comboStop, "Quand la lecture s'arrête, en plus du nombre de tours max :\r\n"
                + "- front stable : 3 tours sans aucun mouvement ni changement de zone\r\n"
                + "- objectifs : tous les objectifs des axes actifs sont atteints\r\n"
                + "- les deux : dès que l'un des deux arrive.");

            AddLabel(boxSim, "Hasard (dés) %", 12, 56);
            _numVariance = AddNumber(boxSim, 150, 52, 70, variance, 0, 50);
            _numVariance.ValueChanged += (s, e) => { if (_session != null) _session.Variance = (double)_numVariance.Value / 100.0; };
            tip.SetToolTip(_numVariance, "Part de hasard sur la force de chaque camp à chaque combat (10 = plus ou moins 10 %).");

            _checkAllAxes = new CheckBox { Text = "Tous les axes actifs", Checked = true, Location = new Point(250, 54), AutoSize = true };
            _checkAllAxes.CheckedChanged += (s, e) => ResetSimulation();
            boxSim.Controls.Add(_checkAllAxes);
            tip.SetToolTip(_checkAllAxes, "Coché : tous les axes qui ont un flag sont joués. Décoché : seuls les axes dont le flag est levé dans la campagne.");

            // ---- Cadre 2 : pertes simulées (par camp) ----
            GroupBox boxLoss = AddGroup(settings, "Pertes simulées par tour (à la place de tes missions)", 94, 98);

            AddLabel(boxLoss, _info.BlueLabel + " : % / tour", 12, 26);
            _numLossBlue = AddNumber(boxLoss, 150, 22, 70, 6, 0, 100);
            _numLossBlue.ValueChanged += (s, e) => { if (_session != null) _session.BlueLossPercent = (double)_numLossBlue.Value; };

            AddLabel(boxLoss, _info.RedLabel + " : % / tour", 250, 26);
            _numLossRed = AddNumber(boxLoss, 400, 22, 70, 12, 0, 100);
            _numLossRed.ValueChanged += (s, e) => { if (_session != null) _session.RedLossPercent = (double)_numLossRed.Value; };

            // Explication au survol : cette valeur imite les missions du joueur, avec la loi de la vraie campagne
            string lossHelp = "Remplace tes missions. A chaque tour, chaque formation de première ligne de ce camp perd ce % d'UN SEUL exemplaire posé de son template, "
                + "jamais plus (comme dans la vraie campagne). 100 = l'exemplaire est entièrement détruit. Tirage entre 0,5 et 1,5 fois cette valeur.";
            // Rappel écrit sous les deux cases, pour ne pas avoir à survoler
            boxLoss.Controls.Add(new Label
            {
                Text = "Chaque tour, chaque formation de première ligne de ce camp perd ce % d'UN SEUL exemplaire posé de son template "
                    + "(tirage entre 0,5 et 1,5 fois la valeur). Ex. : exemplaire de force 10 et 20 % -> environ 2 de force perdue par tour, "
                    + "même si la formation vaut x5. 100 = l'exemplaire entier.",
                Location = new Point(12, 52),
                Size = new Size(470, 42),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.DimGray,
            });
            tip.SetToolTip(_numLossBlue, lossHelp);
            tip.SetToolTip(_numLossRed, lossHelp);

            // ---- Cadre 3 : maîtrise du ciel ----
            GroupBox boxAir = AddGroup(settings, "Maîtrise du ciel", 196, 56);

            AddLabel(boxAir, "Camp", 12, 26);
            _comboAir = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 22), Width = 90 };
            _comboAir.Items.Add("aucun");
            _comboAir.Items.Add(_info.BlueLabel);
            _comboAir.Items.Add(_info.RedLabel);

            string airSide = _info.CombatConfig.AirSuperioritySide;
            _comboAir.SelectedIndex = airSide == WargameSide.Blue ? 1 : airSide == WargameSide.Red ? 2 : 0;
            _comboAir.SelectedIndexChanged += (s, e) => ApplyAirToSession();
            boxAir.Controls.Add(_comboAir);

            AddLabel(boxAir, "Bonus de force %", 250, 26);
            _numAirBonus = AddNumber(boxAir, 400, 22, 70, (decimal)(_info.CombatConfig.AirSuperiorityBonus * 100), 0, 200);
            _numAirBonus.ValueChanged += (s, e) => ApplyAirToSession();
            tip.SetToolTip(_numAirBonus, "Le camp qui a la maîtrise du ciel gagne ce % de force au combat (attaque et défense).");

            // ---- Cadre 4 : armée ----
            GroupBox boxArmy = AddGroup(settings, "Armée", 256, 86);

            // Nombre normal de formations par zone (une zone de front en reçoit plus si l'ennemi d'en face est plus fort)
            AddLabel(boxArmy, "Max par zone", 12, 26);
            _numMaxPerZone = AddNumber(boxArmy, 150, 22, 70, _info.CombatConfig.MaxFormationsPerZone, 1, 30);
            _numMaxPerZone.ValueChanged += (s, e) => { if (_session != null) _session.MaxPerZone = (int)_numMaxPerZone.Value; };
            tip.SetToolTip(_numMaxPerZone, "Nombre normal de formations par zone. Une zone de front peut en recevoir plus si l'ennemi d'en face est plus fort.");

            // Force perdue par une formation à chaque repli
            decimal retreatLoss = _info.CombatConfig.RetreatLossFraction > 0 ? (decimal)(_info.CombatConfig.RetreatLossFraction * 100) : 5;
            AddLabel(boxArmy, "Perte au repli %", 250, 26);
            _numRetreatLoss = AddNumber(boxArmy, 400, 22, 70, retreatLoss, 0, 90);
            _numRetreatLoss.ValueChanged += (s, e) => { if (_session != null) _session.RetreatLossPercent = (double)_numRetreatLoss.Value; };
            tip.SetToolTip(_numRetreatLoss, "Force perdue par une formation à chaque repli.");

            // Une formation qui tombe sous ce % de sa force nominale est dissoute
            AddLabel(boxArmy, "Dissolution sous %", 12, 56);
            _numDisband = AddNumber(boxArmy, 150, 52, 70, (decimal)(_info.CombatConfig.DisbandBelowFraction * 100), 0, 90);
            _numDisband.ValueChanged += (s, e) => { if (_session != null) _session.DisbandPercent = (double)_numDisband.Value; };
            tip.SetToolTip(_numDisband, "Une formation au contact de l'ennemi, sous ce % de sa force nominale, est dissoute.");

            // ---- Cadre 5 : test du ravitaillement (convois routiers et aériens) ----
            GroupBox boxSupply = AddGroup(settings, "Ravitaillement : test des convois", 346, 150);

            _checkConvoys = new CheckBox { Text = "Convois et points de ravitaillement avancés", Checked = _info.CombatConfig.SupplyConvoys, Location = new Point(12, 20), AutoSize = true };
            _checkConvoys.CheckedChanged += (s, e) => ApplySupplyTestToSession();
            boxSupply.Controls.Add(_checkConvoys);
            tip.SetToolTip(_checkConvoys, "Coché : les zones mal ravitaillées reçoivent un convoi qui alimente un point avancé (réserve de quelques tours). "
                + "Remplace le délai de grâce. Par défaut : le réglage supply_convoys de la campagne. Effet au prochain tour.");

            AddLabel(boxSupply, _info.BlueLabel, 292, 22);
            AddLabel(boxSupply, _info.RedLabel, 382, 22);

            AddLabel(boxSupply, "Route : part DÉTRUITE %", 12, 48);
            _numConvoyLossBlue = AddNumber(boxSupply, 290, 44, 70, 0, 0, 100);
            _numConvoyLossRed = AddNumber(boxSupply, 380, 44, 70, 0, 0, 100);

            AddLabel(boxSupply, "Aérien : ravitaillement prévu", 12, 74);
            _checkAirBlue = new CheckBox { Location = new Point(292, 73), AutoSize = true };
            _checkAirRed = new CheckBox { Location = new Point(382, 73), AutoSize = true };
            boxSupply.Controls.Add(_checkAirBlue);
            boxSupply.Controls.Add(_checkAirRed);

            AddLabel(boxSupply, "Aérien : part DÉTRUITE %", 12, 100);
            _numAirBlue = AddNumber(boxSupply, 290, 96, 70, 0, 0, 100);
            _numAirRed = AddNumber(boxSupply, 380, 96, 70, 0, 0, 100);

            // Avertissement (orange) : convois cochés mais templates "convoy" / "supplypoint" absents du catalogue
            _labelConvoyWarn = new Label
            {
                Location = new Point(12, 124),
                Size = new Size(470, 20),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.DarkOrange,
                Visible = false,
            };
            boxSupply.Controls.Add(_labelConvoyWarn);

            // Une valeur de test n'a d'effet qu'avec les convois activés : on coche la case tout seul
            foreach (NumericUpDown n in new[] { _numConvoyLossBlue, _numConvoyLossRed, _numAirBlue, _numAirRed })
            {
                NumericUpDown num = n;
                num.ValueChanged += (s, e) =>
                {
                    if (num.Value > 0 && !_checkConvoys.Checked) _checkConvoys.Checked = true;
                    ApplySupplyTestToSession();
                };
            }
            foreach (CheckBox c in new[] { _checkAirBlue, _checkAirRed })
            {
                CheckBox chk = c;
                chk.CheckedChanged += (s, e) =>
                {
                    if (chk.Checked && !_checkConvoys.Checked) _checkConvoys.Checked = true;
                    ApplySupplyTestToSession();
                };
            }

            tip.SetToolTip(_numConvoyLossBlue, "Part de la cargaison des convois routiers détruite à chaque tour (0 = tout arrive, 100 = tout est détruit). Effet au prochain tour.");
            tip.SetToolTip(_numConvoyLossRed, tip.GetToolTip(_numConvoyLossBlue));
            tip.SetToolTip(_checkAirBlue, "Coché : ce camp ravitaille aussi ses points avancés par air (hélicoptère, aérolargage). "
                + "S'ajoute à la route, et seule façon de ravitailler un secteur sans route (poche, plage isolée).");
            tip.SetToolTip(_checkAirRed, tip.GetToolTip(_checkAirBlue));
            tip.SetToolTip(_numAirBlue, "Part des livraisons aériennes détruite à chaque tour (0 = tout arrive, 100 = tout est abattu). Sans effet si l'aérien n'est pas prévu.");
            tip.SetToolTip(_numAirRed, tip.GetToolTip(_numAirBlue));
            tip.SetToolTip(_labelConvoyWarn, "Étape « Templates » de l'éditeur : donne le type « convoy » (camions) et « supplypoint » à un template de chaque camp. "
                + "Sans eux, le fichier d'ordres pour scriptsMod (wargame_logistics.lua) aura des convois et des points sans véhicules.");

            // ---- Boutons ----
            // 5 boutons de même taille ; lecture et arrêt en icônes de lecteur média (police à symboles, assez grande pour être lisible)
            int by = 504, bw = 91, bh = 34;
            var iconFont = new Font("Segoe UI Symbol", 14f, FontStyle.Bold);

            var buttonNext = new Button { Text = "Tour suivant", Location = new Point(10, by), Width = bw, Height = bh };
            buttonNext.Click += (s, e) => { StopPlay(); DoStep(); };

            _buttonPlay = new Button { Text = "▶", Font = iconFont, ForeColor = Color.DarkGreen, Location = new Point(10 + (bw + 7), by), Width = bw, Height = bh };
            _buttonPlay.Click += (s, e) => { if (!_timer.Enabled) StartPlay(); };
            tip.SetToolTip(_buttonPlay, "Lecture : joue les tours les uns après les autres.");

            _buttonStop = new Button { Text = "■", Font = iconFont, ForeColor = Color.DarkRed, Location = new Point(10 + 2 * (bw + 7), by), Width = bw, Height = bh, Enabled = false };
            _buttonStop.Click += (s, e) => StopPlay();
            tip.SetToolTip(_buttonStop, "Arrêt : stoppe la lecture (l'état reste affiché ; Réinitialiser pour repartir du début).");

            var buttonReset = new Button { Text = "Réinitialiser", Location = new Point(10 + 3 * (bw + 7), by), Width = bw, Height = bh };
            buttonReset.Click += (s, e) => ResetSimulation();

            var buttonSaveLog = new Button { Text = "Journal complet", Location = new Point(10 + 4 * (bw + 7), by), Width = bw, Height = bh };
            buttonSaveLog.Click += (s, e) => SaveFullLog();
            tip.SetToolTip(buttonSaveLog, "Le journal détaillé (Debug\\Wargame\\wargame_preview_log.txt) est enregistré automatiquement à chaque tour. Ce bouton affiche où il se trouve.");

            settings.Controls.Add(buttonNext);
            settings.Controls.Add(_buttonPlay);
            settings.Controls.Add(_buttonStop);
            settings.Controls.Add(buttonReset);
            settings.Controls.Add(buttonSaveLog);

            // ---- Jouer la situation du tour dans DCS (export vers la campagne + SkipMission), et revenir en arrière ----
            var buttonPlayDcs = new Button { Text = "Jouer ce tour dans DCS", Location = new Point(10, by + bh + 6), Width = 2 * bw + 7, Height = bh,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            buttonPlayDcs.Click += (s, e) => PlayTurnInDcs();
            tip.SetToolTip(buttonPlayDcs, "Ecrit la situation du tour affiché dans la campagne (dossier Active, après sauvegarde) puis génère la mission avec SkipMission.");
            settings.Controls.Add(buttonPlayDcs);

            var buttonRestore = new Button { Text = "Restaurer une sauvegarde...", Location = new Point(10 + 2 * (bw + 7), by + bh + 6), Width = 3 * bw + 14, Height = bh };
            buttonRestore.Click += (s, e) => RestoreBackup();
            tip.SetToolTip(buttonRestore, "Remet en place un dossier Active sauvegardé (Active_Backups). L'Active actuel est lui-même mis de côté avant.");
            settings.Controls.Add(buttonRestore);

            // ---- Affichage des axes : on peut les masquer ou les rendre transparents pour voir les points de force ----
            _checkShowAxes = new CheckBox { Text = "Afficher les axes", Checked = true, Location = new Point(10, 588), AutoSize = true };
            _checkShowAxes.CheckedChanged += (s, e) => _mapView.ShowAxes = _checkShowAxes.Checked;
            settings.Controls.Add(_checkShowAxes);

            AddLabel(settings, "Opacité des axes %", 170, 590);
            _numAxisOpacity = AddNumber(settings, 300, 586, 60, 35, 0, 100);
            _numAxisOpacity.ValueChanged += (s, e) => _mapView.AxisOpacity = (float)_numAxisOpacity.Value / 100f;
            _mapView.AxisOpacity = 0.35f;

            _checkShowArrows = new CheckBox { Text = "Flèches", Checked = true, Location = new Point(378, 588), AutoSize = true };
            _checkShowArrows.CheckedChanged += (s, e) => _mapView.ShowArrows = _checkShowArrows.Checked;
            settings.Controls.Add(_checkShowArrows);
            tip.SetToolTip(_checkShowArrows, "Une flèche par zone ennemie à laquelle une zone fait face. Grosse = force principale, petite = garde de flanc.");

            _checkShowSupply = new CheckBox { Text = "Afficher le ravitaillement (couleurs, convois)", Checked = false, Location = new Point(10, 612), AutoSize = true };
            _checkShowSupply.CheckedChanged += (s, e) => RefreshSupplyOverlay();
            settings.Controls.Add(_checkShowSupply);
            tip.SetToolTip(_checkShowSupply, "Colore chaque zone selon son ravitaillement : vert = plein, rouge = faible, gris hachuré = coupée. S = source. Survol = détail.");

            _labelStatus = new Label
            {
                Location = new Point(10, 634),
                Size = new Size(485, 58),
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
                    Append("Nombre de tours max atteint.");
                    StopPlay();
                }
                else if (_session.LandingPending())
                {
                    // un débarquement reste à faire : on continue
                }
                else if ((_comboStop.SelectedIndex == 1 || _comboStop.SelectedIndex == 3) && _session.QuietTurns >= 3)
                {
                    Append("Front stable depuis 3 tours, arrêt.");
                    StopPlay();
                }
                else if ((_comboStop.SelectedIndex == 2 || _comboStop.SelectedIndex == 3) && _session.AllObjectivesReached())
                {
                    Append("Tous les objectifs des axes sont atteints, arrêt.");
                    StopPlay();
                }
            };
        }

        // Petit cadre titré : les contrôles posés dedans ont des coordonnées relatives au cadre
        private static GroupBox AddGroup(Control parent, string title, int y, int height)
        {
            var box = new GroupBox { Text = title, Location = new Point(6, y), Size = new Size(494, height) };
            parent.Controls.Add(box);
            return box;
        }

        private static void AddLabel(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
        }

        private static NumericUpDown AddNumber(Control parent, int x, int y, int width, decimal value, decimal min, decimal max)
        {
            var num = new NumericUpDown
            {
                Location = new Point(x, y),
                Width = width,
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
            };

            parent.Controls.Add(num);
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

            WargameAxisPathFinder.CostConfig = _info.CombatConfig;   // les chemins suivent le relief et les routes selon les réglages de la campagne
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
            ApplySupplyTestToSession();

            // Triggers de la campagne : lus UNE fois à l'ouverture (bouton "Relire" pour les relire), ici on remet juste l'état à zéro
            if (_triggerSim != null) _triggerSim.Reset();
            _session.Triggers = _triggerSim;
            _session.Clock = _clock;
            _session.TriggersNote = _triggerError;
            _session.Events = _events;
            _session.SetObjectives(_objectives);

            _session.WriteHeader(_campaignName);

            _txtJournal.Text = "Départ. Clique sur \"Tour suivant\" ou \"Lecture\".\r\n";
            SaveFullLog(true);   // le fichier repart avec l'en-tête de cette nouvelle simulation

            _mapView.LoadMap(_mapImage, _calibration, _session.Zones);
            _mapView.SetAxes(_axes);
            _mapView.SetObjectives(_objectives);
            _mapView.ObjectiveRightClicked -= MapObjectiveRightClicked;
            _mapView.ObjectiveRightClicked += MapObjectiveRightClicked;

            RefreshMap();
        }

        // Lit et compile camp_triggers_init.lua (à l'ouverture, ou sur demande avec le bouton Relire)
        private void LoadTriggers()
        {
            if (_triggerSim != null) _triggerSim.Dispose();

            _triggerSim = WargameTriggerSim.Load(TriggersPath(), out _triggerError);

            LoadClock();
            if (_triggerSim != null) _triggerSim.Clock = _clock;

            _labelTriggers.Text = _triggerSim == null
                ? "Aucun trigger lu : " + _triggerError
                : _triggerSim.Triggers.Count + " trigger(s) lus dans camp_triggers_init.lua (lus une seule fois). Rejoués au début de chaque tour : condition vraie -> actions.";
        }

        // -------------------- Événements programmés --------------------

        private class ObjectiveItem
        {
            public WargameObjective Obj;
            public override string ToString()
            {
                return Obj.Name + (Obj.IsBridge ? "  (pont)" : "") + (Obj.SupplyValue > 0 ? "  (source " + Obj.SupplyValue.ToString("0") + ")" : "");
            }
        }

        private string EventsPath()
        {
            return Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Init", "Wargame", "preview_events.json");
        }

        private void LoadEvents()
        {
            _events = new List<PreviewEvent>();

            try
            {
                if (File.Exists(EventsPath()))
                    _events = JsonConvert.DeserializeObject<List<PreviewEvent>>(File.ReadAllText(EventsPath())) ?? new List<PreviewEvent>();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("WargameCombatPreview | erreur lecture preview_events.json : " + ex.Message);
            }
        }

        private void SaveEvents()
        {
            try
            {
                string path = EventsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(_events, Formatting.Indented));
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("WargameCombatPreview | erreur écriture preview_events.json : " + ex.Message);
            }
        }

        private void BuildEventsTab(ToolTip tip)
        {
            _tabEvents = new TabPage("Événements");

            var top = new Panel { Dock = DockStyle.Top, Height = 104 };

            top.Controls.Add(new Label { Text = "Cible", Location = new Point(8, 12), AutoSize = true });
            _comboEvTarget = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(60, 8), Width = 320 };
            foreach (WargameObjective o in _objectives.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                _comboEvTarget.Items.Add(new ObjectiveItem { Obj = o });
            if (_comboEvTarget.Items.Count > 0) _comboEvTarget.SelectedIndex = 0;
            top.Controls.Add(_comboEvTarget);

            top.Controls.Add(new Label { Text = "Tour", Location = new Point(8, 42), AutoSize = true });
            _numEvTurn = new NumericUpDown { Location = new Point(60, 38), Width = 60, Minimum = 1, Maximum = 500, Value = 4 };
            top.Controls.Add(_numEvTurn);

            _comboEvAction = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(135, 38), Width = 100 };
            _comboEvAction.Items.Add("Détruire");
            _comboEvAction.Items.Add("Réparer");
            _comboEvAction.SelectedIndex = 0;
            top.Controls.Add(_comboEvAction);

            var buttonAdd = new Button { Text = "Ajouter", Location = new Point(250, 36), Width = 80, Height = 26 };
            buttonAdd.Click += (s, e) => AddEventFromControls();
            top.Controls.Add(buttonAdd);

            var buttonDelete = new Button { Text = "Supprimer", Location = new Point(336, 36), Width = 90, Height = 26 };
            buttonDelete.Click += (s, e) => DeleteSelectedEvents();
            top.Controls.Add(buttonDelete);

            top.Controls.Add(new Label
            {
                Text = "Clic droit sur un objectif de la carte = ajout rapide. Un événement s'applique au début de son tour : "
                    + "objectif détruit = plus de ravitaillement (source) ou route coupée (pont). Valable pour la lecture et le rapport.",
                Location = new Point(8, 68),
                Size = new Size(480, 34),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.DimGray,
            });

            _listEvents = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
            _listEvents.Columns.Add("Tour", 50);
            _listEvents.Columns.Add("Action", 80);
            _listEvents.Columns.Add("Cible", 330);
            _listEvents.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteSelectedEvents(); };

            _tabEvents.Controls.Add(_listEvents);   // Fill d'abord, puis le Top
            _tabEvents.Controls.Add(top);

            tip.SetToolTip(_numEvTurn, "Numéro du tour au début duquel l'événement s'applique (1 = premier tour).");

            RefreshEventList();
        }

        private void RefreshEventList()
        {
            _listEvents.BeginUpdate();
            _listEvents.Items.Clear();

            foreach (PreviewEvent ev in _events.OrderBy(x => x.Turn).ThenBy(x => x.Target, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(ev.Turn.ToString());
                item.SubItems.Add(ev.Action == PreviewEvent.Destroy ? "Détruire" : "Réparer");
                item.SubItems.Add(ev.Target);
                item.Tag = ev;
                _listEvents.Items.Add(item);
            }

            _listEvents.EndUpdate();
        }

        private void AddEvent(int turn, string action, string target)
        {
            // Même cible, même tour : le dernier choix remplace l'ancien
            _events.RemoveAll(x => x.Turn == turn && string.Equals(x.Target, target, StringComparison.OrdinalIgnoreCase));
            _events.Add(new PreviewEvent { Turn = turn, Action = action, Target = target });

            SaveEvents();
            RefreshEventList();
        }

        private void AddEventFromControls()
        {
            ObjectiveItem item = _comboEvTarget.SelectedItem as ObjectiveItem;
            if (item == null) return;

            AddEvent((int)_numEvTurn.Value, _comboEvAction.SelectedIndex == 0 ? PreviewEvent.Destroy : PreviewEvent.Repair, item.Obj.Name);
        }

        private void DeleteSelectedEvents()
        {
            foreach (ListViewItem item in _listEvents.SelectedItems)
                _events.Remove((PreviewEvent)item.Tag);

            SaveEvents();
            RefreshEventList();
        }

        // Clic droit sur un objectif de la carte : menu d'ajout rapide
        private void MapObjectiveRightClicked(WargameObjective obj, Point screenPoint)
        {
            for (int i = 0; i < _comboEvTarget.Items.Count; i++)
            {
                ObjectiveItem item = (ObjectiveItem)_comboEvTarget.Items[i];
                if (item.Obj == obj) { _comboEvTarget.SelectedIndex = i; break; }
            }

            int nextTurn = _session.Turn + 1;
            int chosenTurn = (int)_numEvTurn.Value;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Détruire au prochain tour (" + nextTurn + ")", null, (s, e) => AddEvent(nextTurn, PreviewEvent.Destroy, obj.Name));
            menu.Items.Add("Détruire au tour " + chosenTurn + " (réglé dans l'onglet)", null, (s, e) => AddEvent(chosenTurn, PreviewEvent.Destroy, obj.Name));
            menu.Items.Add("Réparer au tour " + chosenTurn, null, (s, e) => AddEvent(chosenTurn, PreviewEvent.Repair, obj.Name));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Retirer les événements de " + obj.Name, null, (s, e) =>
            {
                _events.RemoveAll(x => string.Equals(x.Target, obj.Name, StringComparison.OrdinalIgnoreCase));
                SaveEvents();
                RefreshEventList();
            });
            menu.Items.Add("Ouvrir l'onglet Événements", null, (s, e) => _tabs.SelectedTab = _tabEvents);

            menu.Show(screenPoint);
        }

        // Lit les temps de conf_mod.lua (Init d'abord, puis la racine de la campagne) et met les sélecteurs à jour
        private void LoadClock()
        {
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(_campaignName);
            string path = Path.Combine(campaignFolder, "Init", "conf_mod.lua");
            if (!File.Exists(path)) path = Path.Combine(campaignFolder, "conf_mod.lua");

            string campInit = Path.Combine(campaignFolder, "Init", "camp_init.lua");
            if (!File.Exists(campInit)) campInit = Path.Combine(campaignFolder, "camp_init.lua");

            string note;
            _clock = WargameCampClock.FromConfMod(path, campInit, out note);

            if (!string.IsNullOrEmpty(note))
                FormUtils.LogRegister("WargameCombatPreview | horloge : " + note);
        }

        // Ecrit la situation du tour affiché dans la campagne (après sauvegarde de Active), puis lance SkipMission
        private void PlayTurnInDcs()
        {
            if (_session == null || _session.Turn < 1)
            {
                MessageBox.Show("Joue au moins un tour avant de lancer une mission.", "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            StopPlay();

            string folder = WargameZoneRepository.GetCampaignFolder(_campaignName);
            string bat = Path.Combine(folder, "SkipMission.bat");
            if (!File.Exists(bat))
            {
                MessageBox.Show("SkipMission.bat introuvable dans le dossier de la campagne :\n" + folder, "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int turn = _session.Turn;
            DialogResult answer = MessageBox.Show(
                "Jouer la situation du tour " + turn + " dans DCS ?\n\n"
                + "ATTENTION : l'état actuel de la campagne (dossier Active) va être ÉCRASÉ : zones et formations du wargame, cibles, "
                + "unités au sol de l'ancien DCE (pertes et zones capturées), flags, mission n°" + turn + ", date et heure du tour.\n\n"
                + "Une sauvegarde complète du dossier Active est faite avant, dans Active_Backups "
                + "(bouton « Restaurer une sauvegarde » pour revenir en arrière).\n\n"
                + "DCS DOIT ÊTRE FERMÉ : le carnet de vol (logbook.lua) est modifié pour que DCS lance la mission « _ongoing » "
                + "(la 1re mission est notée comme jouée).\n\nContinuer ?",
                "Wargame - jouer ce tour", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes) return;

            // Tout le détail de l'export va dans un fichier du journal wargame (Debug\Wargame\wargame_M0xx_PreviewExport_...)
            WargameLog.Begin(_campaignName, "PreviewExport", turn);
            try
            {
                string backup;
                try
                {
                    backup = WargamePreviewExport.BackupActive(_campaignName, "Preview_T" + turn.ToString("00"));
                    WargameLog.Line("Active backup : " + backup);
                }
                catch (Exception ex)
                {
                    WargameLog.Line("ERROR backup : " + ex.Message + " (nothing changed)");
                    MessageBox.Show("Sauvegarde impossible, rien n'a été modifié :\n" + ex.Message, "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                List<string> report;
                Cursor = Cursors.WaitCursor;
                try
                {
                    report = WargamePreviewExport.Export(_campaignName, _session, _clock, new Random());
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    WargameLog.Line("ERROR export : " + ex);
                    MessageBox.Show("Erreur pendant l'écriture de la campagne :\n" + ex.Message
                        + "\n\nLa campagne est peut-être à moitié modifiée : restaure la sauvegarde\n" + backup,
                        "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Cursor = Cursors.Default;

                Append("=== Tour " + turn + " écrit dans la campagne ===");
                foreach (string line in report) Append("  " + line);
                Append("  sauvegarde de l'Active d'avant : " + backup);

                // Progression de campagne DCS (logbook.lua) : la 1re mission passe pour jouée, DCS ouvrira _ongoing.miz
                string logbookMessage;
                DcsLogbook.TrySkipFirstMission(DcsLogbook.ReadSavedGamesPath(folder), _campaignName, out logbookMessage);
                Append("  " + logbookMessage);
                WargameLog.Line(logbookMessage);
                WargameLog.Line("preview log of this simulation : " + Path.Combine(WargameLog.Folder(_campaignName), "wargame_preview_log.txt"));
                WargameLog.Line("SkipMission started (ScriptsMod window)");
                Append("  journal : " + WargameLog.Folder(_campaignName));
            }
            finally
            {
                WargameLog.End();
            }

            using (var runner = new ScriptsModRunner_Form(bat, folder, _campaignName, null))
            {
                runner.ShowDialog(this);
            }
        }

        // Remet en place un dossier Active sauvegardé
        private void RestoreBackup()
        {
            string root = WargamePreviewExport.BackupRoot(_campaignName);
            if (!Directory.Exists(root) || Directory.GetDirectories(root).Length == 0)
            {
                MessageBox.Show("Aucune sauvegarde trouvée dans :\n" + root, "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string chosen;
            using (var dialog = new FolderBrowserDialog { Description = "Choisis la sauvegarde à remettre en place (un dossier dans Active_Backups)", SelectedPath = root, ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                chosen = dialog.SelectedPath;
            }

            if (!File.Exists(Path.Combine(chosen, "camp_status.lua")))
            {
                MessageBox.Show("Ce dossier n'est pas une sauvegarde de l'Active (pas de camp_status.lua) :\n" + chosen, "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show("Remettre en place cette sauvegarde ?\n\n" + chosen + "\n\nL'Active actuel sera mis de côté dans Active_Backups (BeforeRestore_...).",
                    "Wargame - restaurer", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            try
            {
                WargamePreviewExport.RestoreActive(_campaignName, chosen);
                Append("Sauvegarde remise en place : " + chosen);

                WargameLog.Begin(_campaignName, "RestoreBackup", WargameLogistics.ReadMission(WargameZoneRepository.GetCampaignFolder(_campaignName)));
                WargameLog.Line("Active folder restored from : " + chosen);
                WargameLog.Line("the Active folder before the restore was kept in Active_Backups (BeforeRestore_...)");
                WargameLog.End();
                MessageBox.Show("Sauvegarde remise en place.", "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Restauration impossible :\n" + ex.Message, "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Valeurs de test du ravitaillement (cadre "Ravitaillement : test des convois") vers la session : effet au prochain tour
        private void ApplySupplyTestToSession()
        {
            if (_session == null || _checkConvoys == null) return;

            _session.ConvoysOn = _checkConvoys.Checked;
            _session.BlueConvoyLossPercent = (double)_numConvoyLossBlue.Value;
            _session.RedConvoyLossPercent = (double)_numConvoyLossRed.Value;
            _session.BlueAirPercent = _checkAirBlue.Checked ? 100.0 - (double)_numAirBlue.Value : 0;   // part LIVREE
            _session.RedAirPercent = _checkAirRed.Checked ? 100.0 - (double)_numAirRed.Value : 0;

            UpdateConvoyWarning();
        }

        // Case des convois cochée mais templates "convoy" / "supplypoint" absents du catalogue : ligne orange sous les réglages
        private void UpdateConvoyWarning()
        {
            if (_labelConvoyWarn == null || _checkConvoys == null) return;

            List<string> missing = _checkConvoys.Checked
                ? WargameCombatPreview.MissingTemplates(_catalog, _info, true, false)
                : new List<string>();

            _labelConvoyWarn.Visible = missing.Count > 0;
            if (missing.Count > 0)
                _labelConvoyWarn.Text = "⚠ Templates missing: " + string.Join(", ", missing) + " (editor, step 5)";
        }

        private string TriggersPath()
        {
            return Path.Combine(WargameZoneRepository.GetCampaignFolder(_campaignName), "Init", "camp_triggers_init.lua");
        }

        // Remet la liste des triggers à jour (état de chacun)
        private void RefreshTriggers()
        {
            _listTriggers.BeginUpdate();
            _listTriggers.Items.Clear();

            if (_triggerSim != null)
            {
                foreach (WargameTrigger t in _triggerSim.Triggers)
                {
                    string state = t.Forced ? "forced: fires next turn" : t.Status;
                    var item = new ListViewItem(t.Name) { Tag = t };
                    item.SubItems.Add(state);
                    if (t.Fired) item.ForeColor = Color.DarkGreen;
                    else if (state.StartsWith("cannot") || state.StartsWith("condition")) item.ForeColor = Color.DarkOrange;
                    _listTriggers.Items.Add(item);
                }
            }

            _listTriggers.EndUpdate();
        }

        private void ForceSelectedTrigger()
        {
            if (_triggerSim == null || _listTriggers.SelectedItems.Count == 0) return;

            WargameTrigger t = _listTriggers.SelectedItems[0].Tag as WargameTrigger;
            if (t == null) return;

            _triggerSim.Force(t);
            Append("Trigger forcé : " + t.Name + " (ses actions partiront au début du prochain tour).");
            RefreshTriggers();
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

        // Journal complet de la Preview : <campagne>\Debug\Wargame\wargame_preview_log.txt, réécrit AUTOMATIQUEMENT
        // au départ et après chaque tour (toujours à jour, sans passer par le bouton). quiet = sans message dans le journal.
        private void SaveFullLog(bool quiet = false)
        {
            if (_session == null) return;
            string path = Path.Combine(WargameLog.Folder(_campaignName), "wargame_preview_log.txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                _session.SaveFullLog(path);
                if (!quiet) Append("Journal complet enregistré : " + path);
            }
            catch (Exception ex)
            {
                Append("Journal complet NON enregistré : " + ex.Message);
            }
        }

        private void DoStep()
        {
            List<string> lines = _session.Step();
            Append(string.Join("\r\n", lines));
            RefreshMap();
            SaveFullLog(true);
        }

        private void RefreshMap()
        {
            _mapView.SetZoneCaptions(_session.BuildCaptions());
            _mapView.SetZoneDots(_session.BuildDots());
            _mapView.SetZoneArrows(_session.BuildArrows());
            _mapView.SetFlashZones(_session.LastFlippedIds);
            _mapView.SetDestroyedObjectives(_session.DestroyedObjectives());
            RefreshSupplyOverlay();
            _labelStatus.Text = _session.BuildStatus((int)_numTurns.Value);
            RefreshTriggers();
        }

        // Calque ravitaillement : recalculé à chaque rafraîchissement de la carte (tour, reset, événement)
        private void RefreshSupplyOverlay()
        {
            if (_mapView == null || _session == null) return;

            bool show = _checkShowSupply != null && _checkShowSupply.Checked;
            _mapView.SetSupplyOverlay(show ? _session.BuildSupplyMap() : null);
            _mapView.SetConvoys(show ? _session.Convoys : null);
        }

        private void Append(string text)
        {
            _txtJournal.AppendText(text + "\r\n");
        }

        private void StartPlay()
        {
            _timer.Interval = (int)_numDelay.Value;
            _timer.Start();
            _buttonPlay.Enabled = false;
            _buttonStop.Enabled = true;
        }

        private void StopPlay()
        {
            _timer.Stop();
            _buttonPlay.Enabled = true;
            _buttonStop.Enabled = false;
        }

        // -------------------- Rapport texte (N parties d'un coup) --------------------

        private void RunReport()
        {
            Cursor = Cursors.WaitCursor;
            WargameTriggerSim reportSim = null;
            try
            {
                // Simulation de triggers à part (mêmes triggers, déjà lus) pour ne pas toucher à la partie en cours
                if (_triggerSim != null) reportSim = _triggerSim.CloneFresh();

                WargameAxisPathFinder.CostConfig = _info.CombatConfig;   // les chemins suivent le relief et les routes selon les réglages de la campagne
            WargameAxisPathFinder.RecomputeAll(_axes, _zones);
            WargameAxisPathFinder.RememberAttackers(_axes, _zones);   // attaquant = camp qui tient la zone de départ MAINTENANT

                _txtReport.Text = WargameCombatPreview.Run(
                    _zones, _axes, ComputeFlags(), _catalog, _info,
                    (int)_numTurns.Value, (int)_numRuns.Value,
                    (double)_numVariance.Value / 100.0, (double)_numLossBlue.Value, (double)_numLossRed.Value,
                    AirSideFromCombo(), (double)_numAirBonus.Value / 100.0,
                    (double)_numRetreatLoss.Value, (int)_numMaxPerZone.Value, (double)_numDisband.Value,
                    reportSim, _events, _objectives);
            }
            catch (Exception ex)
            {
                _txtReport.Text = "Echec du rapport : " + ex.Message;
            }
            finally
            {
                if (reportSim != null) reportSim.Dispose();
                Cursor = Cursors.Default;
            }
        }
    }
}
