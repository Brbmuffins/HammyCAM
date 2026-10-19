using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// The feather pillow: it leaks the odd feather from its corners as it moves (a puff of them when you talk), a
    /// hard shake pops a seam, and !shake or the face slam bursts it in a snowstorm: hundreds of feathers blown up
    /// out of the frame that come fluttering down for ages, swaying and turning, and settle on the desk, where the
    /// typing hands puff them back up. K sweeps them away. Hundreds of feathers are drawn in a few instanced calls.
    /// </summary>
    public sealed class FeatherPillow : GagAvatar
    {
        [Tooltip("The seam corners (rest, avatar space) the feathers leak from: top left, top right.")]
        public Vector3 leftCorner = new Vector3(-0.373f, 1.093f, -0.003f), rightCorner = new Vector3(0.368f, 1.093f, -0.003f);
        [Tooltip("The middle of the pillow (rest): a burst comes from all over it.")]
        public Vector3 centre = new Vector3(0f, 0.603f, 0f);
        [Tooltip("Most feathers at once (in the air and on the desk).")]
        public int maxFeathers = 900;

        struct Feather
        {
            public Vector3 Position, Velocity;
            public float Size, Phase, Yaw, Tilt, Roll, Spin, LandedAt;
            public bool Landed;
        }

        readonly List<Feather> feathers = new List<Feather>();
        Matrix4x4[] matrices = new Matrix4x4[1023];
        Mesh quad;
        Material featherMaterial;
        Anchor left, right, middle;
        float owed, nextSeam, nextHandCheck;
        Vector3 lastLeftHand, lastRightHand, lastMouse;

        public override void TestMoment() => FromCorner(45, 1.4f);
        public override string TestReport => $"{feathers.Count} feathers";
        public override string MessName => "a storm of feathers";
        public int FeatherCount => feathers.Count;

        protected override void Begin()
        {
            left = Pin(leftCorner);
            right = Pin(rightCorner);
            middle = Pin(centre);
            quad = FeatherMesh();
            if (Mess.Instance && Mess.Instance.stainMaterial)
            {
                featherMaterial = new Material(Mess.Instance.stainMaterial) { name = "Feather", enableInstancing = true };
                featherMaterial.SetTexture("_BaseMap", FeatherTexture());
                featherMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
                featherMaterial.SetFloat("_Smoothness", 0.05f);
                featherMaterial.SetFloat("_Cull", 0f); // both sides: a feather is seen from above and below
            }
            DeskDebris.Cleaned += OnCleaned;
            DeskDebris.AreaCleaned += OnAreaCleaned;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            DeskDebris.Cleaned -= OnCleaned;
            DeskDebris.AreaCleaned -= OnAreaCleaned;
            if (featherMaterial) Destroy(featherMaterial);
        }

        // The feathers stay on the desk when another avatar comes on (they're mess like any other): a switched-off
        // avatar can't update, so the desk runs a coroutine that keeps them falling and drawn until it's back.
        protected override void OnDisable()
        {
            base.OnDisable();
            if (feathers.Count > 0 && DeskDebris.Instance) DeskDebris.Instance.StartCoroutine(KeepDrawing());
        }

        System.Collections.IEnumerator KeepDrawing()
        {
            while (!isActiveAndEnabled && feathers.Count > 0)
            {
                Simulate(Mathf.Min(Time.deltaTime, 1f / 30f));
                Draw();
                yield return null;
            }
        }

        protected override void Tick(float dt)
        {
            float speed = meter.Speed, over = Mathf.Max(0f, meter.Shake - 8f);
            // A feather now and then from the corners as it moves, more when shaken.
            owed += (0.05f + speed * 0.5f + over * 0.04f) * dt;
            for (int guard = 0; owed >= 1f && guard < 6; guard++)
            {
                owed -= 1f;
                FromCorner(1, 0.4f);
            }
            if (mouthJustOpened) FromCorner(Random.Range(1, 4), 0.7f); // talking puffs a few out
            // A hard shake pops a seam.
            if (meter.Shake > 45f && Time.time >= nextSeam)
            {
                nextSeam = Time.time + 4f;
                Sfx.Play("pop", 0.7f, 0.15f);
                rig.Bonk(0.12f);
                FromCorner(45, 1.4f);
            }
            Simulate(dt);
            Draw();
        }

        public override void Burst() => Explode(160, 1f);

        public override void OnSlam() => Explode(320, 1.35f);

        public override void Whack(float strength)
        {
            base.Whack(strength);
            Explode(Mathf.RoundToInt(70 * strength), 0.8f);
        }

        /// <summary>The seam gives: feathers blast out of the whole pillow, up and out of the frame, in a cloud of fluff.</summary>
        void Explode(int count, float force)
        {
            Sfx.Play("pop", 1f, 0.1f);
            Sfx.Play("spore_puff", 0.8f, 0.1f);
            rig.Bonk(0.3f * force);
            Vector3 c = Pos(middle);
            for (int i = 0; i < count; i++)
            {
                Vector3 at = c + Vector3.Scale(Random.insideUnitSphere, new Vector3(body.extents.x * 0.8f, body.extents.y * 0.7f, body.extents.z));
                Vector3 out3 = (at - c).normalized;
                Vector3 v = out3 * Random.Range(0.4f, 1.6f) * force + Vector3.up * Random.Range(1.2f, 3.6f) * force + Vector3.back * Random.Range(0f, 0.9f) + meter.Velocity * 0.4f;
                Spawn(at, v);
            }
            if (puffs != null)
                for (int i = 0; i < 14; i++)
                    puffs.Emit(c + Random.insideUnitSphere * 0.25f, Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f, new Color(1f, 1f, 1f, 0.45f),
                        Random.Range(0.25f, 0.4f), Random.Range(1.4f, 2.4f), 2.8f, 2f);
        }

        void FromCorner(int count, float force)
        {
            for (int i = 0; i < count; i++)
            {
                bool l = Random.value < 0.5f;
                Vector3 at = Pos(l ? left : right);
                Vector3 side = new Vector3(l ? -1f : 1f, 0f, 0f);
                Spawn(at, (side * Random.Range(0.2f, 0.6f) + Vector3.up * Random.Range(0.3f, 0.9f) + Vector3.back * Random.Range(0f, 0.4f)) * force + meter.Velocity * 0.6f);
            }
        }

        void Spawn(Vector3 at, Vector3 velocity)
        {
            if (feathers.Count >= maxFeathers)
            {
                // The oldest one lying on the desk makes room (else the oldest of all).
                int oldest = feathers.FindIndex(f => f.Landed);
                feathers.RemoveAt(oldest >= 0 ? oldest : 0);
            }
            feathers.Add(new Feather
            {
                Position = at,
                Velocity = velocity,
                Size = Random.Range(0.035f, 0.065f),
                Phase = Random.Range(0f, 100f),
                Yaw = Random.Range(0f, 360f),
                Tilt = Random.Range(-30f, 30f),
                Roll = Random.Range(-40f, 40f),
                Spin = Random.Range(-120f, 120f),
            });
        }

        /// <summary>
        /// Airborne feathers: thrown fast, they're slowed hard by the air and then sink slowly, swaying side to side
        /// and rocking (a falling feather's zig-zag). Landed ones lie flat until a hand passes and puffs them up.
        /// </summary>
        void Simulate(float dt)
        {
            if (feathers.Count == 0) return;
            DeskScene desk = Chaos ? Chaos.desk : null;
            bool hands = desk && desk.Shown && Time.time >= nextHandCheck;
            Vector3 lh = default, rh = default, mouse = default, lv = default, rv = default, mv = default;
            if (hands)
            {
                const float since = 0.05f;
                nextHandCheck = Time.time + since;
                lh = desk.LeftHandPosition;
                rh = desk.RightHandPosition;
                mouse = desk.MousePosition;
                bool primed = lastLeftHand != Vector3.zero;
                lv = primed ? (lh - lastLeftHand) / since : Vector3.zero;
                rv = primed ? (rh - lastRightHand) / since : Vector3.zero;
                mv = primed ? (mouse - lastMouse) / since : Vector3.zero;
                lastLeftHand = lh;
                lastRightHand = rh;
                lastMouse = mouse;
            }
            float t = Time.time;
            for (int i = feathers.Count - 1; i >= 0; i--)
            {
                Feather f = feathers[i];
                if (f.Landed)
                {
                    if (hands)
                    {
                        // A hand (or the mouse) moving through it lifts it.
                        Vector3 kick = Lift(f.Position, lh, lv) + Lift(f.Position, rh, rv) + Lift(f.Position, mouse, mv);
                        if (kick != Vector3.zero)
                        {
                            f.Landed = false;
                            f.Velocity = kick;
                            f.Spin = Random.Range(-160f, 160f);
                        }
                    }
                    feathers[i] = f;
                    continue;
                }
                // Air: strong drag, gravity to a slow sinking speed, a sway that grows as it slows down.
                f.Velocity *= Mathf.Exp(-2.4f * dt);
                f.Velocity.y = Mathf.Max(f.Velocity.y - 3.5f * dt, -0.16f);
                float calm = 1f - Mathf.Clamp01(f.Velocity.magnitude / 1.2f);
                float sway = Mathf.Sin(t * 2.2f + f.Phase) * 0.22f * calm;
                f.Position += (f.Velocity + new Vector3(sway, 0f, Mathf.Cos(t * 1.7f + f.Phase) * 0.06f * calm)) * dt;
                f.Roll = Mathf.Sin(t * 2.2f + f.Phase + 1.3f) * 38f * calm + f.Roll * (1f - calm) * 0.98f;
                f.Yaw += f.Spin * dt * (0.4f + calm);
                float ground = DeskScene.SurfaceHeight(f.Position.x, f.Position.z);
                if (!float.IsNegativeInfinity(ground) && f.Position.y <= ground + 0.004f && f.Velocity.y <= 0f)
                {
                    f.Position.y = ground + 0.003f;
                    f.Landed = true;
                    f.LandedAt = t;
                    f.Roll = Random.Range(-8f, 8f);
                    f.Tilt = Random.Range(-6f, 6f);
                }
                else if (f.Position.y < -1.5f)
                {
                    feathers.RemoveAt(i); // fell off the desk, out of sight
                    continue;
                }
                feathers[i] = f;
            }
        }

        static Vector3 Lift(Vector3 feather, Vector3 hand, Vector3 handVelocity)
        {
            if (hand == Vector3.zero || handVelocity.sqrMagnitude < 0.04f) return Vector3.zero;
            Vector3 d = feather - hand;
            if (new Vector2(d.x, d.z).sqrMagnitude > 0.07f * 0.07f || d.y > 0.06f) return Vector3.zero;
            return new Vector3(handVelocity.x * 0.5f + Random.Range(-0.15f, 0.15f), Random.Range(0.35f, 0.8f), handVelocity.z * 0.5f + Random.Range(-0.15f, 0.15f));
        }

        void Draw()
        {
            if (!featherMaterial || !quad || feathers.Count == 0) return;
            var rp = new RenderParams(featherMaterial) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
            int n = 0;
            foreach (Feather f in feathers)
            {
                Quaternion r = f.Landed
                    ? Quaternion.Euler(f.Tilt, f.Yaw, f.Roll)
                    : Quaternion.Euler(f.Tilt + 10f, f.Yaw, f.Roll);
                matrices[n++] = Matrix4x4.TRS(f.Position, r, new Vector3(f.Size * 0.42f, f.Size, f.Size));
                if (n == matrices.Length)
                {
                    Graphics.RenderMeshInstanced(rp, quad, 0, matrices, n);
                    n = 0;
                }
            }
            if (n > 0) Graphics.RenderMeshInstanced(rp, quad, 0, matrices, n);
        }

        void OnCleaned() => feathers.Clear();

        void OnAreaCleaned(Vector3 at, float radius)
        {
            feathers.RemoveAll(f => f.Landed && new Vector2(f.Position.x - at.x, f.Position.z - at.z).sqrMagnitude < radius * radius);
        }

        /// <summary>A feather lying flat (y = 0), 1 across and 1 long (scaled per feather), with a little curve to its vane.</summary>
        static Mesh FeatherMesh()
        {
            var mesh = new Mesh { name = "Feather" };
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            const int rows = 4;
            for (int j = 0; j <= rows; j++)
            {
                float t = (float)j / rows;
                float z = t - 0.5f, lift = 0.08f * Mathf.Sin(t * Mathf.PI);
                v.Add(new Vector3(-0.5f, lift * 0.5f, z));
                v.Add(new Vector3(0.5f, lift * 0.5f, z));
                v.Add(new Vector3(0f, lift, z));
                uv.Add(new Vector2(0f, t));
                uv.Add(new Vector2(1f, t));
                uv.Add(new Vector2(0.5f, t));
            }
            for (int j = 0; j < rows; j++)
            {
                int a = j * 3, b = (j + 1) * 3;
                tri.AddRange(new[] { a, b, a + 2, a + 2, b, b + 2 });       // left half
                tri.AddRange(new[] { a + 2, b + 2, a + 1, a + 1, b + 2, b + 1 }); // right half
            }
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A white down feather: a soft vane round a thin quill, its barbs fanning out, fluffy at the base.</summary>
        static Texture2D FeatherTexture()
        {
            const int w = 64, h = 128;
            var px = new Color32[w * h];
            var random = new System.Random(5);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, t = (y + 0.5f) / h; // across -1..1, along 0 (base) .. 1 (tip)
                float width = Mathf.Sin(Mathf.Clamp01(t * 1.05f) * Mathf.PI) * (0.75f + 0.25f * t) * 0.95f;
                float edge = Mathf.Abs(u) / Mathf.Max(0.02f, width);
                // Barbs: thin lines angled up from the quill; the base is all fluff.
                float barb = 0.5f + 0.5f * Mathf.Sin((t * 40f - Mathf.Abs(u) * 9f) * 2f);
                float fluff = Mathf.Lerp(1f, 0.45f + 0.55f * barb, Mathf.Clamp01(t * 3f));
                float alpha = Mathf.Clamp01((1f - edge) * 4f) * fluff * (0.9f + 0.1f * (float)random.NextDouble());
                // Ragged edge and a split or two in the vane.
                if (edge > 0.75f && random.NextDouble() < 0.35) alpha *= 0.4f;
                float quill = Mathf.Clamp01(1f - Mathf.Abs(u) * 28f) * Mathf.Clamp01(1.1f - t);
                byte shade = (byte)(238 + 17 * (1f - edge) - 25 * quill);
                alpha = Mathf.Max(alpha, quill * 0.95f);
                px[y * w + x] = new Color32(shade, shade, (byte)Mathf.Min(255, shade + 4), (byte)(alpha * 255f));
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Feather", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
