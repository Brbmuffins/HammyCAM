using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Development input until the webcam tracker is wired in: the mouse plays your head. Left/right
    /// leans, up stretches, down scrunches; Space opens the mouth, B blinks. "Simulate webcam" (J key)
    /// imitates a real tracker (30 updates a second plus jitter), so the smoothing can be judged honestly.
    /// </summary>
    public sealed class MouseHeadPoseSource : HeadPoseSource
    {
        [Tooltip("Imitate a webcam tracker: only update trackerRate times a second, with jitter.")]
        public bool simulateWebcam = true;
        [Range(5f, 60f)] public float trackerRate = 30f;
        [Range(0f, 0.15f)] public float jitter = 0.04f;

        HeadPose held = HeadPose.Rest;
        float nextSample;

        public override string Label => simulateWebcam
            ? $"Mouse, simulating a {trackerRate:0} Hz webcam with jitter (J to toggle)"
            : "Mouse, clean (J to simulate a webcam)";

        public override bool TryGetPose(out HeadPose pose)
        {
            Vector2 m = Keys.MouseFromCentre();
            var live = new HeadPose
            {
                Offset = new Vector3(m.x, m.y, 0f),
                // Turn toward the side you lean, look up when you stretch, tilt with the lean.
                Rotation = Quaternion.Euler(m.y * 15f, -m.x * 25f, -m.x * 10f),
                MouthOpen = Keys.Held(MuffinKey.Space) ? 1f : 0f,
                BlinkLeft = Keys.Held(MuffinKey.B) ? 1f : 0f,
                BlinkRight = Keys.Held(MuffinKey.B) ? 1f : 0f,
            };
            if (!simulateWebcam)
            {
                pose = live;
                return true;
            }
            if (Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + 1f / trackerRate;
                live.Offset += Random.insideUnitSphere * jitter;
                live.Rotation *= Quaternion.Euler(Random.insideUnitSphere * (jitter * 60f));
                held = live;
            }
            // Mouth and blinks stay instant: only the pose is sampled like a tracker.
            held.MouthOpen = live.MouthOpen;
            held.BlinkLeft = live.BlinkLeft;
            held.BlinkRight = live.BlinkRight;
            pose = held;
            return true;
        }
    }
}
