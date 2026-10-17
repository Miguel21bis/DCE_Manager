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
        // camp_init : retreat_loss_fraction.
        public double RetreatLossFraction = 0.05;

        // Multiplicateur de ravitaillement d'une zone COUPEE de toute source amie
        // (poche encerclée). 1 = la coupure ne change rien.
        public double CutOffSupplyFactor = 0.5;

        // Variation aléatoire de la force d'attaque : 0 = aucun hasard (combat
        // reproductible), 0.1 = attaque multipliée par un facteur entre 0.9 et 1.1.
        public double RandomVariance = 0.0;

        // Ravitaillement : part de la force NOMINALE récupérée par mission, avant les
        // facteurs de route. 0 = aucun renfort.
        public double SupplyRecoveryRate = 0.10;

        // Multiplicateur de récupération pour une formation en première ligne
        // (zone voisine d'une zone ennemie).
        public double FrontLineRecoveryFactor = 0.5;

        // Occupation : quand une zone est conquise, la formation la plus forte de la zone d'où
        // part l'attaque victorieuse avance dedans (sinon la zone conquise reste vide et
        // rebascule dès le tour suivant). false = personne n'avance.
        public bool AdvanceOnConquest = true;

        // Alignement du front : une zone en saillant (plus de voisines ennemies que d'amies, au moins 2
        // ennemies) recule SI, après répartition de ses formations entre ses fronts (voir AllocateForces),
        // il lui manque encore de la force pour les tenir. Une zone qui attaque, ou une zone assez forte,
        // reste en place.
        public bool FrontAlignmentEnabled = true;

        // Zones vides : passent au camp qui a clairement le plus d'influence autour d'elles
        // (voir SettleEmptyZones). false = une zone vide garde son camp.
        public bool EmptyZoneInfluence = true;

        // Poids de l'influence des zones à 2 pas (les voisines directes comptent 1)
        public double InfluenceDecay = 0.5;

        // Renforts : les formations de l'arrière montent vers le front, d'une zone par mission
        // (voir MoveTowardFront). false = elles ne bougent qu'en avançant après une conquête
        // ou en se repliant.
        public bool AxisReinforcement = true;

        // Nombre normal de formations par zone. Une zone de première ligne peut en recevoir
        // PLUS si sa force n'atteint pas encore ce qu'il faut pour affronter l'ennemi d'en face.
        public int MaxFormationsPerZone = 3;

        // Usure au combat : part de sa force que perd, à chaque mission, une formation engagée dans un combat
        // jugé sur un axe actif (les deux camps). 0 = un combat ne coûte rien : seules les frappes du joueur
        // usent les formations. camp_init : combat_attrition. (Les pertes par tour de la preview sont un
        // réglage de TEST à part : elles simulent les missions DCS.)
        public double CombatAttrition = 0.02;

        // Récupération des formations ENGAGÉES dans un combat ce tour (attaquantes comme défenseuses),
        // en part de la récupération normale : 1 = elles récupèrent comme les autres (l'offensive ne s'use
        // jamais), 0 = aucune récupération tant qu'elles se battent. camp_init : engaged_recovery.
        public double EngagedRecoveryFactor = 0.25;

        // Plafond DUR de formations par zone, quelle que soit la menace d'en face (voir AcceptsFormation).
        // Au-delà du nombre normal, une zone de première ligne ne reçoit des renforts que jusqu'à ce plafond :
        // cela borne le nombre d'unités posées dans DCS et évite la surenchère entre les deux camps.
        public int MaxFormationsHard = 6;

        // Une formation dont la force tombe sous cette part de sa force NOMINALE est dissoute
        // (voir RemoveBrokenFormations). 0.25 = sous 25 %. 0 = jamais.
        public double DisbandBelowFraction = 0.25;

        // Zones "jouées dans DCS" : première ligne + ce nombre de zones derrière elle (voir FindDcsZones).
        // Les formations ailleurs restent sur la carte, sans unités posées. 20 = tout est posé.
        public int DcsZoneDepth = 1;

        // Supériorité aérienne : "blue", "red" ou "" (aucune). Le camp concerné voit sa force
        // multipliée par (1 + AirSuperiorityBonus) dans chaque combat, en attaque comme en défense.
        public string AirSuperioritySide = "";
        public double AirSuperiorityBonus = 0.3;

        // ---- Grilles de camp_init (lues par WargameConfigGrid) ----
        // Une valeur par type de terrain / de route. Valeurs de départ = tables de WargameZoneData.cs ;
        // camp_init (grilles "terrain" et "supply_route") les remplace, case par case.
        // Elles ne servent qu'à la PRODUCTION : la preview les reprend telles quelles, sans les modifier.

        // Multiplicateur de défense du terrain (grille terrain, colonne 1)
        public Dictionary<string, double> TerrainDefense =
            new Dictionary<string, double>(WargameTerrain.DefenseCoef, StringComparer.OrdinalIgnoreCase);

        // Vitesse de récupération selon la route (grille supply_route, colonne 1)
        public Dictionary<string, double> RouteRecovery =
            new Dictionary<string, double>(WargameSupplyRoute.Capacity, StringComparer.OrdinalIgnoreCase);

        // Force au combat selon la route, attaque comme défense (grille supply_route, colonne 2).
        // Valeurs de départ = anciennes capacités : le combat ne change pas tant que camp_init n'y touche pas.
        public Dictionary<string, double> RouteCombat =
            new Dictionary<string, double>(WargameSupplyRoute.Capacity, StringComparer.OrdinalIgnoreCase);

        public double GetTerrainDefense(string terrain) { return Lookup(TerrainDefense, terrain); }
        public double GetRouteRecovery(string route) { return Lookup(RouteRecovery, route); }
        public double GetRouteCombat(string route) { return Lookup(RouteCombat, route); }

        // Type inconnu ou vide : pas d'effet (1.0)
        private static double Lookup(Dictionary<string, double> table, string key)
        {
            double value;
            if (!string.IsNullOrEmpty(key) && table.TryGetValue(key, out value))
                return value;

            return 1.0;
        }

        // Preview : pas d'écriture dans log.txt (des milliers de combats), et un crochet pour
        // récupérer le détail de chaque jugement.
        public bool Quiet = false;
        public Action<string> Trace = null;

        // Détail technique (journal complet de la preview) : beaucoup plus bavard que Trace, jamais affiché
        // dans le journal normal. null = rien n'est calculé pour ça.
        public Action<string> Detail = null;

        // Copie des réglages (la preview change le hasard, l'air, etc. sans toucher à ceux de la campagne)
        public WargameCombatConfig Clone()
        {
            return (WargameCombatConfig)MemberwiseClone();
        }

        // Valeurs par défaut.
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

        // Flags des axes dont l'objectif est atteint (à transmettre à scriptsMod)
        public HashSet<string> ReachedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int Advanced;   // formations avancées dans une zone conquise (comptées aussi dans MovedFormationIds)

        // Formations montées vers le front par les renforts (comptées aussi dans MovedFormationIds)
        public HashSet<int> ReinforcedFormationIds = new HashSet<int>();

        // Pour chaque formation en contact avec l'ennemi : id de la zone ennemie vers laquelle elle est
        // tournée (état FINAL du tour). Sert à orienter la repose dans DCS ; une formation qui a bougé
        // pendant ce tour est reposée ailleurs, son entrée n'est alors qu'indicative.
        public Dictionary<int, string> FacingZoneByFormationId = new Dictionary<int, string>();

        // Formations usées par un combat ce tour (voir WargameCombatConfig.CombatAttrition)
        public int Worn;

        // Formations engagées dans un combat jugé ce tour : elles récupèrent moins (EngagedRecoveryFactor)
        public HashSet<int> EngagedFormationIds = new HashSet<int>();

        // Formations dissoutes car trop faibles (voir RemoveBrokenFormations)
        public int Disbanded;
        public List<string> DisbandedNotes = new List<string>();
    }

    // Résolution du combat entre zones (passe 1), lancée par WargameEngineLosses juste
    // après l'application des pertes de la mission.
    //
    // On ne regarde QUE les axes actifs (flag posé par un trigger de campagne), et sur
    // chaque axe, les paires de zones consécutives de contrôle différent :
    //
    //   attaquant = le camp qui tient la zone de départ (A) de l'axe
    //   attaque   = force affectée au front x ravitaillement x air (x hasard)
    //   défense   = force affectée au front x ravitaillement x terrain x air
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
    //
    // Avant de juger, chaque zone qui a plusieurs fronts répartit ses formations entre eux (voir
    // AllocateForces) : la force principale attaque, des formations gardent les flancs. Une paire
    // n'est donc jugée qu'avec la force affectée à CE front.
    //
    // Ensuite, dans l'ordre : alignement du front (seulement pour une zone qui ne tient pas ses
    // fronts), montée des renforts vers le front, zones vides qui suivent l'influence,
    // objectifs atteints.
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

        // Une paire de zones consécutives de contrôle différent sur un axe actif
        private class AxisPair
        {
            public WargameAxis Axis;
            public WargameZoneData AttZone;
            public WargameZoneData DefZone;
            public int AttIndex;             // positions sur l'axe
            public int DefIndex;
            public string AttackSide;
            public string DefendSide;
        }

        // Répartition des formations d'une zone entre ses fronts (voisines ennemies), voir AllocateForces
        private class ZoneAllocation
        {
            public string AttackTargetId;                       // null si la zone n'attaque pas
            public Dictionary<string, double> PowerByDirection  // force affectée à chaque front (clé = id de la zone ennemie)
                = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<WargameFormation>> FormationsByDirection   // formations affectées à chaque front
                = new Dictionary<string, List<WargameFormation>>(StringComparer.OrdinalIgnoreCase);
            public double Deficit;                              // force qui manque pour garder tous les fronts (0 = tout est couvert)
        }

        public static WargameCombatResult Resolve(
            List<WargameZoneData> zones,
            List<WargameAxis> axes,
            HashSet<string> activeFlags,
            WargameCombatConfig config,
            Random rng,
            WargameTemplateCatalog catalog = null)   // facultatif : sert à garder au repos les formations trop faibles
        {
            var result = new WargameCombatResult();

            if (zones == null || axes == null || axes.Count == 0)
                return result;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData zone in zones)
                zoneById[zone.Id] = zone;

            Dictionary<string, List<string>> links = BuildLinks(zones);
            HashSet<string> cutOff = FindCutOffZones(zones, links, zoneById);

            Detail(config, "  cut-off zones (no road to a supply source): " + (cutOff.Count == 0 ? "none" : string.Join(", ", cutOff.OrderBy(x => x))));

            // ---- 1. Lister les paires de front des axes actifs (état de départ) ----

            var pairs = new List<AxisPair>();
            int activeAxes = 0;

            // Couples de zones voisines SUR un axe actif (dans les deux sens) : seuls ces fronts se battent
            var frontPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (WargameAxis axis in axes)
            {
                if (!axis.IsActive(activeFlags))
                    continue;

                WargameZoneData startZone;
                if (!zoneById.TryGetValue(axis.Path[0], out startZone) || !IsFightingSide(startZone.Control))
                {
                    Log(config, "WargameEngineCombat | axe '" + axis.Name + "' ignoré : sa zone de départ n'est tenue ni par le bleu ni par le rouge");
                    continue;
                }

                // Le camp attaquant est celui qui tenait la zone de départ au chargement (WargameAxis.AttackerSide),
                // même si cette zone a été perdue depuis : l'axe ne change pas d'attaquant en cours de route.
                string attackSide = GetAttackSide(axis, startZone);

                if (IsObjectiveReached(axis, attackSide, zoneById))
                    continue;   // objectif atteint : l'axe ne combat plus

                activeAxes++;

                for (int k = 0; k < axis.Path.Count - 1; k++)
                {
                    frontPairs.Add(axis.Path[k] + "|" + axis.Path[k + 1]);
                    frontPairs.Add(axis.Path[k + 1] + "|" + axis.Path[k]);
                }

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

                    pairs.Add(new AxisPair
                    {
                        Axis = axis,
                        AttZone = aAttacks ? a : b,
                        DefZone = aAttacks ? b : a,
                        AttIndex = aAttacks ? i : i + 1,
                        DefIndex = aAttacks ? i + 1 : i,
                        AttackSide = attackSide,
                        DefendSide = defendSide,
                    });
                }
            }

            // ---- 1b. Chaque zone qui attaque choisit UNE cible, puis répartit ses formations entre ses fronts ----

            Dictionary<string, string> attackTargets = ChooseAttackTargets(pairs, config);
            Dictionary<string, ZoneAllocation> allocations = AllocateForces(zones, zoneById, links, cutOff, attackTargets, frontPairs, config, true, null);

            // ---- 1c. Juger toutes les paires, sur l'état de départ ----

            var candidates = new List<FlipCandidate>();
            var foughtPairs = new List<AxisPair>();   // paires où les deux côtés ont de la force engagée (usure)

            foreach (AxisPair p in pairs)
            {
                string chosenTarget;
                if (attackTargets.TryGetValue(p.AttZone.Id, out chosenTarget)
                    && !string.Equals(chosenTarget, p.DefZone.Id, StringComparison.OrdinalIgnoreCase))
                {
                    Trace(config, "  " + p.Axis.Name + " : " + p.AttZone.Id + " attacks " + chosenTarget + " this turn, " + p.DefZone.Id + " is left alone");
                    continue;   // une zone n'attaque qu'une cible par tour
                }

                // Seule la force affectée à CE front compte (voir AllocateForces)
                double attackPower = DirectionPower(allocations, p.AttZone, p.AttackSide, p.DefZone.Id);
                double attackSupply = SupplyFactor(p.AttZone, cutOff, config);
                double attackAir = AirCoef(p.AttackSide, config);
                double attack = attackPower * attackSupply * attackAir * Luck(config, rng);

                double defensePower = DirectionPower(allocations, p.DefZone, p.DefendSide, p.AttZone.Id);
                double defenseSupply = SupplyFactor(p.DefZone, cutOff, config);
                double terrainCoef = config.GetTerrainDefense(p.DefZone.Terrain);
                double defenseAir = AirCoef(p.DefendSide, config);
                double defense = defensePower * defenseSupply * terrainCoef * defenseAir;

                double total = attack + defense;
                if (total <= 0)
                    continue;   // deux zones vides face à face : rien ne se passe

                double delta = (attack - defense) / total;
                bool flips = Math.Abs(delta) > config.ControlFlipThreshold;

                Trace(config, "  " + p.Axis.Name + " : " + p.AttZone.Id + " attacks " + p.DefZone.Id
                    + " | attack " + attack.ToString("0.0")
                    + " (force " + attackPower.ToString("0.0") + " x supply " + attackSupply.ToString("0.00") + " x air " + attackAir.ToString("0.00") + ")"
                    + " vs defense " + defense.ToString("0.0")
                    + " (force " + defensePower.ToString("0.0") + " x supply " + defenseSupply.ToString("0.00")
                    + " x terrain " + terrainCoef.ToString("0.00") + " x air " + defenseAir.ToString("0.00") + ")"
                    + " | delta " + delta.ToString("0.00")
                    + (flips ? " -> zone changes hands" : " -> not enough (needs more than " + config.ControlFlipThreshold.ToString("0.00") + ")"));

                if (attack > 0 && defense > 0)
                    foughtPairs.Add(p);

                if (!flips)
                    continue;

                var candidate = new FlipCandidate { Axis = p.Axis, Delta = delta, Attack = attack, Defense = defense };

                if (delta > 0)
                {
                    candidate.Loser = p.DefZone; candidate.LoserIndex = p.DefIndex;
                    candidate.From = p.AttZone; candidate.FromIndex = p.AttIndex;
                    candidate.WinnerSide = p.AttackSide; candidate.LoserSide = p.DefendSide;
                }
                else
                {
                    candidate.Loser = p.AttZone; candidate.LoserIndex = p.AttIndex;
                    candidate.From = p.DefZone; candidate.FromIndex = p.DefIndex;
                    candidate.WinnerSide = p.DefendSide; candidate.LoserSide = p.AttackSide;
                }

                candidates.Add(candidate);
            }

            // ---- 1d. Usure : les formations engagées dans un combat jugé perdent une petite part de leur force ----
            // (après le jugement, avant les replis : une formation qui se replie porte déjà son usure)

            var worn = new HashSet<int>();

            foreach (AxisPair p in foughtPairs)
            {
                foreach (WargameFormation f in DirectionFormations(allocations, p.AttZone, p.AttackSide, p.DefZone.Id)
                    .Concat(DirectionFormations(allocations, p.DefZone, p.DefendSide, p.AttZone.Id)))
                {
                    if (!worn.Add(f.FormationId)) continue;   // une seule fois par tour

                    result.EngagedFormationIds.Add(f.FormationId);

                    if (config.CombatAttrition > 0)
                    {
                        double before = f.ForcePower;
                        f.ForcePower = Math.Max(0, f.ForcePower * (1.0 - config.CombatAttrition));
                        Detail(config, "  wear " + f.Name + " (" + f.Side + ") in " + FindZoneIdOf(zones, f) + " : "
                            + before.ToString("0.0") + " -> " + f.ForcePower.ToString("0.0"));
                    }
                }
            }

            if (config.CombatAttrition > 0)
            {
                result.Worn = worn.Count;
                if (worn.Count > 0)
                    Trace(config, "  wear : " + worn.Count + " formation(s) engaged in a fight lose " + (config.CombatAttrition * 100).ToString("0.#") + "% of their strength");
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

                // Repli : chaque formation (les plus fortes d'abord) prend la première zone de repli
                // qui a de la place. Sans place nulle part, elle est perdue.
                List<WargameZoneData> retreatOptions = FindRetreatZones(c, zoneById, links, flipped);
                WargameZoneData retreat = null;

                int moved = 0, destroyed = 0;

                List<WargameFormation> retreating = c.Loser.Formations
                    .Where(x => x.ForcePower > 0 && string.Equals(x.Side, oldSide, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.ForcePower)
                    .ToList();   // une formation du vainqueur déjà présente reste en place

                foreach (WargameFormation f in retreating)
                {
                    WargameZoneData place = retreatOptions.FirstOrDefault(
                        o => LiveFormations(o, oldSide).Count < config.MaxFormationsPerZone);

                    if (place != null)
                    {
                        c.Loser.Formations.Remove(f);
                        place.Formations.Add(f);
                        double powerBefore = f.ForcePower;
                        f.ForcePower = Math.Max(0, f.ForcePower * (1.0 - config.RetreatLossFraction));
                        Detail(config, "  retreat loss " + f.Name + " : " + powerBefore.ToString("0.0") + " -> " + f.ForcePower.ToString("0.0"));
                        result.MovedFormationIds.Add(f.FormationId);
                        if (retreat == null) retreat = place;
                        moved++;
                    }
                    else
                    {
                        f.ForcePower = 0;   // WargameEngineLosses la retirera de la targetlist
                        destroyed++;
                        Trace(config, "  no room to retreat: " + f.Name + " from " + c.Loser.Id + " is lost");
                    }
                }

                result.Destroyed += destroyed;

                // Occupation : seules les formations affectées à CE front (le groupe d'attaque, voir AllocateForces)
                // avancent dans la zone conquise, les plus fortes d'abord, jusqu'au nombre normal de formations
                // par zone. Celles qui gardent un autre front (flanc, poche ennemie voisine) restent en place :
                // sinon la zone laissée vide derrière l'avance serait reprise par l'ennemi, et le chemin de
                // ravitaillement de l'axe coupé. Les autres suivront avec les renforts.
                string advancedName = null;

                if (config.AdvanceOnConquest)
                {
                    int room = config.MaxFormationsPerZone - LiveFormations(c.Loser, c.WinnerSide).Count;

                    List<WargameFormation> attackGroup = DirectionFormations(allocations, c.From, c.WinnerSide, c.Loser.Id)
                        .Where(x => x.ForcePower > 0 && c.From.Formations.Contains(x))
                        .OrderByDescending(x => x.ForcePower)
                        .ToList();

                    List<WargameFormation> staying = LiveFormations(c.From, c.WinnerSide).Where(x => !attackGroup.Contains(x)).ToList();
                    if (staying.Count > 0)
                        Detail(config, "  advance from " + c.From.Id + " : " + string.Join(", ", staying.Select(x => x.Name))
                            + " stay(s) behind to guard the zone (assigned to another front)");

                    foreach (WargameFormation advancing in attackGroup)
                    {
                        if (room <= 0) break;

                        c.From.Formations.Remove(advancing);
                        c.Loser.Formations.Add(advancing);
                        result.MovedFormationIds.Add(advancing.FormationId);
                        result.Advanced++;
                        advancedName = advancedName == null ? advancing.Name : advancedName + ", " + advancing.Name;
                        room--;
                    }
                }

                Log(config, "WargameEngineCombat | axe '" + c.Axis.Name + "' : " + c.Loser.Id + " " + oldSide + " -> " + c.WinnerSide
                    + " (delta " + c.Delta.ToString("0.00") + ", attaque " + c.Attack.ToString("0.0") + " / défense " + c.Defense.ToString("0.0") + ") ; "
                    + moved + " formation(s) repliée(s)" + (retreat != null ? " vers " + retreat.Id : "") + ", " + destroyed + " détruite(s)"
                    + (advancedName != null ? ", '" + advancedName + "' avance depuis " + c.From.Id : ""));
            }

            if (config.FrontAlignmentEnabled)
                AlignFront(zones, zoneById, links, flipped, attackTargets, frontPairs, config, result);

            if (config.AxisReinforcement)
                MoveTowardFront(zones, zoneById, links, attackTargets, config, result, catalog);

            if (config.EmptyZoneInfluence)
                SettleEmptyZones(zones, zoneById, links, flipped, config, result);

            // ---- Orientation des formations (état final du tour), pour la repose dans DCS ----

            AllocateForces(zones, zoneById, links, FindCutOffZones(zones, links, zoneById), attackTargets, frontPairs, config, false, result);

            // ---- Objectifs d'axe atteints (après les basculements de ce tour) ----

            foreach (WargameAxis axis in axes)
            {
                if (!axis.IsActive(activeFlags))
                    continue;

                WargameZoneData startNow;
                if (!zoneById.TryGetValue(axis.Path[0], out startNow) || !IsFightingSide(startNow.Control))
                    continue;

                if (IsObjectiveReached(axis, GetAttackSide(axis, startNow), zoneById))
                {
                    result.ReachedFlags.Add(axis.ObjectiveFlag);
                    Log(config, "WargameEngineCombat | axe '" + axis.Name + "' : objectif " + axis.Path[axis.Path.Count - 1] + " atteint, flag " + axis.ObjectiveFlag);
                }
            }

            Log(config, "WargameEngineCombat | " + activeAxes + " axe(s) actif(s), " + result.FlippedZones.Count + " zone(s) basculée(s), "
                + result.Advanced + " avance(s), " + result.Worn + " usée(s)");

            return result;
        }

        // Cible d'attaque de chaque zone : une zone n'attaque qu'UN voisin par tour, le plus faible
        // (force x terrain). Une zone qui n'est attaquante sur aucune paire n'a pas d'entrée.
        private static Dictionary<string, string> ChooseAttackTargets(List<AxisPair> pairs, WargameCombatConfig config)
        {
            var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var weakest = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (AxisPair p in pairs)
            {
                double strength = SidePower(p.DefZone, p.DefendSide) * config.GetTerrainDefense(p.DefZone.Terrain);

                if (!targets.ContainsKey(p.AttZone.Id) || strength < weakest[p.AttZone.Id])
                {
                    targets[p.AttZone.Id] = p.DefZone.Id;
                    weakest[p.AttZone.Id] = strength;
                }
            }

            return targets;
        }

        // Répartition des forces : une zone ne peut pas attaquer devant ET tenir ses flancs avec les
        // mêmes formations. Pour chaque zone qui a PLUSIEURS fronts (voisines ennemies) :
        //
        //   - front à garder : la force qu'il faut pour ne pas être enfoncé par la voisine d'en face,
        //     avec le même seuil que le combat = force ennemie / (rapport du seuil x ravitaillement x terrain) ;
        //   - zone qui attaque : sa plus forte formation reste à l'attaque ; les autres, des plus faibles
        //     aux plus fortes, vont garder le front à qui il manque le plus de force ; ce qui reste attaque ;
        //   - zone qui défend : les formations, des plus fortes aux plus faibles, vont là où il manque le
        //     plus de force ; le surplus renforce le front le plus menaçant ;
        //   - les fronts où un axe actif se bat (frontPairs) passent AVANT les simples voisines ennemies :
        //     une formation ne reste pas plantée face à une voisine qui ne peut pas attaquer pendant que le
        //     vrai front est dégarni.
        //
        // Les formations sont entières (une formation = un seul front). Le résultat ne sert qu'à CE tour.
        // Une zone à un seul front n'a pas d'entrée : toute sa force y va (voir DirectionPower).
        // trace : écrit une ligne par zone dans la preview. record : mémorise vers quelle zone ennemie
        // chaque formation est tournée (pour la repose dans DCS), null = rien à mémoriser.
        private static Dictionary<string, ZoneAllocation> AllocateForces(
            List<WargameZoneData> zones,
            Dictionary<string, WargameZoneData> zoneById,
            Dictionary<string, List<string>> links,
            HashSet<string> cutOff,
            Dictionary<string, string> attackTargets,
            HashSet<string> frontPairs,
            WargameCombatConfig config,
            bool trace,
            WargameCombatResult record)
        {
            var allocations = new Dictionary<string, ZoneAllocation>(StringComparer.OrdinalIgnoreCase);

            double threshold = Math.Min(config.ControlFlipThreshold, 0.9);
            double ratio = (1.0 + threshold) / (1.0 - threshold);

            foreach (WargameZoneData zone in zones)
            {
                if (!IsFightingSide(zone.Control)) continue;

                string side = zone.Control;
                string enemySide = OtherSide(side);

                // Les fronts : voisines tenues par l'ennemi
                var directions = new List<WargameZoneData>();
                foreach (string id in links[zone.Id])
                {
                    if (string.Equals(zoneById[id].Control, enemySide, StringComparison.OrdinalIgnoreCase))
                        directions.Add(zoneById[id]);
                }

                if (directions.Count == 0) continue;

                List<WargameFormation> live = LiveFormations(zone, side);
                if (live.Count == 0) continue;

                // Un seul front : tout y va
                if (directions.Count == 1)
                {
                    if (record != null)
                    {
                        foreach (WargameFormation f in live)
                            record.FacingZoneByFormationId[f.FormationId] = directions[0].Id;
                    }
                    continue;
                }

                string attackId;
                attackTargets.TryGetValue(zone.Id, out attackId);
                if (attackId != null && !directions.Any(d => string.Equals(d.Id, attackId, StringComparison.OrdinalIgnoreCase)))
                    attackId = null;   // la cible n'est plus une voisine ennemie (zone changée de main)

                // Ce que vaut notre défense ici (comme dans le jugement : ravitaillement x terrain x air)
                double ownMult = Math.Max(0.01, SupplyFactor(zone, cutOff, config) * config.GetTerrainDefense(zone.Terrain) * AirCoef(side, config));

                var threat = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var isActive = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var need = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var assigned = new Dictionary<string, List<WargameFormation>>(StringComparer.OrdinalIgnoreCase);
                var power = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (WargameZoneData d in directions)
                {
                    // Menace = tout ce que la voisine peut nous envoyer (majorant : elle a peut-être d'autres fronts)
                    double t = SidePower(d, enemySide) * SupplyFactor(d, cutOff, config) * AirCoef(enemySide, config);

                    threat[d.Id] = t;
                    isActive[d.Id] = frontPairs.Contains(zone.Id + "|" + d.Id);
                    need[d.Id] = (attackId != null && string.Equals(d.Id, attackId, StringComparison.OrdinalIgnoreCase))
                        ? 0.0                       // le front d'attaque reçoit ce qui reste
                        : t / (ratio * ownMult);    // front à garder
                    assigned[d.Id] = new List<WargameFormation>();
                    power[d.Id] = 0.0;
                }

                List<WargameFormation> pool;

                if (attackId != null)
                {
                    WargameFormation main = live.OrderByDescending(f => f.ForcePower).First();
                    assigned[attackId].Add(main);
                    power[attackId] += main.ForcePower;

                    pool = live.Where(f => f != main).OrderBy(f => f.ForcePower).ToList();            // les plus faibles gardent d'abord
                }
                else
                {
                    pool = live.OrderByDescending(f => f.ForcePower).ToList();                          // les plus fortes d'abord
                }

                foreach (WargameFormation f in pool)
                {
                    // Le front à qui il manque le plus de force : d'abord parmi les fronts d'axe actif,
                    // puis parmi les simples voisines ennemies
                    string target = null;

                    for (int pass = 0; pass < 2 && target == null; pass++)
                    {
                        double bestMissing = 1e-9;

                        foreach (WargameZoneData d in directions)
                        {
                            if (isActive[d.Id] != (pass == 0)) continue;

                            double missing = need[d.Id] - power[d.Id];
                            if (missing > bestMissing)
                            {
                                bestMissing = missing;
                                target = d.Id;
                            }
                        }
                    }

                    if (target == null)
                    {
                        // Tout est couvert : le surplus attaque, ou renforce le front le plus menaçant
                        // (un front d'axe actif d'abord)
                        target = attackId != null
                            ? attackId
                            : directions.OrderByDescending(d => isActive[d.Id]).ThenByDescending(d => threat[d.Id]).First().Id;
                    }

                    assigned[target].Add(f);
                    power[target] += f.ForcePower;
                }

                double deficit = 0;
                foreach (WargameZoneData d in directions)
                    deficit += Math.Max(0.0, need[d.Id] - power[d.Id]);

                var allocation = new ZoneAllocation { AttackTargetId = attackId, Deficit = deficit };
                foreach (WargameZoneData d in directions)
                {
                    allocation.PowerByDirection[d.Id] = power[d.Id];
                    allocation.FormationsByDirection[d.Id] = assigned[d.Id];
                }

                allocations[zone.Id] = allocation;

                if (record != null)
                {
                    foreach (WargameZoneData d in directions)
                    {
                        foreach (WargameFormation f in assigned[d.Id])
                            record.FacingZoneByFormationId[f.FormationId] = d.Id;
                    }
                }

                if (trace && config.Trace != null)
                {
                    var parts = new List<string>();
                    foreach (WargameZoneData d in directions)
                    {
                        bool isAttack = attackId != null && string.Equals(d.Id, attackId, StringComparison.OrdinalIgnoreCase);

                        parts.Add(assigned[d.Id].Count + " formation(s) (force " + power[d.Id].ToString("0.0") + ") toward " + d.Id
                            + (isAttack ? " [attack]" : " [guard, needs " + need[d.Id].ToString("0.0") + "]"));
                    }

                    Trace(config, "  allocation " + zone.Id + " (" + side + ") : " + string.Join(" ; ", parts)
                        + (deficit > 0 ? " | missing " + deficit.ToString("0.0") : ""));
                }
            }

            return allocations;
        }

        // Formations affectées par "zone" (camp "side") au front tourné vers la zone ennemie "enemyId".
        // Zone sans répartition (un seul front) : toutes ses formations vivantes.
        private static List<WargameFormation> DirectionFormations(Dictionary<string, ZoneAllocation> allocations, WargameZoneData zone, string side, string enemyId)
        {
            ZoneAllocation allocation;
            List<WargameFormation> list;

            if (allocations.TryGetValue(zone.Id, out allocation) && allocation.FormationsByDirection.TryGetValue(enemyId, out list))
                return list;

            return LiveFormations(zone, side);
        }

        // Force affectée par "zone" (camp "side") au front tourné vers la zone ennemie "enemyId".
        // Zone sans répartition (un seul front) : toute sa force.
        private static double DirectionPower(Dictionary<string, ZoneAllocation> allocations, WargameZoneData zone, string side, string enemyId)
        {
            ZoneAllocation allocation;
            double value;

            if (allocations.TryGetValue(zone.Id, out allocation) && allocation.PowerByDirection.TryGetValue(enemyId, out value))
                return value;

            return SidePower(zone, side);
        }

        // Alignement du front : une zone en saillant qui NE PEUT PAS tenir ses fronts recule.
        // Saillant = au moins 2 voisines ennemies, et plus d'ennemies que d'amies. Elle ne recule que si,
        // une fois ses formations réparties entre ces fronts (AllocateForces, sur l'état APRES les
        // basculements du tour), il manque encore de la force quelque part. Une zone qui attaque, ou assez
        // forte, reste en place. Le repérage se fait sur un instantané, donc pas de cascade dans la même
        // passe : les saillants créés par ce recul seront traités à la mission suivante.
        private static void AlignFront(
            List<WargameZoneData> zones,
            Dictionary<string, WargameZoneData> zoneById,
            Dictionary<string, List<string>> links,
            HashSet<string> flipped,
            Dictionary<string, string> attackTargets,
            HashSet<string> frontPairs,
            WargameCombatConfig config,
            WargameCombatResult result)
        {
            Dictionary<string, ZoneAllocation> allocations =
                AllocateForces(zones, zoneById, links, FindCutOffZones(zones, links, zoneById), attackTargets, frontPairs, config, false, null);

            // 1. Repérer les saillants (état actuel)
            var salients = new List<WargameZoneData>();

            foreach (WargameZoneData zone in zones)
            {
                if (!IsFightingSide(zone.Control)) continue;
                if (flipped.Contains(zone.Id)) continue;
                if (attackTargets.ContainsKey(zone.Id)) continue;   // une zone qui attaque ne recule pas

                string side = zone.Control;
                if (!zone.Formations.Any(f => string.Equals(f.Side, side, StringComparison.OrdinalIgnoreCase) && f.ForcePower > 0))
                    continue;

                int friends = 0, enemies = 0;
                foreach (string id in links[zone.Id])
                {
                    string control = zoneById[id].Control;
                    if (!IsFightingSide(control)) continue;

                    if (string.Equals(control, side, StringComparison.OrdinalIgnoreCase)) friends++;
                    else enemies++;
                }

                if (enemies >= 2 && enemies > friends)
                {
                    ZoneAllocation allocation;
                    if (allocations.TryGetValue(zone.Id, out allocation) && allocation.Deficit > 0)
                        salients.Add(zone);   // il lui manque de la force pour tenir tous ses fronts
                }
            }

            // 2. Les faire reculer, une par une
            var abandoned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (WargameZoneData zone in salients)
            {
                string side = zone.Control;
                string enemySide = OtherSide(side);

                // centre de gravité des voisines ennemies = d'où vient la menace
                double ex = 0, ey = 0;
                int enemyCount = 0;
                var friendlyNeighbors = new List<WargameZoneData>();

                foreach (string id in links[zone.Id])
                {
                    WargameZoneData n = zoneById[id];

                    if (string.Equals(n.Control, enemySide, StringComparison.OrdinalIgnoreCase))
                    {
                        ex += n.Center.X; ey += n.Center.Y; enemyCount++;
                    }
                    else if (string.Equals(n.Control, side, StringComparison.OrdinalIgnoreCase)
                        && !flipped.Contains(n.Id) && !abandoned.Contains(n.Id))
                    {
                        friendlyNeighbors.Add(n);
                    }
                }

                if (friendlyNeighbors.Count == 0 || enemyCount == 0)
                    continue;   // nulle part où reculer : on tient la position

                ex /= enemyCount; ey /= enemyCount;

                // Voisines amies, de la plus éloignée de la menace à la plus proche
                List<WargameZoneData> ordered = friendlyNeighbors
                    .OrderByDescending(n => (n.Center.X - ex) * (n.Center.X - ex) + (n.Center.Y - ey) * (n.Center.Y - ey))
                    .ToList();

                // Chaque formation (les plus fortes d'abord) prend la première voisine qui a de la place.
                // Sans place, elle reste : un repli volontaire ne détruit personne.
                int moved = 0;
                WargameZoneData target = null;

                foreach (WargameFormation f in LiveFormations(zone, side).OrderByDescending(x => x.ForcePower))
                {
                    WargameZoneData place = ordered.FirstOrDefault(n => LiveFormations(n, side).Count < config.MaxFormationsPerZone);
                    if (place == null) break;

                    zone.Formations.Remove(f);
                    place.Formations.Add(f);
                    f.ForcePower = Math.Max(0, f.ForcePower * (1.0 - config.RetreatLossFraction));
                    result.MovedFormationIds.Add(f.FormationId);
                    if (target == null) target = place;
                    moved++;
                }

                if (moved == 0)
                    continue;   // aucune place derrière : on tient la position

                abandoned.Add(zone.Id);

                Trace(config, "  front alignment : " + zone.Id + " is a salient it cannot hold, " + moved + " formation(s) pulled back to " + target.Id);

                Log(config, "WargameEngineCombat | alignement du front : " + zone.Id + " (saillant " + side + ") évacuée (reste à son camp, vide), "
                    + moved + " formation(s) repliée(s) vers " + target.Id);
            }
        }

        // Montée vers le front. L'armée n'est pas censée rester au dépôt : ses formations suivent
        // le front, au contact, flancs compris. Pour chaque camp :
        //
        //   - première ligne = zones amies voisines d'une zone ennemie ;
        //   - distance = nombre de zones amies à traverser pour atteindre la première ligne ;
        //   - chaque formation d'une zone d'arrière fait UN pas vers une voisine plus proche du
        //     front (en partant des zones les plus proches : la place se libère devant).
        //
        // Capacité d'une zone (dynamique) : MaxFormationsPerZone formations, et au-delà tant que
        // sa force n'atteint pas ce qu'il faut face à TOUS ses fronts (somme des besoins) :
        //   - front que la zone attaque : force ennemie x terrain x (1+seuil)/(1-seuil), de quoi percer ;
        //   - autres fronts             : force ennemie (parité).
        // Si 3 formations suffisent, on reste à 3 ; si l'ennemi en aligne 5 pour 80 de force, on
        // en rassemble autant qu'il faut pour dépasser 80. Jamais plus que MaxFormationsHard.
        private static void MoveTowardFront(
            List<WargameZoneData> zones,
            Dictionary<string, WargameZoneData> zoneById,
            Dictionary<string, List<string>> links,
            Dictionary<string, string> attackTargets,
            WargameCombatConfig config,
            WargameCombatResult result,
            WargameTemplateCatalog catalog)
        {
            double threshold = Math.Min(config.ControlFlipThreshold, 0.9);
            double ratio = (1.0 + threshold) / (1.0 - threshold);

            foreach (string side in new[] { WargameSide.Blue, WargameSide.Red })
            {
                string enemySide = OtherSide(side);

                // 1. Première ligne et force qu'il y faut
                var need = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var distance = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<string>();

                foreach (WargameZoneData zone in zones)
                {
                    if (!string.Equals(zone.Control, side, StringComparison.OrdinalIgnoreCase)) continue;

                    bool isFront = false;
                    double needed = 0;

                    string attackId;
                    attackTargets.TryGetValue(zone.Id, out attackId);   // front que cette zone attaque (null = aucun)

                    foreach (string id in links[zone.Id])
                    {
                        WargameZoneData n = zoneById[id];
                        if (!string.Equals(n.Control, enemySide, StringComparison.OrdinalIgnoreCase)) continue;

                        isFront = true;

                        double p = SidePower(n, enemySide);
                        if (attackId != null && string.Equals(id, attackId, StringComparison.OrdinalIgnoreCase))
                            p *= config.GetTerrainDefense(n.Terrain) * ratio;   // front d'attaque : de quoi percer

                        needed += p;   // la zone doit pouvoir faire face à tous ses fronts
                    }

                    if (isFront)
                    {
                        need[zone.Id] = needed;
                        distance[zone.Id] = 0;
                        queue.Enqueue(zone.Id);
                    }
                }

                if (queue.Count == 0)
                    continue;   // pas de front pour ce camp

                foreach (KeyValuePair<string, double> kv in need.OrderBy(x => x.Key))
                    Detail(config, "  reinforce " + side + " : front zone " + kv.Key + " needs " + kv.Value.ToString("0.0") + " (has " + SidePower(zoneById[kv.Key], side).ToString("0.0") + ")");

                // 2. Distance au front, à travers les zones amies
                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();

                    foreach (string next in links[current])
                    {
                        if (distance.ContainsKey(next)) continue;
                        if (!string.Equals(zoneById[next].Control, side, StringComparison.OrdinalIgnoreCase)) continue;

                        distance[next] = distance[current] + 1;
                        queue.Enqueue(next);
                    }
                }

                // 3. Un pas vers le front, des zones les plus proches aux plus lointaines
                List<WargameZoneData> rear = zones
                    .Where(z => distance.ContainsKey(z.Id) && distance[z.Id] > 0)
                    .OrderBy(z => distance[z.Id])
                    .ToList();

                foreach (WargameZoneData zone in rear)
                {
                    int zoneDistance = distance[zone.Id];

                    foreach (WargameFormation f in LiveFormations(zone, side).OrderByDescending(x => x.ForcePower))
                    {
                        if (result.MovedFormationIds.Contains(f.FormationId)) continue;   // déjà bougée ce tour

                        // Une formation sous le seuil de dissolution reste à l'arrière : elle récupère au lieu de monter
                        // se faire dissoudre au contact (voir RemoveBrokenFormations).
                        if (catalog != null && config.DisbandBelowFraction > 0)
                        {
                            double nominal = f.GetNominalForcePower(catalog);
                            if (nominal > 0 && f.ForcePower < nominal * config.DisbandBelowFraction)
                            {
                                Detail(config, "  reinforce " + side + " : " + f.Name + " stays in " + zone.Id + " (too weak: "
                                    + (100.0 * f.ForcePower / nominal).ToString("0") + "% of full strength, it recovers first)");
                                continue;
                            }
                        }

                        // Meilleure voisine plus proche du front : celle à qui il manque le plus de force
                        WargameZoneData best = null;
                        double bestScore = double.MinValue;

                        foreach (string id in links[zone.Id])
                        {
                            int d;
                            if (!distance.TryGetValue(id, out d) || d != zoneDistance - 1) continue;

                            WargameZoneData target = zoneById[id];

                            double targetNeed;
                            need.TryGetValue(id, out targetNeed);   // 0 si ce n'est pas la première ligne

                            if (!AcceptsFormation(target, side, targetNeed, config)) continue;

                            double score = targetNeed - SidePower(target, side);
                            if (best == null || score > bestScore)
                            {
                                best = target;
                                bestScore = score;
                            }
                        }

                        if (best == null) continue;

                        zone.Formations.Remove(f);
                        best.Formations.Add(f);
                        result.MovedFormationIds.Add(f.FormationId);
                        result.ReinforcedFormationIds.Add(f.FormationId);
                    }
                }
            }
        }

        // Une zone accepte une formation de plus si elle n'est pas pleine, ou si sa force
        // n'atteint pas encore ce qu'il lui faut (voir MoveTowardFront) - mais jamais au-delà
        // du plafond dur MaxFormationsHard.
        private static bool AcceptsFormation(WargameZoneData zone, string side, double needed, WargameCombatConfig config)
        {
            int count = LiveFormations(zone, side).Count;
            int hardCap = Math.Max(config.MaxFormationsHard, config.MaxFormationsPerZone);

            if (count >= hardCap)
                return false;

            if (count < config.MaxFormationsPerZone)
                return true;

            return SidePower(zone, side) < needed;
        }

        // Formations vivantes d'un camp dans une zone
        private static List<WargameFormation> LiveFormations(WargameZoneData zone, string side)
        {
            return zone.Formations
                .Where(f => f.ForcePower > 0 && string.Equals(f.Side, side, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Zones vides : une zone sans aucune force passe au camp qui a clairement le plus
        // d'influence autour d'elle. Influence d'un camp = force de ses zones voisines (poids 1)
        // + force des voisines des voisines (poids InfluenceDecay). Même seuil que le combat :
        // il faut un écart net (delta > ControlFlipThreshold), sinon la zone garde son camp.
        // Il faut aussi une force VOISINE : une formation à deux zones de distance ne suffit pas
        // à occuper une zone. Calculé sur un instantané : une couronne par tour, pas de cascade.
        private static void SettleEmptyZones(
            List<WargameZoneData> zones,
            Dictionary<string, WargameZoneData> zoneById,
            Dictionary<string, List<string>> links,
            HashSet<string> flipped,
            WargameCombatConfig config,
            WargameCombatResult result)
        {
            var toFlip = new List<WargameZoneData>();
            var messages = new List<string>();

            foreach (WargameZoneData zone in zones)
            {
                if (!IsFightingSide(zone.Control)) continue;
                if (flipped.Contains(zone.Id)) continue;

                // pas vide : un combat ou un axe s'en occupera
                if (SidePower(zone, WargameSide.Blue) > 0 || SidePower(zone, WargameSide.Red) > 0)
                    continue;

                double blue = 0, red = 0;
                double blueDirect = 0, redDirect = 0;   // force des voisines DIRECTES (il faut un contact)

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { zone.Id };
                var ring = new List<string>(links[zone.Id]);
                double weight = 1.0;

                for (int depth = 1; depth <= 2; depth++)
                {
                    var next = new List<string>();

                    foreach (string id in ring)
                    {
                        if (!seen.Add(id)) continue;

                        WargameZoneData n = zoneById[id];
                        blue += SidePower(n, WargameSide.Blue) * weight;
                        red += SidePower(n, WargameSide.Red) * weight;

                        if (depth == 1)
                        {
                            blueDirect += SidePower(n, WargameSide.Blue);
                            redDirect += SidePower(n, WargameSide.Red);
                            next.AddRange(links[id]);
                        }
                    }

                    ring = next;
                    weight = config.InfluenceDecay;
                }

                bool ownerIsBlue = string.Equals(zone.Control, WargameSide.Blue, StringComparison.OrdinalIgnoreCase);
                double own = ownerIsBlue ? blue : red;
                double other = ownerIsBlue ? red : blue;
                double otherDirect = ownerIsBlue ? redDirect : blueDirect;

                if (otherDirect <= 0) continue;                                       // pas de force voisine : rien ne change

                double total = own + other;
                if (total <= 0) continue;
                if ((other - own) / total <= config.ControlFlipThreshold) continue;   // écart pas assez net

                toFlip.Add(zone);
                messages.Add("  influence : empty zone " + zone.Id + " goes from " + zone.Control + " to " + OtherSide(zone.Control)
                    + " (influence " + other.ToString("0.0") + " vs " + own.ToString("0.0") + ")");
            }

            for (int i = 0; i < toFlip.Count; i++)
            {
                toFlip[i].Control = OtherSide(toFlip[i].Control);
                result.FlippedZones.Add(toFlip[i]);
                Trace(config, messages[i]);
            }
        }

        // Zones dont les formations sont posées en unités dans DCS : les zones de première ligne
        // (voisines d'une zone ennemie) et celles à moins de "depth" pas derrière elles.
        // Les zones qui ne sont ni bleues ni rouges sont toujours incluses. Si aucun front
        // n'existe, tout est inclus (rien à économiser).
        public static HashSet<string> FindDcsZones(List<WargameZoneData> zones, int depth)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (zones == null) return result;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData zone in zones)
                zoneById[zone.Id] = zone;

            Dictionary<string, List<string>> links = BuildLinks(zones);

            var distance = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();

            foreach (WargameZoneData zone in zones)
            {
                if (!IsFightingSide(zone.Control))
                {
                    result.Add(zone.Id);   // zone non réglée : on ne la filtre jamais
                    continue;
                }

                bool isFront = links[zone.Id].Any(id => IsFightingSide(zoneById[id].Control)
                    && !string.Equals(zoneById[id].Control, zone.Control, StringComparison.OrdinalIgnoreCase));

                if (isFront)
                {
                    distance[zone.Id] = 0;
                    queue.Enqueue(zone.Id);
                }
            }

            if (queue.Count == 0)
            {
                foreach (WargameZoneData zone in zones)
                    result.Add(zone.Id);
                return result;
            }

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (distance[current] >= depth) continue;

                foreach (string next in links[current])
                {
                    if (distance.ContainsKey(next)) continue;

                    distance[next] = distance[current] + 1;
                    queue.Enqueue(next);
                }
            }

            foreach (string id in distance.Keys)
                result.Add(id);

            return result;
        }

        // Formations trop affaiblies : sous DisbandBelowFraction de leur force NOMINALE, elles sont
        // dissoutes (ForcePower = 0, WargameEngineLosses retire alors leurs unités de la targetlist).
        // Seules les formations AU CONTACT de l'ennemi (zone voisine d'une zone ennemie) sont dissoutes :
        // à l'arrière, une formation affaiblie se reconstitue (ravitaillement) au lieu de disparaître.
        // A appeler après le combat (repli compris) et avant le ravitaillement.
        public static void RemoveBrokenFormations(
            List<WargameZoneData> zones, WargameTemplateCatalog catalog, WargameCombatConfig config, WargameCombatResult result)
        {
            if (zones == null || config.DisbandBelowFraction <= 0)
                return;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in zones)
                zoneById[z.Id] = z;

            Dictionary<string, List<string>> links = BuildLinks(zones);

            foreach (WargameZoneData zone in zones)
            {
                bool inContact = IsFightingSide(zone.Control) && links[zone.Id].Any(id => IsFightingSide(zoneById[id].Control)
                    && !string.Equals(zoneById[id].Control, zone.Control, StringComparison.OrdinalIgnoreCase));

                foreach (WargameFormation f in zone.Formations)
                {
                    if (f.ForcePower <= 0) continue;

                    double nominal = f.GetNominalForcePower(catalog);
                    if (nominal <= 0) continue;

                    if (f.ForcePower >= nominal * config.DisbandBelowFraction) continue;

                    string percent = (100.0 * f.ForcePower / nominal).ToString("0");

                    if (!inContact)
                    {
                        Detail(config, "  weak but not disbanded: " + f.Name + " in " + zone.Id + " (" + percent + "% of full strength, not in contact: it recovers)");
                        continue;
                    }

                    string note = f.Name + " (" + zone.Id + ", " + percent + "% of its full strength)";

                    f.ForcePower = 0;
                    result.Disbanded++;
                    result.DisbandedNotes.Add(note);

                    Log(config, "WargameEngineCombat | formation dissoute (trop affaiblie) : " + note);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------
        // Sorties pour scriptsMod et pour le joueur (appelées par WargameEngineLosses, en fin de tour)
        // ---------------------------------------------------------------------------------------------

        // Photo du camp de chaque zone : à prendre AVANT Resolve, pour savoir ce qui a changé ensuite.
        public static Dictionary<string, string> SnapshotControl(List<WargameZoneData> zones)
        {
            var snap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in zones)
                snap[z.Id] = z.Control;
            return snap;
        }

        // Nom de flag utilisable dans un trigger : lettres, chiffres et _ seulement.
        public static string ZoneFlagName(string zoneId, string side)
        {
            var sb = new System.Text.StringBuilder("wg_");
            foreach (char c in zoneId)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            sb.Append('_').Append(side.ToLowerInvariant());
            return sb.ToString();
        }

        // Une ligne par flag, relue par DEBRIEF_Master.lua :
        //   "nom"  = flag levé        "-nom" = flag baissé
        // Pour chaque zone : wg_<zone>_blue / wg_<zone>_red. Celui du propriétaire est levé, l'autre baissé
        // (zone contestée : les deux baissés). Les flags d'objectif d'axe atteint sont ajoutés tels quels.
        public static List<string> BuildFlagLines(List<WargameZoneData> zones, WargameCombatResult combat)
        {
            var lines = new List<string>();

            foreach (WargameZoneData z in zones)
            {
                bool blue = string.Equals(z.Control, WargameSide.Blue, StringComparison.OrdinalIgnoreCase);
                bool red = string.Equals(z.Control, WargameSide.Red, StringComparison.OrdinalIgnoreCase);

                lines.Add((blue ? "" : "-") + ZoneFlagName(z.Id, WargameSide.Blue));
                lines.Add((red ? "" : "-") + ZoneFlagName(z.Id, WargameSide.Red));
            }

            lines.AddRange(combat.ReachedFlags);
            return lines;
        }

        // Rapport court du tour (texte simple, affichable tel quel).
        public static List<string> BuildReport(List<WargameZoneData> zones, Dictionary<string, string> before, WargameCombatResult combat)
        {
            var lines = new List<string> { "WARGAME REPORT", "" };
            int changes = 0;

            foreach (WargameZoneData z in zones)
            {
                string old;
                if (!before.TryGetValue(z.Id, out old) || string.Equals(old, z.Control, StringComparison.OrdinalIgnoreCase))
                    continue;

                changes++;
                lines.Add(IsFightingSide(z.Control)
                    ? z.Control + " took zone " + z.Id + (IsFightingSide(old) ? " (from " + old + ")" : "")
                    : "Zone " + z.Id + " is now contested");
            }

            if (changes == 0)
                lines.Add("The front did not move.");

            int retreats = Math.Max(0, combat.MovedFormationIds.Count - combat.Advanced - combat.ReinforcedFormationIds.Count);
            if (retreats > 0)
                lines.Add(retreats + " formation(s) fell back.");

            if (combat.Worn > 0)
                lines.Add(combat.Worn + " formation(s) worn down by fighting.");

            foreach (string note in combat.DisbandedNotes)
                lines.Add("Disbanded: " + note);

            foreach (string flag in combat.ReachedFlags)
                lines.Add("Objective reached (" + flag + ")");

            int blueZones = zones.Count(z => string.Equals(z.Control, WargameSide.Blue, StringComparison.OrdinalIgnoreCase));
            int redZones = zones.Count(z => string.Equals(z.Control, WargameSide.Red, StringComparison.OrdinalIgnoreCase));
            lines.Add("");
            lines.Add("Zones held: Blue " + blueZones + " / Red " + redZones);

            return lines;
        }

        // Renforts : chaque formation vivante récupère une part de sa force nominale,
        // selon la route de sa zone, la proximité du front, et sans rien en poche coupée.
        // "combat" (facultatif) : les formations engagées ce tour récupèrent moins (EngagedRecoveryFactor).
        public static void ApplySupply(List<WargameZoneData> zones, WargameTemplateCatalog catalog, WargameCombatConfig config,
            WargameCombatResult combat = null)
        {
            if (zones == null || config.SupplyRecoveryRate <= 0)
                return;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData zone in zones)
                zoneById[zone.Id] = zone;

            Dictionary<string, List<string>> links = BuildLinks(zones);
            HashSet<string> cutOff = FindCutOffZones(zones, links, zoneById);

            int reinforced = 0, blocked = 0;
            double totalGain = 0;

            foreach (WargameZoneData zone in zones)
            {
                if (!IsFightingSide(zone.Control))
                    continue;

                bool isCut = cutOff.Contains(zone.Id);
                bool isFront = links[zone.Id].Any(id => IsFightingSide(zoneById[id].Control)
                    && !string.Equals(zoneById[id].Control, zone.Control, StringComparison.OrdinalIgnoreCase));

                double rate = config.SupplyRecoveryRate
                    * config.GetRouteRecovery(zone.SupplyRoute)
                    * (isFront ? config.FrontLineRecoveryFactor : 1.0);

                foreach (WargameFormation f in zone.Formations)
                {
                    if (f.ForcePower <= 0) continue;   // épuisée : définitif
                    if (!string.Equals(f.Side, zone.Control, StringComparison.OrdinalIgnoreCase)) continue;

                    double nominal = f.GetNominalForcePower(catalog);
                    double missing = nominal - f.ForcePower;
                    if (missing <= 0) continue;

                    if (isCut)
                    {
                        blocked++;
                        Detail(config, "  supply " + f.Name + " in " + zone.Id + " : none (cut off)");
                        continue;
                    }

                    bool engaged = combat != null && combat.EngagedFormationIds.Contains(f.FormationId);
                    double formationRate = engaged ? rate * config.EngagedRecoveryFactor : rate;

                    double gain = Math.Min(missing, nominal * formationRate);
                    Detail(config, "  supply " + f.Name + " in " + zone.Id + " : +" + gain.ToString("0.0")
                        + " (rate " + (formationRate * 100).ToString("0.0") + "% of " + nominal.ToString("0.0")
                        + (isFront ? ", front line" : "") + (engaged ? ", engaged in a fight" : "") + ")");
                    f.ForcePower += gain;
                    totalGain += gain;
                    reinforced++;
                }
            }

            Log(config, "WargameEngineCombat | ravitaillement : " + reinforced + " formation(s) renforcée(s) (+"
                + totalGain.ToString("0.0") + " de force), " + blocked + " bloquée(s) en poche coupée");
        }

        // Camp attaquant d'un axe : celui mémorisé au chargement (WargameAxis.AttackerSide), sinon,
        // pour un axe qui n'en a pas encore, celui qui tient la zone de départ.
        private static string GetAttackSide(WargameAxis axis, WargameZoneData startZone)
        {
            if (IsFightingSide(axis.AttackerSide))
                return axis.AttackerSide;

            return startZone.Control;
        }

        // L'objectif est atteint quand la dernière zone de l'axe est tenue par le camp attaquant.
        private static bool IsObjectiveReached(WargameAxis axis, string attackSide, Dictionary<string, WargameZoneData> zoneById)
        {
            WargameZoneData endZone;
            if (!zoneById.TryGetValue(axis.Path[axis.Path.Count - 1], out endZone))
                return false;

            return string.Equals(endZone.Control, attackSide, StringComparison.OrdinalIgnoreCase);
        }

        // Zones où peuvent se replier les formations d'une zone perdue, par ordre de préférence :
        //   1. la zone suivante en arrière sur le MEME axe, si elle est encore amie ;
        //   2. puis les voisines amies, de la plus éloignée de la zone d'où vient l'attaque à la plus proche.
        // Liste vide = pas de repli possible (les formations sont perdues).
        private static List<WargameZoneData> FindRetreatZones(
            FlipCandidate c,
            Dictionary<string, WargameZoneData> zoneById,
            Dictionary<string, List<string>> links,
            HashSet<string> flipped)
        {
            var options = new List<WargameZoneData>();

            int behindIndex = c.LoserIndex + (c.LoserIndex - c.FromIndex);

            if (behindIndex >= 0 && behindIndex < c.Axis.Path.Count)
            {
                WargameZoneData behind;
                if (zoneById.TryGetValue(c.Axis.Path[behindIndex], out behind) && IsValidRetreat(behind, c, flipped))
                    options.Add(behind);
            }

            List<string> neighborIds;
            if (links.TryGetValue(c.Loser.Id, out neighborIds))
            {
                var others = new List<WargameZoneData>();

                foreach (string id in neighborIds)
                {
                    WargameZoneData candidate;
                    if (zoneById.TryGetValue(id, out candidate) && IsValidRetreat(candidate, c, flipped) && !options.Contains(candidate))
                        others.Add(candidate);
                }

                options.AddRange(others.OrderByDescending(o =>
                    (o.Center.X - c.From.Center.X) * (o.Center.X - c.From.Center.X)
                    + (o.Center.Y - c.From.Center.Y) * (o.Center.Y - c.From.Center.Y)));
            }

            return options;
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

        // Force du camp donné dans la zone
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
            double factor = config.GetRouteCombat(zone.SupplyRoute);

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

        // Supériorité aérienne : le camp qui a le ciel combat avec une force majorée
        private static double AirCoef(string side, WargameCombatConfig config)
        {
            if (string.IsNullOrEmpty(config.AirSuperioritySide))
                return 1.0;

            return string.Equals(side, config.AirSuperioritySide, StringComparison.OrdinalIgnoreCase)
                ? 1.0 + config.AirSuperiorityBonus
                : 1.0;
        }

        internal static bool IsFightingSide(string control)
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
        internal static Dictionary<string, List<string>> BuildLinks(List<WargameZoneData> zones)
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

        // Journal de log.txt, coupé pendant la preview (des milliers de combats)
        private static void Log(WargameCombatConfig config, string message)
        {
            if (config != null && config.Quiet)
                return;

            FormUtils.LogRegister(message);
        }

        // Détail d'un jugement, renvoyé à la preview si elle l'a demandé
        private static void Detail(WargameCombatConfig config, string message)
        {
            if (config != null && config.Detail != null)
                config.Detail(message);
        }

        private static string FindZoneIdOf(List<WargameZoneData> zones, WargameFormation f)
        {
            foreach (WargameZoneData z in zones)
                if (z.Formations.Contains(f)) return z.Id;
            return "?";
        }

        // Photo lisible de la carte, pour le journal complet de la preview : une ligne par zone
        // (camp, terrain, route, étiquettes, force de chaque camp, formations).
        public static List<string> DescribeState(List<WargameZoneData> zones, WargameTemplateCatalog catalog, WargameCombatConfig config)
        {
            var lines = new List<string>();

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in zones)
                zoneById[z.Id] = z;

            Dictionary<string, List<string>> links = BuildLinks(zones);
            HashSet<string> cutOff = FindCutOffZones(zones, links, zoneById);
            HashSet<string> dcs = FindDcsZones(zones, config.DcsZoneDepth);

            foreach (WargameZoneData z in zones.OrderBy(x => x.Id))
            {
                var tags = new List<string>();

                if (IsFightingSide(z.Control) && links[z.Id].Any(id => IsFightingSide(zoneById[id].Control)
                    && !string.Equals(zoneById[id].Control, z.Control, StringComparison.OrdinalIgnoreCase)))
                    tags.Add("FRONT");
                if (cutOff.Contains(z.Id)) tags.Add("CUT-OFF");
                if (dcs.Contains(z.Id)) tags.Add("DCS");
                if (z.SupplySource > 0) tags.Add("SOURCE");
                if (z.Formations.Count(f => f.ForcePower > 0) == 0) tags.Add("EMPTY");

                var sb = new System.Text.StringBuilder();
                sb.Append("  ").Append(z.Id).Append(" [").Append(z.Control).Append("] ")
                  .Append(z.Terrain).Append(" x").Append(config.GetTerrainDefense(z.Terrain).ToString("0.00"))
                  .Append(", route ").Append(z.SupplyRoute)
                  .Append(" (recovery x").Append(config.GetRouteRecovery(z.SupplyRoute).ToString("0.00"))
                  .Append(", combat x").Append(config.GetRouteCombat(z.SupplyRoute).ToString("0.00")).Append(")")
                  .Append(" ").Append(tags.Count > 0 ? "<" + string.Join(" ", tags) + ">" : "")
                  .Append(" | neighbours ").Append(string.Join(",", links[z.Id].OrderBy(x => x)))
                  .Append(" | power blue ").Append(SidePower(z, WargameSide.Blue).ToString("0.0"))
                  .Append(" / red ").Append(SidePower(z, WargameSide.Red).ToString("0.0"));

                foreach (WargameFormation f in z.Formations)
                {
                    double nominal = f.GetNominalForcePower(catalog);
                    sb.Append("\r\n      ").Append(f.Name).Append(" (").Append(f.Side).Append(") ")
                      .Append(f.ForcePower.ToString("0.0")).Append(" / ").Append(nominal.ToString("0.0"))
                      .Append(nominal > 0 ? " (" + (100.0 * f.ForcePower / nominal).ToString("0") + "%)" : "");
                }

                lines.Add(sb.ToString());
            }

            return lines;
        }

        private static void Trace(WargameCombatConfig config, string message)
        {
            if (config != null && config.Trace != null)
                config.Trace(message);
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
