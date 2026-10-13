using System;
using System.Globalization;
using System.IO;
using System.Collections;
using System.Text;
using UnityEngine;

namespace SbgMod
{
    /// <summary>
    /// Test hook, disabled unless "[7. Dev] Command File" is true. Polls BepInEx/config/sbgmod.cmd, runs one command
    /// per line and logs the outcome, so features can be exercised and inspected without window focus.
    /// </summary>
    public sealed class DevChannel
    {
        private readonly string path;
        private float nextPoll;
        public bool ForceAim;

        private readonly string logPath;
        private readonly bool armed;
        private bool trialsRunning;

        public DevChannel(string configDir)
        {
            path = Path.Combine(configDir, "sbgmod.cmd");
            logPath = Path.Combine(configDir, "sbgmod-dev.log");
            // Only a game instance started with -sbgdev listens for commands, so a test session can never
            // drive a normal play session that happens to be running from the same install.
            armed = Array.IndexOf(Environment.GetCommandLineArgs(), "-sbgdev") >= 0;
            if (armed)
            {
                try
                {
                    File.WriteAllText(logPath, "dev channel armed, pid " + System.Diagnostics.Process.GetCurrentProcess().Id + Environment.NewLine);
                }
                catch (IOException)
                {
                }
            }
        }

        public bool Armed => armed;

        public void StartTrials(Plugin plugin, int count, float min, float max)
        {
            if (trialsRunning)
            {
                Plugin.Say("Self-test is already running", Kind.Warn);
                return;
            }
            if (!Game.IsHost || !Game.InDrivingRange)
            {
                Plugin.Say("Self-test needs you to be the host in the driving range", Kind.Warn);
                return;
            }
            plugin.StartCoroutine(Trials(plugin, count, min, max));
        }

