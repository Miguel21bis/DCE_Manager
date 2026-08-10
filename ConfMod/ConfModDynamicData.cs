using System.Collections.Generic;

namespace DCE_Manager.Parameters
{
    // Generic, schema-driven replacement for the old per-block POCOs (ConfModWeatherData,
    // ConfModDifficultyData, etc.). Every field described by a "@ui" tag in conf_mod.lua
    // ends up here, keyed by its dotted path - no C# class needs to change when a field
    // is added, removed or moved in conf_mod.lua.
    internal class ConfModDynamicData
    {
        public string CampaignName { get; set; }

        // Chemin du fichier .lua dont proviennent Schema/Values (conf_mod.lua,
        // camp_init.lua, ou n'importe quel autre fichier tagué @ui). C'est ce chemin,
        // et non plus CampaignName, qui indique où écrire lors d'un Save().
        public string FilePath { get; set; }

        // All @ui fields found in the file, in file order.
        public List<ConfUiFieldSchema> Schema { get; set; }

        // Current value per field path. Types: bool (checkbox), double (numeric/slider),
        // string (text and combo - for combo this is the option's raw token, e.g. "0",
        // "fast", "true", matching one of that field's UiOption.Value entries).
        public Dictionary<string, object> Values { get; set; }

        public ConfModDynamicData()
        {
            Schema = new List<ConfUiFieldSchema>();
            Values = new Dictionary<string, object>();
        }
    }
}
