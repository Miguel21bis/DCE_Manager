using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DCE_Manager.Utils;
using System.Drawing;

namespace DCE_Manager
{
    // Consomme le résultat de la dernière mission jouée pour chaque formation
    // wargame déjà posée, et prépare la suivante :
    //
    //   1. lit alive% (Parser_WargameFormationLosses) pour en déduire combien de
    //      ForcePower l'exemplaire actuellement posé a perdu
    //   2. lance le COMBAT entre zones (WargameEngineCombat) sur les axes actifs :
    //      des zones peuvent changer de camp, les perdants se replient ou sont détruits
    //   3. tant qu'il reste au moins un exemplaire complet en réserve, repose un
    //      exemplaire tout frais (100%) - le joueur ne voit jamais la formation
    //      s'affaiblir tant que la réserve tient
    //   4. en dessous d'un exemplaire complet, pose exactement ce qu'il reste
    //      (dégradation réelle et visible, proportionnelle)
    //   5. à 0, ne repose rien : la formation est épuisée
    //   6. les objectifs situés dans une zone qui a basculé changent de camp
    //
    // Appelé AVANT de lancer la mission suivante, au même endroit que
    // Saver_TargetList_Wargame.WriteNewActiveFormations (qui lui s'occupe des
    // formations jamais encore posées) - alive% reflète toujours le résultat de
    // la mission qui vient de se jouer, déjà présent dans le fichier à ce moment.
    internal static class WargameEngineLosses
    {
        private static readonly Regex WargameFormationFlagRegex = new Regex(@"\bwargameFormation\b""?\]?\s*=\s*true");
        private static readonly Regex FormationIdRegex = new Regex(@"wargameFormationId""?\]?\s*=\s*(\d+)");

        // Marqueur posé dans Active par l'export de la Preview : le prochain ProcessBeforeMission ne rejoue pas de tour
        internal const string ExportedFlagFile = "wargame_exported.flag";

        // Tour réel (avant SkipMission) : tout le détail va dans un fichier du journal wargame (Debug\Wargame_DCEM)
        public static void ProcessBeforeMission(string campaignName)
        {
            string folder = WargameZoneRepository.GetCampaignFolder(campaignName);
            WargameLog.Begin(campaignName, "Turn", WargameLogistics.ReadMission(folder) + 1);
            try
            {
                ProcessBeforeMissionCore(campaignName);
            }
            catch (Exception ex)
            {
                WargameLog.Line("ERROR : " + ex);
                throw;
            }
            finally
            {
                WargameLog.End();
            }
        }

        private static void ProcessBeforeMissionCore(string campaignName)
        {
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);
            string activePath = Path.Combine(campaignFolder, "Active", "targetlist.lua");
            if (!File.Exists(activePath))
                return; // rien encore posé, WriteNewActiveFormations s'en chargera pour la toute première fois

            // Situation écrite par la Preview ("Jouer ce tour dans DCS") : le tour est DÉJÀ joué et les formations
            // déjà posées. Aucune mission n'a été jouée depuis : on ne rejoue pas un tour par-dessus (une seule fois).
            string exportedFlag = Path.Combine(campaignFolder, "Active", ExportedFlagFile);
            if (File.Exists(exportedFlag))
            {
                try { File.Delete(exportedFlag); } catch { }
                WargameLog.Line("WargameEngineLosses | " + campaignName + " : situation écrite par la Preview, tour du wargame NON rejoué cette fois");
                return;
            }

            Dictionary<int, double> losses = new Parser_WargameFormationLosses().Load(activePath);
            if (losses.Count == 0)
            {
                WargameLog.Line("no wargame formation in Active/targetlist.lua : nothing to do (first formations are posted by WriteNewActiveFormations)");
                return; // aucune formation wargame encore présente à traiter
            }

            List<WargameZoneData> zones = WargameZoneRepository.LoadOrGenerateInit(campaignName);
            new WargameZoneActiveLoader().ApplyActiveState(WargameZoneRepository.GetActiveWargameZonesPath(campaignName), zones);

