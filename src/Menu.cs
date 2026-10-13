using BepInEx.Configuration;
using UnityEngine;

namespace SbgMod
{
    /// <summary>
    /// IMGUI menu: dark charcoal panels, green accent, sidebar tabs, status panel.
    /// All textures are 1x1 solid colours created once; nothing is animated.
    /// </summary>
    public sealed class Menu
    {
        private static readonly string[] Tabs = { "Hole Assist", "Ball Control", "Weapon Aim Assist", "Weapon Options", "Player Speed", "Speedrun", "Visuals", "Settings" };

        private const float Width = 700f, Height = 548f, Side = 168f, StatusHeight = 70f, TitleHeight = 30f;
        private const float LabelWidth = 210f, ControlWidth = 190f;

        private static readonly Color Bg = Hex(0x15171A), Panel = Hex(0x1D2024), Card = Hex(0x24282D), Line = Hex(0x30353B);
        // The game renders in linear colour space, which darkens IMGUI colours, so text tones are chosen brighter than they should look.
        private static readonly Color Accent = Hex(0x4FD890), AccentDim = Hex(0x2F8F5C), TextCol = Hex(0xF2F4F6), Dim = Hex(0xC3C9D0);
        private static readonly Color Amber = Hex(0xE0A43A), Red = Hex(0xE0635C), Off = Hex(0x3A4047);

        private Rect window = new Rect(60f, 60f, Width, Height);
        private int tab;
        private readonly Vector2[] scroll = new Vector2[8];
        private bool dragging;
        private Vector2 dragOffset;

        private GUISkin skin;
        private GUIStyle title, tabOn, tabOff, header, label, dim, value, pillOn, pillOff, button, keyButton, keyWait, badge, status, card, caption;
        private GUIStyle captionRight;
        private Texture2D tAccent, tAccentDim, tOff, tCard, tLine, tPanel, tHover, tBg;

        /// <summary>Bind currently waiting for a key press, or null.</summary>
        public Bind Capturing;

        public int Tab
        {
            get => tab;
            set => tab = Mathf.Clamp(value, 0, Tabs.Length - 1);
        }

        private readonly Plugin plugin;

