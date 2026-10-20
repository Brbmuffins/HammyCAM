using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The pickle jar: pickles bob about in the brine (you see them through it), sloshing as it moves. A hard shake
    /// (or !shake) pops the lid and a pickle leaps out, wriggles about on the desk and lies there in a puddle of brine.
    /// The face slam slops brine everywhere and throws two out. The lid screws itself back on after a while.
    /// </summary>
    public sealed class PickleJar : GagAvatar
    {
        [Tooltip("The jar's inside middle, the brine's surface and the lid's top (rest, avatar space), and its inside radius.")]
        public Vector3 jarCentre = new Vector3(0f, 0.495f, 0f), brineTop = new Vector3(0f, 0.955f, 0f), lidTop = new Vector3(0f, 1.15f, 0f);
        public float innerRadius = 0.345f, brineBottom = 0.036f;
        public int pickleCount = 5;

        static readonly Color Pickle = new Color(0.36f, 0.55f, 0.16f), Brine = new Color(0.82f, 0.86f, 0.4f, 0.6f);

        sealed class Floater
        {
            public Transform T;
            public Vector3 P, V;
            public float Spin;
        }

        readonly List<Floater> pickles = new List<Floater>();
        Anchor centre, lid;
        Vector3 lastCentre, lastVelocity;
        float nextPop, refillAt = -1f;
        bool lidOff;

        public override string MessName => "pickles and brine";
        protected override bool UsesFace => false;

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "jump", Label = "A hard shake pops the lid and a pickle jumps out" });
        }

        public override void TestMoment() => Run(PopLid(1));
        public override string TestReport => $"{pickles.Count} pickles in the jar{(lidOff ? ", lid off" : "")}";

        protected override void Begin()
        {
            centre = Pin(jarCentre);
            lid = Pin(lidTop);
            for (int i = 0; i < pickleCount; i++) AddPickle();
            lastCentre = Pos(centre);
        }

        void AddPickle()
        {
            Transform t = PickleShape();
            t.SetParent(centre.Bone, false);
            pickles.Add(new Floater
            {
                T = t,
                P = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(-0.2f, 0.25f), Random.Range(-0.15f, 0.15f)),
                Spin = Random.Range(0f, 360f),
            });
        }

        /// <summary>A gherkin: a fat bumpy green capsule with a few warts.</summary>
        Transform PickleShape()
        {
            Transform t = Item(PrimitiveType.Capsule, Pickle, new Vector3(0.09f, 0.13f, 0.09f));
            for (int k = 0; k < 5; k++)
            {
                Transform wart = Item(PrimitiveType.Sphere, Color.Lerp(Pickle, Color.black, 0.15f), Vector3.one * 0.18f);
                wart.SetParent(t, false);
                Vector2 d = Random.insideUnitCircle.normalized * 0.48f;
                wart.localPosition = new Vector3(d.x, Random.Range(-0.6f, 0.6f), d.y);
            }
            return t;
        }

        protected override void Tick(float dt)
        {
            Simulate(dt);
            if (On("jump") && !lidOff && meter.Shake > 50f && Time.time >= nextPop) Run(PopLid(1));
            if (lidOff && refillAt > 0f && Time.time >= refillAt)
            {
                refillAt = -1f;
                lidOff = false;
                HidePart("lid", false);
                Sfx.Play("scrub", 0.3f, 0.1f);
                while (pickles.Count < pickleCount) AddPickle();
            }
        }

        /// <summary>Pickles float in the brine: buoyant, slowed by it, jolted by the jar, kept inside it and apart.</summary>
        void Simulate(float dt)
        {
            Transform bone = centre.Bone;
            if (!bone) return;
            Vector3 at = Pos(centre);
            Vector3 v = (at - lastCentre) / dt;
            Vector3 a = Vector3.ClampMagnitude((v - lastVelocity) / dt, 40f);
            lastCentre = at;
            lastVelocity = v;
            Vector3 jolt = bone.InverseTransformDirection(-a * 0.25f);
            float step = Mathf.Min(dt, 1f / 30f);
            float floorY = brineBottom - jarCentre.y + 0.07f, surfaceY = brineTop.y - jarCentre.y - 0.07f;
            float t = Time.time;
            foreach (Floater p in pickles)
            {
                // Just buoyant enough to drift up, with a lazy bob.
                p.V += (jolt + Vector3.up * (0.25f + 0.15f * Mathf.Sin(t * 0.7f + p.Spin))) * step;
                p.V *= Mathf.Exp(-2.2f * step);
                p.P += p.V * step;
                p.P.y = Mathf.Clamp(p.P.y, floorY, surfaceY);
                float d = new Vector2(p.P.x, p.P.z).magnitude, wall = innerRadius - 0.06f;
                if (d > wall)
                {
                    p.P.x *= wall / d;
                    p.P.z *= wall / d;
                }
                p.Spin += (p.V.magnitude * 120f + 8f) * step;
            }
            for (int i = 0; i < pickles.Count; i++)
                for (int j = i + 1; j < pickles.Count; j++)
                {
                    Vector3 d = pickles[j].P - pickles[i].P;
                    float l = d.magnitude;
                    if (l >= 0.1f || l < 1e-6f) continue;
                    Vector3 n = d / l * ((0.1f - l) * 0.5f);
                    pickles[i].P -= n;
                    pickles[j].P += n;
                }
            foreach (Floater p in pickles)
            {
                if (!p.T) continue;
                p.T.localPosition = centre.Local + p.P;
                p.T.localRotation = Quaternion.Euler(70f + 20f * Mathf.Sin(p.Spin * 0.02f), p.Spin, 0f);
            }
        }

        /// <summary>POP: the lid flies off, brine slops out, and pickles jump.</summary>
        IEnumerator PopLid(int jumpers)
        {
            nextPop = Time.time + 8f;
            if (!lidOff)
            {
                lidOff = true;
                Sfx.Play("pop", 1f, 0.05f);
                HidePart("lid", true);
                Transform cap = Item(PrimitiveType.Cylinder, new Color(0.2f, 0.45f, 0.15f), new Vector3(0.62f, 0.04f, 0.62f) * innerRadius * 1.8f);
                cap.position = Pos(lid);
                ToDesk(cap, new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(3f, 4f), -0.4f), innerRadius * 0.6f, 0.03f);
            }
            for (int i = 0; i < Scaled(18); i++)
                Drop(Pos(lid) + Random.insideUnitSphere * innerRadius * 0.5f, Outward() * Random.Range(0.3f, 0.9f) + Vector3.up * Random.Range(0.6f, 1.4f), Brine,
                    Random.Range(0.012f, 0.02f), Random.Range(0.03f, 0.05f));
            for (int i = 0; i < jumpers && pickles.Count > 0; i++)
            {
                yield return new WaitForSeconds(0.15f);
                Floater p = pickles[0];
                pickles.RemoveAt(0);
                if (p.T) p.T.SetParent(null, true);
                Run(Wriggle(p.T));
            }
            refillAt = Time.time + 10f;
        }

        /// <summary>Out of the jar in an arc, then a few wriggles on the desk before it lies still in a little puddle.</summary>
        IEnumerator Wriggle(Transform pickle)
        {
            if (!pickle) yield break;
            Vector3 from = Pos(lid) + Vector3.up * 0.05f;
            Vector3 to = transform.position + new Vector3(Random.Range(0.3f, 0.5f) * (Random.value < 0.5f ? -1f : 1f), 0f, body.min.z - Random.Range(0.05f, 0.25f));
            to.y = Mathf.Max(0f, DeskScene.SurfaceHeight(to.x, to.z)) + 0.05f;
            for (float t = 0f; t < 0.7f && pickle; t += Time.deltaTime)
            {
                float k = t / 0.7f;
                pickle.position = Vector3.Lerp(from, to, k) + Vector3.up * 4f * 0.45f * k * (1f - k);
                pickle.rotation = Quaternion.Euler(k * 720f, 30f, 90f);
                yield return null;
            }
            Sfx.Play("flop", 0.6f, 0.2f);
            if (Mess.Instance && Keep()) Mess.Instance.DeskStain(to, 0.06f, Brine, 120f, 0.3f, true);
            for (float t = 0f; t < 2.2f && pickle; t += Time.deltaTime)
            {
                float hop = Mathf.Abs(Mathf.Sin(t * 9f)) * 0.03f * (1f - t / 2.2f);
                pickle.position = to + Vector3.up * hop;
                pickle.rotation = Quaternion.Euler(0f, 30f + Mathf.Sin(t * 14f) * 25f * (1f - t / 2.2f), 90f);
                yield return null;
            }
            if (pickle) ToDesk(pickle, Vector3.zero, 0.05f, 0.045f);
        }

        public override void Burst() => Run(PopLid(1));

        public override void OnSlam()
        {
            Sfx.Play("water_spill", 0.8f, 0.08f);
            if (Mess.Instance) Mess.Instance.DeskStain(InFront(0f, 0.1f), 0.18f, Brine, 150f, 0.3f, true, new Vector2(0f, -1f));
            Run(PopLid(2));
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            lidOff = false;
            refillAt = -1f;
        }
    }
}
