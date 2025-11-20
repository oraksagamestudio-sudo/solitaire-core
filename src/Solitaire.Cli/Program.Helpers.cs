using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    partial class Program
    {
        // ---- arg split ----
        static IEnumerable<string> SplitArgs(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine)) yield break;
            int i = 0;
            while (i < commandLine.Length)
            {
                while (i < commandLine.Length && char.IsWhiteSpace(commandLine[i])) i++;
                if (i >= commandLine.Length) yield break;

                if (commandLine[i] == '\"')
                {
                    i++;
                    int start = i;
                    while (i < commandLine.Length && commandLine[i] != '\"') i++;
                    yield return commandLine.Substring(start, i - start);
                    if (i < commandLine.Length && commandLine[i] == '\"') i++;
                }
                else
                {
                    int start = i;
                    while (i < commandLine.Length && !char.IsWhiteSpace(commandLine[i])) i++;
                    yield return commandLine.Substring(start, i - start);
                }
            }
        }

        // ---- move kind aliases ----
        static MoveKind ParseMoveKind(string token)
        {
            string t = token.Trim();
            MoveKind mk;
            if (Enum.TryParse<MoveKind>(t, true, out mk)) return mk;
            switch (t.ToLowerInvariant())
            {
                case "t2t": return MoveKind.TableauToTableau;
                case "t2c": return MoveKind.TableauToCell;
                case "c2t": return MoveKind.CellToTableau;
                case "t2f": return MoveKind.TableauToFoundation;
                case "c2f": return MoveKind.CellToFoundation;
                case "f2t": return MoveKind.FoundationToTableau;
                case "f2c": return MoveKind.FoundationToCell;
            }
            throw new ArgumentException("Unknown move kind: " + token + " (try: t2t, t2c, c2t, t2f, c2f, f2t, f2c)");
        }

        // ---- suit helpers ----
        static int SuitIndex(Suit s)
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

        static int ParseSuitIndex(string token)
        {
            string t = token.Trim().ToLowerInvariant();
            if (t == "s" || t == "spade" || t == "spades") return 0;
            if (t == "h" || t == "heart" || t == "hearts") return 1;
            if (t == "d" || t == "diamond" || t == "diamonds") return 2;
            if (t == "c" || t == "club" || t == "clubs") return 3;
            int idx;
            if (int.TryParse(t, out idx)) return idx;
            return -1;
        }
        private static bool IsTempItemStep(ReplayMove s)
        {
            return s != null
                && string.Equals(s.kind, "item", StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.op, "temp", StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsAnyItemStep(ReplayMove s)
        {
            return s != null && string.Equals(s.kind, "item", StringComparison.OrdinalIgnoreCase);
        }

        static List<int> FindSiphonCandidateSuits(FreeCellState s)
        {
            var list = new List<int>();
            for (int suit = 0; suit < 4; suit++)
            {
                int needed = s.FoundationTop[suit] + 1;
                if (needed < 1 || needed > 13) continue;
                bool found = false;
                // cells
                for (int c = 0; c < s.Cells.Length && !found; c++)
                    if (s.Cells[c].HasValue && (int)s.Cells[c].Value.Rank == needed && SuitIndex(s.Cells[c].Value.Suit) == suit) found = true;
                // tableaus
                for (int t = 0; t < s.Tableaus.Length && !found; t++)
                {
                    var pile = s.Tableaus[t];
                    for (int i = pile.Count - 1; i >= 0; i--)
                    {
                        var card = pile[i];
                        if ((int)card.Rank == needed && SuitIndex(card.Suit) == suit) { found = true; break; }
                    }
                }
                if (found) list.Add(suit);
            }
            return list;
        }

        // ---- replay file IO ----
        static ReplayFile ReadReplay(string path)
        {
            var json = File.ReadAllText(path);
            var rf = JsonSerializer.Deserialize<ReplayFile>(json);
            if (rf == null) throw new InvalidOperationException("Invalid replay file.");
            if (rf.gameId != "FreeCell") throw new InvalidOperationException("Unsupported gameId: " + rf.gameId);
            if (rf.version != 1) throw new InvalidOperationException("Unsupported version: " + rf.version);
            if (rf.config == null) rf.config = new ReplayConfig();
            if (rf.moves == null) rf.moves = new List<ReplayMove>();
            if (rf.tags == null) rf.tags = new List<string>();
            if (rf.metadata == null) rf.metadata = new Dictionary<string,string>();
            if (rf.createdAt == null) rf.createdAt = "";
            if (rf.notes == null) rf.notes = "";
            return rf;
        }

        // ---- scoring (hint) ----
        struct ScoredMove { public Move move; public int score; public string reason; }
        static IEnumerable<ScoredMove> ScoreMoves(FreeCellState s)
        {
            foreach (var m in s.GetLegalMoves())
            {
                int score = 0;
                var reasons = new List<string>();
                Card topCard;
                int srcCount;
                bool destEmpty;
                switch (m.Kind)
                {
                    case MoveKind.TableauToFoundation:
                        topCard = s.Tableaus[m.From][s.Tableaus[m.From].Count - 1];
                        score += 1000; reasons.Add("to-foundation");
                        score += (int)topCard.Rank;
                        break;
                    case MoveKind.CellToFoundation:
                        topCard = s.Cells[m.From].Value;
                        score += 1100; reasons.Add("from-cell-to-foundation");
                        score += (int)topCard.Rank;
                        break;
                    case MoveKind.TableauToCell:
                        srcCount = s.Tableaus[m.From].Count;
                        score -= 200; reasons.Add("fills-cell");
                        if (srcCount == 1) { score += 300; reasons.Add("frees-tableau"); }
                        break;
                    case MoveKind.CellToTableau:
                        score += 200; reasons.Add("empties-cell");
                        destEmpty = s.Tableaus[m.To].Count == 0;
                        if (destEmpty) { score += 120; reasons.Add("to-empty-tableau"); }
                        else { score += 60; reasons.Add("builds-sequence"); }
                        break;
                    case MoveKind.TableauToTableau:
                        destEmpty = s.Tableaus[m.To].Count == 0;
                        srcCount = s.Tableaus[m.From].Count;
                        score += 220; reasons.Add("build");
                        if (m.Count > 1) { score += 50 * (m.Count - 1); reasons.Add("sequence x" + m.Count); }
                        if (destEmpty) { score += 150; reasons.Add("to-empty-tableau"); }
                        if (srcCount == m.Count) { score += 300; reasons.Add("frees-tableau"); }
                        break;
                }
                yield return new ScoredMove { move = m, score = score, reason = string.Join(",", reasons) };
            }
        }

        // ---- temp close wrapper (for existing call sites) ----
        static void MaybeCloseTempCell() => MaybeCloseTempCell(false);
    }
}