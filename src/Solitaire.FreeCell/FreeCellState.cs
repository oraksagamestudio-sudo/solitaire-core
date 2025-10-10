using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Solitaire.Core;

namespace Solitaire.FreeCell
{
    public sealed class FreeCellState
    {
        public readonly List<Card>[] Tableaus;
        public readonly Card?[] Cells;
        public readonly int[] FoundationTop; // 0=Spade,1=Heart,2=Diamond,3=Club ; value: highest rank (0 if empty)
        public readonly int MoveCount;
        public readonly FreeCellConfig Config;
        private readonly uint Seed;

        private FreeCellState(uint seed, FreeCellConfig cfg, List<Card>[] t, Card?[] cells, int[] ftop, int moves)
        {
            this.Seed = seed;
            this.Config = cfg;
            this.Tableaus = t;
            this.Cells = cells;
            this.FoundationTop = ftop;
            this.MoveCount = moves;
        }

        public static FreeCellState NewGame(uint seed, FreeCellConfig cfg)
        {
            var deck = MakeShuffledDeck(seed);
            var t = new List<Card>[cfg.Tableaus];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>();
            for (int i = 0; i < deck.Count; i++)
            {
                t[i % cfg.Tableaus].Add(deck[i]);
            }
            var cells = new Card?[cfg.Cells];
            var ftop = new int[4];
            return new FreeCellState(seed, cfg, t, cells, ftop, 0);
        }

        // Local deterministic shuffle to avoid DeckUtils dependency
        private static List<Card> MakeShuffledDeck(uint seed)
        {
            var deck = new List<Card>(52);
            Suit[] suits = new[] { Suit.Spade, Suit.Heart, Suit.Diamond, Suit.Club };
            for (int si = 0; si < suits.Length; si++)
            {
                for (int r = 1; r <= 13; r++)
                {
                    deck.Add(new Card(suits[si], (Rank)r));
                }
            }
            var rnd = new Random(unchecked((int)seed));
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var tmp = deck[i];
                deck[i] = deck[j];
                deck[j] = tmp;
            }
            return deck;
        }

        // --- Compatibility shim: read AllowFoundationDownMoves via reflection (default: true) ---
        private static bool AllowDown(FreeCellConfig cfg)
        {
            if (cfg == null) return true;
            var prop = cfg.GetType().GetProperty("AllowFoundationDownMoves", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                var val = prop.GetValue(cfg);
                if (val is bool b) return b;
            }
            return true; // default permissive if property doesn't exist
        }

        public IEnumerable<Move> GetLegalMoves()
        {
            for (int i = 0; i < Tableaus.Length; i++)
            {
                if (Tableaus[i].Count > 0)
                {
                    var top = Tableaus[i][Tableaus[i].Count - 1];
                    if (CanMoveToFoundation(top)) yield return new Move(MoveKind.TableauToFoundation, i, SuitIndex(top.Suit), 1);

                    for (int c = 0; c < Cells.Length; c++)
                        if (!Cells[c].HasValue) yield return new Move(MoveKind.TableauToCell, i, c, 1);

                    for (int j = 0; j < Tableaus.Length; j++)
                    {
                        if (i == j) continue;
                        if (CanPlaceOnTableau(Tableaus[j], top)) yield return new Move(MoveKind.TableauToTableau, i, j, 1);
                    }
                }
            }

            for (int c = 0; c < Cells.Length; c++)
            {
                if (Cells[c].HasValue)
                {
                    var card = Cells[c].Value;
                    if (CanMoveToFoundation(card)) yield return new Move(MoveKind.CellToFoundation, c, SuitIndex(card.Suit), 1);
                    for (int j = 0; j < Tableaus.Length; j++)
                    {
                        if (CanPlaceOnTableau(Tableaus[j], card)) yield return new Move(MoveKind.CellToTableau, c, j, 1);
                    }
                }
            }

            if (AllowDown(Config))
            {
                for (int f = 0; f < 4; f++)
                {
                    int r = FoundationTop[f];
                    if (r <= 0) continue;
                    var suit = IndexSuit(f);
                    var moving = new Card(suit, (Rank)r);

                    for (int c = 0; c < Cells.Length; c++)
                        if (!Cells[c].HasValue) yield return new Move(MoveKind.FoundationToCell, f, c, 1);

                    for (int j = 0; j < Tableaus.Length; j++)
                    {
                        if (CanPlaceOnTableau(Tableaus[j], moving)) yield return new Move(MoveKind.FoundationToTableau, f, j, 1);
                    }
                }
            }
        }

        public FreeCellState Apply(Move m)
        {
            var t = CloneTableaus();
            var cells = (Card?[])Cells.Clone();
            var ftop = (int[])FoundationTop.Clone();
            int moves = MoveCount + 1;

            switch (m.Kind)
            {
                case MoveKind.TableauToCell:
                {
                    RequireTableauIndex(m.From); RequireCellIndex(m.To);
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
                    if (!cells[m.From].HasValue) throw new InvalidOperationException("Cell empty.");
                    var card = cells[m.From].Value;
                    if (!CanPlaceOnTableau(t[m.To], card)) throw new InvalidOperationException("Illegal placement.");
                    cells[m.From] = null;
                    t[m.To].Add(card);
                    break;
                }
                case MoveKind.TableauToTableau:
                {
                    RequireTableauIndex(m.From); RequireTableauIndex(m.To);
                    if (t[m.From].Count == 0) throw new InvalidOperationException("Empty tableau.");
                    int cnt = Math.Max(1, m.Count);
                    if (cnt > t[m.From].Count) throw new InvalidOperationException("Not enough cards.");
                    if (cnt != 1) throw new InvalidOperationException("Only single card moves supported here.");
                    var card = t[m.From][t[m.From].Count - 1];
                    if (!CanPlaceOnTableau(t[m.To], card)) throw new InvalidOperationException("Illegal placement.");
                    t[m.From].RemoveAt(t[m.From].Count - 1);
                    t[m.To].Add(card);
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
                    if (!cells[m.From].HasValue) throw new InvalidOperationException("Cell empty.");
                    var card = cells[m.From].Value;
                    if (SuitIndex(card.Suit) != m.To) throw new InvalidOperationException("Wrong foundation.");
                    if (!CanMoveToFoundation(card)) throw new InvalidOperationException("Illegal to foundation.");
                    cells[m.From] = null;
                    ftop[m.To] = (int)card.Rank;
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
                    if (cells[m.To].HasValue) throw new InvalidOperationException("Cell not empty.");
                    var card = new Card(IndexSuit(m.From), (Rank)r);
                    ftop[m.From] = r - 1;
                    cells[m.To] = card;
                    break;
                }
                default:
                    throw new NotSupportedException("Unknown move kind: " + m.Kind);
            }

            return new FreeCellState(Seed, Config, t, cells, ftop, moves);
        }

        private List<Card>[] CloneTableaus()
        {
            var t = new List<Card>[Tableaus.Length];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>(Tableaus[i]);
            return t;
        }

        private static bool CanPlaceOnTableau(List<Card> dest, Card card)
        {
            if (dest.Count == 0) return true; // FreeCell: any card allowed on empty column
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

        private static bool IsRed(Suit s)
        {
            return s == Suit.Heart || s == Suit.Diamond;
        }

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
    }
}
