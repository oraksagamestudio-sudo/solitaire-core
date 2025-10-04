using Xunit;
using System.Linq;
using Solitaire.Core;
using Solitaire.FreeCell;
using System.Collections.Generic;

namespace Solitaire.Core.Tests
{
    public class FreeCellSequenceTests
    {
        private static Card C(Suit s, Rank r) => new Card(s, r, true);

        [Fact]
        public void Sequence_MaxMovable_Respects_Buffer_When_Dest_Not_Empty()
        {
            var s = FreeCellState.NewGame(1);
            foreach (var t in s.Tableaus) t.Clear();

            s.Tableaus[0].AddRange(new [] { C(Suit.Spade, Rank.Eight), C(Suit.Heart, Rank.Seven),
                                            C(Suit.Spade, Rank.Six),  C(Suit.Heart, Rank.Five),
                                            C(Suit.Spade, Rank.Four), C(Suit.Heart, Rank.Three)});
            s.Tableaus[1].Add(C(Suit.Heart, Rank.Nine));

            for (int i = 2; i < 8; i++) s.Tableaus[i].Add(C(Suit.Club, Rank.King));

            var moves = s.GetLegalMoves().Where(m => m.Kind == MoveKind.TableauToTableau && m.From == 0 && m.To == 1).ToList();

            Assert.Contains(moves, m => m.Count == 5);
            Assert.DoesNotContain(moves, m => m.Count >= 6);
        }

        [Fact]
        public void Sequence_Dest_Empty_Consumes_One_Empty_Tableau()
        {
            var s = FreeCellState.NewGame(1);
            foreach (var t in s.Tableaus) t.Clear();

            s.Tableaus[0].AddRange(new [] { C(Suit.Spade, Rank.Eight), C(Suit.Heart, Rank.Seven),
                                            C(Suit.Spade, Rank.Six),  C(Suit.Heart, Rank.Five),
                                            C(Suit.Spade, Rank.Four), C(Suit.Heart, Rank.Three)});
            // dest=1 empty
            for (int i = 2; i < 8; i++) s.Tableaus[i].Add(C(Suit.Club, Rank.King));

            var moves = s.GetLegalMoves().Where(m => m.Kind == MoveKind.TableauToTableau && m.From == 0 && m.To == 1).ToList();

            Assert.Contains(moves, m => m.Count == 5);
            Assert.DoesNotContain(moves, m => m.Count >= 6);
        }

        [Fact]
        public void Apply_Sequence_Move_Moves_Slice_Preserving_Order()
        {
            var s = FreeCellState.NewGame(1);
            foreach (var t in s.Tableaus) t.Clear();

            s.Tableaus[0].AddRange(new [] { C(Suit.Spade, Rank.Eight), C(Suit.Heart, Rank.Seven),
                                            C(Suit.Spade, Rank.Six),  C(Suit.Heart, Rank.Five) });
            s.Tableaus[1].Add(C(Suit.Heart, Rank.Nine));

            var m = new Move(MoveKind.TableauToTableau, 0, 1, 4);
            var s2 = (FreeCellState)s.Apply(m);

            var dst = s2.Tableaus[1];
            var tail = dst.Skip(dst.Count - 5).ToArray();
            Assert.Equal("Nine of Heart", tail[0].ToString().Replace(" (down)", ""));
            Assert.Equal("Eight of Spade", tail[1].ToString().Replace(" (down)", ""));
            Assert.Equal("Seven of Heart", tail[2].ToString().Replace(" (down)", ""));
            Assert.Equal("Six of Spade", tail[3].ToString().Replace(" (down)", ""));
            Assert.Equal("Five of Heart", tail[4].ToString().Replace(" (down)", ""));
            Assert.Empty(s2.Tableaus[0]);
        }
    }
}
