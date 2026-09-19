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

        // campaignsRoot = dossier "...\Missions\Campaigns" (celui qui contient nameCamp).
        // destinationZipPath = chemin complet du .zip à créer (écrasé s'il existe déjà).
        // Retourne un compte-rendu des livrées trouvées/manquantes (liste vide si la campagne
        // n'utilise aucune livrée custom ou si oob_air_init.lua est absent/illisible).
        // onProgress(pourcentage, libellé) : facultatif, appelé depuis le thread d'appel.
        // onProgress : facultatif, appele depuis le thread d'appel.
        // token : permet d'interrompre proprement depuis le bouton Cancel.
        public static List<string> ExportCampaign(string campaignsRoot, string nameCamp, string destinationZipPath,
            bool includeLiveries = true, bool includeDoc = true,
            Action<CampaignProgressInfo> onProgress = null, CancellationToken token = default(CancellationToken))
        {
            string campaignPath = Path.Combine(campaignsRoot, nameCamp);

            if (!Directory.Exists(campaignPath))
                throw new DirectoryNotFoundException("Campaign folder not found: " + campaignPath);

            if (File.Exists(destinationZipPath))
                File.Delete(destinationZipPath);

            List<string> liveryReport = new List<string>();

            using (var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create))
            {
                Report(onProgress, 0, "Scanning campaign folder...", 0, campaignPath);

                // 1. Tous les fichiers du dossier de campagne, sauf Active/Debug/Debriefing,
                // et sauf Doc/ si l'utilisateur l'a decoche.
                string[] campaignFiles = Directory.GetFiles(campaignPath, "*", SearchOption.AllDirectories)
                    .Where(f => !IsInEmptiedFolder(campaignPath, f))
                    .Where(f => includeDoc || !IsDirectlyUnderFolder(campaignPath, f, "Doc"))
                    .ToArray();

                for (int i = 0; i < campaignFiles.Length; i++)
                {
                    token.ThrowIfCancellationRequested();

                    string filePath = campaignFiles[i];
                    string entryName = nameCamp + "/" + GetRelativePath(campaignPath, filePath).Replace('\\', '/');
                    zip.CreateEntryFromFile(filePath, entryName, GetCompressionLevel(filePath));

                    // Les fichiers de campagne sont legers face aux livrees : 0 -> 20%.
                    Report(onProgress,
                        campaignFiles.Length > 0 ? 20 * (i + 1) / campaignFiles.Length : 20,
                        "Campaign files (" + (i + 1) + "/" + campaignFiles.Length + ")",
                        campaignFiles.Length > 0 ? 100 * (i + 1) / campaignFiles.Length : 100,
                        filePath);
                }

                // 2. On garde quand meme les dossiers "session" dans l'archive, mais vides :
                // une entree de dossier explicite (nom termine par "/") suffit a ce que
                // l'extraction les recree, prets a etre remplis par la prochaine partie.
                foreach (string folderName in FoldersToEmpty)
                {
                    if (Directory.Exists(Path.Combine(campaignPath, folderName)))
                    {
                        zip.CreateEntry(nameCamp + "/" + folderName + "/");
                    }
                }

                // 3. Fichiers "satellites" : PAS dans le dossier de la campagne, mais juste a
                // cote, dans campaignsRoot. On les remet a la racine du zip (pas sous nameCamp/)
                // pour qu'ils retombent au meme endroit relatif une fois l'archive extraite.
                string[] satelliteFiles =
                {
                    nameCamp + "_first.miz",
                    nameCamp + "_ongoing.miz",
                    nameCamp + ".cmp",
                    nameCamp + ".png"
                };

                for (int i = 0; i < satelliteFiles.Length; i++)
                {
                    token.ThrowIfCancellationRequested();

                    string fullPath = Path.Combine(campaignsRoot, satelliteFiles[i]);

                    Report(onProgress, 20 + 5 * (i + 1) / satelliteFiles.Length,
                        "Campaign files (.miz / .cmp / .png)", 100 * (i + 1) / satelliteFiles.Length, fullPath);

                    if (File.Exists(fullPath))
                    {
                        zip.CreateEntryFromFile(fullPath, satelliteFiles[i], GetCompressionLevel(fullPath));
                    }
                    else
                    {
                        FormUtils.LogRegister("CampaignExporter | fichier satellite absent (ignore) : " + fullPath);
                    }
                }

                // 4. Livrees custom utilisees par les squads (Init + Active), si demandees et
                // presentes sur ce PC. Jamais l'avion/le mod lui-meme.
                if (includeLiveries)
                {
                    liveryReport = AddLiveries(zip, nameCamp, campaignPath, onProgress, token);
                }
                else
                {
                    Report(onProgress, 100, "Done", 100, "");
                }
            }

            FormUtils.LogRegister("CampaignExporter | Export termine pour '" + nameCamp + "' -> " + destinationZipPath);

            return liveryReport;
        }

        private static void Report(Action<CampaignProgressInfo> onProgress,
            int overallPercent, string overallText, int detailPercent, string detailText)
        {
            onProgress?.Invoke(new CampaignProgressInfo
            {
                OverallPercent = overallPercent,
                OverallText = overallText,
                DetailPercent = detailPercent,
                DetailText = detailText
            });
        }

        // Textures et .miz sont deja compresses : Optimal coute beaucoup de CPU pour un gain
        // quasi nul. C'est la principale cause des longues pauses apparentes sur une livree.
        private static CompressionLevel GetCompressionLevel(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            switch (ext)
            {
                case ".dds":
                case ".tga":
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".miz":
                case ".zip":
                    return CompressionLevel.Fastest;

                default:
                    return CompressionLevel.Optimal;
            }
        }

        // Windows est insensible a la casse : Directory.Exists("B-52H") repond true alors que
        // le dossier reel s'appelle "b-52h". On recupere donc le nom REEL tel qu'ecrit sur le
        // disque, sinon on recree dans le zip un dossier dont la casse ne correspond pas a ce
        // qu'attend DCS.
        private static string GetRealFolderName(string parentPath, string candidateName)
        {
            try
            {
                string match = Directory.GetDirectories(parentPath, candidateName)
                    .Select(Path.GetFileName)
                    .FirstOrDefault();

                return match ?? candidateName;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("CampaignExporter | nom reel de dossier non resolu pour '"
                                    + candidateName + "' : " + ex.Message);
                return candidateName;
            }
        }

        private static List<string> AddLiveries(ZipArchive zip, string nameCamp, string campaignPath,
    Action<CampaignProgressInfo> onProgress = null, CancellationToken token = default(CancellationToken))
        {
            var report = new List<string>();
            var pairs = new HashSet<(string type, string livery)>();

            // Origine de chaque paire, pour le compte-rendu : Init = dotation d'origine de la
            // campagne, Active = etat courant d'une partie en cours (peut contenir des escadrons
            // apparus en cours de route, absents du fichier Init). Les deux sont donc lus.
            var originByPair = new Dictionary<(string type, string livery), string>();

            string initFile = Path.Combine(campaignPath, "Init", "oob_air_init.lua");
            string activeFile = Path.Combine(campaignPath, "Active", "oob_air.lua");

            Report(onProgress, 25, "Reading squadrons...", 0, initFile);
            CollectLiveryPairsFromOobAirFile(initFile, pairs, originByPair, @"Init\oob_air_init.lua");

            Report(onProgress, 25, "Reading squadrons...", 0, activeFile);
            CollectLiveryPairsFromOobAirFile(activeFile, pairs, originByPair, @"Active\oob_air.lua");

            if (pairs.Count == 0)
            {
                Report(onProgress, 100, "Done", 100, "");
                return report;
            }

            // Le dossier Liveries\ ne porte quasiment jamais le nom du "type" (ex: type
            // "F-4E-45MC" -> dossier "F-4E") : la correspondance vient de folderModName dans
            // ScriptsMod\Data\UTIL_Data.lua (table Data_divers). Si ScriptsMod est absent, on
            // informe une seule fois et on retombe sur type = nom de dossier.
            Report(onProgress, 25, "Resolving aircraft folder names...", 0, "UTIL_Data.lua");

            bool scriptsModFound;
            Dictionary<string, string> folderNameByType = LoadAircraftFolderNameMap(out scriptsModFound);

            if (!scriptsModFound)
            {
                report.Add("Note: ScriptsMod not found - aircraft folder names could not be resolved "
                          + "(UTIL_Data.lua), raw type names were used instead. Some custom liveries "
                          + "under a different folder name may have been missed.");
            }

            // Fichier facultatif tenu a la main par le campaignMaker : droits de redistribution
            // + lien source par livree. Absent = comportement inchange (jamais bloquant).
            Dictionary<string, LiverySourceInfo> sources = LoadLiverySourceInfo(campaignPath);

            string liveriesRoot = Path.Combine(ParamConf.PATH_SavedGames_DCS, "Liveries");

            // Une entree par livree traitee, pour CREDITS_LINK_SKIN.txt genere a la fin.
            var creditsLines = new List<string>();

            var orderedPairs = pairs.OrderBy(p => p.type).ThenBy(p => p.livery).ToList();
            int pairIndex = 0;

            foreach (var pair in orderedPairs)
            {
                token.ThrowIfCancellationRequested();

                pairIndex++;

                // Nom de dossier attendu : folderModName (UTIL_Data.lua) sinon le type brut.
                string folderName;
                if (folderNameByType.TryGetValue(pair.type, out string mapped))
                {
                    folderName = mapped;
                }
                else
                {
                    folderName = pair.type;
                    if (scriptsModFound)
                        FormUtils.LogRegister("CampaignExporter | pas de folderModName dans UTIL_Data.lua pour le type '"
                                            + pair.type + "' : dossier suppose = '" + pair.type + "'");
                }

                string typeFolderPath = Path.Combine(liveriesRoot, folderName);

                // Les livrees representent l'essentiel du volume : 25% -> 100% de la barre.
                Report(onProgress, 25 + 75 * pairIndex / orderedPairs.Count,
                    "Liveries (" + pairIndex + "/" + orderedPairs.Count + ") : " + pair.type + " / " + pair.livery,
                    0, typeFolderPath);

                // Dossier du type absent -> considere comme une livree DCS "core" (fournie
                // avec le jeu ou le module), rien a embarquer, rien a signaler.
                if (!Directory.Exists(typeFolderPath))
                    continue;

                // Casse exacte du disque (voir GetRealFolderName) : on la reprend pour le type
                // ET pour la livree, afin que l'arborescence du zip soit strictement identique
                // a celle attendue par DCS.
                folderName = GetRealFolderName(liveriesRoot, folderName);
                typeFolderPath = Path.Combine(liveriesRoot, folderName);

                string liveryFolderName = GetRealFolderName(typeFolderPath, pair.livery);
                string liverySourcePath = Path.Combine(typeFolderPath, liveryFolderName);

                // Deux ecritures possibles de la cle dans livery_sources.txt : avec le nom de
                // dossier resolu via folderModName (ex: [b-52h\...]) ou avec le type brut de
                // oob_air_init.lua (ex: [B-52H\...]). On accepte les deux, le campaignMaker n'a
                // pas a deviner lequel le code a retenu.
                string sourceKeyFolder = folderName + "\\" + pair.livery;
                string sourceKeyType = pair.type + "\\" + pair.livery;

                LiverySourceInfo sourceInfo;
                bool sourceKnown = sources.TryGetValue(sourceKeyFolder, out sourceInfo)
                                || sources.TryGetValue(sourceKeyType, out sourceInfo);

                if (sources.Count > 0 && !sourceKnown)
                    FormUtils.LogRegister("CampaignExporter | aucune entree livery_sources.txt pour : ["
                                        + sourceKeyFolder + "] ni [" + sourceKeyType + "]");

                if (!Directory.Exists(liverySourcePath))
                {
                    string origin = originByPair.TryGetValue(pair, out string o) ? o : "unknown file";

                    report.Add("Livery NOT FOUND, skipped:\r\n"
                             + "    type      : " + pair.type + "\r\n"
                             + "    livery    : " + pair.livery + "\r\n"
                             + "    declared in: " + origin + "\r\n"
                             + "    searched  : " + liverySourcePath);
                    continue;
                }

                // "redistribute=no" declare explicitement : on n'embarque PAS le skin (respect
                // de la licence), on garde juste le lien pour que celui qui installe aille le
                // telecharger lui-meme.
                if (sourceInfo?.Redistribute == false)
                {
                    report.Add("Excluded (redistribution not allowed):\r\n"
                             + "    type      : " + pair.type + "\r\n"
                             + "    livery    : " + pair.livery + "\r\n"
                             + "    declared in: " + (originByPair.TryGetValue(pair, out string oe) ? oe : "unknown file") + "\r\n"
                             + "    folder    : " + liverySourcePath);

                    creditsLines.Add("- " + pair.type + " / " + pair.livery);
                    if (!string.IsNullOrEmpty(sourceInfo.Author))
                        creditsLines.Add("    -> author: " + sourceInfo.Author);
                    creditsLines.Add("    -> NOT included in this package (redistribution not allowed).");
                    creditsLines.Add(string.IsNullOrEmpty(sourceInfo.Link)
                        ? "    -> download it yourself before installing this campaign."
                        : "    -> download it yourself before installing this campaign: " + sourceInfo.Link);
                    creditsLines.Add("");
                    continue;
                }

                string[] liveryFiles = Directory.GetFiles(liverySourcePath, "*", SearchOption.AllDirectories);
                string creditFileName = liveryFiles.Select(Path.GetFileName).FirstOrDefault(IsCreditLikeFileName);

                for (int i = 0; i < liveryFiles.Length; i++)
                {
                    token.ThrowIfCancellationRequested();

                    string filePath = liveryFiles[i];
                    string relative = GetRelativePath(liverySourcePath, filePath).Replace('\\', '/');
                    string entryName = "Liveries/" + folderName + "/" + liveryFolderName + "/" + relative;
                    zip.CreateEntryFromFile(filePath, entryName, GetCompressionLevel(filePath));

                    Report(onProgress, 25 + 75 * pairIndex / orderedPairs.Count,
                        "Liveries (" + pairIndex + "/" + orderedPairs.Count + ") : " + pair.type + " / " + pair.livery,
                        100 * (i + 1) / liveryFiles.Length,
                        "(" + (i + 1) + "/" + liveryFiles.Length + ")  " + filePath);
                }

                report.Add("Included: " + pair.type + " / " + pair.livery);

                creditsLines.Add("- " + pair.type + " / " + pair.livery);

                if (!string.IsNullOrEmpty(sourceInfo?.Author))
                    creditsLines.Add("    -> author: " + sourceInfo.Author);

                if (creditFileName != null)
                    creditsLines.Add("    -> credit file included: " + creditFileName);

                if (sourceInfo?.Link != null)
                    creditsLines.Add("    -> source: " + sourceInfo.Link);

                if (string.IsNullOrEmpty(sourceInfo?.Author) && creditFileName == null && sourceInfo?.Link == null)
                    creditsLines.Add("    -> no author, no credit file, and no known source. If you know "
                                    + "the author, please add a mention here (name, repo/website link, or "
                                    + "ED User Files page) before distributing.");

                if (sourceInfo?.Redistribute == null)
                    creditsLines.Add("    -> redistribution rights unknown - verify the license before "
                                    + "sharing this package (add an entry to Init\\livery_sources.txt "
                                    + "to remember it next time).");

                creditsLines.Add("");
            }

            if (creditsLines.Count > 0)
            {
                // Toujours sous Doc\, qu'il ait ete coche a l'export ou non : c'est un fichier
                // genere, pas du contenu Doc preexistant du campaignMaker.
                ZipArchiveEntry creditsEntry = zip.CreateEntry(nameCamp + "/Doc/CREDITS_LINK_SKIN.txt");
                using (var writer = new StreamWriter(creditsEntry.Open()))
                {
                    writer.WriteLine("Liveries included in this package");
                    writer.WriteLine("==================================");
                    writer.WriteLine();
                    foreach (string line in creditsLines)
                        writer.WriteLine(line);
                }
            }

            Report(onProgress, 100, "Done", 100, "");

            return report;
        }

        private class LiverySourceInfo
        {
            public string Author;
            public bool? Redistribute; // null = non renseigné
            public string Link;
        }

        // Fichier optionnel, tenu à la main par le campaignMaker : Init\livery_sources.txt.
        // Un bloc par livrée, format proche d'un .ini (plus lisible à la main qu'une ligne à
        // rallonge, et évite les soucis si un nom d'auteur contient un caractère "spécial") :
        //
        //   [dossier\nom de la livery]
        //   author = nom de l'auteur
        //   redistribute = yes|no
        //   link = lien vers le dépôt/site/page ED de l'auteur
        //
        // Tous les champs sont facultatifs. Absent (fichier, bloc ou champ) = comportement
        // inchangé, jamais bloquant.
        // Fichier optionnel, tenu a la main par le campaignMaker : livery_sources.txt.
        // Cherche dans Doc\ est l'emplacement le
        // plus intuitif cote utilisateur, c'est aussi la ou est genere CREDITS_LINK_SKIN.txt.
        // Un bloc par livree, format proche d'un .ini :
        //
        //   [dossier\nom de la livery]
        //   author = nom de l'auteur
        //   redistribute = yes|no
        //   link = lien vers le depot/site/page ED de l'auteur
        //
        // ATTENTION : "nom de la livery" doit etre le nom EXACT du champ livery dans
        // oob_air_init.lua (ex: [F111C\RAAF 1 SQN]), pas le nom du mod avion.
        // Tous les champs sont facultatifs. Absent (fichier, bloc ou champ) = comportement
        // inchange, jamais bloquant.
        private static Dictionary<string, LiverySourceInfo> LoadLiverySourceInfo(string campaignPath)
        {
            var result = new Dictionary<string, LiverySourceInfo>(StringComparer.OrdinalIgnoreCase);

            string sourcePath = Path.Combine(campaignPath, "Doc", "livery_sources.txt");

            if (!File.Exists(sourcePath))
            {
                FormUtils.LogRegister("CampaignExporter | livery_sources.txt absent de Doc\\ : "
                                    + "aucun droit de redistribution declare. Cherche : " + sourcePath);
                return result;
            }

            LiverySourceInfo current = null;

            foreach (string rawLine in File.ReadAllLines(sourcePath))
            {
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string key = line.Substring(1, line.Length - 2).Trim();
                    current = new LiverySourceInfo();
                    result[key] = current;
                    continue;
                }

                if (current == null)
                    continue; // ligne "champ = valeur" avant tout bloc [xxx] : ignoree

                int eq = line.IndexOf('=');
                if (eq < 0)
                    continue;

                string field = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                switch (field)
                {
                    case "author":
                        current.Author = value;
                        break;

                    case "redistribute":
                        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase)) current.Redistribute = true;
                        else if (value.Equals("no", StringComparison.OrdinalIgnoreCase)) current.Redistribute = false;
                        break;

                    case "link":
                        current.Link = value;
                        break;
                }
            }

            FormUtils.LogRegister("CampaignExporter | livery_sources.txt lu (" + sourcePath + ") : "
                                + result.Count + " entree(s) : " + string.Join(" | ", result.Keys));

            return result;
        }

        private static bool IsCreditLikeFileName(string fileName)
        {
            string lower = fileName.ToLowerInvariant();
            string[] keywords = { "readme", "credit", "license", "licence", "author" };
            return keywords.Any(k => lower.Contains(k));
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
        private static void CollectLiveryPairsFromOobAirFile(string filePath, HashSet<(string type, string livery)> pairs,
    Dictionary<(string type, string livery), string> originByPair = null, string originLabel = null)
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
                                AddPair(pairs, originByPair, originLabel, type, liveryName);
                        }
                    }
                    else
                    {
                        string liveryName = liveryRaw?.ToString();
                        if (!string.IsNullOrEmpty(liveryName))
                            AddPair(pairs, originByPair, originLabel, type, liveryName);
                    }
                }
            }
        }

        // Enregistre la paire et, la premiere fois qu'on la voit, le fichier d'ou elle vient.
        // Une paire presente dans Init ET Active reste attribuee a Init (lu en premier).
        private static void AddPair(HashSet<(string type, string livery)> pairs,
            Dictionary<(string type, string livery), string> originByPair,
            string originLabel, string type, string liveryName)
        {
            var pair = (type, liveryName);
            pairs.Add(pair);

            if (originByPair != null && originLabel != null && !originByPair.ContainsKey(pair))
                originByPair[pair] = originLabel;
        }

        // Vrai si le fichier est (directement ou plus profond) dans un des dossiers "session".
        // Vrai si le fichier est (directement ou plus profond) dans un des dossiers "session".
        private static bool IsInEmptiedFolder(string campaignPath, string filePath)
        {
            return FoldersToEmpty.Any(f => IsDirectlyUnderFolder(campaignPath, filePath, f));
        }

        // Vrai si le fichier vit directement sous campaignPath\<folderName>\... (à n'importe
        // quelle profondeur en dessous).
        private static bool IsDirectlyUnderFolder(string campaignPath, string filePath, string folderName)
        {
            string relative = GetRelativePath(campaignPath, filePath);
            string[] segments = relative.Split(Path.DirectorySeparatorChar);

            return segments.Length > 1 && string.Equals(segments[0], folderName, StringComparison.OrdinalIgnoreCase);
        }

        // .NET Framework 4.8 n'a pas Path.GetRelativePath : on passe par Uri, comme ailleurs
        // dans le projet quand on a besoin d'un chemin relatif.
        private static string GetRelativePath(string rootPath, string fullPath)
        {
            string rootWithSlash = rootPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar;

            Uri rootUri = new Uri(rootWithSlash);
            Uri fullUri = new Uri(fullPath);

            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fullUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }
    }
}