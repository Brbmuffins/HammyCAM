using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Sugar sprinkles coming off a cupcake's top onto the desk (DeskDebris): a steady trickle all the time,
    /// more whenever it moves at all (turning counts: the rim swings round), and a shower when it's shaken.
    /// </summary>
    [RequireComponent(typeof(BendRig))]
    public sealed class SprinkleShedder : MonoBehaviour, IMessy
    {
        [Tooltip("Sprinkles a second even when it's still.")]
        public float strayPerSecond = 1.2f;
        [Tooltip("Sprinkles a second for each m/s the rim of the top moves (moving or turning).")]
        public float perSpeed = 22f;
        [Tooltip("Shake (m/s² at the top) above which it showers on top of that.")]
        public float threshold = 2f;
        [Tooltip("Sprinkles a second for each m/s² of shake above the threshold.")]
        public float perShake = 5f;
        [Tooltip("Where they come off: a ring this wide around the top's middle (metres), this tall.")]
        public float topRadius = 0.3f;
        public float topHeight = 0.15f;

        BendRig rig;
        readonly MotionMeter meter = new MotionMeter();
        float owed;

        void Awake() => rig = GetComponent<BendRig>();

        void OnEnable() => meter.Reset();

        Transform Top => rig.HasFloppyTip ? rig.FloppyBones[rig.FloppyBones.Length / 2] : rig.HeadBone;

        void Update()
        {
            DeskDebris debris = DeskDebris.Instance;
            Transform top = rig ? Top : null;
            if (!debris || !top) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            meter.Update(top, topRadius, dt);
            owed += (strayPerSecond + meter.Speed * perSpeed + Mathf.Max(0f, meter.Shake - threshold) * perShake) * dt;
            for (int guard = 0; owed >= 1f && guard < 20; guard++)
            {
                owed -= 1f;
                Emit(debris, top.position, meter.Velocity, 1f);
            }
        }

        public string MessName => "sprinkles";

        /// <summary>!shake: a shower of sprinkles.</summary>
        public void Burst() => Burst(70);

        /// <summary>A lot at once (the face slam).</summary>
        public void Burst(int count)
        {
            DeskDebris debris = DeskDebris.Instance;
            Transform top = rig ? Top : null;
            if (!debris || !top) return;
            for (int i = 0; i < count; i++) Emit(debris, top.position, Vector3.zero, 2f);
        }

        void Emit(DeskDebris debris, Vector3 centre, Vector3 velocity, float fling)
        {
            Vector2 around = Random.insideUnitCircle.normalized;
            Vector3 outward = new Vector3(around.x, 0f, around.y);
            Vector3 position = centre + outward * topRadius * Random.Range(0.7f, 1f) + Vector3.up * Random.Range(-0.4f, 0.7f) * topHeight;
            Vector3 v = velocity * 0.9f + outward * Random.Range(0.1f, 0.5f) * fling + Vector3.up * Random.Range(0f, 0.6f) * fling;
            debris.EmitSprinkle(position, v);
        }
    }
}
