using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Loki.Data
{
    /// <summary>
    /// A dict with Python's order rules (client wave 3b, w3data): iteration in insertion order, an update keeps the key's place,
    /// a deletion frees it and a re-insertion goes to the end. Lua tables in <see cref="Lub51"/> and JSON objects use it, so the
    /// tools' dict order (and lub51.py's pairs() order) is kept.
    /// </summary>
    public sealed class OrderedMap<TKey, TValue>
    {
        private readonly Dictionary<TKey, int> _index;
        private readonly List<TKey> _keys = new List<TKey>();
        private readonly List<TValue> _values = new List<TValue>();
        private readonly List<bool> _live = new List<bool>();
        private int _count;

        public OrderedMap(IEqualityComparer<TKey> cmp = null) { _index = new Dictionary<TKey, int>(cmp ?? EqualityComparer<TKey>.Default); }

        public int Count => _count;

        public bool TryGetValue(TKey k, out TValue v)
        {
            if (_index.TryGetValue(k, out int i)) { v = _values[i]; return true; }
            v = default; return false;
        }

        public bool ContainsKey(TKey k) => _index.ContainsKey(k);

        /// <summary>d[k] = v: an existing key keeps its place (and its first key object, as Python keeps it).</summary>
        public void Set(TKey k, TValue v)
        {
            if (_index.TryGetValue(k, out int i)) { _values[i] = v; return; }
            _index[k] = _keys.Count; _keys.Add(k); _values.Add(v); _live.Add(true); _count++;
        }

        public bool Remove(TKey k)
        {
            if (!_index.TryGetValue(k, out int i)) return false;
            _index.Remove(k); _live[i] = false; _keys[i] = default; _values[i] = default; _count--;
            if (_keys.Count > 64 && _count < _keys.Count / 2) Compact();
            return true;
        }

        private void Compact()
        {
            var ks = new List<TKey>(_count); var vs = new List<TValue>(_count);
            for (int i = 0; i < _keys.Count; i++) if (_live[i]) { ks.Add(_keys[i]); vs.Add(_values[i]); }
            _keys.Clear(); _values.Clear(); _live.Clear(); _index.Clear();
            for (int i = 0; i < ks.Count; i++) { _index[ks[i]] = i; _keys.Add(ks[i]); _values.Add(vs[i]); _live.Add(true); }
        }

        /// <summary>A snapshot of the live entries in order (Python's list(d.items())).</summary>
        public List<KeyValuePair<TKey, TValue>> Items()
        {
            var o = new List<KeyValuePair<TKey, TValue>>(_count);
            for (int i = 0; i < _keys.Count; i++) if (_live[i]) o.Add(new KeyValuePair<TKey, TValue>(_keys[i], _values[i]));
            return o;
        }

        public List<TKey> Keys()
        {
            var o = new List<TKey>(_count);
            for (int i = 0; i < _keys.Count; i++) if (_live[i]) o.Add(_keys[i]);
            return o;
        }

        public TValue this[TKey k]
        {
            get { if (TryGetValue(k, out var v)) return v; throw new KeyNotFoundException(k?.ToString()); }
            set => Set(k, value);
        }
    }

    /// <summary>
    /// Python's json module for the tools' files: <see cref="Parse"/> is json.load (objects as ordered string-keyed maps, integers
    /// as long, other numbers as double), <see cref="Dump"/> is json.dump with ensure_ascii, separators and indent as the tools
    /// pass them (Lib/json/encoder.py: escaping, float repr, "\n" + indent per level, item separator "," when indent is set).
    /// </summary>
    public static class PyJson
    {
        public sealed class Obj
        {
            public readonly OrderedMap<string, object> Map = new OrderedMap<string, object>(StringComparer.Ordinal);
            public object Get(string k) => Map.TryGetValue(k, out var v) ? v : null;
        }

        // ------------------------------------------------------------------ parse
        public static object Parse(string s)
        {
            int i = 0;
            Ws(s, ref i);
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new PyError("json: extra data at " + i);
            return v;
        }

        private static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }

        private static object Value(string s, ref int i)
        {
            if (i >= s.Length) throw new PyError("json: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var o = new Obj(); i++; Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new PyError("json: key expected at " + i);
                    string k = Str(s, ref i); Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new PyError("json: ':' expected at " + i);
                    i++; Ws(s, ref i);
                    o.Map.Set(k, Value(s, ref i)); Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new PyError("json: ',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    Ws(s, ref i); l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new PyError("json: ',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            int st = i;
            if (i < s.Length && s[i] == '-') i++;
            if (i >= s.Length || s[i] < '0' || s[i] > '9') throw new PyError("json: value expected at " + st);
            if (s[i] == '0') i++; else while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            bool isFloat = false;
            if (i < s.Length && s[i] == '.') { isFloat = true; i++; while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E')) { isFloat = true; i++; if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++; while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; }
            string num = s.Substring(st, i - st);
            if (!isFloat) return long.Parse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Str(string s, ref int i)
        {
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new PyError("json: unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: throw new PyError("json: bad escape \\" + e);
                }
            }
        }

        // ------------------------------------------------------------------ dump
        public static string Dump(object v, bool ensureAscii, string itemSep, string keySep, int indent = -1)
        {
            var sb = new StringBuilder();
            Write(sb, v, ensureAscii, itemSep, keySep, indent, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v, bool ea, string isep, string ksep, int indent, int level)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(FloatStr(d)); return;
                case string s: Quote(sb, s, ea); return;
                case Obj o:
                {
                    var items = o.Map.Items();
                    if (items.Count == 0) { sb.Append("{}"); return; }
                    sb.Append('{');
                    string nl = indent >= 0 ? "\n" + new string(' ', indent * (level + 1)) : null;
                    if (nl != null) sb.Append(nl);
                    for (int k = 0; k < items.Count; k++)
                    {
                        if (k > 0) { sb.Append(isep); if (nl != null) sb.Append(nl); }
                        Quote(sb, items[k].Key, ea); sb.Append(ksep);
                        Write(sb, items[k].Value, ea, isep, ksep, indent, level + 1);
                    }
                    if (nl != null) sb.Append('\n').Append(' ', indent * level);
                    sb.Append('}');
                    return;
                }
                case System.Collections.IList list:
                {
                    if (list.Count == 0) { sb.Append("[]"); return; }
                    sb.Append('[');
                    string nl = indent >= 0 ? "\n" + new string(' ', indent * (level + 1)) : null;
                    if (nl != null) sb.Append(nl);
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (k > 0) { sb.Append(isep); if (nl != null) sb.Append(nl); }
                        Write(sb, list[k], ea, isep, ksep, indent, level + 1);
                    }
                    if (nl != null) sb.Append('\n').Append(' ', indent * level);
                    sb.Append(']');
                    return;
                }
            }
            throw new PyError("json: cannot write " + v.GetType().Name);
        }

        /// <summary>float.__repr__ with json's allow_nan spellings.</summary>
        private static string FloatStr(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            return Py.ReprFloat(d);
        }

        private static void Quote(StringBuilder sb, string s, bool ea)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); continue;
                    case '"': sb.Append("\\\""); continue;
                    case '\b': sb.Append("\\b"); continue;
                    case '\f': sb.Append("\\f"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\r': sb.Append("\\r"); continue;
                    case '\t': sb.Append("\\t"); continue;
                }
                if (c < 0x20 || (ea && c > 0x7E)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
