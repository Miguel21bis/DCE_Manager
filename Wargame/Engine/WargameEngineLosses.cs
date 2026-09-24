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
    //   - lit alive% (Parser_WargameFormationLosses) pour en déduire combien de
    //     ForcePower l'exemplaire actuellement posé a perdu
    //   - tant qu'il reste au moins un exemplaire complet en réserve, repose un
    //     exemplaire tout frais (100%) - le joueur ne voit jamais la formation
    //     s'affaiblir tant que la réserve tient
    //   - en dessous d'un exemplaire complet, pose exactement ce qu'il reste
    //     (dégradation réelle et visible, proportionnelle)
    //   - à 0, ne repose rien : la formation est épuisée
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
            string activePath = Path.Combine(WargameZoneRepository.GetCampaignFolder(campaignName), "Active", "targetlist.lua");
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

            var linesToRemove = new HashSet<int>();
            var newBlocksBySide = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { WargameSide.Blue, new List<string>() },
                { WargameSide.Red, new List<string>() },
            };
            Dictionary<string, int> nextIndexBySide = Saver_TargetList_Wargame.FindNextNumericIndexBySide(lines);

            int refreshed = 0, degraded = 0, retired = 0;

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

                    if (formation.ForcePower <= 0)
                    {
                        if (blockByFormationId.TryGetValue(formation.FormationId, out TargetListBlock oldBlock))
                            for (int i = oldBlock.Start; i <= oldBlock.End; i++)
                                linesToRemove.Add(i);

                        retired++;
                        continue; // épuisée, rien à reposer
                    }

                    List<WargameTemplateUnit> layout = new Parser_WargameTemplateLayout()
                        .LoadLayout(campaignInfo.GetTemplateFilePath(formation.Template));

                    if (layout.Count == 0)
                    {
                        FormUtils.LogRegister("WargameEngineLosses | template introuvable pour '" + formation.Name + "', repose annulée ce tour");
                        continue; // ancien bloc laissé tel quel, on retentera au prochain tour
                    }

                    double fraction = Math.Min(1.0, formation.ForcePower / powerPerExemplar);
                    if (fraction < 1.0)
                    {
                        int unitCount = Math.Max(1, (int)Math.Round(layout.Count * fraction));
                        layout = layout.Take(unitCount).ToList();
                    }

                    int sectorIndex = zone.Formations.IndexOf(formation);
                    int sectorCount = zone.Formations.Count;
                    PointF? threatPoint = Saver_TargetList_Wargame.FindNearestEnemyZoneCenter(zone, formation.Side, zones);

                    List<WargamePlacedUnit> placed = WargameSpawnSolver.PlaceTemplate(zone, layout, spawnAreas, rng, threatPoint, sectorIndex, sectorCount);
                    if (placed == null)
                    {
                        FormUtils.LogRegister("WargameEngineLosses | aucune position trouvée pour '" + formation.Name + "', repose annulée ce tour (réserve conservée)");
                        continue; // ancien bloc laissé tel quel, on retentera au prochain tour
                    }

                    if (blockByFormationId.TryGetValue(formation.FormationId, out TargetListBlock replacedBlock))
                        for (int i = replacedBlock.Start; i <= replacedBlock.End; i++)
                            linesToRemove.Add(i);

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

            FormUtils.LogRegister("WargameEngineLosses | " + campaignName + " : " + refreshed + " rafraîchie(s), "
                + degraded + " dégradée(s), " + retired + " épuisée(s)");
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