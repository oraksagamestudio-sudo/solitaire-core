using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
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
        public ReplayConfig config { get; set; }
        public List<ReplayMove> moves { get; set; }
    }
    public sealed class ReplayConfig
    {
        public int cells { get; set; }
        public int foundations { get; set; }
        public int tableaus { get; set; }
        public bool allowSequenceMoves { get; set; }
    }
    public sealed class ReplayMove
    {
        public string kind { get; set; }
        public int from { get; set; }
        public int to { get; set; }
        public int count { get; set; }
    }

    class Program
    {
        private static FreeCellState _fc = null;
        private static List<Move> _log = new List<Move>();
        private static uint _currentSeed = 0;
        private static FreeCellConfig _currentConfig = FreeCellConfig.Default;

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
                    DumpFreeCell(_fc);
                    break;
                }
                case "fc-legal":
                {
                    EnsureFc();
                    var moves = _fc.GetLegalMoves().ToList();
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
                        return;
                    }
                    var kind = (MoveKind)Enum.Parse(typeof(MoveKind), args[1], true);
                    int from = int.Parse(args[2]);
                    int to = int.Parse(args[3]);
                    int count = (args.Count >= 5 ? int.Parse(args[4]) : 1);
                    var m = new Move(kind, from, to, count);
                    _fc = (FreeCellState)_fc.Apply(m);
                    _log.Add(m);
                    Console.WriteLine("[OK] Move applied.");
                    DumpFreeCell(_fc);
                    break;
                }
                case "save":
                {
                    EnsureFc();
                    if (args.Count < 2) { Console.WriteLine("Usage: save <path.json>"); return; }
                    var path = args[1];
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
                            kind = Enum.GetName(typeof(MoveKind), m.Kind),
                            from = m.From, to = m.To, count = m.Count
                        }).ToList()
                    };
                    var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    var json = System.Text.Json.JsonSerializer.Serialize(rf, opts);
                    File.WriteAllText(path, json);
                    Console.WriteLine("[Saved] " + path + " (" + rf.moves.Count + " moves)");
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
                    DumpFreeCell(_fc);
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
                    DumpFreeCell(_fc);
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

        static ReplayFile ReadReplay(string path)
        {
            var json = File.ReadAllText(path);
            var rf = System.Text.Json.JsonSerializer.Deserialize<ReplayFile>(json);
            if (rf == null) throw new InvalidOperationException("Invalid replay file.");
            if (rf.gameId != "FreeCell") throw new InvalidOperationException("Unsupported gameId: " + rf.gameId);
            if (rf.version != 1) throw new InvalidOperationException("Unsupported version: " + rf.version);
            if (rf.config == null) throw new InvalidOperationException("Missing config.");
            if (rf.moves == null) rf.moves = new List<ReplayMove>();
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
            Console.WriteLine("    Kinds: TableauToCell | CellToTableau | TableauToFoundation | CellToFoundation | TableauToTableau");
            Console.WriteLine("  save <path.json>             Save current game as replay JSON");
            Console.WriteLine("  replay <path.json> [--until N]  Replay file (apply first N moves)");
            Console.WriteLine("  load <path.json>             Load file and set current state to result");
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
