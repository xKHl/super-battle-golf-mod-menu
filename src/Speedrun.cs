using System.Collections.Generic;
using System.Diagnostics;

namespace SbgMod
{
    /// <summary>
    /// Local run timer (System.Diagnostics.Stopwatch) plus hole/stroke data read from the game.
    /// "LOCAL" values are measured here; "GAME" values come from CourseManager and are server-synced.
    /// </summary>
    public sealed class Speedrun
    {
        public struct Split
        {
            public int hole;          // GAME: CourseManager.CurrentHoleCourseIndex + 1
            public double holeTime;   // LOCAL
            public double runTime;    // LOCAL
            public int strokes;       // GAME: PlayerState.matchStrokes, -1 if unavailable
            public int par;           // GAME
        }

        private readonly Stopwatch run = new Stopwatch();
        private double holeStart;
        private MatchState lastState = MatchState.Initializing;
        private int lastHoleIndex = -1;
        private bool holeScored;

        public readonly List<Split> Splits = new List<Split>();
        public double FlashUntil;

        public bool Running => run.IsRunning;
        public double RunTime => run.Elapsed.TotalSeconds;
        public double HoleTime => holeScored ? 0 : RunTime - holeStart;
        public bool HoleScored => holeScored;

        public void Reset()
        {
            run.Reset();
            Splits.Clear();
            holeStart = 0;
            holeScored = false;
            Plugin.Say("Speedrun: run reset", Kind.Info);
        }

        public void StartNow()
        {
            if (!run.IsRunning)
            {
                run.Start();
            }
            holeStart = RunTime;
            holeScored = false;
        }

        public void Stop() => run.Stop();

        public static bool TryGetGameData(out int hole, out int par, out int matchStrokes, out int courseStrokes)
        {
            hole = par = matchStrokes = courseStrokes = -1;
            try
            {
                if (GameManager.LocalPlayerInfo == null)
                {
                    return false;
                }
                hole = CourseManager.CurrentHoleCourseIndex + 1;
                par = CourseManager.GetCurrentHolePar();
                CourseManager.PlayerState state = CourseManager.GetLocalPlayerState();
                matchStrokes = state.matchStrokes;
                courseStrokes = state.courseStrokes;
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        public void Update()
        {
            if (!Plugin.Cfg.Speedrun.Value)
            {
                return;
            }
            MatchState state = Game.State;
            int holeIndex = Game.Try(() => CourseManager.CurrentHoleCourseIndex, -1);
            bool inMatch = Game.Golfer != null;
            if (inMatch && (state != lastState || holeIndex != lastHoleIndex))
            {
                // A hole becomes playable when the game leaves TeeOff for Ongoing.
                if (state == MatchState.Ongoing && (lastState < MatchState.Ongoing || holeIndex != lastHoleIndex))
                {
                    if (Plugin.Cfg.SpeedrunAutoStart.Value)
                    {
                        StartNow();
                    }
                    else if (run.IsRunning)
                    {
                        holeStart = RunTime;
                        holeScored = false;
                    }
                }
                lastState = state;
                lastHoleIndex = holeIndex;
            }
        }

        /// <summary>Called from the game's LocalPlayerMatchResolutionChanged event when it reports Scored.</summary>
        public void OnScored()
        {
            FlashUntil = UnityEngine.Time.unscaledTimeAsDouble + 4.0;
            if (!Plugin.Cfg.Speedrun.Value || !run.IsRunning || holeScored)
            {
                return;
            }
            holeScored = true;
            TryGetGameData(out int hole, out int par, out int strokes, out _);
            Splits.Add(new Split { hole = hole, holeTime = RunTime - holeStart, runTime = RunTime, strokes = strokes, par = par });
            if (Splits.Count > 18)
            {
                Splits.RemoveAt(0);
            }
        }

        public static string Format(double seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }
            int minutes = (int)(seconds / 60.0);
            double rest = seconds - minutes * 60.0;
            return minutes.ToString("0") + ":" + rest.ToString("00.00");
        }
    }
}
