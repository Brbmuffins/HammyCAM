using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The kitchen sponge: open your mouth and it's squeezed: it squashes, soapy water dribbles out of its bottom and
    /// bubbles float up out of its pores and pop. It's the one mess-maker that also cleans: the face slam scrubs the
    /// desk under it clean (with a heap of suds). !shake sprays suds about.
    /// </summary>
    public sealed class KitchenSponge : GagAvatar
    {
        [Tooltip("The scrubber's top and the front's middle (rest, avatar space).")]
        public Vector3 top, front;

        static readonly Color Soapy = new Color(0.82f, 0.9f, 1f, 0.55f), Suds = new Color(0.97f, 0.98f, 1f);

        Anchor topAnchor, frontAnchor;
        readonly List<(Transform t, float born, float life, Vector3 drift)> bubbles = new List<(Transform, float, float, Vector3)>();
        float squeezeOwed, nextSquish;

        public override string MessName => "suds and bubbles";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "bubbles", Label = "Bubbles float up when it's squeezed" });
            into.Add(new AvatarFeature { Key = "cleans", Label = "The face slam scrubs the desk clean under it" });
        }

        public override void TestMoment() => Squeeze(1f);
        public override string TestReport => $"{bubbles.Count} bubbles";

        protected override void Begin()
        {
            if (top == Vector3.zero) top = new Vector3(0f, body.max.y, 0f);
            if (front == Vector3.zero) front = new Vector3(0f, body.size.y * 0.5f, body.min.z);
            topAnchor = Pin(top);
            frontAnchor = Pin(front);
        }

        protected override void Tick(float dt)
        {
            if (mouthJustOpened) Squeeze(Mathf.Lerp(0.6f, 1.2f, mouth));
            else if (mouth > MouthThreshold)
            {
                squeezeOwed += mouth * 6f * Amount * dt;
                for (; squeezeOwed >= 1f; squeezeOwed -= 1f)
                {
                    Dribble();
                    if (On("bubbles")) Bubble(Pos(frontAnchor) + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.3f, 0.3f), -0.02f));
                }
            }
            float t = Time.time;
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var (b, born, life, drift) = bubbles[i];
                if (!b || t - born > life)
                {
                    if (b)
                    {
                        BitSound("pop", 0.15f);
                        Destroy(b.gameObject);
                    }
                    bubbles.RemoveAt(i);
                    continue;
                }
                b.position += (drift + new Vector3(Mathf.Sin((t - born) * 3f + i) * 0.05f, 0f, 0f)) * dt;
            }
        }

        void Squeeze(float strength)
        {
            if (Time.time < nextSquish) return;
            nextSquish = Time.time + 0.4f;
            Sfx.Play("squelch", 0.6f * strength, 0.15f);
            rig.Bonk(0.3f * strength);
            for (int i = 0; i < Scaled(Mathf.RoundToInt(8 * strength)); i++) Dribble();
            if (On("bubbles"))
                for (int i = 0; i < Mathf.RoundToInt(6 * strength); i++)
                    Bubble(Pos(frontAnchor) + new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(-0.35f, 0.35f), -0.02f));
        }

        /// <summary>Soapy water running out of its bottom edge.</summary>
        void Dribble()
        {
            Vector3 at = transform.position + new Vector3(Random.Range(-body.extents.x, body.extents.x) * 0.8f, 0.03f, body.min.z * Random.Range(0.3f, 1f));
            Drop(at, new Vector3(Random.Range(-0.3f, 0.3f), 0.1f, -Random.Range(0.1f, 0.5f)), Soapy, Random.Range(0.012f, 0.018f), Random.Range(0.035f, 0.05f));
        }

        /// <summary>A soap bubble out of a pore, drifting up and wobbling until it pops.</summary>
        void Bubble(Vector3 at)
        {
            Material bubble = Chaos ? Chaos.bubble : null;
            if (!bubble || bubbles.Count > 30) return;
            Transform b = Item(PrimitiveType.Sphere, Color.white, Vector3.one * Random.Range(0.025f, 0.06f));
            b.GetComponent<MeshRenderer>().sharedMaterial = bubble;
            b.position = at;
            bubbles.Add((b, Time.time, Random.Range(1.5f, 3.5f), new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(0.12f, 0.25f), -Random.Range(0.02f, 0.1f))));
        }

        public override void Burst()
        {
            Squeeze(1.4f);
            for (int i = 0; i < Scaled(6); i++)
                if (Mess.Instance)
                    Mess.Instance.Blob(null, InFront(Random.Range(-0.4f, 0.4f), Random.Range(0.02f, 0.3f)) + Vector3.up * 0.02f, new Vector3(0.06f, 0.035f, 0.06f), Suds, 60f, 0.3f);
        }

        public override void OnSlam() => Run(Scrub());

        /// <summary>Face down on the desk it scrubs: the mess under and in front of it goes, leaving a heap of suds.</summary>
        IEnumerator Scrub()
        {
            Sfx.Play("scrub", 0.9f, 0.08f);
            Squeeze(1.2f);
            yield return new WaitForSeconds(0.2f);
            if (On("cleans"))
                for (int i = 0; i < 4; i++)
                {
                    WipeDesk(InFront(Random.Range(-0.3f, 0.3f), Random.Range(0f, 0.35f)), 0.32f);
                    yield return new WaitForSeconds(0.15f);
                }
            if (Mess.Instance)
                for (int i = 0; i < 4; i++)
                    Mess.Instance.Blob(null, InFront(Random.Range(-0.3f, 0.3f), Random.Range(0.02f, 0.2f)) + Vector3.up * 0.02f, new Vector3(0.07f, 0.04f, 0.07f), Suds, 40f, 0.3f);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            foreach (var (b, _, _, _) in bubbles) if (b) Destroy(b.gameObject);
            bubbles.Clear();
        }
    }
}
