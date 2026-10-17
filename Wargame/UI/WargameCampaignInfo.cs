using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DCE_Manager.Utils;
using NLua;
using System.Globalization;

namespace DCE_Manager
{
    // Contexte "campagne" du wargame, regroupé ici pour éviter d'éparpiller :
    //  - les libellés affichés des deux camps, lus dans Init/camp_init.lua
    //    (country_blue / country_red) : le campaignMaker voit "US" / "Iran",
    //    jamais "blue" / "red"
    //  - la liste des templates .stm disponibles par camp, lus dans
    //    Templates/wargame_blue et Templates/wargame_red
    internal class WargameCampaignInfo
    {
        public string BlueLabel = "Blue";
        public string RedLabel = "Red";

        public List<string> BlueTemplates = new List<string>();
        public List<string> RedTemplates = new List<string>();

        // Dossiers d'où viennent les templates, gardés pour pouvoir retrouver le
        // chemin d'un .stm et le parser (comptage des unités).
        public string BlueFolder = "";
        public string RedFolder = "";
        // true si camp.wargame_config a été trouvée dans camp_init.lua - sert à
        // distinguer "wargame jamais configuré pour cette campagne" d'un simple
        // souci de libellés par défaut.
        public bool HasWargameConfig = false;

        // Marge de sécurité (mètres) entre une formation posée et le bord de sa
        // zone, appliquée seulement côté ennemi (voir WargameSpawnSolver). Lue
        // dans camp.wargame_config.zone_edge_margin_meters ; à défaut, la valeur
        // par défaut du solveur est utilisée. Par campagne plutôt qu'en dur dans
        // le code : chaque théâtre/échelle de carte peut avoir besoin d'une
        // valeur différente.
        public double ZoneEdgeMarginMeters = WargameSpawnSolver.DefaultZoneEdgeMarginMeters;

        // Réglages d'équilibrage du combat : valeurs par défaut du code, surchargées par
        // camp.wargame_config dans Init/camp_init.lua (voir ReadCombatConfig).
        public WargameCombatConfig CombatConfig = new WargameCombatConfig();

        public static WargameCampaignInfo Load(string campaignName)
        {
            var info = new WargameCampaignInfo();

            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);

            info.LoadSideLabels(Path.Combine(campaignFolder, "Init", "camp_init.lua"));

            info.BlueFolder = Path.Combine(campaignFolder, "Init", "Wargame", "Templates_blue");
            info.RedFolder = Path.Combine(campaignFolder, "Init", "Wargame", "Templates_red");

            info.BlueTemplates = ListTemplates(info.BlueFolder);
            info.RedTemplates = ListTemplates(info.RedFolder);

            return info;
        }

        // Libellé affiché pour une valeur de Control (WargameSide.*)
        public string GetLabel(string control)
        {
            if (control == WargameSide.Blue) return BlueLabel;
            if (control == WargameSide.Red) return RedLabel;
            return "Not set";
        }

        // Templates proposables pour une zone : ceux de son camp, ou les deux
        // si la zone est contestée (les deux camps peuvent y avoir des unités).
        public List<string> GetTemplatesFor(string control)
        {
            if (control == WargameSide.Blue) return BlueTemplates;
            if (control == WargameSide.Red) return RedTemplates;

            return BlueTemplates.Concat(RedTemplates).ToList();
        }

        // Retrouve le camp d'un template, utile quand on ajoute une unité dans une
        // zone contestée (où les deux listes sont proposées ensemble).
        public string GetSideOfTemplate(string template)
        {
            if (BlueTemplates.Contains(template, StringComparer.OrdinalIgnoreCase))
                return WargameSide.Blue;

            if (RedTemplates.Contains(template, StringComparer.OrdinalIgnoreCase))
                return WargameSide.Red;

            return "";
        }

        // Chemin complet du .stm d'un template, ou "" s'il est introuvable.
        public string GetTemplateFilePath(string template)
        {
            if (string.IsNullOrEmpty(template))
                return "";

            string side = GetSideOfTemplate(template);
            string folder = side == WargameSide.Blue ? BlueFolder : side == WargameSide.Red ? RedFolder : "";

            if (string.IsNullOrEmpty(folder))
                return "";

            return Path.Combine(folder, template + ".stm");
        }

