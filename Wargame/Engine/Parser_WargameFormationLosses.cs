using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DCE_Manager
{
    // Lit Active/targetlist.lua et en extrait, pour chaque bloc marqué
    // wargameFormation = true, son wargameFormationId (ancre stable, jamais
    // recyclée - voir WargameState) et son alive% courant tel que ScriptsMod
    // l'a recalculé après la dernière mission jouée.
    //
    // Lecture seule - c'est WargameEngineLosses qui décide quoi faire de ces
    // chiffres (déduire le ForcePower, reposer un exemplaire complet ou
    // partiel...). Même principe de balayage que Parser_WargameObjectives.
    internal class Parser_WargameFormationLosses
    {
        private static readonly Regex WargameFormationFlagRegex = new Regex(@"\bwargameFormation\b""?\]?\s*=\s*true");

        public Dictionary<int, double> Load(string activeTargetListPath)
        {
            var result = new Dictionary<int, double>();

            if (!File.Exists(activeTargetListPath))
                return result;

            List<string> lines = File.ReadAllLines(activeTargetListPath).ToList();

            List<TargetListBlock> blocks = TargetListBlockScanner.ScanBlocks(lines,
                (l, start, end) => Enumerable.Range(start, end - start + 1).Any(k => WargameFormationFlagRegex.IsMatch(l[k])),
                out _);

            foreach (TargetListBlock block in blocks.Where(b => b.Matched))
            {
                (int formationId, double alive) = ReadFormationIdAndAlive(lines, block);
                if (formationId <= 0) continue; // bloc mal formé (id absent) - on l'ignore plutôt que de planter

                result[formationId] = alive;
            }

            return result;
        }

        // Balayage à plat de toutes les lignes du bloc, comme Parser_WargameObjectives :
        // TryReadField est ancré en début de ligne, donc ["alive"] ne peut pas être
        // confondu avec ["alive_last"], ni avec un champ de sous-élément (qui n'a que
        // dead/lasthit/name). Pas besoin de suivre la profondeur - et vouloir le faire
        // était justement le bug : les champs du groupe sont à depth 1, jamais 0.
        private (int formationId, double alive) ReadFormationIdAndAlive(List<string> lines, TargetListBlock block)
        {
            int formationId = 0;
            double alive = 100; // si le champ manque, on suppose intacte plutôt que détruite

            for (int i = block.Start; i <= block.End; i++)
            {
                string line = lines[i];

                if (TryReadField(line, "wargameFormationId", out string idText) &&
                    int.TryParse(idText, out int id))
                    formationId = id;

                if (TryReadField(line, "alive", out string aliveText) &&
                    double.TryParse(aliveText, NumberStyles.Any, CultureInfo.InvariantCulture, out double aliveVal))
                    alive = aliveVal;
            }

            return (formationId, alive);
        }

        // Cherche un champ dans les deux styles possibles sur une même ligne :
        //   ["nom"] = valeur,     ou     nom = valeur,
        private bool TryReadField(string line, string fieldName, out string value)
        {
            value = null;

            var m = Regex.Match(line, @"^\s*(?:\[""" + Regex.Escape(fieldName) + @"""\]|" + Regex.Escape(fieldName)
                + @")\s*=\s*(.+?),?\s*$");

            if (!m.Success) return false;

            value = m.Groups[1].Value.Trim();
            return true;
        }
    }
}