using System;
using System.Collections;
using UnityEngine;

namespace SbgMod
{
    /// <summary>
    /// Tools for the local player's OWN ball.
    ///
    /// Authority notes (from the decompiled game):
    ///  * The ball is simulated by the server. Moving its transform on a client is only cosmetic.
    ///  * PlayerGolfer.CmdRestartBall is the one un-gated command that repositions the ball:
    ///    the server puts it at playerPos + up*0.25 + right*swingHitBoxCenter.x (ServerReturnBallToPlayer).
    ///  * The local player IS client-authoritative (PlayerMovement.Teleport / NetworkRigidbody.CmdTeleport).
    ///  So, as a non-host client, "place my ball at X" = stand at X, native restart, walk back.
    ///  The server rate-limits restart to one per 3 s (AntiCheatRateChecker "Restart ball").
    /// </summary>
    public sealed class BallControl
    {
        public const float RestartCooldown = 3.2f;
        private const float RestartHeight = 0.25f;

        private float lastRestart = -100f;

        public bool Busy { get; private set; }

        public float CooldownLeft => Mathf.Max(0f, RestartCooldown - (Time.unscaledTime - lastRestart));

        private static float HitBoxSide => Game.Try(() => GameManager.GolfSettings.GetLocalPlayerEffectiveSwingHitBoxLocalCenter().x);

        /// <summary>Where the player must stand so the server's restart puts the ball at ballTarget.</summary>
        public static Vector3 PlayerPosFor(Vector3 ballTarget, Quaternion playerRotation)
        {
            return ballTarget - Vector3.up * RestartHeight - (playerRotation * Vector3.right) * HitBoxSide;
        }

        private bool Ready(out string why)
        {
            if (Busy)
            {
                why = "another ball action is still running";
                return false;
            }
            if (!Game.BallUsable(out why))
            {
                return false;
            }
            PlayerInfo p = Game.Player;
            if (p == null || Game.Movement == null)
            {
                why = "no local player";
                return false;
            }
            if (Game.Try(() => p.ActiveGolfCartSeat.IsValid()))
            {
                why = "leave the golf cart first";
                return false;
            }
            if (!Game.IsHost && CooldownLeft > 0f)
            {
                why = "server restart cooldown " + CooldownLeft.ToString("0.0") + " s";
                return false;
            }
            return true;
        }

        /// <summary>Host only: same statements as PlayerGolfer.ServerReturnBallToPlayer, at an arbitrary position.</summary>
        internal static void ServerPlaceBall(GolfBall ball, Vector3 position, Quaternion rotation)
        {
            Entity e = ball.AsEntity;
            e.InformWillTeleport();
            ball.transform.SetPositionAndRotation(position, rotation);
            e.Rigidbody.position = position;
            e.Rigidbody.rotation = rotation;
            ball.Rigidbody.linearVelocity = Vector3.zero;
            ball.Rigidbody.angularVelocity = Vector3.zero;
            e.InformTeleported();
            ball.ServerInformNoLongerInHole();
            ball.OnRespawned();
        }

        // ---------------------------------------------------------------- Ball Recall

        public void Recall()
        {
            if (!Plugin.Cfg.BallRecall.Value)
            {
                Plugin.Say("Ball Recall is disabled", Kind.Warn);
                return;
            }
            if (!Ready(out string why))
            {
                Plugin.Say("Ball Recall unavailable: " + why, Kind.Warn);
                return;
            }
            PlayerGolfer g = Game.Golfer;
            Vector3 spawn = g.transform.position + Vector3.up * RestartHeight + g.transform.right * HitBoxSide;
            if (Plugin.Cfg.SafeGroundCheck.Value && !SafeSpot(spawn, Game.BallRadius(Game.Ball), out why))
            {
                Plugin.Say("Ball Recall refused: " + why, Kind.Warn);
                return;
            }
            Plugin.I.StartCoroutine(RecallRoutine(g));
        }

        private IEnumerator RecallRoutine(PlayerGolfer g)
        {
            Busy = true;
            GolfBall ball = g.OwnBall;
            lastRestart = Time.unscaledTime;
            g.CmdRestartBall(); // native; runs ServerReturnBallToPlayer on the server
            float t = 0f;
            bool arrived = false;
            while (t < 2.5f && ball != null && g != null)
            {
                if (Vector3.Distance(Game.BallPos(ball), g.transform.position) < 2.5f)
                {
                    arrived = true;
                    break;
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Busy = false;
            Plugin.Say(arrived ? "Ball recalled (server position confirmed)" : "Ball Recall sent, but the ball did not arrive (rejected or rate-limited)", arrived ? Kind.Ok : Kind.Fail);
        }

        // ---------------------------------------------------------------- Ball Teleport

        public void TeleportToAim()
        {
            if (!Plugin.Cfg.BallTeleport.Value)
            {
                Plugin.Say("Ball Teleport is disabled", Kind.Warn);
                return;
            }
            if (!Ready(out string why))
            {
                Plugin.Say("Ball Teleport unavailable: " + why, Kind.Warn);
                return;
            }
            Camera cam = Game.Cam;
            if (cam == null)
            {
                Plugin.Say("Ball Teleport unavailable: no camera", Kind.Warn);
                return;
            }
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 800f, Game.GroundMask, QueryTriggerInteraction.Ignore))
            {
                Plugin.Say("Ball Teleport: no ground under the crosshair", Kind.Warn);
                return;
            }
            bool safe = Plugin.Cfg.SafeGroundCheck.Value;
            if (safe && hit.normal.y < 0.25f)
            {
                Plugin.Say("Ball Teleport refused: surface is a wall or too steep", Kind.Warn);
                return;
            }
            float radius = Game.BallRadius(Game.Ball);
            Vector3 target = hit.point + Vector3.up * Mathf.Max(Plugin.Cfg.TeleportHeight.Value, RestartHeight);
            if (safe)
            {
                int tries = 0;
                while (Physics.CheckSphere(target, radius, Game.GroundMask, QueryTriggerInteraction.Ignore) && tries++ < 6)
                {
                    target += Vector3.up * 0.25f;
                }
                if (tries > 6)
                {
                    Plugin.Say("Ball Teleport refused: target is inside solid geometry", Kind.Warn);
                    return;
                }
            }
            Plugin.I.StartCoroutine(PlaceBall(target, hit.point.y, null, (ok, msg) =>
                Plugin.Say(ok ? "Ball teleported " + Vector3.Distance(Game.Player.transform.position, target).ToString("0") + " m away (server position confirmed)" : "Ball Teleport failed: " + msg, ok ? Kind.Ok : Kind.Fail)));
        }

