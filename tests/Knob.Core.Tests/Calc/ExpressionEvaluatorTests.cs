using Knob.Core.Calc;

namespace Knob.Core.Tests.Calc;

public class ExpressionEvaluatorTests
{
    private static decimal Eval(string s)
    {
        Assert.True(ExpressionEvaluator.TryEvaluate(s, out var r, out var err), err);
        return r;
    }

    private static string Error(string s)
    {
        Assert.False(ExpressionEvaluator.TryEvaluate(s, out _, out var err));
        Assert.False(string.IsNullOrEmpty(err));
        return err;
    }

    [Theory]
    [InlineData("1+2*3", "7")]
    [InlineData("(1+2)*3", "9")]
    [InlineData("-2^2", "-4")]
    [InlineData("2^3^2", "512")]
    [InlineData("50%", "0.5")]
    [InlineData("0.1+0.2", "0.3")]
    [InlineData("2^-1", "0.5")]
    [InlineData("--3", "3")]
    [InlineData("10 - 2 - 3", "5")]
    [InlineData("12 / 4 / 3", "1")]
    [InlineData("3×4÷2", "6")]
    [InlineData("1,234.5 + 0.5", "1235")]
    [InlineData("200 * 15%", "30")]
    [InlineData(" ( 2 ) ", "2")]
    [InlineData("2^0.5", "1.4142135623731")]
    public void Evaluates(string expr, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Math.Round(Eval(expr), 13));

    [Fact]
    public void DivisionByZero() => Assert.Equal("Division by zero", Error("1/0"));

    [Theory]
    [InlineData("2+")]
    [InlineData("(1+2")]
    [InlineData("1+2)")]
    [InlineData("*3")]
    [InlineData("2 3")]
    [InlineData("abc")]
    [InlineData("1..2")]
    public void Malformed_IsError(string expr) => Error(expr);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_IsError(string? expr) => Error(expr!);

    [Theory]
    [InlineData("99999999999999999999999999999*10")]
    [InlineData("10^1000")]
    [InlineData("9^9^9")]
    public void Overflow_IsErrorNotException(string expr) => Assert.Equal("Overflow", Error(expr));
}
