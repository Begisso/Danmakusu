// Made by the tool that baked the pictures of the Earth (Resources/Textures/earth_*.webp): where each chunk sits in them. Do not change it by hand.
namespace osu.Game.Rulesets.Danmakusu.UI
{
    internal static class EarthPieces
    {
        /// <summary>Every chunk of the Earth: its middle and its size, as shares of the Earth's radius, along the line of the cut and across it; which end of the line it is at; whether it breaks away at the cut (or else at the split).</summary>
        public static readonly (float X, float Y, float W, float H, int End, bool AtCut)[] All =
        {
            (-0.8509f, 0.0091f, 0.1940f, 0.1745f, 0, true),
            (-0.9095f, 0.1413f, 0.1810f, 0.2826f, 0, true),
            (-0.7904f, 0.2064f, 0.1458f, 0.1992f, 0, false),
            (-0.9128f, -0.0592f, 0.1745f, 0.1758f, 0, true),
            (-0.8516f, 0.2806f, 0.2266f, 0.1810f, 0, false),
            (-0.7786f, -0.1595f, 0.2109f, 0.2383f, 0, false),
            (-0.7552f, 0.0352f, 0.1641f, 0.2188f, 0, false),
            (-0.9349f, -0.1549f, 0.1302f, 0.2734f, 0, false),
            (0.9251f, -0.1764f, 0.1497f, 0.1940f, 1, true),
            (0.8457f, -0.1341f, 0.1237f, 0.1432f, 1, false),
            (0.8171f, 0.0163f, 0.3138f, 0.2253f, 1, true),
            (0.8164f, -0.2096f, 0.0911f, 0.1146f, 1, false),
            (0.7363f, -0.0723f, 0.1549f, 0.2331f, 1, false),
            (0.8242f, 0.2220f, 0.3151f, 0.2539f, 1, false),
            (0.7780f, -0.1797f, 0.0898f, 0.1328f, 1, false),
            (0.9043f, 0.0579f, 0.1914f, 0.3086f, 1, true),
        };

        /// <summary>At 121 places evenly along the line of the cut: how far (a share of the Earth's radius) it is to what will be gone by the time the line is there, a hole of a chunk that breaks away at the cut or the rim. 0: nothing is there for the line to glow on.</summary>
        public static readonly float[] LineRoom =
        {
            0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0068f, 0.0228f, 0.0326f, 0.0368f, 0.0514f, 0.0661f, 0.0890f, 0.1113f, 0.1275f, 0.1477f, 0.1635f, 0.1788f, 0.2011f, 0.2195f, 0.2369f, 0.2560f, 0.2670f, 0.2780f, 0.2931f, 0.3078f, 0.3259f, 0.3471f, 0.3608f, 0.3743f, 0.3917f, 0.4059f, 0.4261f, 0.4484f, 0.4614f, 0.4767f, 0.4898f, 0.4976f, 0.5124f, 0.5274f, 0.5417f, 0.5586f, 0.5761f, 0.6016f, 0.6178f, 0.6336f, 0.6495f, 0.6594f, 0.6758f, 0.6930f, 0.7083f, 0.7269f, 0.7171f, 0.6943f, 0.6781f, 0.6633f, 0.6469f, 0.6354f, 0.6196f, 0.5985f, 0.5789f, 0.5577f, 0.5359f, 0.5211f, 0.5039f, 0.4816f, 0.4625f, 0.4391f, 0.4184f, 0.4071f, 0.3923f, 0.3786f, 0.3665f, 0.3464f, 0.3297f, 0.3165f, 0.2996f, 0.2859f, 0.2722f, 0.2517f, 0.2305f, 0.2114f, 0.1896f, 0.1748f, 0.1630f, 0.1439f, 0.1281f, 0.1101f, 0.0894f, 0.0775f, 0.0644f, 0.0488f, 0.0373f, 0.0169f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 0.0000f, 
        };

