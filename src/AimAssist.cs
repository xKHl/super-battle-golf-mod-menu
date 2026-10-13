using UnityEngine;

namespace SbgMod
{
    /// <summary>
    /// Weapon aim assist. Every aimed item in this build takes its aim from the gameplay camera:
    ///  * DuelingPistol / ElephantGun / RocketLauncher: PlayerInventory.GetFirearmAimPoint (camera ray).
    ///  * Railgun: PlayerInventory.CastRailgunAim (camera-derived aim point, sphere cast).
    ///  * FreezeBomb: PlayerInventory.GetFreezeBombAimDirection (camera forward with a pitch remap).
    /// So the assist steers the active camera module (yaw/pitch) toward the chosen target while the key is held.
    /// It cannot remove the pistol/elephant gun random spread (RandomlyRotatedDeg(MaxInaccuracyAngle)).
    /// </summary>
    public sealed class AimAssist
    {
        public enum Mode { Unsupported, Hitscan, Projectile, Ballistic }

        private static readonly RaycastHit[] hits = new RaycastHit[24];

        public Transform TargetTransform { get; private set; }
        public Vector3 AimPoint { get; private set; }
        public bool HasTarget { get; private set; }
        public string TargetName { get; private set; } = "";
        public string WeaponStatus { get; private set; } = "no item equipped";
        public Mode WeaponMode { get; private set; }

        private LockOnTarget locked;

        public static Mode Classify(ItemType item, out string status, out float weaponRange, out float projectileSpeed)
        {
            ItemSettings s = Game.Try(() => GameManager.ItemSettings);
            weaponRange = float.MaxValue;
            projectileSpeed = 0f;
            if (s == null)
            {
                status = "item settings not loaded";
                return Mode.Unsupported;
            }
            switch (item)
            {
                case ItemType.DuelingPistol:
                    weaponRange = s.DuelingPistolMaxShotDistance;
                    status = "Dueling Pistol: hitscan, supported (random spread up to " + s.DuelingPistolMaxInaccuracyAngle.ToString("0.#") + " deg remains)";
                    return Mode.Hitscan;
                case ItemType.ElephantGun:
                    weaponRange = s.ElephantGunMaxShotDistance;
                    status = "Elephant Gun: hitscan, supported (random spread up to " + s.ElephantGunMaxInaccuracyAngle.ToString("0.#") + " deg remains)";
                    return Mode.Hitscan;
                case ItemType.Railgun:
                    weaponRange = s.RailgunMaxShotDistance;
                    status = "Railgun: hitscan beam, supported (hold through the charge)";
                    return Mode.Hitscan;
                case ItemType.RocketLauncher:
                    projectileSpeed = s.RocketVelocity;
                    status = "Rocket Launcher: projectile " + s.RocketVelocity.ToString("0") + " m/s, supported with lead";
                    return Mode.Projectile;
                case ItemType.FreezeBomb:
                    projectileSpeed = s.FreezeBombShotSpeed;
                    status = "Freeze Bomb: arcing projectile " + s.FreezeBombShotSpeed.ToString("0") + " m/s, supported (drag-free arc estimate)";
                    return Mode.Ballistic;
                case ItemType.None:
                    status = "no item equipped";
                    return Mode.Unsupported;
                case ItemType.OrbitalLaser:
                    status = "Orbital Laser: uses the game's own target lock, not assisted";
                    return Mode.Unsupported;
                case ItemType.Thunderstorm:
                    status = "Thunderstorm: ground-targeted area effect, not assisted";
                    return Mode.Unsupported;
                case ItemType.Airhorn:
                case ItemType.FlashCamera:
                    status = item + ": area effect around you, no aiming involved";
                    return Mode.Unsupported;
                default:
                    status = item + ": not an aimed weapon";
                    return Mode.Unsupported;
            }
        }

        /// <summary>Why each candidate is or is not selectable right now (dev dump).</summary>
        public string Diagnose()
        {
            Camera cam = Game.Cam;
            PlayerInventory inv = Game.Inventory;
            if (cam == null || inv == null)
            {
                return "no camera/inventory";
            }
            Classify(Game.Try(() => inv.GetEffectivelyEquippedItem(), ItemType.None), out _, out float weaponRange, out float speed);
            System.Text.StringBuilder sb = new System.Text.StringBuilder("enabled " + Plugin.Cfg.AimAssist.Value + " keyHeld " + Plugin.Cfg.AimKey.Held + " forced " + Plugin.I.Dev.ForceAim + " menuOpen " + Plugin.MenuOpen + " weaponRange " + weaponRange + " speed " + speed + " fov " + Plugin.Cfg.AimFov.Value + " range " + Plugin.Cfg.AimRange.Value + " |");
            foreach (LockOnTarget t in LockOnTargetManager.Targets)
            {
                if (t == null || (!t.AsEntity.IsPlayer && t.GetComponent<TargetDummy>() == null))
                {
                    continue;
                }
                Vector3 to = t.GetLockOnPosition() - cam.transform.position;
                sb.Append(' ').Append(t.name).Append(": eligible ").Append(Eligible(t)).Append(" dist ").Append(to.magnitude.ToString("0")).Append(" angle ").Append(Vector3.Angle(cam.transform.forward, to).ToString("0")).Append(" los ").Append(HasLineOfSight(cam.transform.position, t)).Append(';');
            }
            return sb.ToString();
        }

