using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DemoRestaurant.Logic.Online
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A small JSON document model for the v2 API. JsonUtility cannot read maps (<c>recipes</c>, <c>buy</c>,
    /// <c>inventory</c>) or the ledger's per-type payloads, so the online layer parses and writes JSON with this.
    /// Objects keep their key order. Reading a missing key or the wrong kind returns <see cref="Missing"/> (a null),
    /// so lookups chain without checks: <c>state["layout"]["room"]["width"].AsInt()</c>.
    /// </summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Missing = new JsonValue(JsonKind.Null);

        private readonly List<JsonValue> _items;
        private readonly List<KeyValuePair<string, JsonValue>> _fields;
        private readonly string _string;
        private readonly double _number;
        private readonly bool _bool;

        public JsonKind Kind { get; }

        private JsonValue(JsonKind kind)
        {
            Kind = kind;
            if (kind == JsonKind.Array) _items = new List<JsonValue>();
            if (kind == JsonKind.Object) _fields = new List<KeyValuePair<string, JsonValue>>();
        }

        private JsonValue(string s) : this(JsonKind.String) => _string = s;
        private JsonValue(double n) : this(JsonKind.Number) => _number = n;
        private JsonValue(bool b) : this(JsonKind.Bool) => _bool = b;

        public static JsonValue Null() => new JsonValue(JsonKind.Null);
        public static JsonValue Object() => new JsonValue(JsonKind.Object);
        public static JsonValue Array() => new JsonValue(JsonKind.Array);
        public static JsonValue Of(string s) => s == null ? Null() : new JsonValue(s);
        public static JsonValue Of(long n) => new JsonValue((double)n);
        public static JsonValue Of(double n) => new JsonValue(n);
        public static JsonValue Of(bool b) => new JsonValue(b);

        public bool IsNull => Kind == JsonKind.Null;
        public int Count => _items?.Count ?? _fields?.Count ?? 0;
        public IReadOnlyList<JsonValue> Items => _items ?? (IReadOnlyList<JsonValue>)System.Array.Empty<JsonValue>();
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Fields =>
            _fields ?? (IReadOnlyList<KeyValuePair<string, JsonValue>>)System.Array.Empty<KeyValuePair<string, JsonValue>>();

        public JsonValue this[string key]
        {
            get
            {
                if (_fields != null)
                    foreach (var f in _fields)
                        if (f.Key == key) return f.Value;
                return Missing;
            }
        }

        public JsonValue this[int index] => _items != null && index >= 0 && index < _items.Count ? _items[index] : Missing;

        public bool Has(string key)
        {
            if (_fields == null) return false;
            foreach (var f in _fields)
                if (f.Key == key) return true;
            return false;
        }

        /// <summary>Sets (or replaces) a field and returns this object, for building bodies fluently.</summary>
        public JsonValue Set(string key, JsonValue value)
        {
            if (_fields == null) throw new InvalidOperationException("Not a JSON object");
            value ??= Null();
            for (int i = 0; i < _fields.Count; i++)
                if (_fields[i].Key == key) { _fields[i] = new KeyValuePair<string, JsonValue>(key, value); return this; }
            _fields.Add(new KeyValuePair<string, JsonValue>(key, value));
            return this;
        }

        public JsonValue Set(string key, string value) => Set(key, Of(value));
        public JsonValue Set(string key, long value) => Set(key, Of(value));
        public JsonValue Set(string key, bool value) => Set(key, Of(value));

        public JsonValue Remove(string key)
        {
            _fields?.RemoveAll(f => f.Key == key);
            return this;
        }

        public JsonValue Add(JsonValue value)
        {
            if (_items == null) throw new InvalidOperationException("Not a JSON array");
            _items.Add(value ?? Null());
            return this;
        }

        public string AsString(string fallback = null) => Kind == JsonKind.String ? _string : fallback;
        public double AsDouble(double fallback = 0) => Kind == JsonKind.Number ? _number : fallback;
        public long AsLong(long fallback = 0) => Kind == JsonKind.Number ? (long)Math.Round(_number) : fallback;
        public int AsInt(int fallback = 0) => Kind == JsonKind.Number ? (int)Math.Round(_number) : fallback;
        public bool AsBool(bool fallback = false) => Kind == JsonKind.Bool ? _bool : fallback;

        /// <summary>An object of numbers as a dictionary (e.g. <c>{"rice": 2}</c>); anything else is empty.</summary>
        public Dictionary<string, int> AsCounts()
        {
            var result = new Dictionary<string, int>();
            foreach (var f in Fields)
                if (f.Value.Kind == JsonKind.Number) result[f.Key] = f.Value.AsInt();
            return result;
        }

        public static JsonValue FromCounts(IEnumerable<KeyValuePair<string, int>> counts)
        {
            var o = Object();
            var sorted = new List<KeyValuePair<string, int>>(counts ?? System.Array.Empty<KeyValuePair<string, int>>());
            sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            foreach (var pair in sorted)
                if (pair.Value > 0) o.Set(pair.Key, pair.Value);
            return o;
        }

        // ---------------------------------------------------------------- writing

        public override string ToString()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        private void Write(StringBuilder sb)
        {
            switch (Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(_bool ? "true" : "false"); break;
                case JsonKind.Number: WriteNumber(sb, _number); break;
                case JsonKind.String: WriteString(sb, _string); break;
                case JsonKind.Array:
                    sb.Append('[');
                    for (int i = 0; i < _items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        _items[i].Write(sb);
                    }
                    sb.Append(']');
                    break;
                default:
                    sb.Append('{');
                    for (int i = 0; i < _fields.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteString(sb, _fields[i].Key);
                        sb.Append(':');
                        _fields[i].Value.Write(sb);
                    }
                    sb.Append('}');
                    break;
            }
        }

        private static void WriteNumber(StringBuilder sb, double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) { sb.Append("null"); return; }
            if (n == Math.Floor(n) && Math.Abs(n) < 9e15) sb.Append(((long)n).ToString(CultureInfo.InvariantCulture));
            else sb.Append(n.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------------- reading

        /// <summary>Parses a JSON text. Throws <see cref="FormatException"/> on malformed input.</summary>
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new FormatException("No JSON");
            int i = 0;
            var value = ReadValue(text, ref i, 0);
            SkipSpace(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after JSON at " + i);
            return value;
        }

        /// <summary>Like <see cref="Parse"/> but returns false instead of throwing.</summary>
        public static bool TryParse(string text, out JsonValue value)
        {
            try
            {
                value = Parse(text);
                return true;
            }
            catch (FormatException)
            {
                value = Missing;
                return false;
            }
        }

        private const int MaxDepth = 64;

        private static JsonValue ReadValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("JSON nested too deeply");
            SkipSpace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{':
                {
                    i++;
                    var o = Object();
                    SkipSpace(s, ref i);
                    if (Peek(s, i) == '}') { i++; return o; }
                    while (true)
                    {
                        SkipSpace(s, ref i);
                        if (Peek(s, i) != '"') throw new FormatException("Expected a key at " + i);
                        string key = ReadString(s, ref i);
                        SkipSpace(s, ref i);
                        Expect(s, ref i, ':');
                        o.Set(key, ReadValue(s, ref i, depth + 1));
                        SkipSpace(s, ref i);
                        if (Peek(s, i) == ',') { i++; continue; }
                        Expect(s, ref i, '}');
                        return o;
                    }
                }
                case '[':
                {
                    i++;
                    var a = Array();
                    SkipSpace(s, ref i);
                    if (Peek(s, i) == ']') { i++; return a; }
                    while (true)
                    {
                        a.Add(ReadValue(s, ref i, depth + 1));
                        SkipSpace(s, ref i);
                        if (Peek(s, i) == ',') { i++; continue; }
                        Expect(s, ref i, ']');
                        return a;
                    }
                }
                case '"': return new JsonValue(ReadString(s, ref i));
                case 't': ReadWord(s, ref i, "true"); return Of(true);
                case 'f': ReadWord(s, ref i, "false"); return Of(false);
                case 'n': ReadWord(s, ref i, "null"); return Null();
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return new JsonValue(ReadNumber(s, ref i));
                    throw new FormatException($"Unexpected '{c}' at {i}");
            }
        }

        private static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';

        private static void Expect(string s, ref int i, char c)
        {
            if (Peek(s, i) != c) throw new FormatException($"Expected '{c}' at {i}");
            i++;
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static void ReadWord(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("Bad literal at " + i);
            i += word.Length;
        }

        private static double ReadNumber(string s, ref int i)
        {
            int start = i;
            if (Peek(s, i) == '-') i++;
            while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                throw new FormatException("Bad number at " + start);
            return n;
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("Unterminated escape");
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
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw new FormatException("Bad \\u escape at " + i);
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape at " + i);
                }
            }
        }
    }
}
