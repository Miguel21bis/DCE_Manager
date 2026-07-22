using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Writes conf_mod.lua via targeted line-by-line replacement, driven entirely by the
    // schema extracted from the file's own "@ui" tags. Adding, removing or moving a
    // field in conf_mod.lua changes what gets written here automatically - nothing to
    // touch in C#. Everything outside the tagged fields (comments, non-tagged columns,
    // formatting) is preserved byte-for-byte.
    internal class ConfModWriter
    {
        private readonly ConfModLoader _loader = new ConfModLoader();

        public bool Save(ConfModDynamicData data)
        {
            string path = _loader.GetConfModPath(data.CampaignName);

            if (!File.Exists(path))
            {
                FormUtils.LogRegister("ConfModWriter | file not found: " + path);
                return false;
            }

            string[] lines = File.ReadAllLines(path);
            var blockRangeCache = new Dictionary<string, Tuple<int, int>>();
            var missingFields = new List<string>();

            foreach (ConfUiFieldSchema field in data.Schema)
            {
                object value;

                if (!data.Values.TryGetValue(field.Path, out value))
                    continue;

                if (field.Type == UiFieldType.Matrix)
                {
                    SaveMatrixField(lines, field, value as Dictionary<string, double[]>, blockRangeCache, missingFields);
                    continue;
                }

                int start, end;

                if (!TryResolveContainerRange(lines, field.ContainerPath, blockRangeCache, out start, out end))
                {
                    missingFields.Add(field.Path);
                    continue;
                }

                string literal = BuildLiteral(field, value);

                if (!ReplaceRawValue(lines, start, end, field.Key, literal))
                    missingFields.Add(field.Path);
            }

            if (missingFields.Count > 0)
                FormUtils.LogRegister("ConfModWriter | fields not found, skipped: " + string.Join(", ", missingFields.ToArray()));

            string tmpPath = path + ".tmp";

            using (var sw = new StreamWriter(tmpPath))
            {
                foreach (string line in lines)
                    sw.WriteLine(line);
            }

            File.Copy(tmpPath, path, overwrite: true);
            File.Delete(tmpPath);

            _loader.InvalidateCache(data.CampaignName);

            FormUtils.LogRegister("ConfModWriter | conf_mod.lua saved for " + data.CampaignName);

            return true;
        }

        private static string BuildLiteral(ConfUiFieldSchema field, object value)
        {
            switch (field.Type)
            {
                case UiFieldType.Checkbox:
                    return Convert.ToBoolean(value) ? "true" : "false";

                case UiFieldType.Numeric:
                case UiFieldType.Slider:
                    {
                        double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);

                        if (field.ZeroIsFalse && d == 0)
                            return "false";

                        return d.ToString(CultureInfo.InvariantCulture);
                    }

                case UiFieldType.Text:
                    return "\"" + (value != null ? value.ToString() : "").Replace("\"", "") + "\"";

                case UiFieldType.Combo:
                    return ComboLiteral(value != null ? value.ToString() : "");

                default:
                    return value != null ? value.ToString() : "";
            }
        }

        // A combo's stored value is a raw token (e.g. "0", "fast", "true"). Tokens that
        // are valid Lua booleans or numbers are written bare; anything else is quoted as
        // a Lua string. This single rule is what lets a field like silenceATC mix a
        // quoted "auto" with bare true/false options without any per-field special case.
        private static string ComboLiteral(string token)
        {
            if (token == "true" || token == "false")
                return token;

            double d;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return token;

            return "\"" + token.Replace("\"", "") + "\"";
        }

        // ---------------------------------------------------------------
        // Matrix fields: only the declared columns (field.ColSpecs, 1-based
        // positions) are overwritten inside each row's inline Lua array; every
        // other position, and the rest of the file, is left untouched.
        // ---------------------------------------------------------------

        private static void SaveMatrixField(
            string[] lines,
            ConfUiFieldSchema field,
            Dictionary<string, double[]> rows,
            Dictionary<string, Tuple<int, int>> blockRangeCache,
            List<string> missingFields)
        {
            if (rows == null)
                return;

            int matrixStart, matrixEnd;

            // field.Path IS the matrix's own container (e.g. "campMod.RepairOption.blue"
            // or "...blue.runway"), so resolving it as a "container path" gives exactly
            // the line range from its opening "{" to its matching "}".
            if (!TryResolveContainerRange(lines, field.Path, blockRangeCache, out matrixStart, out matrixEnd))
            {
                missingFields.Add(field.Path);
                return;
            }

            foreach (UiOption rowSpec in field.RowSpecs)
            {
                double[] fullArray;

                if (!rows.TryGetValue(rowSpec.Value, out fullArray))
                    continue;

                bool selfRow = field.RowSpecs.Count == 1 && rowSpec.Value == field.Key;
                int lineIndex = FindRowLine(lines, matrixStart, matrixEnd, rowSpec.Value, selfRow);

                if (lineIndex < 0 || !ReplacePositionsInLine(lines, lineIndex, field.ColSpecs, fullArray))
                    missingFields.Add(field.Path + "." + rowSpec.Value);
            }
        }

        // Finds the line holding one matrix row's inline array within [start, end]:
        //  - normal row: a "rowKey = { ... }" single-line declaration.
        //  - self row (single-row matrix, e.g. "runway"): the bare comma-separated
        //    values line with no "key =" prefix (the only content between the
        //    matrix's own opening and closing lines).
        private static int FindRowLine(string[] lines, int start, int end, string rowKey, bool selfRow)
        {
            if (selfRow)
            {
                var bareValuesRegex = new Regex(@"^\s*[-\d.]+(\s*,\s*[-\d.]+)*\s*,?\s*$");

                for (int i = start + 1; i < end; i++)
                {
                    string code = ConfUiSchemaParser.StripComment(lines[i]);

                    if (code.Trim().Length > 0 && bareValuesRegex.IsMatch(code))
                        return i;
                }

                return -1;
            }

            var rowRegex = new Regex(@"^\s*" + Regex.Escape(rowKey) + @"\s*=\s*{");

            for (int i = start; i <= end; i++)
            {
                if (rowRegex.IsMatch(ConfUiSchemaParser.StripComment(lines[i])))
                    return i;
            }

            return -1;
        }

        // Replaces only the declared 1-based positions inside a single line's inline
        // "{ a, b, c, ... }" array, leaving every other position, the indentation and
        // any trailing comment untouched.
        private static bool ReplacePositionsInLine(string[] lines, int lineIndex, List<UiOption> colSpecs, double[] fullArray)
        {
            string line = lines[lineIndex];
            int commentIdx = line.IndexOf("--", StringComparison.Ordinal);
            string codeOnly = commentIdx >= 0 ? line.Substring(0, commentIdx) : line;
            string commentPart = commentIdx >= 0 ? line.Substring(commentIdx) : "";

            int braceStart = codeOnly.IndexOf('{');
            int braceEnd = codeOnly.LastIndexOf('}');

            if (braceStart < 0 || braceEnd < 0 || braceEnd <= braceStart)
                return false;

            string inner = codeOnly.Substring(braceStart + 1, braceEnd - braceStart - 1);
            string[] parts = inner.Split(',');

            foreach (UiOption col in colSpecs)
            {
                int oneBased;

                if (!int.TryParse(col.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out oneBased))
                    continue;

                int idx = oneBased - 1;

                if (idx < 0 || idx >= parts.Length || idx >= fullArray.Length)
                    continue;

                parts[idx] = " " + fullArray[idx].ToString(CultureInfo.InvariantCulture) + " ";
            }

            string newInner = string.Join(",", parts);
            lines[lineIndex] = codeOnly.Substring(0, braceStart + 1) + newInner + codeOnly.Substring(braceEnd) + commentPart;

            return true;
        }

        // ---------------------------------------------------------------
        // Shared block/line resolution (scalar and matrix fields alike)
        // ---------------------------------------------------------------

        // Resolves a dotted path (e.g. "mission_ini.weather" or, for a matrix field,
        // its own full path) to a line range, walking one nested "key = { ... }" block
        // at a time. "" resolves to the whole file. Intermediate ranges are cached so
        // a container shared by many fields (e.g. "mission_ini") is only scanned once.
        private static bool TryResolveContainerRange(string[] lines, string containerPath, Dictionary<string, Tuple<int, int>> cache, out int start, out int end)
        {
            Tuple<int, int> cached;

            if (cache.TryGetValue(containerPath, out cached))
            {
                start = cached.Item1;
                end = cached.Item2;
                return true;
            }

            if (string.IsNullOrEmpty(containerPath))
            {
                start = 0;
                end = lines.Length - 1;
                cache[containerPath] = Tuple.Create(start, end);
                return true;
            }

            string[] segments = containerPath.Split('.');
            int rangeStart = 0;
            int rangeEnd = lines.Length - 1;
            string builtPath = "";

            foreach (string segment in segments)
            {
                builtPath = builtPath.Length == 0 ? segment : builtPath + "." + segment;

                Tuple<int, int> cachedSegment;

                if (cache.TryGetValue(builtPath, out cachedSegment))
                {
                    rangeStart = cachedSegment.Item1;
                    rangeEnd = cachedSegment.Item2;
                    continue;
                }

                int blockStart, blockEnd;

                if (!TryFindBlock(lines, segment, rangeStart, rangeEnd, out blockStart, out blockEnd))
                {
                    start = -1;
                    end = -1;
                    return false;
                }

                rangeStart = blockStart;
                rangeEnd = blockEnd;
                cache[builtPath] = Tuple.Create(rangeStart, rangeEnd);
            }

            start = rangeStart;
            end = rangeEnd;
            return true;
        }

        // Finds "key = {" within [searchStart, searchEnd] and its matching "}", following
        // brace nesting depth (not the first "}" encountered, which may belong to a
        // nested sub-table). Braces inside comments are ignored.
        private static bool TryFindBlock(string[] lines, string key, int searchStart, int searchEnd, out int blockStart, out int blockEnd)
        {
            blockStart = -1;
            blockEnd = -1;

            var openRegex = new Regex(@"^\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*{");

            for (int i = searchStart; i <= searchEnd; i++)
            {
                if (openRegex.IsMatch(ConfUiSchemaParser.StripComment(lines[i])))
                {
                    blockStart = i;
                    break;
                }
            }

            if (blockStart < 0)
                return false;

            int depth = CountBraces(ConfUiSchemaParser.StripComment(lines[blockStart]));

            for (int i = blockStart + 1; i <= searchEnd; i++)
            {
                depth += CountBraces(ConfUiSchemaParser.StripComment(lines[i]));

                if (depth <= 0)
                {
                    blockEnd = i;
                    return true;
                }
            }

            return false;
        }

        private static int CountBraces(string line)
        {
            int depth = 0;

            foreach (char c in line)
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
            }

            return depth;
        }

        // Replaces only the value token of "key = xxx" (number, true/false, or a quoted
        // string) on the matching line between startIndex and endIndex, keeping
        // indentation and the trailing comment untouched. Handles both plain keys and
        // bracket-quoted keys (["key"] = ...).
        private static bool ReplaceRawValue(string[] lines, int startIndex, int endIndex, string key, string newValueLiteral)
        {
            var regex = new Regex(
                @"^(\s*(?:\[""" + Regex.Escape(key) + @"""\]|" + Regex.Escape(key) + @")\s*=\s*)(""[^""]*""|[^\s,]+)(.*)$");

            for (int i = startIndex; i <= endIndex; i++)
            {
                Match m = regex.Match(lines[i]);

                if (m.Success)
                {
                    lines[i] = m.Groups[1].Value + newValueLiteral + m.Groups[3].Value;
                    return true;
                }
            }

            return false;
        }
    }
}