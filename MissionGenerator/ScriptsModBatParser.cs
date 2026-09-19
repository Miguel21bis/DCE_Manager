using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DCE_Manager.Utils
{
    // FirstMission.bat / SkipMission.bat sont toujours structurés pareil : ils appellent
    // "%pathDCS%bin\luae.exe" sur "..\..\..\ScriptsMod.%versionPackageICM%\BAT_<Nom>.lua".
    // Seul Init\path.bat change réellement d'un PC à l'autre (chemin DCS, Saved Games, version du
    // ScriptsMod) - c'est donc le seul fichier qu'on a encore besoin de lire.
    public static class ScriptsModBatParser
    {
        public class LaunchInfo
        {
            public string ExePath;
            public string Arguments;
            public Dictionary<string, string> EnvironmentVariables = new Dictionary<string, string>();
        }

        // Une ligne "set "NOM=VALEUR"" dans Init\path.bat.
        private static readonly Regex RegexSetVar =
            new Regex(@"set\s+""([^=""]+)=([^""]*)""", RegexOptions.IgnoreCase);

        // batPath : chemin complet vers FirstMission.bat ou SkipMission.bat. Seul le nom de
        // fichier sert, pour en déduire BAT_FirstMission.lua / BAT_SkipMission.lua.
        // luaScriptName : à renseigner quand le .bat ne suit PAS cette convention de nommage
        // (ex: DEBUG_DebriefMission.bat -> Debrief_Master.lua).
        // Retourne null si Init\path.bat est absent ou dans un format inattendu.
        public static LaunchInfo Parse(string batPath, string campaignFolder, string luaScriptName = null)
        {
            string pathBatFile = Path.Combine(campaignFolder, "Init", "path.bat");

            if (!File.Exists(pathBatFile))
                return null;

            var info = new LaunchInfo();

            string pathBatContent = File.ReadAllText(pathBatFile);
            foreach (Match setMatch in RegexSetVar.Matches(pathBatContent))
                info.EnvironmentVariables[setMatch.Groups[1].Value] = setMatch.Groups[2].Value;

            if (!info.EnvironmentVariables.ContainsKey("pathDCS") ||
                !info.EnvironmentVariables.ContainsKey("versionPackageICM"))
                return null;

            if (string.IsNullOrEmpty(luaScriptName))
                luaScriptName = "BAT_" + Path.GetFileNameWithoutExtension(batPath) + ".lua";

            info.ExePath = info.EnvironmentVariables["pathDCS"] + @"bin\luae.exe";
            info.Arguments = @"..\..\..\ScriptsMod." + info.EnvironmentVariables["versionPackageICM"] + @"\" + luaScriptName;

            return info;
        }
    }
}
