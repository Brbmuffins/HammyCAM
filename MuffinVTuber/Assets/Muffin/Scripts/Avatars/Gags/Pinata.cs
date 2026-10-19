using System.Collections;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The star piñata, on a string from the ceiling: every whack (!whack's bat, !shake, the face slam, or a really
    /// hard shake of your own head) spins it, knocks confetti and the odd sweet out of it and takes it closer to
    /// breaking; after enough hits it BURSTS: sweets rain onto the desk with a blizzard of confetti and paper
    /// scraps. A while later it's been taped back together and filled up again.
    /// </summary>
    public sealed class Pinata : GagAvatar
    {
        [Tooltip("Hits it takes to burst.")]
        public int hitsToBreak = 7;
        [Tooltip("The top point and the middle (rest, avatar space); zero = worked out from the model's size.")]
        public Vector3 topTip, centre;
        [Tooltip("Seconds until it's taped back up after bursting.")]
        public float refillAfter = 25f;

        static readonly Color[] Party =
        {
            new Color(1f, 0.25f, 0.55f), new Color(1f, 0.82f, 0.1f), new Color(0.2f, 0.75f, 1f), new Color(0.35f, 0.9f, 0.35f),
            new Color(0.7f, 0.35f, 1f), new Color(1f, 0.5f, 0.12f),
        };

        Anchor tip, middle;
        LineRenderer stringLine;
        float damage, nextSelfHit;
        bool broken;

        public float Damage => damage;
        public bool Broken => broken;
        public override void TestMoment()
        {
            damage = 0.99f;
            Hit(1f, 1f);
        }
        public override string TestReport => broken ? "burst" : $"damage {damage:0.00}";
        public override string MessName => "candy and confetti";

        protected override void Begin()
        {
            if (topTip == Vector3.zero) topTip = new Vector3(0f, body.max.y - 0.02f, 0f);
            if (centre == Vector3.zero) centre = body.center;
            tip = Pin(topTip);
            middle = Pin(centre);
            // The string it hangs from, up out of the frame.
            stringLine = new GameObject("Piñata string").AddComponent<LineRenderer>();
            stringLine.transform.SetParent(transform, false);
            stringLine.positionCount = 2;
            stringLine.widthMultiplier = 0.006f;
            stringLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Material m = Tint(new Color(0.92f, 0.88f, 0.78f));
            if (m) stringLine.sharedMaterial = m;
        }

        protected override void Tick(float dt)
        {
            if (stringLine)
            {
                Vector3 from = Pos(tip);
                stringLine.SetPosition(0, from);
                stringLine.SetPosition(1, new Vector3(transform.position.x, from.y + 3f, transform.position.z));
            }
            // A really hard shake of your own head is a hit too (you can bash it open yourself).
            if (!broken && meter.Shake > 55f && Time.time >= nextSelfHit)
            {
                nextSelfHit = Time.time + 1.2f;
                Hit(0.4f, Random.value < 0.5f ? -1f : 1f);
            }
            if (mouthJustOpened && !broken && Random.value < 0.35f) Confetti(6, 0.6f); // a cheer shakes a little loose
        }

        public override void Whack(float strength) => Hit(strength, Random.value < 0.5f ? -1f : 1f);

        public override void Burst() => Hit(1f, Random.value < 0.5f ? -1f : 1f);

        public override void OnSlam() => Hit(2.5f, 0f);

        void Hit(float strength, float side)
        {
            rig.Bonk(0.3f * Mathf.Min(strength, 1.5f));
            rig.KickTip(new Vector3((side == 0f ? Random.Range(-1f, 1f) : side) * 2.2f, 0.4f, Random.Range(-0.3f, 0.3f)) * Mathf.Min(strength, 1.6f));
            if (broken)
            {
                // Nothing left in it: just a sad, papery thwack and a couple of scraps.
                Sfx.Play("flop", 0.6f, 0.15f);
                Scraps(3);
                return;
            }
            Sfx.Play("pop", 0.5f, 0.2f);
            Confetti(Mathf.RoundToInt(18 * strength), 1f);
            Scraps(Mathf.RoundToInt(2 * strength));
            for (int i = 0; i < Mathf.RoundToInt(Random.Range(0f, 1.6f) * strength); i++) Sweet(Pos(middle), 1f);
            damage += strength / hitsToBreak;
            if (damage >= 1f) Run(BurstOpen());
        }

        IEnumerator BurstOpen()
        {
            broken = true;
            Sfx.Play("pop", 1f, 0.05f);
            Sfx.Play("trash_clatter", 0.8f, 0.1f);
            Sfx.Play("sprinkle_rain", 0.8f, 0.1f);
            rig.Bonk(0.45f);
            Vector3 c = Pos(middle);
            Confetti(260, 1.6f);
            Scraps(18);
            for (int i = 0; i < 34; i++)
            {
                Sweet(c + Random.insideUnitSphere * 0.12f, 1.5f);
                if (i % 6 == 5) yield return null; // spread over a few frames
            }
            if (puffs != null)
                for (int i = 0; i < 10; i++)
                    puffs.Emit(c + Random.insideUnitSphere * 0.2f, Random.insideUnitSphere * 0.7f, new Color(1f, 0.95f, 0.85f, 0.35f), 0.22f, 1.4f, 2.5f, 2f);
            yield return new WaitForSeconds(refillAfter);
            // Taped back together and filled up again.
            Sfx.Play("scrub", 0.4f, 0.1f);
            rig.Bonk(0.12f);
            damage = 0f;
            broken = false;
        }

        /// <summary>Confetti off the piñata (the desk's sprinkles, in party colours), flying out and fluttering down.</summary>
        void Confetti(int count, float force)
        {
            if (!DeskDebris.Instance) return;
            Vector3 c = Pos(middle);
            for (int i = 0; i < count; i++)
            {
                Vector3 at = c + Vector3.Scale(Random.insideUnitSphere, new Vector3(body.extents.x * 0.7f, body.extents.y * 0.5f, body.extents.z * 0.6f));
                Vector3 v = (at - c).normalized * Random.Range(0.5f, 1.6f) * force + Vector3.up * Random.Range(0.4f, 1.8f) * force + Vector3.back * Random.Range(0f, 0.6f);
                DeskDebris.Instance.EmitSprinkle(at, v);
            }
        }

        /// <summary>Torn bits of crepe-paper fringe.</summary>
        void Scraps(int count)
        {
            Vector3 c = Pos(middle);
            for (int i = 0; i < count; i++)
            {
                Transform scrap = Item(PrimitiveType.Cube, Party[Random.Range(0, Party.Length)], new Vector3(Random.Range(0.03f, 0.06f), 0.003f, Random.Range(0.02f, 0.04f)));
                scrap.position = c + Random.insideUnitSphere * body.extents.x * 0.6f;
                scrap.rotation = Random.rotation;
                ToDesk(scrap, Outward() * Random.Range(0.4f, 1.1f) + Vector3.up * Random.Range(0.6f, 1.6f), 0.025f, 0.0015f);
            }
        }

        /// <summary>A sweet: a wrapped candy (twists at both ends) or a lollipop, bouncing onto the desk.</summary>
        void Sweet(Vector3 at, float force)
        {
            Color colour = Party[Random.Range(0, Party.Length)];
            Transform sweet;
            if (Random.value < 0.7f)
            {
                sweet = Item(PrimitiveType.Capsule, colour, new Vector3(0.026f, 0.022f, 0.026f));
                sweet.rotation = Quaternion.Euler(0f, 0f, 90f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Transform twist = Item(PrimitiveType.Sphere, Color.Lerp(colour, Color.white, 0.35f), Vector3.one);
                    twist.SetParent(sweet, false);
                    twist.localPosition = new Vector3(0f, s * 1.25f, 0f);
                    twist.localScale = new Vector3(0.9f, 0.5f, 0.9f);
                }
            }
            else
            {
                sweet = Item(PrimitiveType.Sphere, colour, new Vector3(0.04f, 0.04f, 0.015f));
                Transform stick = Item(PrimitiveType.Cylinder, new Color(0.97f, 0.96f, 0.93f), Vector3.one);
                stick.SetParent(sweet, false);
                stick.localPosition = new Vector3(0f, -1.4f, 0f);
                stick.localScale = new Vector3(0.12f, 1.2f, 0.3f);
            }
            sweet.position = at;
            Vector3 v = Outward() * Random.Range(0.3f, 0.9f) * force + Vector3.up * Random.Range(0.5f, 1.4f) * force + meter.Velocity * 0.4f;
            ToDesk(sweet, v, 0.022f, 0.012f, ball: Random.value < 0.3f);
            BitSound("trash_clatter", 0.25f);
        }
    }
}