        /// <summary>The very big chunks the two halves break into at the split: the middle and the size of each, as shares of the Earth's radius, along the line of the cut and across it, and which half it is of.</summary>
        public static readonly (float X, float Y, float W, float H, int Half)[] Big =
        {
            (-0.4844f, -0.2962f, 0.6094f, 0.6862f, 0),
            (0.0469f, -0.2741f, 0.5807f, 0.5091f, 0),
            (0.6159f, -0.3047f, 0.7031f, 0.4271f, 0),
            (-0.6270f, -0.5827f, 0.6654f, 0.6706f, 0),
            (-0.0775f, -0.7318f, 0.6549f, 0.5365f, 0),
            (0.5267f, -0.6862f, 0.6732f, 0.5729f, 0),
            (-0.4720f, 0.1458f, 0.6133f, 0.4193f, 1),
            (0.0117f, 0.1745f, 0.4453f, 0.4844f, 1),
            (0.4733f, 0.2591f, 0.6523f, 0.4948f, 1),
            (-0.6022f, 0.6107f, 0.6602f, 0.6849f, 1),
            (-0.0378f, 0.6354f, 0.5703f, 0.7292f, 1),
            (0.5697f, 0.6133f, 0.7435f, 0.7370f, 1),
        };

        /// <summary>The many small chunks that come out all along the cut at the split: the middle and the size of each, as shares of the Earth's radius, along the line of the cut and across it.</summary>
        public static readonly (float X, float Y, float W, float H)[] Debris =
        {
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (-0.7109f, -0.1016f, 0.1042f, 0.1432f),
            (-0.6973f, 0.1120f, 0.1185f, 0.1667f),
            (-0.6432f, -0.0664f, 0.1224f, 0.1641f),
            (-0.6009f, 0.1257f, 0.1315f, 0.1940f),
            (-0.5553f, -0.0944f, 0.1523f, 0.1966f),
            (-0.5137f, 0.1406f, 0.1445f, 0.1927f),
            (-0.4714f, -0.0684f, 0.0990f, 0.1576f),
            (-0.4447f, 0.0736f, 0.1393f, 0.1836f),
            (-0.3978f, -0.0703f, 0.0794f, 0.1328f),
            (-0.3470f, 0.1022f, 0.2018f, 0.2279f),
            (-0.3171f, -0.0846f, 0.1680f, 0.1797f),
            (-0.2682f, 0.0664f, 0.1146f, 0.1380f),
            (-0.2454f, -0.1413f, 0.1706f, 0.1992f),
            (-0.1934f, 0.0124f, 0.1211f, 0.1393f),
            (-0.1621f, -0.1934f, 0.1523f, 0.1862f),
            (-0.1198f, 0.0566f, 0.1250f, 0.1628f),
            (-0.0729f, -0.1549f, 0.1927f, 0.2396f),
            (-0.0254f, 0.0553f, 0.1003f, 0.1654f),
            (0.0391f, -0.1406f, 0.1198f, 0.1719f),
            (0.0482f, 0.0384f, 0.1797f, 0.2201f),
            (0.1172f, -0.1517f, 0.0911f, 0.1107f),
            (0.1706f, 0.0430f, 0.1302f, 0.1380f),
            (0.1712f, -0.0885f, 0.2044f, 0.1927f),
            (0.2396f, 0.1315f, 0.1953f, 0.1849f),
            (0.2878f, -0.0456f, 0.1536f, 0.1927f),
            (0.3177f, 0.0807f, 0.1328f, 0.1354f),
            (0.3724f, -0.0482f, 0.1068f, 0.1484f),
            (0.4062f, 0.1022f, 0.1198f, 0.1576f),
            (0.4225f, -0.1035f, 0.2227f, 0.2461f),
            (0.4863f, 0.0742f, 0.1602f, 0.1562f),
            (0.5130f, -0.0638f, 0.1198f, 0.1927f),
            (0.5755f, 0.1458f, 0.2266f, 0.2214f),
            (0.6224f, -0.0638f, 0.1589f, 0.2214f),
            (0.6341f, 0.0495f, 0.1068f, 0.1510f),
            (0.6914f, -0.0716f, 0.0781f, 0.1016f),
            (0.6849f, 0.1452f, 0.0391f, 0.0612f),
            (0.7318f, -0.0846f, 0.0182f, 0.0391f),
            (0.8001f, 0.2428f, 0.0924f, 0.0326f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
            (0.0007f, 0.0007f, 0.0065f, 0.0065f),
        };
    }
}
