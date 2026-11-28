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
            [BossMechanic.Flowers] = 3, // Yuuka (2 until 2026-11-28, when the sunflowers' stems and green leaves began to hurt too)
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
            [BossMechanic.Prism] = 4, // Sunny Milk (3 until 2026-11-25: every split bullet has a beam tail two beats long that hurts)
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
            [BossMechanic.Miracle] = 4, // Sanae (3 until 2026-11-28, when every second ring of her attacks came out of a spinning star in the middle, twice as dense)

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
            [BossMechanic.Dango] = 2, // Ringo (3 until 2026-11-28, when only every second ring left copies: half the threes and half the dango)
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
            [BossMechanic.Kasha] = 5, // Rin Kaenbyou (4 until 2026-11-28, when it stopped eight beats later instead of sixteen - twice as many swollen at once - and swelled to four and a half times its size; what she fires stops and swells sixteen beats later, with a flashing warning; 3 until 2026-11-24, when the swell became three times its size, and her pauses' bullets swell too)
            [BossMechanic.Komainu] = 3, // Aunn Komano (4 until 2026-11-28, when her bullets' chase was halved to the speed they were fired at;curving beams with small loops, fast; every bullet sets off at you again and again - it no longer swells, 2026-11-22)
            [BossMechanic.Patrol] = 4, // Momiji Inubashiri (what leaves the top and the bottom piles up and crashes in the middle, flying off with beams behind it; no pauses)
            [BossMechanic.Starlight] = 4, // Star Sapphire (3 until 2026-11-22, when its stars became twice as dense along its lines; a turning sphere of stars spinning round her (it tumbled until 2026-11-22) as big as the screen, growing and pulled back, one after another; no pauses)
            [BossMechanic.Frog] = 5, // Suwako Moriya (Junko's beams in a fan, faster, bouncing once; her reshots in five fans)
            [BossMechanic.Wind] = 5, // Aya Shameimaru (2026-11-22: dashes edge to edge through the bullets, leaving beams; what she passes comes apart into feathers flying out sideways, twice as fast; homing maple fans. 4 until she dashed twice as fast on the half beats and fanned out twice as many leaves twice as often, the same day)
            [BossMechanic.Spoiler] = 2, // Hatate Himekaidou (3 until 2026-11-25, when her photo turned only what is in the frame. 2026-11-22: a photo every eight beats turns every bullet one way, half as fast; caught in the frame, the character lags behind the mouse for two beats)
            [BossMechanic.Creation] = 5, // Shinki (4 until 2026-11-25: every bullet out of two wings, the streams twice as dense, her lasers four times as long. 2026-11-22: everything fired comes out of her six wings as turning streams; the lasers from her wings, straight)
            [BossMechanic.Mirage] = 4, // Watari Nina (2026-11-22: a huge golden beam at you every 16 beats; what leaves the screen comes back in, flying at it; fans of big balls)
            [BossMechanic.Effigy] = 4, // Ubame Chirizuka (2026-11-22: fans of dolls that hurt and shoot what they touch at you again, bigger; those bounce off each other)
            [BossMechanic.Fossil] = 4, // Ariya Iwanaga (2026-11-22: everything stops every 16 beats, the circles and sliders hold their shots and fire them all at once; a grid of bouncing ellipses that never stops)
            [BossMechanic.Keyboard] = 3, // Lyrica Prismriver (2026-11-23: every circle and slider again four beats later, further out, with half its shots)
            [BossMechanic.Vampire] = 3, // Kurumi (2026-11-23: sliders shoot beams, circles rings of straight beams; every beam that leaves the screen fans bullets back in)
            [BossMechanic.Staff] = 4, // Son Biten (2026-11-23: rings of fifteen thrown beams spinning round their own middles (round her until 2026-11-24); the sliders' beams stand, doubled, then fly off spinning)
            [BossMechanic.Curse] = 5, // Noroiko (4 until 2026-11-28, when her curses got their real size - they were dots since 2026-11-23 - and every ring of her section became 呪 in beams; 2026-11-23: two homing, speeding curses every half beat; the sliders throw them too, as big as they are long since 2026-11-26)
            [BossMechanic.Surprise] = 4, // Kogasa Tatara (2026-11-23: six very dense spirals; the circles and sliders turn round the middle, stay eight beats hurting, and shoot out over that time)
            [BossMechanic.Thunder] = 4, // Iku Nagae (5 from 2026-11-27 until 2026-11-28, when only every second ring became a star and every beam of one got five gaps; 4 until 2026-11-27: every ring of five a perfect star of slider-wide beams; 2026-11-26, reworked: every ring of her section five bullets joined as a star of beams, half the stars hurting and half harmless, swapping every four beats; 2026-11-24: spiralling rings with beams behind every second bullet, ties in fives)
            [BossMechanic.Sword] = 4, // Meira (2026-11-24: slides after you leaving thick beams that burst into rings; streams of aimed fans; no pauses. 2026-11-26: the slides straight into where you were, at half the speed, the beams half as thick)
            [BossMechanic.Statue] = 4, // Narumi Yatadera (3 until 2026-11-28, when what her waves take flies at twice its own full speed - twice the slowed speed it had before, hardly seen; 2026-11-24: everything at half speed until her waves give it back; homing balls shooting rings; no pauses)
            [BossMechanic.Jealousy] = 4, // Parsee Mizuhashi (2026-11-24: balls leaving trails of Yuyuko's flowers that burst into petals; a clone in the middle whose arrowheads fan back in from the edge)
            [BossMechanic.Gatekeeper] = 4, // Sara (2026-11-25: everything twice as fast by her, half as fast at the edge; two thick beams closing in on you; gapless rings with narrow gaps)
            [BossMechanic.Bamboo] = 4, // Mai Teireida (2026-11-25: 36 beams round her, always shown or there; every bullet moves in steps, 45 degrees to either side; no pauses)
            [BossMechanic.Myoga] = 4, // Satono Nishida (2026-11-25: what leaves the top comes back down, what leaves the bottom sends a talisman at you; streams of stars)
            [BossMechanic.Satellites] = 3, // Takane Yamashiro (2026-11-25: the bigger the slower, basic bullets twice as fast; big bullets grab the small ones into orbits; no pauses)
            [BossMechanic.Hare] = 3, // Tewi Inaba (2026-11-25: an X of four big beams that swings into two parallel ones beside her, sixteen beats; she stays put)
            [BossMechanic.Psychokinesis] = 4, // Sumireko Usami (2026-11-25: a huge beam over you that drifts your way and explodes into stars, every sixteen beats)
            [BossMechanic.Citrus] = 4, // Orange (3 until 2026-11-28, when four oranges took turns instead of two; 2026-11-25: an orange in the middle whose ten slices fly out and back, two oranges taking turns)
            [BossMechanic.Pinwheel] = 3, // Yuiman Asama (2026-11-26: everything fired comes out of four turning points in arcs, her own streams too; rings of big arrowheads that let bullets out sideways)
            [BossMechanic.Cubes] = 4, // Yachie Kicchou (2026-11-26: breathing rings of turning arrowheads every beat, a lattice of cubes over the screen solid from the middle out)
            [BossMechanic.Space] = 4, // Rikako Asakura (2026-11-26: asteroids that bullets bounce off, a satellite with a big two-way beam and a grid that waves make solid; 2026-11-27: twice as many asteroids, half as big, her satellite turning half as fast)
            [BossMechanic.YinYang] = 3, // Shingyoku (2026-11-27: throws herself, or the others, as a big yin-yang orb that bullets bounce off, every eight beats)
            [BossMechanic.EvilEyes] = 5, // YuugenMagan (4 until 2026-11-28, when every bullet her eyes shoot again became three, thirty degrees apart; 2026-11-27: five eyes that shoot every second bullet again at you, thunders every sixteen beats, her sliders' beams, what is hit thrown back golden)
            [BossMechanic.BigBat] = 3, // Elis (2026-11-27: chases you as a big bat, taking the bullets she flies through, then shoots them back side by side with beams behind them)
            [BossMechanic.AngelOfDeath] = 4, // Sariel (2026-11-27: rings of homing bats that stream bullets at you, beams that leave turn into bats, bullets come in at the other side; her last word aims everything at you)
            [BossMechanic.Underworld] = 4, // Kikuri (2026-11-27: in the middle; everything flies out from her in her attacks, rings of 36 waving beams, every fourth bullet out of the screen shot again from her)
            [BossMechanic.Sweep] = 3, // Konngara (2026-11-27: an endless beam that sweeps once round her every sixteen beats, cutting the bullets in its way)
            [BossMechanic.Distance] = 4, // Komachi Onozuka (2026-11-27: stops every other half beat and throws a fast flying arc at you that cuts what it goes over; 3 until arcs came twice as often, twice as fast)
            [BossMechanic.WellBucket] = 3, // Kisume (2026-11-27: everything falls from the top; she follows you along the top and fires three thick beams down every four beats)
            [BossMechanic.KnifeTrail] = 3, // Louise (2026-11-27: every twentieth bullet big, leaving knives that wait eight beats and fly at you)
            [BossMechanic.Poison] = 3, // Medicine Melancholy (2026-11-27: bullets that touch burst into petals; fast streams from all round her)
            [BossMechanic.Mining] = 4, // Momoyo Himemushi (2026-11-28: twice as many blocks, flying four times as slowly and hurting in flight, the centipede turns at you for eight beats; 2026-11-27: stone blocks along the walls, a charging centipede that bursts them into squares of bullets)
            [BossMechanic.Rooster] = 3, // Kutaka Niwatari (2026-11-27: everything at half speed; what the circles and sliders fire comes at you in two streams)
            [BossMechanic.History] = 4, // Keine Kamishirasawa (2026-11-28: streams of beam from her magic circle; recorded places shoot beams or rings that split at the walls twice)
            [BossMechanic.Harvest] = 4, // Minoriko Aki (2026-11-28: what is on the screen comes again as slow leaves; pairs of beams all round her, the leaves bouncing off them)
            [BossMechanic.Beckon] = 3, // Mike Goutokuji (2026-11-28: big bullets that burst into rings; every tenth bullet big and golden; golden waves burst the big ones)
            [BossMechanic.Violin] = 5, // Lunasa Prismriver (4 until 2026-11-28, when her beams fanned out from her circle and stayed twice as long - one volley always there - cutting every bullet they touch, with osu!mania notes in their gaps; 2026-11-27: five parallel beams at you every eight beats; the objects break into pieces that hurt, cut in two along her beams)

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
