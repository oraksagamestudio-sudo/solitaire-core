// FILE: src/Solitaire.Cli/Program.cs
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
    // NOTE: Program is now partial. Helpers and models are split across files.
    partial class Program
    {
        private static FreeCellState _fc = null;
        private static List<Move> _log = new List<Move>();
        private static List<ReplayMove> _steps = new List<ReplayMove>(); // moves + items timeline
        private static uint _currentSeed = 0;
        private static FreeCellConfig _currentConfig = FreeCellConfig.Default;

        // pretty print is ON by default
        private static bool _pretty = true;

        // Items
        private static bool _tempActive = false;
        private static int _tempCellIndex = 4;
        private static int _tempCharges = 0;
        private static int _grabCharges = 0;
        private static int _siphonCharges = 0; // pull next-needed suit card from anywhere to foundation
        private static bool _tempEverHeld = false; // 임시셀이 한 번이라도 카드를 보유했는가(활성화 이후)
        // --- session base tracking for undo ---
        private static bool _baseIsSnapshot = false;
        private static ReplaySnapshot _baseSnapshot = null; // Program.Snapshot.cs의 타입
        private static string _baseShuffleKind = "xor";     // "xor" | "dotnet"
        // --- baseline-from-seed tracking ---
        private static bool _baseIsSeed = false;
        private static uint _baseSeed = 0;
        private static FreeCellConfig _baseConfig = FreeCellConfig.Default;
        private static int _baseTempCharges = 0, _baseGrabCharges = 0, _baseSiphonCharges = 0;

        static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "repl", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Solitaire CLI v" + CliInfo.Version() + " - REPL mode");
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
                        _steps.Clear();
                        _tempActive = false;
                        _tempCharges = 0;
                        _grabCharges = 0;
                        _siphonCharges = 0;
                        _gameFinished = false;
                        Console.WriteLine("[FreeCell] New game. Seed=" + seed);
                        SetSessionBaseToSeed(_currentSeed, _currentConfig, "xor", _steps, _tempCharges, _grabCharges, _siphonCharges);
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
                            Console.WriteLine("  Kind aliases: t2t, t2c, c2t, t2f, c2f, f2t, f2c");
                            return;
                        }
                        var kind = ParseMoveKind(args[1]);
                        int from = int.Parse(args[2]);
                        int to = int.Parse(args[3]);
                        int count = (args.Count >= 5 ? int.Parse(args[4]) : 1);
                        var m = new Move(kind, from, to, count);

                        // ---- 추가: T2T가 임시셀에 '관여'하면 사용자 확인 ----
                        bool tempInvolved = _tempActive
                            && kind == MoveKind.TableauToTableau
                            && (
                                TempCapacityNeeded(_fc, m) // 빈 공간 용량 사용
                                || (_fc.Cells.Length > _tempCellIndex && _fc.Cells[_tempCellIndex].HasValue) // 임시셀에 실제 카드 보유 중
                            );

                        if (tempInvolved)
                        {
                            Console.Write("[TempCell] This T2T move will involve the Temp cell. Proceed? [y/N]: ");
                            var ans = Console.ReadLine()?.Trim().ToLowerInvariant();
                            if (!(ans == "y" || ans == "yes"))
                            {
                                Console.WriteLine("[CANCELLED] Move aborted.");
                                return;
                            }
                        }

                        bool usedTempCapacity = TempCapacityNeeded(_fc, m);

                        _fc = (FreeCellState)_fc.Apply(m);
                        _log.Add(m);
                        _steps.Add(new ReplayMove
                        {
                            kind = "move",
                            op = Enum.GetName(typeof(MoveKind), kind) ?? "TableauToCell",
                            from = from,
                            to = to,
                            count = count
                        });

                        if (_tempActive && _fc.Cells.Length > _tempCellIndex && _fc.Cells[_tempCellIndex].HasValue)
                            _tempEverHeld = true;

                        MaybeCloseTempCell(usedTempCapacity);
                        RecalcVictoryAndAnnounce(_fc);
                        Console.WriteLine("[OK] Move applied.");
                        DumpFreeCell(_fc);
                        break;
                    }
                case "fc-f2t":
                    {
                        EnsureFc();
                        if (args.Count < 3) { Console.WriteLine("Usage: fc-f2t <foundationIndex 0..3> <tableauIndex 0..N-1>"); return; }
                        int f = int.Parse(args[1]);
                        int t = int.Parse(args[2]);
                        FoundationPop("t", f, t);
                        RecalcVictoryAndAnnounce(_fc);
                        break;
                    }
                case "fc-f2c":
                    {
                        EnsureFc();
                        if (args.Count < 3) { Console.WriteLine("Usage: fc-f2c <foundationIndex 0..3> <cellIndex 0..C-1>"); return; }
                        int f = int.Parse(args[1]);
                        int c = int.Parse(args[2]);
                        FoundationPop("c", f, c);
                        RecalcVictoryAndAnnounce(_fc);
                        break;
                    }

                // ---- Items: show/set/use ----
                case "items":
                    {
                        if (args.Count == 1 || (args.Count == 2 && args[1] == "show"))
                        {
                            ShowItems();
                            return;
                        }
                        if (args.Count >= 3 && args[1] == "set")
                        {
                            int i = 2;
                            while (i < args.Count)
                            {
                                if (i + 1 < args.Count && args[i] == "temp") { _tempCharges = int.Parse(args[i + 1]); i += 2; continue; }
                                if (i + 1 < args.Count && args[i] == "grab") { _grabCharges = int.Parse(args[i + 1]); i += 2; continue; }
                                if (i + 1 < args.Count && (args[i] == "siphon" || args[i] == "sip")) { _siphonCharges = int.Parse(args[i + 1]); i += 2; continue; }
                                break;
                            }
                            Console.WriteLine("[Items] temp=" + _tempCharges + " grab=" + _grabCharges + " siphon=" + _siphonCharges);
                            return;
                        }
                        Console.WriteLine("Usage: items [show] | items set temp <n> [grab <n>] [siphon <n>]");
                        return;
                    }
                case "use-temp":
                    {
                        EnsureFc();
                        if (_tempActive) { Console.WriteLine("[TempCell] already active."); break; }
                        if (_tempCharges <= 0) { Console.WriteLine("[TempCell] no charges."); break; }

                        _steps.Add(new ReplayMove { kind = "item", op = "temp" });

                        _tempActive = true;
                        _tempCharges--;
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

                        RecalcVictoryAndAnnounce(_fc);
                        Console.WriteLine("[TempCell] Activated (virtual cell index " + _tempCellIndex + ").");
                        DumpFreeCell(_fc);
                        break;
                    }
                case "grab":
                    {
                        EnsureFc();
                        if (_grabCharges <= 0) { Console.WriteLine("[Grab] no charges."); break; }
                        if (args.Count < 3) { Console.WriteLine("Usage: grab <tableauIndex> <depth>   (depth>=0: from top, depth<0: from bottom, -1=bottom)"); break; }
                        int tIndex = int.Parse(args[1]);
                        if (tIndex < 0 || tIndex >= _fc.Tableaus.Length) { Console.WriteLine("[Grab] invalid tableau index."); break; }
                        var pile = _fc.Tableaus[tIndex];
                        int sel = int.Parse(args[2]);

                        int from;
                        if (sel >= 0)
                        {
                            if (sel >= pile.Count) { Console.WriteLine("[Grab] invalid depth."); break; }
                            from = pile.Count - 1 - sel; // top-based
                        }
                        else
                        {
                            int idxFromBottom = (-sel) - 1; // -1 = bottom
                            if (idxFromBottom < 0 || idxFromBottom >= pile.Count) { Console.WriteLine("[Grab] invalid depth."); break; }
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

                        _steps.Add(new ReplayMove { kind = "item", op = "grab", from = tIndex, arg = sel });
                        _grabCharges--;
                        RecalcVictoryAndAnnounce(_fc);
                        Console.WriteLine("[Grab] Pulled " + card.ToString() + " to top of T" + tIndex + ".");
                        MaybeCloseTempCell(false);
                        DumpFreeCell(_fc);
                        break;
                    }
                case "siphon":
                    {
                        EnsureFc();
                        if (_siphonCharges <= 0) { Console.WriteLine("[Siphon] no charges."); break; }
                        if (args.Count < 2) { Console.WriteLine("Usage: siphon <s|h|d|c|rand>"); break; }
                        string tok = args[1].ToLowerInvariant();
                        int suitIndex = -1;
                        if (tok == "rand" || tok == "random")
                        {
                            var candidates = FindSiphonCandidateSuits(_fc);
                            if (candidates.Count == 0) { Console.WriteLine("[Siphon] no available suit to promote."); break; }
                            var rnd = new Random(unchecked(Environment.TickCount));
                            suitIndex = candidates[rnd.Next(candidates.Count)];
                        }
                        else
                        {
                            suitIndex = ParseSuitIndex(tok);
                        }
                        if (suitIndex < 0 || suitIndex > 3) { Console.WriteLine("[Siphon] invalid suit token: " + args[1]); break; }

                        FreeCellState next;
                        Card moved;
                        string source;
                        if (_fc.TrySiphonNextToFoundation(suitIndex, out next, out moved, out source))
                        {
                            _fc = next;
                            _siphonCharges--;
                            _steps.Add(new ReplayMove { kind = "item", op = "siphon", arg = suitIndex });
                            RecalcVictoryAndAnnounce(_fc);
                            Console.WriteLine("[Siphon] Moved " + moved.ToString() + " from " + source + " to foundation " + suitIndex + ".");
                            DumpFreeCell(_fc);
                        }
                        else
                        {
                            Console.WriteLine("[Siphon] Target card not found anywhere for suit " + args[1] + ".");
                        }
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
                            config = new ReplayConfig
                            {
                                cells = _currentConfig.Cells,
                                foundations = _currentConfig.Foundations,
                                tableaus = _currentConfig.Tableaus,
                                allowSequenceMoves = _currentConfig.AllowSequenceMoves
                            },
                            moves = new List<ReplayMove>(_steps),
                            createdAt = DateTimeOffset.Now.ToString("o"),
                            notes = note,
                            tags = tagList,
                            metadata = new Dictionary<string, string> {
                            { "cli", "Solitaire.Cli" },
                            { "cliVersion", "1" },
                            { "os", Environment.OSVersion.ToString() },
                            { "items.temp.active", _tempActive ? "1" : "0" },
                            { "items.temp.charges", _tempCharges.ToString() },
                            { "items.grab.charges", _grabCharges.ToString() },
                            { "items.siphon.charges", _siphonCharges.ToString() },
                            { "game.finished", _gameFinished ? "1" : "0" }
                        },
                            snapshot = MakeSnapshot(_fc)
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
                        string usedShuffle = "xor";

                        Move firstMoveForShuffle;
                        if (TryGetFirstMoveForShuffle(rf.moves, out firstMoveForShuffle))
                        {
                            try { var _ = (FreeCellState)s.Apply(firstMoveForShuffle); }
                            catch
                            {
                                var s2 = FreeCellState.NewGame(rf.seed, cfg, "dotnet");
                                try { var __ = (FreeCellState)s2.Apply(firstMoveForShuffle); s = s2; usedShuffle = "dotnet"; }
                                catch { }
                            }
                        }
                        Console.WriteLine("[Replay] using shuffle='" + usedShuffle + "'");

                        _fc = s;
                        _currentConfig = cfg;
                        _log.Clear();
                        _steps.Clear();
                        _gameFinished = false;

                        int applied = 0;
                        for (int i = 0; i < rf.moves.Count && applied < until; i++)
                        {
                            var step = rf.moves[i];

                            try
                            {
                                if (TryParseMoveFromReplay(step, out var mv))
                                {
                                    bool usedTempCapacity = TempCapacityNeeded(_fc, mv);
                                    _fc = (FreeCellState)_fc.Apply(mv);
                                    _log.Add(mv);

                                    if (_tempActive && _fc.Cells.Length > _tempCellIndex && _fc.Cells[_tempCellIndex].HasValue)
                                        _tempEverHeld = true;

                                    MaybeCloseTempCell(usedTempCapacity);
                                    _steps.Add(step);
                                }
                                else if (string.Equals(step.kind, "item", StringComparison.OrdinalIgnoreCase))
                                {
                                    bool ok = ApplyReplayItem(step);
                                    if (!ok) throw new InvalidOperationException("Failed to apply item step: " + (step.op ?? step.kind));
                                    _steps.Add(step);
                                }
                                else
                                {
                                    throw new InvalidOperationException("Unknown replay step kind: " + step.kind);
                                }

                                applied++;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("[REPLAY ERROR] step " + (i + 1) + ": " + ex.Message);
                                if (rf.snapshot != null)
                                {
                                    try
                                    {
                                        _fc = BuildFromSnapshot(rf.seed, cfg, rf.snapshot);
                                        Console.WriteLine("[Replay] fell back to snapshot.");
                                        applied = rf.moves.Count;
                                        _steps = new List<ReplayMove>(rf.moves);
                                    }
                                    catch { }
                                }
                                break;
                            }
                        }

                        _steps = rf.moves.Take(applied).ToList();

                        _currentSeed = rf.seed;
                        _currentConfig = cfg;
                        _log = rf.moves.Take(applied).Where(IsMoveStep).Select(ToMove).ToList();

                        if (rf.metadata != null)
                        {
                            string v;
                            if (rf.metadata.TryGetValue("items.temp.active", out v)) _tempActive = v == "1";
                            if (rf.metadata.TryGetValue("items.temp.charges", out v)) int.TryParse(v, out _tempCharges);
                            if (rf.metadata.TryGetValue("items.grab.charges", out v)) int.TryParse(v, out _grabCharges);
                            if (rf.metadata.TryGetValue("items.siphon.charges", out v)) int.TryParse(v, out _siphonCharges);
                        }

                        RecalcVictoryAndAnnounce(_fc);
                        SetSessionBaseToSeed(_currentSeed, _currentConfig, usedShuffle, _steps, _tempCharges, _grabCharges, _siphonCharges);
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

                        string usedShuffle = "xor";
                        Move firstMoveForShuffle;
                        if (TryGetFirstMoveForShuffle(rf.moves, out firstMoveForShuffle))
                        {
                            var tryS = FreeCellState.NewGame(rf.seed, cfg);
                            try { var _ = (FreeCellState)tryS.Apply(firstMoveForShuffle); usedShuffle = "xor"; }
                            catch
                            {
                                var tryS2 = FreeCellState.NewGame(rf.seed, cfg, "dotnet");
                                try { var __ = (FreeCellState)tryS2.Apply(firstMoveForShuffle); usedShuffle = "dotnet"; }
                                catch { }
                            }
                        }

                        // snapshot이 있으면 강제 로드
                        if (rf.snapshot != null && rf.snapshot.tableaus != null && rf.snapshot.tableaus.Length == cfg.Tableaus)
                        {
                            _fc = BuildFromSnapshot(rf.seed, cfg, rf.snapshot);
                            _currentSeed = rf.seed;
                            _currentConfig = cfg;

                            _log.Clear();
                            _steps = new List<ReplayMove>(rf.moves);

                            if (rf.metadata != null && rf.metadata.TryGetValue("game.finished", out var fin))
                                _gameFinished = fin == "1";
                            else
                                _gameFinished = IsVictory(_fc);

                            RecalcVictoryAndAnnounce(_fc);
                            Console.WriteLine("[Loaded] " + path + " | from snapshot");
                            SetSessionBaseToSeed(_currentSeed, _currentConfig, usedShuffle, _steps, _tempCharges, _grabCharges, _siphonCharges);
                            DumpFreeCell(_fc);
                            break;
                        }

                        var s = FreeCellState.NewGame(rf.seed, cfg);

                        _fc = s;
                        _currentConfig = cfg;
                        _log.Clear();
                        _steps.Clear();
                        _gameFinished = false;

                        int applied = 0;
                        foreach (var step in rf.moves)
                        {
                            if (TryParseMoveFromReplay(step, out var mv))
                            {
                                bool usedTempCapacity = TempCapacityNeeded(_fc, mv);
                                _fc = (FreeCellState)_fc.Apply(mv);
                                _log.Add(mv);

                                if (_tempActive && _fc.Cells.Length > _tempCellIndex && _fc.Cells[_tempCellIndex].HasValue)
                                    _tempEverHeld = true;

                                MaybeCloseTempCell(usedTempCapacity);
                                _steps.Add(step);
                            }
                            else if (string.Equals(step.kind, "item", StringComparison.OrdinalIgnoreCase))
                            {
                                bool ok = ApplyReplayItem(step);
                                if (!ok) throw new InvalidOperationException("Failed to apply item step: " + (step.op ?? step.kind));
                                _steps.Add(step);
                            }
                            else
                            {
                                throw new InvalidOperationException("Unknown replay step kind: " + step.kind);
                            }
                            applied++;
                        }

                        _steps = new List<ReplayMove>(_steps);

                        _currentSeed = rf.seed;
                        _currentConfig = cfg;
                        _log = rf.moves.Where(IsMoveStep).Select(ToMove).ToList();

                        if (rf.metadata != null)
                        {
                            string v;
                            if (rf.metadata.TryGetValue("items.temp.active", out v)) _tempActive = v == "1";
                            if (rf.metadata.TryGetValue("items.temp.charges", out v)) int.TryParse(v, out _tempCharges);
                            if (rf.metadata.TryGetValue("items.grab.charges", out v)) int.TryParse(v, out _grabCharges);
                            if (rf.metadata.TryGetValue("items.siphon.charges", out v)) int.TryParse(v, out _siphonCharges);
                        }

                        RecalcVictoryAndAnnounce(_fc);
                        Console.WriteLine("[Loaded] " + path + " | moves=" + applied);
                        SetSessionBaseToSeed(_currentSeed, _currentConfig, usedShuffle, _steps, _tempCharges, _grabCharges, _siphonCharges);
                        DumpFreeCell(_fc);
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
                                          ((a.config.cells == b.config.cells && a.config.foundations == b.config.foundations && a.config.tableaus == b.config.tableaus) ? " (same)" : " (DIFF)"));
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
                        var scored = ScoreMoves(_fc).OrderByDescending(x => x.score).Take(topN).ToList();
                        if (scored.Count == 0) { Console.WriteLine("(no legal moves)"); break; }
                        for (int i = 0; i < scored.Count; i++)
                        {
                            var s = scored[i];
                            Console.WriteLine((i + 1).ToString() + ". " + s.move.ToString() + "  score=" + s.score + "  " + s.reason);
                        }
                        break;
                    }
                case "auto-foundation":
                    {
                        EnsureFc();
                        int applied = 0;
                        while (true)
                        {
                            var move = _fc.GetLegalMoves().FirstOrDefault(m =>
                                m.Kind == MoveKind.TableauToFoundation || m.Kind == MoveKind.CellToFoundation);
                            if (move.Kind == 0 && move.From == 0 && move.To == 0 && move.Count == 0) break;
                            _fc = (FreeCellState)_fc.Apply(move);
                            _log.Add(move);
                            _steps.Add(new ReplayMove
                            {
                                kind = "move",
                                op = Enum.GetName(typeof(MoveKind), move.Kind) ?? "TableauToFoundation",
                                from = move.From,
                                to = move.To,
                                count = move.Count
                            });
                            applied++;
                        }
                        Console.WriteLine("[Auto] foundation moves applied: " + applied);
                        RecalcVictoryAndAnnounce(_fc);
                        DumpFreeCell(_fc);
                        break;
                    }
                case "undo":
                    {
                        EnsureFc();
                        int n = 1;
                        if (args.Count >= 2) int.TryParse(args[1], out n);
                        if (n < 1) n = 1;

                        // 현재 타임라인에서 move step 개수 계산
                        int moveCount = 0;
                        for (int i = 0; i < _steps.Count; i++)
                            if (IsMoveStep(_steps[i])) moveCount++;

                        if (moveCount == 0)
                        {
                            // 무브가 하나도 안 남았으면, 꼬리에 붙은 '아이템 스텝( temp/grab/siphon )'을 1개 되돌린다.
                            if (_steps.Count > 0 && IsAnyItemStep(_steps[_steps.Count - 1]))
                            {
                                var last = _steps[_steps.Count - 1];
                                _steps.RemoveAt(_steps.Count - 1);
                                Console.WriteLine("[Undo] reverted item: " + (last.op ?? last.kind));
                                // 아래의 재빌드 로직으로 이어서 타임라인을 다시 적용
                            }
                            else
                            {
                                Console.WriteLine("[Undo] nothing to undo.");
                                break;
                            }
                        }
                        if (n > moveCount) n = moveCount;

                        // 끝에서부터 move step n개 제거
                        int removed = 0;
                        for (int i = _steps.Count - 1; i >= 0 && removed < n; i--)
                        {
                            if (IsMoveStep(_steps[i]))
                            {
                                _steps.RemoveAt(i);
                                removed++;
                            }
                        }

                        // If the tail is now a Temp activation with no following moves, pop it too.
                        int poppedTailItems = 0;
                        while (_steps.Count > 0 && IsAnyItemStep(_steps[_steps.Count - 1]))
                        {
                            var last = _steps[_steps.Count - 1];
                            _steps.RemoveAt(_steps.Count - 1);
                            poppedTailItems++;
                            Console.WriteLine("[Undo] also reverted item: " + (last.op ?? last.kind));
                        }

                        // 베이스에서 다시 빌드 (seed-baseline 우선)
                        if (_baseIsSeed)
                        {
                            _fc = FreeCellState.NewGame(_baseSeed, _baseConfig, _baseShuffleKind);
                            _log.Clear();
                            _tempActive = false;
                            _tempEverHeld = false;
                            _tempCharges = _baseTempCharges;
                            _grabCharges = _baseGrabCharges;
                            _siphonCharges = _baseSiphonCharges;
                        }
                        else if (_baseIsSnapshot && _baseSnapshot != null)
                        {
                            _fc = BuildFromSnapshot(_currentSeed, _currentConfig, _baseSnapshot);
                            _log.Clear();
                            _tempEverHeld = false;
                        }
                        else
                        {
                            // 비상용: 현재 상태를 베이스 스냅샷으로 잡고 거기서 시작
                            _baseIsSnapshot = true;
                            _baseSnapshot = MakeSnapshot(_fc);
                            _fc = BuildFromSnapshot(_currentSeed, _currentConfig, _baseSnapshot);
                            _log.Clear();
                            _tempEverHeld = false;
                        }

                        for (int i = 0; i < _steps.Count; i++)
                        {
                            var step = _steps[i];
                            if (TryParseMoveFromReplay(step, out var mv))
                            {
                                bool usedTempCapacity = TempCapacityNeeded(_fc, mv);
                                _fc = (FreeCellState)_fc.Apply(mv);
                                _log.Add(mv);

                                if (_tempActive && _fc.Cells.Length > _tempCellIndex && _fc.Cells[_tempCellIndex].HasValue)
                                    _tempEverHeld = true;

                                MaybeCloseTempCell(usedTempCapacity);
                            }
                            else if (string.Equals(step.kind, "item", StringComparison.OrdinalIgnoreCase))
                            {
                                bool ok = ApplyReplayItem(step);
                                if (!ok)
                                {
                                    Console.WriteLine("[Undo] failed to re-apply item step: " + (step.op ?? step.kind));
                                    break;
                                }
                            }
                            else
                            {
                                Console.WriteLine("[Undo] unknown step kind encountered; stopping reapply.");
                                break;
                            }
                        }

                        RecalcVictoryAndAnnounce(_fc);
                        Console.WriteLine("[Undo] reverted " + removed + " move(s).");
                        DumpFreeCell(_fc);
                        break;
                    }
                case "board":
                    {
                        EnsureFc();
                        DumpFreeCell(_fc);
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

        // ===== Utility kept here =====

        // 세션 시작 시점 상태를 스냅샷으로 캡처해서 undo의 베이스로 사용
        static void SetSessionBaseToCurrent()
        {
            _baseIsSnapshot = true;
            _baseIsSeed = false;
            _baseSnapshot = MakeSnapshot(_fc);
            // Start a fresh timeline from this baseline so undo only affects post-baseline steps
            _log.Clear();
            _steps.Clear();
        }

        static (int temp, int grab, int siphon) CountItemUses(IList<ReplayMove> steps)
        {
            int t = 0, g = 0, s = 0;
            if (steps == null) return (0,0,0);
            for (int i = 0; i < steps.Count; i++)
            {
                var x = steps[i];
                if (!string.Equals(x.kind, "item", StringComparison.OrdinalIgnoreCase)) continue;
                var op = (x.op ?? string.Empty).ToLowerInvariant();
                if (op == "temp") t++;
                else if (op == "grab") g++;
                else if (op == "siphon") s++;
            }
            return (t, g, s);
        }

        static void SetSessionBaseToSeed(uint seed, FreeCellConfig cfg, string shuffleKind, IList<ReplayMove> steps, int currentTempCharges, int currentGrabCharges, int currentSiphonCharges)
        {
            _baseIsSnapshot = false;
            _baseIsSeed = true;
            _baseSeed = seed;
            _baseConfig = cfg;
            _baseShuffleKind = string.IsNullOrEmpty(shuffleKind) ? "xor" : shuffleKind;

            var (usedTemp, usedGrab, usedSiphon) = CountItemUses(steps);
            _baseTempCharges = currentTempCharges + usedTemp;
            _baseGrabCharges = currentGrabCharges + usedGrab;
            _baseSiphonCharges = currentSiphonCharges + usedSiphon;
            // 주의: seed 베이스에서는 타임라인을 비우지 않는다. undo는 이 타임라인을 기준으로 되돌림.
        }

        static void Help()
        {
            Console.WriteLine("Solitaire CLI v" + CliInfo.Version());
            Console.WriteLine("Commands:");
            Console.WriteLine("  repl                         Start interactive session");
            Console.WriteLine("  fc-new --seed <u32>          Start a new FreeCell game");
            Console.WriteLine("  fc-legal                     List legal moves");
            Console.WriteLine("  fc-move <Kind> <from> <to> [count]");
            Console.WriteLine("    Kind aliases: t2t, t2c, c2t, t2f, c2f, f2t, f2c");
            Console.WriteLine("  fc-f2t <fIdx> <tIdx>         Move from Foundation(fIdx) back to Tableau(tIdx) via rewind+branch");
            Console.WriteLine("  fc-f2c <fIdx> <cIdx>         Move from Foundation(fIdx) back to Cell(cIdx) via rewind+branch");
            Console.WriteLine("  items [show]                 Show item status");
            Console.WriteLine("  items set temp <n> [grab <n>] [siphon <n>]  Set item charges");
            Console.WriteLine("  use-temp                     Activate temporary cell (consumes 1 charge)");
            Console.WriteLine("  grab <t> <depth>             Pull that card to top of tableau t (consumes 1 charge)");
            Console.WriteLine("  siphon <s|h|d|c|rand>        Pull next-needed suit card from anywhere to foundation (consumes 1 charge)");
            Console.WriteLine("  save <path.json> [--note \"text\"] [--tag a,b,c]  Save current game as replay JSON");
            Console.WriteLine("  replay <path.json> [--until N]  Replay file (apply first N steps)");
            Console.WriteLine("  load <path.json>             Load file and set current state to result");
            Console.WriteLine("  replay-info <path.json>      Show metadata/config/move count");
            Console.WriteLine("  replay-diff <a.json> <b.json>  Compare two replay files");
            Console.WriteLine("  hint [N]                     Show top-N suggested moves with scores");
            Console.WriteLine("  auto-foundation              Auto-apply all legal moves to foundations");
            Console.WriteLine("  undo [N]                     Undo last N moves (since baseline snapshot)");
            Console.WriteLine("  board                        Print current board");
            Console.WriteLine("  pretty on|off                Toggle ANSI colored board rendering (default ON)");
            Console.WriteLine();
            Console.WriteLine("Index notes:");
            Console.WriteLine("  Tableaus: 0..7  | Cells: 0..3 (temp slot index is 4 when active) | Foundations(suit index): 0=Spade,1=Heart,2=Diamond,3=Club");
        }

        static void ShowItems()
        {
            Console.WriteLine("Items: TempCell " + (_tempActive ? "ACTIVE" : "INACTIVE") + " (idx " + _tempCellIndex + "), charges=" + _tempCharges +
                              " | Grab charges=" + _grabCharges + " | Siphon charges=" + _siphonCharges);
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
            ShowItems();
        }
    }
}
        