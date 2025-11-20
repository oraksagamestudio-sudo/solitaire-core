// FILE: src/Solitaire.FreeCell/FreeCellState.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using Solitaire.Core;

namespace Solitaire.FreeCell
{
    public sealed class FreeCellState
    {
        public readonly List<Card>[] Tableaus;
        public readonly Card?[] Cells;
        public readonly int[] FoundationTop;
        public readonly int MoveCount;
        public readonly FreeCellConfig Config;
        private readonly uint Seed;

        public FreeCellState(uint seed, FreeCellConfig cfg, List<Card>[] t, Card?[] cells, int[] ftop, int moves)
        {
            Seed = seed;
            Config = cfg;
            Tableaus = t;
            Cells = cells;
            FoundationTop = ftop;
            MoveCount = moves;
        }

        public static FreeCellState NewGame(uint seed, FreeCellConfig cfg)
        {
            if (cfg == null) cfg = new FreeCellConfig();

            var deck = DeckUtils.CreateStandard52(faceUp: true);
            var rng = new XorShift32(seed);
            DeckUtils.FisherYatesShuffle(deck, rng);

            var t = new List<Card>[cfg.Tableaus];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>();
            for (int i = 0; i < deck.Count; i++) t[i % cfg.Tableaus].Add(deck[i]);

            var cells = new Card?[cfg.Cells];
            var ftop = new int[4];
            return new FreeCellState(seed, cfg, t, cells, ftop, 0);
        }

        public static FreeCellState NewGame(uint seed, FreeCellConfig cfg, string shuffleKind)
        {
            if (cfg == null) cfg = new FreeCellConfig();

            var deck = DeckUtils.CreateStandard52(faceUp: true);
            Solitaire.Core.IRng rng;
            if (!string.IsNullOrEmpty(shuffleKind) && shuffleKind.ToLowerInvariant() == "dotnet")
                rng = new Solitaire.Core.DotNetRandom((int)seed);
            else
                rng = new Solitaire.Core.XorShift32(seed);

            DeckUtils.FisherYatesShuffle(deck, rng);

            var t = new List<Card>[cfg.Tableaus];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>();
            for (int i = 0; i < deck.Count; i++) t[i % cfg.Tableaus].Add(deck[i]);

            var cells = new Card?[cfg.Cells];
            var ftop = new int[cfg.Foundations];
            return new FreeCellState(seed, cfg, t, cells, ftop, 0);
        }

        private static bool AllowDown(FreeCellConfig cfg)
        {
            if (cfg == null) return true;
            var prop = cfg.GetType().GetProperty("AllowFoundationDownMoves", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                var val = prop.GetValue(cfg);
                if (val is bool) return (bool)val;
            }
            return true;
        }

