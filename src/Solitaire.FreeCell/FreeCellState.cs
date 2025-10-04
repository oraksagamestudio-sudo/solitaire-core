using System;
using System.Collections.Generic;
using System.Linq;
using Solitaire.Core;

namespace Solitaire.FreeCell
{
    /// <summary>
    /// Immutable-like game state. Internally uses Lists for convenience,
    /// but Apply creates new lists only for mutated piles (copy-on-write).
    /// C# 7.3-compatible (no nullable reference type syntax / null-forgiving).
    /// </summary>
    public sealed class FreeCellState : IGameState<Move>
    {
        public FreeCellConfig Config { get; }
        public uint Seed { get; }
        public int MoveCount { get; private set; }

        // Piles
        public Card?[] Cells { get; }                // size 4 (nullable value type OK in C# 7.3)
        public List<Card>[] Tableaus { get; }        // size 8
        /// <summary>Top rank per suit (0 = empty, 1..13 = Ace..King). Sum equals total cards in foundations.</summary>
        public int[] FoundationTop { get; }          // size 4

        public bool IsVictory => FoundationTop.Sum() == 52;
        public bool IsStalemate => !IsVictory && !GetLegalMoves().Any();

        private FreeCellState(FreeCellConfig cfg, uint seed, int moveCount,
                              Card?[] cells, List<Card>[] tableaus, int[] foundationTop)
        {
            Config = cfg;
            Seed = seed;
            MoveCount = moveCount;
            Cells = cells;
            Tableaus = tableaus;
            FoundationTop = foundationTop;
        }

        public static FreeCellState NewGame(uint seed, FreeCellConfig cfg = null)
        {
            var config = cfg ?? FreeCellConfig.Default;
            var deck = DeckUtils.CreateStandard52(faceUp: true);
            var rng = new XorShift32(seed);
            DeckUtils.FisherYatesShuffle(deck, rng);

            var tableaus = new List<Card>[config.Tableaus];
            for (int i = 0; i < tableaus.Length; i++) tableaus[i] = new List<Card>(7);
            // Deal: columns 0..3 get 7 cards, 4..7 get 6 cards
            int k = 0;
            for (int col = 0; col < 8; col++)
            {
                int count = (col < 4) ? 7 : 6;
                for (int c = 0; c < count; c++) tableaus[col].Add(deck[k++]);
            }
            var cells = new Card?[config.Cells];
            var foundationTop = new int[config.Foundations]; // all zeros

            return new FreeCellState(config, seed, 0, cells, tableaus, foundationTop);
        }

        public IEnumerable<Move> GetLegalMoves()
        {
            // From tableaus
            for (int i = 0; i < Tableaus.Length; i++)
            {
                if (Tableaus[i].Count == 0) continue;
                var card = Tableaus[i][Tableaus[i].Count - 1];

                // To foundation
                if (CanPlaceOnFoundation(card))
                    yield return new Move(MoveKind.TableauToFoundation, i, SuitIndex.ToIndex(card.Suit), 1);

                // To any empty cell
                for (int c = 0; c < Cells.Length; c++)
                    if (!Cells[c].HasValue)
                        yield return new Move(MoveKind.TableauToCell, i, c, 1);

                // To other tableaus (single card for Phase 2)
                for (int j = 0; j < Tableaus.Length; j++)
                {
                    if (j == i) continue;
                    if (CanPlaceOnTableau(card, j))
                        yield return new Move(MoveKind.TableauToTableau, i, j, 1);
                }
            }

            // From cells
            for (int c = 0; c < Cells.Length; c++)
            {
                if (!Cells[c].HasValue) continue;
                var card = Cells[c].Value;

                if (CanPlaceOnFoundation(card))
                    yield return new Move(MoveKind.CellToFoundation, c, SuitIndex.ToIndex(card.Suit), 1);

                for (int j = 0; j < Tableaus.Length; j++)
                    if (CanPlaceOnTableau(card, j))
                        yield return new Move(MoveKind.CellToTableau, c, j, 1);
            }
        }

        public IGameState<Move> Apply(Move move)
        {
            // Copy-on-write
            var cells = (Card?[])Cells.Clone();
            var tableaus = Tableaus.Select(list => new List<Card>(list)).ToArray();
            var ftop = (int[])FoundationTop.Clone();
            int mc = MoveCount + 1;

            switch (move.Kind)
            {
                case MoveKind.TableauToCell:
                {
                    var src = tableaus[move.From];
                    if (src.Count == 0) throw new InvalidOperationException("Empty tableau.");
                    var card = src[src.Count - 1];
                    if (cells[move.To].HasValue) throw new InvalidOperationException("Cell not empty.");
                    src.RemoveAt(src.Count - 1);
                    cells[move.To] = card;
                    break;
                }
                case MoveKind.CellToTableau:
                {
                    var card = cells[move.From] ?? throw new InvalidOperationException("Cell empty.");
                    if (!CanPlaceOnTableau(card, move.To, tableaus))
                        throw new InvalidOperationException("Illegal move.");
                    tableaus[move.To].Add(card);
                    cells[move.From] = null;
                    break;
                }
                case MoveKind.TableauToFoundation:
                {
                    var src = tableaus[move.From];
                    if (src.Count == 0) throw new InvalidOperationException("Empty tableau.");
                    var card = src[src.Count - 1];
                    if (!CanPlaceOnFoundation(card, ftop))
                        throw new InvalidOperationException("Illegal move.");
                    src.RemoveAt(src.Count - 1);
                    ftop[SuitIndex.ToIndex(card.Suit)] = (int)card.Rank;
                    break;
                }
                case MoveKind.CellToFoundation:
                {
                    var card = cells[move.From] ?? throw new InvalidOperationException("Cell empty.");
                    if (!CanPlaceOnFoundation(card, ftop))
                        throw new InvalidOperationException("Illegal move.");
                    cells[move.From] = null;
                    ftop[SuitIndex.ToIndex(card.Suit)] = (int)card.Rank;
                    break;
                }
                case MoveKind.TableauToTableau:
                {
                    if (move.Count != 1) throw new NotImplementedException("Sequence moves not yet supported.");
                    var src = tableaus[move.From];
                    if (src.Count == 0) throw new InvalidOperationException("Empty tableau.");
                    var card = src[src.Count - 1];
                    if (!CanPlaceOnTableau(card, move.To, tableaus))
                        throw new InvalidOperationException("Illegal move.");
                    src.RemoveAt(src.Count - 1);
                    tableaus[move.To].Add(card);
                    break;
                }
                default: throw new NotSupportedException();
            }

            return new FreeCellState(Config, Seed, mc, cells, tableaus, ftop);
        }

        private bool CanPlaceOnFoundation(Card card) => CanPlaceOnFoundation(card, FoundationTop);
        private static bool CanPlaceOnFoundation(Card card, int[] top)
        {
            int idx = SuitIndex.ToIndex(card.Suit);
            return (int)card.Rank == top[idx] + 1;
        }

        private bool CanPlaceOnTableau(Card card, int to) => CanPlaceOnTableau(card, to, Tableaus);
        private static bool CanPlaceOnTableau(Card card, int to, List<Card>[] tableaus)
        {
            var dst = tableaus[to];
            if (dst.Count == 0)
            {
                // In classic FreeCell, ANY card may be moved to an empty tableau.
                return true;
            }
            var target = dst[dst.Count - 1];
            bool colorsAlt = (card.Color != target.Color);
            bool rankIsOneLower = ((int)card.Rank == (int)target.Rank - 1);
            return colorsAlt && rankIsOneLower;
        }
    }
}
