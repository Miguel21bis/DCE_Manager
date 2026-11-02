using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLua;

namespace DCE_Manager
{
    // ==========================================================================================
    // Guerre aérienne fictive de la Preview.
    //
    //   WargameAirSquad  : un squad (état simulé : prêts, endommagés, perdus, réserve, transférés)
    //   WargameAirModel  : tous les squads + les règles de scriptsMod (DC_CheckTriggers.lua) :
    //                        - usure : pertes aériennes à chaque tour (front, maîtrise du ciel)
    //                        - Action.AirUnitReinforce / Action.AirUnitRepair (mêmes calculs que scriptsMod)
    //                        - renforcement automatique toutes les "Reinforce delay (h)" de conf_mod (RepairOption)
    //                      Départ = Init (oob_air_init.lua) : prêts = number, réserve = reserve, rien de perdu.
    //   WargameAirExport : écrit le résultat dans Active (oob_air.lua, db_airbases.lua, camp_status.lua, camp_triggers.lua)
    //
    // Rien n'est écrit tant que l'export n'est pas lancé. Texte Lua modifié sur place (WargameBaseCapture.LuaText).
    // ==========================================================================================

    internal class WargameAirSquad
    {
        public string Side = "";
        public string Name = "";
        public string Type = "";
        public string Base = "";
        public int Number;              // effectif nominal
        public bool HasReserve;         // le squad a une réserve (champ "reserve")
        public int InitReserve;

        public bool Player;
        public bool Inactive;

        // Etat simulé (roster)
        public int Ready, Damaged, Lost, Reserve, Trans;

        public WargameAirSquad Copy()
        {
            return (WargameAirSquad)MemberwiseClone();
        }

        // Retour à l'état de départ de la campagne
        public void ResetRoster()
        {
            Ready = Number;
            Damaged = 0;
            Lost = 0;
            Trans = 0;
            Reserve = HasReserve ? InitReserve : 0;
        }
    }

    internal class WargameAirModel
    {
        public readonly List<WargameAirSquad> Squads = new List<WargameAirSquad>();

        // Délai (heures) du renforcement automatique, par camp ; 0 = pas de renforcement (RepairOption absent de conf_mod)
        public double BlueInterval, RedInterval;

        // Heure (depuis le début de la campagne) du dernier renforcement automatique joué, par camp
        public double BlueLast, RedLast;

        // Remarques de chargement (pour le journal)
        public readonly List<string> Notes = new List<string>();

        // Totaux depuis le départ (pour le compte rendu)
        public int LostBlue, LostRed, DamagedBlue, DamagedRed, TransferredBlue, TransferredRed, RepairedBlue, RepairedRed;

        private readonly Random _rng = new Random();

        // Part des appareils touchés qui sont détruits (le reste revient endommagé, réparable)
        public const double DestroyedShare = 0.7;

        private WargameAirModel() { }

        // -------------------- Lecture --------------------

        // Structure : Active\oob_air.lua (ce que la campagne contient). Départ : Init\oob_air_init.lua (inactive, base),
        // reserve et number sont ceux du squad. Renvoie null (et la raison) si on ne peut pas.
        public static WargameAirModel Load(string campaignFolder, out string error)
        {
            error = "";
            string activePath = Path.Combine(campaignFolder, "Active", "oob_air.lua");
            if (!File.Exists(activePath))
            {
                error = "Active\\oob_air.lua not found (air war not simulated)";
                return null;
            }

            try
            {
                var model = new WargameAirModel();

                // Départ (Init) : inactive et base de chaque squad, par camp + nom
                var init = new Dictionary<string, WargameAirSquad>(StringComparer.OrdinalIgnoreCase);
                string initPath = Path.Combine(campaignFolder, "Init", "oob_air_init.lua");
                if (File.Exists(initPath))
                    foreach (WargameAirSquad s in ReadSquads(File.ReadAllText(initPath)))
                        init[s.Side + "|" + s.Name] = s;
                else
                    model.Notes.Add("Init\\oob_air_init.lua not found : squads start as they are in Active");

                foreach (WargameAirSquad s in ReadSquads(File.ReadAllText(activePath)))
                {
                    WargameAirSquad i;
                    if (init.TryGetValue(s.Side + "|" + s.Name, out i))
                    {
                        s.Inactive = i.Inactive;
                        if (!string.IsNullOrEmpty(i.Base)) s.Base = i.Base;
                    }
                    s.ResetRoster();
                    model.Squads.Add(s);
                }

                if (model.Squads.Count == 0)
                {
                    error = "no squad found in Active\\oob_air.lua";
                    return null;
                }

                model.ReadRepairOption(campaignFolder);
                return model;
            }
            catch (Exception ex)
            {
                error = "oob_air not readable : " + ex.Message;
                return null;
            }
        }

