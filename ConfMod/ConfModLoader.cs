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
        // Cache par fichier (chemin complet), explicitement invalidé par ConfModWriter
        // après un Save(), et entièrement vidé par ClearCache() quand l'identité d'une
        // campagne devient ambiguë (changement de configuration, ajout/suppression -
        // voir LoadCampaignsAsync). La clé est le chemin du fichier, pas le nom de la
        // campagne : une même campagne a conf_mod.lua ET camp_init.lua, deux fichiers
        // différents qui ne doivent pas se marcher dessus dans le cache.
        private static Dictionary<string, ConfModDynamicData> _cache = new Dictionary<string, ConfModDynamicData>();

        public string GetConfModPath(string campaignName)
        {
            return Path.Combine(
                ParamConf.PATH_SavedGames_DCS,
                @"Mods\tech\DCE\Missions\Campaigns",
                campaignName,
                @"Init\conf_mod.lua");
        }

        // Charge conf_mod.lua pour une campagne (comportement historique, inchangé).
        public ConfModDynamicData Load(string campaignName, bool forceReload = false)
        {
            return Load(campaignName, GetConfModPath(campaignName), forceReload);
        }

        // Charge n'importe quel fichier .lua tagué @ui (conf_mod.lua, camp_init.lua...).
        // campaignName sert juste à l'affichage/au contexte, filePath est ce qui compte
        // vraiment pour la lecture et, plus tard, l'écriture.
        public ConfModDynamicData Load(string campaignName, string filePath, bool forceReload = false)
        {
            if (!forceReload && _cache.ContainsKey(filePath))
                return _cache[filePath];

            if (!File.Exists(filePath))
            {
                FormUtils.LogRegister("ConfModLoader | file not found: " + filePath);
                return null;
            }

            string[] lines = File.ReadAllLines(filePath);

            var data = new ConfModDynamicData
            {
                CampaignName = campaignName,
                FilePath = filePath,
                Schema = ConfUiSchemaParser.Parse(lines)
            };

            // conf_mod.lua/camp_init.lua sont des fichiers de données purs (pas de
            // dofile/require interne), donc exécutables directement sans injecter de
            // variables d'environnement.
            using (Lua lua = new Lua())
            {
                lua.DoFile(filePath);

                foreach (ConfUiFieldSchema field in data.Schema)
                {
                    object luaValue = ResolveLuaValue(lua, field.Path);
                    data.Values[field.Path] = ConvertFromLua(luaValue, field);
                }
            }

            _cache[filePath] = data;

            return data;
        }

        public void InvalidateCache(string campaignName)
        {
            _cache.Remove(GetConfModPath(campaignName));
        }

        // Invalide un fichier précis (utile pour camp_init.lua ou tout autre fichier
        // qui ne suit pas la convention conf_mod.lua).
        public void InvalidateCacheFile(string filePath)
        {
            _cache.Remove(filePath);
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
                    // Si un champ tagué "text" reçoit en fait une table Lua (mauvais tag,
                    // ou @ui mal placé), on ne fait surtout pas .ToString() dessus - ça
                    // renverrait juste le mot "table" et écraserait la table à l'écriture.
                    return (luaValue != null && !(luaValue is LuaTable)) ? luaValue.ToString() : "";

                case UiFieldType.Combo:
                    return ComboTokenFromLua(luaValue);

                case UiFieldType.Matrix:
                    return MatrixFromLua(luaValue, field);

                case UiFieldType.List:
                    return ListFromLua(luaValue);

                default:
                    return luaValue;
            }
        }

        // Lit un tableau Lua de chaînes (1-indexé) en List<string>, ex:
        // pictureBrief.blue = { "Frontline1.png", "Frontline2.png" }.
        private static List<string> ListFromLua(object luaValue)
        {
            var result = new List<string>();
            LuaTable table = luaValue as LuaTable;

            if (table == null)
                return result;

            int i = 1;

            while (true)
            {
                object v = table[i];

                if (v == null)
                    break;

                result.Add(v.ToString());
                i++;
            }

            return result;
        }

        // Builds one row-key -> full positional array entry per schema.RowSpecs.
        // Two shapes are supported:
        //  - multi-row matrix: luaValue is a table of named sub-tables, one per row
        //    (e.g. campMod.RepairOption.blue = { airUnit = {...}, airbase = {...} }).
        //  - single-row "self" matrix: luaValue IS the positional array itself
        //    (e.g. campMod.RepairOption.blue.runway = {0,20,0,0,25,50}), used when
        //    RowSpecs has exactly one entry whose key matches the field's own key.
        private static Dictionary<string, double[]> MatrixFromLua(object luaValue, ConfUiFieldSchema field)
        {
            var rows = new Dictionary<string, double[]>();
            LuaTable matrixTable = luaValue as LuaTable;

            if (matrixTable == null)
                return rows;

            foreach (UiOption rowSpec in field.RowSpecs)
            {
                LuaTable rowArray = matrixTable[rowSpec.Value] as LuaTable;

                if (rowArray == null && field.RowSpecs.Count == 1 && rowSpec.Value == field.Key)
                    rowArray = matrixTable;

                if (rowArray == null)
                {
                    FormUtils.LogRegister("ConfModLoader | matrix row not found: " + field.Path + "." + rowSpec.Value);
                    continue;
                }

                rows[rowSpec.Value] = ExtractPositionalArray(rowArray);
            }

            return rows;
        }

        // Reads a Lua array table (1-based integer keys) into a 0-based double[].
        private static double[] ExtractPositionalArray(LuaTable table)
        {
            var values = new List<double>();
            int i = 1;

            while (true)
            {
                object v = table[i];

                if (v == null)
                    break;

                values.Add(ToDouble(v) ?? 0);
                i++;
            }

            return values.ToArray();
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
