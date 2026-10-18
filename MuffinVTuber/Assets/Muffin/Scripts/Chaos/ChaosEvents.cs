using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// The chaos events themselves, built from simple shapes on the desk (metres; desk top y 0, avatar at the
    /// origin facing the camera at -z). Each leaves some mess behind in Mess, until K.
    /// </summary>
    public sealed partial class ChaosDirector
    {
        static readonly Color WaterColour = new Color(0.45f, 0.72f, 1f, 0.55f);
        static readonly Color TomatoColour = new Color(0.82f, 0.08f, 0.05f, 0.92f);
        static readonly Color CreamColour = new Color(1f, 0.98f, 0.94f, 1f);
        static readonly Color ScorchColour = new Color(0.08f, 0.05f, 0.04f, 0.8f);

        bool extinguish, fireBurning;

        // ---------- helpers ----------

        BendRig Rig => library ? library.ActiveRig : null;

        Transform Make(PrimitiveType type, string name, Material material, Transform parent, Vector3 localPosition, Vector3 localScale, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            return go.transform;
        }

        Transform NewRoot(string name, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            t.position = position;
            return t;
        }

        /// <summary>A spot on the desk away from the avatar's base (x, z), at desk height.</summary>
        static Vector3 DeskSpot(params Vector2[] choices)
        {
            Vector2 c = choices[Random.Range(0, choices.Length)] + Random.insideUnitCircle * 0.05f;
            return new Vector3(c.x, Mathf.Max(0f, DeskScene.SurfaceHeight(c.x, c.y)), c.y);
        }

        static Vector3 RandomDeskPoint()
        {
            for (int i = 0; i < 20; i++)
            {
                var p = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-0.7f, 0.3f));
                if (new Vector2(p.x, p.z).magnitude > 0.38f) return p;
            }
            return new Vector3(0.6f, 0f, -0.5f);
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

        readonly Dictionary<Mesh, (float minX, float maxX, float minZ)[]> bandCache = new Dictionary<Mesh, (float, float, float)[]>();

        /// <summary>
        /// A point on the active avatar's front at a share of its height (0 foot .. 1 top) and across (-1..1):
        /// its world position, and the bone and local offset to stick something there.
        /// </summary>
        bool AvatarFront(float up, float across, out Vector3 world, out Transform bone, out Vector3 local)
        {
            world = local = Vector3.zero;
            bone = null;
            BendRig rig = Rig;
            if (!rig || !rig.TryGetComponent(out SkinnedMeshRenderer smr) || !smr.sharedMesh) return false;
            Mesh mesh = smr.sharedMesh;
            if (!bandCache.TryGetValue(mesh, out var bands))
            {
                const int n = 24;
                bands = new (float, float, float)[n];
                for (int i = 0; i < n; i++) bands[i] = (float.MaxValue, float.MinValue, float.MaxValue);
                Bounds b0 = mesh.bounds;
                foreach (Vector3 v in mesh.vertices)
                {
                    int i = Mathf.Clamp((int)((v.y - b0.min.y) / b0.size.y * n), 0, n - 1);
                    bands[i] = (Mathf.Min(bands[i].Item1, v.x), Mathf.Max(bands[i].Item2, v.x), Mathf.Min(bands[i].Item3, v.z));
                }
                bandCache[mesh] = bands;
            }
            Bounds b = mesh.bounds;
            var band = bands[Mathf.Clamp((int)(up * bands.Length), 0, bands.Length - 1)];
            if (band.maxX < band.minX) return false;
            float x = (band.minX + band.maxX) * 0.5f + across * (band.maxX - band.minX) * 0.5f * 0.75f;
            var meshPoint = new Vector3(x, b.min.y + b.size.y * up, band.minZ - 0.016f); // in front of the face parts
            bone = rig.BoneAt(meshPoint.y, out Vector3 rest);
            local = meshPoint - rest;
            world = bone.TransformPoint(local);
            return true;
        }

        Transform FaceOf(BendRig rig) => rig && rig.TryGetComponent(out DigitalFace face) && face.FaceAnchor ? face.FaceAnchor : rig ? rig.HeadBone : null;

        // ---------- sprinklers ----------

        IEnumerator Sprinklers()
        {
            extinguish = true;
            const int count = 320;
            var drops = new Vector3[count];
            var speed = new float[count];
            for (int i = 0; i < count; i++)
            {
                drops[i] = RainPoint(Random.Range(0f, 2.3f));
                speed[i] = Random.Range(4.5f, 6.5f);
            }
            var matrices = new Matrix4x4[count];
            var rp = new RenderParams(rain) { shadowCastingMode = ShadowCastingMode.Off };
            BendRig rig = Rig;
            StartCoroutine(Wet(rig, 8f));
            SoundLoop spray = Sfx.Loop("sprinklers", 0.8f);
            float duration = 8f, nextPuddle = 0.4f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float strength = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((duration - t) / 1.2f);
                int shown = 0, want = Mathf.RoundToInt(count * strength);
                for (int i = 0; i < count; i++)
                {
                    drops[i].y -= speed[i] * Time.deltaTime;
                    float ground = DeskScene.SurfaceHeight(drops[i].x, drops[i].z);
                    if (drops[i].y < (float.IsNegativeInfinity(ground) ? -0.6f : ground)) drops[i] = RainPoint(2.3f + Random.value * 0.3f);
                    if (i < want) matrices[shown++] = Matrix4x4.TRS(drops[i], Quaternion.identity, new Vector3(0.006f, 0.03f, 0.006f));
                }
                if (shown > 0) Graphics.RenderMeshInstanced(rp, capsule, 0, matrices, shown);
                if (debris) debris.Agitate(0.6f * strength);
                spray?.SetVolume(0.8f * strength);
                if (t > nextPuddle && t < duration - 1.5f && mess)
                {
                    nextPuddle = t + 0.4f;
                    mess.DeskStain(RandomDeskPoint(), Random.Range(0.06f, 0.14f), WaterColour, 100f, 2.5f, true);
                }
                yield return null;
            }
            spray?.Stop(0.6f);
            extinguish = false;
        }

        static Vector3 RainPoint(float y) => new Vector3(Random.Range(-1.1f, 1.1f), y, Random.Range(-0.75f, 0.35f));

        /// <summary>The avatar goes shiny-wet for the rain, drying off over 20 s after.</summary>
        IEnumerator Wet(BendRig rig, float rainSeconds)
        {
            if (!rig || !rig.TryGetComponent(out SkinnedMeshRenderer smr)) yield break;
            Material[] materials = smr.materials;
            var smooth = new List<(Material m, string prop, float dry, float wet)>();
            foreach (Material m in materials)
            {
                if (m.HasProperty("_Smoothness")) smooth.Add((m, "_Smoothness", m.GetFloat("_Smoothness"), 0.95f));
                if (m.HasProperty("roughnessFactor")) smooth.Add((m, "roughnessFactor", m.GetFloat("roughnessFactor"), 0.05f));
            }
            for (float t = 0f; t < 1f; t += Time.deltaTime)
            {
                foreach (var s in smooth) if (s.m) s.m.SetFloat(s.prop, Mathf.Lerp(s.dry, s.wet, t));
                yield return null;
            }
            yield return new WaitForSeconds(rainSeconds - 1f);
            for (float t = 0f; t < 20f; t += Time.deltaTime)
            {
                foreach (var s in smooth) if (s.m) s.m.SetFloat(s.prop, Mathf.Lerp(s.wet, s.dry, t / 20f));
                yield return null;
            }
            foreach (var s in smooth) if (s.m) s.m.SetFloat(s.prop, s.dry);
        }

        // ---------- fire ----------

        /// <summary>Turn a camera-facing quad toward the camera: upright (a flame) or fully (a puff).</summary>
        void FaceCamera(Transform t, bool upright)
        {
            if (!cam) return;
            Vector3 d = t.position - cam.transform.position;
            if (upright) d.y = 0f;
            if (d.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(d);
        }

        /// <summary>
        /// A small fire on the desk: a heap of crumpled paper catches, tongues of flame lick up out of it, sparks
        /// fly, smoke rolls up and away, the light flickers on everything; the paper chars black and a scorch mark
        /// spreads under it. Sprinklers put it out in a hiss of steam. The charred paper stays as mess.
        /// </summary>
        IEnumerator FireEvent()
        {
            if (fireBurning) yield break;
            fireBurning = true;
            extinguish = false;
            Vector3 spot = DeskSpot(new Vector2(-0.62f, -0.12f), new Vector2(0.68f, -0.18f), new Vector2(0.5f, -0.62f), new Vector2(-0.78f, -0.58f), new Vector2(0.22f, -0.62f));
            Transform root = NewRoot("Fire", spot);
            const float life = 12f;
            if (mess) mess.DeskStain(spot, 0.17f, ScorchColour, 300f, life * 0.7f, false, default, false);
            Sfx.Play("fire_ignite");
            SoundLoop crackle = Sfx.Loop("fire_crackle", 0f);
            bool hissed = false;

            // What's burning: crumpled paper, white at first, charring as it goes.
            var block = new MaterialPropertyBlock();
            var paper = new List<Renderer>();
            Color fresh = new Color(0.94f, 0.92f, 0.86f), charred = new Color(0.12f, 0.09f, 0.07f);
            for (int i = 0; i < 5; i++)
            {
                Vector2 c = Random.insideUnitCircle * 0.06f;
                float s = Random.Range(0.06f, 0.09f);
                Transform lump = MakeMesh(JunkMeshes.CrumpledPaper, "Paper", mess ? mess.BlobMaterial(Color.white) : pieTin, root,
                    new Vector3(c.x, s * 0.35f, c.y), new Vector3(s, s * 0.8f, s));
                lump.localRotation = Random.rotation;
                paper.Add(lump.GetComponent<Renderer>());
            }

            // Tongues of flame (camera-facing quads with the flame shader), each its own size and flicker.
            var tongues = new List<(Transform t, Renderer r, float w, float h, Vector2 offset, float seed)>();
            void Tongue(float w, float h, Vector2 offset)
            {
                Transform t = Make(PrimitiveType.Quad, "Flame", flame, root, Vector3.zero, Vector3.zero, false);
                tongues.Add((t, t.GetComponent<Renderer>(), w, h, offset, Random.Range(0f, 50f)));
            }
            Tongue(0.24f, 0.46f, Vector2.zero);
            Tongue(0.17f, 0.32f, new Vector2(-0.06f, 0.02f));
            Tongue(0.16f, 0.3f, new Vector2(0.065f, -0.01f));
            Tongue(0.13f, 0.24f, new Vector2(0.01f, 0.05f));
            Tongue(0.12f, 0.22f, new Vector2(-0.02f, -0.05f));

            var lightObject = new GameObject("Fire light");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.18f, -0.06f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.18f);
            light.range = 2f;

            var sparks = new List<(Transform t, Vector3 v, float born, float life)>();
            var puffs = new List<(Transform t, Renderer r, float born, float life, Vector3 drift, float size, Color colour, float seed)>();

            // Sparks ride the heat up, wandering, and wink out; smoke spreads and thins as it rises and drifts off.
            void Animate(float t, float dt)
            {
                for (int i = sparks.Count - 1; i >= 0; i--)
                {
                    var (s, v, born, span) = sparks[i];
                    float age = t - born;
                    if (age > span || !s)
                    {
                        if (s) Destroy(s.gameObject);
                        sparks.RemoveAt(i);
                        continue;
                    }
                    v += new Vector3(Mathf.PerlinNoise(age * 4f, born) - 0.5f, 0f, Mathf.PerlinNoise(born, age * 4f) - 0.5f) * dt * 3f;
                    s.position += v * dt;
                    s.localScale = Vector3.one * 0.008f * (1f - age / span);
                    sparks[i] = (s, v, born, span);
                }
                for (int i = puffs.Count - 1; i >= 0; i--)
                {
                    var (p, r, born, span, drift, full, colour, seed) = puffs[i];
                    float k = (t - born) / span;
                    if (k >= 1f || !p)
                    {
                        if (p) Destroy(p.gameObject);
                        puffs.RemoveAt(i);
                        continue;
                    }
                    p.position += (drift + new Vector3(0.05f, 0f, 0f) * k) * dt;
                    p.localScale = Vector3.one * Mathf.Lerp(0.1f, full, Mathf.Sqrt(k));
                    FaceCamera(p, false);
                    Color c = colour;
                    c.a *= Mathf.SmoothStep(0f, 1f, k * 5f) * (1f - Mathf.SmoothStep(0.35f, 1f, k));
                    block.Clear();
                    block.SetColor("_Color", c);
                    block.SetFloat("_Seed", seed);
                    r.SetPropertyBlock(block);
                }
            }

            float size = 0f, nextPuff = 0f, sparkOwed = 0f, time = 0f;
            for (; time < life || size > 0.02f; time += Time.deltaTime)
            {
                float t = time, dt = Time.deltaTime;
                bool ending = t >= life || extinguish;
                size = Mathf.MoveTowards(size, ending ? 0f : 1f, dt * (ending ? (extinguish ? 1.6f : 0.6f) : 0.9f));
                float burnt = Mathf.Clamp01(t / (life * 0.8f));

                foreach (var (tr, r, w, h, offset, seed) in tongues)
                {
                    float grow = size * (0.85f + 0.3f * Mathf.PerlinNoise(t * 3f + seed, seed));
                    tr.localScale = new Vector3(w * grow, h * grow, 1f);
                    tr.localPosition = new Vector3(offset.x, 0.02f + h * grow * 0.5f, offset.y);
                    FaceCamera(tr, true);
                    block.Clear();
                    block.SetFloat("_Seed", seed);
                    block.SetFloat("_Intensity", Mathf.Clamp01(size * 1.15f));
                    r.SetPropertyBlock(block);
                }
                foreach (Renderer r in paper)
                {
                    block.Clear();
                    block.SetColor("_BaseColor", Color.Lerp(fresh, charred, Mathf.SmoothStep(0f, 1f, burnt * 1.3f)));
                    r.SetPropertyBlock(block);
                }
                light.intensity = 2.8f * size * (0.7f + 0.6f * Mathf.PerlinNoise(t * 14f, 0.5f));
                crackle?.SetVolume(0.9f * size);
                if (extinguish && !hissed)
                {
                    hissed = true;
                    Sfx.Play("steam_hiss");
                }

                sparkOwed += dt * 14f * size;
                while (sparkOwed >= 1f)
                {
                    sparkOwed -= 1f;
                    Transform spark = Make(PrimitiveType.Sphere, "Spark", flameYellow, transform,
                        spot + new Vector3(Random.Range(-0.05f, 0.05f), 0.1f, Random.Range(-0.05f, 0.05f)), Vector3.one * 0.008f, false);
                    sparks.Add((spark, new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.5f, 1.1f), Random.Range(-0.15f, 0.15f)), t, Random.Range(0.5f, 1.3f)));
                }
                if (t >= nextPuff && (size > 0.05f || extinguish))
                {
                    nextPuff = t + (extinguish ? 0.06f : 0.16f);
                    Transform puff = Make(PrimitiveType.Quad, "Smoke", smoke, transform,
                        spot + new Vector3(Random.Range(-0.04f, 0.04f), 0.3f * Mathf.Max(size, 0.25f), Random.Range(-0.04f, 0.04f)), Vector3.zero, false);
                    Color c = extinguish ? new Color(0.92f, 0.93f, 0.95f, 0.55f) : new Color(0.16f, 0.15f, 0.15f, 0.5f); // steam, or smoke
                    puffs.Add((puff, puff.GetComponent<Renderer>(), t, Random.Range(2.4f, 3.4f),
                        new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(0.22f, 0.36f), Random.Range(-0.04f, 0.06f)), Random.Range(0.4f, 0.6f), c, Random.Range(0f, 50f)));
                }
                Animate(t, dt);
                if (debris && size > 0.5f) debris.PushArea(spot, 0.12f, Vector3.up * 0.6f);
                yield return null;
            }
            foreach (var (tr, _, _, _, _, _) in tongues) Destroy(tr.gameObject);
            Destroy(lightObject);
            crackle?.Stop(0.5f);
            // What's left of the paper stays on the desk, burnt, until it's cleaned up.
            if (mess) mess.Keep(root, 180f);
            fireBurning = false;
            // The last smoke and sparks finish on their own.
            for (; sparks.Count > 0 || puffs.Count > 0; time += Time.deltaTime)
            {
                Animate(time, Time.deltaTime);
                yield return null;
            }
        }

        // ---------- a sneeze on the camera glass ----------

        IEnumerator Slime()
        {
            if (!cam || !slime) yield break;
            const float distance = 0.5f;
            float halfH = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfW = halfH * cam.aspect;
            var drips = new List<(Transform t, MaterialPropertyBlock block, MeshRenderer r, float speed, float delay)>();
            Mesh quad = Mess.PrimitiveMesh(PrimitiveType.Quad);
            int n = Random.Range(7, 11);
            for (int i = 0; i < n; i++)
            {
                float x = Random.Range(-0.92f, 0.92f) * halfW;
                float top = Random.Range(0.3f, 1.05f) * halfH;
                float width = Random.Range(0.06f, 0.14f) * halfH;
                float height = top + halfH * 1.1f;
                var go = new GameObject("Slime");
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = new Vector3(x, top - height * 0.5f, distance);
                go.transform.localScale = new Vector3(width, height, 1f);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = slime;
                r.shadowCastingMode = ShadowCastingMode.Off;
                var block = new MaterialPropertyBlock();
                block.SetFloat("_Aspect", height / width);
                drips.Add((go.transform, block, r, Random.Range(0.05f, 0.18f), Random.Range(0f, 0.6f)));
            }
            const float duration = 14f;
            Sfx.Play("sneeze", 1f, 0.05f); // ah... CHOO: snot all over the lens
            Sfx.Play("slime_splat", 0.7f);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float alpha = Mathf.Clamp01((duration - t) / 3f);
                foreach (var (tr, block, r, speed, delay) in drips)
                {
                    float age = Mathf.Max(0f, t - delay);
                    // A splat that pops in with a wobble, then a drip that runs down the glass.
                    float pop = age < 0.25f ? Mathf.Sin(age / 0.25f * Mathf.PI * 0.5f) * 1.15f : 1f + 0.03f * Mathf.Sin(age * 6f);
                    block.SetFloat("_Pop", Mathf.Min(pop, 1.15f));
                    block.SetFloat("_Length", Mathf.Clamp01((age - 0.2f) * speed));
                    block.SetFloat("_Alpha", age > 0f ? alpha : 0f);
                    r.SetPropertyBlock(block);
                }
                yield return null;
            }
            foreach (var d in drips) if (d.t) Destroy(d.t.gameObject);
        }

        // ---------- drips down the avatar ----------

        Transform MakeMesh(Mesh mesh, string name, Material material, Transform parent, Vector3 localPosition, Vector3 localScale, bool shadows = true)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            return go.transform;
        }

        /// <summary>Where the active avatar's face is, as a share of its height.</summary>
        float FaceUp => Rig && Rig.TryGetComponent(out MeshAvatar avatar) ? avatar.faceFraction : 0.5f;

        /// <summary>
        /// A drip running down the front of the avatar from (up 0..1 of its height, across -1..1): it creeps down in
        /// fits and starts, leaving a thin streak, and drops off onto the desk at the bottom.
        /// </summary>
        public void DripDown(float up, float across, Color colour, float size, float speed = 0.07f)
        {
            if (isActiveAndEnabled && mess) StartCoroutine(DripRoutine(up, across, colour, size, speed));
        }

        IEnumerator DripRoutine(float up, float across, Color colour, float size, float speed)
        {
            BendRig rig = Rig;
            if (!rig || !AvatarFront(up, across, out _, out Transform bone, out Vector3 local)) yield break;
            Transform drip = mess.Blob(bone, local, new Vector3(size, size * 1.4f, size * 0.6f), colour, 60f, 0f, false);
            float streakAt = up, pause = 0f, seed = Random.value * 10f;
            Vector3 streakFrom = local;
            Transform streakBone = bone;
            while (drip && rig == Rig && up > 0.03f)
            {
                float dt = Time.deltaTime;
                if (pause > 0f) pause -= dt;
                else
                {
                    up -= speed * dt * (0.4f + 1.2f * Mathf.PerlinNoise(Time.time * 1.5f, seed));
                    if (Random.value < dt * 0.5f) pause = Random.Range(0.2f, 0.9f); // catches, then runs on
                }
                if (!AvatarFront(up, across, out _, out bone, out local)) break;
                if (drip.parent != bone) drip.SetParent(bone, false);
                drip.localPosition = local;
                drip.localScale = new Vector3(size, size * (1.4f + 0.4f * Mathf.Sin(Time.time * 6f + seed)), size * 0.6f);
                if (streakAt - up > 0.035f)
                {
                    // A thin streak from where the last one ended down to here, so the trail is unbroken.
                    if (streakBone == bone)
                    {
                        float length = (local - streakFrom).magnitude;
                        mess.Blob(bone, (streakFrom + local) * 0.5f, new Vector3(size * 0.5f, length * 1.25f + size * 0.3f, size * 0.3f), colour, 25f, 0.05f, false);
                    }
                    streakAt = up;
                    streakFrom = local;
                    streakBone = bone;
                }
                yield return null;
            }
            if (!drip) yield break;
            Vector3 at = drip.position;
            mess.Forget(drip);
            mess.Drop(at, new Vector3(0f, -0.2f, -0.25f), colour, size, size * 2.2f);
        }

        // ---------- pie to the face ----------

        /// <summary>
        /// A cream pie flies in tumbling and SPLATS: the avatar is knocked back, cream bursts everywhere and rains
        /// down all over the desk, the face is buried in it, drips run down, and the pie slides off and plops onto
        /// the desk. The cream left on the face flies off when the avatar shakes its head.
        /// </summary>
        IEnumerator Pie()
        {
            BendRig rig = Rig;
            Transform face = FaceOf(rig);
            if (!face || !cam || !mess) yield break;
            Transform pie = NewRoot("Pie", cam.transform.position);
            Make(PrimitiveType.Cylinder, "Tin", pieTin, pie, Vector3.zero, new Vector3(0.34f, 0.025f, 0.34f));
            MakeMesh(mess.RandomLump, "Cream", cream, pie, new Vector3(0f, 0.035f, 0f), new Vector3(0.33f, 0.12f, 0.33f));
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f;
                MakeMesh(mess.Swirl, "Swirl", cream, pie, new Vector3(Mathf.Cos(a) * 0.115f, 0.06f, Mathf.Sin(a) * 0.115f), Vector3.one * 0.07f);
            }
            MakeMesh(mess.Swirl, "Top swirl", cream, pie, new Vector3(0f, 0.09f, 0f), Vector3.one * 0.1f);

            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 start = cam.transform.position + cam.transform.right * side * 0.7f - cam.transform.up * 0.45f + cam.transform.forward * 0.8f;
            const float flight = 0.42f;
            Vector3 toCamera = Vector3.back;
            for (float t = 0f; t < flight; t += Time.deltaTime)
            {
                float k = t / flight;
                toCamera = (cam.transform.position - face.position).normalized;
                Vector3 target = face.position + toCamera * 0.06f;
                pie.position = Bezier(start, (start + target) * 0.5f + Vector3.up * 0.35f, target, k);
                // Tumbling in, cream side turning to face the avatar.
                pie.rotation = Quaternion.Slerp(Quaternion.Euler(k * 540f, k * 220f, 0f), Quaternion.FromToRotation(Vector3.up, -toCamera), Mathf.SmoothStep(0.5f, 1f, k));
                yield return null;
            }

            // SPLAT.
            Sfx.Play("pie_splat", 1f, 0.05f);
            if (rig) rig.Bonk(0.4f);
            pie.SetParent(face, true);
            Vector3 stuck = pie.localScale;
            float faceScale = Mathf.Max(0.01f, face.lossyScale.x);
            Vector3 outward = face.InverseTransformDirection(toCamera).normalized;
            // The face buried in cream: big lumps and swirls all over it.
            for (int i = 0; i < 10; i++)
            {
                Vector2 c = Random.insideUnitCircle * 0.13f;
                float s = Random.Range(0.06f, 0.13f);
                mess.Blob(face, new Vector3(c.x, c.y, 0f) / faceScale + outward * (0.015f / faceScale), new Vector3(s, s * 0.85f, s * 0.45f) / faceScale, CreamColour, 45f, 0.05f);
            }
            for (int i = 0; i < 5; i++)
            {
                Vector2 c = Random.insideUnitCircle * 0.14f;
                Transform swirl = mess.Dollop(face, new Vector3(c.x, c.y, 0f) / faceScale + outward * (0.03f / faceScale), Random.Range(0.05f, 0.08f), CreamColour, 45f, 0.1f);
                swirl.localRotation = Quaternion.FromToRotation(Vector3.up, outward + Vector3.up * 0.3f) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }
            // Cream squeezed out all round the tin's rim.
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
                float s = Random.Range(0.05f, 0.09f);
                mess.Blob(face, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (Random.Range(0.15f, 0.19f) / faceScale) + outward * (0.035f / faceScale),
                    new Vector3(s, s * 0.8f, s * 0.6f) / faceScale, CreamColour, 45f, 0.05f);
            }
            // Cream bursts out and rains down all over the desk.
            Vector3 hit = face.position + toCamera * 0.05f;
            for (int i = 0; i < 44; i++)
            {
                Vector3 direction = (toCamera * Random.Range(0.2f, 1f) + Random.insideUnitSphere * 0.9f + Vector3.up * Random.Range(0f, 0.7f)).normalized;
                mess.Drop(hit, direction * Random.Range(0.8f, 2.8f), CreamColour, Random.Range(0.015f, 0.035f), Random.Range(0.025f, 0.06f));
            }
            for (int i = 0; i < 4; i++) DripDown(FaceUp - 0.07f, Random.Range(-0.6f, 0.6f), CreamColour, Random.Range(0.025f, 0.04f), Random.Range(0.05f, 0.09f));
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                pie.localScale = Vector3.Scale(stuck, Vector3.Lerp(new Vector3(1.4f, 0.5f, 1.4f), Vector3.one, t / 0.25f));
                yield return null;
            }
            yield return new WaitForSeconds(1f);

            // It slides down the face, smearing cream as it goes, then drops onto the desk.
            Vector3 from = pie.localPosition;
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 1.1f);
                pie.localPosition = from + Vector3.down * (0.17f / faceScale) * k;
                pie.localRotation *= Quaternion.Euler(0f, 0f, Time.deltaTime * 25f * side);
                yield return null;
            }
            mess.Blob(face, pie.localPosition + outward * (0.01f / faceScale), new Vector3(0.12f, 0.06f, 0.03f) / faceScale, CreamColour, 40f, 0.1f);
            if (!debris)
            {
                Destroy(pie.gameObject);
                yield break;
            }
            debris.AddPiece(pie, new Vector3(0f, -0.3f, -0.4f), 0.15f, 0.03f, disposable: true);
            Sfx.Play("plop", 0.8f);
            yield return new WaitForSeconds(0.7f);
            if (pie) mess.DeskStain(pie.position, 0.16f, CreamColour, 150f, 0.12f, false, new Vector2(0f, -1.5f));
        }

        // ---------- tomatoes ----------

        IEnumerator Fruit()
        {
            int n = Random.Range(4, 7);
            for (int i = 0; i < n; i++)
            {
                StartCoroutine(Tomato());
                yield return new WaitForSeconds(Random.Range(0.15f, 0.3f));
            }
            yield return new WaitForSeconds(1f);
        }

        /// <summary>
        /// A ripe tomato flies in and bursts: juice sprays out in a fan of splatters, pulp sticks and drips (on the
        /// avatar) or splats with seeds (on the desk), and chunks bounce away and roll, leaving juicy trails.
        /// </summary>
        IEnumerator Tomato()
        {
            if (!cam || !mess) yield break;
            BendRig rig = Rig;
            float up = Random.Range(0.3f, 0.85f), across = Random.Range(-0.8f, 0.8f);
            bool atAvatar = Random.value < 0.65f && AvatarFront(up, across, out _, out _, out _);
            Transform bone = null;
            Vector3 local = Vector3.zero, deskTarget = RandomDeskPoint();
            if (atAvatar) AvatarFront(up, across, out _, out bone, out local);
            Transform fruit = NewRoot("Tomato", cam.transform.position);
            MakeMesh(mess.Lumps[0], "Body", tomato, fruit, Vector3.zero, new Vector3(0.13f, 0.11f, 0.13f));
            for (int k = 0; k < 5; k++) // the green star on top
            {
                Transform sepal = Make(PrimitiveType.Sphere, "Leaf", leaf, fruit, Vector3.zero, new Vector3(0.042f, 0.007f, 0.014f));
                sepal.localRotation = Quaternion.Euler(0f, k * 72f, -12f);
                sepal.localPosition = sepal.localRotation * new Vector3(0.018f, 0.054f, 0f);
            }
            Make(PrimitiveType.Cylinder, "Stem", leaf, fruit, new Vector3(0f, 0.062f, 0f), new Vector3(0.008f, 0.01f, 0.008f));
            float side = Random.Range(-1f, 1f);
            Vector3 start = cam.transform.position + cam.transform.right * side * 0.9f - cam.transform.up * Random.Range(0.2f, 0.5f) + cam.transform.forward * 0.7f;
            float flight = Random.Range(0.38f, 0.5f);
            Vector3 target = deskTarget;
            for (float t = 0f; t < flight; t += Time.deltaTime)
            {
                target = bone ? bone.TransformPoint(local) : deskTarget;
                fruit.position = Bezier(start, (start + target) * 0.5f + Vector3.up * 0.3f, target, t / flight);
                fruit.rotation = Quaternion.Euler(t * 900f, t * 400f, 0f);
                yield return null;
            }
            Destroy(fruit.gameObject);
            Sfx.Play("splat", 0.9f, 0.12f);

            Color seeds = new Color(0.98f, 0.86f, 0.5f), pulp = new Color(0.95f, 0.25f, 0.12f);
            Vector3 toCamera = (cam.transform.position - target).normalized;
            if (bone)
            {
                if (rig) rig.Bonk(0.12f);
                // Pulp splatted on the avatar (it slides down and gets shaken off), with seeds in it.
                mess.Blob(bone, local, new Vector3(0.12f, 0.1f, 0.025f), TomatoColour, 40f, 0.04f);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 o = local + new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(-0.05f, 0.05f), -0.004f);
                    float s = Random.Range(0.03f, 0.06f);
                    mess.Blob(bone, o, new Vector3(s, s, 0.012f), i % 2 == 0 ? TomatoColour : pulp, 35f, 0.05f);
                }
                for (int i = 0; i < 5; i++)
                    mess.Blob(bone, local + new Vector3(Random.Range(-0.04f, 0.04f), Random.Range(-0.04f, 0.04f), -0.01f), new Vector3(0.012f, 0.007f, 0.004f), seeds, 35f, 0.05f);
                for (int i = 0; i < 2; i++) DripDown(up - 0.03f, Mathf.Clamp(across + Random.Range(-0.15f, 0.15f), -0.9f, 0.9f), TomatoColour, Random.Range(0.018f, 0.026f), Random.Range(0.06f, 0.1f));
                // Juice sprays back out and down onto the desk.
                for (int i = 0; i < 22; i++)
                {
                    Vector3 direction = (toCamera * Random.Range(0.3f, 1f) + Random.insideUnitSphere * 0.8f + Vector3.up * Random.Range(0f, 0.5f)).normalized;
                    mess.Drop(target, direction * Random.Range(0.6f, 2.2f), TomatoColour, Random.Range(0.008f, 0.02f), Random.Range(0.015f, 0.035f));
                }
            }
            else
            {
                // A big wet splat on the desk with seeds in it, and juice sprayed out flat across the desk.
                mess.DeskStain(target, Random.Range(0.09f, 0.12f), TomatoColour, 150f, 0.07f);
                mess.DeskStain(target + new Vector3(Random.Range(-0.02f, 0.02f), 0f, Random.Range(-0.02f, 0.02f)), 0.05f, pulp, 150f, 0.09f);
                for (int i = 0; i < 7; i++)
                {
                    Vector2 c = Random.insideUnitCircle * 0.06f;
                    mess.Blob(null, target + new Vector3(c.x, 0.004f, c.y), new Vector3(0.012f, 0.005f, 0.008f), seeds, 120f, 0.1f);
                }
                for (int i = 0; i < 20; i++)
                {
                    Vector2 d = Random.insideUnitCircle.normalized;
                    mess.Drop(target + Vector3.up * 0.02f, new Vector3(d.x, 0f, d.y) * Random.Range(0.8f, 2.2f) + Vector3.up * Random.Range(0.3f, 1.2f), TomatoColour,
                        Random.Range(0.008f, 0.018f), Random.Range(0.012f, 0.03f));
                }
            }
            // Chunks bounce away and roll, leaving juicy trails; a flap of skin flops down.
            for (int i = 0; i < 5 && debris; i++)
            {
                float s = Random.Range(0.03f, 0.05f);
                Transform chunk = MakeMesh(mess.RandomLump, "Tomato chunk", tomato, null, Vector3.zero, new Vector3(s, s * 0.8f, s));
                chunk.position = target + Vector3.up * 0.02f;
                chunk.rotation = Random.rotation;
                debris.AddPiece(chunk, new Vector3(Random.Range(-1f, 1f), Random.Range(0.5f, 1.5f), Random.Range(-1f, 0.4f)), s * 0.45f, s * 0.4f, disposable: true, ball: true, trail: TomatoColour);
            }
            if (debris)
            {
                Transform skin = Make(PrimitiveType.Sphere, "Tomato skin", tomato, null, Vector3.zero, new Vector3(0.07f, 0.012f, 0.055f));
                skin.position = target + Vector3.up * 0.03f;
                debris.AddPiece(skin, new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0.4f, 1f), Random.Range(-0.6f, 0.2f)), 0.035f, 0.006f, disposable: true);
            }
        }

        // ---------- whipped cream ----------

        /// <summary>
        /// A can of whipped cream swoops in, nozzle down, and sprays a thick stream: piled-up swirls on the avatar's
        /// top first (they get flung off when it moves, and drip), then a wiggly line of swirls along the desk.
        /// The hands squash the ones on the desk when they go through them.
        /// </summary>
        IEnumerator WhippedCream()
        {
            if (!mess) yield break;
            BendRig rig = Rig;
            // Where it goes: three or four spots on the avatar's top front, then a wiggly line across the desk.
            var targets = new List<(Transform parent, Vector3 local, float size)>();
            int onAvatar = 0;
            for (int i = 0; i < 4; i++)
                if (AvatarFront(Random.Range(0.8f, 0.94f), Random.Range(-0.55f, 0.55f), out _, out Transform bone, out Vector3 local))
                {
                    targets.Add((bone, local, Random.Range(0.08f, 0.11f)));
                    onAvatar++;
                }
            float z0 = Random.Range(-0.55f, -0.4f);
            for (float x = 0.75f; x >= -0.75f; x -= 0.065f)
            {
                var p = new Vector3(x, 0f, z0 + Mathf.Sin(x * 6f) * 0.06f);
                if (new Vector2(p.x, p.z).magnitude > 0.35f) targets.Add((null, p, Random.Range(0.065f, 0.09f)));
            }
            Vector3 TargetPoint(int i) => targets[i].parent ? targets[i].parent.TransformPoint(targets[i].local) : targets[i].local;

            // The can: a big blue can, white cap and nozzle pointing down.
            Transform can = NewRoot("Whipped cream can", TargetPoint(0) + new Vector3(0.6f, 0.5f, -0.2f));
            Make(PrimitiveType.Cylinder, "Can", canBody, can, new Vector3(0f, 0.17f, 0f), new Vector3(0.11f, 0.13f, 0.11f));
            Make(PrimitiveType.Sphere, "Shoulder", canBody, can, new Vector3(0f, 0.04f, 0f), new Vector3(0.11f, 0.05f, 0.11f));
            Make(PrimitiveType.Cylinder, "Cap", cream, can, new Vector3(0f, 0.01f, 0f), new Vector3(0.055f, 0.025f, 0.055f));
            Make(PrimitiveType.Cylinder, "Nozzle", cream, can, new Vector3(0f, -0.03f, 0f), new Vector3(0.022f, 0.025f, 0.022f));
            Vector3 Nozzle() => can.position - can.up * 0.055f;

            SoundLoop hiss = Sfx.Loop("cream_spray", 0.8f);
            float dwellAvatar = 0.35f, dwellDesk = 0.11f;
            var made = new Transform[targets.Count];
            var beads = new List<(Transform t, Vector3 from, float born, int target)>();
            int current = 0;
            float onTarget = 0f, nextBead = 0f;
            for (float t = 0f; current < targets.Count || beads.Count > 0; t += Time.deltaTime)
            {
                float dt = Time.deltaTime;
                if (current < targets.Count)
                {
                    // Swoop over the current spot, nozzle aimed at it, wobbling as it sprays.
                    Vector3 aim = TargetPoint(current);
                    Vector3 hover = aim + new Vector3(0.05f, current < onAvatar ? 0.32f : 0.28f, -0.12f);
                    can.position = Vector3.Lerp(can.position, hover, 1f - Mathf.Exp(-dt * 14f)) + Random.insideUnitSphere * 0.002f;
                    can.rotation = Quaternion.Slerp(can.rotation, Quaternion.FromToRotation(Vector3.up, (can.position - aim).normalized), 1f - Mathf.Exp(-dt * 12f));
                    onTarget += dt;
                    if (t >= nextBead)
                    {
                        nextBead = t + 0.018f;
                        Transform bead = Make(PrimitiveType.Sphere, "Cream", cream, transform, Nozzle(), Vector3.one * 0.026f, false);
                        beads.Add((bead, bead.position, t, current));
                    }
                    if (onTarget >= (current < onAvatar ? dwellAvatar : dwellDesk))
                    {
                        onTarget = 0f;
                        current++;
                    }
                }
                for (int i = beads.Count - 1; i >= 0; i--)
                {
                    var (bead, from, born, index) = beads[i];
                    float f = (t - born) / 0.14f;
                    Vector3 to = TargetPoint(index);
                    if (f >= 1f)
                    {
                        Destroy(bead.gameObject);
                        beads.RemoveAt(i);
                        if (!made[index])
                        {
                            // The first of the stream to arrive starts a swirl, which builds up while the can stays on it.
                            var (parent, local, size) = targets[index];
                            float build = (index < onAvatar ? dwellAvatar : dwellDesk) + 0.1f;
                            made[index] = mess.Dollop(parent, local, size, CreamColour, parent ? 60f : 150f, build);
                            if (parent)
                            {
                                Vector3 outward = parent.InverseTransformDirection((cam ? cam.transform.position - to : Vector3.back).normalized);
                                made[index].localRotation = Quaternion.FromToRotation(Vector3.up, (outward + Vector3.up).normalized) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                            }
                        }
                        else if (Random.value < 0.08f) mess.Drop(to + Vector3.up * 0.02f, Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f, CreamColour, 0.014f, 0.02f);
                        continue;
                    }
                    bead.position = Vector3.Lerp(from, to, f) + Vector3.down * Mathf.Sin(f * Mathf.PI) * 0.02f;
                }
                yield return null;
            }
            hiss?.Stop(0.25f);
            // Cream on the avatar starts to run.
            for (int i = 0; i < Mathf.Min(2, onAvatar); i++) DripDown(0.8f, Random.Range(-0.4f, 0.4f), CreamColour, 0.03f, 0.05f);
            // The can is tossed away, spinning.
            Vector3 v = new Vector3(Random.Range(-1f, 1f), 2.2f, 0.6f);
            for (float t = 0f; t < 0.7f; t += Time.deltaTime)
            {
                v += Vector3.down * 3f * Time.deltaTime;
                can.position += v * Time.deltaTime;
                can.rotation *= Quaternion.Euler(Time.deltaTime * 500f, 0f, Time.deltaTime * 200f);
                yield return null;
            }
            Destroy(can.gameObject);
        }

        // ---------- clean-up: bucket and sponge ----------

        bool scrubbing;

        /// <summary>K: a bucket pops onto the desk and a soapy sponge scrubs it clean, row by row.</summary>
        public void ScrubClean()
        {
            if (!scrubbing) StartCoroutine(Scrub());
        }

        IEnumerator Scrub()
        {
            scrubbing = true;
            Color bucketBlue = new Color(0.25f, 0.5f, 0.85f), sponge = new Color(1f, 0.85f, 0.25f), scrubber = new Color(0.2f, 0.65f, 0.3f),
                foam = new Color(0.97f, 0.98f, 1f), suds = new Color(0.95f, 0.97f, 1f, 0.55f);
            Material Tint(Color c) => mess ? mess.BlobMaterial(c) : cream;
            Mesh Lump() => mess ? mess.RandomLump : null;
            var block = new MaterialPropertyBlock();

            // Soap bubbles: they wobble as they float up, shimmer, and pop.
            var bubbles = new List<(Transform t, Renderer r, float born, float life, Vector3 drift, float size, float seed)>();
            void Bubble(Vector3 at, float size, Vector3 drift, float life)
            {
                if (!bubble) return;
                Transform b = Make(PrimitiveType.Sphere, "Bubble", bubble, transform, Vector3.zero, Vector3.zero, false);
                b.position = at;
                bubbles.Add((b, b.GetComponent<Renderer>(), Time.time, life, drift, size, Random.Range(0f, 50f)));
            }
            void Bubbles()
            {
                float now = Time.time, dt = Time.deltaTime;
                for (int i = bubbles.Count - 1; i >= 0; i--)
                {
                    var (b, r, born, life, drift, size, seed) = bubbles[i];
                    float k = (now - born) / life;
                    if (k >= 1f || !b)
                    {
                        if (b) Destroy(b.gameObject);
                        bubbles.RemoveAt(i);
                        continue;
                    }
                    b.position += (drift + new Vector3(Mathf.Sin(now * 3f + seed) * 0.04f, 0f, Mathf.Cos(now * 2.3f + seed) * 0.03f)) * dt;
                    // Grows in, wobbles, and at the end swells a touch and pops.
                    float s = size * Mathf.Min(1f, k * 8f) * (k > 0.92f ? 1f + (k - 0.92f) * 3f : 1f);
                    float wobble = 0.08f * Mathf.Sin(now * 9f + seed);
                    b.localScale = new Vector3(s * (1f + wobble), s * (1f - wobble), s * (1f + wobble * 0.5f));
                    block.Clear();
                    block.SetFloat("_Seed", seed);
                    block.SetFloat("_Alpha", k > 0.92f ? 1f - (k - 0.92f) / 0.08f : 1f);
                    r.SetPropertyBlock(block);
                }
            }

            // The bucket: soapy water heaped with foam, bubbles drifting off it.
            Vector3 bucketSpot = new Vector3(0.88f, 0f, 0.18f);
            Transform bucket = NewRoot("Bucket", bucketSpot);
            Make(PrimitiveType.Cylinder, "Pail", Tint(bucketBlue), bucket, new Vector3(0f, 0.09f, 0f), new Vector3(0.2f, 0.09f, 0.2f));
            Make(PrimitiveType.Cylinder, "Rim", Tint(bucketBlue * 0.8f), bucket, new Vector3(0f, 0.18f, 0f), new Vector3(0.21f, 0.006f, 0.21f));
            for (int i = 0; i < 9; i++)
            {
                Vector2 c = Random.insideUnitCircle * 0.065f;
                float s = Random.Range(0.05f, 0.08f);
                MakeMesh(Lump(), "Foam", Tint(foam), bucket, new Vector3(c.x, 0.19f + Random.Range(0f, 0.015f), c.y), new Vector3(s, s * 0.7f, s), false);
            }
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                bucket.localScale = Vector3.one * Mathf.Sin(t / 0.25f * Mathf.PI * 0.6f) * 1.15f;
                yield return null;
            }
            bucket.localScale = Vector3.one;
            Sfx.Play("bucket_plonk");
            SoundLoop squeak = Sfx.Loop("scrub", 0.7f);
            float nextPops = 0.5f;

            // The sponge, with a cap of foam on it.
            Transform pad = NewRoot("Sponge", bucketSpot + Vector3.up * 0.2f);
            Make(PrimitiveType.Cube, "Sponge", Tint(sponge), pad, new Vector3(0f, 0.035f, 0f), new Vector3(0.14f, 0.05f, 0.09f));
            Make(PrimitiveType.Cube, "Scrubber", Tint(scrubber), pad, new Vector3(0f, 0.005f, 0f), new Vector3(0.14f, 0.012f, 0.09f));
            for (int i = 0; i < 4; i++)
                MakeMesh(Lump(), "Foam", Tint(foam), pad, new Vector3(Random.Range(-0.05f, 0.05f), 0.065f, Random.Range(-0.03f, 0.03f)), Vector3.one * Random.Range(0.035f, 0.05f), false);

            // A zigzag over the whole desk, back to front, skipping the avatar's own spot.
            var path = new List<Vector3> { pad.position };
            bool leftToRight = false;
            for (float z = 0.26f; z >= -0.72f; z -= 0.16f, leftToRight = !leftToRight)
            {
                path.Add(new Vector3(leftToRight ? -1.02f : 1.02f, 0f, z));
                path.Add(new Vector3(leftToRight ? 1.02f : -1.02f, 0f, z));
            }
            path.Add(bucketSpot + Vector3.up * 0.2f);
            const float speed = 2.6f; // m/s
            float nextBubble = 0f, nextSuds = 0f, nextBucketBubble = 0f, clock = 0f;
            for (int leg = 1; leg < path.Count; leg++)
            {
                Vector3 a = path[leg - 1], b = path[leg];
                float length = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z)) + Mathf.Abs(a.y - b.y);
                for (float s = 0f; s < length; s += speed * Time.deltaTime)
                {
                    clock += Time.deltaTime;
                    Vector3 p = Vector3.Lerp(a, b, s / Mathf.Max(0.01f, length));
                    float ground = DeskScene.SurfaceHeight(p.x, p.z);
                    bool lifted = leg == 1 || leg == path.Count - 1;
                    // Ride over the avatar's base and the keyboard; scrub with a wiggle.
                    float y = lifted ? p.y : Mathf.Max(0f, float.IsNegativeInfinity(ground) ? 0f : ground);
                    if (!lifted && new Vector2(p.x, p.z).magnitude < 0.36f) y = 0.12f;
                    pad.position = new Vector3(p.x, y, p.z) + new Vector3(0f, 0f, Mathf.Sin(clock * 40f) * 0.02f);
                    pad.rotation = Quaternion.Euler(0f, Mathf.Sin(clock * 25f) * 12f, 0f);
                    if (!lifted)
                    {
                        if (mess) mess.CleanArea(pad.position, 0.14f);
                        if (debris) debris.CleanArea(pad.position, 0.12f);
                        if (clock >= nextBubble)
                        {
                            nextBubble = clock + 0.035f;
                            Bubble(pad.position + new Vector3(Random.Range(-0.08f, 0.08f), 0.05f, Random.Range(-0.06f, 0.06f)), Random.Range(0.015f, 0.055f),
                                new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(0.12f, 0.35f), Random.Range(-0.06f, 0.04f)), Random.Range(0.9f, 1.8f));
                        }
                        // A soapy wet sheen behind the sponge, drying off in a few seconds.
                        if (mess && clock >= nextSuds && y < 0.1f)
                        {
                            nextSuds = clock + 0.06f;
                            mess.DeskStain(pad.position + new Vector3(Random.Range(-0.04f, 0.04f), 0f, Random.Range(-0.03f, 0.03f)), Random.Range(0.05f, 0.08f), suds, 2.5f, 0.1f, true, default, false);
                        }
                    }
                    if (clock >= nextBucketBubble)
                    {
                        nextBucketBubble = clock + 0.25f;
                        Bubble(bucketSpot + new Vector3(Random.Range(-0.06f, 0.06f), 0.22f, Random.Range(-0.06f, 0.06f)), Random.Range(0.02f, 0.045f),
                            new Vector3(Random.Range(-0.03f, 0.03f), Random.Range(0.08f, 0.2f), Random.Range(-0.03f, 0.03f)), Random.Range(1.5f, 2.5f));
                    }
                    Bubbles();
                    squeak?.SetVolume(lifted ? 0.15f : 0.7f);
                    if (clock >= nextPops)
                    {
                        nextPops = clock + Random.Range(0.8f, 1.6f);
                        Sfx.Play("bubbles", 0.5f, 0.15f);
                    }
                    yield return null;
                }
            }
            squeak?.Stop(0.3f);
            Destroy(pad.gameObject);
            // Anything left (on the avatar, mid-air) goes too, and the bucket leaves; the last bubbles float off and pop.
            if (debris) debris.Clean();
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                bucket.localScale = Vector3.one * (1f - t / 0.25f);
                Bubbles();
                yield return null;
            }
            Destroy(bucket.gameObject);
            scrubbing = false;
            while (bubbles.Count > 0)
            {
                Bubbles();
                yield return null;
            }
        }

        // ---------- water spill ----------

        IEnumerator WaterSpill()
        {
            Sfx.Play("water_spill");
            Vector3 spot = DeskSpot(new Vector2(0.34f, -0.24f), new Vector2(-0.42f, -0.18f), new Vector2(0.62f, 0.05f), new Vector2(-0.68f, 0.02f));
            Transform pitcher = NewRoot("Pitcher", spot);
            Transform body = new GameObject("Body").transform;
            body.SetParent(pitcher, false);
            Make(PrimitiveType.Cylinder, "Glass", glass, body, new Vector3(0f, 0.11f, 0f), new Vector3(0.13f, 0.11f, 0.13f));
            Make(PrimitiveType.Cylinder, "Water", water, body, new Vector3(0f, 0.08f, 0f), new Vector3(0.115f, 0.075f, 0.115f), false);
            Make(PrimitiveType.Cube, "Handle", glass, body, new Vector3(0.085f, 0.12f, 0f), new Vector3(0.02f, 0.12f, 0.03f));
            // Pops in, wobbles…
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                pitcher.localScale = Vector3.one * Mathf.Sin(t / 0.25f * Mathf.PI * 0.6f) * 1.15f;
                yield return null;
            }
            pitcher.localScale = Vector3.one;
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                pitcher.rotation = Quaternion.Euler(Mathf.Sin(t * 30f) * 7f, 0f, Mathf.Sin(t * 23f) * 5f);
                yield return null;
            }
            // …and tips over toward the camera, pivoting on its front edge.
            Vector3 pivot = spot + new Vector3(0f, 0f, -0.065f);
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                float k = t / 0.35f;
                pitcher.rotation = Quaternion.Euler(-90f * k * k, 0f, 0f);
                pitcher.position = pivot + pitcher.rotation * new Vector3(0f, 0f, 0.065f);
                yield return null;
            }
            pitcher.rotation = Quaternion.Euler(-90f, 0f, 0f);
            pitcher.position = pivot + pitcher.rotation * new Vector3(0f, 0f, 0.065f);
            // Water gushes out and runs toward the front of the desk, carrying things along.
            Vector3 mouth = pitcher.TransformPoint(new Vector3(0f, 0.22f, 0f));
            Vector3 flow = mouth;
            for (float t = 0f; t < 2.6f; t += Time.deltaTime)
            {
                if (mess && t < 1.4f)
                    for (int i = 0; i < 2; i++)
                        mess.Drop(mouth + Random.insideUnitSphere * 0.02f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0f, 0.4f), Random.Range(-1.4f, -0.5f)), WaterColour, 0.014f, 0.05f);
                flow += new Vector3(Random.Range(-0.05f, 0.05f), 0f, -0.32f) * Time.deltaTime;
                if (flow.z > DeskScene.DeskMinZ + 0.03f)
                {
                    if (mess && Random.value < 0.25f) mess.DeskStain(flow + Random.insideUnitSphere * 0.03f, Random.Range(0.05f, 0.1f), WaterColour, 100f, 0.5f, true);
                    if (debris) debris.PushArea(flow, 0.14f, new Vector3(0f, 0f, -0.6f));
                }
                else if (mess && Random.value < 0.4f)
                    mess.Drop(new Vector3(flow.x, 0f, DeskScene.DeskMinZ + 0.005f), new Vector3(0f, -0.2f, -0.2f), WaterColour, 0.012f, 0.03f); // over the edge
                yield return null;
            }
            if (mess) mess.Keep(pitcher, 60f);
        }
    }
}
