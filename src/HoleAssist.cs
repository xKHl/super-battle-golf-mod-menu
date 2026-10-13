using System.Collections;
using UnityEngine;

namespace SbgMod
{
    /// <summary>
    /// Auto Hole: solves a launch with Trajectory.Solve and fires it through the game's own
    /// Hittable.HitWithGolfSwing (the call PlayerGolfer.SwingRoutine makes), then registers the stroke
    /// exactly like SwingRoutine does. Scoring is decided by the server (GolfHoleTrigger.OnTriggerEnter);
    /// the mod only reports success when the game calls PlayerGolfer.InformScored for the local player.
    ///
    /// Cup geometry (measured in-game): the cup is a pit. hole.transform is at ground level in the middle of the
    /// opening and the scoring trigger box starts about 0.6 m below the rim. So the ball has to pass through the
    /// opening without clipping the rim; the solver therefore aims the ball centre at the middle of the opening
    /// at rim height, and the opening radius is measured with raycasts.
    ///
    /// Cup Snap: native CmdFinishHole when the host's "Console Commands" rule allows it, otherwise
    /// BallControl.PlaceBall directly above the cup.
    /// </summary>
    public sealed class HoleAssist
    {
        public struct CupInfo
        {
            public GolfHole hole;
            public Vector3 center;      // middle of the opening at rim height
            public float openRadius;    // measured radius of the opening
            public float okRadius;      // how far off-centre the ball centre may cross the rim plane
            public bool measured;
        }

        /// <summary>Everything the UI shows about the last executed attempt. Separate from the live preview.</summary>
        public sealed class Report
        {
            public int attempts;
            public float startDistance, power, pitch, predictedMiss;
            public bool predictedDrop, hasLanding, hasRest, holed, running;
            public Vector3 predictedLanding, actualLanding, rest;
            public float landingError, restDistance, closest;
            public string stage = "idle";
            public string note = "";
        }

        private enum Phase { Idle, Flight }

        private Phase phase;
        private int attemptsLeft;
        private float shotTime, restTime;
        private Vector3 shotStart, lastBallPos, lastBallVel;
        private CupInfo shotCup;
        private Trajectory.Shot shot;

        // Corrections learned from observed flights on the current hole.
        private float speedScale = 1f;
        private float yawOffsetDeg;
        private GolfHole learnedFor;

        private Trajectory.Shot preview;
        private bool hasPreview;
        private float previewTime = -10f;
        private Vector3 previewKey;

        private static CupInfo cupCache;

        public readonly Report Last = new Report();
        public string Outcome = "idle";
        public Kind OutcomeKind = Kind.Info;

        public bool Busy => phase != Phase.Idle;
        public bool HasPreview => hasPreview && Plugin.Cfg.HolePreview.Value;
        public Trajectory.Shot Preview => preview;
        public float SpeedScale => speedScale;
        public float YawOffset => yawOffsetDeg;

        // ---------------------------------------------------------------- cup geometry

        public static bool TryGetCup(out CupInfo cup)
        {
            GolfHole hole = Game.Hole;
            if (hole == null)
            {
                cup = default;
                return false;
            }
            if (cupCache.hole != hole || (cupCache.center - hole.transform.position).sqrMagnitude > 0.01f)
            {
                cupCache = Measure(hole);
            }
            cup = cupCache;
            return true;
        }

        /// <summary>Shape used by overlays: opening centre, tolerance radius.</summary>
        public static bool TryGetCup(out Vector3 cup, out float radius, out GolfHole hole)
        {
            bool ok = TryGetCup(out CupInfo info);
            cup = info.center;
            radius = info.okRadius;
            hole = info.hole;
            return ok;
        }

        public static int CupMask(GolfHole hole)
        {
            int mask = Game.GroundMask;
            if (hole != null && hole.Collider != null)
            {
                mask |= 1 << hole.Collider.gameObject.layer;
            }
            return mask;
        }

