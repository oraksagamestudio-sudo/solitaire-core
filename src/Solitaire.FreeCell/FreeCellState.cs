using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Solitaire.Core;

namespace Solitaire.FreeCell
{
    /// <summary>
    /// FreeCell core state with optional items:
    /// - Shuffle: DeckUtils.CreateStandard52 + DeckUtils.FisherYatesShuffle(XorShift32) (replay compatible)
    /// - Multi-card Tableau-to-Tableau sequence moves (buffer-aware, emits maximal xN)
    /// - Apply() supports m.Count > 1 (order preserved)
    /// - AllowFoundationDownMoves via reflection (default: true if property missing)
    /// - Items:
    ///     * Temp Cell: one extra cell slot at index TempCellSlotIndex (== Config.Cells). Inactive until used.
    ///       UseTempCell() activates if you have charges left. When the temp cell becomes empty after moving its card out,
    ///       it deactivates automatically. Charges are consumed on activation.
    ///     * Grab: GrabCard(tableau, depthFromTop) pulls a deeper card within a tableau to the top. Consumes one grab charge.
    /// </summary>
    public sealed class FreeCellState
    {
        public readonly List<Card>[] Tableaus;
        public readonly Card?[] Cells;
        /// <summary>0=Spade,1=Heart,2=Diamond,3=Club ; value: highest rank (0 if empty)</summary>
        public readonly int[] FoundationTop;
        public readonly int MoveCount;
        public readonly FreeCellConfig Config;
        private readonly uint Seed;

        // Items state
        public readonly int TempCellSlotIndex; // fixed at Config.Cells
        public readonly bool TempCellActive;
        public readonly int TempCellChargesLeft;
        public readonly int GrabChargesLeft;

        private FreeCellState(
            uint seed,
            FreeCellConfig cfg,
            List<Card>[] t,
            Card?[] cells,
            int[] ftop,
            int moves,
            bool tempActive,
            int tempChargesLeft,
            int grabChargesLeft,
            int tempCellSlotIndex
        )
        {
            Seed = seed;
            Config = cfg;
            Tableaus = t;
            Cells = cells;
            FoundationTop = ftop;
            MoveCount = moves;

            TempCellActive = tempActive;
            TempCellChargesLeft = tempChargesLeft;
            GrabChargesLeft = grabChargesLeft;
            TempCellSlotIndex = tempCellSlotIndex;
        }

        public static FreeCellState NewGame(uint seed, FreeCellConfig cfg)
        {
            if (cfg == null) cfg = new FreeCellConfig();

            // Restore original shuffle to match recorded replays
            var deck = DeckUtils.CreateStandard52(faceUp: true);
            var rng = new XorShift32(seed);
            DeckUtils.FisherYatesShuffle(deck, rng);

            var t = new List<Card>[cfg.Tableaus];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>();
            for (int i = 0; i < deck.Count; i++) t[i % cfg.Tableaus].Add(deck[i]);

            // +1 slot reserved for temp cell (index == cfg.Cells). It is unusable until activated.
            var cells = new Card?[cfg.Cells + 1];
            var ftop = new int[4];
            return new FreeCellState(seed, cfg, t, cells, ftop, 0, false, cfg.TempCellCharges, cfg.GrabCharges, cfg.Cells);
        }

