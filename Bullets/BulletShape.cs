namespace osu.Game.Rulesets.Danmakusu.Bullets
{
    /// <summary>How a bullet is drawn. Collision always uses a circle (see <see cref="BulletEmitter.HitRadius"/>).</summary>
    public enum BulletShape
    {
        /// <summary>Plain bullets, finish bullets.</summary>
        Circle,

        /// <summary>Hitclap bullets: an ellipse whose long axis points along the direction of travel.</summary>
        Ellipse,

        /// <summary>Hitwhistle bullets: the classic Touhou arrowhead, a sharp tip pointing along the direction of travel.</summary>
        Triangle,

        /// <summary>Hitfinish bullets: a sharp five-pointed star, one tip pointing along the direction of travel.</summary>
        Star,

        /// <summary>The stars of spinners and bosses: the same star, but plump and rounded, with a soft glowing gradient.</summary>
        SoftStar,

        /// <summary>A throwing knife (a picture): it points along the direction of travel. The hitbox is a circle.</summary>
        Knife,

        /// <summary>A heart (Koishi's bullets): the tip points along the direction of travel.</summary>
        Heart,

        /// <summary>A flower petal (Yuyuko's bullets): the tip points along the direction of travel.</summary>
        Petal,

        /// <summary>A slice of a laser beam (Junko's bullets): a long, thin blade that points along the direction of travel.</summary>
        Slice,

        /// <summary>A train carriage (a picture), long side along the direction of travel. The hitbox is the rectangle of the carriage.</summary>
        Train,

        /// <summary>A sakura flower (Yuyuko: the petals that fall, and the stars of her spinner): five petals round a darker middle, drawn and hit as big as the flowers of her tree (see DanmakusuBalance.YUYUKO_FALL_FLOWER_SCALE).</summary>
        Flower,

        /// <summary>A paper talisman (a rectangle: a white border and writing over a body in the bullet's combo colour; Yukari's frozen bullets). It is as big as a bullet, its long side along the direction of travel. The hitbox is the bullet's circle.</summary>
        Talisman,

        /// <summary>A maple leaf (Shizuha's autumn leaves), the one on the flag of Canada: its top points along the direction of travel.</summary>
        Maple,

        /// <summary>An eighth note (Benben's notes; the outline of the user's picture): its stem points along the way it faces (the notes keep facing up, so they stand upright), the flag to the right.</summary>
        Note,

        /// <summary>A feather (Matenshi's; a picture, the user's): its tip points along the direction of travel. The hitbox is its outline (<see cref="DanmakusuBalance.FEATHER"/>).</summary>
        Feather,
    }

    /// <summary>The shape the bullets of a ring are arranged in (the bullets themselves keep their own <see cref="BulletShape"/>).</summary>
    public enum RingShape
    {
        Circle,
        Square,
        Triangle,
        Star,

        /// <summary>Five corners (Yorihime's rings).</summary>
        Pentagon,

        /// <summary>Six corners (Shion's rings).</summary>
        Hexagon,

        /// <summary>A sakura flower: five petals, each with a notch at its tip (Yuyuko).</summary>
        Flower,

        /// <summary>A flat ellipse, twice as wide as it is high (Seiga Kaku's rings).</summary>
        Ellipse,
    }

    /// <summary>Which hitsound class a ring belongs to; decides how many bullets it has (see <see cref="DanmakusuBalance.BulletsPerRing"/>).</summary>
    public enum BulletKind
    {
        /// <summary>No clap, whistle or finish.</summary>
        Normal,

        /// <summary>Hitclap.</summary>
        Clap,

        /// <summary>Hitwhistle.</summary>
        Whistle,

        /// <summary>Hitfinish (and the last object of a combo): few, big bullets, with the first one aimed at the player.</summary>
        Finish,
    }
}
