using Solitaire.Core;

namespace Solitaire.FreeCell
{
    public enum MoveKind
    {
        TableauToCell,
        CellToTableau,
        TableauToFoundation,
        CellToFoundation,
        TableauToTableau,
        FoundationToTableau,   // 추가
        FoundationToCell       // 추가
    }

    public readonly struct Index
    {
        public int Value { get; }
        public Index(int v) { Value = v; }
        public override string ToString() => Value.ToString();
    }

    public readonly struct Move
    {
        public MoveKind Kind { get; }
        public int From { get; }
        public int To { get; }
        /// <summary>For TableauToTableau sequences; Phase 2 uses 1.</summary>
        public int Count { get; }

        public Move(MoveKind kind, int from, int to, int count = 1)
        {
            Kind = kind; From = from; To = to; Count = count;
        }
        public override string ToString() => $"{Kind} {From}->{To} x{Count}";
    }

    internal static class SuitIndex
    {
        public static int ToIndex(Suit s)
        {
            switch (s)
            {
                case Suit.Spade: return 0;
                case Suit.Heart: return 1;
                case Suit.Diamond: return 2;
                case Suit.Club: return 3;
                default: return 0;
            }
        }
    }
}