            WargameCampaignInfo campaignInfo = WargameCampaignInfo.Load(campaignName);
           
            WargameTemplateCatalog catalog = WargameTemplateCatalog.LoadAndSync(campaignName, campaignInfo);
            WargameSpawnAreas spawnAreas = Saver_TargetList_Wargame.LoadSpawnAreas(campaignName);
            var rng = new Random();

            List<string> lines = File.ReadAllLines(activePath).ToList();

            List<TargetListBlock> blocks = TargetListBlockScanner.ScanBlocks(lines,
                (l, start, end) => Enumerable.Range(start, end - start + 1).Any(k => WargameFormationFlagRegex.IsMatch(l[k])),
                out Dictionary<string, int> sideCloseLine);

            Dictionary<int, TargetListBlock> blockByFormationId = new Dictionary<int, TargetListBlock>();
            foreach (TargetListBlock block in blocks.Where(b => b.Matched))
            {
                int id = ReadFormationId(lines, block);
                if (id > 0) blockByFormationId[id] = block;
            }

            // ---- 1. Pertes de la mission qui vient de se jouer ----

            foreach (WargameZoneData zone in zones)
            {
                foreach (WargameFormation formation in zone.Formations)
                {
                    if (!losses.TryGetValue(formation.FormationId, out double alive))
                        continue; // jamais encore posée, laissée à WriteNewActiveFormations

                    double powerPerExemplar = catalog.GetPower(formation.Template);
                    if (powerPerExemplar <= 0) powerPerExemplar = 10.0;

                    // La déduction de perte est indépendante de la repose : elle
                    // reflète un fait déjà arrivé (la mission qui vient de se
                    // jouer), qu'on arrive ou non à reposer un exemplaire ensuite.
                    double postedPower = Math.Min(formation.ForcePower, powerPerExemplar);
                    double lostPower = postedPower * (1 - alive / 100.0);
                    double before = formation.ForcePower;
                    formation.ForcePower = Math.Max(0, formation.ForcePower - lostPower);

                    if (alive < 100)
                        WargameLog.Line("loss " + formation.Name + " (" + formation.Side + ", " + zone.Id + ") : alive " + alive.ToString("0") + "% -> "
                            + before.ToString("0.0") + " -> " + formation.ForcePower.ToString("0.0"));
                }
            }
            WargameLog.Line("losses read for " + losses.Count + " posted block(s)");

            // Ravitaillement : sources et ponts lus dans la targetlist (état réel des objectifs)
            try
            {
                WargameEngineCombat.ApplyObjectivesToSupply(zones, new Parser_WargameObjectives().Load(activePath), campaignInfo.CombatConfig);
            }
            catch (Exception ex)
            {
                WargameLog.Line("WargameEngineLosses | objectifs de ravitaillement non lus : " + ex.Message);
            }

            // Logistique de la mission jouée : points avancés abîmés / détruits, résultats des convois et de l'aérien
            foreach (string line in WargameLogistics.ReadResults(campaignFolder, zones, campaignInfo.CombatConfig))
                WargameLog.Line("WargameLogistics | " + line);

            // ---- 2. Combat entre zones (axes actifs uniquement) ----

            Dictionary<string, string> controlBefore = WargameEngineCombat.SnapshotControl(zones);   // <-- AJOUTER

            WargameCombatResult combat = new WargameCombatResult();
            try
            {
                HashSet<string> activeFlags = WargameCampFlags.LoadActiveFlags(Path.Combine(campaignFolder, "Active", "camp_status.lua"));
                List<WargameAxis> axes = WargameZoneRepository.State != null ? WargameZoneRepository.State.Axes : null;

                campaignInfo.CombatConfig.Detail = WargameLog.Line;   // détail complet du moteur dans le journal du tour
                combat = WargameEngineCombat.Resolve(zones, axes, activeFlags, campaignInfo.CombatConfig, rng, catalog);
            }
            catch (Exception ex)
            {
                // Le combat ne doit jamais empêcher la mission de se générer
                WargameLog.Line("WargameEngineCombat | échec de la résolution, combat ignoré ce tour : " + ex.Message);
                combat = new WargameCombatResult();
            }

