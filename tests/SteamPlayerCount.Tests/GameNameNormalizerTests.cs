using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class GameNameNormalizerTests
    {
        [Theory]
        [InlineData("Prey", "prey")]
        [InlineData("PREY®", "prey")]
        [InlineData("  Prey  ", "prey")]
        [InlineData("Prey - Mooncrash", "preymooncrash")]
        [InlineData("The Witcher 3: Wild Hunt", "thewitcher3wildhunt")]
        [InlineData("Ori & the Blind Forest", "oriandtheblindforest")]
        [InlineData("Ori and the Blind Forest", "oriandtheblindforest")]
        [InlineData("DOOM™ Eternal", "doometernal")]
        public void Normalize_reduces_a_name_to_letters_and_digits(string input, string expected)
        {
            Assert.Equal(expected, GameNameNormalizer.Normalize(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("???")]
        public void Normalize_returns_empty_when_nothing_comparable_remains(string input)
        {
            Assert.Equal(string.Empty, GameNameNormalizer.Normalize(input));
        }
    }
}
