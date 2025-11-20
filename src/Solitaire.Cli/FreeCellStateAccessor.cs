using System;
using System.Collections.Generic;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    internal sealed class FreeCellStateAccessor
    {
        private readonly FreeCellState _src;
        public FreeCellStateAccessor(FreeCellState src) { _src = src; }

        public FreeCellState WithTableaus(List<Card>[] t)
        {
            return new FreeCellState(
                GetField<uint>(_src, "Seed"),
                _src.Config,
                t,
                (Card?[])_src.Cells.Clone(),
                (int[])_src.FoundationTop.Clone(),
                _src.MoveCount
            );
        }

        public FreeCellState WithConfigAndCells(FreeCellConfig cfg, Card?[] cells)
        {
            var t = new List<Card>[_src.Tableaus.Length];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Card>(_src.Tableaus[i]);
            return new FreeCellState(
                GetField<uint>(_src, "Seed"),
                cfg,
                t,
                cells,
                (int[])_src.FoundationTop.Clone(),
                _src.MoveCount
            );
        }

        private static T GetField<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f == null) throw new InvalidOperationException("Internal layout changed.");
            return (T)f.GetValue(o);
        }
    }
}