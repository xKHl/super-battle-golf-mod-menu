using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SbgMod
{
    /// <summary>A rebindable key or mouse button, stored in the config as text ("F7", "Mouse1", "None").</summary>
    public sealed class Bind
    {
        public readonly string Label;
        public readonly ConfigEntry<string> Entry;
        private Key key = Key.None;
        private int mouse = -1;

        public Bind(ConfigFile cfg, string section, string name, string def, string desc)
        {
            Label = name;
            Entry = cfg.Bind(section, name, def, desc + " (keyboard key name such as F7, or Mouse0-Mouse4, or None)");
            Entry.SettingChanged += (_, __) => Parse();
            Parse();
        }

        public string Text => Entry.Value;

        public void Set(string value) => Entry.Value = value;

        private void Parse()
        {
            key = Key.None;
            mouse = -1;
            string v = (Entry.Value ?? "").Trim();
            if (v.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) && int.TryParse(v.Substring(5), out int m) && m >= 0 && m <= 4)
            {
                mouse = m;
            }
            else if (!Enum.TryParse(v, true, out key))
            {
                key = Key.None;
            }
        }

        private ButtonControl Control()
        {
            if (mouse >= 0)
            {
                return MouseButton(mouse);
            }
            if (key == Key.None || Keyboard.current == null)
            {
                return null;
            }
            return Keyboard.current[key];
        }

        public static ButtonControl MouseButton(int index)
        {
            Mouse m = Mouse.current;
            if (m == null)
            {
                return null;
            }
            switch (index)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.backButton;
                case 4: return m.forwardButton;
                default: return null;
            }
        }

        public bool Down
        {
            get
            {
                ButtonControl c = Control();
                return c != null && c.wasPressedThisFrame;
            }
        }

        public bool Held
        {
            get
            {
                ButtonControl c = Control();
                return c != null && c.isPressed;
            }
        }
    }

    public sealed class ModConfig
    {
        public readonly ConfigFile File;
        public readonly List<Bind> Binds = new List<Bind>();

        // Hole Assist
        public ConfigEntry<bool> AutoHole, CupSnap, HolePreview, AllowOvercharge, AdaptShots;
        public ConfigEntry<int> AutoHoleRetries;
        public ConfigEntry<float> AutoHoleMaxPitch;
        public Bind AutoHoleKey, CupSnapKey;

        // Ball Control
        public ConfigEntry<bool> BallTeleport, BallRecall, BallFinder, SafeGroundCheck;
        public ConfigEntry<float> TeleportHeight;
        public Bind TeleportKey, RecallKey;

        // Aim Assist
        public ConfigEntry<bool> AimAssist, AimIndicator, AimLineOfSight, AimLead, AimDummies;
        public ConfigEntry<float> AimFov, AimRange, AimStrength, AimSmooth;
        public Bind AimKey;

        // Speedrun
        public ConfigEntry<bool> Speedrun, SpeedrunAutoStart;
        public Bind ResetRunKey;

        // Visuals
        public ConfigEntry<bool> ShowTrajectory, ShowLanding, ShowCup, ShowCupDistance, ShowBallDistance, ShowHud;

        // Settings
        public Bind MenuKey;
        public ConfigEntry<float> UiScale;
        public ConfigEntry<bool> DevCommands;

        // Weapon Options / Player Speed
        public ConfigEntry<bool> InfiniteAmmo, InfiniteUses, NoCooldown, SpeedEnabled;
        public ConfigEntry<float> SpeedMultiplier;
        public Bind SpeedKey;

        public ModConfig(ConfigFile file)
        {
            File = file;
            const string H = "1. Hole Assist", B = "2. Ball Control", A = "3. Weapon Aim Assist", S = "4. Speedrun", V = "5. Visuals", G = "6. Settings";

            AutoHole = file.Bind(H, "Auto Hole", true, "Enable the Auto Hole hotkey (solves and fires a native swing at the cup).");
            CupSnap = file.Bind(H, "Cup Snap", false, "Enable the experimental Cup Snap hotkey (drops your ball into the cup).");
            HolePreview = file.Bind(H, "Solution Preview", true, "Draw the solved Auto Hole trajectory.");
            AllowOvercharge = file.Bind(H, "Allow Overcharge Power", true, "Let Auto Hole use the game's overcharge range (power above 100%).");
            AdaptShots = file.Bind(H, "Adapt After Miss", true, "Correct power/aim for the next attempt from the observed ball flight.");
            AutoHoleRetries = file.Bind(H, "Auto Retries", 2, new ConfigDescription("Extra automatic attempts after a miss.", new AcceptableValueRange<int>(0, 5)));
            AutoHoleMaxPitch = file.Bind(H, "Max Launch Pitch", 60f, new ConfigDescription("Steepest launch angle to try (capped to the game's max swing pitch).", new AcceptableValueRange<float>(15f, 80f)));
            AutoHoleKey = Add(new Bind(file, H, "Auto Hole Key", "F9", "Fire Auto Hole."));
            CupSnapKey = Add(new Bind(file, H, "Cup Snap Key", "F10", "Run Cup Snap."));

            BallTeleport = file.Bind(B, "Ball Teleport", true, "Enable teleporting your own ball to the aimed ground point.");
            BallRecall = file.Bind(B, "Ball Recall", true, "Enable recalling your own ball to your player.");
            BallFinder = file.Bind(B, "Ball Finder", true, "Show direction and distance to your own ball.");
            SafeGroundCheck = file.Bind(B, "Safe Ground Check", true, "Refuse placements inside geometry or without ground underneath.");
            TeleportHeight = file.Bind(B, "Teleport Height Offset", 0.3f, new ConfigDescription("Height above the ground hit for the teleported ball (metres).", new AcceptableValueRange<float>(0.25f, 3f)));
            TeleportKey = Add(new Bind(file, B, "Ball Teleport Key", "F7", "Teleport your ball to the aimed ground point."));
            RecallKey = Add(new Bind(file, B, "Ball Recall Key", "F8", "Recall your ball to your player."));

            AimAssist = file.Bind(A, "Aim Assist", false, "Enable weapon aim assist while the aim key is held.");
            AimIndicator = file.Bind(A, "Target Indicator", true, "Draw a marker on the current target.");
            AimLineOfSight = file.Bind(A, "Line Of Sight Check", true, "Only target opponents with a clear line of sight.");
            AimLead = file.Bind(A, "Lead Moving Targets", true, "Account for target velocity and projectile travel time.");
            AimDummies = file.Bind(A, "Include Practice Dummies", true, "Allow driving-range target dummies as targets (for offline testing).");
            AimFov = file.Bind(A, "Field Of View", 25f, new ConfigDescription("Full cone angle in degrees.", new AcceptableValueRange<float>(2f, 180f)));
            AimRange = file.Bind(A, "Max Range", 80f, new ConfigDescription("Maximum target distance in metres (also capped by weapon range).", new AcceptableValueRange<float>(5f, 300f)));
            AimStrength = file.Bind(A, "Strength", 0.8f, new ConfigDescription("0 = off, 1 = full correction.", new AcceptableValueRange<float>(0f, 1f)));
            AimSmooth = file.Bind(A, "Smoothness", 0.08f, new ConfigDescription("Tracking time constant in seconds (0 = snap).", new AcceptableValueRange<float>(0f, 0.6f)));
            AimKey = Add(new Bind(file, A, "Aim Key", "LeftAlt", "Hold to activate aim assist."));

            const string W = "3b. Weapon Options", P = "3c. Player Speed";
            InfiniteAmmo = file.Bind(W, "Infinite Ammo", false, "Firearms (Dueling Pistol, Elephant Gun, Rocket Launcher, Railgun, Freeze Bomb) keep their shots. Host only: the server owns the use counter.");
            InfiniteUses = file.Bind(W, "Infinite Weapon Uses", false, "Other items (Coffee, Airhorn, Landmine, Spring Boots, ...) keep their uses. Host only.");
            NoCooldown = file.Bind(W, "No Weapon Cooldown", false, "Removes the Elephant Gun shot cooldown (the only weapon cooldown in the game).");
            SpeedEnabled = file.Bind(P, "Speed Control", false, "Apply the movement speed multiplier.");
            SpeedMultiplier = file.Bind(P, "Movement Speed Multiplier", 1f, new ConfigDescription("1.0 = normal speed.", new AcceptableValueRange<float>(0.5f, 5f)));
            SpeedKey = Add(new Bind(file, P, "Speed Toggle Key", "F4", "Toggle Speed Control."));

            Speedrun = file.Bind(S, "Speedrun Mode", true, "Enable the speedrun timer and split tracking.");
            SpeedrunAutoStart = file.Bind(S, "Auto Start", true, "Start the run timer when a hole becomes playable.");
            ResetRunKey = Add(new Bind(file, S, "Reset Run Key", "F5", "Reset the run timer and splits."));

            ShowTrajectory = file.Bind(V, "Trajectory Preview", true, "Draw a predicted flight path while you aim a swing.");
            ShowLanding = file.Bind(V, "Landing Marker", true, "Mark the predicted first landing point.");
            ShowCup = file.Bind(V, "Cup Indicator", true, "Mark the cup on screen.");
            ShowCupDistance = file.Bind(V, "Cup Distance", true, "Show distance from your ball to the cup.");
            ShowBallDistance = file.Bind(V, "Ball Distance", true, "Show distance from you to your ball.");
            ShowHud = file.Bind(V, "Speedrun HUD", true, "Show the minimal speedrun HUD.");

            MenuKey = Add(new Bind(file, G, "Menu Key", "F6", "Open or close the mod menu."));
            DevCommands = file.Bind("7. Dev", "Command File", false, "Testing hook: run commands from BepInEx/config/sbgmod.cmd. Leave off for normal play.");
            UiScale = file.Bind(G, "UI Scale", 1f, new ConfigDescription("Menu and HUD scale.", new AcceptableValueRange<float>(0.75f, 2f)));
        }

        private Bind Add(Bind b)
        {
            Binds.Add(b);
            return b;
        }
    }
}
