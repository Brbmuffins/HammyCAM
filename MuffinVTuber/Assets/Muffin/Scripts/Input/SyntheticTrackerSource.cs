using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// A pretend face tracker for the self-test: updates at a tracker's rate with random noise like a webcam
    /// tracker's, holds still, then turns the head and leans in one sudden step. Run through the real
    /// avatar, it shows how long the avatar takes to follow (lag) and how much it shakes while you hold still.
    /// </summary>
    public sealed class SyntheticTrackerSource : HeadPoseSource
    {
        public float rate = 22f;
        public float noiseDegrees = 1.5f;
        public float noiseLean = 0.02f;
        public float stepDegrees = 30f;
        public float stepLean = 0.6f;
        public float stepAt = 2.5f;
        [Tooltip("Smooth like the webcam source does (once per update); off = raw, as before that existed.")]
        public bool usePoseFilter = true;

        public override string Label => "Synthetic tracker (self-test)";
        public override bool PreFiltered => usePoseFilter;

        readonly PoseFilter filter = new PoseFilter();
        System.Random random = new System.Random(1);
        float startedAt, nextUpdate, lastUpdate;
        HeadPose current = HeadPose.Rest;

        public void Begin(int seed)
        {
            random = new System.Random(seed);
            filter.Reset();
            startedAt = nextUpdate = lastUpdate = Time.unscaledTime;
            current = HeadPose.Rest;
        }

        float Gauss()
        {
            double u = 1.0 - random.NextDouble(), v = random.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v));
        }

        public override bool TryGetPose(out HeadPose pose)
        {
            float now = Time.unscaledTime;
            if (now >= nextUpdate)
            {
                bool stepped = now - startedAt >= stepAt;
                var degrees = new Vector3(Gauss() * noiseDegrees, (stepped ? stepDegrees : 0f) + Gauss() * noiseDegrees, Gauss() * noiseDegrees * 0.5f);
                var lean = new Vector3((stepped ? stepLean : 0f) + Gauss() * noiseLean, Gauss() * noiseLean, Gauss() * noiseLean);
                if (usePoseFilter) filter.Filter(ref lean, ref degrees, now - lastUpdate);
                lastUpdate = now;
                current = HeadPose.Rest;
                current.Offset = lean;
                current.Rotation = Quaternion.Euler(degrees);
                nextUpdate += 1f / rate;
                if (nextUpdate < now) nextUpdate = now + 1f / rate;
            }
            pose = current;
            return true;
        }
    }
}
