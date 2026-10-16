using System;

namespace FStudio.MatchEngine
{
    // Versioned gameplay-only stream. Presentation never consumes this state.
    public static class SoccerSimulationRandom
    {
        public const string Algorithm = "xorshift32-v1";
        private static uint state = 1;
        public static void Reset(int seed) => state = unchecked((uint)seed) == 0 ? 0x9e3779b9u : unchecked((uint)seed);
        private static uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
        public static int Range(int min, int max)
        {
            if (max < min) { var swap = min; min = max; max = swap; }
            if (max == min) return min;
            return (int)(min + (long)(Next() % (ulong)((long)max - min)));
        }
        public static float Range(float min, float max) => min + (max - min) * ((Next() >> 8) / 16777216f);
    }
}