        // Squads d'un fichier oob_air (Init ou Active) : camp = table de niveau 1, squad = table de niveau 2
        private static List<WargameAirSquad> ReadSquads(string text)
        {
            var list = new List<WargameAirSquad>();
            var lua = new WargameBaseCapture.LuaText(text);

            foreach (WargameBaseCapture.LuaText.Block side in lua.Blocks.Where(b => b.DepthBefore == 1))
            {
                string sideKey = lua.KeyOf(side);
                bool blue = string.Equals(sideKey, "blue", StringComparison.OrdinalIgnoreCase);
                bool red = string.Equals(sideKey, "red", StringComparison.OrdinalIgnoreCase);
                if (!blue && !red) continue;

                foreach (WargameBaseCapture.LuaText.Block b in lua.Blocks.Where(x => x.DepthBefore == 2 && x.Open > side.Open && x.Close < side.Close))
                {
                    string name = lua.StringField(b, "name");
                    if (string.IsNullOrEmpty(name)) continue;

                    var s = new WargameAirSquad { Side = blue ? "blue" : "red", Name = name };
                    s.Type = lua.StringField(b, "type") ?? "";
                    s.Base = lua.StringField(b, "base") ?? "";

                    double n;
                    if (lua.NumberField(b, "number", out n)) s.Number = (int)n;
                    if (lua.NumberField(b, "reserve", out n)) { s.HasReserve = true; s.InitReserve = (int)n; }

                    bool flag;
                    if (lua.BoolField(b, "player", out flag)) s.Player = flag;
                    if (lua.BoolField(b, "inactive", out flag)) s.Inactive = flag;

                    list.Add(s);
                }
            }

            return list;
        }

        // conf_mod.lua : campMod.RepairOption[camp].airUnit[3] = délai de renforcement (heures).
        // Un conf_mod pas à jour n'a pas cette table : pas de renforcement automatique, mais rien ne plante.
        private void ReadRepairOption(string campaignFolder)
        {
            string path = Path.Combine(campaignFolder, "Init", "conf_mod.lua");
            if (!File.Exists(path)) path = Path.Combine(campaignFolder, "conf_mod.lua");
            if (!File.Exists(path))
            {
                Notes.Add("conf_mod.lua not found : no automatic reinforcement");
                return;
            }

            try
            {
                using (Lua lua = new Lua())
                {
                    lua.DoString("os = nil io = nil debug = nil dofile = nil loadfile = nil require = nil");
                    lua.DoFile(path);

                    LuaTable campMod = lua["campMod"] as LuaTable;
                    LuaTable repair = campMod != null ? campMod["RepairOption"] as LuaTable : null;
                    if (repair == null)
                    {
                        Notes.Add("conf_mod.lua has no campMod.RepairOption (outdated file) : no automatic reinforcement");
                        return;
                    }

                    BlueInterval = ReadInterval(repair, "blue");
                    RedInterval = ReadInterval(repair, "red");

                    if (BlueInterval <= 0 || RedInterval <= 0)
                        Notes.Add("RepairOption airUnit[3] missing or 0 for a side : no automatic reinforcement for that side");
                }
            }
            catch (Exception ex)
            {
                BlueInterval = RedInterval = 0;
                Notes.Add("conf_mod.lua RepairOption not readable (" + ex.Message + ") : no automatic reinforcement");
            }
        }

        private static double ReadInterval(LuaTable repair, string side)
        {
            LuaTable sideTable = repair[side] as LuaTable;
            LuaTable air = sideTable != null ? sideTable["airUnit"] as LuaTable : null;
            if (air == null) return 0;

            foreach (object key in air.Keys)
            {
                try
                {
                    if (Convert.ToDouble(key, CultureInfo.InvariantCulture) != 3) continue;
                    double v = Convert.ToDouble(air[key], CultureInfo.InvariantCulture);
                    return v >= 0.1 ? v : 0;     // sous 6 minutes : on ignore (évite une boucle énorme)
                }
                catch { return 0; }
            }
            return 0;
        }

