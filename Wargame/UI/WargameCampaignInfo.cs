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

        // Réglage fin facultatif : Init/Wargame/wargame_balance.lua. Absent = valeurs du code.
        // Ordre de lecture : valeurs du code -> camp_init (anciennes campagnes) -> wargame_balance.lua (le dernier gagne).
        public bool BalanceFileFound = false;

        public static WargameCampaignInfo Load(string campaignName)
        {
            var info = new WargameCampaignInfo();

            string campaignFolder = WargameZoneRepository.GetCampaignFolder(campaignName);

            info.LoadSideLabels(Path.Combine(campaignFolder, "Init", "camp_init.lua"));

            string balancePath = Path.Combine(campaignFolder, "Init", "Wargame", "wargame_balance.lua");
            if (info.HasWargameConfig)
                EnsureBalanceFile(balancePath);

            info.LoadBalanceFile(balancePath);

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

        // Crée wargame_balance.lua (avec toutes les valeurs par défaut) s'il n'existe pas. Jamais écrasé s'il existe.
        // Échec d'écriture (dossier en lecture seule...) : on continue avec les valeurs du code.
        private static void EnsureBalanceFile(string balancePath)
        {
            try
            {
                if (File.Exists(balancePath))
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(balancePath));
                File.WriteAllText(balancePath, BalanceFileTemplate.Replace("\r\n", "\n").Replace("\n", "\r\n"), new System.Text.UTF8Encoding(false));
                FormUtils.LogRegister("WargameCampaignInfo | wargame_balance.lua créé avec les valeurs par défaut : " + balancePath);
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("WargameCampaignInfo | wargame_balance.lua non créé : " + ex.Message);
            }
        }

        // Contenu du fichier créé automatiquement (copie de wargame_balance.lua : à garder synchronisé quand une option est ajoutée)
        private const string BalanceFileTemplate = @"-- wargame_balance.lua : fine tuning of the wargame.
-- Location: <campaign>\Init\Wargame\wargame_balance.lua
-- DCE_Manager creates this file by itself, with the default values, when it is missing.
-- Every key is optional: a key you delete keeps its built-in value. Values here win over the same key in camp_init.lua.
-- It is read by DCE_Manager only (the wargame engine and the Preview), not by the DCS scripts.