        private void LoadSideLabels(string campInitPath)
        {
            if (!File.Exists(campInitPath))
            {
                FormUtils.LogRegister("WargameCampaignInfo | camp_init.lua introuvable : " + campInitPath);
                return;
            }

            try
            {
                using (Lua lua = new Lua())
                {
                    lua.DoString(@"
                        os = nil
                        io = nil
                        file = nil
                        debug = nil
                    ");

                    lua.DoFile(campInitPath);

                    LuaTable camp = lua["camp"] as LuaTable;
                    if (camp == null)
                    {
                        FormUtils.LogRegister("WargameCampaignInfo | table 'camp' introuvable dans " + campInitPath);
                        return;
                    }

                    LuaTable wargameConfig = camp["wargame_config"] as LuaTable;
                    if (wargameConfig == null)
                    {
                        FormUtils.LogRegister("WargameCampaignInfo | table 'camp.wargame_config' absente de " + campInitPath);
                        return;
                    }

                    HasWargameConfig = true;

                    string blue = wargameConfig["country_blue"]?.ToString();
                    string red = wargameConfig["country_red"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(blue)) BlueLabel = blue;
                    if (!string.IsNullOrWhiteSpace(red)) RedLabel = red;

                    object marginRaw = wargameConfig["zone_edge_margin_meters"];
                    if (marginRaw != null
                        && double.TryParse(Convert.ToString(marginRaw, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out double margin)
                        && margin >= 0)
                    {
                        ZoneEdgeMarginMeters = margin;
                    }

                    ReadCombatConfig(wargameConfig);
                }
            }
            catch (Exception ex)
            {
                // camp_init pas encore recalé sur la référence (champs absents), ou
                // fichier illisible : on garde les libellés par défaut plutôt que de planter.
                FormUtils.LogRegister("WargameCampaignInfo | erreur lecture camp_init.lua : " + ex.Message);
            }
        }

        // Chaque clé est facultative : absente, la valeur par défaut du code est gardée.
        private void ReadCombatConfig(LuaTable cfg)
        {
            ReadNumber(cfg, "control_flip_threshold", ref CombatConfig.ControlFlipThreshold);
            ReadNumber(cfg, "retreat_loss_fraction", ref CombatConfig.RetreatLossFraction);
            ReadNumber(cfg, "cutoff_supply_factor", ref CombatConfig.CutOffSupplyFactor);
            ReadNumber(cfg, "random_variance", ref CombatConfig.RandomVariance);
            ReadNumber(cfg, "supply_recovery_rate", ref CombatConfig.SupplyRecoveryRate);
            ReadNumber(cfg, "front_line_recovery_factor", ref CombatConfig.FrontLineRecoveryFactor);
            ReadNumber(cfg, "engaged_recovery", ref CombatConfig.EngagedRecoveryFactor);
            ReadNumber(cfg, "combat_attrition", ref CombatConfig.CombatAttrition);
            ReadNumber(cfg, "influence_decay", ref CombatConfig.InfluenceDecay);
            ReadBool(cfg, "advance_on_conquest", ref CombatConfig.AdvanceOnConquest);
            ReadBool(cfg, "front_alignment", ref CombatConfig.FrontAlignmentEnabled);
            ReadBool(cfg, "empty_zone_influence", ref CombatConfig.EmptyZoneInfluence);
            ReadBool(cfg, "axis_reinforcement", ref CombatConfig.AxisReinforcement);

            double dcsDepth = CombatConfig.DcsZoneDepth;
            ReadNumber(cfg, "dcs_zone_depth", ref dcsDepth);
            CombatConfig.DcsZoneDepth = (int)Math.Round(dcsDepth);

            double maxPerZone = CombatConfig.MaxFormationsPerZone;
            ReadNumber(cfg, "max_formations_per_zone", ref maxPerZone);
            CombatConfig.MaxFormationsPerZone = Math.Max(1, (int)Math.Round(maxPerZone));

            double hardCap = CombatConfig.MaxFormationsHard;
            ReadNumber(cfg, "max_formations_hard", ref hardCap);
            CombatConfig.MaxFormationsHard = Math.Max(1, (int)Math.Round(hardCap));

            ReadNumber(cfg, "disband_below_fraction", ref CombatConfig.DisbandBelowFraction);

            // Grilles (terrain et routes) : une colonne = une valeur par ligne
            WargameConfigGrid.ReadColumn(cfg, "terrain", 1, CombatConfig.TerrainDefense, 0.5, 3.0);
            WargameConfigGrid.ReadColumn(cfg, "supply_route", 1, CombatConfig.RouteRecovery, 0.0, 3.0);
            WargameConfigGrid.ReadColumn(cfg, "supply_route", 2, CombatConfig.RouteCombat, 0.1, 3.0);

            string air = cfg["air_superiority"]?.ToString();
            if (!string.IsNullOrWhiteSpace(air))
            {
                air = air.Trim().ToLowerInvariant();
                CombatConfig.AirSuperioritySide = (air == "blue" || air == "red") ? air : "";
            }
            ReadNumber(cfg, "air_superiority_bonus", ref CombatConfig.AirSuperiorityBonus);
        }

        private static void ReadNumber(LuaTable table, string key, ref double target)
        {
            object raw = table[key];
            double value;

            // Convert.ToString(..., InvariantCulture) : un double Lua 0.25 reste "0.25".
            // Un simple raw.ToString() donne "0,25" sur un Windows en français, que le
            // TryParse relit ensuite comme 25 (la virgule passe pour un séparateur de milliers).
            if (raw != null
                && double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                && value >= 0)
            {
                target = value;
            }
        }

        private static void ReadBool(LuaTable table, string key, ref bool target)
        {
            if (table[key] is bool)
                target = (bool)table[key];
        }

        private static List<string> ListTemplates(string folder)
        {
            var list = new List<string>();

            if (!Directory.Exists(folder))
            {
                FormUtils.LogRegister("WargameCampaignInfo | dossier de templates absent : " + folder);
                return list;
            }

            foreach (string file in Directory.GetFiles(folder, "*.stm"))
                list.Add(Path.GetFileNameWithoutExtension(file));

            list.Sort(StringComparer.OrdinalIgnoreCase);

            return list;
        }
    }
}
