using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WukongGuard.Core
{
    // Only the JSON features needed by rules.json. Rejects malformed and duplicate keys.
    internal sealed class StrictJson
    {
        private readonly string input;
        private int offset;

        private StrictJson(string input) { this.input = input; }

        internal static object Parse(string input)
        {
            if (input == null || input.Length > 65536) throw new FormatException("JSON size limit exceeded");
            var parser = new StrictJson(input);
            object value = parser.Value(0);
            parser.White();
            if (parser.offset != input.Length) throw new FormatException("Trailing JSON data");
            return value;
        }

        private object Value(int depth)
        {
            if (depth > 12) throw new FormatException("JSON nesting limit exceeded");
            White();
            if (offset >= input.Length) throw new FormatException("Unexpected end of JSON");
            char c = input[offset];
            if (c == '{') return Object(depth + 1);
            if (c == '[') return Array(depth + 1);
            if (c == '"') return String();
            if (c == '-' || c >= '0' && c <= '9') return Number();
            if (Take("true")) return true;
            if (Take("false")) return false;
            if (Take("null")) return null;
            throw new FormatException("Invalid JSON value at " + offset);
        }

        private Dictionary<string, object> Object(int depth)
        {
            Expect('{');
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            White();
            if (Peek('}')) { offset++; return result; }
            while (true)
            {
                White();
                string key = String();
                White();
                Expect(':');
                if (result.ContainsKey(key)) throw new FormatException("Duplicate JSON key: " + key);
                result.Add(key, Value(depth));
                White();
                if (Peek('}')) { offset++; return result; }
                Expect(',');
            }
        }

        private List<object> Array(int depth)
        {
            Expect('[');
            var result = new List<object>();
            White();
            if (Peek(']')) { offset++; return result; }
            while (true)
            {
                result.Add(Value(depth));
                White();
                if (Peek(']')) { offset++; return result; }
                Expect(',');
            }
        }

        private string String()
        {
            Expect('"');
            var result = new StringBuilder();
            while (offset < input.Length)
            {
                char c = input[offset++];
                if (c == '"') return result.ToString();
                if (c < 32) throw new FormatException("Control character in JSON string");
                if (c != '\\') { result.Append(c); continue; }
                if (offset >= input.Length) throw new FormatException("Incomplete JSON escape");
                c = input[offset++];
                switch (c)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (offset + 4 > input.Length) throw new FormatException("Incomplete Unicode escape");
                        if (!ushort.TryParse(input.Substring(offset, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out ushort value)) throw new FormatException("Invalid Unicode escape");
                        result.Append((char)value);
                        offset += 4;
                        break;
                    default: throw new FormatException("Invalid JSON escape");
                }
            }
            throw new FormatException("Unterminated JSON string");
        }

        private double Number()
        {
            int start = offset;
            if (Peek('-')) offset++;
            if (Peek('0')) offset++;
            else
            {
                if (offset >= input.Length || input[offset] < '1' || input[offset] > '9')
                    throw new FormatException("Invalid JSON number");
                while (Digit()) offset++;
            }
            if (Peek('.'))
            {
                offset++;
                if (!Digit()) throw new FormatException("Invalid JSON fraction");
                while (Digit()) offset++;
            }
            if (Peek('e') || Peek('E'))
            {
                offset++;
                if (Peek('+') || Peek('-')) offset++;
                if (!Digit()) throw new FormatException("Invalid JSON exponent");
                while (Digit()) offset++;
            }
            if (!double.TryParse(input.Substring(start, offset - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number))
                throw new FormatException("JSON number out of range");
            return number;
        }

        private bool Digit() => offset < input.Length && input[offset] >= '0' && input[offset] <= '9';
        private bool Peek(char c) => offset < input.Length && input[offset] == c;
        private void Expect(char c)
        {
            if (!Peek(c)) throw new FormatException("Expected '" + c + "' at " + offset);
            offset++;
        }
        private bool Take(string value)
        {
            if (offset + value.Length > input.Length || string.CompareOrdinal(input, offset, value, 0, value.Length) != 0)
                return false;
            offset += value.Length;
            return true;
        }
        private void White()
        {
            while (offset < input.Length && (input[offset] == ' ' || input[offset] == '\n'
                || input[offset] == '\r' || input[offset] == '\t')) offset++;
        }
    }
}
