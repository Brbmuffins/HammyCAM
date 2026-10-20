using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The lava lamp: glowing blobs of wax drift slowly up and down inside its liquid, squashing and stretching, and
    /// it lights the desk. Talking heats it up: the blobs race and swell. Let it overheat (or !shake, or the slam) and
    /// it ERUPTS: the cap pops off and hot goo fountains out of the top and drips down the sides.
    /// </summary>
    public sealed class LavaLamp : GagAvatar
    {
        [Tooltip("The bottom and top of the liquid (rest, avatar space), and its radius from bottom to top as [height, radius] pairs.")]
        public Vector3 bottleBottom = new Vector3(0f, 0.368f, 0f), bottleTop = new Vector3(0f, 0.951f, 0f);
        public Vector2[] profile = { new Vector2(0.368f, 0.168f), new Vector2(0.538f, 0.221f), new Vector2(0.66f, 0.205f), new Vector2(0.951f, 0.129f) };
        [Tooltip("The open top of the glass neck under the cap (rest, avatar space): where an eruption comes out.")]
        public Vector3 neck = new Vector3(0f, 1.03f, 0f);
        public Color wax = new Color(1f, 0.62f, 0.12f);
        public int blobCount = 7;

        sealed class Blob
        {
            public Transform T;
            public float Phase, Speed, Size, X, Z;
        }

        readonly List<Blob> blobs = new List<Blob>();
        Anchor bottom, top, mouthOfNeck;
        Light glow;
        float heat, wobble;
        bool erupting;

        public float Heat => heat;
        public override string MessName => "hot lava-lamp goo";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "light", Label = "It lights up the desk" });
            into.Add(new AvatarFeature { Key = "overheat", Label = "Talking a lot overheats it and it erupts" });
        }

        public override void TestMoment() => Run(Erupt());
        public override string TestReport => $"heat {heat:0.00}";

        protected override void Begin()
        {
            bottom = Pin(bottleBottom);
            top = Pin(bottleTop);
            mouthOfNeck = Pin(neck);
            Material glowing = Glow(wax, "Lava wax");
            for (int i = 0; i < blobCount; i++)
            {
                var b = new Blob
                {
                    T = Item(PrimitiveType.Sphere, wax, Vector3.zero),
                    Phase = Random.Range(0f, 10f),
                    Speed = Random.Range(0.6f, 1.1f),
                    Size = Random.Range(0.6f, 1f),
                    X = Random.Range(-0.4f, 0.4f),
                    Z = Random.Range(-0.4f, 0.4f),
                };
                if (glowing) b.T.GetComponent<MeshRenderer>().sharedMaterial = glowing;
                b.T.SetParent(bottom.Bone, false);
                blobs.Add(b);
            }
            var light = new GameObject("Lava glow");
            light.transform.SetParent(transform, false);
            glow = light.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = Color.Lerp(wax, new Color(1f, 0.3f, 0.6f), 0.4f);
            glow.range = 1.7f;
        }

        /// <summary>The liquid's radius at height y (rest).</summary>
        float RadiusAt(float y)
        {
            if (y <= profile[0].x) return profile[0].y;
            for (int i = 1; i < profile.Length; i++)
                if (y <= profile[i].x) return Mathf.Lerp(profile[i - 1].y, profile[i].y, Mathf.InverseLerp(profile[i - 1].x, profile[i].x, y));
            return profile[profile.Length - 1].y;
        }

        protected override void Tick(float dt)
        {
            if (!erupting)
            {
                float talk = Mathf.Clamp01(mouth * 1.6f);
                heat = Mathf.Clamp01(heat + (talk * 0.06f - 0.02f) * dt);
                if (heat >= 1f && On("overheat")) Run(Erupt());
            }
            wobble += dt * (0.25f + heat * 1.2f);
            float h = bottleTop.y - bottleBottom.y;
            foreach (Blob b in blobs)
            {
                // Each rises and sinks on its own slow cycle; heat speeds them up and swells them.
                float k = 0.5f - 0.5f * Mathf.Cos((wobble * b.Speed + b.Phase));
                float y = bottleBottom.y + h * Mathf.Lerp(0.12f, 0.88f, k);
                float radius = RadiusAt(y);
                float size = radius * 0.55f * b.Size * (1f + heat * 0.35f);
                float stretch = 1f + 0.35f * Mathf.Sin(wobble * b.Speed * 1.7f + b.Phase);
                var local = new Vector3(b.X * (radius - size * 0.5f), y - bottleBottom.y, b.Z * (radius - size * 0.5f));
                b.T.localPosition = bottom.Local + local;
                b.T.localScale = new Vector3(size / Mathf.Sqrt(stretch), size * stretch, size / Mathf.Sqrt(stretch));
            }
            if (glow)
            {
                glow.transform.position = Pos(bottom) + Vector3.up * h * 0.5f + Vector3.back * 0.2f;
                glow.intensity = On("light") ? (0.9f + heat * 1.4f + (erupting ? 1.5f : 0f)) * (0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 2f, 0.7f)) : 0f;
            }
        }

        IEnumerator Erupt()
        {
            if (erupting) yield break;
            erupting = true;
            Sfx.Play("pop", 1f, 0.05f);
            Sfx.Play("slop", 0.8f, 0.1f);
            HidePart("cap", true);
            rig.Bonk(0.35f);
            Transform cap = Item(PrimitiveType.Cylinder, new Color(0.55f, 0.3f, 0.75f), new Vector3(0.14f, 0.08f, 0.14f));
            cap.position = Pos(mouthOfNeck) + Vector3.up * 0.06f;
            ToDesk(cap, new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(3.5f, 4.5f), -0.3f), 0.07f, 0.04f);
            Color goo = new Color(wax.r, wax.g, wax.b, 0.95f);
            yield return Stream(() => Pos(mouthOfNeck), () => (Vector3.up + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.1f))).normalized,
                goo, 2.2f, 55f, new Vector2(1.8f, 3.2f), new Vector2(0.016f, 0.028f), 0.05f);
            for (int i = 0; i < 4; i++)
            {
                RunDown(Random.Range(0.8f, 0.9f), Random.Range(-0.6f, 0.6f), goo, 0.028f, 0.08f);
                yield return new WaitForSeconds(0.25f);
            }
            heat = 0.2f;
            yield return new WaitForSeconds(6f);
            HidePart("cap", false);
            erupting = false;
        }

        public override void Burst() => Run(Erupt());
        public override void OnSlam() => Run(Erupt());

        protected override void OnDisable()
        {
            base.OnDisable();
            erupting = false;
        }
    }
}
