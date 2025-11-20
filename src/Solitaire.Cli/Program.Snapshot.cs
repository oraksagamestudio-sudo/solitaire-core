using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    partial class Program
    {
        static readonly string RankMap = "a23456789tjqk"; // 1..13
        static readonly string SuitMap = "shdc";          // 0..3

        static string EncodeCard(Card c)
        {
            int r = (int)c.Rank;
            int s = SuitIndex(c.Suit);
            char rc = RankMap[r - 1];
            char sc = SuitMap[s];
            return new string(new char[] { rc, sc });
        }

        static Card DecodeCard2(string code2)
        {
            if (string.IsNullOrEmpty(code2) || code2.Length != 2)
                throw new ArgumentException("invalid card code: " + code2);
            char rc = code2[0];
            char sc = code2[1];

            int r = RankMap.IndexOf(char.ToLowerInvariant(rc)) + 1;
            if (r <= 0) throw new ArgumentException("invalid rank: " + rc);
            int s = SuitMap.IndexOf(char.ToLowerInvariant(sc));
            if (s < 0) throw new ArgumentException("invalid suit: " + sc);

            Suit su = s == 0 ? Suit.Spade : (s == 1 ? Suit.Heart : (s == 2 ? Suit.Diamond : Suit.Club));
            Rank rk = (Rank)r;
            return new Card(su, rk);
        }

        static string EncodePile(List<Card> pile)
        {
            var sb = new StringBuilder(pile.Count * 2);
            for (int i = 0; i < pile.Count; i++) sb.Append(EncodeCard(pile[i])); // bottom..top
            return sb.ToString();
        }

        static List<Card> DecodePile(string encoded)
        {
            var list = new List<Card>();
            if (string.IsNullOrEmpty(encoded)) return list;
            if ((encoded.Length % 2) != 0) throw new ArgumentException("bad pile length");
            for (int i = 0; i < encoded.Length; i += 2)
            {
                string c2 = encoded.Substring(i, 2);
                list.Add(DecodeCard2(c2));
            }
            return list; // bottom..top
        }

        static string EncodeFoundationTop(int suitIndex, int topRank)
        {
            if (topRank <= 0) return "";
            char rc = RankMap[topRank - 1];
            char sc = SuitMap[suitIndex];
            return new string(new char[] { rc, sc });
        }

        static int DecodeFoundationRank(string top2, int expectSuitIndex)
        {
            if (string.IsNullOrEmpty(top2)) return 0;
            if (top2.Length != 2) throw new ArgumentException("bad foundation code");
            int r = RankMap.IndexOf(char.ToLowerInvariant(top2[0])) + 1;
            int s = SuitMap.IndexOf(char.ToLowerInvariant(top2[1]));
            if (r <= 0 || s < 0) throw new ArgumentException("bad foundation code");
            if (s != expectSuitIndex)
                throw new ArgumentException("foundation suit mismatch: " + top2);
            return r;
        }

        static ReplaySnapshot MakeSnapshot(FreeCellState s)
        {
            var snap = new ReplaySnapshot();
            snap.tableaus = new string[s.Tableaus.Length];
            for (int i = 0; i < s.Tableaus.Length; i++)
                snap.tableaus[i] = EncodePile(s.Tableaus[i]);

            snap.cells = new string[s.Cells.Length];
            for (int i = 0; i < s.Cells.Length; i++)
                snap.cells[i] = s.Cells[i].HasValue ? EncodeCard(s.Cells[i].Value) : "";

            snap.foundations = new string[4];
            for (int suit = 0; suit < 4; suit++)
                snap.foundations[suit] = EncodeFoundationTop(suit, s.FoundationTop[suit]);

            snap.moveCount = s.MoveCount;

            // items & temp
            snap.tempActive = _tempActive;
            snap.tempCellIndex = _tempCellIndex;
            snap.tempCharges = _tempCharges;
            snap.grabCharges = _grabCharges;
            snap.siphonCharges = _siphonCharges;

            return snap;
        }

        static FreeCellState BuildFromSnapshot(uint seed, FreeCellConfig cfg, ReplaySnapshot snap)
        {
            if (snap == null) throw new ArgumentNullException("snap");
            if (snap.tableaus == null || snap.tableaus.Length != cfg.Tableaus)
                throw new InvalidOperationException("snapshot.tableaus size mismatch");

            var t = new List<Card>[cfg.Tableaus];
            for (int i = 0; i < t.Length; i++)
                t[i] = DecodePile(i < snap.tableaus.Length ? snap.tableaus[i] : "");

            var cells = new Card?[cfg.Cells];
            for (int i = 0; i < cells.Length; i++)
            {
                string code = (snap.cells != null && i < snap.cells.Length) ? snap.cells[i] : "";
                cells[i] = string.IsNullOrEmpty(code) ? (Card?)null : DecodeCard2(code);
            }

            var fTop = new int[4];
            for (int suit = 0; suit < 4; suit++)
            {
                string code = (snap.foundations != null && suit < snap.foundations.Length) ? snap.foundations[suit] : "";
                fTop[suit] = DecodeFoundationRank(code, suit);
            }

            int moveCount = snap.moveCount;

            // Construct state directly (no reflection)
            var s = new FreeCellState(seed, cfg, t, cells, fTop, moveCount);

            // restore items & temp
            _tempActive = snap.tempActive;
            _tempCellIndex = snap.tempCellIndex;
            _tempCharges = snap.tempCharges;
            _grabCharges = snap.grabCharges;
            _siphonCharges = snap.siphonCharges;

            return s;
        }
    }
}