        private static CupInfo Measure(GolfHole hole)
        {
            CupInfo c = new CupInfo { hole = hole, center = hole.transform.position, openRadius = 0.5f };
            int mask = CupMask(hole);
            // Walk outward from the middle in 12 directions; the first ray that finds ground near rim height is the rim.
            float best = float.MaxValue;
            bool centreIsOpen = !Physics.Raycast(c.center + Vector3.up * 0.6f, Vector3.down, 0.85f, mask, QueryTriggerInteraction.Ignore);
            if (centreIsOpen)
            {
                for (int d = 0; d < 12; d++)
                {
                    Vector3 dir = Quaternion.AngleAxis(d * 30f, Vector3.up) * Vector3.forward;
                    for (float r = 0.05f; r <= 1.5f; r += 0.025f)
                    {
                        if (Physics.Raycast(c.center + dir * r + Vector3.up * 0.6f, Vector3.down, 0.85f, mask, QueryTriggerInteraction.Ignore))
                        {
                            best = Mathf.Min(best, r);
                            break;
                        }
                    }
                }
            }
            if (best < float.MaxValue)
            {
                c.openRadius = best;
                c.measured = true;
            }
            else
            {
                GolfHoleTrigger trig = hole.GetComponentInChildren<GolfHoleTrigger>(true);
                if (trig != null && trig.ballTrigger != null && trig.ballTrigger.bounds.size.sqrMagnitude > 1e-6f)
                {
                    c.openRadius = Mathf.Min(trig.ballTrigger.bounds.extents.x, trig.ballTrigger.bounds.extents.z);
                }
            }
            float ballRadius = Game.Ball != null ? Game.BallRadius(Game.Ball) : 0.15f;
            c.okRadius = Mathf.Clamp(c.openRadius - ballRadius * 0.9f, 0.06f, 0.6f);
            return c;
        }

        // ---------------------------------------------------------------- solving

        private bool SolveNow(out Trajectory.Shot result, out CupInfo cup, out string why)
        {
            result = default;
            cup = default;
            if (!Game.BallUsable(out why))
            {
                return false;
            }
            if (!TryGetCup(out cup))
            {
                why = "no active hole in this scene";
                return false;
            }
            if (cup.hole != learnedFor)
            {
                learnedFor = cup.hole;
                speedScale = 1f;
                yawOffsetDeg = 0f;
            }
            GolfBall ball = Game.Ball;
            Hittable h = ball.AsEntity.AsHittable;
            float rule = Game.Try(() => MatchSetupRules.GetValue(MatchSetupRules.Rule.SwingPower), 1f);
            float speedPerPower = h.SwingSettings.MaxPowerSwingHitSpeed * rule;
            if (speedPerPower <= 0.01f)
            {
                why = "swing power rule is zero";
                return false;
            }
            float maxPower = Plugin.Cfg.AllowOvercharge.Value ? 1f + GameManager.GolfSettings.MaxSwingOvercharge : 1f;
            float maxPitch = Mathf.Min(Plugin.Cfg.AutoHoleMaxPitch.Value, GameManager.GolfSettings.MaxSwingPitch);
            Trajectory.Env env = Trajectory.Capture(ball);
            env.groundMask |= CupMask(cup.hole);
            bool adapt = Plugin.Cfg.AdaptShots.Value;
            result = Trajectory.Solve(Game.BallPos(ball), Vector3.zero, cup.center, cup.okRadius, env, speedPerPower, maxPower, maxPitch, adapt ? speedScale : 1f, adapt ? yawOffsetDeg : 0f);
            return result.valid;
        }

        public string DescribeSolution()
        {
            if (!SolveNow(out Trajectory.Shot s, out CupInfo cup, out string why))
            {
                return "none: " + why;
            }
            return "power " + s.power.ToString("0.000") + " speed " + s.speed.ToString("0.00") + " pitch " + s.pitch + " reaches " + s.reachesCup + " miss " + s.predictedMiss.ToString("0.00") + " end " + s.path.end.ToString("F2")
                + " note " + s.note + " | cup centre " + cup.center.ToString("F2") + " openRadius " + cup.openRadius.ToString("0.000") + " ok " + cup.okRadius.ToString("0.000") + " measured " + cup.measured + " | learned scale " + speedScale.ToString("0.000") + " yaw " + yawOffsetDeg.ToString("0.00");
        }

        // ---------------------------------------------------------------- execution

