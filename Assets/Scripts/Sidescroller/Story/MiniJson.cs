using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sidescroller
{
    /// <summary>
    /// 小型遞迴下降 json 解析器（cutscene-format.md §5）。object → <c>Dictionary&lt;string, object&gt;</c>、array → <c>List&lt;object&gt;</c>、
    /// string、number（boxed double）、bool（boxed）、null。錯誤時丟 <see cref="FormatException"/>，訊息含行號、欄號與附近的文字。
    /// 允許前後空白與 UTF-8 BOM；不支援註解與尾端逗號。只用參考型別參數的泛型，不需要補充元數據。
    /// </summary>
    public sealed class MiniJson
    {
        private readonly string _text;
        private int _pos;
        private readonly List<string> _warnings;

        private MiniJson(string text, List<string> warnings)
        {
            _text = text;
            _warnings = warnings;
        }

        /// <summary>解析整份文字。warnings 可為 null（key 重複的警告會被丟棄）。</summary>
        public static object Parse(string text, List<string> warnings = null)
        {
            if (text == null)
                throw new FormatException("json 文字是 null");
            var parser = new MiniJson(text, warnings);
            if (parser._text.Length > 0 && parser._text[0] == '﻿')
                parser._pos = 1;
            parser.SkipWhitespace();
            var value = parser.ParseValue();
            parser.SkipWhitespace();
            if (parser._pos < parser._text.Length)
                throw parser.Error("值的後面還有多餘的文字");
            return value;
        }

        #region 解析
        private object ParseValue()
        {
            if (_pos >= _text.Length)
                throw Error("文字意外結束，缺少值");
            var c = _text[_pos];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': ExpectWord("true"); return true;
                case 'f': ExpectWord("false"); return false;
                case 'n': ExpectWord("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ParseNumber();
                    throw Error($"不認得的字元 '{c}'");
            }
        }

        private Dictionary<string, object> ParseObject()
        {
            var result = new Dictionary<string, object>();
            _pos++; // {
            SkipWhitespace();
            if (Peek() == '}')
            {
                _pos++;
                return result;
            }
            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                    throw Error("物件的 key 必須是字串");
                var keyPos = _pos;
                var key = ParseString();
                SkipWhitespace();
                if (Peek() != ':')
                    throw Error("key 後面缺少 ':'");
                _pos++;
                SkipWhitespace();
                var value = ParseValue();
                if (result.ContainsKey(key))
                    _warnings?.Add($"{Location(keyPos)}：key \"{key}\" 重複，以後者為準");
                result[key] = value;
                SkipWhitespace();
                var c = Peek();
                if (c == ',')
                {
                    _pos++;
                    continue;
                }
                if (c == '}')
                {
                    _pos++;
                    return result;
                }
                throw Error("物件裡缺少 ',' 或 '}'");
            }
        }

        private List<object> ParseArray()
        {
            var result = new List<object>();
            _pos++; // [
            SkipWhitespace();
            if (Peek() == ']')
            {
                _pos++;
                return result;
            }
            while (true)
            {
                SkipWhitespace();
                result.Add(ParseValue());
                SkipWhitespace();
                var c = Peek();
                if (c == ',')
                {
                    _pos++;
                    continue;
                }
                if (c == ']')
                {
                    _pos++;
                    return result;
                }
                throw Error("陣列裡缺少 ',' 或 ']'");
            }
        }

        private string ParseString()
        {
            _pos++; // "
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _text.Length)
                    throw Error("字串沒有結尾的 '\"'");
                var c = _text[_pos++];
                if (c == '"')
                    return sb.ToString();
                if (c < 0x20)
                {
                    _pos--;
                    throw Error("字串裡不能有未跳脫的控制字元（換行請寫 \\n）");
                }
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (_pos >= _text.Length)
                    throw Error("跳脫字元後文字意外結束");
                var e = _text[_pos++];
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
                        // \uXXXX；代理對是兩個連續的 \u，各自 Append 後自然組成正確的 UTF-16
                        if (_pos + 4 > _text.Length)
                            throw Error("\\u 後面需要 4 個十六進位數字");
                        var hex = _text.Substring(_pos, 4);
                        if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            throw Error($"\\u{hex} 不是合法的十六進位");
                        sb.Append((char)code);
                        _pos += 4;
                        break;
                    default:
                        _pos--;
                        throw Error($"不認得的跳脫字元 '\\{e}'");
                }
            }
        }

        private object ParseNumber()
        {
            var start = _pos;
            if (Peek() == '-')
                _pos++;
            if (Peek() == '0')
                _pos++;
            else if (IsDigit(Peek()))
                while (IsDigit(Peek())) _pos++;
            else
                throw Error("數字格式錯誤");
            if (Peek() == '.')
            {
                _pos++;
                if (!IsDigit(Peek()))
                    throw Error("小數點後面需要數字");
                while (IsDigit(Peek())) _pos++;
            }
            if (Peek() == 'e' || Peek() == 'E')
            {
                _pos++;
                if (Peek() == '+' || Peek() == '-')
                    _pos++;
                if (!IsDigit(Peek()))
                    throw Error("指數需要數字");
                while (IsDigit(Peek())) _pos++;
            }
            var s = _text.Substring(start, _pos - start);
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                _pos = start;
                throw Error($"無法解析數字 {s}");
            }
            return d;
        }

        private void ExpectWord(string word)
        {
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
                throw Error($"預期是 {word}");
            _pos += word.Length;
        }
        #endregion

        #region 輔助
        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

        private void SkipWhitespace()
        {
            while (_pos < _text.Length)
            {
                var c = _text[_pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    _pos++;
                else
                    break;
            }
        }

        /// <summary>「第 L 行第 C 欄」（從 1 起算）。</summary>
        private string Location(int pos)
        {
            int line = 1, col = 1;
            for (var i = 0; i < pos && i < _text.Length; i++)
            {
                if (_text[i] == '\n')
                {
                    line++;
                    col = 1;
                }
                else
                {
                    col++;
                }
            }
            return $"第 {line} 行第 {col} 欄";
        }

        private FormatException Error(string message)
        {
            var from = Math.Max(0, _pos - 15);
            var to = Math.Min(_text.Length, _pos + 15);
            var near = _text.Substring(from, to - from).Replace("\r", " ").Replace("\n", " ");
            return new FormatException($"json 語法錯誤（{Location(_pos)}）：{message}；附近：「{near}」");
        }
        #endregion
    }
}
