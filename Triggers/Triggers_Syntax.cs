using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    public enum NodeKind { Call, Number, Text, Bool, Nil, Table, Ref, Binary, Not, Paren }

    // Un morceau d'expression Lua reconnu : un appel Return.X(...) / Action.X(...), une valeur,
    // une comparaison, un and / or, etc. Un seul type pour tout, pour rester simple.
    public class LuaNode
    {
        public NodeKind Kind;
        public string Name = "";                    // Call : "Return.Mission" ; Ref : le texte ; Binary : l'opérateur ; Text : la valeur
        public double Number;
        public bool Bool;
        public List<LuaNode> Items = new List<LuaNode>();   // arguments, éléments de table, opérandes (Binary : 2, Not / Paren : 1)
        public string Source = "";                  // texte d'origine de ce morceau
        public FuncDef Def;                         // Call : fiche du catalogue (null = fonction inconnue)

        // Ce morceau et tout ce qu'il contient.
        public IEnumerable<LuaNode> Walk()
        {
            yield return this;

            foreach (LuaNode item in Items)
            {
                foreach (LuaNode inner in item.Walk())
                    yield return inner;
            }
        }

        // Faux si une fonction appelée n'est pas dans le catalogue : le texte reste alors "brut".
        public bool IsFullyKnown()
        {
            return !Walk().Any(n => n.Kind == NodeKind.Call && n.Def == null);
        }
    }

    // Lit une condition ou une action Lua et la transforme en LuaNode.
    // Renvoie null dès que le texte contient quelque chose qu'on ne sait pas lire : l'appelant le garde alors tel quel.
    // Sous-ensemble volontairement petit de Lua : appels Return.* / Action.*, nombres, textes, true / false / nil,
    // tables de valeurs, + - * / %, comparaisons, and / or / not, parenthèses, et les valeurs du catalogue (Refs).
    public class LuaExprParser
    {
        private static readonly Regex NumberRx = new Regex(@"\G(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?");
        private static readonly string[] CompareOps = { "==", "~=", "<=", ">=", "<", ">" };

        private readonly string _s;
        private int _i;
        private bool _ok = true;

        private LuaExprParser(string text)
        {
            _s = text ?? "";
        }

        public static LuaNode ParseCondition(string text)
        {
            var p = new LuaExprParser(text);
            p.SkipSpaces();

            if (p._i >= p._s.Length)
                return null;

            LuaNode node = p.ParseOr();
            p.SkipSpaces();

            if (!p._ok || p._i < p._s.Length)
                return null;

            return node;
        }

        // Une action reconnue = UN seul appel Action.Xxx(...), avec ou sans ';' à la fin.
        public static LuaNode ParseAction(string text)
        {
            var p = new LuaExprParser(text);
            p.SkipSpaces();

            if (p._i >= p._s.Length)
                return null;

            LuaNode node = p.ParseOr();

            if (!p._ok)
                return null;

            p.SkipSpaces();

            if (p._i < p._s.Length && p._s[p._i] == ';')
            {
                p._i++;
                p.SkipSpaces();
            }

            if (p._i < p._s.Length)
                return null;

            if (node.Kind != NodeKind.Call || !node.Name.StartsWith("Action.", StringComparison.Ordinal))
                return null;

            return node;
        }

        // ---------------------------------------------------------------
        // Petits outils
        // ---------------------------------------------------------------

        private void SkipSpaces()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                _i++;
        }

        private LuaNode Fail()
        {
            _ok = false;
            return null;
        }

        private string Slice(int start)
        {
            return _s.Substring(start, _i - start).Trim();
        }

        private static bool IsIdentChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        // Avance si le mot (and, or, not) est là, en entier.
        private bool TryWord(string word)
        {
            SkipSpaces();

            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                return false;

            int end = _i + word.Length;

            if (end < _s.Length && IsIdentChar(_s[end]))
                return false;

            _i = end;
            return true;
        }

        private bool TryOp(string op)
        {
            SkipSpaces();

            if (string.CompareOrdinal(_s, _i, op, 0, op.Length) != 0)
                return false;

            _i += op.Length;
            return true;
        }

        private LuaNode Binary(string op, LuaNode left, LuaNode right, int start)
        {
            var node = new LuaNode { Kind = NodeKind.Binary, Name = op, Source = Slice(start) };
            node.Items.Add(left);
            node.Items.Add(right);
            return node;
        }

        // ---------------------------------------------------------------
        // Grammaire, de la priorité la plus faible (or) à la plus forte (valeur)
        // ---------------------------------------------------------------

        private LuaNode ParseOr()
        {
            SkipSpaces();
            int start = _i;

            LuaNode left = ParseAnd();

            while (_ok && TryWord("or"))
            {
                LuaNode right = ParseAnd();

                if (!_ok)
                    return null;

                left = Binary("or", left, right, start);
            }

            return _ok ? left : null;
        }

        private LuaNode ParseAnd()
        {
            SkipSpaces();
            int start = _i;

            LuaNode left = ParseCompare();

            while (_ok && TryWord("and"))
            {
                LuaNode right = ParseCompare();

                if (!_ok)
                    return null;

                left = Binary("and", left, right, start);
            }

            return _ok ? left : null;
        }

        private LuaNode ParseCompare()
        {
            SkipSpaces();
            int start = _i;

            LuaNode left = ParseAdd();

            if (!_ok)
                return null;

            foreach (string op in CompareOps)
            {
                if (TryOp(op))
                {
                    LuaNode right = ParseAdd();

                    if (!_ok)
                        return null;

                    return Binary(op, left, right, start);
                }
            }

            return left;
        }

        private LuaNode ParseAdd()
        {
            SkipSpaces();
            int start = _i;

            LuaNode left = ParseMul();

            while (_ok)
            {
                SkipSpaces();

                bool isPlus = _i < _s.Length && _s[_i] == '+';
                bool isMinus = _i < _s.Length && _s[_i] == '-' && !(_i + 1 < _s.Length && _s[_i + 1] == '-'); // "--" = commentaire

                if (!isPlus && !isMinus)
                    break;

                string op = isPlus ? "+" : "-";
                _i++;

                LuaNode right = ParseMul();

                if (!_ok)
                    return null;

                left = Binary(op, left, right, start);
            }

            return _ok ? left : null;
        }

        private LuaNode ParseMul()
        {
            SkipSpaces();
            int start = _i;

            LuaNode left = ParseUnary();

            while (_ok)
            {
                SkipSpaces();

                if (_i >= _s.Length || (_s[_i] != '*' && _s[_i] != '/' && _s[_i] != '%'))
                    break;

                string op = _s[_i].ToString();
                _i++;

                LuaNode right = ParseUnary();

                if (!_ok)
                    return null;

                left = Binary(op, left, right, start);
            }

            return _ok ? left : null;
        }

        private LuaNode ParseUnary()
        {
            SkipSpaces();
            int start = _i;

            if (TryWord("not"))
            {
                LuaNode inner = ParseUnary();

                if (!_ok)
                    return null;

                var not = new LuaNode { Kind = NodeKind.Not, Name = "not", Source = Slice(start) };
                not.Items.Add(inner);
                return not;
            }

            // Nombre négatif : seulement devant un nombre
            if (_i < _s.Length && _s[_i] == '-' && !(_i + 1 < _s.Length && _s[_i + 1] == '-'))
            {
                _i++;
                LuaNode inner = ParseUnary();

                if (!_ok || inner.Kind != NodeKind.Number)
                    return Fail();

                inner.Number = -inner.Number;
                inner.Source = Slice(start);
                return inner;
            }

            return ParsePrimary();
        }

        private LuaNode ParsePrimary()
        {
            SkipSpaces();
            int start = _i;

            if (_i >= _s.Length)
                return Fail();

            char c = _s[_i];

            if (c == '(')
            {
                _i++;
                LuaNode inner = ParseOr();

                if (!_ok)
                    return null;

                SkipSpaces();

                if (_i >= _s.Length || _s[_i] != ')')
                    return Fail();

                _i++;

                var paren = new LuaNode { Kind = NodeKind.Paren, Name = "()", Source = Slice(start) };
                paren.Items.Add(inner);
                return paren;
            }

            if (char.IsDigit(c))
                return ParseNumber(start);

            if (c == '"' || c == '\'')
                return ParseString(start);

            if (c == '{')
                return ParseTable(start);

            if (char.IsLetter(c) || c == '_')
                return ParseName(start);

            return Fail();
        }

        private LuaNode ParseNumber(int start)
        {
            Match m = NumberRx.Match(_s, _i);

            if (!m.Success)
                return Fail();

            double value;

            if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return Fail();

            _i += m.Length;

            return new LuaNode { Kind = NodeKind.Number, Number = value, Source = Slice(start) };
        }

        private LuaNode ParseString(int start)
        {
            char quote = _s[_i++];
            var sb = new StringBuilder();

            while (_i < _s.Length)
            {
                char ch = _s[_i++];

                if (ch == quote)
                    return new LuaNode { Kind = NodeKind.Text, Name = sb.ToString(), Source = Slice(start) };

                if (ch != '\\')
                {
                    sb.Append(ch);
                    continue;
                }

                if (_i >= _s.Length)
                    break;

                char e = _s[_i++];

                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\\':
                    case '"':
                    case '\'': sb.Append(e); break;
                    default: return Fail(); // autre séquence d'échappement : on ne touche pas
                }
            }

            return Fail(); // chaîne jamais fermée
        }

        private LuaNode ParseTable(int start)
        {
            _i++; // {

            var table = new LuaNode { Kind = NodeKind.Table, Name = "{}" };

            while (true)
            {
                SkipSpaces();

                if (_i >= _s.Length)
                    return Fail();

                if (_s[_i] == '}')
                {
                    _i++;
                    break;
                }

                LuaNode item = ParseOr();

                if (!_ok)
                    return null;

                table.Items.Add(item);
                SkipSpaces();

                if (_i < _s.Length && (_s[_i] == ',' || _s[_i] == ';'))
                    _i++;
                else if (_i >= _s.Length || _s[_i] != '}')
                    return Fail();
            }

            table.Source = Slice(start);
            return table;
        }

        private bool ReadIdent(StringBuilder sb)
        {
            if (_i >= _s.Length || !(char.IsLetter(_s[_i]) || _s[_i] == '_'))
                return false;

            while (_i < _s.Length && IsIdentChar(_s[_i]))
                sb.Append(_s[_i++]);

            return true;
        }

        // Un nom : true / false / nil, un appel Return.X(...) ou Action.X(...), ou une valeur du catalogue (Refs).
        private LuaNode ParseName(int start)
        {
            var path = new StringBuilder();

            if (!ReadIdent(path))
                return Fail();

            int segments = 1;

            while (_i < _s.Length)
            {
                if (_s[_i] == '.')
                {
                    _i++;
                    path.Append('.');

                    if (!ReadIdent(path))
                        return Fail();

                    segments++;
                }
                else if (_s[_i] == '[')
                {
                    _i++;
                    SkipSpaces();

                    LuaNode key;

                    if (_i < _s.Length && (_s[_i] == '"' || _s[_i] == '\''))
                        key = ParseString(_i);
                    else if (_i < _s.Length && char.IsDigit(_s[_i]))
                        key = ParseNumber(_i);
                    else
                        return Fail();

                    if (!_ok)
                        return null;

                    SkipSpaces();

                    if (_i >= _s.Length || _s[_i] != ']')
                        return Fail();

                    _i++;

                    // Écriture normalisée (guillemets doubles) pour comparer aux motifs du catalogue
                    path.Append(key.Kind == NodeKind.Text
                        ? "[\"" + key.Name + "\"]"
                        : "[" + key.Number.ToString("R", CultureInfo.InvariantCulture) + "]");

                    segments++;
                }
                else
                {
                    break;
                }
            }

            string name = path.ToString();

            if (segments == 1)
            {
                if (name == "true") return new LuaNode { Kind = NodeKind.Bool, Bool = true, Source = Slice(start) };
                if (name == "false") return new LuaNode { Kind = NodeKind.Bool, Bool = false, Source = Slice(start) };
                if (name == "nil") return new LuaNode { Kind = NodeKind.Nil, Source = Slice(start) };
            }

            // Appel de fonction ?
            int save = _i;
            SkipSpaces();

            if (_i < _s.Length && _s[_i] == '(')
            {
                bool isReturnOrAction = segments == 2
                    && (name.StartsWith("Return.", StringComparison.Ordinal) || name.StartsWith("Action.", StringComparison.Ordinal));

                if (!isReturnOrAction)
                    return Fail();

                return ParseCall(name, start);
            }

            _i = save;

            // Valeur sans appel : doit être connue du catalogue
            foreach (RefDef r in TriggerCatalog.Refs)
            {
                if (r.Pattern.IsMatch(name))
                    return new LuaNode { Kind = NodeKind.Ref, Name = name, Source = Slice(start) };
            }

            return Fail();
        }

        private LuaNode ParseCall(string name, int start)
        {
            _i++; // (

            var call = new LuaNode { Kind = NodeKind.Call, Name = name, Def = TriggerCatalog.Find(name) };

            SkipSpaces();

            if (_i < _s.Length && _s[_i] == ')')
            {
                _i++;
                call.Source = Slice(start);
                return call;
            }

            while (true)
            {
                LuaNode arg = ParseOr();

                if (!_ok)
                    return null;

                call.Items.Add(arg);
                SkipSpaces();

                if (_i >= _s.Length)
                    return Fail();

                if (_s[_i] == ',')
                {
                    _i++;
                    continue;
                }

                if (_s[_i] == ')')
                {
                    _i++;
                    break;
                }

                return Fail();
            }

            call.Source = Slice(start);
            return call;
        }
    }

    // Transforme un LuaNode en phrase lisible : "the alive % of target "X" is less than 50".
    public static class TriggerText
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Regex PlaceholderRx = new Regex(@"\{(\d+)\}");
        private static readonly Regex StringRx = new Regex("\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*'");
        private static readonly Regex NumberRx = new Regex(@"\b\d+(\.\d+)?\b");
        private static readonly Regex SpacesRx = new Regex(@"\s+");

        public static string Describe(LuaNode n)
        {
            switch (n.Kind)
            {
                case NodeKind.Number: return n.Number.ToString("R", Inv);
                case NodeKind.Text: return "\"" + n.Name + "\"";
                case NodeKind.Bool: return n.Bool ? "true" : "false";
                case NodeKind.Nil: return "empty";
                case NodeKind.Table: return "[" + string.Join(", ", n.Items.Select(Describe)) + "]";
                case NodeKind.Paren: return "(" + Describe(n.Items[0]) + ")";
                case NodeKind.Not: return "NOT " + Describe(n.Items[0]);
                case NodeKind.Ref: return DescribeRef(n);
                case NodeKind.Call: return DescribeCall(n);
                case NodeKind.Binary: return Describe(n.Items[0]) + OperatorWords(n.Name) + Describe(n.Items[1]);
            }

            return n.Source;
        }

        // Phrase d'une condition entière ("true" tout seul = toujours).
        public static string DescribeCondition(LuaNode n)
        {
            if (n.Kind == NodeKind.Bool)
                return n.Bool ? "always" : "never";

            return Describe(n);
        }

        private static string OperatorWords(string op)
        {
            switch (op)
            {
                case "and": return Lang.T("  AND  ");
                case "or": return Lang.T("  OR  ");
                case "==": return Lang.T(" is ");
                case "~=": return Lang.T(" is not ");
                case "<": return Lang.T(" is less than ");
                case "<=": return Lang.T(" is at most ");
                case ">": return Lang.T(" is more than ");
                case ">=": return Lang.T(" is at least ");
            }

            return " " + op + " "; // + - * / %
        }

        private static string DescribeCall(LuaNode n)
        {
            if (n.Def == null)
                return n.Source;

            return PlaceholderRx.Replace(n.Def.Sentence, m =>
            {
                int index = int.Parse(m.Groups[1].Value, Inv);

                if (index < n.Items.Count)
                {
                    if (index < n.Def.Params.Count)
                    {
                        TrigParam pt = n.Def.Params[index].Type;
                        if (pt == TrigParam.Weather && n.Items[index].Kind == NodeKind.Text)
                            return Triggers_Form.DescribeWeather(n.Items[index].Name);
                        if ((pt == TrigParam.ShipRoute || pt == TrigParam.ShipZone) && n.Items[index].Kind == NodeKind.Table)
                            return Triggers_Form.DescribeRoute(n.Items[index]);
                    }
                    return Describe(n.Items[index]);
                }

                if (index < n.Def.Params.Count && n.Def.Params[index].Optional)
                    return n.Def.Params[index].Default;

                return Lang.T("(missing)");
            });
        }

        private static string DescribeRef(LuaNode n)
        {
            foreach (RefDef r in TriggerCatalog.Refs)
            {
                Match m = r.Pattern.Match(n.Name);

                if (m.Success)
                    return r.Sentence.Replace("{1}", m.Groups.Count > 1 ? Lang.W(m.Groups[1].Value) : "");
            }

            return n.Source;
        }

        // "Forme" d'un texte : les textes entre guillemets deviennent S et les nombres N.
        // Sert au scan pour compter les formes de texte les plus fréquentes qu'on ne sait pas encore décomposer.
        public static string Shape(string s)
        {
            s = StringRx.Replace(s, "S");
            s = NumberRx.Replace(s, "N");
            s = SpacesRx.Replace(s, " ").Trim();

            return s.Length > 150 ? s.Substring(0, 150) + "..." : s;
        }
    }

    // Contrôle un fichier de triggers : décompose chaque condition / action, vérifie la syntaxe Lua,
    // les arguments, les noms existants et les flags. Ne change rien dans les fichiers.
    // Chaque remarque commence par ERROR: (le moteur échouera ou ignorera), Warning: (suspect) ou Note: (info).
    public static class TriggerChecker
    {
        public const string ErrorTag = "ERROR: ";
        public const string WarnTag = "Warning: ";
        public const string NoteTag = "Note: ";

        private static readonly Regex SetFlagRx = new Regex(@"Action\.SetCampFlag\(\s*(""([^""]*)""|'([^']*)'|(-?\d+(?:\.\d+)?))");
        private static readonly Regex AddFlagRx = new Regex(@"Action\.AddCampFlag\(\s*(""([^""]*)""|'([^']*)'|(-?\d+(?:\.\d+)?))");
        private static readonly Regex ChunkPrefixRx = new Regex(@"^.*?\]:\d+:\s*", RegexOptions.Singleline);

        public static void Run(CampTriggersFile file)
        {
            TriggerLists lists = file.Lists ?? new TriggerLists();

            // Passe 1 : décomposition + flags posés par les actions (lus dans le texte, même s'il est brut)
            var flagsSet = new HashSet<string>(StringComparer.Ordinal);
            var flagsInitialized = new HashSet<string>(StringComparer.Ordinal);

            foreach (CampTrigger t in file.Triggers)
            {
                t.ConditionNode = t.HasCondition ? LuaExprParser.ParseCondition(t.Condition) : null;
                t.ActionNodes.Clear();

                foreach (string action in t.Actions)
                {
                    t.ActionNodes.Add(LuaExprParser.ParseAction(action));

                    foreach (Match m in SetFlagRx.Matches(action))
                    {
                        flagsSet.Add(FlagKeyFromMatch(m));
                        flagsInitialized.Add(FlagKeyFromMatch(m));
                    }

                    foreach (Match m in AddFlagRx.Matches(action))
                        flagsSet.Add(FlagKeyFromMatch(m));
                }
            }

            // Passe 2 : contrôles (un état Lua pour toute la passe, juste pour vérifier la syntaxe)
            Lua lua = null;

            try
            {
                lua = DcemLua.NewState();
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("TriggerChecker | état Lua indisponible, syntaxe non vérifiée : " + ex.Message);
            }

            try
            {
                foreach (CampTrigger t in file.Triggers)
                    CheckTrigger(t, lists, lua, flagsSet, flagsInitialized);
            }
            finally
            {
                if (lua != null)
                    lua.Dispose();
            }
        }

        private static string FlagKeyFromMatch(Match m)
        {
            // Nombre -> "#5", texte -> "$nom" (en Lua la clé 5 et la clé "5" sont différentes)
            if (m.Groups[4].Success)
                return "#" + double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);

            return "$" + (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
        }

        private static string FlagKey(LuaNode arg)
        {
            if (arg.Kind == NodeKind.Number)
                return "#" + arg.Number.ToString("R", CultureInfo.InvariantCulture);

            if (arg.Kind == NodeKind.Text)
                return "$" + arg.Name;

            return null; // pas une valeur fixe
        }

        private static int _actionIndex = -1;       // l'action en cours de contrôle (-1 = la condition ou le trigger lui-même)

        private static void Add(CampTrigger t, string text)
        {
            if (!t.Problems.Contains(text))
                t.Problems.Add(text);

            if (_actionIndex >= 0 && !text.StartsWith(NoteTag, StringComparison.Ordinal))
            {
                List<string> list;

                if (!t.ActionProblems.TryGetValue(_actionIndex, out list))
                {
                    list = new List<string>();
                    t.ActionProblems[_actionIndex] = list;
                }

                if (!list.Contains(text))
                    list.Add(text);
            }
        }

        private static void CheckTrigger(CampTrigger t, TriggerLists lists, Lua lua, HashSet<string> flagsSet, HashSet<string> flagsInitialized)
        {
            // ----- condition -----
            if (!t.HasCondition)
            {
                Add(t, ErrorTag + "No condition. The engine needs one (write true for 'always').");
            }
            else
            {
                string syntax = SyntaxError(lua, "if " + t.Condition + " then return true end");

                if (syntax != null)
                    Add(t, ErrorTag + "The condition is not valid Lua: " + syntax);

                if (t.ConditionNode != null)
                    CheckNode(t, t.ConditionNode, lists, flagsSet, flagsInitialized);
            }

            // ----- actions -----
            if (!t.HasAction)
                Add(t, ErrorTag + "No action. The engine stops when this trigger fires.");
            else if (t.Actions.Count == 0)
                Add(t, WarnTag + "The action list is empty: this trigger does nothing.");

            for (int i = 0; i < t.Actions.Count; i++)
            {
                _actionIndex = i;

                try
                {
                    string syntax = SyntaxError(lua, t.Actions[i]);

                    if (syntax != null)
                        Add(t, ErrorTag + "Action " + (i + 1) + " is not valid Lua: " + syntax);

                    if (i < t.ActionNodes.Count && t.ActionNodes[i] != null)
                        CheckNode(t, t.ActionNodes[i], lists, flagsSet, flagsInitialized);
                }
                finally
                {
                    _actionIndex = -1;
                }
            }
        }

        // Pour l'éditeur : vérifie un texte Lua saisi à la main. null = valide.
        public static string CheckLuaSyntax(string code)
        {
            try
            {
                using (Lua lua = DcemLua.NewState())
                    return SyntaxError(lua, code);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Compile le texte sans l'exécuter. Renvoie le message d'erreur Lua, ou null si tout va bien.
        private static string SyntaxError(Lua lua, string code)
        {
            if (lua == null)
                return null;

            try
            {
                lua["__src"] = code;
                object[] result = lua.DoString("local f, e = load(__src) return e");

                if (result != null && result.Length > 0 && result[0] is string)
                {
                    string message = ChunkPrefixRx.Replace((string)result[0], "");
                    return message.Length > 120 ? message.Substring(0, 120) + "..." : message;
                }
            }
            catch (Exception)
            {
                // la vérification est un confort : en cas de souci on ne dit rien
            }

            return null;
        }

        private static void CheckNode(CampTrigger t, LuaNode root, TriggerLists lists, HashSet<string> flagsSet, HashSet<string> flagsInitialized)
        {
            foreach (LuaNode n in root.Walk())
            {
                if (n.Kind != NodeKind.Call)
                    continue;

                FuncDef d = n.Def;

                if (d == null)
                {
                    Add(t, WarnTag + "Unknown function " + n.Name + " (not in the catalogue): kept as raw Lua.");
                    continue;
                }

                int min = d.MinArgs;
                int max = d.Params.Count;

                if (n.Items.Count < min)
                    Add(t, ErrorTag + d.FullName + " needs " + min + " argument(s), found " + n.Items.Count + ".");
                else if (n.Items.Count > max)
                    Add(t, WarnTag + d.FullName + " takes " + max + " argument(s), found " + n.Items.Count + " (the extra ones are ignored).");

                for (int i = 0; i < n.Items.Count && i < max; i++)
                    CheckArg(t, d, i, n.Items[i], lists);

                CheckFlags(t, n, flagsSet, flagsInitialized);

                if (d.FullName == "Return.DatePassed" || d.FullName == "Return.DateBefore")
                    CheckDateArgs(t, n);
            }
        }

        // Return.DatePassed(année, mois, jour) : on repère un ordre inversé (jour, mois, année) ou une valeur impossible.
        private static void CheckDateArgs(CampTrigger t, LuaNode call)
        {
            if (call.Items.Count < 3 || call.Items.Take(3).Any(x => x.Kind != NodeKind.Number))
                return;

            double y = call.Items[0].Number, m = call.Items[1].Number, d = call.Items[2].Number;

            if (y <= 31 && d >= 1900)
                Add(t, WarnTag + call.Name + "(" + y + ", " + m + ", " + d + "): the order looks reversed. The engine expects (year, month, day).");
            else if (m < 1 || m > 12)
                Add(t, WarnTag + call.Name + ": month " + m + " is not between 1 and 12 (the engine expects year, month, day).");
            else if (d < 1 || d > 31)
                Add(t, WarnTag + call.Name + ": day " + d + " is not between 1 and 31 (the engine expects year, month, day).");
            else if (y < 1900 || y > 2100)
                Add(t, WarnTag + call.Name + ": year " + y + " looks wrong (the engine expects year, month, day).");
        }

        private static void CheckArg(CampTrigger t, FuncDef d, int index, LuaNode a, TriggerLists lists)
        {
            ParamDef p = d.Params[index];
            string where = d.FullName + " argument " + (index + 1) + " (" + p.Label + ")";
            NodeKind k = a.Kind;

            switch (p.Type)
            {
                case TrigParam.Number:
                    if (k == NodeKind.Text || k == NodeKind.Bool || k == NodeKind.Nil || k == NodeKind.Table)
                        Add(t, ErrorTag + where + " must be a number.");
                    break;

                case TrigParam.Bool:
                    if (k == NodeKind.Number || k == NodeKind.Text || k == NodeKind.Nil || k == NodeKind.Table)
                        Add(t, ErrorTag + where + " must be true or false.");
                    break;

                case TrigParam.Flag:
                    if (k == NodeKind.Bool || k == NodeKind.Nil || k == NodeKind.Table)
                        Add(t, ErrorTag + where + " must be a number or a text.");
                    break;

                case TrigParam.Choice:
                    if (k == NodeKind.Number || k == NodeKind.Bool || k == NodeKind.Table)
                        Add(t, ErrorTag + where + " must be a text.");
                    else if (k == NodeKind.Text && LooksLikePicture(a.Name))
                        Add(t, WarnTag + where + ": \"" + a.Name + "\" is a file name, not a side. The engine does NOT add this picture to the briefing (use \"blue\", \"red\" or \"all\").");
                    else if (k == NodeKind.Text && !p.Choices.Contains(a.Name, StringComparer.Ordinal))
                        Add(t, WarnTag + where + ": \"" + a.Name + "\" is not one of: " + string.Join(", ", p.Choices.Select(c => "\"" + c + "\"")) + ".");
                    break;

                case TrigParam.Text:
                case TrigParam.TargetTitle:
                case TrigParam.TargetName:
                case TrigParam.AirUnit:
                case TrigParam.Airbase:
                    if (k == NodeKind.Table)
                    {
                        if (!p.AllowTable)
                        {
                            Add(t, ErrorTag + where + " must be a text, not a list.");
                            break;
                        }

                        foreach (LuaNode item in a.Items)
                        {
                            if (item.Kind != NodeKind.Text)
                                Add(t, ErrorTag + where + ": the list must contain texts only.");
                            else
                                CheckName(t, where, p.Type, item.Name, lists);
                        }
                    }
                    else if (k == NodeKind.Number || k == NodeKind.Bool || k == NodeKind.Nil)
                    {
                        Add(t, ErrorTag + where + " must be a text.");
                    }
                    else if (k == NodeKind.Text)
                    {
                        CheckName(t, where, p.Type, a.Name, lists);
                    }
                    break;
            }
        }

        // Un nom de fichier image à la place d'un choix : le moteur (AddImage) ne fait rien dans ce cas.
        private static bool LooksLikePicture(string v)
        {
            string l = v.ToLowerInvariant();
            return l.EndsWith(".png") || l.EndsWith(".jpg") || l.EndsWith(".jpeg") || l.EndsWith(".bmp");
        }

        // Le nom existe-t-il dans la campagne ? (seulement si le fichier de référence a pu être lu)
        private static void CheckName(CampTrigger t, string where, TrigParam type, string value, TriggerLists lists)
        {
            if (type == TrigParam.TargetTitle && lists.HasTargets && !lists.TargetTitles.Contains(value))
            {
                // La cible peut être créée plus tard (template activé) : on ne se plaint que si rien ne l'explique.
                if (!lists.ActiveTargetTitles.Contains(value) && !MentionsTemplateFor(t, value))
                    Add(t, WarnTag + "Target \"" + value + "\" not found in targetlist_init.lua or Active\\targetlist.lua, and no template of this trigger creates it.");
            }
            else if (type == TrigParam.TargetName && lists.HasTargets && !lists.TargetNames.Contains(value))
            {
                if (!MentionsTemplateFor(t, value))
                    Add(t, WarnTag + "Target \"" + value + "\" not found in targetlist_init.lua (name), and no template of this trigger creates it.");
            }
            else if (type == TrigParam.AirUnit && lists.HasAirUnits && !lists.AirUnits.Contains(value))
                Add(t, WarnTag + "Air unit \"" + value + "\" not found in oob_air_init.lua.");
            else if (type == TrigParam.Airbase && lists.HasAirbases && !lists.Airbases.Contains(value))
                Add(t, WarnTag + "Airbase \"" + value + "\" not found in db_airbases.lua.");
        }

        // Un TemplateActive du même trigger porte-t-il le même nom (avec ou sans ".stm") ?
        private static bool MentionsTemplateFor(CampTrigger t, string value)
        {
            if (t.Actions == null) return false;
            foreach (string a in t.Actions)
                if (a != null && a.Contains("TemplateActive") && a.Contains(value)) return true;
            return false;
        }

        private static void CheckFlags(CampTrigger t, LuaNode call, HashSet<string> flagsSet, HashSet<string> flagsInitialized)
        {
            if (call.Items.Count == 0)
                return;

            string key = FlagKey(call.Items[0]);

            if (key == null)
                return;

            string shown = call.Items[0].Kind == NodeKind.Text ? "\"" + call.Items[0].Name + "\"" : call.Items[0].Source;

            if (call.Name == "Return.CampFlag" && !flagsSet.Contains(key))
                Add(t, NoteTag + "Flag " + shown + " is read but no trigger of this file sets it (it may be set elsewhere).");

            if (call.Name == "Action.AddCampFlag" && !flagsInitialized.Contains(key))
                Add(t, WarnTag + "Flag " + shown + " is increased but never set with SetCampFlag in this file: the engine fails if the flag is still empty.");
        }
    }
}
