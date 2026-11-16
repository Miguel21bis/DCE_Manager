using System.Collections.Generic;
using System.Drawing;

namespace DCE_Manager
{
    // Valeurs internes de Control. Le campaignMaker ne voit jamais "blue"/"red" :
    // l'UI affiche les libellés lus dans camp_init (country_blue / country_red).
    // On garde ces valeurs internes pour que le lien vers les dossiers de templates
    // (Templates/wargame_blue, Templates/wargame_red) soit direct.
    internal static class WargameSide
    {
        public const string Blue = "blue";
        public const string Red = "red";
        public const string Contested = "contested";   // interne : zone pas encore réglée
    }

    // Type de terrain d'une zone. Le campaignMaker choisit un type, le coefficient
    // de défense associé vit dans la table ci-dessous : une seule sélection au lieu
    // d'un chiffre à doser, et les valeurs restent ajustables en un seul endroit.
    internal static class WargameTerrain
    {
        public const string Plain = "plain";
        public const string Hills = "hills";
        public const string Mountain = "mountain";
        public const string Urban = "urban";
        public const string Coastal = "coastal";

        public static readonly string[] All = { Plain, Hills, Mountain, Urban, Coastal };

        // Multiplicateur appliqué à la puissance défensive de la zone
        public static readonly Dictionary<string, double> DefenseCoef = new Dictionary<string, double>
        {
            { Plain, 1.0 },
            { Hills, 1.3 },
            { Mountain, 1.8 },
            { Urban, 1.6 },
            { Coastal, 1.1 },
        };

        public static double GetDefenseCoef(string terrain)
        {
            if (!string.IsNullOrEmpty(terrain) && DefenseCoef.TryGetValue(terrain, out double coef))
                return coef;

            return 1.0;
        }
    }

    // Qualité de desserte logistique de la zone. Volontairement au niveau zone et
    // pas par voisin : les relations de déplacement entre zones seront traitées
    // plus tard, ça ne bloque pas le calcul de ravitaillement d'ici là.
    internal static class WargameSupplyRoute
    {
        public const string None = "none";
        public const string Track = "track";
        public const string Road = "road";
        public const string Highway = "highway";

        public static readonly string[] All = { None, Track, Road, Highway };

        // Part du flux de ravitaillement que la zone est capable d'absorber
        public static readonly Dictionary<string, double> Capacity = new Dictionary<string, double>
        {
            { None, 0.25 },
            { Track, 0.5 },
            { Road, 1.0 },
            { Highway, 1.5 },
        };

        public static double GetCapacity(string route)
        {
            if (!string.IsNullOrEmpty(route) && Capacity.TryGetValue(route, out double cap))
                return cap;

            return 1.0;
        }
    }

    // Etat global du wargame pour une campagne, stocké en en-tête de
    // wargame_zones_init.lua. Le compteur n'est jamais décrémenté ni réutilisé :
    // un id libéré par une formation supprimée ne doit pas ressurgir sur une autre,
    // sinon un vieux débriefing se raccrocherait à la mauvaise formation.
    internal class WargameState
    {
        public int NextFormationId = 1;

        public List<WargameAxis> Axes = new List<WargameAxis>();

        public int AllocateFormationId()
        {
            int id = NextFormationId;
            NextFormationId++;
            return id;
        }
    }

    // Une zone DCS (mission.triggers.zones) peut être un cercle ou un "quad"
    // (polygone à 4 points, ou plus si dessiné autrement). On garde les deux
    // représentations possibles plutôt que de forcer un cercle en polygone.
    internal enum WargameZoneShape
    {
        Circle,
        Polygon,
    }

    // Une zone du wargame (front dynamique).
    // Géométrie (Shape/Center/Radius/DcsPoints) remplie par le parser depuis le .miz.
    // Champs de jeu remplis par le campaignMaker via l'UI.
    internal class WargameZoneData
    {
        // Identifiant de la zone = son nom dans DCS (ex: "CQ95")
        public string Id;

        public WargameZoneShape Shape;

        // Toujours renseigné : centre de la zone (cercle ou polygone), coordonnées DCS
        public PointF Center;

        // Rempli seulement si Shape == Circle
        public float Radius;

