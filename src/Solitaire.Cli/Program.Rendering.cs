using System;
using System.Linq;
using System.Text;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    partial class Program
    {
        // ===== Alignment helpers (ANSI-aware) =====
        static int VisibleLen(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int len = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\x1b' && i + 1 < s.Length && s[i + 1] == '[')
                {
                    int j = i + 2;
                    while (j < s.Length && (char.IsDigit(s[j]) || s[j] == ';')) j++;
                    if (j < s.Length && s[j] == 'm') { i = j; continue; }
                }
                len++;
            }
            return len;
        }

        static string RightPadVisible(string s, int width)
        {
            int v = VisibleLen(s);
            if (v >= width) return s;
            return s + new string(' ', width - v);
        }

        // --- Pretty board (ANSI) ---
        static void DumpBoardPretty(FreeCellState s)
        {
            const int CW = 4;
            string Reset = "\x1b[0m";
            string Red = "\x1b[31m";
            string Bold = "\x1b[1m";

            Console.WriteLine(Bold + "Moves=" + s.MoveCount + Reset);

            var suits = new [] { Suit.Spade, Suit.Heart, Suit.Diamond, Suit.Club };
            var sb = new StringBuilder();
            sb.Append("Foundations: ");
            for (int i = 0; i < 4; i++)
            {
                var rank = s.FoundationTop[i];
                string name = rank == 0 ? "-" : RankShort((Rank)rank);
                string suit = SuitSymbol(suits[i]);
                bool red = (suits[i] == Suit.Heart || suits[i] == Suit.Diamond);
                sb.Append("[" + (red ? Red : "") + suit + Reset + ":" + name + "] ");
            }
            Console.WriteLine(sb.ToString());

            var csb = new StringBuilder();
            csb.Append("Cells: ");
            for (int i = 0; i < s.Cells.Length; i++)
            {
                string token = s.Cells[i].HasValue ? RenderCardShort(s.Cells[i].Value, true) : "--";
                csb.Append(RightPadVisible(token, CW) + "  ");
            }
            Console.WriteLine(csb.ToString());

            Console.WriteLine("Items: TempCell: " + (_tempActive ? "ACTIVE" : "INACTIVE") + " (idx " + _tempCellIndex + "), charges=" + _tempCharges +
                              " | Grab charges=" + _grabCharges + " | Siphon charges=" + _siphonCharges);

            var hb = new StringBuilder();
            for (int col = 0; col < s.Tableaus.Length; col++)
            {
                string label = "T" + col;
                hb.Append("  " + RightPadVisible(label, CW));
            }
            Console.WriteLine(Bold + hb.ToString() + Reset);

            int maxH = 0;
            for (int i = 0; i < s.Tableaus.Length; i++) if (s.Tableaus[i].Count > maxH) maxH = s.Tableaus[i].Count;

            for (int r = 0; r < maxH; r++)
            {
                var line = new StringBuilder();
                for (int col = 0; col < s.Tableaus.Length; col++)
                {
                    var pile = s.Tableaus[col];
                    int idx = r;
                    string cell = (idx < pile.Count) ? RenderCardShort(pile[idx], true) : "";
                    line.Append("  " + RightPadVisible(cell, CW));
                }
                Console.WriteLine(line.ToString());
            }
        }

        static string RenderCardShort(Card c, bool ansi)
        {
            string suit = SuitSymbol(c.Suit);
            string r = RankShort(c.Rank);
            bool red = (c.Suit == Suit.Heart || c.Suit == Suit.Diamond);
            string Reset = "\x1b[0m";
            string Red = "\x1b[31m";
            if (ansi && red) return Red + r + suit + Reset;
            return r + suit;
        }

        static string SuitSymbol(Suit s)
        {
            switch (s)
            {
                case Suit.Spade: return "♠";
                case Suit.Heart: return "♥";
                case Suit.Diamond: return "♦";
                case Suit.Club: return "♣";
            }
            return "?";
        }

        static string RankShort(Rank r)
        {
            int v = (int)r;
            if (v == 1) return "A";
            if (v >= 2 && v <= 10) return v.ToString();
            if (v == 11) return "J";
            if (v == 12) return "Q";
            if (v == 13) return "K";
            return "?";
        }
    }
}