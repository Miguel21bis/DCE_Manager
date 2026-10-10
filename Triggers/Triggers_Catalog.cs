using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    // Type attendu pour un paramètre d'une fonction Return.* / Action.*.
    public enum TrigParam
    {
        Any,            // pas de contrôle
        Text,
        Number,
        Bool,
        Choice,         // une valeur parmi une courte liste (blue / red ...)
        Flag,           // numéro ou nom de flag de campagne
        TargetTitle,    // titleName d'un target (targetlist_init.lua)
        TargetName,     // name d'un target (targetlist_init.lua)
        AirUnit,        // nom d'escadrille (oob_air_init.lua)
        Airbase,        // nom de base (db_airbases.lua)
        Weather,        // "weather = { trend = 40, ... }" (Action.SetWeather)
        ShipRoute,      // {{"point1", "point2"}, {"point3"}} (Action.ShipMission)
        ShipZone,       // {"point1", "point2", "point3"} (Return.ShipGroupInPoly)
        ShipGroup,      // nom d'un groupe de navires (base_mission.miz, oob_ground)
        Time,           // heure HH:MM depuis le début de la campagne, écrite en secondes
        ShipSpeed       // vitesse en m/s, facultative (Action.ShipMission)
    }

    // Ce que renvoie une fonction Return.*.
    public enum TrigValue { None, Number, Bool, Text, Any }

    public class ParamDef
    {
        public string Label { get { return Lang.T(_label); } set { _label = value; } }
        private string _label = "";
        public TrigParam Type = TrigParam.Any;
        public bool Optional;
        public string Default { get { return Lang.T(_default); } set { _default = value; } }     // texte affiché quand le paramètre optionnel est absent
        private string _default = "";
        public string[] Choices;                // pour TrigParam.Choice
        public bool AllowTable;                 // accepte aussi une table { "a", "b" }
        public string Source = "";              // lu dans l'annotation --@param de ScriptsMod : d'où viennent les suggestions (mot-clé ou dossier/motif)
        public string Note = "";                // texte --@param de ScriptsMod, affiché en infobulle
        public double? Min, Max;                // bornes d'un nombre (--@param ... | type=percent, ou min= max=)
        public string Unit = "";                // affichée après le nombre : %, m/s...
    }

    // Documentation écrite dans DC_CheckTriggers.lua juste au-dessus d'une fonction (lignes --@doc, --@param, --@example, --@returns).
    public class FuncDoc
    {
        public List<string> Lines = new List<string>();
        public List<string> Examples = new List<string>();
        public string Returns = "";
        public string Deprecated;                                                               // --@deprecated : texte = pourquoi / par quoi la remplacer (null = pas obsolète)
        public string Category = "";                                                            // --@category : le thème dans lequel la fonction est rangée
        public bool Hidden;                                                                     // --@hide : la fonction n'est pas proposée dans l'éditeur
        public List<string> ParamNames = new List<string>();                                    // noms Lua des paramètres, dans l'ordre
        public Dictionary<string, string[]> Params = new Dictionary<string, string[]>();       // nom -> { source, texte }
    }

    public class FuncDef
    {
        public string Kind = "";                // "Return" (condition) ou "Action"
        public string Name = "";                // sans le préfixe
        // Category, Sentence et Help sont traduits à la lecture (voir Lang) : le catalogue reste écrit en anglais.
        public string Category { get { return CategoryText(CategoryOverride != null ? CategoryOverride : _category); } set { _category = value; } }
        // Le nom d'un thème dans la langue choisie. "Targets" reste "Targets" en français aussi (voulu : seulement pour les thèmes).
        public static string CategoryText(string english) { return english == "Targets" ? english : Lang.T(english); }
        public string CategoryRaw { get { return _category; } }     // le thème du catalogue, en anglais (sans traduction)
        public List<string> ExtraCategories = new List<string>();      // --@category Ground, Ships : la fonction apparaît aussi dans ces thèmes
        public IEnumerable<string> AllCategories { get { yield return Category; foreach (string c in ExtraCategories) yield return CategoryText(c); } }
        public string CategoryOverride;         // --@category dans ScriptsMod : prend la place du thème du catalogue
        public string Sentence { get { return Lang.T(_sentence); } set { _sentence = value; } }     // phrase affichée, {0} {1}... = les paramètres
        public string Help { get { return Lang.T(_help); } set { _help = value; } }
        private string _category = "", _sentence = "", _help = "";
        public TrigValue Returns = TrigValue.None;
        public bool Auto;                       // true = trouvée toute seule dans DC_CheckTriggers.lua (pas écrite dans ce catalogue)
        public List<ParamDef> Params = new List<ParamDef>();
        public string Deprecated;               // null = fonction à jour ; sinon la raison (--@deprecated dans ScriptsMod)
        public bool IsDeprecated { get { return Deprecated != null; } }
        public FuncDoc Doc;                     // annotations --@ trouvées dans ScriptsMod (null = aucune)

        // L'aide du catalogue + ce qui est écrit dans ScriptsMod, pour les infobulles.
        public string HelpFull
        {
            get
            {
                string h = Help;

                if (Deprecated != null)
                    h = Lang.T("DEPRECATED: ") + Deprecated + "\r\n\r\n" + h;

                if (Doc == null || Auto)
                    return h;

                var sb = new StringBuilder(h);

                if (Doc.Lines.Count > 0)
                    sb.Append("\r\n\r\n").Append(string.Join("\r\n", Doc.Lines.ToArray()));

                if (Doc.Returns.Length > 0)
                    sb.Append("\r\n").Append(Lang.T("Returns: ")).Append(Doc.Returns);

                foreach (string ex in Doc.Examples)
                    sb.Append("\r\n").Append(Lang.T("Example: ")).Append(ex);

                return sb.ToString();
            }
        }

        public string FullName { get { return Kind + "." + Name; } }

        // Le texte d'une ligne des listes de choix : les fonctions obsolètes sont marquées.
        public string ListText(string sentence)
        {
            return Deprecated != null ? Lang.T("(deprecated) ") + sentence : sentence;
        }

        public int MinArgs
        {
            get { return Params.Count(p => !p.Optional); }
        }
    }

    // Valeur lue sans appel de fonction : GroundTarget["blue"].percent, camp.date.day...
    public class RefDef
    {
        public Regex Pattern;
        public string Sentence { get { return Lang.T(_sentence); } set { _sentence = value; } }     // {1} = premier groupe du motif
        public string Help { get { return Lang.T(_help); } set { _help = value; } }
        private string _sentence = "", _help = "";
    }

    // LE catalogue : tout ce que l'éditeur sait décomposer. Un texte qui n'y est pas reste du Lua brut.
    // Pour ajouter une fonction : une ligne Fn(...) dans le constructeur statique, rien d'autre.
    // Les fonctions viennent de DC_CheckTriggers.lua (ScriptsMod).
    public static class TriggerCatalog
    {
        private static readonly Dictionary<string, FuncDef> _funcs = new Dictionary<string, FuncDef>(StringComparer.Ordinal);
        private static readonly List<RefDef> _refs = new List<RefDef>();

        public static IEnumerable<FuncDef> All { get { return _funcs.Values; } }
        public static IList<RefDef> Refs { get { return _refs; } }

        public static FuncDef Find(string fullName)
        {
            FuncDef def;
            return _funcs.TryGetValue(fullName, out def) ? def : null;
        }

        private static ParamDef P(string label, TrigParam type, bool optional = false, string def = "", bool allowTable = false)
        {
            return new ParamDef { Label = label, Type = type, Optional = optional, Default = def, AllowTable = allowTable };
        }

        private static ParamDef Pick(string label, bool optional, string def, params string[] choices)
        {
            return new ParamDef { Label = label, Type = TrigParam.Choice, Optional = optional, Default = def, Choices = choices };
        }

        private static void Fn(string kind, string name, string category, string sentence, TrigValue returns, string help, params ParamDef[] ps)
        {
            var f = new FuncDef { Kind = kind, Name = name, Category = category, Sentence = sentence, Returns = returns, Help = help };
            f.Params.AddRange(ps);
            _funcs[f.FullName] = f;
        }

        // ------------------------------------------------------------------------------------------
        //  Fonctions trouvées toute seules dans DC_CheckTriggers.lua
        //  Une fonction "Return.X(...)" ou "Action.X(...)" ajoutée dans ScriptsMod est utilisable dans l'éditeur
        //  sans attendre une nouvelle version de DCE_Manager : elle apparaît dans la catégorie "New in ScriptsMod",
        //  avec le nom de ses paramètres et les commentaires "--" écrits juste au-dessus. Pour lui donner une vraie phrase
        //  et des champs typés, il suffit d'ajouter une ligne Fn(...) plus bas : le catalogue écrit à la main passe toujours avant.
        //  Appelée à chaque ouverture de la fenêtre ; le fichier n'est relu que s'il a changé.
        // ------------------------------------------------------------------------------------------

        private static DateTime _scanStamp = DateTime.MinValue;
        private static readonly Regex DefRx = new Regex(@"^\s*function\s+(Return|Action)\.([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)");

        public static int AutoCount
        {
            get { return _funcs.Values.Count(f => f.Auto); }
        }

        public static void Refresh()
        {
            try
            {
                string path = Path.Combine(DcemLua.ScriptsModPath, "DC_CheckTriggers.lua");

                if (!File.Exists(path))
                    return;

                DateTime stamp = File.GetLastWriteTimeUtc(path);

                if (stamp == _scanStamp)
                    return;

                _scanStamp = stamp;

                foreach (string key in _funcs.Where(kv => kv.Value.Auto).Select(kv => kv.Key).ToList())
                    _funcs.Remove(key);

                // la doc lue la dernière fois est effacée : on la relit entièrement
                foreach (FuncDef known in _funcs.Values)
                {
                    known.Doc = null;
                    known.Deprecated = null;
                    known.CategoryOverride = null;
                    known.ExtraCategories.Clear();

                    foreach (ParamDef kp in known.Params)
                    {
                        kp.Source = "";
                        kp.Note = "";
                        kp.Min = null;
                        kp.Max = null;
                        kp.Unit = "";
                    }
                }

                var comments = new List<string>();
                var doc = new FuncDoc();
                bool hasDoc = false;

                foreach (string line in File.ReadAllLines(path))
                {
                    string t = line.Trim();

                    if (t.StartsWith("--@"))
                    {
                        ParseDocLine(doc, t.Substring(3).Trim());
                        hasDoc = true;
                        continue;
                    }

                    if (t.StartsWith("--"))
                    {
                        comments.Add(t.TrimStart('-', ' ', '\t'));
                        continue;
                    }

                    Match m = DefRx.Match(line);

                    if (m.Success)
                    {
                        string kind = m.Groups[1].Value, name = m.Groups[2].Value;
                        // inusitées : on ne les propose pas (ou bien --@hide au-dessus de la fonction dans ScriptsMod)
                        bool hidden = (hasDoc && doc.Hidden) || kind + "." + name == "Action.TextPlayMission";

                        if (!hidden)
                        {
                            AddAuto(kind, name, m.Groups[3].Value, hasDoc ? new List<string>(doc.Lines) : comments);

                            FuncDef f;

                            if (hasDoc && _funcs.TryGetValue(kind + "." + name, out f))
                                ApplyDoc(f, doc, m.Groups[3].Value);
                        }
                    }

                    comments.Clear();
                    doc = new FuncDoc();
                    hasDoc = false;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("TriggerCatalog.Refresh | " + ex.Message);
            }
        }

        // "doc texte" / "param nom | source=... | texte" / "example ..." / "returns ..."
        private static void ParseDocLine(FuncDoc doc, string s)
        {
            int sp = 0;

            while (sp < s.Length && !char.IsWhiteSpace(s[sp]))
                sp++;

            string tag = s.Substring(0, sp).ToLowerInvariant();
            string rest = s.Substring(sp).Trim();

            switch (tag)
            {
                case "doc": if (rest.Length > 0) doc.Lines.Add(rest); break;
                case "example": if (rest.Length > 0) doc.Examples.Add(rest); break;
                case "returns": doc.Returns = rest; break;
                case "hide": doc.Hidden = true; break;
                case "category": doc.Category = rest; break;
                case "deprecated": doc.Deprecated = CleanReason(rest); break;
                case "param":
                    string[] parts = rest.Split('|');
                    string pname = parts[0].Trim();
                    string source = "", text = "", type = "", min = "", max = "", unit = "";

                    for (int i = 1; i < parts.Length; i++)
                    {
                        string part = parts[i].Trim();

                        if (part.StartsWith("source=", StringComparison.OrdinalIgnoreCase))
                            source = part.Substring(7).Trim();
                        else if (IsOptionPart(part))
                        {
                            // "type=probability | min=0 | max=1" ou tout sur un champ : "type=probability, min=0, max=1"
                            var rest2 = new List<string>();

                            foreach (string token in part.Split(','))
                            {
                                string tk = token.Trim();

                                if (tk.StartsWith("type=", StringComparison.OrdinalIgnoreCase))
                                    type = tk.Substring(5).Trim().ToLowerInvariant();
                                else if (tk.StartsWith("min=", StringComparison.OrdinalIgnoreCase))
                                    min = tk.Substring(4).Trim();
                                else if (tk.StartsWith("max=", StringComparison.OrdinalIgnoreCase))
                                    max = tk.Substring(4).Trim();
                                else if (tk.StartsWith("unit=", StringComparison.OrdinalIgnoreCase))
                                    unit = tk.Substring(5).Trim();
                                else if (tk.Length > 0)
                                    rest2.Add(tk);
                            }

                            if (rest2.Count > 0)
                                text = text.Length > 0 ? text + " | " + string.Join(", ", rest2.ToArray()) : string.Join(", ", rest2.ToArray());
                        }
                        else if (part.Length > 0)
                            text = text.Length > 0 ? text + " | " + part : part;
                    }

                    if (pname.Length > 0)
                        doc.Params[pname] = new[] { source, text, type, min, max, unit };

                    break;
            }
        }

        // --@category airUnit retrouve le thème existant "Air units" : majuscules, espaces, pluriel et langue sans importance.
        // Quelques synonymes sont reconnus ; un nom inconnu crée un nouveau thème (écrit comme vous l'avez tapé).
        private static string ResolveCategory(string wanted)
        {
            Func<string, string> norm = x => x.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "").TrimEnd('s');
            string key = norm(wanted);

            string alias;

            if (CategoryAliases.TryGetValue(key, out alias))
                return alias;

            foreach (FuncDef f in _funcs.Values)
            {
                foreach (string raw in new[] { f.CategoryRaw, f.CategoryOverride })
                {
                    if (raw == null)
                        continue;

                    if (norm(raw) == key || norm(Lang.T(raw)) == key)
                        return raw;
                }
            }

            return wanted;
        }

        private static readonly Dictionary<string, string> CategoryAliases = new Dictionary<string, string>
        {
            { "heliunit", "Air units" },
            { "helicopter", "Air units" },
            { "airbase", "Air bases" },
            { "border", "Border" },
            { "boarder", "Border" },
            { "frontier", "Border" },
            { "frontline", "Border" }
        };

        // "| alternates= Use the editor X" ou "Use the editor X" : on garde seulement la phrase.
        private static string CleanReason(string raw)
        {
            string r = raw.TrimStart('|', ' ', '\t');

            foreach (string prefix in new[] { "alternates=", "alternate=", "use=", "replace=", "instead=" })
                if (r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    r = r.Substring(prefix.Length).Trim();
                    break;
                }

            return r.Length > 0 ? r : Lang.T("no longer used");
        }

        // Un champ qui commence par type=, min=, max= ou unit= (source= garde ses virgules : c'est une liste de valeurs).
        private static bool IsOptionPart(string part)
        {
            return part.StartsWith("type=", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("min=", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("max=", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("unit=", StringComparison.OrdinalIgnoreCase);
        }

        // Relie la doc à la fonction : chaque --@param est rattaché au paramètre de même nom dans la ligne "function".
        private static void ApplyDoc(FuncDef f, FuncDoc doc, string parameters)
        {
            foreach (string raw in parameters.Split(','))
            {
                string n = raw.Trim();

                if (n.Length > 0 && n != "...")
                    doc.ParamNames.Add(n);
            }

            f.Doc = doc;
            f.Deprecated = doc.Deprecated;

            if (doc.Category.Length > 0)
            {
                // --@category Ground, Ships : le premier est le thème principal, les autres sont des thèmes en plus
                string[] wanted = doc.Category.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

                for (int c = 0; c < wanted.Length; c++)
                {
                    string name = wanted[c].Trim();

                    if (name.Length == 0)
                        continue;

                    name = ResolveCategory(name);

                    if (f.CategoryOverride == null)
                        f.CategoryOverride = name;
                    else if (name != f.CategoryOverride && !f.ExtraCategories.Contains(name))
                        f.ExtraCategories.Add(name);
                }
            }

            for (int i = 0; i < doc.ParamNames.Count && i < f.Params.Count; i++)
            {
                string[] v;

                if (!doc.Params.TryGetValue(doc.ParamNames[i], out v))
                    continue;

                f.Params[i].Source = v[0];
                f.Params[i].Note = v[1];

                // type=percent (0 à 100, "%") ou type=number avec min= max= unit= : un champ chiffré
                if (v[2] == "percent" || v[2] == "probability" || v[2] == "number")
                {
                    double lo, hi;
                    f.Params[i].Type = TrigParam.Number;

                    if (v[2] == "probability")
                    {
                        f.Params[i].Min = 0;
                        f.Params[i].Max = 1;
                    }

                    if (v[2] == "percent")
                    {
                        f.Params[i].Min = 0;
                        f.Params[i].Max = 100;
                        f.Params[i].Unit = "%";
                    }

                    if (double.TryParse(v[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lo))
                        f.Params[i].Min = lo;

                    if (double.TryParse(v[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hi))
                        f.Params[i].Max = hi;

                    if (v[5].Length > 0)
                        f.Params[i].Unit = v[5];
                }

                if (v[0].Length > 0 && f.Params[i].Type == TrigParam.Any)
                    f.Params[i].Type = TrigParam.Text;          // une liste de noms = du texte
            }
        }

        private static void AddAuto(string kind, string name, string parameters, List<string> comments)
        {
            if (_funcs.ContainsKey(kind + "." + name))
                return;                                         // déjà dans le catalogue écrit à la main

            var f = new FuncDef
            {
                Kind = kind,
                Name = name,
                Category = "New in ScriptsMod",
                Returns = kind == "Return" ? TrigValue.Any : TrigValue.None,
                Help = comments.Count > 0 ? string.Join(" ", comments.Take(6).ToArray()) : "Found in DC_CheckTriggers.lua. No description there.",
                Auto = true
            };

            var parts = new List<string>();

            foreach (string raw in parameters.Split(','))
            {
                string p = raw.Trim();

                if (p.Length == 0 || p == "...")
                    continue;

                // Lua ne dit pas si un paramètre est facultatif : on ne bloque donc jamais (Optional)
                f.Params.Add(new ParamDef { Label = p, Type = TrigParam.Any, Optional = true });
                parts.Add("{" + (f.Params.Count - 1) + "}");
            }

            f.Sentence = name + (parts.Count > 0 ? " (" + string.Join(", ", parts.ToArray()) + ")" : "");
            _funcs[f.FullName] = f;
        }

        private static void Ref(string pattern, string sentence, string help)
        {
            _refs.Add(new RefDef { Pattern = new Regex(pattern), Sentence = sentence, Help = help });
        }

        static TriggerCatalog()
        {
            const string Cond = "Return";
            const string Act = "Action";

            // ---------------- CONDITIONS (Return.*) ----------------
            Fn(Cond, "Mission", "Campaign", "the mission number", TrigValue.Number,
                "Number of the mission being generated. The first mission is number 1.");
            Fn(Cond, "Time", "Date and time", "the time of day (in seconds)", TrigValue.Number,
                "Time of day in seconds since midnight.");
            Fn(Cond, "Day", "Date and time", "the day of the month", TrigValue.Number, "Day of the month of the campaign date.");
            Fn(Cond, "Month", "Date and time", "the month number", TrigValue.Number, "Month of the campaign date (1 to 12).");
            Fn(Cond, "Year", "Date and time", "the year", TrigValue.Number, "Year of the campaign date.");
            Fn(Cond, "DatePassed", "Date and time", "the campaign date is {0}-{1}-{2} or later", TrigValue.Bool,
                "True when the campaign date is on or after the given date. Safe with time jumps.",
                P("year", TrigParam.Number), P("month", TrigParam.Number), P("day", TrigParam.Number));
            Fn(Cond, "DateBefore", "Date and time", "the campaign date is before {0}-{1}-{2}", TrigValue.Bool,
                "True while the campaign date is earlier than the given date (the opposite of DatePassed, no 'not' needed).",
                P("year", TrigParam.Number), P("month", TrigParam.Number), P("day", TrigParam.Number));
            Fn(Cond, "CampFlag", "Flags", "flag {0}", TrigValue.Any,
                "Value of a campaign flag, as it was at the START of the mission generation (changes made by other triggers in the same run are not seen). Empty if never set.",
                P("flag", TrigParam.Flag));

            Fn(Cond, "AirUnitActive", "Air units", "air unit {0} is active", TrigValue.Bool,
                "True when the air unit is not deactivated.", P("air unit", TrigParam.AirUnit));
            Fn(Cond, "AirUnitReady", "Air units", "the ready aircraft of {0}", TrigValue.Number,
                "Number of ready aircraft in the air unit.", P("air unit", TrigParam.AirUnit));
            Fn(Cond, "AirUnitAlive", "Air units", "the alive aircraft of {0} (ready + damaged + reserve)", TrigValue.Number,
                "Number of ready, damaged and reserve aircraft in the air unit.", P("air unit", TrigParam.AirUnit));
            Fn(Cond, "totalAirUnitAliveBySide", "Air units", "all the alive aircraft of side {0}", TrigValue.Number,
                "Total of ready, damaged and reserve aircraft of the active air units of a side.",
                Pick("side", false, "", "blue", "red", "neutral"));
            Fn(Cond, "AirUnitBase", "Air units", "the base of air unit {0}", TrigValue.Text,
                "Name of the airbase the air unit operates from.", P("air unit", TrigParam.AirUnit));
            Fn(Cond, "AirUnitPlayer", "Air units", "air unit {0} is playable", TrigValue.Bool,
                "True when the air unit can be flown by a player.", P("air unit", TrigParam.AirUnit));

            Fn(Cond, "TargetAlive", "Targets", "the alive % of target {0}", TrigValue.Number,
                "Percentage of the target still alive (100 = untouched). The target is found by its titleName.",
                P("target", TrigParam.TargetTitle));
            Fn(Cond, "targetIsActive", "Targets", "target {0} is active", TrigValue.Bool,
                "True when the target is active. The target is found by its titleName.", P("target", TrigParam.TargetTitle));
            Fn(Cond, "BaseAlive", "Targets", "the alive % of base {0}", TrigValue.Number,
                "Percentage of the base targets still alive. Accepts a target titleName or an airbase name.", P("base", TrigParam.Text));

            Fn(Cond, "UnitDead", "Ground", "unit {0} is dead", TrigValue.Bool,
                "True when this vehicle or ship unit is dead.", P("unit", TrigParam.Text));
            Fn(Cond, "GroupHidden", "Ground", "group {0} is hidden", TrigValue.Bool,
                "Hidden status of a vehicle or ship group.", P("group", TrigParam.Text));
            Fn(Cond, "GroupProbability", "Ground", "the spawn probability of group {0}", TrigValue.Number,
                "Spawn probability of a vehicle or ship group, from 0 to 1.", P("group", TrigParam.Text));
            Fn(Cond, "ShipGroupInPoly", "Ships", "ship group {0} is inside the area {1}", TrigValue.Bool,
                "True when the ship group is inside the polygon made of the given reference points.",
                P("group", TrigParam.ShipGroup), P("zones", TrigParam.ShipZone));

            Fn(Cond, "PlaceLogistic", "Logistics", "the logistic weight at {0}", TrigValue.Number,
                "Weight delivered by airlift to the place.", P("place", TrigParam.Airbase));
            Fn(Cond, "LogisticObjectif", "Logistics", "the airlift progress at {0} (% of {1} kg)", TrigValue.Number,
                "Percentage of the airlift objective reached at the place.", P("place", TrigParam.Airbase), P("weight", TrigParam.Number));

            // ---------------- ACTIONS (Action.*) ----------------
            Fn(Act, "None", "Campaign", "do nothing", TrigValue.None, "Empty action.");
            Fn(Act, "CampaignEnd", "Campaign", "end the campaign: {0}", TrigValue.None,
                "Ends the campaign with this result.", Pick("result", false, "", "win", "draw", "loss"));
            Fn(Act, "SetCampFlag", "Flags", "set flag {0} to {1}", TrigValue.None,
                "Gives a value to a campaign flag (number, true/false or text).", P("flag", TrigParam.Flag), P("value", TrigParam.Any));
            Fn(Act, "AddCampFlag", "Flags", "add {1} to flag {0}", TrigValue.None,
                "Adds a number to a campaign flag. The flag must already have a value, or the engine stops.",
                P("flag", TrigParam.Flag), P("number", TrigParam.Number));

            Fn(Act, "Text", "Briefing", "add this text to the briefing: {0}", TrigValue.None,
                "Adds a paragraph to the briefing. The same text is never added twice.", P("text", TrigParam.Text), P("clear", TrigParam.Any, true, ""));
            Fn(Act, "AddImage", "Briefing", "add the briefing picture {0} for {1}", TrigValue.None,
                "Adds a picture to the briefing of one side or of both sides.",
                P("file", TrigParam.Text), Pick("side", true, "all", "blue", "red", "all", ""));
            Fn(Act, "SetWeather", "Weather", "change the weather: {0}", TrigValue.None,
                "Changes the settings used to generate the weather of the next missions (written to conf_mod.lua). Only the ticked settings change.",
                P("weather", TrigParam.Weather));

            Fn(Act, "TargetActive", "Targets", "set target {0} active to {1}", TrigValue.None,
                "Turns a target on or off. The target is found by its titleName.", P("target", TrigParam.TargetTitle), P("active", TrigParam.Bool));
            Fn(Act, "TargetPriority", "Targets", "set the priority of target {0} to {1}", TrigValue.None,
                "Changes the priority of a target. The target is found by its titleName.", P("target", TrigParam.TargetTitle), P("priority", TrigParam.Number));
            Fn(Act, "UnitResuscitateOrKill", "Targets", "bring back to life (true) or kill (false) target {0}: {1} (value {2})", TrigValue.None,
                "Resuscitates or kills the units of a target. The target is found by its name (not its titleName).",
                P("target name", TrigParam.TargetName), P("live (true) or kill (false)", TrigParam.Bool), P("value", TrigParam.Number, true, "default"));
            Fn(Act, "AddGroundTargetIntel", "Targets", "add the ground target intel of side {0}", TrigValue.None,
                "Adds the ground targets of a side to the briefing (first mission generation only).", Pick("side", false, "", "blue", "red"));
            Fn(Act, "GroundUnitRepair", "Ground", "repair the ground units", TrigValue.None, "Repairs ground units.");

            Fn(Act, "AirUnitActive", "Air units", "set air unit {0} active to {1}", TrigValue.None,
                "Turns an air unit on or off.", P("air unit", TrigParam.AirUnit), P("active", TrigParam.Bool));
            Fn(Act, "AirUnitPlayer", "Air units", "set air unit {0} playable to {1}", TrigValue.None,
                "Allows or forbids players in an air unit.", P("air unit", TrigParam.AirUnit), P("playable", TrigParam.Bool));
            Fn(Act, "AirUnitBase", "Air units", "move air unit {0} to base {1}", TrigValue.None,
                "Moves an air unit to another base. A list of bases can be given.",
                P("air unit", TrigParam.AirUnit), new ParamDef { Label = "base", Type = TrigParam.Airbase, AllowTable = true });
            Fn(Act, "moveToAnotherBaseOrDeactivate", "Air units", "move air unit {0} to base {1}, or deactivate it", TrigValue.None,
                "Moves an air unit to another base, or deactivates it when that is not possible.",
                P("air unit", TrigParam.AirUnit), new ParamDef { Label = "base", Type = TrigParam.Airbase, AllowTable = true });
            Fn(Act, "AirUnitReinforce", "Air units", "reinforce {1} from {0}", TrigValue.None,
                "Sends aircraft from one air unit to another. With one air unit only, it is reinforced from the reserves.",
                P("from", TrigParam.AirUnit), P("to", TrigParam.AirUnit, true, "the same unit"), P("number (old, ignored)", TrigParam.Number, true, ""));
            Fn(Act, "AirUnitRepair", "Air units", "repair the air units of {0}", TrigValue.None,
                "Repairs the aircraft of one side or of both sides.", Pick("side", true, "both sides", "blue", "red"));
            Fn(Act, "ActivateBaseAndItsUnits", "Air units", "set base {0} and its units active to {1}", TrigValue.None,
                "Turns a base, its targets and its air units on or off.", P("base", TrigParam.Airbase), P("active", TrigParam.Bool));
            Fn(Act, "SideBase", "Air units", "give base {1} to side {0}", TrigValue.None,
                "Changes the side of a base. Move the air units away first.", Pick("side", false, "", "blue", "red"), P("base", TrigParam.Airbase));

            Fn(Act, "GroupHidden", "Ground", "set group {0} hidden to {1}", TrigValue.None,
                "Hides or shows a vehicle or ship group.", P("group", TrigParam.Text), P("hidden", TrigParam.Bool));
            Fn(Act, "GroupProbability", "Ground", "set the spawn probability of group {0} to {1}", TrigValue.None,
                "Probability from 0 to 1.", P("group", TrigParam.Text), P("probability", TrigParam.Number));
            Fn(Act, "GroupMove", "Ground", "move group {0} to the zone {1}", TrigValue.None,
                "Moves a vehicle group to a reference point.", P("group", TrigParam.Text), P("zone", TrigParam.Text));
            Fn(Act, "GroupSlave", "Ground", "move group {0} next to {1} (bearing {2}, distance {3})", TrigValue.None,
                "Moves a vehicle group relative to another group.",
                P("group", TrigParam.Text), P("master group", TrigParam.Text), P("bearing", TrigParam.Number), P("distance", TrigParam.Number));
            Fn(Act, "ShipMission", "Ships", "give a sea route to ship group {0}: {1} (speed {2}, patrol speed {3}, start {4})", TrigValue.None,
                "Gives a movement mission to a ship group. It sails to the points of one alternative (picked at random). Carriers always use their own maximum speed.",
                P("group", TrigParam.ShipGroup), P("zones", TrigParam.ShipRoute), P("cruise speed", TrigParam.ShipSpeed, true, "default"),
                P("patrol speed", TrigParam.ShipSpeed, true, "no patrol"), P("start time", TrigParam.Time, true, "now"));
            Fn(Act, "KillPedro", "Ground", "kill helicopter unit {0}", TrigValue.None, "Marks a helicopter unit as dead.", P("unit", TrigParam.Text));

            Fn(Act, "TemplateActive", "Templates", "activate the template {0}", TrigValue.None,
                "Activates a ground template (moving front). A list of templates activates one at random.",
                new ParamDef { Label = "template", Type = TrigParam.Text, AllowTable = true }, P("extra", TrigParam.Any, true, ""), P("extra", TrigParam.Any, true, ""));
            Fn(Act, "TemplateDeactivate", "Templates", "deactivate the template {0}", TrigValue.None,
                "Deactivates a ground template.", new ParamDef { Label = "template", Type = TrigParam.Text, AllowTable = true }, P("extra", TrigParam.Any, true, ""));
            Fn(Act, "LoadFileBorder", "Templates", "load the border file {0}", TrigValue.None, "Loads a border file.", P("file", TrigParam.Text));

            Fn(Act, "RestrictedLoadout", "Loadouts", "restrict the player loadouts with {0}", TrigValue.None,
                "Forbids some items to players, from a restricted loadout file.", P("file", TrigParam.Text));
            Fn(Act, "AuthorizedLoadout", "Loadouts", "authorize the loadouts named {0}", TrigValue.None,
                "Authorizes some loadouts for the AI.", P("name", TrigParam.Text));

            Fn(Act, "LogisticObjectif", "Logistics", "set the airlift objective at {0} to {1} kg", TrigValue.None,
                "Sets an airlift objective for a place.", P("place", TrigParam.Airbase), P("weight", TrigParam.Number));

            // ---------------- VALEURS SANS APPEL ----------------
            Ref(@"^GroundTarget\[""(blue|red)""\]\.percent$", "the ground targets percent ({1} list)",
                "Percentage computed by the engine for the ground targets of a side.");
            Ref(@"^camp\.date\.(day|month|year)$", "the campaign {1}", "Part of the campaign date.");
            Ref(@"^MissionInstance$", "the mission instance (1 = first generation)",
                "Counts the generations of the same mission; 1 for the first one.");
            Ref(@"^campMod\.RepairMinimumDestroyed$", "the 'repair minimum destroyed' setting",
                "Setting from conf_mod.lua, replaced by its value when the condition is read.");
        }
    }

    // Les noms existants dans la campagne : servent à repérer un nom de cible / escadrille / base inconnu.
    // Un fichier absent ou illisible = liste vide ET Has... = false : dans ce cas on ne contrôle rien.
    public class TriggerLists
    {
        public HashSet<string> TargetTitles = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> TargetNames = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> AirUnits = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Airbases = new HashSet<string>(StringComparer.Ordinal);

        // Cibles de Active\targetlist.lua : elles peuvent être créées en cours de campagne (templates activés par un trigger)
        public HashSet<string> ActiveTargetTitles = new HashSet<string>(StringComparer.Ordinal);

        public bool HasTargets;
        public bool HasAirUnits;
        public bool HasAirbases;

        // Groupes de navires et zones trigger : lus dans Init\base_mission.miz (et oob_ground), seulement quand on en a besoin
        public HashSet<string> ShipGroups = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Zones = new HashSet<string>(StringComparer.Ordinal);
        public bool HasShipGroups;

        // groupes de véhicules, unités au sol/navires et unités d'hélicoptères (même lecture que les navires)
        public HashSet<string> VehicleGroups = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> GroundUnits = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> HeliUnits = new HashSet<string>(StringComparer.Ordinal);
        public bool HasZones;

        private string _campaignName = "";
        private bool _missionLoaded;

        public static TriggerLists Load(string campaignName)
        {
            var lists = new TriggerLists();
            lists._campaignName = campaignName;

            lists.LoadTargets(DcemLua.CampaignInitFile(campaignName, "targetlist_init.lua"), lists.TargetTitles, lists.TargetNames, true);
            lists.LoadTargets(Path.Combine(DcemLua.CampaignPath(campaignName), "Active", "targetlist.lua"), lists.ActiveTargetTitles, lists.TargetNames, false);
            lists.LoadAirUnits(DcemLua.CampaignInitFile(campaignName, "oob_air_init.lua"));
            lists.LoadAirbases(DcemLua.CampaignInitFile(campaignName, "db_airbases.lua"));

            return lists;
        }

        // Lit base_mission.miz (un zip, fichier "mission" dedans) et oob_ground : groupes de navires + zones trigger.
        // Appelé au premier besoin seulement : le fichier "mission" pèse plusieurs Mo.
        public void EnsureMissionLoaded()
        {
            if (_missionLoaded)
                return;

            _missionLoaded = true;

            string miz = DcemLua.CampaignInitFile(_campaignName, "base_mission.miz");

            if (File.Exists(miz))
            {
                try
                {
                    string text;

                    using (ZipArchive zip = ZipFile.OpenRead(miz))
                    {
                        ZipArchiveEntry entry = zip.GetEntry("mission");

                        if (entry == null)
                            return;

                        using (var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8))
                            text = reader.ReadToEnd();
                    }

                    using (Lua lua = DcemLua.NewState())
                    {
                        lua.DoString("os = nil\nio = nil\nfile = nil\ndebug = nil");
                        lua.DoString(text);

                        LuaTable mission = lua["mission"] as LuaTable;
                        LuaTable triggers = mission == null ? null : mission["triggers"] as LuaTable;
                        LuaTable zones = triggers == null ? null : triggers["zones"] as LuaTable;

                        if (zones != null)
                        {
                            foreach (object key in zones.Keys)
                            {
                                LuaTable zone = zones[key] as LuaTable;
                                string name = zone == null ? null : zone["name"] as string;

                                if (!string.IsNullOrEmpty(name))
                                    Zones.Add(name);
                            }

                            HasZones = true;
                        }

                        LuaTable coalitions = mission == null ? null : mission["coalition"] as LuaTable;

                        if (coalitions != null)
                        {
                            foreach (object sideKey in coalitions.Keys)
                            {
                                LuaTable side = coalitions[sideKey] as LuaTable;
                                LuaTable countries = side == null ? null : side["country"] as LuaTable;

                                if (countries != null)
                                    AddGroups(countries);
                            }

                            HasShipGroups = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("TriggerLists | lecture impossible de " + miz + " : " + ex.Message);
                }
            }

            // oob_ground (groupes déjà connus de la campagne) : Init d'abord, puis Active s'il existe
            LoadOobShips(DcemLua.CampaignInitFile(_campaignName, "oob_ground_init.lua"));
            LoadOobShips(Path.Combine(DcemLua.CampaignPath(_campaignName), "Active", "oob_ground.lua"));
        }

        // ----- listes demandées par une annotation --@param ... | source=... dans DC_CheckTriggers.lua -----
        // source = un mot-clé (restrictedNames, restrictedFiles, borderFiles, zones, groups, bases, targets, units)
        //       ou un dossier de la campagne avec un motif : /Restricted_loadouts/*.miz
        // null = source inconnue : le champ reste un texte libre.
        public List<string> ListFromSource(string source)
        {
            string key = (source ?? "").Trim();

            if (LiteralChoices(key) != null)
                return null;                            // une liste de valeurs (true/false...), pas des noms de fichiers

            switch (key.ToLowerInvariant())
            {
                case "restrictednames":
                case "restrictedname":
                    return RestrictedNames();
                case "restrictedfiles":
                    return CampaignFiles("Restricted_loadouts", "*.miz").Union(CampaignFiles("Loadouts", "*.miz")).ToList();
                case "borderfiles":
                    return CampaignFiles("Files", "*.miz");
                case "zones":
                    EnsureMissionLoaded();
                    return HasZones ? Zones.ToList() : null;
                case "groups":
                case "shipgroups":
                    EnsureMissionLoaded();
                    return HasShipGroups ? ShipGroups.ToList() : null;
                case "bases":
                    return Airbases.Union(TargetTitles).Union(ActiveTargetTitles).ToList();
                case "targets":
                case "targettitle":
                case "targettitles":
                    return TargetTitles.Union(ActiveTargetTitles).ToList();
                case "targetname":
                case "targetnames":
                    return TargetNames.ToList();
                case "units":
                case "airunits":
                    return AirUnits.ToList();
                case "vehiclegroups":
                    EnsureMissionLoaded();
                    return VehicleGroups.Count > 0 ? VehicleGroups.ToList() : null;
                case "groundgroups":
                    EnsureMissionLoaded();
                    return VehicleGroups.Union(ShipGroups).ToList();
                case "groundunits":
                    EnsureMissionLoaded();
                    return GroundUnits.Count > 0 ? GroundUnits.ToList() : null;
                case "heliunits":
                    EnsureMissionLoaded();
                    return HeliUnits.Count > 0 ? HeliUnits.ToList() : null;
            }

            string norm = key.Replace('\\', '/').Trim('/');
            int cut = norm.LastIndexOf('/');

            if (cut < 0)
                return null;

            string folder = norm.Substring(0, cut);
            string file = norm.Substring(cut + 1);
            string pattern = file.Contains("*") ? file : "*" + Path.GetExtension(file);

            return CampaignFiles(folder, pattern == "*" ? "*.*" : pattern);
        }

        // source=true/false ou source=low, medium, high : une courte liste de valeurs écrites telles quelles.
        // Ce n'est pas une liste de valeurs si c'est un chemin (/Dossier/..., avec * ou un point) : null.
        public static string[] LiteralChoices(string source)
        {
            string s = (source ?? "").Trim();

            if (s.Length == 0 || s[0] == '/' || s.IndexOfAny(new[] { '*', '.', '\\' }) >= 0)
                return null;

            string[] parts = s.Split(new[] { '/', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

            return parts.Length >= 2 ? parts : null;
        }

        // Une valeur de la liste -> le Lua à écrire : true, false, nil et les nombres tels quels, le reste entre guillemets.
        public static string LuaLiteral(string value)
        {
            double number;

            if (value == "true" || value == "false" || value == "nil"
                || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
                return value;

            return LuaText.Quote(value);
        }

        // Les noms de fichiers d'un dossier de la campagne (avec leur extension).
        private List<string> CampaignFiles(string folder, string pattern)
        {
            try
            {
                string dir = Path.Combine(DcemLua.CampaignPath(_campaignName), folder);

                if (!Directory.Exists(dir))
                    return new List<string>();

                return Directory.GetFiles(dir, pattern).Select(f => Path.GetFileName(f)).ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        // Les noms que AuthorizedLoadout peut recevoir : toutes les valeurs restrictedCondition des db_loadouts (pas les noms de fichiers .miz).
        private List<string> RestrictedNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var rxOne = new Regex("restrictedCondition\\s*=\\s*[\"']([^\"']+)[\"']");
            var rxMany = new Regex("restrictedCondition\\s*=\\s*\\{([^}]*)\\}");
            var rxItem = new Regex("[\"']([^\"']+)[\"']");

            string[] places =
            {
                Path.Combine(DcemLua.ScriptsModPath, "db_loadouts"),
                DcemLua.CampaignInitFile(_campaignName, "db_loadouts")
            };

            foreach (string place in places)
            {
                try
                {
                    IEnumerable<string> files;

                    if (Directory.Exists(place))
                        files = Directory.GetFiles(place, "*.lua");
                    else if (File.Exists(place))
                        files = new[] { place };
                    else if (File.Exists(place + ".lua"))
                        files = new[] { place + ".lua" };
                    else
                        continue;

                    foreach (string file in files)
                    {
                        string text = File.ReadAllText(file);

                        foreach (Match m in rxOne.Matches(text))
                            names.Add(m.Groups[1].Value);

                        foreach (Match m in rxMany.Matches(text))
                            foreach (Match item in rxItem.Matches(m.Groups[1].Value))
                                names.Add(item.Groups[1].Value);
                    }
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("TriggerLists | db_loadouts illisible : " + ex.Message);
                }
            }

            return names.ToList();
        }

        // ----- note "armes interdites" sous Action.RestrictedLoadout -----
        private readonly Dictionary<string, string> _restrictedCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Lit le .miz restreint comme le fait le moteur (unit.payload.restricted de chaque avion) et le résume en texte.
        public string RestrictedSummary(string file)
        {
            string name = (file ?? "").Trim();

            if (name.Length == 0)
                return "";

            string cached;

            if (_restrictedCache.TryGetValue(name, out cached))
                return cached;

            if (!name.EndsWith(".miz", StringComparison.OrdinalIgnoreCase))
                name += ".miz";

            string path = null;

            foreach (string folder in new[] { "Restricted_loadouts", "Loadouts" })
            {
                string candidate = Path.Combine(DcemLua.CampaignPath(_campaignName), folder, name);

                if (File.Exists(candidate)) { path = candidate; break; }
            }

            string result;

            if (path == null)
            {
                result = Lang.T("File not found in Restricted_loadouts or Loadouts.");
            }
            else
            {
                try
                {
                    result = ReadRestricted(path);
                }
                catch (Exception ex)
                {
                    FormUtils.LogRegister("TriggerLists | restricted miz illisible : " + ex.Message);
                    result = Lang.T("Could not read this file.");
                }
            }

            _restrictedCache[(file ?? "").Trim()] = result;

            return result;
        }

        private static string ReadRestricted(string path)
        {
            string text;

            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry entry = zip.GetEntry("mission");

                if (entry == null)
                    return Lang.T("Could not read this file.");

                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    text = reader.ReadToEnd();
            }

            // type d'avion -> pylône -> objets interdits
            var byType = new SortedDictionary<string, SortedDictionary<int, SortedSet<string>>>(StringComparer.OrdinalIgnoreCase);

            using (Lua lua = DcemLua.NewState())
            {
                lua.DoString("os = nil\nio = nil\nfile = nil\ndebug = nil");
                lua.DoString(text);

                LuaTable mission = lua["mission"] as LuaTable;
                LuaTable coalitions = mission == null ? null : mission["coalition"] as LuaTable;

                if (coalitions != null)
                {
                    foreach (object sideKey in coalitions.Keys)
                    {
                        LuaTable side = coalitions[sideKey] as LuaTable;
                        LuaTable countries = side == null ? null : side["country"] as LuaTable;

                        if (countries == null)
                            continue;

                        foreach (object countryKey in countries.Keys)
                        {
                            LuaTable country = countries[countryKey] as LuaTable;

                            if (country == null)
                                continue;

                            foreach (object categoryKey in country.Keys)
                            {
                                LuaTable category = country[categoryKey] as LuaTable;
                                LuaTable groups = category == null ? null : category["group"] as LuaTable;

                                if (groups == null)
                                    continue;

                                foreach (object groupKey in groups.Keys)
                                {
                                    LuaTable group = groups[groupKey] as LuaTable;
                                    LuaTable units = group == null ? null : group["units"] as LuaTable;

                                    if (units == null)
                                        continue;

                                    foreach (object unitKey in units.Keys)
                                        AddRestrictedUnit(byType, units[unitKey] as LuaTable);
                                }
                            }
                        }
                    }
                }
            }

            if (byType.Count == 0)
                return Lang.T("Nothing is forbidden in this file.");

            var sb = new StringBuilder();
            sb.Append(Lang.T("Forbidden in this file (by aircraft type, then pylon):")).Append("\r\n");

            foreach (var type in byType)
            {
                sb.Append("\r\n").Append(type.Key).Append("\r\n");

                foreach (var pylon in type.Value)
                    sb.Append("   ").Append(Lang.T("pylon ")).Append(pylon.Key).Append(": ").Append(string.Join(", ", pylon.Value.ToArray())).Append("\r\n");
            }

            return sb.ToString();
        }

        private static void AddRestrictedUnit(SortedDictionary<string, SortedDictionary<int, SortedSet<string>>> byType, LuaTable unit)
        {
            string type = unit == null ? null : unit["type"] as string;
            LuaTable payload = unit == null ? null : unit["payload"] as LuaTable;
            LuaTable restricted = payload == null ? null : payload["restricted"] as LuaTable;

            if (string.IsNullOrEmpty(type) || restricted == null)
                return;

            SortedDictionary<int, SortedSet<string>> pylons;

            if (!byType.TryGetValue(type, out pylons))
            {
                pylons = new SortedDictionary<int, SortedSet<string>>();
                byType[type] = pylons;
            }

            foreach (object pylonKey in restricted.Keys)
            {
                LuaTable items = restricted[pylonKey] as LuaTable;
                int pylonNumber;

                if (items == null)
                    continue;

                try { pylonNumber = Convert.ToInt32(pylonKey); }
                catch (Exception) { continue; }

                SortedSet<string> set;

                if (!pylons.TryGetValue(pylonNumber, out set))
                {
                    set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    pylons[pylonNumber] = set;
                }

                foreach (object itemKey in items.Keys)
                {
                    string item = items[itemKey] as string;

                    if (!string.IsNullOrEmpty(item))
                        set.Add(item);
                }
            }
        }

        // countries : table de pays. On relève les groupes de navires, de véhicules et les noms d'unités.
        private void AddGroups(LuaTable countries)
        {
            foreach (object countryKey in countries.Keys)
            {
                LuaTable country = countries[countryKey] as LuaTable;

                if (country == null)
                    continue;

                CollectNames(country["ship"] as LuaTable, ShipGroups, GroundUnits);
                CollectNames(country["vehicle"] as LuaTable, VehicleGroups, GroundUnits);
                CollectNames(country["helicopter"] as LuaTable, null, HeliUnits);
            }
        }

        // category = country.ship / country.vehicle / country.helicopter : groups[i].name et groups[i].units[j].name
        private static void CollectNames(LuaTable category, HashSet<string> groupNames, HashSet<string> unitNames)
        {
            LuaTable groups = category == null ? null : category["group"] as LuaTable;

            if (groups == null)
                return;

            foreach (object groupKey in groups.Keys)
            {
                LuaTable group = groups[groupKey] as LuaTable;

                if (group == null)
                    continue;

                string name = group["name"] as string;

                if (groupNames != null && !string.IsNullOrEmpty(name))
                    groupNames.Add(name);

                LuaTable units = group["units"] as LuaTable;

                if (units == null)
                    continue;

                foreach (object unitKey in units.Keys)
                {
                    LuaTable unit = units[unitKey] as LuaTable;
                    string unitName = unit == null ? null : unit["name"] as string;

                    if (!string.IsNullOrEmpty(unitName))
                        unitNames.Add(unitName);
                }
            }
        }

        private void LoadOobShips(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                using (Lua lua = DcemLua.NewState())
                {
                    LuaTable root = ReadGlobalTable(lua, path, "oob_ground");

                    if (root == null)
                        return;

                    foreach (object sideKey in root.Keys)
                    {
                        LuaTable side = root[sideKey] as LuaTable;

                        if (side != null)
                            AddGroups(side);
                    }

                    HasShipGroups = true;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("TriggerLists | lecture impossible de " + path + " : " + ex.Message);
            }
        }

        // Lit un fichier de données Lua dans un état bac à sable et renvoie sa table globale.
        // Le résultat n'est valable que tant que 'lua' est ouvert.
        private static LuaTable ReadGlobalTable(Lua lua, string path, string tableName)
        {
            lua.DoString("os = nil\nio = nil\nfile = nil\ndebug = nil");
            lua.DoFile(path);
            return lua[tableName] as LuaTable;
        }

        private void LoadTargets(string path, HashSet<string> titles, HashSet<string> names, bool isInit)
        {
            if (!File.Exists(path))
                return;

            try
            {
                using (Lua lua = DcemLua.NewState())
                {
                    LuaTable root = ReadGlobalTable(lua, path, "targetlist");

                    if (root == null)
                        return;

                    foreach (object sideKey in root.Keys)
                    {
                        LuaTable side = root[sideKey] as LuaTable;

                        if (side == null)
                            continue;

                        foreach (object targetKey in side.Keys)
                        {
                            LuaTable target = side[targetKey] as LuaTable;

                            if (target == null)
                                continue;

                            string title = target["titleName"] as string;
                            string name = target["name"] as string;

                            // targetlist_init : la clé de la table EST le nom ; titleName/name n'existent que dans Active\targetlist.lua
                            string key = targetKey as string;
                            if (!string.IsNullOrEmpty(key)) { titles.Add(key); names.Add(key); }

                            if (!string.IsNullOrEmpty(title)) titles.Add(title);
                            if (!string.IsNullOrEmpty(name)) names.Add(name);
                        }
                    }

                    if (isInit) HasTargets = true;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("TriggerLists | lecture impossible de " + path + " : " + ex.Message);
            }
        }

        private void LoadAirUnits(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                using (Lua lua = DcemLua.NewState())
                {
                    LuaTable root = ReadGlobalTable(lua, path, "oob_air");

                    if (root == null)
                        return;

                    foreach (object sideKey in root.Keys)
                    {
                        LuaTable side = root[sideKey] as LuaTable;

                        if (side == null)
                            continue;

                        foreach (object unitKey in side.Keys)
                        {
                            LuaTable unit = side[unitKey] as LuaTable;

                            if (unit == null)
                                continue;

                            string name = unit["name"] as string;

                            if (!string.IsNullOrEmpty(name))
                                AirUnits.Add(name);
                        }
                    }

                    HasAirUnits = true;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("TriggerLists | lecture impossible de " + path + " : " + ex.Message);
            }
        }

        private void LoadAirbases(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                using (Lua lua = DcemLua.NewState())
                {
                    LuaTable root = ReadGlobalTable(lua, path, "db_airbases");

                    if (root == null)
                        return;

                    foreach (object baseKey in root.Keys)
                    {
                        string name = baseKey as string;

                        if (!string.IsNullOrEmpty(name))
                            Airbases.Add(name);
                    }

                    HasAirbases = true;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("TriggerLists | lecture impossible de " + path + " : " + ex.Message);
            }
        }
    }
}
