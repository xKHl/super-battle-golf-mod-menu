using System.Collections.Generic;
using UnityEngine;

namespace SbgMod
{
    /// <summary>
    /// Airborne ball prediction using the same per-FixedUpdate steps as the game:
    /// GolfBall.ApplyGravity, then Hittable.ApplyAirDamping (quadratic drag relative to wind).
    /// Ground rolling is NOT simulated (it depends on terrain layers and contact normals).
    /// </summary>
    public static class Trajectory
    {
        public struct Env
        {
            public Vector3 gravity, wind;
            public bool applyWind;
            public float windFactor, crossWindFactor, drag, rbDamping, dt, radius;
            public int groundMask;
            public int foliageMask;   // tree canopies: trigger volumes that apply heavy drag (Entity foliage drag)
            public bool startsInFoliage;
        }

        public sealed class Path
        {
            public readonly List<Vector3> points = new List<Vector3>(128);
            public Vector3 end;
            public bool hitGround;
            public bool reachedCup;
            public bool blockedByFoliage;
            public float minCupDistance = float.MaxValue;
        }

        public struct Shot
        {
            public bool valid;       // a launch was produced at all
            public bool reachesCup;  // predicted flight passes through the cup
            public Vector3 direction;
            public float speed, power, pitch, predictedMiss;
            public Path path;
            public string note;
        }

        public static Env Capture(GolfBall ball)
        {
            Hittable h = ball.AsEntity.AsHittable;
            WindHittableSettings w = h.settings != null ? h.settings.Wind : null;
            Vector3 wind = Game.Try(() => WindManager.Wind);
            Env e = new Env
            {
                // GolfBall.Awake sets useGravity = false and ApplyGravity adds Physics.gravity once per step.
                gravity = Physics.gravity * (ball.Rigidbody != null && ball.Rigidbody.useGravity ? 2f : 1f),
                wind = wind,
                // GolfBall.OnWasHitByGolfSwing sets canBeAffectedByWind = true, so a struck ball always sees the wind.
                applyWind = w != null && wind.sqrMagnitude > 1e-6f,
                windFactor = w != null ? w.WindFactor : 0f,
                crossWindFactor = w != null ? w.CrossWindFactor : 0f,
                drag = GameManager.GolfBallSettings.LinearAirDragFactor,
                rbDamping = ball.Rigidbody != null ? ball.Rigidbody.linearDamping : 0f,
                dt = Time.fixedDeltaTime,
                radius = Game.BallRadius(ball),
                groundMask = Game.BallCollisionMask(ball)
            };
            int foliageLayer = LayerMask.NameToLayer("Foliage");
            e.foliageMask = foliageLayer >= 0 ? 1 << foliageLayer : 0;
            e.groundMask &= ~e.foliageMask;
            e.startsInFoliage = e.foliageMask != 0 && Physics.CheckSphere(Game.BallPos(ball), e.radius, e.foliageMask, QueryTriggerInteraction.Collide);
            return e;
        }

        public static void Step(ref Vector3 p, ref Vector3 v, in Env e)
        {
            v += e.gravity * e.dt;
            Vector3 windTerm = Vector3.zero;
            if (e.applyWind)
            {
                Vector3 along = Vector3.Project(e.wind, v);
                windTerm = along * e.windFactor + (e.wind - along) * e.crossWindFactor;
            }
            Vector3 rel = v - windTerm;
            v -= rel * Mathf.Max(0f, e.drag * rel.sqrMagnitude * e.dt);
            if (e.rbDamping > 0f)
            {
                v *= Mathf.Max(0f, 1f - e.rbDamping * e.dt);
            }
            p += v * e.dt;
        }

        public static Path Simulate(Vector3 start, Vector3 v0, in Env e, Vector3? cup, float cupRadius, bool collide, int maxSteps = 1500, int stride = 2)
        {
            Path path = new Path();
            Vector3 p = start, v = v0;
            path.points.Add(p);
            for (int i = 0; i < maxSteps; i++)
            {
                Vector3 prev = p;
                Step(ref p, ref v, e);
                if (cup.HasValue)
                {
                    float d = DistancePointSegment(cup.Value, prev, p);
                    if (d < path.minCupDistance)
                    {
                        path.minCupDistance = d;
                    }
                    if (d <= cupRadius && v.y <= 0f)
                    {
                        path.reachedCup = true;
                        path.end = cup.Value;
                        path.points.Add(cup.Value);
                        return path;
                    }
                }
                if (collide && i > 1)
                {
                    Vector3 seg = p - prev;
                    float len = seg.magnitude;
                    if (len > 1e-5f && Physics.SphereCast(prev, e.radius * 0.9f, seg / len, out RaycastHit hit, len, e.groundMask, QueryTriggerInteraction.Ignore))
                    {
                        path.hitGround = true;
                        path.end = hit.point;
                        path.points.Add(hit.point);
                        return path;
                    }
                    // Flying through a canopy is not a collision, but the drag there stops the ball: treat it as blocked.
                    if (len > 1e-5f && e.foliageMask != 0 && Physics.SphereCast(prev, e.radius, seg / len, out RaycastHit leaf, len, e.foliageMask, QueryTriggerInteraction.Collide))
                    {
                        path.hitGround = true;
                        path.blockedByFoliage = true;
                        path.end = leaf.point;
                        path.points.Add(leaf.point);
                        return path;
                    }
                }
                if (i % stride == 0)
                {
                    path.points.Add(p);
                }
                if (p.y < start.y - 400f)
                {
                    break;
                }
            }
            path.end = p;
            path.points.Add(p);
            return path;
        }

