using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using static DCE_Manager.Utils.FormUtils;

namespace DCE_Manager
{
    // Empaquette une campagne installée dans un .zip distribuable.
    // Pourquoi : permettre à un autre joueur d'installer la campagne sur son propre
    // DCE_Manager, sans lui filer au passage nos parties en cours (Active), nos logs de
    // debug (Debug) ou nos anciens débriefings (Debriefing). Même liste de dossiers "session"
    // que celle vidée par Clone_Form après un clonage. En plus des fichiers de la campagne,
    // embarque aussi les livrées custom référencées par les squads (Init + Active), quand
    // elles sont présentes sur ce PC - jamais les avions/mods eux-mêmes.
    internal static class CampaignExporter
    {
        private static readonly string[] FoldersToEmpty = { "Active", "Debug", "Debriefing" };

        // Fichiers déposés par DCS à la racine du dossier de campagne à la fin d'une mission.
        // Ils appartiennent à la partie en cours de l'auteur : jamais dans un paquet distribué
        // (sinon l'installation chez l'autre joueur afficherait un "debrief en attente").
        private static readonly string[] EndOfMissionFiles =
        {
            "camp_status.lua", "MissionEventsLog.lua", "scen_destroyed.lua", "zoneSAR.lua", "Mission_LL_Positions.lua"
        };

        // campaignsRoot = dossier "...\Missions\Campaigns" (celui qui contient nameCamp).
        // destinationZipPath = chemin complet du .zip à créer (écrasé s'il existe déjà).
        // Retourne un compte-rendu des livrées trouvées/manquantes (liste vide si la campagne
        // n'utilise aucune livrée custom ou si oob_air_init.lua est absent/illisible).
        // progress (facultatif) : appelé pendant l'export pour la fenêtre de progression.
        // token (facultatif) : si l'utilisateur annule, une OperationCanceledException est levée ;
        // l'appelant supprime alors le zip partiel.
        public static List<string> ExportCampaign(string campaignsRoot, string nameCamp, string destinationZipPath,
    bool includeLiveries = true, bool includeDoc = true,
    Action<CampaignProgressInfo> progress = null, CancellationToken token = default(CancellationToken))
        {
            string campaignPath = Path.Combine(campaignsRoot, nameCamp);

            if (!Directory.Exists(campaignPath))
                throw new DirectoryNotFoundException("Campaign folder not found: " + campaignPath);

            if (File.Exists(destinationZipPath))
                File.Delete(destinationZipPath);

            List<string> liveryReport = new List<string>();

            using (var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create))
            {
                // 1. Tous les fichiers du dossier de campagne, sauf ceux qui vivent dans
                // Active/Debug/Debriefing, sauf les fichiers de fin de mission, et sauf Doc/ si
                // l'utilisateur l'a décoché.
                // On dresse d'abord la liste des fichiers à mettre : ça donne le total pour le pourcentage.
                List<string> campaignFiles = new List<string>();

                foreach (string filePath in Directory.GetFiles(campaignPath, "*", SearchOption.AllDirectories))
                {
                    if (IsInEmptiedFolder(campaignPath, filePath))
                        continue;

                    if (IsEndOfMissionFile(campaignPath, filePath))
                        continue;

                    if (!includeDoc && IsDirectlyUnderFolder(campaignPath, filePath, "Doc"))
                        continue;

                    campaignFiles.Add(filePath);
                }

                int fileIndex = 0;

                foreach (string filePath in campaignFiles)
                {
                    token.ThrowIfCancellationRequested();

                    string relativeFile = GetRelativePath(campaignPath, filePath);
                    string entryName = nameCamp + "/" + relativeFile.Replace('\\', '/');
                    zip.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);

                    fileIndex++;

                    // Pas à chaque fichier (des milliers de messages inutiles) : environ tous les 10 fichiers
                    if (fileIndex % 10 == 0 || fileIndex == campaignFiles.Count)
                    {
                        int percentFiles = (int)(100L * fileIndex / campaignFiles.Count);
                        ReportProgress(progress, percentFiles * (includeLiveries ? 50 : 90) / 100,
                            "Packing campaign files...", percentFiles, relativeFile);
                    }
                }

