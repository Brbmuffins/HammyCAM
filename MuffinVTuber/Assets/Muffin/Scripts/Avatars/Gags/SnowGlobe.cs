using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// The snow globe: a few hundred flakes of glitter inside the glass. Shake or turn it and they whirl up in a
    /// blizzard round the little scene, then drift slowly down and settle. Talking stirs them a little. !shake is a
    /// proper shake; the face slam CRACKS it: water and glitter leak out across the desk (the glitter's on the desk
    /// for good, like real glitter), and after a while it has resealed itself and refilled.
    /// </summary>
    public sealed class SnowGlobe : GagAvatar
    {
        [Tooltip("The globe's middle and inside radius (rest, avatar space); zero = worked out from the model's size.")]
        public Vector3 globeCentre;
        public float globeRadius = 0.25f;
        [Tooltip("How far below the globe's middle the little snowy scene's ground is.")]
        public float groundDepth = 0.14f;
        public int flakes = 420;

        static readonly Color Water = new Color(0.6f, 0.8f, 1f, 0.55f);

        Vector3[] p, v;
        Quaternion[] spin;
        float[] size;
        int live;
        Anchor globe;
        Material snow, gold, blue;
        Mesh flake, snowQuad;
        Matrix4x4[] matrices;
        Vector3 lastCentre, lastVelocity;
        float stir;
        bool cracked;

        public int Live => live;
        public float Stir => stir;
        public override void TestMoment() => Burst();
        public override string TestReport => $"{live} flakes, stir {stir:0.00}";
        public override string MessName => "a snow globe blizzard";

        protected override void Begin()
        {
            if (globeCentre == Vector3.zero) globeCentre = new Vector3(0f, body.size.y * 0.6f, 0f);
            globe = Pin(globeCentre);
            p = new Vector3[flakes];
            v = new Vector3[flakes];
            spin = new Quaternion[flakes];
            size = new float[flakes];
            matrices = new Matrix4x4[flakes];
            for (int i = 0; i < flakes; i++) Settle(i);
            live = flakes;
            flake = Mess.PrimitiveMesh(PrimitiveType.Cube);
            snowQuad = Mess.PrimitiveMesh(PrimitiveType.Quad);
            // Snow is bright white whatever the light (unlit, facing the camera); the glitter catches the light.
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit)
            {
                snow = new Material(unlit) { name = "Snow", enableInstancing = true };
                snow.SetColor("_BaseColor", new Color(0.97f, 0.98f, 1f));
            }
            gold = Sparkle(new Color(1f, 0.85f, 0.3f), 0.15f); // (shiny metal would mirror the dim room and look brown)
            blue = Sparkle(new Color(0.75f, 0.9f, 1f), 0.1f);
            lastCentre = Pos(globe);
        }

        Material Sparkle(Color c, float metal)
        {
            Material m = Tint(c);
            if (!m) return null;
            m = new Material(m) { name = "Glitter", enableInstancing = true };
            m.SetFloat("_Smoothness", 0.85f);
            m.SetFloat("_Metallic", metal);
            return m;
        }

        /// <summary>A flake lying on the little scene's ground.</summary>
        void Settle(int i)
        {
            Vector2 d = Random.insideUnitCircle * globeRadius * 0.75f;
            p[i] = new Vector3(d.x, -groundDepth + 0.004f, d.y);
            v[i] = Vector3.zero;
            spin[i] = Random.rotation;
            size[i] = Random.Range(0.012f, 0.022f);
        }

        protected override void Tick(float dt)
        {
            Transform bone = globe.Bone;
            if (!bone) return;
            Vector3 centre = Pos(globe);
            Vector3 vel = (centre - lastCentre) / dt;
            Vector3 acc = (vel - lastVelocity) / dt;
            lastCentre = centre;
            lastVelocity = vel;
            // A jolt (or turning) whirls them up; it dies down over a few seconds.
            stir = Mathf.Max(stir * Mathf.Exp(-dt * 0.6f), Mathf.Clamp01((acc.magnitude - 3f) / 25f), Mathf.Clamp01(meter.Speed * 0.9f));
            if (mouthJustOpened) stir = Mathf.Max(stir, 0.25f);
            Vector3 jolt = bone.InverseTransformDirection(-Vector3.ClampMagnitude(acc, 30f));
            float step = Mathf.Min(dt, 1f / 30f);
            float wall = globeRadius - 0.01f;
            float t = Time.time;
            for (int i = 0; i < live; i++)
            {
                Vector3 q = p[i];
                // Slow sinking in the water, the jolt, and a swirl round the middle while stirred.
                Vector3 swirl = new Vector3(-q.z, 0f, q.x) * 2.2f * stir + new Vector3(Mathf.PerlinNoise(i * 0.3f, t * 0.7f) - 0.5f, Mathf.PerlinNoise(t * 0.7f, i * 0.3f) - 0.45f, Mathf.PerlinNoise(i * 0.7f + 3f, t * 0.6f) - 0.5f) * 0.5f * stir;
                v[i] += (jolt * 0.06f + swirl + Vector3.down * 0.05f) * step;
                v[i] *= Mathf.Exp(-2.5f * step);
                q += v[i] * step;
                float d = q.magnitude;
                if (d > wall)
                {
                    q *= wall / d;
                    v[i] *= -0.2f;
                }
                if (q.y < -groundDepth + 0.004f)
                {
                    q.y = -groundDepth + 0.004f;
                    v[i] = new Vector3(v[i].x * 0.5f, 0f, v[i].z * 0.5f);
                }
                p[i] = q;
                spin[i] = Quaternion.Euler(v[i].magnitude * 900f * step, 140f * step, 0f) * spin[i];
            }
            Draw(bone);
        }

        void Draw(Transform bone)
        {
            if (!flake || live == 0) return;
            Matrix4x4 toWorld = bone.localToWorldMatrix;
            Vector3 offset = globe.Local;
            Camera cam = Camera.main;
            void Batch(Material m, Mesh mesh, int from, int every, bool billboard)
            {
                if (!m) return;
                int n = 0;
                for (int i = from; i < live; i += every)
                {
                    Vector3 at = toWorld.MultiplyPoint3x4(offset + p[i]);
                    matrices[n++] = billboard && cam
                        ? Matrix4x4.TRS(at, cam.transform.rotation * Quaternion.Euler(0f, 0f, spin[i].eulerAngles.z), Vector3.one * size[i] * 0.8f)
                        : Matrix4x4.TRS(at, bone.rotation * spin[i], new Vector3(size[i], size[i] * 0.15f, size[i]));
                }
                if (n > 0) Graphics.RenderMeshInstanced(new RenderParams(m) { shadowCastingMode = ShadowCastingMode.Off }, mesh, 0, matrices, n);
            }
            Batch(snow, snowQuad, 0, 2, true);
            Batch(gold, flake, 1, 4, false);
            Batch(blue, flake, 3, 4, false);
        }

        public override void Burst()
        {
            Sfx.Play("sprinkle_rain", 0.5f, 0.1f);
            rig.Bonk(0.1f);
            stir = 1f;
            for (int i = 0; i < live; i++) v[i] += Random.insideUnitSphere * 0.5f + Vector3.up * 0.4f;
        }

        public override void OnSlam()
        {
            if (!cracked) Run(Crack());
        }

        /// <summary>CRACK: the glass splits low on its front, water and glitter gush out over the desk.</summary>
        IEnumerator Crack()
        {
            cracked = true;
            Sfx.Play("egg_crack", 1f, 0.05f);
            Sfx.Play("water_spill", 0.8f, 0.08f);
            Vector3 Leak() => Pos(globe) + Vector3.back * globeRadius * 0.95f + Vector3.down * globeRadius * 0.3f;
            Run(Stream(Leak, () => (Vector3.back + Vector3.down * 0.3f + Vector3.right * Random.Range(-0.3f, 0.3f)).normalized, Water, 2.5f, 50f,
                new Vector2(0.6f, 1.4f), new Vector2(0.012f, 0.02f), 0.06f));
            if (Mess.Instance) Mess.Instance.DeskStain(InFront(0f, 0.1f), 0.18f, Water, 160f, 1.5f, true, new Vector2(0f, -0.8f));
            // The glitter washes out with it and stays on the desk.
            for (int k = 0; k < 30; k++)
            {
                for (int i = 0; i < 12 && DeskDebris.Instance; i++)
                    DeskDebris.Instance.EmitSprinkle(Leak() + Random.insideUnitSphere * 0.02f, new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0f, 0.5f), -Random.Range(0.4f, 1.2f)));
                live = Mathf.Max(30, live - 8);
                yield return new WaitForSeconds(0.08f);
            }
            yield return new WaitForSeconds(30f);
            // Resealed and topped up.
            for (int i = live; i < flakes; i++) Settle(i);
            live = flakes;
            cracked = false;
        }
    }
}
