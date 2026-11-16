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

        // Débarquements (plans LANDING_n, voir ProcessLandings).
        // Part de force perdue par une vague qui échoue sur une plage ennemie (elle retente ensuite).
        // balance : landing_fail_loss.
        public double LandingFailLoss = 0.15;

        // Nombre de nouvelles tentatives d'une vague repoussée avant qu'elle soit abandonnée (la suivante prend le relais).
        // balance : landing_max_retries.
        public double LandingMaxRetries = 2;

        // Rembarquement : une plage perdue dont les troupes ne peuvent se replier nulle part, la flotte étant là (flag de l'appui naval),
        // les voit rembarquer au lieu d'être perdues, avec cette part de force PERDUE. balance : landing_reembark_loss.
        public double ReembarkLoss = 0.5;

        // Rempli à chaque tour par ApplyNavalCover : appui feu de la flotte sur une tête de plage tenue (zone -> valeur, et camp couvert)
        public Dictionary<string, double> NavalCoverByZone = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> NavalCoverSideByZone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Tours d'attente entre deux vagues d'un même plan (0 = les vagues partent toutes au même tour).
        // Le trigger peut l'écraser pour un plan. balance : landing_wave_gap.
        public double LandingWaveGap = 1;

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

        // Une zone coupée du ravitaillement peut-elle encore ATTAQUER ? false (défaut) = non, elle ne fait que défendre.
        public bool CutOffCanAttack = false;

        // Délai de grâce : nombre de tours pendant lesquels une zone coupée peut encore attaquer (stock emporté avec les formations,
        // typiquement après un débarquement). Le compte repart à zéro quand une vague arrive ou que la zone est ravitaillée.
        public double CutOffGraceTurns = 3;

        // ---- Convois et points de ravitaillement avancés (supply_convoys) ----
        // Activés : les zones mal ravitaillées reçoivent un convoi, qui alimente un point avancé (petite source + stock en TONNES).
        // Le point avancé REMPLACE alors le délai de grâce (CutOffGraceTurns ne sert plus, sauf pour une vague qui débarque).
        public bool SupplyConvoys = false;
        public double ConvoyMax = 3;                 // convois maximum par camp et par tour
        public double ConvoyNeedLevel = 0.6;         // une zone avec des formations, sous ce niveau, a besoin d'un convoi
        public double ForwardPointCapacity = 40;     // capacité de ravitaillement d'un point avancé actif
        public double ForwardPointStockTons = 300;   // stock maximum d'un point avancé, en tonnes (1 tonne = 1000 kg)
        public double ConvoyTruckTons = 10;          // charge d'UN véhicule de convoi, en tonnes (charge d'un convoi = nombre de véhicules de son template x ceci)
        // Hélicoptères (ravitaillement aérien d'un secteur) : charge d'une rotation par camp, en tonnes (UH-1H environ 1,2 t, Mi-8MT environ 3 t),
        // rotations maximum par point et par mission, flotte par camp et par mission (tous points confondus), et part du besoin non couvert
        // que les hélicos apportent EN PLUS d'un convoi routier (secours). Sans route, ils visent tout le besoin non couvert.
        public double HeliCargoBlueTons = 1.2;
        public double HeliCargoRedTons = 3.0;
        public double HeliMaxSorties = 1;
        public double HeliFleet = 3;
        public double HeliRoadShare = 0.3;
        public double ConvoyPayloadTons = 100;       // charge d'un convoi quand elle ne vient pas d'un template (pas de template "convoy", ou ravitaillement aérien seul)
        public double ConvoyFreeRange = 4;           // portée (en coût) que les sources couvrent SANS convoi quand les convois sont activés
        public double ForwardRebuildLoss = 0.5;      // point avancé détruit à la mission d'avant : le convoi qui le rebâtit ne recharge que (1 - ceci)

        // Usure des formations coupées dont la réserve est vide depuis 2 tours ou plus : force perdue par tour, et plancher
        public double CutOffAttrition = 0.05;
        public double CutOffAttritionFloor = 0.25;

        // Remplis avant chaque tour (ce ne sont pas des réglages) : part de la cargaison qui arrive (0 à 1).
        // Production : valeurs par défaut tant que l'état des convois n'est pas relu ; Preview : contrôles de test.
        public double ConvoyDeliveredBlue = 1.0, ConvoyDeliveredRed = 1.0;   // convois routiers
        public double AirDeliveredBlue = 0.0, AirDeliveredRed = 0.0;         // ravitaillement aérien : part (0 à 1) des tonnes PLANIFIEES par air qui arrivent (test de la preview)

        // Résultats réels de la mission jouée, par emplacement de convoi ("BLUE_1"...) : prioritaires sur les valeurs par camp
        // ci-dessus quand ils existent (lus par WargameLogistics.ReadResults avant le tour)
        public Dictionary<string, double> ConvoyDeliveredBySlot = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        // Air : TONNES réellement livrées par emplacement (scriptsMod rend des kg, convertis en tonnes à la lecture)
        public Dictionary<string, double> AirDeliveredBySlot = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Remplis par PlanConvoys : zone -> capacité du point avancé actif, et les convois du tour (journal, carte)
        public Dictionary<string, double> ForwardSupplyByZone = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public List<WargameEngineCombat.WargameConvoy> Convoys = new List<WargameEngineCombat.WargameConvoy>();

        // ---- Consommation en TONNES (1 tonne = 1000 kg) : coefficients selon l'état de la formation ----
        // La consommation du catalogue est par JOUR (24 h) ; un tour n'en vaut qu'une part (TurnDayFraction).
        // Le besoin d'une formation sur un tour = xN x consommation du template x part du jour x le coefficient de son état :
        //   arrière (pas au contact de l'ennemi) / front sans combat / engagée en attaque / engagée en défense.
        // Pour l'instant le besoin n'est que CALCULE et affiché dans la preview : il ne change rien au jeu.
        // balance : supply_use_rear, supply_use_front, supply_use_attack, supply_use_defend.
        // Part d'un jour que dure un tour (0.125 = 8 tours par jour). Calculée au chargement d'après conf_mod.lua
        // (durée de mission + pause moyenne, et jour / nuit) : voir WargameCampaignInfo.LoadTurnLength.
        public double TurnDayFraction = 0.25;
        public double SupplyUseRear = 0.5;
        public double SupplyUseFront = 1.0;
        public double SupplyUseAttack = 2.0;
        public double SupplyUseDefend = 1.5;

        // Zones vides : passent au camp qui a clairement le plus d'influence autour d'elles
        // (voir SettleEmptyZones). false = une zone vide garde son camp.
        public bool EmptyZoneInfluence = true;

        // Poids de l'influence des zones à 2 pas (les voisines directes comptent 1)
        public double InfluenceDecay = 0.5;

        // Renforts : les formations de l'arrière montent vers le front, d'une zone par mission
        // (voir MoveTowardFront). false = elles ne bougent qu'en avançant après une conquête
        // ou en se repliant.
        public bool AxisReinforcement = true;

        // Les axes actifs attirent le SURPLUS de l'armée attaquante : une formation qui n'est pas nécessaire pour garder
        // les flancs de sa zone marche, d'une zone par mission, vers la pointe d'un axe (voir PullSurplusToAxes).
        // false = elles ne suivent que le front le plus proche, comme avant. camp_init : axis_pull_surplus.
        public bool AxisPullSurplus = true;

        // Garnison d'objectif : la zone finale de chaque axe actif garde toujours UNE formation (la plus faible de la zone,
        // elle est là pour occuper le terrain, pas pour se battre). Elle ne marche plus vers le front. Si la zone est vide,
        // une formation faible de l'arrière vient l'occuper. false = comme avant. wargame_balance : objective_garrison.
        public bool ObjectiveGarrison = true;

        // Chemin des axes : true = le chemin tient compte du relief et des routes des zones (une montagne sans route coûte
        // beaucoup plus qu'une plaine avec route, mêmes tables que le ravitaillement), false = chemin le plus court en
        // distance seulement, comme avant. wargame_balance : axis_use_terrain.
        public bool AxisUseTerrain = true;

        // Chemin FLEXIBLE des axes : nombre de zones de détour accepté pour contourner une forte résistance
        // (voir AdaptAxisPaths). 0 = le chemin prévu ne change jamais. camp_init : axis_flex_detour.
        public int AxisFlexDetour = 0;

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

        // NE SERT PLUS : MaxFormationsPerZone est maintenant la limite stricte (largeur de front : seules les formations
        // les plus fortes, au plus ce nombre, se battent ; une zone ne reçoit pas plus de formations que cela).
        // Gardé pour ne pas casser les camp_init existants.
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

        // ---- Réseau de ravitaillement (voir FindCutOffZones) ----
        // true = le ravitaillement des zones dépend de la distance à une source, du terrain et des routes.
        // false = ancien fonctionnement : une zone est soit reliée à une source (niveau 100 %), soit coupée.
        public bool SupplyNetwork = true;

        // Coût qu'un ravitaillement peut payer avant de s'éteindre. Une zone de plaine avec une route normale
        // coûte environ 1 : 15 = environ 15 zones de bonne route. Une montagne sans route peut coûter plus de 10 à elle seule.
        public double SupplyRange = 15.0;

        // Niveau de ravitaillement (0 à 1) sous lequel une zone est considérée COUPEE
        public double SupplyMinLevel = 0.15;

        // Coût de passage du terrain, pour le ravitaillement (grille terrain, colonne 2)
        public Dictionary<string, double> TerrainMove = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { WargameTerrain.Plain, 1.0 }, { WargameTerrain.Hills, 1.5 }, { WargameTerrain.Mountain, 3.0 },
            { WargameTerrain.Urban, 1.5 }, { WargameTerrain.Coastal, 1.0 },
        };

        // Débit de ravitaillement de la route (grille supply_route, colonne 3). Valeurs de départ = anciennes capacités.
        public Dictionary<string, double> RouteFlow =
            new Dictionary<string, double>(WargameSupplyRoute.Capacity, StringComparer.OrdinalIgnoreCase);

        // ---- Objectifs qui comptent pour le ravitaillement (champs wargameSupply / wargameBridge de la targetlist) ----
        // Part de débit de route qui reste quand TOUS les ponts d'une zone sont détruits : 0.1 = la route ne passe plus
        // qu'à 10 % de sa capacité. Avec un pont vivant, la route est à 100 %.
        public double BridgeDownFactor = 0.1;

        // Valeur totale des objectifs-sources d'une zone qui donne un ravitaillement à 100 % ; en dessous, la source est
        // proportionnellement plus faible (un dépôt à moitié détruit = une source à moitié moins forte).
        public double SupplySourceFull = 100.0;

        // Remplis à chaque tour par ApplyObjectivesToSupply (ce ne sont pas des réglages) :
        // zone -> valeur des objectifs-sources (déjà multipliée par leur état), présente même si elle vaut 0
        public Dictionary<string, double> ObjectiveSupplyByZone = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        // zone -> part du meilleur pont encore vivant (0 à 1), seulement pour les zones qui ont un pont
        public Dictionary<string, double> BridgeAliveByZone = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        // zone-plage -> valeur du ravitaillement par mer d'un plan de débarquement actif (rempli par ApplyLandingSupply)
        public Dictionary<string, double> LandingSupplyByZone = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public double GetTerrainMove(string terrain) { return Lookup(TerrainMove, terrain); }
        public double GetRouteFlow(string route) { return Lookup(RouteFlow, route); }

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

        // Plages où un débarquement vient d'arriver ce tour : AlignFront ne les évacue pas comme saillant
        public HashSet<string> LandedZoneIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        // Parmi les engagées : celles qui attaquent, et celles qui défendent (sert au besoin en tonnes, voir ComputeSupplyDemand)
        public HashSet<int> AttackingFormationIds = new HashSet<int>();
        public HashSet<int> DefendingFormationIds = new HashSet<int>();

        // Formations dissoutes car trop faibles (voir RemoveBrokenFormations)
        public int Disbanded;
        public List<string> DisbandedNotes = new List<string>();

        // Renforts sortis de la réserve ce tour (voir DeployReinforcements)
        public HashSet<int> DeployedFormationIds = new HashSet<int>();
        public List<string> DeployNotes = new List<string>();     // une ligne par ordre traité (arrivé, en attente, refusé)
        public List<string> ConsumedFlags = new List<string>();   // flags d'ordre traités : à baisser dans la campagne
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

        // Point d'entrée : si le chemin flexible est activé, les axes prennent leur chemin ADAPTE pendant ce tour
        // seulement. Le chemin prévu (celui de l'éditeur) est toujours remis ensuite : rien n'est mémorisé,
        // chaque tour repart du plan et de l'état de la carte.
        public static WargameCombatResult Resolve(
            List<WargameZoneData> zones,
            List<WargameAxis> axes,
            HashSet<string> activeFlags,
            WargameCombatConfig config,
            Random rng,
            WargameTemplateCatalog catalog = null)   // facultatif : sert à garder au repos les formations trop faibles
        {
            Dictionary<WargameAxis, List<string>> planned = null;

            // Largeur de front : pendant ce tour, une zone ne bat qu'avec ses MaxFormationsPerZone formations les plus fortes
            // (voir SidePower et FightingFormations). Les autres attendent, sans combattre.
            _combatWidth = Math.Max(1, config.MaxFormationsPerZone);

            try
            {
                if (config.AxisFlexDetour > 0)
                    planned = AdaptAxisPaths(zones, axes, activeFlags, config);

                return ResolveCore(zones, axes, activeFlags, config, rng, catalog);
            }
            finally
            {
                _combatWidth = 0;

                if (planned != null)
                {
                    foreach (KeyValuePair<WargameAxis, List<string>> kv in planned)
                        kv.Key.Path = kv.Value;
                }
            }
        }

        // Marche : nombre de zones qu'une formation peut parcourir en UNE mission (montée vers le front, surplus vers un axe).
        // Elle peut TRAVERSER une zone pleine : la limite de formations ne compte qu'à l'arrivée, la formation s'arrête à la
        // dernière zone avec de la place sur son trajet. Sans ça, une zone pleine bloquerait toute la file derrière elle.
        private const int MarchSteps = 2;

        // Nombre de formations qui se battent par zone pendant Resolve (0 = toutes : hors Resolve, pour les journaux).
        // Champ statique plutôt qu'un paramètre de plus dans chaque méthode ; propre à chaque thread.
        [ThreadStatic]
        private static int _combatWidth;

        private static WargameCombatResult ResolveCore(
            List<WargameZoneData> zones,
            List<WargameAxis> axes,
            HashSet<string> activeFlags,
            WargameCombatConfig config,
            Random rng,
            WargameTemplateCatalog catalog)
        {
            var result = new WargameCombatResult();

            // Les renforts arrivent AVANT le combat : ils comptent dans le tour qui commence
            DeployReinforcements(zones, activeFlags, config, result);

            // Débarquements : les plans lancés par un trigger tentent leur plage (avant le combat des axes)
            ProcessLandings(zones, axes, activeFlags, config, rng, result);

            // Formations restées en mer (vagues abandonnées) : elles débarquent dès que la plage est à leur camp
            LandLeftovers(zones, axes, config, rng, result);

            // La flotte couvre de ses feux les têtes de plage tenues (défense de la plage contre les contre-attaques)
            ApplyNavalCover(zones, activeFlags, config);

            // Ravitaillement par mer des plages tenues (après le débarquement : la plage vient peut-être d'être prise)
            ApplyLandingSupply(zones, activeFlags, config);

            // Convois : un par secteur mal ravitaillé (plafonné), qui recharge son point avancé
            PlanConvoys(zones, config, catalog);

            if (zones == null || axes == null || axes.Count == 0)
                return result;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData zone in zones)
                zoneById[zone.Id] = zone;

            Dictionary<string, List<string>> links = BuildLinks(zones);
            HashSet<string> cutOff = FindCutOffZones(zones, links, zoneById, config);

            // Depuis combien de tours chaque zone est coupée (le compte repart à zéro si la zone change de camp ou est ravitaillée)
            foreach (WargameZoneData z in zones)
            {
                if (cutOff.Contains(z.Id) && IsFightingSide(z.Control) && !IsOffMapId(z.Id))   // boîtes RESERVE / LANDING : jamais "coupées"
                {
                    if (!string.Equals(z.CutOffSide, z.Control, StringComparison.OrdinalIgnoreCase))
                    {
                        z.CutOffSide = z.Control;
                        z.CutOffTurns = 0;
                    }
                    z.CutOffTurns++;
                }
                else
                {
                    z.CutOffTurns = 0;
                    z.CutOffSide = "";
                }
            }

            Detail(config, "  cut-off zones (no road to a supply source): " + (cutOff.Count == 0 ? "none" : string.Join(", ", cutOff.OrderBy(x => x))));

            // Sources de chaque camp (force en %), et ponts : détruits, ils ralentissent le ravitaillement de leur zone
            foreach (string sideName in new[] { WargameSide.Blue, WargameSide.Red })
            {
                List<WargameZoneData> sideZoneList = zones.Where(z => string.Equals(z.Control, sideName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!HasSupplySystem(sideZoneList, config))
                {
                    Detail(config, "  supply sources " + sideName + " : NONE - no supply network for this side (set 'Source' on a zone in the wargame editor, or wargameSupply on an objective)");
                    continue;
                }

                var parts = sideZoneList.Where(z => SourceStrength(z, config) > 0).OrderBy(z => z.Id)
                    .Select(z => z.Id + " " + (SourceStrength(z, config) * 100).ToString("0") + "%").ToList();
                Detail(config, "  supply sources " + sideName + " : " + (parts.Count == 0 ? "ALL DESTROYED - every zone is cut off" : string.Join(", ", parts)));

                foreach (WargameZoneData z in sideZoneList.OrderBy(x => x.Id))
                {
                    double bridge;
                    if (config.BridgeAliveByZone.TryGetValue(z.Id, out bridge))
                        Detail(config, "  supply bridge in " + z.Id + " : " + (bridge * 100).ToString("0") + "% alive -> road flow x"
                            + (config.BridgeDownFactor + (1.0 - config.BridgeDownFactor) * bridge).ToString("0.00"));
                }
            }

            // Zones ravitaillées à moins de 100 % : niveau, source, coût et pas le plus dur
            if (_supplyLevels != null)
                foreach (KeyValuePair<string, double> kv in _supplyLevels.Where(x => x.Value < 0.995).OrderBy(x => x.Key))
                    Detail(config, "  supply level " + kv.Key + " : " + (kv.Value * 100).ToString("0") + "% (" + _supplyNotes[kv.Key] + ")");

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

                    // Plage où un débarquement vient d'arriver : le tour est celui du débarquement, le combat reprend au suivant
                    if (result.LandedZoneIds.Contains(a.Id) || result.LandedZoneIds.Contains(b.Id))
                        continue;

                    bool aAttacks = string.Equals(a.Control, attackSide, StringComparison.OrdinalIgnoreCase);

                    // Sans ravitaillement, pas d'offensive : la zone coupée peut défendre, pas attaquer
                    WargameZoneData attackerZone = aAttacks ? a : b;
                    if (!config.CutOffCanAttack && cutOff.Contains(attackerZone.Id))
                    {
                        if (ExhaustedForOffensive(attackerZone, attackerZone.Control, config))
                        {
                            Trace(config, "  " + axis.Name + " : " + attackerZone.Id + " cannot attack " + (aAttacks ? b : a).Id
                                + ": cut off from supply, its own stock is gone");
                            continue;
                        }

                        double left = EffectiveGrace(config) - attackerZone.CutOffTurns + 1;
                        Trace(config, "  " + axis.Name + " : " + attackerZone.Id + " is cut off but attacks on its own stock ("
                            + left.ToString("0") + " turn(s) left, this one included)");
                    }

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
                double navalCover = NavalCoverOf(p.DefZone, p.DefendSide, config);
                double defense = defensePower * defenseSupply * terrainCoef * defenseAir + navalCover;

                double total = attack + defense;
                if (total <= 0)
                    continue;   // deux zones vides face à face : rien ne se passe

                double delta = (attack - defense) / total;
                bool flips = Math.Abs(delta) > config.ControlFlipThreshold;

                Trace(config, "  " + p.Axis.Name + " : " + p.AttZone.Id + " attacks " + p.DefZone.Id
                    + " | attack " + attack.ToString("0.0")
                    + " (force " + attackPower.ToString("0.0") + " x supply " + attackSupply.ToString("0.00") + " x air " + attackAir.ToString("0.00") + ")"
                    + " vs defense " + defense.ToString("0.0") + (navalCover > 0 ? " (naval fire support " + navalCover.ToString("0.0") + " included)" : "")
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
                // Qui attaque, qui défend (pour le besoin en tonnes) : une formation qui attaque ne compte pas comme défenseuse
                foreach (WargameFormation fa in DirectionFormations(allocations, p.AttZone, p.AttackSide, p.DefZone.Id))
                    result.AttackingFormationIds.Add(fa.FormationId);
                foreach (WargameFormation fd in DirectionFormations(allocations, p.DefZone, p.DefendSide, p.AttZone.Id))
                    if (!result.AttackingFormationIds.Contains(fd.FormationId))
                        result.DefendingFormationIds.Add(fd.FormationId);

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

            result.DefendingFormationIds.ExceptWith(result.AttackingFormationIds);

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

                // Les vainqueurs emportent avec eux leur retard de ravitaillement dans la zone conquise :
                // sinon chaque avance remettrait le délai de grâce à zéro (cutoff_grace_turns)
                if (c.From.CutOffTurns != 0 && string.Equals(c.From.CutOffSide, c.WinnerSide, StringComparison.OrdinalIgnoreCase))
                {
                    c.Loser.CutOffTurns = c.From.CutOffTurns;
                    c.Loser.CutOffSide = c.WinnerSide;
                }
                else
                {
                    c.Loser.CutOffTurns = 0;
                    c.Loser.CutOffSide = "";
                }
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
                        // Tête de plage perdue, flotte présente : la formation rembarque (gros malus) au lieu d'être perdue
                        WargameZoneData fleet = FleetFor(zones, c.Loser, oldSide, activeFlags);
                        if (fleet != null)
                        {
                            double powerBefore = f.ForcePower;
                            c.Loser.Formations.Remove(f);
                            fleet.Formations.Add(f);
                            f.ForcePower = Math.Max(0, f.ForcePower * (1.0 - config.ReembarkLoss));
                            f.Wave = 0;   // restera en mer, et redébarquera quand la plage sera de nouveau à son camp (LandLeftovers)
                            result.MovedFormationIds.Add(f.FormationId);
                            moved++;
                            Trace(config, "  re-embark: " + f.Name + " leaves " + c.Loser.Id + " by sea to " + fleet.Id + " : "
                                + powerBefore.ToString("0.0") + " -> " + f.ForcePower.ToString("0.0"));
                            continue;
                        }

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

            // Le surplus de l'attaquant file vers la pointe des axes AVANT la montée vers le front le plus proche
            // (une formation déjà déplacée ne bouge plus ce tour)
            // Garnisons des zones objectif : désignées et complétées AVANT les mouvements du surplus
            MarkGarrisons(zones, zoneById, axes, activeFlags, config);
            FeedObjectiveGarrisons(zones, zoneById, links, axes, activeFlags, config, result, catalog);

            if (config.AxisPullSurplus)
                PullSurplusToAxes(zones, zoneById, links, axes, activeFlags, attackTargets, config, result, catalog);

            if (config.AxisReinforcement)
                MoveTowardFront(zones, zoneById, links, attackTargets, config, result, catalog);

            if (config.EmptyZoneInfluence)
                SettleEmptyZones(zones, zoneById, links, flipped, config, result);

            // ---- Orientation des formations (état final du tour), pour la repose dans DCS ----

            AllocateForces(zones, zoneById, links, FindCutOffZones(zones, links, zoneById, config), attackTargets, frontPairs, config, false, result);

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

                List<WargameFormation> live = FightingFormations(zone, side);
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

            return FightingFormations(zone, side);
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
                AllocateForces(zones, zoneById, links, FindCutOffZones(zones, links, zoneById, config), attackTargets, frontPairs, config, false, null);

            // 1. Repérer les saillants (état actuel)
            var salients = new List<WargameZoneData>();

            foreach (WargameZoneData zone in zones)
            {
                if (!IsFightingSide(zone.Control)) continue;
                if (flipped.Contains(zone.Id)) continue;
                if (result.LandedZoneIds.Contains(zone.Id)) continue;   // tête de pont du tour : on la tient
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
                        if (result.MovedFormationIds.Contains(f.FormationId))
                        {
                            Detail(config, "  reinforce " + side + " : " + f.Name + " already moved this turn (" + zone.Id + ")");
                            continue;   // déjà bougée ce tour
                        }

                        if (IsGarrison(f))
                        {
                            Detail(config, "  reinforce " + side + " : " + f.Name + " stays in " + zone.Id + " (objective garrison)");
                            continue;
                        }

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

                        // Marche vers le front : jusqu'à MarchSteps zones, en traversant les zones pleines ;
                        // à chaque pas, la voisine plus proche du front qui a de la place, puis celle à qui il manque le plus de force.
                        // La formation s'arrête à la dernière zone avec de la place sur son trajet.
                        WargameZoneData best = null;
                        WargameZoneData walking = zone;
                        int walkDistance = zoneDistance;

                        for (int step = 0; step < MarchSteps && walkDistance > 0; step++)
                        {
                            WargameZoneData next = null;
                            double nextScore = double.MinValue;
                            bool nextHasRoom = false;

                            foreach (string id in links[walking.Id])
                            {
                                int d;
                                if (!distance.TryGetValue(id, out d) || d != walkDistance - 1) continue;

                                WargameZoneData target = zoneById[id];

                                double targetNeed;
                                need.TryGetValue(id, out targetNeed);   // 0 si ce n'est pas la première ligne

                                bool hasRoom = AcceptsFormation(target, side, targetNeed, config);
                                double score = targetNeed - SidePower(target, side);

                                if (next == null || (hasRoom && !nextHasRoom) || (hasRoom == nextHasRoom && score > nextScore))
                                {
                                    next = target;
                                    nextScore = score;
                                    nextHasRoom = hasRoom;
                                }
                            }

                            if (next == null) break;

                            walking = next;
                            walkDistance--;

                            if (nextHasRoom) best = walking;
                        }

                        if (best == null)
                        {
                            Detail(config, "  reinforce " + side + " : " + f.Name + " waits in " + zone.Id + " (distance to front " + zoneDistance
                                + ", walked to " + walking.Id + ", no room on the way)");
                            continue;
                        }

                        Detail(config, "  reinforce " + side + " : " + f.Name + " " + zone.Id + " -> " + best.Id + " (distance to front " + zoneDistance + ")");

                        zone.Formations.Remove(f);
                        best.Formations.Add(f);
                        result.MovedFormationIds.Add(f.FormationId);
                        result.ReinforcedFormationIds.Add(f.FormationId);
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // Le surplus file vers les axes
        // ------------------------------------------------------------------------------------------

        private class AxisHead
        {
            public WargameAxis Axis;
            public WargameZoneData Head;                 // dernière zone amie de l'axe, au contact de la suivante
            public double Missing;                       // force qui manque à la pointe pour percer la zone suivante
            public double Needed;                        // force d'attaque qu'il faut pour percer la zone suivante
            public double AttackMissing;                 // ce qui manque à la SEULE formation qui attaque (la plus forte), les autres gardent les flancs
            public int Room;                             // places libres à la pointe (largeur de front - formations déjà là)
            public Dictionary<string, int> Distance;     // nombre de pas, à travers les zones amies, jusqu'à la pointe
        }

        // Rotation à la pointe d'un axe bloquée : une formation de réserve remplace celle de la pointe seulement
        // si elle vaut au moins ce multiple de sa force (évite les échanges pour un gain minime).
        private const double RotationGain = 1.25;

        // Marge sur la force d'attaque nécessaire (le vrai combat tient aussi compte du ravitaillement, de l'air et de la chance) :
        // sans elle, une pointe "juste suffisante sur le papier" ne ferait jamais de rotation.
        private const double RotationNeedMargin = 1.3;

        // La plus faible formation vivante d'une zone qui n'a pas déjà bougé ce tour (null = aucune)
        private static WargameFormation WeakestMovable(WargameZoneData zone, string side, WargameCombatResult result)
        {
            return LiveFormations(zone, side)
                .Where(f => !result.MovedFormationIds.Contains(f.FormationId) && !IsGarrison(f))
                .OrderBy(f => f.ForcePower)
                .FirstOrDefault();
        }

        // ------------------------------------------------------------------------------------------
        // Garnison des zones objectif (zone finale de chaque axe actif)
        // ------------------------------------------------------------------------------------------

        // Formations désignées garnison pour ce tour (renseigné par MarkGarrisons, lu par IsGarrison)
        [ThreadStatic]
        private static HashSet<int> _garrisonIds;

        // Une relève remplace la garnison seulement si la nouvelle vaut moins que cette part de l'ancienne
        private const double GarrisonReliefRatio = 0.6;

        private static bool IsGarrison(WargameFormation f)
        {
            return _garrisonIds != null && _garrisonIds.Contains(f.FormationId);
        }

        // Zones finales des axes actifs, tenues par un des deux camps (sans doublon)
        private static List<WargameZoneData> ObjectiveZonesOf(
            List<WargameAxis> axes, HashSet<string> activeFlags, Dictionary<string, WargameZoneData> zoneById)
        {
            var list = new List<WargameZoneData>();
            if (axes == null) return list;

            foreach (WargameAxis axis in axes)
            {
                if (!axis.IsActive(activeFlags) || axis.Path.Count == 0) continue;

                WargameZoneData end;
                if (!zoneById.TryGetValue(axis.Path[axis.Path.Count - 1], out end)) continue;
                if (!IsFightingSide(end.Control) || list.Contains(end)) continue;

                list.Add(end);
            }

            return list;
        }

        // Désigne la garnison de chaque zone objectif : sa formation la plus faible
        private static void MarkGarrisons(
            List<WargameZoneData> zones, Dictionary<string, WargameZoneData> zoneById,
            List<WargameAxis> axes, HashSet<string> activeFlags, WargameCombatConfig config)
        {
            _garrisonIds = new HashSet<int>();
            if (!config.ObjectiveGarrison) return;

            foreach (WargameZoneData z in ObjectiveZonesOf(axes, activeFlags, zoneById))
            {
                WargameFormation g = LiveFormations(z, z.Control).OrderBy(f => f.ForcePower).FirstOrDefault();
                if (g != null)
                {
                    _garrisonIds.Add(g.FormationId);
                    Detail(config, "  garrison " + z.Control + " : " + g.Name + " holds objective zone " + z.Id);
                }
            }
        }

        // Complète les garnisons : zone objectif vide -> la plus faible formation disponible de l'arrière vient l'occuper
        // (elle marche MarchSteps zones par tour) ; garnison nettement plus forte qu'une formation toute proche -> relève.
        private static void FeedObjectiveGarrisons(
            List<WargameZoneData> zones,
            Dictionary<string, WargameZoneData> zoneById,
            Dictionary<string, List<string>> links,
            List<WargameAxis> axes,
            HashSet<string> activeFlags,
            WargameCombatConfig config,
            WargameCombatResult result,
            WargameTemplateCatalog catalog)
        {
            if (!config.ObjectiveGarrison) return;

            foreach (WargameZoneData z in ObjectiveZonesOf(axes, activeFlags, zoneById))
            {
                string side = z.Control;
                string enemy = OtherSide(side);

                WargameFormation garrison = LiveFormations(z, side).OrderBy(f => f.ForcePower).FirstOrDefault();

                // Distance (en pas, à travers les zones amies) jusqu'à la zone objectif
                var dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { z.Id, 0 } };
                var queue = new Queue<string>();
                queue.Enqueue(z.Id);

                while (queue.Count > 0)
                {
                    string current = queue.Dequeue();

                    foreach (string next in links[current])
                    {
                        if (dist.ContainsKey(next)) continue;
                        if (!string.Equals(zoneById[next].Control, side, StringComparison.OrdinalIgnoreCase)) continue;

                        dist[next] = dist[current] + 1;
                        queue.Enqueue(next);
                    }
                }

                // Candidats : formations de l'arrière (zones qui ne touchent pas l'ennemi), libres, pas trop faibles
                WargameFormation best = null;
                WargameZoneData bestZone = null;
                double bestScore = double.MaxValue;

                foreach (WargameZoneData x in zones)
                {
                    if (x.Id == z.Id || !dist.ContainsKey(x.Id)) continue;
                    if (garrison != null && dist[x.Id] > MarchSteps) continue;   // relève : seulement si elle arrive ce tour
                    if (links[x.Id].Any(id => string.Equals(zoneById[id].Control, enemy, StringComparison.OrdinalIgnoreCase))) continue;

                    foreach (WargameFormation f in LiveFormations(x, side))
                    {
                        if (result.MovedFormationIds.Contains(f.FormationId) || IsGarrison(f)) continue;

                        if (catalog != null && config.DisbandBelowFraction > 0)
                        {
                            double nominal = f.GetNominalForcePower(catalog);
                            if (nominal > 0 && f.ForcePower < nominal * config.DisbandBelowFraction) continue;
                        }

                        // Plus faible ET proche : la distance pèse dans le choix, sinon la même formation faible
                        // est réclamée tour après tour par des objectifs opposés et fait des allers-retours
                        double score = f.ForcePower * (1.0 + 0.3 * dist[x.Id]);

                        if (best == null || score < bestScore)
                        {
                            best = f;
                            bestZone = x;
                            bestScore = score;
                        }
                    }
                }

                if (best == null)
                {
                    if (garrison == null)
                        Detail(config, "  garrison " + side + " : objective zone " + z.Id + " is empty and no free formation is available in the rear");
                    continue;
                }

                // Relève : échange sur place
                if (garrison != null)
                {
                    if (best.ForcePower >= garrison.ForcePower * GarrisonReliefRatio) continue;   // pas assez de gain

                    z.Formations.Remove(garrison);
                    bestZone.Formations.Add(garrison);
                    bestZone.Formations.Remove(best);
                    z.Formations.Add(best);

                    result.MovedFormationIds.Add(garrison.FormationId);
                    result.MovedFormationIds.Add(best.FormationId);

                    Detail(config, "  garrison " + side + " : RELIEF " + best.Name + " (" + best.ForcePower.ToString("0.0") + ") " + bestZone.Id + " -> " + z.Id
                        + " replaces " + garrison.Name + " (" + garrison.ForcePower.ToString("0.0") + ") sent back to " + bestZone.Id);
                    continue;
                }

                // Zone vide : la formation marche vers elle (MarchSteps zones par tour), s'arrête à la dernière zone avec de la place
                WargameZoneData walking = bestZone;
                WargameZoneData stop = null;
                int d = dist[bestZone.Id];

                for (int step = 0; step < MarchSteps && d > 0; step++)
                {
                    WargameZoneData next = null;

                    foreach (string id in links[walking.Id])
                    {
                        int dn;
                        if (!dist.TryGetValue(id, out dn) || dn != d - 1) continue;

                        WargameZoneData candidate = zoneById[id];
                        if (next == null || AcceptsFormation(candidate, side, 0.0, config)) next = candidate;
                    }

                    if (next == null) break;

                    walking = next;
                    d--;

                    if (AcceptsFormation(walking, side, 0.0, config)) stop = walking;
                }

                if (stop == null)
                {
                    Detail(config, "  garrison " + side + " : " + best.Name + " waits in " + bestZone.Id + " (no room on the way to " + z.Id + ")");
                    continue;
                }

                bestZone.Formations.Remove(best);
                stop.Formations.Add(best);
                result.MovedFormationIds.Add(best.FormationId);

                Detail(config, "  garrison " + side + " : " + best.Name + " (" + best.ForcePower.ToString("0.0") + ") " + bestZone.Id + " -> " + stop.Id
                    + (stop.Id == z.Id ? " (occupies objective zone " : " (on its way to objective zone ") + z.Id + ")");
            }

            // Les garnisons ont pu changer : on les désigne de nouveau avant les mouvements du surplus
            MarkGarrisons(zones, zoneById, axes, activeFlags, config);
        }

        // Pointe d'un axe : index de la première zone du chemin tenue par l'attaquant dont la suivante est à l'ennemi.
        // -1 = pas de contact sur cet axe.
        private static int FindAxisHead(WargameAxis axis, string attackSide, Dictionary<string, WargameZoneData> zoneById)
        {
            for (int i = 0; i < axis.Path.Count - 1; i++)
            {
                WargameZoneData a, b;
                if (!zoneById.TryGetValue(axis.Path[i], out a) || !zoneById.TryGetValue(axis.Path[i + 1], out b))
                    continue;

                if (string.Equals(a.Control, attackSide, StringComparison.OrdinalIgnoreCase)
                    && IsFightingSide(b.Control)
                    && !string.Equals(b.Control, attackSide, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        // Force à garder dans une zone face aux voisines ennemies, la cible d'attaque exceptée
        // (même seuil que le combat et AllocateForces : force ennemie / (rapport du seuil x ravitaillement x terrain))
        private static double GuardNeed(
            WargameZoneData zone, string side, string attackId,
            Dictionary<string, WargameZoneData> zoneById, Dictionary<string, List<string>> links,
            HashSet<string> cutOff, WargameCombatConfig config, double ratio)
        {
            string enemySide = OtherSide(side);
            double ownMult = Math.Max(0.01, SupplyFactor(zone, cutOff, config) * config.GetTerrainDefense(zone.Terrain) * AirCoef(side, config));
            double need = 0;

            foreach (string id in links[zone.Id])
            {
                WargameZoneData d = zoneById[id];

                if (!string.Equals(d.Control, enemySide, StringComparison.OrdinalIgnoreCase)) continue;
                if (attackId != null && string.Equals(id, attackId, StringComparison.OrdinalIgnoreCase)) continue;

                need += SidePower(d, enemySide) * SupplyFactor(d, cutOff, config) * AirCoef(enemySide, config) / (ratio * ownMult);
            }

            return need;
        }

        // Une armée ne reste pas plantée derrière pendant que la pointe de son offensive manque de force.
        // Pour chaque camp qui attaque sur un axe actif :
        //   - pointe de chaque axe = dernière zone amie du chemin, au contact de la suivante ; il lui "manque" la force
        //     qu'il faut pour percer la zone suivante (force ennemie x terrain x rapport du seuil) moins ce qu'elle a ;
        //   - dans chaque zone, une formation reste si la zone en a besoin pour garder ses flancs (plus faibles d'abord),
        //     si elle attaque (la plus forte), ou si c'est la seule d'une zone au contact de l'ennemi ;
        //   - les autres sont LIBRES : d'un pas par mission, à travers les zones amies, vers la pointe de l'axe à qui il
        //     manque le plus de force, en suivant de préférence le chemin de l'axe.
        // Une formation trop faible (sous le seuil de dissolution) ne marche pas : elle récupère d'abord.
        private static void PullSurplusToAxes(
            List<WargameZoneData> zones,
            Dictionary<string, WargameZoneData> zoneById,
            Dictionary<string, List<string>> links,
            List<WargameAxis> axes,
            HashSet<string> activeFlags,
            Dictionary<string, string> attackTargets,
            WargameCombatConfig config,
            WargameCombatResult result,
            WargameTemplateCatalog catalog)
        {
            if (axes == null) return;

            HashSet<string> cutOff = FindCutOffZones(zones, links, zoneById, config);

            double threshold = Math.Min(config.ControlFlipThreshold, 0.9);
            double ratio = (1.0 + threshold) / (1.0 - threshold);

            foreach (string side in new[] { WargameSide.Blue, WargameSide.Red })
            {
                string enemySide = OtherSide(side);

                // 1. Pointes d'axe de ce camp
                var heads = new List<AxisHead>();

                foreach (WargameAxis axis in axes)
                {
                    if (!axis.IsActive(activeFlags)) continue;

                    WargameZoneData startZone;
                    if (!zoneById.TryGetValue(axis.Path[0], out startZone) || !IsFightingSide(startZone.Control)) continue;
                    if (!string.Equals(GetAttackSide(axis, startZone), side, StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsObjectiveReached(axis, side, zoneById)) continue;

                    int headIndex = FindAxisHead(axis, side, zoneById);
                    if (headIndex < 0) continue;

                    WargameZoneData head = zoneById[axis.Path[headIndex]];
                    WargameZoneData target = zoneById[axis.Path[headIndex + 1]];

                    double needed = SidePower(target, enemySide) * config.GetTerrainDefense(target.Terrain) * ratio;
                    double missing = needed - SidePower(head, side);

                    // Distance (en pas, à travers les zones amies) de chaque zone à la pointe
                    var distance = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var queue = new Queue<string>();
                    distance[head.Id] = 0;
                    queue.Enqueue(head.Id);

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

                    Detail(config, "  axis pull " + side + " : axis '" + axis.Name + "' head " + head.Id + " -> " + target.Id
                        + ", needs " + needed.ToString("0.0") + " (has " + SidePower(head, side).ToString("0.0") + ")");

                    int room = Math.Max(0, config.MaxFormationsPerZone - LiveFormations(head, side).Count);

                    WargameFormation strongestInHead = LiveFormations(head, side).OrderByDescending(x => x.ForcePower).FirstOrDefault();
                    double attackMissing = needed * RotationNeedMargin - (strongestInHead != null ? strongestInHead.ForcePower : 0.0);

                    heads.Add(new AxisHead { Axis = axis, Head = head, Missing = missing, Needed = needed, AttackMissing = attackMissing, Room = room, Distance = distance });
                }

                if (heads.Count == 0)
                {
                    Detail(config, "  axis pull " + side + " : no active axis head for this side");
                    continue;
                }

                Detail(config, "  axis pull " + side + " : engine with march " + MarchSteps + " zone(s) per turn, " + heads.Count + " axis head(s)");
                foreach (AxisHead hh in heads)
                    Detail(config, "  axis pull " + side + " : head " + hh.Head.Id + " (axis " + hh.Axis.Name + ") missing " + hh.Missing.ToString("0.0")
                        + ", room " + hh.Room + ", reachable zones: " + string.Join(",", hh.Distance.Keys.OrderBy(k => k)));

                var headZoneIds = new HashSet<string>(heads.Select(h => h.Head.Id), StringComparer.OrdinalIgnoreCase);

                // 2. Zones de l'arrière vers la pointe : la place se libère devant
                List<WargameZoneData> sideZones = zones
                    .Where(z => string.Equals(z.Control, side, StringComparison.OrdinalIgnoreCase)
                        && !headZoneIds.Contains(z.Id)
                        && heads.Any(h => h.Distance.ContainsKey(z.Id)))
                    .OrderBy(z => heads.Where(h => h.Distance.ContainsKey(z.Id)).Min(h => h.Distance[z.Id]))
                    .ToList();

                foreach (WargameZoneData zone in sideZones)
                {
                    List<WargameFormation> live = LiveFormations(zone, side)
                        .Where(f => !result.MovedFormationIds.Contains(f.FormationId)).ToList();
                    if (live.Count == 0) continue;

                    Detail(config, "  axis pull " + side + " : checking zone " + zone.Id + " (" + live.Count + " formation(s) able to move)");

                    string attackId;
                    attackTargets.TryGetValue(zone.Id, out attackId);

                    double guard = GuardNeed(zone, side, attackId, zoneById, links, cutOff, config, ratio);
                    bool touchesEnemy = links[zone.Id].Any(id => string.Equals(zoneById[id].Control, enemySide, StringComparison.OrdinalIgnoreCase));

                    WargameFormation main = attackId != null ? LiveFormations(zone, side).OrderByDescending(f => f.ForcePower).First() : null;

                    // Ce qui reste : les plus faibles gardent d'abord, jusqu'à couvrir le besoin de garde
                    double kept = 0;
                    int keptCount = main != null ? 1 : 0;
                    var free = new List<WargameFormation>();

                    foreach (WargameFormation f in LiveFormations(zone, side).OrderBy(x => x.ForcePower))
                    {
                        if (f == main) continue;

                        if (IsGarrison(f))
                        {
                            kept += f.ForcePower;
                            keptCount++;
                            Detail(config, "    axis pull " + side + " : " + f.Name + " stays in " + zone.Id + " (objective garrison)");
                            continue;
                        }

                        bool mustStay = kept < guard || (touchesEnemy && keptCount == 0);
                        if (mustStay)
                        {
                            kept += f.ForcePower;
                            keptCount++;
                            Detail(config, "    axis pull " + side + " : " + f.Name + " stays in " + zone.Id + " (guard: needs " + guard.ToString("0.0")
                                + ", kept " + kept.ToString("0.0") + ", touches enemy " + touchesEnemy + ")");
                            continue;
                        }

                        if (result.MovedFormationIds.Contains(f.FormationId)) continue;
                        free.Add(f);
                    }

                    foreach (WargameFormation f in free.OrderByDescending(x => x.ForcePower))
                    {
                        // Formation trop faible : elle récupère au lieu de marcher au contact
                        if (catalog != null && config.DisbandBelowFraction > 0)
                        {
                            double nominal = f.GetNominalForcePower(catalog);
                            if (nominal > 0 && f.ForcePower < nominal * config.DisbandBelowFraction)
                            {
                                Detail(config, "    axis pull " + side + " : " + f.Name + " stays in " + zone.Id + " (too weak, recovers)");
                                continue;
                            }
                        }

                        // L'axe à qui il manque le plus de force, atteignable depuis cette zone
                        // (seules les pointes qui ont de la place comptent ; parmi elles, celle à qui il manque le plus de force)
                        AxisHead chosen = null;
                        bool rotation = false;
                        foreach (AxisHead h in heads)
                        {
                            if (h.Room <= 0 || !h.Distance.ContainsKey(zone.Id)) continue;   // pointe pleine : elle ne peut rien recevoir, on regarde l'axe suivant

                            if (chosen == null || h.Missing > chosen.Missing || (h.Missing == chosen.Missing && h.Room > chosen.Room)) chosen = h;
                        }

                        // Rotation : toutes les pointes sont pleines, mais une pointe à qui il manque de la force tient une formation
                        // nettement plus faible que celle-ci. On vise cette pointe : sur place on échange, sinon on se rapproche.
                        if (chosen == null)
                        {
                            foreach (AxisHead h in heads)
                            {
                                if (h.Room > 0 || h.AttackMissing <= 0 || !h.Distance.ContainsKey(zone.Id)) continue;

                                WargameFormation weakest = WeakestMovable(h.Head, side, result);
                                if (weakest == null || f.ForcePower < weakest.ForcePower * RotationGain) continue;

                                if (chosen == null || h.AttackMissing > chosen.AttackMissing) chosen = h;
                            }

                            rotation = chosen != null;
                        }

                        if (chosen == null)
                        {
                            Detail(config, "    axis pull " + side + " : " + f.Name + " stays in " + zone.Id + " (no axis needs force or room, or zone not linked to a head)");
                            break;   // aucun axe n'a besoin de plus de force
                        }

                        // Échange à la pointe : la plus forte arrive, la plus faible de la pointe recule dans la zone d'où elle vient
                        if (rotation && chosen.Distance[zone.Id] <= MarchSteps)
                        {
                            WargameFormation weak = WeakestMovable(chosen.Head, side, result);
                            if (weak != null)
                            {
                                chosen.Head.Formations.Remove(weak);
                                zone.Formations.Add(weak);
                                zone.Formations.Remove(f);
                                chosen.Head.Formations.Add(f);

                                result.MovedFormationIds.Add(f.FormationId);
                                result.MovedFormationIds.Add(weak.FormationId);
                                result.ReinforcedFormationIds.Add(f.FormationId);
                                chosen.Missing -= f.ForcePower - weak.ForcePower;
                                chosen.AttackMissing = chosen.Needed * RotationNeedMargin - LiveFormations(chosen.Head, side).Max(x => x.ForcePower);

                                Detail(config, "  axis pull " + side + " : ROTATION " + f.Name + " (" + f.ForcePower.ToString("0.0") + ") " + zone.Id + " -> " + chosen.Head.Id
                                    + " replaces " + weak.Name + " (" + weak.ForcePower.ToString("0.0") + ") sent back to " + zone.Id);
                                continue;
                            }
                        }

                        // Marche vers la pointe : jusqu'à MarchSteps zones, de préférence sur le chemin de l'axe (route).
                        // Les zones pleines se traversent ; on s'arrête à la dernière zone qui a de la place.
                        WargameZoneData best = null;
                        WargameZoneData walking = zone;
                        int walkDistance = chosen.Distance[zone.Id];

                        for (int step = 0; step < MarchSteps && walkDistance > 0; step++)
                        {
                            WargameZoneData next = null;
                            int nextPathIndex = -2;
                            bool nextHasRoom = false;

                            foreach (string id in links[walking.Id])
                            {
                                int d;
                                if (!chosen.Distance.TryGetValue(id, out d) || d != walkDistance - 1) continue;

                                WargameZoneData candidate = zoneById[id];
                                bool hasRoom = AcceptsFormation(candidate, side, 0.0, config);
                                int pathIndex = chosen.Axis.GetIndex(id);

                                if (next == null || (hasRoom && !nextHasRoom) || (hasRoom == nextHasRoom && pathIndex > nextPathIndex))
                                {
                                    next = candidate;
                                    nextPathIndex = pathIndex;
                                    nextHasRoom = hasRoom;
                                }
                            }

                            if (next == null) break;

                            walking = next;
                            walkDistance--;

                            if (nextHasRoom) best = walking;   // dernière zone avec de la place sur le trajet
                        }

                        if (best == null)
                        {
                            Detail(config, "    axis pull " + side + " : " + f.Name + " waits in " + zone.Id + " (axis '" + chosen.Axis.Name
                                + "', distance " + chosen.Distance[zone.Id] + ", walked to " + walking.Id + ", no room on the way)");
                            continue;   // pas de place sur le trajet : on attend
                        }

                        zone.Formations.Remove(f);
                        best.Formations.Add(f);
                        result.MovedFormationIds.Add(f.FormationId);
                        result.ReinforcedFormationIds.Add(f.FormationId);
                        chosen.Missing -= f.ForcePower;
                        if (best.Id == chosen.Head.Id) chosen.Room--;

                        Detail(config, "  axis pull " + side + " : " + f.Name + " " + zone.Id + " -> " + best.Id
                            + " (toward axis '" + chosen.Axis.Name + "' head " + chosen.Head.Id + ")");
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // Chemin flexible des axes
        // ------------------------------------------------------------------------------------------

        // Centre d'une zone, comme WargameAxisPathFinder (centre de gravité du polygone)
        private static PointF ZoneCenterOf(WargameZoneData zone)
        {
            if (zone.Shape == WargameZoneShape.Polygon && zone.DcsPoints != null && zone.DcsPoints.Count >= 3)
                return WargameAxisPathFinder.PolygonCentroid(zone.DcsPoints);

            return zone.Center;
        }

        private static double CenterDistance(Dictionary<string, PointF> centers, string a, string b)
        {
            double dx = centers[a].X - centers[b].X;
            double dy = centers[a].Y - centers[b].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Plus court chemin pondéré (Dijkstra). null = aucun chemin. Zones "blocked" interdites.
        private static List<string> WeightedSegment(
            Dictionary<string, List<string>> links, Dictionary<string, PointF> centers,
            string from, string to, HashSet<string> blocked, Func<string, double> zoneCost)
        {
            var dist = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { from, 0.0 } };
            var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                string current = null;
                double best = double.MaxValue;

                foreach (KeyValuePair<string, double> kv in dist)
                {
                    if (!done.Contains(kv.Key) && kv.Value < best)
                    {
                        best = kv.Value;
                        current = kv.Key;
                    }
                }

                if (current == null) return null;
                if (string.Equals(current, to, StringComparison.OrdinalIgnoreCase)) break;

                done.Add(current);

                foreach (string next in links[current])
                {
                    if (done.Contains(next) || blocked.Contains(next)) continue;

                    double candidate = best + CenterDistance(centers, current, next) * zoneCost(next);

                    double old;
                    if (!dist.TryGetValue(next, out old) || candidate < old)
                    {
                        dist[next] = candidate;
                        previous[next] = current;
                    }
                }
            }

            var path = new List<string>();
            string step = to;
            while (step != null)
            {
                path.Add(step);
                string back;
                step = previous.TryGetValue(step, out back) ? back : null;
            }

            path.Reverse();
            return path;
        }

        // Chemin flexible : pour chaque axe actif qui a une pointe, on recalcule la SUITE du chemin (de la pointe jusqu'à
        // l'objectif, en passant par les zones de passage restantes) avec un coût par zone :
        //   distance x (1 + résistance) x route
        //   résistance = force ennemie x terrain / force dont dispose l'attaquant pour cet axe (plafonnée à 4) ;
        //   route      = 1 / racine(combat de la route) : la route verte coûte moins cher qu'une piste.
        // Le nouveau chemin n'est pris que s'il coûte au moins 15 % de moins que le chemin prévu (pas de zigzag)
        // et s'il n'a pas plus de AxisFlexDetour zones de plus. Renvoie les chemins PREVUS à remettre après le tour.
        private static Dictionary<WargameAxis, List<string>> AdaptAxisPaths(
            List<WargameZoneData> zones, List<WargameAxis> axes, HashSet<string> activeFlags, WargameCombatConfig config)
        {
            var planned = new Dictionary<WargameAxis, List<string>>();
            if (zones == null || axes == null || axes.Count == 0) return planned;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in zones) zoneById[z.Id] = z;

            Dictionary<string, List<string>> links = BuildLinks(zones);

            var centers = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in zones) centers[z.Id] = ZoneCenterOf(z);

            // Axes actifs par camp attaquant (pour partager la force disponible)
            var attackSideOf = new Dictionary<WargameAxis, string>();
            var axesPerSide = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (WargameAxis axis in axes)
            {
                if (!axis.IsActive(activeFlags)) continue;

                WargameZoneData startZone;
                if (!zoneById.TryGetValue(axis.Path[0], out startZone) || !IsFightingSide(startZone.Control)) continue;

                string side = GetAttackSide(axis, startZone);
                if (IsObjectiveReached(axis, side, zoneById)) continue;

                attackSideOf[axis] = side;
                int n;
                axesPerSide.TryGetValue(side, out n);
                axesPerSide[side] = n + 1;
            }

            foreach (KeyValuePair<WargameAxis, string> kv in attackSideOf)
            {
                WargameAxis axis = kv.Key;
                string side = kv.Value;
                string enemySide = OtherSide(side);

                int headIndex = FindAxisHead(axis, side, zoneById);
                if (headIndex < 0) continue;

                double totalPower = zones
                    .Where(z => !IsReserveZone(z) && string.Equals(z.Control, side, StringComparison.OrdinalIgnoreCase))
                    .Sum(z => SidePower(z, side));
                double available = Math.Max(10.0, totalPower / Math.Max(1, axesPerSide[side]));

                Func<string, double> zoneCost = id =>
                {
                    WargameZoneData z = zoneById[id];
                    double cost = 1.0 / Math.Sqrt(Math.Max(0.25, config.GetRouteCombat(z.SupplyRoute)));

                    if (string.Equals(z.Control, enemySide, StringComparison.OrdinalIgnoreCase))
                    {
                        double resistance = SidePower(z, enemySide) * config.GetTerrainDefense(z.Terrain) / available;
                        cost *= 1.0 + Math.Min(4.0, resistance);
                    }

                    return cost;
                };

                // Suite prévue : de la pointe à l'objectif
                List<string> oldPath = axis.Path;
                List<string> oldTail = oldPath.Skip(headIndex).ToList();

                // Zones déjà parcourues : interdites au nouveau chemin
                var blocked = new HashSet<string>(oldPath.Take(headIndex), StringComparer.OrdinalIgnoreCase);

                // Zones de passage restantes (obligatoires), dans l'ordre prévu, puis l'objectif
                var stops = new List<string>();
                foreach (string via in axis.ViaZoneIds)
                {
                    if (axis.GetIndex(via) > headIndex) stops.Add(via);
                }
                stops.Add(oldPath[oldPath.Count - 1]);

                var newTail = new List<string> { oldPath[headIndex] };
                bool failed = false;

                foreach (string stop in stops)
                {
                    string from = newTail[newTail.Count - 1];
                    if (string.Equals(from, stop, StringComparison.OrdinalIgnoreCase)) continue;

                    var forbidden = new HashSet<string>(blocked, StringComparer.OrdinalIgnoreCase);
                    foreach (string used in newTail) forbidden.Add(used);
                    forbidden.Remove(from);

                    List<string> segment = WeightedSegment(links, centers, from, stop, forbidden, zoneCost);
                    if (segment == null || segment.Count < 2) { failed = true; break; }

                    for (int i = 1; i < segment.Count; i++) newTail.Add(segment[i]);
                }

                if (failed) continue;
                if (newTail.SequenceEqual(oldTail, StringComparer.OrdinalIgnoreCase)) continue;

                double oldCost = TailCost(oldTail, centers, zoneCost);
                double newCost = TailCost(newTail, centers, zoneCost);

                bool shorterEnough = newTail.Count <= oldTail.Count + config.AxisFlexDetour;
                bool cheaperEnough = newCost < oldCost * 0.85;

                Detail(config, "  flexible path '" + axis.Name + "' : planned " + string.Join(">", oldTail) + " (cost " + oldCost.ToString("0") + ")"
                    + " | candidate " + string.Join(">", newTail) + " (cost " + newCost.ToString("0") + ")"
                    + (shorterEnough && cheaperEnough ? " -> adopted" : " -> kept the plan"));

                if (!shorterEnough || !cheaperEnough) continue;

                var adapted = oldPath.Take(headIndex).ToList();
                adapted.AddRange(newTail);

                planned[axis] = oldPath;
                axis.Path = adapted;

                Trace(config, "  axis '" + axis.Name + "' goes around the resistance : " + string.Join(" > ", newTail));
            }

            return planned;
        }

        private static double TailCost(List<string> tail, Dictionary<string, PointF> centers, Func<string, double> zoneCost)
        {
            double total = 0;

            for (int i = 1; i < tail.Count; i++)
                total += CenterDistance(centers, tail[i - 1], tail[i]) * zoneCost(tail[i]);

            return total;
        }

        // Une zone accepte une formation de plus tant qu'elle n'a pas MaxFormationsPerZone formations : plus de
        // dépassement selon la menace, une formation qui n'a pas de place attend dans la zone d'avant (file d'attente).
        // "needed" n'est plus utilisé, gardé pour ne pas changer les appels.
        private static bool AcceptsFormation(WargameZoneData zone, string side, double needed, WargameCombatConfig config)
        {
            return LiveFormations(zone, side).Count < config.MaxFormationsPerZone;
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

                        // Une zone qui a épuisé son stock (coupée depuis trop longtemps) ne projette plus d'influence :
                        // sans ravitaillement, une armée ne peut pas non plus occuper de nouvelles zones vides
                        double nBlue = ExhaustedForOffensive(n, WargameSide.Blue, config) ? 0 : SidePower(n, WargameSide.Blue);
                        double nRed = ExhaustedForOffensive(n, WargameSide.Red, config) ? 0 : SidePower(n, WargameSide.Red);

                        blue += nBlue * weight;
                        red += nRed * weight;

                        if (depth == 1)
                        {
                            blueDirect += nBlue;
                            redDirect += nRed;
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

                // La zone occupée hérite du retard de ravitaillement des voisines du nouveau camp (délai de grâce)
                bool found = false;
                int inherited = 0;
                foreach (string id in links[toFlip[i].Id])
                {
                    WargameZoneData n = zoneById[id];
                    if (n.CutOffTurns == 0 || !string.Equals(n.CutOffSide, toFlip[i].Control, StringComparison.OrdinalIgnoreCase)) continue;

                    inherited = found ? Math.Max(inherited, n.CutOffTurns) : n.CutOffTurns;   // la voisine la plus en retard
                    found = true;
                }
                toFlip[i].CutOffTurns = found ? inherited : 0;
                toFlip[i].CutOffSide = found ? toFlip[i].Control : "";

                result.FlippedZones.Add(toFlip[i]);
                Trace(config, messages[i]);
            }
        }

        // Zone coupée depuis plus longtemps que le délai de grâce : les formations de ce camp n'ont plus de stock
        // (elles ne peuvent plus attaquer ni occuper de zone vide). Vrai seulement si cutoff_can_attack est désactivé.
        private static bool ExhaustedForOffensive(WargameZoneData zone, string side, WargameCombatConfig config)
        {
            return !config.CutOffCanAttack
                && zone.CutOffTurns > EffectiveGrace(config)
                && string.Equals(zone.CutOffSide, side, StringComparison.OrdinalIgnoreCase);
        }

        // Délai de grâce (stock emporté) : remplacé par les points avancés quand les convois sont activés
        private static double EffectiveGrace(WargameCombatConfig config)
        {
            return config.SupplyConvoys ? 0 : Math.Max(0, config.CutOffGraceTurns);
        }

        // Zones dont les formations sont posées en unités dans DCS : les zones de première ligne
        // (voisines d'une zone ennemie) et celles à moins de "depth" pas derrière elles.
        // Les zones qui ne sont ni bleues ni rouges sont toujours incluses. Si aucun front
        // n'existe, tout est inclus (rien à économiser), SAUF les boîtes hors carte (RESERVE, LANDING) :
        // leurs formations ne sont JAMAIS posées en unités dans DCS, quoi qu'il arrive.
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
                if (IsOffMapId(zone.Id))
                    continue;   // réserve ou plan de débarquement : jamais jouée dans DCS

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
                    if (!IsOffMapId(zone.Id))
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

        // ---- Besoin en ravitaillement (tonnes par tour) ----
        // Une ligne par formation vivante d'une zone de la carte (les boîtes RESERVE / LANDING et les zones sans camp sont ignorées :
        // une formation en transit ne consomme rien).
        public class WargameSupplyDemand
        {
            public string ZoneId;
            public string Side;
            public WargameFormation Formation;
            public string State;      // "rear", "front", "attack", "defense"
            public double Strength;   // force restante / force nominale, de 0 à 1 (une formation affaiblie consomme moins)
            public double Base;       // xN x consommation du template (par jour) x part du jour d'un tour x force restante, en tonnes par tour (état "front")
            public double Coef;       // coefficient de l'état (config)
            public double Tons;       // Base x Coef
        }

        // Besoin d'un tour = xN x tonnes par jour du template x part du jour d'un tour x force restante (0 à 1) x coefficient de l'état.
        // Calcule le besoin du tour. A appeler APRES Resolve (result sait qui a attaqué / défendu) ; la position est celle de fin de tour.
        //   engagée en attaque -> SupplyUseAttack      engagée en défense -> SupplyUseDefend
        //   sinon au contact de l'ennemi (zone voisine d'une zone ennemie) -> SupplyUseFront
        //   sinon (arrière) -> SupplyUseRear
        // Ne modifie rien.
        public static List<WargameSupplyDemand> ComputeSupplyDemand(
            List<WargameZoneData> zones, WargameTemplateCatalog catalog, WargameCombatConfig config, WargameCombatResult result)
        {
            var list = new List<WargameSupplyDemand>();
            if (zones == null || catalog == null || config == null) return list;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in zones) zoneById[z.Id] = z;

            Dictionary<string, List<string>> links = BuildLinks(zones);

            foreach (WargameZoneData zone in zones)
            {
                if (IsOffMapId(zone.Id) || !IsFightingSide(zone.Control)) continue;

                bool front = links[zone.Id].Any(id => IsFightingSide(zoneById[id].Control)
                    && !string.Equals(zoneById[id].Control, zone.Control, StringComparison.OrdinalIgnoreCase));

                foreach (WargameFormation f in zone.Formations)
                {
                    if (f.ForcePower <= 0) continue;
                    if (!string.Equals(f.Side, zone.Control, StringComparison.OrdinalIgnoreCase)) continue;

                    string state;
                    double coef;

                    if (result != null && result.AttackingFormationIds.Contains(f.FormationId)) { state = "attack"; coef = config.SupplyUseAttack; }
                    else if (result != null && result.DefendingFormationIds.Contains(f.FormationId)) { state = "defense"; coef = config.SupplyUseDefend; }
                    else if (front) { state = "front"; coef = config.SupplyUseFront; }
                    else { state = "rear"; coef = config.SupplyUseRear; }

                    // Formation affaiblie = moins de véhicules = moins de consommation (plafonné à 100 %)
                    double strength;
                    double baseTons = FormationBaseTons(f, catalog, config, out strength);

                    list.Add(new WargameSupplyDemand
                    {
                        ZoneId = zone.Id,
                        Side = f.Side,
                        Formation = f,
                        State = state,
                        Strength = strength,
                        Base = baseTons,
                        Coef = coef,
                        Tons = baseTons * coef,
                    });
                }
            }

            return list;
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

        // ---- Renforts : réserve + ordres de déploiement ----
        //
        // Une zone dont le nom commence par RESERVE (ex. RESERVE_BLUE, RESERVE_RED) est une REMISE : le
        // campaignMaker y range des formations prêtes au combat, en attente. Elle n'a aucun voisin (voir
        // BuildLinks), n'est jamais jouée dans DCS, ne prend part à aucun combat.
        //
        // Un trigger de campagne lève le flag   wg_deploy__FORMATION__ZONE   (nom de la formation et id de la
        // zone, avec _ à la place de tout ce qui n'est pas lettre ou chiffre : Secteur-30 -> Secteur_30).
        // Au tour suivant la formation quitte la réserve et arrive à pleine force dans la zone, à condition
        // que la zone soit AMIE et pas PLEINE (au contact de l'ennemi, c'est permis). Sinon elle attend et
        // le flag reste levé. Un ordre traité (arrivée, formation ou zone inconnue) est baissé automatiquement.
        internal const string ReserveZonePrefix = "RESERVE";
        internal const string LandingZonePrefix = "LANDING";   // plan de débarquement : boîte hors carte, comme une réserve
        internal const string DeployFlagPrefix = "wg_deploy__";

        // Vrai pour une réserve ET pour un plan de débarquement : toutes les "boîtes" posées hors du front
        internal static bool IsReserveZone(WargameZoneData zone)
        {
            return zone != null && IsOffMapId(zone.Id);
        }

        internal static bool IsOffMapId(string id)
        {
            return id != null
                && (id.StartsWith(ReserveZonePrefix, StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith(LandingZonePrefix, StringComparison.OrdinalIgnoreCase));
        }

        // Vrai seulement pour un plan de débarquement (LANDING_n)
        internal static bool IsLandingZone(WargameZoneData zone)
        {
            return zone != null && zone.Id != null && zone.Id.StartsWith(LandingZonePrefix, StringComparison.OrdinalIgnoreCase);
        }

        // Même nettoyage que ZoneFlagName : lettres, chiffres et _ seulement
        internal static string FlagKey(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in text ?? "")
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        // Nom du flag d'ordre pour une formation et une zone (sert aussi à la preview)
        internal static string DeployFlagName(string formationName, string zoneId)
        {
            return DeployFlagPrefix + FlagKey(formationName) + "__" + FlagKey(zoneId);
        }

        // Traite les flags wg_deploy__... levés. Les flags traités sont retirés de activeFlags (copie en
        // mémoire) et listés dans result.ConsumedFlags pour que la campagne les baisse (voir BuildFlagLines).
        internal static void DeployReinforcements(List<WargameZoneData> zones, HashSet<string> activeFlags,
            WargameCombatConfig config, WargameCombatResult result)
        {
            if (zones == null || activeFlags == null || activeFlags.Count == 0)
                return;

            List<string> orders = activeFlags
                .Where(f => f != null && f.StartsWith(DeployFlagPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string flag in orders)
            {
                string rest = flag.Substring(DeployFlagPrefix.Length);
                int cut = rest.IndexOf("__", StringComparison.Ordinal);

                if (cut <= 0 || cut + 2 >= rest.Length)
                {
                    FinishOrder(config, result, activeFlags, flag, true, "Reinforcement order '" + flag + "' ignored: expected wg_deploy__FORMATION__ZONE.");
                    continue;
                }

                string formationKey = rest.Substring(0, cut);
                string zoneKey = rest.Substring(cut + 2);

                // La formation doit être rangée dans une réserve
                WargameZoneData reserve = null;
                WargameFormation formation = null;

                foreach (WargameZoneData z in zones)
                {
                    if (!IsReserveZone(z)) continue;

                    formation = z.Formations.FirstOrDefault(f => string.Equals(FlagKey(f.Name), formationKey, StringComparison.OrdinalIgnoreCase));
                    if (formation != null) { reserve = z; break; }
                }

                if (formation == null)
                {
                    FinishOrder(config, result, activeFlags, flag, true,
                        "Reinforcement '" + formationKey + "' not found in a reserve (already deployed, or misspelled): order dropped.");
                    continue;
                }

                WargameZoneData target = zones.FirstOrDefault(z => !IsReserveZone(z)
                    && string.Equals(FlagKey(z.Id), zoneKey, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    FinishOrder(config, result, activeFlags, flag, true,
                        "Reinforcement " + formation.Name + ": zone '" + zoneKey + "' does not exist, order dropped.");
                    continue;
                }

                if (!string.Equals(target.Control, formation.Side, StringComparison.OrdinalIgnoreCase))
                {
                    FinishOrder(config, result, activeFlags, flag, false,
                        "Reinforcement " + formation.Name + " waits in reserve: zone " + target.Id + " is not held by " + formation.Side + ".");
                    continue;
                }

                int live = target.Formations.Count(f => f.ForcePower > 0
                    && string.Equals(f.Side, formation.Side, StringComparison.OrdinalIgnoreCase));
                int hardCap = config.MaxFormationsPerZone;

                if (live >= hardCap)
                {
                    FinishOrder(config, result, activeFlags, flag, false,
                        "Reinforcement " + formation.Name + " waits in reserve: zone " + target.Id + " is full (" + live + "/" + hardCap + ").");
                    continue;
                }

                reserve.Formations.Remove(formation);
                target.Formations.Add(formation);
                result.DeployedFormationIds.Add(formation.FormationId);

                FinishOrder(config, result, activeFlags, flag, true,
                    "Reinforcement " + formation.Name + " arrived in " + target.Id + " (from " + reserve.Id + ", force "
                    + formation.ForcePower.ToString("0.0") + ").");
            }
        }

        // consume = true : l'ordre est terminé, son flag sera baissé. false : il reste levé, on réessaie au tour suivant.
        private static void FinishOrder(WargameCombatConfig config, WargameCombatResult result, HashSet<string> activeFlags,
            string flag, bool consume, string note)
        {
            result.DeployNotes.Add(note);
            Detail(config, "  " + note);
            Log(config, "WargameEngineCombat | " + note);

            if (!consume) return;

            result.ConsumedFlags.Add(flag);
            activeFlags.Remove(flag);
        }

        // ---- Débarquements : plans LANDING_n ----
        //
        // Un plan = une boîte LANDING_n (formations rangées par vague, colonne Wave) + une plage visée.
        // UN trigger lève   wg_land__PLAN[__PLAGE[__ECART]]   (Action.WargameLand). PLAGE : par défaut celle du plan
        // ("default" pour la garder quand on donne un écart). ECART : tours entre deux vagues (défaut : LandingWaveGap).
        // Ensuite le moteur déroule les vagues dans l'ordre de leur numéro, sans autre trigger :
        //   - la vague de numéro le plus bas tente la plage. Plage amie ou vide : elle arrive à pleine force.
        //   - plage ennemie : jugée comme un combat (attaque = force de la vague, défense = défenseurs x terrain).
        //       delta > seuil de conquête : la plage bascule, les défenseurs se replient, la vague arrive avec une force
        //                                   proportionnelle au rapport de force (part = attaque / (attaque + défense)) ;
        //       delta entre -seuil et +seuil : échec, la vague perd LandingFailLoss, les défenseurs s'usent, elle retente
        //                                   au tour suivant (les missions des joueurs peuvent entre-temps affaiblir la défense) ;
        //       delta (sans hasard) sous -seuil : plus aucune chance, la vague est ABANDONNEE (Wave = 0, elle reste dans la
        //                                   boîte) et la suivante prend le relais.
        //   - une vague réglée (arrivée ou abandonnée) fait attendre la suivante ECART tours (0 = tout de suite).
        // Le flag reste levé tant qu'il reste une vague à faire. Le compteur d'attente est dans le plan (LandingWait,
        // enregistré dans l'Active) : -1 = pas lancé.
        internal const string LandFlagPrefix = "wg_land__";

        // Nom du flag d'un ordre de débarquement (sert aussi à la preview). zoneId vide = plage du plan ; gap < 0 = écart par défaut.
        internal static string LandFlagName(string planId, string zoneId, int gap)
        {
            string name = LandFlagPrefix + FlagKey(planId);

            if (!string.IsNullOrEmpty(zoneId)) name += "__" + FlagKey(zoneId);
            else if (gap >= 0) name += "__default";

            if (gap >= 0) name += "__" + gap;

            return name;
        }

        private enum LandingOutcome { Landed, Retry, Abandoned }

        internal static void ProcessLandings(List<WargameZoneData> zones, List<WargameAxis> axes, HashSet<string> activeFlags,
            WargameCombatConfig config, Random rng, WargameCombatResult result)
        {
            if (zones == null || activeFlags == null || activeFlags.Count == 0)
                return;

            List<string> orders = activeFlags
                .Where(f => f != null && f.StartsWith(LandFlagPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (orders.Count == 0)
                return;

            Dictionary<string, List<string>> links = BuildLinks(zones);

            foreach (string flag in orders)
            {
                string[] parts = flag.Substring(LandFlagPrefix.Length).Split(new[] { "__" }, StringSplitOptions.None);
                string planKey = parts[0];
                string zoneKey = parts.Length > 1 ? parts[1] : "";

                int gap = (int)Math.Round(Math.Max(0, config.LandingWaveGap));
                if (parts.Length > 2)
                {
                    int parsedGap;
                    if (int.TryParse(parts[2], out parsedGap) && parsedGap >= 0)
                        gap = parsedGap;
                }

                WargameZoneData plan = zones.FirstOrDefault(z => IsLandingZone(z)
                    && string.Equals(FlagKey(z.Id), planKey, StringComparison.OrdinalIgnoreCase));

                if (plan == null)
                {
                    FinishOrder(config, result, activeFlags, flag, true, "Landing order '" + flag + "' ignored: no landing plan named '" + planKey + "'.");
                    continue;
                }

                string attackSide = plan.Control;
                if (!IsFightingSide(attackSide))
                {
                    FinishOrder(config, result, activeFlags, flag, true, "Landing " + plan.Id + ": the plan has no side (blue or red), order dropped.");
                    continue;
                }

                string targetKey = (zoneKey.Length == 0 || string.Equals(zoneKey, "default", StringComparison.OrdinalIgnoreCase))
                    ? FlagKey(plan.LandingTarget)
                    : zoneKey;

                WargameZoneData target = targetKey.Length == 0 ? null : zones.FirstOrDefault(z => !IsReserveZone(z)
                    && string.Equals(FlagKey(z.Id), targetKey, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    FinishOrder(config, result, activeFlags, flag, true,
                        "Landing " + plan.Id + ": no target beach (set one in the editor, or name an existing zone in the trigger), order dropped.");
                    continue;
                }

                // Lancement, ou attente entre deux vagues
                if (plan.LandingWait < 0)
                {
                    plan.LandingWait = 0;
                    LandNote(config, result, "Landing plan " + plan.Id + " launched toward " + target.Id + " (" + gap + " turn(s) between waves).");
                }
                else if (plan.LandingWait > 0)
                {
                    plan.LandingWait--;
                }

                if (plan.LandingWait > 0)
                {
                    LandNote(config, result, "Landing " + plan.Id + ": next wave in " + plan.LandingWait + " turn(s).");
                    continue;
                }

                // Les vagues, dans l'ordre. Elles s'enchaînent au même tour seulement si l'écart est 0.
                while (true)
                {
                    List<WargameFormation> waiting = plan.Formations.Where(f => f.ForcePower > 0 && f.Wave > 0).ToList();

                    if (waiting.Count == 0)
                    {
                        plan.LandingWait = -1;
                        FinishOrder(config, result, activeFlags, flag, true, "Landing " + plan.Id + ": every wave is done.");
                        break;
                    }

                    int wave = waiting.Min(f => f.Wave);
                    List<WargameFormation> group = waiting.Where(f => f.Wave == wave).ToList();

                    LandingOutcome outcome = AttemptLanding(zones, plan, target, group, wave, attackSide, links, config, rng, result,
                        NavalSupportOf(plan, activeFlags));

                    // Des troupes ont posé le pied sur la plage : si elle est le départ d'un axe, il devient le leur
                    if (result.LandedZoneIds.Contains(target.Id))
                        AdoptAxes(axes, target, attackSide, config);

                    if (outcome == LandingOutcome.Retry)
                        break;   // la vague retentera au prochain tour ; les suivantes attendent

                    bool anyLeft = plan.Formations.Any(f => f.ForcePower > 0 && f.Wave > 0);

                    if (!anyLeft)
                    {
                        plan.LandingWait = -1;
                        FinishOrder(config, result, activeFlags, flag, true, "Landing " + plan.Id + ": every wave is done.");
                        break;
                    }

                    if (gap > 0)
                    {
                        plan.LandingWait = gap;
                        break;
                    }
                }
            }
        }

        // Plan de débarquement du camp "side" dont "beach" est la plage, et dont la flotte est là (flag de l'appui naval levé,
        // ou pas de flag) : c'est là que les troupes peuvent rembarquer. null = pas de flotte, pas de rembarquement.
        private static WargameZoneData FleetFor(List<WargameZoneData> zones, WargameZoneData beach, string side, HashSet<string> activeFlags)
        {
            foreach (WargameZoneData plan in zones)
            {
                if (!IsLandingZone(plan)) continue;
                if (!string.Equals(plan.Control, side, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(plan.LandingBeach, beach.Id, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(plan.LandingTarget, beach.Id, StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrEmpty(plan.NavalFlag)
                    && (activeFlags == null || !activeFlags.Any(x => string.Equals(x, plan.NavalFlag, StringComparison.OrdinalIgnoreCase))))
                    continue;

                return plan;
            }

            return null;
        }

        // Formations d'un plan restées dans la boîte (vague abandonnée après trop d'échecs) : quand il n'y a plus aucune vague
        // en attente et que la plage du plan est tenue par leur camp, elles débarquent à pleine force (plage amie).
        // Sans ordre de débarquement : c'est la fin naturelle du plan. Plage pleine : elles attendent le tour suivant.
        private static void LandLeftovers(List<WargameZoneData> zones, List<WargameAxis> axes, WargameCombatConfig config,
            Random rng, WargameCombatResult result)
        {
            if (zones == null) return;

            Dictionary<string, List<string>> links = null;

            foreach (WargameZoneData plan in zones.Where(z => IsLandingZone(z)).ToList())
            {
                if (!IsFightingSide(plan.Control)) continue;
                if (plan.Formations.Any(x => x.ForcePower > 0 && x.Wave > 0)) continue;   // des vagues restent à jouer : le plan suit son cours

                List<WargameFormation> left = plan.Formations.Where(x => x.ForcePower > 0 && x.Wave <= 0).ToList();
                if (left.Count == 0) continue;

                // Plage : la dernière où le plan a débarqué, sinon celle visée par défaut ; elle doit être à leur camp
                WargameZoneData beach = null;
                foreach (string id in new[] { plan.LandingBeach, plan.LandingTarget })
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    WargameZoneData z = zones.FirstOrDefault(x => !IsOffMapId(x.Id) && string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (z != null && string.Equals(z.Control, plan.Control, StringComparison.OrdinalIgnoreCase)) { beach = z; break; }
                }

                if (beach == null) continue;

                if (links == null) links = BuildLinks(zones);

                LandNote(config, result, "Landing " + plan.Id + ": " + left.Count + " formation(s) still at sea land on " + beach.Id + " (held by their side).");
                AttemptLanding(zones, plan, beach, left, 0, plan.Control, links, config, rng, result, 0);

                if (result.LandedZoneIds.Contains(beach.Id))
                    AdoptAxes(axes, beach, plan.Control, config);
            }
        }

        // La plage est le départ d'un axe : le camp qui débarque en devient l'attaquant (sinon ses troupes n'auraient
        // aucun axe à suivre, l'axe restant rattaché au camp qui tenait la plage au chargement).
        private static void AdoptAxes(List<WargameAxis> axes, WargameZoneData target, string attackSide, WargameCombatConfig config)
        {
            if (axes == null) return;

            foreach (WargameAxis axis in axes)
            {
                if (!axis.IsValid) continue;
                if (!string.Equals(axis.Path[0], target.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(axis.Side)) continue;   // camp choisi dans l'éditeur : on n'y touche pas
                if (string.Equals(axis.AttackerSide, attackSide, StringComparison.OrdinalIgnoreCase)) continue;

                axis.AttackerSide = attackSide;
                Trace(config, "  landing : axis '" + axis.Name + "' now attacked by " + attackSide + " from " + target.Id);
            }
        }

        // Une tentative de débarquement de la vague "wave" du plan sur la plage "target"
        // Appui feu de la flotte sur les têtes de plage : chaque plan dont l'appui est actif (flag) couvre la dernière plage où ses
        // troupes ont débarqué, tant que son camp la tient. La valeur s'ajoute à la défense de la plage dans les combats.
        internal static void ApplyNavalCover(List<WargameZoneData> zones, HashSet<string> activeFlags, WargameCombatConfig config)
        {
            config.NavalCoverByZone = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            config.NavalCoverSideByZone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (zones == null) return;

            foreach (WargameZoneData plan in zones)
            {
                if (!IsLandingZone(plan) || string.IsNullOrEmpty(plan.LandingBeach)) continue;

                double naval = NavalSupportOf(plan, activeFlags);
                if (naval <= 0) continue;

                WargameZoneData beach = zones.FirstOrDefault(z => string.Equals(z.Id, plan.LandingBeach, StringComparison.OrdinalIgnoreCase));
                if (beach == null || !string.Equals(beach.Control, plan.Control, StringComparison.OrdinalIgnoreCase)) continue;

                double current;
                config.NavalCoverByZone.TryGetValue(beach.Id, out current);
                config.NavalCoverByZone[beach.Id] = current + naval;
                config.NavalCoverSideByZone[beach.Id] = plan.Control;
            }
        }

        private static double NavalCoverOf(WargameZoneData zone, string side, WargameCombatConfig config)
        {
            double value;
            string coveredSide;
            if (!config.NavalCoverByZone.TryGetValue(zone.Id, out value)) return 0;
            if (!config.NavalCoverSideByZone.TryGetValue(zone.Id, out coveredSide) || !string.Equals(coveredSide, side, StringComparison.OrdinalIgnoreCase)) return 0;
            return value;
        }

        // Appui feu naval d'un plan de débarquement : sa valeur, si son flag (facultatif) est levé ; 0 sinon
        private static double NavalSupportOf(WargameZoneData plan, HashSet<string> activeFlags)
        {
            if (plan.NavalSupport <= 0) return 0;

            if (!string.IsNullOrEmpty(plan.NavalFlag)
                && (activeFlags == null || !activeFlags.Any(x => string.Equals(x, plan.NavalFlag, StringComparison.OrdinalIgnoreCase))))
                return 0;

            return plan.NavalSupport;
        }

        private static LandingOutcome AttemptLanding(List<WargameZoneData> zones, WargameZoneData plan, WargameZoneData target,
            List<WargameFormation> group, int wave, string attackSide, Dictionary<string, List<string>> links,
            WargameCombatConfig config, Random rng, WargameCombatResult result, double naval)
        {
            string defendSide = OtherSide(attackSide);

            // Ceux qui peuvent débarquer : les plus forts, dans la limite de la place sur la plage.
            // Assaut sur une plage ennemie : nombre normal de formations par zone. Plage déjà à nous : on peut l'encombrer
            // jusqu'au maximum absolu (MaxFormationsHard), sinon une plage tenue par des formations qui gardent le front
            // bloquerait les vagues suivantes pour toujours ; le surplus partira ensuite vers l'intérieur.
            bool friendlyBeach = string.Equals(target.Control, attackSide, StringComparison.OrdinalIgnoreCase);
            int cap = friendlyBeach ? Math.Max(config.MaxFormationsPerZone, config.MaxFormationsHard) : config.MaxFormationsPerZone;
            int room = cap - LiveFormations(target, attackSide).Count;
            List<WargameFormation> landers = group.OrderByDescending(f => f.ForcePower).Take(Math.Max(0, room)).ToList();

            if (landers.Count == 0)
            {
                LandNote(config, result, "Landing " + plan.Id + " wave " + wave + " waits: beach " + target.Id + " is full.");
                return LandingOutcome.Retry;
            }

            double share = 1.0;
            bool hostile = !string.Equals(target.Control, attackSide, StringComparison.OrdinalIgnoreCase);

            if (hostile)
            {
                // Appui feu de la flotte (canons, missiles, hélicoptères embarqués) : s'ajoute à la force des troupes qui débarquent
                double troopPower = landers.Sum(f => f.ForcePower) * AirCoef(attackSide, config);
                double basePower = troopPower + naval;
                double defense = SidePower(target, defendSide) * config.GetTerrainDefense(target.Terrain) * AirCoef(defendSide, config);

                // Sans hasard : sert à savoir s'il reste une chance
                double plainTotal = basePower + defense;
                double plainDelta = plainTotal <= 0 ? -1.0 : (basePower - defense) / plainTotal;

                if (plainDelta < -config.ControlFlipThreshold)
                {
                    foreach (WargameFormation f in group)
                        f.Wave = 0;

                    plan.LandingRetries = 0;
                    LandNote(config, result, "Landing " + plan.Id + " wave " + wave + " ABANDONED at " + target.Id
                        + ": attack " + basePower.ToString("0.0") + " vs defense " + defense.ToString("0.0") + " (no chance left). Next wave takes over.");
                    return LandingOutcome.Abandoned;
                }

                double attack = troopPower * Luck(config, rng) + naval;   // le hasard ne joue que sur les troupes
                double total = attack + defense;
                double delta = total <= 0 ? -1.0 : (attack - defense) / total;

                if (delta <= config.ControlFlipThreshold)
                {
                    // Echec : la vague s'use, les défenseurs aussi. Elle retentera au prochain tour.
                    foreach (WargameFormation f in group)
                        f.ForcePower = Math.Max(0, f.ForcePower * (1.0 - config.LandingFailLoss));

                    // Trop d'échecs : la vague renonce (elle reste dans la boîte), la suivante prendra le relais
                    plan.LandingRetries++;
                    if (plan.LandingRetries > config.LandingMaxRetries)
                    {
                        foreach (WargameFormation f in group)
                            f.Wave = 0;
                        plan.LandingRetries = 0;

                        LandNote(config, result, "Landing " + plan.Id + " wave " + wave + " ABANDONED at " + target.Id + " after "
                            + (config.LandingMaxRetries + 1).ToString("0") + " failed attempts (attack " + attack.ToString("0.0")
                            + " vs defense " + defense.ToString("0.0") + "). Next wave takes over.");
                        return LandingOutcome.Abandoned;
                    }

                    if (config.CombatAttrition > 0)
                    {
                        foreach (WargameFormation d in FightingFormations(target, defendSide))
                            d.ForcePower = Math.Max(0, d.ForcePower * (1.0 - config.CombatAttrition));
                    }

                    LandNote(config, result, "Landing " + plan.Id + " wave " + wave + " repulsed at " + target.Id
                        + ": attack " + attack.ToString("0.0") + (naval > 0 ? " (naval fire support " + naval.ToString("0.0") + " included)" : "") + " vs defense " + defense.ToString("0.0") + " (delta " + delta.ToString("0.00")
                        + ", needs more than " + config.ControlFlipThreshold.ToString("0.00") + "). It loses " + (config.LandingFailLoss * 100).ToString("0")
                        + "% and tries again next turn.");
                    return LandingOutcome.Retry;
                }

                // Succès : la force qui prend pied est proportionnelle au rapport de force vécu
                share = total <= 0 ? 1.0 : attack / total;

                // Les défenseurs se replient vers une zone amie voisine (la plus éloignée de la boîte), sinon ils sont perdus
                var options = new List<WargameZoneData>();
                List<string> neighborIds;
                if (links.TryGetValue(target.Id, out neighborIds))
                {
                    options = zones.Where(z => !IsReserveZone(z)
                            && neighborIds.Contains(z.Id, StringComparer.OrdinalIgnoreCase)
                            && string.Equals(z.Control, defendSide, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(z => (z.Center.X - plan.Center.X) * (z.Center.X - plan.Center.X)
                            + (z.Center.Y - plan.Center.Y) * (z.Center.Y - plan.Center.Y))
                        .ToList();
                }

                int fellBack = 0, lost = 0;

                foreach (WargameFormation d in LiveFormations(target, defendSide).OrderByDescending(x => x.ForcePower))
                {
                    WargameZoneData place = options.FirstOrDefault(o => LiveFormations(o, defendSide).Count < config.MaxFormationsPerZone);

                    if (place != null)
                    {
                        target.Formations.Remove(d);
                        place.Formations.Add(d);
                        d.ForcePower = Math.Max(0, d.ForcePower * (1.0 - config.RetreatLossFraction));
                        result.MovedFormationIds.Add(d.FormationId);
                        fellBack++;
                    }
                    else
                    {
                        d.ForcePower = 0;   // WargameEngineLosses la retirera de la targetlist
                        result.Destroyed++;
                        lost++;
                    }
                }

                target.Control = attackSide;
                if (!result.FlippedZones.Contains(target))
                    result.FlippedZones.Add(target);

                LandNote(config, result, "Landing " + plan.Id + " wave " + wave + " TOOK " + target.Id + ": attack " + attack.ToString("0.0")
                    + (naval > 0 ? " (naval fire support " + naval.ToString("0.0") + " included)" : "")
                    + " vs defense " + defense.ToString("0.0") + " (delta " + delta.ToString("0.00") + "). Arrives at " + (share * 100).ToString("0")
                    + "% of its strength; " + fellBack + " defender(s) fell back, " + lost + " lost.");
            }

            // Arrivée : à pleine force sur une plage amie, au prorata du rapport de force sur une plage prise
            foreach (WargameFormation f in landers)
            {
                plan.Formations.Remove(f);
                f.ForcePower = f.ForcePower * share;
                target.Formations.Add(f);
                result.DeployedFormationIds.Add(f.FormationId);
            }

            if (landers.Count > 0)
            {
                result.LandedZoneIds.Add(target.Id);
                plan.LandingRetries = 0;
                plan.LandingBeach = target.Id;   // tête de plage couverte ensuite par la flotte (ApplyNavalCover)

                // La vague arrive avec son propre stock : le délai de grâce repart. Avec les convois (délai 0), la vague
                // garde quand même ses cutoff_grace_turns tours de stock, notés en négatif (-3 = encore 3 tours avant de compter)
                target.CutOffTurns = config.SupplyConvoys ? -(int)Math.Round(Math.Max(0, config.CutOffGraceTurns)) : 0;
                target.CutOffSide = attackSide;
            }

            if (!hostile)
                LandNote(config, result, "Landing " + plan.Id + " wave " + wave + ": " + landers.Count + " formation(s) arrived at " + target.Id + " at full strength.");

            // Une vague plus large que la place disponible débarque en plusieurs fois
            return landers.Count < group.Count ? LandingOutcome.Retry : LandingOutcome.Landed;
        }

        private static void LandNote(WargameCombatConfig config, WargameCombatResult result, string note)
        {
            result.DeployNotes.Add(note);
            Detail(config, "  " + note);
            Log(config, "WargameEngineCombat | " + note);
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
                if (IsReserveZone(z)) continue;   // une réserve n'est pas un territoire

                bool blue = string.Equals(z.Control, WargameSide.Blue, StringComparison.OrdinalIgnoreCase);
                bool red = string.Equals(z.Control, WargameSide.Red, StringComparison.OrdinalIgnoreCase);

                lines.Add((blue ? "" : "-") + ZoneFlagName(z.Id, WargameSide.Blue));
                lines.Add((red ? "" : "-") + ZoneFlagName(z.Id, WargameSide.Red));
            }

            lines.AddRange(combat.ReachedFlags);

            // Ordres de renfort traités : on baisse leur flag pour qu'ils ne se rejouent pas
            foreach (string consumed in combat.ConsumedFlags)
                lines.Add("-" + consumed);

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
                if (IsReserveZone(z)) continue;
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

            foreach (string note in combat.DeployNotes)
                lines.Add(note);

            foreach (string flag in combat.ReachedFlags)
                lines.Add("Objective reached (" + flag + ")");

            int blueZones = zones.Count(z => !IsReserveZone(z) && string.Equals(z.Control, WargameSide.Blue, StringComparison.OrdinalIgnoreCase));
            int redZones = zones.Count(z => !IsReserveZone(z) && string.Equals(z.Control, WargameSide.Red, StringComparison.OrdinalIgnoreCase));
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
            if (zones == null)
                return;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData zone in zones)
                zoneById[zone.Id] = zone;

            // Points avancés dont la zone a été prise pendant le combat : perdus (stock pris ou détruit), avant la récupération
            DropLostForwardPoints(zoneById, config);

            // Les secteurs des points avancés consomment leur besoin du tour dans le stock (tonnes)
            ConsumeForwardStock(zones, zoneById, catalog, config, combat);

            if (config.SupplyRecoveryRate <= 0)
                return;

            Dictionary<string, List<string>> links = BuildLinks(zones);
            HashSet<string> cutOff = FindCutOffZones(zones, links, zoneById, config);

            int reinforced = 0, blocked = 0, starving = 0;
            double totalGain = 0;

            foreach (WargameZoneData zone in zones)
            {
                if (!IsFightingSide(zone.Control))
                    continue;

                bool isCut = cutOff.Contains(zone.Id);
                bool isFront = links[zone.Id].Any(id => IsFightingSide(zoneById[id].Control)
                    && !string.Equals(zoneById[id].Control, zone.Control, StringComparison.OrdinalIgnoreCase));

                double level = SupplyLevelOf(zone);

                double rate = config.SupplyRecoveryRate
                    * config.GetRouteRecovery(zone.SupplyRoute)
                    * level
                    * (isFront ? config.FrontLineRecoveryFactor : 1.0);

                foreach (WargameFormation f in zone.Formations)
                {
                    if (f.ForcePower <= 0) continue;   // épuisée : définitif
                    if (!string.Equals(f.Side, zone.Control, StringComparison.OrdinalIgnoreCase)) continue;

                    double nominal = f.GetNominalForcePower(catalog);

                    // Usure : coupée, réserve vide depuis 2 tours ou plus -> perte lente, même sans combat (jusqu'au plancher)
                    if (isCut && config.CutOffAttrition > 0 && !IsOffMapId(zone.Id)
                        && zone.CutOffTurns >= EffectiveGrace(config) + 2
                        && string.Equals(zone.CutOffSide, zone.Control, StringComparison.OrdinalIgnoreCase))
                    {
                        double floor = nominal * Math.Max(0, config.CutOffAttritionFloor);
                        if (f.ForcePower > floor)
                        {
                            double before = f.ForcePower;
                            f.ForcePower = Math.Max(floor, f.ForcePower * (1.0 - config.CutOffAttrition));
                            starving++;
                            Detail(config, "  starving " + f.Name + " in " + zone.Id + " : " + before.ToString("0.0") + " -> " + f.ForcePower.ToString("0.0")
                                + " (cut off, no stock left)");
                        }
                    }

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
                        + (isFront ? ", front line" : "") + (engaged ? ", engaged in a fight" : "")
                        + (level < 1.0 ? ", supply level " + (level * 100).ToString("0") + "%" : "") + ")");
                    f.ForcePower += gain;
                    totalGain += gain;
                    reinforced++;
                }
            }

            if (starving > 0)
                Trace(config, "  starving : " + starving + " formation(s) cut off without stock lose " + (config.CutOffAttrition * 100).ToString("0.#") + "% of their strength");

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

        // Niveau de ravitaillement (0 à 1) de chaque zone, calculé par FindCutOffZones, et d'où il vient.
        // Champs statiques (propres à chaque thread) pour ne pas ajouter un paramètre à chaque méthode du moteur.
        [ThreadStatic]
        private static Dictionary<string, double> _supplyLevels;
        [ThreadStatic]
        private static Dictionary<string, string> _supplyNotes;

        // Niveau d'une zone : 1 si le camp n'a aucune source (fonction désactivée) ou si la zone n'est pas concernée
        private static double SupplyLevelOf(WargameZoneData zone)
        {
            double level;
            if (_supplyLevels != null && _supplyLevels.TryGetValue(zone.Id, out level))
                return level;

            return 1.0;
        }

        // Coût de ravitailler À TRAVERS une zone : coût du terrain / débit de la route.
        // Plaine + route = 1 ; montagne + piste = 6 ; montagne sans route = 12.
        private static double SupplyCostOf(WargameZoneData zone, WargameCombatConfig config)
        {
            double flow = config.GetRouteFlow(zone.SupplyRoute);

            // Pont : détruit, la route de la zone ne passe presque plus (BridgeDownFactor) ; vivant, elle passe normalement
            double bridge;
            if (config.BridgeAliveByZone.TryGetValue(zone.Id, out bridge))
                flow *= config.BridgeDownFactor + (1.0 - config.BridgeDownFactor) * bridge;

            return config.GetTerrainMove(zone.Terrain) / Math.Max(0.01, flow);
        }

        // Force de la source d'une zone, de 0 (pas une source) à 1 (ravitaillement complet).
        // Le champ "Source" posé à la main compte pour 1 (comme avant) ; sinon, valeur des objectifs-sources / SupplySourceFull.
        // Seuls les OBJECTIFS (entrepôt, port...) avec une valeur "wargameSupply" sont des sources : le champ "Source" des zones est ignoré.
        private static double SourceStrength(WargameZoneData zone, WargameCombatConfig config)
        {
            double value = SourceValue(zone, config);

            if (value > 0)
                return Math.Min(1.0, value / Math.Max(0.1, config.SupplySourceFull));

            return 0.0;
        }

        // Capacité brute d'une zone source (objectifs + mer + point avancé), sur l'échelle de SupplySourceFull
        private static double SourceValue(WargameZoneData zone, WargameCombatConfig config)
        {
            double value = 0, sea;
            // Source d'objectif : ignorée si la zone vient de changer de main (liste calculée avant un débarquement ou une conquête)
            bool captured = !string.IsNullOrEmpty(zone.SupplyOwner)
                && !string.Equals(zone.SupplyOwner, zone.Control, StringComparison.OrdinalIgnoreCase);

            double objective;
            if (!captured && config.ObjectiveSupplyByZone.TryGetValue(zone.Id, out objective)) value += objective;
            if (config.LandingSupplyByZone.TryGetValue(zone.Id, out sea)) value += sea;

            double forward;
            if (config.ForwardSupplyByZone.TryGetValue(zone.Id, out forward)) value += forward;

            return value;
        }

        // Le camp a-t-il un système de ravitaillement ? Oui si une de ses zones a un "Source" manuel OU un objectif-source
        // (même détruit : un camp dont toutes les sources sont détruites est coupé, pas ignoré).
        private static bool HasSupplySystem(List<WargameZoneData> sideZones, WargameCombatConfig config)
        {
            return sideZones.Any(z => config.ObjectiveSupplyByZone.ContainsKey(z.Id) || config.LandingSupplyByZone.ContainsKey(z.Id)
                || config.ForwardSupplyByZone.ContainsKey(z.Id));
        }

        // Range dans la config ce que les objectifs de la targetlist apportent au ravitaillement :
        // sources (wargameSupply x état) et ponts (wargameBridge). Appelé avant chaque résolution, avec les objectifs
        // lus dans la targetlist (Active en production, Init dans la preview).
        public static void ApplyObjectivesToSupply(List<WargameZoneData> zones, List<WargameObjective> objectives, WargameCombatConfig config,
            Dictionary<WargameObjective, double> aliveOverride = null)
        {
            var supply = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var bridges = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (objectives != null)
            {
                foreach (WargameObjective obj in objectives)
                {
                    if (obj.SupplyValue <= 0 && !obj.IsBridge) continue;

                    WargameZoneData zone = FindZoneContaining(zones, obj.Position);
                    if (zone == null) continue;

                    // aliveOverride : état simulé par la Preview (événements), sans toucher à l'objectif lui-même
                    double aliveValue = obj.Alive;
                    if (aliveOverride != null && aliveOverride.TryGetValue(obj, out double simulated)) aliveValue = simulated;

                    double alive = Math.Max(0.0, Math.Min(1.0, aliveValue / 100.0));

                    if (obj.SupplyValue > 0)
                    {
                        // Source capturée (la zone n'est plus tenue par son camp de départ) : elle ne sert plus à personne.
                        // La clé reste présente avec 0, pour que le camp soit bien vu comme "sans source" et non "sans système".
                        double supplyAlive = alive;
                        if (!string.IsNullOrEmpty(zone.SupplyOwner)
                            && !string.Equals(zone.SupplyOwner, zone.Control, StringComparison.OrdinalIgnoreCase))
                            supplyAlive = 0;

                        double current;
                        supply.TryGetValue(zone.Id, out current);
                        supply[zone.Id] = current + obj.SupplyValue * supplyAlive;
                    }

                    if (obj.IsBridge)
                    {
                        double best;
                        bridges.TryGetValue(zone.Id, out best);
                        bridges[zone.Id] = Math.Max(best, alive);   // un seul pont vivant suffit à garder la route
                    }
                }
            }

            config.ObjectiveSupplyByZone = supply;
            config.BridgeAliveByZone = bridges;
        }

        // Ravitaillement par mer d'un plan de débarquement : la plage visée (LandingTarget) devient une source tant qu'elle est tenue
        // par le camp du plan et que le flag du plan (SupplyFlag, facultatif) est levé. Valeur = "SupplySource" du plan.
        // Remplace à chaque appel la liste précédente (config.LandingSupplyByZone).
        public static void ApplyLandingSupply(List<WargameZoneData> zones, HashSet<string> activeFlags, WargameCombatConfig config)
        {
            var sea = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (zones != null)
            {
                foreach (WargameZoneData plan in zones)
                {
                    if (!IsLandingZone(plan) || plan.SupplySource <= 0 || string.IsNullOrEmpty(plan.LandingTarget)) continue;
                    if (!IsFightingSide(plan.Control)) continue;

                    if (!string.IsNullOrEmpty(plan.SupplyFlag)
                        && (activeFlags == null || !activeFlags.Any(f => string.Equals(f, plan.SupplyFlag, StringComparison.OrdinalIgnoreCase))))
                        continue;

                    WargameZoneData beach = zones.FirstOrDefault(z => !IsOffMapId(z.Id)
                        && string.Equals(z.Id, plan.LandingTarget, StringComparison.OrdinalIgnoreCase));

                    if (beach == null || !string.Equals(beach.Control, plan.Control, StringComparison.OrdinalIgnoreCase)) continue;

                    double current;
                    sea.TryGetValue(beach.Id, out current);
                    sea[beach.Id] = current + plan.SupplySource;
                }
            }

            config.LandingSupplyByZone = sea;
        }

        // Ravitaillement par réseau. Pour chaque camp qui a au moins une zone source (SupplySource > 0) :
        // plus court chemin (Dijkstra) depuis les sources, à travers les zones AMIES seulement. Le coût d'un pas
        // = distance entre les centres (en "distance moyenne entre voisines") x coût moyen des deux zones.
        // Niveau d'une zone = 1 - coût / SupplyRange (la meilleure source gagne). Une zone dont le niveau tombe
        // sous SupplyMinLevel, ou qu'aucune source n'atteint (poche encerclée), est COUPEE.
        // Un camp qui n'a aucune zone source est ignoré, sinon toutes ses zones passeraient pour coupées.
        // SupplyNetwork = false : ancien comportement (relié = niveau 1, sinon coupé).
        private static HashSet<string> FindCutOffZones(
            List<WargameZoneData> zones,
            Dictionary<string, List<string>> links,
            Dictionary<string, WargameZoneData> zoneById,
            WargameCombatConfig config)
        {
            var cutOff = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _supplyLevels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _supplyNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Distance moyenne entre centres de zones voisines : l'unité de distance du ravitaillement
            var centers = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in zones) centers[z.Id] = ZoneCenterOf(z);

            double sum = 0;
            int count = 0;
            foreach (WargameZoneData z in zones)
                foreach (string id in links[z.Id])
                    if (string.Compare(z.Id, id, StringComparison.OrdinalIgnoreCase) < 0) { sum += CenterDistance(centers, z.Id, id); count++; }

            double unit = count > 0 && sum > 0 ? sum / count : 1.0;
            // Avec les convois, les sources ne distribuent elles-mêmes qu'autour d'elles (ConvoyFreeRange) : plus loin, il faut un convoi
            double range = Math.Max(0.1, config.SupplyConvoys ? config.ConvoyFreeRange : config.SupplyRange);

            bool anySystem = HasSupplySystem(zones, config);

            foreach (string side in new[] { WargameSide.Blue, WargameSide.Red })
            {
                List<WargameZoneData> sideZones = zones
                    .Where(z => string.Equals(z.Control, side, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!HasSupplySystem(sideZones, config))
                {
                    // Si l'autre camp a un réseau, un camp sans aucune source est COUPE partout (pas à 100 %).
                    // Si personne n'en a, la fonction est éteinte (anciennes campagnes inchangées).
                    if (anySystem)
                    {
                        foreach (WargameZoneData zone in sideZones)
                        {
                            _supplyLevels[zone.Id] = 0;
                            _supplyNotes[zone.Id] = "this side has no supply source";
                            cutOff.Add(zone.Id);
                        }
                    }

                    continue;
                }

                List<WargameZoneData> sources = sideZones.Where(z => SourceStrength(z, config) > 0).ToList();

                var dist = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var from = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);       // source d'où vient le ravitaillement
                var worstCost = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);  // plus gros pas du trajet
                var worstZone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // et où il se trouve
                var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (WargameZoneData source in sources)
                {
                    // Une source faible part avec une partie du coût déjà "payée" : son niveau de départ = sa force
                    dist[source.Id] = config.SupplyNetwork ? range * (1.0 - SourceStrength(source, config)) : 0.0;
                    from[source.Id] = source.Id;
                    worstCost[source.Id] = 0;
                    worstZone[source.Id] = "";
                }

                while (true)
                {
                    string current = null;
                    foreach (KeyValuePair<string, double> kv in dist)
                        if (!done.Contains(kv.Key) && (current == null || kv.Value < dist[current])) current = kv.Key;

                    if (current == null) break;
                    done.Add(current);

                    foreach (string next in links[current])
                    {
                        if (done.Contains(next)) continue;
                        if (!string.Equals(zoneById[next].Control, side, StringComparison.OrdinalIgnoreCase)) continue;

                        double step = 0;
                        if (config.SupplyNetwork)
                            step = CenterDistance(centers, current, next) / unit
                                * 0.5 * (SupplyCostOf(zoneById[current], config) + SupplyCostOf(zoneById[next], config));

                        double newDist = dist[current] + step;
                        double known;
                        if (dist.TryGetValue(next, out known) && known <= newDist) continue;

                        dist[next] = newDist;
                        from[next] = from[current];

                        if (step > worstCost[current]) { worstCost[next] = step; worstZone[next] = next; }
                        else { worstCost[next] = worstCost[current]; worstZone[next] = worstZone[current]; }
                    }
                }

                foreach (WargameZoneData zone in sideZones)
                {
                    double d;
                    if (!dist.TryGetValue(zone.Id, out d))
                    {
                        _supplyLevels[zone.Id] = 0;
                        _supplyNotes[zone.Id] = "no friendly way to a source";
                        cutOff.Add(zone.Id);
                        continue;
                    }

                    double level = config.SupplyNetwork ? Math.Max(0.0, 1.0 - d / range) : 1.0;
                    _supplyLevels[zone.Id] = level;
                    _supplyNotes[zone.Id] = "from " + from[zone.Id] + ", cost " + d.ToString("0.0") + " of " + range.ToString("0.0")
                        + (worstZone[zone.Id].Length > 0 ? ", hardest step into " + worstZone[zone.Id] + " (" + worstCost[zone.Id].ToString("0.0") + ")" : "");

                    if (config.SupplyNetwork && level < config.SupplyMinLevel)
                        cutOff.Add(zone.Id);
                }
            }

            return cutOff;
        }

        // ---- Carte de ravitaillement (affichage seulement : colore les zones selon leur niveau) ----
        // ---- Convois et points de ravitaillement avancés ----
        // Un convoi par secteur mal ravitaillé (plafond ConvoyMax par camp). Il part d'une source par la route (zones amies)
        // et recharge le point avancé de son secteur. Sans route (poche, plage isolée), seul l'aérien peut le recharger.
        public class WargameConvoy
        {
            public string Side;
            public string SourceId = "";                          // source de départ ("" = pas de route)
            public string TargetId = "";                          // zone du point avancé
            public List<string> Route = new List<string>();       // zones traversées, de la source au point avancé
            public List<string> ServedIds = new List<string>();   // zones du secteur
            public bool ByRoad;
            public double Delivered;                              // part de la cargaison arrivée (route + air), 0 à 1
            public double ReserveBefore, ReserveAfter;            // stock du point avancé, en TONNES (avant livraison / après livraison puis consommation)
            public double DeliveredTons;                          // tonnes livrées ce tour (part livrée x charge du convoi)
            public double PayloadTons;                            // charge du convoi s'il arrive en entier (véhicules du template x ConvoyTruckTons)
            public int AirSorties;                                // rotations d'hélicoptère voulues pour ce secteur (l'export les limite selon la flotte et la portée)
            public double AirPlannedTons;                         // tonnes que ces rotations apporteraient
            public double AirTons;                                // tonnes réellement livrées par air ce tour
            public string Template = "";                          // template "convoy" choisi (le même sert à fabriquer le convoi en jeu), "" = aucun
            public double DemandEstimate;                         // besoin estimé du secteur en début de tour, en tonnes
            public double Coverage = 1.0;                         // part du besoin couverte par le stock (0 à 1) : multiplie la capacité du point
            public double UsedTons;                               // tonnes réellement consommées par le secteur (rempli par ApplySupply)
            public double Capacity;                               // capacité du point avancé (plafonnée par la source)
            public bool Lost;                                     // la zone du point avancé a été prise pendant le tour : point perdu
            public int SlotIndex;                                 // numéro d'emplacement (1 à ConvoyMax), stable d'un tour à l'autre
            public string Slot = "";                              // "BLUE_1" : nom du point WG_POINT_BLUE_1 et du convoi WG_CONVOY_BLUE_1
            public bool Rebuilt;                                  // le point avait été détruit : rebâti avec un malus
        }

        // Appelé une fois par tour, au début de la résolution (après le débarquement). Recalcule les secteurs, les convois,
        // et remplit config.ForwardSupplyByZone (les points avancés actifs, qui servent ensuite de sources).
        // catalog (facultatif) : sert à estimer le besoin en tonnes du secteur ; sans lui, le stock n'est pas comparé au besoin (couverture 100 %).
        internal static void PlanConvoys(List<WargameZoneData> zones, WargameCombatConfig config, WargameTemplateCatalog catalog = null)
        {
            config.ForwardSupplyByZone = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            config.Convoys = new List<WargameConvoy>();

            if (zones == null) return;

            if (!config.SupplyConvoys)
            {
                foreach (WargameZoneData z in zones) { z.ForwardReserve = 0; z.ForwardSide = ""; }
                return;
            }

            List<WargameZoneData> mapZones = zones.Where(z => !IsOffMapId(z.Id)).ToList();
            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in mapZones) zoneById[z.Id] = z;

            Dictionary<string, List<string>> links = BuildLinks(mapZones);
            FindCutOffZones(mapZones, links, zoneById, config);   // niveaux du réseau SEUL (aucun point avancé pour l'instant)

            var centers = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in mapZones) centers[z.Id] = ZoneCenterOf(z);

            foreach (string side in new[] { WargameSide.Blue, WargameSide.Red })
            {
                List<WargameZoneData> sideZones = mapZones
                    .Where(z => string.Equals(z.Control, side, StringComparison.OrdinalIgnoreCase)).ToList();

                // Réserves du tour précédent (seulement dans des zones encore tenues), puis on efface tout : on replace ci-dessous
                var oldReserve = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var oldSlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);        // emplacement du point de la zone
                var destroyed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);              // point détruit à la mission d'avant
                foreach (WargameZoneData z in sideZones)
                {
                    if (!string.Equals(z.ForwardSide, side, StringComparison.OrdinalIgnoreCase)) continue;
                    if (z.ForwardReserve > 0) oldReserve[z.Id] = z.ForwardReserve;
                    if (z.ForwardSlot > 0) oldSlot[z.Id] = z.ForwardSlot;
                    if (z.ForwardDestroyed) destroyed.Add(z.Id);
                }

                foreach (WargameZoneData z in zones)
                    if (string.Equals(z.ForwardSide, side, StringComparison.OrdinalIgnoreCase))
                    {
                        z.ForwardReserve = 0; z.ForwardSide = ""; z.ForwardSlot = 0; z.ForwardDestroyed = false;
                    }

                var usedSlots = new HashSet<int>();

                int max = (int)Math.Round(config.ConvoyMax);
                if (max <= 0) continue;

                // Zones qui ont besoin d'un convoi : des formations, et un réseau qui ne les ravitaille pas assez.
                // Priorité : la première ligne, puis les plus mal ravitaillées.
                List<WargameZoneData> needy = sideZones
                    .Where(z => LiveFormations(z, side).Count > 0 && SupplyLevelOf(z) < config.ConvoyNeedLevel
                        && SourceStrength(z, config) <= 0)   // une zone source ne s'envoie pas de convoi à elle-même
                    .OrderByDescending(z => IsFrontZone(z, links, zoneById) ? 1 : 0)
                    .ThenBy(z => SupplyLevelOf(z))
                    .ThenBy(z => z.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (needy.Count == 0) continue;

                // Secteurs : une zone "graine" par convoi, si possible pas voisines entre elles ; chaque zone va à la graine la plus proche
                int count = Math.Min(max, needy.Count);
                var seeds = new List<WargameZoneData>();
                foreach (WargameZoneData z in needy)
                {
                    if (seeds.Count >= count) break;
                    if (seeds.Any(s => links[s.Id].Contains(z.Id, StringComparer.OrdinalIgnoreCase))) continue;
                    seeds.Add(z);
                }
                foreach (WargameZoneData z in needy)
                {
                    if (seeds.Count >= count) break;
                    if (!seeds.Contains(z)) seeds.Add(z);
                }

                var groups = new Dictionary<WargameZoneData, List<WargameZoneData>>();
                foreach (WargameZoneData s in seeds) groups[s] = new List<WargameZoneData>();
                foreach (WargameZoneData z in needy)
                {
                    WargameZoneData best = seeds.OrderBy(s => CenterDistance(centers, s.Id, z.Id)).First();
                    groups[best].Add(z);
                }

                // Routes : le chemin le plus RAPIDE depuis les sources du camp à travers ses zones (Dijkstra), avec le même coût que
                // le ravitaillement : distance x coût de la zone (terrain / débit de la route, pont détruit). Un convoi préfère donc
                // un détour par la route à une traversée de collines sans route.
                var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var cost = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var settled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (WargameZoneData src in sideZones.Where(z => SourceStrength(z, config) > 0))
                {
                    parent[src.Id] = null;
                    cost[src.Id] = 0;
                }
                while (true)
                {
                    string current = null;
                    foreach (KeyValuePair<string, double> kv in cost)
                        if (!settled.Contains(kv.Key) && (current == null || kv.Value < cost[current])) current = kv.Key;

                    if (current == null) break;
                    settled.Add(current);

                    foreach (string next in links[current])
                    {
                        if (settled.Contains(next)) continue;
                        if (!string.Equals(zoneById[next].Control, side, StringComparison.OrdinalIgnoreCase)) continue;

                        double step = CenterDistance(centers, current, next)
                            * 0.5 * (SupplyCostOf(zoneById[current], config) + SupplyCostOf(zoneById[next], config));
                        double known;
                        if (cost.TryGetValue(next, out known) && known <= cost[current] + step) continue;

                        cost[next] = cost[current] + step;
                        parent[next] = current;
                    }
                }

                foreach (KeyValuePair<WargameZoneData, List<WargameZoneData>> g in groups)
                {
                    // Point avancé : la graine du secteur (la zone prioritaire, en général la première ligne) si la route y arrive,
                    // sinon la zone du secteur reliée par la route la plus proche de la graine ; sans route du tout, la graine (air)
                    WargameZoneData seed = g.Key;
                    WargameZoneData target = parent.ContainsKey(seed.Id) ? seed
                        : g.Value.Where(z => parent.ContainsKey(z.Id))
                            .OrderBy(z => CenterDistance(centers, z.Id, seed.Id)).ThenBy(z => z.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    bool byRoad = target != null;
                    if (target == null) target = g.Key;

                    var convoy = new WargameConvoy { Side = side, TargetId = target.Id, ByRoad = byRoad };
                    convoy.ServedIds = g.Value.Select(z => z.Id).ToList();

                    if (byRoad)
                    {
                        for (string id = target.Id; id != null; id = parent[id])
                            convoy.Route.Insert(0, id);
                        convoy.SourceId = convoy.Route[0];
                    }

                    // Emplacement : celui du point du secteur au tour d'avant (le nom reste le même), sinon le premier libre
                    int slot = 0, s;
                    foreach (WargameZoneData z in g.Value)
                        if (oldSlot.TryGetValue(z.Id, out s) && !usedSlots.Contains(s)) { slot = s; break; }
                    if (slot == 0)
                        for (int n = 1; n <= Math.Max(max, 1) + 10; n++)
                            if (!usedSlots.Contains(n)) { slot = n; break; }
                    usedSlots.Add(slot);
                    convoy.SlotIndex = slot;
                    convoy.Slot = side.ToUpperInvariant() + "_" + slot;

                    // Livraison par la route : résultat réel de la mission (par emplacement) s'il existe, sinon la valeur par camp.
                    // C'est la part (0 à 1) de la charge du convoi qui arrive.
                    double ground = 0, bySlot;
                    if (byRoad)
                        ground = config.ConvoyDeliveredBySlot.TryGetValue(convoy.Slot, out bySlot) ? Clamp01(bySlot)
                            : Clamp01(side == WargameSide.Blue ? config.ConvoyDeliveredBlue : config.ConvoyDeliveredRed);
                    convoy.Delivered = ground;

                    // Point détruit à la mission d'avant : le convoi le rebâtit, mais ne recharge qu'une partie
                    convoy.Rebuilt = g.Value.Any(z => destroyed.Contains(z.Id));

                    // Stock en TONNES : ce qui restait au point avancé du secteur (déjà diminué de la consommation du tour d'avant, voir
                    // ConsumeForwardStock) + la livraison du convoi (part livrée x charge d'un convoi), plafonné au stock maximum
                    double before = 0, r;
                    foreach (WargameZoneData z in g.Value)
                        if (oldReserve.TryGetValue(z.Id, out r)) before = Math.Max(before, r);

                    // Charge du convoi = nombre de véhicules de son template x tonnes par véhicule (le campaignMaker règle la taille du convoi
                    // avec le template). Le template est choisi ici, de façon fixe selon l'emplacement, pour que l'export fabrique le même.
                    double payload = Math.Max(0, config.ConvoyPayloadTons);
                    if (catalog != null && byRoad)
                    {
                        List<WargameTemplateEntry> convoyTemplates = catalog.OfType(WargameUnitType.Convoy, side)
                            .OrderBy(t => t.Template, StringComparer.OrdinalIgnoreCase).ToList();

                        if (convoyTemplates.Count > 0)
                        {
                            WargameTemplateEntry pick = convoyTemplates[(slot - 1) % convoyTemplates.Count];
                            convoy.Template = pick.Template;
                            // Tonnes emportées réglées dans le catalogue, sinon véhicules du template x tonnes par véhicule
                            if (pick.CarryTons > 0) payload = pick.CarryTons;
                            else if (pick.VehicleCount > 0) payload = pick.VehicleCount * Math.Max(0, config.ConvoyTruckTons);
                        }
                    }
                    convoy.PayloadTons = payload;

                    // Besoin estimé du secteur (tonnes) en début de tour
                    convoy.DemandEstimate = EstimateSectorDemand(g.Value, side, links, zoneById, catalog, config);

                    // Hélicoptères : ils visent le besoin que le stock actuel ne couvre pas (une part seulement si un convoi routier y va,
                    // tout le besoin sans route). Nombre de rotations = tonnes voulues / charge d'une rotation, plafonné par point.
                    double heliCargo = side == WargameSide.Blue ? config.HeliCargoBlueTons : config.HeliCargoRedTons;
                    double heliWanted = Math.Max(0, convoy.DemandEstimate - before) * (byRoad ? config.HeliRoadShare : 1.0);
                    int maxSorties = (int)Math.Round(Math.Max(0, config.HeliMaxSorties));
                    convoy.AirSorties = (heliCargo > 0.001 && heliWanted > 0.05 && maxSorties > 0)
                        ? Math.Min(maxSorties, (int)Math.Ceiling(heliWanted / heliCargo - 1e-9)) : 0;
                    convoy.AirPlannedTons = convoy.AirSorties * heliCargo;

                    // Livraison par air : tonnes rendues par la mission (par emplacement) si elles existent, sinon une part du plan (test)
                    double airTons;
                    if (!config.AirDeliveredBySlot.TryGetValue(convoy.Slot, out airTons))
                        airTons = convoy.AirPlannedTons * Clamp01(side == WargameSide.Blue ? config.AirDeliveredBlue : config.AirDeliveredRed);
                    convoy.AirTons = Math.Max(0, airTons);

                    convoy.DeliveredTons = ground * payload + convoy.AirTons;
                    if (convoy.Rebuilt)
                        convoy.DeliveredTons *= Math.Max(0, 1.0 - config.ForwardRebuildLoss);

                    double after = Math.Max(0, Math.Min(Math.Max(0, config.ForwardPointStockTons), before + convoy.DeliveredTons));

                    // Le stock couvre-t-il le besoin du secteur ? Sinon le point fournit moins (baisse proportionnelle de sa capacité)
                    convoy.Coverage = convoy.DemandEstimate > 0.001 ? Math.Min(1.0, after / convoy.DemandEstimate) : 1.0;

                    // Un convoi n'apporte que ce que sa source peut fournir : point avancé plafonné à la capacité de la source
                    // (par la route) ; un point ravitaillé seulement par air garde la capacité normale d'un point avancé
                    double capacity = config.ForwardPointCapacity;
                    if (byRoad) capacity = Math.Min(capacity, SourceValue(zoneById[convoy.SourceId], config));
                    convoy.Capacity = capacity;
                    convoy.ReserveBefore = before;
                    convoy.ReserveAfter = after;

                    if (after > 0.001)
                    {
                        target.ForwardReserve = after;
                        target.ForwardSide = side;
                        target.ForwardSlot = slot;

                        double current;
                        config.ForwardSupplyByZone.TryGetValue(target.Id, out current);
                        config.ForwardSupplyByZone[target.Id] = current + capacity * convoy.Coverage;
                    }

                    config.Convoys.Add(convoy);

                    Trace(config, "  convoy " + convoy.Slot + " -> " + target.Id + (convoy.Rebuilt ? " [rebuilds a destroyed point]" : "") + " (serves " + string.Join(", ", convoy.ServedIds) + ") : "
                        + (byRoad ? "road from " + convoy.SourceId + " (" + convoy.Route.Count + " zone(s))" : "NO ROAD (air only)")
                        + ", road " + (ground * 100).ToString("0") + "% of the payload, air " + convoy.AirTons.ToString("0.#") + " t (planned " + convoy.AirPlannedTons.ToString("0.#") + " t, " + convoy.AirSorties + " sortie(s))"
                        + ", stock " + before.ToString("0.#") + " t + delivered " + convoy.DeliveredTons.ToString("0.#") + " t (payload " + convoy.PayloadTons.ToString("0.#") + " t" + (convoy.Template.Length > 0 ? ", template " + convoy.Template : "") + ") = " + after.ToString("0.#") + " t"
                        + (convoy.DemandEstimate > 0.001 ? ", sector needs about " + convoy.DemandEstimate.ToString("0.#") + " t -> covered " + (convoy.Coverage * 100).ToString("0") + "%" : "")
                        + (after > 0.001 ? ", forward point active (capacity " + (capacity * convoy.Coverage).ToString("0.#") + " of " + capacity.ToString("0.#") + ")" : ", forward point EMPTY"));
                }
            }
        }

        // Besoin estimé (tonnes) des formations d'un secteur en DEBUT de tour : on ne sait pas encore qui va combattre,
        // donc seulement front (au contact de l'ennemi) ou arrière. Le vrai besoin est calculé en fin de tour (ComputeSupplyDemand).
        private static double EstimateSectorDemand(List<WargameZoneData> served, string side,
            Dictionary<string, List<string>> links, Dictionary<string, WargameZoneData> zoneById, WargameTemplateCatalog catalog, WargameCombatConfig config)
        {
            if (catalog == null) return 0;

            double total = 0;

            foreach (WargameZoneData z in served)
            {
                double coef = IsFrontZone(z, links, zoneById) ? config.SupplyUseFront : config.SupplyUseRear;

                foreach (WargameFormation f in LiveFormations(z, side))
                {
                    double strength;
                    total += FormationBaseTons(f, catalog, config, out strength) * coef;
                }
            }

            return total;
        }

        // Tonnes consommées en UN TOUR par une formation à l'état "front" :
        // xN x consommation du template (par jour) x part du jour d'un tour x force restante (0 à 1)
        private static double FormationBaseTons(WargameFormation f, WargameTemplateCatalog catalog, WargameCombatConfig config, out double strength)
        {
            double nominal = f.GetNominalForcePower(catalog);
            strength = nominal > 0 ? Math.Min(1.0, f.ForcePower / nominal) : 1.0;
            return f.Multiplier * catalog.GetConsumption(f.Template) * config.TurnDayFraction * strength;
        }

        // Fin de tour : le secteur de chaque point avancé consomme son besoin REEL (attaque / défense / front / arrière) dans le stock.
        // Stock insuffisant : il tombe à 0 (le manque s'est déjà traduit par une capacité réduite au début du tour).
        private static void ConsumeForwardStock(List<WargameZoneData> zones, Dictionary<string, WargameZoneData> zoneById,
            WargameTemplateCatalog catalog, WargameCombatConfig config, WargameCombatResult combat)
        {
            if (catalog == null || config.Convoys == null || config.Convoys.Count == 0) return;

            List<WargameSupplyDemand> demand = ComputeSupplyDemand(zones, catalog, config, combat);

            foreach (WargameConvoy convoy in config.Convoys)
            {
                if (convoy.Lost) continue;

                WargameZoneData target;
                if (!zoneById.TryGetValue(convoy.TargetId, out target)) continue;
                if (!string.Equals(target.ForwardSide, convoy.Side, StringComparison.OrdinalIgnoreCase)) continue;   // point vide ou déjà retiré

                var served = new HashSet<string>(convoy.ServedIds, StringComparer.OrdinalIgnoreCase);
                double used = demand.Where(d => d.Side == convoy.Side && served.Contains(d.ZoneId)).Sum(d => d.Tons);

                double stock = target.ForwardReserve;
                double taken = Math.Min(stock, used);

                convoy.UsedTons = used;
                target.ForwardReserve = stock - taken;
                convoy.ReserveAfter = target.ForwardReserve;

                Detail(config, "  forward stock " + convoy.Slot + " (" + target.Id + ") : " + stock.ToString("0.#") + " t, sector used " + used.ToString("0.#")
                    + " t -> " + target.ForwardReserve.ToString("0.#") + " t left" + (used > stock ? " (stock too small: " + (used - stock).ToString("0.#") + " t missing)" : ""));

                if (target.ForwardReserve <= 0.001)
                {
                    target.ForwardReserve = 0;   // point épuisé : plus rien à enregistrer (voir Saver_WargameZoneActive)
                    convoy.ReserveAfter = 0;
                }
            }
        }

        // Un point avancé planifié en début de tour, dans une zone que l'ennemi a prise pendant le combat, est perdu
        private static void DropLostForwardPoints(Dictionary<string, WargameZoneData> zoneById, WargameCombatConfig config)
        {
            if (config.Convoys == null) return;

            foreach (WargameConvoy convoy in config.Convoys)
            {
                WargameZoneData target;
                if (!zoneById.TryGetValue(convoy.TargetId, out target)) continue;
                if (string.Equals(target.Control, convoy.Side, StringComparison.OrdinalIgnoreCase)) continue;

                convoy.Lost = true;
                convoy.ReserveAfter = 0;
                config.ForwardSupplyByZone.Remove(target.Id);

                if (string.Equals(target.ForwardSide, convoy.Side, StringComparison.OrdinalIgnoreCase))
                {
                    target.ForwardReserve = 0;
                    target.ForwardSide = "";
                }

                Trace(config, "  convoy " + convoy.Side + " : forward point in " + target.Id + " LOST (zone taken by " + target.Control + ")");
            }
        }

        private static bool IsFrontZone(WargameZoneData zone, Dictionary<string, List<string>> links, Dictionary<string, WargameZoneData> zoneById)
        {
            return links[zone.Id].Any(id => IsFightingSide(zoneById[id].Control)
                && !string.Equals(zoneById[id].Control, zone.Control, StringComparison.OrdinalIgnoreCase));
        }

        private static double Clamp01(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        public class WargameSupplyInfo
        {
            public double Level;        // 0 à 1
            public bool CutOff;         // sous le seuil, ou aucun chemin vers une source
            public bool IsSource;       // la zone contient une source (manuelle ou objectif)
            public string Note;         // d'où vient le ravitaillement, coût...
        }

        // Calcule le niveau de ravitaillement de chaque zone de la carte (hors boîtes RESERVE / LANDING).
        // Les zones d'un camp sans aucune source ne sont pas dans le résultat (pas de réseau = pas de couleur).
        // Appeler ApplyObjectivesToSupply avant, pour que les objectifs-sources et les ponts soient pris en compte.
        public static Dictionary<string, WargameSupplyInfo> ComputeSupplyMap(List<WargameZoneData> zones, WargameCombatConfig config)
        {
            var result = new Dictionary<string, WargameSupplyInfo>(StringComparer.OrdinalIgnoreCase);
            if (zones == null || config == null) return result;

            List<WargameZoneData> mapZones = zones.Where(z => !IsOffMapId(z.Id)).ToList();
            if (mapZones.Count == 0) return result;

            var zoneById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);
            foreach (WargameZoneData z in mapZones) zoneById[z.Id] = z;

            // Les champs statiques du moteur sont sauvegardés : un calcul d'affichage ne doit rien changer à un tour en cours
            Dictionary<string, double> oldLevels = _supplyLevels;
            Dictionary<string, string> oldNotes = _supplyNotes;

            try
            {
                Dictionary<string, List<string>> links = BuildLinks(mapZones);
                HashSet<string> cutOff = FindCutOffZones(mapZones, links, zoneById, config);

                foreach (WargameZoneData z in mapZones)
                {
                    double level;
                    if (!_supplyLevels.TryGetValue(z.Id, out level))
                        continue;   // camp sans système de ravitaillement (ou zone neutre)

                    string note;
                    _supplyNotes.TryGetValue(z.Id, out note);

                    result[z.Id] = new WargameSupplyInfo
                    {
                        Level = level,
                        CutOff = cutOff.Contains(z.Id),
                        IsSource = SourceStrength(z, config) > 0,
                        Note = note ?? "",
                    };
                }
            }
            finally
            {
                _supplyLevels = oldLevels;
                _supplyNotes = oldNotes;
            }

            return result;
        }

        // Force du camp donné dans la zone
        // Pendant Resolve, seules les formations qui se battent comptent (largeur de front, voir FightingFormations)
        private static double SidePower(WargameZoneData zone, string side)
        {
            double total = 0;

            if (_combatWidth > 0)
            {
                foreach (WargameFormation f in FightingFormations(zone, side))
                    total += f.ForcePower;

                return total;
            }

            foreach (WargameFormation f in zone.Formations)
            {
                if (string.Equals(f.Side, side, StringComparison.OrdinalIgnoreCase))
                    total += f.ForcePower;
            }

            return total;
        }

        // Les formations qui SE BATTENT dans la zone : les plus fortes, au plus MaxFormationsPerZone (largeur de front).
        // Les autres sont en surplus : en attente derrière, elles ne comptent ni en attaque ni en défense.
        private static List<WargameFormation> FightingFormations(WargameZoneData zone, string side)
        {
            List<WargameFormation> live = LiveFormations(zone, side);

            if (_combatWidth <= 0 || live.Count <= _combatWidth)
                return live;

            return live.OrderByDescending(f => f.ForcePower).Take(_combatWidth).ToList();
        }

        private static double SupplyFactor(WargameZoneData zone, HashSet<string> cutOff, WargameCombatConfig config)
        {
            double factor = config.GetRouteCombat(zone.SupplyRoute);

            if (cutOff.Contains(zone.Id))
            {
                factor *= config.CutOffSupplyFactor;
            }
            else
            {
                // niveau de ravitaillement partiel : entre le facteur "coupée" (niveau 0) et 1 (niveau 1)
                double level = SupplyLevelOf(zone);
                if (level < 1.0)
                    factor *= config.CutOffSupplyFactor + (1.0 - config.CutOffSupplyFactor) * level;
            }

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
                if (IsReserveZone(zone)) continue;   // la réserve est hors carte : aucun lien, jamais de combat ni de passage

                foreach (string n in zone.Neighbors)
                {
                    if (!links.ContainsKey(n)) continue;
                    if (IsOffMapId(n)) continue;

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
            HashSet<string> cutOff = FindCutOffZones(zones, links, zoneById, config);
            HashSet<string> dcs = FindDcsZones(zones, config.DcsZoneDepth);

            foreach (WargameZoneData z in zones.OrderBy(x => x.Id))
            {
                var tags = new List<string>();

                if (IsFightingSide(z.Control) && links[z.Id].Any(id => IsFightingSide(zoneById[id].Control)
                    && !string.Equals(zoneById[id].Control, z.Control, StringComparison.OrdinalIgnoreCase)))
                    tags.Add("FRONT");
                if (IsLandingZone(z)) tags.Add("LANDING");
                else if (IsReserveZone(z)) tags.Add("RESERVE");
                else if (cutOff.Contains(z.Id)) tags.Add("CUT-OFF");
                else if (SupplyLevelOf(z) < 0.995) tags.Add("SUPPLY " + (SupplyLevelOf(z) * 100).ToString("0") + "%");
                if (dcs.Contains(z.Id)) tags.Add("DCS");
                if (SourceStrength(z, config) > 0) tags.Add("SOURCE");
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
