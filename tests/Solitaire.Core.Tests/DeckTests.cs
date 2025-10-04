using Xunit;
using Solitaire.Core;
using System.Linq;

namespace Solitaire.Core.Tests
{
    public class DeckTests
    {
        [Fact]
        public void StandardDeck_Has52UniqueCards()
        {
            var deck = DeckUtils.CreateStandard52();
            Assert.Equal(52, deck.Count);
            var unique = deck.Select(c => $"{c.Suit}-{(int)c.Rank}").Distinct().Count();
            Assert.Equal(52, unique);
        }

        [Fact]
        public void Shuffle_IsDeterministic_WithSameSeed()
        {
            var d1 = DeckUtils.CreateStandard52();
            var d2 = DeckUtils.CreateStandard52();
            var rng1 = new XorShift32(12345);
            var rng2 = new XorShift32(12345);
            DeckUtils.FisherYatesShuffle(d1, rng1);
            DeckUtils.FisherYatesShuffle(d2, rng2);
            Assert.True(d1.Select(c => $"{c.Suit}-{(int)c.Rank}").SequenceEqual(
                        d2.Select(c => $"{c.Suit}-{(int)c.Rank}")));
        }
    }
}