        public Menu(Plugin plugin)
        {
            this.plugin = plugin;
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        private static Texture2D Tex(Color c)
        {
            Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static GUIStyle Flat(GUIStyle from, Texture2D normal, Texture2D hover, Color text, int size, TextAnchor anchor, FontStyle font = FontStyle.Normal)
        {
            GUIStyle s = new GUIStyle(from) { fontSize = size, alignment = anchor, fontStyle = font, wordWrap = false, richText = false, border = new RectOffset(0, 0, 0, 0) };
            s.normal.background = normal;
            s.hover.background = hover ?? normal;
            s.active.background = hover ?? normal;
            s.focused.background = normal;
            s.onNormal.background = normal;
            s.onHover.background = hover ?? normal;
            s.onActive.background = hover ?? normal;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = text;
            s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = text;
            return s;
        }

        private void Build()
        {
            tAccent = Tex(Accent);
            tAccentDim = Tex(AccentDim);
            tOff = Tex(Off);
            tCard = Tex(Card);
            tLine = Tex(Line);
            tPanel = Tex(Panel);
            tHover = Tex(Hex(0x2C3137));
            tBg = Tex(Bg);

            GUIStyle baseLabel = GUI.skin.label, baseButton = GUI.skin.button;
            title = Flat(baseLabel, null, null, TextCol, 14, TextAnchor.MiddleLeft, FontStyle.Bold);
            tabOn = Flat(baseButton, tCard, tCard, Accent, 13, TextAnchor.MiddleLeft, FontStyle.Bold);
            tabOff = Flat(baseButton, tPanel, tHover, Dim, 13, TextAnchor.MiddleLeft);
            tabOn.padding = tabOff.padding = new RectOffset(16, 6, 0, 0);
            tabOn.margin = tabOff.margin = new RectOffset(0, 0, 0, 2);
            header = Flat(baseLabel, null, null, Accent, 13, TextAnchor.MiddleLeft, FontStyle.Bold);
            header.margin = new RectOffset(0, 0, 2, 4);
            label = Flat(baseLabel, null, null, TextCol, 13, TextAnchor.MiddleLeft);
            dim = Flat(baseLabel, null, null, Dim, 12, TextAnchor.UpperLeft);
            dim.wordWrap = true;
            value = Flat(baseLabel, null, null, TextCol, 13, TextAnchor.MiddleLeft, FontStyle.Bold);
            pillOn = Flat(baseButton, tAccent, tAccent, Hex(0x0E1A13), 12, TextAnchor.MiddleCenter, FontStyle.Bold);
            pillOff = Flat(baseButton, tOff, tHover, Dim, 12, TextAnchor.MiddleCenter, FontStyle.Bold);
            button = Flat(baseButton, tOff, tAccentDim, TextCol, 13, TextAnchor.MiddleCenter);
            keyButton = Flat(baseButton, tOff, tHover, TextCol, 13, TextAnchor.MiddleCenter, FontStyle.Bold);
            keyWait = Flat(baseButton, tAccentDim, tAccentDim, TextCol, 12, TextAnchor.MiddleCenter);
            badge = Flat(baseLabel, null, null, TextCol, 12, TextAnchor.MiddleLeft, FontStyle.Bold);
            status = Flat(baseLabel, null, null, TextCol, 13, TextAnchor.UpperLeft);
            caption = Flat(baseLabel, null, null, Dim, 11, TextAnchor.MiddleLeft, FontStyle.Bold);
            caption.padding = new RectOffset(0, 0, 0, 0);
            status.padding = new RectOffset(0, 0, 0, 0);
            status.wordWrap = true;
            card = new GUIStyle { padding = new RectOffset(12, 12, 8, 10), margin = new RectOffset(0, 6, 0, 8) };
            card.normal.background = tCard;

            skin = ScriptableObject.CreateInstance<GUISkin>();
            skin.hideFlags = HideFlags.HideAndDontSave;
            skin.label = label;
            skin.button = button;
            skin.horizontalSlider = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = 6f, margin = new RectOffset(4, 4, 10, 8), border = new RectOffset(0, 0, 0, 0) };
            skin.horizontalSlider.normal.background = tLine;
            skin.horizontalSliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedHeight = 16f, fixedWidth = 10f, border = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, -5, 0) };
            skin.horizontalSliderThumb.normal.background = skin.horizontalSliderThumb.hover.background = skin.horizontalSliderThumb.active.background = tAccent;
            skin.verticalScrollbar = new GUIStyle(GUI.skin.verticalScrollbar) { fixedWidth = 8f, border = new RectOffset(0, 0, 0, 0), margin = new RectOffset(2, 0, 0, 0) };
            skin.verticalScrollbar.normal.background = tPanel;
            skin.verticalScrollbarThumb = new GUIStyle(GUI.skin.verticalScrollbarThumb) { fixedWidth = 8f, border = new RectOffset(0, 0, 0, 0) };
            skin.verticalScrollbarThumb.normal.background = skin.verticalScrollbarThumb.hover.background = skin.verticalScrollbarThumb.active.background = tOff;
            skin.verticalScrollbarUpButton = GUIStyle.none;
            skin.verticalScrollbarDownButton = GUIStyle.none;
            skin.horizontalScrollbar = GUIStyle.none;
            skin.horizontalScrollbarThumb = GUIStyle.none;
            skin.horizontalScrollbarLeftButton = GUIStyle.none;
            skin.horizontalScrollbarRightButton = GUIStyle.none;
            skin.scrollView = GUIStyle.none;
            skin.box = card;
        }

