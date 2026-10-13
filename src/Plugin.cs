using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SbgMod
{
    public enum Kind { Info, Ok, Warn, Fail }

    [BepInPlugin(Guid, "SBG Mod Menu", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.sbgmod";
        public const string Version = "1.0.0";

        public static Plugin I { get; private set; }
        public static ModConfig Cfg { get; private set; }
        public static bool MenuOpen { get; private set; }

        /// <summary>Large on-screen notice, used while an automated test is driving the game.</summary>
        public static string Banner;

        public static string StatusText { get; private set; }
        public static Kind StatusKind { get; private set; }
        public static float StatusTime { get; private set; }

        public BallControl Balls { get; private set; }
        public HoleAssist Hole { get; private set; }
        public AimAssist Aim { get; private set; }
        public Speedrun Run { get; private set; }

        public DevChannel Dev { get; private set; }

        private Visuals visuals;
        private Menu menu;
        private float lastErrorLog = -10f;
        private Harmony harmony;
        private bool sawUpdate, sawGui;

        public static void Say(string text, Kind kind)
        {
            StatusText = text;
            StatusKind = kind;
            StatusTime = Time.unscaledTime;
            if (I != null)
            {
                I.Log("[" + kind + "] " + text);
            }
        }

        private void Awake()
        {
            I = this;
            Cfg = new ModConfig(Config);
            Balls = new BallControl();
            Hole = new HoleAssist();
            Aim = new AimAssist();
            Run = new Speedrun();
            visuals = new Visuals();
            menu = new Menu(this);
            Dev = new DevChannel(Paths.ConfigPath);
            harmony = Harmony.CreateAndPatchAll(typeof(Plugin), Guid);
            harmony.PatchAll(typeof(Tweaks));
            Logger.LogInfo("SBG Mod Menu " + Version + " loaded. Menu key: " + Cfg.MenuKey.Text);
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            Tweaks.Restore();
            SetMenu(false);
        }

        /// <summary>Number of times the game has told us our own ball entered a cup this session.</summary>
        public static int HoledCount { get; private set; }

        /// <summary>
        /// PlayerGolfer.InformScored runs on every client when a ball enters the cup (GolfBall.RpcInformEnteredHole -> OnEnteredHole).
        /// Unlike the Scored match resolution it also fires in the driving range, so it is the one reliable "holed" signal.
        /// </summary>
        [HarmonyPatch(typeof(PlayerGolfer), nameof(PlayerGolfer.InformScored))]
        [HarmonyPostfix]
        private static void AfterInformScored(PlayerGolfer __instance)
        {
            if (I == null || __instance == null || !__instance.isLocalPlayer)
            {
                return;
            }
            HoledCount++;
            I.Hole.OnScored();
            I.Run.OnScored();
        }

        public void Log(string text)
        {
            Logger.LogInfo(text);
            Dev?.Mirror(text);
        }

        public void ToggleMenu() => SetMenu(!MenuOpen);

        public void ShowTab(int index)
        {
            SetMenu(true);
            menu.Tab = index;
        }

        private void SetMenu(bool open)
        {
            if (MenuOpen == open)
            {
                return;
            }
            MenuOpen = open;
            Log(open ? "Menu opened" : "Menu closed");
            menu.Capturing = null;
            Game.Try(() =>
            {
                CursorManager.SetCursorForceUnlocked(open);
                return true;
            });
        }

        private void Update()
        {
            if (!sawUpdate)
            {
                sawUpdate = true;
                Logger.LogInfo("Update loop running. Keyboard device: " + (Keyboard.current != null ? Keyboard.current.displayName : "none"));
            }
            try
            {
                if (menu.Capturing != null)
                {
                    CaptureBind();
                }
                else
                {
                    Hotkeys();
                }
                Dev.Update(this);
                Tweaks.Update();
                Run.Update();
                Hole.Update();
                Aim.Update();
                visuals.Update();
            }
            catch (Exception e)
            {
                if (Time.unscaledTime - lastErrorLog > 2f)
                {
                    lastErrorLog = Time.unscaledTime;
                    Logger.LogError(e);
                }
            }
        }

        private void Hotkeys()
        {
            if (Cfg.MenuKey.Down)
            {
                SetMenu(!MenuOpen);
            }
            if (Cfg.TeleportKey.Down)
            {
                Balls.TeleportToAim();
            }
            if (Cfg.RecallKey.Down)
            {
                Balls.Recall();
            }
            if (Cfg.AutoHoleKey.Down)
            {
                Hole.Trigger();
            }
            if (Cfg.CupSnapKey.Down)
            {
                Hole.CupSnap(Balls);
            }
            if (Cfg.SpeedKey.Down)
            {
                Cfg.SpeedEnabled.Value = !Cfg.SpeedEnabled.Value;
                Say("Player speed " + (Cfg.SpeedEnabled.Value ? "ON x" + Tweaks.SpeedMultiplier.ToString("0.0") : "OFF (normal)"), Kind.Info);
            }
            if (Cfg.ResetRunKey.Down && Cfg.Speedrun.Value)
            {
                Run.Reset();
            }
        }

        private void CaptureBind()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    menu.Capturing = null;
                    return;
                }
                if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)
                {
                    menu.Capturing.Set("None");
                    menu.Capturing = null;
                    return;
                }
                foreach (KeyControl k in kb.allKeys)
                {
                    if (k != null && k.wasPressedThisFrame)
                    {
                        menu.Capturing.Set(k.keyCode.ToString());
                        menu.Capturing = null;
                        return;
                    }
                }
            }
            for (int i = 1; i <= 4; i++)
            {
                ButtonControl b = Bind.MouseButton(i);
                if (b != null && b.wasPressedThisFrame)
                {
                    menu.Capturing.Set("Mouse" + i);
                    menu.Capturing = null;
                    return;
                }
            }
        }

        private void OnGUI()
        {
            if (!sawGui)
            {
                sawGui = true;
                Logger.LogInfo("OnGUI running.");
            }
            try
            {
                visuals.Draw(Hole, Aim, Run);
                if (MenuOpen)
                {
                    menu.Draw();
                }
            }
            catch (Exception e)
            {
                if (Time.unscaledTime - lastErrorLog > 2f)
                {
                    lastErrorLog = Time.unscaledTime;
                    Logger.LogError(e);
                }
            }
        }
    }
}
