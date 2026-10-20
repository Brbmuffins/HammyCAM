using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The bubble tea: tapioca pearls roll about the bottom of the cup (pressed against the glass, where you see
    /// them) and slosh as it moves. Open your mouth and it SLURPS: pearls shoot up the straw and out of the top, arc
    /// through the air and bounce across the desk with drips of tea. !shake is a coughing fit of pearls; the face slam
    /// pops the lid: tea everywhere and the lot of them rolling about.
    /// </summary>
    public sealed class BubbleTea : GagAvatar
    {
        [Tooltip("The straw's open top and bottom end, the cup's inside floor and the tea's surface (rest, avatar space).")]
        public Vector3 strawTop = new Vector3(-0.031f, 1.142f, -0.017f), strawBottom = new Vector3(0.132f, 0.11f, 0.074f);
        public Vector3 cupBottom = new Vector3(0f, 0.014f, 0f), teaTop = new Vector3(0f, 0.709f, 0f);
        [Tooltip("The cup's inside radius at the bottom and at the tea's surface.")]
        public float radiusBottom = 0.214f, radiusTop = 0.27f;
        public int pearlCount = 34;

        static readonly Color Pearl = new Color(0.1f, 0.06f, 0.05f), Tea = new Color(0.78f, 0.58f, 0.4f, 0.85f);

        struct Ball
        {
            public Transform T;
            public Vector3 P, V; // cup space (the bone), from the floor's middle
        }

        readonly List<Ball> pearls = new List<Ball>();
        Anchor floor, straw;
        Vector3 lastFloor, lastVelocity;
        Material pearlMaterial;
        float r = 0.035f, slurpOwed, nextRefill;

        public override string MessName => "tapioca pearls";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "refill", Label = "Refills itself with pearls" });
        }

        public override void TestMoment() => Run(Slurp(10));
        public override string TestReport => $"{pearls.Count} pearls";

        protected override void Begin()
        {
            floor = Pin(cupBottom);
            straw = Pin(strawTop);
            pearlMaterial = Tint(Pearl);
            if (pearlMaterial)
            {
                pearlMaterial = new Material(pearlMaterial) { name = "Pearl" };
                pearlMaterial.SetFloat("_Smoothness", 0.85f);
            }
            for (int i = 0; i < pearlCount; i++) AddPearl();
            lastFloor = Pos(floor);
        }

        void AddPearl()
        {
            Transform t = Item(PrimitiveType.Sphere, Pearl, Vector3.one * r * 2f);
            if (pearlMaterial) t.GetComponent<MeshRenderer>().sharedMaterial = pearlMaterial;
            t.SetParent(floor.Bone, false);
            Vector2 d = Random.insideUnitCircle * radiusBottom * 0.8f;
            pearls.Add(new Ball { T = t, P = new Vector3(d.x, r + Random.Range(0f, 0.15f), d.y) });
        }

        /// <summary>The cup's inside radius at height y above the floor.</summary>
        float RadiusAt(float y) => Mathf.Lerp(radiusBottom, radiusTop, Mathf.Clamp01(y / Mathf.Max(0.01f, teaTop.y - cupBottom.y)));

        protected override void Tick(float dt)
        {
            Simulate(dt);
            if (mouthJustOpened && mouth > 0.45f) Run(Slurp(Scaled(Random.Range(3, 7))));
            else if (mouthOpenFor > 0.6f)
            {
                slurpOwed += 4f * Amount * dt;
                for (; slurpOwed >= 1f; slurpOwed -= 1f) Shoot();
            }
            if (On("refill") && pearls.Count < pearlCount * 0.5f && Time.time >= nextRefill)
            {
                nextRefill = Time.time + 1.4f;
                AddPearl();
            }
        }

        /// <summary>Pearls in the tea: they sink, roll to the wall and slosh; the outer ones press against the glass so they show.</summary>
        void Simulate(float dt)
        {
            Transform bone = floor.Bone;
            if (!bone) return;
            Vector3 at = Pos(floor);
            Vector3 v = (at - lastFloor) / dt;
            Vector3 a = Vector3.ClampMagnitude((v - lastVelocity) / dt, 40f);
            lastFloor = at;
            lastVelocity = v;
            Vector3 g = bone.InverseTransformDirection(Vector3.down * 3f - a * 0.35f); // heavy in syrup: slow, and jolts slosh them
            float step = Mathf.Min(dt, 1f / 30f);
            for (int i = 0; i < pearls.Count; i++)
            {
                Ball b = pearls[i];
                b.V += g * step;
                b.V *= Mathf.Exp(-3f * step);
                b.P += b.V * step;
                if (b.P.y < r)
                {
                    b.P.y = r;
                    if (b.V.y < 0f) b.V.y *= -0.2f;
                }
                // Out to the wall (a little into it, so the outer ones touch the glass in front of the tea).
                float wall = RadiusAt(b.P.y) - r * 0.35f;
                float d = new Vector2(b.P.x, b.P.z).magnitude;
                if (d > wall)
                {
                    b.P.x *= wall / d;
                    b.P.z *= wall / d;
                    b.V.x *= 0.5f;
                    b.V.z *= 0.5f;
                }
                pearls[i] = b;
            }
            for (int i = 0; i < pearls.Count; i++)
                for (int j = i + 1; j < pearls.Count; j++)
                {
                    Ball bi = pearls[i], bj = pearls[j];
                    Vector3 d = bj.P - bi.P;
                    float l = d.magnitude;
                    if (l >= r * 2f || l < 1e-6f) continue;
                    Vector3 n = d / l;
                    float push = (r * 2f - l) * 0.5f;
                    bi.P -= n * push;
                    bj.P += n * push;
                    pearls[i] = bi;
                    pearls[j] = bj;
                }
            foreach (Ball b in pearls) if (b.T) b.T.localPosition = floor.Local + b.P;
        }

        /// <summary>A few pearls up the straw one after another, then out of the top.</summary>
        IEnumerator Slurp(int count)
        {
            Sfx.Play("slurp", 0.8f, 0.15f);
            for (int i = 0; i < count; i++)
            {
                Shoot();
                yield return new WaitForSeconds(Random.Range(0.06f, 0.14f));
            }
        }

        /// <summary>The pearl nearest the straw's bottom goes up it and flies out of the top onto the desk.</summary>
        void Shoot()
        {
            if (pearls.Count == 0) return;
            Vector3 bottomLocal = strawBottom - cupBottom;
            int best = 0;
            for (int i = 1; i < pearls.Count; i++)
                if ((pearls[i].P - bottomLocal).sqrMagnitude < (pearls[best].P - bottomLocal).sqrMagnitude) best = i;
            Ball b = pearls[best];
            pearls.RemoveAt(best);
            if (b.T) Destroy(b.T.gameObject);
            BitSound("pop", 0.4f);
            rig.Bonk(0.03f);
            Transform shot = Item(PrimitiveType.Sphere, Pearl, Vector3.one * r * 2f);
            if (pearlMaterial) shot.GetComponent<MeshRenderer>().sharedMaterial = pearlMaterial;
            shot.position = Pos(straw);
            Vector3 aim = (Vector3.up * 0.8f + new Vector3(TurnedSide * 0.6f + Random.Range(-0.4f, 0.4f), 0f, -Random.Range(0.3f, 0.7f))).normalized;
            ToDesk(shot, aim * Random.Range(2f, 3f) + meter.Velocity * 0.4f, r, r, ball: true, trail: Tea);
            if (Random.value < 0.5f) Drop(Pos(straw), aim * Random.Range(1.5f, 2.3f), Tea, Random.Range(0.01f, 0.016f), 0.03f);
        }

        public override void Burst() => Run(Slurp(Scaled(12)));

        public override void OnSlam()
        {
            Sfx.Play("water_spill", 0.7f, 0.08f);
            Sfx.Play("pop", 0.9f, 0.05f);
            for (int i = 0; i < Scaled(40); i++)
                Drop(Pos(straw) + Vector3.down * 0.3f + Random.insideUnitSphere * 0.1f, Outward() * Random.Range(0.3f, 1f) + Vector3.up * Random.Range(0.4f, 1.2f), Tea,
                    Random.Range(0.014f, 0.024f), Random.Range(0.04f, 0.06f));
            if (Mess.Instance) Mess.Instance.DeskStain(InFront(0f, 0.12f), 0.16f, Tea, 150f, 0.3f, true, new Vector2(0f, -1f));
            int n = Mathf.Min(pearls.Count, Scaled(20));
            for (int i = 0; i < n; i++)
            {
                Ball b = pearls[pearls.Count - 1];
                pearls.RemoveAt(pearls.Count - 1);
                Vector3 at = b.T ? b.T.position : Pos(floor);
                if (b.T) Destroy(b.T.gameObject);
                Transform shot = Item(PrimitiveType.Sphere, Pearl, Vector3.one * r * 2f);
                if (pearlMaterial) shot.GetComponent<MeshRenderer>().sharedMaterial = pearlMaterial;
                shot.position = at;
                ToDesk(shot, Outward() * Random.Range(0.4f, 1.2f) + Vector3.up * Random.Range(0.3f, 0.9f), r, r, ball: true, trail: Tea);
            }
        }
    }
}
