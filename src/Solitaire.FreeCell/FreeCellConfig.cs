namespace Solitaire.FreeCell
{
    /// <summary>
    /// FreeCell configuration, now with optional item counts.
    /// </summary>
    public sealed class FreeCellConfig
    {
        public int Cells { get; }
        public int Foundations { get; }
        public int Tableaus { get; }
        /// <summary>Allow multi-card sequence moves between tableaus.</summary>
        public bool AllowSequenceMoves { get; }

        // Items (default 0 to avoid affecting tests)
        public int TempCellCharges { get; }
        public int GrabCharges { get; }

        public FreeCellConfig(
            int cells = 4,
            int foundations = 4,
            int tableaus = 8,
            bool allowSequenceMoves = true,
            int tempCellCharges = 0,
            int grabCharges = 0
        )
        {
            Cells = cells;
            Foundations = foundations;
            Tableaus = tableaus;
            AllowSequenceMoves = allowSequenceMoves;
            TempCellCharges = tempCellCharges;
            GrabCharges = grabCharges;
        }

        public static FreeCellConfig Default
        {
            get { return new FreeCellConfig(true); }
        }

        // convenience ctor for Default
        private FreeCellConfig(bool defaultTag)
        {
            Cells = 4;
            Foundations = 4;
            Tableaus = 8;
            AllowSequenceMoves = true;
            TempCellCharges = 0;
            GrabCharges = 0;
        }
    }
}