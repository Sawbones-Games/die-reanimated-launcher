/*
 * Does this mod cost the player a smooth frame?
 *
 * Anything a player installs must not cost them frames, and "it felt fine" is not a measurement.
 *
 * So the mod measures itself, and the measurement has to survive two constraints:
 *
 *   1. It cannot lean on external tooling, which would distort what we are measuring. Frames are timed
 *      in-process and the numbers go to the mod's own log file.
 *   2. It cannot itself allocate, or it would manufacture the problem it exists to detect. Per frame this
 *      writes two numbers into arrays allocated once at startup, and nothing else. The report — sorting,
 *      formatting, one log line — happens once per window and is deliberately charged to the NEXT window
 *      rather than hidden, which is why the counters reset after it.
 *
 * OFF by default, and off means ABSENT: Bootstrap does not create the component unless `frameStats` names
 * a window length, so a shipped build with the default config runs not one instruction of this.
 *
 * ⭐ The control is the SAME CLIENT with `enabled=0`, not a different build. Frame time depends on the
 * scene, the window size, what else is running and what the GPU driver feels like; comparing two processes
 * launched ten minutes apart compares those things too. This component is therefore created BEFORE the
 * enabled check in Bootstrap, so one config flip gives a baseline taken on the same machine in the same
 * session. docs/reference/gamepad-patch.md records the numbers this produced.
 *
 * WHY GC DROPS, and not "allocated bytes": the runtime these clients use has no per-thread allocation
 * counter to read. What it does have is a heap size that GROWS as you allocate and
 * FALLS when it collects, so sampling GC.GetTotalMemory(false) every frame and summing the two directions
 * separately gives both halves of the story: how fast the mod fills the heap, and how often the collector
 * has to empty it. "Twice a second" is a drop RATE, and that is the number this was built to print.
 */

using System;
using UnityEngine;

namespace DieGamepad
{
    class FrameStats : MonoBehaviour
    {
        /// <summary>Frames held per window. 4096 is ~27 s at 150 fps, comfortably past the longest window
        /// anyone should need; a window that overruns it stops sampling and says so rather than growing.</summary>
        const int Capacity = 4096;

        /// <summary>A frame this long is a STUTTER a player would see, whatever the average says. 50 ms is
        /// three missed frames at 60 Hz — past the point where it reads as a hitch rather than as a slightly
        /// slow frame.</summary>
        const float StutterMs = 50f;

        readonly float[] _ms = new float[Capacity];
        int _n;
        bool _full;

        float _windowStart;
        float _lastFrame;

        long _lastHeap;
        long _grew;          // bytes the heap gained across the window
        long _shrank;        // bytes it gave back — i.e. what the collector reclaimed
        int _drops;          // times it gave any back: collections, near enough
        bool _heapReadable = true;

        void Start()
        {
            Log.Line("frame stats ON, reporting every " + Cfg.FrameStats + "s"
                     + " (this is a diagnostic; leave frameStats=0 in a shipped config)");
            Reset();
        }

        void LateUpdate()
        {
            float now = Time.realtimeSinceStartup;

            // The first frame after a report, and the first after startup, measure from a reset point and
            // would otherwise charge this window for everything before it.
            if (_lastFrame > 0f)
            {
                float ms = (now - _lastFrame) * 1000f;
                if (_n < Capacity) _ms[_n++] = ms; else _full = true;
            }
            _lastFrame = now;

            SampleHeap();

            if (now - _windowStart >= Cfg.FrameStats) Report(now - _windowStart);
        }

        void SampleHeap()
        {
            if (!_heapReadable) return;
            long heap;
            try { heap = GC.GetTotalMemory(false); }
            catch (Exception) { _heapReadable = false; return; }

            if (_lastHeap > 0L)
            {
                long d = heap - _lastHeap;
                if (d > 0L) _grew += d;
                else if (d < 0L) { _shrank -= d; _drops++; }
            }
            _lastHeap = heap;
        }

        void Report(float seconds)
        {
            if (_n < 2) { Reset(); return; }

            // In place, so no array is allocated to sort. The order is not wanted again — the window ends
            // here.
            Array.Sort(_ms, 0, _n);

            int stutters = 0;
            for (int i = _n - 1; i >= 0 && _ms[i] > StutterMs; i--) stutters++;

            float p50 = Pct(0.50f), p99 = Pct(0.99f);
            // "Long" relative to this machine rather than to an absolute: a frame that takes twice the
            // median is a hitch on a 144 Hz box as much as on a 60 Hz one.
            float longMs = p50 * 2f;
            int longs = 0;
            for (int i = _n - 1; i >= 0 && _ms[i] > longMs; i--) longs++;

            Log.Line(string.Format(
                "frames: {0} in {1:0.0}s ({2:0} fps) | p50 {3:0.00}ms p99 {4:0.00}ms max {5:0.00}ms"
                + " | >2xp50 {6} ({7:0.0}/s) | >{8:0}ms {9} ({10:0.0}/s) | heap +{11:0} KB/s, gc {12} ({13:0.0}/s, {14:0} KB/s){15}",
                _n, seconds, _n / seconds,
                p50, p99, _ms[_n - 1],
                longs, longs / seconds,
                StutterMs, stutters, stutters / seconds,
                _heapReadable ? _grew / 1024f / seconds : 0f,
                _heapReadable ? _drops.ToString() : "?",
                _drops / seconds,
                _heapReadable ? _shrank / 1024f / seconds : 0f,
                _full ? " | TRUNCATED, lower frameStats" : ""));

            Reset();
        }

        /// <summary>Percentile of the sorted window. Nearest-rank, which for a few thousand samples is the
        /// same answer as interpolating and is harder to get wrong.</summary>
        float Pct(float q)
        {
            int i = (int)(q * (_n - 1));
            if (i < 0) i = 0; else if (i >= _n) i = _n - 1;
            return _ms[i];
        }

        void Reset()
        {
            _n = 0;
            _full = false;
            _windowStart = Time.realtimeSinceStartup;
            _lastFrame = 0f;          // do not charge the next window for the report itself
            _grew = 0L; _shrank = 0L; _drops = 0; _lastHeap = 0L;
        }
    }
}
