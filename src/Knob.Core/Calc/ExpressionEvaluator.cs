using System.Globalization;

namespace Knob.Core.Calc;

/// <summary>
/// Recursive-descent calculator on <see cref="decimal"/> (so 0.1+0.2 = 0.3).
/// Grammar (lowest → highest precedence):
///   expr    := term (('+'|'-') term)*
///   term    := unary (('*'|'×'|'/'|'÷') unary)*
///   unary   := ('-'|'+') unary | power          → -2^2 = -4
///   power   := postfix ('^' unary)?             → right-assoc: 2^3^2 = 512
///   postfix := primary '%'*                     → 50% = 0.5
///   primary := number | '(' expr ')'           → number allows ',' thousands separators
/// Never throws: every failure becomes an error message.
/// </summary>
public static class ExpressionEvaluator
{
    public static bool TryEvaluate(string? input, out decimal result, out string error)
    {
        result = 0;
        error = "";
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Empty";
            return false;
        }
        try
        {
            var p = new Parser(input);
            result = p.ParseAll();
            return true;
        }
        catch (CalcException ex)
        {
            error = ex.Message;
        }
        catch (OverflowException)
        {
            error = "Overflow";
        }
        catch (DivideByZeroException)
        {
            error = "Division by zero";
        }
        return false;
    }

    private sealed class CalcException(string message) : Exception(message);

    private sealed class Parser(string s)
    {
        private int _i;

        public decimal ParseAll()
        {
            var v = ParseExpr();
            SkipWs();
            if (_i < s.Length) throw new CalcException(s[_i] == ')' ? "Unmatched )" : $"Unexpected '{s[_i]}'");
            return v;
        }

        private decimal ParseExpr()
        {
            var v = ParseTerm();
            while (true)
            {
                SkipWs();
                if (Eat('+')) v += ParseTerm();
                else if (Eat('-')) v -= ParseTerm();
                else return v;
            }
        }

        private decimal ParseTerm()
        {
            var v = ParseUnary();
            while (true)
            {
                SkipWs();
                if (Eat('*') || Eat('×')) v *= ParseUnary();
                else if (Eat('/') || Eat('÷'))
                {
                    var d = ParseUnary();
                    if (d == 0) throw new CalcException("Division by zero");
                    v /= d;
                }
                else return v;
            }
        }

        private decimal ParseUnary()
        {
            SkipWs();
            if (Eat('-')) return -ParseUnary();
            if (Eat('+')) return ParseUnary();
            return ParsePower();
        }

        private decimal ParsePower()
        {
            var b = ParsePostfix();
            SkipWs();
            return Eat('^') ? Pow(b, ParseUnary()) : b;
        }

        private decimal ParsePostfix()
        {
            var v = ParsePrimary();
            while (true)
            {
                SkipWs();
                if (Eat('%')) v /= 100m;
                else return v;
            }
        }

        private decimal ParsePrimary()
        {
            SkipWs();
            if (_i >= s.Length) throw new CalcException("Incomplete expression");
            if (Eat('('))
            {
                var v = ParseExpr();
                SkipWs();
                if (!Eat(')')) throw new CalcException("Missing )");
                return v;
            }
            if (char.IsAsciiDigit(s[_i]) || s[_i] == '.') return ParseNumber();
            throw new CalcException($"Unexpected '{s[_i]}'");
        }

        private decimal ParseNumber()
        {
            var start = _i;
            var digits = new System.Text.StringBuilder();
            var dot = false;
            while (_i < s.Length)
            {
                var c = s[_i];
                if (char.IsAsciiDigit(c)) digits.Append(c);
                else if (c == '.')
                {
                    if (dot) throw new CalcException("Invalid number");
                    dot = true;
                    digits.Append(c);
                }
                else if (c == ',' && !dot && digits.Length > 0 && _i + 1 < s.Length && char.IsAsciiDigit(s[_i + 1])) { /* thousands separator */ }
                else break;
                _i++;
            }
            var text = digits.ToString();
            if (text == "." || text.Length == 0) throw new CalcException($"Invalid number at {start + 1}");
            if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v))
                throw new OverflowException();
            return v;
        }

        private static decimal Pow(decimal b, decimal e)
        {
            if (e == decimal.Truncate(e) && Math.Abs(e) <= int.MaxValue)
            {
                var n = (long)e;
                var neg = n < 0;
                if (neg) n = -n;
                if (n == 0) return 1;
                decimal result = 1, x = b;
                while (true)
                {
                    if ((n & 1) == 1) result *= x;  // throws OverflowException when too big
                    n >>= 1;
                    if (n == 0) break;
                    x *= x;
                }
                if (!neg) return result;
                if (result == 0) throw new CalcException("Division by zero");
                return 1m / result;
            }

            var d = Math.Pow((double)b, (double)e);
            if (double.IsNaN(d)) throw new CalcException("Undefined");
            if (double.IsInfinity(d) || Math.Abs(d) > (double)decimal.MaxValue) throw new OverflowException();
            return (decimal)d;
        }

        private void SkipWs()
        {
            while (_i < s.Length && char.IsWhiteSpace(s[_i])) _i++;
        }

        private bool Eat(char c)
        {
            if (_i < s.Length && s[_i] == c)
            {
                _i++;
                return true;
            }
            return false;
        }
    }
}
