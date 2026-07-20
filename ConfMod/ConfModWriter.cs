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
    // touch in C#. Everything outside the tagged fields (comments, campMod,
    // pictureBrief, formatting) is preserved byte-for-byte.
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

        // Resolves a dotted container path (e.g. "mission_ini.weather") to a line range,
        // walking one nested "key = { ... }" block at a time. "" resolves to the whole
        // file. Intermediate ranges are cached so a container shared by many fields
        // (e.g. "mission_ini") is only scanned for once.
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
        // indentation and the trailing comment (including the @ui tag) untouched.
        // Handles both plain keys and bracket-quoted keys (["key"] = ...).
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
