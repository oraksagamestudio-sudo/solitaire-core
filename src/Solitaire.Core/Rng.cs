namespace Solitaire.Core
{
    public sealed class XorShift32 : IRng
    {
        private uint _state;
        public XorShift32(uint seed)
        {
            _state = seed == 0 ? 2463534242u : seed;
        }
        public int Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return (int)(x & 0x7FFFFFFF);
        }
        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new System.ArgumentOutOfRangeException(nameof(maxExclusive));
            uint range = (uint)(maxExclusive - minInclusive);
            uint val = (uint)Next();
            return minInclusive + (int)(val % range);
        }
    }
}
