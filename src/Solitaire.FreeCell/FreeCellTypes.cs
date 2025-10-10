using System;

namespace Solitaire.FreeCell
{
    public enum MoveKind
    {
        TableauToTableau = 1,
        TableauToCell = 2,
        CellToTableau = 3,
        TableauToFoundation = 4,
        CellToFoundation = 5,
        FoundationToTableau = 6, // NEW
        FoundationToCell = 7     // NEW
    }

    public struct Move
    {
        public MoveKind Kind;
        public int From;
        public int To;
        public int Count;

        public Move(MoveKind kind, int from, int to, int count)
        {
            this.Kind = kind;
            this.From = from;
            this.To = to;
            this.Count = count;
        }

        public override string ToString()
        {
            return Kind.ToString() + " " + From + "->" + To + " x" + Count;
        }
    }
}
