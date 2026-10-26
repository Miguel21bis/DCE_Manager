using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLua;

namespace DCE_Manager
{
    // Un trigger lu dans camp_triggers_init.lua
    internal class WargameTrigger
    {
        public string Name;
        public bool Active = true;
        public bool Once;
        public string Condition = "";
        public List<string> Actions = new List<string>();

        // Etat pendant la simulation
        public bool Fired;
        public int FiredTurn;
        public bool Forced;         // l'utilisateur a demandé de le déclencher au prochain tour
        public string Status = "waiting";

        // Copie des définitions seules (pas de l'état) : sert à créer une simulation neuve sans relire le fichier
        public WargameTrigger CopyDefinition()
        {
            return new WargameTrigger { Name = Name, Active = Active, Once = Once, Condition = Condition, Actions = new List<string>(Actions) };
        }
    }

    // Horloge de campagne de la preview : date et heure de chaque mission (= chaque tour), pour les triggers "par date".
    // Approximation : mission suivante = début de la précédente + durée de mission + pause MOYENNE entre missions
    // (conf_mod.lua : mission_duration, idle_time_min, idle_time_max). Avec onlyDayMission, un départ hors du jour
    // (dawn / dusk) est repoussé à l'aube suivante.
    internal class WargameCampClock
    {
        public DateTime Start = new DateTime(2006, 7, 1, 8, 0, 0);   // début de la mission 1 (camp_init.lua : camp.date et camp.time)
        public double MissionDuration = 5400;                        // secondes
        public double IdleMin = 10800;
        public double IdleMax = 14400;
        public bool OnlyDay = false;
        public double Dawn = 21600;                                  // secondes depuis minuit
        public double Dusk = 65700;

        // Secondes entre le début d'une mission et le début de la suivante (pause moyenne)
        public double CycleSeconds
        {
            get { return MissionDuration + (IdleMin + IdleMax) / 2.0; }
        }

        // Date et heure de départ de la mission (tour) numéro "turn" (1 = première)
        public DateTime At(int turn)
        {
            DateTime t = Start;

            for (int i = 1; i < turn; i++)
            {
                t = t.AddSeconds(CycleSeconds);

                if (OnlyDay)
                {
                    double s = t.TimeOfDay.TotalSeconds;
                    if (s < Dawn) t = t.Date.AddSeconds(Dawn);
                    else if (s > Dusk) t = t.Date.AddDays(1).AddSeconds(Dawn);
                }
            }

            return t;
        }

        // Lit les réglages de temps : durées et pauses dans conf_mod.lua (mission_ini), date et heure de départ dans
        // camp_init.lua (camp.date, camp.time). Un réglage absent garde la valeur par défaut.
        // note = ce qui s'est passé, pour l'afficher.
        public static WargameCampClock FromConfMod(string path, string campInitPath, out string note)
        {
            var clock = new WargameCampClock();
            note = "";

            ReadStart(clock, campInitPath);

            if (!File.Exists(path))
            {
                note = "no conf_mod.lua found: default timing (mission 90 min, pause 3 h to 4 h)";
                return clock;
            }

            try
            {
                using (Lua lua = new Lua())
                {
                    lua.DoString("os = nil io = nil debug = nil dofile = nil loadfile = nil require = nil");
                    lua.DoFile(path);

                    LuaTable ini = lua["mission_ini"] as LuaTable;
                    if (ini == null)
                    {
                        note = "table mission_ini not found in conf_mod.lua: default timing";
                        return clock;
                    }

                    clock.MissionDuration = Num(ini["mission_duration"], clock.MissionDuration);
                    clock.IdleMin = Num(ini["idle_time_min"], clock.IdleMin);
                    clock.IdleMax = Math.Max(clock.IdleMin, Num(ini["idle_time_max"], clock.IdleMax));
                    clock.Dawn = Num(ini["dawn"], clock.Dawn);
                    clock.Dusk = Num(ini["dusk"], clock.Dusk);
                    clock.OnlyDay = ini["onlyDayMission"] is bool && (bool)ini["onlyDayMission"];

                    // Sans camp_init lisible, on retombe sur la date courante de conf_mod
                    if (!_startFromCampInit)
                        SetStartDate(clock, ini["current_date"] as LuaTable, 8 * 3600);
                }
            }
            catch (Exception ex)
            {
                note = "conf_mod.lua not readable (" + ex.Message + "): default timing";
            }

            return clock;
        }