        // Rempli seulement si Shape == Polygon (coordonnées DCS absolues, dans l'ordre de dessin)
        public List<PointF> DcsPoints = new List<PointF>();

        // ----- Champs de jeu -----

        // WargameSide.Blue / Red (Contested = pas encore réglée)
        public string Control = WargameSide.Contested;

        public List<WargameFormation> Formations = new List<WargameFormation>();
        //public WargameIrregularMarker Irregular;   // null = pas de mode asymétrique
        public double ResupplyModifier;
        public List<string> Neighbors = new List<string>();

        // 0 = zone ordinaire. Sinon, débit de ravitaillement injecté dans le réseau
        // par cette zone (port, base arrière, frontière amie).
        public double SupplySource;

        // WargameTerrain.* : pèse sur la puissance défensive
        public string Terrain = WargameTerrain.Plain;

        // WargameSupplyRoute.* : pèse sur le ravitaillement reçu
        public string SupplyRoute = WargameSupplyRoute.Road;

        // Seulement pour un PLAN DE DEBARQUEMENT (zone dont l'id commence par LANDING) :
        // id de la zone-plage visée par défaut. Vide = pas encore choisie.
        // Le trigger peut viser une autre zone, c'est la valeur par défaut du plan.
        public string LandingTarget = "";

        // Plan de débarquement seulement : ravitaillement venu de la mer. La valeur est "SupplySource" (même échelle qu'un objectif "Supply").
        // SupplyFlag (facultatif) : tant que ce flag n'est pas levé, la source est inactive (ex. flag levé par un trigger Return.ShipGroupInPoly).
        public string SupplyFlag = "";

        // Plan de débarquement seulement : appui feu de la flotte, ajouté à la force d'assaut de chaque vague (même échelle que ForcePower).
        // NavalFlag (facultatif) : l'appui ne compte que si ce flag est levé (ex. trigger qui vérifie la présence des navires).
        public double NavalSupport = 0;
        public string NavalFlag = "";

        // Plan de débarquement en cours : échecs d'affilée de la vague actuelle, et dernière plage où ses troupes ont débarqué
        // (couverte ensuite par la flotte). Evoluent pendant la campagne : enregistrés dans l'Active seulement.
        public int LandingRetries = 0;
        public string LandingBeach = "";

        // Camp qui tenait la zone au départ de la simulation (Preview seulement, jamais enregistré).
        // Un objectif-source dans une zone tenue par un autre camp que celui-ci a été capturé : sa valeur tombe à 0.
        // Vide = règle inactive (éditeur, campagne réelle).
        public string SupplyOwner = "";

        // Depuis combien de tours la zone est coupée du ravitaillement (sous le camp CutOffSide). Les formations vivent sur leur stock
        // le temps du délai de grâce (cutoff_grace_turns), puis la zone ne peut plus attaquer. Enregistré dans l'Active.
        public int CutOffTurns = 0;
        public string CutOffSide = "";

        // Point de ravitaillement avancé posé dans cette zone (convois activés) : réserve restante en tours, et camp. Enregistré dans l'Active.
        public double ForwardReserve = 0;
        public string ForwardSide = "";

        // Emplacement du point (1, 2, 3... -> WG_POINT_BLUE_1) : stable d'un tour à l'autre. Enregistré dans l'Active.
        public int ForwardSlot = 0;

        // Le point posé dans DCS a été détruit pendant la mission jouée (lu avant le tour, jamais enregistré)
        public bool ForwardDestroyed;

        // Plan de débarquement en cours : -1 = pas lancé. Sinon nombre de tours à attendre avant la prochaine vague
        // (0 = elle peut partir). Evolue pendant la campagne : enregistré dans l'Active seulement.
        public int LandingWait = -1;

        // Force totale de la zone = somme de ses formations. Calculée, jamais stockée,
        // pour éviter qu'un total et le détail ne désynchronisent.
        public double GetTotalForcePower()
        {
            double total = 0;

            foreach (WargameFormation f in Formations)
                total += f.ForcePower;

            return total;
        }
    }