        // -------------------- Règles (copie de DC_CheckTriggers.lua) --------------------

        private WargameAirSquad Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return Squads.FirstOrDefault(s => s.Name == name);
        }

        private bool SideMatches(WargameAirSquad s, string side)
        {
            return string.Equals(s.Side, side, StringComparison.OrdinalIgnoreCase);
        }

        // Return.AirUnitAlive : prêts + endommagés + réserve (nil si le squad n'existe pas)
        public object LuaAlive(string name)
        {
            WargameAirSquad s = Find(name);
            if (s == null) return null;
            return (double)(s.Ready + s.Damaged + (s.HasReserve ? s.Reserve : 0));
        }

        // Return.totalAirUnitAliveBySide : squads actifs du camp. 9999 si rien au mission 1 (comme scriptsMod).
        public object LuaTotal(string side, double mission)
        {
            double sum = 0;
            foreach (WargameAirSquad s in Squads)
                if (SideMatches(s, side) && !s.Inactive)
                    sum += s.Ready + s.Damaged + (s.HasReserve ? s.Reserve : 0);

            if (sum == 0 && mission <= 1) sum = 9999;
            return sum;
        }

        // Return.AirUnitActive
        public object LuaActive(string name)
        {
            WargameAirSquad s = Find(name);
            if (s == null) return null;
            return !s.Inactive;
        }

        // Return.AirUnitBase
        public object LuaBase(string name)
        {
            WargameAirSquad s = Find(name);
            return s == null ? null : s.Base;
        }

        // Action.AirUnitActive
        public void LuaSetActive(string name, bool state)
        {
            WargameAirSquad s = Find(name);
            if (s != null) s.Inactive = !state;
        }

        // Action.AirUnitBase (le premier nom de base suffit pour la Preview)
        public void LuaSetBase(string name, string baseName)
        {
            WargameAirSquad s = Find(name);
            if (s != null && !string.IsNullOrEmpty(baseName)) s.Base = baseName;
        }

        // Action.AirUnitReinforce(source, dest) : même calcul que scriptsMod
        //   (source, "")   : la réserve du squad source renforce ce squad
        //   (source, dest) : transfert d'un squad à l'autre ; si "source" n'existe pas, la réserve de "dest" est utilisée
        public void LuaReinforce(string sourceName, string destName)
        {
            WargameAirSquad src = Find(sourceName);
            WargameAirSquad dst = Find(destName);
            bool casA = false, casB = false;
            int pool;

            if (dst == null)
            {
                if (src == null) return;
                if (!src.HasReserve) return;
                dst = src;
                pool = src.Reserve;
                casA = true;
            }
            else if (src == null)
            {
                if (!dst.HasReserve) return;
                src = dst;
                pool = dst.Reserve;
                casB = true;
            }
            else
            {
                pool = src.Ready;
            }

            if (dst.Ready >= dst.Number || pool <= 0) return;

            int request = dst.Number - dst.Ready;
            int maxtrans = request;
            int minTrans = 0;

            if (maxtrans > 0)
            {
                double ratio = (double)maxtrans / pool;
                if (ratio < 0.5) { maxtrans = 8; minTrans = 2; }
                else if (ratio < 0.75) { maxtrans = 4; minTrans = 1; }
                else maxtrans = 2;
            }

            if (maxtrans > request) maxtrans = request;
            if (maxtrans > pool) maxtrans = pool;
            if (minTrans > maxtrans) minTrans = maxtrans - 1;
            if (minTrans < 0) minTrans = 0;

            int trans = _rng.Next(minTrans, maxtrans + 1);
            if (trans <= 0) return;

            dst.Ready += trans;
            pool -= trans;

            if (casA)
            {
                src.Reserve = pool;
                src.Trans += trans;
            }
            else if (casB)
            {
                dst.Reserve = pool;
                dst.Trans += trans;
            }
            else
            {
                src.Ready -= trans;
                src.Trans += trans;
            }

            if (dst.Side == "blue") TransferredBlue += trans; else TransferredRed += trans;
        }

