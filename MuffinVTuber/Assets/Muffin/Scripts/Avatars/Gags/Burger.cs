using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The burger: as it moves, sauce oozes and drips down its sides, shreds of lettuce and the odd sesame seed fall
    /// off. Open your mouth and it's squeezed: ketchup and mustard squirt out of both sides of the patty. !shake flings
    /// it all about; the face slam squashes it flat: sauce everywhere and tomato slices and lettuce skidding out.
    /// </summary>
    public sealed class Burger : GagAvatar
    {
        [Tooltip("The patty's middle and the underside of the top bun (rest, avatar space), and the patty's radius.")]
        public Vector3 patty = new Vector3(0.011f, 0.334f, 0.002f), topBunBase = new Vector3(0.011f, 0.581f, 0.002f);
        public float pattyRadius = 0.517f;

        static readonly Color Ketchup = new Color(0.8f, 0.06f, 0.04f, 0.95f), Mustard = new Color(1f, 0.8f, 0.05f, 0.95f),
            Lettuce = new Color(0.45f, 0.78f, 0.28f), Tomato = new Color(0.9f, 0.2f, 0.12f), Seed = new Color(0.98f, 0.95f, 0.85f),
            Grease = new Color(0.55f, 0.35f, 0.15f, 0.5f);

        Anchor middle, bunBase;
        float owed, squeezeOwed;

        public override string MessName => "sauce, lettuce and tomato";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "squeeze", Label = "Opening your mouth squeezes sauce out of the sides" });
            into.Add(new AvatarFeature { Key = "drips", Label = "Sauce drips and bits fall off as it moves" });
        }

        public override void TestMoment() => Run(Squeeze(1.4f));

        protected override void Begin()
        {
            middle = Pin(patty);
            bunBase = Pin(topBunBase);
        }

        /// <summary>A point on the patty's edge at angle a (degrees, 0 = screen right), riding along.</summary>
        Vector3 Edge(float a)
        {
            float r = a * Mathf.Deg2Rad;
            return Pos(middle) + new Vector3(Mathf.Cos(r), 0.03f, -Mathf.Abs(Mathf.Sin(r)) * 0.4f) * pattyRadius * 0.95f;
        }

        protected override void Tick(float dt)
        {
            if (On("drips"))
            {
                owed += (0.15f + meter.Speed * 1.4f + Mathf.Max(0f, meter.Shake - 8f) * 0.05f) * Amount * dt;
                for (; owed >= 1f; owed -= 1f)
                {
                    float pick = Random.value;
                    if (pick < 0.35f) RunDown(Random.Range(0.26f, 0.34f), Random.Range(-0.8f, 0.8f), Random.value < 0.6f ? Ketchup : Mustard, 0.024f, 0.08f);
                    else if (pick < 0.75f) Shred();
                    else SesameSeed();
                }
            }
            if (On("squeeze") && mouth > MouthThreshold)
            {
                // Squeezed while the mouth stays open: sauce squirts out of both sides.
                squeezeOwed += mouth * 30f * Amount * dt;
                for (; squeezeOwed >= 1f; squeezeOwed -= 1f)
                {
                    bool left = Random.value < 0.5f;
                    Vector3 from = Edge(left ? 180f : 0f);
                    Vector3 v = new Vector3(left ? -1f : 1f, Random.Range(0.1f, 0.5f), -Random.Range(0.1f, 0.4f)) * Random.Range(0.9f, 1.6f);
                    Drop(from, v + meter.Velocity * 0.4f, Random.value < 0.6f ? Ketchup : Mustard, Random.Range(0.012f, 0.02f), 0.03f);
                }
            }
            if (mouthJustOpened) Sfx.Play("squirt", 0.6f, 0.15f);
        }

        void Shred()
        {
            Transform bit = Item(PrimitiveType.Cube, Lettuce, new Vector3(Random.Range(0.04f, 0.07f), 0.004f, Random.Range(0.025f, 0.045f)));
            bit.position = Edge(Random.Range(0f, 180f)) + Vector3.up * 0.2f;
            bit.rotation = Random.rotation;
            ToDesk(bit, Outward() * Random.Range(0.2f, 0.6f) + Vector3.up * Random.Range(0.1f, 0.5f) + meter.Velocity * 0.6f, 0.025f, 0.002f);
        }

        void SesameSeed()
        {
            Transform seed = Lump(Seed, new Vector3(0.012f, 0.005f, 0.02f));
            seed.position = Pos(bunBase) + new Vector3(Random.Range(-0.3f, 0.3f), 0.5f, Random.Range(-0.2f, 0.1f));
            ToDesk(seed, Outward() * Random.Range(0.2f, 0.6f) + Vector3.up * 0.3f + meter.Velocity * 0.6f, 0.008f, 0.0025f);
        }

        void TomatoSlice()
        {
            Transform slice = Item(PrimitiveType.Cylinder, Tomato, new Vector3(0.12f, 0.006f, 0.12f));
            slice.position = Edge(Random.Range(0f, 180f)) + Vector3.up * 0.15f;
            ToDesk(slice, Outward() * Random.Range(0.6f, 1.3f) + Vector3.up * Random.Range(0.3f, 0.7f), 0.06f, 0.006f);
        }

        IEnumerator Squeeze(float strength)
        {
            Sfx.Play("squirt", 0.9f, 0.1f);
            rig.Bonk(0.2f * strength);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Run(Stream(() => Edge(side < 0f ? 180f : 0f), () => new Vector3(side, 0.35f, -0.25f).normalized, i == 0 ? Ketchup : Mustard,
                    0.5f * strength, 60f, new Vector2(1.2f, 2f), new Vector2(0.014f, 0.022f), 0.035f));
            }
            yield return null;
        }

        public override void Burst()
        {
            Run(Squeeze(1f));
            for (int i = 0; i < Scaled(6); i++) Shred();
            for (int i = 0; i < Scaled(6); i++) SesameSeed();
        }

        public override void OnSlam()
        {
            Sfx.Play("pie_splat", 0.8f, 0.08f);
            Sfx.Play("squelch", 0.8f, 0.1f);
            Run(Squeeze(1.6f));
            for (int i = 0; i < Scaled(3); i++) TomatoSlice();
            for (int i = 0; i < Scaled(8); i++) Shred();
            if (Mess.Instance)
            {
                Mess.Instance.DeskStain(InFront(-0.15f, 0.08f), 0.1f, Ketchup, 150f, 0.15f, false, new Vector2(-0.3f, -1.4f));
                Mess.Instance.DeskStain(InFront(0.15f, 0.08f), 0.09f, Mustard, 150f, 0.15f, false, new Vector2(0.3f, -1.4f));
                Mess.Instance.DeskStain(InFront(0f, 0f), 0.2f, Grease, 150f, 0.5f, true);
            }
        }
    }
}
