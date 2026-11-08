using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Danmakusu.Bullets;
using osuTK;

namespace osu.Game.Rulesets.Danmakusu.UI
{
    public partial class BulletField
    {
        // ======================================================================================================================================================
        // Chimata Tenkyuu: every eight beats two walls of talismans twice the size of her rings' come in, at half a plain bullet's speed, from the top and the bottom at the same time, and the next time from the left and the right, side by side with their pictures touching. A talisman of a wall that comes within four circle radii of
        // the player (only those: not her rings, not anyone else's bullets) turns into a small knife with a blue handle, stops its flight and creeps at the player at a quarter of a plain bullet's speed, the way the player was at that moment: so the walls part
        // round the player and the knives pile up. She herself shoots a ring of talismans five circle radii round the player, aimed at where they are, at half the speed of a plain bullet, every four beats (every two in a spinner).
        // ======================================================================================================================================================

        private sealed class MarketAct : StageAct
        {
            private readonly double[] walls;
            private readonly double[] rings;
            private readonly List<int> watched = new List<int>(); // (the talismans of the act that are still flying: they turn into knives too)
            private int nextWall, nextRing;

            public MarketAct(BossPeriod period, double[] walls, double[] rings)
            {
                Period = period;
                this.walls = walls;
                this.rings = rings;
                Start = period.ShootStart;
                End = period.End;
            }

            public override void Reset(BulletField f, double time)
            {
                ClearBullets();
                watched.Clear();
                nextWall = nextRing = 0;
            }

            private void addTalisman(BulletField f, double t, Vector2 at, Vector2 velocity, float radius, bool turnsIntoKnife)
            {
                int index = Bullets.Count;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = at,
                    V = velocity,
                    Radius = radius,
                    Hit = radius * DanmakusuBalance.BULLET_HITBOX_SCALE,
                    Style = f.comboStyle(0, true, Period.Rank),
                    Shape = BulletShape.Talisman,
                    Until = t + f.stageLife(velocity),
                });

                if (turnsIntoKnife && Bullets.Count > index)
                    watched.Add(index);
            }

            /// <summary>A wall: talismans side by side, along the edge they come from, their pictures touching (a talisman flies along its long side, so the wall is as many of its short sides as it takes).</summary>
            private void addWall(BulletField f, double t, Vector2 from, Vector2 to, Vector2 velocity, float radius)
            {
                float length = Vector2.Distance(from, to);
                float width = 2f * radius * DanmakusuBalance.TALISMAN_LENGTH_FACTOR * DanmakusuBalance.TALISMAN_ASPECT;
                int count = Math.Max((int)MathF.Ceiling(length / (width * DanmakusuBalance.CHIMATA_WALL_SPACING)), 1);

                for (int i = 0; i <= count; i++)
                    addTalisman(f, t, Vector2.Lerp(from, to, i / (float)count), velocity, radius, true);
            }

            private void throwWalls(BulletField f, double t, bool sideways)
            {
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.CHIMATA_WALL_TALISMAN_SIZE;
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.CHIMATA_WALL_SPEED;
                Vector2 low = f.stageMin - new Vector2(radius), high = f.stageMax + new Vector2(radius);

                if (sideways)
                {
                    addWall(f, t, low, new Vector2(low.X, high.Y), new Vector2(speed, 0f), radius);
                    addWall(f, t, new Vector2(high.X, low.Y), high, new Vector2(-speed, 0f), radius);
                }
                else
                {
                    addWall(f, t, low, new Vector2(high.X, low.Y), new Vector2(0f, speed), radius);
                    addWall(f, t, new Vector2(low.X, high.Y), high, new Vector2(0f, -speed), radius);
                }
            }

            private void throwRing(BulletField f, double t)
            {
                float radius = f.stageBaseRadius(Period) * DanmakusuBalance.CHIMATA_TALISMAN_SIZE;
                float speed = stageBulletSpeed(Period) * DanmakusuBalance.CHIMATA_RING_SPEED;
                float distance = f.circleRadius * DanmakusuBalance.CHIMATA_RING_CIRCLES;
                Vector2 player = f.characterPosition;

                for (int k = 0; k < DanmakusuBalance.CHIMATA_RING_BULLETS; k++)
                {
                    float angle = k * MathF.Tau / DanmakusuBalance.CHIMATA_RING_BULLETS;
                    Vector2 outward = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

                    addTalisman(f, t, player + outward * distance, -outward * speed, radius, false); // (these stay talismans)
                }
            }

            private float reach(BulletField f) => f.circleRadius * DanmakusuBalance.CHIMATA_NEAR_CIRCLES;

            /// <summary>What has come near the player is a knife now, that creeps at them (as big as before the wall's talismans were made bigger: the knife's size goes by her rings' talismans).</summary>
            private void addKnife(BulletField f, double t, Vector2 at, float speed, float radius, float hit, int style)
            {
                Vector2 aim = f.characterPosition - at;
                Vector2 way = aim.LengthSquared > 1e-4f ? Vector2.Normalize(aim) : Vector2.UnitY;
                Vector2 velocity = way * (Math.Max(speed, 0.02f) * DanmakusuBalance.CHIMATA_KNIFE_SPEED);
                float smaller = DanmakusuBalance.CHIMATA_TALISMAN_SIZE / DanmakusuBalance.CHIMATA_WALL_TALISMAN_SIZE;

                radius *= smaller;
                hit *= smaller;

                AddBullet(new StageBullet
                {
                    T0 = t,
                    P0 = at,
                    V = velocity,
                    Face = way,
                    Radius = radius * DanmakusuBalance.CHIMATA_KNIFE_SIZE,
                    Hit = hit * DanmakusuBalance.CHIMATA_KNIFE_SIZE,
                    Style = style,
                    Shape = BulletShape.Knife,
                    BlueHandle = true,
                    Until = t + f.stageLife(velocity),
                });
            }

            public override void Update(BulletField f, double now)
            {
                if (f.failed)
                    return;

                while (nextWall < walls.Length && walls[nextWall] <= now)
                    throwWalls(f, walls[nextWall], nextWall++ % 2 == 1);

                while (nextRing < rings.Length && rings[nextRing] <= now)
                    throwRing(f, rings[nextRing++]);

                float near = reach(f);

                for (int i = watched.Count - 1; i >= 0; i--)
                {
                    int index = watched[i];
                    var talisman = Bullets[index];

                    if (now > talisman.Until || f.stageConsumed.Contains(Id * 65536 + index))
                    {
                        watched.RemoveAt(i);
                        continue;
                    }

                    if (!f.stageBulletAt(talisman, now, out Vector2 position) || (position - f.characterPosition).LengthSquared > near * near)
                        continue;

                    talisman.Until = now;
                    Bullets[index] = talisman;
                    watched.RemoveAt(i);
                    // (the knives creep as fast as they did before the walls were slowed down: a quarter of a plain bullet's speed)
                    addKnife(f, now, position, talisman.V.Length / DanmakusuBalance.CHIMATA_WALL_SPEED, talisman.Radius, talisman.Hit, talisman.Style);
                }
            }

        }
    }
}