        // Vrai quand la date de départ a été lue dans camp_init.lua
        [ThreadStatic]
        private static bool _startFromCampInit;

        // Date et heure de départ de la campagne : camp_init.lua, camp.date et camp.time (secondes depuis minuit)
        private static void ReadStart(WargameCampClock clock, string campInitPath)
        {
            _startFromCampInit = false;

            if (string.IsNullOrEmpty(campInitPath) || !File.Exists(campInitPath)) return;

            try
            {
                using (Lua lua = new Lua())
                {
                    lua.DoString("os = nil io = nil debug = nil dofile = nil loadfile = nil require = nil");
                    lua.DoFile(campInitPath);

                    LuaTable camp = lua["camp"] as LuaTable;
                    if (camp == null) return;

                    LuaTable date = camp["date"] as LuaTable;
                    if (date == null) return;

                    SetStartDate(clock, date, (int)Num(camp["time"], 8 * 3600));
                    _startFromCampInit = true;
                }
            }
            catch
            {
                // illisible : on garde les valeurs par défaut (ou celles de conf_mod)
            }
        }

        private static void SetStartDate(WargameCampClock clock, LuaTable date, int secondsOfDay)
        {
            if (date == null) return;

            int year = (int)Num(date["year"], 2006);
            int month = (int)Num(date["month"], 7);
            int day = (int)Num(date["day"], 1);

            if (year < 1753 || year > 9998) year = 2006;   // DateTimePicker : pas avant 1753
            if (month < 1 || month > 12) month = 7;
            day = Math.Max(1, Math.Min(DateTime.DaysInMonth(year, month), day));

            secondsOfDay = Math.Max(0, Math.Min(86399, secondsOfDay));

            clock.Start = new DateTime(year, month, day).AddSeconds(secondsOfDay);
        }

        private static double Num(object value, double fallback)
        {
            if (value is double) return (double)value;
            if (value is long) return (long)value;
            if (value is int) return (int)value;
            return fallback;   // absent, ou "false" (zero-false) : on garde la valeur par défaut
        }

        // Une ligne pour l'en-tête du journal
        public string Describe()
        {
            return "mission " + FormatSeconds(MissionDuration) + " + pause " + FormatSeconds(IdleMin) + " to " + FormatSeconds(IdleMax)
                + " (average cycle " + FormatSeconds(CycleSeconds) + ")" + (OnlyDay ? " | daylight only " + FormatClock(Dawn) + "-" + FormatClock(Dusk) : "");
        }

        public static string FormatSeconds(double seconds)
        {
            int total = (int)Math.Round(seconds);
            return (total / 3600) + "h" + ((total % 3600) / 60).ToString("00");
        }

        private static string FormatClock(double seconds)
        {
            return FormatSeconds(seconds);
        }
    }

    // Rejoue les triggers de camp_triggers_init.lua pendant la preview, pour tester la campagne avant la prod.
    //
    // On ne charge PAS DC_CheckTriggers.lua (il a besoin de tout scriptsMod). On donne aux conditions et aux
    // actions un petit monde Lua à nous (NLua) :
    //   - Return.Mission, Return.CampFlag, Return.Wargame... : vrais, calculés sur l'état de la preview
    //   - Return.Time/Day/Month/Year/DatePassed/DateBefore    : vrais, calculés sur l'horloge (WargameCampClock)
    //   - Action.SetCampFlag, AddCampFlag, WargameDeploy, WargameLand : vraies, elles lèvent les flags de la preview
    //   - Action.Text                                          : notée dans le journal
    //   - tout le reste (TargetAlive, GroundTarget...)         : "non simulable" -> le trigger reste en attente,
    //                                                            sauf si on le force à la main.
    // Rien n'est écrit sur le disque.
    internal class WargameTriggerSim : IDisposable
    {
        public readonly List<WargameTrigger> Triggers = new List<WargameTrigger>();

