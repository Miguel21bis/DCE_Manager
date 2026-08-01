using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Statut renvoyé par UpdateCampaign : permet à l'appelant de distinguer "référence
    // introuvable" (ScriptsMod pas à jour) d'un "échec de fusion" (bug interne, pas la
    // même cause, pas le même message à montrer à l'utilisateur).
    internal enum ConfUpdateResult
    {
        Ok,
        ReferenceMissing,
        MergeAborted
    }

    // Reconstruit entièrement conf_mod.lua à partir de UTIL_REF_conf_mod.lua : le
    // fichier final prend l'architecture du template (structure, ordre, commentaires,
    // tags @ui) de bout en bout, et va chercher dans l'ancien fichier local la valeur
    // de chaque champ quand elle existe déjà - sinon il garde la valeur par défaut de
    // la référence. Tout ce que l'ancien fichier contenait mais qui n'existe pas dans
    // la référence (anciennes tables, commentaires isolés, pictureBrief...) disparaît
    // simplement, puisqu'on ne le recopie jamais : on ne part plus du fichier local
    // pour le "nettoyer", on part de la référence et on va juste piocher les valeurs.
    //
    // Les 4 tables de premier niveau de la référence (mission_ini_check,
    // mission_forcedOptions_check, Debug_check, campMod_check) sont renommées à la
    // volée en enlevant le suffixe "_check" (générique : n'importe quelle clé qui se
    // termine par "_check" est traitée pareil, pas besoin d'une liste figée).
    //
    // Un champ peut s'étendre sur plusieurs lignes sans que la ligne d'ouverture se
    // termine forcément "toute seule" par une accolade (ex: "runway = {0, 20, 0, 0,
    // 25, 50" suivi de "}," sur la ligne d'après, ou "clé =" seule puis "{" sur la
    // ligne suivante) : on détecte ça en comptant les accolades plutôt que par un
    // format figé.
    internal class ConfModTemplateUpdater
    {
        private const string CheckSuffix = "_check";

        private readonly ConfModLoader _loader = new ConfModLoader();

        public string GetReferencePath()
        {
            return Path.Combine(
                ParamConf.PATH_SavedGames_DCS,
                @"Mods\tech\DCE\ScriptsMod.NG",
                "UTIL_REF_conf_mod.lua");
        }

        // Met à jour une seule campagne. Appelée à l'ouverture d'une campagne
        // (Config, génération de mission) ou en boucle par UpdateAllCampaigns.
        public ConfUpdateResult UpdateCampaign(string campaignName)
        {
            string localPath = _loader.GetConfModPath(campaignName);
            string refPath = GetReferencePath();

            if (!File.Exists(localPath))
            {
                FormUtils.LogRegister("ConfModTemplateUpdater | conf_mod.lua introuvable pour " + campaignName);
                return ConfUpdateResult.Ok; // pas un problème de référence, rien à signaler à ce sujet
            }

            if (!File.Exists(refPath))
            {
                FormUtils.LogRegister("ConfModTemplateUpdater | UTIL_REF_conf_mod.lua introuvable : " + refPath);
                return ConfUpdateResult.ReferenceMissing;
            }

            List<string> localLines = new List<string>(File.ReadAllLines(localPath));
            List<string> refLines = new List<string>(File.ReadAllLines(refPath));

            int cursor = 0;
            List<string> newFile = MergeBlockBody(refLines, ref cursor, localLines, 0, localLines.Count - 1, true);

            int braceBalance = 0;

            foreach (string line in newFile)
                braceBalance += CountBraces(ConfUiSchemaParser.StripComment(line));

            if (braceBalance != 0)
            {
                FormUtils.LogRegister("ConfModTemplateUpdater | ABANDON, accolades déséquilibrées (delta=" + braceBalance + ") pour " + campaignName + " - aucune écriture faite, fichier local inchangé");
                return ConfUpdateResult.MergeAborted;
            }

            if (!SameContent(newFile, localLines))
            {
                File.WriteAllLines(localPath, newFile.ToArray());
                _loader.InvalidateCache(campaignName);
                FormUtils.LogRegister("ConfModTemplateUpdater | conf_mod.lua mis à jour pour " + campaignName);
            }

            return ConfUpdateResult.Ok;
        }

        // Pour le bouton "Update conf_mod" : refait la même chose sur toutes les campagnes.
        public void UpdateAllCampaigns(IEnumerable<string> campaignNames)
        {
            foreach (string name in campaignNames)
                UpdateCampaign(name);
        }

        private static bool SameContent(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        // ---------------------------------------------------------------
        // Reconstruction récursive, dans l'ordre de la référence. Utilisée aussi bien
        // pour le fichier entier (localStart=0, localEnd=dernière ligne) que pour un
        // sous-bloc (weather, RepairOption.blue...). refIndex pointe sur la première
        // ligne à traiter côté référence, et se retrouve, à la sortie, sur sa ligne de
        // fermeture (ou à la fin du fichier s'il n'y en a pas, cas du niveau racine).
        // ---------------------------------------------------------------

        private List<string> MergeBlockBody(List<string> refLines, ref int refIndex, List<string> localLines, int localStart, int localEnd, bool localExists)
        {
            var output = new List<string>();

            while (refIndex < refLines.Count)
            {
                string refLine = refLines[refIndex];
                string code = ConfUiSchemaParser.StripComment(refLine);

                if (IsCloseLine(code))
                    break; // on laisse l'appelant gérer la ligne de fermeture

                Match keyMatch = FieldKeyRegex.Match(code);

                if (!keyMatch.Success)
                {
                    // commentaire ou ligne vide : on garde celle de la référence
                    output.Add(refLine);
                    refIndex++;
                    continue;
                }

                string key = keyMatch.Groups["bkey"].Success ? keyMatch.Groups["bkey"].Value : keyMatch.Groups["key"].Value;

                // Une clé de la référence en "xxx_check" correspond à "xxx" côté client
                // (mission_ini_check -> mission_ini, etc.) - générique, pas de liste figée.
                string localKey = key.EndsWith(CheckSuffix, StringComparison.OrdinalIgnoreCase)
                    ? key.Substring(0, key.Length - CheckSuffix.Length)
                    : key;

                int delta = CountBraces(code);

                if (delta > 0)
                {
                    // Ce champ s'étend sur plusieurs lignes (peu importe si la ligne
                    // d'ouverture se termine "seule" ou a déjà des valeurs dessus).
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
                    bool localSubExists = localExists && TryFindBlock(localLines, localKey, localStart, localEnd, out localSubStart, out localSubEnd);

                    if (looksLikeContainer)
                    {
                        // Vrai conteneur (des "clé =" à l'intérieur, ex: weather, date) :
                        // on garde la ligne d'ouverture de la référence, et on fusionne
                        // le contenu récursivement, clé par clé.
                        output.Add(key == localKey ? refLine : RenameKeyToken(refLine, key, localKey));
                        refIndex++;

                        List<string> inner = MergeBlockBody(refLines, ref refIndex, localLines, localSubStart, localSubEnd, localSubExists);

                        output.AddRange(inner);
                        output.Add(refLines[refIndex]); // ligne de fermeture, prise dans la référence
                        refIndex++;
                    }
                    else
                    {
                        // Valeurs brutes, pas de "clé =" à l'intérieur (ex: runway) : on
                        // prend le bloc EN ENTIER (ouverture + valeurs + fermeture) d'un
                        // seul côté - celui du client s'il existe déjà, sinon celui de la
                        // référence - jamais un mélange des deux, qui casse dès que le
                        // découpage en lignes ne correspond pas exactement (ex: le client
                        // a tout mis sur une seule ligne, la référence sur plusieurs).
                        if (localSubExists)
                        {
                            for (int i = localSubStart; i <= localSubEnd; i++)
                                output.Add(localLines[i]);
                        }
                        else
                        {
                            for (int i = refOpenIdx; i <= refCloseIdx; i++)
                                output.Add(refLines[i]);
                        }

                        refIndex = refCloseIdx + 1;
                    }

                    continue;
                }

                // delta <= 0 : ligne "clé = valeur" simple (scalaire, ou tableau
                // entièrement sur une seule ligne)
                string mergedLine = refLine;

                if (localExists)
                {
                    int localSubStart, localSubEnd;

                    // Cas où la clé est un simple scalaire côté référence, mais un
                    // bloc multi-lignes côté client.
                    if (TryFindBlock(localLines, localKey, localStart, localEnd, out localSubStart, out localSubEnd))
                    {
                        for (int i = localSubStart; i <= localSubEnd; i++)
                            output.Add(localLines[i]);

                        refIndex++;
                        continue;
                    }

                    string localToken = FindValueToken(localLines, localStart, localEnd, localKey);

                    if (localToken != null)
                        mergedLine = ReplaceValueToken(refLine, key, localToken);
                }

                output.Add(mergedLine);
                refIndex++;
            }

            return output;
        }

        // Renomme uniquement le jeton de clé en tout début de ligne (ex: transforme
        // "mission_ini_check = {" en "mission_ini = {"), sans toucher au reste.
        private static string RenameKeyToken(string line, string oldKey, string newKey)
        {
            return Regex.Replace(line, @"^(\s*)" + Regex.Escape(oldKey) + @"(\s*=)", "$1" + newKey.Replace("$", "$$") + "$2");
        }

        // ---------------------------------------------------------------
        // Petits outils texte
        // ---------------------------------------------------------------

        // capture juste la clé en début de ligne, peu importe ce qu'il y a après le "="
        private static readonly Regex FieldKeyRegex =
            new Regex(@"^\s*(?:\[""(?<bkey>[^""]+)""\]|(?<key>[A-Za-z_][A-Za-z0-9_]*))\s*=");

        private static bool IsCloseLine(string code)
        {
            return Regex.IsMatch(code, @"^\s*}\s*,?\s*$");
        }

        // Cherche "key = {" dans [start, end] et sa fermeture, en suivant la
        // profondeur des accolades (pas juste la première "}" rencontrée). Accepte
        // aussi le cas où "key =" est seule sur sa ligne et l'accolade ouvrante arrive
        // sur une ligne suivante - dans ce cas, blockStart pointe sur la ligne de
        // l'accolade elle-même (pas sur la ligne "key ="), pour que la ligne "key ="
        // reste intacte et que le calcul de ce qui doit être retiré reste juste.
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

        // Renvoie l'index de la prochaine ligne non vide/non commentée si c'est
        // exactement "{" (accolade seule sur sa ligne), sinon -1.
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
