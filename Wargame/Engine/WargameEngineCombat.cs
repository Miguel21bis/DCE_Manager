using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Réglages du combat. Tout est ici, à un seul endroit, pour pouvoir équilibrer
    // une campagne sans toucher au reste du moteur.
    internal class WargameCombatConfig
    {
        // Une zone ne bascule que si |Delta| dépasse ce seuil. Delta va de -1 (défense
        // écrasante) à +1 (attaque écrasante) ; 0.25 = l'attaque doit valoir environ
        // 1.7 x la défense pour percer.
        public double ControlFlipThreshold = 0.25;

        // Part de ForcePower perdue par une formation qui se replie (0 = repli sans perte).
        public double RetreatLossFraction = 0.0;

        // Multiplicateur de ravitaillement d'une zone COUPEE de toute source amie
        // (poche encerclée). 1 = la coupure ne change rien.
        public double CutOffSupplyFactor = 0.5;

        // Variation aléatoire de la force d'attaque : 0 = aucun hasard (combat
        // reproductible), 0.1 = attaque multipliée par un facteur entre 0.9 et 1.1.
        public double RandomVariance = 0.0;

        // Valeurs utilisées par le moteur. Modifier ici pour changer l'équilibrage.
        public static readonly WargameCombatConfig Current = new WargameCombatConfig();
    }

    // Ce qui s'est passé pendant la résolution, pour que l'appelant (WargameEngineLosses)
    // sache quoi reposer et quels objectifs changer de camp.
    internal class WargameCombatResult
    {
        public List<WargameZoneData> FlippedZones = new List<WargameZoneData>();

        // Formations déplacées vers une autre zone : à reposer là-bas
        public HashSet<int> MovedFormationIds = new HashSet<int>();

        public int Destroyed;
    }

    // Résolution du combat entre zones (passe 1), lancée par WargameEngineLosses juste
    // après l'application des pertes de la mission.
    //
    // On ne regarde QUE les axes actifs (flag posé par un trigger de campagne), et sur
    // chaque axe, les paires de zones consécutives de contrôle différent :
    //
    //   attaquant = le camp qui tient la zone de départ (A) de l'axe
    //   attaque   = ForcePower du camp attaquant dans sa zone x ravitaillement de la zone
    //   défense   = ForcePower du défenseur dans sa zone x ravitaillement x coef. de terrain
    //   Delta     = (attaque - défense) / (attaque + défense)
    //
    //   Delta >  seuil : la zone défendue bascule chez l'attaquant
    //   Delta < -seuil : contre-attaque, la zone attaquante bascule chez le défenseur
    //   sinon          : le front ne bouge pas
    //
    // Toutes les paires sont jugées sur l'état de DEPART (résolution simultanée) ; les
    // plus nettes (|Delta| le plus grand) sont appliquées en premier, et une zone ne
    // change de main qu'une fois par tour.
    //
    // Quand une zone bascule, les formations du camp qui la perd se replient : d'abord
    // en arrière SUR LE MEME AXE, sinon vers la voisine amie la plus éloignée de
    // l'attaque, sinon elles sont détruites (poche sans issue).
    internal static class WargameEngineCombat
    {
        private class FlipCandidate
        {
            public WargameAxis Axis;
            public WargameZoneData Loser;
            public WargameZoneData From;     // zone d'où part l'attaque victorieuse
            public int LoserIndex;           // positions sur l'axe
            public int FromIndex;
            public string WinnerSide;
            public string LoserSide;
            public double Delta;
            public double Attack;
            public double Defense;
        }

        public static WargameCombatResult Resolve(
            List<WargameZoneData> zones,
            List<WargameAxis> axes,
            HashSet<string> activeFlags,
            WargameCombatConfig config,
            Random rng)
        {
            var result = new WargameCombatResult();

            if (zones == null || axes == null || axes.Count == 0)
                return result;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData zone in zones)
                zoneById[zone.Id] = zone;

            Dictionary<string, List<string>> links = BuildLinks(zones);
            HashSet<string> cutOff = FindCutOffZones(zones, links, zoneById);

            // ---- 1. Juger toutes les paires, sur l'état de départ ----

            var candidates = new List<FlipCandidate>();
            int activeAxes = 0;

            foreach (WargameAxis axis in axes)
            {
                if (!axis.IsActive(activeFlags))
                    continue;

                WargameZoneData startZone;
                if (!zoneById.TryGetValue(axis.Path[0], out startZone) || !IsFightingSide(startZone.Control))
                {
                    FormUtils.LogRegister("WargameEngineCombat | axe '" + axis.Name + "' ignoré : sa zone de départ n'est tenue ni par le bleu ni par le rouge");
                    continue;
                }

                activeAxes++;
                string attackSide = startZone.Control;
                string defendSide = OtherSide(attackSide);

                for (int i = 0; i < axis.Path.Count - 1; i++)
                {
                    WargameZoneData a, b;
                    if (!zoneById.TryGetValue(axis.Path[i], out a) || !zoneById.TryGetValue(axis.Path[i + 1], out b))
                        continue;

                    if (!IsFightingSide(a.Control) || !IsFightingSide(b.Control))
                        continue;

                    if (string.Equals(a.Control, b.Control, StringComparison.OrdinalIgnoreCase))
                        continue;   // pas de front entre ces deux-là

                    bool aAttacks = string.Equals(a.Control, attackSide, StringComparison.OrdinalIgnoreCase);
                    WargameZoneData attZone = aAttacks ? a : b;
                    WargameZoneData defZone = aAttacks ? b : a;
                    int attIndex = aAttacks ? i : i + 1;
                    int defIndex = aAttacks ? i + 1 : i;

                    double attack = SidePower(attZone, attackSide) * SupplyFactor(attZone, cutOff, config) * Luck(config, rng);
                    double defense = SidePower(defZone, defendSide) * SupplyFactor(defZone, cutOff, config)
                        * WargameTerrain.GetDefenseCoef(defZone.Terrain);

                    double total = attack + defense;
                    if (total <= 0)
                        continue;   // deux zones vides face à face : rien ne se passe

                    double delta = (attack - defense) / total;
                    if (Math.Abs(delta) <= config.ControlFlipThreshold)
                        continue;

                    var candidate = new FlipCandidate { Axis = axis, Delta = delta, Attack = attack, Defense = defense };

                    if (delta > 0)
                    {
                        candidate.Loser = defZone; candidate.LoserIndex = defIndex;
                        candidate.From = attZone; candidate.FromIndex = attIndex;
                        candidate.WinnerSide = attackSide; candidate.LoserSide = defendSide;
                    }
                    else
                    {
                        candidate.Loser = attZone; candidate.LoserIndex = attIndex;
                        candidate.From = defZone; candidate.FromIndex = defIndex;
                        candidate.WinnerSide = defendSide; candidate.LoserSide = attackSide;
                    }

                    candidates.Add(candidate);
                }
            }

            // ---- 2. Appliquer, les plus nettes d'abord ----

            var flipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FlipCandidate c in candidates.OrderByDescending(x => Math.Abs(x.Delta)))
            {
                if (flipped.Contains(c.Loser.Id)) continue;   // déjà changée de main ce tour
                if (flipped.Contains(c.From.Id)) continue;    // la zone d'attaque vient d'être perdue

                string oldSide = c.Loser.Control;
                c.Loser.Control = c.WinnerSide;
                flipped.Add(c.Loser.Id);
                result.FlippedZones.Add(c.Loser);

                WargameZoneData retreat = FindRetreatZone(c, zoneById, links, flipped);

                int moved = 0, destroyed = 0;

                foreach (WargameFormation f in c.Loser.Formations.ToList())
                {
                    if (!string.Equals(f.Side, oldSide, StringComparison.OrdinalIgnoreCase))
                        continue;   // une formation du vainqueur déjà présente reste en place

                    if (retreat != null)
                    {
                        c.Loser.Formations.Remove(f);
                        retreat.Formations.Add(f);
                        f.ForcePower = Math.Max(0, f.ForcePower * (1.0 - config.RetreatLossFraction));
                        result.MovedFormationIds.Add(f.FormationId);
                        moved++;
                    }
                    else
                    {
                        f.ForcePower = 0;   // WargameEngineLosses la retirera de la targetlist
                        destroyed++;
                    }
                }

                result.Destroyed += destroyed;

                FormUtils.LogRegister("WargameEngineCombat | axe '" + c.Axis.Name + "' : " + c.Loser.Id + " " + oldSide + " -> " + c.WinnerSide
                    + " (delta " + c.Delta.ToString("0.00") + ", attaque " + c.Attack.ToString("0.0") + " / défense " + c.Defense.ToString("0.0") + ") ; "
                    + moved + " formation(s) repliée(s)" + (retreat != null ? " vers " + retreat.Id : "") + ", " + destroyed + " détruite(s)");
            }

            FormUtils.LogRegister("WargameEngineCombat | " + activeAxes + " axe(s) actif(s), " + result.FlippedZones.Count + " zone(s) basculée(s)");

            return result;
        }

        // Zone où se replient les formations d'une zone perdue :
        //   1. la zone suivante en arrière sur le MEME axe, si elle est encore amie ;
        //   2. sinon la voisine amie la plus éloignée de la zone d'où vient l'attaque ;
        //   3. sinon null (pas de repli possible -> destruction).
        private static WargameZoneData FindRetreatZone(
            FlipCandidate c,
            Dictionary<string, WargameZoneData> zoneById,
            Dictionary<string, List<string>> links,
            HashSet<string> flipped)
        {
            int behindIndex = c.LoserIndex + (c.LoserIndex - c.FromIndex);

            if (behindIndex >= 0 && behindIndex < c.Axis.Path.Count)
            {
                WargameZoneData behind;
                if (zoneById.TryGetValue(c.Axis.Path[behindIndex], out behind) && IsValidRetreat(behind, c, flipped))
                    return behind;
            }

            WargameZoneData best = null;
            double bestDistance = -1;

            List<string> neighborIds;
            if (!links.TryGetValue(c.Loser.Id, out neighborIds))
                return null;

            foreach (string id in neighborIds)
            {
                WargameZoneData candidate;
                if (!zoneById.TryGetValue(id, out candidate) || !IsValidRetreat(candidate, c, flipped))
                    continue;

                double dx = candidate.Center.X - c.From.Center.X;
                double dy = candidate.Center.Y - c.From.Center.Y;
                double distance = dx * dx + dy * dy;

                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private static bool IsValidRetreat(WargameZoneData zone, FlipCandidate c, HashSet<string> flipped)
        {
            return string.Equals(zone.Control, c.LoserSide, StringComparison.OrdinalIgnoreCase)
                && !flipped.Contains(zone.Id)
                && !string.Equals(zone.Id, c.From.Id, StringComparison.OrdinalIgnoreCase);
        }

        // Zones d'un camp qui ne sont reliées, par des zones amies, à AUCUNE zone source de
        // ravitaillement (SupplySource > 0) : poches encerclées, coupées de la force
        // principale. Un camp qui n'a aucune zone source est ignoré (fonction désactivée),
        // sinon toutes ses zones passeraient pour coupées.
        private static HashSet<string> FindCutOffZones(
            List<WargameZoneData> zones,
            Dictionary<string, List<string>> links,
            Dictionary<string, WargameZoneData> zoneById)
        {
            var cutOff = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string side in new[] { WargameSide.Blue, WargameSide.Red })
            {
                List<WargameZoneData> sideZones = zones
                    .Where(z => string.Equals(z.Control, side, StringComparison.OrdinalIgnoreCase)).ToList();

                List<WargameZoneData> sources = sideZones.Where(z => z.SupplySource > 0).ToList();
                if (sources.Count == 0)
                    continue;

                var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<string>();

                foreach (WargameZoneData source in sources)
                {
                    reached.Add(source.Id);
                    queue.Enqueue(source.Id);
                }

                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();

                    foreach (string next in links[current])
                    {
                        if (reached.Contains(next)) continue;
                        if (!string.Equals(zoneById[next].Control, side, StringComparison.OrdinalIgnoreCase)) continue;

                        reached.Add(next);
                        queue.Enqueue(next);
                    }
                }

                foreach (WargameZoneData zone in sideZones)
                {
                    if (!reached.Contains(zone.Id))
                        cutOff.Add(zone.Id);
                }
            }

            return cutOff;
        }

        // Force du camp donné dans la zone (une zone contestée peut abriter les deux camps)
        private static double SidePower(WargameZoneData zone, string side)
        {
            double total = 0;

            foreach (WargameFormation f in zone.Formations)
            {
                if (string.Equals(f.Side, side, StringComparison.OrdinalIgnoreCase))
                    total += f.ForcePower;
            }

            return total;
        }

        private static double SupplyFactor(WargameZoneData zone, HashSet<string> cutOff, WargameCombatConfig config)
        {
            double factor = WargameSupplyRoute.GetCapacity(zone.SupplyRoute);

            if (cutOff.Contains(zone.Id))
                factor *= config.CutOffSupplyFactor;

            return factor;
        }

        private static double Luck(WargameCombatConfig config, Random rng)
        {
            if (config.RandomVariance <= 0 || rng == null)
                return 1.0;

            return 1.0 + (rng.NextDouble() * 2.0 - 1.0) * config.RandomVariance;
        }

        private static bool IsFightingSide(string control)
        {
            return string.Equals(control, WargameSide.Blue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(control, WargameSide.Red, StringComparison.OrdinalIgnoreCase);
        }

        private static string OtherSide(string side)
        {
            return string.Equals(side, WargameSide.Blue, StringComparison.OrdinalIgnoreCase)
                ? WargameSide.Red
                : WargameSide.Blue;
        }

        // Liens dans les deux sens, comme pour le calcul des axes
        private static Dictionary<string, List<string>> BuildLinks(List<WargameZoneData> zones)
        {
            var links = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (WargameZoneData zone in zones)
                links[zone.Id] = new List<string>();

            foreach (WargameZoneData zone in zones)
            {
                foreach (string n in zone.Neighbors)
                {
                    if (!links.ContainsKey(n)) continue;

                    if (!links[zone.Id].Contains(n)) links[zone.Id].Add(n);
                    if (!links[n].Contains(zone.Id)) links[n].Add(zone.Id);
                }
            }

            return links;
        }

        // Zone qui contient le point (coordonnées DCS), null si aucune
        public static WargameZoneData FindZoneContaining(List<WargameZoneData> zones, PointF point)
        {
            foreach (WargameZoneData zone in zones)
            {
                if (zone.DcsPoints == null || zone.DcsPoints.Count < 3)
                    continue;

                bool inside = false;
                int j = zone.DcsPoints.Count - 1;

                for (int i = 0; i < zone.DcsPoints.Count; i++)
                {
                    PointF pa = zone.DcsPoints[i], pb = zone.DcsPoints[j];

                    if ((pa.Y > point.Y) != (pb.Y > point.Y))
                    {
                        float xCross = (pb.X - pa.X) * (point.Y - pa.Y) / (pb.Y - pa.Y) + pa.X;
                        if (point.X < xCross) inside = !inside;
                    }

                    j = i;
                }

                if (inside)
                    return zone;
            }

            return null;
        }
    }
}
