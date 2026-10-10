using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Un trigger de campagne tel qu'il est lu dans Init\camp_triggers_init.lua.
    // Étape 3 : les champs simples (nom, active, once, textes du briefing) peuvent être modifiés puis enregistrés (voir CampTriggersFile.Save).
    // Les textes sont gardés tels quels, la décomposition est à côté.
    public class CampTrigger
    {
        public string Name = "";
        public int ReadProblemCount;            // nombre de remarques venues de la lecture du fichier (les suivantes viennent des contrôles)
        public bool NameGenerated;              // true = pas de champ 'name' dans le fichier : le nom affiché est inventé, on ne l'écrit pas
        public string KeyName;                  // clé d'origine si le fichier est une table nommée, sinon null
        public bool HasActive;                  // false = le champ 'active' n'existe pas dans le fichier
        public bool Active;
        public bool? Once;                      // null = champ absent (le trigger se rejoue à chaque mission)
        public bool HasCondition;
        public string Condition = "";
        public bool HasAction;
        public List<string> Actions = new List<string>();
        public bool ActionWasSingleString;      // action = 'Action.X(...)' au lieu de { 'Action.X(...)' }
        public string ExpiresText;              // null = absent ; sinon le contenu rendu en texte Lua
        public List<KeyValuePair<string, string>> OtherKeys = new List<KeyValuePair<string, string>>(); // clés inconnues, à conserver

        // Remarques : chaque ligne commence par "ERROR: ", "Warning: " ou "Note: " (voir TriggerChecker).
        public List<string> Problems = new List<string>();

        // Les mêmes remarques, rangées par numéro d'action (0 = première action) : pour marquer la ligne fautive dans THEN.
        public Dictionary<int, List<string>> ActionProblems = new Dictionary<int, List<string>>();

        public string ActionProblemText(int index)
        {
            List<string> list;
            return ActionProblems.TryGetValue(index, out list) ? string.Join("\r\n", list.Select(x => Lang.Problem(x)).ToArray()) : "";
        }

        // Résultat de la décomposition, rempli par TriggerChecker.Run. null = texte non décomposé (Lua brut).
        public LuaNode ConditionNode;
        public List<LuaNode> ActionNodes = new List<LuaNode>();   // même ordre que Actions

        // Copie indépendante (autre nom). Les remarques sont refaites par CampTriggersFile.Recheck().
        public CampTrigger Clone(string newName)
        {
            var c = new CampTrigger();
            c.Name = newName;
            c.KeyName = null;
            c.NameGenerated = false;
            c.HasActive = HasActive;
            c.Active = Active;
            c.Once = Once;
            c.HasCondition = HasCondition;
            c.Condition = Condition;
            c.HasAction = HasAction;
            c.Actions = new List<string>(Actions);
            c.ActionWasSingleString = false;
            c.ExpiresText = ExpiresText;
            c.OtherKeys = new List<KeyValuePair<string, string>>(OtherKeys);
            c.ReadProblemCount = 0;
            return c;
        }

        public bool ConditionIsRaw
        {
            get { return HasCondition && Condition.Trim().Length > 0 && !IsKnown(ConditionNode); }
        }

        public bool ActionIsRaw(int index)
        {
            return index >= ActionNodes.Count || !IsKnown(ActionNodes[index]);
        }

        private static bool IsKnown(LuaNode node)
        {
            return node != null && node.IsFullyKnown();
        }

        // Nombre de textes (condition + actions) qu'on ne sait pas décomposer en champs.
        public int RawStringCount
        {
            get
            {
                int n = ConditionIsRaw ? 1 : 0;

                for (int i = 0; i < Actions.Count; i++)
                {
                    if (Actions[i].Trim().Length > 0 && ActionIsRaw(i))
                        n++;
                }

                return n;
            }
        }

        public int ErrorCount
        {
            get { return Problems.Count(p => p.StartsWith(TriggerChecker.ErrorTag, StringComparison.Ordinal)); }
        }

        public int WarningCount
        {
            get { return Problems.Count(p => p.StartsWith(TriggerChecker.WarnTag, StringComparison.Ordinal)); }
        }

        public int NoteCount
        {
            get { return Problems.Count(p => p.StartsWith(TriggerChecker.NoteTag, StringComparison.Ordinal)); }
        }

        public bool MatchesFilter(string filter)
        {
            if (Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (Condition.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            foreach (string a in Actions)
            {
                if (a.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        // Recherche par NOM, indulgente : tous les mots tapés doivent être dans le nom (dans n'importe quel ordre),
        // sans tenir compte des majuscules ni des accents ; une petite faute de frappe est pardonnée sur les mots de 4 lettres et plus.
        public bool MatchesName(string query)
        {
            string name = Fold(Name);
            string[] nameWords = name.Split(new[] { ' ', '-', '.', ',', '(', ')', ':', '/' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string word in Fold(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (name.IndexOf(word, StringComparison.Ordinal) >= 0)
                    continue;

                if (word.Length >= 4 && nameWords.Any(w => IsNear(word, w)))
                    continue;

                return false;
            }

            return true;
        }

        // Recherche par FONCTION DCE : tous les mots tapés doivent apparaître dans la condition ou dans les actions
        // (ex. "TargetActive", "Action.TargetActive", "Return.DatePassed 1972").
        public bool MatchesFunction(string query)
        {
            foreach (string word in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Condition.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (Actions.Any(a => a.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                return false;
            }

            return true;
        }

        // minuscules, sans accents, "_" = espace
        private static string Fold(string text)
        {
            string d = (text ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);

            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                sb.Append(c == '_' ? ' ' : char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        // Le mot tapé est "proche" d'un mot du nom (ou du début de ce mot) : une lettre de différence (deux pour les mots longs).
        private static bool IsNear(string word, string candidate)
        {
            int allowed = word.Length >= 7 ? 2 : 1;

            if (Distance(word, candidate) <= allowed)
                return true;

            return candidate.Length > word.Length && Distance(word, candidate.Substring(0, word.Length)) <= allowed;
        }

        // Distance de Levenshtein (nombre de lettres à changer / ajouter / retirer).
        private static int Distance(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 2)
                return 99;

            var row = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++)
                row[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                int diagonal = row[0];
                row[0] = i;

                for (int j = 1; j <= b.Length; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }

            return row[b.Length];
        }

        // Texte affiché dans la liste de gauche : ⚠ + le nombre de problèmes (erreurs et avertissements).
        public override string ToString()
        {
            return (Active ? "\u2714 " : "\u2716 ") + Name;      // le \u26A0 est dessiné devant la ligne par la liste (le nom peut être coupé)
        }
    }

    // Le contenu complet d'un camp_triggers_init.lua.
    public class CampTriggersFile
    {
        public bool SourceHasBom;
        public bool SourceIsUtf8 = true;
        public int CommentLineCount;            // lignes du fichier d'origine qui contiennent un commentaire (perdus à l'écriture)

        public string FilePath = "";
        public string Format = "";              // "list", "named table", "mixed", "empty"
        public string LoadError;                // null = lecture OK
        public bool ActiveCopyExists;           // Active\camp_triggers.lua présent
        public List<CampTrigger> Triggers = new List<CampTrigger>();
        public List<string> Problems = new List<string>();      // remarques sur le fichier lui-même (celles des triggers sont dans chaque trigger)
        public TriggerLists Lists;                              // noms existants dans la campagne (cibles, escadrilles, bases)

        public int RawStringCount
        {
            get { return Triggers.Sum(t => t.RawStringCount); }
        }

        public int ErrorCount
        {
            get { return Triggers.Sum(t => t.ErrorCount) + Problems.Count(p => p.StartsWith(TriggerChecker.ErrorTag, StringComparison.Ordinal)); }
        }

        public int WarningCount
        {
            get { return Triggers.Sum(t => t.WarningCount) + Problems.Count(p => p.StartsWith(TriggerChecker.WarnTag, StringComparison.Ordinal)); }
        }

        public int NoteCount
        {
            get { return Triggers.Sum(t => t.NoteCount) + Problems.Count(p => p.StartsWith(TriggerChecker.NoteTag, StringComparison.Ordinal)); }
        }

        // Toutes les remarques du fichier et des triggers, en une liste de lignes (sans les simples notes).
        public List<string> AllProblems(bool includeNotes)
        {
            var all = new List<string>();

            foreach (string p in Problems)
            {
                if (includeNotes || !p.StartsWith(TriggerChecker.NoteTag, StringComparison.Ordinal))
                    all.Add(p);
            }

            foreach (CampTrigger t in Triggers)
            {
                foreach (string p in t.Problems)
                {
                    if (includeNotes || !p.StartsWith(TriggerChecker.NoteTag, StringComparison.Ordinal))
                        all.Add("'" + t.Name + "' - " + p);
                }
            }

            return all;
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Lit Init\camp_triggers_init.lua. Ne lève jamais d'exception : tout problème va dans LoadError.
        // Le lecteur avale les 3 formes rencontrées :
        //   - liste        { { name = ..., ... }, { ... } }             (ordre du fichier gardé)
        //   - table nommée { ["Nom"] = { ... }, ["Autre"] = { ... } }   (ordre alphabétique, faute de mieux)
        //   - forme sérialisée par le moteur ( ['name'] = ..., [1] = ... ) : c'est la même chose pour Lua
        public static CampTriggersFile Load(string campaignName)
        {
            var result = new CampTriggersFile();
            result.FilePath = DcemLua.CampaignInitFile(campaignName, "camp_triggers_init.lua");
            result.ActiveCopyExists = File.Exists(System.IO.Path.Combine(DcemLua.CampaignPath(campaignName), "Active", "camp_triggers.lua"));

            if (!File.Exists(result.FilePath))
            {
                result.LoadError = Lang.T("File not found: ") + result.FilePath;
                return result;
            }

            result.InspectSource();

            try
            {
                using (Lua lua = DcemLua.NewState())
                {
                    // Même bac à sable que ConfModForm : un fichier de données n'a rien à faire avec le disque.
                    lua.DoString("os = nil\nio = nil\nfile = nil\ndebug = nil");
                    lua.DoFile(result.FilePath);

                    LuaTable root = lua["camp_triggers"] as LuaTable;

                    if (root == null)
                    {
                        result.LoadError = Lang.T("The file does not define a table named 'camp_triggers'.");
                        return result;
                    }

                    result.ReadRoot(root);
                }

                result.Lists = TriggerLists.Load(campaignName);
                TriggerChecker.Run(result);
            }
            catch (Exception ex)
            {
                result.LoadError = ex.Message;
                FormUtils.LogRegister("CampTriggersFile | lecture impossible de " + result.FilePath + " : " + ex.Message);
            }

            return result;
        }

        // ----------------------------------------------------------------------------------------
        //  ÉCRITURE (étape 3)
        // ----------------------------------------------------------------------------------------

        // Regarde le fichier d'origine : BOM, UTF-8 valide ?, combien de lignes de commentaires.
        private void InspectSource()
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(FilePath);
                SourceHasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                int skip = SourceHasBom ? 3 : 0;

                string text = new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip);
                CommentLineCount = Regex.Matches(text, @"^[^\r\n""']*--", RegexOptions.Multiline).Count;
            }
            catch (DecoderFallbackException)
            {
                SourceIsUtf8 = false;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("CampTriggersFile | inspection impossible de " + FilePath + " : " + ex.Message);
            }
        }

        // Remarques de lecture qui veulent dire "une donnée n'a pas pu être lue telle quelle" : écrire la perdrait.
        private static readonly string[] LossMarkers =
        {
            "(ignored)", "ignored.", "is not a text", "is not true/false", "neither a text nor a list", "unsupported"
        };

        // Peut-on enregistrer sans rien perdre ? Sinon 'reason' explique pourquoi.
        public bool CanSave(out string reason)
        {
            reason = null;

            if (LoadError != null)
            {
                reason = Lang.T("The file could not be read, so it cannot be saved.");
                return false;
            }

            if (!SourceIsUtf8)
            {
                reason = Lang.T("The file is not saved as UTF-8. Saving would damage the accented letters.\r\nConvert it to UTF-8 first (for example with Notepad++).");
                return false;
            }

            foreach (string p in Problems)
            {
                if (LossMarkers.Any(m => p.Contains(m)))
                {
                    reason = Lang.T("Saving would lose data that could not be read:\r\n") + Lang.Problem(p);
                    return false;
                }
            }

            foreach (CampTrigger t in Triggers)
            {
                foreach (string p in t.Problems.Take(t.ReadProblemCount))
                {
                    if (LossMarkers.Any(m => p.Contains(m)))
                    {
                        reason = Lang.T("Saving would lose data that could not be read, in '") + t.Name + "':\r\n" + Lang.Problem(p)
                            + Lang.T("\r\nFix this trigger in the Lua file first.");
                        return false;
                    }
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (CampTrigger t in Triggers)
            {
                if (t.NameGenerated)
                    continue;

                if (t.Name.Trim().Length == 0)
                {
                    reason = Lang.T("A trigger has an empty name.");
                    return false;
                }

                if (!seen.Add(t.Name))
                {
                    reason = Lang.T("Two triggers have the same name: '") + t.Name + Lang.T("'. Names must be unique. Rename one of them.");
                    return false;
                }
            }

            return true;
        }

        // Le fichier au format "liste" : { { name=..., active=..., once=..., condition=..., action={...}, expires=... }, ... }
        // Les textes sont réécrits tels quels (même valeur Lua), les clés inconnues sont conservées.
        public string ToLuaText()
        {
            int start, length;
            return ToLuaText(null, out start, out length);
        }

        // Pareil, et dit où se trouve le bloc de "focus" dans le texte produit (pour que l'onglet CODE s'ouvre dessus).
        public string ToLuaText(CampTrigger focus, out int focusStart, out int focusLength)
        {
            const string nl = "\r\n";
            var sb = new StringBuilder();

            focusStart = 0;
            focusLength = 0;

            sb.Append("camp_triggers =" + nl + "{" + nl);

            foreach (CampTrigger t in Triggers)
            {
                int blockStart = sb.Length;

                sb.Append("\t{" + nl);

                if (!t.NameGenerated)
                    sb.Append("\t\tname = " + LuaText.Quote(t.Name) + "," + nl);

                if (t.HasActive)
                    sb.Append("\t\tactive = " + (t.Active ? "true" : "false") + "," + nl);

                if (t.Once != null)
                    sb.Append("\t\tonce = " + (t.Once.Value ? "true" : "false") + "," + nl);

                if (t.HasCondition)
                    sb.Append("\t\tcondition = " + LuaText.Quote(t.Condition) + "," + nl);

                if (t.HasAction)
                {
                    sb.Append("\t\taction =" + nl + "\t\t{" + nl);

                    foreach (string a in t.Actions)
                        sb.Append("\t\t\t" + LuaText.Quote(a) + "," + nl);

                    sb.Append("\t\t}," + nl);
                }

                if (t.ExpiresText != null)
                    sb.Append("\t\texpires = " + t.ExpiresText + "," + nl);

                foreach (KeyValuePair<string, string> kv in t.OtherKeys)
                {
                    string key = kv.Key;

                    if (!key.StartsWith("[", StringComparison.Ordinal) && !Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                        key = "[" + LuaText.Quote(key) + "]";

                    sb.Append("\t\t" + key + " = " + kv.Value + "," + nl);
                }

                sb.Append("\t}," + nl);

                if (focus != null && ReferenceEquals(t, focus))
                {
                    focusStart = blockStart;
                    focusLength = Math.Max(0, sb.Length - blockStart - nl.Length);     // sans le dernier retour à la ligne
                }
            }

            sb.Append("}" + nl);

            return sb.ToString();
        }

        // Enregistre. Étapes : contrôles -> copies de sécurité -> écriture d'un fichier temporaire -> relecture et
        // comparaison avec ce qu'on voulait écrire -> seulement alors remplacement du vrai fichier.
        // Si quelque chose ne va pas, le fichier d'origine n'a pas été touché.
        // Copies : ".bak" = le tout premier original (jamais écrasé) ; ".prev" = la version d'avant cet enregistrement.
        public bool Save(out string error)
        {
            if (!CanSave(out error))
                return false;

            string tmp = FilePath + ".tmp";

            try
            {
                string bak = FilePath + ".bak";

                if (!File.Exists(bak))
                    File.Copy(FilePath, bak);

                File.Copy(FilePath, FilePath + ".prev", true);

                File.WriteAllText(tmp, ToLuaText(), new UTF8Encoding(SourceHasBom));

                CampTriggersFile check = ParseOnly(tmp);
                string difference = check.LoadError != null
                    ? Lang.T("the new file cannot be read back: ") + check.LoadError
                    : FirstDifference(check);

                if (difference != null)
                {
                    File.Delete(tmp);
                    error = Lang.T("Safety check failed, nothing was changed (") + difference + ").";
                    return false;
                }

                File.Copy(tmp, FilePath, true);
                File.Delete(tmp);
                return true;
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("CampTriggersFile | enregistrement impossible de " + FilePath + " : " + ex.Message);

                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }

                error = Lang.T("Could not write the file: ") + ex.Message;
                return false;
            }
        }

        // Lit un fichier de triggers SANS contrôles (juste pour comparer).
        private static CampTriggersFile ParseOnly(string path)
        {
            var r = new CampTriggersFile();
            r.FilePath = path;

            try
            {
                using (Lua lua = DcemLua.NewState())
                {
                    lua.DoString("os = nil\nio = nil\nfile = nil\ndebug = nil");
                    lua.DoFile(path);

                    LuaTable root = lua["camp_triggers"] as LuaTable;

                    if (root == null)
                        r.LoadError = "no 'camp_triggers' table";
                    else
                        r.ReadRoot(root);
                }
            }
            catch (Exception ex)
            {
                r.LoadError = ex.Message;
            }

            return r;
        }

        // Compare le fichier relu avec le modèle : null = identique.
        private string FirstDifference(CampTriggersFile other)
        {
            if (other.Triggers.Count != Triggers.Count)
                return "trigger count " + Triggers.Count + " -> " + other.Triggers.Count;

            for (int i = 0; i < Triggers.Count; i++)
            {
                CampTrigger a = Triggers[i];
                CampTrigger b = other.Triggers[i];
                string who = "'" + a.Name + "'";

                if (!a.NameGenerated && a.Name != b.Name) return who + ": name";
                if (a.HasActive != b.HasActive || a.Active != b.Active) return who + ": active";
                if (a.Once != b.Once) return who + ": once";
                if (a.HasCondition != b.HasCondition || a.Condition != b.Condition) return who + ": condition";
                if (a.HasAction != b.HasAction || !a.Actions.SequenceEqual(b.Actions)) return who + ": action";
                if (a.ExpiresText != b.ExpiresText) return who + ": expires";
                if (a.OtherKeys.Count != b.OtherKeys.Count) return who + ": other keys";

                for (int k = 0; k < a.OtherKeys.Count; k++)
                {
                    if (a.OtherKeys[k].Key != b.OtherKeys[k].Key || a.OtherKeys[k].Value != b.OtherKeys[k].Value)
                        return who + ": other keys";
                }
            }

            return null;
        }

        private void ReadRoot(LuaTable root)
        {
            var numericKeys = new List<KeyValuePair<double, object>>();
            var namedKeys = new List<KeyValuePair<string, object>>();

            foreach (object k in root.Keys)
            {
                double index;

                if (TryGetIndex(k, out index))
                    numericKeys.Add(new KeyValuePair<double, object>(index, k));
                else
                    namedKeys.Add(new KeyValuePair<string, object>(Convert.ToString(k, Inv), k));
            }

            numericKeys.Sort((a, b) => a.Key.CompareTo(b.Key));
            namedKeys.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));

            if (numericKeys.Count > 0 && namedKeys.Count > 0) Format = "mixed (list + named entries)";
            else if (numericKeys.Count > 0) Format = "list";
            else if (namedKeys.Count > 0) Format = "named table (will become a list when saved)";
            else Format = "empty";

            int position = 0;

            foreach (KeyValuePair<double, object> kv in numericKeys)
            {
                position++;
                AddEntry(null, root[kv.Value], position, "[" + kv.Key.ToString("R", Inv) + "]");
            }

            foreach (KeyValuePair<string, object> kv in namedKeys)
            {
                position++;
                AddEntry(kv.Key, root[kv.Value], position, "[\"" + kv.Key + "\"]");
            }

            MarkDuplicates();
        }

        // Nom unique obligatoire : le 2e trigger de même nom reçoit une remarque.
        private void MarkDuplicates()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (CampTrigger t in Triggers)
            {
                if (!seen.Add(t.Name))
                    t.Problems.Add(TriggerChecker.WarnTag + "Duplicate name: another trigger is also called '" + t.Name + "'.");
            }
        }

        // Refait tous les contrôles après une modification (les remarques de lecture, elles, sont gardées).
        public void Recheck()
        {
            foreach (CampTrigger t in Triggers)
            {
                t.ActionProblems.Clear();

                if (t.Problems.Count > t.ReadProblemCount)
                    t.Problems.RemoveRange(t.ReadProblemCount, t.Problems.Count - t.ReadProblemCount);
            }

            MarkDuplicates();
            TriggerChecker.Run(this);
        }

        private void AddEntry(string key, object value, int position, string where)
        {
            LuaTable table = value as LuaTable;

            if (table == null)
            {
                Problems.Add(TriggerChecker.WarnTag + "Entry " + where + " is not a table, ignored.");
                return;
            }

            CampTrigger t = ReadTrigger(key, table, position);

            Triggers.Add(t);
        }

        private static CampTrigger ReadTrigger(string key, LuaTable table, int position)
        {
            var t = new CampTrigger();
            t.KeyName = key;

            string nameField = null;

            foreach (object k in table.Keys)
            {
                string field = k as string;
                object v = table[k];

                if (field == null)
                {
                    t.OtherKeys.Add(new KeyValuePair<string, string>("[" + Convert.ToString(k, Inv) + "]", ValueToText(v)));
                    continue;
                }

                switch (field)
                {
                    case "name":
                        nameField = v as string;
                        if (nameField == null)
                            t.Problems.Add(TriggerChecker.WarnTag + "'name' is not a text.");
                        break;

                    case "active":
                        t.HasActive = true;
                        if (v is bool) t.Active = (bool)v;
                        else t.Problems.Add(TriggerChecker.WarnTag + "'active' is not true/false.");
                        break;

                    case "once":
                        if (v is bool) t.Once = (bool)v;
                        else t.Problems.Add(TriggerChecker.WarnTag + "'once' is not true/false.");
                        break;

                    case "condition":
                        t.HasCondition = true;
                        if (v is string) t.Condition = (string)v;
                        else
                        {
                            t.Condition = ValueToText(v);
                            t.Problems.Add(TriggerChecker.ErrorTag + "'condition' is not a text (shown as Lua).");
                        }
                        break;

                    case "action":
                        ReadActions(t, v);
                        break;

                    case "expires":
                        t.ExpiresText = ValueToText(v);
                        CheckExpires(t, v);
                        break;

                    default:
                        t.OtherKeys.Add(new KeyValuePair<string, string>(field, ValueToText(v)));
                        break;
                }
            }

            t.OtherKeys.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            // Le nom : la clé gagne dans une table nommée (c'est elle que le moteur utilise).
            if (key != null)
            {
                if (nameField != null && nameField != key)
                    t.Problems.Add(TriggerChecker.WarnTag + "Key '" + key + "' differs from the 'name' field '" + nameField + "' (the key is kept).");

                t.Name = key;
            }
            else if (!string.IsNullOrEmpty(nameField))
            {
                t.Name = nameField;
            }
            else
            {
                t.NameGenerated = true;
                t.Name = "(unnamed #" + position.ToString(Inv) + ")";
                t.Problems.Add(TriggerChecker.WarnTag + "No 'name' field.");
            }

            t.ReadProblemCount = t.Problems.Count;
            return t;
        }

        private static void ReadActions(CampTrigger t, object v)
        {
            t.HasAction = true;

            if (v is string)
            {
                t.ActionWasSingleString = true;
                t.Actions.Add((string)v);
                return;
            }

            LuaTable list = v as LuaTable;

            if (list == null)
            {
                t.Problems.Add(TriggerChecker.ErrorTag + "'action' is neither a text nor a list.");
                return;
            }

            var items = new List<KeyValuePair<double, object>>();

            foreach (object k in list.Keys)
            {
                double index;

                if (!TryGetIndex(k, out index))
                {
                    t.Problems.Add(TriggerChecker.WarnTag + "'action' contains a non numeric key '" + Convert.ToString(k, Inv) + "' (ignored).");
                    continue;
                }

                items.Add(new KeyValuePair<double, object>(index, list[k]));
            }

            items.Sort((a, b) => a.Key.CompareTo(b.Key));

            foreach (KeyValuePair<double, object> item in items)
            {
                if (item.Value is string)
                {
                    t.Actions.Add((string)item.Value);
                }
                else
                {
                    t.Actions.Add(ValueToText(item.Value));
                    t.Problems.Add(TriggerChecker.ErrorTag + "An action is not a text (shown as Lua).");
                }
            }
        }

        // expires : le moteur attend une table { day = .., month = .., year = .. } (ou, ancien format, son texte).
        // Sans les trois nombres il l'ignore, et le trigger n'expire jamais.
        private static void CheckExpires(CampTrigger t, object v)
        {
            if (v is string)
            {
                t.Problems.Add(TriggerChecker.NoteTag + "'expires' is written as a text (old form). The engine converts it.");
                return;
            }

            LuaTable e = v as LuaTable;

            if (e == null)
            {
                t.Problems.Add(TriggerChecker.WarnTag + "'expires' should be a table { day = .., month = .., year = .. }: the engine ignores it.");
                return;
            }

            double day, month, year;

            if (!TryGetIndex(e["day"], out day) || !TryGetIndex(e["month"], out month) || !TryGetIndex(e["year"], out year))
            {
                t.Problems.Add(TriggerChecker.WarnTag + "'expires' needs day, month and year as numbers: the engine ignores it.");
                return;
            }

            if (month < 1 || month > 12 || day < 1 || day > 31)
                t.Problems.Add(TriggerChecker.WarnTag + "'expires' is not a real date (day " + day.ToString(Inv) + ", month " + month.ToString(Inv) + ").");
        }

        private static bool TryGetIndex(object key, out double index)
        {
            index = 0;

            if (key is long || key is int || key is double)
            {
                index = Convert.ToDouble(key, Inv);
                return true;
            }

            return false;
        }

        // Rend une valeur Lua en texte Lua lisible (pour expires et les clés inconnues).
        private static string ValueToText(object v)
        {
            if (v == null) return "nil";
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is string) return LuaText.Quote((string)v);
            if (v is long || v is int) return Convert.ToString(v, Inv);
            if (v is double) return ((double)v).ToString("R", Inv);

            LuaTable table = v as LuaTable;
            if (table != null) return TableToText(table);

            return "nil --[[ unsupported: " + v.GetType().Name + " ]]";
        }

        private static string TableToText(LuaTable table)
        {
            var numeric = new List<KeyValuePair<double, object>>();
            var named = new List<KeyValuePair<string, object>>();

            foreach (object k in table.Keys)
            {
                double index;

                if (TryGetIndex(k, out index))
                    numeric.Add(new KeyValuePair<double, object>(index, table[k]));
                else
                    named.Add(new KeyValuePair<string, object>(Convert.ToString(k, Inv), table[k]));
            }

            numeric.Sort((a, b) => a.Key.CompareTo(b.Key));
            named.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            bool sequence = true;

            for (int i = 0; i < numeric.Count; i++)
            {
                if (numeric[i].Key != i + 1)
                {
                    sequence = false;
                    break;
                }
            }

            var parts = new List<string>();

            foreach (KeyValuePair<double, object> kv in numeric)
            {
                parts.Add(sequence
                    ? ValueToText(kv.Value)
                    : "[" + kv.Key.ToString("R", Inv) + "] = " + ValueToText(kv.Value));
            }

            foreach (KeyValuePair<string, object> kv in named)
            {
                string keyText = Regex.IsMatch(kv.Key, @"^[A-Za-z_][A-Za-z0-9_]*$") ? kv.Key : "[" + LuaText.Quote(kv.Key) + "]";
                parts.Add(keyText + " = " + ValueToText(kv.Value));
            }

            return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
        }
    }

    // Écriture de textes Lua : la valeur relue est toujours identique à celle d'origine.
    public static class LuaText
    {
        // Choisit les guillemets qui évitent le plus d'échappements (comme dans les fichiers de campagne : 'Action.X("...")').
        public static string Quote(string s)
        {
            char q = (s.IndexOf('"') >= 0 && s.IndexOf('\'') < 0) ? '\'' : '"';
            var sb = new StringBuilder();
            sb.Append(q);

            foreach (char c in s)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == q) sb.Append('\\').Append(q);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32 || c == 127) sb.Append('\\').Append(((int)c).ToString("D3", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }

            return sb.Append(q).ToString();
        }
    }

    // La "famille" d'un trigger dans la liste : sa condition principale (une mission, une date, un flag...).
    public class TriggerGroupKey
    {
        public int Rank;                // ordre des familles : 0 mission, 1 date, 2 flag, 3 autre, 4 alternatives (or), 5 sans condition, 6 Lua brut
        public double Sort;             // ordre dans la famille (n° de mission, date AAAAMMJJ)
        public string Id = "";          // clé stable (pour replier / déplier un groupe)
        public string Label = "";       // texte affiché
    }

    // Classe les triggers par condition principale. Lecture seule : ne modifie rien.
    // Mode : "Auto" (la condition la plus parlante : mission, puis date, puis flag...), "Mission", "Date", "Flag", "None" (ordre du fichier).
    public static class TriggerGrouper
    {
        public static readonly string[] Modes = { "Auto", "Mission", "Date", "Flag", "None" };

        private static TriggerGroupKey Key(int rank, double sort, string id, string label)
        {
            return new TriggerGroupKey { Rank = rank, Sort = sort, Id = id, Label = label };
        }

        public static TriggerGroupKey Classify(CampTrigger t, string mode)
        {
            if (mode == "None")
                return Key(0, 0, "all", "All triggers (file order)");

            if (!t.HasCondition || t.Condition.Trim().Length == 0)
                return Key(5, 0, "always", "Always (no condition)");

            LuaNode root = t.ConditionNode;

            if (root == null)
                return Key(6, 0, "raw", "Raw Lua condition");

            var atoms = new List<LuaNode>();
            bool topOr = !Flatten(root, atoms);

            if (topOr && mode == "Auto")
                return Key(4, 0, "or", "Alternatives (OR)");

            TriggerGroupKey best = null;

            foreach (LuaNode atom in atoms)
            {
                TriggerGroupKey k = AtomKey(atom);

                if (k == null)
                    continue;

                if (mode == "Mission" && !k.Id.StartsWith("mission:", StringComparison.Ordinal)) continue;
                if (mode == "Date" && k.Rank != 1) continue;
                if (mode == "Flag" && k.Rank != 2) continue;

                if (best == null || k.Rank < best.Rank)
                    best = k;
            }

            if (best != null)
                return best;

            return mode == "Auto" ? Key(3, 0, "other", "Other conditions") : Key(8, 0, "none", "Other (not " + mode.ToLowerInvariant() + ")");
        }

        // Met à plat les "and" du dessus. Renvoie false si le dessus est un "or" (alors atoms reste vide).
        private static bool Flatten(LuaNode n, List<LuaNode> atoms)
        {
            if (n.Kind == NodeKind.Paren && n.Items.Count == 1)
                return Flatten(n.Items[0], atoms);

            if (n.Kind == NodeKind.Binary && n.Name == "or")
                return false;

            if (n.Kind == NodeKind.Binary && n.Name == "and" && n.Items.Count == 2)
            {
                bool a = Flatten(n.Items[0], atoms);
                bool b = Flatten(n.Items[1], atoms);
                return a && b;
            }

            atoms.Add(n);
            return true;
        }

        private static bool IsCompare(string op)
        {
            return op == "==" || op == "~=" || op == "<" || op == "<=" || op == ">" || op == ">=";
        }

        private static string Flip(string op)
        {
            switch (op)
            {
                case "<": return ">";
                case "<=": return ">=";
                case ">": return "<";
                case ">=": return "<=";
                default: return op;
            }
        }

        private static bool IsMissionValue(LuaNode n)
        {
            return (n.Kind == NodeKind.Ref && n.Name == "MissionInstance")
                || (n.Kind == NodeKind.Call && n.Name == "Return.Mission");
        }

        // La famille d'une condition simple (null = rien de reconnu).
        private static TriggerGroupKey AtomKey(LuaNode atom)
        {
            // un NOT ou des parenthèses ne changent pas le sujet de la condition
            while ((atom.Kind == NodeKind.Not || atom.Kind == NodeKind.Paren) && atom.Items.Count == 1)
                atom = atom.Items[0];

            // mission : MissionInstance == 30, Return.Mission() >= 3 ...
            if (atom.Kind == NodeKind.Binary && IsCompare(atom.Name) && atom.Items.Count == 2)
            {
                LuaNode left = atom.Items[0], right = atom.Items[1];
                string op = atom.Name;

                if (right.Kind == NodeKind.Number && IsMissionValue(left))
                    return MissionKey(op, right.Number);

                if (left.Kind == NodeKind.Number && IsMissionValue(right))
                    return MissionKey(Flip(op), left.Number);
            }

            foreach (LuaNode n in atom.Walk())
            {
                if (IsMissionValue(n))
                    return Key(3, 0, "mission:?", "Mission (other test)");

                if (n.Kind == NodeKind.Call && (n.Name == "Return.DatePassed" || n.Name == "Return.DateBefore") && n.Items.Count >= 3
                    && n.Items[0].Kind == NodeKind.Number && n.Items[1].Kind == NodeKind.Number && n.Items[2].Kind == NodeKind.Number)
                {
                    int y = (int)n.Items[0].Number, m = (int)n.Items[1].Number, d = (int)n.Items[2].Number;
                    string label = y.ToString("0000", CultureInfo.InvariantCulture) + "-" + m.ToString("00", CultureInfo.InvariantCulture) + "-" + d.ToString("00", CultureInfo.InvariantCulture);
                    if (n.Name == "Return.DateBefore")
                        return Key(1, y * 10000.0 + m * 100 + d - 0.5, "datebefore:" + label, "Date before " + label);

                    return Key(1, y * 10000.0 + m * 100 + d, "date:" + label, "Date " + label);
                }

                if (n.Kind == NodeKind.Ref && n.Name.StartsWith("camp.date.", StringComparison.Ordinal))
                    return Key(1, 99999999, "date:?", "Date (other test)");

                if (n.Kind == NodeKind.Call && n.Name == "Return.CampFlag")
                {
                    string flag = FlagText(n);
                    double number;
                    double sort = double.TryParse(flag, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : 1e9;   // numéros d'abord, noms ensuite
                    return Key(2, sort, "flag:" + flag, "Flag " + flag);
                }
            }

            foreach (LuaNode n in atom.Walk())
            {
                if (n.Kind == NodeKind.Call && n.Def != null)
                    return Key(3, 0, "cat:" + n.Def.Category, n.Def.Category);
            }

            return null;
        }

        // Le flag désigné par un appel Return.CampFlag(...) : son numéro ou son nom.
        private static string FlagText(LuaNode call)
        {
            if (call.Items.Count == 0)
                return "?";

            LuaNode a = call.Items[0];

            if (a.Kind == NodeKind.Number)
                return a.Number.ToString("0.##", CultureInfo.InvariantCulture);

            if (a.Kind == NodeKind.Text)
                return LooksNumeric(a.Name) ? "\"" + a.Name + "\"" : a.Name;     // "802" (texte) ne se confond pas avec 802 (nombre)

            return a.Source.Trim();
        }

        private static bool LooksNumeric(string text)
        {
            double n;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out n);
        }

        // ----- rapport des flags : qui les lit, qui les positionne -----

        private static readonly Regex FlagReadRx = new Regex(@"Return\.CampFlag\s*\(\s*(""[^""]*""|'[^']*'|[^)\s,]+)", RegexOptions.Compiled);
        private static readonly Regex FlagSetRx = new Regex(@"Action\.(?:Set|Add)CampFlag\s*\(\s*(""[^""]*""|'[^']*'|[^),\s]+)", RegexOptions.Compiled);

        private static string CleanFlag(string raw)
        {
            string f = raw.Trim();

            if (f.Length >= 2 && (f[0] == '"' || f[0] == '\'') && f[f.Length - 1] == f[0])
                f = f.Substring(1, f.Length - 2);

            bool quoted = f.Length != raw.Trim().Length;
            double number;

            if (double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                f = quoted ? "\"" + f + "\"" : number.ToString("0.##", CultureInfo.InvariantCulture);

            return f;
        }

        private static void AddUse(Dictionary<string, List<string>> map, string flag, string trigger)
        {
            List<string> list;

            if (!map.TryGetValue(flag, out list))
            {
                list = new List<string>();
                map[flag] = list;
            }

            if (!list.Contains(trigger))
                list.Add(trigger);
        }

        private static string NameList(List<string> names)
        {
            const int max = 12;

            if (names == null || names.Count == 0)
                return "-";

            string text = string.Join(", ", names.Take(max).ToArray());
            return names.Count > max ? text + "  (+" + (names.Count - max) + " more)" : text;
        }

        // Lit le texte Lua des conditions et des actions (même celles qu'on ne décompose pas).
        private static void CollectFlags(CampTriggersFile file, Dictionary<string, List<string>> readers, Dictionary<string, List<string>> setters)
        {
            foreach (CampTrigger t in file.Triggers)
            {
                foreach (Match m in FlagReadRx.Matches(t.Condition ?? ""))
                    AddUse(readers, CleanFlag(m.Groups[1].Value), t.Name);

                foreach (string a in t.Actions)
                {
                    foreach (Match m in FlagSetRx.Matches(a ?? ""))
                        AddUse(setters, CleanFlag(m.Groups[1].Value), t.Name);

                    // un flag lu dans le texte d'une action (rare) compte aussi comme lu
                    foreach (Match m in FlagReadRx.Matches(a ?? ""))
                        AddUse(readers, CleanFlag(m.Groups[1].Value), t.Name);
                }
            }
        }

        private static List<string> OrderFlags(IEnumerable<string> flags)
        {
            return flags.OrderBy(f =>
            {
                double n;
                return double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 1e9;   // numéros d'abord, noms ensuite
            }).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Tous les flags qu'on trouve dans les triggers (numéros ou noms), pour les listes déroulantes.
        public static List<string> KnownFlags(CampTriggersFile file)
        {
            var readers = new Dictionary<string, List<string>>();
            var setters = new Dictionary<string, List<string>>();
            CollectFlags(file, readers, setters);

            var all = new HashSet<string>(readers.Keys);
            all.UnionWith(setters.Keys);

            return OrderFlags(all);
        }

        public static string FlagReport(CampTriggersFile file)
        {
            var readers = new Dictionary<string, List<string>>();
            var setters = new Dictionary<string, List<string>>();
            CollectFlags(file, readers, setters);

            var all = new HashSet<string>(readers.Keys);
            all.UnionWith(setters.Keys);

            List<string> ordered = OrderFlags(all);

            var sb = new StringBuilder();
            sb.AppendLine(Lang.T("FLAGS used by the triggers : ") + ordered.Count);
            sb.AppendLine(Lang.T("(read = tested in a condition ; set = changed by an action)"));
            sb.AppendLine();

            foreach (string f in ordered)
            {
                List<string> r, w;
                readers.TryGetValue(f, out r);
                setters.TryGetValue(f, out w);

                sb.AppendLine("Flag " + f + Lang.T("    read by ") + (r == null ? 0 : r.Count) + Lang.T("  |  set by ") + (w == null ? 0 : w.Count));
                sb.AppendLine(Lang.T("    read by : ") + NameList(r));
                sb.AppendLine(Lang.T("    set by  : ") + NameList(w));

                if (w == null)
                    sb.AppendLine(Lang.T("    Note: no trigger sets it (the mission or the engine may)."));
                else if (r == null)
                    sb.AppendLine(Lang.T("    Note: no trigger reads it."));

                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ----- pour l'onglet GRAPH -----

        private static void SplitAtoms(LuaNode n, List<LuaNode> atoms)
        {
            if (n.Kind == NodeKind.Paren && n.Items.Count == 1)
                SplitAtoms(n.Items[0], atoms);
            else if (n.Kind == NodeKind.Binary && (n.Name == "and" || n.Name == "or") && n.Items.Count == 2)
            {
                SplitAtoms(n.Items[0], atoms);
                SplitAtoms(n.Items[1], atoms);
            }
            else
                atoms.Add(n);
        }

        private static TriggerGroupKey FlagKey(string flag)
        {
            double number;
            double sort = double.TryParse(flag, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : 1e9;
            return Key(2, sort, "flag:" + flag, "Flag " + flag);
        }

        // Toutes les conditions simples d'un trigger (and ET or à plat), une clé par sujet : mission, date, flag, catégorie...
        public static List<TriggerGroupKey> AtomKeys(CampTrigger t)
        {
            var keys = new List<TriggerGroupKey>();
            var seen = new HashSet<string>();

            Action<TriggerGroupKey> add = k =>
            {
                if (seen.Add(k.Id))
                    keys.Add(k);
            };

            if (!t.HasCondition || t.Condition.Trim().Length == 0)
            {
                add(Key(5, 0, "always", "Always (no condition)"));
                return keys;
            }

            if (t.ConditionNode == null)
            {
                foreach (string f in ReadFlagsInText(t.Condition))
                    add(FlagKey(f));

                if (keys.Count == 0)
                    add(Key(6, 0, "raw", "Raw Lua condition"));

                return keys;
            }

            var atoms = new List<LuaNode>();
            SplitAtoms(t.ConditionNode, atoms);

            foreach (LuaNode atom in atoms)
            {
                add(AtomKey(atom) ?? Key(3, 0, "other", "Other conditions"));

                // plusieurs flags dans la même condition : on les garde tous
                foreach (LuaNode n in atom.Walk())
                {
                    if (n.Kind == NodeKind.Call && n.Name == "Return.CampFlag")
                        add(FlagKey(FlagText(n)));
                }
            }

            return keys;
        }

        public static string FlagName(LuaNode call)
        {
            return FlagText(call);
        }

        public static List<string> ReadFlagsInText(string text)
        {
            var list = new List<string>();

            foreach (Match m in FlagReadRx.Matches(text ?? ""))
                list.Add(CleanFlag(m.Groups[1].Value));

            return list;
        }

        public static List<string> SetFlagsInText(string text)
        {
            var list = new List<string>();

            foreach (Match m in FlagSetRx.Matches(text ?? ""))
                list.Add(CleanFlag(m.Groups[1].Value));

            return list;
        }

        private static TriggerGroupKey MissionKey(string op, double number)
        {
            string n = number.ToString("0.##", CultureInfo.InvariantCulture);
            string label;
            double sort = number;

            switch (op)
            {
                case "==": label = "Mission " + n; break;
                case ">=": label = "Mission " + n + " or later"; sort += 0.2; break;
                case ">": label = "After mission " + n; sort += 0.3; break;
                case "<": label = "Before mission " + n; sort += 0.4; break;
                case "<=": label = "Up to mission " + n; sort += 0.5; break;
                default: label = "Not mission " + n; sort += 0.6; break;
            }

            return Key(0, sort, "mission:" + op + n, label);
        }
    }
}