        // Action.AirUnitRepair(camp) : chaque appareil endommagé a 10 % de chances d'être réparé ("" = les deux camps)
        public void LuaRepair(string side)
        {
            bool blue = side != "red";
            bool red = side != "blue";

            foreach (WargameAirSquad s in Squads)
            {
                if (s.Side == "blue" && !blue) continue;
                if (s.Side == "red" && !red) continue;

                int repair = 0;
                for (int n = 0; n < s.Damaged; n++)
                    if (_rng.Next(1, 101) <= 10) repair++;
                if (repair <= 0) continue;

                s.Damaged -= repair;

                if (s.HasReserve && s.Ready + repair >= s.Number)
                {
                    int back = s.Ready + repair - s.Number;
                    s.Reserve += back;
                    s.Trans -= back;
                    s.Ready = s.Number;
                }
                else
                {
                    s.Ready += repair;
                }

                if (s.Side == "blue") RepairedBlue += repair; else RepairedRed += repair;
            }
        }

        // Renforcement automatique (scriptsMod le joue à chaque génération, avec rattrapage des intervalles manqués).
        // hours = heures de campagne écoulées au moment de la mission. Renvoie des lignes de journal.
        public List<string> AutoReinforce(double hours)
        {
            var log = new List<string>();
            int before = TransferredBlue + TransferredRed;

            foreach (string side in new[] { "blue", "red" })
            {
                double interval = side == "blue" ? BlueInterval : RedInterval;
                if (interval <= 0) continue;

                double last = side == "blue" ? BlueLast : RedLast;
                int rounds = 0;

                while (hours >= last + interval && rounds < 1000)
                {
                    foreach (WargameAirSquad s in Squads.Where(x => x.Side == side).ToList())
                        if (s.HasReserve && s.Reserve > 0)
                            LuaReinforce(s.Name, "");

                    last += interval;
                    rounds++;
                }

                if (side == "blue") BlueLast = last; else RedLast = last;
                if (rounds > 0) log.Add("automatic reinforcement " + side + " : " + rounds + " round(s) (every " + interval.ToString("0.#", CultureInfo.InvariantCulture) + " h)");
            }

            int moved = TransferredBlue + TransferredRed - before;
            if (moved > 0) log.Add("  " + moved + " aircraft moved from reserve to ready squads");
            return log;
        }

        // Usure d'un tour : chaque appareil prêt d'un squad actif (hors joueur) a "p" chance d'être touché ;
        // 70 % des touchés sont détruits, les autres reviennent endommagés. Renvoie des lignes de journal.
        public List<string> Attrition(string side, double p)
        {
            var log = new List<string>();
            if (p <= 0) return log;

            int lost = 0, damaged = 0;
            foreach (WargameAirSquad s in Squads)
            {
                if (s.Side != side || s.Player || s.Inactive || s.Ready <= 0) continue;

                int hit = 0;
                for (int n = 0; n < s.Ready; n++)
                    if (_rng.NextDouble() < p) hit++;
                if (hit <= 0) continue;

                int dead = 0;
                for (int n = 0; n < hit; n++)
                    if (_rng.NextDouble() < DestroyedShare) dead++;

                s.Ready -= hit;
                s.Lost += dead;
                s.Damaged += hit - dead;
                lost += dead;
                damaged += hit - dead;
            }

            if (side == "blue") { LostBlue += lost; DamagedBlue += damaged; }
            else { LostRed += lost; DamagedRed += damaged; }

            if (lost + damaged > 0)
                log.Add("air war " + side + " : " + lost + " aircraft lost, " + damaged + " damaged (rate " + (p * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%)");
            return log;
        }

        // Une ligne de résumé par camp (journal, compte rendu d'export)
        public string Summary(string side)
        {
            int ready = 0, damaged = 0, reserve = 0, nominal = 0, squads = 0;
            foreach (WargameAirSquad s in Squads)
            {
                if (s.Side != side || s.Inactive) continue;
                ready += s.Ready; damaged += s.Damaged; reserve += s.HasReserve ? s.Reserve : 0; nominal += s.Number; squads++;
            }
            return side + " : " + squads + " active squad(s), ready " + ready + "/" + nominal + ", damaged " + damaged + ", reserve " + reserve;
        }
    }

