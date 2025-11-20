using System;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    partial class Program
    {
        private static bool _gameFinished = false;

        static bool IsVictory(FreeCellState s)
        {
            if (s == null) return false;
            // All four foundations top to 13 (K)
            for (int i = 0; i < 4; i++)
                if (s.FoundationTop[i] != 13) return false;
            return true;
        }

        // Detect & announce once. Does not block further moves.
        static void RecalcVictoryAndAnnounce(FreeCellState s)
        {
            bool win = IsVictory(s);
            if (win && !_gameFinished)
            {
                _gameFinished = true;
                Console.WriteLine("[WIN] All foundations complete. Game is finished, but you can keep moving.");
            }
            else if (!win && _gameFinished)
            {
                _gameFinished = false; // e.g., after foundation-pop rewind
            }
        }
    }
}