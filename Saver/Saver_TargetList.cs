using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    // Écrit UNIQUEMENT les priorités modifiées dans targetlist_init.lua / targetlist.lua.
    // Pourquoi ne pas régénérer tout le fichier comme Saver_Campaign le fait pour oob_air.lua :
    // TargetAssetInfo ne capture qu'une partie des champs Lua (zone, refpoint, elements,
    // attributes, firepower...). Le régénérer à partir de ce modèle simplifié perdrait ces
    // données. On modifie donc juste la ligne "priority" de la cible concernée, en texte brut.
    internal class Saver_TargetList
    {
        public static void SaveInit(string pathFile, List<TargetAssetInfo> targets)
        {
            Save(pathFile, targets, folderFile: "Init", quotedPriorityKey: false, nameFromOpeningLine: true);
        }

        public static void SaveActive(string pathFile, List<TargetAssetInfo> targets)
        {
            Save(pathFile, targets, folderFile: "Active", quotedPriorityKey: true, nameFromOpeningLine: false);
        }

        // quotedPriorityKey : Init écrit "priority = 18,", Active écrit "["priority"] = 18,"
        // nameFromOpeningLine : en Init le nom est sur la ligne d'ouverture du bloc
        // ( ["Nom"] = { ), en Active le bloc est indexé par un numéro et le nom se trouve
        // n'importe où dans le bloc, dans le champ ["titleName"] (avant OU après "priority"
        // selon les cas -> on ne peut pas se contenter d'un parcours ligne par ligne dans
        // l'ordre, il faut traiter le bloc entier d'un coup une fois ses bornes connues).
        private static void Save(string pathFile, List<TargetAssetInfo> targets, string folderFile, bool quotedPriorityKey, bool nameFromOpeningLine)
        {
            if (!File.Exists(pathFile))
                return;

            var priorityByName = targets
                .Where(t => t.FolderFile == folderFile)
                .ToDictionary(t => t.TitleName, t => t.Priority);

            if (priorityByName.Count == 0)
                return;

            string[] lines = File.ReadAllLines(pathFile);

            var priorityLineRegex = quotedPriorityKey
                ? new Regex(@"^(\s*\[""priority""\]\s*=\s*)(-?\d+)(,?\s*)$")
                : new Regex(@"^(\s*priority\s*=\s*)(-?\d+)(,?\s*)$");

            var titleNameRegex = new Regex(@"\[""titleName""\]\s*=\s*""(.+?)""");
            var openingNameRegex = new Regex(@"^\s*\[""(.+?)""\]\s*=\s*\{?\s*$");

            int depth = 0;
            int blockStart = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                bool opens = line.Contains("{") && !line.Contains("}");
                bool closes = line.Contains("}") && !line.Contains("{");

                if (opens)
                {
                    depth++;

                    // Profondeur 3 = racine > camp (blue/red) > cible individuelle -> on note
                    // où commence son contenu, on le traitera d'un bloc quand il se refermera.
                    if (depth == 3)
                        blockStart = i;
                }

                if (closes)
                {
                    if (depth == 3)
                        ProcessBlock(lines, blockStart, i, nameFromOpeningLine, openingNameRegex, titleNameRegex, priorityLineRegex, priorityByName);

                    depth--;
                }
            }

            File.WriteAllLines(pathFile, lines);
        }

        // Traite un bloc de cible complet (de sa ligne d'ouverture à sa ligne de fermeture) :
        // détermine son nom, et si une nouvelle priorité est prévue pour ce nom, patche la
        // ligne "priority" trouvée n'importe où dans le bloc.
        private static void ProcessBlock(string[] lines, int start, int end, bool nameFromOpeningLine,
            Regex openingNameRegex, Regex titleNameRegex, Regex priorityLineRegex,
            Dictionary<string, int> priorityByName)
        {
            string blockName = null;

            if (nameFromOpeningLine)
            {
                var mName = openingNameRegex.Match(lines[start]);
                if (mName.Success)
                    blockName = mName.Groups[1].Value;
            }
            else
            {
                for (int i = start; i <= end; i++)
                {
                    var mTitle = titleNameRegex.Match(lines[i]);
                    if (mTitle.Success)
                    {
                        blockName = mTitle.Groups[1].Value;
                        break;
                    }
                }
            }

            if (blockName == null || !priorityByName.TryGetValue(blockName, out int newPriority))
                return;

            for (int i = start; i <= end; i++)
            {
                var mPriority = priorityLineRegex.Match(lines[i]);
                if (mPriority.Success)
                {
                    lines[i] = mPriority.Groups[1].Value + newPriority + mPriority.Groups[3].Value;
                    break;
                }
            }
        }
    }
}
