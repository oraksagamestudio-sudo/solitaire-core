using System;
using System.Linq;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    partial class Program
    {
        // Rewind to the move that placed current foundation top, then branch to Tableau/Cell
        static void FoundationPop(string destType, int foundationIndex, int destIndex)
        {
            if (_fc == null) throw new InvalidOperationException("No FreeCell game.");
            if (foundationIndex < 0 || foundationIndex > 3) throw new ArgumentOutOfRangeException(nameof(foundationIndex));

            if (destType == "t")
            {
                if (destIndex < 0 || destIndex >= _currentConfig.Tableaus) throw new ArgumentOutOfRangeException(nameof(destIndex));
            }
            else if (destType == "c")
            {
                if (destIndex < 0 || destIndex >= _currentConfig.Cells) throw new ArgumentOutOfRangeException(nameof(destIndex));
            }
            else throw new ArgumentException("destType must be 't' or 'c'");

            var topRank = _fc.FoundationTop[foundationIndex];
            if (topRank == 0)
            {
                Console.WriteLine("[F-POP] Foundation " + foundationIndex + " is empty.");
                return;
            }

            int idx = -1;
            for (int i = _log.Count - 1; i >= 0; i--)
            {
                var m = _log[i];
                if ((m.Kind == MoveKind.TableauToFoundation || m.Kind == MoveKind.CellToFoundation) && m.To == foundationIndex)
                { idx = i; break; }
            }
            if (idx < 0)
            {
                Console.WriteLine("[F-POP] Cannot locate the move that placed the current top on foundation " + foundationIndex + ".");
                return;
            }

            var s2 = FreeCellState.NewGame(_currentSeed, _currentConfig);
            for (int i = 0; i < idx; i++) s2 = (FreeCellState)s2.Apply(_log[i]);
            var placingMove = _log[idx];

            Move branch;
            bool hasBranch = true;
            if (destType == "t")
            {
                if (placingMove.Kind == MoveKind.TableauToFoundation)
                    branch = new Move(MoveKind.TableauToTableau, placingMove.From, destIndex, 1);
                else
                    branch = new Move(MoveKind.CellToTableau, placingMove.From, destIndex, 1);
            }
            else
            {
                if (placingMove.Kind == MoveKind.TableauToFoundation)
                    branch = new Move(MoveKind.TableauToCell, placingMove.From, destIndex, 1);
                else
                {
                    if (placingMove.From != destIndex)
                    {
                        Console.WriteLine("[F-POP] The card originally came from cell " + placingMove.From + ". Can only return to the same cell.");
                        return;
                    }
                    hasBranch = false;
                    branch = new Move(MoveKind.TableauToCell, 0, 0, 0);
                }
            }

            if (hasBranch)
            {
                bool legal = s2.GetLegalMoves().Any(m => m.Kind == branch.Kind && m.From == branch.From && m.To == branch.To && m.Count == branch.Count);
                if (!legal)
                {
                    Console.WriteLine("[F-POP] Illegal branch move at that point in history: " + branch.ToString());
                    return;
                }
            }

            int dropped = _log.Count - idx;
            _log.RemoveRange(idx, dropped);
            _fc = s2;
            if (hasBranch)
            {
                _fc = (FreeCellState)_fc.Apply(branch);
                _log.Add(branch);
            }

            Console.WriteLine("[F-POP] Rewound " + dropped + " move(s) and branched " + (hasBranch ? "with: " + branch.ToString() : "(no-op to original cell)"));
            // temp: close if empty
            MaybeCloseTempCell();
            DumpFreeCell(_fc);
        }
    }
}