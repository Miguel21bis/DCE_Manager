using System.Collections.Generic;

namespace DCE_Manager.Parameters
{
    internal enum UiFieldType
    {
        Checkbox,
        Numeric,
        Slider,
        Combo,
        Text,
        Matrix
    }

    // One selectable choice for a Combo field. ToString() returns the label so it
    // displays correctly when the option is added directly to a WinForms ComboBox.
    internal class UiOption
    {
        public string Value { get; }
        public string Label { get; }

        public UiOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    // Parsed representation of one "-- @ui ..." tag found in conf_mod.lua.
    // Grammar: -- @ui <type> [min=N] [max=N] [group="Name"] [label="Text"]
    //          [options="value:Label,..."] [zero-false=true] [step=N] [help=free text]
    internal class ConfUiFieldSchema
    {
        // Dotted path to this field, e.g. "mission_ini.weather.trend"
        public string Path { get; set; }
        // Last path segment, e.g. "trend"
        public string Key { get; set; }
        // Path to the containing table, e.g. "mission_ini.weather" ("" for top-level)
        public string ContainerPath { get; set; }
        // Line index in the source file - preserves the file's own field order in the UI
        public int LineIndex { get; set; }

        public UiFieldType Type { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double Step { get; set; }
        public string Group { get; set; }
        public string Label { get; set; }
        public string Help { get; set; }
        // True if a value of 0 must round-trip as the Lua literal "false"
        public bool ZeroIsFalse { get; set; }
        public List<UiOption> Options { get; set; }

        // Minimum DCE_Manager.UserLevel required to see this field. Defaults to
        // UserLevel.Player; a tag's "audience=campaignMaker" attribute raises it.
        public int MinLevel { get; set; }

        // Matrix only: rows and columns, reusing UiOption (Value:Label). For a row,
        // Value is the Lua key (e.g. "airUnit"). For a column, Value is the 1-based
        // positional index into that row's Lua array (e.g. "2" for deathPoint).
        public List<UiOption> RowSpecs { get; set; }
        public List<UiOption> ColSpecs { get; set; }

        public ConfUiFieldSchema()
        {
            Step = 1;
            Group = "General";
            MinLevel = DCE_Manager.UserLevel.Player;
            Options = new List<UiOption>();
            RowSpecs = new List<UiOption>();
            ColSpecs = new List<UiOption>();
        }
    }
}