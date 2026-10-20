using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The avocado half: open your mouth wide and the pit POPS out of its socket and rolls off across the desk (the
    /// hollow it left shows); a few seconds later it rolls back, hops up and plops into place. Moving flicks bits of
    /// guacamole about; !shake throws a dollop; the face slam smears guac all over the desk and pops the pit.
    /// </summary>
    public sealed class Avocado : GagAvatar
    {
        [Tooltip("The pit's middle (rest, avatar space) and its radius.")]
        public Vector3 pit = new Vector3(-0.002f, 0.367f, -0.135f);
        public float pitRadius = 0.186f;

        static readonly Color Guac = new Color(0.55f, 0.72f, 0.25f), GuacSmear = new Color(0.5f, 0.68f, 0.22f, 0.85f), PitBrown = new Color(0.45f, 0.24f, 0.12f);

        Anchor socket;
        Transform loose;
        Material pitMaterial;
        float owed;
        bool pitOut;

        public override string MessName => "guacamole and a runaway pit";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "pit", Label = "Opening your mouth wide pops the pit out" });
            into.Add(new AvatarFeature { Key = "guac", Label = "Moving flicks guacamole about" });
        }

        public override void TestMoment() => Run(PopPit());
        public override string TestReport => pitOut ? "pit out" : "pit in";

        protected override void Begin()
        {
            socket = Pin(pit);
            pitMaterial = Tint(PitBrown);
            if (pitMaterial)
            {
                pitMaterial = new Material(pitMaterial) { name = "Avocado pit" };
                pitMaterial.SetFloat("_Smoothness", 0.7f);
            }
        }

        protected override void Tick(float dt)
        {
            if (On("pit") && !pitOut && mouthJustOpened && mouth > 0.55f) Run(PopPit());
            if (On("guac"))
            {
                owed += (meter.Speed * 0.6f + Mathf.Max(0f, meter.Shake - 10f) * 0.03f) * Amount * dt;
                for (; owed >= 1f; owed -= 1f) GuacBit(0.8f);
            }
        }

        IEnumerator PopPit()
        {
            if (pitOut) yield break;
            pitOut = true;
            Sfx.Play("pop", 1f, 0.05f);
            Sfx.Play("squelch", 0.6f, 0.1f);
            rig.Bonk(0.2f);
            HidePart("pit", true);
            loose = Item(PrimitiveType.Sphere, PitBrown, Vector3.one * pitRadius * 2f);
            if (pitMaterial) loose.GetComponent<MeshRenderer>().sharedMaterial = pitMaterial;
            loose.position = Pos(socket);
            if (DeskDebris.Instance) DeskDebris.Instance.AddPiece(loose, new Vector3(Random.Range(-0.9f, 0.9f), 1.4f, -1.4f), pitRadius, pitRadius, disposable: false, ball: true);
            yield return new WaitForSeconds(7f);
            // Home again: it rolls back, hops up and plops into the socket.
            if (DeskDebris.Instance && loose) DeskDebris.Instance.Take(loose);
            Vector3 from = loose ? loose.position : Pos(socket);
            for (float t = 0f; t < 0.6f && loose; t += Time.deltaTime)
            {
                float k = t / 0.6f;
                loose.position = Vector3.Lerp(from, Pos(socket), k) + Vector3.up * 4f * 0.3f * k * (1f - k);
                loose.Rotate(Vector3.right, 720f * Time.deltaTime, Space.World);
                yield return null;
            }
            if (loose) Destroy(loose.gameObject);
            loose = null;
            HidePart("pit", false);
            Sfx.Play("plop", 0.7f, 0.1f);
            rig.Bonk(0.12f);
            pitOut = false;
        }

        void GuacBit(float force)
        {
            Transform bit = Lump(Guac, Vector3.one * Random.Range(0.02f, 0.035f));
            bit.position = Pos(socket) + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.1f, 0.4f), -0.05f);
            ToDesk(bit, Outward() * Random.Range(0.3f, 0.8f) * force + Vector3.up * Random.Range(0.3f, 0.8f) * force + meter.Velocity * 0.5f, 0.014f, 0.01f);
        }

        public override void Burst()
        {
            Sfx.Play("squelch", 0.7f, 0.1f);
            for (int i = 0; i < Scaled(10); i++) GuacBit(1.2f);
            if (Mess.Instance) Mess.Instance.Dollop(null, InFront(Random.Range(-0.2f, 0.2f), 0.15f), 0.1f, Guac, 120f);
        }

        public override void OnSlam()
        {
            Sfx.Play("pie_splat", 0.8f, 0.1f);
            if (Mess.Instance)
            {
                Mess.Instance.DeskStain(InFront(0f, 0.08f), 0.2f, GuacSmear, 150f, 0.2f, false, new Vector2(0f, -1.2f));
                for (int i = 0; i < Scaled(4); i++)
                    Mess.Instance.Blob(null, InFront(Random.Range(-0.3f, 0.3f), Random.Range(0.02f, 0.3f)) + Vector3.up * 0.015f, new Vector3(0.07f, 0.03f, 0.07f), Guac, 150f, 0.2f);
            }
            Run(PopPit());
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (loose)
            {
                if (DeskDebris.Instance) DeskDebris.Instance.Take(loose);
                Destroy(loose.gameObject);
            }
            loose = null;
            pitOut = false;
        }
    }
}
