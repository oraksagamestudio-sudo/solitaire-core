using Xunit;
using System.Linq;
using Solitaire.FreeCell;

namespace Solitaire.Core.Tests
{
    public class FreeCellTests
    {
        [Fact]
        public void NewGame_Deals_7_6_Distribution()
        {
            var s = FreeCellState.NewGame(12345, new FreeCellConfig());
            // First 4 columns have 7, last 4 have 6
            for (int i = 0; i < 8; i++)
            {
                var expected = (i < 4) ? 7 : 6;
                Assert.Equal(expected, s.Tableaus[i].Count);
            }
            // Total 52
            Assert.Equal(52, s.Tableaus.Sum(t => t.Count));
        }

        [Fact]
        public void LegalMoves_NotEmpty_OnStart()
        {
            var s = FreeCellState.NewGame(1, new FreeCellConfig());
            Assert.True(s.GetLegalMoves().Any());
        }
    }
}
