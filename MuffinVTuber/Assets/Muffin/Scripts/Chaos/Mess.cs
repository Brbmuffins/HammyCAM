using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// The mess that stays: splats on the desk (puddles, splatters, sauce trails, scorch marks) and blobs stuck
    /// on the avatar or lying on the desk, each fading away after a while, plus liquid drops that fly, land and
    /// splat (drops of the same colour landing together grow one puddle). It stays alive: fresh splats land with
    /// a wobble, puddles spread a little, the hands smear wet stains and squish blobs lying on the desk, and blobs
    /// stuck on the avatar fly off when it's shaken. K cleans it all.
    /// </summary>
    public sealed class Mess : MonoBehaviour
    {
        public static Mess Instance { get; private set; }

        public DeskScene desk;
        [Tooltip("Transparent lit material: given a splat shape and tinted per stain.")]
        public Material stainMaterial;
        [Tooltip("Lit material for blobs (cream, chunks), tinted per blob.")]
        public Material blobMaterial;
        [Tooltip("Most stains kept at once (the oldest go first).")]
        public int maxStains = 500;
        public float gravity = 14f;
        [Tooltip("Shake (m/s²) at which blobs stuck on the avatar start flying off.")]
        public float flingShake = 5f;

        sealed class Stain
        {
            public Transform Transform;
            public float Born, Life, GrowTime;
            public Vector3 FullScale;
            public Color Colour;
            public bool Puddle;    // on the desk: grows when drops land on it, and spreads a little
            public bool Smearable; // wet: the hands drag streaks out of it
            public bool Blob;      // a lump: lying on the desk, the hands squish it flat
            public bool Flingable; // stuck on the avatar: flies off when it's shaken
            public bool Dry;       // powder: settles in softly, no wet wobble
            public int Smears;
            public float LastSmear, Shake;
            public Vector3 LastPosition, LastVelocity;
            public bool Primed;
        }

        struct Droplet
        {
            public Vector3 Position, Velocity;
            public Color Colour;
            public float Size, Puddle;
        }

        readonly List<Stain> stains = new List<Stain>();
        readonly List<Droplet> drops = new List<Droplet>();
        readonly Dictionary<Color, Material> stainMaterials = new Dictionary<Color, Material>();
        readonly Dictionary<(Color, Texture2D), Material> splatMaterials = new Dictionary<(Color, Texture2D), Material>();
        readonly Dictionary<(Color, Texture2D), Material> dustMaterials = new Dictionary<(Color, Texture2D), Material>();
        readonly Dictionary<Color, Material> blobMaterials = new Dictionary<Color, Material>();
        readonly Dictionary<Color, List<Matrix4x4>> dropBatches = new Dictionary<Color, List<Matrix4x4>>();
        Mesh flat, sphere;
        Texture2D[] splats, puddles, dusts;
        Transform root;
        int layer;
        float lastSmear, nextDripSound, nextSquelch;

        public int StainCount => stains.Count;
        /// <summary>Blobs flung off the avatar and stains smeared or squished by the hands so far (the self-test counts them).</summary>
        public int Flung { get; private set; }
        public int Smeared { get; private set; }
        /// <summary>Lumpy round shapes (diameter about 1): blobs, chunks, crumpled paper.</summary>
        public Mesh[] Lumps { get; private set; }
        /// <summary>A piped swirl of whipped cream (about 1 wide and 1 tall).</summary>
        public Mesh Swirl { get; private set; }

        void Awake()
        {
            Instance = this;
            sphere = PrimitiveMesh(PrimitiveType.Sphere);
            flat = FlatQuad(0.25f / SplatRadius); // the splat body (SplatRadius of the texture) comes out 1 across
            splats = new Texture2D[5];
            for (int i = 0; i < splats.Length; i++) splats[i] = MakeSplat(11 + i * 7, false);
            puddles = new Texture2D[3];
            for (int i = 0; i < puddles.Length; i++) puddles[i] = MakeSplat(101 + i * 13, true);
            dusts = new Texture2D[4];
            for (int i = 0; i < dusts.Length; i++) dusts[i] = MakeDust(201 + i * 17);
            Lumps = new Mesh[4];
            for (int i = 0; i < Lumps.Length; i++) Lumps[i] = Lumpy(i * 31 + 5, 0.14f + i * 0.03f);
            Swirl = PipedSwirl();
            root = new GameObject("Mess").transform;
            root.SetParent(transform, false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
            return mesh;
        }

        public Mesh RandomLump => Lumps[Random.Range(0, Lumps.Length)];

        /// <summary>The plain see-through material in a colour (drops in flight).</summary>
        public Material StainMaterial(Color c) => Tinted(stainMaterials, stainMaterial, c);
        public Material BlobMaterial(Color c) => Tinted(blobMaterials, blobMaterial, c);

        Material SplatMaterial(Color c, Texture2D shape)
        {
            if (splatMaterials.TryGetValue((c, shape), out Material m)) return m;
            m = new Material(stainMaterial) { enableInstancing = true };
            m.SetColor("_BaseColor", c);
            m.SetTexture("_BaseMap", shape);
            splatMaterials[(c, shape)] = m;
            return m;
        }

        /// <summary>The same see-through material with no shine at all: dry powder, not a wet stain.</summary>
        Material DustMaterial(Color c, Texture2D shape)
        {
            if (dustMaterials.TryGetValue((c, shape), out Material m)) return m;
            m = new Material(stainMaterial) { enableInstancing = true };
            m.SetColor("_BaseColor", c);
            m.SetTexture("_BaseMap", shape);
            m.SetFloat("_Smoothness", 0f);
            dustMaterials[(c, shape)] = m;
            return m;
        }

        static Material Tinted(Dictionary<Color, Material> cache, Material template, Color c)
        {
            if (cache.TryGetValue(c, out Material m)) return m;
            m = new Material(template) { enableInstancing = true };
            m.SetColor("_BaseColor", c);
            cache[c] = m;
            return m;
        }

        /// <summary>
        /// A splat lying on the desk at (x, z), radius metres across its main body (its spatters reach further):
        /// a puddle, splat, trail dot or scorch mark. It lands with a wobble over growTime and fades after life
        /// seconds. Puddles merge with drops of their colour and spread a little. flow (m/s along the desk)
        /// stretches it the way it was moving; smearable stains get dragged about by the hands.
        /// </summary>
        public Transform DeskStain(Vector3 at, float radius, Color colour, float life = 120f, float growTime = 0.3f, bool puddle = false,
            Vector2 flow = default, bool smearable = true) => PlaceOnDesk(at, radius, colour, life, growTime, puddle, flow, smearable, false);

        /// <summary>
        /// A dusting of dry powder (flour) on the desk, radius metres across: soft-edged, grainy and matte, with
        /// specks round it. It settles in without a wobble and the hands don't smear it.
        /// </summary>
        public Transform DeskDust(Vector3 at, float radius, Color colour, float life = 120f, float growTime = 0.8f) =>
            PlaceOnDesk(at, radius, colour, life, growTime, false, default, false, true);

        Transform PlaceOnDesk(Vector3 at, float radius, Color colour, float life, float growTime, bool puddle, Vector2 flow, bool smearable, bool dust)
        {
            if (float.IsNegativeInfinity(DeskScene.SurfaceHeight(at.x, at.z))) return null; // off the desk
            float speed = flow.magnitude;
            float along = 1f + Mathf.Min(speed * 0.35f, 1.4f);
            var stretch = new Vector3(Random.Range(0.85f, 1.15f) / Mathf.Sqrt(along), 1f, Random.Range(0.85f, 1.15f) * along);
            var scale = new Vector3(radius * 2f * stretch.x, 1f, radius * 2f * stretch.z);
            // Kept on the desk: a splat near the edge moves in (or shrinks) so its spatters don't hang off in the air.
            float reach = 0.25f / SplatRadius * Mathf.Max(scale.x, scale.z) * 0.85f * (puddle ? 1.12f : 1f);
            float roomX = (DeskScene.DeskMaxX - DeskScene.DeskMinX) * 0.5f, roomZ = (DeskScene.DeskMaxZ - DeskScene.DeskMinZ) * 0.5f;
            if (reach > Mathf.Min(roomX, roomZ))
            {
                scale *= Mathf.Min(roomX, roomZ) / reach;
                reach = Mathf.Min(roomX, roomZ);
            }
            at.x = Mathf.Clamp(at.x, DeskScene.DeskMinX + reach, DeskScene.DeskMaxX - reach);
            at.z = Mathf.Clamp(at.z, DeskScene.DeskMinZ + reach, DeskScene.DeskMaxZ - reach);
            float ground = DeskScene.SurfaceHeight(at.x, at.z);
            // Each new stain sits a hair above the last, so overlapping ones don't flicker.
            layer = (layer + 1) % 200;
            var position = new Vector3(at.x, ground + 0.0015f + layer * 0.00004f, at.z);
            float yaw = speed > 0.05f ? Mathf.Atan2(flow.x, flow.y) * Mathf.Rad2Deg : Random.Range(0f, 360f);
            Texture2D shape = dust ? dusts[Random.Range(0, dusts.Length)]
                : puddle && speed < 1f ? puddles[Random.Range(0, puddles.Length)] : splats[Random.Range(0, splats.Length)];
            var t = Make(flat, dust ? DustMaterial(colour, shape) : SplatMaterial(colour, shape), position, Quaternion.Euler(0f, yaw, 0f));
            Stain s = Add(t, scale, colour, life, growTime);
            s.Puddle = puddle;
            s.Smearable = smearable;
            s.Dry = dust;
            return t;
        }

        /// <summary>
        /// A lumpy blob (a splat on the avatar, a chunk) stuck on parent (or lying where it's put); it fades after
        /// life seconds. Stuck on the avatar and flingable, it flies off when the avatar is shaken.
        /// </summary>
        public Transform Blob(Transform parent, Vector3 localPosition, Vector3 localScale, Color colour, float life = 30f, float growTime = 0.15f,
            bool flingable = true)
        {
            var t = Make(RandomLump, BlobMaterial(colour), Vector3.zero, Quaternion.identity);
            t.SetParent(parent ? parent : root, false);
            t.localPosition = localPosition;
            Stain s = Add(t, localScale, colour, life, growTime);
            s.Blob = !parent;
            s.Flingable = flingable && parent;
            return t;
        }

        /// <summary>A piped swirl of whipped cream, size metres across, on parent (riding it) or on the desk (world position).</summary>
        public Transform Dollop(Transform parent, Vector3 position, float size, Color colour, float life = 120f, float growTime = 0.3f, bool flingable = true)
        {
            var t = Make(Swirl, BlobMaterial(colour), Vector3.zero, Quaternion.identity);
            t.SetParent(parent ? parent : root, false);
            if (parent) t.localPosition = position;
            else t.position = new Vector3(position.x, Mathf.Max(0f, DeskScene.SurfaceHeight(position.x, position.z)), position.z);
            t.localRotation = Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));
            float scale = parent ? Mathf.Max(0.01f, parent.lossyScale.x) : 1f;
            Stain s = Add(t, new Vector3(size, size * Random.Range(0.8f, 1.1f), size) / scale, colour, life, growTime);
            s.Blob = !parent;
            s.Flingable = flingable && parent;
            return t;
        }

        /// <summary>Any object that should stay as mess for a while and go when K is pressed (a tipped pitcher).</summary>
        public void Keep(Transform t, float life)
        {
            if (!t) return;
            t.SetParent(root, true);
            stains.Add(new Stain { Transform = t, Born = Time.time, Life = life, GrowTime = 0f, FullScale = t.localScale });
        }

        /// <summary>Stop keeping one (it's gone, or turned into something else, like a drip reaching the desk).</summary>
        public void Forget(Transform t)
        {
            int i = stains.FindIndex(s => s.Transform == t);
            if (i >= 0) Remove(i);
            else if (t) Destroy(t.gameObject);
        }

        Stain Add(Transform t, Vector3 scale, Color colour, float life, float growTime)
        {
            if (stains.Count >= maxStains) Remove(0);
            t.localScale = growTime > 0f ? Vector3.zero : scale;
            var s = new Stain { Transform = t, Born = Time.time, Life = life, GrowTime = growTime, FullScale = scale, Colour = colour };
            stains.Add(s);
            return s;
        }

        Transform Make(Mesh mesh, Material material, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Mess");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>A drop of liquid: it falls, and where it lands it makes or grows a puddle of `puddle` radius.</summary>
        public void Drop(Vector3 position, Vector3 velocity, Color colour, float size = 0.012f, float puddle = 0.035f)
        {
            if (drops.Count > 1200) return;
            drops.Add(new Droplet { Position = position, Velocity = velocity, Colour = colour, Size = size, Puddle = puddle });
        }

        /// <summary>
        /// A hand (or the mouse) at centre, moving at velocity, over the desk: a wet stain under it gets a streak
        /// dragged out of it the way it's moving, and a blob lying there gets squished flat.
        /// </summary>
        public void Smear(Vector3 centre, float radius, Vector3 velocity)
        {
            var along = new Vector2(velocity.x, velocity.z);
            float speed = along.magnitude;
            float now = Time.time;
            if (speed < 0.15f || now - lastSmear < 0.08f) return; // a smear now and then, not a churn
            Vector2 direction = along / speed;
            for (int i = 0; i < stains.Count; i++)
            {
                Stain s = stains[i];
                if (!s.Transform || s.Transform.parent != root || (!s.Smearable && !s.Blob)) continue;
                Vector3 p = s.Transform.position;
                float height = centre.y - p.y;
                if (height > 0.07f || height < -0.05f) continue;
                float r = s.FullScale.x * 0.5f;
                float reach = radius + r * 0.5f;
                if (new Vector2(p.x - centre.x, p.z - centre.z).sqrMagnitude > reach * reach) continue;
                if (s.Blob)
                {
                    // Squish: a lump becomes a splat, pushed the way the hand went.
                    Squelch();
                    DeskStain(p, Mathf.Max(0.02f, r * 1.3f), s.Colour, 90f, 0.06f, false, direction * 2f);
                    Remove(i);
                    Smeared++;
                    lastSmear = now;
                    return;
                }
                if (s.Smears >= 2 || now - s.LastSmear < 0.25f || now - s.Born > 40f) continue; // only fresh, wet stains smear
                s.Smears++;
                s.LastSmear = now;
                Squelch();
                float length = Mathf.Clamp(speed * 0.15f, 0.03f, 0.14f);
                float width = Mathf.Max(0.012f, r * 0.45f);
                Vector3 at = p + new Vector3(direction.x, 0f, direction.y) * (r * 0.5f + length * 0.5f);
                Color c = s.Colour;
                c.a *= 0.75f;
                float left = s.Life - (now - s.Born);
                if (DeskStain(at, width, c, Mathf.Max(20f, left * 0.7f), 0.08f, false, direction * (length / width) * 1.2f))
                    stains[stains.Count - 1].Smears = 2; // a streak doesn't smear again
                Smeared++;
                lastSmear = now;
                return; // one at a time
            }
        }

        void Squelch()
        {
            if (Time.time < nextSquelch) return;
            nextSquelch = Time.time + 0.18f;
            Sfx.Play("squelch", 0.45f, 0.15f);
        }

        /// <summary>
        /// The nearest stain or lump on the desk (seen from above) that passes the test and isn't already going, if any:
        /// what the vacuum's hose goes after when nobody steers it.
        /// </summary>
        public bool NearestOnDesk(Vector3 from, System.Func<Vector3, bool> allowed, out Vector3 at)
        {
            at = default;
            float best = float.MaxValue;
            foreach (Stain s in stains)
            {
                if (!s.Transform || s.Transform.parent != root) continue;   // only things on the desk
                if (s.Life - (Time.time - s.Born) < 0.6f) continue;           // already being cleaned up, or drying out
                Vector3 p = s.Transform.position;
                float d = new Vector2(p.x - from.x, p.z - from.z).sqrMagnitude;
                if (d >= best || !allowed(p)) continue;
                best = d;
                at = p;
            }
            return best < float.MaxValue;
        }

        /// <summary>The sponge passes over (x, z): stains on the desk there shrink away fast.</summary>
        public void CleanArea(Vector3 centre, float radius)
        {
            foreach (Stain s in stains)
            {
                if (!s.Transform || s.Transform.parent != root) continue; // only things on the desk
                Vector3 p = s.Transform.position;
                if (new Vector2(p.x - centre.x, p.z - centre.z).magnitude > radius + s.FullScale.x * 0.3f) continue;
                // Ends its life within a quarter second (shrinking as it goes).
                float left = s.Life - (Time.time - s.Born);
                if (left > 0.25f) s.Born -= left - 0.25f;
            }
            for (int i = drops.Count - 1; i >= 0; i--)
                if (new Vector2(drops[i].Position.x - centre.x, drops[i].Position.z - centre.z).magnitude < radius) drops.RemoveAt(i);
        }

        /// <summary>Instant: everything goes.</summary>
        public void Clean()
        {
            for (int i = stains.Count - 1; i >= 0; i--) Remove(i);
            drops.Clear();
        }

        void Remove(int i)
        {
            if (stains[i].Transform) Destroy(stains[i].Transform.gameObject);
            stains.RemoveAt(i);
        }

        static float Overshoot(float x)
        {
            // Lands, overshoots a touch and settles: reads as wet and squashy instead of a sticker appearing.
            float u = x - 1f;
            return 1f + 2.70158f * u * u * u + 1.70158f * u * u;
        }

        void Update()
        {
            if (desk) root.gameObject.SetActive(desk.Shown);
            float now = Time.time, dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 1f / 30f);

            for (int i = stains.Count - 1; i >= 0; i--)
            {
                Stain s = stains[i];
                if (!s.Transform)
                {
                    stains.RemoveAt(i);
                    continue;
                }
                float age = now - s.Born, left = s.Life - age;
                if (left <= 0f)
                {
                    Remove(i);
                    continue;
                }

                // Stuck on the avatar: shaken hard enough, it flies off (and splats wherever it lands).
                if (s.Flingable)
                {
                    Vector3 p = s.Transform.position;
                    if (s.Primed)
                    {
                        Vector3 v = (p - s.LastPosition) / dt;
                        s.Shake = Mathf.Lerp(s.Shake, Mathf.Min(((v - s.LastVelocity) / dt).magnitude, 200f), 1f - Mathf.Exp(-dt * 10f));
                        s.LastVelocity = v;
                    }
                    s.LastPosition = p;
                    s.Primed = true;
                    float chance = Mathf.Max(0f, s.Shake - flingShake) * 0.12f + Mathf.Max(0f, s.LastVelocity.magnitude - 0.6f) * 0.4f;
                    if (age > 0.6f && Random.value < chance * dt)
                    {
                        Vector3 size = Vector3.Scale(s.FullScale, s.Transform.parent ? s.Transform.parent.lossyScale : Vector3.one);
                        float d = Mathf.Clamp(Mathf.Max(size.x, size.y) * 0.7f, 0.012f, 0.12f);
                        Drop(p, s.LastVelocity * 1.1f + Random.insideUnitSphere * 0.3f + Vector3.up * 0.3f, s.Colour, d, d * 0.9f);
                        Remove(i);
                        Flung++;
                        continue;
                    }
                }

                // Grow in with a wobble, spread (puddles), shrink away at the end of life.
                bool growing = s.GrowTime > 0f && age < s.GrowTime;
                bool spreading = s.Puddle && age < 4f;
                float fade = Mathf.Clamp01(left / Mathf.Min(4f, s.Life * 0.25f));
                if (growing || spreading || fade < 1f)
                {
                    float k = !growing ? 1f : s.Dry ? Mathf.SmoothStep(0.3f, 1f, age / s.GrowTime) : Overshoot(age / s.GrowTime);
                    Vector3 scale = s.FullScale * (k * fade);
                    if (s.Puddle)
                    {
                        float spread = 1f + 0.12f * Mathf.SmoothStep(0f, 1f, age / 4f);
                        scale.x *= spread;
                        scale.z *= spread;
                    }
                    s.Transform.localScale = scale;
                }
                else if (s.GrowTime > 0f && s.Transform.localScale != s.FullScale && age < s.GrowTime + 0.2f)
                    s.Transform.localScale = s.FullScale; // settled exactly
            }

            // Drops: fall; land on the desk and splat (or grow a puddle); fall off its edge and vanish.
            foreach (var list in dropBatches.Values) list.Clear();
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                Droplet d = drops[i];
                float before = d.Position.y;
                d.Velocity += Vector3.down * gravity * dt;
                d.Position += d.Velocity * dt;
                float ground = DeskScene.SurfaceHeight(d.Position.x, d.Position.z);
                if (before >= ground && d.Position.y < ground)
                {
                    Land(d);
                    drops.RemoveAt(i);
                    continue;
                }
                if (d.Position.y < -1.5f)
                {
                    drops.RemoveAt(i);
                    continue;
                }
                drops[i] = d;
                if (!dropBatches.TryGetValue(d.Colour, out var batch)) dropBatches[d.Colour] = batch = new List<Matrix4x4>();
                if (batch.Count < 1023)
                    batch.Add(Matrix4x4.TRS(d.Position, Quaternion.LookRotation(d.Velocity.sqrMagnitude > 1e-6f ? d.Velocity : Vector3.down) * Quaternion.Euler(90f, 0f, 0f),
                        new Vector3(d.Size, d.Size * Mathf.Clamp(1f + d.Velocity.magnitude * 0.25f, 1f, 2.5f), d.Size)));
            }
            if (desk && !desk.Shown) return;
            foreach (var pair in dropBatches)
                if (pair.Value.Count > 0)
                    Graphics.RenderMeshInstanced(new RenderParams(StainMaterial(pair.Key)), sphere, 0, pair.Value);
        }

        /// <summary>A drop hit the desk: grow the nearest puddle of its colour, or splat (stretched the way it flew).</summary>
        void Land(Droplet d)
        {
            // Wet plips as drops land (not every one: a downpour would just be a hiss).
            if (Time.time >= nextDripSound)
            {
                nextDripSound = Time.time + Random.Range(0.12f, 0.3f);
                Sfx.Play("drip", Mathf.Clamp(d.Size * 25f, 0.15f, 0.5f), 0.15f);
            }
            var flow = new Vector2(d.Velocity.x, d.Velocity.z);
            foreach (Stain s in stains)
            {
                if (!s.Puddle || !s.Transform || s.Colour != d.Colour) continue;
                Vector3 p = s.Transform.position;
                float r = s.FullScale.x * 0.5f;
                if (new Vector2(p.x - d.Position.x, p.z - d.Position.z).magnitude > r + d.Puddle * 0.5f) continue;
                // Area adds up: a puddle grows slower the bigger it gets, up to about 28 cm across.
                float grown = Mathf.Min(0.14f, Mathf.Sqrt(r * r + d.Puddle * d.Puddle * 0.35f));
                s.FullScale = new Vector3(grown * 2f, s.FullScale.y, grown * 2f * (s.FullScale.z / Mathf.Max(1e-4f, s.FullScale.x)));
                s.Transform.localScale = s.FullScale;
                s.Born = Time.time - Mathf.Max(s.GrowTime, 4f); // landing drops refresh it
                return;
            }
            DeskStain(d.Position, d.Puddle, d.Colour, 100f, 0.2f, true, flow);
        }

        // ---------- shapes, made once at start ----------

        /// <summary>The main body of a splat texture is this far from the middle (texture widths); spatters go further out.</summary>
        const float SplatRadius = 0.3f;

        /// <summary>A flat square lying face up (y = 0), half as wide as given, uv across it.</summary>
        static Mesh FlatQuad(float half)
        {
            var mesh = new Mesh { name = "Splat" };
            mesh.vertices = new[] { new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half), new Vector3(half, 0f, half), new Vector3(half, 0f, -half) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A splat's shape as a white texture with alpha: a wobbly-edged body and, for a splat, little drops
        /// around it and spikes shooting out (each ending in a drop); a puddle is rounder with only a drop or two.
        /// The edge is a touch thicker than the middle, like liquid.
        /// </summary>
        static Texture2D MakeSplat(int seed, bool puddle)
        {
            const int n = 256;
            var random = new System.Random(seed);
            float R() => (float)random.NextDouble();
            const int waves = 6;
            var amplitude = new float[waves];
            var frequency = new float[waves];
            var phase = new float[waves];
            for (int k = 0; k < waves; k++)
            {
                frequency[k] = 2 + k * (puddle ? 1 : 2) + random.Next(0, 2);
                amplitude[k] = (puddle ? 0.07f : 0.11f) / (1f + k * 0.6f) * (0.5f + R());
                phase[k] = R() * 6.2832f;
            }
            int dotCount = puddle ? 2 : 14, spikeCount = puddle ? 0 : 7;
            var dots = new List<Vector3>(); // x, y (from the middle), radius
            for (int k = 0; k < dotCount; k++)
            {
                float a = R() * 6.2832f, d = Mathf.Lerp(SplatRadius * 1.15f, puddle ? 0.4f : 0.47f, R());
                dots.Add(new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, Mathf.Lerp(0.007f, puddle ? 0.022f : 0.026f, R() * R())));
            }
            var spikes = new (float angle, float length, float width)[spikeCount];
            for (int k = 0; k < spikeCount; k++)
            {
                spikes[k] = (R() * 6.2832f, Mathf.Lerp(0.05f, 0.15f, R()), Mathf.Lerp(0.014f, 0.03f, R()));
                float tip = SplatRadius + spikes[k].length + 0.012f;
                dots.Add(new Vector3(Mathf.Cos(spikes[k].angle) * tip, Mathf.Sin(spikes[k].angle) * tip, spikes[k].width * 0.75f));
            }

            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f);
                float r = p.magnitude, a = Mathf.Atan2(p.y, p.x);
                float edge = SplatRadius;
                for (int k = 0; k < waves; k++) edge += amplitude[k] * SplatRadius * Mathf.Sin(frequency[k] * a + phase[k]);
                float d = r - edge; // < 0 inside the body
                foreach ((float angle, float length, float width) in spikes)
                {
                    float da = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    float along = r * Mathf.Cos(da), across = Mathf.Abs(r * Mathf.Sin(da));
                    if (along < SplatRadius * 0.7f || along > SplatRadius + length) continue;
                    float w = width * (1f - Mathf.Clamp01((along - SplatRadius) / length) * 0.8f);
                    d = Mathf.Min(d, across - w);
                }
                foreach (Vector3 dot in dots) d = Mathf.Min(d, (p - new Vector2(dot.x, dot.y)).magnitude - dot.z);
                float alpha = Mathf.Clamp01(0.5f - d * n * 0.6f); // a soft edge, a pixel or two wide
                float depth = Mathf.Clamp01(-d / (SplatRadius * 0.25f));
                alpha *= Mathf.Lerp(1f, puddle ? 0.78f : 0.88f, depth);
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = puddle ? "Puddle" : "Splat", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>
        /// A dusting of powder as a white texture with alpha: no hard edge and no spatters, just a soft patch with
        /// a lumpy outline that thins out unevenly (thicker in drifts), grainy all through, with a scatter of
        /// specks and little clumps round it. The grain also greys the white a touch so it reads as dry.
        /// </summary>
        static Texture2D MakeDust(int seed)
        {
            const int n = 256;
            var random = new System.Random(seed);
            float R() => (float)random.NextDouble();
            const int waves = 5;
            var amplitude = new float[waves];
            var frequency = new float[waves];
            var phase = new float[waves];
            for (int k = 0; k < waves; k++)
            {
                frequency[k] = 2 + k * 2 + random.Next(0, 2);
                amplitude[k] = 0.14f / (1f + k * 0.5f) * (0.5f + R());
                phase[k] = R() * 6.2832f;
            }
            var specks = new List<Vector3>(); // x, y (from the middle), radius
            for (int k = 0; k < 40; k++)
            {
                float a = R() * 6.2832f, d = Mathf.Lerp(SplatRadius * 0.7f, 0.46f, Mathf.Sqrt(R()));
                specks.Add(new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, Mathf.Lerp(0.003f, 0.012f, R() * R())));
            }
            Vector2 drift = new Vector2(R() * 50f, R() * 50f);

            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f);
                float r = p.magnitude, a = Mathf.Atan2(p.y, p.x);
                float edge = SplatRadius;
                for (int k = 0; k < waves; k++) edge += amplitude[k] * SplatRadius * Mathf.Sin(frequency[k] * a + phase[k]);
                // Thick in the middle, thinning out softly over most of the way to the edge.
                float body = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge * 0.25f, edge * 1.1f, r));
                // Drifts: thicker and thinner in patches.
                float patch = Mathf.PerlinNoise(drift.x + p.x * 9f, drift.y + p.y * 9f);
                float grain = R();
                float alpha = body * Mathf.Lerp(0.35f, 1f, patch) * Mathf.Lerp(0.55f, 1f, grain);
                foreach (Vector3 s in specks)
                {
                    float d = (p - new Vector2(s.x, s.y)).magnitude - s.z;
                    if (d < 0.004f) alpha = Mathf.Max(alpha, Mathf.Clamp01(0.5f - d * n * 0.5f) * 0.85f);
                }
                byte shade = (byte)Mathf.Lerp(222f, 255f, grain);
                pixels[y * n + x] = new Color32(shade, shade, shade, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Dust", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>A lumpy ball about 1 across (an icosphere pushed in and out by a few smooth waves).</summary>
        static Mesh Lumpy(int seed, float lumpiness)
        {
            var random = new System.Random(seed);
            float R() => (float)random.NextDouble();
            Mesh mesh = Icosphere(2);
            Vector3[] v = mesh.vertices;
            var waves = new (Vector3 direction, float frequency, float amplitude, float phase)[4];
            for (int k = 0; k < waves.Length; k++)
                waves[k] = (new Vector3(R() - 0.5f, R() - 0.5f, R() - 0.5f).normalized, 2.5f + k * 1.8f, lumpiness / (1f + k), R() * 6.2832f);
            for (int i = 0; i < v.Length; i++)
            {
                float push = 1f;
                foreach (var w in waves) push += w.amplitude * Mathf.Sin(Vector3.Dot(v[i], w.direction) * w.frequency + w.phase);
                v[i] = v[i] * 0.5f * push;
            }
            mesh.vertices = v;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.name = "Lump";
            return mesh;
        }

        internal static Mesh Icosphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var middles = new Dictionary<long, int>();
            int Middle(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (middles.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) * 0.5f).normalized);
                middles[key] = v.Count - 1;
                return v.Count - 1;
            }
            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>(faces.Count * 4);
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetTriangles(faces, 0);
            return mesh;
        }

        /// <summary>
        /// Whipped cream from a star nozzle: ridges twisting up a cone that steps in as the bag circles round,
        /// to a little curl at the top. Base about 1 across, about 1 tall.
        /// </summary>
        static Mesh PipedSwirl()
        {
            const int rings = 30, segments = 48;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float h = (float)i / rings;
                float profile = Mathf.Pow(1f - h, 0.62f) * (0.84f + 0.16f * Mathf.Cos(h * 2.6f * 2f * Mathf.PI));
                profile *= Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(h * 6f)); // rounded where it meets the plate
                float twist = h * 2.4f * Mathf.PI;
                float curl = Mathf.Pow(h, 3f) * 0.14f;
                for (int j = 0; j < segments; j++)
                {
                    float a = (float)j / segments * 2f * Mathf.PI;
                    float ridge = 1f + 0.12f * Mathf.Cos(a * 8f + twist) * (1f - h * 0.5f);
                    float r = 0.5f * profile * ridge;
                    vertices.Add(new Vector3(Mathf.Cos(a) * r + curl, h * 0.95f, Mathf.Sin(a) * r));
                }
            }
            int Index(int ring, int segment) => ring * segments + (segment % segments);
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < segments; j++)
            {
                triangles.AddRange(new[] { Index(i, j), Index(i + 1, j), Index(i, j + 1) });
                triangles.AddRange(new[] { Index(i, j + 1), Index(i + 1, j), Index(i + 1, j + 1) });
            }
            int bottom = vertices.Count;
            vertices.Add(Vector3.zero);
            for (int j = 0; j < segments; j++) triangles.AddRange(new[] { bottom, Index(0, j), Index(0, j + 1) });
            var mesh = new Mesh { name = "Piped cream" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