        /// <summary>Point where the flight descends through targetY, ignoring collisions.</summary>
        private static Vector3 CrossingAtHeight(Vector3 start, Vector3 v0, in Env e, float targetY)
        {
            Vector3 p = start, v = v0;
            bool wasAbove = start.y > targetY;
            for (int i = 0; i < 1500; i++)
            {
                Vector3 prev = p;
                Step(ref p, ref v, e);
                if (p.y > targetY)
                {
                    wasAbove = true;
                }
                else if (v.y < 0f)
                {
                    if (!wasAbove)
                    {
                        return prev; // apex stays below the target height: report the apex so range still grows with speed
                    }
                    float t = Mathf.Approximately(prev.y, p.y) ? 1f : Mathf.Clamp01((prev.y - targetY) / (prev.y - p.y));
                    return Vector3.Lerp(prev, p, t);
                }
            }
            return p;
        }

        private static Vector3 Launch(Vector3 flatDir, float pitchDeg)
        {
            Vector3 right = Vector3.Cross(Vector3.up, flatDir).normalized;
            return Quaternion.AngleAxis(-pitchDeg, right) * flatDir;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>
        /// Finds launch direction + speed so the flight descends through the cup.
        /// speedScale / yawOffsetDeg are corrections learned from earlier observed flights.
        /// </summary>
        public static Shot Solve(Vector3 start, Vector3 baseVelocity, Vector3 cup, float cupRadius, in Env e, float speedPerPower, float maxPower, float maxPitch, float speedScale, float yawOffsetDeg)
        {
            Shot best = default;
            best.predictedMiss = float.MaxValue;
            float maxSpeed = speedPerPower * maxPower;
            float farthest = 0f;
            float farthestPitch = Mathf.Min(40f, maxPitch);

            for (float pitch = maxPitch; pitch >= 14.9f; pitch -= 5f)
            {
                Vector3 aim = cup;
                float speed = 0f;
                Vector3 dir = Vector3.forward;
                bool reachable = true;
                for (int iter = 0; iter < 4 && reachable; iter++)
                {
                    Vector3 flat = Flat(aim - start);
                    float want = flat.magnitude;
                    if (want < 0.05f)
                    {
                        flat = Flat(cup - start).sqrMagnitude > 1e-6f ? Flat(cup - start) : Vector3.forward;
                    }
                    flat.Normalize();
                    dir = Launch(flat, pitch);

                    float maxRange = Vector3.Dot(Flat(CrossingAtHeight(start, baseVelocity + dir * maxSpeed, e, cup.y) - start), flat);
                    if (maxRange > farthest)
                    {
                        farthest = maxRange;
                        farthestPitch = pitch;
                    }
                    if (maxRange < want)
                    {
                        reachable = false;
                        break;
                    }
                    float lo = 0f, hi = maxSpeed;
                    for (int k = 0; k < 22; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        float range = Vector3.Dot(Flat(CrossingAtHeight(start, baseVelocity + dir * mid, e, cup.y) - start), flat);
                        if (range < want)
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                    }
                    speed = 0.5f * (lo + hi);
                    // Cross-wind drift: shift the aim point by the lateral miss and re-solve.
                    Vector3 cross = CrossingAtHeight(start, baseVelocity + dir * speed, e, cup.y);
                    Vector3 miss = Flat(cross - cup);
                    aim -= miss;
                }
                if (!reachable)
                {
                    continue;
                }

                Path path = Simulate(start, baseVelocity + dir * speed, e, cup, cupRadius, true);
                float missDist = path.reachedCup ? 0f : path.minCupDistance;
                if (missDist < best.predictedMiss)
                {
                    // Apply learned corrections to what is actually commanded.
                    Vector3 cmdDir = Mathf.Abs(yawOffsetDeg) > 1e-4f ? Quaternion.AngleAxis(yawOffsetDeg, Vector3.up) * dir : dir;
                    best.valid = true;
                    best.reachesCup = path.reachedCup;
                    best.direction = cmdDir;
                    best.speed = Mathf.Min(speed * speedScale, maxSpeed);
                    best.power = best.speed / speedPerPower;
                    best.pitch = pitch;
                    best.predictedMiss = missDist;
                    best.path = path;
                    best.note = path.reachedCup ? "direct" : path.blockedByFoliage ? "path goes through tree foliage" : "blocked or near miss";
                }
                if (path.reachedCup)
                {
                    break;
                }
            }

            if (!best.valid)
            {
                // Out of range: advance as far as possible toward the cup.
                Vector3 flat = Flat(cup - start);
                if (flat.sqrMagnitude < 1e-6f)
                {
                    flat = Vector3.forward;
                }
                flat.Normalize();
                Vector3 dir = Launch(flat, farthestPitch);
                best.valid = true;
                best.reachesCup = false;
                best.direction = dir;
                best.speed = maxSpeed;
                best.power = maxPower;
                best.pitch = farthestPitch;
                best.path = Simulate(start, baseVelocity + dir * maxSpeed, e, cup, cupRadius, true);
                best.predictedMiss = best.path.minCupDistance;
                best.note = "out of range (max carry ~" + farthest.ToString("0") + " m), advancing";
            }
            if (e.startsInFoliage)
            {
                // Foliage drag is not modelled, so nothing launched from inside a canopy can be trusted.
                best.reachesCup = false;
                best.note = "ball is inside tree foliage, the shot will be slowed (prediction unreliable)";
                best.predictedMiss = Mathf.Max(best.predictedMiss, 1f);
            }
            return best;
        }

        public static float DistancePointSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-10f)
            {
                return Vector3.Distance(point, a);
            }
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / len2);
            return Vector3.Distance(point, a + ab * t);
        }
    }
}