            // Formations trop affaiblies (après combat et repli) : dissoutes avant le ravitaillement
            WargameEngineCombat.RemoveBrokenFormations(zones, catalog, campaignInfo.CombatConfig, combat);

            WriteWargameOutputs(campaignFolder, zones, controlBefore, combat);   // <-- AJOUTER (remplace WriteReachedFlags)


            // ---- 2b. Ravitaillement : les formations vivantes récupèrent de la force ----

            try
            {
                WargameEngineCombat.ApplySupply(zones, catalog, campaignInfo.CombatConfig, combat);
            }
            catch (Exception ex)
            {
                WargameLog.Line("WargameEngineLosses | échec du ravitaillement, ignoré ce tour : " + ex.Message);
            }

            // ---- 3. Retrait / repose des blocs, dans la zone où chaque formation se trouve MAINTENANT ----

            var linesToRemove = new HashSet<int>();
            var newBlocksBySide = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { WargameSide.Blue, new List<string>() },
                { WargameSide.Red, new List<string>() },
            };
            Dictionary<string, int> nextIndexBySide = Saver_TargetList_Wargame.FindNextNumericIndexBySide(lines);

            // Zones jouées dans DCS : les formations ailleurs sont retirées de la targetlist (elles gardent leur force)
            HashSet<string> dcsZones = WargameEngineCombat.FindDcsZones(zones, campaignInfo.CombatConfig.DcsZoneDepth);

            int refreshed = 0, degraded = 0, retired = 0, parked = 0;

            foreach (WargameZoneData zone in zones)
            {
                foreach (WargameFormation formation in zone.Formations)
                {
                    if (!losses.ContainsKey(formation.FormationId))
                        continue; // jamais encore posée, laissée à WriteNewActiveFormations

                    bool moved = combat.MovedFormationIds.Contains(formation.FormationId);

                    double powerPerExemplar = catalog.GetPower(formation.Template);
                    if (powerPerExemplar <= 0) powerPerExemplar = 10.0;

                    if (formation.ForcePower <= 0)
                    {
                        if (blockByFormationId.TryGetValue(formation.FormationId, out TargetListBlock oldBlock))
                            for (int i = oldBlock.Start; i <= oldBlock.End; i++)
                                linesToRemove.Add(i);

                        retired++;
                        WargameLog.Line("repost " + formation.Name + " : exhausted, block removed");
                        continue; // épuisée (ou détruite par le combat), rien à reposer
                    }

                    if (!dcsZones.Contains(zone.Id))
                    {
                        // Hors de la zone jouée (repli, ou front qui s'est éloigné) : plus d'unités en jeu
                        RemoveOldBlock(blockByFormationId, formation, linesToRemove);
                        parked++;
                        WargameLog.Line("repost " + formation.Name + " : " + zone.Id + " not played in DCS, block removed (strength kept " + formation.ForcePower.ToString("0.0") + ")");
                        continue;
                    }

                    List<WargameTemplateUnit> layout = new Parser_WargameTemplateLayout()
                        .LoadLayout(campaignInfo.GetTemplateFilePath(formation.Template));

                    if (layout.Count == 0)
                    {
                        WargameLog.Line("WargameEngineLosses | template introuvable pour '" + formation.Name + "', repose annulée ce tour");
                        if (moved) RemoveOldBlock(blockByFormationId, formation, linesToRemove); // sinon elle resterait dans la zone perdue
                        continue; // sinon ancien bloc laissé tel quel, on retentera au prochain tour
                    }

                    double fraction = Math.Min(1.0, formation.ForcePower / powerPerExemplar);
                    layout = TrimLayout(layout, fraction);

                    // Rang parmi les formations VIVANTES du même camp (les mortes et celles de l'autre camp ne prennent pas de secteur)
                    List<WargameFormation> mates = zone.Formations.Where(x => x.ForcePower > 0 && x.Side == formation.Side).ToList();
                    int sectorIndex = Math.Max(0, mates.IndexOf(formation));
                    int sectorCount = Math.Max(1, mates.Count);

                    // Orientation : vers la zone ennemie que la formation attaque ou garde (voir AllocateForces)
                    PointF? threatPoint = FindFacingPoint(zone, formation, zones, combat);

                    List<WargamePlacedUnit> placed = WargameSpawnSolver.PlaceTemplate(zone, layout, spawnAreas, rng, threatPoint, sectorIndex, sectorCount,
                        formation.Side, zones, campaignInfo.ZoneEdgeMarginMeters);
                    if (placed == null)
                    {
                        WargameLog.Line("WargameEngineLosses | aucune position trouvée pour '" + formation.Name + "', repose annulée ce tour (réserve conservée)");
                        if (moved) RemoveOldBlock(blockByFormationId, formation, linesToRemove);
                        continue; // sinon ancien bloc laissé tel quel, on retentera au prochain tour
                    }

                    RemoveOldBlock(blockByFormationId, formation, linesToRemove);

                    formation.SpawnGeneration++;
                    string namePrefix = formation.Name + "_G" + formation.SpawnGeneration;

                    string targetSide = Saver_TargetList_Wargame.TargetTableSide(formation.Side);
                    int index = nextIndexBySide.TryGetValue(targetSide, out int n) ? n : 1;
                    nextIndexBySide[targetSide] = index + 1;

                    newBlocksBySide[targetSide].AddRange(
                        Saver_TargetList_Wargame.BuildActiveBlockLines(index, formation, placed, namePrefix));

                    if (fraction < 1.0) degraded++; else refreshed++;
                    WargameLog.Line("repost " + namePrefix + " in " + zone.Id + " : " + placed.Count + " unit(s)" + (fraction < 1.0 ? " (partial " + (fraction * 100).ToString("0") + "%)" : "")
                        + (moved ? " (moved)" : ""));
                }
            }