                // 1b. Dossiers VIDES de la campagne (ex: un Sounds ou un Doc vide) : sans entrée de
                // dossier explicite, ils disparaissaient du paquet. Les dossiers Active/Debug/Debriefing
                // sont traités plus bas, et Doc est ignoré si l'utilisateur l'a décoché.
                foreach (string dirPath in Directory.GetDirectories(campaignPath, "*", SearchOption.AllDirectories))
                {
                    if (Directory.EnumerateFileSystemEntries(dirPath).Any())
                        continue;

                    string relativeDir = GetRelativePath(campaignPath, dirPath);
                    string firstSegment = relativeDir.Split(Path.DirectorySeparatorChar)[0];

                    if (FoldersToEmpty.Any(f => string.Equals(f, firstSegment, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (!includeDoc && string.Equals(firstSegment, "Doc", StringComparison.OrdinalIgnoreCase))
                        continue;

                    zip.CreateEntry(nameCamp + "/" + relativeDir.Replace('\\', '/') + "/");
                }

                // 2. On garde quand même les dossiers "session" dans l'archive, mais vides :
                // une entrée de dossier explicite (nom terminé par "/") suffit à ce que
                // l'extraction les recrée, prêts à être remplis par la prochaine partie.
                foreach (string folderName in FoldersToEmpty)
                {
                    if (Directory.Exists(Path.Combine(campaignPath, folderName)))
                    {
                        zip.CreateEntry(nameCamp + "/" + folderName + "/");
                    }
                }

                // 3. Fichiers "satellites" : PAS dans le dossier de la campagne, mais juste à
                // côté, dans campaignsRoot (même convention que CampaignRepair). On les remet
                // à la racine du zip (pas sous nameCamp/) pour qu'ils retombent au même
                // endroit relatif une fois l'archive extraite dans campaignsRoot.
                string[] satelliteFiles =
                {
                    nameCamp + "_first.miz",
                    nameCamp + "_ongoing.miz",
                    nameCamp + ".cmp",
                    nameCamp + ".png"
                };

                foreach (string fileName in satelliteFiles)
                {
                    token.ThrowIfCancellationRequested();

                    string fullPath = Path.Combine(campaignsRoot, fileName);
                    if (File.Exists(fullPath))
                    {
                        zip.CreateEntryFromFile(fullPath, fileName, CompressionLevel.Optimal);
                    }
                    else
                    {
                        FormUtils.LogRegister("CampaignExporter | fichier satellite absent (ignoré) : " + fullPath);
                    }
                }

                // 4. Livrées custom utilisées par les squads (Init + Active), si demandées et
                // présentes sur ce PC. Jamais l'avion/le mod lui-même.
                ReportProgress(progress, includeLiveries ? 50 : 90, "Packing campaign files...", 100, "");

                if (includeLiveries)
                {
                    liveryReport = AddLiveries(zip, campaignPath, progress, token);
                }
            }

            ReportProgress(progress, 100, "Export finished", 100, "");

            FormUtils.LogRegister("CampaignExporter | Export terminé pour '" + nameCamp + "' -> " + destinationZipPath);

            return liveryReport;
        }

        // Envoie un message à la fenêtre de progression (ne fait rien si progress est null).
        private static void ReportProgress(Action<CampaignProgressInfo> progress, int overallPercent, string overallText,
            int detailPercent, string detailText)
        {
            if (progress == null)
                return;

            progress(new CampaignProgressInfo
            {
                OverallPercent = Math.Max(0, Math.Min(100, overallPercent)),
                OverallText = overallText,
                DetailPercent = Math.Max(0, Math.Min(100, detailPercent)),
                DetailText = detailText
            });
        }

        private static List<string> AddLiveries(ZipArchive zip, string campaignPath,
            Action<CampaignProgressInfo> progress, CancellationToken token)
        {
            var report = new List<string>();
            var pairs = new HashSet<(string type, string livery)>();

            CollectLiveryPairsFromOobAirFile(Path.Combine(campaignPath, "Init", "oob_air_init.lua"), pairs);
            CollectLiveryPairsFromOobAirFile(Path.Combine(campaignPath, "Active", "oob_air.lua"), pairs);

            if (pairs.Count == 0)
                return report;

            // Le dossier Liveries\ ne porte quasiment jamais le nom du "type" (ex: type
            // "F-4E-45MC" -> dossier "F-4E") : la correspondance vient de folderModName dans
            // ScriptsMod\Data\UTIL_Data.lua (table Data_divers). Si ScriptsMod est absent, on
            // informe une seule fois et on retombe sur type = nom de dossier.
            bool scriptsModFound;
            Dictionary<string, string> folderNameByType = LoadAircraftFolderNameMap(out scriptsModFound);

            if (!scriptsModFound)
            {
                report.Add("Note: ScriptsMod not found - aircraft folder names could not be resolved " +
                            "(UTIL_Data.lua), raw type names were used instead. Some custom liveries " +
                            "under a different folder name may have been missed.");
            }

            string liveriesRoot = Path.Combine(ParamConf.PATH_SavedGames_DCS, "Liveries");

            int pairIndex = 0;

            foreach (var pair in pairs.OrderBy(p => p.type).ThenBy(p => p.livery))
            {
                token.ThrowIfCancellationRequested();

                pairIndex++;
                ReportProgress(progress, 50 + 45 * pairIndex / pairs.Count, "Packing liveries...",
                    100 * pairIndex / pairs.Count, pair.type + " / " + pair.livery);

                string folderName = folderNameByType.TryGetValue(pair.type, out string mapped) ? mapped : pair.type;
                string typeFolderPath = Path.Combine(liveriesRoot, folderName);

                // Dossier du type absent -> considéré comme une livrée DCS "core" (fournie
                // avec le jeu ou le module), rien à embarquer, rien à signaler. Distinction
                // encore floue à ce stade (à affiner plus tard).
                if (!Directory.Exists(typeFolderPath))
                    continue;

                string liverySourcePath = Path.Combine(typeFolderPath, pair.livery);

                if (!Directory.Exists(liverySourcePath))
                {
                    report.Add("Not found on this PC, skipped: " + pair.type + " / " + pair.livery);
                    continue;
                }

                foreach (string filePath in Directory.GetFiles(liverySourcePath, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();

                    string relative = GetRelativePath(liverySourcePath, filePath).Replace('\\', '/');
                    string entryName = "Liveries/" + folderName + "/" + pair.livery + "/" + relative;
                    zip.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
                }

                report.Add("Included: " + pair.type + " / " + pair.livery);
            }

            return report;
        }

        // Table type -> nom réel du dossier sous Liveries\, lue depuis ScriptsMod\Data\
        // UTIL_Data.lua (table Data_divers, champ folderModName). scriptsModFound = false si
        // le fichier est introuvable (ScriptsMod pas installé/pas à jour) - la map reste vide
        // et l'appelant retombe sur type = nom de dossier, sans bloquer l'export.
        private static Dictionary<string, string> LoadAircraftFolderNameMap(out bool scriptsModFound)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            scriptsModFound = false;

            string utilDataPath = DcemLua.Resolve("UTIL_Data.lua");

            if (!File.Exists(utilDataPath))
                return map;

            scriptsModFound = true;

            LuaObject luaObj;
            try
            {
                luaObj = LuaParser.ParseFile(utilDataPath, "Data_divers");
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("CampaignExporter | lecture Data_divers ignorée : " + ex.Message);
                return map;
            }

            var types = luaObj?.luaobj as Dictionary<string, LuaObject>;
            if (types == null)
                return map;

            foreach (var typeEntry in types)
            {
                var props = typeEntry.Value.luaobj as Dictionary<string, LuaObject>;
                if (props != null && props.ContainsKey("folderModName"))
                {
                    string folderModName = props["folderModName"].luaobj?.ToString();
                    if (!string.IsNullOrEmpty(folderModName))
                        map[typeEntry.Key] = folderModName;
                }
            }

            return map;
        }

        // Lecture Lua autonome, volontairement séparée de Parser_OobAir : celui-ci alimente des
        // listes STATIQUES globales (List_oob_air_Manager), les réutiliser ici écraserait
        // silencieusement la campagne actuellement chargée dans l'UI si ce n'est pas la même.
        // On ne s'intéresse qu'à "type" et "livery", rien d'autre.
        private static void CollectLiveryPairsFromOobAirFile(string filePath, HashSet<(string type, string livery)> pairs)
        {
            if (!File.Exists(filePath))
                return;

            LuaObject luaObj;
            try
            {
                luaObj = LuaParser.ParseFile(filePath, "oob_air");
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("CampaignExporter | lecture liveries ignorée (" + Path.GetFileName(filePath) + "): " + ex.Message);
                return;
            }

            var sides = luaObj?.luaobj as Dictionary<string, LuaObject>;
            if (sides == null)
                return;

            foreach (var sideEntry in sides) // "blue" / "red"
            {
                var squads = sideEntry.Value.luaobj as Dictionary<string, LuaObject>;
                if (squads == null)
                    continue;

                foreach (var squadEntry in squads)
                {
                    var props = squadEntry.Value.luaobj as Dictionary<string, LuaObject>;
                    if (props == null || !props.ContainsKey("type") || !props.ContainsKey("livery"))
                        continue;

                    string type = props["type"].luaobj?.ToString();
                    if (string.IsNullOrEmpty(type))
                        continue;

                    object liveryRaw = props["livery"].luaobj;

                    if (liveryRaw is Dictionary<string, LuaObject> liveryDict)
                    {
                        foreach (LuaObject lv in liveryDict.Values)
                        {
                            string liveryName = lv.luaobj?.ToString();
                            if (!string.IsNullOrEmpty(liveryName))
                                pairs.Add((type, liveryName));
                        }
                    }
                    else
                    {
                        string liveryName = liveryRaw?.ToString();
                        if (!string.IsNullOrEmpty(liveryName))
                            pairs.Add((type, liveryName));
                    }
                }
            }
        }

        // Vrai si le fichier est (directement ou plus profond) dans un des dossiers "session".
        private static bool IsInEmptiedFolder(string campaignPath, string filePath)
        {
            return FoldersToEmpty.Any(f => IsDirectlyUnderFolder(campaignPath, filePath, f));
        }

        // Vrai si le fichier est un des fichiers de fin de mission ET se trouve directement à la racine
        // de la campagne (un fichier du même nom dans un sous-dossier, ex: Doc\, n'est pas concerné).
        private static bool IsEndOfMissionFile(string campaignPath, string filePath)
        {
            string relative = GetRelativePath(campaignPath, filePath);

            return relative.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                   EndOfMissionFiles.Contains(relative, StringComparer.OrdinalIgnoreCase);
        }

        // Vrai si le fichier vit directement sous campaignPath\<folderName>\... (à n'importe
        // quelle profondeur en dessous).
        private static bool IsDirectlyUnderFolder(string campaignPath, string filePath, string folderName)
        {
            string relative = GetRelativePath(campaignPath, filePath);
            string[] segments = relative.Split(Path.DirectorySeparatorChar);

            return segments.Length > 1 && string.Equals(segments[0], folderName, StringComparison.OrdinalIgnoreCase);
        }

        // .NET Framework 4.8 n'a pas Path.GetRelativePath. Avant, on passait par Uri : un nom contenant
        // "%" ou "#" (ex: "100%", "Op #1") était alors mal décodé. Ici, fullPath vient toujours de
        // Directory.GetFiles / GetDirectories sur rootPath : il commence donc par rootPath, on coupe simplement.
        private static string GetRelativePath(string rootPath, string fullPath)
        {
            string rootWithSlash = rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar;

            return fullPath.Substring(rootWithSlash.Length);
        }
    }
}
