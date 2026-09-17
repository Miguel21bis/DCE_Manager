using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Point d'entrée "normal" pour obtenir la liste des zones wargame d'une campagne :
    // - si Init/wargame_zones_init.lua existe déjà -> on le charge tel quel (les
    //   champs de jeu édités par le campaignMaker sont dedans, pas dans le .miz)
    // - sinon -> première fois, on parse Init/wargame_zone.miz et on génère le fichier
    //
    // ReparseGeometry() sert pour le cas où le campaignMaker redessine ses zones dans
    // DCS après coup : on reprend la nouvelle géométrie, mais on garde les champs de
    // jeu déjà édités pour toute zone dont l'Id existe encore des deux côtés.
    //
    // ForceRegenerate() est le seul chemin qui écrase délibérément un fichier déjà
    // existant, même s'il contient du travail édité à la main. Utilisé uniquement
    // par le bouton "Create Warzone" de l'éditeur, avec confirmation explicite -
    // jamais appelé automatiquement.
    internal class WargameZoneRepository
    {
        // Etat global (compteur d'identifiants de formation) relu au dernier
        // LoadOrGenerateInit / ReparseGeometry / ForceRegenerate. A repasser tel
        // quel au Save.
        public static WargameState State = new WargameState();

        public static List<WargameZoneData> LoadOrGenerateInit(string campaignName)
        {
            string mizPath = GetMizPath(campaignName);
            string initLuaPath = GetInitLuaPath(campaignName);

            if (File.Exists(initLuaPath))
            {
                var loader = new WargameZoneInitLoader();
                List<WargameZoneData> loaded = loader.Load(initLuaPath);
                State = loader.State;
                return loaded;
            }

            if (!File.Exists(mizPath))
            {
                FormUtils.LogRegister("WargameZoneRepository | ni wargame_zones_init.lua ni wargame_zone.miz trouvés pour " + campaignName);
                return new List<WargameZoneData>();
            }

            List<WargameZoneData> zones = new Parser_WargameZones().LoadZonesFromMiz(mizPath);
            WargameGeometryCleaner.WeldNearbyVertices(zones);
            ApplyAutoNeighbors(zones, null);

            State = new WargameState();
            Saver_WargameZoneInit.Save(initLuaPath, zones, State);

            FormUtils.LogRegister("WargameZoneRepository | wargame_zones_init.lua généré pour '" + campaignName + "' (" + zones.Count + " zone(s))");

            return zones;
        }

        // Reprend la géométrie depuis le .miz, garde les champs de jeu des zones
        // déjà connues (même Id), puis réécrit wargame_zones_init.lua. Les zones
        // neuves (absentes de l'ancien fichier) reçoivent des voisins automatiques ;
        // les zones déjà connues gardent exactement ce que le campaignMaker a coché.
        public static List<WargameZoneData> ReparseGeometry(string campaignName)
        {
            string mizPath = GetMizPath(campaignName);
            string initLuaPath = GetInitLuaPath(campaignName);

            if (!File.Exists(mizPath))
            {
                FormUtils.LogRegister("WargameZoneRepository | wargame_zone.miz introuvable pour " + campaignName);
                return new List<WargameZoneData>();
            }

            List<WargameZoneData> newGeometry = new Parser_WargameZones().LoadZonesFromMiz(mizPath);
            WargameGeometryCleaner.WeldNearbyVertices(newGeometry);

            var existingById = new Dictionary<string, WargameZoneData>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(initLuaPath))
            {
                var loader = new WargameZoneInitLoader();
                existingById = loader.Load(initLuaPath).ToDictionary(z => z.Id, StringComparer.OrdinalIgnoreCase);
                State = loader.State;
            }

            foreach (WargameZoneData newZone in newGeometry)
            {
                if (existingById.TryGetValue(newZone.Id, out WargameZoneData oldZone))
                {
                    newZone.Control = oldZone.Control;
                    newZone.Formations = oldZone.Formations;
                    newZone.Irregular = oldZone.Irregular;
                    newZone.ResupplyModifier = oldZone.ResupplyModifier;
                    newZone.Neighbors = oldZone.Neighbors;
                    newZone.SupplySource = oldZone.SupplySource;
                    newZone.Terrain = oldZone.Terrain;
                    newZone.SupplyRoute = oldZone.SupplyRoute;
                }
            }

            ApplyAutoNeighbors(newGeometry, existingById.Keys);

            Saver_WargameZoneInit.Save(initLuaPath, newGeometry, State);

            FormUtils.LogRegister("WargameZoneRepository | géométrie re-parsée pour '" + campaignName + "' (" + newGeometry.Count + " zone(s))");

            return newGeometry;
        }

        // Repart de zéro : reparse le .miz et ECRASE wargame_zones_init.lua même
        // s'il existe déjà et contient du travail édité à la main (camp, formations,
        // voisins, etc). Le compteur de FormationId repart aussi à zéro.
        //
        // A n'appeler qu'après confirmation explicite du campaignMaker - voir
        // WargameEditor_Form.CreateWarzone(), seul appelant prévu.
        public static List<WargameZoneData> ForceRegenerate(string campaignName)
        {
            string mizPath = GetMizPath(campaignName);
            string initLuaPath = GetInitLuaPath(campaignName);

            if (!File.Exists(mizPath))
            {
                FormUtils.LogRegister("WargameZoneRepository | wargame_zone.miz introuvable pour " + campaignName);
                return new List<WargameZoneData>();
            }

            List<WargameZoneData> zones = new Parser_WargameZones().LoadZonesFromMiz(mizPath);
            WargameGeometryCleaner.WeldNearbyVertices(zones);
            ApplyAutoNeighbors(zones, null);

            State = new WargameState();

            Saver_WargameZoneInit.Save(initLuaPath, zones, State);

            FormUtils.LogRegister("WargameZoneRepository | wargame_zones_init.lua RECREE (Create Warzone) pour '"
                + campaignName + "' (" + zones.Count + " zone(s))");

            return zones;
        }

        // Remplit automatiquement les voisins des zones neuves, par proximité de
        // contour. knownIds = null -> toutes les zones sont neuves (première
        // génération / regénération complète). knownIds fourni -> seules les zones
        // absentes de cette liste sont neuves ; les autres gardent ce que le
        // campaignMaker a déjà réglé, jamais touché ici.
        private static void ApplyAutoNeighbors(List<WargameZoneData> zones, IEnumerable<string> knownIds)
        {
            var known = knownIds != null
                ? new HashSet<string>(knownIds, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, List<string>> detected = WargameNeighborDetector.DetectNeighbors(zones);

            int newCount = 0;

            foreach (WargameZoneData zone in zones)
            {
                if (known.Contains(zone.Id))
                    continue;

                newCount++;

                if (detected.TryGetValue(zone.Id, out List<string> neighbors))
                    zone.Neighbors = neighbors;
            }

            FormUtils.LogRegister("WargameZoneRepository | voisins auto-détectés pour " + newCount + " zone(s) neuve(s)");
        }

        internal static string GetCampaignFolder(string campaignName)
        {
            return Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns", campaignName);
        }

        // Fichiers déposés par le campaignMaker : directement sous Init/Wargame/.
        internal static string GetMizPath(string campaignName)
        {
            return Path.Combine(GetCampaignFolder(campaignName), "Init", "Wargame", "wargame_zone.miz");
        }

        internal static string GetSpawnMizPath(string campaignName)
        {
            return Path.Combine(GetCampaignFolder(campaignName), "Init", "Wargame", "wargame_spawn.miz");
        }

        internal static string GetMapImagePath(string campaignName)
        {
            return Path.Combine(GetCampaignFolder(campaignName), "Init", "Wargame", "wargame_map.jpg");
        }

        // Fichiers générés par DCE_Manager : sous Init/Wargame/DCE_Generated/.
        internal static string GetInitLuaPath(string campaignName)
        {
            return Path.Combine(GetCampaignFolder(campaignName), "Init", "Wargame", "DCE_Generated", "wargame_zones_init.lua");
        }

        internal static string GetCalibrationPath(string campaignName)
        {
            return Path.Combine(GetCampaignFolder(campaignName), "Init", "Wargame", "DCE_Generated", "wargame_map_calib.json");
        }

        internal static string GetTemplateCatalogPath(string campaignName)
        {
            return Path.Combine(GetCampaignFolder(campaignName), "Init", "Wargame", "DCE_Generated", "wargame_templates.lua");
        }

        // Etat courant en cours de campagne (control, formations, etc). Voir
        // Saver_WargameZoneActive / WargameZoneActiveLoader.
        internal static string GetActiveWargameZonesPath(string campaignName)
        {
            return Path.Combine(GetCampaignFolder(campaignName), "Active", "wargame_zones.lua");
        }
    }
}
