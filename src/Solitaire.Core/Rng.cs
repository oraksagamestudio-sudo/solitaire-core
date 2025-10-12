using System;
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


    /// <summary>
    /// .NET System.Random compatible RNG for deterministic legacy shuffles.
    /// </summary>
    public sealed class DotNetRandom : IRng
    {
        private const int MBIG = int.MaxValue;
        private const int MSEED = 161803398;
        private const int MZ = 0;
        private int inext;
        private int inextp;
        private readonly int[] SeedArray = new int[56];

        public DotNetRandom(int Seed)
        {
            int subtraction = MSEED - Math.Abs(Seed);
            if (subtraction < 0) subtraction += MBIG;
            SeedArray[55] = subtraction;
            int mj = subtraction, mk = 1;
            for (int i = 1; i < 55; i++)
            {
                int ii = (21 * i) % 55;
                SeedArray[ii] = mk;
                mk = mj - mk;
                if (mk < 0) mk += MBIG;
                mj = SeedArray[ii];
            }
            inext = 0;
            inextp = 21;
            for (int k = 0; k < 4; k++)
            {
                for (int i = 1; i < 56; i++)
                {
                    SeedArray[i] -= SeedArray[1 + (i + 30) % 55];
                    if (SeedArray[i] < 0) SeedArray[i] += MBIG;
                }
            }
        }

        private int InternalSample()
        {
            int retVal;
            int locINext = inext;
            int locINextp = inextp;

            if (++locINext >= 56) locINext = 1;
            if (++locINextp >= 56) locINextp = 1;

            retVal = SeedArray[locINext] - SeedArray[locINextp];
            if (retVal == MBIG) retVal--;
            if (retVal < 0) retVal += MBIG;

            SeedArray[locINext] = retVal;
            inext = locINext;
            inextp = locINextp;

            return retVal;
        }

        public int Next()
        {
            return InternalSample();
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            if (minInclusive == maxExclusive) return minInclusive;
            if (minInclusive > maxExclusive) { var t = minInclusive; minInclusive = maxExclusive; maxExclusive = t; }
            // Scale InternalSample() into [min,max)
            double sample = InternalSample() * (1.0 / MBIG);
            int range = maxExclusive - minInclusive;
            int result = (int)(minInclusive + (sample * range));
            if (result >= maxExclusive) result = maxExclusive - 1;
            return result;
        }
    }

}
