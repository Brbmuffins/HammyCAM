using System.Collections;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The base of the code-built "gag" avatars (props-service/src/procedural: the feather pillow, the volcano, the
    /// piñata...). Each has one big moment, set off by the face (mouth, pucker, a long blink), by how it moves
    /// (MotionMeter), and by the chaos set: !shake is Burst, the face slam is OnSlam, !whack is Whack.
    ///
    /// Points on the model (a crater, a nozzle) are given in the avatar's own space at rest, as props-service's
    /// build printed them (anchors), and ride along with the bone at their height (Pin / Pos).
    /// What this keeps for every gag: the motion meter, the face's state (mouth just opened, held open, a pucker,
    /// eyes shut), and helpers for throwing things onto the desk, drops and streams of liquid, soft puffs, drips
    /// down the front and things that stick to the camera glass.
    /// </summary>
    [RequireComponent(typeof(BendRig))]
    public abstract class GagAvatar : MonoBehaviour, IMessy
    {
        protected BendRig rig;
        protected readonly MotionMeter meter = new MotionMeter();
        /// <summary>The model's size at rest (MeshAvatar's mesh bounds: centred, standing on y = 0).</summary>
        protected Bounds body;
        protected PuffPool puffs;

        // The face, worked out every frame before Tick.
        protected float mouth, mouthOpenFor, puckerFor, eyesShutFor;
        protected bool mouthJustOpened, mouthJustClosed;
        bool mouthWasOpen;
        float nextBitSound;
        bool started;

        public abstract string MessName { get; }
        /// <summary>!shake: a good dose of this avatar's mess, now.</summary>
        public abstract void Burst();
        /// <summary>The face slam (R, !slam): it lands face-first on the desk.</summary>
        public virtual void OnSlam() => Burst();
        /// <summary>!whack: a bat to the head. Most just bonk and wobble; the piñata counts the hits.</summary>
        public virtual void Whack(float strength)
        {
            rig.Bonk(0.25f * strength);
            rig.KickTip(new Vector3(Random.value < 0.5f ? -1.5f : 1.5f, 0.2f, 0f) * strength);
        }

        /// <summary>Self-test: sets off its signature moment directly, as the face (or a hard shake) would.</summary>
        public virtual void TestMoment() => Burst();
        /// <summary>Self-test: a few words on its state (how many feathers, gumballs left...).</summary>
        public virtual string TestReport => "";

        /// <summary>How open the mouth must be to count as open.</summary>
        protected virtual float MouthThreshold => 0.42f;
        /// <summary>The part the motion meter watches (the head bone) and how far out its rim is.</summary>
        protected virtual float MeterRadius => body.extents.x * 0.8f;

        protected virtual void Awake()
        {
            rig = GetComponent<BendRig>();
            if (TryGetComponent(out MeshAvatar avatar) && avatar.mesh) body = avatar.mesh.bounds;
            else body = new Bounds(new Vector3(0f, 0.575f, 0f), new Vector3(0.6f, 1.15f, 0.4f));
        }

        protected virtual void OnEnable()
        {
            meter.Reset();
            mouthWasOpen = false;
            mouthOpenFor = puckerFor = eyesShutFor = 0f;
        }

        protected virtual void OnDisable() => puffs?.Clear();

        protected virtual void OnDestroy() => puffs?.Dispose();

        /// <summary>Once the rig is built (MeshAvatar builds it in Awake): pin anchors, make parts.</summary>
        protected virtual void Begin() { }

        /// <summary>Every frame, after the meter and the face were read.</summary>
        protected abstract void Tick(float dt);

        void Update()
        {
            if (!rig || !rig.HeadBone) return;
            if (!started)
            {
                started = true;
                Material smoke = Chaos ? Chaos.smoke : null;
                if (smoke) puffs = new PuffPool(smoke, 160, name + " puffs");
                Begin();
            }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            meter.Update(rig.HeadBone, MeterRadius, dt);

            HeadPose pose = rig.Current;
            mouth = pose.MouthOpen;
            bool open = mouth > MouthThreshold;
            mouthJustOpened = open && !mouthWasOpen;
            mouthJustClosed = !open && mouthWasOpen;
            mouthWasOpen = open;
            mouthOpenFor = open ? mouthOpenFor + dt : 0f;
            // A pucker ("ooh", blowing out a candle): lips pushed forward, mouth only a little open.
            bool pucker = pose.MouthWide < -0.4f && mouth < 0.55f;
            puckerFor = pucker ? puckerFor + dt : 0f;
            bool shut = pose.BlinkLeft > 0.7f && pose.BlinkRight > 0.7f;
            eyesShutFor = shut ? eyesShutFor + dt : 0f;

            Tick(dt);
            if (puffs != null && Camera.main) puffs.Update(Camera.main, dt);
        }

        // ---- Where things are ----------------------------------------------------------------------------

        /// <summary>A point on the model (rest, avatar space) pinned to the bone that carries it.</summary>
        protected struct Anchor
        {
            public Transform Bone;
            public Vector3 Local;
            public bool Valid => Bone;
        }

        protected Anchor Pin(Vector3 rest)
        {
            Transform bone = rig.BoneAt(rest.y, out Vector3 at);
            return new Anchor { Bone = bone, Local = rest - at };
        }

        protected Vector3 Pos(Anchor a) => a.Bone ? a.Bone.TransformPoint(a.Local) : transform.position;
        protected Vector3 Dir(Anchor a, Vector3 restDirection) => a.Bone ? a.Bone.TransformDirection(restDirection) : transform.TransformDirection(restDirection);

        /// <summary>Which way the avatar's face points (it turns with your head): straight at the camera is (0, 0, -1).</summary>
        protected Vector3 Facing => rig.HeadBone ? rig.HeadBone.TransformDirection(Vector3.back) : Vector3.back;

        /// <summary>How far it's turned to a side, -1 (screen left) .. 1 (screen right), exaggerated so small turns count.</summary>
        protected float TurnedSide => Mathf.Clamp(Facing.x * 2.5f, -1f, 1f);

        /// <summary>A desk point in front of the avatar (x across, z toward the camera from its front).</summary>
        protected Vector3 InFront(float x, float z) => transform.position + new Vector3(x, 0f, body.min.z - z);

        protected static ChaosDirector Chaos => ChaosDirector.Instance;
        protected bool IsActive => Chaos && Chaos.library && Chaos.library.ActiveRig == rig;

        // ---- Things off the avatar -----------------------------------------------------------------------

        protected Material Tint(Color c) => Mess.Instance ? Mess.Instance.BlobMaterial(c) : null;

        protected Transform Item(PrimitiveType type, Color colour, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            Material m = Tint(colour);
            if (m) r.sharedMaterial = m;
            go.transform.localScale = scale;
            return go.transform;
        }

        protected Transform Lump(Color colour, Vector3 scale)
        {
            Transform t = Item(PrimitiveType.Sphere, colour, scale);
            if (Mess.Instance) t.GetComponent<MeshFilter>().sharedMesh = Mess.Instance.RandomLump;
            t.rotation = Random.rotation;
            return t;
        }

        /// <summary>Hands a piece to the desk (it falls, lands, gets pushed about and swept up by K).</summary>
        protected void ToDesk(Transform piece, Vector3 velocity, float radius, float halfThickness, bool ball = false, Color? trail = null, bool upright = false)
        {
            if (DeskDebris.Instance) DeskDebris.Instance.AddPiece(piece, velocity, radius, halfThickness, disposable: true, ball: ball, trail: trail, upright: upright);
            else Destroy(piece.gameObject, 5f);
        }

        protected void Drop(Vector3 at, Vector3 velocity, Color colour, float size, float puddle)
        {
            if (Mess.Instance) Mess.Instance.Drop(at, velocity, colour, size, puddle);
        }

        /// <summary>A drip runs down the avatar's front from `up` (share of its height), `across` (-1..1) from its middle.</summary>
        protected void RunDown(float up, float across, Color colour, float size, float speed = 0.09f)
        {
            if (IsActive) Chaos.DripDown(up, Mathf.Clamp(across, -0.75f, 0.75f), colour, size, speed);
        }

        /// <summary>
        /// A stream (a gush, a geyser): drops in quick succession along an aim asked every frame, with a slow wobble,
        /// strongest at the start. speed and size are ranges (start → end of the stream for speed).
        /// </summary>
        protected IEnumerator Stream(System.Func<Vector3> from, System.Func<Vector3> aim, Color colour, float seconds, float perSecond,
            Vector2 speed, Vector2 size, float puddle)
        {
            float owed = 0f, seed = Random.value * 10f;
            for (float t = 0f; t < seconds && Mess.Instance; t += Time.deltaTime)
            {
                owed += perSecond * Time.deltaTime;
                float pace = Mathf.Lerp(speed.y, speed.x, t / seconds);
                Vector3 wobble = new Vector3(Mathf.PerlinNoise(seed, t * 3f) - 0.5f, 0f, Mathf.PerlinNoise(seed + 3f, t * 3f) - 0.5f) * 0.2f;
                Vector3 direction = (aim() + wobble).normalized;
                for (; owed >= 1f; owed -= 1f)
                    Mess.Instance.Drop(from() + Random.insideUnitSphere * 0.008f, direction * pace * Random.Range(0.92f, 1.08f) + meter.Velocity * 0.5f,
                        colour, Random.Range(size.x, size.y), puddle);
                yield return null;
            }
        }

        /// <summary>Which way something leaves: the way the avatar's moving, else out to a side and a bit toward the camera.</summary>
        protected Vector3 SpillDirection(float still = 0.04f)
        {
            Vector3 flat = new Vector3(meter.Velocity.x, 0f, meter.Velocity.z);
            Vector3 dir = flat.sqrMagnitude > still ? flat.normalized : new Vector3(Random.value < 0.5f ? -1f : 1f, 0f, -Random.Range(0.3f, 0.9f)).normalized;
            if (dir.z > 0.2f) dir.z = -dir.z; // onto the desk, in view
            return dir.normalized;
        }

        /// <summary>A random direction out to the sides and toward the camera (things land on the desk in view).</summary>
        protected static Vector3 Outward()
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            if (d.y > 0.3f) d.y = -d.y;
            return new Vector3(d.x, 0f, d.y);
        }

        /// <summary>A sound for a bit coming off, at most a dozen a second.</summary>
        protected void BitSound(string sound, float volume)
        {
            if (Time.time < nextBitSound) return;
            nextBitSound = Time.time + 0.08f;
            Sfx.Play(sound, volume, 0.3f);
        }

        protected IEnumerator After(float seconds, System.Action then)
        {
            yield return new WaitForSeconds(seconds);
            then();
        }

        /// <summary>Starts a coroutine only while this avatar is out (switched-off avatars can't run them).</summary>
        protected void Run(IEnumerator routine)
        {
            if (isActiveAndEnabled) StartCoroutine(routine);
        }

        /// <summary>Turns a camera-facing quad to the camera, upright (flames) or fully (puffs).</summary>
        protected static void FaceCamera(Transform t, bool upright)
        {
            Camera cam = Camera.main;
            if (!cam) return;
            Vector3 to = t.position - cam.transform.position;
            if (upright) to.y = 0f;
            if (to.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(to);
        }
    }
}
