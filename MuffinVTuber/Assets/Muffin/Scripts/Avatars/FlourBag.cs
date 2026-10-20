using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// A paper sack of flour. Shake it and flour puffs out of the crimped top; shake it harder and the air in the
    /// room fills with a white haze that thickens the more you go on, until at the far end everything whites out.
    /// It then takes a good while to settle (the desk is left dusted white).
    ///
    /// One number does the work: <see cref="Haze"/> (0 clear, 1 total whiteout). It rises with how fast the sack's
    /// top is moving or turning and, much more, how hard it's being shaken (MotionMeter), and the air clears
    /// slowly on its own. What you see follows from it:
    /// - puffs off the sack itself (more the harder it's shaken), drifting out, up and slowly down,
    /// - big soft puffs all over the picture, in front of and behind the avatar (a camera-sized haze), as many as
    ///   the haze calls for,
    /// - above about half haze, a veil right in front of the camera that goes from milky to opaque white.
    /// Everything is camera-facing quads on the shared Muffin/Smoke material, pooled, so a whiteout costs a
    /// bounded number of draws. Premultiplied alpha, so a transparent OBS capture whites out properly too.
    /// </summary>
    [RequireComponent(typeof(BendRig))]
    public sealed class FlourBag : MonoBehaviour, IMessy, IAvatarFeatures
    {
        // ---- The streamer's settings for it (its page in the menu) ----
        int seenVersion = -1;
        float amount = 1f;
        bool whiteout = true;
        string settingsId;

        public void Describe(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = AvatarSettings.Amount, Label = "How much flour it throws about", Slider = true });
            into.Add(new AvatarFeature { Key = "whiteout", Label = "A hard shake whites out the whole picture" });
        }

        void RefreshSettings()
        {
            if (seenVersion == AvatarSettings.Version) return;
            seenVersion = AvatarSettings.Version;
            settingsId ??= AvatarSettings.IdOf(this);
            amount = Mathf.Clamp01(AvatarSettings.Value(settingsId, AvatarSettings.Amount, 1f));
            whiteout = AvatarSettings.On(settingsId, "whiteout", true);
        }

        [Tooltip("Smoke shader material (Muffin/Smoke) the puffs and the veil are drawn with.")]
        public Material smoke;
        [Tooltip("The top's radius and height (metres): where the flour comes off.")]
        public float topRadius = 0.25f, topHeight = 0.15f;
        [Tooltip("How far the crimped top is above the bone things come off from.")]
        public float topLift;
        [Tooltip("Shake (m/s² at the top) above which it starts to build up haze.")]
        public float shakeThreshold = 4f;

        [Header("Haze (0 = clear, 1 = whiteout)")]
        [Tooltip("Haze gained a second for each m/s the top moves (moving or turning).")]
        public float hazePerSpeed = 0.02f;
        [Tooltip("Haze gained a second for each m/s² of shake above the threshold. Raise it for a quicker whiteout.")]
        public float hazePerShake = 0.004f;
        [Tooltip("The air clears this much a second whatever the haze, and this share of the haze on top: thick air hangs about, thin air goes.")]
        public float clearFlat = 0.035f, clearShare = 0.1f; // from the slam's cloud to clear air in about 10-12 s
        [Tooltip("The haze above which the veil in front of the camera starts to show; it is opaque at 1.")]
        [Range(0.2f, 0.95f)] public float veilFrom = 0.45f;
        [Tooltip("The most quads alive at once (sack puffs plus room haze).")]
        public int maxPuffs = 190;

        static readonly Color Flour = new Color(0.96f, 0.95f, 0.92f, 1f);
        static readonly Color FlourDust = new Color(0.97f, 0.96f, 0.93f, 0.6f);

        sealed class Puff
        {
            public Transform t;
            public MeshRenderer r;
            public Vector3 vel;
            public float age, life, size, alpha, seed, drag, fall;
            public bool room, live;
        }

        BendRig rig;
        readonly MotionMeter meter = new MotionMeter();
        readonly List<Puff> pool = new List<Puff>();
        readonly Stack<Puff> free = new Stack<Puff>();
        readonly List<Transform> veils = new List<Transform>();
        readonly List<MeshRenderer> veilRenderers = new List<MeshRenderer>();
        Transform root;
        MaterialPropertyBlock block;
        float bagOwed, dustOwed, nextIdle, nextSound;
        int roomLive;

        /// <summary>How thick the air is: 0 clear, 1 total whiteout.</summary>
        public float Haze { get; private set; }

        public string MessName => "a cloud of flour";

        void Awake()
        {
            rig = GetComponent<BendRig>();
            block = new MaterialPropertyBlock();
            nextIdle = Time.time + Random.Range(5f, 10f);
        }

        void OnEnable()
        {
            meter.Reset();
            Haze = 0f;
        }

        void OnDisable() => Clear();

        void OnDestroy()
        {
            if (root) Destroy(root.gameObject);
        }

        Transform Top => rig.HasFloppyTip ? rig.FloppyBones[rig.FloppyBones.Length / 2] : rig.HeadBone;
        Vector3 TopPoint => Top.position + Top.up * topLift;

        // ---- The pool ---------------------------------------------------------------------------------------

        void EnsureRoot()
        {
            if (root) return;
            root = new GameObject("Flour haze").transform;
            for (int i = 0; i < 3; i++)
            {
                Transform v = NewQuad("Veil");
                veils.Add(v);
                veilRenderers.Add(v.GetComponent<MeshRenderer>());
                v.gameObject.SetActive(false);
            }
        }

        Transform NewQuad(string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root, false);
            MeshRenderer r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (smoke) r.sharedMaterial = smoke;
            return go.transform;
        }

        Puff Take()
        {
            EnsureRoot();
            Puff p;
            if (free.Count > 0) p = free.Pop();
            else if (pool.Count < maxPuffs)
            {
                p = new Puff { t = NewQuad("Puff") };
                p.r = p.t.GetComponent<MeshRenderer>();
                pool.Add(p);
            }
            else return null;
            p.live = true;
            p.age = 0f;
            p.t.gameObject.SetActive(true);
            return p;
        }

        void Release(Puff p)
        {
            if (!p.live) return;
            p.live = false;
            if (p.room) roomLive--;
            if (p.t) p.t.gameObject.SetActive(false);
            free.Push(p);
        }

        /// <summary>Everything gone at once (switching to another avatar).</summary>
        void Clear()
        {
            Haze = 0f;
            roomLive = 0;
            free.Clear();
            foreach (Puff p in pool)
            {
                p.live = false;
                p.room = false;
                if (p.t) p.t.gameObject.SetActive(false);
                free.Push(p);
            }
            foreach (Transform v in veils) if (v) v.gameObject.SetActive(false);
        }

        // ---- Update -----------------------------------------------------------------------------------------

        void Update()
        {
            Camera cam = Camera.main;
            if (!rig || !cam) return;
            Transform top = Top;
            if (!top) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            meter.Update(top, topRadius, dt);
            float speed = meter.Speed, over = Mathf.Max(0f, meter.Shake - shakeThreshold);

            // The haze: what shaking puts in, less what settles.
            RefreshSettings();
            Haze = Mathf.Clamp01(Haze + ((speed * hazePerSpeed + over * hazePerShake) * amount - (clearFlat + Haze * clearShare)) * dt);
            if (!whiteout) Haze = Mathf.Min(Haze, veilFrom - 0.02f);

            // Flour off the sack itself.
            bagOwed += (0.35f + speed * 7f + over * 0.9f) * dt;
            bagOwed = Mathf.Min(bagOwed, 12f);
            for (int guard = 0; bagOwed >= 1f && guard < 8; guard++)
            {
                bagOwed -= 1f;
                SackPuff(false);
            }
            if (over > 20f && Time.time >= nextSound)
            {
                nextSound = Time.time + Random.Range(0.25f, 0.5f);
                Sfx.Play("spore_puff", Mathf.Clamp01(0.2f + over * 0.008f), 0.2f);
            }
            if (Time.time >= nextIdle)
            {
                nextIdle = Time.time + Random.Range(6f, 12f);
                SackPuff(false);
            }

            // Flour settling on the desk, more the thicker the air.
            dustOwed += (0.1f + Haze * 3f) * dt;
            for (int guard = 0; dustOwed >= 1f && guard < 3; guard++)
            {
                dustOwed -= 1f;
                Dust(Random.Range(0.05f, 0.1f) * (1f + Haze));
            }

            // The room's haze: as many big soft puffs across the picture as the haze calls for.
            int target = Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, Haze) * maxPuffs * 0.65f);
            for (int guard = 0; roomLive < target && guard < 4; guard++) RoomPuff(cam);

            UpdatePuffs(cam, dt);
            UpdateVeil(cam);
        }

        void UpdatePuffs(Camera cam, float dt)
        {
            foreach (Puff p in pool)
            {
                if (!p.live) continue;
                p.age += dt;
                float k = p.age / p.life;
                if (k >= 1f)
                {
                    Release(p);
                    continue;
                }
                p.t.position += p.vel * dt;
                p.vel *= Mathf.Exp(-p.drag * dt);
                p.vel.y -= p.fall * dt;
                p.t.localScale = Vector3.one * p.size * Mathf.Lerp(0.5f, 1.6f, Mathf.Sqrt(k));
                p.t.rotation = Quaternion.LookRotation(p.t.position - cam.transform.position);
                // Fades in fast, thins out slowly. The room's haze also thins as the air clears.
                float envelope = Mathf.SmoothStep(0f, 1f, k * 6f) * (1f - Mathf.SmoothStep(0.45f, 1f, k));
                float strength = p.room ? Mathf.Lerp(0.55f, 1f, Haze) : 1f;
                Color c = Flour;
                c.a = p.alpha * envelope * strength;
                block.Clear();
                block.SetColor("_Color", c);
                block.SetFloat("_Seed", p.seed);
                p.r.SetPropertyBlock(block);
            }
        }

        /// <summary>The whiteout: three big soft quads right in front of the camera, opaque together at full haze.</summary>
        void UpdateVeil(Camera cam)
        {
            float amount = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(veilFrom, 1f, Haze));
            for (int i = 0; i < veils.Count; i++)
            {
                Transform v = veils[i];
                if (!v) continue;
                // Each layer carries a share, so together they reach (nearly) solid white.
                float alpha = amount * (i == 0 ? 0.98f : 0.9f);
                bool show = alpha > 0.01f;
                if (v.gameObject.activeSelf != show) v.gameObject.SetActive(show);
                if (!show) continue;
                float distance = cam.nearClipPlane + 0.1f + i * 0.05f;
                float height = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float diagonal = Mathf.Sqrt(height * height * (1f + cam.aspect * cam.aspect));
                v.position = cam.transform.position + cam.transform.forward * distance;
                v.rotation = Quaternion.LookRotation(cam.transform.forward);
                v.localScale = Vector3.one * diagonal * 6.5f; // the puff's solid core covers the whole screen, its soft edge is well outside
                Color c = Flour;
                c.a = alpha;
                block.Clear();
                block.SetColor("_Color", c);
                block.SetFloat("_Seed", 11f + i * 17f);
                veilRenderers[i].SetPropertyBlock(block);
            }
        }

        // ---- Spawning ---------------------------------------------------------------------------------------

        /// <summary>A puff of flour from the sack's top and shoulders; a big one is a whump.</summary>
        void SackPuff(bool big)
        {
            Puff p = Take();
            if (p == null) return;
            Vector2 around = Random.insideUnitCircle.normalized;
            if (around.y > 0.3f) around.y = -around.y; // mostly toward the camera and the sides
            Vector3 outward = new Vector3(around.x, 0f, around.y);
            p.room = false;
            p.t.position = TopPoint + outward * topRadius * Random.Range(0.5f, 1f) + Vector3.down * Random.Range(0f, topHeight * 3f);
            p.vel = meter.Velocity * 0.3f + outward * Random.Range(0.15f, 0.6f) * (big ? 2f : 1f) + Vector3.up * Random.Range(0.1f, 0.5f);
            p.size = Random.Range(0.18f, 0.34f) * (1f + Haze * 0.6f) * (big ? 1.8f : 1f);
            p.life = Random.Range(1.8f, 3.2f);
            p.alpha = Random.Range(0.6f, 0.85f);
            p.seed = Random.Range(0f, 50f);
            p.drag = 1.1f;
            p.fall = 0.12f;
            p.t.localScale = Vector3.one * p.size * 0.5f;
        }

        /// <summary>A big soft puff somewhere in the picture, at any distance from the camera (the ones behind the avatar are hidden by it).</summary>
        void RoomPuff(Camera cam)
        {
            Puff p = Take();
            if (p == null) return;
            float distance = Random.Range(0.8f, 3.4f);
            float height = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            p.room = true;
            roomLive++;
            p.t.position = cam.ViewportToWorldPoint(new Vector3(Random.Range(-0.2f, 1.2f), Random.Range(-0.2f, 1.2f), distance));
            p.vel = Random.insideUnitSphere * 0.06f + Vector3.up * 0.02f;
            p.size = height * Random.Range(0.3f, 0.65f);
            p.life = Random.Range(3f, 5.5f);
            p.alpha = Mathf.Lerp(0.35f, 0.8f, Haze);
            p.seed = Random.Range(0f, 50f);
            p.drag = 0.4f;
            p.fall = 0f;
            p.t.localScale = Vector3.one * p.size * 0.5f;
        }

        /// <summary>A dry white dusting where flour has settled on the desk.</summary>
        void Dust(float radius)
        {
            if (!Mess.Instance) return;
            Vector3 at = transform.position + new Vector3(Random.Range(-0.55f, 0.55f), 0f, Random.Range(-0.7f, -0.15f));
            Mess.Instance.DeskDust(at, radius, FlourDust, 70f, 1f);
        }

        // ---- The chaos set ----------------------------------------------------------------------------------

        /// <summary>!shake: the sack is given a good one: a big whump of flour and the air thickens fast.</summary>
        public void Burst()
        {
            Sfx.Play("spore_puff", 0.9f, 0.1f);
            Haze = Mathf.Min(1f, Haze + 0.7f);
            for (int i = 0; i < 30; i++) SackPuff(true);
            for (int i = 0; i < 5; i++) Dust(Random.Range(0.07f, 0.13f));
        }

        /// <summary>The face slam: it lands on the desk in a burst of flour.</summary>
        public void OnSlam()
        {
            Sfx.Play("spore_puff", 1f, 0.08f);
            Haze = Mathf.Max(Haze, 0.9f);
            for (int i = 0; i < 40; i++) SackPuff(true);
            for (int i = 0; i < 7; i++) Dust(Random.Range(0.08f, 0.15f));
        }
    }
}
