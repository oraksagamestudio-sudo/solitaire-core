using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Solitaire.Core;
using Solitaire.FreeCell;

namespace Solitaire.Cli
{
    public sealed class ReplayFile
    {
        public int version { get; set; } = 1;
        public string gameId { get; set; } = "FreeCell";
        public uint seed { get; set; }
        public ReplayConfig config { get; set; } = new ReplayConfig();
        public List<ReplayMove> moves { get; set; } = new List<ReplayMove>();
        public string createdAt { get; set; } = "";
        public string notes { get; set; } = "";
        public List<string> tags { get; set; } = new List<string>();
        public Dictionary<string,string> metadata { get; set; } = new Dictionary<string, string>();
    }
    public sealed class ReplayConfig
    {
        public int cells { get; set; } = 4;
        public int foundations { get; set; } = 4;
        public int tableaus { get; set; } = 8;
        public bool allowSequenceMoves { get; set; } = true;
    }
    public sealed class ReplayMove
    {
        public string kind { get; set; } = "";
        public int from { get; set; }
        public int to { get; set; }
        public int count { get; set; }
    }

    class Program
    {
        private static FreeCellState? _fc = null;
        private static List<Move> _log = new List<Move>();
        private static uint _currentSeed = 0;
        private static FreeCellConfig _currentConfig = FreeCellConfig.Default;

        private static bool _pretty = false;

