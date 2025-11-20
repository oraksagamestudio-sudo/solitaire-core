using System;
using System.Linq;
using System.Collections.Generic;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    partial class Program
    {
        // Parse a step as a real move (supports legacy format)
        static bool TryParseMoveFromReplay(ReplayMove rm, out Move move)
        {
            if (string.Equals(rm.kind, "move", StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<MoveKind>(rm.op ?? "", true, out var mk))
                {
                    move = new Move();
                    return false;
                }
                move = new Move(mk, rm.from, rm.to, rm.count > 0 ? rm.count : 1);
                return true;
            }
            if (Enum.TryParse<MoveKind>(rm.kind ?? "", true, out var legacyMk))
            {
                move = new Move(legacyMk, rm.from, rm.to, rm.count > 0 ? rm.count : 1);
                return true;
            }
            move = new Move();
            return false;
        }

        static bool IsMoveStep(ReplayMove rm)
        {
            if (rm == null) return false;
            if (string.Equals(rm.kind, "move", StringComparison.OrdinalIgnoreCase)) return true;
            return Enum.TryParse<MoveKind>(rm.kind ?? "", true, out _); // legacy
        }

        static Move ToMove(ReplayMove rm)
        {
            if (string.Equals(rm.kind, "move", StringComparison.OrdinalIgnoreCase))
            {
                var mk = (MoveKind)Enum.Parse(typeof(MoveKind), rm.op, true);
                return new Move(mk, rm.from, rm.to, rm.count > 0 ? rm.count : 1);
            }
            else
            {
                var mk = (MoveKind)Enum.Parse(typeof(MoveKind), rm.kind, true); // legacy
                return new Move(mk, rm.from, rm.to, rm.count > 0 ? rm.count : 1);
            }
        }

        // Helper: find the first real move in steps for shuffle detection
        static bool TryGetFirstMoveForShuffle(IReadOnlyList<ReplayMove> steps, out Move m0)
        {
            if (steps != null)
            {
                foreach (var st in steps)
                {
                    if (TryParseMoveFromReplay(st, out var mv))
                    {
                        m0 = mv;
                        return true;
                    }
                }
            }
            m0 = new Move();
            return false;
        }

        // Helper for replaying/undoing a recorded Grab step (supports negative sel)
        private static bool TryComputeGrabFromIndex(List<Card> pile, int sel, out int fromIndex)
        {
            fromIndex = -1;
            if (pile == null || pile.Count == 0) return false;

            if (sel >= 0)
            {
                // from top: 0 = top, 1 = one below top ...
                if (sel >= pile.Count) return false;
                fromIndex = pile.Count - 1 - sel;
                return true;
            }
            else
            {
                // from bottom: -1 = bottom, -2 = one above bottom ...
                int idxFromBottom = (-sel) - 1;
                if (idxFromBottom < 0 || idxFromBottom >= pile.Count) return false;
                fromIndex = idxFromBottom;
                return true;
            }
        }
    }
}