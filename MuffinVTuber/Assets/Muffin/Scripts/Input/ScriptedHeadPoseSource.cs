using UnityEngine;

namespace Muffin
{
    /// <summary>A repeatable head movement for demos and the automated self-test: sways, bobs and talks.</summary>
    public sealed class ScriptedHeadPoseSource : HeadPoseSource
    {
        [Tooltip("Seconds of stillness before moving (the self-test photographs the rest pose first).")]
        public float startDelay;
        [Tooltip("Move the way head-turn webcam control does: turns, nods and tilts (a tilt leans), no body movement.")]
        public bool headTurnsOnly;
        float startTime;

        public override string Label => "Scripted demo movement";

        void OnEnable() => startTime = Time.time;

        public override bool TryGetPose(out HeadPose pose)
        {
            float t = Time.time - startTime - startDelay;
            if (t < 0f)
            {
                pose = HeadPose.Rest;
                return true;
            }
            float ramp = Mathf.Clamp01(t / 0.6f);
            if (headTurnsOnly)
            {
                // Glancing between screens and chatting: turns up to about ±30°, small nods, a tilt now and then.
                float turn = (Mathf.Sin(t * 1.6f) * 25f + Mathf.Sin(t * 3.5f + 1f) * 6f) * ramp;
                float nod = Mathf.Sin(t * 2.4f + 0.5f) * 8f * ramp;
                float tilt = Mathf.Sin(t * 1.1f + 2f) * 10f * ramp;
                pose = new HeadPose
                {
                    Offset = new Vector3(-tilt / 24f + Mathf.Sin(t * 0.8f) * 0.2f * ramp, 0f, 0f), // tilt, plus half of a gentle sway
                    Rotation = Quaternion.Euler(nod, turn, tilt),
                    MouthOpen = Mathf.Clamp01(Mathf.Sin(t * 7f) * 0.7f + 0.2f) * ramp,
                    BlinkLeft = Mathf.Repeat(t, 3.1f) < 0.12f ? 1f : 0f,
                    BlinkRight = Mathf.Repeat(t, 3.1f) < 0.12f ? 1f : 0f,
                };
                return true;
            }
            float sway = Mathf.Sin(t * 1.6f) * 0.95f * ramp;
            float bob = Mathf.Sin(t * 1.1f + 0.8f) * 0.85f * ramp;
            pose = new HeadPose
            {
                Offset = new Vector3(sway, bob, Mathf.Sin(t * 0.7f) * 0.3f * ramp),
                Rotation = Quaternion.Euler(bob * 12f, -sway * 22f, -sway * 9f),
                MouthOpen = Mathf.Clamp01(Mathf.Sin(t * 7f) * 0.7f + 0.2f) * ramp,
                BlinkLeft = Mathf.Repeat(t, 3.1f) < 0.12f ? 1f : 0f,
                BlinkRight = Mathf.Repeat(t, 3.1f) < 0.12f ? 1f : 0f,
            };
            return true;
        }
    }
}
