using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel;

namespace Lvgl.Assistant.Plugins;

/// <summary>
/// Arithmetic the model should not do in its head.
/// </summary>
/// <remarks>
/// The expression evaluator is a hand-written recursive-descent parser rather than
/// <c>DataTable.Compute</c> or a scripting engine. Compute cannot do powers or functions and has
/// odd locale behaviour; a scripting engine would happily execute whatever a prompt-injected page
/// told it to. This parser only understands numbers and the operators listed below, so the worst
/// a malicious input can do is fail to parse.
/// </remarks>
public sealed class MathPlugin
{
    [KernelFunction("evaluate")]
    [Description(
        "Evaluates an arithmetic expression. Supports + - * / % ^, parentheses, and the functions " +
        "sqrt, abs, min, max, round, floor, ceil, log, log10, exp, sin, cos, tan, and the " +
        "constants pi and e.")]
    public string Evaluate([Description("The expression, e.g. '(800 - 190) / 3'.")] string expression)
    {
        try
        {
            var value = ExpressionParser.Evaluate(expression);
            return value.ToString("G15", CultureInfo.InvariantCulture);
        }
        catch (FormatException ex)
        {
            return $"I could not evaluate that: {ex.Message}";
        }
    }

    [KernelFunction("percent_of")]
    [Description("What is <percent>% of <value>.")]
    public string PercentOf(double percent, double value) =>
        (value * percent / 100.0).ToString("G15", CultureInfo.InvariantCulture);

    [KernelFunction("scale_to_screen")]
    [Description(
        "Converts a percentage of a screen dimension into whole pixels. Use this when laying out " +
        "LVGL widgets: LvCoord.Percent values are bit-encoded and cannot be used in arithmetic.")]
    public string ScaleToScreen(
        [Description("Screen width or height in pixels.")] int dimension,
        [Description("Percentage of that dimension, 0-100.")] double percent) =>
        ((int)Math.Round(dimension * percent / 100.0)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A small recursive-descent parser for arithmetic. Grammar, loosest binding first:
    /// <c>expr := term (('+'|'-') term)*</c>,
    /// <c>term := factor (('*'|'/'|'%') factor)*</c>,
    /// <c>factor := unary ('^' factor)?</c> (right-associative),
    /// <c>unary := ('+'|'-')? primary</c>,
    /// <c>primary := number | name '(' args ')' | constant | '(' expr ')'</c>.
    /// </summary>
    internal static class ExpressionParser
    {
        public static double Evaluate(string? expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new FormatException("the expression was empty");
            }

            var position = 0;
            var value = ParseExpression(expression, ref position);

            SkipWhitespace(expression, ref position);
            if (position < expression.Length)
            {
                throw new FormatException($"unexpected '{expression[position]}' at position {position}");
            }

            return value;
        }

        private static double ParseExpression(string text, ref int position)
        {
            var value = ParseTerm(text, ref position);

            while (true)
            {
                SkipWhitespace(text, ref position);
                if (position >= text.Length) return value;

                var op = text[position];
                if (op is not ('+' or '-')) return value;

                position++;
                var right = ParseTerm(text, ref position);
                value = op == '+' ? value + right : value - right;
            }
        }

        private static double ParseTerm(string text, ref int position)
        {
            var value = ParseFactor(text, ref position);

            while (true)
            {
                SkipWhitespace(text, ref position);
                if (position >= text.Length) return value;

                var op = text[position];
                if (op is not ('*' or '/' or '%')) return value;

                position++;
                var right = ParseFactor(text, ref position);

                if (op is '/' or '%' && right == 0)
                {
                    throw new FormatException("division by zero");
                }

                value = op switch
                {
                    '*' => value * right,
                    '/' => value / right,
                    _ => value % right,
                };
            }
        }

        private static double ParseFactor(string text, ref int position)
        {
            var value = ParseUnary(text, ref position);

            SkipWhitespace(text, ref position);
            if (position < text.Length && text[position] == '^')
            {
                position++;
                // Right-associative: 2^3^2 is 2^(3^2).
                var exponent = ParseFactor(text, ref position);
                return Math.Pow(value, exponent);
            }

            return value;
        }

        private static double ParseUnary(string text, ref int position)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length) throw new FormatException("the expression ended early");

            // `ref ++position` is not a valid argument, so the increment happens first.
            switch (text[position])
            {
                case '-':
                    position++;
                    return -ParseUnary(text, ref position);
                case '+':
                    position++;
                    return ParseUnary(text, ref position);
                default:
                    return ParsePrimary(text, ref position);
            }
        }

        private static double ParsePrimary(string text, ref int position)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length) throw new FormatException("the expression ended early");

            if (text[position] == '(')
            {
                position++;
                var inner = ParseExpression(text, ref position);
                Expect(text, ref position, ')');
                return inner;
            }

            if (char.IsLetter(text[position])) return ParseNamed(text, ref position);

            var start = position;
            while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.')) position++;

            if (position == start) throw new FormatException($"unexpected '{text[position]}' at position {position}");

            var literal = text[start..position];
            return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new FormatException($"'{literal}' is not a number");
        }

        private static double ParseNamed(string text, ref int position)
        {
            var start = position;
            while (position < text.Length && char.IsLetterOrDigit(text[position])) position++;

            var name = text[start..position].ToLowerInvariant();

            SkipWhitespace(text, ref position);
            if (position >= text.Length || text[position] != '(')
            {
                return name switch
                {
                    "pi" => Math.PI,
                    "e" => Math.E,
                    "tau" => Math.Tau,
                    _ => throw new FormatException($"'{name}' is not a known constant"),
                };
            }

            position++;
            var arguments = new List<double>();

            SkipWhitespace(text, ref position);
            if (position < text.Length && text[position] == ')')
            {
                position++;
            }
            else
            {
                while (true)
                {
                    arguments.Add(ParseExpression(text, ref position));
                    SkipWhitespace(text, ref position);

                    if (position >= text.Length) throw new FormatException($"'{name}(' was never closed");
                    if (text[position] == ',') { position++; continue; }

                    Expect(text, ref position, ')');
                    break;
                }
            }

            return Apply(name, arguments);
        }

        private static double Apply(string name, List<double> args)
        {
            double One() => args.Count == 1
                ? args[0]
                : throw new FormatException($"{name} takes one argument, got {args.Count}");

            double Two(int index) => args.Count == 2
                ? args[index]
                : throw new FormatException($"{name} takes two arguments, got {args.Count}");

            return name switch
            {
                "sqrt" => Math.Sqrt(One()),
                "abs" => Math.Abs(One()),
                "round" => args.Count == 2 ? Math.Round(args[0], (int)args[1]) : Math.Round(One()),
                "floor" => Math.Floor(One()),
                "ceil" => Math.Ceiling(One()),
                "log" => Math.Log(One()),
                "log10" => Math.Log10(One()),
                "exp" => Math.Exp(One()),
                "sin" => Math.Sin(One()),
                "cos" => Math.Cos(One()),
                "tan" => Math.Tan(One()),
                "min" => Math.Min(Two(0), Two(1)),
                "max" => Math.Max(Two(0), Two(1)),
                "pow" => Math.Pow(Two(0), Two(1)),
                _ => throw new FormatException($"'{name}' is not a function I know"),
            };
        }

        private static void Expect(string text, ref int position, char expected)
        {
            SkipWhitespace(text, ref position);

            if (position >= text.Length || text[position] != expected)
            {
                throw new FormatException($"expected '{expected}' at position {position}");
            }

            position++;
        }

        private static void SkipWhitespace(string text, ref int position)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
        }
    }
}
