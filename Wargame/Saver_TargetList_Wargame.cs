using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Ecrit les formations wargame dans les targetlists.
    //
    // Deux points d'entrée bien séparés, jamais mélangés (voir échange avec Miguel) :
    //
    //  - WriteInitialFormations : appelé juste avant FirstMission.bat. Régénère
    //    l'INTEGRALITE des blocs wargame (wargameFormation = true) dans
    //    Init/targetlist_init.lua - toute trace d'un tour précédent est retirée puis
    //    réécrite depuis l'état courant de l'éditeur wargame. Le reste du fichier
    //    (cibles posées à la main par le campaignMaker) n'est jamais touché.
    //
    //  - WriteNewActiveFormations : appelé juste avant SkipMission.bat. N'ajoute que
    //    les formations qui n'ont PAS ENCORE de bloc dans Active/targetlist.lua
    //    (recherche par wargameFormationId). Les blocs déjà présents ne sont jamais
    //    retouchés - repositionner une formation qui a bougé de zone est un problème
    //    différent (Jalon 2, pas encore construit).
    //
    // Technique : ni l'un ni l'autre ne réécrit le fichier en entier. On repère les
    // blocs par comptage de profondeur d'accolades (même principe que
    // Saver_TargetList), et on insère/retire du texte brut ligne par ligne. Tout ce
    // qui n'est pas wargame reste identique au caractère près.
    internal static class Saver_TargetList_Wargame
    {
        // Valeurs par défaut appliquées à chaque cible wargame créée. Cohérentes avec
        // toutes les cibles au sol observées dans vos fichiers réels.
        private const string DefaultTask = "Strike";
        private const string DefaultClass = "static";

        private static readonly Regex WargameFormationFlagRegex = new Regex(@"\bwargameFormation\b\s*=\s*true");
        private static readonly Regex WargameFormationIdRegex = new Regex(@"wargameFormationId""?\]?\s*=\s*(\d+)");
        private static readonly Regex SideKeyRegex = new Regex(@"\[""(blue|red)""\]");
        private static readonly Regex NumericKeyRegex = new Regex(@"^\s*\[(\d+)\]\s*=");

        // -------------------- Point d'entrée 1 : Init, une seule fois --------------------

        public static void WriteInitialFormations(string campaignName)
        {
            string initPath = Path.Combine(WargameZoneRepository.GetCampaignFolder(campaignName), "Init", "targetlist_init.lua");
            if (!File.Exists(initPath))
            {
                FormUtils.LogRegister("Saver_TargetList_Wargame | targetlist_init.lua introuvable pour " + campaignName);
                return;
            }

            RemoveAllWargameBlocksFromActive(campaignName);

            List<(WargameZoneData zone, WargameFormation formation)> all = LoadAllFormations(campaignName);
            if (all.Count == 0)
                return;

            WargameCampaignInfo campaignInfo = WargameCampaignInfo.Load(campaignName);
            WargameSpawnAreas spawnAreas = LoadSpawnAreas(campaignName);
            var rng = new Random();

            // side -> lignes Lua déjà indentées, prêtes à insérer telles quelles
            var newBlocksBySide = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { WargameSide.Blue, new List<string>() },
                { WargameSide.Red, new List<string>() },
            };

            foreach (var pair in all)
            {
                WargameZoneData zone = pair.zone;
                WargameFormation formation = pair.formation;

                List<WargamePlacedUnit> placed = PlaceFormation(formation, zone, campaignInfo, spawnAreas, rng);
                if (placed == null) continue;

                if (!newBlocksBySide.TryGetValue(TargetTableSide(formation.Side), out List<string> bucket))
                {
                    FormUtils.LogRegister("Saver_TargetList_Wargame | formation '" + formation.Name + "' ignorée (camp '" + formation.Side + "' inconnu)");
                    continue;
                }

                bucket.AddRange(BuildInitBlockLines(formation, placed));
            }

            List<string> lines = File.ReadAllLines(initPath).ToList();
            List<string> rewritten = RemoveExistingWargameBlocksAndInsertNew(lines, newBlocksBySide);

            File.WriteAllLines(initPath, rewritten);

            FormUtils.LogRegister("Saver_TargetList_Wargame | targetlist_init.lua régénéré pour '" + campaignName
                + "' (" + all.Count + " formation(s) wargame)");
        }

        // Retire tout bloc wargame (wargameFormation = true) de Active/targetlist.lua,
        // sans rien y insérer. Appelé au reset (FirstMission) pour ne pas laisser
        // trainer des formations d'un essai précédent alors qu'on recommence à zéro.
        private static void RemoveAllWargameBlocksFromActive(string campaignName)
        {
            string activePath = Path.Combine(WargameZoneRepository.GetCampaignFolder(campaignName), "Active", "targetlist.lua");
            if (!File.Exists(activePath)) return;

            List<string> lines = File.ReadAllLines(activePath).ToList();
            List<FoundBlock> blocks = ScanBlocks(lines, out _);

            var linesToRemove = new HashSet<int>();
            foreach (FoundBlock b in blocks.Where(b => b.IsWargame))
                for (int i = b.Start; i <= b.End; i++)
                    linesToRemove.Add(i);

            if (linesToRemove.Count == 0) return;

            var output = new List<string>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
                if (!linesToRemove.Contains(i)) output.Add(lines[i]);

            File.WriteAllLines(activePath, output);

            FormUtils.LogRegister("Saver_TargetList_Wargame | reset : blocs wargame retirés de Active/targetlist.lua pour '" + campaignName + "'");
        }

        // -------------------- Point d'entrée 2 : Active, à chaque mission suivante --------------------

        public static void WriteNewActiveFormations(string campaignName)
        {
            string activePath = Path.Combine(WargameZoneRepository.GetCampaignFolder(campaignName), "Active", "targetlist.lua");
            if (!File.Exists(activePath))
            {
                FormUtils.LogRegister("Saver_TargetList_Wargame | Active/targetlist.lua introuvable pour " + campaignName);
                return;
            }

            List<(WargameZoneData zone, WargameFormation formation)> all = LoadAllFormations(campaignName);
            if (all.Count == 0)
                return;

            List<string> lines = File.ReadAllLines(activePath).ToList();

            HashSet<int> existingIds = FindExistingWargameFormationIds(lines);
            Dictionary<string, int> nextIndexBySide = FindNextNumericIndexBySide(lines);

            List<(WargameZoneData zone, WargameFormation formation)> missing = all
                .Where(p => !existingIds.Contains(p.formation.FormationId))
                .ToList();

            if (missing.Count == 0)
                return; // rien de nouveau, on ne touche pas au fichier

            WargameCampaignInfo campaignInfo = WargameCampaignInfo.Load(campaignName);
            WargameSpawnAreas spawnAreas = LoadSpawnAreas(campaignName);
            var rng = new Random();

            var newBlocksBySide = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { WargameSide.Blue, new List<string>() },
                { WargameSide.Red, new List<string>() },
            };

            foreach (var pair in missing)
            {
                WargameZoneData zone = pair.zone;
                WargameFormation formation = pair.formation;

                List<WargamePlacedUnit> placed = PlaceFormation(formation, zone, campaignInfo, spawnAreas, rng);
                if (placed == null) continue;

                if (!newBlocksBySide.TryGetValue(TargetTableSide(formation.Side), out List<string> bucket))
                {
                    FormUtils.LogRegister("Saver_TargetList_Wargame | formation '" + formation.Name + "' ignorée (camp '" + formation.Side + "' inconnu)");
                    continue;
                }

                string targetSide = TargetTableSide(formation.Side);
                int index = nextIndexBySide.TryGetValue(targetSide, out int n) ? n : 1;
                nextIndexBySide[targetSide] = index + 1;

                bucket.AddRange(BuildActiveBlockLines(index, formation, placed));
            }

            List<string> rewritten = InsertBlocksBeforeSideClosing(lines, newBlocksBySide);

            File.WriteAllLines(activePath, rewritten);

            FormUtils.LogRegister("Saver_TargetList_Wargame | " + missing.Count + " nouvelle(s) formation(s) ajoutée(s) à Active/targetlist.lua pour '" + campaignName + "'");
        }

        // -------------------- Placement (solveur) --------------------

        private static List<WargamePlacedUnit> PlaceFormation(WargameFormation formation, WargameZoneData zone,
            WargameCampaignInfo campaignInfo, WargameSpawnAreas spawnAreas, Random rng)
        {
            string stmPath = campaignInfo?.GetTemplateFilePath(formation.Template);
            if (string.IsNullOrEmpty(stmPath))
            {
                FormUtils.LogRegister("Saver_TargetList_Wargame | template introuvable pour la formation '" + formation.Name + "'");
                return null;
            }

            List<WargameTemplateUnit> layout = new Parser_WargameTemplateLayout().LoadLayout(stmPath);
            if (layout.Count == 0)
            {
                FormUtils.LogRegister("Saver_TargetList_Wargame | template '" + formation.Template + "' sans unité lisible (formation '" + formation.Name + "')");
                return null;
            }

            List<WargamePlacedUnit> placed = WargameSpawnSolver.PlaceTemplate(zone, layout, spawnAreas, rng);
            if (placed == null)
            {
                FormUtils.LogRegister("Saver_TargetList_Wargame | aucune position trouvée pour la formation '" + formation.Name + "' dans la zone '" + zone.Id + "'");
                return null;
            }

            return placed;
        }

        private static WargameSpawnAreas LoadSpawnAreas(string campaignName)
        {
            string spawnMizPath = WargameZoneRepository.GetSpawnMizPath(campaignName);
            return File.Exists(spawnMizPath) ? new Parser_WargameSpawnAreas().LoadFromMiz(spawnMizPath) : new WargameSpawnAreas();
        }

        private static List<(WargameZoneData zone, WargameFormation formation)> LoadAllFormations(string campaignName)
        {
            var result = new List<(WargameZoneData, WargameFormation)>();

            List<WargameZoneData> zones = WargameZoneRepository.LoadOrGenerateInit(campaignName);
            foreach (WargameZoneData zone in zones)
            {
                if (zone.Formations == null) continue;
                foreach (WargameFormation formation in zone.Formations)
                    result.Add((zone, formation));
            }

            return result;
        }

        // -------------------- Génération du texte Lua --------------------

        // Init : clés non-quotées (task = "Strike",), attributs sur une ligne compacte.
        private static List<string> BuildInitBlockLines(WargameFormation f, List<WargamePlacedUnit> placed)
        {
            (float gx, float gy) = GetCentroid(placed);
            var lines = new List<string>();

            lines.Add("\t\t[\"" + f.Name + "\"] = ");
            lines.Add("\t\t{");
            lines.Add("\t\t\ttask = \"" + DefaultTask + "\",");
            lines.Add("\t\t\tpriority = " + f.Priority + ",");
            lines.Add("\t\t\tattributes = {" + string.Join(", ", f.Attributes.Select(a => "\"" + a + "\"")) + "},");
            lines.Add("\t\t\tfirepower = ");
            lines.Add("\t\t\t{");
            lines.Add("\t\t\t\tmin = " + FormatNumber(f.FirepowerMin) + ",");
            lines.Add("\t\t\t\tmax = " + FormatNumber(f.FirepowerMax) + ",");
            lines.Add("\t\t\t},");
            lines.Add("\t\t\tATO = true,");
            lines.Add("\t\t\tclass = \"" + DefaultClass + "\",");
            lines.Add("\t\t\talive = 100,");
            lines.Add("\t\t\tx = " + FormatNumber(gx) + ",");
            lines.Add("\t\t\ty = " + FormatNumber(gy) + ",");
            lines.Add("\t\t\twargameFormationId = " + f.FormationId + ",");
            lines.Add("\t\t\twargameFormation = true,");
            lines.Add("\t\t\twargameTemplate = \"" + f.Template + ".stm\",");
            lines.Add("\t\t\telements = ");
            lines.Add("\t\t\t{");

            for (int i = 0; i < placed.Count; i++)
            {
                lines.Add("\t\t\t\t[" + (i + 1) + "] = ");
                lines.Add("\t\t\t\t{");
                lines.Add("\t\t\t\t\tname = \"" + f.Name + "-" + (i + 1) + "\",");
                lines.Add("\t\t\t\t\ttemplateUnitName = \"" + EscapeLua(placed[i].Name) + "\",");
                lines.Add("\t\t\t\t\tx = " + FormatNumber(placed[i].Position.X) + ",");
                lines.Add("\t\t\t\t\ty = " + FormatNumber(placed[i].Position.Y) + ",");
                lines.Add("\t\t\t\t\tclass = \"" + DefaultClass + "\",");
                lines.Add("\t\t\t\t},");
            }

            lines.Add("\t\t\t},");
            lines.Add("\t\t},");

            return lines;
        }

        // Active : clés quotées (["task"] = "Strike",), indexé numériquement, avec
        // titleName - même forme que les cibles réelles déjà présentes dans le fichier.
        // Active : clés quotées (["task"] = "Strike",), indexé numériquement, avec
        // titleName - même forme que les cibles réelles déjà présentes dans le fichier.
        private static List<string> BuildActiveBlockLines(int index, WargameFormation f, List<WargamePlacedUnit> placed)
        {
            (float gx, float gy) = GetCentroid(placed);
            var lines = new List<string>();

            lines.Add("\t\t[" + index + "] = ");
            lines.Add("\t\t{");
            lines.Add("\t\t\t[\"titleName\"] = \"" + f.Name + "\",");
            lines.Add("\t\t\t[\"name\"] = \"" + f.Name + "\",");
            lines.Add("\t\t\t[\"task\"] = \"" + DefaultTask + "\",");
            lines.Add("\t\t\t[\"priority\"] = " + f.Priority + ",");
            lines.Add("\t\t\t[\"attributes\"] = ");
            lines.Add("\t\t\t{");
            for (int i = 0; i < f.Attributes.Count; i++)
                lines.Add("\t\t\t\t[" + (i + 1) + "] = \"" + f.Attributes[i] + "\",");
            lines.Add("\t\t\t},");
            lines.Add("\t\t\t[\"firepower\"] = ");
            lines.Add("\t\t\t{");
            lines.Add("\t\t\t\t[\"min\"] = " + FormatNumber(f.FirepowerMin) + ",");
            lines.Add("\t\t\t\t[\"max\"] = " + FormatNumber(f.FirepowerMax) + ",");
            lines.Add("\t\t\t},");
            lines.Add("\t\t\t[\"ATO\"] = true,");
            lines.Add("\t\t\t[\"class\"] = \"" + DefaultClass + "\",");
            lines.Add("\t\t\t[\"alive\"] = 100,");
            lines.Add("\t\t\t[\"x\"] = " + FormatNumber(gx) + ",");
            lines.Add("\t\t\t[\"y\"] = " + FormatNumber(gy) + ",");
            lines.Add("\t\t\t[\"wargameFormationId\"] = " + f.FormationId + ",");
            lines.Add("\t\t\t[\"wargameFormation\"] = true,");
            lines.Add("\t\t\t[\"wargameTemplate\"] = \"" + f.Template + ".stm\",");
            lines.Add("\t\t\t[\"elements\"] = ");
            lines.Add("\t\t\t{");

            for (int i = 0; i < placed.Count; i++)
            {
                lines.Add("\t\t\t\t[" + (i + 1) + "] = ");
                lines.Add("\t\t\t\t{");
                lines.Add("\t\t\t\t\t[\"name\"] = \"" + f.Name + "-" + (i + 1) + "\",");
                lines.Add("\t\t\t\t\t[\"templateUnitName\"] = \"" + EscapeLua(placed[i].Name) + "\",");
                lines.Add("\t\t\t\t\t[\"x\"] = " + FormatNumber(placed[i].Position.X) + ",");
                lines.Add("\t\t\t\t\t[\"y\"] = " + FormatNumber(placed[i].Position.Y) + ",");
                lines.Add("\t\t\t\t\t[\"class\"] = \"" + DefaultClass + "\",");
                lines.Add("\t\t\t\t},");
            }

            lines.Add("\t\t\t},");
            lines.Add("\t\t},");

            return lines;
        }

        private static (float x, float y) GetCentroid(List<WargamePlacedUnit> placed)
        {
            float sumX = 0, sumY = 0;
            foreach (WargamePlacedUnit u in placed)
            {
                sumX += u.Position.X;
                sumY += u.Position.Y;
            }
            return (sumX / placed.Count, sumY / placed.Count);
        }

        // La table targetlist.blue contient les cibles QUE LE BLEU PEUT FRAPPER,
        // donc les unités du camp ROUGE - et inversement. La clé du tableau
        // Lua est le camp qui vise, pas le camp propriétaire de l'unité/zone.
        // Utilisée pour les formations ET pour les objectifs (même règle,
        // confirmée sur un vrai cas : OBJ_South en zone blue -> table red).
        internal static string TargetTableSide(string side)
        {
            if (side == WargameSide.Blue) return WargameSide.Red;
            if (side == WargameSide.Red) return WargameSide.Blue;
            return WargameSide.Blue; // repli, ne devrait pas arriver
        }

        private static string FormatNumber(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // Echappe les guillemets - défensif, au cas où un nom d'unité de template
        // en contiendrait (improbable mais pas interdit par DCS).
        private static string EscapeLua(string value)
        {
            return (value ?? "").Replace("\"", "\\\"");
        }

        // -------------------- Balayage du fichier (profondeur d'accolades) --------------------

        // Un bloc de cible individuelle, repéré par comptage de profondeur :
        // racine(1) > camp blue/red(2) > cible(3). Même principe que Saver_TargetList.
        private struct FoundBlock
        {
            public int Start, End;
            public bool IsWargame;
            public string Side;
        }

        private static List<FoundBlock> ScanBlocks(List<string> lines, out Dictionary<string, int> sideCloseLine)
        {
            var blocks = new List<FoundBlock>();
            sideCloseLine = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int depth = 0;
            int blockStart = -1;
            string pendingSide = null;
            string currentSideAtDepth2 = null;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                var sideMatch = SideKeyRegex.Match(line);
                if (sideMatch.Success)
                    pendingSide = sideMatch.Groups[1].Value;

                bool opens = line.Contains("{") && !line.Contains("}");
                bool closes = line.Contains("}") && !line.Contains("{");

                if (opens)
                {
                    depth++;
                    if (depth == 2) currentSideAtDepth2 = pendingSide;

                    if (depth == 3)
                    {
                        blockStart = i;

                        // Si l'accolade est seule sur sa ligne, la clé d'ouverture est sur
                        // la ligne précédente - il faut l'inclure dans le bloc, sinon un
                        // nettoyage répété laisse une en-tête orpheline derrière lui
                        // (exactement le bug qu'on vient de trouver).
                        if (line.Trim() == "{" && i > 0 && !lines[i - 1].Contains("{") && !lines[i - 1].Contains("}"))
                            blockStart = i - 1;
                    }
                }

                if (closes)
                {
                    if (depth == 3)
                    {
                        bool isWargame = false;
                        for (int k = blockStart; k <= i; k++)
                        {
                            if (WargameFormationFlagRegex.IsMatch(lines[k])) { isWargame = true; break; }
                        }

                        blocks.Add(new FoundBlock { Start = blockStart, End = i, IsWargame = isWargame, Side = currentSideAtDepth2 });
                    }

                    if (depth == 2 && currentSideAtDepth2 != null)
                        sideCloseLine[currentSideAtDepth2] = i;

                    depth--;
                }
            }

            return blocks;
        }

        // Init : retire tous les anciens blocs wargame (peu importe leur camp), puis
        // insère les blocs neufs juste avant la fermeture de chaque table de camp.
        private static List<string> RemoveExistingWargameBlocksAndInsertNew(List<string> lines, Dictionary<string, List<string>> newBlocksBySide)
        {
            List<FoundBlock> blocks = ScanBlocks(lines, out Dictionary<string, int> sideCloseLine);

            var linesToRemove = new HashSet<int>();
            foreach (FoundBlock b in blocks.Where(b => b.IsWargame))
                for (int i = b.Start; i <= b.End; i++)
                    linesToRemove.Add(i);

            return InsertBlocksBeforeSideClosing(lines, newBlocksBySide, linesToRemove, sideCloseLine);
        }

        // Active : n'ajoute rien à retirer, juste l'insertion des blocs neufs.
        private static List<string> InsertBlocksBeforeSideClosing(List<string> lines, Dictionary<string, List<string>> newBlocksBySide)
        {
            ScanBlocks(lines, out Dictionary<string, int> sideCloseLine);
            return InsertBlocksBeforeSideClosing(lines, newBlocksBySide, new HashSet<int>(), sideCloseLine);
        }

        private static List<string> InsertBlocksBeforeSideClosing(List<string> lines, Dictionary<string, List<string>> newBlocksBySide,
            HashSet<int> linesToRemove, Dictionary<string, int> sideCloseLine)
        {
            // ligne de fermeture -> camp, pour savoir quoi insérer juste avant chaque ligne
            var closeLineToSide = sideCloseLine
                .GroupBy(kvp => kvp.Value)
                .ToDictionary(g => g.Key, g => g.First().Key);

            var output = new List<string>(lines.Count);

            for (int i = 0; i < lines.Count; i++)
            {
                if (linesToRemove.Contains(i))
                    continue;

                if (closeLineToSide.TryGetValue(i, out string side) &&
                    newBlocksBySide.TryGetValue(side, out List<string> toInsert) && toInsert.Count > 0)
                {
                    output.AddRange(toInsert);
                }

                output.Add(lines[i]);
            }

            return output;
        }

        private static HashSet<int> FindExistingWargameFormationIds(List<string> lines)
        {
            var result = new HashSet<int>();

            foreach (string line in lines)
            {
                var m = WargameFormationIdRegex.Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int id))
                    result.Add(id);
            }

            return result;
        }

        // Prochain numéro libre pour chaque camp = plus grand index numérique trouvé + 1.
        private static Dictionary<string, int> FindNextNumericIndexBySide(List<string> lines)
        {
            var maxBySide = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { WargameSide.Blue, 0 },
                { WargameSide.Red, 0 },
            };

            // Un [N] = en tête de ligne, à la profondeur qui suit directement un
            // ["blue"]/["red"] connu le plus récemment, est la clé numérique d'un bloc
            // de cible pour ce camp.
            int depth = 0;
            string pendingSide = null;
            string currentSideAtDepth2 = null;

            foreach (string line in lines)
            {
                var sideMatch = SideKeyRegex.Match(line);
                if (sideMatch.Success)
                    pendingSide = sideMatch.Groups[1].Value;

                var numMatch = NumericKeyRegex.Match(line);
                if (numMatch.Success && depth == 1 && currentSideAtDepth2 != null &&
                    int.TryParse(numMatch.Groups[1].Value, out int n))
                {
                    if (n > maxBySide[currentSideAtDepth2])
                        maxBySide[currentSideAtDepth2] = n;
                }

                bool opens = line.Contains("{") && !line.Contains("}");
                bool closes = line.Contains("}") && !line.Contains("{");

                if (opens)
                {
                    depth++;
                    if (depth == 2) currentSideAtDepth2 = pendingSide;
                }
                if (closes) depth--;
            }

            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { WargameSide.Blue, maxBySide[WargameSide.Blue] + 1 },
                { WargameSide.Red, maxBySide[WargameSide.Red] + 1 },
            };
        }
    }
}