        public void Draw()
        {
            if (skin == null || tBg == null)
            {
                Build();
            }
            Matrix4x4 oldMatrix = GUI.matrix;
            GUISkin oldSkin = GUI.skin;
            float s = Visuals.Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            GUI.skin = skin;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.backgroundColor = Color.white;

            float maxX = Screen.width / s - 80f, maxY = Screen.height / s - 40f;
            window.x = Mathf.Clamp(window.x, -Width + 120f, Mathf.Max(0f, maxX));
            window.y = Mathf.Clamp(window.y, 0f, Mathf.Max(0f, maxY));
            HandleDrag();

            GUI.DrawTexture(new Rect(window.x - 1f, window.y - 1f, Width + 2f, Height + 2f), tLine);
            GUI.DrawTexture(window, tBg);
            GUI.DrawTexture(new Rect(window.x, window.y, Width, TitleHeight), tPanel);
            GUI.DrawTexture(new Rect(window.x, window.y + TitleHeight - 2f, Width, 2f), tAccentDim);
            GUI.Label(new Rect(window.x + 14f, window.y, 300f, TitleHeight - 2f), "SBG MOD MENU", title);
            GUI.Label(new Rect(window.x + Width - 210f, window.y, 196f, TitleHeight - 2f), "v" + Plugin.Version + "   " + Role(), captionRight ?? (captionRight = new GUIStyle(caption) { alignment = TextAnchor.MiddleRight }));

            // Sidebar
            Rect side = new Rect(window.x, window.y + TitleHeight, Side, Height - TitleHeight - StatusHeight);
            GUI.DrawTexture(side, tPanel);
            GUILayout.BeginArea(new Rect(side.x, side.y + 8f, side.width, side.height - 8f));
            for (int i = 0; i < Tabs.Length; i++)
            {
                bool on = tab == i;
                Rect r = GUILayoutUtility.GetRect(Side, 34f);
                if (GUI.Button(r, Tabs[i], on ? tabOn : tabOff))
                {
                    tab = i;
                    Capturing = null;
                }
                if (on)
                {
                    GUI.DrawTexture(new Rect(r.x, r.y, 3f, r.height), tAccent);
                }
            }
            GUILayout.EndArea();

            // Content
            Rect content = new Rect(window.x + Side + 12f, window.y + TitleHeight + 10f, Width - Side - 18f, Height - TitleHeight - StatusHeight - 16f);
            GUILayout.BeginArea(content);
            scroll[tab] = GUILayout.BeginScrollView(scroll[tab], false, true);
            ModConfig c = Plugin.Cfg;
            switch (tab)
            {
                case 0: HoleTab(c); break;
                case 1: BallTab(c); break;
                case 2: AimTab(c); break;
                case 3: WeaponTab(c); break;
                case 4: SpeedTab(c); break;
                case 5: RunTab(c); break;
                case 6: VisualsTab(c); break;
                default: SettingsTab(c); break;
            }
            GUILayout.Space(6f);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Status panel
            Rect st = new Rect(window.x, window.y + Height - StatusHeight, Width, StatusHeight);
            GUI.DrawTexture(st, tPanel);
            GUI.DrawTexture(new Rect(st.x, st.y, Width, 1f), tLine);
            Color kc = Visuals.KindColor(Plugin.StatusKind);
            Visuals.Rect(new Rect(st.x, st.y + 1f, 4f, StatusHeight - 1f), Plugin.StatusText == null ? Off : kc);
            GUI.Label(new Rect(st.x + 14f, st.y + 6f, 200f, 20f), "LAST ACTION", caption);
            status.normal.textColor = Plugin.StatusText == null ? Dim : kc;
            GUI.Label(new Rect(st.x + 14f, st.y + 26f, Width - 28f, StatusHeight - 28f), Plugin.StatusText ?? "Nothing yet. Actions and their real outcome appear here.", status);

            GUI.skin = oldSkin;
            GUI.matrix = oldMatrix;
        }

        private static string Role() => Game.IsHost ? "host" : Game.Golfer != null ? "client" : "not in a match";

        private void HandleDrag()
        {
            Event e = Event.current;
            Rect bar = new Rect(window.x, window.y, Width, TitleHeight);
            if (e.type == EventType.MouseDown && e.button == 0 && bar.Contains(e.mousePosition))
            {
                dragging = true;
                dragOffset = e.mousePosition - new Vector2(window.x, window.y);
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                dragging = false;
            }
            else if (dragging && e.type == EventType.MouseDrag)
            {
                window.x = e.mousePosition.x - dragOffset.x;
                window.y = e.mousePosition.y - dragOffset.y;
                e.Use();
            }
        }

        // ---------------------------------------------------------------- widgets

