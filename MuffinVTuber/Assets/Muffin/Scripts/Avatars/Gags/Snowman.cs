using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The snowman: it slowly melts over the stream, dripping into a growing puddle at its feet, and puffs of cold
    /// breath come out when you talk. A hard shake knocks its hat off; !shake shakes snow off it in a flurry; the face
    /// slam knocks its carrot nose, coal buttons and hat flying. Everything is back on a little later.
    /// </summary>
    public sealed class Snowman : GagAvatar
    {
        [Tooltip("The carrot's tip, the hat's middle and the head's middle (rest, avatar space).")]
        public Vector3 carrot, hat, head;
        public Vector3[] buttons = new Vector3[0];
        public float hatSize = 0.22f;

        static readonly Color Melt = new Color(0.75f, 0.88f, 1f, 0.55f), Snow = new Color(0.97f, 0.98f, 1f), Carrot = new Color(1f, 0.5f, 0.1f),
            Coal = new Color(0.08f, 0.08f, 0.09f), Breath = new Color(0.95f, 0.97f, 1f, 0.35f);

        Anchor nose, hatAnchor, headAnchor;
        float nextDrip, nextPuddle, puddle = 0.06f, nextHat, backAt = -1f;
        bool hatOff, partsOff;

        public override string MessName => "snow, a carrot and a hat";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "melt", Label = "Melts slowly, dripping into a puddle" });
            into.Add(new AvatarFeature { Key = "breath", Label = "Cold breath when you talk" });
            into.Add(new AvatarFeature { Key = "hat", Label = "A hard shake knocks its hat off" });
        }

        public override void TestMoment() => KnockHat();
        public override string TestReport => (hatOff ? "hat off" : "hat on") + (partsOff ? ", nose and buttons off" : "");

        protected override void Begin()
        {
            if (head == Vector3.zero) head = new Vector3(0f, body.size.y * 0.7f, 0f);
            if (carrot == Vector3.zero) carrot = head + new Vector3(0f, 0f, body.min.z);
            if (hat == Vector3.zero) hat = new Vector3(0f, body.max.y - hatSize * 0.5f, 0f);
            nose = Pin(carrot);
            hatAnchor = Pin(hat);
            headAnchor = Pin(head);
            nextDrip = Time.time + Random.Range(6f, 12f);
            nextPuddle = Time.time + 15f;
        }

        protected override void Tick(float dt)
        {
            if (On("melt"))
            {
                if (Time.time >= nextDrip)
                {
                    nextDrip = Time.time + Random.Range(8f, 16f);
                    RunDown(Random.Range(0.45f, 0.65f), Random.Range(-0.7f, 0.7f), Melt, 0.022f, 0.05f);
                }
                if (Time.time >= nextPuddle && Mess.Instance)
                {
                    nextPuddle = Time.time + Random.Range(20f, 35f);
                    puddle = Mathf.Min(0.22f, puddle + 0.015f);
                    Mess.Instance.DeskStain(InFront(Random.Range(-0.1f, 0.1f), 0.02f), puddle, Melt, 600f, 2f, true);
                }
            }
            if (On("breath") && mouth > 0.25f && puffs != null && Random.value < 10f * dt)
                puffs.Emit(Pos(nose) + Vector3.down * 0.06f, new Vector3(Random.Range(-0.1f, 0.1f), 0.05f, -Random.Range(0.3f, 0.6f)), Breath, 0.05f, 1.2f, 3f, 1.5f, 0.03f);
            if (On("hat") && !hatOff && meter.Shake > 50f && Time.time >= nextHat) KnockHat();
            if (backAt > 0f && Time.time >= backAt) PutBack();
        }

        void KnockHat()
        {
            if (hatOff) return;
            hatOff = true;
            nextHat = Time.time + 12f;
            if (backAt < 0f) backAt = Time.time + 10f;
            HidePart("hat", true);
            Sfx.Play("flop", 0.6f, 0.15f);
            // The top hat: a crown and a brim, spinning off onto the desk.
            Transform crown = Item(PrimitiveType.Cylinder, Coal, new Vector3(hatSize * 0.75f, hatSize * 0.45f, hatSize * 0.75f));
            Transform brim = Item(PrimitiveType.Cylinder, Coal, Vector3.one);
            brim.SetParent(crown, false);
            brim.localPosition = new Vector3(0f, -0.95f, 0f);
            brim.localScale = new Vector3(1.5f, 0.06f, 1.5f);
            crown.position = Pos(hatAnchor);
            ToDesk(crown, new Vector3(Random.Range(-1f, 1f), Random.Range(1.6f, 2.2f), -0.5f), hatSize * 0.55f, hatSize * 0.25f);
        }

        /// <summary>The face slam: nose and buttons fly, the hat comes off, a dusting of snow.</summary>
        void KnockParts()
        {
            partsOff = true;
            backAt = Time.time + 12f;
            HidePart("carrot", true);
            HidePart("coal", true);
            Sfx.Play("pop", 0.6f, 0.1f);
            Transform c = Item(PrimitiveType.Capsule, Carrot, new Vector3(0.035f, 0.08f, 0.035f));
            c.position = Pos(nose);
            c.rotation = Quaternion.Euler(90f, 0f, 0f);
            ToDesk(c, new Vector3(Random.Range(-0.7f, 0.7f), 1.2f, -1.2f), 0.04f, 0.018f);
            for (int i = 0; i < Mathf.Max(3, buttons.Length); i++)
            {
                Vector3 at = i < buttons.Length ? Pos(Pin(buttons[i])) : Pos(headAnchor) + Vector3.down * (0.2f + i * 0.12f) + Vector3.back * body.extents.z;
                Transform lump = Lump(Coal, Vector3.one * 0.035f);
                lump.position = at;
                ToDesk(lump, Outward() * Random.Range(0.4f, 1f) + Vector3.up * Random.Range(0.4f, 1f), 0.018f, 0.016f);
            }
            KnockHat();
        }

        void PutBack()
        {
            backAt = -1f;
            hatOff = partsOff = false;
            ShowAllParts();
            Sfx.Play("scrub", 0.3f, 0.1f);
        }

        void SnowFlurry(int chunks)
        {
            for (int i = 0; i < chunks; i++)
            {
                Transform chunk = Lump(Snow, Vector3.one * Random.Range(0.025f, 0.05f));
                chunk.position = Pos(headAnchor) + Random.insideUnitSphere * body.extents.x * 0.7f;
                ToDesk(chunk, Outward() * Random.Range(0.3f, 0.9f) + Vector3.up * Random.Range(0.4f, 1f), 0.018f, 0.014f);
            }
            if (puffs != null)
                for (int i = 0; i < 12; i++)
                    puffs.Emit(Pos(headAnchor) + Random.insideUnitSphere * 0.25f, Random.insideUnitSphere * 0.4f + Vector3.down * 0.1f, new Color(1f, 1f, 1f, 0.35f), 0.12f, 1.6f, 2.5f, 1.2f, -0.02f);
            if (Mess.Instance)
                for (int i = 0; i < 3; i++)
                    Mess.Instance.DeskDust(InFront(Random.Range(-0.35f, 0.35f), Random.Range(0.02f, 0.3f)), Random.Range(0.05f, 0.09f), new Color(0.97f, 0.98f, 1f, 0.75f), 120f, 0.6f);
        }

        public override void Burst()
        {
            Sfx.Play("spore_puff", 0.6f, 0.1f);
            SnowFlurry(Scaled(10));
            KnockHat();
        }

        public override void OnSlam()
        {
            SnowFlurry(Scaled(14));
            KnockParts();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            hatOff = partsOff = false;
            backAt = -1f;
        }
    }
}
