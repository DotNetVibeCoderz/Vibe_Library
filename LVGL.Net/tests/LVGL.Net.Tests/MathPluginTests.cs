using System.Globalization;
using Lvgl.Assistant.Plugins;

namespace Lvgl.Tests;

/// <summary>
/// Covers the expression evaluator the assistant uses instead of doing arithmetic in the model.
/// </summary>
public class MathPluginTests
{
    private readonly MathPlugin _plugin = new();

    private double Evaluate(string expression) =>
        double.Parse(_plugin.Evaluate(expression), CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("1 + 1", 2)]
    [InlineData("10 - 4 - 3", 3)]           // left-associative
    [InlineData("2 + 3 * 4", 14)]           // precedence
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("7 / 2", 3.5)]
    [InlineData("7 % 3", 1)]
    [InlineData("-5 + 2", -3)]
    [InlineData("--5", 5)]
    [InlineData("2 ^ 10", 1024)]
    [InlineData("2 ^ 3 ^ 2", 512)]          // right-associative: 2^(3^2)
    public void Arithmetic_follows_the_usual_rules(string expression, double expected)
    {
        Assert.Equal(expected, Evaluate(expression), 9);
    }

    [Theory]
    [InlineData("sqrt(16)", 4)]
    [InlineData("abs(-3)", 3)]
    [InlineData("min(3, 8)", 3)]
    [InlineData("max(3, 8)", 8)]
    [InlineData("round(2.4)", 2)]
    [InlineData("round(2.456, 2)", 2.46)]
    [InlineData("floor(2.9)", 2)]
    [InlineData("ceil(2.1)", 3)]
    [InlineData("pow(3, 4)", 81)]
    public void Functions_are_evaluated(string expression, double expected)
    {
        Assert.Equal(expected, Evaluate(expression), 9);
    }

    [Fact]
    public void Constants_are_available()
    {
        Assert.Equal(Math.PI, Evaluate("pi"), 9);
        Assert.Equal(Math.E, Evaluate("e"), 9);
    }

    [Fact]
    public void Nested_calls_and_expressions_compose()
    {
        Assert.Equal(5, Evaluate("sqrt(9 + max(8, 16))"), 9);
    }

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("2 $ 3")]
    [InlineData("frobnicate(2)")]
    [InlineData("")]
    public void Bad_input_returns_an_explanation_rather_than_throwing(string expression)
    {
        // The result goes back to the model as a tool result, so it must be readable text, not an
        // exception that kills the turn.
        var result = _plugin.Evaluate(expression);

        Assert.StartsWith("I could not evaluate that", result, StringComparison.Ordinal);
    }

    [Fact]
    public void The_evaluator_does_not_execute_anything_it_is_given()
    {
        // The parser only knows numbers, operators and a fixed function list. Text that looks like
        // code is a parse failure, which is the security property that matters when a scraped page
        // can reach this tool.
        var result = _plugin.Evaluate("System.IO.File.Delete(\"x\")");

        Assert.StartsWith("I could not evaluate that", result, StringComparison.Ordinal);
    }

    [Fact]
    public void PercentOf_computes_a_percentage()
    {
        Assert.Equal("25", _plugin.PercentOf(25, 100));
    }

    [Theory]
    [InlineData(800, 50, "400")]
    [InlineData(480, 33.3, "160")]
    public void ScaleToScreen_returns_whole_pixels(int dimension, double percent, string expected)
    {
        // This exists so the model computes layout sizes in pixels rather than trying to do
        // arithmetic on LvCoord.Percent, which is bit-encoded and would silently corrupt.
        Assert.Equal(expected, _plugin.ScaleToScreen(dimension, percent));
    }
}
