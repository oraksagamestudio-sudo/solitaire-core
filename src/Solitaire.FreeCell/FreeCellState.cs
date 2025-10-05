using System;
using System.Collections.Generic;
using System.Linq;
using Solitaire.Core;

namespace Solitaire.FreeCell
{
    public sealed class FreeCellState : IGameState<Move>
    {
        public FreeCellConfig Config { get; }
        public uint Seed { get; }
        public int MoveCount { get; private set; }

        public Card?[] Cells { get; }
        public List<Card>[] Tableaus { get; }
        public int[] FoundationTop { get; }

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
            int k = 0;
            for (int col = 0; col < 8; col++)
            {
                int count = (col < 4) ? 7 : 6;
                for (int c = 0; c < count; c++) tableaus[col].Add(deck[k++]);
            }
            var cells = new Card?[config.Cells];
            var foundationTop = new int[config.Foundations];

            return new FreeCellState(config, seed, 0, cells, tableaus, foundationTop);
        }

        public IEnumerable<Move> GetLegalMoves()
        {
            int emptyCells = 0;
            for (int c = 0; c < Cells.Length; c++) if (!Cells[c].HasValue) emptyCells++;
            int emptyTabs = 0;
            for (int t = 0; t < Tableaus.Length; t++) if (Tableaus[t].Count == 0) emptyTabs++;

            for (int i = 0; i < Tableaus.Length; i++)
            {
                if (Tableaus[i].Count == 0) continue;
                var srcList = Tableaus[i];
                var top = srcList[srcList.Count - 1];

                if (CanPlaceOnFoundation(top))
                    yield return new Move(MoveKind.TableauToFoundation, i, SuitIndex.ToIndex(top.Suit), 1);

                for (int c = 0; c < Cells.Length; c++)
                    if (!Cells[c].HasValue)
                        yield return new Move(MoveKind.TableauToCell, i, c, 1);

                for (int j = 0; j < Tableaus.Length; j++)
                    if (j != i && CanPlaceOnTableau(top, j))
                        yield return new Move(MoveKind.TableauToTableau, i, j, 1);

                if (Config.AllowSequenceMoves)
                {
                    int run = TailRunLength(srcList);
                    if (run > 1)
                    {
                        for (int j = 0; j < Tableaus.Length; j++)
                        {
                            if (j == i) continue;
                            bool destEmpty = Tableaus[j].Count == 0;
                            int maxByBuffer = ComputeMaxMovable(emptyCells, emptyTabs, destEmpty);
                            int maxLen = Math.Min(run, maxByBuffer);
                            if (maxLen <= 1) continue;

                            for (int count = 2; count <= maxLen; count++)
                            {
                                var bottom = srcList[srcList.Count - count];
                                if (CanPlaceOnTableauBottom(bottom, j))
                                    yield return new Move(MoveKind.TableauToTableau, i, j, count);
                            }
                        }
                    }
                }
            }

            for (int c = 0; c < Cells.Length; c++)
            {
                if (!Cells[c].HasValue) continue;
                var card = Cells[c].Value;

                if (CanPlaceOnFoundation(card))
                    yield return new Move(MoveKind.CellToFoundation, c, SuitIndex.ToIndex(card.Suit), 1);

                for (int j = 0; j < Tableaus.Length; j++)
                    if (CanPlaceOnTableau(card, j))
                        yield return new Move(MoveKind.CellToTableau, c, j, 1);

                // === Reverse from Foundation ===
                for (int f = 0; f < 4; f++)
                {
                    int topRank = this.FoundationTop[f];
                    if (topRank <= 0) continue;
                    var fromCard = new Card((Suit)f, (Rank)topRank);

                    // to tableau
                    for (int t = 0; t < this.Tableaus.Length; t++)
                    {
                        var pile = this.Tableaus[t];
                        bool ok;
                        if (pile.Count == 0) ok = true; // FreeCell 규칙: 빈 테이블로는 어떤 카드도 가능
                        else
                        {
                            var dest = pile[pile.Count - 1];
                            ok = IsOppositeColor(fromCard, dest) && ((int)dest.Rank == (int)fromCard.Rank + 1);
                        }
                        if (ok) yield return new Move(MoveKind.FoundationToTableau, f, t, 1);
                    }

                    // to cell
                    for (int c = 0; c < this.Cells.Length; c++)
                    {
                        if (!this.Cells[c].HasValue)
                        {
                            yield return new Move(MoveKind.FoundationToCell, f, c, 1);
                            break;
                        }
                    }
                }
            }
        }

