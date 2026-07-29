using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Met à jour le conf_mod.lua d'une campagne en comparant sa structure à celle du
    // fichier de référence UTIL_ConfModCheck.lua (mission_ini_check, etc.).
    //
    // Principe simple : on reconstruit chaque bloc dans l'ordre de la référence.
    // - si la clé existe déjà côté client, on garde SA valeur, mais on prend la ligne
    //   (commentaire, tag @ui...) de la référence
    // - si la clé n'existe pas côté client, on copie la ligne de la référence telle quelle
    // - une clé côté client qui n'est plus dans la référence disparaît (nettoyage automatique)
    // - sauf "movedBullseye" et "pictureBrief", toujours préservés tels quels
    //
    // Tout le reste du fichier (pictureBrief en dehors des 4 blocs, mise en forme, etc.)
    // n'est jamais touché.
    internal class ConfModTemplateUpdater
    {
        private static readonly string[] ProtectedKeys = { "movedBullseye", "pictureBrief" };

        // correspondance nom du bloc dans la référence -> nom du bloc côté client
        private static readonly string[,] BlockPairs =
        {
            { "mission_ini_check", "mission_ini" },
            { "mission_forcedOptions_check", "mission_forcedOptions" },
            { "Debug_check", "Debug" },
            { "campMod_check", "campMod" },
        };

        private readonly ConfModLoader _loader = new ConfModLoader();

        public string GetReferencePath()
        {
            return Path.Combine(
                ParamConf.PATH_SavedGames_DCS,
                @"Mods\tech\DCE\ScriptsMod.NG",
                "UTIL_ConfModCheck.lua");
        }

        // Met à jour une seule campagne. Appelée à l'ouverture d'une campagne
        // (Config, génération de mission) ou en boucle par UpdateAllCampaigns.
        public void UpdateCampaign(string campaignName)
        {
            string localPath = _loader.GetConfModPath(campaignName);
            string refPath = GetReferencePath();

            if (!File.Exists(localPath))
            {
                FormUtils.LogRegister("ConfModTemplateUpdater | conf_mod.lua introuvable pour " + campaignName);
                return;
            }

            if (!File.Exists(refPath))
            {
                FormUtils.LogRegister("ConfModTemplateUpdater | UTIL_ConfModCheck.lua introuvable : " + refPath);
                return;
            }

            List<string> localLines = new List<string>(File.ReadAllLines(localPath));
            List<string> refLines = new List<string>(File.ReadAllLines(refPath));

            bool changed = false;

            for (int i = 0; i < BlockPairs.GetLength(0); i++)
            {
                string refKey = BlockPairs[i, 0];
                string localKey = BlockPairs[i, 1];

                int refStart, refEnd;

                if (!TryFindBlock(refLines, refKey, 0, refLines.Count - 1, out refStart, out refEnd))
                    continue; // la référence n'a pas (ou plus) ce bloc, rien à faire

                int localStart, localEnd;
                bool localExists = TryFindBlock(localLines, localKey, 0, localLines.Count - 1, out localStart, out localEnd);

                int cursor = refStart + 1;
                List<string> merged = MergeBlockBody(refLines, ref cursor, localLines, localStart, localEnd, localExists);

                if (localExists)
                {
                    int oldInnerCount = localEnd - localStart - 1;
                    localLines.RemoveRange(localStart + 1, oldInnerCount);
                    localLines.InsertRange(localStart + 1, merged);
                }
                else
                {
                    // la campagne n'a jamais eu ce bloc du tout : on l'ajoute en entier à la fin
                    localLines.Add(localKey + " = {");
                    localLines.AddRange(merged);
                    localLines.Add("}");
                }

                changed = true;
            }

            if (changed)
            {
                File.WriteAllLines(localPath, localLines.ToArray());
                _loader.InvalidateCache(campaignName);
                FormUtils.LogRegister("ConfModTemplateUpdater | conf_mod.lua mis à jour pour " + campaignName);
            }
        }

        // Pour le bouton "Update conf_mod" : refait la même chose sur toutes les campagnes.
        public void UpdateAllCampaigns(IEnumerable<string> campaignNames)
        {
            foreach (string name in campaignNames)
                UpdateCampaign(name);
        }

        // ---------------------------------------------------------------
        // Reconstruction récursive d'un bloc, dans l'ordre de la référence.
        // refIndex pointe sur la première ligne à l'intérieur du bloc (juste après
        // le "{"), et se retrouve, à la sortie, sur la ligne de fermeture "}".
        // ---------------------------------------------------------------

        private List<string> MergeBlockBody(List<string> refLines, ref int refIndex, List<string> localLines, int localStart, int localEnd, bool localExists)
        {
            var output = new List<string>();
            var handledKeys = new HashSet<string>();

            while (refIndex < refLines.Count)
            {
                string refLine = refLines[refIndex];
                string code = ConfUiSchemaParser.StripComment(refLine);

                if (IsCloseLine(code))
                    break; // on laisse l'appelant gérer la ligne de fermeture

                Match openMatch = ContainerOpenRegex.Match(code);

                if (openMatch.Success)
                {
                    string subKey = openMatch.Groups["bkey"].Success ? openMatch.Groups["bkey"].Value : openMatch.Groups["key"].Value;
                    handledKeys.Add(subKey);

                    int localSubStart = -1, localSubEnd = -1;
                    bool localSubExists = localExists && TryFindBlock(localLines, subKey, localStart, localEnd, out localSubStart, out localSubEnd);

                    output.Add(refLine);

                    refIndex++;
                    List<string> inner = MergeBlockBody(refLines, ref refIndex, localLines, localSubStart, localSubEnd, localSubExists);
                    output.AddRange(inner);

                    output.Add(refLines[refIndex]); // ligne de fermeture du sous-bloc, prise dans la référence
                    refIndex++;
                    continue;
                }

                Match valueMatch = ValueLineRegex.Match(code);

                if (valueMatch.Success)
                {
                    string key = valueMatch.Groups["bkey"].Success ? valueMatch.Groups["bkey"].Value : valueMatch.Groups["key"].Value;
                    handledKeys.Add(key);

                    string mergedLine = refLine;

                    if (localExists)
                    {
                        string localToken = FindValueToken(localLines, localStart, localEnd, key);

                        if (localToken != null)
                            mergedLine = ReplaceValueToken(refLine, key, localToken);
                    }

                    output.Add(mergedLine);
                    refIndex++;
                    continue;
                }

                // commentaire ou ligne vide : on garde celle de la référence
                output.Add(refLine);
                refIndex++;
            }

            // Clés protégées (movedBullseye, pictureBrief) : jamais dans la référence,
            // toujours recopiées telles quelles si elles existent côté client.
            if (localExists)
            {
                foreach (string protectedKey in ProtectedKeys)
                {
                    if (handledKeys.Contains(protectedKey))
                        continue;

                    List<string> protectedLines = ExtractWholeEntry(localLines, localStart, localEnd, protectedKey);

                    if (protectedLines != null)
                        output.AddRange(protectedLines);
                }
            }

            return output;
        }

        // ---------------------------------------------------------------
        // Petits outils texte, volontairement simples et autonomes (pas de
        // dépendance vers les regex privées de ConfModWriter).
        // ---------------------------------------------------------------

        private static readonly Regex ContainerOpenRegex =
            new Regex(@"^\s*(?:\[""(?<bkey>[^""]+)""\]|(?<key>[A-Za-z_][A-Za-z0-9_]*))\s*=\s*{\s*$");

        // valeur = chaîne entre guillemets, OU tableau { ... } sur une seule ligne, OU jeton simple
        private static readonly Regex ValueLineRegex =
            new Regex(@"^(\s*(?:\[""(?<bkey>[^""]+)""\]|(?<key>[A-Za-z_][A-Za-z0-9_]*))\s*=\s*)(""[^""]*""|\{[^{}]*\}|[^\s,]+)(.*)$");

        private static bool IsCloseLine(string code)
        {
            return Regex.IsMatch(code, @"^\s*}\s*,?\s*$");
        }

        // Cherche "key = {" dans [start, end] et sa fermeture, en suivant la
        // profondeur des accolades (pas juste la première "}" rencontrée).
        private static bool TryFindBlock(List<string> lines, string key, int start, int end, out int blockStart, out int blockEnd)
        {
            blockStart = -1;
            blockEnd = -1;

            var openRegex = new Regex(@"^\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*{");

            for (int i = start; i <= end; i++)
            {
                if (openRegex.IsMatch(ConfUiSchemaParser.StripComment(lines[i])))
                {
                    blockStart = i;
                    break;
                }
            }

            if (blockStart < 0)
                return false;

            int depth = CountBraces(ConfUiSchemaParser.StripComment(lines[blockStart]));

            for (int i = blockStart + 1; i <= end; i++)
            {
                depth += CountBraces(ConfUiSchemaParser.StripComment(lines[i]));

                if (depth <= 0)
                {
                    blockEnd = i;
                    return true;
                }
            }

            return false;
        }

        private static int CountBraces(string line)
        {
            int depth = 0;

            foreach (char c in line)
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }

            return depth;
        }

        // Retourne la valeur brute (texte, telle quelle) d'une clé trouvée dans [start, end].
        private static string FindValueToken(List<string> lines, int start, int end, string key)
        {
            if (start < 0 || end < 0)
                return null;

            var regex = new Regex(@"^(\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*)(""[^""]*""|\{[^{}]*\}|[^\s,]+)(.*)$");

            for (int i = start; i <= end; i++)
            {
                Match m = regex.Match(lines[i]);

                if (m.Success)
                    return m.Groups[2].Value;
            }

            return null;
        }

        // Remplace juste le jeton de valeur d'une ligne, en gardant tout le reste
        // (indentation, clé, commentaire) intact.
        private static string ReplaceValueToken(string line, string key, string newToken)
        {
            var regex = new Regex(@"^(\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*)(""[^""]*""|\{[^{}]*\}|[^\s,]+)(.*)$");
            Match m = regex.Match(line);

            if (!m.Success)
                return line;

            return m.Groups[1].Value + newToken + m.Groups[3].Value;
        }

        // Récupère telles quelles les lignes d'une clé protégée (ligne simple, ou bloc
        // entier si c'est une table comme campMod.movedBullseye).
        private static List<string> ExtractWholeEntry(List<string> lines, int start, int end, string key)
        {
            int blockStart, blockEnd;

            if (TryFindBlock(lines, key, start, end, out blockStart, out blockEnd))
            {
                var result = new List<string>();

                for (int i = blockStart; i <= blockEnd; i++)
                    result.Add(lines[i]);

                return result;
            }

            string token = FindValueToken(lines, start, end, key);

            if (token == null)
                return null;

            var regex = new Regex(@"^\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=");

            for (int i = start; i <= end; i++)
            {
                if (regex.IsMatch(ConfUiSchemaParser.StripComment(lines[i])))
                    return new List<string> { lines[i] };
            }

            return null;
        }
    }
}