        /// <summary>Mirror of every mod log line, flushed immediately (the BepInEx log is buffered and may be locked by another instance).</summary>
        public void Mirror(string line)
        {
            if (!armed)
            {
                return;
            }
            try
            {
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }

        private static void Log(string s) => Plugin.I.Log("[dev] " + s);

        public void Update(Plugin plugin)
        {
            if (!armed || !Plugin.Cfg.DevCommands.Value || Time.unscaledTime < nextPoll)
            {
                return;
            }
            nextPoll = Time.unscaledTime + 0.2f;
            if (!Application.runInBackground)
            {
                Application.runInBackground = true;
            }
            if (!File.Exists(path))
            {
                return;
            }
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
                File.Delete(path);
            }
            catch (IOException)
            {
                return;
            }
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                try
                {
                    Run(plugin, line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                }
                catch (Exception e)
                {
                    Log("command '" + line + "' threw: " + e);
                }
            }
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static Vector3 V(string[] a, int i) => new Vector3(F(a[i]), F(a[i + 1]), F(a[i + 2]));

        private void Run(Plugin plugin, string[] a)
        {
            switch (a[0].ToLowerInvariant())
            {
                case "host":
                    BNetworkManager.SetLobbyMode(LobbyMode.InviteOnly);
                    MainMenu menu = UnityEngine.Object.FindFirstObjectByType<MainMenu>();
                    if (menu == null)
                    {
                        Log("host: no MainMenu in this scene");
                        break;
                    }
                    menu.StartHost();
                    Log("host: StartHost called, lobby mode InviteOnly");
                    break;
                case "dump":
                    Dump(plugin);
                    break;
                case "ball":
                    BallDispenser d = UnityEngine.Object.FindFirstObjectByType<BallDispenser>();
                    if (d == null)
                    {
                        Log("ball: no BallDispenser in scene");
                        break;
                    }
                    d.LocalPlayerInteract();
                    Log("ball: dispenser interact sent");
                    break;
                case "trials":
                    StartTrials(plugin, int.Parse(a[1]), F(a[2]), F(a[3]));
                    break;
                case "autohole":
                    plugin.Hole.Trigger();
                    break;
                case "cupsnap":
                    plugin.Hole.CupSnap(plugin.Balls, a.Length > 1 && a[1] == "carry");
                    break;
                case "recall":
                    plugin.Balls.Recall();
                    break;
                case "teleport":
                    plugin.Balls.TeleportTo(V(a, 1), a.Length > 4 && a[4] == "carry");
                    break;
                case "tp":
                    Game.Movement.Teleport(V(a, 1), Game.Movement.transform.rotation, false);
                    Log("tp: player moved to " + V(a, 1));
                    break;
                case "look":
                    if (Game.Try(() => CameraModuleController.CurrentModule) is OrbitCameraModule orbit)
                    {
                        orbit.SetYaw(F(a[1]));
                        orbit.SetPitch(F(a[2]));
                        Log("look: orbit yaw " + orbit.Yaw + " pitch " + orbit.Pitch);
                    }
                    else
                    {
                        Log("look: current module is " + Game.Try(() => CameraModuleController.CurrentModuleType.ToString(), "none"));
                    }
                    break;
                case "aim":
                    ForceAim = a.Length > 1 && a[1] == "on";
                    Log("aim: force hold " + ForceAim);
                    break;
                case "set":
                    foreach (var def in Plugin.Cfg.File.Keys)
                    {
                        if (string.Equals(def.Key.Replace(" ", ""), a[1], StringComparison.OrdinalIgnoreCase))
                        {
                            Plugin.Cfg.File[def].SetSerializedValue(a[2]);
                            Log("set: " + def.Key + " = " + Plugin.Cfg.File[def].GetSerializedValue());
                        }
                    }
                    break;
                case "tab":
                    plugin.ShowTab(int.Parse(a[1]));
                    break;
                case "menu":
                    plugin.ToggleMenu();
                    break;
                case "shot":
                    ScreenCapture.CaptureScreenshot(a[1]);
                    Log("shot: " + a[1]);
                    break;
                case "give":
                    if (!Game.IsHost)
                    {
                        Log("give: host only");
                        break;
                    }
                    ItemType give = (ItemType)Enum.Parse(typeof(ItemType), a[1], true);
                    Log("give: " + give + " added=" + Game.Inventory.ServerTryAddItem(give, a.Length > 2 ? int.Parse(a[2]) : 3));
                    break;
                case "equip":
                    Log("equip: slot " + a[1] + " selected=" + Game.Inventory.TrySelectItemSlot(int.Parse(a[1]), true) + " now " + Game.Inventory.GetEffectivelyEquippedItem());
                    break;
                case "shoot":
                    plugin.StartCoroutine(Shoot());
                    break;
                case "walk":
                    plugin.StartCoroutine(Walk(F(a[1])));
                    break;
                case "banner":
                    Plugin.Banner = a.Length > 1 ? string.Join(" ", a, 1, a.Length - 1) : null;
                    break;
                case "use":
                    bool eat;
                    Log("use: TryUseItem=" + Game.Inventory.TryUseItem(false, out eat) + " equipped " + Game.Inventory.GetEffectivelyEquippedItem());
                    break;
                case "run":
                    if (a[1] == "start")
                    {
                        plugin.Run.StartNow();
                    }
                    else
                    {
                        plugin.Run.Reset();
                    }
                    Log("run: " + a[1]);
                    break;
                default:
                    Log("unknown command " + a[0]);
                    break;
            }
        }

        /// <summary>Hold aim (right mouse), then click fire (left mouse) through the InputSystem, and report the slot count.</summary>
        private static IEnumerator Shoot()
        {
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            PlayerInventory inv = Game.Inventory;
            if (mouse == null || inv == null)
            {
                Log("shoot: no mouse or inventory");
                yield break;
            }
            int index = inv.EquippedItemIndex;
            ItemType item = inv.GetEffectivelyEquippedItem();
            int before = index >= 0 ? inv.GetEffectiveSlot(index).remainingUses : -1;
            var aim = new UnityEngine.InputSystem.LowLevel.MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right);
            var fire = aim.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            float t = 0f;
            while (t < 1.2f)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, aim);
                t += Time.deltaTime;
                yield return null;
            }
            bool aiming = inv.IsAimingItemNetworked();
            t = 0f;
            while (t < 0.15f)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, fire);
                t += Time.deltaTime;
                yield return null;
            }
            t = 0f;
            while (t < 1.2f)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, aim);
                t += Time.deltaTime;
                yield return null;
            }
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState());
            int after = index >= 0 && index < inv.slots.Count ? inv.GetEffectiveSlot(index).remainingUses : -1;
            Log("shoot: " + item + " kept=" + Tweaks.UsesKept + " returned=" + Tweaks.HashesReturned + " uses " + before + " -> " + after + " (slot now " + (index >= 0 && index < inv.slots.Count ? inv.GetEffectiveSlot(index).itemType.ToString() : "?") + ") infiniteAmmo=" + Plugin.Cfg.InfiniteAmmo.Value);
        }

        /// <summary>Hold W through the InputSystem for a while and report the top grounded speed reached.</summary>
        private static IEnumerator Walk(float seconds)
        {
            UnityEngine.InputSystem.Keyboard kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null)
            {
                Log("walk: no keyboard device");
                yield break;
            }
            Vector3 from = Game.Player.transform.position;
            float t = 0f, peak = 0f;
            while (t < seconds)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
                if (Game.Movement != null && Game.Movement.IsGrounded)
                {
                    peak = Mathf.Max(peak, Tweaks.CurrentSpeed());
                }
                t += Time.deltaTime;
                yield return null;
            }
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            Vector3 moved = Game.Player.transform.position - from;
            moved.y = 0f;
            Log("walk: " + seconds + " s, factor " + PlayerMovement.consoleSpeedFactor + ", peak grounded speed " + peak.ToString("0.00") + " m/s, moved " + moved.magnitude.ToString("0.0") + " m, base run speed " + GameManager.PlayerMovementSettings.DefaultMoveSpeed);
        }

        /// <summary>Host only: drop the ball at spread-out spots around the cup, run Auto Hole, and tally real results.</summary>
        private IEnumerator Trials(Plugin plugin, int count, float minDist, float maxDist)
        {
            trialsRunning = true;
            try
            {
                IEnumerator inner = TrialsInner(plugin, count, minDist, maxDist);
                while (inner.MoveNext())
                {
                    yield return inner.Current;
                }
            }
            finally
            {
                trialsRunning = false;
                Plugin.Banner = null;
            }
        }

        private static IEnumerator TrialsInner(Plugin plugin, int count, float minDist, float maxDist)
        {
            if (!Game.IsHost)
            {
                Log("trials: host only");
                yield break;
            }
            int holed = 0, firstTry = 0, ran = 0, shots = 0;
            bool wasEnabled = Plugin.Cfg.AutoHole.Value;
            Plugin.Cfg.AutoHole.Value = true;
            for (int i = 0; i < count; i++)
            {
                Plugin.Banner = "AUTOMATED TEST " + (i + 1) + "/" + count + " RUNNING - please do not touch the game until this bar disappears";
                GolfBall ball = Game.Ball;
                if (ball == null || !HoleAssist.TryGetCup(out HoleAssist.CupInfo cup))
                {
                    Log("trials: no ball or hole");
                    Plugin.Banner = null;
                    yield break;
                }
                float d = Mathf.Lerp(minDist, maxDist, (i + 0.5f) / count);
                Vector3 spot = cup.center + Quaternion.Euler(0f, i * 137.5f, 0f) * Vector3.forward * d;
                if (!Physics.Raycast(spot + Vector3.up * 80f, Vector3.down, out RaycastHit hit, 200f, Game.GroundMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.8f)
                {
                    Log("[trial] " + i + " skipped: no usable ground at " + d.ToString("0.0") + " m");
                    continue;
                }
                BallControl.ServerPlaceBall(ball, hit.point + Vector3.up * 0.3f, Quaternion.identity);
                float wait = 0f, still = 0f;
                while (wait < 12f && still < 1f)
                {
                    still = ball != null && ball.IsStationary && !ball.IsHidden ? still + Time.deltaTime : 0f;
                    wait += Time.deltaTime;
                    yield return null;
                }
                if (still < 1f)
                {
                    Log("[trial] " + i + " skipped: ball did not settle");
                    continue;
                }
                float startDist = Vector3.Distance(Game.BallPos(ball), cup.center);
                float elevation = Game.BallPos(ball).y - cup.center.y;
                int before = Plugin.HoledCount;
                plugin.Hole.Trigger();
                yield return null;
                float t = 0f;
                while (plugin.Hole.Busy && t < 150f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                bool ok = Plugin.HoledCount > before;
                if (plugin.Hole.Last.attempts == 0)
                {
                    Log("[trial] " + i + " not fired: " + plugin.Hole.Outcome);
                    continue;
                }
                ran++;
                shots += plugin.Hole.Last.attempts;
                if (ok)
                {
                    holed++;
                    if (plugin.Hole.Last.attempts == 1)
                    {
                        firstTry++;
                    }
                }
                Log("[trial] " + i + " dist " + startDist.ToString("0.0") + " elev " + elevation.ToString("0.0") + " -> " + (ok ? "HOLED" : "MISS") + " in " + plugin.Hole.Last.attempts + " attempt(s)" + (ok ? "" : " rest " + plugin.Hole.Last.restDistance.ToString("0.00")));
                // The driving range hides a holed ball and respawns it a few seconds later; wait for that to finish.
                float settle = 0f, visible = 0f;
                while (settle < 15f && visible < 2f)
                {
                    visible = ball != null && !ball.IsHidden ? visible + Time.deltaTime : 0f;
                    settle += Time.deltaTime;
                    yield return null;
                }
            }
            Plugin.Banner = null;
            Plugin.Cfg.AutoHole.Value = wasEnabled;
            Log("[trials] done: " + holed + "/" + ran + " holed, " + firstTry + " on the first attempt, " + shots + " shots total, range " + minDist + "-" + maxDist + " m");
        }

        private static string P(Vector3 v) => "(" + v.x.ToString("0.00", CultureInfo.InvariantCulture) + " " + v.y.ToString("0.00", CultureInfo.InvariantCulture) + " " + v.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";

        private static void Add(StringBuilder sb, string name, Func<object> f)
        {
            string v;
            try
            {
                object o = f();
                v = o is Vector3 vec ? P(vec) : Convert.ToString(o, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                v = "<" + e.GetType().Name + ">";
            }
            sb.Append("\n    ").Append(name).Append(" = ").Append(v);
        }

        private static void Dump(Plugin plugin)
        {
            StringBuilder sb = new StringBuilder("state dump");
            Add(sb, "scene", () => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            Add(sb, "focused/runInBackground", () => Application.isFocused + "/" + Application.runInBackground);
            Add(sb, "host", () => Game.IsHost);
            Add(sb, "drivingRange", () => Game.InDrivingRange);
            Add(sb, "matchState", () => CourseManager.MatchState);
            Add(sb, "consoleCommandsRule", () => Game.HostCheatsEnabled);
            Add(sb, "swingsAllowed", () => Game.SwingsAllowed);
            Add(sb, "wind", () => WindManager.Wind);
            Add(sb, "fixedDeltaTime", () => Time.fixedDeltaTime);
            Add(sb, "gravity", () => Physics.gravity);
            PlayerGolfer g = Game.Golfer;
            Add(sb, "golfer", () => g != null ? g.name : "null");
            if (g != null)
            {
                Add(sb, "player.pos", () => g.transform.position);
                Add(sb, "player.forward", () => g.transform.forward);
                Add(sb, "player.grounded", () => Game.Movement.IsGrounded);
                Add(sb, "resolution", () => g.MatchResolution);
                Add(sb, "aimingSwing/charging", () => g.IsAimingSwing + "/" + g.IsChargingSwing);
                Add(sb, "swingPitch", () => g.SwingPitch);
                Add(sb, "hitBoxCenter", () => GameManager.GolfSettings.GetLocalPlayerEffectiveSwingHitBoxLocalCenter());
                Add(sb, "hitBoxSize", () => GameManager.GolfSettings.GetLocalPlayerEffectiveSwingHitBoxSize());
                Add(sb, "maxSwingPitch", () => GameManager.GolfSettings.MaxSwingPitch);
                Add(sb, "maxOvercharge", () => GameManager.GolfSettings.MaxSwingOvercharge);
                Add(sb, "swingPowerRule", () => MatchSetupRules.GetValue(MatchSetupRules.Rule.SwingPower));
                Add(sb, "equipped", () => Game.Inventory.GetEffectivelyEquippedItem());
                Add(sb, "aim.weapon", () => plugin.Aim.WeaponStatus);
                Add(sb, "aim.target", () => plugin.Aim.HasTarget ? plugin.Aim.TargetName + " at " + P(plugin.Aim.AimPoint) : "none");
            }
            GolfBall b = Game.Ball;
            Add(sb, "ball", () => b != null ? b.name : "null");
            if (b != null)
            {
                Add(sb, "ball.pos", () => Game.BallPos(b));
                Add(sb, "ball.vel", () => b.Rigidbody.linearVelocity);
                Add(sb, "ball.stationary/hidden/grounded", () => b.IsStationary + "/" + b.IsHidden + "/" + b.IsGrounded);
                Add(sb, "ball.oob", () => b.OutOfBoundsReturnState);
                Add(sb, "ball.radius", () => Game.BallRadius(b));
                Add(sb, "ball.kinematic/useGravity/linDamp", () => b.Rigidbody.isKinematic + "/" + b.Rigidbody.useGravity + "/" + b.Rigidbody.linearDamping);
                Add(sb, "ball.simulatedHere/authority/predicted", () => b.AsEntity.IsSimulatingRigidbody() + "/" + b.AsEntity.HasRigidbodyAuthority() + "/" + (b.AsEntity.PredictedRigidbody != null));
                Add(sb, "ball.maxSwingSpeed", () => b.AsEntity.AsHittable.SwingSettings.MaxPowerSwingHitSpeed);
                Add(sb, "ball.maxPuttSpeed", () => b.AsEntity.AsHittable.SwingSettings.MaxPowerPuttHitSpeed);
                Add(sb, "ball.airDrag", () => GameManager.GolfBallSettings.LinearAirDragFactor);
                Add(sb, "ball.wind factors", () => b.AsEntity.AsHittable.settings.Wind.IsAffectedByWind + " " + b.AsEntity.AsHittable.settings.Wind.WindFactor + " " + b.AsEntity.AsHittable.settings.Wind.CrossWindFactor);
            }
            GolfHole h = Game.Hole;
            Add(sb, "hole", () => h != null ? h.name : "null");
            if (h != null)
            {
                Add(sb, "hole.pos", () => h.transform.position);
                Add(sb, "hole.collider", () => h.Collider != null ? h.Collider.GetType().Name + " " + P(h.Collider.bounds.center) + " size " + P(h.Collider.bounds.size) : "null");
                GolfHoleTrigger t = h.GetComponentInChildren<GolfHoleTrigger>(true);
                Add(sb, "hole.ballTrigger", () => t.ballTrigger.GetType().Name + " " + P(t.ballTrigger.bounds.center) + " size " + P(t.ballTrigger.bounds.size) + " enabled " + t.ballTrigger.enabled);
                Add(sb, "hole.generalTrigger", () => t.generalTrigger.GetType().Name + " " + P(t.generalTrigger.bounds.center) + " size " + P(t.generalTrigger.bounds.size));
                Add(sb, "cup target", () => HoleAssist.TryGetCup(out Vector3 cup, out float r, out _) ? P(cup) + " r " + r : "none");
                if (b != null)
                {
                    Add(sb, "ball->cup", () => Vector3.Distance(Game.BallPos(b), h.transform.position));
                    Add(sb, "solution", () => plugin.Hole.DescribeSolution());
                }
            }
            Add(sb, "camera", () => CameraModuleController.CurrentModuleType + " pos " + P(Game.Cam.transform.position) + " fwd " + P(Game.Cam.transform.forward));
            Add(sb, "lockOnTargets", () =>
            {
                StringBuilder t = new StringBuilder();
                foreach (LockOnTarget lt in LockOnTargetManager.Targets)
                {
                    if (lt != null)
                    {
                        t.Append(lt.name).Append(lt.AsEntity.IsPlayer ? "[player]" : lt.GetComponent<TargetDummy>() != null ? "[dummy]" : "").Append(' ').Append(P(lt.GetLockOnPosition())).Append("; ");
                    }
                }
                return t.ToString();
            });
            Add(sb, "aim.diag", () => plugin.Aim.Diagnose());
            Add(sb, "ballMask", () => "ball " + System.Convert.ToString(Game.BallCollisionMask(Game.Ball), 2) + " ground " + System.Convert.ToString(Game.GroundMask, 2) + " ballLayer " + LayerMask.LayerToName(Game.Ball.Collider.gameObject.layer) + " playerLayer " + LayerMask.LayerToName(Game.Player.gameObject.layer));
            Add(sb, "speed", () => "peak " + Tweaks.PeakSpeed.ToString("0.00") + " at x" + Tweaks.PeakFactor + " factor " + PlayerMovement.consoleSpeedFactor + " current " + Tweaks.CurrentSpeed().ToString("0.00") + " m/s default " + GameManager.PlayerMovementSettings.DefaultMoveSpeed);
            Add(sb, "slots", () =>
            {
                StringBuilder t = new StringBuilder();
                PlayerInventory inv = Game.Inventory;
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    InventorySlot slot = inv.GetEffectiveSlot(i);
                    t.Append(i).Append(":").Append(slot.itemType).Append("x").Append(slot.remainingUses).Append(" ");
                }
                return t + "| elephant cooldown " + GameManager.ItemSettings.ElephantGunShotCooldown + " uses " + Tweaks.UsesStatus();
            });
            Add(sb, "preview", () => plugin.Hole.HasPreview ? "yes, " + plugin.Hole.Preview.path.points.Count + " points, reaches " + plugin.Hole.Preview.reachesCup : "no");
            Add(sb, "splits", () =>
            {
                StringBuilder t = new StringBuilder();
                foreach (Speedrun.Split sp in plugin.Run.Splits)
                {
                    t.Append("H").Append(sp.hole).Append(' ').Append(Speedrun.Format(sp.holeTime)).Append(' ').Append(sp.strokes).Append("str; ");
                }
                return t.ToString();
            });
            Add(sb, "run", () => Speedrun.Format(plugin.Run.RunTime) + " running " + plugin.Run.Running + " splits " + plugin.Run.Splits.Count);
            Add(sb, "gameData", () => Speedrun.TryGetGameData(out int hole, out int par, out int st, out int cs) ? "hole " + hole + " par " + par + " strokes " + st + " course " + cs : "unavailable");
            Log(sb.ToString());
        }
    }
}
