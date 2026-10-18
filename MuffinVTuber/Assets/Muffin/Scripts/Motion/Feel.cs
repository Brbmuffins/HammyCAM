using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The one "feel" setting the streamer picks ([ smoother, ] snappier) for webcam tracking: how hard
    /// tracker noise is smoothed and how quickly the face turn follows.
    ///
    /// Each level is the quickest setup found for its amount of shake, in a simulation of the whole head-turn
    /// chain (a 22 Hz tracker with 1.5° of noise, this filter, the step smoothing and the turn spring). Time to
    /// cover 90% of a sudden turn / shake while holding still: 1: 0.13 s / 0.07, 2: 0.12 / 0.08,
    /// 3: 0.10 / 0.13, 4: 0.10 / 0.16, 5: 0.09 / 0.19. For comparison, the earlier "heavy" setup was
    /// 0.17 s / 0.13 and the first "jumpy" one 0.07 s / 0.47.
    /// </summary>
    public static class Feel
    {
        public const int Levels = 5;
        public const int Default = 3;
        public static int Level = Default;

        // Per level: turn beta (per °/s), step smoothing cutoff (Hz) and beta (per rad/s), turn spring (Hz),
        // and a multiplier on the body's other springs (stem, twist, stretch: their jiggle stays).
        static readonly (float angleBeta, float stepCutoff, float stepBeta, float turn, float springs)[] Rows =
        {
            (0.05f, 4f, 1f, 8f, 0.9f),
            (0.05f, 4f, 1f, 10f, 0.95f),
            (0.08f, 4f, 1f, 12f, 1.05f),
            (0.08f, 8f, 2f, 10f, 1.15f),
            (0.08f, 8f, 2f, 12f, 1.25f),
        };

        static (float angleBeta, float stepCutoff, float stepBeta, float turn, float springs) Row => Rows[Mathf.Clamp(Level, 1, Levels) - 1];

        /// <summary>Smoothing while you hold still (Hz): low, so the avatar holds still too.</summary>
        public const float MinCutoff = 0.5f;
        /// <summary>How quickly the filter notices you've started to move (Hz).</summary>
        public const float DerivativeCutoff = 2f;
        /// <summary>How far a real move opens the filter (per degree per second): high, so moves come through at once.</summary>
        public static float AngleBeta => Row.angleBeta;
        /// <summary>The same for leaning (per lean unit per second; a full lean is about 50x a degree's share).</summary>
        public static float OffsetBeta => Row.angleBeta * 50f;
        /// <summary>The avatar's light pass that evens out the tracker's 20-30 Hz steps.</summary>
        public static float StepCutoff => Row.stepCutoff;
        public static float StepBeta => Row.stepBeta;
        /// <summary>The face-turn spring's speed (Hz) for webcam tracking.</summary>
        public static float TurnFrequency => Row.turn;
        /// <summary>Multiplier on the other springs' speeds.</summary>
        public static float SpringSpeed => Row.springs;
        /// <summary>MediaPipe's adaptive smoothing speed: 1 is Face Object Stream's own (follow at 30-72 a second).</summary>
        public static float AdaptiveScale => new[] { 0.4f, 0.65f, 1f, 1.3f, 1.6f }[Mathf.Clamp(Level, 1, Levels) - 1];

        public static string Name => Level switch
        {
            1 => "smoothest",
            2 => "smooth",
            3 => "balanced",
            4 => "snappy",
            _ => "snappiest",
        };
    }

    /// <summary>
    /// Smooths a face tracker's head pose once per tracker update, using the real time between updates.
    /// A 20-30 Hz tracker seen from a 60 fps frame loop arrives in steps, and a filter run every frame
    /// reads each step as a burst of speed and lets noise through. Run here, the One Euro filter sees the
    /// true speed: calm when you hold still, wide open (no lag) when you really move.
    /// </summary>
    public sealed class PoseFilter
    {
        readonly OneEuroVector3 offset = new OneEuroVector3(Feel.MinCutoff, 1f, Feel.DerivativeCutoff);
        readonly OneEuroVector3 angles = new OneEuroVector3(Feel.MinCutoff, 0.05f, Feel.DerivativeCutoff);
        Vector3 smoothOffset, smoothAngles;
        bool primed;

        /// <summary>
        /// Face Object Stream's adaptive smoothing instead (for MediaPipe, whose points barely jitter): follow at
        /// 30 a second for small moves, up to 72 for big ones (beyond 0.22 rad), frame-rate independent.
        /// </summary>
        public bool Adaptive { get; set; }

        public void Reset()
        {
            offset.Reset();
            angles.Reset();
            primed = false;
        }

        /// <summary>Offset in lean units, angles in degrees (pitch, yaw, roll), dt in seconds since the last update.</summary>
        public void Filter(ref Vector3 leanOffset, ref Vector3 degrees, float dt)
        {
            if (Adaptive)
            {
                dt = Mathf.Min(Mathf.Max(dt, 0.001f), 0.1f);
                if (!primed)
                {
                    smoothOffset = leanOffset;
                    smoothAngles = degrees;
                    primed = true;
                    return;
                }
                Vector3 d = degrees - smoothAngles;
                float movement = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z)) * Mathf.Deg2Rad;
                movement = Mathf.Max(movement, (leanOffset - smoothOffset).magnitude * 0.3f);
                float rate = Mathf.Lerp(30f, 72f, Mathf.Clamp01(movement / 0.22f)) * Feel.AdaptiveScale;
                float blend = 1f - Mathf.Exp(-rate * dt);
                smoothAngles += d * blend;
                smoothOffset += (leanOffset - smoothOffset) * blend;
                degrees = smoothAngles;
                leanOffset = smoothOffset;
                return;
            }
            dt = Mathf.Clamp(dt, 0.005f, 0.25f);
            offset.Beta = Feel.OffsetBeta;
            angles.Beta = Feel.AngleBeta;
            leanOffset = offset.Filter(leanOffset, dt);
            degrees = angles.Filter(degrees, dt);
        }
    }
}
