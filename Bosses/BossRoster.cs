using System.Collections.Generic;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// The bosses a map can have: one entry per character. The colour signature of an entry (1-4 colours with their share of the character's clothes,
    /// hair, tools and accessories - not the skin) is what the combo colours of a map are matched against (see <see cref="BossMatcher"/>).
    /// The pictures are Resources/Bosses/Sprites/&lt;Id&gt;.png and Resources/Bosses/Heads/&lt;Id&gt;.png.
    /// The signature of an entry is four colours picked by eye from the character's picture, in this order: eye, hair, main clothes colour, second clothes colour (or tool / weapon).
    /// </summary>
    public static class BossRoster
    {
        private static BossInfo make(int index, string id, string name, int template, BossMechanic mechanic, params (int r, int g, int b, float weight)[] colours)
        {
            var list = new Color4[colours.Length];
            var weights = new float[colours.Length];

            for (int i = 0; i < colours.Length; i++)
            {
                list[i] = new Color4((byte)colours[i].r, (byte)colours[i].g, (byte)colours[i].b, 255);
                weights[i] = colours[i].weight;
            }

            return new BossInfo(index, id, name, template, mechanic, list, weights);
        }

        public static readonly IReadOnlyList<BossInfo> All = new[]
        {
            make(0, "alice_margatroid", "Alice Margatroid", 1, BossMechanic.Dolls, (74, 143, 216, 0.2f), (232, 207, 116, 0.3f), (63, 100, 188, 0.3f), (240, 238, 234, 0.2f)),
            make(1, "aunn_komano", "Aunn Komano", 2, BossMechanic.None, (63, 165, 138, 0.2f), (143, 211, 181, 0.3f), (240, 230, 226, 0.3f), (208, 72, 72, 0.2f)),
            make(2, "aya_shameimaru", "Aya Shameimaru", 3, BossMechanic.None, (192, 58, 58, 0.2f), (42, 26, 26, 0.3f), (244, 232, 228, 0.3f), (214, 58, 54, 0.2f)),
            make(3, "benben_tsukumo", "Benben Tsukumo", 4, BossMechanic.None, (200, 74, 106, 0.2f), (154, 127, 200, 0.3f), (240, 168, 96, 0.3f), (184, 71, 46, 0.2f)),
            make(4, "biten_son", "Biten Son", 5, BossMechanic.None, (138, 90, 46, 0.2f), (138, 90, 50, 0.3f), (79, 168, 232, 0.3f), (94, 158, 94, 0.2f)),
            make(5, "byakuren_hijiri", "Byakuren Hijiri", 1, BossMechanic.Scrolls, (74, 58, 58, 0.2f), (90, 58, 110, 0.3f), (233, 169, 138, 0.3f), (74, 58, 80, 0.2f)),
            make(6, "chen", "Chen", 2, BossMechanic.Shikigami, (106, 58, 42, 0.2f), (154, 74, 58, 0.3f), (214, 58, 58, 0.3f), (63, 122, 69, 0.2f)),
            make(7, "chimata_tenkyuu", "Chimata Tenkyuu", 3, BossMechanic.Market, (58, 42, 138, 0.2f), (47, 42, 138, 0.3f), (90, 75, 200, 0.3f), (242, 232, 58, 0.2f)),
            make(8, "chiyari_tenkajin", "Chiyari Tenkajin", 4, BossMechanic.Bloodlust, (168, 58, 138, 0.2f), (58, 42, 122, 0.3f), (168, 216, 160, 0.3f), (106, 42, 144, 0.2f)),
            make(9, "chiyuri_kitashirakawa", "Chiyuri Kitashirakawa", 5, BossMechanic.None, (200, 160, 48, 0.2f), (232, 192, 48, 0.3f), (248, 246, 240, 0.3f), (62, 74, 136, 0.2f)),
            make(10, "cirno", "Cirno", 1, BossMechanic.Freeze, (58, 106, 224, 0.2f), (111, 168, 232, 0.3f), (90, 106, 208, 0.3f), (168, 216, 240, 0.2f)),
            make(11, "clownpiece", "Clownpiece", 2, BossMechanic.Moon, (216, 72, 111, 0.2f), (240, 160, 74, 0.3f), (216, 64, 63, 0.3f), (74, 88, 184, 0.2f)),
            make(12, "daiyousei", "Daiyousei", 3, BossMechanic.Verdant, (106, 192, 96, 0.2f), (122, 216, 74, 0.3f), (74, 120, 216, 0.3f), (242, 216, 72, 0.2f)),
            make(13, "doremy_sweet", "Doremy Sweet", 4, BossMechanic.Dream, (90, 90, 192, 0.2f), (58, 58, 138, 0.3f), (58, 42, 40, 0.3f), (216, 64, 72, 0.2f)),
            make(14, "eika_ebisu", "Eika Ebisu", 5, BossMechanic.Stacking, (216, 58, 74, 0.2f), (240, 236, 236, 0.3f), (168, 208, 240, 0.3f), (224, 72, 120, 0.2f)),
            make(15, "eiki_shiki", "Eiki Shiki", 1, BossMechanic.Judgement, (74, 90, 168, 0.2f), (74, 176, 96, 0.3f), (63, 88, 176, 0.3f), (42, 42, 54, 0.2f)),
            make(16, "eirin_yagokoro", "Eirin Yagokoro", 2, BossMechanic.Elixir, (74, 88, 168, 0.2f), (224, 224, 240, 0.3f), (58, 58, 138, 0.3f), (216, 58, 58, 0.2f)),
            make(17, "ellen", "Ellen", 3, BossMechanic.None, (216, 160, 48, 0.2f), (240, 200, 96, 0.3f), (216, 58, 58, 0.3f), (248, 232, 224, 0.2f)),
            make(18, "elly", "Elly", 4, BossMechanic.Reaping, (216, 154, 58, 0.2f), (224, 168, 80, 0.3f), (176, 58, 58, 0.3f), (138, 74, 122, 0.2f)),
            make(19, "enoko_mitsugashira", "Enoko Mitsugashira", 5, BossMechanic.Traps, (216, 64, 58, 0.2f), (184, 184, 216, 0.3f), (106, 88, 168, 0.3f), (216, 64, 47, 0.2f)),
            make(20, "eternity_larva", "Eternity Larva", 1, BossMechanic.None, (74, 88, 192, 0.2f), (74, 152, 216, 0.3f), (74, 138, 58, 0.3f), (240, 200, 56, 0.2f)),
            make(21, "flandre_scarlet", "Flandre Scarlet", 2, BossMechanic.Destruction, (216, 58, 58, 0.2f), (240, 192, 112, 0.3f), (216, 42, 50, 0.3f), (240, 232, 224, 0.2f)),
            make(22, "fujiwara_no_mokou", "Fujiwara no Mokou", 3, BossMechanic.Rebirth, (216, 58, 58, 0.2f), (237, 228, 224, 0.3f), (200, 56, 58, 0.3f), (106, 42, 42, 0.2f)),
            make(23, "gengetsu", "Gengetsu", 4, BossMechanic.Haste, (224, 160, 48, 0.2f), (240, 200, 96, 0.3f), (248, 216, 224, 0.3f), (216, 58, 72, 0.2f)),
            make(24, "hata_no_kokoro", "Hata no Kokoro", 5, BossMechanic.None, (216, 64, 96, 0.2f), (240, 176, 208, 0.3f), (58, 154, 138, 0.3f), (232, 120, 74, 0.2f)),
            make(25, "hatate_himekaidou", "Hatate Himekaidou", 1, BossMechanic.None, (184, 122, 58, 0.2f), (122, 80, 48, 0.3f), (138, 58, 168, 0.3f), (216, 74, 160, 0.2f)),
            make(26, "hecatia_lapislazuli", "Hecatia Lapislazuli", 2, BossMechanic.ThreeWorlds, (216, 58, 58, 0.2f), (216, 56, 74, 0.3f), (42, 42, 48, 0.3f), (240, 216, 96, 0.2f)),
            make(27, "hina_kagiyama", "Hina Kagiyama", 3, BossMechanic.None, (58, 176, 144, 0.2f), (58, 192, 144, 0.3f), (216, 48, 90, 0.3f), (176, 40, 72, 0.2f)),
            make(28, "hisami_yomotsu", "Hisami Yomotsu", 4, BossMechanic.Recording, (154, 58, 184, 0.2f), (90, 58, 160, 0.3f), (58, 42, 90, 0.3f), (63, 138, 74, 0.2f)),
            make(29, "hong_meiling", "Hong Meiling", 5, BossMechanic.Qi, (74, 138, 216, 0.2f), (216, 64, 47, 0.3f), (122, 138, 48, 0.3f), (216, 208, 208, 0.2f)),
            make(30, "ichirin_kumoi", "Ichirin Kumoi", 1, BossMechanic.Cloud, (74, 136, 200, 0.2f), (90, 176, 224, 0.3f), (58, 82, 200, 0.3f), (232, 160, 48, 0.2f)),
            make(31, "iku_nagae", "Iku Nagae", 2, BossMechanic.Thunder, (176, 74, 200, 0.2f), (74, 58, 160, 0.3f), (42, 42, 54, 0.3f), (232, 80, 106, 0.2f)),
            make(32, "joon_yorigami", "Joon Yorigami", 3, BossMechanic.Fortune, (216, 160, 48, 0.2f), (240, 168, 184, 0.3f), (242, 230, 224, 0.3f), (154, 72, 192, 0.2f)),
            make(33, "junko", "Junko", 4, BossMechanic.Purification, (216, 58, 58, 0.2f), (240, 168, 64, 0.3f), (42, 42, 48, 0.3f), (216, 48, 48, 0.2f)),
            make(34, "kagerou_imaizumi", "Kagerou Imaizumi", 5, BossMechanic.Claws, (216, 58, 58, 0.2f), (106, 42, 42, 0.3f), (216, 58, 58, 0.3f), (90, 82, 84, 0.2f)),
            make(35, "kaguya_houraisan", "Kaguya Houraisan", 1, BossMechanic.ImpossibleRequests, (216, 64, 58, 0.2f), (42, 30, 38, 0.3f), (240, 168, 192, 0.3f), (200, 40, 58, 0.2f)),
            make(36, "kana_anaberal", "Kana Anaberal", 2, BossMechanic.None, (216, 168, 64, 0.2f), (240, 208, 96, 0.3f), (74, 74, 216, 0.3f), (244, 238, 240, 0.2f)),
            make(37, "kanako_yasaka", "Kanako Yasaka", 3, BossMechanic.Onbashira, (138, 74, 216, 0.2f), (106, 72, 192, 0.3f), (216, 58, 58, 0.3f), (46, 42, 46, 0.2f)),
            make(38, "kasen_ibaraki", "Kasen Ibaraki", 4, BossMechanic.None, (216, 64, 58, 0.2f), (232, 88, 122, 0.3f), (58, 122, 56, 0.3f), (238, 228, 224, 0.2f)),
            make(39, "keiki_haniyasushin", "Keiki Haniyasushin", 5, BossMechanic.None, (138, 74, 184, 0.2f), (58, 154, 216, 0.3f), (232, 208, 64, 0.3f), (63, 143, 120, 0.2f)),
            make(40, "keine_kamishirasawa", "Keine Kamishirasawa", 1, BossMechanic.None, (216, 64, 58, 0.2f), (184, 200, 232, 0.3f), (90, 98, 192, 0.3f), (240, 240, 248, 0.2f)),
            make(41, "kisume", "Kisume", 2, BossMechanic.None, (74, 176, 128, 0.2f), (47, 168, 120, 0.3f), (244, 242, 240, 0.3f), (154, 106, 58, 0.2f)),
            make(42, "koakuma", "Koakuma", 3, BossMechanic.Bats, (216, 64, 58, 0.2f), (232, 88, 122, 0.3f), (42, 42, 48, 0.3f), (192, 120, 56, 0.2f)),
            make(43, "kogasa_tatara", "Kogasa Tatara", 4, BossMechanic.None, (216, 58, 72, 0.2f), (184, 200, 224, 0.3f), (200, 232, 224, 0.3f), (90, 42, 120, 0.2f)),
            make(44, "koishi_komeiji", "Koishi Komeiji", 5, BossMechanic.Unconscious, (58, 176, 112, 0.2f), (138, 200, 128, 0.3f), (232, 176, 48, 0.3f), (46, 154, 90, 0.2f)),
            make(45, "komachi_onozuka", "Komachi Onozuka", 1, BossMechanic.Distance, (216, 64, 58, 0.2f), (232, 48, 74, 0.3f), (58, 90, 200, 0.3f), (244, 232, 224, 0.2f)),
            make(46, "kotohime", "Kotohime", 2, BossMechanic.None, (216, 64, 58, 0.2f), (184, 48, 64, 0.3f), (168, 58, 184, 0.3f), (216, 112, 184, 0.2f)),
            make(47, "kurumi", "Kurumi", 3, BossMechanic.None, (216, 160, 48, 0.2f), (240, 216, 144, 0.3f), (42, 42, 52, 0.3f), (244, 240, 242, 0.2f)),
            make(48, "kutaka_niwatari", "Kutaka Niwatari", 4, BossMechanic.None, (216, 64, 58, 0.2f), (216, 58, 42, 0.3f), (232, 120, 56, 0.3f), (248, 232, 192, 0.2f)),
            make(49, "kyouko_kasodani", "Kyouko Kasodani", 5, BossMechanic.Echo, (58, 160, 168, 0.2f), (90, 160, 152, 0.3f), (232, 168, 160, 0.3f), (138, 90, 58, 0.2f)),
            make(50, "letty_whiterock", "Letty Whiterock", 1, BossMechanic.Winter, (90, 88, 200, 0.2f), (184, 160, 216, 0.3f), (90, 74, 192, 0.3f), (242, 240, 248, 0.2f)),
            make(51, "lily_white", "Lily White", 2, BossMechanic.Spring, (90, 154, 224, 0.2f), (240, 200, 160, 0.3f), (248, 240, 244, 0.3f), (216, 58, 72, 0.2f)),
            make(52, "louise", "Louise", 3, BossMechanic.None, (216, 168, 64, 0.2f), (240, 216, 160, 0.3f), (248, 220, 228, 0.3f), (138, 72, 184, 0.2f)),
            make(53, "luna_child", "Luna Child", 4, BossMechanic.Silence, (216, 64, 58, 0.2f), (240, 192, 112, 0.3f), (248, 224, 208, 0.3f), (138, 90, 58, 0.2f)),
            make(54, "lunasa_prismriver", "Lunasa Prismriver", 5, BossMechanic.None, (138, 122, 90, 0.2f), (232, 208, 96, 0.3f), (42, 42, 68, 0.3f), (90, 120, 200, 0.2f)),
            make(55, "lyrica_prismriver", "Lyrica Prismriver", 1, BossMechanic.None, (138, 90, 58, 0.2f), (200, 176, 144, 0.3f), (216, 40, 58, 0.3f), (46, 42, 48, 0.2f)),
            make(56, "mai", "Mai", 2, BossMechanic.None, (122, 88, 200, 0.2f), (184, 176, 232, 0.3f), (248, 208, 224, 0.3f), (240, 160, 200, 0.2f)),
            make(57, "mai_teireida", "Mai Teireida", 3, BossMechanic.None, (90, 122, 88, 0.2f), (138, 122, 106, 0.3f), (47, 138, 74, 0.3f), (74, 78, 88, 0.2f)),
            make(58, "mamizou_futatsuiwa", "Mamizou Futatsuiwa", 4, BossMechanic.Illusions, (138, 90, 58, 0.2f), (122, 42, 72, 0.3f), (160, 122, 72, 0.3f), (58, 138, 56, 0.2f)),
            make(59, "marisa_kirisame", "Marisa Kirisame", 0, BossMechanic.MasterSpark, (232, 184, 48, 0.2f), (242, 208, 64, 0.3f), (42, 42, 48, 0.3f), (248, 244, 240, 0.2f)),
            make(60, "matenshi", "Matenshi", 1, BossMechanic.None, (90, 58, 58, 0.2f), (74, 42, 56, 0.3f), (138, 132, 144, 0.3f), (224, 200, 192, 0.2f)),
            make(61, "mayumi_joutouguu", "Mayumi Joutouguu", 2, BossMechanic.None, (232, 176, 48, 0.2f), (240, 192, 64, 0.3f), (232, 128, 31, 0.3f), (216, 208, 192, 0.2f)),
            make(62, "medicine_melancholy", "Medicine Melancholy", 3, BossMechanic.Poison, (90, 106, 216, 0.2f), (240, 200, 96, 0.3f), (216, 40, 58, 0.3f), (63, 42, 42, 0.2f)),
            make(63, "megumu_iizunamaru", "Megumu Iizunamaru", 4, BossMechanic.None, (216, 64, 58, 0.2f), (30, 42, 88, 0.3f), (42, 90, 144, 0.3f), (232, 184, 48, 0.2f)),
            make(64, "meira", "Meira", 5, BossMechanic.None, (160, 74, 200, 0.2f), (138, 88, 208, 0.3f), (200, 48, 56, 0.3f), (58, 54, 64, 0.2f)),
            make(65, "merlin_prismriver", "Merlin Prismriver", 1, BossMechanic.None, (74, 106, 200, 0.2f), (192, 208, 240, 0.3f), (244, 216, 224, 0.3f), (240, 200, 48, 0.2f)),
            make(66, "mike_goutokuji", "Mike Goutokuji", 2, BossMechanic.None, (216, 128, 58, 0.2f), (248, 232, 232, 0.3f), (232, 106, 56, 0.3f), (248, 216, 208, 0.2f)),
            make(67, "mima", "Mima", 3, BossMechanic.HalfMoon, (74, 154, 88, 0.2f), (58, 168, 72, 0.3f), (42, 58, 168, 0.3f), (240, 216, 64, 0.2f)),
            make(68, "minamitsu_murasa", "Minamitsu Murasa", 4, BossMechanic.Anchors, (58, 168, 144, 0.2f), (58, 48, 56, 0.3f), (244, 240, 240, 0.3f), (168, 168, 180, 0.2f)),
            make(69, "minoriko_aki", "Minoriko Aki", 5, BossMechanic.None, (216, 64, 58, 0.2f), (240, 168, 56, 0.3f), (232, 90, 40, 0.3f), (106, 58, 40, 0.2f)),
            make(70, "misumaru_tamatsukuri", "Misumaru Tamatsukuri", 1, BossMechanic.None, (90, 74, 58, 0.2f), (138, 106, 72, 0.3f), (232, 152, 42, 0.3f), (216, 58, 58, 0.2f)),
            make(71, "momiji_inubashiri", "Momiji Inubashiri", 2, BossMechanic.None, (216, 160, 48, 0.2f), (240, 240, 240, 0.3f), (42, 42, 48, 0.3f), (216, 58, 58, 0.2f)),
            make(72, "momoyo_himemushi", "Momoyo Himemushi", 3, BossMechanic.None, (106, 154, 88, 0.2f), (184, 216, 224, 0.3f), (47, 90, 58, 0.3f), (240, 208, 56, 0.2f)),
            make(73, "mononobe_no_futo", "Mononobe no Futo", 4, BossMechanic.Drift, (138, 74, 200, 0.2f), (200, 192, 224, 0.3f), (244, 232, 236, 0.3f), (122, 72, 192, 0.2f)),
            make(74, "mugetsu", "Mugetsu", 5, BossMechanic.Crescent, (216, 160, 48, 0.2f), (240, 208, 64, 0.3f), (63, 90, 200, 0.3f), (244, 240, 240, 0.2f)),
            make(75, "mystia_lorelei", "Mystia Lorelei", 1, BossMechanic.Blindness, (216, 64, 58, 0.2f), (240, 120, 144, 0.3f), (240, 160, 176, 0.3f), (192, 106, 160, 0.2f)),
            make(76, "narumi_yatadera", "Narumi Yatadera", 2, BossMechanic.None, (176, 74, 74, 0.2f), (46, 32, 40, 0.3f), (154, 154, 160, 0.3f), (216, 56, 58, 0.2f)),
            make(77, "nazrin", "Nazrin", 3, BossMechanic.Dowsing, (216, 64, 58, 0.2f), (154, 152, 176, 0.3f), (58, 54, 64, 0.3f), (184, 180, 212, 0.2f)),
            make(78, "nemuno_sakata", "Nemuno Sakata", 4, BossMechanic.Blades, (216, 58, 58, 0.2f), (200, 208, 224, 0.3f), (232, 138, 40, 0.3f), (216, 72, 58, 0.2f)),
            make(79, "nitori_kawashiro", "Nitori Kawashiro", 5, BossMechanic.None, (74, 106, 200, 0.2f), (58, 152, 216, 0.3f), (42, 122, 168, 0.3f), (58, 154, 72, 0.2f)),
            make(80, "noroiko", "Noroiko", 1, BossMechanic.None, (58, 192, 112, 0.2f), (216, 42, 42, 0.3f), (122, 42, 152, 0.3f), (237, 232, 240, 0.2f)),
            make(81, "nue_houjuu", "Nue Houjuu", 2, BossMechanic.Unknown, (216, 58, 58, 0.2f), (42, 38, 48, 0.3f), (216, 64, 63, 0.3f), (63, 111, 216, 0.2f)),
            make(82, "okina_matara", "Okina Matara", 3, BossMechanic.Doors, (216, 160, 48, 0.2f), (232, 192, 64, 0.3f), (232, 154, 42, 0.3f), (42, 42, 48, 0.2f)),
            make(83, "orange", "Orange", 4, BossMechanic.None, (58, 154, 88, 0.2f), (216, 58, 47, 0.3f), (74, 154, 72, 0.3f), (240, 216, 88, 0.2f)),
            make(84, "parsee_mizuhashi", "Parsee Mizuhashi", 5, BossMechanic.None, (58, 176, 88, 0.2f), (240, 208, 96, 0.3f), (232, 168, 64, 0.3f), (74, 58, 152, 0.2f)),
            make(85, "patchouli_knowledge", "Patchouli Knowledge", 1, BossMechanic.SevenElements, (160, 88, 200, 0.2f), (154, 88, 208, 0.3f), (224, 192, 240, 0.3f), (240, 208, 64, 0.2f)),
            make(86, "raiko_horikawa", "Raiko Horikawa", 2, BossMechanic.Taiko, (200, 58, 72, 0.2f), (216, 48, 58, 0.3f), (58, 42, 56, 0.3f), (200, 64, 63, 0.2f)),
            make(87, "ran_yakumo", "Ran Yakumo", 3, BossMechanic.Foxes, (216, 160, 48, 0.2f), (240, 192, 96, 0.3f), (244, 236, 244, 0.3f), (58, 74, 160, 0.2f)),
            make(88, "reimu_hakurei", "Reimu Hakurei", 4, BossMechanic.FantasySeal, (138, 58, 42, 0.2f), (58, 42, 42, 0.3f), (216, 40, 46, 0.3f), (244, 240, 240, 0.2f)),
            make(89, "reisen_udongein_inaba", "Reisen Udongein Inaba", 5, BossMechanic.Lunatic, (216, 40, 58, 0.2f), (106, 72, 200, 0.3f), (216, 80, 94, 0.3f), (248, 232, 232, 0.2f)),
            make(90, "remilia_scarlet", "Remilia Scarlet", 1, BossMechanic.FateSpear, (216, 40, 58, 0.2f), (138, 138, 216, 0.3f), (248, 216, 224, 0.3f), (216, 40, 64, 0.2f)),
            make(91, "rika", "Rika", 2, BossMechanic.None, (192, 74, 72, 0.2f), (106, 58, 48, 0.3f), (176, 48, 64, 0.3f), (122, 58, 152, 0.2f)),
            make(92, "rikako_asakura", "Rikako Asakura", 3, BossMechanic.None, (138, 88, 200, 0.2f), (90, 42, 152, 0.3f), (246, 228, 236, 0.3f), (58, 58, 68, 0.2f)),
            make(93, "rin_kaenbyou", "Rin Kaenbyou", 4, BossMechanic.None, (216, 64, 58, 0.2f), (216, 47, 47, 0.3f), (58, 90, 72, 0.3f), (74, 138, 216, 0.2f)),
            make(94, "ringo", "Ringo", 5, BossMechanic.Dango, (216, 160, 48, 0.2f), (232, 176, 80, 0.3f), (232, 152, 48, 0.3f), (232, 208, 96, 0.2f)),
            make(95, "rumia", "Rumia", 1, BossMechanic.Darkness, (216, 64, 58, 0.2f), (240, 208, 128, 0.3f), (42, 38, 48, 0.3f), (216, 58, 58, 0.2f)),
            make(96, "sagume_kishin", "Sagume Kishin", 2, BossMechanic.Rewind, (216, 40, 58, 0.2f), (216, 216, 220, 0.3f), (122, 42, 106, 0.3f), (224, 208, 176, 0.2f)),
            make(97, "saki_kurokoma", "Saki Kurokoma", 3, BossMechanic.Stampede, (216, 64, 58, 0.2f), (42, 36, 40, 0.3f), (216, 90, 42, 0.3f), (138, 180, 224, 0.2f)),
            make(98, "sakuya_izayoi", "Sakuya Izayoi", 4, BossMechanic.TimeStop, (90, 106, 160, 0.2f), (208, 208, 220, 0.3f), (58, 74, 168, 0.3f), (248, 240, 240, 0.2f)),
            make(99, "sanae_kochiya", "Sanae Kochiya", 5, BossMechanic.Miracle, (74, 160, 136, 0.2f), (58, 192, 80, 0.3f), (244, 240, 248, 0.3f), (63, 74, 192, 0.2f)),
            make(100, "sannyo_komakusa", "Sannyo Komakusa", 1, BossMechanic.Smoke, (216, 40, 58, 0.2f), (184, 160, 216, 0.3f), (200, 64, 74, 0.3f), (90, 58, 120, 0.2f)),
            make(101, "sara", "Sara", 2, BossMechanic.None, (216, 64, 58, 0.2f), (240, 160, 192, 0.3f), (216, 48, 90, 0.3f), (244, 240, 248, 0.2f)),
            make(102, "satono_nishida", "Satono Nishida", 3, BossMechanic.None, (138, 88, 200, 0.2f), (138, 90, 72, 0.3f), (168, 56, 184, 0.3f), (46, 42, 54, 0.2f)),
            make(103, "satori_komeiji", "Satori Komeiji", 4, BossMechanic.Recollection, (216, 74, 138, 0.2f), (240, 160, 200, 0.3f), (248, 208, 224, 0.3f), (216, 56, 63, 0.2f)),
            make(104, "seiga_kaku", "Seiga Kaku", 5, BossMechanic.Necromancy, (74, 106, 200, 0.2f), (58, 104, 216, 0.3f), (160, 220, 212, 0.3f), (246, 232, 240, 0.2f)),
            make(105, "seija_kijin", "Seija Kijin", 1, BossMechanic.ScreenMirror, (216, 40, 58, 0.2f), (74, 58, 58, 0.3f), (216, 74, 74, 0.3f), (244, 236, 236, 0.2f)),
            make(106, "seiran", "Seiran", 2, BossMechanic.Volley, (216, 64, 58, 0.2f), (58, 88, 216, 0.3f), (138, 192, 232, 0.3f), (232, 208, 176, 0.2f)),
            make(107, "sekibanki", "Sekibanki", 3, BossMechanic.Heads, (216, 40, 58, 0.2f), (216, 42, 42, 0.3f), (216, 58, 58, 0.3f), (90, 58, 184, 0.2f)),
            make(108, "shinki", "Shinki", 4, BossMechanic.None, (122, 154, 216, 0.2f), (240, 240, 250, 0.3f), (200, 40, 58, 0.3f), (244, 224, 232, 0.2f)),
            make(109, "shinmyoumaru_sukuna", "Shinmyoumaru Sukuna", 5, BossMechanic.Inchling, (122, 88, 200, 0.2f), (90, 58, 152, 0.3f), (216, 74, 122, 0.3f), (46, 42, 48, 0.2f)),
            make(110, "shion_yorigami", "Shion Yorigami", 1, BossMechanic.Misfortune, (58, 138, 216, 0.2f), (42, 184, 216, 0.3f), (240, 216, 216, 0.3f), (58, 58, 138, 0.2f)),
            make(111, "shizuha_aki", "Shizuha Aki", 2, BossMechanic.Autumn, (232, 160, 48, 0.2f), (240, 168, 56, 0.3f), (216, 58, 40, 0.3f), (240, 138, 56, 0.2f)),
            make(112, "shou_toramaru", "Shou Toramaru", 3, BossMechanic.None, (216, 160, 48, 0.2f), (224, 168, 48, 0.3f), (232, 136, 42, 0.3f), (244, 238, 240, 0.2f)),
            make(113, "soga_no_tojiko", "Soga no Tojiko", 4, BossMechanic.Storm, (122, 176, 138, 0.2f), (200, 216, 160, 0.3f), (47, 138, 88, 0.3f), (42, 42, 48, 0.2f)),
            make(114, "star_sapphire", "Star Sapphire", 5, BossMechanic.None, (192, 138, 72, 0.2f), (138, 90, 64, 0.3f), (106, 104, 200, 0.3f), (240, 232, 240, 0.2f)),
            make(115, "suika_ibuki", "Suika Ibuki", 1, BossMechanic.Density, (216, 160, 48, 0.2f), (232, 160, 72, 0.3f), (122, 72, 168, 0.3f), (216, 40, 58, 0.2f)),
            make(116, "sumireko_usami", "Sumireko Usami", 2, BossMechanic.Psychokinesis, (106, 74, 56, 0.2f), (74, 46, 46, 0.3f), (46, 38, 48, 0.3f), (122, 58, 138, 0.2f)),
            make(117, "sunny_milk", "Sunny Milk", 3, BossMechanic.Prism, (74, 138, 216, 0.2f), (240, 168, 56, 0.3f), (200, 88, 58, 0.3f), (244, 238, 232, 0.2f)),
            make(118, "suwako_moriya", "Suwako Moriya", 4, BossMechanic.Frog, (216, 160, 48, 0.2f), (232, 192, 96, 0.3f), (106, 58, 168, 0.3f), (216, 180, 104, 0.2f)),
            make(119, "takane_yamashiro", "Takane Yamashiro", 5, BossMechanic.None, (106, 138, 88, 0.2f), (200, 216, 144, 0.3f), (47, 138, 58, 0.3f), (63, 96, 112, 0.2f)),
            make(120, "tenshi_hinanawi", "Tenshi Hinanawi", 1, BossMechanic.Earthquake, (216, 40, 58, 0.2f), (42, 74, 216, 0.3f), (58, 90, 200, 0.3f), (244, 240, 244, 0.2f)),
            make(121, "tewi_inaba", "Tewi Inaba", 2, BossMechanic.None, (216, 40, 58, 0.2f), (42, 36, 40, 0.3f), (248, 208, 216, 0.3f), (232, 128, 58, 0.2f)),
            make(122, "toyosatomimi_no_miko", "Toyosatomimi no Miko", 3, BossMechanic.Rays, (216, 160, 64, 0.2f), (232, 192, 128, 0.3f), (138, 58, 168, 0.3f), (216, 40, 58, 0.2f)),
            make(123, "tsukasa_kudamaki", "Tsukasa Kudamaki", 4, BossMechanic.None, (216, 160, 48, 0.2f), (240, 192, 112, 0.3f), (244, 240, 236, 0.3f), (216, 160, 96, 0.2f)),
            make(124, "urumi_ushizaki", "Urumi Ushizaki", 5, BossMechanic.Momentum, (216, 64, 58, 0.2f), (90, 86, 96, 0.3f), (244, 240, 240, 0.3f), (232, 192, 48, 0.2f)),
            make(125, "utsuho_reiuji", "Utsuho Reiuji", 1, BossMechanic.Nuclear, (216, 40, 58, 0.2f), (42, 26, 30, 0.3f), (58, 160, 88, 0.3f), (244, 240, 240, 0.2f)),
            make(126, "wakasagihime", "Wakasagihime", 2, BossMechanic.Tide, (90, 88, 200, 0.2f), (42, 90, 152, 0.3f), (58, 154, 88, 0.3f), (244, 216, 224, 0.2f)),
            make(127, "watatsuki_no_toyohime", "Watatsuki no Toyohime", 3, BossMechanic.FreeFall, (216, 160, 48, 0.2f), (240, 208, 144, 0.3f), (122, 72, 192, 0.3f), (240, 168, 192, 0.2f)),
            make(128, "watatsuki_no_yorihime", "Watatsuki no Yorihime", 4, BossMechanic.Return, (216, 40, 58, 0.2f), (192, 160, 216, 0.3f), (232, 120, 122, 0.3f), (240, 216, 88, 0.2f)),
            make(129, "wriggle_nightbug", "Wriggle Nightbug", 5, BossMechanic.Swarm, (58, 192, 128, 0.2f), (47, 192, 112, 0.3f), (58, 58, 152, 0.3f), (216, 40, 58, 0.2f)),
            make(130, "yachie_kicchou", "Yachie Kicchou", 1, BossMechanic.None, (216, 64, 58, 0.2f), (240, 208, 96, 0.3f), (106, 176, 200, 0.3f), (63, 143, 96, 0.2f)),
            make(131, "yamame_kurodani", "Yamame Kurodani", 2, BossMechanic.None, (216, 160, 48, 0.2f), (232, 176, 48, 0.3f), (200, 88, 42, 0.3f), (74, 58, 52, 0.2f)),
            make(132, "yatsuhashi_tsukumo", "Yatsuhashi Tsukumo", 3, BossMechanic.None, (106, 74, 58, 0.2f), (138, 90, 72, 0.3f), (90, 42, 58, 0.3f), (180, 138, 208, 0.2f)),
            make(133, "yoshika_miyako", "Yoshika Miyako", 4, BossMechanic.None, (58, 58, 152, 0.2f), (58, 74, 160, 0.3f), (216, 64, 58, 0.3f), (42, 42, 68, 0.2f)),
            make(134, "youmu_konpaku", "Youmu Konpaku", 5, BossMechanic.Slash, (58, 168, 160, 0.2f), (232, 236, 236, 0.3f), (47, 154, 104, 0.3f), (60, 64, 76, 0.2f)),
            make(135, "yukari_yakumo", "Yukari Yakumo", 1, BossMechanic.Gaps, (216, 160, 48, 0.2f), (240, 208, 96, 0.3f), (90, 58, 168, 0.3f), (240, 236, 244, 0.2f)),
            make(136, "yuki", "Yuki", 2, BossMechanic.None, (216, 160, 48, 0.2f), (240, 208, 112, 0.3f), (58, 48, 54, 0.3f), (244, 240, 240, 0.2f)),
            make(137, "yumeko", "Yumeko", 3, BossMechanic.None, (216, 160, 48, 0.2f), (240, 184, 48, 0.3f), (216, 40, 58, 0.3f), (244, 240, 240, 0.2f)),
            make(138, "yumemi_okazaki", "Yumemi Okazaki", 4, BossMechanic.Possibility, (216, 40, 58, 0.2f), (232, 48, 74, 0.3f), (216, 40, 58, 0.3f), (248, 240, 240, 0.2f)),
            make(139, "yuugi_hoshiguma", "Yuugi Hoshiguma", 5, BossMechanic.Shockwave, (216, 160, 48, 0.2f), (240, 192, 96, 0.3f), (244, 240, 240, 0.3f), (200, 74, 104, 0.2f)),
            make(140, "yuuka_kazami", "Yuuka Kazami", 1, BossMechanic.Flowers, (216, 40, 58, 0.2f), (58, 192, 96, 0.3f), (216, 40, 58, 0.3f), (244, 216, 224, 0.2f)),
            make(141, "yuuma_toutetsu", "Yuuma Toutetsu", 2, BossMechanic.None, (216, 40, 58, 0.2f), (216, 220, 228, 0.3f), (74, 104, 168, 0.3f), (58, 62, 72, 0.2f)),
            make(142, "yuyuko_saigyouji", "Yuyuko Saigyouji", 3, BossMechanic.ButterflyFans, (216, 112, 168, 0.2f), (240, 160, 192, 0.3f), (106, 138, 216, 0.3f), (63, 63, 154, 0.2f)),
            make(143, "zanmu_nippaku", "Zanmu Nippaku", 4, BossMechanic.Death, (216, 40, 58, 0.2f), (74, 42, 106, 0.3f), (90, 160, 88, 0.3f), (74, 106, 168, 0.2f)),
        };

        /// <summary>The boss of a map whose colours cannot be read at all.</summary>
        public static BossInfo Default
        {
            get
            {
                foreach (var boss in All)
                {
                    if (boss.Id == "marisa_kirisame")
                        return boss;
                }

                return All[0];
            }
        }
    }
}