        public void Update()
        {
            HasTarget = false;
            PlayerInventory inv = Game.Inventory;
            Camera cam = Game.Cam;
            if (inv == null || cam == null)
            {
                WeaponStatus = "not in a match";
                WeaponMode = Mode.Unsupported;
                locked = null;
                return;
            }
            ItemType item = Game.Try(() => inv.GetEffectivelyEquippedItem(), ItemType.None);
            WeaponMode = Classify(item, out string status, out float weaponRange, out float speed);
            WeaponStatus = status;
            if (!Plugin.Cfg.AimAssist.Value || WeaponMode == Mode.Unsupported || !(Plugin.Cfg.AimKey.Held || Plugin.I.Dev.ForceAim) || Plugin.MenuOpen)
            {
                locked = null;
                return;
            }

            float range = Mathf.Min(Plugin.Cfg.AimRange.Value, weaponRange);
            if (locked == null || !StillValid(locked, cam, range))
            {
                locked = Pick(cam, range);
            }
            if (locked == null)
            {
                return;
            }

            Vector3 point = locked.GetLockOnPosition();
            Vector3 origin = cam.transform.position;
            Vector3 aim = point;
            bool ballisticOk = true;
            if (WeaponMode != Mode.Hitscan && speed > 0.1f)
            {
                Vector3 velocity = Plugin.Cfg.AimLead.Value ? Game.Try(() => locked.AsEntity.GetNetworkedVelocity()) : Vector3.zero;
                Vector3 muzzle = WeaponMode == Mode.Ballistic ? Game.Try(() => inv.GetFreezeBombBombPosition(), origin) : Game.Try(() => inv.GetRocketLauncherRocketPosition(), origin);
                float t = Vector3.Distance(muzzle, point) / speed;
                for (int i = 0; i < 3; i++)
                {
                    aim = point + velocity * t;
                    t = Vector3.Distance(muzzle, aim) / speed;
                }
                if (WeaponMode == Mode.Ballistic)
                {
                    ballisticOk = SteerBallistic(cam, muzzle, aim, speed);
                }
            }
            AimPoint = aim;
            HasTarget = true;
            TargetTransform = locked.transform;
            TargetName = Describe(locked);
            if (WeaponMode != Mode.Ballistic)
            {
                Steer(cam, (aim - origin).normalized);
            }
            else if (!ballisticOk)
            {
                TargetName += " (out of arc range)";
            }
        }

        private static string Describe(LockOnTarget t)
        {
            if (t.AsEntity != null && t.AsEntity.IsPlayer)
            {
                return Game.Try(() => CourseManager.GetPlayerName(t.AsEntity.PlayerInfo.PlayerId), "player");
            }
            return t.name;
        }

        private static bool Eligible(LockOnTarget t)
        {
            if (t == null || t.AsEntity == null)
            {
                return false;
            }
            PlayerInfo me = Game.Player;
            if (me != null && t == me.AsEntity.AsLockOnTarget)
            {
                return false;
            }
            if (t.AsEntity.IsPlayer)
            {
                // The game's own validity rules: teammates, spectators, resolved players, invisibility, knockout immunity.
                return Game.Try(() => t.IsValidForLocalPlayer(false));
            }
            return Plugin.Cfg.AimDummies.Value && t.GetComponent<TargetDummy>() != null;
        }

        private static bool StillValid(LockOnTarget t, Camera cam, float range)
        {
            if (!Eligible(t))
            {
                return false;
            }
            Vector3 to = t.GetLockOnPosition() - cam.transform.position;
            if (to.magnitude > range * 1.1f)
            {
                return false;
            }
            // Wider cone to keep a target than to acquire one, so tracking does not flicker.
            if (Vector3.Angle(cam.transform.forward, to) > Plugin.Cfg.AimFov.Value * 0.5f + 10f)
            {
                return false;
            }
            return !Plugin.Cfg.AimLineOfSight.Value || HasLineOfSight(cam.transform.position, t);
        }

