using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The gumball machine: its glass globe is full of real gumballs that tumble and slosh as it rocks. Open your
    /// mouth and CLUNK, a gumball rolls out of the chute and bounces off across the desk. !shake rattles out a
    /// handful; the face slam cracks the globe and they all pour out over the desk. It fills itself back up slowly.
    /// </summary>
    public sealed class GumballMachine : GagAvatar
    {
        [Tooltip("The globe's middle and inside radius, and where a gumball comes out of the chute (rest, avatar space).")]
        public Vector3 globeCentre, chuteMouth;
        public float globeRadius = 0.2f;
        [Tooltip("How far below the globe's middle its floor is (where it sits in the base).")]
        public float floorDepth = 0.17f;
        [Tooltip("Gumballs in a full globe.")]
        public int full = 48;

        static readonly Color[] Colours =
        {
            new Color(0.95f, 0.15f, 0.2f), new Color(1f, 0.78f, 0.1f), new Color(0.2f, 0.6f, 1f), new Color(0.25f, 0.85f, 0.35f),
            new Color(1f, 0.45f, 0.75f), new Color(1f, 0.55f, 0.1f), new Color(0.98f, 0.97f, 0.95f), new Color(0.6f, 0.35f, 0.95f),
        };

        struct Ball
        {
            public Transform T;
            public Vector3 P, V; // in the globe's own space (the head bone), from its middle
            public Color Colour;
        }

        readonly List<Ball> balls = new List<Ball>();
        Anchor globe, chute;
        Vector3 lastGlobe, lastGlobeVelocity;
        float r, nextOut, nextRefill;

        public int Inside => balls.Count;
        public override void TestMoment() => Run(Rattle(3));
        public override string TestReport => $"{balls.Count} gumballs inside";
        public override string MessName => "gumballs";

        protected override void Begin()
        {
            if (globeCentre == Vector3.zero) globeCentre = new Vector3(0f, body.size.y * 0.66f, 0f);
            if (chuteMouth == Vector3.zero) chuteMouth = new Vector3(0f, body.size.y * 0.12f, body.min.z);
            globe = Pin(globeCentre);
            chute = Pin(chuteMouth);
            r = globeRadius * 0.16f;
            for (int i = 0; i < full; i++) AddBall(Random.insideUnitSphere * globeRadius * 0.6f + Vector3.down * globeRadius * 0.3f);
            lastGlobe = Pos(globe);
        }

        void AddBall(Vector3 local)
        {
            Color c = Colours[Random.Range(0, Colours.Length)];
            Transform t = Item(PrimitiveType.Sphere, c, Vector3.one * r * 2f);
            t.SetParent(globe.Bone, false);
            balls.Add(new Ball { T = t, P = local, V = Vector3.zero, Colour = c });
        }

        protected override void Tick(float dt)
        {
            Simulate(dt);
            if (mouthJustOpened && Time.time >= nextOut) Dispense();
            // It fills itself back up, a ball at a time, dropping in from the top.
            if (balls.Count < full * 0.45f && Time.time >= nextRefill)
            {
                nextRefill = Time.time + 1.6f;
                AddBall(new Vector3(Random.Range(-0.3f, 0.3f), 0.75f, Random.Range(-0.3f, 0.3f)) * globeRadius);
            }
        }

        /// <summary>
        /// The balls inside, in the globe's own space: gravity (as the globe tilts) and the globe's own jolts push
        /// them about; they bounce off the glass and each other.
        /// </summary>
        void Simulate(float dt)
        {
            Transform bone = globe.Bone;
            if (!bone) return;
            Vector3 centre = Pos(globe);
            Vector3 v = (centre - lastGlobe) / dt;
            Vector3 a = (v - lastGlobeVelocity) / dt;
            lastGlobe = centre;
            lastGlobeVelocity = v;
            Vector3 g = bone.InverseTransformDirection(Vector3.down * 9.8f - Vector3.ClampMagnitude(a, 40f) * 0.6f);
            float wall = globeRadius - r;
            float step = Mathf.Min(dt, 1f / 30f);
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                b.V += g * step;
                b.V *= Mathf.Exp(-1.2f * step);
                b.P += b.V * step;
                float d = b.P.magnitude;
                if (d > wall)
                {
                    Vector3 n = b.P / d;
                    b.P = n * wall;
                    float into = Vector3.Dot(b.V, n);
                    if (into > 0f) b.V -= n * into * 1.35f; // a little bounce
                    b.V *= 0.92f;                            // rolling friction on the glass
                }
                if (b.P.y < -floorDepth + r)
                {
                    b.P.y = -floorDepth + r;
                    if (b.V.y < 0f) b.V.y *= -0.3f;
                    b.V.x *= 0.9f;
                    b.V.z *= 0.9f;
                }
                balls[i] = b;
            }
            // Keep them apart.
            float min = r * 2f;
            for (int i = 0; i < balls.Count; i++)
                for (int j = i + 1; j < balls.Count; j++)
                {
                    Ball bi = balls[i], bj = balls[j];
                    Vector3 d = bj.P - bi.P;
                    float l = d.magnitude;
                    if (l >= min || l < 1e-6f) continue;
                    Vector3 n = d / l;
                    float push = (min - l) * 0.5f;
                    bi.P -= n * push;
                    bj.P += n * push;
                    float rel = Vector3.Dot(bj.V - bi.V, n);
                    if (rel < 0f)
                    {
                        bi.V += n * rel * 0.6f;
                        bj.V -= n * rel * 0.6f;
                    }
                    balls[i] = bi;
                    balls[j] = bj;
                }
            Vector3 offset = globe.Local;
            foreach (Ball b in balls)
                if (b.T) b.T.localPosition = offset + b.P;
        }

        /// <summary>CLUNK: the lowest gumball goes down the works and rolls out of the chute onto the desk.</summary>
        void Dispense()
        {
            if (balls.Count == 0) return;
            nextOut = Time.time + 0.35f;
            int lowest = 0;
            for (int i = 1; i < balls.Count; i++) if (balls[i].P.y < balls[lowest].P.y) lowest = i;
            Ball b = balls[lowest];
            balls.RemoveAt(lowest);
            if (b.T) Destroy(b.T.gameObject);
            Sfx.Play("bucket_plonk", 0.6f, 0.2f);
            Run(After(0.18f, () => Roll(b.Colour, Pos(chute), new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.3f, 0.8f), -Random.Range(0.5f, 1f)))));
        }

        void Roll(Color colour, Vector3 at, Vector3 velocity)
        {
            Transform t = Item(PrimitiveType.Sphere, colour, Vector3.one * r * 2f);
            t.position = at;
            ToDesk(t, velocity + meter.Velocity * 0.4f, r, r, ball: true);
            BitSound("pop", 0.3f);
        }

        public override void Burst() => Run(Rattle(6));

        IEnumerator Rattle(int count)
        {
            Sfx.Play("trash_clatter", 0.5f, 0.15f);
            for (int i = 0; i < count && balls.Count > 0; i++)
            {
                rig.Bonk(0.06f);
                nextOut = 0f;
                Dispense();
                yield return new WaitForSeconds(0.14f);
            }
        }

        public override void OnSlam() => Run(Crack());

        /// <summary>The globe cracks: shards of glass and every gumball pour out of the front onto the desk.</summary>
        IEnumerator Crack()
        {
            Sfx.Play("egg_crack", 1f, 0.05f);
            Sfx.Play("trash_clatter", 0.9f, 0.08f);
            Material glass = Chaos ? Chaos.glass : null;
            Vector3 front = Pos(globe) + Vector3.back * globeRadius;
            for (int i = 0; i < 8 && glass; i++)
            {
                Transform shard = Item(PrimitiveType.Cube, Color.white, new Vector3(Random.Range(0.02f, 0.05f), 0.004f, Random.Range(0.02f, 0.04f)));
                shard.GetComponent<MeshRenderer>().sharedMaterial = glass;
                shard.position = front + Random.insideUnitSphere * 0.05f;
                shard.rotation = Random.rotation;
                ToDesk(shard, Outward() * Random.Range(0.3f, 0.9f) + Vector3.up * Random.Range(0.4f, 1f), 0.02f, 0.002f);
            }
            while (balls.Count > 0)
            {
                Ball b = balls[balls.Count - 1];
                balls.RemoveAt(balls.Count - 1);
                Vector3 at = b.T ? b.T.position : front;
                if (b.T) Destroy(b.T.gameObject);
                Roll(b.Colour, at, Outward() * Random.Range(0.3f, 1.1f) + new Vector3(0f, Random.Range(0.2f, 0.9f), -Random.Range(0.2f, 0.6f)));
                if (balls.Count % 3 == 0) yield return null;
            }
            nextRefill = Time.time + 6f;
        }
    }
}
