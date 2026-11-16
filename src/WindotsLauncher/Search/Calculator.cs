using System.Globalization;
using System.Text;

namespace WindotsLauncher.Search;

/// <summary>
/// A small expression calculator: + - * / % ^ ( ), the constants pi and e, and the functions sqrt, abs, sin, cos, tan,
/// asin, acos, atan, ln, log, log10, exp, floor, ceil, round, plus pow, min, max and mod (arguments separated by ';').
/// A comma between digits is a decimal comma, as in "1,5 + 2" (this machine is pt-BR); "0x1F" and "0b101" are accepted.
/// </summary>
public static class Calculator
{
    /// <summary>
    /// The value of what was typed, or null when it is not a calculation: it must parse completely and use at least an
    /// operator or a function, so "42" or a lone "e" is left to the app search.
    /// </summary>
    public static double? TryEvaluate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var p = new Parser(text);
            var v = p.ParseAll();
            if (!p.UsedOperatorOrFunction || double.IsNaN(v) || double.IsInfinity(v)) return null;
            return v;
        }
        catch (FormatException) { return null; }
    }

    /// <summary>The number the way it is shown and copied: up to 12 significant digits, with the machine's decimal separator.</summary>
    public static string Format(double v)
    {
        if (Math.Abs(v) < 1e-12) v = 0;
        return v.ToString("G12", CultureInfo.CurrentCulture);
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;
        public bool UsedOperatorOrFunction { get; private set; }

        public Parser(string s) => _s = s.Replace("×", "*").Replace("÷", "/").Replace("**", "^");

        public double ParseAll()
        {
            var v = Expr();
            Skip();
            if (_i < _s.Length) throw new FormatException("unexpected " + _s[_i]);
            return v;
        }

        private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private bool Eat(char c) { Skip(); if (_i < _s.Length && _s[_i] == c) { _i++; return true; } return false; }

        private double Expr()
        {
            var v = Term();
            while (true)
            {
                if (Eat('+')) { UsedOperatorOrFunction = true; v += Term(); }
                else if (Eat('-')) { UsedOperatorOrFunction = true; v -= Term(); }
                else return v;
            }
        }

        private double Term()
        {
            var v = Unary();
            while (true)
            {
                if (Eat('*')) { UsedOperatorOrFunction = true; v *= Unary(); }
                else if (Eat('/')) { UsedOperatorOrFunction = true; v /= Unary(); }
                else if (Eat('%')) { UsedOperatorOrFunction = true; v = v % Unary(); }
                else return v;
            }
        }

        private double Unary()
        {
            if (Eat('-')) { UsedOperatorOrFunction = true; return -Unary(); }
            if (Eat('+')) return Unary();
            return Power();
        }

        private double Power()
        {
            var b = Primary();
            if (Eat('^')) { UsedOperatorOrFunction = true; return Math.Pow(b, Unary()); }   // right associative
            return b;
        }

        private double Primary()
        {
            Skip();
            if (_i >= _s.Length) throw new FormatException("end");
            var c = _s[_i];
            if (c == '(')
            {
                _i++;
                var v = Expr();
                if (!Eat(')')) throw new FormatException("missing )");
                return v;
            }
            if (char.IsDigit(c) || c == '.' || c == ',') return Number();
            if (char.IsLetter(c)) return Name();
            throw new FormatException("unexpected " + c);
        }

        private double Number()
        {
            if (_s[_i] == '0' && _i + 1 < _s.Length && (_s[_i + 1] == 'x' || _s[_i + 1] == 'X' || _s[_i + 1] == 'b' || _s[_i + 1] == 'B'))
            {
                var hex = _s[_i + 1] is 'x' or 'X';
                _i += 2;
                var start = _i;
                while (_i < _s.Length && Uri.IsHexDigit(_s[_i]) && (hex || _s[_i] is '0' or '1')) _i++;
                if (_i == start) throw new FormatException("digits");
                return Convert.ToInt64(_s[start.._i], hex ? 16 : 2);
            }
            var sb = new StringBuilder();
            var seenPoint = false;
            while (_i < _s.Length)
            {
                var ch = _s[_i];
                if (char.IsDigit(ch)) sb.Append(ch);
                else if ((ch == '.' || ch == ',') && !seenPoint && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1])) { sb.Append('.'); seenPoint = true; }
                else if ((ch == 'e' || ch == 'E') && sb.Length > 0 && _i + 1 < _s.Length && (char.IsDigit(_s[_i + 1]) || ((_s[_i + 1] == '+' || _s[_i + 1] == '-') && _i + 2 < _s.Length && char.IsDigit(_s[_i + 2]))))
                {
                    sb.Append('e'); _i++; sb.Append(_s[_i]);   // exponent: 1e3, 2.5e-4
                }
                else break;
                _i++;
            }
            if (sb.Length == 0) throw new FormatException("number");
            return double.Parse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private double Name()
        {
            var start = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
            var name = _s[start.._i].ToLowerInvariant();
            Skip();
            if (_i < _s.Length && _s[_i] == '(')
            {
                _i++;
                var args = new List<double>();
                Skip();
                if (!Eat(')'))
                {
                    do { args.Add(Expr()); } while (Eat(';'));
                    if (!Eat(')')) throw new FormatException("missing )");
                }
                UsedOperatorOrFunction = true;
                return Call(name, args);
            }
            return name switch
            {
                "pi" => Math.PI,
                "e" => Math.E,
                "tau" => Math.Tau,
                _ => throw new FormatException("unknown name " + name),
            };
        }

        private static double Call(string name, List<double> a)
        {
            double One() => a.Count == 1 ? a[0] : throw new FormatException(name + " takes one argument");
            double Two() => a.Count == 2 ? 0 : throw new FormatException(name + " takes two arguments (separated by ;)");
            switch (name)
            {
                case "sqrt": return Math.Sqrt(One());
                case "abs": return Math.Abs(One());
                case "sin": return Math.Sin(One());
                case "cos": return Math.Cos(One());
                case "tan": return Math.Tan(One());
                case "asin": return Math.Asin(One());
                case "acos": return Math.Acos(One());
                case "atan": return Math.Atan(One());
                case "ln": return Math.Log(One());
                case "log": case "log10": return Math.Log10(One());
                case "exp": return Math.Exp(One());
                case "floor": return Math.Floor(One());
                case "ceil": return Math.Ceiling(One());
                case "round": return Math.Round(One(), MidpointRounding.AwayFromZero);
                case "pow": Two(); return Math.Pow(a[0], a[1]);
                case "min": Two(); return Math.Min(a[0], a[1]);
                case "max": Two(); return Math.Max(a[0], a[1]);
                case "mod": Two(); return a[0] % a[1];
                default: throw new FormatException("unknown function " + name);
            }
        }
    }
}
