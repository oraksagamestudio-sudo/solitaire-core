namespace Solitaire.Core
{
    public enum Suit { Spade, Heart, Diamond, Club }
    public enum Color { Black, Red }
    public enum Rank { Ace = 1, Two, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King }

    public struct Card
    {
        public Suit Suit { get; }
        public Rank Rank { get; }
        public bool FaceUp { get; }
        public Color Color => (Suit == Suit.Spade || Suit == Suit.Club) ? Color.Black : Color.Red;

        public Card(Suit suit, Rank rank, bool faceUp = true)
        {
            Suit = suit;
            Rank = rank;
            FaceUp = faceUp;
        }
        public override string ToString() => $"{Rank} of {Suit}{(FaceUp ? "" : " (down)")}";
    }

    public interface IRng
    {
        int Next();
        int Next(int minInclusive, int maxExclusive);
    }

    public interface IGameState<TMove>
    {
        int MoveCount { get; }
        bool IsVictory { get; }
        bool IsStalemate { get; }
        System.Collections.Generic.IEnumerable<TMove> GetLegalMoves();
        IGameState<TMove> Apply(TMove move);
    }

    public interface ISerializer<TState>
    {
        string Serialize(TState state);
        TState Deserialize(string payload);
    }
}
