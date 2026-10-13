using System.Collections.Generic;
using UnityEngine;

namespace SbgMod
{
    /// <summary>IMGUI drawing helpers and the world overlays / HUD. Everything here is display only.</summary>
    public sealed class Visuals
    {
        private static Texture2D white;
        private static GUIStyle label, big, hud;

        private Trajectory.Path swingPath;
        private float swingPathTime;

        private static readonly Color BallColor = new Color(1f, 0.85f, 0.2f);
        private static readonly Color CupColor = new Color(0.35f, 1f, 0.45f);
        private static readonly Color PathColor = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color SolveColor = new Color(0.3f, 0.9f, 1f, 0.9f);
        private static readonly Color TargetColor = new Color(1f, 0.3f, 0.3f);

        /// <summary>User scale times a resolution factor so the UI keeps its size above 1080p.</summary>
        public static float Scale => Plugin.Cfg.UiScale.Value * Mathf.Max(1f, Screen.height / 1080f);

        public static void EnsureStyles()
        {
            if (white == null)
            {
                white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
                white.hideFlags = HideFlags.HideAndDontSave;
            }
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, richText = false, wordWrap = false };
                big = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
                label.normal.textColor = Color.white;
                hud = new GUIStyle(label) { alignment = TextAnchor.MiddleLeft, fontSize = 13 };
            }
            label.fontSize = Mathf.RoundToInt(15f * Scale);
        }

        public static void Rect(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        public static void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f)
            {
                return;
            }
            Matrix4x4 m = GUI.matrix;
            Color old = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), white);
            GUI.matrix = m;
            GUI.color = old;
        }

        public static void Text(Vector2 center, string text, Color c, GUIStyle style = null)
        {
            style = style ?? label;
            Vector2 size = style.CalcSize(new GUIContent(text));
            Rect r = new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            GUI.color = c;
            GUI.Label(r, text, style);
            GUI.color = old;
        }

        private static void Diamond(Vector2 p, float s, Color c)
        {
            Line(p + new Vector2(0, -s), p + new Vector2(s, 0), c, 2f);
            Line(p + new Vector2(s, 0), p + new Vector2(0, s), c, 2f);
            Line(p + new Vector2(0, s), p + new Vector2(-s, 0), c, 2f);
            Line(p + new Vector2(-s, 0), p + new Vector2(0, -s), c, 2f);
        }

        public static bool ToScreen(Camera cam, Vector3 world, out Vector2 screen)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            screen = new Vector2(s.x, Screen.height - s.y);
            return s.z > 0.05f;
        }

        /// <summary>Marker at a world point; when off-screen or behind the camera it becomes an edge arrow.</summary>
        private static void Indicator(Camera cam, Vector3 world, string text, Color c)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 p = new Vector2(s.x, Screen.height - s.y);
            float margin = 48f;
            bool onScreen = s.z > 0.05f && p.x > margin && p.x < Screen.width - margin && p.y > margin && p.y < Screen.height - margin;
            if (onScreen)
            {
                Diamond(p, 7f, c);
                Text(p + new Vector2(0f, -20f), text, c);
                return;
            }
            Vector2 dir = p - centre;
            if (s.z <= 0.05f)
            {
                dir = -dir; // projection flips behind the camera
            }
            if (dir.sqrMagnitude < 1f)
            {
                dir = Vector2.up;
            }
            dir.Normalize();
            float scale = Mathf.Min((Screen.width * 0.5f - margin) / Mathf.Max(Mathf.Abs(dir.x), 1e-4f), (Screen.height * 0.5f - margin) / Mathf.Max(Mathf.Abs(dir.y), 1e-4f));
            Vector2 edge = centre + dir * scale;
            Vector2 side = new Vector2(-dir.y, dir.x);
            Vector2 tip = edge + dir * 14f;
            Line(tip, edge - dir * 6f + side * 10f, c, 3f);
            Line(tip, edge - dir * 6f - side * 10f, c, 3f);
            Line(edge - dir * 6f + side * 10f, edge - dir * 6f - side * 10f, c, 3f);
            Text(edge - dir * 30f, text, c);
        }

        private static void DrawPath(Camera cam, List<Vector3> pts, Color c, float width)
        {
            bool havePrev = false;
            Vector2 prev = default;
            for (int i = 0; i < pts.Count; i++)
            {
                bool vis = ToScreen(cam, pts[i], out Vector2 cur);
                if (vis && havePrev)
                {
                    Line(prev, cur, c, width);
                }
                prev = cur;
                havePrev = vis;
            }
        }

        public void Update()
        {
            // Manual swing preview: same launch the game would apply (GetSwingDirection * power * hit speed).
            swingPath = swingPath != null && Time.time - swingPathTime < 0.25f ? swingPath : null;
            if (!Plugin.Cfg.ShowTrajectory.Value && !Plugin.Cfg.ShowLanding.Value)
            {
                return;
            }
            if (Time.time - swingPathTime < 0.08f)
            {
                return;
            }
            PlayerGolfer g = Game.Golfer;
            GolfBall ball = Game.Ball;
            if (g == null || ball == null || ball.IsHidden || !(g.IsAimingSwing || g.IsChargingSwing) || g.SwingPitch <= 0f)
            {
                return;
            }
            Hittable h = ball.AsEntity.AsHittable;
            float power = g.IsChargingSwing ? g.SwingNormalizedPower : 1f;
            float speed = power * h.SwingSettings.MaxPowerSwingHitSpeed * Game.Try(() => MatchSetupRules.GetValue(MatchSetupRules.Rule.SwingPower), 1f);
            Vector3 dir = g.GetSwingDirection(g.SwingPitch);
            swingPath = Trajectory.Simulate(Game.BallPos(ball), dir.normalized * speed, Trajectory.Capture(ball), null, 0f, true, 1200, 3);
            swingPathTime = Time.time;
        }

        public void Draw(HoleAssist hole, AimAssist aim, Speedrun run)
        {
            EnsureStyles();
            Camera cam = Game.Cam;
            ModConfig cfg = Plugin.Cfg;
            PlayerInfo me = Game.Player;
            if (cam != null && me != null)
            {
                GolfBall ball = Game.Ball;
                bool ballVisible = ball != null && !ball.IsHidden;
                HoleAssist.TryGetCup(out Vector3 cup, out _, out GolfHole holeObj);

                if (cfg.ShowTrajectory.Value && swingPath != null)
                {
                    DrawPath(cam, swingPath.points, PathColor, 2f);
                }
                if (cfg.ShowLanding.Value && swingPath != null && swingPath.hitGround && ToScreen(cam, swingPath.end, out Vector2 land))
                {
                    Diamond(land, 9f, PathColor);
                    Text(land + new Vector2(0f, 20f), "landing " + Vector3.Distance(swingPath.points[0], swingPath.end).ToString("0") + " m", PathColor);
                }
                if (hole.HasPreview && hole.Preview.path != null)
                {
                    DrawPath(cam, hole.Preview.path.points, hole.Preview.reachesCup ? SolveColor : new Color(1f, 0.6f, 0.2f, 0.9f), 2f);
                }
                if (holeObj != null && (cfg.ShowCup.Value || cfg.ShowCupDistance.Value))
                {
                    string text = "CUP";
                    if (cfg.ShowCupDistance.Value && ballVisible)
                    {
                        text += " " + Vector3.Distance(Game.BallPos(ball), cup).ToString("0.0") + " m from ball";
                    }
                    if (cfg.ShowCup.Value)
                    {
                        Indicator(cam, cup + Vector3.up * 0.5f, text, CupColor);
                    }
                    else
                    {
                        Text(new Vector2(Screen.width * 0.5f, 70f), text, CupColor);
                    }
                }
                if (cfg.BallFinder.Value || cfg.ShowBallDistance.Value)
                {
                    if (ballVisible)
                    {
                        string text = "BALL";
                        if (cfg.ShowBallDistance.Value)
                        {
                            text += " " + Vector3.Distance(me.transform.position, Game.BallPos(ball)).ToString("0.0") + " m";
                        }
                        if (cfg.BallFinder.Value)
                        {
                            Indicator(cam, Game.BallPos(ball) + Vector3.up * 0.3f, text, BallColor);
                        }
                        else
                        {
                            Text(new Vector2(Screen.width * 0.5f, 90f), text, BallColor);
                        }
                    }
                    else if (cfg.BallFinder.Value)
                    {
                        Text(new Vector2(Screen.width * 0.5f, 90f), ball == null ? "BALL: not found (no ball assigned)" : "BALL: not in play (holed/hidden)", new Color(1f, 0.5f, 0.4f));
                    }
                }
                if (cfg.AimIndicator.Value && aim.HasTarget && ToScreen(cam, aim.AimPoint, out Vector2 tp))
                {
                    Diamond(tp, 12f, TargetColor);
                    Text(tp + new Vector2(0f, -24f), aim.TargetName, TargetColor);
                }
            }
            DrawHud(run, hole);
            DrawToast();
        }

        private void DrawHud(Speedrun run, HoleAssist hole)
        {
            ModConfig cfg = Plugin.Cfg;
            if (!cfg.ShowHud.Value || !cfg.Speedrun.Value || Game.Golfer == null)
            {
                return;
            }
            Matrix4x4 old = GUI.matrix;
            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            bool flash = Time.unscaledTimeAsDouble < run.FlashUntil;
            Rect box = new Rect(12f, Screen.height / s * 0.3f, 210f, 84f + Mathf.Min(run.Splits.Count, 4) * 16f);
            Rect(box, flash ? new Color(0.1f, 0.45f, 0.15f, 0.8f) : new Color(0f, 0f, 0f, 0.7f));
            big.normal.textColor = run.Running ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            GUI.Label(new Rect(box.x + 8f, box.y + 4f, 200f, 26f), Speedrun.Format(run.RunTime), big);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 8f, box.y + 30f, 200f, 18f), flash ? "HOLE COMPLETE" : (run.Running ? "hole " + Speedrun.Format(run.HoleTime) + "  (local clock)" : "timer stopped"), hud);
            string game = Speedrun.TryGetGameData(out int holeNo, out int par, out int strokes, out int course)
                ? "Hole " + holeNo + "  Par " + par + "  Strokes " + strokes + " (" + course + ")"
                : "game data unavailable";
            GUI.Label(new Rect(box.x + 8f, box.y + 46f, 200f, 18f), game, hud);
            GUI.Label(new Rect(box.x + 8f, box.y + 62f, 200f, 18f), "splits:" + (run.Splits.Count == 0 ? " none" : ""), hud);
            int first = Mathf.Max(0, run.Splits.Count - 4);
            for (int i = first; i < run.Splits.Count; i++)
            {
                Speedrun.Split sp = run.Splits[i];
                GUI.Label(new Rect(box.x + 8f, box.y + 78f + (i - first) * 16f, 200f, 18f), "H" + sp.hole + "  " + Speedrun.Format(sp.holeTime) + "  " + (sp.strokes >= 0 ? sp.strokes + " str" : "-"), hud);
            }
            GUI.matrix = old;
        }

        private static void DrawToast()
        {
            if (!string.IsNullOrEmpty(Plugin.Banner))
            {
                Rect(new Rect(0f, 0f, Screen.width, 46f * Scale), new Color(0.55f, 0.1f, 0.1f, 0.9f));
                Text(new Vector2(Screen.width * 0.5f, 23f * Scale), Plugin.Banner, Color.white, big);
            }
            if (Plugin.StatusText == null || Time.unscaledTime - Plugin.StatusTime > 5f || Plugin.MenuOpen)
            {
                return;
            }
            Text(new Vector2(Screen.width * 0.5f, Screen.height - 110f), Plugin.StatusText, KindColor(Plugin.StatusKind));
        }

        public static Color KindColor(Kind k)
        {
            switch (k)
            {
                case Kind.Ok: return new Color(0.4f, 1f, 0.5f);
                case Kind.Warn: return new Color(1f, 0.85f, 0.3f);
                case Kind.Fail: return new Color(1f, 0.45f, 0.4f);
                default: return Color.white;
            }
        }
    }
}