            if (linesToRemove.Count > 0 || newBlocksBySide.Values.Any(v => v.Count > 0))
            {
                List<string> rewritten = TargetListBlockScanner.RemoveAndInsert(lines, linesToRemove, sideCloseLine, newBlocksBySide);
                rewritten = TargetListBlockScanner.RenumberBlocks(rewritten);
                File.WriteAllLines(activePath, rewritten);
            }

            Saver_WargameZoneActive.Save(WargameZoneRepository.GetActiveWargameZonesPath(campaignName), zones);

            // ---- 4. Objectifs des zones qui ont basculé ----

            if (combat.FlippedZones.Count > 0)
            {
                MoveObjectivesOfFlippedZones(activePath, zones, combat.FlippedZones);

                // Source capturée : à 0 pour de bon (détruite par celui qui recule, inutilisable par celui qui avance)
                int zeroed = WargameLogistics.ZeroCapturedSupply(activePath, zones, combat.FlippedZones);
                if (zeroed > 0) WargameLog.Line("WargameLogistics | " + zeroed + " source(s) de ravitaillement capturée(s) mise(s) à 0");
            }

            // ---- 5. Ancien DCE : dans une zone capturée, tout ce qui est au sol et appartenait au perdant est détruit ----
            // (définitivement : une reprise plus tard ne le ressuscite pas, comme les entrepôts)
            if (combat.FlippedZones.Count > 0)
            {
                var captured = new Dictionary<WargameZoneData, string>();
                foreach (WargameZoneData z in combat.FlippedZones)
                {
                    string old;
                    if (!controlBefore.TryGetValue(z.Id, out old)) continue;
                    if (old != WargameSide.Blue && old != WargameSide.Red) continue;
                    if (string.Equals(old, z.Control, StringComparison.OrdinalIgnoreCase)) continue;
                    if (WargameEngineCombat.IsOffMapId(z.Id)) continue;
                    captured[z] = old;
                }

                foreach (string line in WargameOldDce.DestroyInCapturedZones(campaignFolder, zones, captured, WargameOldDce.ReadCampTotalTimeS(campaignFolder)))
                    WargameLog.Line("WargameEngineLosses | " + line);
            }

