using System;
using System.Collections.Generic;
using System.IO;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    internal class Parser_TargetList
    {
        private static readonly string[] Sides = { "blue", "red" };

        // Charge séparément Init\targetlist_init.lua et Active\targetlist.lua (si présent).
        // Comme pour les squads (Parser_OobAir) : chaque cible existe en version "Init" ET en
        // version "Active" dans la même liste, distinguées par FolderFile. C'est l'affichage
        // (LoadGridTargets) qui choisit laquelle montrer selon le bouton Init/Active sélectionné.
        public List<TargetAssetInfo> LoadTargets(string campaignName)
        {
            var result = new List<TargetAssetInfo>();

            LoadInitFile(campaignName, result);
            LoadActiveFile(campaignName, result);

            // même tri que dans ScriptsMod.NG (priorité décroissante)
            result.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            return result;
        }

        private void LoadInitFile(string campaignName, List<TargetAssetInfo> result)
        {
            string pathFile = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns",
                campaignName, "Init", "targetlist_init.lua");

            if (!File.Exists(pathFile))
            {
                FormUtils.LogRegister($"targetlist_init.lua not found: {pathFile}");
                return;
            }

            using (Lua lua = new Lua())
            {
                lua.DoFile(pathFile);

                LuaTable targetlist = lua["targetlist"] as LuaTable;
                if (targetlist == null)
                {
                    FormUtils.LogRegister("targetlist table not found in targetlist_init.lua.");
                    return;
                }

                foreach (string side in Sides)
                {
                    LuaTable sideTable = targetlist[side] as LuaTable;
                    if (sideTable == null) continue;

                    foreach (object key in sideTable.Keys)
                    {
                        LuaTable t = sideTable[key] as LuaTable;
                        if (t == null) continue;

                        // Pas de "class" = tâche aérienne (CAP, AWACS, SAR, Refueling, Intercept,
                        // Transport...), pas une cible physique au sol -> on ignore.
                        object classVal = t["class"];
                        if (classVal == null) continue;

                        result.Add(new TargetAssetInfo
                        {
                            Side = side,
                            FolderFile = "Init",
                            TitleName = key.ToString(),
                            Task = t["task"]?.ToString(),
                            Class = classVal.ToString(),
                            Priority = t["priority"] != null ? Convert.ToInt32(t["priority"]) : 0,
                            Inactive = t["inactive"] != null && Convert.ToBoolean(t["inactive"]),
                            Alive = 100, // pas encore de mission jouée -> tout est intact
                        });
                    }
                }
            }
        }

        private void LoadActiveFile(string campaignName, List<TargetAssetInfo> result)
        {
            string pathFile = Path.Combine(ParamConf.PATH_SavedGames_DCS, @"Mods\tech\DCE\Missions\Campaigns",
                campaignName, "Active", "targetlist.lua");

            if (!File.Exists(pathFile))
                return; // campagne pas encore jouée -> pas de version Active pour l'instant

            using (Lua lua = new Lua())
            {
                lua.DoFile(pathFile);

                LuaTable targetlist = lua["targetlist"] as LuaTable;
                if (targetlist == null)
                {
                    FormUtils.LogRegister("targetlist table not found in targetlist.lua.");
                    return;
                }

                foreach (string side in Sides)
                {
                    LuaTable sideTable = targetlist[side] as LuaTable;
                    if (sideTable == null) continue;

                    // Ici les clés sont numériques, le nom d'origine est dans le champ titleName
                    foreach (object key in sideTable.Keys)
                    {
                        LuaTable t = sideTable[key] as LuaTable;
                        if (t == null) continue;

                        object classVal = t["class"];
                        if (classVal == null) continue;

                        string titleName = t["titleName"]?.ToString();
                        if (string.IsNullOrEmpty(titleName)) continue;

                        result.Add(new TargetAssetInfo
                        {
                            Side = side,
                            FolderFile = "Active",
                            TitleName = titleName,
                            Task = t["task"]?.ToString(),
                            Class = classVal.ToString(),
                            Priority = t["priority"] != null ? Convert.ToInt32(t["priority"]) : 0,
                            Inactive = t["inactive"] != null && Convert.ToBoolean(t["inactive"]),
                            Alive = t["alive"] != null ? Convert.ToInt32(t["alive"]) : 100,
                        });
                    }
                }
            }
        }
    }
}
