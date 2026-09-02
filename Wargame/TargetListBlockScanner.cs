using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DCE_Manager
{
    // Un bloc de cible individuelle dans une targetlist, repéré par comptage de
    // profondeur d'accolades : racine(1) > camp blue/red(2) > cible(3).
    //
    // Start inclut la ligne de clé d'ouverture (["Nom"] = ou [27] =) même quand
    // elle est sur une ligne séparée de l'accolade - sans ça, un nettoyage ou un
    // déplacement de bloc laisse une en-tête orpheline derrière lui (bug réel
    // rencontré et corrigé une première fois sur les formations wargame).
    internal struct TargetListBlock
    {
        public int Start, End;
        public string Side;   // "blue" ou "red", le camp qui VISE cette cible
        public string Key;    // le nom entre crochets, ou le numéro pour un bloc Active
        public bool Matched;  // true si le critère de recherche (isMatch) a été trouvé dedans
    }

    // Balayage partagé d'un fichier targetlist (Init ou Active), utilisé par tout
    // ce qui doit repérer, retirer ou déplacer des blocs de cible : formations
    // wargame (Saver_TargetList_Wargame) et objectifs wargame
    // (Parser_WargameObjectives / WargameObjectiveWriter).
    //
    // Ne modifie jamais rien - lecture seule. Les appelants décident quoi faire
    // des blocs trouvés (retirer, patcher un champ, déplacer entre camps...).
    internal static class TargetListBlockScanner
    {
        private static readonly Regex SideKeyRegex = new Regex(@"\[""(blue|red)""\]");

        // Capture la clé d'un bloc de cible, quotée ou numérique :
        // ["Nom"] =   ou   [27] =
        private static readonly Regex EntryKeyRegex = new Regex(@"\[(?:""([^""]+)""|(\d+))\]\s*=");

        // isMatch : reçoit les lignes du bloc (Start..End inclus) et dit si ce bloc
        // correspond au critère cherché (ex: contient wargameFormation = true).
        public static List<TargetListBlock> ScanBlocks(List<string> lines, System.Func<List<string>, int, int, bool> isMatch,
            out Dictionary<string, int> sideCloseLine)
        {
            var blocks = new List<TargetListBlock>();
            sideCloseLine = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

            int depth = 0;
            int blockStart = -1;
            string pendingSide = null;
            string pendingKey = null;
            string currentSideAtDepth2 = null;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                var sideMatch = SideKeyRegex.Match(line);
                if (sideMatch.Success)
                {
                    pendingSide = sideMatch.Groups[1].Value;
                }
                else if (depth == 2)
                {
                    // Seulement juste avant l'ouverture d'un bloc de cible - une fois
                    // à l'intérieur (depth 3+), tout ["x"]/["y"]/["1"] rencontré est
                    // un champ interne, pas une nouvelle clé de bloc. Sans ce garde,
                    // pendingKey finit par contenir le dernier champ vu avant la
                    // fermeture (bug réel : tous les objectifs se nommaient "y").
                    var keyMatch = EntryKeyRegex.Match(line);
                    if (keyMatch.Success)
                        pendingKey = keyMatch.Groups[1].Success ? keyMatch.Groups[1].Value : keyMatch.Groups[2].Value;
                }

                bool opens = line.Contains("{") && !line.Contains("}");
                bool closes = line.Contains("}") && !line.Contains("{");

                if (opens)
                {
                    depth++;
                    if (depth == 2) currentSideAtDepth2 = pendingSide;

                    if (depth == 3)
                    {
                        blockStart = i;

                        // Clé sur la ligne précédente, accolade seule sur celle-ci :
                        // il faut inclure la ligne de clé dans le bloc.
                        if (line.Trim() == "{" && i > 0 && !lines[i - 1].Contains("{") && !lines[i - 1].Contains("}"))
                            blockStart = i - 1;
                    }
                }

                if (closes)
                {
                    if (depth == 3)
                    {
                        bool matched = isMatch(lines, blockStart, i);
                        blocks.Add(new TargetListBlock
                        {
                            Start = blockStart,
                            End = i,
                            Side = currentSideAtDepth2,
                            Key = pendingKey,
                            Matched = matched,
                        });
                    }

                    if (depth == 2 && currentSideAtDepth2 != null)
                        sideCloseLine[currentSideAtDepth2] = i;

                    depth--;
                }
            }

            return blocks;
        }

        // Retire les blocs indiqués (par index dans "blocks") et insère de nouvelles
        // lignes juste avant la fermeture de chaque camp indiqué dans insertBySide.
        // Les deux opérations en une passe pour ne jamais désynchroniser les index
        // de ligne entre un retrait et une insertion faits séparément.
        public static List<string> RemoveAndInsert(List<string> lines, HashSet<int> linesToRemove,
            Dictionary<string, int> sideCloseLine, Dictionary<string, List<string>> insertBySide)
        {
            var closeLineToSide = sideCloseLine
                .GroupBy(kvp => kvp.Value)
                .ToDictionary(g => g.Key, g => g.First().Key);

            var output = new List<string>(lines.Count);

            for (int i = 0; i < lines.Count; i++)
            {
                if (linesToRemove.Contains(i))
                    continue;

                if (closeLineToSide.TryGetValue(i, out string side) &&
                    insertBySide != null && insertBySide.TryGetValue(side, out List<string> toInsert) && toInsert.Count > 0)
                {
                    output.AddRange(toInsert);
                }

                output.Add(lines[i]);
            }

            return output;
        }
    }
}
