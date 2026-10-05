using System.Collections.Generic;
using SteamPlayerCount.Core;
using Xunit;

namespace SteamPlayerCount.Tests
{
    public class StrictNameMatcherTests
    {
        private static readonly List<SteamCandidate> PreyResults = new List<SteamCandidate>
        {
            new SteamCandidate(480490, "Prey", "app"),
            new SteamCandidate(865670, "Prey - Mooncrash", "app"),
            new SteamCandidate(609380, "Prey Demo", "app"),
            new SteamCandidate(354427, "Predator/Prey Pack", "app"),
            new SteamCandidate(2537120, "911: Prey", "app"),
        };

        [Fact]
        public void Picks_the_only_candidate_with_the_same_normalized_name()
        {
            var match = StrictNameMatcher.FindMatch("PREY®", PreyResults);

            Assert.NotNull(match);
            Assert.Equal(480490, match.AppId);
        }

        [Fact]
        public void Returns_null_when_no_candidate_has_the_same_name()
        {
            Assert.Null(StrictNameMatcher.FindMatch("Prey 2", PreyResults));
        }

        [Fact]
        public void Returns_null_when_two_different_apps_share_the_name()
        {
            var candidates = new List<SteamCandidate>
            {
                new SteamCandidate(1, "Doom", "app"),
                new SteamCandidate(2, "DOOM", "app"),
            };

            Assert.Null(StrictNameMatcher.FindMatch("Doom", candidates));
        }

        [Fact]
        public void The_same_app_listed_twice_is_not_ambiguous()
        {
            var candidates = new List<SteamCandidate>
            {
                new SteamCandidate(7, "Doom", "app"),
                new SteamCandidate(7, "DOOM", "app"),
            };

            Assert.Equal(7, StrictNameMatcher.FindMatch("Doom", candidates).AppId);
        }

        [Fact]
        public void Ignores_candidates_that_are_not_apps()
        {
            var candidates = new List<SteamCandidate>
            {
                new SteamCandidate(5, "Prey", "bundle"),
                new SteamCandidate(6, "Prey", null),
            };

            Assert.Null(StrictNameMatcher.FindMatch("Prey", candidates));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("???")]
        public void A_name_with_nothing_comparable_never_matches(string gameName)
        {
            var candidates = new List<SteamCandidate>
            {
                new SteamCandidate(9, "!!!", "app"),
                new SteamCandidate(10, "", "app"),
            };

            Assert.Null(StrictNameMatcher.FindMatch(gameName, candidates));
        }

        [Fact]
        public void Tolerates_null_candidates_and_null_entries()
        {
            Assert.Null(StrictNameMatcher.FindMatch("Prey", null));
            Assert.Null(StrictNameMatcher.FindMatch("Prey", new List<SteamCandidate> { null }));
        }
    }
}
