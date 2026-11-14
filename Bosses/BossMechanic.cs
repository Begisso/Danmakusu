namespace osu.Game.Rulesets.Danmakusu.Bosses
{
    /// <summary>
    /// The special mechanic of a boss (a stub: so far only the name and the idea; <see cref="UI.BulletField"/> has the place where each one is to be built).
    /// </summary>
    public enum BossMechanic
    {
        /// <summary>No mechanic defined yet: only the placeholder attack.</summary>
        None,

        /// <summary>Mystia: every attack halves all bullet speeds and turns a flashlight on (a small circle of sight round the player); the pauses restore everything.</summary>
        Blindness,

        /// <summary>
        /// Clownpiece: a big moon and a triple laser circle the centre of the screen on opposite sides (the moon appears towards the player); bullets
        /// vanish in the moon, touching it hurts like a slider, all bullet sizes and counts are doubled, and her attack never pauses.
        /// </summary>
        Moon,

        /// <summary>Murasa: from the middle she throws big anchors on chains of bubbles to the walls (eight round her every sixteen beats, one at the player every four); eight beats later they sink and the bubbles pop.</summary>
        Anchors,

        /// <summary>Fans of butterfly bullets and slow death spirals.</summary>
        ButterflyFans,

        /// <summary>
        /// Rumia: no pauses. Every eight beats (as often as Sannyo's smoke) she throws a ring of 10 invisible circles, each five circle radii big, at the speed of Raiko's ripples (a sound at every throw). They do nothing to the player; every bullet and every piece of beam inside one cannot be seen
        /// (half see-through over the edge, with a smooth change), the way Raiko's ripples change what they go over (what is hidden still hurts). Her own shots are rings of 10 plain bullets, one every beat, that turn 30 degrees a second.
        /// </summary>
        Darkness,

        /// <summary>Bullets gather and scatter, a pulsing density of the field.</summary>
        Density,

        /// <summary>
        /// Flandre: no pauses; she throws knives (in a spinner too) and three clones turn round her, two and a half circle radii away (the other way every 18 beats). A red eye is put down on the player's place and explodes eight beats later (as the next one is put down), taking every bullet and every
        /// slice of beam within three circle radii of it (it hurts the player too). What it took is shot again, once from each clone (two beats after the clone before): twice as fast, half as big, outwards from the eye (along the line from it through where the thing was), glowing like Remilia's last bullets, and it cannot be taken again.
        /// Every fourth thing it took also sends four bullets out of its place (right, down, left, up).
        /// </summary>
        Destruction,

        /// <summary>Bullets travel further depending on how far the player is.</summary>
        Distance,

        /// <summary>Doll satellites orbit the boss and fire in turn.</summary>
        Dolls,

        /// <summary>
        /// Kyouko: during an attack two transparent glass walls close in (from the top and the bottom in the first attack, from the left and the right in the next, and so on) until each is a quarter of the
        /// playfield from its edge, within the first half of the attack, and then stay until the pause starts, when they are gone. They do nothing to the player; every bullet (the falling ones and the arrowheads too, not the laser beams) bounces off them twice, then passes through. She shoots paper talismans in rings of two, four times as often as Yukari's pattern (in a spinner the stars of the spinner become talismans); her rings turn the other way every time new walls appear.
        /// </summary>
        Echo,

        /// <summary>Okina: portals in the wall behind her and behind the player (bending round corners); in an attack she shoots talismans backwards, half as often, in the pauses forwards.</summary>
        Doors,

        /// <summary>Nazrin: no pauses; she shoots arrowheads that fly straight, and every sixteen beats she turns the bullets inside the circle round her (marked in the map's combo colours) into arrowheads that are pulled towards the player (about half of them, mixed, into ellipses that fall towards them).</summary>
        Dowsing,

        /// <summary>Tenshi: ricochet stars; every eighth bullet that leaves through the bottom comes back as three; a shockwave every eight beats makes every fourth bullet that leaves where it has been come back.</summary>
        Earthquake,

        /// <summary>Alternate arrow volleys and medicine orbs.</summary>
        Elixir,

        /// <summary>
        /// Reimu: she stands in the middle. While she attacks, no bullet can leave the screen: each stops where it touches the border and lies there, and the ones that come later lie against them without
        /// overlapping. In the pauses bullets fly as usual and what lies there stays; every few beats she throws a yin-yang orb at the player (hurts like a slider, flies like Hecatia's worlds: ricochets off the border and off other orbs until the pause ends). When the pause ends
        /// the pile is gone and so are the orbs. In a break and in a spinner she does both all the time, and shoots nothing but talismans (and the orb).
        /// </summary>
        FantasySeal,

        /// <summary>A huge aimed spear of bullets after a short telegraph.</summary>
        FateSpear,

        /// <summary>Yuuka: every fourth bullet that goes out of the bottom of the screen grows into a sunflower there, which after eight beats flies up and sheds a ring of petals.</summary>
        Flowers,

        /// <summary>Ran: talismans that slow down as they fly; eight beams along golden spirals that grow to the edge of the screen, then go from where they started, and bullets bounce off them meanwhile.</summary>
        Foxes,

        /// <summary>Bullets freeze mid-air, then shatter outwards.</summary>
        Freeze,

        /// <summary>Bouncing frog bullets and iron rings.</summary>
        Frog,

        /// <summary>Gaps open in the screen: bullets teleport from one gap to another. Every ring fired in her attacks is two, the second turned ten degrees. A train comes at the player when an attack begins and in every pause.</summary>
        Gaps,

        /// <summary>Decoy copies of the boss that fire fake bullets (harmless).</summary>
        Illusions,

        /// <summary>From the top of the screen, without a pause, under a giant moon that comes slowly down: what leaves the top falls back as stars, three degrees to either side of straight down, that leave beams behind that narrow to a point; slider beams that leave the top fall back in short purple pieces, and every ring turns the other way from the one before. She comes back after the last section until the song ends, with more stars, longer beams and bullets that change shape.</summary>
        ImpossibleRequests,

        /// <summary>Eiki: dense rings of talismans; when an attack begins a circle is placed round the player, who cannot leave it; bullets and beams that enter it turn to its middle at half their speed, and have their speed back when it is gone.</summary>
        Judgement,

        /// <summary>Reisen: shoots very short straight beams from a fixed spot; in an attack everything on the screen freezes (see-through, in a dim purple light) and then slides three circles to the left or the right (harmless meanwhile).</summary>
        Lunatic,

        /// <summary>The boss fires master sparks (this is what the laser bursts are).</summary>
        MasterSpark,

        /// <summary>Sanae Kochiya: in her attacks every ring has twice as many bullets, half as big, and the round ones are five-pointed stars; the background is dimmed green with a giant star. She shoots rings of 30 small ricochet stars in a star outline that turns (every second ring the other way).</summary>
        Miracle,

        /// <summary>Slow giant orbs that explode into dense rings.</summary>
        Nuclear,

        /// <summary>
        /// Toyohime: she shoots ellipses instead of stars. During her attacks every bullet that leaves the top of the screen stops there and falls freely, and every other bullet that comes within 5 circle radii of the player is pulled towards the player
        /// (it turns until it points at the player, then flies straight on). In the pauses neither happens, and every bullet that is fired during a pause ricochets once more than it would (once if it did not at all).
        /// </summary>
        FreeFall,

        /// <summary>Pillars of bullets fall from above.</summary>
        Onbashira,

        /// <summary>Poison clouds linger on the field and slowly expand.</summary>
        Poison,

        /// <summary>Cards, bottles and signs thrown around by telekinesis.</summary>
        Psychokinesis,

        /// <summary>Junko: every slider of her section is a sliderwhistle, but it shoots its laser from Junko (along the slider's path, as if the slider lay where she is), not from where it is; her own shots are slices of beam. Every slice of a beam is see-through and does not hurt for its first four beats, then it is as usual.</summary>
        Purification,

        /// <summary>
        /// Hong Meiling: no pauses. In pulls of eight beats she draws in every bullet within five circle radii of her (other bosses' too; a ring in her colours shows the reach, as Nazrin's). What is drawn in is fed into one big ball on her: its area is the sum of their areas, its speed
        /// and colour the average of theirs. When the pull is over it is thrown at the player (a plain round bullet from then on; the Mokou impact sound plays) and the next pull begins at once. The ball glows, has bloom, dims the bullets round it and is as sharp as Mokou's big circle.
        /// She shoots fans of 10 plain bullets, spread over 90 degrees, at the player.
        /// </summary>
        Qi,

        /// <summary>
        /// Koakuma: every twentieth bullet that flies while she attacks turns into a bat (a flapping bat, black with a white outline that fades inwards); what she shoots herself (her stars, in a spinner the spinner's stars) is bats from the start.
        /// A bat is locked onto the player and turns towards them twice as fast as an arrowhead; once it has been within two circle radii of the player for eight beats in all it fades out. A bat dims the bullets within one circle radius of it.
        /// </summary>
        Bats,

        /// <summary>
        /// Sunny Milk: during her attacks a big prism with a rainbow halo turns in the middle of the screen (half as fast as a spinner's beams, the other way every sixteen beats). A bullet that crosses it turns parallel to the side
        /// of the prism that is nearest its way, and when it leaves, its way is mirrored (across the line that stands on that side) and it splits into seven bullets, spread over 120 degrees, in different colours (they depend on the colour
        /// of the bullet that went in) and a third of the size. The prism hurts like a slider. She shoots triangles of plain bullets.
        /// </summary>
        Prism,

        /// <summary>
        /// Seiga Kaku: every second bullet that leaves the playfield through a wall comes back: it turns round and flies the way it came, four times as slowly, and fades out after sixteen beats. It is twice as dark and has a talisman
        /// on it (only to look at), which fits inside its body and always faces the middle of the playfield. She shoots talismans in ellipses (also in a spinner). She has no beams of her own.
        /// </summary>
        Necromancy,

        /// <summary>From the middle, without a pause: the busiest cones of the screen light up, their bullets are pulled in, the cones turn into still beams and what was pulled in comes out as five rings; big bullets fly at her from outside the screen.</summary>
        Rays,

        /// <summary>
        /// Mokou: no pauses. She stands opposite the player until her first attack, then jumps at the player in a straight line (no homing) until she reaches the edge of the screen, where she stays for
        /// 8 beats with a huge circle bullet growing on her (it glows and darkens the bullets round it). Every jump has the default warning. Each landing sends twenty rings of circle bullets out of the
        /// edge of the ball in a spiral, shakes the screen and plays a sound. She shoots nothing else; touching her while she jumps hurts. In a break and in a spinner she instead throws a ball at the player
        /// every 16 beats (after the default warning, in which it grows): half the size in flight, and on hitting the wall it grows to the huge ball's size and acts the same.
        /// </summary>
        Rebirth,

        /// <summary>
        /// Satori Komeiji: every two beats she looks at which way the player moved (from where they were two beats ago to where they are now; nothing is shown). Bullets that are inside a cone of 90 degrees round that
        /// way, up to five circle radii from the player, turn slowly (eight times slower than before) to fly perpendicular to the line the player traced, towards it. She shoots what Koishi shoots (hearts).
        /// </summary>
        Recollection,

        /// <summary>
        /// Seija: during her attacks the whole playfield (cursor included, not the HUD) is mirrored along both axes - it turns smoothly through 180 degrees and back in the pauses.
        /// Her picture is not mirrored with it (but mirrored along both axes while she warns). She shoots plain round bullets instead of stars, and no laser beams. Everything that exists when the reversal starts (bullets and beams) bounces once off the border of the screen during it and turns red; a short sound plays when the screen turns over and when it returns.
        /// </summary>
        ScreenMirror,

        /// <summary>Byakuren: five homing beams from round her in every attack, and five from round every spot where every fifth circle or slider of her section started, 32 beats later (she holds 5 spots at a time); they live like Koakuma's bats (spinning beams in a spinner).</summary>
        Scrolls,

        /// <summary>
        /// Patchouli Knowledge: no pauses; every eight beats the element changes: fire, water, wood, metal, earth. Fire: ripples (like Raiko's, smoother, twice as fast) turn what they touch orange-red with a red ring; eight beats later it grows to the
        /// ring while it shakes, turns yellow, glows and fades out. Water: wide blue ripples catch what they touch: every second bullet gets a see-through, harmless clone that rides the ripple outwards (spread evenly over its part on the screen) and takes the bullet's place four beats later, the others become eight times as slow and blue, and recover in sixteen beats. Wood: the edges of the playfield turn brown; every tenth bullet that leaves
        /// grows into a little glowing tree (wood that hurts) with ten leaves, which are thrown into the centre at half speed when it is grown. Metal: a ripple like Zanmu's puts a knife on top of what it touches (which flies on); the knife freezes, pointing at the player, for eight beats. Earth: every twentieth bullet on the screen turns into a glowing brown stone as big as an osu! circle that bursts into five bullets of the size it had, with an orange flash, where it hits the wall.
        /// She shoots squares of plain bullets (also in a spinner).
        /// </summary>
        SevenElements,

        /// <summary>Fast bouncing bullets.</summary>
        Shikigami,

        /// <summary>Slashes through the watched bullets nearest to the middle of the screen, see-through first, that poke holes in slider beams (the slices fall like petals); rings of straight beams flying out of her.</summary>
        Slash,

        /// <summary>A song of notes: rhythm-timed bullet waves.</summary>
        Song,

        /// <summary>Raiko: five taiko drums circle her and are beaten in turn; every beat sends out a ripple that makes bullets swell.</summary>
        Taiko,

        /// <summary>Ringo: every bullet leaves two copies behind it, every sixth is thrown out again, and big bullets turn the others round.</summary>
        Dango,

        /// <summary>
        /// Lily White: branches grow from the sides and corners of the screen (each as big as a sub-branch of Yuyuko's tree, wood that hurts). In her attacks every bullet that goes out of the screen at a side grows a flower
        /// on the branches, evenly spread (as often and as fast as Yuyuko's); eight beats after it started to grow, each flower bursts into petal bullets. In her pauses no new flowers grow, but the ones there still burst on time.
        /// </summary>
        Spring,

        /// <summary>
        /// Urumi Ushizaki: no pauses. Every eight beats a push is announced: for four beats everything on the screen glows, turns light grey and shakes a little, and every eighth bullet swells to one and a half times its size; then all of it gets an extra
        /// straight speed in the direction of the line from her to the player (as it is when the announcement begins), which adds up with every push. What is shot while the push is announced is caught by it too.
        /// Every circle that is hit and every slider that is over gets a grey combo colour, glows a little and falls on the player, as Zanmu's bullets fall (a slider keeps its shape).
        /// </summary>
        Momentum,

        /// <summary>
        /// Shinmyoumaru Sukuna: in her attacks every bullet and every beam (and its hitbox) is sized by its distance from her: nothing at the edge of her magic circle, up to three times its size as far from that edge as the playfield is wide, and the
        /// further the more it glows. During the warning before an attack the sizes and the glow change to those numbers and everything shakes (1-2 px); they change back after the attack. Kyouko's wall sound plays at the start of every attack.
        /// </summary>
        Inchling,

        /// <summary>
        /// Luna Child: in her attacks every hitsound is muted, and every bullet and beam is drawn blurry (soft glowing edges), more the further from her; inside her magic circle all is normal (the hitboxes stay as they are). She shoots nothing else than rings of arc beams (in a spinner too): every two beats a ring of 20 arcs with 20 gaps between them
        /// flies out from her as a circle, turned a little further every time; they hurt like a slider body.
        /// </summary>
        Silence,

        /// <summary>
        /// Ichirin Kumoi and Unzan: the cloud's head stands behind her, looking at the player. In her attacks every 8 beats a hand (left and right in turn) is drawn slowly back (a warning), then thrown at the player along an outer arc, speeding up a little, to the wall behind the player; Mokou's landing rings (no big circle) come out
        /// of it, the field shakes 1-2 px for 4 beats and the Moon's hit sound of Hecatia plays; 8 beats later the hand returns in a straight line. In a pause both returned hands clap in front of her: 4 rings of ellipses whose outline is an arrowhead bullet (a sharp tip towards the player, arc sides), and the same shake and sound. Hands at home swing a little, slowly. In the warning the head grows from nothing, the hands fade in and the first hand is already drawn back; it all goes the other way when her phase is over. She shoots nothing else (the spinner's stars are gone).
        /// </summary>
        Cloud,

        /// <summary>Daiyousei: in her attacks everything turns green and glows, hurts like a slider body, comes in again from the other side when it leaves the screen (once), and cannot be destroyed: others only get a copy.</summary>
        Verdant,

        /// <summary>Wakasagihime: small stars, twice as often; in her attacks the screen is ocean blue, bullets swim from side to side, some are bubbles that pop into rings, and seaweed grows from the bottom and floats upwards.</summary>
        Tide,

        /// <summary>Tojiko: lightning from the top of a dark screen, see-through and harmless at first, and at the player, throwing small bullets back from where it ends; every bullet that leaves through the top falls back as a small bright ellipse.</summary>
        Storm,

        /// <summary>Yumemi: flowers of beams; in her attacks every tenth bullet has a beam for a tail; in her pauses everything is pulled away from her and the screen shakes.</summary>
        Possibility,

        /// <summary>Eika: ellipses in rings; every four beats everything gets as much speed again sideways, the way depending on the distance from her; in her attacks bullets that touch stick together, as the law of impulse says.</summary>
        Stacking,

        /// <summary>Futo: everything loses speed the nearer it gets to the wall; when a pause starts a boat comes at the player out of the wall nearest her, from behind her; what it touches slides out and is turned once to fly straight out of it.</summary>
        Drift,

        /// <summary>Shion: talismans in hexagons that come back once from the border; every eight beats everything turns dark blue and flies back the way it came, with red arrows as a warning.</summary>
        Misfortune,

        /// <summary>Chimata: walls of talismans from two sides every four beats, rings of talismans round the player; the talismans of the walls that come near the player turn into knives that creep at them.</summary>
        Market,

        /// <summary>Hisami: rings of four; every four beats of an attack what is round the player leaves previews that fly off two beats later, and slider beams are fired again four beats later.</summary>
        Recording,

        /// <summary>Chiyari: bats that fly straight; in her attacks three rings of pools of blood turn round her, and a bullet that touches one turns red and is aimed at the player.</summary>
        Bloodlust,

        /// <summary>Yuugi: big basic bullets; every eight beats a stamp sends out a ring that throws the bullets it touches outwards, the faster and the brighter the closer to her.</summary>
        Shockwave,

        /// <summary>Joon: golden bullets that fall; every four beats part of the bullets turn gold and fall, bouncing twice like balls, losing half their energy every time.</summary>
        Fortune,

        /// <summary>Shizuha: she shoots autumn leaves (maple leaves); every fourth bullet turns orange in an attack, falls as a leaf in the pause, lies on the bottom of the screen and falls upwards in the next attack.</summary>
        Autumn,

        /// <summary>Saki: rings of beams; attacks like Remilia's, with a brown dim and a giant horse's head; every bullet turns the way from her to the player and speeds up to 1.2 times its speed over an attack, and back over the pause.</summary>
        Stampede,

        /// <summary>Wriggle: every sixteenth bullet on the screen is a firefly, twice as big, bright green, glowing, that drifts slowly and wanders from side to side.</summary>
        Swarm,

        /// <summary>Three planets fire different patterns at once.</summary>
        ThreeWorlds,

        /// <summary>Lightning lines with a long telegraph.</summary>
        Thunder,

        /// <summary>
        /// Sakuya: during her attacks several spirals of knives (pointing at her) appear round her, a good way off, and stay with her while every bullet and beam on the screen stands still.
        /// When the attack ends everything on the screen is mirrored along both axes and turns into knives; the held knives are mirrored too and fly out at the player without turning.
        /// She shoots knives instead of stars (in a spinner too).
        /// </summary>
        TimeStop,

        /// <summary>Bullets fire from random positions around the player.</summary>
        Unconscious,

        /// <summary>Bullets whose shape and colour are unknown until they are close.</summary>
        Unknown,

        /// <summary>
        /// Letty: no pauses (her attacks follow each other); she shoots arrowheads (which fly straight) instead of stars. ALL the sliders of her section are sliderwhistles, so every slider shoots its laser beams (from where it is, as a whistled slider does). Every slice of a beam lives 4 beats; then it is gone at once (the beam is eaten away from its oldest end at half the speed it grew) and
        /// dissipates into bullets (one per beam, round, half size and half speed, every eighth of a beat along the beam, so the whole beam is gone two slider-lengths after the first slice), which fly against the way the slider's path goes there.
        /// </summary>
        Winter,

        /// <summary>
        /// Sannyo Komakusa: in her attacks she throws a ring of 8 smoke clouds every 8 beats (grey, see-through, slowly swelling and swaying, half as fast as Raiko's ripples, never fading: they go on until they have left the playfield). Bullets inside smoke are half transparent, twice as big
        /// and hurt like a slider body (they are not used up, they cost the slider damage every interval). The smoke itself does not hurt. A sound plays at every throw (the one of Kyouko's walls).
        /// </summary>
        Smoke,

        /// <summary>
        /// Seiran: during an attack every ring has a quarter of its bullets, and every bullet is shot eight times in a row (the way Ringo's bullets have copies behind them): the first shot is the bullet alone,
        /// the second has one more bullet to its left and one to its right (turned a third as far as Yukari's side bullets are), and the third to the eighth have two on each side. Her bullets are as big as a plain bullet.
        /// </summary>
        Volley,

        /// <summary>
        /// Zanmu Nippaku: no pauses; she shoots basic bullets (also in a spinner) and has no big beams. Every sixteen beats she sends out one smooth, see-through grey ripple (twice as fast as Raiko's, it does not hurt; a power-up warning and an impact sound at every one): every bullet and every beam that it goes over stops
        /// where it is (grey, still hurting), and eight beats after the ripple started (at the latest when her section ends) everything it has stopped falls like it does when the player dies, in the direction of the line from her to the player (as it is when the fall begins; it does not change while it falls), getting darker as it falls (no warning). While her section lasts a giant grey see-through
        /// skull shows over a dimmed grey background and grows slightly, and the graveyard sound plays an octave down.
        /// </summary>
        Death,

        /// <summary>
        /// Watatsuki no Yorihime: she shoots paper talismans in rings shaped like a pentagon (also in a spinner). With other bosses on the map she does what they do herself: in a section she shares, what the boss beside her does, the whole section long and four beats behind; in a section of her own the last one first, each after a gap of
        /// four beats, with half their bullets and beams. When there is nobody to repeat, during an attack every second bullet that has passed all the way through the border of the screen is shot again from her, half as big and half as fast, towards the place where it left. Every slider's beam that reaches the border is copied from her too, towards that place, half as wide. She has no big beams of her own.
        /// </summary>
        Return,

        /// <summary>
        /// Doremy: in her attacks every circle and slider has a dark blue copy mirrored through the middle of the playfield, which shoots what the original shoots, mirrored, three quarters
        /// as big and twice as fast (the spinner's stars too). No beams; plain bullets, in two chains of three-bullet rings that turn opposite ways.
        /// </summary>
        Dream,

        /// <summary>Sekibanki: every eight beats of an attack she throws a homing head that leaves a beam behind it; the slices of the beam stay for 32 beats, then fly off.</summary>
        Heads,

        /// <summary>
        /// Sagume: as soon as an attack ends it is played again backwards - every circle and slider of it again where it was, white, the last one first (a slider the other way along its
        /// path), shooting what the original shot the same way, one and a half times as big and half as fast.
        /// </summary>
        Rewind,

        /// <summary>
        /// Gengetsu: no pauses; she shoots basic bullets in double rings. Every ring is shot twice, right after each other, every bullet on the screen speeds up evenly from its own speed to twice it over each of her long attacks,
        /// every fourth bullet is 1.2 times as big, and every fourth beam is twice as thick.
        /// </summary>
        Haste,

        /// <summary>
        /// Enoko Mitsugashira: bear traps, put down like Flandre's eyes (three every sixteen beats, two beats apart, on the player's place, with an approach circle), 2.25 circle radii wide. A closed approach circle arms
        /// the trap, which stays; stepping into it shuts it: it hurts, fades out, and holds the character for four beats. At most 24 on the map (the oldest fades out).
        /// </summary>
        Traps,

        /// <summary>
        /// Nemuno Sakata: she throws Remilia's knives with blades twice as long, in rings. In her attacks the bullets of every fifth circle or slider come out where the player is (not where it is) and fall the way
        /// from it to the player, eight times more gently than ellipses. Every tenth slider beam of her section stops when its slider ends, turns to point at the player, two copies of it come out on each side, and
        /// all five fall towards the player like Zanmu's.
        /// </summary>
        Blades,

        /// <summary>Mugetsu: she shoots arrowheads that fly straight, in rings. In her attacks every bullet goes round her as fast as it flies, half of them one way and half the other, and every fourth ring is three times as dense.</summary>
        Crescent,

        /// <summary>Mima: no pauses; stars with beams for tails; every sixteen beats a giant crescent moon on the player, its back to the outside, grows see-through for four beats, then is solid for eight: it hurts like a slider body and everything that flies into it bounces off it once; when it goes its edges fly off as stars with tails.</summary>
        HalfMoon,

        /// <summary>Kagerou Imaizumi: no pauses; every sixteen beats she throws herself at where the player was, shooting dense white rings only as she flies, and stays eight beats, clawing every beat: three arcs that reach anywhere, see-through first, that cut what is in them in two when they light up, one piece flying on and the other back.</summary>
        Claws,

        /// <summary>Elly: no pauses; Kagerou's claw, one big arc half the playfield's height from her, twice as slow; she slides after the player until they are in reach, and swings a scythe all the time whose blade hurts like a slider body.</summary>
        Reaping,

        /// <summary>
        /// Misumaru Tamatsukuri: she throws a yin-yang orb at the player every eight beats (one and a half times as big as Reimu's; it ricochets off the walls and off the other orbs, hurts like a slider body and is gone after
        /// sixteen beats), and every thirtieth bullet of her section turns into one. In her attacks bullets that touch ricochet off each other's bodies, once each.
        /// </summary>
        Orbs,

        /// <summary>
        /// Suika Ibuki: every four beats she stomps: what is within four circle radii of her is thrown on twice as fast, glowing like magma, with a narrow tail of beam two beats long, and bursts into four small ellipses at the
        /// wall; the stomp hurts the player there, and thin glowing cracks in the ground hurt like a slider body for two beats. Every sixteen beats a wide glowing crack, as jagged as the World's Ender's cut, splits the ground
        /// from her towards the player; it hurts like a slider body and eight beats later it explodes into glowing ellipses.
        /// </summary>
        Fissure,

        /// <summary>
        /// Yuki: in her attacks the old laser bursts (five beams from her, the first at the player, turning slowly towards them), but every slice of a beam goes out where the player was when it was fired, and leaves a
        /// burning scorch mark there for eight beats that hurts like a slider body. Every four beats she shoots rings of five ricochet stars from five places round her.
        /// </summary>
        Scorch,

        /// <summary>
        /// Yumeko: fans of five knives (yellow handles) at the player every two beats; every eighth bullet turns into a knife that falls towards where the player was. Everything that leaves the screen in sixteen beats
        /// comes back in the next four as knives, from where it left, straight across from its wall, four times as close together.
        /// </summary>
        Recall,

        /// <summary>
        /// Shou Toramaru: a cross of beams grows from the middle of the screen past its edges and turns, clockwise in her attacks and anticlockwise in her pauses; every fourth bullet fired comes out of the cross instead,
        /// spread evenly along it, flying across it to one side or the other. Every four beats she shoots a ring of sixteen beams that wave from side to side.
        /// </summary>
        Pagoda,

        /// <summary>
        /// Eirin Yagokoro: every eight beats the four nearest bullets of every group are joined into a constellation by a beam (see-through for four beats first), and the bullets of the constellations (only those)
        /// stand still for four beats while the beams hurt; then the beams are gone and they fly on. Constellations do not overlap.
        /// </summary>
        Constellation,

        /// <summary>
        /// Megumu Iizunamaru: no pauses; every half beat five straight endless beams from her, see-through for four beats and solid for four, each set turned five degrees from the last (the other way every 32 beats).
        /// Every eight beats every bullet on the screen turns into a star like Marisa's and ricochets once.
        /// </summary>
        Galaxy,

        /// <summary>
        /// Kana Anaberal: every sixteen beats 32 bullets stop, grow to twice their size, turn white and glow; eight beats later they fly off, each its own way, twice as fast, and ricochet three times. Every eight beats
        /// she shoots a fan of three bullets twice as big as a plain one at the player.
        /// </summary>
        Poltergeist,

        /// <summary>
        /// Eternity Larva: she shoots butterflies that flap their wings and drift and wander as Wriggle's fireflies. Every eight beats every twentieth bullet on the screen turns aquamarine and flies at half its speed with
        /// a red ring round it (as Patchouli's fire) for four beats; then it swells to a big round bullet two circle radii wide, with Mokou's impact sound, and shrinks back over the next four beats.
        /// </summary>
        Metamorphosis,

        /// <summary>
        /// Nitori Kawashiro: in every attack a giant spinning icosahedron of beams (one for every edge) fades in round the middle of the playfield, turning about the line through two corners across the screen, every
        /// next attack the other way; it grows to as wide as the playfield over eight beats and lasts the whole attack, and in the pause it comes apart into plain bullets half the size. She shoots an arc of beam ten
        /// degrees wide at the player every quarter beat.
        /// </summary>
        Icosahedron,

        /// <summary>
        /// Mai: no pauses; one giant snowflake of beams after another, standing where she was: it grows from the middle out over eight beats as Yuyuko's tree does, then goes again from the middle out over the next
        /// eight, coming apart into plain bullets half the size that fly out from its middle.
        /// </summary>
        Snowflake,

        /// <summary>
        /// Benben Tsukumo: every slider's beam in her section has another above and below it (harmless for four beats); she throws three parallel beams at the player in her attacks; every second bullet that crosses a
        /// beam becomes a note that flies at half speed, swelling to twice it and back, and turns towards the player once.
        /// </summary>
        Strings,

        /// <summary>
        /// Ellen: rings of talismans every two beats, every two neighbouring talismans joined by a beam (harmless for four beats); Socrates, her cat, comes out of her and every two beats jumps onto the bullet
        /// within four circle radii nearest the player, rides it, and hurts like a slider body.
        /// </summary>
        Familiar,

        /// <summary>
        /// Yoshika Miyako: as Seiga, but every fourth bullet that leaves the playfield in her section comes back, at its own speed, leaving static copies of the talisman behind it, end to end, each there for eight
        /// beats. She throws fans of five knives with red handles at the player.
        /// </summary>
        Jiangshi,

        /// <summary>
        /// Matenshi: always in the middle; her big wings come out of her and open, then sway, and hurt like a slider body; every second bullet fired comes out of the wings as a glowing feather, flying away from
        /// her. Every four beats two beams three circle radii wide come down from the top, one over the player and one elsewhere, after a preview of four beats, and last four beats.
        /// </summary>
        FallenAngel,

        /// <summary>
        /// Yatsuhashi Tsukumo: no pauses. Her section is an 18-key osu!mania convert of the map (osu!mania's own converter): every note falls down its lane from the top of the screen as a beam four times as thick as a
        /// normal one, in four beats from top to bottom, reaching the bottom on its note (an approach circle closes at the top of its lane first); held notes are long beams. The map's own objects stay.
        /// </summary>
        Koto,

        /// <summary>
        /// Keiki Haniyasushin: in her section the rings come half as often with twice as many bullets, and every ring is a square; every fifth bullet that goes out of the screen comes back the way it came as a
        /// beam four beats long. She shoots very dense squares of plain bullets every four beats.
        /// </summary>
        Haniwa,

        /// <summary>
        /// Tsukasa Kudamaki: every fourth beam of her section bounces once off the border of the screen. She shoots rings of six big balls (two circle radii, drawn sharp); every tenth bullet fired in her section
        /// comes out of the edge of the balls instead, the six in turn, and flies on as it was fired.
        /// </summary>
        PipeFox,
    }
}