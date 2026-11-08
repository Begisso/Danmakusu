using System;
using System.Collections.Generic;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// Chooses the bosses of a map from its combo colours: the colours are compared to the colour signature of every boss in OKLab (a colour space in which
    /// the distance between two colours is about how different they look) and the boss whose signature is nearest wins.
    /// Up to 4 colours give one boss; more than 4 give two: the first 4 colours choose the first one, the last 4 the second one
    /// (never the same: if both would choose the same boss, the second gets his next-best match).
    /// </summary>
    public static class BossMatcher
    {
        /// <summary>The colours that choose one boss.</summary>
        public const int COLOURS_PER_BOSS = 4;

        public static BossInfo[] Match(IReadOnlyList<Color4> mapColours)
        {
            if (mapColours.Count == 0)
                return [BossRoster.Default];

            if (mapColours.Count <= COLOURS_PER_BOSS)
                return [rank(mapColours, 0, mapColours.Count)[0]];

            var first = rank(mapColours, 0, COLOURS_PER_BOSS);
            var second = rank(mapColours, mapColours.Count - COLOURS_PER_BOSS, COLOURS_PER_BOSS);

            // (first and last colours may share colours when there are 5-7 of them; that is fine)
            BossInfo other = second[0] != first[0] ? second[0] : second[1];

            return [first[0], other];
        }

        /// <summary>The bosses, the one whose signature is nearest to colours [start, start + count) first.</summary>
        private static BossInfo[] rank(IReadOnlyList<Color4> colours, int start, int count)
        {
            var lab = new (float l, float a, float b)[count];

            for (int i = 0; i < count; i++)
                lab[i] = toOkLab(colours[start + i]);

            var roster = BossRoster.All;
            var scored = new (float score, int index)[roster.Count];

            for (int i = 0; i < roster.Count; i++)
                scored[i] = (distance(lab, roster[i]), i);

            Array.Sort(scored, (x, y) => x.score != y.score ? x.score.CompareTo(y.score) : x.index.CompareTo(y.index));

            var result = new BossInfo[Math.Min(3, roster.Count)];

            for (int i = 0; i < result.Length; i++)
                result[i] = roster[scored[i].index];

            return result;
        }

        /// <summary>How far a boss's signature is from the map's colours: every map colour to its nearest signature colour (equal weights) plus every signature colour to its nearest map colour (weighted by its share).</summary>
        private static float distance((float l, float a, float b)[] map, BossInfo boss)
        {
            var signature = new (float l, float a, float b)[boss.Colours.Length];

            for (int j = 0; j < signature.Length; j++)
                signature[j] = toOkLab(boss.Colours[j]);

            float mapToBoss = 0;

            foreach (var colour in map)
            {
                float nearest = float.MaxValue;

                foreach (var sig in signature)
                    nearest = Math.Min(nearest, labDistance(colour, sig));

                mapToBoss += nearest / map.Length;
            }

            float bossToMap = 0;
            float weightSum = 0;

            for (int j = 0; j < signature.Length; j++)
            {
                float nearest = float.MaxValue;

                foreach (var colour in map)
                    nearest = Math.Min(nearest, labDistance(signature[j], colour));

                bossToMap += nearest * boss.Weights[j];
                weightSum += boss.Weights[j];
            }

            return mapToBoss + bossToMap / Math.Max(weightSum, 1e-6f);
        }

        private static float labDistance((float l, float a, float b) x, (float l, float a, float b) y)
        {
            float dl = x.l - y.l, da = x.a - y.a, db = x.b - y.b;
            return MathF.Sqrt(dl * dl + da * da + db * db);
        }

        private static float linear(float c)
            => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// <summary>sRGB -> OKLab (Bjorn Ottosson).</summary>
        private static (float l, float a, float b) toOkLab(Color4 colour)
        {
            float r = linear(colour.R), g = linear(colour.G), b = linear(colour.B);

            float l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
            float m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
            float s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);

            return (0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }
    }
}