    // Une formation présente dans une zone : l'échelon que le campaignMaker nomme
    // et suit (IR_151_INF).
    //
    // Vocabulaire volontairement distinct de DCS, où "group" = un ensemble d'unités
    // et "unit" = le plus petit élément.
    //
    // UN SEUL exemplaire du template est posé en jeu. Multiplier (xN) dit combien
    // d'exemplaires il est censé représenter : c'est ce qui évite de faire exploser
    // la targetlist en posant des centaines de véhicules.
    //
    //     ForcePower nominale = Multiplier x Power du template (catalogue)
    //
    // ForcePower est ensuite la valeur vivante : elle descend avec les pertes.
    internal class WargameFormation
    {
        // Clé réelle, attribuée une fois et jamais modifiée ni réutilisée.
        // Reportée dans la targetlist (wargameFormationId) pour faire le lien.
        public int FormationId;

        // Nom lisible, modifiable par le campaignMaker (ex: "IR_151_INF")
        public string Name = "";

        public string Template;    // nom du fichier .stm, sans extension
        public string Side;        // WargameSide.Blue / Red (une zone contestée peut avoir les deux)
        public int Multiplier = 1; // xN : le template posé en représente N
        public double ForcePower;  // force courante

        // Numéro de vague, utilisé seulement dans un plan de débarquement (zone LANDING_n) :
        // toutes les formations de la vague 1 partent ensemble au trigger "vague 1", etc.
        // Ignoré partout ailleurs. 0 = vague abandonnée par le moteur (plus aucune chance sur la plage) : elle reste
        // dans la boîte mais n'est plus jamais lancée.
        public int Wave = 1;

        // Incrémenté à chaque fois qu'un nouvel exemplaire est reposé en jeu
        // (voir WargameEngineLosses) - sert uniquement à générer des noms DCS
        // uniques d'une génération à l'autre, jamais utilisé dans un calcul.
        public int SpawnGeneration = 1;

        // Surcharges optionnelles pour CETTE formation précise. null = utiliser la
        // valeur du template dans le catalogue. Même principe que Multiplier/
        // DefaultMultiplier : le catalogue donne le défaut, l'instance peut s'en
        // écarter (ex: cette DCA-là est prioritaire car elle protège un point clé).
        public int Priority;
        public List<string> Attributes = new List<string> { "Vehicles" };
        public double FirepowerMin;
        public double FirepowerMax;


        //public int? PriorityOverride;
        //public List<string> AttributesOverride;
        //public double? FirepowerMinOverride;
        //public double? FirepowerMaxOverride;

        //public int GetEffectivePriority(WargameTemplateCatalog catalog)
        //{
        //    if (PriorityOverride.HasValue) return PriorityOverride.Value;
        //    return catalog?.Find(Template)?.Priority ?? 5;
        //}

        //public List<string> GetEffectiveAttributes(WargameTemplateCatalog catalog)
        //{
        //    if (AttributesOverride != null && AttributesOverride.Count > 0) return AttributesOverride;
        //    return catalog?.Find(Template)?.Attributes ?? new List<string> { "Vehicles" };
        //}

        //public double GetEffectiveFirepowerMin(WargameTemplateCatalog catalog)
        //{
        //    if (FirepowerMinOverride.HasValue) return FirepowerMinOverride.Value;
        //    return catalog?.Find(Template)?.FirepowerMin ?? 2;
        //}

        //public double GetEffectiveFirepowerMax(WargameTemplateCatalog catalog)
        //{
        //    if (FirepowerMaxOverride.HasValue) return FirepowerMaxOverride.Value;
        //    return catalog?.Find(Template)?.FirepowerMax ?? 2;
        //}


        public double GetNominalForcePower(WargameTemplateCatalog catalog)
        {
            double power = catalog?.GetPower(Template) ?? 10.0;
            return Multiplier * power;
        }

        // Pertes après mission : chaque unité détruite coûte Power / nombre d'unités
        // du template. Détruire l'équivalent d'un template complet coûte donc
        // exactement Power, quel que soit le xN de la formation.
        public void ApplyVehicleLosses(int destroyedVehicles, WargameTemplateCatalog catalog)
        {
            double perVehicle = catalog?.GetPowerPerVehicle(Template) ?? 0;

            ForcePower -= destroyedVehicles * perVehicle;

            if (ForcePower < 0) ForcePower = 0;
        }
    }

}