        public IGameState<Move> Apply(Move move)
        {
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
                    var src = tableaus[move.From];
                    if (src.Count == 0) throw new InvalidOperationException("Empty tableau.");
                    int count = move.Count;
                    if (count < 1) throw new InvalidOperationException("Count must be >= 1.");
                    if (count == 1)
                    {
                        var one = src[src.Count - 1];
                        if (!CanPlaceOnTableau(one, move.To, tableaus))
                            throw new InvalidOperationException("Illegal move.");
                        src.RemoveAt(src.Count - 1);
                        tableaus[move.To].Add(one);
                    }
                    else
                    {
                        if (!Config.AllowSequenceMoves) throw new InvalidOperationException("Sequence moves disabled.");
                        if (src.Count < count) throw new InvalidOperationException("Source has fewer cards than requested.");
                        if (!IsProperTailRun(src, count)) throw new InvalidOperationException("Slice is not a proper sequence.");
                        var bottom = src[src.Count - count];

                        int emptyCells = 0;
                        for (int c = 0; c < cells.Length; c++) if (!cells[c].HasValue) emptyCells++;
                        int emptyTabs = 0;
                        for (int t = 0; t < tableaus.Length; t++) if (tableaus[t].Count == 0) emptyTabs++;
                        bool destEmpty = tableaus[move.To].Count == 0;
                        int maxByBuffer = ComputeMaxMovable(emptyCells, emptyTabs, destEmpty);
                        if (count > maxByBuffer) throw new InvalidOperationException("Exceeds movable sequence size.");

                        if (!CanPlaceOnTableauBottom(bottom, move.To, tableaus))
                            throw new InvalidOperationException("Illegal destination for sequence.");

                        var dst = tableaus[move.To];
                        for (int k = src.Count - count; k < src.Count; k++) dst.Add(src[k]);
                        src.RemoveRange(src.Count - count, count);
                    }
                    break;
                }
                case MoveKind.FoundationToTableau:
                {
                    if (m.Count != 1) throw new InvalidOperationException("Foundation->Tableau supports count=1.");
                    if (m.From < 0 || m.From >= 4) throw new ArgumentOutOfRangeException("From");
                    if (m.To < 0 || m.To >= this.Tableaus.Length) throw new ArgumentOutOfRangeException("To");
                    int top = this.FoundationTop[m.From];
                    if (top <= 0) throw new InvalidOperationException("Source foundation empty.");

                    var card = new Card((Suit)m.From, (Rank)top);
                    var dst = new List<Card>(this.Tableaus[m.To]);
                    if (dst.Count != 0)
                    {
                        var destTop = dst[dst.Count - 1];
                        if (!(IsOppositeColor(card, destTop) && ((int)destTop.Rank == (int)card.Rank + 1)))
                            throw new InvalidOperationException("Illegal Foundation->Tableau move.");
                    }

                    var newTabs = (List<Card>[])this.Tableaus.Clone();
                    dst.Add(card);
                    newTabs[m.To] = dst;

                    var newFound = (int[])this.FoundationTop.Clone();
                    newFound[m.From] = top - 1;

                    return new FreeCellState(newTabs, (Card?[])this.Cells.Clone(), newFound, this.MoveCount + 1, this.Config);
                }
                case MoveKind.FoundationToCell:
                {
                    if (m.Count != 1) throw new InvalidOperationException("Foundation->Cell supports count=1.");
                    if (m.From < 0 || m.From >= 4) throw new ArgumentOutOfRangeException("From");
                    if (m.To < 0 || m.To >= this.Cells.Length) throw new ArgumentOutOfRangeException("To");
                    int top = this.FoundationTop[m.From];
                    if (top <= 0) throw new InvalidOperationException("Source foundation empty.");
                    if (this.Cells[m.To].HasValue) throw new InvalidOperationException("Target cell not empty.");

                    var newCells = (Card?[])this.Cells.Clone();
                    newCells[m.To] = new Card((Suit)m.From, (Rank)top);

                    var newFound = (int[])this.FoundationTop.Clone();
                    newFound[m.From] = top - 1;

                    return new FreeCellState((List<Card>[])this.Tableaus.Clone(), newCells, newFound, this.MoveCount + 1, this.Config);
                }

                default: throw new NotSupportedException();
            }

            return new FreeCellState(Config, Seed, mc, cells, tableaus, ftop);
        }

        private static int TailRunLength(List<Card> pile)
        {
            int n = pile.Count;
            if (n == 0) return 0;
            int count = 1;
            for (int i = n - 1; i - 1 >= 0; i--)
            {
                var a = pile[i];
                var b = pile[i - 1];
                bool colorsAlt = (a.Color != b.Color);
                bool rankDesc = ((int)a.Rank == (int)b.Rank - 1);
                if (colorsAlt && rankDesc) count++;
                else break;
            }
            return count;
        }

        private static bool IsProperTailRun(List<Card> pile, int count)
        {
            if (count <= 0 || pile.Count < count) return false;
            int n = pile.Count;
            for (int i = n - count + 1; i < n; i++)
            {
                var a = pile[i];
                var b = pile[i - 1];
                bool colorsAlt = (a.Color != b.Color);
                bool rankDesc = ((int)a.Rank == (int)b.Rank - 1);
                if (!(colorsAlt && rankDesc)) return false;
            }
            return true;
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
            if (dst.Count == 0) return true;
            var target = dst[dst.Count - 1];
            bool colorsAlt = (card.Color != target.Color);
            bool rankIsOneLower = ((int)card.Rank == (int)target.Rank - 1);
            return colorsAlt && rankIsOneLower;
        }

        private bool CanPlaceOnTableauBottom(Card bottom, int to) => CanPlaceOnTableauBottom(bottom, to, Tableaus);
        private static bool CanPlaceOnTableauBottom(Card bottom, int to, List<Card>[] tableaus)
        {
            var dst = tableaus[to];
            if (dst.Count == 0) return true;
            var target = dst[dst.Count - 1];
            bool colorsAlt = (bottom.Color != target.Color);
            bool rankIsOneLower = ((int)bottom.Rank == (int)target.Rank - 1);
            return colorsAlt && rankIsOneLower;
        }

        private static int ComputeMaxMovable(int emptyCells, int emptyTableaus, bool destEmpty)
        {
            int k = emptyTableaus;
            if (destEmpty) k = Math.Max(0, k - 1);
            long max = (long)(emptyCells + 1);
            for (int i = 0; i < k; i++) max *= 2;
            if (max < 1) max = 1;
            if (max > 52) max = 52;
            return (int)max;
        }

        static bool IsRed(Suit s) { return s == Suit.Heart || s == Suit.Diamond; }
        static bool IsOppositeColor(Card a, Card b) { return IsRed(a.Suit) != IsRed(b.Suit); }

    }
}
