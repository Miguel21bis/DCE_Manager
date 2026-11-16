using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DCE_Manager
{
    // Base aérienne capturée : quand une zone wargame change de camp, les bases du perdant situées
    // dans cette zone sont désactivées, ainsi que TOUS les squadrons qui y sont basés.
    // Définitif (comme les unités au sol de l'ancien DCE) : une reprise de la zone ne les réactive pas.
    //
    // Écrit uniquement dans Active\db_airbases.lua ("inactive") et Active\oob_air.lua ("inactive" par squad),
    // en texte brut : on ne régénère pas les fichiers (aucun risque de perdre un champ).
    // Appelé par WargameOldDce.DestroyInCapturedZones, donc identique pour un vrai tour et pour l'export de la Preview.
    //
    // Cas particuliers :
    //   - un squad joueur est basé là : la base est laissée active (rapport), à changer à la main
    //   - base à (0,0) (porte-avions...) ou sans camp : ignorée
    internal static class WargameBaseCapture
    {
        public static List<string> Deactivate(string campaignFolder, Dictionary<WargameZoneData, string> captured)
        {
            var report = new List<string>();
            if (captured == null || captured.Count == 0) return report;

            try
            {
                string airPath = Path.Combine(campaignFolder, "Active", "db_airbases.lua");
                string oobPath = Path.Combine(campaignFolder, "Active", "oob_air.lua");
                if (!File.Exists(airPath) || !File.Exists(oobPath))
                {
                    report.Add("air bases NOT checked : Active\\db_airbases.lua or oob_air.lua not found");
                    return report;
                }

                string airText = File.ReadAllText(airPath);
                string oobText = File.ReadAllText(oobPath);
                LuaText air = new LuaText(airText);
                LuaText oob = new LuaText(oobText);

                List<WargameZoneData> capturedZones = captured.Keys.ToList();

                // 1. Bases du perdant, dans une zone capturée, encore actives
                var toClose = new Dictionary<string, LuaText.Block>(StringComparer.OrdinalIgnoreCase);
                var zoneOfBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (LuaText.Block b in air.Blocks.Where(x => x.DepthBefore == 1))
                {
                    string name = air.KeyOf(b);
                    if (string.IsNullOrEmpty(name)) continue;

                    string side = air.StringField(b, "side");
                    double x, y;
                    if (side == null || !air.NumberField(b, "x", out x) || !air.NumberField(b, "y", out y)) continue;
                    if (Math.Abs(x) < 1 && Math.Abs(y) < 1) continue;   // porte-avions et autres bases sans position

                    bool inactive;
                    if (air.BoolField(b, "inactive", out inactive) && inactive) continue;

                    WargameZoneData zone = WargameEngineCombat.FindZoneContaining(capturedZones, new PointF((float)x, (float)y));
                    if (zone == null) continue;

                    string loser = captured[zone];
                    if (!string.Equals(side, loser, StringComparison.OrdinalIgnoreCase)) continue;

                    toClose[name] = b;
                    zoneOfBase[name] = zone.Id;
                }

                if (toClose.Count == 0)
                {
                    report.Add("air bases : none to deactivate in the captured zones");
                    return report;
                }

                // 2. Squads basés sur ces bases (depth 2 = [camp] -> [n] = { squad })
                var squadsOfBase = new Dictionary<string, List<LuaText.Block>>(StringComparer.OrdinalIgnoreCase);
                var playerBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (LuaText.Block b in oob.Blocks.Where(x => x.DepthBefore == 2))
                {
                    string baseName = oob.StringField(b, "base");
                    if (baseName == null || !toClose.ContainsKey(baseName)) continue;

                    bool player;
                    if (oob.BoolField(b, "player", out player) && player)
                    {
                        playerBases.Add(baseName);
                        continue;
                    }

                    List<LuaText.Block> list;
                    if (!squadsOfBase.TryGetValue(baseName, out list))
                        squadsOfBase[baseName] = list = new List<LuaText.Block>();
                    list.Add(b);
                }

                // 3. Modifications
                var airEdits = new List<LuaText.Edit>();
                var oobEdits = new List<LuaText.Edit>();
                int basesDone = 0, squadsDone = 0;

                foreach (KeyValuePair<string, LuaText.Block> kv in toClose)
                {
                    string baseName = kv.Key;

                    if (playerBases.Contains(baseName))
                    {
                        report.Add("air base '" + baseName + "' (" + zoneOfBase[baseName] + ") captured but KEPT ACTIVE : the player squad is based there, move it to another base");
                        continue;
                    }

                    air.SetInactive(kv.Value, airEdits);
                    basesDone++;

                    int n = 0;
                    List<LuaText.Block> squads;
                    if (squadsOfBase.TryGetValue(baseName, out squads))
                    {
                        foreach (LuaText.Block s in squads)
                        {
                            bool already;
                            if (oob.BoolField(s, "inactive", out already) && already) continue;
                            oob.SetInactive(s, oobEdits);
                            n++;
                        }
                    }
                    squadsDone += n;
                    report.Add("air base '" + baseName + "' (" + zoneOfBase[baseName] + ") captured : deactivated for good with " + n + " squad(s)");
                }

                if (basesDone == 0) return report;

                // 4. Écriture (sans BOM, fins de ligne conservées)
                File.WriteAllText(airPath, air.Apply(airEdits), new UTF8Encoding(false));
                File.WriteAllText(oobPath, oob.Apply(oobEdits), new UTF8Encoding(false));

                report.Add("air bases : " + basesDone + " base(s) and " + squadsDone + " squad(s) deactivated for good");
            }
            catch (Exception ex)
            {
                report.Add("air bases NOT deactivated (" + ex.Message + ")");
                WargameLog.Line("WargameBaseCapture | désactivation des bases impossible : " + ex);
            }

            return report;
        }

        // Usure aérienne simulée par la Preview : chaque squad non joueur et actif d'un camp garde
        // "survie" (0 à 1) de ses appareils prêts ; la différence passe dans roster.lost.
        // Analyse légère d'un texte Lua : où sont les tables { } et leur profondeur, en ignorant
        // les accolades dans les chaînes et les commentaires. Permet de lire / modifier un champ
        // du niveau direct d'une table sans toucher aux tables imbriquées.
        internal sealed class LuaText
        {
            internal sealed class Block
            {
                public int Open;         // position du '{'
                public int Close;        // position du '}'
                public int DepthBefore;  // nombre de '{' ouverts avant celui-ci
            }

            internal struct Edit
            {
                public int Pos;
                public int Length;
                public string Text;
            }

            private readonly string _text;
            private readonly int[] _level;     // niveau d'imbrication de chaque caractère
            private readonly bool[] _ignored;  // dans une chaîne ou un commentaire
            public readonly List<Block> Blocks = new List<Block>();
            private readonly string _eol;

            public LuaText(string text)
            {
                _text = text;
                _level = new int[text.Length];
                _ignored = new bool[text.Length];
                _eol = text.Contains("\r\n") ? "\r\n" : "\n";

                var stack = new Stack<Block>();
                int i = 0;
                while (i < text.Length)
                {
                    char c = text[i];
                    _level[i] = stack.Count;

                    if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
                    {
                        int end;
                        if (i + 3 < text.Length && text[i + 2] == '[' && text[i + 3] == '[')
                        {
                            end = text.IndexOf("]]", i + 4, StringComparison.Ordinal);
                            end = end < 0 ? text.Length : end + 2;
                        }
                        else
                        {
                            end = text.IndexOf('\n', i);
                            if (end < 0) end = text.Length;
                        }
                        Ignore(i, end, stack.Count);
                        i = end;
                        continue;
                    }

                    if (c == '"' || c == '\'')
                    {
                        int j = i + 1;
                        while (j < text.Length && text[j] != c && text[j] != '\n')
                        {
                            if (text[j] == '\\') j++;
                            j++;
                        }
                        int end = Math.Min(text.Length, j + 1);
                        Ignore(i, end, stack.Count);
                        i = end;
                        continue;
                    }

                    if (c == '{')
                    {
                        var b = new Block { Open = i, DepthBefore = stack.Count };
                        Blocks.Add(b);
                        stack.Push(b);
                    }
                    else if (c == '}' && stack.Count > 0)
                    {
                        Block b = stack.Pop();
                        b.Close = i;
                        _level[i] = stack.Count;
                    }
                    i++;
                }

                // Tables jamais fermées (fichier abîmé) : on les écarte
                Blocks.RemoveAll(b => b.Close <= b.Open);
            }

            private void Ignore(int from, int to, int level)
            {
                for (int k = from; k < to && k < _text.Length; k++)
                {
                    _ignored[k] = true;
                    _level[k] = level;
                }
            }

            // Nom de la clé qui précède la table : ["Nellis AFB"] = { ou Nellis = {
            public string KeyOf(Block b)
            {
                int start = Math.Max(0, b.Open - 200);
                string before = _text.Substring(start, b.Open - start);
                Match m = Regex.Match(before, @"(?:\[\s*(?:""((?:[^""\\]|\\.)*)""|'((?:[^'\\]|\\.)*)')\s*\]|([A-Za-z_]\w*))\s*=\s*$");
                if (!m.Success) return null;
                if (m.Groups[1].Success) return m.Groups[1].Value;
                return m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            }

            // Premier match de la regex située au niveau DIRECT de la table (pas dans une sous-table)
            private Match DirectMatch(Block b, string pattern)
            {
                int innerStart = b.Open + 1;
                string inner = _text.Substring(innerStart, b.Close - innerStart);
                foreach (Match m in Regex.Matches(inner, pattern))
                {
                    int abs = innerStart + m.Index;
                    if (!_ignored[abs] && _level[abs] == b.DepthBefore + 1)
                        return m;
                }
                return null;
            }

            private static string Key(string name)
            {
                return @"(?:\[\s*[""']" + name + @"[""']\s*\]|\b" + name + @")\s*=\s*";
            }

            public string StringField(Block b, string name)
            {
                // guillemets doubles ou simples (l'Init écrit souvent side = 'blue')
                Match m = DirectMatch(b, Key(name) + @"(?:""((?:[^""\\]|\\.)*)""|'((?:[^'\\]|\\.)*)')");
                if (m == null) return null;
                return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            }

            public bool NumberField(Block b, string name, out double value)
            {
                value = 0;
                Match m = DirectMatch(b, Key(name) + @"(-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)");
                return m != null && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            public bool BoolField(Block b, string name, out bool value)
            {
                value = false;
                Match m = DirectMatch(b, Key(name) + @"(true|false)");
                if (m == null) return false;
                value = m.Groups[1].Value == "true";
                return true;
            }

            // Sous-table directe d'un bloc (ex. roster dans un squad)
            public Block Child(Block parent, string key)
            {
                foreach (Block c in Blocks)
                {
                    if (c.DepthBefore != parent.DepthBefore + 1 || c.Open < parent.Open || c.Close > parent.Close) continue;
                    if (string.Equals(KeyOf(c), key, StringComparison.Ordinal)) return c;
                }
                return null;
            }

            // Remplace la valeur d'un nombre existant (ne crée rien)
            public void SetNumber(Block b, string name, double value, List<Edit> edits)
            {
                Match m = DirectMatch(b, Key(name) + @"(-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)");
                if (m == null) return;

                Group g = m.Groups[1];
                edits.Add(new Edit { Pos = b.Open + 1 + g.Index, Length = g.Length, Text = value.ToString("0.##", CultureInfo.InvariantCulture) });
            }

            // inactive = true : remplace la valeur existante, sinon ajoute la ligne juste après le '{'
            public void SetInactive(Block b, List<Edit> edits)
            {
                Match m = DirectMatch(b, Key("inactive") + @"(true|false)");
                if (m != null)
                {
                    Group g = m.Groups[1];
                    edits.Add(new Edit { Pos = b.Open + 1 + g.Index, Length = g.Length, Text = "true" });
                    return;
                }

                edits.Add(new Edit
                {
                    Pos = b.Open + 1,
                    Length = 0,
                    Text = _eol + new string('\t', b.DepthBefore + 1) + "[\"inactive\"] = true,"
                });
            }

            // Numéro de la clé qui précède la table : [3] = {  (sinon -1)
            public int NumberKeyOf(Block b)
            {
                int start = Math.Max(0, b.Open - 60);
                string before = _text.Substring(start, b.Open - start);
                Match m = Regex.Match(before, @"\[\s*(\d+)\s*\]\s*=\s*$");
                return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            }

            // Booléen : remplace la valeur existante ; sinon ajoute la ligne juste après le '{' (seulement si addIfAbsent)
            public void SetBool(Block b, string name, bool value, List<Edit> edits, bool addIfAbsent)
            {
                Match m = DirectMatch(b, Key(name) + @"(true|false)");
                if (m != null)
                {
                    Group g = m.Groups[1];
                    string old = g.Value;
                    string now = value ? "true" : "false";
                    if (old != now) edits.Add(new Edit { Pos = b.Open + 1 + g.Index, Length = g.Length, Text = now });
                    return;
                }

                if (!addIfAbsent) return;
                edits.Add(new Edit
                {
                    Pos = b.Open + 1,
                    Length = 0,
                    Text = _eol + new string('\t', b.DepthBefore + 1) + "[\"" + name + "\"] = " + (value ? "true" : "false") + ","
                });
            }

            // Texte : remplace la valeur existante (ne crée rien)
            public void SetString(Block b, string name, string value, List<Edit> edits)
            {
                Match m = DirectMatch(b, Key(name) + @"""((?:[^""\\]|\\.)*)""");
                if (m == null) return;

                Group g = m.Groups[1];
                string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                if (g.Value == escaped) return;
                edits.Add(new Edit { Pos = b.Open + 1 + g.Index, Length = g.Length, Text = escaped });
            }

            // Nombre : remplace la valeur existante, sinon ajoute la ligne juste après le '{'
            public void SetNumberOrAdd(Block b, string name, double value, List<Edit> edits)
            {
                Match m = DirectMatch(b, Key(name) + @"(-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)");
                string text = value.ToString("0.##", CultureInfo.InvariantCulture);
                if (m != null)
                {
                    Group g = m.Groups[1];
                    edits.Add(new Edit { Pos = b.Open + 1 + g.Index, Length = g.Length, Text = text });
                    return;
                }

                edits.Add(new Edit
                {
                    Pos = b.Open + 1,
                    Length = 0,
                    Text = _eol + new string('\t', b.DepthBefore + 1) + "[\"" + name + "\"] = " + text + ","
                });
            }

            // Remplace tout l'intérieur d'une table par une liste de champs "nom = valeur" (déjà écrits en Lua : 1 ligne par champ)
            public void ReplaceFields(Block b, List<string> fields, List<Edit> edits)
            {
                var sb = new StringBuilder();
                string tabs = new string('\t', b.DepthBefore + 1);
                foreach (string f in fields) sb.Append(_eol).Append(tabs).Append(f).Append(',');
                sb.Append(_eol).Append(new string('\t', b.DepthBefore));

                edits.Add(new Edit { Pos = b.Open + 1, Length = b.Close - b.Open - 1, Text = sb.ToString() });
            }

            public string Apply(List<Edit> edits)
            {
                var sb = new StringBuilder(_text);
                foreach (Edit e in edits.OrderByDescending(x => x.Pos))
                {
                    sb.Remove(e.Pos, e.Length);
                    sb.Insert(e.Pos, e.Text);
                }
                return sb.ToString();
            }
        }
    }
}