    // ==========================================================================================
    // Ecriture dans Active
    // ==========================================================================================
    internal static class WargameAirExport
    {
        // Actions de trigger que la Preview rejoue vraiment. Un trigger "once" qui n'a que celles-ci peut être
        // considéré comme fait ; sinon il reste actif et tournera pour de vrai à la prochaine génération.
        private static readonly HashSet<string> SimulatedActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "SetCampFlag", "AddCampFlag", "Text", "WargameDeploy", "WargameLand",
            "AirUnitRepair", "AirUnitReinforce", "AirUnitActive", "AirUnitBase"
        };

        private static readonly Regex ActionNameRx = new Regex(@"^\s*Action\.([A-Za-z0-9_]+)");

        // ETAPE 1 (avant la désactivation des bases capturées) : squads et bases remis comme au départ de la campagne.
        //   oob_air.lua      : inactive et base de chaque squad = ceux du modèle (Init + actions de triggers)
        //   db_airbases.lua  : inactive de chaque base = celui de l'Init (sauf les FARP du wargame, gérées par WargameFarpStore)
        public static List<string> ResetToInit(string campaignFolder, WargameAirModel air)
        {
            var report = new List<string>();

            try
            {
                // ---- oob_air.lua ----
                string oobPath = Path.Combine(campaignFolder, "Active", "oob_air.lua");
                if (File.Exists(oobPath))
                {
                    var lua = new WargameBaseCapture.LuaText(File.ReadAllText(oobPath));
                    var edits = new List<WargameBaseCapture.LuaText.Edit>();

                    foreach (var side in lua.Blocks.Where(b => b.DepthBefore == 1))
                    {
                        string sideKey = lua.KeyOf(side);
                        if (!string.Equals(sideKey, "blue", StringComparison.OrdinalIgnoreCase) && !string.Equals(sideKey, "red", StringComparison.OrdinalIgnoreCase)) continue;

                        foreach (var b in lua.Blocks.Where(x => x.DepthBefore == 2 && x.Open > side.Open && x.Close < side.Close))
                        {
                            string name = lua.StringField(b, "name");
                            WargameAirSquad s = air.Squads.FirstOrDefault(q => q.Name == name && string.Equals(q.Side, sideKey, StringComparison.OrdinalIgnoreCase));
                            if (s == null) continue;

                            lua.SetBool(b, "inactive", s.Inactive, edits, s.Inactive);
                            if (!string.IsNullOrEmpty(s.Base)) lua.SetString(b, "base", s.Base, edits);
                        }
                    }

                    if (edits.Count > 0) File.WriteAllText(oobPath, lua.Apply(edits), new UTF8Encoding(false));
                    report.Add("air squads reset to the start of the campaign (active/inactive, base) : " + edits.Count + " change(s)");
                }

                // ---- db_airbases.lua ----
                string dbPath = Path.Combine(campaignFolder, "Active", "db_airbases.lua");
                string dbInit = Path.Combine(campaignFolder, "Init", "db_airbases.lua");
                if (File.Exists(dbPath) && File.Exists(dbInit))
                {
                    var initBases = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    var initLua = new WargameBaseCapture.LuaText(File.ReadAllText(dbInit));
                    foreach (var b in initLua.Blocks.Where(x => x.DepthBefore == 1))
                    {
                        string key = initLua.KeyOf(b);
                        if (key == null) continue;
                        bool flag;
                        initBases[key] = initLua.BoolField(b, "inactive", out flag) && flag;
                    }

                    var lua = new WargameBaseCapture.LuaText(File.ReadAllText(dbPath));
                    var edits = new List<WargameBaseCapture.LuaText.Edit>();
                    foreach (var b in lua.Blocks.Where(x => x.DepthBefore == 1))
                    {
                        string key = lua.KeyOf(b);
                        bool want;
                        if (key == null || !initBases.TryGetValue(key, out want)) continue;     // base absente de l'Init : on n'y touche pas

                        // FARP du wargame : son état actif/inactif suit la plage (WargameFarpStore.SyncActive, dans les deux sens) : on n'y touche pas
                        bool isFarp;
                        if (lua.BoolField(b, "wargameFarp", out isFarp) && isFarp) continue;
                        lua.SetBool(b, "inactive", want, edits, want);
                    }

                    if (edits.Count > 0) File.WriteAllText(dbPath, lua.Apply(edits), new UTF8Encoding(false));
                    report.Add("air bases reset to the start of the campaign (inactive) : " + edits.Count + " change(s)");
                }
            }
            catch (Exception ex)
            {
                report.Add("air squads / bases NOT reset (" + ex.Message + ")");
                WargameLog.Line("WargameAirExport.ResetToInit : " + ex);
            }

            return report;
        }

