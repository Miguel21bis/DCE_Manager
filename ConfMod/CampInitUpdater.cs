using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Met à jour camp_init.lua d'une campagne en le recalant sur UTIL_REF_camp_init.lua,
    // sur le même principe que ConfModTemplateUpdater pour conf_mod.lua :
    // - on reconstruit le contenu dans l'ordre de la référence
    // - on garde la valeur locale si la clé existe déjà
    // - une clé locale absente de la référence disparaît... sauf si elle est listée dans
    //   MigrateToConfMod, auquel cas sa valeur est d'abord recopiée dans conf_mod.lua
    //
    // Un champ peut s'étendre sur plusieurs lignes sans que la ligne d'ouverture se
    // termine "toute seule" par une accolade (ex: pictureBrief.blue avec ses images en
    // dessous). On détecte ça en comptant les accolades de chaque ligne plutôt que par
    // un format figé - une liste comme pictureBrief.blue n'est jamais qu'un bloc
    // multi-lignes qui ne contient aucune ligne "clé = ...", donc le même mécanisme la
    // gère automatiquement (pas besoin de détection spéciale pour les listes).
    internal class CampInitUpdater
    {
        // Anciennes variables qui ont pu traîner dans camp_init.lua, à reverser dans
        // conf_mod.lua plutôt que simplement supprimées. Table extensible : ajouter une
        // ligne suffit, pas besoin de toucher au reste du code.
        private static readonly Dictionary<string, string> MigrateToConfMod = new Dictionary<string, string>
        {
            { "weather.trend", "mission_ini.weather.trend" },
            { "weather.variance", "mission_ini.weather.variance" },
            { "weather.refTemp", "mission_ini.weather.refTemp" },
            { "weather.instability", "mission_ini.weather.instability" },
            { "weather.windActivity", "mission_ini.weather.windActivity" },
            { "weather.winDirection", "mission_ini.weather.winDirection" },
        };

        private readonly ConfModLoader _confModLoader = new ConfModLoader();

        public string GetReferencePath()
        {
            return Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\ScriptsMod.NG", "UTIL_REF_camp_init.lua");
        }

        public string GetCampInitPath(string campaignName)
        {
            return Path.Combine(
                ParamConf.PATH_SavedGames_DCS,
                @"Mods\tech\DCE\Missions\Campaigns",
                campaignName,
                @"Init\camp_init.lua");
        }

        public ConfUpdateResult UpdateCampaign(string campaignName)
        {
            string localPath = GetCampInitPath(campaignName);
            string refPath = GetReferencePath();

            if (!File.Exists(localPath))
            {
                FormUtils.LogRegister("CampInitUpdater | camp_init.lua introuvable pour " + campaignName);
                return ConfUpdateResult.Ok; // pas un problème de référence, rien à signaler à ce sujet
            }

            if (!File.Exists(refPath))
            {
                FormUtils.LogRegister("CampInitUpdater | UTIL_REF_camp_init.lua introuvable : " + refPath);
                return ConfUpdateResult.ReferenceMissing;
            }

            // 1) reverse vers conf_mod.lua les vieilles variables connues, avant de les perdre
            MigrateObsoleteFields(campaignName, localPath, refPath);

            // 2) recale camp_init.lua sur la référence (ordre, clés manquantes, obsolètes...)
            List<string> localLines = new List<string>(File.ReadAllLines(localPath));
            List<string> refLines = new List<string>(File.ReadAllLines(refPath));

            int refStart, refEnd;

            if (!TryFindBlock(refLines, "REF_camp", 0, refLines.Count - 1, out refStart, out refEnd))
            {
                FormUtils.LogRegister("CampInitUpdater | bloc 'REF_camp' introuvable dans la référence");
                return ConfUpdateResult.ReferenceMissing;
            }

            int localStart, localEnd;
            bool localExists = TryFindBlock(localLines, "camp", 0, localLines.Count - 1, out localStart, out localEnd);

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
                localLines.Add("camp = {");
                localLines.AddRange(merged);
                localLines.Add("}");
            }

            int braceBalance = 0;

            foreach (string line in localLines)
                braceBalance += CountBraces(ConfUiSchemaParser.StripComment(line));

            if (braceBalance != 0)
            {
                FormUtils.LogRegister("CampInitUpdater | ABANDON, accolades déséquilibrées (delta=" + braceBalance + ") pour " + campaignName + " - aucune écriture faite, fichier local inchangé");
                return ConfUpdateResult.MergeAborted;
            }

            File.WriteAllLines(localPath, localLines.ToArray());
            FormUtils.LogRegister("CampInitUpdater | camp_init.lua mis à jour pour " + campaignName);
            return ConfUpdateResult.Ok;
        }

        // ---------------------------------------------------------------
        // Migration des vieux champs vers conf_mod.lua
        // ---------------------------------------------------------------

        private void MigrateObsoleteFields(string campaignName, string localPath, string refPath)
        {
            Dictionary<string, object> localFlat = FlattenLuaFile(localPath, "camp");
            Dictionary<string, object> refFlat = FlattenLuaFile(refPath, "REF_camp");

            if (localFlat == null || refFlat == null)
                return;

            string confModPath = _confModLoader.GetConfModPath(campaignName);
            bool confModChanged = false;

            foreach (KeyValuePair<string, object> entry in localFlat)
            {
                if (refFlat.ContainsKey(entry.Key))
                    continue; // toujours valide dans la référence, rien à migrer

                string target;

                if (!MigrateToConfMod.TryGetValue(entry.Key, out target))
                {
                    FormUtils.LogRegister("CampInitUpdater | variable obsolète (sans migration) : " + entry.Key);
                    continue;
                }

                if (SetConfModValue(confModPath, target, entry.Value))
                {
                    confModChanged = true;
                    FormUtils.LogRegister("CampInitUpdater | " + entry.Key + " porté vers conf_mod." + target);
                }
            }

            if (confModChanged)
                _confModLoader.InvalidateCache(campaignName);
        }

        // Charge un fichier Lua "clé = { ... }" avec NLua et aplatit son contenu en
        // {"a.b.c" = valeur}. Une table-liste (t[1] existe) est traitée comme une
        // feuille, pas explorée plus loin (ex: pictureBrief.blue).
        private static Dictionary<string, object> FlattenLuaFile(string path, string globalName)
        {
            var result = new Dictionary<string, object>();

            using (Lua lua = new Lua())
            {
                lua.DoFile(path);

                LuaTable root = lua[globalName] as LuaTable;

                if (root == null)
                {
                    FormUtils.LogRegister("CampInitUpdater | table '" + globalName + "' introuvable dans " + path);
                    return null;
                }

                Flatten(root, "", result);
            }

            return result;
        }

        private static void Flatten(LuaTable table, string prefix, Dictionary<string, object> result)
        {
            foreach (object keyObj in table.Keys)
            {
                string key = keyObj.ToString();
                string path = prefix.Length == 0 ? key : prefix + "." + key;

                object value = table[keyObj];
                LuaTable subTable = value as LuaTable;

                if (subTable != null && subTable[1] == null)
                    Flatten(subTable, path, result);
                else
                    result[path] = value;
            }
        }

        // Écrit une valeur unique à un chemin pointé dans conf_mod.lua (ex:
        // "mission_ini.weather.trend"), en ne touchant que cette ligne.
        private static bool SetConfModValue(string confModPath, string dottedPath, object value)
        {
            if (!File.Exists(confModPath))
            {
                FormUtils.LogRegister("CampInitUpdater | conf_mod.lua introuvable : " + confModPath);
                return false;
            }

            string[] segments = dottedPath.Split('.');
            string key = segments[segments.Length - 1];

            List<string> lines = new List<string>(File.ReadAllLines(confModPath));

            int start = 0;
            int end = lines.Count - 1;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                int blockStart, blockEnd;

                if (!TryFindBlock(lines, segments[i], start, end, out blockStart, out blockEnd))
                {
                    FormUtils.LogRegister("CampInitUpdater | bloc '" + segments[i] + "' introuvable dans conf_mod.lua");
                    return false;
                }

                start = blockStart;
                end = blockEnd;
            }

            string literal = FormatLuaLiteral(value);

            if (!ReplaceValueToken(lines, start, end, key, literal))
            {
                FormUtils.LogRegister("CampInitUpdater | clé '" + key + "' introuvable dans conf_mod.lua");
                return false;
            }

            File.WriteAllLines(confModPath, lines.ToArray());
            return true;
        }

        private static string FormatLuaLiteral(object value)
        {
            if (value is bool)
                return ((bool)value) ? "true" : "false";

            if (value is double || value is int || value is long || value is float)
                return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

            return "\"" + value.ToString().Replace("\"", "") + "\"";
        }

        // ---------------------------------------------------------------
        // Reconstruction récursive du bloc camp, dans l'ordre de la référence
        // (même principe que ConfModTemplateUpdater.MergeBlockBody).
        // ---------------------------------------------------------------

        private static List<string> MergeBlockBody(List<string> refLines, ref int refIndex, List<string> localLines, int localStart, int localEnd, bool localExists)
        {
            var output = new List<string>();

            while (refIndex < refLines.Count)
            {
                string refLine = refLines[refIndex];
                string code = ConfUiSchemaParser.StripComment(refLine);

                if (IsCloseLine(code))
                    break;

                Match keyMatch = FieldKeyRegex.Match(code);

                if (!keyMatch.Success)
                {
                    output.Add(refLine);
                    refIndex++;
                    continue;
                }

                string key = keyMatch.Groups["bkey"].Success ? keyMatch.Groups["bkey"].Value : keyMatch.Groups["key"].Value;
                int delta = CountBraces(code);

                if (delta > 0)
                {
                    // Ce champ s'étend sur plusieurs lignes. On regarde si son contenu
                    // a au moins une ligne "clé = ..." : si oui c'est un vrai conteneur
                    // (ex: date), sinon ce sont des valeurs brutes (ex: pictureBrief.blue,
                    // une liste d'images sans "clé =" devant).
                    int refOpenIdx = refIndex;
                    int refCloseIdx = FindMatchingClose(refLines, refOpenIdx);

                    bool looksLikeContainer = false;

                    for (int i = refOpenIdx + 1; i < refCloseIdx; i++)
                    {
                        if (FieldKeyRegex.IsMatch(ConfUiSchemaParser.StripComment(refLines[i])))
                        {
                            looksLikeContainer = true;
                            break;
                        }
                    }

                    int localSubStart = -1, localSubEnd = -1;
                    bool localSubExists = localExists && TryFindBlock(localLines, key, localStart, localEnd, out localSubStart, out localSubEnd);

                    output.Add(refLine);
                    refIndex++;

                    List<string> inner;

                    if (looksLikeContainer)
                    {
                        inner = MergeBlockBody(refLines, ref refIndex, localLines, localSubStart, localSubEnd, localSubExists);
                    }
                    else
                    {
                        inner = localSubExists
                            ? localLines.GetRange(localSubStart + 1, localSubEnd - localSubStart - 1)
                            : refLines.GetRange(refIndex, refCloseIdx - refIndex);

                        refIndex = refCloseIdx;
                    }

                    output.AddRange(inner);
                    output.Add(refLines[refIndex]); // ligne de fermeture, prise dans la référence
                    refIndex++;
                    continue;
                }

                string mergedLine = refLine;

                if (localExists)
                {
                    int localSubStart, localSubEnd;

                    if (TryFindBlock(localLines, key, localStart, localEnd, out localSubStart, out localSubEnd))
                    {
                        for (int i = localSubStart; i <= localSubEnd; i++)
                            output.Add(localLines[i]);

                        refIndex++;
                        continue;
                    }

                    string localToken = FindValueToken(localLines, localStart, localEnd, key);

                    if (localToken != null)
                        mergedLine = ReplaceValueToken(refLine, key, localToken);
                }

                output.Add(mergedLine);
                refIndex++;
            }

            return output;
        }

        // ---------------------------------------------------------------
        // Petits outils texte (mêmes principes que dans ConfModTemplateUpdater,
        // gardés autonomes ici aussi).
        // ---------------------------------------------------------------

        private static readonly Regex FieldKeyRegex =
            new Regex(@"^\s*(?:\[""(?<bkey>[^""]+)""\]|(?<key>[A-Za-z_][A-Za-z0-9_]*))\s*=");

        private static bool IsCloseLine(string code)
        {
            return Regex.IsMatch(code, @"^\s*}\s*,?\s*$");
        }

        private static bool TryFindBlock(List<string> lines, string key, int start, int end, out int blockStart, out int blockEnd)
        {
            blockStart = -1;
            blockEnd = -1;

            var openRegex = new Regex(@"^\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*{");
            var splitOpenRegex = new Regex(@"^\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*$");

            for (int i = start; i <= end; i++)
            {
                string code = ConfUiSchemaParser.StripComment(lines[i]);

                if (openRegex.IsMatch(code))
                {
                    blockStart = i;
                    break;
                }

                if (splitOpenRegex.IsMatch(code))
                {
                    int braceIdx = FindNextBraceLine(lines, i, end);

                    if (braceIdx >= 0)
                    {
                        blockStart = braceIdx;
                        break;
                    }
                }
            }

            if (blockStart < 0)
                return false;

            blockEnd = FindMatchingClose(lines, blockStart);
            return blockEnd >= 0;
        }

        private static int FindNextBraceLine(List<string> lines, int fromIndex, int end)
        {
            for (int i = fromIndex + 1; i <= end; i++)
            {
                string trimmed = ConfUiSchemaParser.StripComment(lines[i]).Trim();

                if (trimmed.Length == 0)
                    continue;

                return trimmed == "{" ? i : -1;
            }

            return -1;
        }

        private static int FindMatchingClose(List<string> lines, int openIndex)
        {
            int depth = CountBraces(ConfUiSchemaParser.StripComment(lines[openIndex]));

            if (depth <= 0)
                return openIndex; // construction déjà refermée sur sa propre ligne

            for (int i = openIndex + 1; i < lines.Count; i++)
            {
                depth += CountBraces(ConfUiSchemaParser.StripComment(lines[i]));

                if (depth <= 0)
                    return i;
            }

            return -1;
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

        private static bool ReplaceValueToken(List<string> lines, int start, int end, string key, string newToken)
        {
            var regex = new Regex(@"^(\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*)(""[^""]*""|\{[^{}]*\}|[^\s,]+)(.*)$");

            for (int i = start; i <= end; i++)
            {
                Match m = regex.Match(lines[i]);

                if (m.Success)
                {
                    lines[i] = m.Groups[1].Value + newToken + m.Groups[3].Value;
                    return true;
                }
            }

            return false;
        }

        private static string ReplaceValueToken(string line, string key, string newToken)
        {
            var regex = new Regex(@"^(\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*)(""[^""]*""|\{[^{}]*\}|[^\s,]+)(.*)$");
            Match m = regex.Match(line);

            if (!m.Success)
                return line;

            return m.Groups[1].Value + newToken + m.Groups[3].Value;
        }
    }
}
