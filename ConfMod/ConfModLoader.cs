using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;
using NLua;

namespace DCE_Manager
{
    internal class ConfModLoader
    {
        // Cache per campaign, explicitly invalidated by ConfModWriter after a Save().
        private static Dictionary<string, ConfModDynamicData> _cache = new Dictionary<string, ConfModDynamicData>();

        public string GetConfModPath(string campaignName)
        {
            return Path.Combine(
                ParamConf.PATH_SavedGames_DCS,
                @"Mods\tech\DCE\Missions\Campaigns",
                campaignName,
                @"Init\conf_mod.lua");
        }

        public ConfModDynamicData Load(string campaignName, bool forceReload = false)
        {
            if (!forceReload && _cache.ContainsKey(campaignName))
                return _cache[campaignName];

            string path = GetConfModPath(campaignName);

            if (!File.Exists(path))
            {
                FormUtils.LogRegister("ConfModLoader | file not found: " + path);
                return null;
            }

            string[] lines = File.ReadAllLines(path);

            var data = new ConfModDynamicData
            {
                CampaignName = campaignName,
                Schema = ConfUiSchemaParser.Parse(lines)
            };

            // conf_mod.lua is a pure data file (no internal dofile/require), so it
            // can be executed directly without injecting any environment variables.
            using (Lua lua = new Lua())
            {
                lua.DoFile(path);

                foreach (ConfUiFieldSchema field in data.Schema)
                {
                    object luaValue = ResolveLuaValue(lua, field.Path);
                    data.Values[field.Path] = ConvertFromLua(luaValue, field);
                }
            }

            _cache[campaignName] = data;

            return data;
        }

        public void InvalidateCache(string campaignName)
        {
            _cache.Remove(campaignName);
        }

        // Called whenever campaign identity becomes ambiguous across configurations:
        // switching configuration (DCSA/DCSB...), or adding/removing a campaign.
        // Different configurations can contain campaigns with the same folder name,
        // and the cache is only keyed by that name, so it must be fully cleared rather
        // than invalidated one entry at a time.
        public static void ClearCache()
        {
            _cache.Clear();
        }

        // Walks a dotted path (e.g. "mission_ini.weather.trend") through nested
        // LuaTables, starting from a global.
        private static object ResolveLuaValue(Lua lua, string path)
        {
            string[] segments = path.Split('.');
            object current = lua[segments[0]];

            for (int i = 1; i < segments.Length && current != null; i++)
            {
                LuaTable t = current as LuaTable;
                current = t != null ? t[segments[i]] : null;
            }

            return current;
        }

        private static object ConvertFromLua(object luaValue, ConfUiFieldSchema field)
        {
            switch (field.Type)
            {
                case UiFieldType.Checkbox:
                    return ToBool(luaValue);

                case UiFieldType.Numeric:
                case UiFieldType.Slider:
                    if (field.ZeroIsFalse && luaValue is bool)
                        return 0d;

                    double? d = ToDouble(luaValue);
                    return d ?? (field.Min ?? 0);

                case UiFieldType.Text:
                    return luaValue != null ? luaValue.ToString() : "";

                case UiFieldType.Combo:
                    return ComboTokenFromLua(luaValue);

                default:
                    return luaValue;
            }
        }

        // Converts a raw Lua value into the token string used by that field's
        // UiOption.Value list (e.g. true -> "true", 6.0 -> "6", "auto" -> "auto").
        private static string ComboTokenFromLua(object luaValue)
        {
            if (luaValue == null)
                return "";

            if (luaValue is bool)
                return ((bool)luaValue) ? "true" : "false";

            double? d = ToDouble(luaValue);

            if (d.HasValue)
                return FormatNumberToken(d.Value);

            return luaValue.ToString();
        }

        private static string FormatNumberToken(double d)
        {
            return d == Math.Floor(d)
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString(CultureInfo.InvariantCulture);
        }

        private static bool ToBool(object value)
        {
            if (value == null)
                return false;

            if (value is bool)
                return (bool)value;

            double? d = ToDouble(value);
            return d.HasValue && d.Value != 0;
        }

        private static double? ToDouble(object value)
        {
            if (value == null || value is bool)
                return null;

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }
    }
}