        public static FreeCellState NewGame(uint seed, FreeCellConfig cfg, string shuffleKind)
        {
            if (cfg == null) cfg = new FreeCellConfig();

            var deck = DeckUtils.CreateStandard52(faceUp: true);
            Solitaire.Core.IRng rng;
            if (!string.IsNullOrEmpty(shuffleKind) && shuffleKind.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                rng = new Solitaire.Core.DotNetRandom((int)seed);
            else
                rng = new Solitaire.Core.XorShift32(seed);

            DeckUtils.FisherYatesShuffle(deck, rng);

            var t = new List<Card>[cfg.Tableaus];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>();
            for (int i = 0; i < deck.Count; i++) t[i % cfg.Tableaus].Add(deck[i]);

            var cells = new Card?[cfg.Cells + 1]; // include temp slot
            var ftop = new int[cfg.Foundations]; // all 0 (empty)
            return new FreeCellState(seed, cfg, t, cells, ftop, 0, false, cfg.TempCellCharges, cfg.GrabCharges, cfg.Cells);
        }

        // Reflection shim for optional config switch
        private static bool AllowDown(FreeCellConfig cfg)
        {
            if (cfg == null) return true;
            var prop = cfg.GetType().GetProperty("AllowFoundationDownMoves", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                var val = prop.GetValue(cfg);
                if (val is bool b) return b;
            }
            return true;
        }

        // Item helpers
        private bool IsCellSlotUsable(int idx)
        {
            if (idx < 0 || idx >= Cells.Length) return false;
            if (idx < Config.Cells) return true;
            // temp slot
            return idx == TempCellSlotIndex && TempCellActive;
        }

        public IEnumerable<Move> GetLegalMoves()
        {
            // Tableau -> Foundation / Cell / Tableau
            for (int src = 0; src < Tableaus.Length; src++)
            {
                var sList = Tableaus[src];
                if (sList.Count > 0)
                {
                    var top = sList[sList.Count - 1];
                    if (CanMoveToFoundation(top)) yield return new Move(MoveKind.TableauToFoundation, src, SuitIndex(top.Suit), 1);

                    for (int c = 0; c < Cells.Length; c++)
                        if (IsCellSlotUsable(c) && !Cells[c].HasValue) yield return new Move(MoveKind.TableauToCell, src, c, 1);

                    // Multi-card sequence moves: emit maximal count per (src,dst)
                    for (int dst = 0; dst < Tableaus.Length; dst++)
                    {
                        if (src == dst) continue;
                        int maxSeq = MaxMovableSequenceCount(src, dst);
                        if (maxSeq > 0)
                            yield return new Move(MoveKind.TableauToTableau, src, dst, maxSeq);
                    }
                }
            }

            // Cell -> Foundation / Tableau
            for (int c = 0; c < Cells.Length; c++)
            {
                if (!IsCellSlotUsable(c)) continue;
                if (Cells[c].HasValue)
                {
                    var card = Cells[c].Value;
                    if (CanMoveToFoundation(card)) yield return new Move(MoveKind.CellToFoundation, c, SuitIndex(card.Suit), 1);
                    for (int dst = 0; dst < Tableaus.Length; dst++)
                        if (CanPlaceOnTableau(Tableaus[dst], card)) yield return new Move(MoveKind.CellToTableau, c, dst, 1);
                }
            }

            // Foundation -> (optional down moves)
            if (AllowDown(Config))
            {
                for (int f = 0; f < 4; f++)
                {
                    int r = FoundationTop[f];
                    if (r <= 0) continue;
                    var suit = IndexSuit(f);
                    var moving = new Card(suit, (Rank)r);

                    for (int c = 0; c < Cells.Length; c++)
                        if (IsCellSlotUsable(c) && !Cells[c].HasValue) yield return new Move(MoveKind.FoundationToCell, f, c, 1);

                    for (int dst = 0; dst < Tableaus.Length; dst++)
                        if (CanPlaceOnTableau(Tableaus[dst], moving)) yield return new Move(MoveKind.FoundationToTableau, f, dst, 1);
                }
            }
        }

        public FreeCellState Apply(Move m)
        {
            var t = CloneTableaus();
            var cells = (Card?[])Cells.Clone();
            var ftop = (int[])FoundationTop.Clone();

            bool tempActive = TempCellActive;
            int tempCharges = TempCellChargesLeft;
            int grabCharges = GrabChargesLeft;

            int moves = MoveCount + 1;

            switch (m.Kind)
            {
                case MoveKind.TableauToCell:
                {
                    RequireTableauIndex(m.From); RequireCellIndex(m.To);
                    if (!IsCellSlotUsable(m.To)) throw new InvalidOperationException("Cell slot not usable.");
                    if (t[m.From].Count == 0) throw new InvalidOperationException("Empty tableau.");
                    if (cells[m.To].HasValue) throw new InvalidOperationException("Cell not empty.");
                    var card = t[m.From][t[m.From].Count - 1];
                    t[m.From].RemoveAt(t[m.From].Count - 1);
                    cells[m.To] = card;
                    break;
                }
                case MoveKind.CellToTableau:
                {
                    RequireCellIndex(m.From); RequireTableauIndex(m.To);
                    if (!IsCellSlotUsable(m.From)) throw new InvalidOperationException("Cell slot not usable.");
                    if (!cells[m.From].HasValue) throw new InvalidOperationException("Cell empty.");
                    var card = cells[m.From].Value;
                    if (!CanPlaceOnTableau(t[m.To], card)) throw new InvalidOperationException("Illegal placement.");
                    cells[m.From] = null;
                    t[m.To].Add(card);
                    if (m.From == TempCellSlotIndex) tempActive = false; // auto-deactivate when emptied
                    break;
                }
                case MoveKind.TableauToTableau:
                {
                    RequireTableauIndex(m.From); RequireTableauIndex(m.To);
                    if (t[m.From].Count == 0) throw new InvalidOperationException("Empty tableau.");
                    int cnt = Math.Max(1, m.Count);
                    if (cnt > t[m.From].Count) throw new InvalidOperationException("Not enough cards.");

                    int start = t[m.From].Count - cnt;
                    // Validate the slice forms a proper alternating descending sequence
                    for (int i = start + 1; i < t[m.From].Count; i++)
                    {
                        if (!FormsSequence(t[m.From][i - 1], t[m.From][i]))
                            throw new InvalidOperationException("Sequence broken within slice.");
                    }
                    // Validate destination placement using the bottom of the slice
                    var bottom = t[m.From][start];
                    if (!CanPlaceOnTableau(t[m.To], bottom))
                        throw new InvalidOperationException("Illegal placement.");

                    // Safety: ensure cnt <= MaxMovableSequenceCount(m.From, m.To)
                    int maxAllowed = MaxMovableSequenceCount(m.From, m.To);
                    if (cnt > maxAllowed) throw new InvalidOperationException("Sequence exceeds buffer capacity.");

                    // Move slice preserving order
                    for (int i = start; i < t[m.From].Count; i++)
                        t[m.To].Add(t[m.From][i]);
                    // Remove moved range
                    for (int i = 0; i < cnt; i++)
                        t[m.From].RemoveAt(t[m.From].Count - 1);
                    break;
                }
                case MoveKind.TableauToFoundation:
                {
                    RequireTableauIndex(m.From); RequireFoundationIndex(m.To);
                    if (t[m.From].Count == 0) throw new InvalidOperationException("Empty tableau.");
                    var card = t[m.From][t[m.From].Count - 1];
                    if (SuitIndex(card.Suit) != m.To) throw new InvalidOperationException("Wrong foundation.");
                    if (!CanMoveToFoundation(card)) throw new InvalidOperationException("Illegal to foundation.");
                    t[m.From].RemoveAt(t[m.From].Count - 1);
                    ftop[m.To] = (int)card.Rank;
                    break;
                }
                case MoveKind.CellToFoundation:
                {
                    RequireCellIndex(m.From); RequireFoundationIndex(m.To);
                    if (!IsCellSlotUsable(m.From)) throw new InvalidOperationException("Cell slot not usable.");
                    if (!cells[m.From].HasValue) throw new InvalidOperationException("Cell empty.");
                    var card = cells[m.From].Value;
                    if (SuitIndex(card.Suit) != m.To) throw new InvalidOperationException("Wrong foundation.");
                    if (!CanMoveToFoundation(card)) throw new InvalidOperationException("Illegal to foundation.");
                    cells[m.From] = null;
                    ftop[m.To] = (int)card.Rank;
                    if (m.From == TempCellSlotIndex) tempActive = false; // auto-deactivate when emptied
                    break;
                }
                case MoveKind.FoundationToTableau:
                {
                    RequireFoundationIndex(m.From); RequireTableauIndex(m.To);
                    if (!AllowDown(Config)) throw new InvalidOperationException("Foundation down moves disabled.");
                    int r = ftop[m.From];
                    if (r <= 0) throw new InvalidOperationException("Foundation empty.");
                    var card = new Card(IndexSuit(m.From), (Rank)r);
                    if (!CanPlaceOnTableau(t[m.To], card)) throw new InvalidOperationException("Illegal placement.");
                    ftop[m.From] = r - 1;
                    t[m.To].Add(card);
                    break;
                }
                case MoveKind.FoundationToCell:
                {
                    RequireFoundationIndex(m.From); RequireCellIndex(m.To);
                    if (!AllowDown(Config)) throw new InvalidOperationException("Foundation down moves disabled.");
                    int r = ftop[m.From];
                    if (r <= 0) throw new InvalidOperationException("Foundation empty.");
                    if (!IsCellSlotUsable(m.To)) throw new InvalidOperationException("Cell slot not usable.");
                    if (cells[m.To].HasValue) throw new InvalidOperationException("Cell not empty.");
                    var card = new Card(IndexSuit(m.From), (Rank)r);
                    ftop[m.From] = r - 1;
                    cells[m.To] = card;
                    break;
                }
                default:
                    throw new NotSupportedException("Unknown move kind: " + m.Kind);
            }

            return new FreeCellState(Seed, Config, t, cells, ftop, moves, tempActive, tempCharges, grabCharges, TempCellSlotIndex);
        }

        // ===== Items API =====

        /// <summary>Activate the temporary cell if you have charges left and the temp slot is empty. Consumes 1 charge on activation.</summary>
        public FreeCellState UseTempCell()
        {
            if (TempCellChargesLeft <= 0) throw new InvalidOperationException("No temp-cell charges left.");
            if (TempCellActive) throw new InvalidOperationException("Temp cell already active.");
            if (Cells[TempCellSlotIndex].HasValue) throw new InvalidOperationException("Temp cell slot is occupied.");
            return new FreeCellState(Seed, Config, CloneTableaus(), (Card?[])Cells.Clone(), (int[])FoundationTop.Clone(),
                                     MoveCount + 1, true, TempCellChargesLeft - 1, GrabChargesLeft, TempCellSlotIndex);
        }

        /// <summary>Grab a card located 'depthFromTop' deep within tableau 'tableau' (0 = current top) and move it to the top of that tableau. Consumes 1 grab charge.</summary>
        public FreeCellState GrabCard(int tableau, int depthFromTop)
        {
            RequireTableauIndex(tableau);
            if (GrabChargesLeft <= 0) throw new InvalidOperationException("No grab charges left.");
            var t = CloneTableaus();
            var pile = t[tableau];
            if (pile.Count == 0) throw new InvalidOperationException("Empty tableau.");
            if (depthFromTop < 0 || depthFromTop >= pile.Count) throw new ArgumentOutOfRangeException("depthFromTop");
            int idx = pile.Count - 1 - depthFromTop; // 0 from top => last index
            if (idx == pile.Count - 1)
            {
                // already at top; still consumes a charge to "stabilize"
            }
            else
            {
                var card = pile[idx];
                pile.RemoveAt(idx);
                pile.Add(card);
            }
            return new FreeCellState(Seed, Config, t, (Card?[])Cells.Clone(), (int[])FoundationTop.Clone(),
                                     MoveCount + 1, TempCellActive, TempCellChargesLeft, GrabChargesLeft - 1, TempCellSlotIndex);
        }

        public FreeCellState WithItemCounts(int tempCharges, int grabCharges)
        {
            if (tempCharges < 0) tempCharges = 0;
            if (grabCharges < 0) grabCharges = 0;
            return new FreeCellState(Seed, Config, CloneTableaus(), (Card?[])Cells.Clone(), (int[])FoundationTop.Clone(),
                                     MoveCount, TempCellActive, tempCharges, grabCharges, TempCellSlotIndex);
        }

        public string ItemsStatusString()
        {
            return "TempCell: " + (TempCellActive ? "ACTIVE" : "INACTIVE") + " (idx " + TempCellSlotIndex + "), charges=" + TempCellChargesLeft
                 + " | Grab charges=" + GrabChargesLeft;
        }

        private List<Card>[] CloneTableaus()
        {
            var t = new List<Card>[Tableaus.Length];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>(Tableaus[i]);
            return t;
        }

        private static bool FormsSequence(Card lower, Card upper)
        {
            // 'lower' is beneath 'upper' on a tableau; for a valid sequence: lower rank == upper rank + 1 and alternating colors
            bool altColor = IsRed(lower.Suit) != IsRed(upper.Suit);
            bool rankOK = ((int)lower.Rank) == ((int)upper.Rank) + 1;
            return altColor && rankOK;
        }

        private static bool CanPlaceOnTableau(List<Card> dest, Card card)
        {
            if (dest.Count == 0) return true; // any card allowed on empty column
            var top = dest[dest.Count - 1];
            bool altColor = IsRed(top.Suit) != IsRed(card.Suit);
            bool rankOK = ((int)top.Rank) == ((int)card.Rank) + 1;
            return altColor && rankOK;
        }

        private bool CanMoveToFoundation(Card card)
        {
            int idx = SuitIndex(card.Suit);
            int expected = FoundationTop[idx] + 1;
            return (int)card.Rank == expected;
        }

        private static bool IsRed(Suit s) { return (s == Suit.Heart) || (s == Suit.Diamond); }

        private static int SuitIndex(Suit s)
        {
            switch (s)
            {
                case Suit.Spade: return 0;
                case Suit.Heart: return 1;
                case Suit.Diamond: return 2;
                case Suit.Club: return 3;
            }
            return 0;
        }

        private static Suit IndexSuit(int idx)
        {
            switch (idx)
            {
                case 0: return Suit.Spade;
                case 1: return Suit.Heart;
                case 2: return Suit.Diamond;
                case 3: return Suit.Club;
            }
            return Suit.Spade;
        }

        private void RequireTableauIndex(int i)
        {
            if (i < 0 || i >= Tableaus.Length) throw new ArgumentOutOfRangeException("tableau");
        }
        private void RequireCellIndex(int i)
        {
            if (i < 0 || i >= Cells.Length) throw new ArgumentOutOfRangeException("cell");
        }
        private void RequireFoundationIndex(int i)
        {
            if (i < 0 || i >= 4) throw new ArgumentOutOfRangeException("foundation");
        }

        // === Multi-card sequence helpers ===
        private int MaxMovableSequenceCount(int src, int dst)
        {
            var sList = Tableaus[src];
            var dList = Tableaus[dst];
            if (sList.Count == 0) return 0;

            // Longest valid descending alternating run at the tail of src
            int serial = 1;
            for (int i = sList.Count - 1; i - 1 >= 0; i--)
            {
                if (FormsSequence(sList[i - 1], sList[i])) serial++;
                else break;
            }

            // Buffer capacity counts only usable empty cells
            int freeCells = 0;
            for (int i = 0; i < Cells.Length; i++) if (IsCellSlotUsable(i) && !Cells[i].HasValue) freeCells++;
            int emptyTableaus = 0;
            for (int i = 0; i < Tableaus.Length; i++) if (Tableaus[i].Count == 0) emptyTableaus++;
            // Destination empty consumes one empty tableau (cannot be used as staging)
            bool destEmpty = dList.Count == 0;
            int usableEmpties = emptyTableaus - (destEmpty ? 1 : 0);
            if (usableEmpties < 0) usableEmpties = 0;
            long capacity = (long)(freeCells + 1);
            for (int k = 0; k < usableEmpties; k++) capacity *= 2L;
            if (capacity < 1) capacity = 1;
            int maxByBuffer = (int)Math.Min(int.MaxValue, capacity);

            // If destination not empty, require bottom-of-slice to be placeable
            if (dList.Count > 0)
            {
                int best = 0;
                int kMax = Math.Min(serial, maxByBuffer);
                for (int k = kMax; k >= 1; k--)
                {
                    var bottom = sList[sList.Count - k];
                    if (CanPlaceOnTableau(dList, bottom)) { best = k; break; }
                }
                return best;
            }
            else
            {
                // Destination empty: any sequence length up to min(serial, buffer)
                return Math.Min(serial, maxByBuffer);
            }
        }
    }
}