using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The goldfish bowl: water that sloshes as the bowl rocks (hard turns slop it over the rim onto the desk) and a
    /// goldfish swimming round in it, blowing bubbles. A really hard shake (or !shake, or the face slam) flips the
    /// fish clean out: it lands on the desk, flops about, slapping wet marks, and then leaps back into the bowl with
    /// a splash.
    /// </summary>
    public sealed class GoldfishBowl : GagAvatar
    {
        [Tooltip("The bowl's middle and inside radius, the top opening, and the water level (rest, avatar space).")]
        public Vector3 bowlCentre;
        public float bowlRadius = 0.28f;
        [Tooltip("The bowl's inside half-height, if it's a little taller than round (0 = round).")]
        public float bowlHalfHeight;
        public float openingY, openingRadius = 0.18f, waterY, gravelY;

        static readonly Color WaterSplash = new Color(0.55f, 0.75f, 1f, 0.6f), Orange = new Color(1f, 0.42f, 0.02f), Fin = new Color(1f, 0.6f, 0.15f);

        Anchor bowl;
        Transform waterT, fish, tail;
        Mesh waterMesh;
        Vector3[] waterVerts;
        int topRingStart, topCentre;
        Vector2 slope, slopeVelocity;
        Vector3 lastCentre, lastVelocity;
        float angle, nextSpill, nextBubble, nextFlip;
        bool fishOut;
        readonly List<(Transform t, float born)> bubbles = new List<(Transform, float)>();

        public bool FishOut => fishOut;
        public override void TestMoment() => Run(FlipOut());
        public override string TestReport => fishOut ? "fish out" : "fish in";
        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "flip", Label = "A really hard shake flips the fish out" });
            into.Add(new AvatarFeature { Key = "slop", Label = "Water slops over the rim" });
            into.Add(new AvatarFeature { Key = "bubbles", Label = "The fish blows bubbles" });
        }

        protected override bool UsesFace => false;

        public override string MessName => "fish-bowl water (and a fish)";

        protected override void Begin()
        {
            if (bowlCentre == Vector3.zero) bowlCentre = new Vector3(0f, body.size.y * 0.47f, 0f);
            if (openingY == 0f) openingY = body.max.y - 0.02f;
            if (waterY == 0f) waterY = bowlCentre.y + bowlRadius * 0.55f;
            if (gravelY == 0f) gravelY = bowlCentre.y - bowlRadius * 0.7f;
            bowl = Pin(bowlCentre);
            BuildWater();
            BuildFish();
            lastCentre = Pos(bowl);
        }

        // ---- Water ---------------------------------------------------------------------------------------

        void BuildWater()
        {
            Material water = Chaos ? Chaos.water : null;
            if (!water) return;
            // Clearer than the spill water, so the fish shows through it.
            water = new Material(water) { name = "Bowl water" };
            water.SetColor("_BaseColor", new Color(0.55f, 0.85f, 1f, 0.28f));
            float R = bowlRadius * 0.98f, H = (bowlHalfHeight > 0f ? bowlHalfHeight : bowlRadius) * 0.98f, y0 = gravelY - bowlCentre.y, y1 = waterY - bowlCentre.y;
            const int rings = 10, around = 32;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float y = Mathf.Lerp(y0, y1, (float)j / rings);
                float r = R * Mathf.Sqrt(Mathf.Max(0.0001f, 1f - (y / H) * (y / H)));
                for (int i = 0; i < around; i++)
                {
                    float a = Mathf.PI * 2f * i / around;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = j * around + i, b = j * around + (i + 1) % around, c = (j + 1) * around + (i + 1) % around, d = (j + 1) * around + i;
                    tris.AddRange(new[] { a, c, b, a, d, c });
                }
            topRingStart = rings * around;
            topCentre = verts.Count;
            verts.Add(new Vector3(0f, y1, 0f));
            for (int i = 0; i < around; i++) tris.AddRange(new[] { topCentre, topRingStart + (i + 1) % around, topRingStart + i });
            int bottomCentre = verts.Count;
            verts.Add(new Vector3(0f, y0, 0f));
            for (int i = 0; i < around; i++) tris.AddRange(new[] { bottomCentre, i, (i + 1) % around });
            waterMesh = new Mesh { name = "Bowl water" };
            waterMesh.SetVertices(verts);
            waterMesh.SetTriangles(tris, 0);
            waterMesh.RecalculateNormals();
            waterMesh.MarkDynamic();
            waterVerts = verts.ToArray();
            var go = new GameObject("Bowl water");
            go.transform.SetParent(bowl.Bone, false);
            go.transform.localPosition = bowl.Local;
            go.AddComponent<MeshFilter>().sharedMesh = waterMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = water;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterT = go.transform;
        }

        void Slosh(float dt)
        {
            Transform bone = bowl.Bone;
            if (!bone) return;
            Vector3 centre = Pos(bowl);
            Vector3 vel = (centre - lastCentre) / dt;
            Vector3 acc = Vector3.ClampMagnitude((vel - lastVelocity) / dt, 40f);
            lastCentre = centre;
            lastVelocity = vel;
            // The surface wants to lie square to gravity minus the bowl's jolt, seen from inside the bowl.
            Vector3 n = bone.InverseTransformDirection((Vector3.up * 9.8f + acc).normalized);
            Vector2 target = new Vector2(-n.x / Mathf.Max(0.3f, n.y), -n.z / Mathf.Max(0.3f, n.y));
            float w = Mathf.PI * 2f * 1.8f;
            slopeVelocity += ((target - slope) * w * w - slopeVelocity * 2f * 0.25f * w) * Mathf.Min(dt, 1f / 30f);
            slope += slopeVelocity * Mathf.Min(dt, 1f / 30f);
            slope = Vector2.ClampMagnitude(slope, 0.9f);
            if (waterMesh)
            {
                float y1 = waterY - bowlCentre.y;
                for (int i = topRingStart; i <= topCentre; i++)
                {
                    Vector3 q = waterVerts[i];
                    waterVerts[i] = new Vector3(q.x, y1 + slope.x * q.x + slope.y * q.z, q.z);
                }
                waterMesh.vertices = waterVerts;
                waterMesh.RecalculateNormals();
            }
            // Over the rim: the high side's wave slops out and splashes on the desk.
            float rise = slope.magnitude * openingRadius;
            if (On("slop") && rise > openingY - waterY && Time.time >= nextSpill && Mess.Instance)
            {
                nextSpill = Time.time + 0.05f;
                Vector3 dirLocal = new Vector3(slope.x, 0f, slope.y).normalized;
                Vector3 dir = bone.TransformDirection(dirLocal);
                Vector3 rim = bone.TransformPoint(bowl.Local + new Vector3(0f, openingY - bowlCentre.y, 0f) + dirLocal * openingRadius);
                for (int i = 0; i < 3; i++)
                    Drop(rim, dir * Random.Range(0.4f, 0.9f) + Vector3.up * Random.Range(0.3f, 0.8f) + vel * 0.6f, WaterSplash, Random.Range(0.012f, 0.02f), Random.Range(0.035f, 0.05f));
                BitSound("slosh", 0.4f);
            }
        }

        // ---- The fish ------------------------------------------------------------------------------------

        void BuildFish()
        {
            float L = bowlRadius * 0.55f;
            fish = new GameObject("Goldfish").transform;
            fish.SetParent(bowl.Bone, false);
            Transform bodyT = Item(PrimitiveType.Sphere, Orange, new Vector3(0.45f, 0.62f, 1f) * L);
            bodyT.SetParent(fish, false);
            tail = new GameObject("Tail").transform;
            tail.SetParent(fish, false);
            tail.localPosition = new Vector3(0f, 0f, -0.48f * L);
            Transform fin = Item(PrimitiveType.Cube, Fin, new Vector3(0.03f, 0.55f, 0.5f) * L);
            fin.SetParent(tail, false);
            fin.localPosition = new Vector3(0f, 0f, -0.22f * L);
            fin.localRotation = Quaternion.Euler(45f, 0f, 0f);
            Transform dorsal = Item(PrimitiveType.Cube, Fin, new Vector3(0.02f, 0.25f, 0.35f) * L);
            dorsal.SetParent(fish, false);
            dorsal.localPosition = new Vector3(0f, 0.3f * L, -0.05f * L);
            dorsal.localRotation = Quaternion.Euler(30f, 0f, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                Transform eye = Item(PrimitiveType.Sphere, Color.white, Vector3.one * 0.13f * L);
                eye.SetParent(fish, false);
                eye.localPosition = new Vector3(s * 0.19f * L, 0.08f * L, 0.3f * L);
                Transform pupil = Item(PrimitiveType.Sphere, new Color(0.05f, 0.05f, 0.08f), Vector3.one * 0.6f);
                pupil.SetParent(eye, false);
                pupil.localPosition = new Vector3(s * 0.3f, 0f, 0.25f);
            }
        }

        void Swim(float dt)
        {
            if (fishOut || !fish) return;
            float r = bowlRadius * 0.42f, depth = Mathf.Lerp(gravelY, waterY, 0.5f) - bowlCentre.y;
            angle += dt * 0.55f;
            float bob = Mathf.Sin(Time.time * 1.3f) * bowlRadius * 0.08f;
            var local = new Vector3(Mathf.Cos(angle) * r, depth + bob, Mathf.Sin(angle) * r);
            var ahead = new Vector3(-Mathf.Sin(angle), 0.1f * Mathf.Cos(Time.time * 1.3f), Mathf.Cos(angle));
            fish.localPosition = bowl.Local + local;
            fish.localRotation = Quaternion.LookRotation(ahead);
            if (tail) tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 9f) * 28f, 0f);
            // Bubbles from its mouth, up to the surface.
            if (On("bubbles") && Time.time >= nextBubble)
            {
                nextBubble = Time.time + Random.Range(0.5f, 1.4f);
                Material bubble = Chaos ? Chaos.bubble : null;
                if (bubble && bubbles.Count < 8)
                {
                    Transform b = Item(PrimitiveType.Sphere, Color.white, Vector3.one * Random.Range(0.012f, 0.02f));
                    b.GetComponent<MeshRenderer>().sharedMaterial = bubble;
                    b.SetParent(bowl.Bone, false);
                    b.localPosition = fish.localPosition + fish.localRotation * new Vector3(0f, 0f, bowlRadius * 0.2f);
                    bubbles.Add((b, Time.time));
                }
            }
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var (b, born) = bubbles[i];
                if (!b)
                {
                    bubbles.RemoveAt(i);
                    continue;
                }
                b.localPosition += new Vector3(Mathf.Sin((Time.time - born) * 8f) * 0.01f * dt, 0.09f * dt, 0f);
                if (b.localPosition.y - bowl.Local.y > waterY - bowlCentre.y)
                {
                    Destroy(b.gameObject);
                    bubbles.RemoveAt(i);
                }
            }
        }

        /// <summary>The fish flips out: lands on the desk beside the bowl, flops about, then leaps back in.</summary>
        IEnumerator FlipOut()
        {
            if (fishOut || !fish) yield break;
            fishOut = true;
            nextFlip = Time.time + 12f;
            Sfx.Play("slosh", 0.8f, 0.1f);
            float side = Random.value < 0.5f ? -1f : 1f;
            Vector3 start = fish.position;
            Vector3 land = transform.position + new Vector3(side * (body.extents.x + Random.Range(0.12f, 0.25f)), 0f, body.min.z * 0.6f - Random.Range(0.05f, 0.2f));
            land.y = Mathf.Max(0f, DeskScene.SurfaceHeight(land.x, land.z)) + 0.02f;
            fish.SetParent(null, true);
            for (int i = 0; i < 10; i++)
                Drop(Pos(bowl) + Vector3.up * (openingY - bowlCentre.y), new Vector3(side * Random.Range(0.2f, 0.7f), Random.Range(0.6f, 1.3f), -Random.Range(0.1f, 0.5f)),
                    WaterSplash, Random.Range(0.012f, 0.02f), 0.04f);
            yield return Leap(start, land, 0.75f, side * 540f);
            Sfx.Play("flop", 0.9f, 0.1f);
            // Flop, flop, flop.
            float until = Time.time + Random.Range(3f, 4.2f);
            Vector3 at = land;
            while (Time.time < until && fish)
            {
                Vector3 hop = at + new Vector3(Random.Range(-0.06f, 0.06f), 0f, Random.Range(-0.05f, 0.05f));
                hop.y = Mathf.Max(0f, DeskScene.SurfaceHeight(hop.x, hop.z)) + 0.02f;
                float yaw = Random.Range(0f, 360f);
                float flight = Random.Range(0.28f, 0.42f);
                Vector3 from = at;
                for (float t = 0f; t < flight && fish; t += Time.deltaTime)
                {
                    float k = t / flight;
                    fish.position = Vector3.Lerp(from, hop, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * Random.Range(0.06f, 0.09f);
                    fish.rotation = Quaternion.Euler(0f, yaw, 90f * Mathf.Sign(Mathf.Sin(k * Mathf.PI * 2f + 0.3f)) * (1f - Mathf.Abs(Mathf.Cos(k * Mathf.PI)) * 0.4f));
                    if (tail) tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 26f) * 40f, 0f);
                    yield return null;
                }
                at = hop;
                Sfx.Play("flop", 0.5f, 0.25f);
                if (Mess.Instance) Mess.Instance.DeskStain(at, Random.Range(0.03f, 0.05f), WaterSplash, 40f, 0.1f);
                yield return new WaitForSeconds(Random.Range(0.12f, 0.35f));
            }
            // A leap back into the bowl, and a splash.
            Vector3 mouthOfBowl = Pos(bowl) + Vector3.up * (openingY - bowlCentre.y + 0.02f);
            yield return Leap(at, mouthOfBowl, 0.7f, -side * 360f);
            Sfx.Play("plop", 0.8f, 0.1f);
            for (int i = 0; i < 14; i++)
                Drop(mouthOfBowl, Outward() * Random.Range(0.2f, 0.6f) + Vector3.up * Random.Range(0.8f, 1.5f), WaterSplash, Random.Range(0.012f, 0.02f), 0.04f);
            if (fish) fish.SetParent(bowl.Bone, true);
            fishOut = false;
        }

        IEnumerator Leap(Vector3 from, Vector3 to, float seconds, float spinDegrees)
        {
            float height = Mathf.Max(0.25f, (to.y - from.y) + 0.3f);
            for (float t = 0f; t < seconds && fish; t += Time.deltaTime)
            {
                float k = t / seconds;
                fish.position = Vector3.Lerp(from, to, k) + Vector3.up * 4f * height * k * (1f - k);
                fish.rotation = Quaternion.Euler(spinDegrees * k, 0f, 90f);
                if (tail) tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 30f) * 40f, 0f);
                yield return null;
            }
            if (fish) fish.position = to;
        }

        protected override void Tick(float dt)
        {
            Slosh(dt);
            Swim(dt);
            if (!fishOut && On("flip") && meter.Shake > 60f && Time.time >= nextFlip) Run(FlipOut());
        }

        public override void Burst()
        {
            slopeVelocity += Random.insideUnitCircle.normalized * 3f;
            Run(FlipOut());
        }

        public override void OnSlam()
        {
            slopeVelocity += new Vector2(0f, -4f);
            Run(FlipOut());
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            // Switched off mid-flop: the fish goes straight home.
            if (fishOut && fish && bowl.Bone) fish.SetParent(bowl.Bone, true);
            fishOut = false;
        }
    }
}
