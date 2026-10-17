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

        public static void ProcessBeforeMission(string campaignName)
        {
            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);
            string activePath = Path.Combine(campaignFolder, "Active", "targetlist.lua");
            if (!File.Exists(activePath))
                return; // rien encore posé, WriteNewActiveFormations s'en chargera pour la toute première fois

            Dictionary<int, double> losses = new Parser_WargameFormationLosses().Load(activePath);
            if (losses.Count == 0)
                return; // aucune formation wargame encore présente à traiter

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
                    formation.ForcePower = Math.Max(0, formation.ForcePower - lostPower);
                }
            }


            // ---- 2. Combat entre zones (axes actifs uniquement) ----

            Dictionary<string, string> controlBefore = WargameEngineCombat.SnapshotControl(zones);   // <-- AJOUTER

            WargameCombatResult combat = new WargameCombatResult();
            try
            {
                HashSet<string> activeFlags = WargameCampFlags.LoadActiveFlags(Path.Combine(campaignFolder, "Active", "camp_status.lua"));
                List<WargameAxis> axes = WargameZoneRepository.State != null ? WargameZoneRepository.State.Axes : null;

                combat = WargameEngineCombat.Resolve(zones, axes, activeFlags, campaignInfo.CombatConfig, rng, catalog);
            }
            catch (Exception ex)
            {
                // Le combat ne doit jamais empêcher la mission de se générer
                FormUtils.LogRegister("WargameEngineCombat | échec de la résolution, combat ignoré ce tour : " + ex.Message);
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
                FormUtils.LogRegister("WargameEngineLosses | échec du ravitaillement, ignoré ce tour : " + ex.Message);
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
                        continue; // épuisée (ou détruite par le combat), rien à reposer
                    }

                    if (!dcsZones.Contains(zone.Id))
                    {
                        // Hors de la zone jouée (repli, ou front qui s'est éloigné) : plus d'unités en jeu
                        RemoveOldBlock(blockByFormationId, formation, linesToRemove);
                        parked++;
                        continue;
                    }

                    List<WargameTemplateUnit> layout = new Parser_WargameTemplateLayout()
                        .LoadLayout(campaignInfo.GetTemplateFilePath(formation.Template));

                    if (layout.Count == 0)
                    {
                        FormUtils.LogRegister("WargameEngineLosses | template introuvable pour '" + formation.Name + "', repose annulée ce tour");
                        if (moved) RemoveOldBlock(blockByFormationId, formation, linesToRemove); // sinon elle resterait dans la zone perdue
                        continue; // sinon ancien bloc laissé tel quel, on retentera au prochain tour
                    }

                    double fraction = Math.Min(1.0, formation.ForcePower / powerPerExemplar);
                    if (fraction < 1.0)
                    {
                        int unitCount = Math.Max(1, (int)Math.Round(layout.Count * fraction));
                        layout = layout.Take(unitCount).ToList();
                    }

                    // Rang parmi les formations VIVANTES du même camp (les mortes et celles de l'autre camp ne prennent pas de secteur)
                    List<WargameFormation> mates = zone.Formations.Where(x => x.ForcePower > 0 && x.Side == formation.Side).ToList();
                    int sectorIndex = Math.Max(0, mates.IndexOf(formation));
                    int sectorCount = Math.Max(1, mates.Count);

                    // Orientation : vers la zone ennemie que la formation attaque ou garde (voir AllocateForces)
                    PointF? threatPoint = FindFacingPoint(zone, formation, zones, combat);

                    List<WargamePlacedUnit> placed = WargameSpawnSolver.PlaceTemplate(zone, layout, spawnAreas, rng, threatPoint, sectorIndex, sectorCount);
                    if (placed == null)
                    {
                        FormUtils.LogRegister("WargameEngineLosses | aucune position trouvée pour '" + formation.Name + "', repose annulée ce tour (réserve conservée)");
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
                MoveObjectivesOfFlippedZones(activePath, zones, combat.FlippedZones);

            FormUtils.LogRegister("WargameEngineLosses | " + campaignName + " : " + refreshed + " rafraîchie(s), "
                                + degraded + " dégradée(s), " + retired + " épuisée(s), " + parked + " rangée(s) hors DCS"
                + (combat.FlippedZones.Count > 0 || combat.Destroyed > 0
                    ? " | combat : " + combat.FlippedZones.Count + " zone(s) basculée(s), "
                     + (combat.MovedFormationIds.Count - combat.Advanced) + " repli(s), " + combat.Advanced + " avance(s), " + combat.Destroyed + " détruite(s)"
                    : ""));
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
                FormUtils.LogRegister("WargameEngineLosses | flags/rapport non écrits : " + ex.Message);
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
        private static void MoveObjectivesOfFlippedZones(string activePath, List<WargameZoneData> zones, List<WargameZoneData> flipped)
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
                FormUtils.LogRegister("WargameEngineLosses | objectifs non déplacés après le combat : " + ex.Message);
            }
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