            // ---- 6. Points de ravitaillement avancés posés dans DCS + ordres de convois / aérien pour scriptsMod ----
            int mission = WargameLogistics.ReadMission(campaignFolder) + 1;   // la mission qui va être générée
            foreach (string line in WargameLogistics.PostPointsAndOrders(campaignName, zones, campaignInfo.CombatConfig.Convoys, mission))
                WargameLog.Line("WargameLogistics | " + line);

            WargameLog.Line("WargameEngineLosses | " + campaignName + " : " + refreshed + " rafraîchie(s), "
                                + degraded + " dégradée(s), " + retired + " épuisée(s), " + parked + " rangée(s) hors DCS"
                + (combat.FlippedZones.Count > 0 || combat.Destroyed > 0
                    ? " | combat : " + combat.FlippedZones.Count + " zone(s) basculée(s), "
                     + (combat.MovedFormationIds.Count - combat.Advanced) + " repli(s), " + combat.Advanced + " avance(s), " + combat.Destroyed + " détruite(s)"
                    : ""));
        }

        // Repose TOUTES les formations wargame dans Active/targetlist.lua d'après l'état actuel (Init + Active) :
        // tous les anciens blocs wargame sont retirés, puis chaque formation vivante d'une zone jouée dans DCS est reposée
        // (à 100 % tant qu'il lui reste au moins un exemplaire, sinon au prorata). Sert à l'export de la Preview.
        //   facing : formation -> zone ennemie vers laquelle elle est tournée (peut être vide)
        internal static string RepostAllFormations(string campaignName, Dictionary<int, string> facing)
        {
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);
            string activePath = Path.Combine(campaignFolder, "Active", "targetlist.lua");
            if (!File.Exists(activePath))
                return "Active/targetlist.lua not found : formations NOT posted";

            List<WargameZoneData> zones = WargameZoneRepository.LoadOrGenerateInit(campaignName);
            new WargameZoneActiveLoader().ApplyActiveState(WargameZoneRepository.GetActiveWargameZonesPath(campaignName), zones);

            WargameCampaignInfo campaignInfo = WargameCampaignInfo.Load(campaignName);
            WargameTemplateCatalog catalog = WargameTemplateCatalog.LoadAndSync(campaignName, campaignInfo);
            WargameSpawnAreas spawnAreas = Saver_TargetList_Wargame.LoadSpawnAreas(campaignName);
            var rng = new Random();

            // Orientation : on passe par un résultat de combat "vide" qui ne contient que les orientations
            var combat = new WargameCombatResult();
            if (facing != null)
                foreach (KeyValuePair<int, string> kv in facing) combat.FacingZoneByFormationId[kv.Key] = kv.Value;

            List<string> lines = File.ReadAllLines(activePath).ToList();

            List<TargetListBlock> blocks = TargetListBlockScanner.ScanBlocks(lines,
                (l, start, end) => Enumerable.Range(start, end - start + 1).Any(k => WargameFormationFlagRegex.IsMatch(l[k])),
                out Dictionary<string, int> sideCloseLine);

            var linesToRemove = new HashSet<int>();
            foreach (TargetListBlock block in blocks.Where(b => b.Matched))
                for (int i = block.Start; i <= block.End; i++)
                    linesToRemove.Add(i);

            var newBlocksBySide = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { WargameSide.Blue, new List<string>() },
                { WargameSide.Red, new List<string>() },
            };
            Dictionary<string, int> nextIndexBySide = Saver_TargetList_Wargame.FindNextNumericIndexBySide(lines);
            HashSet<string> dcsZones = WargameEngineCombat.FindDcsZones(zones, campaignInfo.CombatConfig.DcsZoneDepth);

            int posted = 0, failed = 0;
            var failedNames = new List<string>();

            foreach (WargameZoneData zone in zones)
            {
                if (!dcsZones.Contains(zone.Id)) continue;

                List<WargameFormation> mates = zone.Formations.Where(x => x.ForcePower > 0).ToList();
                foreach (WargameFormation formation in mates)
                {
                    double powerPerExemplar = catalog.GetPower(formation.Template);
                    if (powerPerExemplar <= 0) powerPerExemplar = 10.0;

                    List<WargameTemplateUnit> layout = new Parser_WargameTemplateLayout()
                        .LoadLayout(campaignInfo.GetTemplateFilePath(formation.Template));
                    if (layout.Count == 0) { failed++; failedNames.Add(formation.Name + " (no template)"); continue; }

                    double fraction = Math.Min(1.0, formation.ForcePower / powerPerExemplar);
                    layout = TrimLayout(layout, fraction);

                    List<WargameFormation> sameSide = mates.Where(x => x.Side == formation.Side).ToList();
                    int sectorIndex = Math.Max(0, sameSide.IndexOf(formation));
                    int sectorCount = Math.Max(1, sameSide.Count);

                    PointF? threatPoint = FindFacingPoint(zone, formation, zones, combat);

                    // Même placement que Saver_TargetList_Wargame (camp, zones voisines, marge au bord de zone)
                    List<WargamePlacedUnit> placed = WargameSpawnSolver.PlaceTemplate(zone, layout, spawnAreas, rng, threatPoint, sectorIndex, sectorCount,
                        formation.Side, zones, campaignInfo.ZoneEdgeMarginMeters);
                    if (placed == null)
                    {
                        failed++; failedNames.Add(formation.Name + " (" + zone.Id + ")");
                        WargameLog.Line("post " + formation.Name + " in " + zone.Id + " : NO POSITION FOUND, not posted in DCS");
                        continue;
                    }

                    formation.SpawnGeneration++;
                    string namePrefix = formation.Name + "_G" + formation.SpawnGeneration;

                    string targetSide = Saver_TargetList_Wargame.TargetTableSide(formation.Side);
                    int index = nextIndexBySide.TryGetValue(targetSide, out int n) ? n : 1;
                    nextIndexBySide[targetSide] = index + 1;

                    newBlocksBySide[targetSide].AddRange(Saver_TargetList_Wargame.BuildActiveBlockLines(index, formation, placed, namePrefix));
                    posted++;
                    WargameLog.Line("post " + namePrefix + " in " + zone.Id + " : " + placed.Count + " unit(s)" + (fraction < 1.0 ? " (partial " + (fraction * 100).ToString("0") + "%)" : ""));
                }
            }

            List<string> rewritten = TargetListBlockScanner.RemoveAndInsert(lines, linesToRemove, sideCloseLine, newBlocksBySide);
            rewritten = TargetListBlockScanner.RenumberBlocks(rewritten);
            File.WriteAllLines(activePath, rewritten);

            Saver_WargameZoneActive.Save(WargameZoneRepository.GetActiveWargameZonesPath(campaignName), zones);   // générations de relève

            return "targetlist : " + posted + " formation(s) posted in DCS zones"
                + (failed > 0 ? ", " + failed + " could not be placed : " + string.Join(", ", failedNames) : "");
        }

        // Flags (relus par DEBRIEF_Master.lua) et rapport du tour, réécrits à chaque tour.
        private static void WriteWargameOutputs(string campaignFolder, List<WargameZoneData> zones,
            Dictionary<string, string> controlBefore, WargameCombatResult combat)
        {
            try
            {
                string active = Path.Combine(campaignFolder, "Active");
                File.WriteAllLines(Path.Combine(active, "wargame_flags.txt"), WargameEngineCombat.BuildFlagLines(zones, combat));
                File.WriteAllLines(Path.Combine(active, "wargame_report.txt"), WargameEngineCombat.BuildReport(zones, controlBefore, combat));
            }
            catch (Exception ex)
            {
                WargameLog.Line("WargameEngineLosses | flags/rapport non écrits : " + ex.Message);
            }
        }

        // Point vers lequel la formation est tournée dans DCS : la zone ennemie qu'elle attaque ou garde.
        // Si le moteur n'en a pas retenu (ou si elle est périmée), on retombe sur la zone ennemie la plus proche.
        private static PointF? FindFacingPoint(WargameZoneData zone, WargameFormation formation,
            List<WargameZoneData> zones, WargameCombatResult combat)
        {
            string facingId;
            if (combat.FacingZoneByFormationId.TryGetValue(formation.FormationId, out facingId))
            {
                WargameZoneData facing = zones.FirstOrDefault(z => string.Equals(z.Id, facingId, StringComparison.OrdinalIgnoreCase));

                if (facing != null && facing != zone
                    && !string.Equals(facing.Control, formation.Side, StringComparison.OrdinalIgnoreCase))
                    return facing.Center;
            }

            return Saver_TargetList_Wargame.FindNearestEnemyZoneCenter(zone, formation.Side, zones);
        }

        private static void RemoveOldBlock(Dictionary<int, TargetListBlock> blockByFormationId, WargameFormation formation, HashSet<int> linesToRemove)
        {
            if (blockByFormationId.TryGetValue(formation.FormationId, out TargetListBlock oldBlock))
                for (int i = oldBlock.Start; i <= oldBlock.End; i++)
                    linesToRemove.Add(i);
        }

        // Les objectifs situés dans une zone qui vient de changer de main passent dans la
        // table de l'autre camp. Même convention que l'éditeur (WargameObjectiveWriter) :
        // on ne touche qu'aux zones bleues/rouges, jamais aux zones contestées.
        internal static void MoveObjectivesOfFlippedZones(string activePath, List<WargameZoneData> zones, List<WargameZoneData> flipped)
        {
            try
            {
                List<WargameObjective> objectives = new Parser_WargameObjectives().Load(activePath);
                if (objectives.Count == 0)
                    return;

                var desiredSides = new Dictionary<string, string>();

                foreach (WargameObjective obj in objectives)
                {
                    WargameZoneData zone = WargameEngineCombat.FindZoneContaining(zones, obj.Position);
                    if (zone == null || !flipped.Contains(zone))
                        continue;
                    if (zone.Control != WargameSide.Blue && zone.Control != WargameSide.Red)
                        continue;

                    desiredSides[obj.Name] = Saver_TargetList_Wargame.TargetTableSide(zone.Control);
                }

                if (desiredSides.Count > 0)
                    WargameObjectiveWriter.ApplyChanges(activePath, objectives, desiredSides);
            }
            catch (Exception ex)
            {
                WargameLog.Line("WargameEngineLosses | objectifs non déplacés après le combat : " + ex.Message);
            }
        }

        // Repose partielle : on garde "fraction" des unités du template. Les véhicules d'abord (valeur de combat, ils sont en tête
        // de la liste), mais TOUJOURS la première unité statique du template : c'est le "groupe principal" que scriptsMod
        // rattache au bloc (sans elle : "aucun groupe principal", bloc sans groupId - vu sur RED_080_MECH_G2 à 4 unités sur 10).
        internal static List<WargameTemplateUnit> TrimLayout(List<WargameTemplateUnit> layout, double fraction)
        {
            if (fraction >= 1.0 || layout.Count == 0)
                return layout;

            int wanted = Math.Max(1, (int)Math.Round(layout.Count * fraction));

            int main = layout.FindIndex(u => u.Category == "static");
            if (main < 0) main = 0;   // template sans statique : le premier groupe est un groupe de véhicules

            var kept = new List<WargameTemplateUnit> { layout[main] };
            for (int i = 0; i < layout.Count && kept.Count < wanted; i++)
                if (i != main) kept.Add(layout[i]);

            return kept;
        }

        private static int ReadFormationId(List<string> lines, TargetListBlock block)
        {
            for (int i = block.Start; i <= block.End; i++)
            {
                Match m = FormationIdRegex.Match(lines[i]);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int id))
                    return id;
            }
            return 0;
        }
    }
}
