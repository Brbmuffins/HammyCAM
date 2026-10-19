using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// Soft camera-facing puffs (steam, smoke, pepper, powder) on the shared Muffin/Smoke material, pooled: emit
    /// one with a colour, a speed and a life; it swells, drifts (slowed by drag, rising or sinking) and thins out.
    /// Premultiplied alpha, so they show in a transparent OBS capture too.
    /// </summary>
    public sealed class PuffPool
    {
        sealed class Puff
        {
            public Transform T;
            public MeshRenderer R;
            public Vector3 Velocity;
            public Color Colour;
            public float Age, Life, Size, Grow, Drag, Rise, Seed;
            public bool Live;
        }

        readonly Material material;
        readonly int max;
        readonly string name;
        readonly List<Puff> pool = new List<Puff>();
        readonly Stack<Puff> free = new Stack<Puff>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        Transform root;

        /// <summary>Every puff's opacity is multiplied by this (a room haze thinning as the air clears).</summary>
        public float AlphaScale = 1f;
        public int Live { get; private set; }

        public PuffPool(Material material, int max, string name)
        {
            this.material = material;
            this.max = max;
            this.name = name;
        }

        public void Emit(Vector3 at, Vector3 velocity, Color colour, float size, float life, float grow = 2.5f, float drag = 1f, float rise = 0f)
        {
            Puff p = Take();
            if (p == null) return;
            p.T.position = at;
            p.Velocity = velocity;
            p.Colour = colour;
            p.Size = size;
            p.Life = Mathf.Max(0.05f, life);
            p.Grow = grow;
            p.Drag = drag;
            p.Rise = rise;
            p.Seed = Random.Range(0f, 50f);
            p.T.localScale = Vector3.one * size * 0.4f;
        }

        public void Update(Camera cam, float dt)
        {
            foreach (Puff p in pool)
            {
                if (!p.Live) continue;
                p.Age += dt;
                float k = p.Age / p.Life;
                if (k >= 1f)
                {
                    Release(p);
                    continue;
                }
                p.T.position += p.Velocity * dt;
                p.Velocity *= Mathf.Exp(-p.Drag * dt);
                p.Velocity.y += p.Rise * dt;
                p.T.localScale = Vector3.one * p.Size * Mathf.Lerp(0.4f, p.Grow, Mathf.Sqrt(k));
                if (cam) p.T.rotation = Quaternion.LookRotation(p.T.position - cam.transform.position);
                Color c = p.Colour;
                c.a *= Mathf.SmoothStep(0f, 1f, k * 6f) * (1f - Mathf.SmoothStep(0.45f, 1f, k)) * AlphaScale;
                block.Clear();
                block.SetColor("_Color", c);
                block.SetFloat("_Seed", p.Seed);
                p.R.SetPropertyBlock(block);
            }
        }

        /// <summary>Everything gone at once (the avatar was switched off).</summary>
        public void Clear()
        {
            free.Clear();
            Live = 0;
            foreach (Puff p in pool)
            {
                p.Live = false;
                if (p.T) p.T.gameObject.SetActive(false);
                free.Push(p);
            }
        }

        public void Dispose()
        {
            if (root) Object.Destroy(root.gameObject);
        }

        Puff Take()
        {
            if (!root) root = new GameObject(name).transform;
            Puff p;
            if (free.Count > 0) p = free.Pop();
            else if (pool.Count < max)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Puff";
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                var r = go.GetComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterial = material;
                p = new Puff { T = go.transform, R = r };
                pool.Add(p);
            }
            else return null;
            p.Live = true;
            p.Age = 0f;
            p.T.gameObject.SetActive(true);
            Live++;
            return p;
        }

        void Release(Puff p)
        {
            if (!p.Live) return;
            p.Live = false;
            Live--;
            if (p.T) p.T.gameObject.SetActive(false);
            free.Push(p);
        }
    }

    /// <summary>
    /// The air in the room filling up (pepper, extinguisher powder): Amount 0 clear .. 1 a total whiteout. Big soft
    /// puffs fill the picture as Amount rises, and above veilFrom a veil in front of the camera goes opaque, like
    /// the flour bag's haze. The air clears by itself (clearFlat a second, plus clearShare of what's there).
    /// </summary>
    public sealed class RoomHaze
    {
        readonly PuffPool room;
        readonly Material material;
        readonly List<(Transform t, MeshRenderer r)> veils = new List<(Transform, MeshRenderer)>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly int maxPuffs;
        Transform veilRoot;

        public Color Colour;
        public float Amount;
        public float VeilFrom = 0.5f;
        public float ClearFlat = 0.035f, ClearShare = 0.1f;

        public RoomHaze(Material smoke, Color colour, int maxPuffs = 110)
        {
            material = smoke;
            Colour = colour;
            this.maxPuffs = maxPuffs;
            room = new PuffPool(smoke, maxPuffs, "Room haze");
        }

        public void Update(Camera cam, float dt)
        {
            if (!cam) return;
            Amount = Mathf.Clamp01(Amount - (ClearFlat + Amount * ClearShare) * dt);
            int target = Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, Amount) * maxPuffs * 0.9f);
            for (int guard = 0; room.Live < target && guard < 4; guard++)
            {
                float distance = Random.Range(0.8f, 3.4f);
                float height = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                Vector3 at = cam.ViewportToWorldPoint(new Vector3(Random.Range(-0.2f, 1.2f), Random.Range(-0.2f, 1.2f), distance));
                Color c = Colour;
                c.a *= Mathf.Lerp(0.4f, 0.85f, Amount);
                room.Emit(at, Random.insideUnitSphere * 0.06f + Vector3.up * 0.02f, c, height * Random.Range(0.3f, 0.65f), Random.Range(3f, 5.5f), 1.6f, 0.4f);
            }
            room.AlphaScale = Mathf.Lerp(0.55f, 1f, Amount);
            room.Update(cam, dt);
            UpdateVeil(cam);
        }

        void UpdateVeil(Camera cam)
        {
            float amount = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(VeilFrom, 1f, Amount));
            if (amount <= 0.01f && veils.Count == 0) return;
            if (!veilRoot)
            {
                veilRoot = new GameObject("Haze veil").transform;
                for (int i = 0; i < 3; i++)
                {
                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(veilRoot, false);
                    var r = go.GetComponent<MeshRenderer>();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.sharedMaterial = material;
                    veils.Add((go.transform, r));
                }
            }
            for (int i = 0; i < veils.Count; i++)
            {
                var (t, r) = veils[i];
                float alpha = amount * (i == 0 ? 0.98f : 0.9f);
                bool show = alpha > 0.01f;
                if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
                if (!show) continue;
                float distance = cam.nearClipPlane + 0.1f + i * 0.05f;
                float height = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float diagonal = Mathf.Sqrt(height * height * (1f + cam.aspect * cam.aspect));
                t.position = cam.transform.position + cam.transform.forward * distance;
                t.rotation = Quaternion.LookRotation(cam.transform.forward);
                t.localScale = Vector3.one * diagonal * 6.5f;
                Color c = Colour;
                c.a = alpha;
                block.Clear();
                block.SetColor("_Color", c);
                block.SetFloat("_Seed", 11f + i * 17f);
                r.SetPropertyBlock(block);
            }
        }

        public void Clear()
        {
            Amount = 0f;
            room.Clear();
            foreach (var (t, _) in veils) if (t) t.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            room.Dispose();
            if (veilRoot) Object.Destroy(veilRoot.gameObject);
        }
    }

    /// <summary>
    /// Something stuck on the camera glass, in front of the lens: a seed that slides slowly down, or a splash that
    /// pops on and runs down in a drip (the sneeze's drip shader, in any colour). It goes by itself after a while.
    /// </summary>
    public sealed class GlassItem : MonoBehaviour
    {
        const float Distance = 0.5f;
        float born, life, slide;
        bool drip;
        Color colour;
        float length, delay;
        Vector3 fullScale;
        MeshRenderer r;
        MaterialPropertyBlock block;

        /// <summary>A small thing (a seed) stuck at viewport point (0..1), size a share of the frame's height.</summary>
        public static GlassItem Stick(Mesh mesh, Material material, Vector2 viewport, float size, float slide, float life, float tilt = 0f)
        {
            Camera cam = Camera.main;
            if (!cam || !material) return null;
            var go = new GameObject("On the glass");
            go.transform.SetParent(cam.transform, false);
            float halfH = Distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfW = halfH * cam.aspect;
            go.transform.localPosition = new Vector3((viewport.x * 2f - 1f) * halfW, (viewport.y * 2f - 1f) * halfH, Distance);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            float s = size * halfH * 2f;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var item = go.AddComponent<GlassItem>();
            item.fullScale = new Vector3(s, s * 0.62f, s * 0.3f);
            go.transform.localScale = item.fullScale * 0.2f;
            item.born = Time.time;
            item.life = life;
            item.slide = slide * halfH * 2f;
            item.r = renderer;
            return item;
        }

        /// <summary>A splash (soda, juice) that pops on at viewport point and runs down; width a share of the frame's height.</summary>
        public static GlassItem Splash(Color colour, Vector2 viewport, float width, float life)
        {
            Camera cam = Camera.main;
            Material slime = ChaosDirector.Instance ? ChaosDirector.Instance.slime : null;
            if (!cam || !slime) return null;
            float halfH = Distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfW = halfH * cam.aspect;
            float top = (viewport.y * 2f - 1f) * halfH;
            float w = width * halfH * 2f;
            // A splat with a short run below it (a long thin run reads as a stick, not a splash).
            float height = Mathf.Min(top + halfH * 1.1f, w * Random.Range(1.8f, 2.6f));
            var go = new GameObject("Splash on the glass");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3((viewport.x * 2f - 1f) * halfW, top - height * 0.5f, Distance);
            go.transform.localScale = new Vector3(w, Mathf.Max(height, w), 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Mess.PrimitiveMesh(PrimitiveType.Quad);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = slime;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var item = go.AddComponent<GlassItem>();
            item.drip = true;
            item.colour = colour;
            item.born = Time.time;
            item.life = life;
            item.r = renderer;
            item.block = new MaterialPropertyBlock();
            item.length = Random.Range(0.04f, 0.09f); // the run creeps only part way down while it lasts
            item.delay = Random.Range(0f, 0.15f);
            item.block.SetFloat("_Aspect", Mathf.Max(height, w) / w);
            item.block.SetColor("_Color", colour);
            return item;
        }

        void Update()
        {
            float age = Time.time - born;
            if (age > life)
            {
                Destroy(gameObject);
                return;
            }
            if (drip)
            {
                float a = Mathf.Max(0f, age - delay);
                float pop = a < 0.25f ? Mathf.Sin(a / 0.25f * Mathf.PI * 0.5f) * 1.15f : 1f + 0.03f * Mathf.Sin(a * 6f);
                block.SetFloat("_Pop", Mathf.Min(pop, 1.15f));
                block.SetFloat("_Length", Mathf.Clamp01((a - 0.2f) * length));
                block.SetFloat("_Alpha", a > 0f ? Mathf.Clamp01((life - age) / 2.5f) : 0f);
                r.SetPropertyBlock(block);
                return;
            }
            // A seed: lands with a little squash, slides down the glass, shrinks away at the end.
            float k = Mathf.Clamp01(age / 0.12f);
            float end = Mathf.Clamp01((life - age) / 0.4f);
            transform.localScale = fullScale * Mathf.Lerp(0.2f, 1f, k) * end;
            transform.localPosition += Vector3.down * slide * Time.deltaTime * Mathf.Clamp01(age - 0.6f);
        }
    }
}