        public void Trigger()
        {
            if (!Plugin.Cfg.AutoHole.Value)
            {
                Plugin.Say("Auto Hole is disabled", Kind.Warn);
                return;
            }
            if (phase != Phase.Idle)
            {
                Plugin.Say("Auto Hole: previous shot still in flight", Kind.Warn);
                return;
            }
            attemptsLeft = Plugin.Cfg.AutoHoleRetries.Value;
            Last.attempts = 0;
            Last.holed = false;
            Fire();
        }

        private void Fire()
        {
            PlayerGolfer g = Game.Golfer;
            string why = null;
            if (g == null)
            {
                why = "no local player";
            }
            else if (g.IsMatchResolved)
            {
                why = "your hole is already resolved (" + g.MatchResolution + ")";
            }
            else if (!Game.SwingsAllowed)
            {
                why = "swings are not allowed yet (match state " + Game.State + ")";
            }
            else if (Game.BallUsable(out why) && !Game.Ball.IsStationary)
            {
                why = "ball is still moving";
            }
            CupInfo cup = default;
            if (why == null)
            {
                SolveNow(out shot, out cup, out why);
            }
            if (why != null)
            {
                phase = Phase.Idle;
                Last.running = false;
                Last.stage = "not fired";
                Set("Auto Hole unavailable: " + why, Kind.Warn);
                return;
            }

            GolfBall ball = Game.Ball;
            Hittable hittable = ball.AsEntity.AsHittable;
            Vector3 localOrigin = ball.transform.InverseTransformPoint(g.transform.position);
            // Native swing hit: applies locally if we simulate the ball, and sends CmdHitWithGolfSwing.
            hittable.HitWithGolfSwing(Vector3.zero, localOrigin, shot.direction, false, shot.power, 0f, SwingType.Regular, g, null);
            // Stroke bookkeeping, as in PlayerGolfer.SwingRoutine.
            HoleProgressBarUi.IncrementStrokes();
            g.OnPlayerHitOwnBall();
            if (!g.isServer)
            {
                g.CmdInformHitOwnBall();
            }

            phase = Phase.Flight;
            shotTime = Time.time;
            restTime = 0f;
            shotStart = Game.BallPos(ball);
            shotCup = cup;
            lastBallPos = shotStart;
            lastBallVel = Vector3.zero;
            preview = shot;
            hasPreview = true;
            previewTime = Time.time + 3f;

            Last.attempts++;
            Last.running = true;
            Last.holed = false;
            Last.startDistance = Flat(cup.center - shotStart).magnitude;
            Last.power = shot.power;
            Last.pitch = shot.pitch;
            Last.predictedDrop = shot.reachesCup;
            Last.predictedMiss = shot.predictedMiss;
            Last.predictedLanding = shot.path.end;
            Last.hasLanding = false;
            Last.hasRest = false;
            Last.closest = float.MaxValue;
            Last.note = shot.note;
            Last.stage = "in flight";
            Set("Auto Hole #" + Last.attempts + " fired: " + (shot.power * 100f).ToString("0.0") + "% @ " + shot.pitch.ToString("0") + " deg from " + Last.startDistance.ToString("0.0") + " m, "
                + (shot.reachesCup ? "predicted to drop in" : "predicted miss " + shot.predictedMiss.ToString("0.0") + " m (" + shot.note + ")"), Kind.Info);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>Called when the game reports the local player's ball entered a cup.</summary>
        public void OnScored()
        {
            if (phase != Phase.Flight)
            {
                return;
            }
            phase = Phase.Idle;
            Last.running = false;
            Last.holed = true;
            Last.stage = "HOLED (confirmed by the game)";
            LogShot("holed");
            Set("Auto Hole: HOLED on attempt " + Last.attempts + " (confirmed by the game)", Kind.Ok);
        }

        public void Update()
        {
            if (phase == Phase.Flight)
            {
                TrackFlight();
            }
            else if (Plugin.Cfg.HolePreview.Value && Plugin.Cfg.AutoHole.Value && Time.time - previewTime > 1f)
            {
                RefreshPreview();
            }
        }

        private void RefreshPreview()
        {
            previewTime = Time.time;
            PlayerGolfer g = Game.Golfer;
            GolfBall ball = Game.Ball;
            if (g == null || ball == null || g.IsMatchResolved || !ball.IsStationary || ball.IsHidden || Game.Hole == null)
            {
                hasPreview = false;
                return;
            }
            Vector3 key = Game.BallPos(ball) + Game.Try(() => WindManager.Wind) * 7f;
            if (hasPreview && (key - previewKey).sqrMagnitude < 0.0004f)
            {
                return;
            }
            previewKey = key;
            hasPreview = SolveNow(out preview, out _, out _);
        }

        private void TrackFlight()
        {
            GolfBall ball = Game.Ball;
            PlayerGolfer g = Game.Golfer;
            if (ball == null || g == null)
            {
                phase = Phase.Idle;
                Last.running = false;
                Last.stage = "lost";
                Set("Auto Hole: ball/player lost", Kind.Fail);
                return;
            }
            Vector3 pos = Game.BallPos(ball);
            Vector3 vel = Time.deltaTime > 1e-5f ? (pos - lastBallPos) / Time.deltaTime : lastBallVel;
            float elapsed = Time.time - shotTime;
            Last.closest = Mathf.Min(Last.closest, Vector3.Distance(pos, shotCup.center));

            // First touchdown: the ball reports grounded, or its vertical speed flips from falling to rising.
            if (!Last.hasLanding && elapsed > 0.12f && !ball.IsHidden)
            {
                bool bounced = lastBallVel.y < -0.5f && vel.y > 0.2f;
                if (ball.IsGrounded || bounced)
                {
                    Last.hasLanding = true;
                    Last.actualLanding = bounced ? lastBallPos : pos;
                    Last.landingError = Flat(Last.actualLanding - Last.predictedLanding).magnitude;
                    Last.stage = "landed, rolling";
                }
            }
            lastBallPos = pos;
            lastBallVel = vel;

            if (ball.IsHidden)
            {
                // Hidden while in flight means the server took the ball (holed or out of bounds); wait for the verdict.
                if (elapsed > 12f)
                {
                    Finish("ball was hidden by the game without a hole confirmation", Kind.Fail, false);
                }
                return;
            }
            bool settled = elapsed > 0.6f && ball.IsStationary;
            restTime = settled ? restTime + Time.deltaTime : 0f;
            bool timedOut = elapsed > 30f;
            bool neverMoved = elapsed > 1.5f && (pos - shotStart).sqrMagnitude < 0.0025f;
            if (restTime < 0.6f && !timedOut && !neverMoved)
            {
                return;
            }
            if (neverMoved)
            {
                Finish("the server did not move the ball (hit rejected)", Kind.Fail, false);
                return;
            }

            Last.hasRest = true;
            Last.rest = pos;
            Last.restDistance = Flat(pos - shotCup.center).magnitude;
            Last.stage = "settled " + Last.restDistance.ToString("0.00") + " m from the cup, not holed";
            Learn();
            LogShot(timedOut ? "timeout" : "settled");
            if (attemptsLeft > 0 && !timedOut)
            {
                attemptsLeft--;
                Fire();
                return;
            }
            Finish("not holed after " + Last.attempts + " attempt(s); ball rests " + Last.restDistance.ToString("0.00") + " m from the cup (closest pass " + Last.closest.ToString("0.00") + " m)", Kind.Fail, true);
        }

        private void Finish(string text, Kind kind, bool logged)
        {
            phase = Phase.Idle;
            Last.running = false;
            if (!logged)
            {
                Last.stage = text;
                LogShot("ended");
            }
            Set("Auto Hole: " + text, kind);
        }

        /// <summary>
        /// Compare the real first touchdown with the predicted one. A consistent range ratio becomes a speed scale,
        /// a consistent sideways angle becomes a yaw offset. Wild errors (tree, obstacle) are ignored.
        /// </summary>
        private void Learn()
        {
            if (!Plugin.Cfg.AdaptShots.Value || !Last.hasLanding)
            {
                return;
            }
            Vector3 want = Flat(Last.predictedLanding - shotStart);
            Vector3 got = Flat(Last.actualLanding - shotStart);
            if (want.magnitude < 2f || got.magnitude < 1f)
            {
                return;
            }
            float ratio = got.magnitude / want.magnitude;
            float angle = Vector3.SignedAngle(want, got, Vector3.up);
            if (ratio < 0.75f || ratio > 1.3f || Mathf.Abs(angle) > 12f)
            {
                return;
            }
            // Carry grows roughly with speed squared at these speeds, hence the square root.
            speedScale = Mathf.Clamp(speedScale * Mathf.Lerp(1f, Mathf.Sqrt(1f / ratio), 0.7f), 0.85f, 1.15f);
            yawOffsetDeg = Mathf.Clamp(yawOffsetDeg - angle * 0.7f, -6f, 6f);
        }

        private void LogShot(string result)
        {
            Plugin.I.Log("[autohole] attempt " + Last.attempts + " result " + result + " start " + shotStart.ToString("F2") + " dist " + Last.startDistance.ToString("0.00")
                + " power " + Last.power.ToString("0.0000") + " pitch " + Last.pitch.ToString("0") + " predictedDrop " + Last.predictedDrop + " predictedMiss " + Last.predictedMiss.ToString("0.00")
                + " predLanding " + Last.predictedLanding.ToString("F2") + (Last.hasLanding ? " actualLanding " + Last.actualLanding.ToString("F2") + " landingError " + Last.landingError.ToString("0.00") : " noLanding")
                + (Last.hasRest ? " rest " + Last.rest.ToString("F2") + " restDist " + Last.restDistance.ToString("0.00") : "") + " closest " + Last.closest.ToString("0.00")
                + " cup " + shotCup.center.ToString("F2") + " open " + shotCup.openRadius.ToString("0.000") + " scale " + speedScale.ToString("0.000") + " yaw " + yawOffsetDeg.ToString("0.00")
                + " wind " + Game.Try(() => WindManager.Wind).ToString("F1"));
        }

        // ---------------------------------------------------------------- Cup Snap

        private int holedAtStart;

        private bool Scored() => Plugin.HoledCount > holedAtStart;

        public void CupSnap(BallControl balls, bool forceCarry = false)
        {
            if (!Plugin.Cfg.CupSnap.Value)
            {
                Plugin.Say("Cup Snap is disabled (enable it in Hole Assist)", Kind.Warn);
                return;
            }
            PlayerGolfer g = Game.Golfer;
            if (g != null && g.IsMatchResolved && g.MatchResolution != PlayerMatchResolution.Eliminated)
            {
                Set("Cup Snap unavailable: your hole is already resolved (" + g.MatchResolution + ")", Kind.Warn);
                return;
            }
            if (!TryGetCup(out CupInfo cup))
            {
                Set("Cup Snap unavailable: no active hole in this scene", Kind.Warn);
                return;
            }
            if (!balls.ReadyForPlacement(out string why))
            {
                Set("Cup Snap unavailable: " + why, Kind.Warn);
                return;
            }
            if (!Game.InDrivingRange && Game.State < MatchState.Ongoing)
            {
                Set("Cup Snap unavailable: hole not in play yet (" + Game.State + ")", Kind.Warn);
                return;
            }

            holedAtStart = Plugin.HoledCount;
            if (Game.HostCheatsEnabled && !forceCarry)
            {
                // The game's own finishHole console command (server adds a penalty stroke).
                g.CmdFinishHole();
                Plugin.I.StartCoroutine(WaitScored("Cup Snap via native finishHole (host has Console Commands on)"));
                return;
            }

            // Just above the rim, centred on the opening, so the ball falls straight into the pit.
            Vector3 target = cup.center + Vector3.up * 0.25f;
            Set("Cup Snap: placing ball over the cup...", Kind.Info);
            Plugin.I.StartCoroutine(balls.PlaceBall(target, cup.center.y, Scored, (ok, msg) =>
                Set(ok ? "Cup Snap: HOLED (confirmed by the game)" : "Cup Snap failed: " + msg, ok ? Kind.Ok : Kind.Fail), forceCarry));
        }

        private IEnumerator WaitScored(string label)
        {
            float t = 0f;
            while (t < 3f && !Scored())
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            bool ok = Scored();
            Set(ok ? label + ": HOLED" : label + ": command sent but no score followed", ok ? Kind.Ok : Kind.Fail);
        }

        private void Set(string text, Kind kind)
        {
            Outcome = text;
            OutcomeKind = kind;
            Plugin.Say(text, kind);
        }
    }
}
