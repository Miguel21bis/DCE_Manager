using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using DCE_Manager.Parameters;
using DCE_Manager.Utils;

namespace DCE_Manager
{
    // Partie de Map_Drawings (voir Map_Drawings.cs) : petits outils de lecture et lecteur Lua
    internal static partial class Map_Drawings
    {
        private static bool IsVisible(Dictionary<string, object> d)
        {
            object v;
            return !d.TryGetValue("visible", out v) || !(v is bool) || (bool)v;
        }

        private static Dictionary<string, object> Sub(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
            if (v is bool) return (bool)v ? "true" : "false";
            return System.Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v is double) return (double)v;
            return 0;
        }

        // "0xRRGGBBAA" -> Color
        private static Color Col(string s, Color fallback)
        {
            if (s == null || s.Length != 10 || !s.StartsWith("0x")) return fallback;
            try
            {
                int r = System.Convert.ToInt32(s.Substring(2, 2), 16);
                int g = System.Convert.ToInt32(s.Substring(4, 2), 16);
                int b = System.Convert.ToInt32(s.Substring(6, 2), 16);
                int a = System.Convert.ToInt32(s.Substring(8, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }
            catch { return fallback; }
        }

        // Petit lecteur de table Lua : tables, textes, nombres, true/false. Les clés deviennent des textes.
        // Il avance TOUJOURS d'au moins un caractère par tour : un caractère inattendu ne peut plus le bloquer
        // (avant, un "]" ou un texte [[...]] isolé le faisait tourner à l'infini jusqu'à saturer la mémoire).
        private class LuaReader
        {
            private readonly string _s;
            private int _i;
            private readonly Dictionary<object, Dictionary<string, int[]>> _spans;     // (facultatif) pour chaque table : début / fin de la valeur de chaque clé dans le texte

            public LuaReader(string s, int start, Dictionary<object, Dictionary<string, int[]>> spans = null) { _s = s; _i = start; _spans = spans; }

            private void Skip()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (char.IsWhiteSpace(c)) { _i++; continue; }
                    if (c == '-' && _i + 1 < _s.Length && _s[_i + 1] == '-')
                    {
                        while (_i < _s.Length && _s[_i] != '\n') _i++;
                        continue;
                    }
                    break;
                }
            }

            // "[[" ou "[=[" : début d'un texte long Lua (et non d'une clé)
            private bool IsLongString()
            {
                return _s[_i] == '[' && _i + 1 < _s.Length && (_s[_i + 1] == '[' || _s[_i + 1] == '=');
            }

            public object ReadValue()
            {
                Skip();
                if (_i >= _s.Length) return null;

                char c = _s[_i];
                if (c == '{') return ReadTable();
                if (c == '"' || c == '\'') return ReadString(c);
                if (IsLongString()) return ReadLongString();

                int start = _i;
                while (_i < _s.Length && ",;}]\r\n \t".IndexOf(_s[_i]) < 0) _i++;
                if (_i == start) { _i++; return null; }       // caractère inattendu : on le saute

                string w = _s.Substring(start, _i - start);
                if (w == "true") return true;
                if (w == "false") return false;
                double d;
                if (double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                return null;
            }

            private string ReadString(char quote)
            {
                var sb = new StringBuilder();
                _i++;       // guillemet ouvrant
                while (_i < _s.Length && _s[_i] != quote)
                {
                    if (_s[_i] == '\\' && _i + 1 < _s.Length) _i++;
                    sb.Append(_s[_i]);
                    _i++;
                }
                _i++;       // guillemet fermant
                return sb.ToString();
            }

            // [[texte]] ou [==[texte]==]
            private string ReadLongString()
            {
                int j = _i + 1, eq = 0;
                while (j < _s.Length && _s[j] == '=') { eq++; j++; }
                if (j >= _s.Length || _s[j] != '[') { _i++; return ""; }

                string close = "]" + new string('=', eq) + "]";
                int end = _s.IndexOf(close, j + 1, StringComparison.Ordinal);
                if (end < 0) { _i = _s.Length; return ""; }

                string text = _s.Substring(j + 1, end - j - 1);
                _i = end + close.Length;
                return text;
            }

            private Dictionary<string, object> ReadTable()
            {
                var t = new Dictionary<string, object>();
                _i++;       // {
                if (_spans != null) _spans[t] = new Dictionary<string, int[]> { { "{", new[] { _i, _i } } };      // où insérer une clé qui n'existe pas
                int auto = 1;
                while (true)
                {
                    Skip();
                    if (_i >= _s.Length) return t;
                    if (_s[_i] == '}') { _i++; return t; }
                    if (_s[_i] == ',' || _s[_i] == ';') { _i++; continue; }

                    int before = _i;
                    string key;
                    if (_s[_i] == '[' && !IsLongString())
                    {
                        _i++;
                        Skip();
                        object k = ReadValue();
                        key = k is double ? ((double)k).ToString(CultureInfo.InvariantCulture) : (k as string ?? "");
                        Skip();
                        if (_i < _s.Length && _s[_i] == ']') _i++;       // ]
                        Skip();
                        if (_i < _s.Length && _s[_i] == '=') _i++;       // =
                    }
                    else
                    {
                        // clé sans crochets (nom = valeur) ou valeur seule
                        int save = _i;
                        while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                        string name = _s.Substring(save, _i - save);
                        Skip();
                        if (name != "" && _i < _s.Length && _s[_i] == '=') { _i++; key = name; }
                        else { _i = save; key = (auto++).ToString(); }
                    }
                    Skip();
                    int valueStart = _i;
                    t[key] = ReadValue();
                    if (_spans != null)
                    {
                        Dictionary<string, int[]> keySpans;
                        if (!_spans.TryGetValue(t, out keySpans)) { keySpans = new Dictionary<string, int[]>(); _spans[t] = keySpans; }
                        keySpans[key] = new[] { valueStart, _i };
                    }

                    if (_i <= before) _i = before + 1;      // garde-fou : rien n'a avancé, on force
                }
            }
        }
    }
}
