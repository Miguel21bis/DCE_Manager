using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;

namespace DCE_Manager
{
    // Reads conf_mod.lua as plain text and extracts every "-- @ui ..." tagged field,
    // together with its position in the Lua table hierarchy (its dotted path) and its
    // position in the file (used to preserve field order and grouping in the UI).
    internal static class ConfUiSchemaParser
    {
        private static readonly Regex ContainerOpenRegex =
            new Regex(@"^\s*(?:\[""(?<bkey>[^""]+)""\]|(?<key>[A-Za-z_][A-Za-z0-9_]*))\s*=\s*{\s*$");

        private static readonly Regex ContainerCloseRegex = new Regex(@"^\s*}\s*,?\s*$");

        private static readonly Regex FieldKeyRegex =
            new Regex(@"^\s*(?:\[""(?<bkey>[^""]+)""\]|(?<key>[A-Za-z_][A-Za-z0-9_]*))\s*=");

        public static List<ConfUiFieldSchema> Parse(string[] lines)
        {
            var result = new List<ConfUiFieldSchema>();
            var stack = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string code = StripComment(raw);

                Match openMatch = ContainerOpenRegex.Match(code);

                if (openMatch.Success)
                {
                    string name = openMatch.Groups["bkey"].Success
                        ? openMatch.Groups["bkey"].Value
                        : openMatch.Groups["key"].Value;

                    stack.Add(name);
                    continue;
                }

                if (ContainerCloseRegex.IsMatch(code))
                {
                    if (stack.Count > 0)
                        stack.RemoveAt(stack.Count - 1);

                    continue;
                }

                int uiIdx = raw.IndexOf("@ui", StringComparison.Ordinal);

                if (uiIdx < 0)
                    continue;

                Match keyMatch = FieldKeyRegex.Match(code);

                if (!keyMatch.Success)
                    continue;

                string key = keyMatch.Groups["bkey"].Success
                    ? keyMatch.Groups["bkey"].Value
                    : keyMatch.Groups["key"].Value;

                string containerPath = string.Join(".", stack.ToArray());
                string path = containerPath.Length > 0 ? containerPath + "." + key : key;

                string tagText = raw.Substring(uiIdx + 3).Trim();

                ConfUiFieldSchema schema = ParseTag(tagText);
                schema.Path = path;
                schema.Key = key;
                schema.ContainerPath = containerPath;
                schema.LineIndex = i;

                if (string.IsNullOrEmpty(schema.Label))
                    schema.Label = key;

                result.Add(schema);
            }

            return result;
        }

        private static ConfUiFieldSchema ParseTag(string tagText)
        {
            var schema = new ConfUiFieldSchema();

            int helpIdx = tagText.IndexOf("help=", StringComparison.Ordinal);
            string head = tagText;

            if (helpIdx >= 0)
            {
                schema.Help = tagText.Substring(helpIdx + "help=".Length).Trim();
                head = tagText.Substring(0, helpIdx);
            }

            List<string> tokens = SplitRespectingQuotes(head);

            if (tokens.Count > 0)
            {
                schema.Type = ParseType(tokens[0]);
                tokens.RemoveAt(0);
            }

            foreach (string token in tokens)
            {
                int eq = token.IndexOf('=');

                if (eq < 0)
                    continue;

                string key = token.Substring(0, eq);
                string value = token.Substring(eq + 1).Trim('"');

                switch (key)
                {
                    case "min":
                        schema.Min = ParseDouble(value);
                        break;
                    case "max":
                        schema.Max = ParseDouble(value);
                        break;
                    case "step":
                        schema.Step = ParseDouble(value) ?? 1;
                        break;
                    case "group":
                        schema.Group = value;
                        break;
                    case "label":
                        schema.Label = value;
                        break;
                    case "zero-false":
                        schema.ZeroIsFalse = value == "true";
                        break;
                    case "options":
                        schema.Options = ParseOptions(value);
                        break;
                }
            }

            return schema;
        }

        private static UiFieldType ParseType(string token)
        {
            switch (token.ToLowerInvariant())
            {
                case "checkbox": return UiFieldType.Checkbox;
                case "numeric": return UiFieldType.Numeric;
                case "slider": return UiFieldType.Slider;
                case "combo": return UiFieldType.Combo;
                case "text": return UiFieldType.Text;
                default: return UiFieldType.Text;
            }
        }

        private static List<UiOption> ParseOptions(string value)
        {
            var list = new List<UiOption>();

            foreach (string part in value.Split(','))
            {
                int idx = part.IndexOf(':');

                if (idx < 0)
                {
                    list.Add(new UiOption(part, part));
                    continue;
                }

                string val = part.Substring(0, idx);
                string label = part.Substring(idx + 1);
                list.Add(new UiOption(val, label));
            }

            return list;
        }

        // Splits on whitespace, keeping the content of "quoted sections" intact
        // (so group="Long label with spaces" stays a single token).
        private static List<string> SplitRespectingQuotes(string text)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in text)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Length = 0;
                    }

                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens;
        }

        private static double? ParseDouble(string value)
        {
            double d;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : (double?)null;
        }

        internal static string StripComment(string line)
        {
            int idx = line.IndexOf("--", StringComparison.Ordinal);
            return idx >= 0 ? line.Substring(0, idx) : line;
        }
    }
}
