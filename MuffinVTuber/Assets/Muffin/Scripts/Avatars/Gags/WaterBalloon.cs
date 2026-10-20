using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The water balloon: it wobbles like jelly. Hold your mouth open and it inflates, bigger and bigger, stretching
    /// and trembling, until it POPS: a huge splash of water that washes the desk clean (the mess slides away and the
    /// stains melt), shreds of rubber everywhere. A moment later a fresh balloon blows itself up. !whack, !shake and
    /// the face slam pop it too.
    /// </summary>
    public sealed class WaterBalloon : GagAvatar
    {
        [Tooltip("The middle of the balloon, its radius and the knot (rest, avatar space).")]
        public Vector3 centre = new Vector3(0f, 0.394f, 0f), knot = new Vector3(0f, 1.102f, 0f);
        public float radius = 0.45f;
        [Tooltip("How much bigger than normal it gets before it pops.")]
        public float burstAt = 1.3f;
        public Color latex = new Color(1f, 0.45f, 0.65f);

        static readonly Color Water = new Color(0.55f, 0.78f, 1f, 0.6f);

        Anchor middle;
        float inflate = 1f, shown = 1f;
        bool popped;

        public override string MessName => "a water-balloon splash";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "inflate", Label = "Holding your mouth open blows it up until it pops" });
            into.Add(new AvatarFeature { Key = "wash", Label = "The splash washes the desk clean" });
        }

        public override void TestMoment() => Run(Pop());
        public override string TestReport => popped ? "popped" : $"size {inflate:0.00}";

        protected override void Begin() => middle = Pin(centre);

        protected override void Tick(float dt)
        {
            if (popped) return;
            if (On("inflate") && mouth > MouthThreshold) inflate += dt * 0.12f * Mathf.Lerp(0.6f, 1.4f, mouth);
            else inflate = Mathf.MoveTowards(inflate, 1f, dt * 0.05f); // a slow leak back down
            if (mouthJustOpened) rig.KickTip(new Vector3(Random.Range(-0.6f, 0.6f), 0.3f, 0f)); // a wobble each time you start talking
            // Stretched near its limit it trembles.
            float tension = Mathf.InverseLerp(1.15f, burstAt, inflate);
            if (tension > 0f && Random.value < tension * 8f * dt) rig.Bonk(0.03f + tension * 0.05f);
            shown = Mathf.Lerp(shown, inflate, 1f - Mathf.Exp(-dt * 6f));
            transform.localScale = Vector3.one * shown;
            if (inflate >= burstAt) Run(Pop());
        }

        IEnumerator Pop()
        {
            if (popped) yield break;
            popped = true;
            Vector3 c = Pos(middle);
            Sfx.Play("pop", 1f, 0.03f);
            Sfx.Play("water_spill", 1f, 0.05f);
            Sfx.Play("splat", 0.8f, 0.1f);
            transform.localScale = Vector3.one * 0.02f;
            float r = radius * shown;
            // The water: a ring of it bursting outward and down, and a big puddle.
            for (int i = 0; i < Scaled(90); i++)
            {
                Vector3 d = Random.onUnitSphere;
                d.y = Mathf.Abs(d.y) * 0.5f;
                Drop(c + d * r * 0.8f, d * Random.Range(1f, 2.6f) + Vector3.up * Random.Range(0.2f, 1f), Water, Random.Range(0.016f, 0.03f), Random.Range(0.05f, 0.08f));
            }
            if (Mess.Instance) Mess.Instance.DeskStain(InFront(0f, 0f), 0.35f, Water, 60f, 0.5f, true);
            // Shreds of rubber.
            for (int i = 0; i < Scaled(10); i++)
            {
                Transform shred = Item(PrimitiveType.Cube, latex, new Vector3(Random.Range(0.03f, 0.07f), 0.004f, Random.Range(0.02f, 0.05f)));
                shred.position = c + Random.onUnitSphere * r * 0.7f;
                shred.rotation = Random.rotation;
                ToDesk(shred, Random.onUnitSphere * Random.Range(1f, 2.4f) + Vector3.up, 0.025f, 0.002f);
            }
            if (On("wash")) Run(Wash(c));
            yield return new WaitForSeconds(2.5f);
            // A fresh one blows itself up.
            Sfx.Play("squelch", 0.4f, 0.2f);
            inflate = 1f;
            for (float t = 0f; t < 1.4f; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 1.4f);
                shown = Mathf.Lerp(0.05f, 1f, k) * (1f + 0.06f * Mathf.Sin(t * 20f) * (1f - k));
                transform.localScale = Vector3.one * shown;
                yield return null;
            }
            shown = 1f;
            transform.localScale = Vector3.one;
            popped = false;
        }

        /// <summary>The wave runs out across the desk: everything lying there is carried off and the stains melt away.</summary>
        IEnumerator Wash(Vector3 from)
        {
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                float reach = Mathf.Lerp(0.2f, 1.3f, t / 1.6f);
                if (DeskDebris.Instance)
                {
                    DeskDebris.Instance.PushArea(from + new Vector3(-reach * 0.5f, 0f, -0.2f), reach * 0.6f, new Vector3(-1.4f, 0.1f, -0.8f));
                    DeskDebris.Instance.PushArea(from + new Vector3(reach * 0.5f, 0f, -0.2f), reach * 0.6f, new Vector3(1.4f, 0.1f, -0.8f));
                }
                yield return null;
            }
            WipeDesk(from + Vector3.back * 0.3f, 0.9f);
        }

        public override void Burst() => Run(Pop());
        public override void OnSlam() => Run(Pop());

        public override void Whack(float strength)
        {
            rig.Bonk(0.2f);
            Run(Pop());
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            transform.localScale = Vector3.one;
            inflate = shown = 1f;
            popped = false;
        }
    }
}
