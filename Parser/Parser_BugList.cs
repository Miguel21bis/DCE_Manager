using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    internal class Parser_BugList
    {
        // Lit Debug/BugList.lua de la campagne et retourne la liste des messages de bug.
        // Retourne une liste vide si le fichier n'existe pas encore (campagne sans bug, ou jamais jouée).
        public List<string> LoadBugList(string campaignName)
        {
            var result = new List<string>();

            string path = Path.Combine(
                ParamConf.PATH_SavedGames_DCS, "Mods", "tech", "DCE", "Missions", "Campaigns",
                campaignName, "Debug", "BugList.lua");

            if (!File.Exists(path))
                return result;

            string content = File.ReadAllText(path);

            // Le fichier est une table Lua : [1] = "message", [2] = "message", ...
            // On capture le texte entre guillemets, en tolérant les guillemets échappés (\")
            var matches = Regex.Matches(content, @"\[\d+\]\s*=\s*""((?:[^""\\]|\\.)*)""");

            foreach (Match m in matches)
            {
                string message = m.Groups[1].Value.Replace("\\\"", "\"");
                result.Add(message);
            }

            return result;
        }
    }
}