        private static LockOnTarget Pick(Camera cam, float range)
        {
            LockOnTarget best = null;
            float bestAngle = Plugin.Cfg.AimFov.Value * 0.5f;
            Vector3 origin = cam.transform.position;
            var targets = Game.Try(() => LockOnTargetManager.Targets);
            if (targets == null)
            {
                return null;
            }
            foreach (LockOnTarget t in targets)
            {
                if (!Eligible(t))
                {
                    continue;
                }
                Vector3 to = t.GetLockOnPosition() - origin;
                if (to.magnitude > range)
                {
                    continue;
                }
                float angle = Vector3.Angle(cam.transform.forward, to);
                if (angle >= bestAngle)
                {
                    continue;
                }
                if (Plugin.Cfg.AimLineOfSight.Value && !HasLineOfSight(origin, t))
                {
                    continue;
                }
                best = t;
                bestAngle = angle;
            }
            return best;
        }

        /// <summary>Same rule as PlayerGolfer.TryGetBestLockOnTarget: only my own or the target's colliders may be in the way.</summary>
        private static bool HasLineOfSight(Vector3 origin, LockOnTarget t)
        {
            Vector3 to = t.GetLockOnPosition() - origin;
            int mask = Game.Try(() => (int)GameManager.LayerSettings.LockOnLineOfSightBlockerMask, Physics.DefaultRaycastLayers);
            int n = Physics.RaycastNonAlloc(origin, to.normalized, hits, to.magnitude, mask, QueryTriggerInteraction.Ignore);
            Hittable mine = Game.Try(() => Game.Player.AsHittable);
            Hittable theirs = t.AsEntity.AsHittable;
            for (int i = 0; i < n; i++)
            {
                Hittable h = hits[i].collider.GetComponentInParent<Hittable>(true);
                if (h == null || (h != mine && h != theirs))
                {
                    return false;
                }
            }
            return true;
        }

        private static float YawOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        /// <summary>Pitch in the camera modules' convention: positive looks down (OrbitCameraModule.Rotate subtracts mouse-up).</summary>
        private static float PitchOf(Vector3 d) => -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;

        private static void Steer(Camera cam, Vector3 wanted)
        {
            Vector3 fwd = cam.transform.forward;
            ApplyDelta(Mathf.DeltaAngle(YawOf(fwd), YawOf(wanted)), PitchOf(wanted) - PitchOf(fwd));
        }

        private static void ApplyDelta(float dYaw, float dPitch)
        {
            float smooth = Plugin.Cfg.AimSmooth.Value;
            float k = Plugin.Cfg.AimStrength.Value * (smooth <= 0.001f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / smooth));
            CameraModule module = Game.Try(() => CameraModuleController.CurrentModule);
            if (module is OrbitCameraModule orbit)
            {
                orbit.SetYaw(orbit.Yaw + dYaw * k);
                orbit.SetPitch(orbit.Pitch + dPitch * k);
            }
            else if (module is FirstPersonCameraModule fp)
            {
                fp.SetYaw(fp.Yaw + dYaw * k);
                fp.SetPitch(fp.Pitch + dPitch * k);
            }
        }

        /// <summary>
        /// Freeze bomb: find the launch elevation that reaches the target on a drag-free arc, then invert the
        /// game's camera-pitch remap (PlayerInventory.GetFreezeBombAimDirection) to get the camera pitch to hold.
        /// </summary>
        private static bool SteerBallistic(Camera cam, Vector3 muzzle, Vector3 aim, float speed)
        {
            Vector3 to = aim - muzzle;
            float d = new Vector2(to.x, to.z).magnitude;
            float h = to.y;
            float g = Mathf.Abs(Physics.gravity.y);
            float v2 = speed * speed;
            float disc = v2 * v2 - g * (g * d * d + 2f * h * v2);
            Vector3 fwd = cam.transform.forward;
            float dYaw = Mathf.DeltaAngle(YawOf(fwd), YawOf(aim - cam.transform.position));
            if (disc < 0f || d < 0.01f)
            {
                ApplyDelta(dYaw, 0f);
                return false;
            }
            float elevation = Mathf.Atan2(v2 - Mathf.Sqrt(disc), g * d) * Mathf.Rad2Deg; // low arc, up-positive
            float wantedOut = -elevation;                                                // game pitch: up-negative
            ItemSettings s = GameManager.ItemSettings;
            float lo = -89f, hi = 89f;
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi);
                float offset = BMath.RemapClamped(0f - s.FreezeBombNoOffsetAimPitch, 0f - s.FreezeBombFullOffsetAimPitch, 0f, 0f - s.FreezeBombAimPitchOffset, mid);
                float result = Mathf.Clamp(mid + offset, 0f - s.FreezeBombAimMaxPitch, 0f - s.FreezeBombAimMinPitch);
                if (result < wantedOut)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            float cameraPitch = 0.5f * (lo + hi);
            ApplyDelta(dYaw, cameraPitch - PitchOf(fwd));
            return true;
        }
    }
}
