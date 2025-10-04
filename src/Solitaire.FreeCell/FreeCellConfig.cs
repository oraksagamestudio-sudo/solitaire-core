namespace Solitaire.FreeCell
{
    /// <summary>
    /// FreeCell configuration.
    /// </summary>
    public sealed class FreeCellConfig
    {
        public int Cells { get; }
        public int Foundations { get; }
        public int Tableaus { get; }
        /// <summary>Allow multi-card sequence moves between tableaus.</summary>
        public bool AllowSequenceMoves { get; }

        public FreeCellConfig(int cells = 4, int foundations = 4, int tableaus = 8, bool allowSequenceMoves = true)
        {
            Cells = cells;
            Foundations = foundations;
            Tableaus = tableaus;
            AllowSequenceMoves = allowSequenceMoves;
        }

        public static FreeCellConfig Default => new FreeCellConfig(allowSequenceMoves: true);
    }
}
