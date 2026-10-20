using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Hammy the hamster. Its own arms are rigged as stretchy cartoon arms (BendRig.AddStretchArm): when you type
    /// they stretch down to the keyboard and its paws tap the keys, the right one goes to the mouse when you use it,
    /// and they spring back to its belly when you stop (the desk's floating mittens step aside while it's on).
    /// Talking stuffs its cheeks, which puff out; open wide and it sprays the mouthful of sunflower seeds across the
    /// desk. !shake is a seed shower; the face slam empties its cheeks everywhere.
    /// </summary>
    public sealed class Hamster : GagAvatar
    {
        [Tooltip("Each arm: where it joins the body and the middle of its paw (rest, avatar space; screen-left arm, the other is mirrored).")]
        public Vector3 shoulder = new Vector3(-0.2f, 0.58f, -0.03f), paw = new Vector3(-0.1f, 0.44f, -0.32f);
        [Tooltip("How high above the desk hand's middle the paw's middle sits (the paw's own size).")]
        public float pawLift = 0.025f;
        [Tooltip("The cheek pouches (rest, avatar space; screen-left one, mirrored) and their fur colour.")]
        public Vector3 cheek = new Vector3(-0.29f, 0.74f, -0.2f);
        public Color fur = new Color(0.93f, 0.66f, 0.3f);

        static readonly Color SeedDark = new Color(0.16f, 0.15f, 0.15f), SeedStripe = new Color(0.85f, 0.84f, 0.8f), Nut = new Color(0.72f, 0.5f, 0.28f);

        BendRig.StretchArm leftArm, rightArm;
        Anchor mouthAnchor, leftCheek, rightCheek;
        Transform leftPouch, rightPouch;
        float stuffed, nextRub, testUntil = -1f;
        float leftReach, leftSpeed, rightReach, rightSpeed;
        bool ownPaws;

        public override string MessName => "a mouthful of sunflower seeds";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "paws", Label = "Types with its own paws (hides the floating hands)" });
            into.Add(new AvatarFeature { Key = "cheeks", Label = "Talking stuffs its cheeks (they puff out)" });
        }

        public override void TestMoment()
        {
            testUntil = Time.time + 0.9f;
            Run(StuffThenSpit());
        }

        /// <summary>The test: cheeks puff right up (seen in the first picture), then PTOO (the second).</summary>
        IEnumerator StuffThenSpit()
        {
            stuffed = 1f;
            yield return new WaitForSeconds(0.85f);
            Spit();
        }

        public override string TestReport => $"cheeks {stuffed:0.00}{(leftArm != null ? $", arms rigged, reach {leftReach:0.00}/{rightReach:0.00}" : ", NO ARMS")}";

        protected override void Begin()
        {
            leftArm = rig.AddStretchArm("Left arm", shoulder, paw);
            rightArm = rig.AddStretchArm("Right arm", Mirror(shoulder), Mirror(paw));
            float face = TryGetComponent(out MeshAvatar avatar) ? avatar.faceFraction : 0.72f;
            mouthAnchor = Pin(new Vector3(0f, body.size.y * (face - 0.06f), body.min.z + 0.03f));
            leftCheek = Pin(cheek);
            rightCheek = Pin(Mirror(cheek));
            Material m = Tint(fur);
            leftPouch = Pouch(leftCheek, m);
            rightPouch = Pouch(rightCheek, m);
            UsePaws(On("paws"));
        }

        static Vector3 Mirror(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        Transform Pouch(Anchor at, Material m)
        {
            Transform t = Item(PrimitiveType.Sphere, fur, Vector3.zero);
            if (m) t.GetComponent<MeshRenderer>().sharedMaterial = m;
            t.SetParent(at.Bone, false);
            t.localPosition = at.Local;
            return t;
        }

        void UsePaws(bool own)
        {
            ownPaws = own;
            DeskScene desk = Chaos ? Chaos.desk : null;
            // The floating mittens shrink away (they still push things about, out of sight) while its own paws type.
            if (desk) desk.SetHands(own ? new DeskScene.HandStyle { Tint = Color.white, Scale = 0.001f } : (DeskScene.HandStyle?)null);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (rig && leftArm != null) UsePaws(On("paws"));
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            DeskScene desk = Chaos ? Chaos.desk : null;
            if (desk) desk.SetHands(null);
        }

        protected override void Tick(float dt)
        {
            if (On("paws") != ownPaws) UsePaws(On("paws"));
            // Typing, the paws stretch down onto the keys and tap them (they follow the desk's own typing hands,
            // hidden while it's on); the right one goes to the mouse when that's in use. Stop, and they spring back.
            DeskInput input = DeskInput.Instance;
            DeskScene desk = Chaos ? Chaos.desk : null;
            bool reaching = ownPaws && desk && desk.Shown && input;
            bool typing = reaching && input.SinceKey < 1.5f, mousing = reaching && input.SinceMouse < 1.5f;
            float step = Mathf.Min(dt, 0.05f);
            Spring(ref leftReach, ref leftSpeed, typing ? 1f : 0f, step);
            Spring(ref rightReach, ref rightSpeed, typing || mousing ? 1f : 0f, step);
            float t = Time.time;
            // Now and then, idle, it rubs its paws together like a hamster washing its face.
            Vector3 rub = Vector3.zero;
            if (t >= nextRub)
            {
                if (t > nextRub + 1.6f) nextRub = t + Random.Range(8f, 16f);
                else rub = new Vector3(Mathf.Sin((t - nextRub) * 18f) * 0.012f, 0.02f + Mathf.Sin((t - nextRub) * 9f) * 0.01f, 0f) * transform.lossyScale.y;
            }
            Vector3 lift = Vector3.up * pawLift * transform.lossyScale.y;
            if (leftArm != null)
                leftArm.Reach(desk ? desk.LeftHandPosition + lift : leftArm.RestPaw, leftReach, rub * (1f - Mathf.Clamp01(leftReach)));
            if (rightArm != null)
                rightArm.Reach(desk ? desk.RightHandPosition + lift : rightArm.RestPaw, rightReach, new Vector3(-rub.x, rub.y, rub.z) * (1f - Mathf.Clamp01(rightReach)));

            // Cheeks: talking stuffs them (they puff out), a big open mouth sprays the lot.
            if (On("cheeks") && mouth > 0.2f && !mouthJustOpened) stuffed = Mathf.Min(1f, stuffed + mouth * 0.12f * dt);
            if (mouthJustOpened && mouth > 0.6f && stuffed > 0.25f) Spit();
            float puff = On("cheeks") || t < testUntil ? Mathf.SmoothStep(0f, 1f, stuffed) : 0f;
            float wobble = 1f + 0.06f * Mathf.Sin(t * 11f) * puff;
            Vector3 s = new Vector3(0.17f, 0.15f, 0.17f) * puff * wobble;
            if (leftPouch) leftPouch.localScale = s;
            if (rightPouch) rightPouch.localScale = s;
        }

        /// <summary>A springy reach: it overshoots a little going out and coming back (boing).</summary>
        static void Spring(ref float x, ref float speed, float goal, float dt)
        {
            speed += ((goal - x) * 150f - speed * 15f) * dt;
            x += speed * dt;
        }

        /// <summary>PTOOOO: the cheeks empty, seeds spray out across the desk the way it's facing.</summary>
        void Spit()
        {
            int seeds = Scaled(Mathf.RoundToInt(8 + 26 * stuffed));
            Sfx.Play("pop", 0.7f, 0.2f);
            Sfx.Play("trash_clatter", 0.35f, 0.2f);
            rig.Bonk(0.2f);
            for (int i = 0; i < seeds; i++) Seed(Pos(mouthAnchor), new Vector3(TurnedSide * 0.7f, 0.3f, -1f).normalized, 1.2f);
            if (stuffed > 0.7f && Keep()) NutBit(Pos(mouthAnchor));
            stuffed = 0f;
        }

        void Seed(Vector3 at, Vector3 aim, float force)
        {
            // A sunflower seed: a dark teardrop with a pale stripe.
            Transform seed = Lump(SeedDark, new Vector3(0.026f, 0.014f, 0.044f));
            Transform stripe = Item(PrimitiveType.Sphere, SeedStripe, Vector3.one);
            stripe.SetParent(seed, false);
            stripe.localPosition = new Vector3(0f, 0.3f, 0f);
            stripe.localScale = new Vector3(0.25f, 0.5f, 0.9f);
            seed.position = at + Random.insideUnitSphere * 0.02f;
            Vector3 v = (aim + Random.insideUnitSphere * 0.45f).normalized * Random.Range(1f, 2.2f) * force + meter.Velocity * 0.4f;
            ToDesk(seed, v, 0.018f, 0.007f);
        }

        void NutBit(Vector3 at)
        {
            Transform nut = Lump(Nut, new Vector3(0.03f, 0.022f, 0.026f));
            nut.position = at;
            ToDesk(nut, new Vector3(Random.Range(-0.5f, 0.5f), 1f, -1.1f), 0.015f, 0.011f, ball: true);
        }

        public override void Burst()
        {
            stuffed = 1f;
            Spit();
        }

        public override void OnSlam() => Run(Shower());

        IEnumerator Shower()
        {
            stuffed = 1f;
            Spit();
            for (int i = 0; i < 16; i++)
            {
                Seed(Pos(mouthAnchor), Outward(), 0.9f);
                yield return new WaitForSeconds(0.03f);
            }
        }

        public override void Whack(float strength)
        {
            base.Whack(strength);
            Sfx.Play("squelch", 0.4f, 0.2f);
            if (stuffed > 0.3f) Spit();
        }
    }
}
