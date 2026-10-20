using System.Collections;
using System.Collections.Generic;
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
    ///
    /// Everything it does is the streamer's to control (its page in the menu, AvatarSettings): whether the face sets it
    /// off at all (for talking to chat without a mess every sentence), how much mess it makes, and each avatar's own
    /// extras (DescribeMore; read them with On).
    /// </summary>
    [RequireComponent(typeof(BendRig))]
    public abstract class GagAvatar : MonoBehaviour, IMessy, IAvatarFeatures
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

        // Its settings, re-read only when something changed in the menu.
        string settingsId;
        int seenVersion = -1;
        readonly Dictionary<string, bool> toggles = new Dictionary<string, bool>();
        Dictionary<string, bool> defaults;
        float amount = 1f;
        bool faceOn = true;

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

        // ---- Settings ------------------------------------------------------------------------------------

        /// <summary>Does it react to the face at all (mouth, pucker, blinks)? Most do; the menu then offers the switch.</summary>
        protected virtual bool UsesFace => true;

        /// <summary>Its own extras for the menu: what else it does that can be switched off.</summary>
        protected virtual void DescribeMore(List<AvatarFeature> into) { }

        public void Describe(List<AvatarFeature> into)
        {
            if (UsesFace) into.Add(new AvatarFeature { Key = AvatarSettings.Face, Label = "Your face sets off its mess (mouth, pucker, blinks)" });
            into.Add(new AvatarFeature { Key = AvatarSettings.Amount, Label = "How much of its mess it makes", Slider = true });
            DescribeMore(into);
        }

        string SettingsId => settingsId ??= AvatarSettings.IdOf(this);

        void Refresh()
        {
            if (seenVersion == AvatarSettings.Version) return;
            seenVersion = AvatarSettings.Version;
            toggles.Clear();
            amount = Mathf.Clamp01(AvatarSettings.Value(SettingsId, AvatarSettings.Amount, 1f));
            faceOn = AvatarSettings.FaceMess && AvatarSettings.On(SettingsId, AvatarSettings.Face, true);
        }

        /// <summary>One of its switches (DescribeMore), as the streamer left it.</summary>
        protected bool On(string key)
        {
            Refresh();
            if (toggles.TryGetValue(key, out bool on)) return on;
            if (defaults == null)
            {
                defaults = new Dictionary<string, bool>();
                var list = new List<AvatarFeature>();
                DescribeMore(list);
                foreach (AvatarFeature f in list) defaults[f.Key] = f.Default;
            }
            on = AvatarSettings.On(SettingsId, key, !defaults.TryGetValue(key, out bool d) || d);
            toggles[key] = on;
            return on;
        }

        /// <summary>How much of its mess it makes, 0..1 (the menu's slider).</summary>
        protected float Amount
        {
            get
            {
                Refresh();
                return amount;
            }
        }

        /// <summary>Whether this bit of mess happens, at the mess amount (all of it at 1, none at 0).</summary>
        protected bool Keep() => Amount >= 0.999f || Random.value < Amount;

        /// <summary>A count scaled by the mess amount (at least one if any were asked for and the amount isn't zero).</summary>
        protected int Scaled(int count) => count <= 0 || Amount <= 0.001f ? 0 : Mathf.Max(1, Mathf.RoundToInt(count * Amount));

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

        protected virtual void OnDisable()
        {
            puffs?.Clear();
            ShowAllParts();
        }

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

            Refresh();
            HeadPose pose = rig.Current;
            // Face triggers switched off: it behaves as if you kept a straight face (the face itself still moves).
            if (!faceOn)
            {
                pose.MouthOpen = 0f;
                pose.MouthWide = 0f;
                pose.BlinkLeft = pose.BlinkRight = 0f;
            }
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
            if (!Keep())
            {
                Destroy(piece.gameObject);
                return;
            }
            if (DeskDebris.Instance) DeskDebris.Instance.AddPiece(piece, velocity, radius, halfThickness, disposable: true, ball: ball, trail: trail, upright: upright);
            else Destroy(piece.gameObject, 5f);
        }

        protected void Drop(Vector3 at, Vector3 velocity, Color colour, float size, float puddle)
        {
            if (Mess.Instance && Keep()) Mess.Instance.Drop(at, velocity, colour, size, puddle);
        }

        /// <summary>A drip runs down the avatar's front from `up` (share of its height), `across` (-1..1) from its middle.</summary>
        protected void RunDown(float up, float across, Color colour, float size, float speed = 0.09f)
        {
            if (IsActive && Keep()) Chaos.DripDown(up, Mathf.Clamp(across, -0.75f, 0.75f), colour, size, speed);
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
                owed += perSecond * Amount * Time.deltaTime;
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

        // ---- Parts of the model ---------------------------------------------------------------------------

        SkinnedMeshRenderer skinCache;
        Material[] originalMaterials;
        static Material invisible;

        /// <summary>
        /// Hides (or brings back) the parts of the model drawn with a material whose name contains part (the
        /// snowman's "carrot", the avocado's "pit"): it flies off as a thrown copy and comes back later.
        /// </summary>
        protected void HidePart(string part, bool hidden)
        {
            if (!skinCache && !TryGetComponent(out skinCache)) skinCache = GetComponentInChildren<SkinnedMeshRenderer>();
            if (!skinCache) return;
            originalMaterials ??= skinCache.sharedMaterials;
            if (!invisible && Mess.Instance && Mess.Instance.stainMaterial)
            {
                invisible = new Material(Mess.Instance.stainMaterial) { name = "Hidden part" };
                invisible.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            }
            Material[] mats = skinCache.sharedMaterials;
            for (int i = 0; i < mats.Length && i < originalMaterials.Length; i++)
                if (originalMaterials[i] && originalMaterials[i].name.ToLowerInvariant().Contains(part))
                    mats[i] = hidden && invisible ? invisible : originalMaterials[i];
            skinCache.sharedMaterials = mats;
        }

        /// <summary>Brings back everything HidePart hid (switching avatars puts it back together).</summary>
        protected void ShowAllParts()
        {
            if (skinCache && originalMaterials != null) skinCache.sharedMaterials = originalMaterials;
        }

        /// <summary>A material that shines in any light (unlit; URP's Unlit ships with the face stickers).</summary>
        protected static Material Glow(Color colour, string label = "Glow")
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (!unlit) return null;
            var m = new Material(unlit) { name = label, enableInstancing = true };
            m.SetColor("_BaseColor", colour);
            return m;
        }

        /// <summary>Wipes the desk round a point: stains shrink away, bits and sprinkles go.</summary>
        protected static void WipeDesk(Vector3 centre, float radius)
        {
            if (Mess.Instance) Mess.Instance.CleanArea(centre, radius);
            if (DeskDebris.Instance) DeskDebris.Instance.CleanArea(centre, radius);
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