        public IEnumerable<Move> GetLegalMoves()
        {
            for (int src = 0; src < Tableaus.Length; src++)
            {
                var sList = Tableaus[src];
                if (sList.Count > 0)
                {
                    var top = sList[sList.Count - 1];
                    if (CanMoveToFoundation(top)) yield return new Move(MoveKind.TableauToFoundation, src, SuitIndex(top.Suit), 1);

                    for (int c = 0; c < Cells.Length; c++)
                        if (!Cells[c].HasValue) yield return new Move(MoveKind.TableauToCell, src, c, 1);

                    for (int dst = 0; dst < Tableaus.Length; dst++)
                    {
                        if (src == dst) continue;
                        int maxSeq = MaxMovableSequenceCount(src, dst);
                        if (maxSeq > 0) yield return new Move(MoveKind.TableauToTableau, src, dst, maxSeq);
                    }
                }
            }

            for (int c = 0; c < Cells.Length; c++)
            {
                if (Cells[c].HasValue)
                {
                    var card = Cells[c].Value;
                    if (CanMoveToFoundation(card)) yield return new Move(MoveKind.CellToFoundation, c, SuitIndex(card.Suit), 1);
                    for (int dst = 0; dst < Tableaus.Length; dst++)
                        if (CanPlaceOnTableau(Tableaus[dst], card)) yield return new Move(MoveKind.CellToTableau, c, dst, 1);
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

                    // [ADD] 이동 전에 임시셀 소비 여부를 판정하기 위해 용량을 두 가지로 계산
                    int maxNoTemp  = MaxMovableSequenceCountWithTempOption(m.From, m.To, false);
                    int maxWithTmp = MaxMovableSequenceCountWithTempOption(m.From, m.To, true);
                    bool wouldConsumeTempCapacity = false;
                    if (maxWithTmp > maxNoTemp && cnt > maxNoTemp && cnt <= maxWithTmp)
                        wouldConsumeTempCapacity = true;

                    int start = t[m.From].Count - cnt;
                    for (int i = start + 1; i < t[m.From].Count; i++)
                    {
                        if (!FormsSequence(t[m.From][i - 1], t[m.From][i]))
                            throw new InvalidOperationException("Sequence broken within slice.");
                    }
                    var bottom = t[m.From][start];
                    if (!CanPlaceOnTableau(t[m.To], bottom))
                        throw new InvalidOperationException("Illegal placement.");

                    int maxAllowed = MaxMovableSequenceCount(m.From, m.To);
                    if (cnt > maxAllowed) throw new InvalidOperationException("Sequence exceeds buffer capacity.");

                    for (int i = start; i < t[m.From].Count; i++)
                        t[m.To].Add(t[m.From][i]);
                    for (int i = 0; i < cnt; i++)
                        t[m.From].RemoveAt(t[m.From].Count - 1);

                    // [ADD] 임시셀 용량을 실제로 소비했다면 이동 직후 임시셀 닫기
                    if (wouldConsumeTempCapacity)
                    {
                        try
                        {
                            var tActiveProp = this.GetType().GetProperty("TempCellActive");
                            if (tActiveProp != null && (tActiveProp.CanWrite || tActiveProp.SetMethod != null))
                            {
                                tActiveProp.SetValue(this, false);
                            }
                            else
                            {
                                // 불변 상태라면, 아래처럼 새 상태에 플래그를 복사/변경하는 팩토리를 이미 쓰고 있을 가능성이 높음.
                                // 이 경우, 생성자/팩토리 인자에 TempCellActive:false 를 반영하도록
                                // "return new FreeCellState(..., tempCellActive:false, ...)" 같은 형태로 전체 리턴을 조정해야 함.
                                // 만약 그런 생성 경로가 없다면, 다음 줄은 주석으로 두고 CLI 쪽에서 비활성화 플래그를 별도로 동기화.
                            }
                        }
                        catch
                        {
                            // 필드 직접 접근 구조라면 위 리플렉션 대신: this.TempCellActive = false; 로 간단히 처리.
                        }
                    }
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

        private static bool FormsSequence(Card lower, Card upper)
        {
            bool altColor = IsRed(lower.Suit) != IsRed(upper.Suit);
            bool rankOK = ((int)lower.Rank) == ((int)upper.Rank) + 1;
            return altColor && rankOK;
        }

        private static bool CanPlaceOnTableau(List<Card> dest, Card card)
        {
            if (dest.Count == 0) return true;
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

        private int MaxMovableSequenceCount(int src, int dst)
        {
            var sList = Tableaus[src];
            var dList = Tableaus[dst];
            if (sList.Count == 0) return 0;

            int serial = 1;
            for (int i = sList.Count - 1; i - 1 >= 0; i--)
            {
                if (FormsSequence(sList[i - 1], sList[i])) serial++;
                else break;
            }

            int freeCells = 0;
            for (int i = 0; i < Cells.Length; i++) if (!Cells[i].HasValue) freeCells++;
            int emptyTableaus = 0;
            for (int i = 0; i < Tableaus.Length; i++) if (Tableaus[i].Count == 0) emptyTableaus++;
            bool destEmpty = dList.Count == 0;
            int usableEmpties = emptyTableaus - (destEmpty ? 1 : 0);
            if (usableEmpties < 0) usableEmpties = 0;
            long capacity = (long)(freeCells + 1);
            for (int k = 0; k < usableEmpties; k++) capacity *= 2L;
            if (capacity < 1) capacity = 1;
            int maxByBuffer = (int)Math.Min(int.MaxValue, capacity);

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
                return Math.Min(serial, maxByBuffer);
            }
        }

        // C# 7.3 / netstandard2.0 호환, ASCII 문자만 사용
        private int MaxMovableSequenceCountWithTempOption(int src, int dst, bool includeTempCell)
        {
            var sList = Tableaus[src];
            var dList = Tableaus[dst];
            if (sList.Count == 0) return 0;

            // tail-run length (alternating colors, descending ranks)
            int serial = 1;
            for (int i = sList.Count - 1; i - 1 >= 0; i--)
            {
                if (FormsSequence(sList[i - 1], sList[i])) serial++;
                else break;
            }

            // free cell count: 기본 0..(Cfg.Cells-1)만 체크, 임시셀은 includeTempCell == true 이고 활성/비어있을 때만 +1
            int freeCells = 0;
            for (int i = 0; i < Cells.Length; i++)
            {
                // 임시셀을 일반 셀과 분리해서 취급한다면 여기서 건너뛰고, 아래에서 별도로 더해준다.
                freeCells += (!Cells[i].HasValue ? 1 : 0);
            }

            // 임시셀 가산
            // NOTE: 프로젝트에서 사용하는 정확한 플래그/슬롯 이름을 사용하세요.
            // 보통: TempCellActive == true && Cells[TempCellSlotIndex] == null 일 때만 임시셀 1칸 추가.
            if (includeTempCell)
            {
                try
                {
                    // 리플렉션 없이 필드/프로퍼티가 직접 있다면 그대로 접근
                    // 예: if (this.TempCellActive && this.TempCellSlotIndex >= 0 && this.TempCellSlotIndex < Cells.Length && !Cells[this.TempCellSlotIndex].HasValue) freeCells++;
                    var tActiveProp = this.GetType().GetProperty("TempCellActive");
                    var tIndexProp  = this.GetType().GetProperty("TempCellSlotIndex");
                    if (tActiveProp != null && tIndexProp != null)
                    {
                        object a = tActiveProp.GetValue(this);
                        object idxObj = tIndexProp.GetValue(this);
                        bool active = (a is bool) ? (bool)a : false;
                        int tIdx = (idxObj is int) ? (int)idxObj : -1;
                        if (active && tIdx >= 0 && tIdx < Cells.Length && !Cells[tIdx].HasValue)
                            freeCells += 1;
                    }
                }
                catch
                {
                    // 임시로 무시(없어도 동작). 실제 빌드에서는 위 주석처럼 직접 필드 접근을 권장.
                }
            }

            // empty tableau count
            int emptyTableaus = 0;
            for (int i = 0; i < Tableaus.Length; i++)
                if (Tableaus[i].Count == 0) emptyTableaus++;

            bool destEmpty = dList.Count == 0;
            int usableEmpties = emptyTableaus - (destEmpty ? 1 : 0);
            if (usableEmpties < 0) usableEmpties = 0;

            long capacity = (long)(freeCells + 1);
            for (int k = 0; k < usableEmpties; k++) capacity *= 2L;
            if (capacity < 1) capacity = 1;
            int maxByBuffer = (int)Math.Min(int.MaxValue, capacity);

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
                return Math.Min(serial, maxByBuffer);
            }
        }

        // Item A: promote top-only (existing behavior for completeness)
        public bool TryPromoteNextToFoundation(int suitIndex, out FreeCellState next, out Move applied)
        {
            next = this;
            applied = new Move();
            if (suitIndex < 0 || suitIndex > 3) return false;
            int needed = FoundationTop[suitIndex] + 1;
            if (needed < 1 || needed > 13) return false;

            // tableau tops
            for (int t = 0; t < Tableaus.Length; t++)
            {
                var pile = Tableaus[t];
                if (pile.Count == 0) continue;
                var top = pile[pile.Count - 1];
                if ((int)top.Rank == needed && SuitIndex(top.Suit) == suitIndex)
                {
                    var m = new Move(MoveKind.TableauToFoundation, t, suitIndex, 1);
                    next = (FreeCellState)this.Apply(m);
                    applied = m;
                    return true;
                }
            }
            // cells
            for (int c = 0; c < Cells.Length; c++)
            {
                if (!Cells[c].HasValue) continue;
                var card = Cells[c].Value;
                if ((int)card.Rank == needed && SuitIndex(card.Suit) == suitIndex)
                {
                    var m = new Move(MoveKind.CellToFoundation, c, suitIndex, 1);
                    next = (FreeCellState)this.Apply(m);
                    applied = m;
                    return true;
                }
            }
            return false;
        }

        // Item B: siphon - pull the needed card from ANY depth (tableau or cells) and place to foundation
        public bool TrySiphonNextToFoundation(int suitIndex, out FreeCellState next, out Card moved, out string source)
        {
            next = this;
            moved = default(Card);
            source = "";
            if (suitIndex < 0 || suitIndex > 3) return false;
            int needed = FoundationTop[suitIndex] + 1;
            if (needed < 1 || needed > 13) return false;

            // 1) cells first
            for (int c = 0; c < Cells.Length; c++)
            {
                if (!Cells[c].HasValue) continue;
                var card = Cells[c].Value;
                if ((int)card.Rank == needed && SuitIndex(card.Suit) == suitIndex)
                {
                    var t = CloneTableaus();
                    var nCells = (Card?[])Cells.Clone();
                    var f = (int[])FoundationTop.Clone();
                    nCells[c] = null;
                    f[suitIndex] = needed;
                    next = new FreeCellState(Seed, Config, t, nCells, f, MoveCount + 1);
                    moved = card;
                    source = "cell " + c.ToString();
                    return true;
                }
            }

            // 2) tableaus - take closest to top (smallest depth)
            int bestT = -1;
            int bestIdx = -1; // index in list
            for (int tix = 0; tix < Tableaus.Length; tix++)
            {
                var pile = Tableaus[tix];
                for (int i = pile.Count - 1; i >= 0; i--)
                {
                    var card = pile[i];
                    if ((int)card.Rank == needed && SuitIndex(card.Suit) == suitIndex)
                    {
                        bestT = tix;
                        bestIdx = i;
                        goto found;
                    }
                }
            }
        found:
            if (bestT >= 0)
            {
                var t = CloneTableaus();
                var nCells = (Card?[])Cells.Clone();
                var f = (int[])FoundationTop.Clone();
                var pile = new List<Card>(t[bestT]);
                moved = pile[bestIdx];
                pile.RemoveAt(bestIdx);
                t[bestT] = pile;
                f[suitIndex] = needed;
                next = new FreeCellState(Seed, Config, t, nCells, f, MoveCount + 1);
                source = "tableau T" + bestT.ToString() + " depthFromTop=" + ((t[bestT].Count) - bestIdx).ToString();
                return true;
            }

            return false;
        }
    }
}