        // Date et heure des missions (voir WargameCampClock). Posée par la Preview ; par défaut : 1er juillet 2006, 08h00.
        public WargameCampClock Clock = new WargameCampClock();

        private Lua _lua;

        // Vrai si une condition parle des zones wargame : sinon on ne les envoie pas à Lua à chaque tour
        private bool _needsZones;

        // Flags de la preview que l'on a recopiés dans camp.flag (pour savoir lesquels retirer si le moteur les consomme)
        private readonly HashSet<string> _mirrored = new HashSet<string>(StringComparer.Ordinal);

        private static readonly Regex NoSimRx = new Regex(@"NOSIM:([A-Za-z0-9_\.]+)");
        private static readonly Regex GlobalRx = new Regex(@"global '([A-Za-z0-9_]+)'");

        // Lua du petit monde de simulation. Les Wargame* sont les mêmes que dans DC_CheckTriggers.lua.
        private const string Prelude = @"
os = nil io = nil debug = nil dofile = nil loadfile = nil require = nil
camp = { mission = 1, flag = {}, time = 28800, date = { year = 2006, month = 7, day = 1 } }
old_flag = {}
__wg = {}
__notes = {}

local function nosim(name)
    return function() error('NOSIM:' .. name, 0) end
end

Return = setmetatable({}, { __index = function(t, k) return nosim('Return.' .. k) end })
Action = setmetatable({}, { __index = function(t, k)
    return function() __notes[#__notes + 1] = 'Action.' .. k .. ' ignored (not simulated)' end
end })

function Return.Mission() return camp.mission end

-- Temps : mêmes fonctions que DC_CheckTriggers.lua, sur l'horloge de la preview (voir WargameCampClock)
local function dateToInt(d) return d.year * 10000 + d.month * 100 + d.day end
function Return.Time() return camp.time end
function Return.Day() return camp.date.day end
function Return.Month() return camp.date.month end
function Return.Year() return camp.date.year end
function Return.DatePassed(y, m, d) return dateToInt(camp.date) >= dateToInt({ year = y, month = m, day = d }) end
function Return.DateBefore(y, m, d) return dateToInt(camp.date) < dateToInt({ year = y, month = m, day = d }) end
function Return.CampFlag(n) return old_flag[n] end

function Return.WargameZoneSide(zone)
    local z = __wg[zone]
    if z then return z.control end
end
function Return.WargameZoneHeldBy(zone, side) return Return.WargameZoneSide(zone) == side end
function Return.WargameZoneForce(zone, side)
    local total = 0
    local z = __wg[zone]
    if z and z.formations then
        for _, f in pairs(z.formations) do
            if f.side == side then total = total + (f.forcePower or 0) end
        end
    end
    return total
end
function Return.WargameSideZones(side)
    local n = 0
    for _, z in pairs(__wg) do
        if z.control == side then n = n + 1 end
    end
    return n
end

function Action.SetCampFlag(n, value) camp.flag[n] = value end
function Action.AddCampFlag(n, add) camp.flag[n] = (camp.flag[n] or 0) + add end
function Action.Text(text) __notes[#__notes + 1] = 'Briefing: ' .. tostring(text) end
function Action.WargameDeploy(formation, zone)
    local function cle(s) return (string.gsub(tostring(s or ''), '[^%w]', '_')) end
    camp.flag['wg_deploy__' .. cle(formation) .. '__' .. cle(zone)] = true
end
function Action.WargameLand(plan, zone, gap)
    local function cle(s) return (string.gsub(tostring(s or ''), '[^%w]', '_')) end
    local name = 'wg_land__' .. cle(plan)
    if zone ~= nil and zone ~= '' then name = name .. '__' .. cle(zone)
    elseif gap ~= nil then name = name .. '__default' end
    if gap ~= nil then name = name .. '__' .. math.floor(tonumber(gap) or 0) end
    camp.flag[name] = true
end

-- Conditions et actions sont compilées UNE fois (loadstring), puis appelées avec pcall à chaque tour
__c = {}
__ce = {}
__a = {}

-- Lua 5.1 : loadstring ; Lua 5.2 et plus : load (selon la version de NLua)
local compile = loadstring or load

function __setcond(i, text)
    __c[i], __ce[i] = compile('return (' .. text .. ')')
    __a[i] = {}
end
function __addaction(i, text)
    local f, e = compile(text)
    if not f then f = function() error(e, 0) end end
    __a[i][#__a[i] + 1] = f
end
function __eval(i)
    if not __c[i] then return false, __ce[i] end
    return pcall(__c[i])
end
function __fire(i)
    local errs = {}
    for _, f in ipairs(__a[i]) do
        local ok, e = pcall(f)
        if not ok then errs[#errs + 1] = tostring(e) end
    end
    return table.concat(errs, '\n')
end
function __clear()
    camp.flag = {}
    old_flag = {}
    __wg = {}
    __notes = {}
end

function __setflag(name, value) camp.flag[name] = value end
function __snapshot()
    old_flag = {}
    for k, v in pairs(camp.flag) do old_flag[k] = v end
end
";

        private WargameTriggerSim() { }

        // Lit camp_triggers_init.lua. Renvoie null (et un message) si le fichier manque ou ne se lit pas.
        public static WargameTriggerSim Load(string path, out string error)
        {
            error = "";

            if (!File.Exists(path))
            {
                error = "no camp_triggers_init.lua in this campaign";
                return null;
            }

            var sim = new WargameTriggerSim();

            try
            {
                // 1) lecture du fichier dans un état à part (jetable)
                using (Lua reader = new Lua())
                {
                    reader.DoString("os = nil io = nil debug = nil dofile = nil loadfile = nil require = nil");
                    reader.DoFile(path);

                    LuaTable table = reader["camp_triggers"] as LuaTable;
                    if (table == null)
                    {
                        error = "table camp_triggers not found in " + Path.GetFileName(path);
                        return null;
                    }

                    foreach (object key in table.Keys)
                    {
                        LuaTable t = table[key] as LuaTable;
                        if (t == null) continue;

                        var trig = new WargameTrigger { Name = key.ToString() };

                        object active = t["active"];
                        trig.Active = !(active is bool) || (bool)active;

                        object once = t["once"];
                        trig.Once = once is bool && (bool)once;

                        object cond = t["condition"];
                        trig.Condition = cond == null ? "true" : (cond is bool ? ((bool)cond ? "true" : "false") : Convert.ToString(cond, System.Globalization.CultureInfo.InvariantCulture));

                        object act = t["action"];
                        LuaTable actTable = act as LuaTable;
                        if (actTable != null)
                        {
                            var keys = new List<object>();
                            foreach (object k in actTable.Keys) keys.Add(k);

                            // dans l'ordre des numéros [1], [2]...
                            foreach (object k in keys.OrderBy(x => x is double ? (double)x : double.MaxValue))
                            {
                                string a = actTable[k] as string;
                                if (!string.IsNullOrWhiteSpace(a)) trig.Actions.Add(a);
                            }
                        }
                        else if (act is string && !string.IsNullOrWhiteSpace((string)act))
                        {
                            trig.Actions.Add((string)act);
                        }

                        sim.Triggers.Add(trig);
                    }
                }

                sim.Triggers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                // 2) le petit monde où l'on évalue conditions et actions
                sim.InitLua();
            }
            catch (Exception ex)
            {
                error = "camp_triggers_init.lua not readable: " + ex.Message;
                sim.Dispose();
                return null;
            }

            return sim;
        }

        // Crée le petit monde Lua et y compile conditions et actions (une seule fois par simulation)
        private void InitLua()
        {
            _lua = new Lua();
            _lua.DoString(Prelude);

            LuaFunction setCond = _lua.GetFunction("__setcond");
            LuaFunction addAction = _lua.GetFunction("__addaction");

            for (int i = 0; i < Triggers.Count; i++)
            {
                WargameTrigger t = Triggers[i];
                setCond.Call(i + 1, t.Condition);
                foreach (string a in t.Actions)
                    addAction.Call(i + 1, a);

                if (t.Condition.IndexOf("Wargame", StringComparison.Ordinal) >= 0)
                    _needsZones = true;
            }
        }

        // Une simulation neuve avec les MÊMES triggers déjà lus : on ne relit pas le fichier
        // (sert au rapport multi-parties, pour ne pas toucher à la partie en cours)
        public WargameTriggerSim CloneFresh()
        {
            var copy = new WargameTriggerSim();
            copy.Clock = Clock;
            foreach (WargameTrigger t in Triggers)
                copy.Triggers.Add(t.CopyDefinition());

            copy.InitLua();
            return copy;
        }

        // Remet tout à zéro (aucun trigger déclenché, aucun flag) sans recompiler ni relire quoi que ce soit
        public void Reset()
        {
            foreach (WargameTrigger t in Triggers)
            {
                t.Fired = false;
                t.FiredTurn = 0;
                t.Forced = false;
                t.Status = "waiting";
            }

            _mirrored.Clear();
            if (_lua != null) _lua.DoString("__clear()");
        }

        public void Dispose()
        {
            if (_lua != null)
            {
                _lua.Dispose();
                _lua = null;
            }
        }

        // Déclenche ce trigger au prochain tour, sans regarder sa condition (pour les conditions non simulables)
        public void Force(WargameTrigger trigger)
        {
            if (trigger != null && !trigger.Fired) trigger.Forced = true;
        }

        // Joue la phase "triggers" d'un tour : ils tournent AVANT la mission, comme à la génération réelle.
        // Les flags levés sont ajoutés à "flags" (le moteur les traite ensuite : renforts, axes...).
        // Renvoie les lignes de journal (en français, comme le reste de la preview).
        public List<string> Run(int turn, List<WargameZoneData> zones, HashSet<string> flags)
        {
            var log = new List<string>();
            if (_lua == null) return log;

            try
            {
                PushState(turn, zones, flags);

                LuaFunction fire_ = _lua.GetFunction("__fire");

                for (int index = 0; index < Triggers.Count; index++)
                {
                    WargameTrigger trig = Triggers[index];

                    if (!trig.Active) { trig.Status = "inactive"; continue; }
                    if (trig.Once && trig.Fired) continue;

                    string before = trig.Status;
                    bool fire = false;

                    if (trig.Forced)
                    {
                        fire = true;
                    }
                    else
                    {
                        bool ok = Evaluate(index + 1, out string problem);
                        if (problem != null)
                        {
                            trig.Status = problem;
                            if (before != trig.Status) log.Add("Trigger '" + trig.Name + "' : " + problem);
                            continue;
                        }
                        fire = ok;
                    }

                    if (!fire)
                    {
                        trig.Status = "waiting";
                        continue;
                    }

                    trig.Fired = true;
                    trig.FiredTurn = turn;
                    trig.Forced = false;
                    trig.Status = "fired at turn " + turn;
                    log.Add("Trigger '" + trig.Name + "' déclenché (tour " + turn + ").");

                    object[] errors = fire_.Call(index + 1);
                    if (errors != null && errors.Length > 0 && errors[0] is string && ((string)errors[0]).Length > 0)
                        log.Add("  action en erreur : " + errors[0]);

                    // une action non répétée ne doit pas rester "déclenchée" si le trigger peut revenir
                    if (!trig.Once) trig.Fired = false;
                }

                PullFlags(flags);

                LuaTable notes = _lua["__notes"] as LuaTable;
                if (notes != null)
                {
                    var lines = new List<string>();
                    foreach (object v in notes.Values) lines.Add(Convert.ToString(v));
                    foreach (string n in lines) log.Add("  " + n);
                    _lua.DoString("__notes = {}");
                }
            }
            catch (Exception ex)
            {
                log.Add("Triggers : erreur de simulation : " + ex.Message);
            }

            return log;
        }

        // Evalue la condition (déjà compilée). problem = null si on a pu répondre, sinon la raison.
        // pcall côté Lua : pas d'exception .NET (lentes) pour les nombreux triggers non simulables.
        private bool Evaluate(int luaIndex, out string problem)
        {
            problem = null;

            object[] r = _lua.GetFunction("__eval").Call(luaIndex);

            bool ok = r != null && r.Length > 0 && r[0] is bool && (bool)r[0];
            if (ok)
            {
                if (r.Length < 2 || r[1] == null) return false;
                if (r[1] is bool) return (bool)r[1];
                return true;    // en Lua, tout sauf nil et false est vrai
            }

            string message = r != null && r.Length > 1 && r[1] != null ? Convert.ToString(r[1]) : "unknown error";

            Match m = NoSimRx.Match(message);
            Match g = GlobalRx.Match(message);     // ex. GroundTarget : variable de la vraie campagne, absente ici

            if (m.Success)
                problem = "cannot simulate (" + m.Groups[1].Value + ") - use Force";
            else if (g.Success)
                problem = "cannot simulate (" + g.Groups[1].Value + ") - use Force";
            else
                problem = "condition error: " + message;

            return false;
        }

        // Etat de la preview -> Lua : mission, flags, zones
        private void PushState(int turn, List<WargameZoneData> zones, HashSet<string> flags)
        {
            LuaFunction setFlag = _lua.GetFunction("__setflag");

            // flags que le moteur a consommés depuis le dernier tour : on les baisse aussi côté Lua
            foreach (string name in _mirrored.ToList())
            {
                if (flags.Contains(name)) continue;
                setFlag.Call(name, null);
                _mirrored.Remove(name);
            }

            foreach (string name in flags)
            {
                setFlag.Call(name, true);
                _mirrored.Add(name);
            }

            // Mission, heure et date de ce tour
            DateTime now = Clock.At(turn);
            string clockLua = "camp.mission = " + turn
                + "\ncamp.time = " + ((int)now.TimeOfDay.TotalSeconds)
                + "\ncamp.date = { year = " + now.Year + ", month = " + now.Month + ", day = " + now.Day + " }";

            if (!_needsZones)
            {
                _lua.DoString(clockLua + "\n__snapshot()");
                return;
            }

            var sb = new StringBuilder();
            sb.Append(clockLua).Append("\n__wg = {\n");

            foreach (WargameZoneData z in zones)
            {
                sb.Append("[\"").Append(Esc(z.Id)).Append("\"] = { control = \"").Append(Esc(z.Control)).Append("\", formations = {");
                foreach (WargameFormation f in z.Formations)
                {
                    sb.Append("{ side = \"").Append(Esc(f.Side)).Append("\", forcePower = ")
                      .Append(f.ForcePower.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" },");
                }
                sb.Append("} },\n");
            }

            sb.Append("}\n__snapshot()");
            _lua.DoString(sb.ToString());
        }

        // Lua -> preview : les flags (noms) vrais sont ajoutés, ceux qu'un trigger a baissés sont retirés
        private void PullFlags(HashSet<string> flags)
        {
            LuaTable camp = _lua["camp"] as LuaTable;
            LuaTable flagTable = camp != null ? camp["flag"] as LuaTable : null;
            if (flagTable == null) return;

            var trueNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (object key in flagTable.Keys)
            {
                string name = key as string;
                if (name == null) continue;

                object value = flagTable[key];
                bool truthy = value != null && !(value is bool && !(bool)value);
                if (truthy) trueNames.Add(name);
            }

            foreach (string name in trueNames)
            {
                flags.Add(name);
                _mirrored.Add(name);
            }

            foreach (string name in _mirrored.ToList())
            {
                if (trueNames.Contains(name)) continue;
                flags.Remove(name);
                _mirrored.Remove(name);
            }
        }

        private static string Esc(string text)
        {
            return (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // Date et heure de la mission "turn", pour les journaux
        public string TimeLabel(int turn)
        {
            return Clock.At(turn).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Pour l'en-tête du journal complet
        public List<string> Describe()
        {
            var lines = new List<string>();
            foreach (WargameTrigger t in Triggers)
            {
                lines.Add("  " + t.Name + (t.Active ? "" : " [inactive]") + (t.Once ? " [once]" : ""));
                lines.Add("      if " + t.Condition);
                foreach (string a in t.Actions) lines.Add("      do " + a);
            }
            return lines;
        }
    }
}
