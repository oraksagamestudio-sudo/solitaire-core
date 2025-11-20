using System;
using System.Collections.Generic;

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
        public ReplaySnapshot snapshot { get; set; } = null; // optional
    }

    public sealed class ReplayConfig
    {
        public int cells { get; set; } = 4;
        public int foundations { get; set; } = 4;
        public int tableaus { get; set; } = 8;
        public bool allowSequenceMoves { get; set; } = true;
    }

    // kind: "move" (normal move) | "item" (temp/grab/siphon)
    // Legacy: kind holds MoveKind (= move) when op is empty
    public sealed class ReplayMove
    {
        public string kind { get; set; } = "";
        public string op { get; set; } = "";
        public int from { get; set; }
        public int to { get; set; }
        public int count { get; set; }
        public int arg { get; set; }
    }

    // Compact snapshot (2-chars per card)
    public sealed class ReplaySnapshot
    {
        public string[] tableaus { get; set; } = new string[0];    // bottom..top
        public string[] cells { get; set; } = new string[0];       // "" or 2-char
        public string[] foundations { get; set; } = new string[4]; // TOP only
        public int moveCount { get; set; } = 0;

        // items
        public bool tempActive { get; set; } = false;
        public int tempCellIndex { get; set; } = 4;
        public int tempCharges { get; set; } = 0;
        public int grabCharges { get; set; } = 0;
        public int siphonCharges { get; set; } = 0;
    }
}