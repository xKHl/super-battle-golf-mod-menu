using System;
using Mirror;
using UnityEngine;

namespace SbgMod
{
    /// <summary>Null-safe accessors over the game's own singletons. Nothing here is cached across scenes.</summary>
    public static class Game
    {
        public static T Try<T>(Func<T> f, T fallback = default)
        {
            try
            {
                return f();
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public static PlayerGolfer Golfer => Try(() => GameManager.LocalPlayerAsGolfer);
        public static PlayerInfo Player => Try(() => GameManager.LocalPlayerInfo);
        public static PlayerMovement Movement => Try(() => GameManager.LocalPlayerMovement);
        public static PlayerInventory Inventory => Try(() => GameManager.LocalPlayerInventory);
        public static Camera Cam => Try(() => GameManager.Camera);
        public static GolfHole Hole => Try(() => GolfHoleManager.MainHole);

        /// <summary>The local player's own ball (never "nearest ball").</summary>
        public static GolfBall Ball
        {
            get
            {
                PlayerGolfer g = Golfer;
                return g != null ? Try(() => g.OwnBall) : null;
            }
        }

        public static bool IsHost => NetworkServer.active;
        public static bool InDrivingRange => Try(() => SingletonBehaviour<DrivingRangeManager>.HasInstance);
        public static bool HostCheatsEnabled => Try(() => MatchSetupRules.IsCheatsEnabled());

        public static MatchState State => Try(() => CourseManager.MatchState, MatchState.Initializing);

        /// <summary>The game only lets swings connect once tee-off has finished (PlayerGolfer.ReleaseSwingChargeInternal).</summary>
        public static bool SwingsAllowed => InDrivingRange || State > MatchState.TeeOff;

        public static bool BallUsable(out string why)
        {
            PlayerGolfer g = Golfer;
            if (g == null)
            {
                why = "no local player";
                return false;
            }
            GolfBall b = Ball;
            if (b == null)
            {
                why = "you have no ball";
                return false;
            }
            if (b.IsHidden)
            {
                why = "ball is hidden (already holed or not in play)";
                return false;
            }
            if (b.OutOfBoundsReturnState != BallOutOfBoundsReturnState.None)
            {
                why = "ball is being returned from out of bounds";
                return false;
            }
            why = null;
            return true;
        }

        public static Vector3 BallPos(GolfBall b) => b.Rigidbody != null ? b.Rigidbody.worldCenterOfMass : b.transform.position;

        public static float BallRadius(GolfBall b)
        {
            SphereCollider c = b.Collider;
            return c != null ? c.radius * Mathf.Max(b.transform.lossyScale.x, 0.0001f) : 0.05f;
        }

        private static int ballMask;
        private static float ballMaskTime = -100f;

        /// <summary>
        /// Every layer the ball physically collides with (project collision matrix plus the collider's own overrides),
        /// minus the local player's body, which the game makes the ball ignore right after a hit.
        /// </summary>
        public static int BallCollisionMask(GolfBall ball)
        {
            if (Time.unscaledTime - ballMaskTime < 5f && ballMask != 0)
            {
                return ballMask;
            }
            int mask = 0;
            SphereCollider c = ball.Collider;
            // GolfBall switches between StationaryBallLayer and DynamicBallLayer; a ball in flight is on the dynamic one.
            int stationary = c != null ? c.gameObject.layer : ball.gameObject.layer;
            int layer = Try(() => GameManager.LayerSettings.DynamicBallLayer, stationary);
            for (int l = 0; l < 32; l++)
            {
                if (!Physics.GetIgnoreLayerCollision(layer, l))
                {
                    mask |= 1 << l;
                }
            }
            if (c != null)
            {
                mask |= c.includeLayers;
                mask &= ~(int)c.excludeLayers;
            }
            int ground = GroundMask;
            PlayerInfo me = Player;
            if (me != null)
            {
                int body = 1 << me.gameObject.layer;
                if ((ground & body) == 0)
                {
                    mask &= ~body;
                }
            }
            mask &= ~(1 << layer);
            mask &= ~(1 << Try(() => GameManager.LayerSettings.StationaryBallLayer, stationary));
            mask |= ground;
            ballMask = mask;
            ballMaskTime = Time.unscaledTime;
            return mask;
        }

        public static int GroundMask => Try(() => (int)GameManager.LayerSettings.BallGroundableMask, Physics.DefaultRaycastLayers);
    }
}