        // ETAPE 2 (après la désactivation des bases capturées) : rosters des squads + minuterie du renforcement automatique
        public static List<string> WriteState(string campaignFolder, WargameAirModel air)
        {
            var report = new List<string>();

            try
            {
                string oobPath = Path.Combine(campaignFolder, "Active", "oob_air.lua");
                if (!File.Exists(oobPath))
                {
                    report.Add("air war NOT written : Active\\oob_air.lua not found");
                    return report;
                }

                var lua = new WargameBaseCapture.LuaText(File.ReadAllText(oobPath));
                var edits = new List<WargameBaseCapture.LuaText.Edit>();
                int squads = 0;

                foreach (var side in lua.Blocks.Where(b => b.DepthBefore == 1))
                {
                    string sideKey = lua.KeyOf(side);
                    if (!string.Equals(sideKey, "blue", StringComparison.OrdinalIgnoreCase) && !string.Equals(sideKey, "red", StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var b in lua.Blocks.Where(x => x.DepthBefore == 2 && x.Open > side.Open && x.Close < side.Close))
                    {
                        string name = lua.StringField(b, "name");
                        WargameAirSquad s = air.Squads.FirstOrDefault(q => q.Name == name && string.Equals(q.Side, sideKey, StringComparison.OrdinalIgnoreCase));
                        if (s == null) continue;

                        var roster = lua.Child(b, "roster");
                        if (roster == null) continue;

                        var fields = new List<string>
                        {
                            "[\"ready\"] = " + s.Ready,
                            "[\"lost\"] = " + s.Lost,
                            "[\"damaged\"] = " + s.Damaged
                        };
                        if (s.HasReserve) fields.Add("[\"reserve\"] = " + s.Reserve);
                        // trans toujours écrit (même à 0) : AirUnitRepair de scriptsMod fait trans - x et plante si le champ est absent
                        if (s.HasReserve || s.Trans != 0) fields.Add("[\"trans\"] = " + s.Trans);

                        lua.ReplaceFields(roster, fields, edits);
                        squads++;
                    }
                }

                if (edits.Count > 0) File.WriteAllText(oobPath, lua.Apply(edits), new UTF8Encoding(false));

                report.Add("air war written : " + squads + " squad roster(s) (lost, damaged, reserve, transferred)");
                report.Add("  " + air.Summary("blue"));
                report.Add("  " + air.Summary("red"));
                report.Add("  totals since the start : blue " + air.LostBlue + " lost / " + air.DamagedBlue + " damaged / " + air.RepairedBlue + " repaired / " + air.TransferredBlue + " reinforced"
                    + " | red " + air.LostRed + " lost / " + air.DamagedRed + " damaged / " + air.RepairedRed + " repaired / " + air.TransferredRed + " reinforced");

                foreach (string note in air.Notes) report.Add("  note : " + note);
                report.AddRange(WriteReinforceClock(campaignFolder, air));
            }
            catch (Exception ex)
            {
                report.Add("air war NOT written (" + ex.Message + ")");
                WargameLog.Line("WargameAirExport.WriteState : " + ex);
            }

            return report;
        }

        // camp_status.lua : camp.automaticReinforce[camp] = heure du dernier renforcement automatique,
        // pour que scriptsMod ne rattrape pas tous les intervalles depuis le début de la campagne.
        private static List<string> WriteReinforceClock(string campaignFolder, WargameAirModel air)
        {
            var report = new List<string>();
            if (air.BlueInterval <= 0 && air.RedInterval <= 0) return report;

            string path = Path.Combine(campaignFolder, "Active", "camp_status.lua");
            if (!File.Exists(path))
            {
                report.Add("  automatic reinforcement clock NOT written : camp_status.lua not found");
                return report;
            }

            var lua = new WargameBaseCapture.LuaText(File.ReadAllText(path));
            var root = lua.Blocks.FirstOrDefault(b => b.DepthBefore == 0);
            if (root == null) return report;

            var edits = new List<WargameBaseCapture.LuaText.Edit>();
            var table = lua.Child(root, "automaticReinforce");
            double scalar;

            if (table != null)
            {
                if (air.BlueInterval > 0) lua.SetNumberOrAdd(table, "blue", air.BlueLast, edits);
                if (air.RedInterval > 0) lua.SetNumberOrAdd(table, "red", air.RedLast, edits);
            }
            else if (lua.NumberField(root, "automaticReinforce", out scalar))
            {
                // ancien format (un seul nombre) : scriptsMod le convertit en table pour les trois camps
                lua.SetNumberOrAdd(root, "automaticReinforce", Math.Min(air.BlueInterval > 0 ? air.BlueLast : double.MaxValue, air.RedInterval > 0 ? air.RedLast : double.MaxValue), edits);
            }
            else
            {
                string tabs = new string('\t', root.DepthBefore + 1);
                edits.Add(new WargameBaseCapture.LuaText.Edit
                {
                    Pos = root.Open + 1,
                    Length = 0,
                    Text = "\r\n" + tabs + "[\"automaticReinforce\"] = { [\"blue\"] = " + air.BlueLast.ToString("0.##", CultureInfo.InvariantCulture)
                        + ", [\"red\"] = " + air.RedLast.ToString("0.##", CultureInfo.InvariantCulture) + ", [\"neutral\"] = 0 },"
                });
            }

            File.WriteAllText(path, lua.Apply(edits), new UTF8Encoding(false));
            report.Add("  automatic reinforcement clock written in camp_status (blue " + air.BlueLast.ToString("0.#", CultureInfo.InvariantCulture)
                + " h, red " + air.RedLast.ToString("0.#", CultureInfo.InvariantCulture) + " h)");
            return report;
        }

        // Triggers "once" déjà déclenchés dans la Preview ET dont toutes les actions sont rejouées par la Preview :
        // ['active'] = false dans Active\camp_triggers.lua (comme scriptsMod après leur déclenchement).
        // Les autres restent actifs : leurs actions n'ont pas été jouées pour de bon.
        public static List<string> MarkFiredTriggers(string campaignFolder, WargameTriggerSim sim)
        {
            var report = new List<string>();
            if (sim == null) return report;

            try
            {
                string path = Path.Combine(campaignFolder, "Active", "camp_triggers.lua");
                if (!File.Exists(path)) return report;

                var fired = new Dictionary<int, WargameTrigger>();
                int notSimulated = 0;
                foreach (WargameTrigger t in sim.Triggers)
                {
                    if (!t.Once || !t.Fired) continue;

                    int index;
                    if (!int.TryParse(t.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) continue;

                    if (AllActionsSimulated(t, sim.Air != null)) fired[index] = t;
                    else notSimulated++;
                }
                if (fired.Count == 0 && notSimulated == 0) return report;

                var lua = new WargameBaseCapture.LuaText(File.ReadAllText(path));
                var edits = new List<WargameBaseCapture.LuaText.Edit>();
                int counter = 0, done = 0;

                foreach (var b in lua.Blocks.Where(x => x.DepthBefore == 1))
                {
                    int key = lua.NumberKeyOf(b);
                    int index = key >= 0 ? key : ++counter;
                    if (key >= 0) counter = key;

                    if (!fired.ContainsKey(index)) continue;
                    lua.SetBool(b, "active", false, edits, true);
                    done++;
                }

                if (edits.Count > 0) File.WriteAllText(path, lua.Apply(edits), new UTF8Encoding(false));
                report.Add("triggers : " + done + " 'once' trigger(s) fired in the preview marked as done (active = false)"
                    + (notSimulated > 0 ? ", " + notSimulated + " fired one(s) left active because some of their actions are not simulated (they will run for real)" : ""));
            }
            catch (Exception ex)
            {
                report.Add("triggers NOT marked (" + ex.Message + ")");
                WargameLog.Line("WargameAirExport.MarkFiredTriggers : " + ex);
            }

            return report;
        }

        private static bool AllActionsSimulated(WargameTrigger t, bool airAttached)
        {
            foreach (string action in t.Actions)
            {
                Match m = ActionNameRx.Match(action);
                if (!m.Success) return false;

                string name = m.Groups[1].Value;
                if (!SimulatedActions.Contains(name)) return false;
                if (name.StartsWith("AirUnit", StringComparison.Ordinal) && !airAttached) return false;
            }
            return true;
        }
    }
}
