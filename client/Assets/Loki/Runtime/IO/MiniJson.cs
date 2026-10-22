using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Loki.IO
{
    /// <summary>
    /// A small JSON reader for the effect tables (Unity's JsonUtility can't do dictionaries): objects → Dictionary&lt;string, object&gt;,
    /// arrays → List&lt;object&gt;, numbers → double, strings, bools, null. Strict enough for machine-written files.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; var v = ParseValue(s, ref i); return v; }

        public static Dictionary<string, object> AsObject(object o) => o as Dictionary<string, object>;
        public static List<object> AsList(object o) => o as List<object>;
        public static double Num(object o, double d = 0) => o is double x ? x : o is bool b ? (b ? 1 : 0) : d;
        public static int Int(object o, int d = 0) => o is double x ? (int)Math.Round(x) : d;
        public static string Str(object o, string d = null) => o is string s ? s : o is double x ? x.ToString(CultureInfo.InvariantCulture) : d;
        public static bool Bool(object o, bool d = false) => o is bool b ? b : o is double x ? x != 0 : d;

        private static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }

        private static object ParseValue(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("json: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                i++; var d = new Dictionary<string, object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = ParseString(s, ref i);
                    Ws(s, ref i); if (s[i] != ':') throw new FormatException("json: ':' expected at " + i); i++;
                    d[k] = ParseValue(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("json: ',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                i++; var l = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ParseValue(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("json: ',' or ']' expected at " + i);
                }
            }
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            if (st == i) throw new FormatException("json: bad token at " + i);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("json: string expected at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break; case '\\': sb.Append('\\'); break; case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break; case 'n': sb.Append('\n'); break; case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("json: unterminated string");
        }
    }
}
