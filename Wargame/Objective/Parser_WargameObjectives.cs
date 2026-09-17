using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DCE_Manager
{
    // Un objectif de campagne (hors wargame à l'origine - bases, entrepôts...) que
    // le wargame doit pouvoir positionner sur la carte et faire suivre le camp de
    // la zone qui le contient. Repéré par wargameObjective = true dans son bloc,
    // posé à la main par le campaignMaker sur les blocs qu'il veut lier au wargame.
    //
    // Contrairement à une formation wargame, DCE_Manager n'est pas propriétaire de
    // ce bloc : il peut contenir n'importe quels autres champs, utiles au reste de
    // la campagne, que le wargame ne connaît pas et ne doit jamais perdre. D'où
    // RawLines : le contenu texte complet et brut du bloc, conservé pour pouvoir le
    // déplacer d'un camp à l'autre sans en réécrire une seule ligne.
    internal class WargameObjective
    {
        public string Name;

        // Clé Lua réelle du bloc dans le fichier (nom en Init, numéro en Active).
        // Sert à retrouver le bloc pour le patcher/déplacer - Name, lui, est le
        // vrai nom conceptuel de l'objectif, pas forcément identique à la clé.
        public string RawKey;

        // Camp qui VISE cet objectif (la table où il se trouve), pas son
        // propriétaire - même convention inversée que pour les formations.
        public string CurrentSide;

        public PointF Position;

        public int Priority;
        public List<string> Attributes = new List<string>();
        public double FirepowerMin;
        public double FirepowerMax;

        // Emplacement brut dans le fichier source, pour patcher ou déplacer.
        public int BlockStart, BlockEnd;
        public List<string> RawLines = new List<string>();
    }

    // Lit tous les objectifs (blocs marqués wargameObjective = true) d'une
    // targetlist. Lecture seule - voir WargameObjectiveWriter pour l'édition et
    // le déplacement de camp.
    internal class Parser_WargameObjectives
    {
        private static readonly Regex ObjectiveFlagRegex = new Regex(@"\bwargameObjective\b\s*=\s*true");

        public List<WargameObjective> Load(string pathFile)
        {
            var result = new List<WargameObjective>();
            if (!File.Exists(pathFile))
                return result;

            List<string> lines = File.ReadAllLines(pathFile).ToList();

            List<TargetListBlock> blocks = TargetListBlockScanner.ScanBlocks(lines,
                (l, start, end) => Enumerable.Range(start, end - start + 1).Any(k => ObjectiveFlagRegex.IsMatch(l[k])),
                out _);

            foreach (TargetListBlock block in blocks.Where(b => b.Matched))
            {
                WargameObjective obj = ParseBlock(lines, block);
                if (obj != null)
                    result.Add(obj);
            }

            return result;
        }

        private WargameObjective ParseBlock(List<string> lines, TargetListBlock block)
        {
            if (string.IsNullOrEmpty(block.Key))
                return null;

            var obj = new WargameObjective
            {
                RawKey = block.Key,
                CurrentSide = block.Side,
                BlockStart = block.Start,
                BlockEnd = block.End,
                RawLines = lines.GetRange(block.Start, block.End - block.Start + 1),
            };

            double groupX = 0, groupY = 0;
            bool hasGroupX = false, hasGroupY = false;

            // Profondeur RELATIVE au bloc lui-même (0 = juste après son accolade
            // d'ouverture). Indispensable : x/y/priority/attributes existent aussi
            // bien au niveau du groupe qu'à l'intérieur de chaque élément - sans
            // s'arrêter à depth 0, on finit par lire la position du DERNIER élément
            // au lieu de celle du groupe (bug réel trouvé sur OBJ_Towla).
            int depth = 0;
            bool isActiveFormat = int.TryParse(block.Key, out _);

            for (int i = block.Start; i <= block.End; i++)
            {
                string line = lines[i];

                if (depth == 0)
                {
                    if (TryReadField(line, "x", out string xText) &&
                        double.TryParse(xText, NumberStyles.Any, CultureInfo.InvariantCulture, out double xVal))
                    { groupX = xVal; hasGroupX = true; }

                    if (TryReadField(line, "y", out string yText) &&
                        double.TryParse(yText, NumberStyles.Any, CultureInfo.InvariantCulture, out double yVal))
                    { groupY = yVal; hasGroupY = true; }

                    if (TryReadField(line, "priority", out string pText) && int.TryParse(pText, out int p))
                        obj.Priority = p;

                    if (TryReadField(line, "attributes", out string attrText) && attrText.TrimStart().StartsWith("{"))
                        obj.Attributes = ParseInlineStringList(attrText);

                    if (isActiveFormat && obj.Name == null)
                    {
                        if (TryReadField(line, "titleName", out string t)) obj.Name = StripQuotes(t);
                        else if (TryReadField(line, "name", out string n)) obj.Name = StripQuotes(n);
                    }
                }

                bool opens = line.Contains("{") && !line.Contains("}");
                bool closes = line.Contains("}") && !line.Contains("{");

                if (opens) depth++;
                if (closes) depth--;
            }

            // Init : la clé EST le nom. Repli si rien d'autre n'a été trouvé.
            if (obj.Name == null)
                obj.Name = block.Key;

            (double fpMin, double fpMax) = ReadFirepower(lines, block.Start, block.End);
            obj.FirepowerMin = fpMin;
            obj.FirepowerMax = fpMax;

            if (hasGroupX && hasGroupY)
            {
                obj.Position = new PointF((float)groupX, (float)groupY);
            }
            else
            {
                PointF? firstElement = ReadFirstElementPosition(lines, block.Start, block.End);
                if (firstElement == null)
                    return null;

                obj.Position = firstElement.Value;
            }

            return obj;
        }

        private static string StripQuotes(string value)
        {
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2);
            return value;
        }


        // Cherche un champ dans les DEUX styles possibles sur une même ligne :
        //   ["nom"] = valeur,     ou     nom = valeur,
        // Renvoie la valeur brute (texte), virgule finale retirée.
        private bool TryReadField(string line, string fieldName, out string value)
        {
            value = null;

            var m = Regex.Match(line, @"^\s*(?:\[""" + Regex.Escape(fieldName) + @"""\]|" + Regex.Escape(fieldName)
                + @")\s*=\s*(.+?),?\s*$");

            if (!m.Success) return false;

            value = m.Groups[1].Value.Trim();
            return true;
        }

        // attributes = {"Structure"}   ou   attributes = {"a", "b"}
        private List<string> ParseInlineStringList(string rawValue)
        {
            var result = new List<string>();
            foreach (Match m in Regex.Matches(rawValue, "\"([^\"]*)\""))
                result.Add(m.Groups[1].Value);

            if (result.Count == 0)
                result.Add("Vehicles");

            return result;
        }

        private (double min, double max) ReadFirepower(List<string> lines, int blockStart, int blockEnd)
        {
            double min = 2, max = 2;

            for (int i = blockStart; i <= blockEnd; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s*(?:\[""firepower""\]|firepower)\s*=\s*$") &&
                    !Regex.IsMatch(lines[i], @"^\s*(?:\[""firepower""\]|firepower)\s*=\s*\{"))
                    continue;

                // Sous-bloc trouvé : cherche min/max entre ici et sa fermeture
                for (int k = i; k <= blockEnd; k++)
                {
                    if (TryReadField(lines[k], "min", out string minText) &&
                        double.TryParse(minText, NumberStyles.Any, CultureInfo.InvariantCulture, out double minVal))
                        min = minVal;

                    if (TryReadField(lines[k], "max", out string maxText) &&
                        double.TryParse(maxText, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxVal))
                        max = maxVal;

                    if (lines[k].Contains("}") && k > i)
                        break;
                }

                break;
            }

            return (min, max);
        }

        private PointF? ReadFirstElementPosition(List<string> lines, int blockStart, int blockEnd)
        {
            for (int i = blockStart; i <= blockEnd; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s*(?:\[""elements""\]|elements)\s*=\s*$") &&
                    !Regex.IsMatch(lines[i], @"^\s*(?:\[""elements""\]|elements)\s*=\s*\{"))
                    continue;

                double x = 0, y = 0;
                bool hasX = false, hasY = false;

                // Le premier élément est la première sous-table [1] rencontrée ;
                // on s'arrête dès qu'on quitte cette sous-table (accolade fermante
                // après avoir trouvé x et y, ou après le [2] suivant).
                bool inFirstElement = false;

                for (int k = i; k <= blockEnd; k++)
                {
                    if (Regex.IsMatch(lines[k], @"^\s*\[1\]\s*=")) inFirstElement = true;
                    else if (Regex.IsMatch(lines[k], @"^\s*\[2\]\s*=")) break; // fin du premier élément

                    if (!inFirstElement) continue;

                    if (TryReadField(lines[k], "x", out string xText) &&
                        double.TryParse(xText, NumberStyles.Any, CultureInfo.InvariantCulture, out double xVal))
                    { x = xVal; hasX = true; }

                    if (TryReadField(lines[k], "y", out string yText) &&
                        double.TryParse(yText, NumberStyles.Any, CultureInfo.InvariantCulture, out double yVal))
                    { y = yVal; hasY = true; }
                }

                if (hasX && hasY)
                    return new PointF((float)x, (float)y);

                break;
            }

            return null;
        }
    }
}