        private void Begin(string titleText)
        {
            GUILayout.BeginVertical(card);
            GUILayout.Label(titleText.ToUpperInvariant(), header);
        }

        private static void End() => GUILayout.EndVertical();

        private void Toggle(ConfigEntry<bool> e, string text = null)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26f));
            GUILayout.Label(text ?? e.Definition.Key, label, GUILayout.Height(24f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(e.Value ? "ON" : "OFF", e.Value ? pillOn : pillOff, GUILayout.Width(58f), GUILayout.Height(22f)))
            {
                e.Value = !e.Value;
            }
            GUILayout.EndHorizontal();
        }

        private void Slider(ConfigEntry<float> e, float min, float max, string format, string unit = "", float step = 0f, string text = null)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26f));
            GUILayout.Label(text ?? e.Definition.Key, label, GUILayout.Width(LabelWidth), GUILayout.Height(24f));
            float v = GUILayout.HorizontalSlider(e.Value, min, max, GUILayout.Width(ControlWidth));
            if (step > 0f)
            {
                v = Mathf.Round(v / step) * step;
            }
            GUILayout.Space(8f);
            GUILayout.Label(e.Value.ToString(format) + unit, value, GUILayout.Height(24f));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(v - e.Value) > 1e-4f)
            {
                e.Value = Mathf.Clamp(v, min, max);
            }
        }

        private void IntSlider(ConfigEntry<int> e, int min, int max, string text = null)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26f));
            GUILayout.Label(text ?? e.Definition.Key, label, GUILayout.Width(LabelWidth), GUILayout.Height(24f));
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(e.Value, min, max, GUILayout.Width(ControlWidth)));
            GUILayout.Space(8f);
            GUILayout.Label(e.Value.ToString(), value, GUILayout.Height(24f));
            GUILayout.EndHorizontal();
            if (v != e.Value)
            {
                e.Value = v;
            }
        }

        private void Key(Bind b, string text = null)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            GUILayout.Label(text ?? b.Label, label, GUILayout.Width(LabelWidth), GUILayout.Height(24f));
            bool waiting = Capturing == b;
            if (GUILayout.Button(waiting ? "press a key  (Esc cancel, Del clear)" : b.Text, waiting ? keyWait : keyButton, GUILayout.Width(waiting ? 250f : 110f), GUILayout.Height(24f)))
            {
                Capturing = waiting ? null : b;
            }
            GUILayout.EndHorizontal();
        }

        private void Note(string text) => GUILayout.Label(text, dim);

        private void Row(string name, string text, Color? colour = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, dim, GUILayout.Width(LabelWidth - 40f));
            Color old = value.normal.textColor;
            value.normal.textColor = colour ?? TextCol;
            value.wordWrap = true;
            GUILayout.Label(text, value);
            value.wordWrap = false;
            value.normal.textColor = old;
            GUILayout.EndHorizontal();
        }

        /// <summary>state: 0 unavailable (amber), 1 available (green), 2 success (green), 3 failed (red), 4 neutral.</summary>
        private void Badge(int state, string text)
        {
            Color c = state == 0 ? Amber : state == 3 ? Red : state == 4 ? Dim : Accent;
            string tag = state == 0 ? "UNAVAILABLE" : state == 1 ? "AVAILABLE" : state == 2 ? "CONFIRMED" : state == 3 ? "FAILED" : "INFO";
            GUILayout.BeginHorizontal();
            Rect dot = GUILayoutUtility.GetRect(10f, 18f, GUILayout.Width(10f));
            Visuals.Rect(new Rect(dot.x, dot.y + 5f, 8f, 8f), c);
            badge.normal.textColor = c;
            GUILayout.Label(tag, badge, GUILayout.Width(96f));
            Color old = dim.normal.textColor;
            GUILayout.Label(text ?? "", dim);
            dim.normal.textColor = old;
            GUILayout.EndHorizontal();
        }

        private bool Button(string text, float width = 150f) => GUILayout.Button(text, button, GUILayout.Width(width), GUILayout.Height(26f));

        private static string V(Vector3 v) => "(" + v.x.ToString("0.0") + ", " + v.y.ToString("0.0") + ", " + v.z.ToString("0.0") + ")";

        // ---------------------------------------------------------------- tabs

        private void HoleTab(ModConfig c)
        {
            HoleAssist h = plugin.Hole;
            Begin("Auto Hole");
            Toggle(c.AutoHole);
            Key(c.AutoHoleKey);
            IntSlider(c.AutoHoleRetries, 0, 5, "Extra attempts after a miss");
            Slider(c.AutoHoleMaxPitch, 15f, 80f, "0", " deg", 5f);
            Toggle(c.AllowOvercharge, "Allow overcharge power (above 100%)");
            Toggle(c.AdaptShots, "Adapt from observed flights");
            string why = null;
            bool ok = Game.Golfer != null && Game.BallUsable(out why) && Game.Hole != null && Game.SwingsAllowed;
            if (Game.Golfer == null)
            {
                why = "not in a match";
            }
            else if (why == null && Game.Hole == null)
            {
                why = "no active hole";
            }
            else if (why == null && !Game.SwingsAllowed)
            {
                why = "waiting for tee-off (" + Game.State + ")";
            }
            Badge(ok ? 1 : 0, ok ? "ready" : why);
            GUILayout.BeginHorizontal();
            if (Button("Fire Auto Hole"))
            {
                h.Trigger();
            }
            GUILayout.EndHorizontal();
            End();

            Begin("Preview (prediction only)");
            GolfBall ball = Game.Ball;
            if (ball != null && HoleAssist.TryGetCup(out HoleAssist.CupInfo cup))
            {
                Row("Ball to cup", Vector3.Distance(Game.BallPos(ball), cup.center).ToString("0.0") + " m   (cup opening " + (cup.openRadius * 2f).ToString("0.00") + " m" + (cup.measured ? ", measured" : ", estimated") + ")");
            }
            else
            {
                Row("Ball to cup", "unavailable");
            }
            if (h.HasPreview && !h.Busy)
            {
                Trajectory.Shot p = h.Preview;
                Row("Planned shot", (p.power * 100f).ToString("0.0") + "% power at " + p.pitch.ToString("0") + " deg");
                Row("Predicted landing", V(p.path.end));
                Row("Predicted result", p.reachesCup ? "drops in" : "misses by " + p.predictedMiss.ToString("0.0") + " m (" + p.note + ")", p.reachesCup ? Accent : Amber);
            }
            else
            {
                Row("Planned shot", h.Busy ? "shot in progress" : "none (ball must be at rest)");
            }
            Row("Learned correction", "power x" + h.SpeedScale.ToString("0.000") + ", aim " + h.YawOffset.ToString("+0.00;-0.00") + " deg");
            Toggle(c.HolePreview, "Draw the planned path in the world");
            End();

            Begin("Last execution (what actually happened)");
            HoleAssist.Report r = h.Last;
            if (r.attempts == 0)
            {
                Note("No Auto Hole shot has been fired yet.");
            }
            else
            {
                Badge(r.holed ? 2 : r.running ? 4 : 3, r.holed ? "hole completed, reported by the game" : r.running ? r.stage : "hole NOT completed");
                Row("Attempts used", r.attempts + " of " + (c.AutoHoleRetries.Value + 1));
                Row("Shot", (r.power * 100f).ToString("0.0") + "% at " + r.pitch.ToString("0") + " deg from " + r.startDistance.ToString("0.0") + " m");
                Row("Predicted", (r.predictedDrop ? "drop in" : "miss by " + r.predictedMiss.ToString("0.0") + " m") + ", landing " + V(r.predictedLanding));
                Row("Actual landing", r.hasLanding ? V(r.actualLanding) + ", " + r.landingError.ToString("0.00") + " m from prediction" : (r.holed ? "went straight in" : "not observed yet"));
                Row("Resting position", r.hasRest ? V(r.rest) + ", " + r.restDistance.ToString("0.00") + " m from cup" : (r.holed ? "in the cup" : "still moving"));
                Row("Stage", r.stage, r.holed ? Accent : TextCol);
            }
            End();

            Begin("Native homing shot");
            Badge(0, "cannot target the cup");
            Note("The game's homing (release above 100% while looking at a target) only locks onto objects with a LockOnTarget component: players, golf carts and practice dummies. The cup has none, and the server steers a homing ball toward a target's body, not toward a point. It is therefore not offered as an Auto Hole strategy.");
            End();

            Begin("Cup Snap (experimental)");
            Toggle(c.CupSnap);
            Key(c.CupSnapKey);
            Note(Game.IsHost ? "Mode: host. The ball is placed in the cup on the server."
                : Game.HostCheatsEnabled ? "Mode: host has Console Commands on. Uses the game's own finishHole command (adds a penalty stroke)."
                : "Mode: client. Your player hops to the cup, the native ball restart drops the ball in, and you are returned. Shares the server's 3 s restart cooldown.");
            if (Button("Run Cup Snap"))
            {
                h.CupSnap(plugin.Balls);
            }
            End();
        }

        private void BallTab(ModConfig c)
        {
            Begin("Ball Teleport");
            Toggle(c.BallTeleport);
            Key(c.TeleportKey);
            Slider(c.TeleportHeight, 0.25f, 3f, "0.00", " m", 0.05f);
            Note("Places your own ball on the ground under the crosshair. As a client your player is moved there for a moment, because the server only places a ball at its owner.");
            End();

            Begin("Ball Recall");
            Toggle(c.BallRecall);
            Key(c.RecallKey);
            Note("Uses the game's own restart-ball command; the server puts the ball beside you.");
            if (Button("Recall now"))
            {
                plugin.Balls.Recall();
            }
            End();

            Begin("Safety and status");
            Toggle(c.SafeGroundCheck, "Safe ground check (both tools)");
            bool ok = plugin.Balls.ReadyForPlacement(out string why);
            Badge(ok ? 1 : 0, ok ? "ready" : why);
            GolfBall ball = Game.Ball;
            if (ball != null && Game.Player != null)
            {
                Row("My ball", Vector3.Distance(Game.Player.transform.position, Game.BallPos(ball)).ToString("0.0") + " m away" + (ball.IsHidden ? ", hidden" : "") + (ball.IsStationary ? ", at rest" : ", moving"));
            }
            End();

            Begin("Ball Finder");
            Toggle(c.BallFinder, "Direction arrow (works off-screen)");
            Toggle(c.ShowBallDistance, "Distance to my ball");
            End();
        }

        private void AimTab(ModConfig c)
        {
            Begin("Aim Assist");
            Toggle(c.AimAssist);
            Key(c.AimKey, "Hold key");
            Slider(c.AimFov, 2f, 180f, "0", " deg", 1f);
            Slider(c.AimRange, 5f, 300f, "0", " m", 5f);
            Slider(c.AimStrength, 0f, 1f, "0.00", "", 0.05f);
            Slider(c.AimSmooth, 0f, 0.6f, "0.00", " s", 0.01f);
            End();

            Begin("Targeting");
            Toggle(c.AimIndicator);
            Toggle(c.AimLineOfSight);
            Toggle(c.AimLead);
            Toggle(c.AimDummies);
            End();

            Begin("Weapon compatibility");
            Badge(plugin.Aim.WeaponMode != AimAssist.Mode.Unsupported ? 1 : 0, plugin.Aim.WeaponStatus);
            if (plugin.Aim.HasTarget)
            {
                Row("Current target", plugin.Aim.TargetName, Accent);
            }
            Note("Assisted: Dueling Pistol, Elephant Gun, Railgun (hitscan), Rocket Launcher (lead), Freeze Bomb (arc + lead).\nNot assisted: Orbital Laser, Thunderstorm, Airhorn, Flash Camera, Landmine and non-weapon items.\nPaused while this menu is open.");
            End();
        }

        private void WeaponTab(ModConfig c)
        {
            bool host = Game.IsHost, inMatch = Game.Golfer != null;
            Begin("Uses and ammunition");
            Toggle(c.InfiniteAmmo, "Infinite Ammo (firearms)");
            Toggle(c.InfiniteUses, "Infinite Weapon Uses (other items)");
            Badge(!inMatch ? 4 : host ? 1 : 0, Tweaks.UsesStatus());
            Row("Uses kept this session", Tweaks.UsesKept.ToString());
            Note("Firearms: Dueling Pistol, Elephant Gun, Rocket Launcher, Railgun, Freeze Bomb.\nOther items: Coffee, Airhorn, Spring Boots, Golf Cart, Landmine, Electromagnet, Orbital Laser, Rocket Driver, Smoke Bomb, Flash Camera, Thunderstorm, Jumbo Burger.");
            Note("How the game works: every slot has a single 'remaining uses' counter. There is no separate ammunition or reload. The host's server owns that counter and pre-approves a one-time code for each use it granted; a use without a valid code is rejected and flagged. So these two options can only take effect when you are the host. As a client they do nothing, by design.");
            End();

            Begin("Cooldown");
            Toggle(c.NoCooldown, "No Weapon Cooldown");
            Badge(1, "Elephant Gun: " + (Tweaks.BaseElephantCooldown >= 0f ? Tweaks.BaseElephantCooldown.ToString("0.00") + " s between shots normally" : "value not loaded yet"));
            Note("The Elephant Gun's shot cooldown is the only weapon cooldown in the game, and it is checked on your own client, so this works as a client too. The server still limits hit messages to about two per second.");
            End();

            Begin("Inventory");
            PlayerInventory inv = Game.Inventory;
            if (inv == null)
            {
                Note("Not in a match.");
            }
            else
            {
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    InventorySlot slot = inv.GetEffectiveSlot(i);
                    if (slot.itemType == ItemType.None)
                    {
                        Row("Slot " + (i + 1), "empty", Dim);
                    }
                    else
                    {
                        bool kept = Tweaks.KeepsUses(slot.itemType);
                        Row("Slot " + (i + 1), slot.itemType + "  x" + slot.remainingUses + (kept ? "   (kept)" : ""), kept ? Accent : TextCol);
                    }
                }
            }
            End();
        }

        private void SpeedTab(ModConfig c)
        {
            Begin("Player Speed");
            Toggle(c.SpeedEnabled, "Speed Control");
            Slider(c.SpeedMultiplier, 0.5f, 5f, "0.0", "x", 0.1f);
            Key(c.SpeedKey);
            bool on = c.SpeedEnabled.Value;
            Badge(on ? 1 : 4, on ? "active: movement x" + Tweaks.SpeedMultiplier.ToString("0.0") : "off: normal game speed");
            Row("Current speed", Game.Movement != null ? Tweaks.CurrentSpeed().ToString("0.0") + " m/s" : "not in a match");
            Row("Top speed seen", Tweaks.PeakSpeed.ToString("0.0") + " m/s at x" + Tweaks.PeakFactor.ToString("0.0") + "   (normal run speed " + Tweaks.BaseRunSpeed.ToString("0.0") + " m/s)");
            Note("Scales your own character's movement through the game's built-in move-speed factor. Your player is simulated on your own client, so this works without being host. It does not touch ball physics, shot power, camera sensitivity or projectiles. The value is set each frame, never multiplied, so it cannot build up, and it returns to 1.0 when switched off.");
            Note("If you keep Left Alt as the aim key, avoid pressing F4 while holding it: Alt+F4 closes the game.");
            End();
        }

        private void RunTab(ModConfig c)
        {
            Speedrun r = plugin.Run;
            Begin("Speedrun");
            Toggle(c.Speedrun);
            Toggle(c.SpeedrunAutoStart, "Auto start when a hole becomes playable");
            Toggle(c.ShowHud, "Show HUD");
            Key(c.ResetRunKey);
            Row("Run time (local clock)", Speedrun.Format(r.RunTime) + (r.Running ? "" : "   stopped"));
            Row("Hole time (local clock)", Speedrun.Format(r.HoleTime));
            if (Speedrun.TryGetGameData(out int hole, out int par, out int strokes, out int course))
            {
                Row("From the game", "hole " + hole + ", par " + par + ", strokes " + strokes + ", course strokes " + course);
            }
            else
            {
                Row("From the game", "unavailable outside a match", Dim);
            }
            GUILayout.BeginHorizontal();
            if (Button(r.Running ? "Stop" : "Start", 100f))
            {
                if (r.Running)
                {
                    r.Stop();
                }
                else
                {
                    r.StartNow();
                }
            }
            if (Button("Reset run", 100f))
            {
                r.Reset();
            }
            GUILayout.EndHorizontal();
            End();

            Begin("Splits (time local, strokes from the game)");
            if (r.Splits.Count == 0)
            {
                Note("No holes completed in this run yet.");
            }
            foreach (Speedrun.Split s in r.Splits)
            {
                Row("Hole " + s.hole, Speedrun.Format(s.holeTime) + "   total " + Speedrun.Format(s.runTime) + "   " + (s.strokes >= 0 ? s.strokes + " strokes (par " + s.par + ")" : "strokes n/a"));
            }
            End();

            Begin("Quick actions");
            GUILayout.BeginHorizontal();
            if (Button("Auto Hole", 120f))
            {
                plugin.Hole.Trigger();
            }
            if (Button("Cup Snap", 120f))
            {
                plugin.Hole.CupSnap(plugin.Balls);
            }
            if (Button("Recall ball", 120f))
            {
                plugin.Balls.Recall();
            }
            GUILayout.EndHorizontal();
            Note("Auto Hole " + c.AutoHoleKey.Text + "   Cup Snap " + c.CupSnapKey.Text + "   Teleport " + c.TeleportKey.Text + "   Recall " + c.RecallKey.Text + "   Speed " + c.SpeedKey.Text);
            End();
        }

        private void VisualsTab(ModConfig c)
        {
            Begin("Shot aids");
            Toggle(c.ShowTrajectory, "Trajectory preview while aiming a swing");
            Toggle(c.ShowLanding, "Estimated landing marker");
            Toggle(c.HolePreview, "Auto Hole planned path");
            Note("Previews model the airborne part only (gravity, drag, wind, obstacles). Bounce and roll after landing are not predicted.");
            End();

            Begin("Markers");
            Toggle(c.ShowCup, "Cup location indicator");
            Toggle(c.ShowCupDistance, "Distance from ball to cup");
            Toggle(c.BallFinder, "Ball direction indicator");
            Toggle(c.ShowBallDistance, "Distance to my ball");
            Toggle(c.AimIndicator, "Aim assist target marker");
            End();

            Begin("HUD");
            Toggle(c.ShowHud, "Speedrun HUD");
            End();
        }

        private void SettingsTab(ModConfig c)
        {
            Begin("Interface");
            Key(c.MenuKey);
            Slider(c.UiScale, 0.75f, 2f, "0.00", "x", 0.05f);
            Note("The interface also scales automatically with screen height above 1080p.");
            End();

            Begin("Keybindings");
            foreach (Bind b in c.Binds)
            {
                if (b != c.MenuKey)
                {
                    Key(b);
                }
            }
            Note("Click a key, then press a keyboard key or mouse button 1-4.");
            End();

            Begin("Configuration");
            GUILayout.BeginHorizontal();
            if (Button("Save config", 130f))
            {
                c.File.Save();
                Plugin.Say("Config saved to " + c.File.ConfigFilePath, Kind.Ok);
            }
            if (Button("Reload config", 130f))
            {
                c.File.Reload();
                Plugin.Say("Config reloaded from disk", Kind.Ok);
            }
            GUILayout.EndHorizontal();
            Note("Settings are also saved automatically whenever they change.\n" + c.File.ConfigFilePath);
            Row("Role", Role());
            Row("Host Console Commands rule", Game.HostCheatsEnabled ? "on" : "off");
            End();

            Begin("Self-test (host, driving range)");
            Note("Drops your ball at 12 spots around the cup and runs Auto Hole at each, then writes the real results to BepInEx/LogOutput.log. Takes about three minutes; leave the controls alone while the red bar is showing.");
            GUILayout.BeginHorizontal();
            if (Button("Short range 2-12 m", 170f))
            {
                plugin.Dev.StartTrials(plugin, 12, 2f, 12f);
            }
            if (Button("Long range 15-140 m", 170f))
            {
                plugin.Dev.StartTrials(plugin, 12, 15f, 140f);
            }
            GUILayout.EndHorizontal();
            End();
        }
    }
}
