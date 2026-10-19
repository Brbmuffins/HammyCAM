using System.Collections;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The watermelon spits seeds: open your mouth and PTOO, a seed or three fly out of it, aimed the way you're
    /// turned; some smack onto the camera glass and slide slowly down it, the rest skitter across the desk. Keep it
    /// open and it machine-guns them. !shake is a seed storm with a spray of juice; the face slam smashes it open:
    /// red chunks, a flood of pink juice, seeds everywhere and a splash on the lens.
    /// </summary>
    public sealed class Watermelon : GagAvatar
    {
        [Tooltip("Where the seeds come out (rest, avatar space): the face's mouth; zero = worked out from the face.")]
        public Vector3 mouthPoint;
        [Tooltip("Share of seeds that hit the camera glass.")]
        [Range(0f, 1f)] public float toGlass = 0.35f;

        static readonly Color Seed = new Color(0.12f, 0.08f, 0.06f), Juice = new Color(0.98f, 0.32f, 0.42f, 0.8f),
            Flesh = new Color(0.95f, 0.22f, 0.3f), Rind = new Color(0.25f, 0.5f, 0.2f), Pale = new Color(0.88f, 0.96f, 0.75f);

        Anchor mouthAnchor;
        float rapidOwed;
        Material seedMaterial;

        public override void TestMoment() => Spit(3);
        public override string MessName => "watermelon seeds and juice";

        protected override void Begin()
        {
            if (mouthPoint == Vector3.zero)
            {
                float face = TryGetComponent(out MeshAvatar avatar) ? avatar.faceFraction : 0.55f;
                mouthPoint = new Vector3(0f, body.size.y * (face - 0.07f), body.min.z + 0.02f);
            }
            mouthAnchor = Pin(mouthPoint);
            seedMaterial = Tint(Seed);
        }

        protected override void Tick(float dt)
        {
            if (mouthJustOpened) Spit(Random.Range(1, 4));
            else if (mouthOpenFor > 0.5f)
            {
                rapidOwed += 7f * dt;
                for (; rapidOwed >= 1f; rapidOwed -= 1f) Spit(1);
            }
            else rapidOwed = 0f;
        }

        public override void Burst() => Run(SeedStorm(26));

        public override void OnSlam() => Smash();

        /// <summary>Which way the seeds go: the way your head is turned, out a bit to the sides, a bit toward the camera.</summary>
        Vector3 Aim()
        {
            float side = TurnedSide + Random.Range(-0.45f, 0.45f);
            return new Vector3(side * 0.9f, Random.Range(0.25f, 0.6f), -Random.Range(0.6f, 1f)).normalized;
        }

        void Spit(int count)
        {
            Sfx.Play("pop", 0.55f, 0.35f);
            rig.Bonk(0.05f);
            for (int i = 0; i < count; i++)
            {
                Vector3 aim = Aim();
                if (Random.value < toGlass) Run(ToGlass(aim));
                else SeedToDesk(Pos(mouthAnchor), aim * Random.Range(1.6f, 2.6f) + meter.Velocity * 0.5f);
            }
        }

        IEnumerator SeedStorm(int count)
        {
            Sfx.Play("squirt", 0.6f, 0.1f);
            Run(Stream(() => Pos(mouthAnchor), Aim, Juice, 0.5f, 50f, new Vector2(1.4f, 2.2f), new Vector2(0.01f, 0.018f), 0.03f));
            for (int i = 0; i < count; i++)
            {
                Spit(1);
                yield return new WaitForSeconds(Random.Range(0.03f, 0.08f));
            }
        }

        /// <summary>A seed flies at the lens: a moment later it smacks onto the glass where it was aimed, and slides down.</summary>
        IEnumerator ToGlass(Vector3 aim)
        {
            yield return new WaitForSeconds(Random.Range(0.08f, 0.16f));
            Vector2 at = new Vector2(Mathf.Clamp01(0.5f + aim.x * 0.42f + Random.Range(-0.12f, 0.12f)), Mathf.Clamp(0.45f + aim.y * 0.45f + Random.Range(-0.12f, 0.12f), 0.15f, 0.9f));
            if (Mess.Instance && seedMaterial)
            {
                GlassItem.Stick(Mess.Instance.Lumps[Random.Range(0, Mess.Instance.Lumps.Length)], seedMaterial, at, Random.Range(0.022f, 0.03f),
                    Random.Range(0.01f, 0.03f), Random.Range(7f, 12f), Random.Range(-40f, 40f));
            }
            Sfx.Play("splat", 0.3f, 0.3f);
        }

        void SeedToDesk(Vector3 at, Vector3 velocity)
        {
            Transform seed = Lump(Seed, new Vector3(0.012f, 0.007f, 0.018f));
            seed.position = at;
            ToDesk(seed, velocity, 0.008f, 0.0035f);
        }

        void Smash()
        {
            Sfx.Play("pie_splat", 1f, 0.06f);
            Sfx.Play("squelch", 0.9f, 0.1f);
            rig.Bonk(0.35f);
            Vector3 front = InFront(0f, 0.08f);
            for (int i = 0; i < 14; i++)
            {
                bool rind = Random.value < 0.3f;
                Transform chunk = Lump(rind ? Rind : Flesh, Vector3.one * Random.Range(0.04f, 0.075f));
                if (rind)
                {
                    // A rind piece: the green skin with a pale band and a bit of red still on it.
                    Transform pale = Lump(Pale, Vector3.one * 0.8f);
                    pale.SetParent(chunk, false);
                    pale.localPosition = new Vector3(0f, 0.25f, 0f);
                }
                chunk.position = front + Vector3.up * 0.15f + Random.insideUnitSphere * 0.1f;
                ToDesk(chunk, Outward() * Random.Range(0.4f, 1.3f) + Vector3.up * Random.Range(0.6f, 1.6f), 0.03f, 0.022f);
            }
            for (int i = 0; i < 40; i++)
                Drop(front + Vector3.up * 0.2f + Random.insideUnitSphere * 0.1f, Outward() * Random.Range(0.3f, 1.2f) + Vector3.up * Random.Range(0.4f, 1.4f), Juice,
                    Random.Range(0.014f, 0.024f), Random.Range(0.035f, 0.06f));
            if (Mess.Instance)
                for (int i = 0; i < 3; i++)
                    Mess.Instance.DeskStain(front + new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.1f, 0.1f)), Random.Range(0.08f, 0.14f), Juice, 160f, 0.3f, true);
            for (int i = 0; i < 24; i++) SeedToDesk(front + Vector3.up * 0.15f, Outward() * Random.Range(0.4f, 1.4f) + Vector3.up * Random.Range(0.5f, 1.5f));
            for (int i = 0; i < 4; i++) Run(ToGlass(new Vector3(Random.Range(-0.9f, 0.9f), Random.Range(0.1f, 0.6f), -1f)));
            GlassItem.Splash(Juice, new Vector2(Random.Range(0.25f, 0.75f), Random.Range(0.55f, 0.8f)), 0.11f, 9f);
            for (int i = 0; i < 3; i++) RunDown(Random.Range(0.6f, 0.75f), Random.Range(-0.6f, 0.6f), Juice, 0.026f);
        }
    }
}
