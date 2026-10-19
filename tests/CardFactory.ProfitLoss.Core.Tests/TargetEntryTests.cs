using CardFactory.ProfitLoss.Core.Services;
using Xunit;

namespace CardFactory.ProfitLoss.Core.Tests;

public sealed class TargetEntryTests
{
    [Theory]
    [InlineData("1450", 1450)]
    [InlineData("1,450", 1450)]
    [InlineData("£1,450.00", 1450)]
    [InlineData("1450.", 1450)]
    [InlineData("1450.5", 1450.5)]
    [InlineData(" 7.20 ", 7.2)]
    [InlineData(".5", 0.5)]
    [InlineData("", 0)]
    public void Reads_TheNumber_AsItIsTyped(string text, double expected)
    {
        Assert.True(TargetEntry.TryRead(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("14.5.0")]
    [InlineData("-5")]
    public void NotANumber_IsNotRead(string text) => Assert.False(TargetEntry.TryRead(text, out _));
}
