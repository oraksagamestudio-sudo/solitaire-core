using System;
using System.Linq;
using System.Collections.Generic;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    partial class Program
    {
        // ---- Temp capacity detection for multi-move t2t ----
        static bool TempCapacityNeeded(FreeCellState pre, Move m)
        {
            if (!_tempActive) return false;
            if (m.Kind != MoveKind.TableauToTableau || m.Count <= 1) return false;

            if (pre.Cells.Length > _tempCellIndex && pre.Cells[_tempCellIndex].HasValue) return false;

            if (_currentConfig.Cells <= 0 || pre.Cells.Length <= _tempCellIndex) return false;

            var cfg0 = new FreeCellConfig(
                _currentConfig.Cells - 1,
                _currentConfig.Foundations,
                _currentConfig.Tableaus,
                _currentConfig.AllowSequenceMoves
            );

            var cells0 = new Card?[pre.Cells.Length - 1];
            Array.Copy(pre.Cells, cells0, cells0.Length);

            var s0 = new FreeCellStateAccessor(pre).WithConfigAndCells(cfg0, cells0);

            bool legalWithoutTemp = s0.GetLegalMoves().Any(x => x.Kind == m.Kind && x.From == m.From && x.To == m.To && x.Count == m.Count);
            return !legalWithoutTemp;
        }

        // ---- Item application (replay) ----
        static void ActivateTempCellForReplay()
        {
            if (_tempActive) return;
            _tempActive = true;
            _tempEverHeld = false;

            if (_fc.Cells.Length <= _tempCellIndex)
            {
                var cfg2 = new FreeCellConfig(
                    _currentConfig.Cells + 1,
                    _currentConfig.Foundations,
                    _currentConfig.Tableaus,
                    _currentConfig.AllowSequenceMoves
                );

                var cells2 = new Card?[_fc.Cells.Length + 1];
                Array.Copy(_fc.Cells, cells2, _fc.Cells.Length);

                _fc = new FreeCellStateAccessor(_fc).WithConfigAndCells(cfg2, cells2);
                _currentConfig = cfg2;
            }
        }

        static void ApplyGrabForReplay(int tIndex, int sel)
        {
            if (tIndex < 0 || tIndex >= _fc.Tableaus.Length) throw new ArgumentOutOfRangeException(nameof(tIndex));
            var pile = _fc.Tableaus[tIndex];
            int from;
            if (sel >= 0)
            {
                if (sel >= pile.Count) throw new ArgumentOutOfRangeException(nameof(sel));
                from = pile.Count - 1 - sel; // from top
            }
            else
            {
                int idxFromBottom = (-sel) - 1; // -1 = bottom
                if (idxFromBottom < 0 || idxFromBottom >= pile.Count) throw new ArgumentOutOfRangeException(nameof(sel));
                from = idxFromBottom;
            }
            var card = pile[from];
            var newPile = new List<Card>(pile);
            newPile.RemoveAt(from);
            newPile.Add(card);

            var t = new List<Card>[_fc.Tableaus.Length];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>(_fc.Tableaus[i]);
            t[tIndex] = newPile;
            _fc = new FreeCellStateAccessor(_fc).WithTableaus(t);

            MaybeCloseTempCell(false); // grab 자체는 임시 용량 사용 아님
        }

        static bool ApplySiphonForReplay(int suitIndex)
        {
            FreeCellState next;
            Card moved;
            string source;
            if (_fc.TrySiphonNextToFoundation(suitIndex, out next, out moved, out source))
            {
                _fc = next;
                return true;
            }
            return false;
        }

        static bool ApplyReplayItem(ReplayMove rm)
        {
            string op = (rm.op ?? "").ToLowerInvariant();
            switch (op)
            {
                case "temp":
                    ActivateTempCellForReplay();
                    return true;
                case "grab":
                    {
                        // step.from = tableau index, step.arg = sel (>=0 from top, <0 from bottom, -1 = bottom)
                        int tIndex = rm.from;
                        int sel = rm.arg;

                        if (tIndex < 0 || tIndex >= _fc.Tableaus.Length)
                        {
                            Console.WriteLine("[Grab-REPLAY] invalid tableau index.");
                            return false;
                        }

                        var pile = _fc.Tableaus[tIndex];
                        if (!TryComputeGrabFromIndex(pile, sel, out var from))
                        {
                            Console.WriteLine("[Grab-REPLAY] invalid depth sel=" + sel);
                            return false;
                        }

                        var card = pile[from];
                        var newPile = new List<Card>(pile);
                        newPile.RemoveAt(from);
                        newPile.Add(card);

                        var t = new List<Card>[_fc.Tableaus.Length];
                        for (int i = 0; i < t.Length; i++) t[i] = new List<Card>(_fc.Tableaus[i]);
                        t[tIndex] = newPile;

                        _fc = new FreeCellStateAccessor(_fc).WithTableaus(t);
                        return true;
                    }
                case "siphon":
                    return ApplySiphonForReplay(rm.arg);
                default:
                    return false;
            }
        }

        // ---- Temp cell auto-close ----
        // Close temp cell when it has been used then emptied (ever-held) OR
        // when a multi-move consumed capacity and the slot is currently empty.
        static void MaybeCloseTempCell(bool usedCapacity)
        {
            if (!_tempActive || _fc == null) return;

            bool hasSlot = _fc.Cells.Length > _tempCellIndex;
            bool isEmpty = hasSlot && !_fc.Cells[_tempCellIndex].HasValue;

            bool shouldClose = isEmpty && (_tempEverHeld || usedCapacity);
            if (!shouldClose) return;

            var cfg2 = new FreeCellConfig(
                _currentConfig.Cells - 1,
                _currentConfig.Foundations,
                _currentConfig.Tableaus,
                _currentConfig.AllowSequenceMoves
            );

            var cells2 = new Card?[_fc.Cells.Length - 1];
            Array.Copy(_fc.Cells, cells2, cells2.Length);

            _fc = new FreeCellStateAccessor(_fc).WithConfigAndCells(cfg2, cells2);
            _currentConfig = cfg2;

            _tempActive = false;
            _tempEverHeld = false;

            Console.WriteLine("[TempCell] Closed.");
        }
    }
}