wargame_balance = {
	retreat_loss_fraction = 0.05, -- @ui numeric min=0 max=0.5 group=""Wargame - combat"" label=""Loss when retreating"" help=Share of its strength a formation loses each time it falls back after losing a zone (it retreats in disorder). 0 = no loss. 0.05 = loses 5%. A formation pushed back several times can end up disbanded (see ""Disband weak formations"" in the army group). [default: 0.05]
	landing_wave_gap = 1, -- @ui numeric min=0 max=10 group=""Wargame - combat"" label=""Landing: turns between waves"" help=A landing plan (LANDING box on the map) lands its waves one after the other, in the order of the Wave numbers, after ONE trigger (Action.WargameLand). This is the number of turns between two waves. 0 = all waves go on the same turn. The trigger can override it for one plan. [default: 1]
	landing_fail_loss = 0.15, -- @ui numeric min=0 max=0.9 group=""Wargame - combat"" label=""Landing: loss when repulsed"" help=Share of its strength a landing wave loses each time it attacks an enemy beach and fails. It tries again next turn while it still has a chance (its force is not less than about 60% of the defense); after that it is abandoned and the next wave goes. The wave that wins arrives with a strength proportional to the force ratio. [default: 0.15]
	landing_max_retries = 2, -- @ui numeric min=0 max=20 group=""Wargame - combat"" label=""Retries of a repulsed wave"" help=A landing wave that is pushed back tries again next mission, up to this many times. After that it gives up and stays at sea, and the next wave takes over. [default: 2]
	landing_reembark_loss = 0.5, -- @ui numeric min=0 max=1 group=""Wargame - combat"" label=""Landing: loss when re-embarking"" help=When a beach is lost and its troops have nowhere to fall back, they go back on board the fleet instead of being destroyed, if the fleet is there (naval fire support flag raised, or no flag). They lose this share of their strength (0.5 = half) and land again when the beach is back in their hands. [default: 0.5]
	combat_attrition = 0.02, -- @ui numeric min=0 max=0.2 group=""Wargame - combat"" label=""Wear in combat"" help=Share of its strength that each formation loses, every mission, when it takes part in a fight on an active axis (both sides). 0 = fights cost nothing: only air strikes wear formations down. 0.02 = 2% per mission. Keep it small so the war stays driven by air power. [default: 0.02]
	front_alignment = true, -- @ui checkbox group=""Wargame - combat"" label=""Straighten the front"" help=If enabled, the DEFENDING side pulls back its zones that stick out into the attacker (surrounded by more enemy neighbours than friendly ones) after each mission, so its front line stays tidy. The attacking side (the one holding the start zone of an active axis) never pulls back this way. If disabled, nobody pulls back. [default: true]
	engaged_recovery = 0.25, -- @ui numeric min=0 max=1 group=""Wargame - army"" label=""Recovery while fighting"" help=How much of its normal recovery a formation keeps in a mission where it fought (attacking or defending). 1 = recovers as usual (an offensive never wears out). 0.25 = recovers a quarter as much. 0 = no recovery while fighting. [default: 0.25]
	axis_reinforcement = true, -- @ui checkbox group=""Wargame - army"" label=""Reinforcements follow the front"" help=If enabled, formations behind the front move up toward it, one zone per mission, and gather in the front line zones (flanks included), so most of the army ends up in contact with the enemy. A front zone takes more formations when the enemy in front of it is strong. If disabled, formations only move when they advance after a conquest or retreat. [default: true]
	axis_pull_surplus = true, -- @ui checkbox group=""Wargame - army"" label=""Army follows the axes"" help=If enabled, formations that are not needed to guard their own zone walk toward the tip of an active attack axis (one zone per mission), so the army pushes along the axis instead of waiting behind. The strongest formation of a zone that attacks, and enough formations to hold its flanks, always stay. If disabled, formations only move up to the nearest front line zone. [default: true]
	objective_garrison = true, -- @ui checkbox group=""Wargame - army"" label=""Garrison on objective zones"" help=If enabled, the last zone of every active axis always keeps ONE formation (the weakest one there): it stays to hold the ground and does not walk to the front. If the zone is empty, a weak formation from the rear comes to occupy it. Strong formations stay free to fight. If disabled, nothing is kept there. [default: true]
	axis_use_terrain = true, -- @ui checkbox group=""Wargame - combat"" label=""Axis paths follow terrain and roads"" help=If enabled, the path of an attack axis between its start, its waypoints and its objective avoids zones that are hard to cross (mountain, no road) and prefers good roads and flat ground, using the same terrain and road values as supply. Zones without terrain or road set count as normal. If disabled, the path is simply the shortest one in distance. [default: true]
	axis_flex_detour = 0, -- @ui numeric min=0 max=10 group=""Wargame - combat"" label=""Flexible axis path (extra zones)"" help=Lets an attack axis go around a strongly defended zone. Every mission the engine looks for a cheaper way from the tip of the axis to its objective (a strong enemy zone costs more, a good road costs less). The new way is used only if it is clearly cheaper and has at most this many extra zones. Zones you set as 'via' stay compulsory. 0 = the axis always follows the path drawn in the editor. [default: 0]
	empty_zone_influence = true, -- @ui checkbox group=""Wargame - army"" label=""Empty zones follow influence"" help=If enabled, a zone with no force in it goes to the side that has clearly more force around it (neighbour zones count fully, zones two steps away count half). Undefended flanks and rear zones fall without a fight, one ring per mission. If disabled, an empty zone keeps its owner until an axis attack reaches it. [default: true]
	supply_network = true, -- @ui checkbox group=""Wargame - supply"" label=""Supply network"" help=If enabled, supplies travel from the supply source zones (set in the wargame editor: port, depot, rear base...) through YOUR zones only. Every zone crossed costs something: terrain (mountains are hard) divided by the quality of its road. The farther and harder the way, the lower the supply level of a zone. Formations recover and fight in proportion to the level of their zone. If disabled, a zone is either connected to a source (full supply) or cut off. [default: on]
	supply_range = 15, -- @ui numeric min=2 max=100 group=""Wargame - supply"" label=""Supply range"" help=How much ""cost"" supplies can pay before they die out. A plain zone with a normal road costs about 1, so 15 = about 15 zones of good road. A mountain without road can cost more than 10 for one zone. Higher = supplies reach farther. [default: 15]
	supply_min_level = 0.15, -- @ui numeric min=0 max=1 group=""Wargame - supply"" label=""Cut-off level"" help=Below this supply level (0 to 1) a zone is considered cut off: no recovery and the cut-off penalty in fights. 0.15 = under 15%. [default: 0.15]
	bridge_down_factor = 0.1, -- @ui numeric min=0.01 max=1 group=""Wargame - supply"" label=""Road flow with destroyed bridges"" help=Objectives marked as BRIDGE in the wargame editor (targetlist field wargameBridge) slow down the supplies passing through their zone. With all the bridges of a zone destroyed, the road keeps only this share of its flow. 0.1 = 10%: supplies almost stop. 1 = bridges change nothing. One living bridge is enough to keep the full road. [default: 0.1]
	supply_source_full = 100, -- @ui numeric min=1 max=1000 group=""Wargame - supply"" label=""Capacity that supplies a whole army"" help=Each supply source (objective with a ""Supply capacity"": port, depot, air base..., sea supply of a landing plan, forward supply point) has a capacity. This value is the capacity that is enough, on its own, to supply a whole army (100 % at the source). It is a capacity, not a stock: a depot never empties, it only gets weaker when damaged. Capacities in the same zone add up. [default: 100]
	cutoff_can_attack = false, -- @ui checkbox group=""Wargame - supply"" label=""Cut-off zones can attack"" help=If disabled, a zone that is cut off from supply cannot attack (it can still defend). This limits how far an offensive can go from its supply sources. [default: false]
	cutoff_grace_turns = 3, -- @ui numeric min=0 max=20 group=""Wargame - supply"" label=""Turns before a cut-off zone stops attacking"" help=A zone that just lost its supply (for example a landing beach) can still attack for this many turns: the formations live on the stock they brought. The count restarts when a landing wave arrives or when supply comes back. [default: 3]
	cutoff_attrition = 0.05, -- @ui numeric min=0 max=0.5 group=""Wargame - supply"" label=""Strength lost per mission without stock"" help=A formation cut off from supply, with no stock left for 2 missions or more, loses this share of its strength each mission, even without fighting (0.05 = 5 %). [default: 0.05]
	cutoff_attrition_floor = 0.25, -- @ui numeric min=0 max=1 group=""Wargame - supply"" label=""Lowest strength from lack of supply"" help=Lack of supply never brings a formation below this share of its normal strength (0.25 = 25 %). [default: 0.25]
	supply_convoys = false, -- @ui checkbox group=""Wargame - convoys"" label=""Supply convoys and forward supply points"" help=If enabled, zones with formations that are badly supplied get a road convoy. Each convoy feeds a forward supply point near the formations: a small supply source with a stock of a few missions. Destroyed convoys do not refill it. The forward point replaces the ""turns before a cut-off zone stops attacking"" stock (except for a landing wave, which keeps its own stock). [default: false]
	convoy_max = 3, -- @ui numeric min=0 max=20 group=""Wargame - convoys"" label=""Maximum convoys per side"" help=Most convoys a side can send each mission. If more zones need one, they are grouped into this many sectors. [default: 3]
	convoy_free_range = 4, -- @ui numeric min=0.5 max=100 group=""Wargame - convoys"" label=""Reach of a source without convoys"" help=When convoys are enabled, a supply source only supplies the zones around it, up to this cost (about 2 to 3 zones on good roads; same costs as ""Supply range""). Farther zones need a convoy and a forward supply point. When convoys are disabled, ""Supply range"" is used. [default: 4]
	convoy_need_level = 0.6, -- @ui numeric min=0 max=1 group=""Wargame - convoys"" label=""Supply level that needs a convoy"" help=A zone with formations whose supply level is below this value (0.6 = 60 %) needs a convoy. Front line zones come first. [default: 0.6]
	forward_point_capacity = 40, -- @ui numeric min=0 max=1000 group=""Wargame - convoys"" label=""Forward supply point capacity"" help=Supply capacity of a forward supply point while it still has stock (same scale as the other sources). [default: 40]
	forward_point_reserve = 3, -- @ui numeric min=0 max=20 group=""Wargame - convoys"" label=""Forward supply point stock (missions)"" help=Stock of a full forward supply point, in missions. A convoy that arrives fills it in proportion to what it delivers (half destroyed = half the stock). Each mission without a convoy uses one mission of stock. [default: 3]
	forward_point_rebuild_loss = 0.5, -- @ui numeric min=0 max=1 group=""Wargame - convoys"" label=""Loss when a destroyed supply point is rebuilt"" help=If the forward supply point was destroyed in the last mission (below 10 %), the next convoy rebuilds it but only refills (1 - this value) of what it delivers (0.5 = half). [default: 0.5]
	terrain = { -- @ui matrix group=""Wargame - terrain and roads"" label=""Terrain"" rows=""plain:Plain,hills:Hills,mountain:Mountain,urban:Urban,coastal:Coastal"" cols=""1:Defense x,2:Supply cost x"" help=Defense x: how much the terrain helps the DEFENDER of a zone. 1.0 = no effect. 1.5 = the defender counts as 50% stronger. Below 1.0 = the defender is weaker. Allowed 0.5 to 3. Supply cost x: how hard it is to bring supplies THROUGH this terrain (see ""Supply range""). 1.0 = easy. 3 = three times harder. Allowed 0.5 to 10. [default: plain 1.0/1.0, hills 1.3/1.5, mountain 1.8/3.0, urban 1.6/1.5, coastal 1.1/1.0]
		plain    = { 1.0, 1.0 },
		hills    = { 1.3, 1.5 },
		mountain = { 1.8, 3.0 },
		urban    = { 1.6, 1.5 },
		coastal  = { 1.1, 1.0 },
	},
	supply_route = { -- @ui matrix group=""Wargame - terrain and roads"" label=""Supply route"" rows=""none:No road,track:Track,road:Road,highway:Highway"" cols=""1:Recovery x,2:Combat x,3:Supply flow x"" help=Recovery x: multiplies how fast formations in the zone regain strength (see ""Reinforcement speed""). Combat x: multiplies the strength of the formations of the zone in a fight, attacking or defending. 1.0 = no effect. Allowed 0 to 3 for Recovery, 0.1 to 3 for Combat. Tip: if you do not want roads to change the fights, set the whole Combat column to 1.0. Supply flow x: how well supplies travel along this road (see ""Supply range""). Low = a bottleneck. Allowed 0.05 to 3. [default: none 0.25/0.25/0.25, track 0.5/0.5/0.5, road 1.0/1.0/1.0, highway 1.5/1.5/1.5]
		none    = { 0.25, 0.25, 0.25 },
		track   = { 0.5,  0.5,  0.5  },
		road    = { 1.0,  1.0,  1.0  },
		highway = { 1.5,  1.5,  1.5  },
	},

	-- Not in the editor list (no @ui line), same rules as the others. Values shown = built-in defaults.
	cutoff_supply_factor = 0.5,        -- combat strength multiplier of a fully cut-off zone (0.5 = half)
	front_line_recovery_factor = 0.5,  -- recovery multiplier of a formation in a front line zone
	influence_decay = 0.5,             -- weight of the influence of zones two steps away
	advance_on_conquest = true,        -- false = nobody moves into a conquered zone
}
";

        // Fichier de réglage fin du campaignMaker. Il contient une table globale "wargame_balance" avec
        // les mêmes clés que camp.wargame_config (control_flip_threshold, terrain, supply_route...).
        // Fichier absent ou illisible : on garde ce qui est déjà chargé, sans erreur.
        private void LoadBalanceFile(string balancePath)
        {
            if (!File.Exists(balancePath))
                return;

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

                    lua.DoFile(balancePath);

                    LuaTable balance = lua["wargame_balance"] as LuaTable;
                    if (balance == null)
                    {
                        FormUtils.LogRegister("WargameCampaignInfo | table 'wargame_balance' absente de " + balancePath);
                        return;
                    }

                    ReadCombatConfig(balance);
                    BalanceFileFound = true;
                }
            }
            catch (Exception ex)
            {
                FormUtils.LogRegister("WargameCampaignInfo | erreur lecture wargame_balance.lua : " + ex.Message);
            }
        }

        // Chaque clé est facultative : absente, la valeur par défaut du code est gardée.
        private void ReadCombatConfig(LuaTable cfg)
        {
            ReadNumber(cfg, "control_flip_threshold", ref CombatConfig.ControlFlipThreshold);
            ReadNumber(cfg, "retreat_loss_fraction", ref CombatConfig.RetreatLossFraction);
            ReadNumber(cfg, "landing_fail_loss", ref CombatConfig.LandingFailLoss);
            ReadNumber(cfg, "landing_max_retries", ref CombatConfig.LandingMaxRetries);
            ReadNumber(cfg, "landing_reembark_loss", ref CombatConfig.ReembarkLoss);
            ReadNumber(cfg, "landing_wave_gap", ref CombatConfig.LandingWaveGap);
            ReadNumber(cfg, "cutoff_supply_factor", ref CombatConfig.CutOffSupplyFactor);
            ReadNumber(cfg, "random_variance", ref CombatConfig.RandomVariance);
            ReadNumber(cfg, "supply_recovery_rate", ref CombatConfig.SupplyRecoveryRate);
            ReadNumber(cfg, "front_line_recovery_factor", ref CombatConfig.FrontLineRecoveryFactor);
            ReadNumber(cfg, "engaged_recovery", ref CombatConfig.EngagedRecoveryFactor);
            ReadNumber(cfg, "combat_attrition", ref CombatConfig.CombatAttrition);
            ReadNumber(cfg, "influence_decay", ref CombatConfig.InfluenceDecay);
            ReadBool(cfg, "advance_on_conquest", ref CombatConfig.AdvanceOnConquest);
            ReadBool(cfg, "front_alignment", ref CombatConfig.FrontAlignmentEnabled);
            ReadBool(cfg, "cutoff_can_attack", ref CombatConfig.CutOffCanAttack);
            ReadNumber(cfg, "cutoff_grace_turns", ref CombatConfig.CutOffGraceTurns);
            ReadNumber(cfg, "cutoff_attrition", ref CombatConfig.CutOffAttrition);
            ReadNumber(cfg, "cutoff_attrition_floor", ref CombatConfig.CutOffAttritionFloor);
            ReadBool(cfg, "supply_convoys", ref CombatConfig.SupplyConvoys);
            ReadNumber(cfg, "convoy_max", ref CombatConfig.ConvoyMax);
            ReadNumber(cfg, "convoy_need_level", ref CombatConfig.ConvoyNeedLevel);
            ReadNumber(cfg, "forward_point_capacity", ref CombatConfig.ForwardPointCapacity);
            ReadNumber(cfg, "forward_point_reserve", ref CombatConfig.ForwardPointReserve);
            ReadNumber(cfg, "convoy_free_range", ref CombatConfig.ConvoyFreeRange);
            ReadNumber(cfg, "forward_point_rebuild_loss", ref CombatConfig.ForwardRebuildLoss);
            ReadBool(cfg, "empty_zone_influence", ref CombatConfig.EmptyZoneInfluence);
            ReadBool(cfg, "axis_reinforcement", ref CombatConfig.AxisReinforcement);
            ReadBool(cfg, "axis_pull_surplus", ref CombatConfig.AxisPullSurplus);
            ReadBool(cfg, "objective_garrison", ref CombatConfig.ObjectiveGarrison);
            ReadBool(cfg, "axis_use_terrain", ref CombatConfig.AxisUseTerrain);

            double flexDetour = CombatConfig.AxisFlexDetour;
            ReadNumber(cfg, "axis_flex_detour", ref flexDetour);
            CombatConfig.AxisFlexDetour = Math.Max(0, (int)Math.Round(flexDetour));

            // Réseau de ravitaillement
            ReadBool(cfg, "supply_network", ref CombatConfig.SupplyNetwork);
            ReadNumber(cfg, "supply_range", ref CombatConfig.SupplyRange);
            ReadNumber(cfg, "supply_min_level", ref CombatConfig.SupplyMinLevel);
            ReadNumber(cfg, "bridge_down_factor", ref CombatConfig.BridgeDownFactor);
            ReadNumber(cfg, "supply_source_full", ref CombatConfig.SupplySourceFull);

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
            WargameConfigGrid.ReadColumn(cfg, "terrain", 2, CombatConfig.TerrainMove, 0.5, 10.0);
            WargameConfigGrid.ReadColumn(cfg, "supply_route", 1, CombatConfig.RouteRecovery, 0.0, 3.0);
            WargameConfigGrid.ReadColumn(cfg, "supply_route", 2, CombatConfig.RouteCombat, 0.1, 3.0);
            WargameConfigGrid.ReadColumn(cfg, "supply_route", 3, CombatConfig.RouteFlow, 0.05, 3.0);

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
