using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Applique au Save : le patch des champs édités (priority/attributes/firepower min/max,
    // et les champs de ravitaillement wargameSupply / wargameBridge) et le déplacement d'un objectif vers le camp adverse de
    // celui qui contrôle sa zone (même convention inversée que les formations :
    // une zone tenue par le rouge -> objectif dans la table blue).
    //
    // Le déplacement recopie le bloc BRUT tel quel (RawLines) - aucune ligne
    // interne n'est réécrite pendant un déplacement, seule sa position dans le
    // fichier change. Les zones Contested ne déplacent jamais rien : ambiguïté
    // volontairement non gérée (choix explicite du campaignMaker).
    internal static class WargameObjectiveWriter
    {
        // Renvoie la liste des PROBLEMES constatés (liste vide = tout est écrit et relu correctement).
        // Après l'écriture, le fichier est relu et comparé à ce que l'éditeur voulait écrire : un champ
        // qui n'a pas pu être écrit (ligne repère absente, bloc introuvable...) est donc signalé,
        // au lieu d'être ignoré en silence.
        public static List<string> ApplyChanges(string pathFile, List<WargameObjective> objectives, Dictionary<string, string> desiredSideByName)
        {
            var problems = new List<string>();

            if (objectives.Count == 0)
                return problems;

            if (!File.Exists(pathFile))
            {
                FormUtils.LogRegister("WargameObjectiveWriter | fichier introuvable, objectifs non écrits : " + pathFile);
                problems.Add("File not found: " + pathFile);
                return problems;
            }

            List<string> lines = File.ReadAllLines(pathFile).ToList();

            // Le fichier a pu être réécrit juste avant (formations wargame, voir SaveZones) :
            // les BlockStart/BlockEnd mémorisés dans chaque obj au chargement du formulaire
            // ne correspondent donc plus forcément aux lignes actuelles (décalage, voire
            // IndexOutOfRange si le fichier a raccourci). On re-scanne le fichier TEL QU'IL
            // EST MAINTENANT et on retrouve chaque bloc par sa clé + son camp, au lieu de
            // faire confiance à des indices figés.
            List<TargetListBlock> blocks = TargetListBlockScanner.ScanBlocks(lines, (l, s, e) => true, out Dictionary<string, int> sideCloseLine);

            // 1) Patch des champs édités, en place, sans toucher au reste du bloc.
            // Du bas du fichier vers le haut : l'ajout ou le retrait d'une ligne (wargameSupply / wargameBridge)
            // ne décale alors jamais les blocs encore à traiter.
            var work = new List<KeyValuePair<WargameObjective, TargetListBlock>>();

            foreach (WargameObjective obj in objectives)
            {
                TargetListBlock currentBlock = blocks.FirstOrDefault(b => b.Key == obj.RawKey && b.Side == obj.CurrentSide);
                if (currentBlock.Key == null)
                {
                    FormUtils.LogRegister("WargameObjectiveWriter | bloc introuvable pour l'objectif '" + obj.Name + "' (fichier changé entre-temps ?), patch ignoré");
                    problems.Add(obj.Name + " : block not found in the file (changed in the meantime?), nothing written");
                    continue;
                }

                work.Add(new KeyValuePair<WargameObjective, TargetListBlock>(obj, currentBlock));
            }

            bool linesChanged = false;

            foreach (KeyValuePair<WargameObjective, TargetListBlock> item in work.OrderByDescending(w => w.Value.Start))
            {
                int blockEnd = item.Value.End;

                if (PatchFields(lines, item.Key, item.Value.Start, ref blockEnd))
                    linesChanged = true;

                if (PatchSupplyLines(lines, item.Key, item.Value.Start, ref blockEnd))
                    linesChanged = true;
            }

            // Des lignes ont été ajoutées ou retirées : les indices des blocs ne sont plus bons, on re-scanne
            if (linesChanged)
                blocks = TargetListBlockScanner.ScanBlocks(lines, (l, s, e) => true, out sideCloseLine);

            // 2) Déplacements de camp, en une seule passe (retrait + insertion)
            var linesToRemove = new HashSet<int>();
            var insertBySide = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase)
    {
        { "blue", new List<string>() },
        { "red", new List<string>() },
    };

            int moved = 0;

            var nextIndexBySide = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

            foreach (WargameObjective obj in objectives)
            {
                if (!desiredSideByName.TryGetValue(obj.Name, out string desiredSide) || string.IsNullOrEmpty(desiredSide))
                    continue; // zone Contested ou introuvable : on ne touche à rien

                if (string.Equals(desiredSide, obj.CurrentSide, System.StringComparison.OrdinalIgnoreCase))
                    continue; // déjà dans le bon camp

                TargetListBlock block = blocks.FirstOrDefault(b => b.Key == obj.RawKey && b.Side == obj.CurrentSide);
                if (block.Key == null) continue; // bloc introuvable (fichier changé entre-temps ?), on ignore prudemment

                for (int i = block.Start; i <= block.End; i++)
                    linesToRemove.Add(i);

                List<string> blockLines = lines.GetRange(block.Start, block.End - block.Start + 1);

                // Active : la clé est un numéro propre à SA table d'origine. La table
                // de destination a sa propre numérotation, indépendante - coller le
                // même numéro créerait une collision silencieuse (la 2e entrée écrase
                // la 1re au chargement Lua). On réattribue donc un numéro libre côté
                // destination. En Init, la clé est un nom unique au fichier entier :
                // rien à renuméroter.
                if (int.TryParse(block.Key, out _))
                {
                    if (!nextIndexBySide.TryGetValue(desiredSide, out int nextIndex))
                        nextIndex = GetMaxNumericKey(blocks, desiredSide) + 1;

                    blockLines[0] = RenumberOpeningLine(blockLines[0], nextIndex);
                    nextIndexBySide[desiredSide] = nextIndex + 1;
                }

                insertBySide[desiredSide].AddRange(blockLines);
                moved++;
            }

            List<string> output = moved > 0
                ? TargetListBlockScanner.RemoveAndInsert(lines, linesToRemove, sideCloseLine, insertBySide)
                : lines;

            File.WriteAllLines(pathFile, output);

            if (moved > 0)
                FormUtils.LogRegister("WargameObjectiveWriter | " + moved + " objectif(s) déplacé(s) de camp dans " + Path.GetFileName(pathFile));

            problems.AddRange(VerifyWrittenFile(pathFile, objectives));

            foreach (string problem in problems)
                FormUtils.LogRegister("WargameObjectiveWriter | PROBLEME : " + problem);

            return problems;
        }

        // Relit le fichier qui vient d'être écrit et compare, objectif par objectif, avec ce que l'éditeur
        // voulait y mettre. C'est la seule façon d'être sûr que la sauvegarde a bien eu lieu.
        private static List<string> VerifyWrittenFile(string pathFile, List<WargameObjective> expected)
        {
            var problems = new List<string>();
            List<WargameObjective> reread = new Parser_WargameObjectives().Load(pathFile);

            foreach (WargameObjective want in expected)
            {
                WargameObjective got = reread.FirstOrDefault(o => o.Name == want.Name);

                if (got == null)
                {
                    problems.Add(want.Name + " : not found when reading the file back (wargameObjective flag lost?)");
                    continue;
                }

                var diffs = new List<string>();

                if (got.Priority != want.Priority) diffs.Add("priority (file " + got.Priority + ", wanted " + want.Priority + ")");
                if (System.Math.Abs(got.SupplyValue - want.SupplyValue) > 0.01) diffs.Add("supply (file " + got.SupplyValue + ", wanted " + want.SupplyValue + ")");
                if (got.IsBridge != want.IsBridge) diffs.Add("bridge (file " + got.IsBridge + ", wanted " + want.IsBridge + ")");
                if (System.Math.Abs(got.FirepowerMin - want.FirepowerMin) > 0.01) diffs.Add("FP min (file " + got.FirepowerMin + ", wanted " + want.FirepowerMin + ")");
                if (System.Math.Abs(got.FirepowerMax - want.FirepowerMax) > 0.01) diffs.Add("FP max (file " + got.FirepowerMax + ", wanted " + want.FirepowerMax + ")");
                // Liste vide voulue : le parser relit "Vehicles" par défaut sur une table inline vide, ce n'est pas une erreur
                bool emptyWanted = want.Attributes.Count == 0 && got.Attributes.Count <= 1;

                if (!emptyWanted && !got.Attributes.SequenceEqual(want.Attributes)) diffs.Add("attributes (file " + string.Join(",", got.Attributes) + ", wanted " + string.Join(",", want.Attributes) + ")");

                if (diffs.Count > 0)
                    problems.Add(want.Name + " : not saved -> " + string.Join("; ", diffs));
            }

            return problems;
        }

        // Renvoie true si des lignes ont été ajoutées ou retirées (table d'attributs multi-lignes réécrite).
        private static bool PatchFields(List<string> lines, WargameObjective obj, int blockStart, ref int blockEnd)
        {
            bool changed = false;

            for (int i = blockStart; i <= blockEnd; i++)
            {
                lines[i] = PatchFieldOnLine(lines[i], "priority", obj.Priority.ToString());

                if (Regex.IsMatch(lines[i], @"^\s*(?:\[""attributes""\]|attributes)\s*=\s*\{"))
                {
                    string inline = "{" + string.Join(", ", obj.Attributes.Select(a => "\"" + a + "\"")) + "}";
                    lines[i] = PatchFieldOnLine(lines[i], "attributes", inline);
                }
            }

            // Format Active : la table d'attributs s'étend sur plusieurs lignes
            changed |= PatchAttributesTable(lines, obj, blockStart, ref blockEnd);

            PatchFirepowerField(lines, blockStart, blockEnd, "min", obj.FirepowerMin);
            PatchFirepowerField(lines, blockStart, blockEnd, "max", obj.FirepowerMax);

            return changed;
        }

        // ["attributes"] =
        // {
        //     [1] = "Bridge",
        //     [2] = "Structure",
        // },
        // Seul le contenu entre les accolades est remplacé ; l'en-tête et la fermeture restent tels quels.
        private static bool PatchAttributesTable(List<string> lines, WargameObjective obj, int blockStart, ref int blockEnd)
        {
            var header = new Regex(@"^\s*(?:\[""attributes""\]|attributes)\s*=\s*$");

            for (int i = blockStart; i <= blockEnd; i++)
            {
                if (!header.IsMatch(lines[i]))
                    continue;

                int open = i + 1;
                if (open > blockEnd || !lines[open].Contains("{") || lines[open].Contains("}"))
                    return false;   // pas une table sur plusieurs lignes : on n'y touche pas

                int close = -1;
                for (int k = open + 1; k <= blockEnd; k++)
                {
                    if (lines[k].Contains("}")) { close = k; break; }
                }

                if (close < 0)
                    return false;

                // Déjà identique : on ne réécrit rien
                List<string> current = Parser_WargameObjectives.ReadAttributesTable(lines, i, blockEnd);
                if (current.SequenceEqual(obj.Attributes))
                    return false;

                Match indentMatch = Regex.Match(lines[open], @"^(\s*)");
                string entryIndent = indentMatch.Groups[1].Value + "\t";

                var newEntries = new List<string>();
                for (int n = 0; n < obj.Attributes.Count; n++)
                    newEntries.Add(entryIndent + "[" + (n + 1) + "] = \"" + obj.Attributes[n] + "\",");

                int oldCount = close - open - 1;
                lines.RemoveRange(open + 1, oldCount);
                lines.InsertRange(open + 1, newEntries);

                int delta = newEntries.Count - oldCount;
                blockEnd += delta;
                return delta != 0;
            }

            return false;
        }

        // Champs de ravitaillement : wargameSupply = N (si N > 0) et wargameBridge = true (si pont).
        // Ligne déjà là -> sa valeur est mise à jour ; ligne absente -> ajoutée juste sous "wargameObjective = true",
        // dans le même style (avec ou sans crochets) ; valeur à 0 / pas un pont -> ligne retirée.
        // Renvoie true si une ligne a été ajoutée ou retirée (les indices du fichier ont alors bougé).
        private static bool PatchSupplyLines(List<string> lines, WargameObjective obj, int blockStart, ref int blockEnd)
        {
            bool changed = false;

            string supplyText = obj.SupplyValue.ToString("0.##", CultureInfo.InvariantCulture);
            changed |= SetOrRemoveLine(lines, blockStart, ref blockEnd, "wargameSupply", obj.SupplyValue > 0 ? supplyText : null);
            changed |= SetOrRemoveLine(lines, blockStart, ref blockEnd, "wargameBridge", obj.IsBridge ? "true" : null);

            return changed;
        }

        private static bool SetOrRemoveLine(List<string> lines, int blockStart, ref int blockEnd, string fieldName, string newValue)
        {
            var fieldRegex = new Regex(@"^\s*(?:\[""" + Regex.Escape(fieldName) + @"""\]|" + Regex.Escape(fieldName) + @")\s*=");
            int existing = -1;
            int flagLine = -1;

            for (int i = blockStart; i <= blockEnd; i++)
            {
                if (existing < 0 && fieldRegex.IsMatch(lines[i])) existing = i;
                if (flagLine < 0 && Regex.IsMatch(lines[i], @"^\s*(?:\[""wargameObjective""\]|wargameObjective)\s*=\s*true")) flagLine = i;
            }

            if (existing >= 0)
            {
                if (newValue == null)
                {
                    lines.RemoveAt(existing);
                    blockEnd--;
                    return true;
                }

                lines[existing] = PatchFieldOnLine(lines[existing], fieldName, newValue);
                return false;
            }

            if (newValue == null)
                return false;   // rien à écrire

            int insertAfter;
            string indent;
            bool quoted;

            if (flagLine >= 0)
            {
                // Même indentation et même style que la ligne "wargameObjective = true"
                Match m = Regex.Match(lines[flagLine], @"^(\s*)(\[""wargameObjective""\]|wargameObjective)");
                indent = m.Groups[1].Value;
                quoted = m.Groups[2].Value.StartsWith("[");
                insertAfter = flagLine;
            }
            else
            {
                // Repli : la ligne repère n'a pas le format attendu (ex. flag collé à d'autres champs sur la même ligne).
                // On insère juste sous l'accolade ouvrante du bloc, dans le style du champ qui la suit.
                int openLine = -1;
                for (int i = blockStart; i <= blockEnd; i++)
                {
                    if (lines[i].Contains("{")) { openLine = i; break; }
                }

                if (openLine < 0 || openLine + 1 > blockEnd)
                    return false;

                string next = lines[openLine + 1];
                indent = Regex.Match(next, @"^(\s*)").Groups[1].Value;
                quoted = Regex.IsMatch(next, @"^\s*\[""");
                insertAfter = openLine;
            }

            string key = quoted ? "[\"" + fieldName + "\"]" : fieldName;

            lines.Insert(insertAfter + 1, indent + key + " = " + newValue + ",");
            blockEnd++;
            return true;
        }

        // Remplace la VALEUR d'un champ en conservant son style d'origine (quoté ou
        // non) - ne touche jamais à l'indentation ni au reste de la ligne.
        private static string PatchFieldOnLine(string line, string fieldName, string newValue)
        {
            var m = Regex.Match(line, @"^(\s*)(\[""" + Regex.Escape(fieldName) + @"""\]|" + Regex.Escape(fieldName)
                + @")(\s*=\s*).+?(,?\s*)$");

            if (!m.Success) return line;

            return m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + newValue + m.Groups[4].Value;
        }

        private static void PatchFirepowerField(List<string> lines, int blockStart, int blockEnd, string subFieldName, double value)
        {
            bool inFirepower = false;

            for (int i = blockStart; i <= blockEnd; i++)
            {
                if (!inFirepower)
                {
                    if (Regex.IsMatch(lines[i], @"^\s*(?:\[""firepower""\]|firepower)\s*=\s*\{") ||
                        Regex.IsMatch(lines[i], @"^\s*(?:\[""firepower""\]|firepower)\s*=\s*$"))
                        inFirepower = true;

                    continue;
                }

                if (lines[i].Contains("}"))
                    return; // sous-bloc fermé sans trouver le champ, abandon silencieux

                if (Regex.IsMatch(lines[i], @"^\s*(?:\[""" + subFieldName + @"""\]|" + subFieldName + @")\s*="))
                {
                    lines[i] = PatchFieldOnLine(lines[i], subFieldName,
                        value.ToString(CultureInfo.InvariantCulture));
                    return;
                }
            }
        }
        private static int GetMaxNumericKey(List<TargetListBlock> blocks, string side)
        {
            int max = 0;
            foreach (TargetListBlock b in blocks)
            {
                if (string.Equals(b.Side, side, System.StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(b.Key, out int n) && n > max)
                    max = n;
            }
            return max;
        }

        // Remplace le [N] en tête de la ligne d'ouverture du bloc par un nouveau
        // numéro, sans toucher au reste de la ligne (espaces, "= " ou "= {"...).
        private static string RenumberOpeningLine(string line, int newIndex)
        {
            return Regex.Replace(line, @"\[\d+\]", "[" + newIndex + "]", RegexOptions.None);
        }


    }
}
