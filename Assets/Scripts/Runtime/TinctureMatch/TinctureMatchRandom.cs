using System;

namespace BarPromenade
{
    internal struct TinctureMatchRandom
    {
        private uint state;

        public TinctureMatchRandom(int seed)
        {
            uint value = unchecked((uint)seed);
            value ^= 0x9E3779B9u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            state = value == 0u ? 0xA341316Cu : value;
        }

        public int NextInt(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMaximum));
            }

            return (int)(
                ((ulong)NextUInt() * (uint)exclusiveMaximum) >> 32);
        }

        private uint NextUInt()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }
    }
}
