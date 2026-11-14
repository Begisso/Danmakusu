using System;
using System.Collections.Generic;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// How hard a boss is to play against, from 1 to 5 stars, shown in the boss list and in her description. The numbers are set by hand from what she does; a placeholder, with only the placeholder attack, has 1.
    /// A new boss needs a line here.
    /// </summary>
    public static class BossDifficulty
    {
        public const int MOST = 5;

        private static readonly Color4 easy = new Color4(0.3f, 0.85f, 0.3f, 1f);
        private static readonly Color4 middle = new Color4(1f, 0.85f, 0.15f, 1f);
        private static readonly Color4 hard = new Color4(0.95f, 0.2f, 0.2f, 1f);

        private static readonly Dictionary<BossMechanic, int> ratings = new Dictionary<BossMechanic, int>
        {
            // 1: little more than the map itself
            [BossMechanic.Swarm] = 3, // Wriggle
            [BossMechanic.Autumn] = 2, // Shizuha

            // 2: one new thing to keep an eye on
            [BossMechanic.Inchling] = 2, // Sukuna
            [BossMechanic.Crescent] = 4, // Mugetsu
            [BossMechanic.Necromancy] = 2, // Seiga
            [BossMechanic.Flowers] = 2, // Yuuka
            [BossMechanic.Fortune] = 3, // Joon
            [BossMechanic.Shockwave] = 2, // Yuugi
            [BossMechanic.Shikigami] = 2, // Chen
            [BossMechanic.Taiko] = 3, // Raiko (since 2026-11-08 what her ripples swell hurts like a slider body)
            [BossMechanic.Volley] = 3, // Seiran

            // 3: a mechanic that changes how the map is dodged
            [BossMechanic.Blindness] = 3, // Mystia
            [BossMechanic.Echo] = 3, // Kyouko
            [BossMechanic.Lunatic] = 4, // Reisen
            [BossMechanic.Heads] = 3, // Sekibanki
            [BossMechanic.Dowsing] = 3, // Nazrin
            [BossMechanic.FateSpear] = 3, // Remilia
            [BossMechanic.FreeFall] = 3, // Toyohime
            [BossMechanic.Unknown] = 3, // Nue
            [BossMechanic.Winter] = 3, // Letty
            [BossMechanic.Prism] = 3, // Sunny Milk
            [BossMechanic.Qi] = 3, // Meiling
            [BossMechanic.Smoke] = 3, // Sannyo (her clouds 1.5 times as big since 2026-11-08: 2.25 times the room, not enough for a star more)
            [BossMechanic.Spring] = 2, // Lily White
            [BossMechanic.Cloud] = 3, // Ichirin
            [BossMechanic.Return] = 3, // Yorihime
            [BossMechanic.Blades] = 4, // Nemuno
            [BossMechanic.Traps] = 3, // Enoko
            [BossMechanic.Tide] = 3, // Wakasagihime
            [BossMechanic.Storm] = 3, // Tojiko
            [BossMechanic.Stacking] = 3, // Ebisu
            [BossMechanic.Foxes] = 4, // Ran
            [BossMechanic.Bloodlust] = 3, // Chiyari
            [BossMechanic.Verdant] = 3, // Daiyousei
            [BossMechanic.ButterflyFans] = 4, // Yuyuko
            [BossMechanic.Freeze] = 3, // Cirno
            [BossMechanic.Miracle] = 3, // Sanae

            // 4: takes away what you rely on, or never lets up
            [BossMechanic.Bats] = 4, // Koakuma
            [BossMechanic.Darkness] = 4, // Rumia
            [BossMechanic.Silence] = 5, // Luna Child
            [BossMechanic.MasterSpark] = 4, // Marisa
            [BossMechanic.Doors] = 4, // Okina
            [BossMechanic.ScreenMirror] = 4, // Seija
            [BossMechanic.TimeStop] = 4, // Sakuya
            [BossMechanic.Unconscious] = 4, // Koishi
            [BossMechanic.SevenElements] = 4, // Patchouli
            [BossMechanic.Recollection] = 5, // Satori (4 until 2026-11-13: her trail behind the player)
            [BossMechanic.Death] = 4, // Zanmu
            [BossMechanic.Scrolls] = 4, // Byakuren
            [BossMechanic.Dream] = 4, // Doremy
            [BossMechanic.Haste] = 5, // Gengetsu
            [BossMechanic.ImpossibleRequests] = 3, // Kaguya
            [BossMechanic.Rays] = 4, // Miko
            [BossMechanic.Possibility] = 4, // Yumemi
            [BossMechanic.Market] = 3, // Chimata
            [BossMechanic.Judgement] = 4, // Eiki
            [BossMechanic.Stampede] = 4, // Saki
            [BossMechanic.Momentum] = 4, // Urumi
            [BossMechanic.Rebirth] = 4, // Mokou
            [BossMechanic.Dango] = 3, // Ringo
            [BossMechanic.Gaps] = 4, // Yukari
            [BossMechanic.Drift] = 4, // Futo
            [BossMechanic.Misfortune] = 4, // Shion
            [BossMechanic.Earthquake] = 2, // Tenshi
            [BossMechanic.HalfMoon] = 4, // Mima
            [BossMechanic.Claws] = 3, // Kagerou
            [BossMechanic.Reaping] = 3, // Elly
            [BossMechanic.Recording] = 4, // Hisami
            [BossMechanic.FantasySeal] = 4, // Reimu
            [BossMechanic.Anchors] = 4, // Murasa
            [BossMechanic.Orbs] = 3, // Misumaru
            [BossMechanic.Fissure] = 4, // Suika
            [BossMechanic.Scorch] = 4, // Yuki
            [BossMechanic.Recall] = 4, // Yumeko
            [BossMechanic.Pagoda] = 3, // Shou (4 until her cross turned half as fast, 2026-11-13)
            [BossMechanic.Constellation] = 3, // Eirin
            [BossMechanic.Galaxy] = 5, // Megumu
            [BossMechanic.Icosahedron] = 5, // Nitori
            [BossMechanic.Snowflake] = 4, // Mai
            [BossMechanic.Strings] = 4, // Benben
            [BossMechanic.Familiar] = 3, // Ellen
            [BossMechanic.Jiangshi] = 3, // Yoshika
            [BossMechanic.FallenAngel] = 4, // Matenshi (3 until 2026-11-14: her wings hurt, beams twice as often and two at a time)
            [BossMechanic.Koto] = 4, // Yatsuhashi
            [BossMechanic.Haniwa] = 4, // Keiki
            [BossMechanic.PipeFox] = 3, // Tsukasa
            [BossMechanic.Poltergeist] = 3, // Kana Anaberal
            [BossMechanic.Metamorphosis] = 3, // Eternity Larva

            // 5: the hardest there are
            [BossMechanic.Moon] = 5, // Clownpiece
            [BossMechanic.Nuclear] = 5, // Utsuho
            [BossMechanic.Purification] = 5, // Junko
            [BossMechanic.ThreeWorlds] = 5, // Hecatia
            [BossMechanic.Rewind] = 4, // Sagume
            [BossMechanic.Slash] = 5, // Youmu
            [BossMechanic.Destruction] = 5, // Flandre
        };

        /// <summary>How many stars a boss has.</summary>
        public static int Of(BossInfo boss) => boss.IsModified && ratings.TryGetValue(boss.Mechanic, out int rating) ? rating : 1;

        /// <summary>The colour of a rating: 1 green, 3 yellow, 5 red, and what lies between them for 2 and 4.</summary>
        public static Color4 ColourOf(int rating)
        {
            float u = (Math.Clamp(rating, 1, MOST) - 1f) / (MOST - 1f) * 2f;

            return u <= 1f ? mix(easy, middle, u) : mix(middle, hard, u - 1f);
        }

        private static Color4 mix(Color4 from, Color4 to, float u)
            => new Color4(from.R + (to.R - from.R) * u, from.G + (to.G - from.G) * u, from.B + (to.B - from.B) * u, 1f);
    }
}
