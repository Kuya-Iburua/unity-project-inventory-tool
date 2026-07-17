using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Kuya.ProjectInventory
{
    // Small dependency-free JSON reader used only for package manifests.
    internal static class MiniJsonReader
    {
        public static object Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using (Parser parser = new Parser(json))
            {
                return parser.ParseValue();
            }
        }

        private sealed class Parser : IDisposable
        {
            private readonly StringReader _reader;

            internal Parser(string json)
            {
                _reader = new StringReader(json);
            }

            public void Dispose()
            {
                _reader.Dispose();
            }

            internal object ParseValue()
            {
                EatWhitespace();
                int c = PeekChar();
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': return ParseLiteral("true", true);
                    case 'f': return ParseLiteral("false", false);
                    case 'n': return ParseLiteral("null", null);
                    case -1: return null;
                    default:
                        if (c == '-' || char.IsDigit((char)c))
                        {
                            return ParseNumber();
                        }

                        throw new FormatException("Unexpected JSON token: " + (char)c);
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
                ReadChar();
                EatWhitespace();
                if (PeekChar() == '}')
                {
                    ReadChar();
                    return result;
                }

                while (true)
                {
                    EatWhitespace();
                    string key = ParseString();
                    EatWhitespace();
                    Expect(':');
                    object value = ParseValue();
                    result[key] = value;
                    EatWhitespace();
                    int c = ReadChar();
                    if (c == '}')
                    {
                        return result;
                    }
                    if (c != ',')
                    {
                        throw new FormatException("Expected ',' or '}' in JSON object.");
                    }
                }
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                ReadChar();
                EatWhitespace();
                if (PeekChar() == ']')
                {
                    ReadChar();
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    EatWhitespace();
                    int c = ReadChar();
                    if (c == ']')
                    {
                        return result;
                    }
                    if (c != ',')
                    {
                        throw new FormatException("Expected ',' or ']' in JSON array.");
                    }
                }
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder builder = new StringBuilder();
                while (true)
                {
                    int c = ReadChar();
                    if (c < 0)
                    {
                        throw new EndOfStreamException("Unterminated JSON string.");
                    }
                    if (c == '"')
                    {
                        return builder.ToString();
                    }
                    if (c != '\\')
                    {
                        builder.Append((char)c);
                        continue;
                    }

                    int escaped = ReadChar();
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            char[] hex = new char[4];
                            for (int i = 0; i < 4; i++)
                            {
                                int h = ReadChar();
                                if (h < 0) throw new EndOfStreamException("Invalid unicode escape.");
                                hex[i] = (char)h;
                            }
                            builder.Append((char)Convert.ToInt32(new string(hex), 16));
                            break;
                        default:
                            throw new FormatException("Invalid JSON escape sequence.");
                    }
                }
            }

            private object ParseNumber()
            {
                StringBuilder builder = new StringBuilder();
                while (true)
                {
                    int c = PeekChar();
                    if (c < 0 || "0123456789+-.eE".IndexOf((char)c) < 0)
                    {
                        break;
                    }
                    builder.Append((char)ReadChar());
                }

                string value = builder.ToString();
                long integer;
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                {
                    return integer;
                }

                double floating;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floating))
                {
                    return floating;
                }

                throw new FormatException("Invalid JSON number: " + value);
            }

            private object ParseLiteral(string literal, object value)
            {
                for (int i = 0; i < literal.Length; i++)
                {
                    if (ReadChar() != literal[i])
                    {
                        throw new FormatException("Invalid JSON literal.");
                    }
                }
                return value;
            }

            private void EatWhitespace()
            {
                while (true)
                {
                    int c = PeekChar();
                    if (c < 0 || !char.IsWhiteSpace((char)c))
                    {
                        return;
                    }
                    ReadChar();
                }
            }

            private void Expect(char expected)
            {
                int actual = ReadChar();
                if (actual != expected)
                {
                    throw new FormatException("Expected '" + expected + "'.");
                }
            }

            private int PeekChar()
            {
                return _reader.Peek();
            }

            private int ReadChar()
            {
                return _reader.Read();
            }
        }
    }
}