        /// <summary>Teleport to an explicit world point (used by the dev channel). forceCarry exercises the client path even as host.</summary>
        public void TeleportTo(Vector3 target, bool forceCarry)
        {
            if (!Ready(out string why))
            {
                Plugin.Say("Ball Teleport unavailable: " + why, Kind.Warn);
                return;
            }
            Plugin.I.StartCoroutine(PlaceBall(target, target.y - RestartHeight, null, (ok, msg) =>
                Plugin.Say(ok ? "Ball teleported to " + target.ToString("F2") + ", ball now at " + Game.BallPos(Game.Ball).ToString("F2") : "Ball Teleport failed: " + msg, ok ? Kind.Ok : Kind.Fail), forceCarry));
        }

        public static bool SafeSpot(Vector3 ballPos, float radius, out string why)
        {
            int mask = Game.GroundMask;
            if (Physics.CheckSphere(ballPos, radius * 0.9f, mask, QueryTriggerInteraction.Ignore))
            {
                why = "spot is inside solid geometry";
                return false;
            }
            if (!Physics.Raycast(ballPos, Vector3.down, 6f, mask, QueryTriggerInteraction.Ignore))
            {
                why = "no ground within 6 m below";
                return false;
            }
            why = null;
            return true;
        }

        /// <summary>
        /// Puts the own ball at ballTarget. Host: direct server placement. Client: carry (teleport self, native restart, return).
        /// success (optional) ends the wait early with a positive result, e.g. "hole was scored".
        /// </summary>
        public IEnumerator PlaceBall(Vector3 ballTarget, float minPlayerY, Func<bool> success, Action<bool, string> done, bool forceCarry = false)
        {
            Busy = true;
            PlayerGolfer g = Game.Golfer;
            GolfBall ball = g.OwnBall;
            PlayerMovement move = Game.Movement;
            bool ok = false;
            string msg = null;

            if (Game.IsHost && !forceCarry)
            {
                ServerPlaceBall(ball, ballTarget, g.transform.rotation);
                float t = 0f;
                ok = success == null;
                while (!ok && t < 3f)
                {
                    ok = success();
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                msg = ok ? null : "placed on the server, but the expected result did not follow";
            }
            else
            {
                Vector3 homePos = move.transform.position;
                Quaternion homeRot = move.transform.rotation;
                Vector3 stand = PlayerPosFor(ballTarget, homeRot);
                if (stand.y < minPlayerY)
                {
                    stand.y = minPlayerY;
                }
                Vector3 expected = stand + Vector3.up * RestartHeight + (homeRot * Vector3.right) * HitBoxSide;
                Rigidbody body = Game.Try(() => Game.Player.AsEntity.Rigidbody);

                move.Teleport(stand, homeRot, false);   // native: also sends NetworkRigidbody.CmdTeleport
                lastRestart = Time.unscaledTime;
                g.CmdRestartBall();                     // ordered after the teleport on the reliable channel

                float t = 0f;
                bool arrived = false;
                while (t < 2.5f && ball != null && move != null)
                {
                    // Hold position so the server-side copy of the player does not drift or fall.
                    if (body != null)
                    {
                        body.position = stand;
                        if (!body.isKinematic)
                        {
                            body.linearVelocity = Vector3.zero;
                        }
                    }
                    move.transform.position = stand;

                    if (success != null && success())
                    {
                        arrived = true;
                        break;
                    }
                    Vector3 bp = Game.BallPos(ball);
                    Vector2 flat = new Vector2(bp.x - expected.x, bp.z - expected.z);
                    if (flat.magnitude < 1.2f && Mathf.Abs(bp.y - expected.y) < 4f)
                    {
                        arrived = true;
                        if (success == null)
                        {
                            break;
                        }
                    }
                    t += Time.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                }
                if (move != null)
                {
                    move.Teleport(homePos, homeRot, false);
                }
                if (success != null)
                {
                    ok = success();
                    msg = ok ? null : (arrived ? "ball reached the spot but the expected result did not follow" : "the server did not move the ball (rejected or rate-limited)");
                }
                else
                {
                    ok = arrived;
                    msg = ok ? null : "the server did not move the ball (rejected or rate-limited)";
                }
            }
            Busy = false;
            done?.Invoke(ok, msg);
        }

        public bool ReadyForPlacement(out string why) => Ready(out why);
    }
}
