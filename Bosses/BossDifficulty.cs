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
            [BossMechanic.FateSpear] = 4, // Remilia (3 until 2026-11-22: her knives of two, three and four blades hurt all along every blade - only round each blade's middle before)
            [BossMechanic.FreeFall] = 3, // Toyohime
            [BossMechanic.Unknown] = 4, // Nue (3 until 2026-11-21: her UFOs hurt like a slider body)
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
            [BossMechanic.Drift] = 5, // Futo (4 until 2026-11-24: five boats twice as fast, pushing beams too)
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
            [BossMechanic.Scorch] = 3, // Yuki (4 until 2026-11-20: one beam, twice as wide, instead of five)
            [BossMechanic.Recall] = 4, // Yumeko
            [BossMechanic.Pagoda] = 3, // Shou (4 until her cross turned half as fast, 2026-11-13)
            [BossMechanic.Constellation] = 4, // Eirin (3 until 2026-11-24: twice as many constellations, what one lets go standing again in the next)
            [BossMechanic.Galaxy] = 5, // Megumu
            [BossMechanic.Icosahedron] = 5, // Nitori
            [BossMechanic.Snowflake] = 4, // Mai
            [BossMechanic.Strings] = 4, // Benben
            [BossMechanic.Familiar] = 3, // Ellen
            [BossMechanic.Jiangshi] = 3, // Yoshika (4 for a few hours of 2026-11-22, while her copies stood on her looping curves as rings all over the screen; now a train follows her and leaves with her)
            [BossMechanic.FallenAngel] = 4, // Matenshi (3 until 2026-11-14: her wings hurt, beams twice as often and two at a time)
            [BossMechanic.Koto] = 5, // Yatsuhashi (4 until 2026-11-22: her beams twice as thick)
            [BossMechanic.Haniwa] = 4, // Keiki
            [BossMechanic.PipeFox] = 3, // Tsukasa
            [BossMechanic.Poltergeist] = 3, // Kana Anaberal
            [BossMechanic.Metamorphosis] = 4, // Eternity Larva (3 until 2026-11-20: twice as many swell, and they hurt like a slider body)
            [BossMechanic.Web] = 4, // Yamame
            [BossMechanic.Onbashira] = 4, // Kanako (3 until 2026-11-21: her circles twice as often and twice as big)
            [BossMechanic.Masks] = 5, // Hata no Kokoro (her moods aim and pull everything at you, with no pauses)
            [BossMechanic.Dolls] = 4, // Alice Margatroid (twelve fumos shooting streams with beams beside them, at you and to either side of you, no pauses)
            [BossMechanic.Trumpet] = 2, // Merlin Prismriver (the objects send out thick rings instead of bullets; standing on them clears the screen round them; 3 until 2026-11-22: nothing hurts round them and on their follow points)
            [BossMechanic.Kasha] = 4, // Rin Kaenbyou (what she fires stops and swells sixteen beats later, with a flashing warning; 3 until 2026-11-24, when the swell became three times its size, and her pauses' bullets swell too)
            [BossMechanic.Komainu] = 4, // Aunn Komano (curving beams with small loops, fast; every bullet sets off at you again and again - it no longer swells, 2026-11-22)
            [BossMechanic.Patrol] = 4, // Momiji Inubashiri (what leaves the top and the bottom piles up and crashes in the middle, flying off with beams behind it; no pauses)
            [BossMechanic.Starlight] = 4, // Star Sapphire (3 until 2026-11-22, when its stars became twice as dense along its lines; a turning sphere of stars spinning round her (it tumbled until 2026-11-22) as big as the screen, growing and pulled back, one after another; no pauses)
            [BossMechanic.Frog] = 5, // Suwako Moriya (Junko's beams in a fan, faster, bouncing once; her reshots in five fans)
            [BossMechanic.Wind] = 5, // Aya Shameimaru (2026-11-22: dashes edge to edge through the bullets, leaving beams; what she passes comes apart into feathers flying out sideways, twice as fast; homing maple fans. 4 until she dashed twice as fast on the half beats and fanned out twice as many leaves twice as often, the same day)
            [BossMechanic.Spoiler] = 3, // Hatate Himekaidou (2026-11-22: a photo every eight beats turns every bullet one way, half as fast; caught in the frame, the character lags behind the mouse for two beats)
            [BossMechanic.Creation] = 4, // Shinki (2026-11-22: everything fired comes out of her six wings as turning streams; the lasers from her wings, straight)
            [BossMechanic.Mirage] = 4, // Watari Nina (2026-11-22: a huge golden beam at you every 16 beats; what leaves the screen comes back in, flying at it; fans of big balls)
            [BossMechanic.Effigy] = 4, // Ubame Chirizuka (2026-11-22: fans of dolls that hurt and shoot what they touch at you again, bigger; those bounce off each other)
            [BossMechanic.Fossil] = 4, // Ariya Iwanaga (2026-11-22: everything stops every 16 beats, the circles and sliders hold their shots and fire them all at once; a grid of bouncing ellipses that never stops)
            [BossMechanic.Keyboard] = 3, // Lyrica Prismriver (2026-11-23: every circle and slider again four beats later, further out, with half its shots)
            [BossMechanic.Vampire] = 3, // Kurumi (2026-11-23: sliders shoot beams, circles rings of straight beams; every beam that leaves the screen fans bullets back in)
            [BossMechanic.Staff] = 4, // Son Biten (2026-11-23: rings of fifteen thrown beams spinning round their own middles (round her until 2026-11-24); the sliders' beams stand, doubled, then fly off spinning)
            [BossMechanic.Curse] = 4, // Noroiko (2026-11-23: two homing, speeding curses every half beat; the sliders throw them too)
            [BossMechanic.Surprise] = 4, // Kogasa Tatara (2026-11-23: six very dense spirals; the circles and sliders turn round the middle, stay eight beats hurting, and shoot out over that time)
            [BossMechanic.Thunder] = 4, // Iku Nagae (2026-11-24: spiralling rings with beams behind every second bullet; every second bullet of every ring tied in fives by beams)
            [BossMechanic.Sword] = 4, // Meira (2026-11-24: slides after you leaving thick beams that burst into rings; streams of aimed fans; no pauses)
            [BossMechanic.Statue] = 3, // Narumi Yatadera (2026-11-24: everything at half speed until her waves give it back; homing balls shooting rings; no pauses)
            [BossMechanic.Jealousy] = 4, // Parsee Mizuhashi (2026-11-24: balls leaving trails of Yuyuko's flowers that burst into petals; a clone in the middle whose arrowheads fan back in from the edge)

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
