using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Applique au Save : le patch des 4 champs édités (priority/attributes/
    // firepower min/max) et le déplacement d'un objectif vers le camp adverse de
    // celui qui contrôle sa zone (même convention inversée que les formations :
    // une zone tenue par le rouge -> objectif dans la table blue).
    //
    // Le déplacement recopie le bloc BRUT tel quel (RawLines) - aucune ligne
    // interne n'est réécrite pendant un déplacement, seule sa position dans le
    // fichier change. Les zones Contested ne déplacent jamais rien : ambiguïté
    // volontairement non gérée (choix explicite du campaignMaker).
    internal static class WargameObjectiveWriter
    {
        public static void ApplyChanges(string pathFile, List<WargameObjective> objectives, Dictionary<string, string> desiredSideByName)
        {
            if (objectives.Count == 0)
                return;

            if (!File.Exists(pathFile))
            {
                FormUtils.LogRegister("WargameObjectiveWriter | fichier introuvable, objectifs non écrits : " + pathFile);
                MessageBox.Show("Cannot save objectives: file not found.\n\n" + pathFile,
                    "Wargame", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> lines = File.ReadAllLines(pathFile).ToList();

            // Le fichier a pu être réécrit juste avant (formations wargame, voir SaveZones) :
            // les BlockStart/BlockEnd mémorisés dans chaque obj au chargement du formulaire
            // ne correspondent donc plus forcément aux lignes actuelles (décalage, voire
            // IndexOutOfRange si le fichier a raccourci). On re-scanne le fichier TEL QU'IL
            // EST MAINTENANT et on retrouve chaque bloc par sa clé + son camp, au lieu de
            // faire confiance à des indices figés.
            List<TargetListBlock> blocks = TargetListBlockScanner.ScanBlocks(lines, (l, s, e) => true, out Dictionary<string, int> sideCloseLine);

            // 1) Patch des champs édités, en place, sans toucher au reste du bloc
            foreach (WargameObjective obj in objectives)
            {
                TargetListBlock currentBlock = blocks.FirstOrDefault(b => b.Key == obj.RawKey && b.Side == obj.CurrentSide);
                if (currentBlock.Key == null)
                {
                    FormUtils.LogRegister("WargameObjectiveWriter | bloc introuvable pour l'objectif '" + obj.Name + "' (fichier changé entre-temps ?), patch ignoré");
                    continue;
                }

                PatchFields(lines, obj, currentBlock.Start, currentBlock.End);
            }

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
        }

        private static void PatchFields(List<string> lines, WargameObjective obj, int blockStart, int blockEnd)
        {
            for (int i = blockStart; i <= blockEnd; i++)
            {
                lines[i] = PatchFieldOnLine(lines[i], "priority", obj.Priority.ToString());

                if (Regex.IsMatch(lines[i], @"^\s*(?:\[""attributes""\]|attributes)\s*=\s*\{"))
                {
                    string inline = "{" + string.Join(", ", obj.Attributes.Select(a => "\"" + a + "\"")) + "}";
                    lines[i] = PatchFieldOnLine(lines[i], "attributes", inline);
                }
            }

            PatchFirepowerField(lines, blockStart, blockEnd, "min", obj.FirepowerMin);
            PatchFirepowerField(lines, blockStart, blockEnd, "max", obj.FirepowerMax);
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