        static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "repl", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Solitaire CLI — REPL mode");
                Console.WriteLine("Type 'help' to see commands. Type 'exit' to quit.");
                while (true)
                {
                    Console.Write("> ");
                    var line = Console.ReadLine();
                    if (line == null) break;
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    if (string.Equals(line, "exit", StringComparison.OrdinalIgnoreCase)) break;

                    var tokens = SplitArgs(line).ToArray();
                    try { RunOnce(tokens); }
                    catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); }
                }
                return 0;
            }

            if (args.Length == 0) { Help(); return 1; }

            try { RunOnce(args); return 0; }
            catch (Exception ex) { Console.WriteLine("[ERROR] " + ex.Message); return 1; }
        }

        static void RunOnce(IReadOnlyList<string> args)
        {
            var cmd = args[0];
            switch (cmd)
            {
                case "fc-new":
                {
                    uint seed = 12345;
                    for (int i = 1; i + 1 < args.Count; i++)
                        if (args[i] == "--seed" && uint.TryParse(args[i + 1], out var s)) seed = s;
                    _currentSeed = seed;
                    _currentConfig = FreeCellConfig.Default;
                    _fc = FreeCellState.NewGame(seed, _currentConfig);
                    _log.Clear();
                    Console.WriteLine("[FreeCell] New game. Seed=" + seed);
                    DumpFreeCell(_fc!);
                    break;
                }
                case "fc-legal":
                {
                    EnsureFc();
                    var moves = _fc!.GetLegalMoves().ToList();
                    for (int i = 0; i < moves.Count; i++) Console.WriteLine((i + 1).ToString() + ". " + moves[i].ToString());
                    Console.WriteLine("Total legal moves: " + moves.Count);
                    break;
                }
                case "fc-move":
                {
                    EnsureFc();
                    if (args.Count < 4)
                    {
                        Console.WriteLine("Usage: fc-move <Kind> <from> <to> [count]");
                        Console.WriteLine("  Kind aliases: t2t, t2c, c2t, t2f, c2f");
                        return;
                    }
                    var kind = ParseMoveKind(args[1]);
                    int from = int.Parse(args[2]);
                    int to = int.Parse(args[3]);
                    int count = (args.Count >= 5 ? int.Parse(args[4]) : 1);
                    var m = new Move(kind, from, to, count);
                    _fc = (FreeCellState)_fc!.Apply(m);
                    _log.Add(m);
                    Console.WriteLine("[OK] Move applied.");
                    DumpFreeCell(_fc!);
                    break;
                }
                case "save":
                {
                    EnsureFc();
                    if (args.Count < 2) { Console.WriteLine("Usage: save <path.json> [--note \"text\"] [--tag a,b,c]"); return; }
                    var path = args[1];

                    string note = "";
                    List<string> tagList = new List<string>();
                    for (int i = 2; i < args.Count; i++)
                    {
                        if (args[i] == "--note" && i + 1 < args.Count) { note = args[i + 1]; i++; }
                        else if (args[i] == "--tag" && i + 1 < args.Count) { tagList = args[i + 1].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); i++; }
                    }

                    var rf = new ReplayFile
                    {
                        version = 1,
                        gameId = "FreeCell",
                        seed = _currentSeed,
                        config = new ReplayConfig {
                            cells = _currentConfig.Cells,
                            foundations = _currentConfig.Foundations,
                            tableaus = _currentConfig.Tableaus,
                            allowSequenceMoves = _currentConfig.AllowSequenceMoves
                        },
                        moves = _log.Select(m => new ReplayMove {
                            kind = Enum.GetName(typeof(MoveKind), m.Kind) ?? "TableauToCell",
                            from = m.From, to = m.To, count = m.Count
                        }).ToList(),
                        createdAt = DateTimeOffset.Now.ToString("o"),
                        notes = note,
                        tags = tagList,
                        metadata = new Dictionary<string,string> {
                            { "cli", "Solitaire.Cli" },
                            { "cliVersion", "1" },
                            { "os", Environment.OSVersion.ToString() }
                        }
                    };

                    var opts = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(rf, opts);
                    File.WriteAllText(path, json);
                    Console.WriteLine("[Saved] " + path + " (" + rf.moves.Count + " moves, tags=" + string.Join(",", tagList) + ")");
                    break;
                }
                case "replay":
                {
                    if (args.Count < 2) { Console.WriteLine("Usage: replay <path.json> [--until N]"); return; }
                    var path = args[1];
                    int until = int.MaxValue;
                    for (int i = 2; i + 1 < args.Count; i++)
                        if (args[i] == "--until" && int.TryParse(args[i + 1], out var n)) until = n;

                    var rf = ReadReplay(path);
                    var cfg = new FreeCellConfig(rf.config.cells, rf.config.foundations, rf.config.tableaus, rf.config.allowSequenceMoves);
                    var s = FreeCellState.NewGame(rf.seed, cfg);

                    int applied = 0;
                    for (int i = 0; i < rf.moves.Count && applied < until; i++)
                    {
                        var rm = rf.moves[i];
                        var mk = (MoveKind)Enum.Parse(typeof(MoveKind), rm.kind, true);
                        var m = new Move(mk, rm.from, rm.to, rm.count);
                        try
                        {
                            s = (FreeCellState)s.Apply(m);
                            applied++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[REPLAY ERROR] step " + (i + 1) + ": " + ex.Message);
                            break;
                        }
                    }

                    _fc = s;
                    _currentSeed = rf.seed;
                    _currentConfig = cfg;
                    _log = rf.moves.Take(applied).Select(rm =>
                        new Move((MoveKind)Enum.Parse(typeof(MoveKind), rm.kind, true), rm.from, rm.to, rm.count)
                    ).ToList();

                    Console.WriteLine("[Replayed] applied " + applied + " moves of " + rf.moves.Count);
                    DumpFreeCell(_fc!);
                    break;
                }
                case "load":
                {
                    if (args.Count < 2) { Console.WriteLine("Usage: load <path.json>"); return; }
                    var path = args[1];
                    var rf = ReadReplay(path);
                    var cfg = new FreeCellConfig(rf.config.cells, rf.config.foundations, rf.config.tableaus, rf.config.allowSequenceMoves);
                    var s = FreeCellState.NewGame(rf.seed, cfg);

                    int applied = 0;
                    foreach (var rm in rf.moves)
                    {
                        var mk = (MoveKind)Enum.Parse(typeof(MoveKind), rm.kind, true);
                        var m = new Move(mk, rm.from, rm.to, rm.count);
                        s = (FreeCellState)s.Apply(m);
                        applied++;
                    }

                    _fc = s;
                    _currentSeed = rf.seed;
                    _currentConfig = cfg;
                    _log = rf.moves.Select(rm =>
                        new Move((MoveKind)Enum.Parse(typeof(MoveKind), rm.kind, true), rm.from, rm.to, rm.count)
                    ).ToList();

                    Console.WriteLine("[Loaded] " + path + " | moves=" + applied);
                    DumpFreeCell(_fc!);
                    break;
                }
                case "replay-info":
                {
                    if (args.Count < 2) { Console.WriteLine("Usage: replay-info <path.json>"); return; }
                    var rf = ReadReplay(args[1]);
                    Console.WriteLine("gameId=" + rf.gameId + "  version=" + rf.version + "  seed=" + rf.seed);
                    Console.WriteLine("config: cells=" + rf.config.cells + " foundations=" + rf.config.foundations + " tableaus=" + rf.config.tableaus + " sequence=" + rf.config.allowSequenceMoves);
                    Console.WriteLine("moves=" + (rf.moves != null ? rf.moves.Count : 0));
                    Console.WriteLine("createdAt=" + (rf.createdAt ?? ""));
                    Console.WriteLine("tags=[" + string.Join(",", rf.tags ?? new List<string>()) + "]");
                    Console.WriteLine("notes=" + (rf.notes ?? ""));
                    Console.WriteLine("metadata: " + (rf.metadata != null ? string.Join(", ", rf.metadata.Select(kv => kv.Key + "=" + kv.Value)) : ""));
                    break;
                }
                case "replay-diff":
                {
                    if (args.Count < 3) { Console.WriteLine("Usage: replay-diff <a.json> <b.json>"); return; }
                    var a = ReadReplay(args[1]);
                    var b = ReadReplay(args[2]);

                    Console.WriteLine("== Summary ==");
                    Console.WriteLine("seed: " + a.seed + " vs " + b.seed + (a.seed == b.seed ? " (same)" : " (DIFF)"));
                    Console.WriteLine("sequence-enabled: " + a.config.allowSequenceMoves + " vs " + b.config.allowSequenceMoves + (a.config.allowSequenceMoves == b.config.allowSequenceMoves ? " (same)" : " (DIFF)"));
                    Console.WriteLine("cells/foundations/tableaus: " + a.config.cells + "/" + a.config.foundations + "/" + a.config.tableaus +
                                      " vs " + b.config.cells + "/" + b.config.foundations + "/" + b.config.tableaus +
                                      ((a.config.cells==b.config.cells && a.config.foundations==b.config.foundations && a.config.tableaus==b.config.tableaus) ? " (same)" : " (DIFF)"));
                    int ac = a.moves != null ? a.moves.Count : 0;
                    int bc = b.moves != null ? b.moves.Count : 0;
                    Console.WriteLine("move count: " + ac + " vs " + bc + (ac == bc ? " (same)" : " (DIFF)"));

                    int minc = Math.Min(ac, bc);
                    int idx = -1;
                    for (int i = 0; i < minc; i++)
                    {
                        var am = a.moves[i]; var bm = b.moves[i];
                        if (!(am.kind == bm.kind && am.from == bm.from && am.to == bm.to && am.count == bm.count))
                        { idx = i; break; }
                    }
                    if (idx == -1)
                    {
                        if (ac == bc) Console.WriteLine("first diff: none (identical move sequences)");
                        else Console.WriteLine("first diff: at " + minc + " (one file has extra moves)");
                    }
                    else
                    {
                        Console.WriteLine("first diff @ " + idx + ":");
                        Console.WriteLine("  A: " + a.moves[idx].kind + " " + a.moves[idx].from + "->" + a.moves[idx].to + " x" + a.moves[idx].count);
                        Console.WriteLine("  B: " + b.moves[idx].kind + " " + b.moves[idx].from + "->" + b.moves[idx].to + " x" + b.moves[idx].count);
                    }
                    break;
                }
                case "hint":
                {
                    EnsureFc();
                    int topN = 5;
                    if (args.Count >= 2) int.TryParse(args[1], out topN);
                    var scored = ScoreMoves(_fc!).OrderByDescending(x => x.score).Take(topN).ToList();
                    if (scored.Count == 0) { Console.WriteLine("(no legal moves)"); break; }
                    for (int i = 0; i < scored.Count; i++)
                    {
                        var s = scored[i];
                        Console.WriteLine((i+1).ToString() + ". " + s.move.ToString() + "  score=" + s.score + "  " + s.reason);
                    }
                    break;
                }
                case "auto-foundation":
                {
                    EnsureFc();
                    int applied = 0;
                    while (true)
                    {
                        var move = _fc!.GetLegalMoves().FirstOrDefault(m =>
                            m.Kind == MoveKind.TableauToFoundation || m.Kind == MoveKind.CellToFoundation);
                        if (move.Kind == 0 && move.From == 0 && move.To == 0 && move.Count == 0) break;
                        _fc = (FreeCellState)_fc!.Apply(move);
                        _log.Add(move);
                        applied++;
                    }
                    Console.WriteLine("[Auto] foundation moves applied: " + applied);
                    DumpFreeCell(_fc!);
                    break;
                }
                case "undo":
                {
                    EnsureFc();
                    int n = 1;
                    if (args.Count >= 2) int.TryParse(args[1], out n);
                    if (n < 1) n = 1;
                    if (_log.Count == 0) { Console.WriteLine("[Undo] nothing to undo."); break; }
                    if (n > _log.Count) n = _log.Count;

                    _log.RemoveRange(_log.Count - n, n);
                    var s2 = FreeCellState.NewGame(_currentSeed, _currentConfig);
                    foreach (var m in _log) s2 = (FreeCellState)s2.Apply(m);
                    _fc = s2;
                    Console.WriteLine("[Undo] reverted " + n + " move(s).");
                    DumpFreeCell(_fc!);
                    break;
                }
                case "board":
                {
                    EnsureFc();
                    DumpFreeCell(_fc!);
                    break;
                }
                case "pretty":
                {
                    if (args.Count < 2) { Console.WriteLine("pretty is " + (_pretty ? "ON" : "OFF")); break; }
                    var opt = args[1].ToLowerInvariant();
                    if (opt == "on") { _pretty = true; Console.WriteLine("[pretty] ON"); }
                    else if (opt == "off") { _pretty = false; Console.WriteLine("[pretty] OFF"); }
                    else Console.WriteLine("Usage: pretty on|off");
                    break;
                }
                case "help":
                    Help();
                    break;
                default:
                    Console.WriteLine("Unknown command: " + cmd);
                    Help();
                    break;
            }
        }

        // ---- Parse move kind with aliases ----
        static MoveKind ParseMoveKind(string token)
        {
            string t = token.Trim();
            if (Enum.TryParse<MoveKind>(t, true, out var mk))
            {
                return mk;
            }
            switch (t.ToLowerInvariant())
            {
                case "t2t": return MoveKind.TableauToTableau;
                case "t2c": return MoveKind.TableauToCell;
                case "c2t": return MoveKind.CellToTableau;
                case "t2f": return MoveKind.TableauToFoundation;
                case "c2f": return MoveKind.CellToFoundation;
            }
            throw new ArgumentException("Unknown move kind: " + token + " (try: t2t, t2c, c2t, t2f, c2f)");
        }

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

        static void Help()
        {
            Console.WriteLine("Solitaire CLI");
            Console.WriteLine("Commands:");
            Console.WriteLine("  repl                         Start interactive session");
            Console.WriteLine("  fc-new --seed <u32>          Start a new FreeCell game");
            Console.WriteLine("  fc-legal                     List legal moves");
            Console.WriteLine("  fc-move <Kind> <from> <to> [count]");
            Console.WriteLine("    Kind aliases: t2t, t2c, c2t, t2f, c2f");
            Console.WriteLine("  save <path.json> [--note \"text\"] [--tag a,b,c]  Save current game as replay JSON");
            Console.WriteLine("  replay <path.json> [--until N]  Replay file (apply first N moves)");
            Console.WriteLine("  load <path.json>             Load file and set current state to result");
            Console.WriteLine("  replay-info <path.json>      Show metadata/config/move count");
            Console.WriteLine("  replay-diff <a.json> <b.json>  Compare two replay files");
            Console.WriteLine("  hint [N]                     Show top-N suggested moves with scores");
            Console.WriteLine("  auto-foundation              Auto-apply all legal moves to foundations");
            Console.WriteLine("  undo [N]                     Undo last N moves (rebuilds from seed)");
            Console.WriteLine("  board                        Print current board");
            Console.WriteLine("  pretty on|off                Toggle ANSI colored board rendering");
            Console.WriteLine();
            Console.WriteLine("Index notes:");
            Console.WriteLine("  Tableaus: 0..7  | Cells: 0..3  | Foundations(suit index): 0=Spade,1=Heart,2=Diamond,3=Club");
        }

        static void EnsureFc()
        {
            if (_fc == null) throw new InvalidOperationException("No FreeCell game. Run: fc-new --seed <seed>");
        }

        static void DumpFreeCell(FreeCellState s)
        {
            if (_pretty) { DumpBoardPretty(s); return; }

            var cellsStr = string.Join(", ", s.Cells.Select(c => c.HasValue ? c.Value.ToString() : "-"));
            Console.WriteLine("Moves=" + s.MoveCount
                              + "  Foundations=" + string.Join(",", s.FoundationTop)
                              + "  Cells=[" + cellsStr + "]");
            for (int i = 0; i < s.Tableaus.Length; i++)
            {
                var t = s.Tableaus[i];
                Console.WriteLine("T" + i + ": " + string.Join(" | ", t.Select(c => c.ToString())));
            }
        }

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
            const int CW = 4; // column cell width (visible)
            string Reset = "\x1b[0m";
            string Red = "\x1b[31m";
            string Bold = "\x1b[1m";

            Console.WriteLine(Bold + "Moves=" + s.MoveCount + Reset);

            // Foundations
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

            // Cells
            var csb = new StringBuilder();
            csb.Append("Cells: ");
            for (int i = 0; i < s.Cells.Length; i++)
            {
                string token = s.Cells[i].HasValue ? RenderCardShort(s.Cells[i].Value, true) : "--";
                csb.Append(RightPadVisible(token, CW) + "  ");
            }
            Console.WriteLine(csb.ToString());

            // Tableaus header
            var hb = new StringBuilder();
            for (int col = 0; col < s.Tableaus.Length; col++)
            {
                string label = "T" + col;
                hb.Append("  " + RightPadVisible(label, CW));
            }
            Console.WriteLine(Bold + hb.ToString() + Reset);

            // Find max height
            int maxH = 0;
            for (int i = 0; i < s.Tableaus.Length; i++) if (s.Tableaus[i].Count > maxH) maxH = s.Tableaus[i].Count;

            // Print rows TOP-aligned (no bottom-sticking), while showing bottom->top within each column
            for (int r = 0; r < maxH; r++)
            {
                var line = new StringBuilder();
                for (int col = 0; col < s.Tableaus.Length; col++)
                {
                    var pile = s.Tableaus[col];
                    int idx = r; // 0=bottom card, grows upward; top card appears on lower rows for taller piles
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
    }
}
