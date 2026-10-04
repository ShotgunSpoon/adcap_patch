using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UnityMCP
{
    /// <summary>
    /// Minimal zero-dependency JSON parser and serializer.
    /// Handles the subset needed for MCP bridge communication.
    /// </summary>
    public static class Json
    {
        #region Parse

        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int pos = 0;
            return ParseValue(json, ref pos);
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            return Parse(json) as Dictionary<string, object>;
        }

        static object ParseValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) return null;

            switch (json[pos])
            {
                case '{': return ParseObj(json, ref pos);
                case '[': return ParseArr(json, ref pos);
                case '"': return ParseStr(json, ref pos);
                case 't': pos += 4; return true;
                case 'f': pos += 5; return false;
                case 'n': pos += 4; return null;
                default: return ParseNum(json, ref pos);
            }
        }

        static Dictionary<string, object> ParseObj(string json, ref int pos)
        {
            var dict = new Dictionary<string, object>();
            pos++; // skip {
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == '}') { pos++; return dict; }

            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                string key = ParseStr(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos < json.Length) pos++; // skip :
                dict[key] = ParseValue(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] == '}') { pos++; break; }
                pos++; // skip ,
            }
            return dict;
        }

        static List<object> ParseArr(string json, ref int pos)
        {
            var list = new List<object>();
            pos++; // skip [
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ']') { pos++; return list; }

            while (pos < json.Length)
            {
                list.Add(ParseValue(json, ref pos));
                SkipWhitespace(json, ref pos);
                if (pos >= json.Length || json[pos] == ']') { pos++; break; }
                pos++; // skip ,
            }
            return list;
        }

        static string ParseStr(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') return "";
            pos++; // skip opening "
            var sb = new StringBuilder();
            while (pos < json.Length && json[pos] != '"')
            {
                if (json[pos] == '\\')
                {
                    pos++;
                    if (pos >= json.Length) break;
                    switch (json[pos])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (pos + 4 < json.Length)
                            {
                                string hex = json.Substring(pos + 1, 4);
                                sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                                pos += 4;
                            }
                            break;
                        default: sb.Append(json[pos]); break;
                    }
                }
                else
                {
                    sb.Append(json[pos]);
                }
                pos++;
            }
            if (pos < json.Length) pos++; // skip closing "
            return sb.ToString();
        }

        static object ParseNum(string json, ref int pos)
        {
            int start = pos;
            if (pos < json.Length && json[pos] == '-') pos++;
            while (pos < json.Length && json[pos] >= '0' && json[pos] <= '9') pos++;

            bool isFloat = false;
            if (pos < json.Length && json[pos] == '.')
            {
                isFloat = true;
                pos++;
                while (pos < json.Length && json[pos] >= '0' && json[pos] <= '9') pos++;
            }
            if (pos < json.Length && (json[pos] == 'e' || json[pos] == 'E'))
            {
                isFloat = true;
                pos++;
                if (pos < json.Length && (json[pos] == '+' || json[pos] == '-')) pos++;
                while (pos < json.Length && json[pos] >= '0' && json[pos] <= '9') pos++;
            }

            string numStr = json.Substring(start, pos - start);
            if (isFloat)
                return double.Parse(numStr, CultureInfo.InvariantCulture);

            if (long.TryParse(numStr, out long l))
                return l <= int.MaxValue && l >= int.MinValue ? (object)(int)l : (object)l;

            return double.Parse(numStr, CultureInfo.InvariantCulture);
        }

        static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') break;
                pos++;
            }
        }

        #endregion

        #region Serialize

        public static string Serialize(object obj)
        {
            var sb = new StringBuilder();
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        static void SerializeValue(object obj, StringBuilder sb)
        {
            if (obj == null) { sb.Append("null"); return; }
            if (obj is string s) { SerializeString(s, sb); return; }
            if (obj is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (obj is int i) { sb.Append(i); return; }
            if (obj is long l) { sb.Append(l); return; }
            if (obj is float f) { sb.Append(f.ToString(CultureInfo.InvariantCulture)); return; }
            if (obj is double d) { sb.Append(d.ToString(CultureInfo.InvariantCulture)); return; }
            if (obj is byte[] bytes) { SerializeString(Convert.ToBase64String(bytes), sb); return; }
            if (obj is Dictionary<string, object> dict) { SerializeDict(dict, sb); return; }
            if (obj is List<object> list) { SerializeList(list, sb); return; }

            if (obj is System.Collections.IDictionary idict)
            {
                SerializeGenericDict(idict, sb);
                return;
            }

            if (obj is System.Collections.IEnumerable enumerable)
            {
                SerializeGenericEnumerable(enumerable, sb);
                return;
            }

            SerializeString(obj.ToString(), sb);
        }

        static void SerializeGenericDict(System.Collections.IDictionary dict, StringBuilder sb)
        {
            sb.Append('{');
            bool first = true;
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                SerializeString(entry.Key?.ToString() ?? "", sb);
                sb.Append(':');
                SerializeValue(entry.Value, sb);
                first = false;
            }
            sb.Append('}');
        }

        static void SerializeGenericEnumerable(System.Collections.IEnumerable enumerable, StringBuilder sb)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first) sb.Append(',');
                SerializeValue(item, sb);
                first = false;
            }
            sb.Append(']');
        }

        static void SerializeString(string s, StringBuilder sb)
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
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        static void SerializeDict(Dictionary<string, object> dict, StringBuilder sb)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(',');
                SerializeString(kvp.Key, sb);
                sb.Append(':');
                SerializeValue(kvp.Value, sb);
                first = false;
            }
            sb.Append('}');
        }

        static void SerializeList(List<object> list, StringBuilder sb)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeValue(list[i], sb);
            }
            sb.Append(']');
        }

        #endregion

        #region Helpers

        public static string Response(string id, object data)
        {
            return Serialize(new Dictionary<string, object>
            {
                ["id"] = id,
                ["success"] = true,
                ["data"] = data
            });
        }

        public static string Error(string id, string message)
        {
            return Serialize(new Dictionary<string, object>
            {
                ["id"] = id,
                ["success"] = false,
                ["error"] = message
            });
        }

        public static string GetString(Dictionary<string, object> dict, string key, string defaultVal = "")
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val is string s) return s;
            return defaultVal;
        }

        public static int GetInt(Dictionary<string, object> dict, string key, int defaultVal = 0)
        {
            if (dict == null || !dict.TryGetValue(key, out var val)) return defaultVal;
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val is double d) return (int)d;
            return defaultVal;
        }

        public static bool GetBool(Dictionary<string, object> dict, string key, bool defaultVal = false)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val is bool b) return b;
            return defaultVal;
        }

        public static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var val) && val is Dictionary<string, object> d) return d;
            return null;
        }

        #endregion
    }
}
