using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The birthday cake: five little flames on its candles, flickering and leaning as it moves. Pucker and blow (an
    /// "ooh" held a moment) and they all go out in a puff of smoke, then a burst of confetti; they relight one by one a
    /// few seconds later. !shake flings frosting and sprinkles; the face slam plants it face-first: frosting and cake
    /// all over the desk, candles out.
    /// </summary>
    public sealed class BirthdayCake : GagAvatar
    {
        [Tooltip("The candles' wick tips and the middle of the top (rest, avatar space).")]
        public Vector3[] wicks = new Vector3[0];
        public Vector3 top;
        public Color frosting = new Color(1f, 0.72f, 0.82f);

        static readonly Color Cake = new Color(0.95f, 0.82f, 0.55f), Cream = new Color(1f, 0.97f, 0.92f), Smoke = new Color(0.5f, 0.5f, 0.52f, 0.4f);

        readonly List<(SmallFlame flame, Anchor wick)> flames = new List<(SmallFlame, Anchor)>();
        Anchor topAnchor;
        Light glow;
        bool lit = true;
        float relightAt;

        public override string MessName => "frosting and confetti";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "confetti", Label = "Confetti when you blow the candles out" });
            into.Add(new AvatarFeature { Key = "light", Label = "The candles light up the desk" });
        }

        public override void TestMoment() => BlowOut();
        public override string TestReport => lit ? "candles lit" : "blown out";

        protected override void Begin()
        {
            if (top == Vector3.zero) top = new Vector3(0f, body.max.y - 0.08f, 0f);
            topAnchor = Pin(top);
            Material flame = Chaos ? Chaos.flame : null;
            if (wicks.Length == 0)
                for (int i = 0; i < 5; i++)
                {
                    float a = i / 5f * Mathf.PI * 2f;
                    wicks = AddTo(wicks, top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * body.extents.x * 0.45f + Vector3.up * 0.06f);
                }
            if (flame) foreach (Vector3 w in wicks) flames.Add((new SmallFlame(flame, transform, 1.1f), Pin(w)));
            var light = new GameObject("Candlelight");
            light.transform.SetParent(transform, false);
            glow = light.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.7f, 0.35f);
            glow.range = 1.6f;
        }

        static Vector3[] AddTo(Vector3[] list, Vector3 p)
        {
            var l = new List<Vector3>(list) { p };
            return l.ToArray();
        }

        protected override void Tick(float dt)
        {
            if (lit && puckerFor > 0.22f) BlowOut();
            if (!lit && Time.time >= relightAt) Run(Relight());
            float on = 0f;
            foreach (var (flame, wick) in flames)
            {
                flame.Update(Pos(wick), dt);
                if (flame.Lit) on++;
            }
            if (glow)
            {
                glow.transform.position = Pos(topAnchor) + Vector3.up * 0.15f + Vector3.back * 0.1f;
                glow.intensity = On("light") ? on / Mathf.Max(1, flames.Count) * 1.6f * (0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 10f, 0.4f)) : 0f;
            }
        }

        void BlowOut()
        {
            lit = false;
            relightAt = Time.time + Random.Range(4f, 6f);
            Sfx.Play("spore_puff", 0.6f, 0.15f);
            foreach (var (flame, wick) in flames)
            {
                flame.Set(false);
                puffs?.Emit(Pos(wick), new Vector3(Random.Range(-0.03f, 0.03f), 0.22f, 0f), Smoke, 0.04f, 2f, 3.5f, 0.3f, 0.02f);
            }
            // Make a wish: confetti!
            if (On("confetti")) Run(After(0.35f, () => Confetti(Scaled(140))));
        }

        IEnumerator Relight()
        {
            lit = true;
            foreach (var (flame, _) in flames)
            {
                flame.Set(true);
                Sfx.Play("fire_ignite", 0.3f, 0.2f);
                yield return new WaitForSeconds(0.18f);
            }
        }

        void Confetti(int count)
        {
            if (!DeskDebris.Instance) return;
            Sfx.Play("pop", 0.8f, 0.1f);
            Sfx.Play("sprinkle_rain", 0.5f, 0.1f);
            Vector3 c = Pos(topAnchor) + Vector3.up * 0.2f;
            for (int i = 0; i < count; i++)
                DeskDebris.Instance.EmitSprinkle(c + Random.insideUnitSphere * 0.1f, Random.insideUnitSphere * 1.2f + Vector3.up * Random.Range(1.2f, 2.6f) + Vector3.back * 0.4f);
        }

        void Frosting(int blobs, float force)
        {
            for (int i = 0; i < blobs; i++)
                Drop(Pos(topAnchor) + Random.insideUnitSphere * body.extents.x * 0.5f, Outward() * Random.Range(0.3f, 0.9f) * force + Vector3.up * Random.Range(0.4f, 1.1f) * force,
                    Random.value < 0.6f ? frosting : Cream, Random.Range(0.016f, 0.026f), Random.Range(0.035f, 0.055f));
        }

        public override void Burst()
        {
            Sfx.Play("squelch", 0.6f, 0.15f);
            Frosting(Scaled(20), 1f);
            Confetti(Scaled(60));
        }

        public override void OnSlam()
        {
            Sfx.Play("pie_splat", 0.9f, 0.08f);
            if (lit) BlowOut();
            Frosting(Scaled(30), 1.2f);
            if (Mess.Instance)
            {
                Mess.Instance.DeskStain(InFront(0f, 0.1f), 0.16f, frosting, 150f, 0.2f, false, new Vector2(0f, -1.2f));
                for (int i = 0; i < Scaled(8); i++)
                    Mess.Instance.Blob(null, InFront(Random.Range(-0.3f, 0.3f), Random.Range(0.02f, 0.25f)) + Vector3.up * 0.02f,
                        Vector3.one * Random.Range(0.04f, 0.08f), Random.value < 0.5f ? Cake : frosting, 150f, 0.2f);
            }
        }
    }
}
