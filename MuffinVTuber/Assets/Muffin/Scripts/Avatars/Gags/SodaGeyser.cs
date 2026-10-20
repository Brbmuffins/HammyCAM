using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The 2-litre soda bottle: shaking it (and talking) builds up fizz: it hisses, swells and foam leaks round the
    /// cap. Open your mouth wide once it's fizzing (or !shake: a few mints drop in) and the cap BLOWS OFF: a geyser of
    /// soda shoots up out of the top of the frame and rains back down all over the desk, sticky puddles everywhere,
    /// splashes on the camera glass. The cap comes back after a while (a fresh bottle).
    /// </summary>
    public sealed class SodaGeyser : GagAvatar
    {
        [Tooltip("The bottle's mouth under the cap, and the top of the cap (rest, avatar space); zero = the model's top.")]
        public Vector3 neckTop, capTop;

        static readonly Color Soda = new Color(0.55f, 0.3f, 0.12f, 0.85f), Foam = new Color(0.96f, 0.88f, 0.74f, 0.9f),
            Sticky = new Color(0.5f, 0.28f, 0.1f, 0.6f), Mist = new Color(0.95f, 0.88f, 0.78f, 0.35f), CapRed = new Color(0.85f, 0.12f, 0.1f);

        Anchor neck;
        float fizz, hissOwed;
        bool blowing, capOff;
        SkinnedMeshRenderer skin;
        int capSlot = -1;
        Material capMaterial, invisible;
        SoundLoop hiss;

        public float Fizz => fizz;
        public override void TestMoment()
        {
            fizz = 1f;
            Run(Blow(1f));
        }
        public override string TestReport => capOff ? "cap off" : $"fizz {fizz:0.00}";
        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "hiss", Label = "Hisses and leaks foam while it fizzes" });
            into.Add(new AvatarFeature { Key = "lens", Label = "A splash of the rain lands on the camera glass" });
        }

        public override string MessName => "a soda geyser";

        protected override void Begin()
        {
            if (capTop == Vector3.zero) capTop = new Vector3(0f, body.max.y, 0f);
            if (neckTop == Vector3.zero) neckTop = capTop + Vector3.down * 0.04f;
            neck = Pin(neckTop);
            // The cap is its own material on the model: when it flies off, that part of the bottle is hidden.
            skin = GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin)
            {
                Material[] mats = skin.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && mats[i].name.ToLowerInvariant().Contains("cap")) capSlot = i;
                if (capSlot >= 0) capMaterial = mats[capSlot];
            }
            if (Mess.Instance && Mess.Instance.stainMaterial)
            {
                invisible = new Material(Mess.Instance.stainMaterial) { name = "Gone" };
                invisible.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            }
        }

        protected override void Tick(float dt)
        {
            if (blowing) return;
            float over = Mathf.Max(0f, meter.Shake - 6f);
            fizz = Mathf.Clamp01(fizz + (over * 0.006f + meter.Speed * 0.012f + (mouth > 0.3f ? 0.02f : 0f) - 0.012f) * dt);
            // It hisses and swells, and foam leaks round the cap, the more it's fizzing.
            if (fizz > 0.35f && On("hiss"))
            {
                hiss ??= Sfx.Loop("steam_hiss", 0f);
                hiss?.SetVolume((fizz - 0.35f) * 0.6f);
                if (!capOff)
                {
                    hissOwed += fizz * 6f * dt;
                    for (; hissOwed >= 1f; hissOwed -= 1f)
                        puffs?.Emit(Pos(neck) + Random.insideUnitSphere * 0.02f, new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.1f, 0.3f), Random.Range(-0.1f, 0f)), Mist, 0.05f, 0.9f, 2.5f, 1.5f);
                    if (Random.value < fizz * dt * 2f) rig.Bonk(0.04f + fizz * 0.05f);
                }
            }
            else if (hiss != null)
            {
                hiss.Stop();
                hiss = null;
            }
            if (fizz >= 1f || (mouthJustOpened && mouth > 0.55f && fizz > 0.3f)) Run(Blow(Mathf.Lerp(0.8f, 1.25f, fizz)));
        }

        public override void Burst()
        {
            if (!blowing) Run(Mints());
        }

        public override void OnSlam()
        {
            fizz = 1f;
            if (!blowing) Run(Blow(1.1f));
        }

        /// <summary>!shake: a roll of mints is dropped in through the top, and that's that.</summary>
        IEnumerator Mints()
        {
            blowing = true;
            Vector3 above = Pos(neck) + Vector3.up * 0.9f;
            for (int i = 0; i < 4; i++)
            {
                Transform mint = Item(PrimitiveType.Cylinder, new Color(0.98f, 0.98f, 0.96f), new Vector3(0.03f, 0.006f, 0.03f));
                mint.position = above + Random.insideUnitSphere * 0.03f;
                mint.rotation = Random.rotation;
                Destroy(mint.gameObject, 0.55f);
                Run(FallInto(mint, 0.5f));
                yield return new WaitForSeconds(0.08f);
            }
            yield return new WaitForSeconds(0.45f);
            Sfx.Play("bubbles", 0.8f, 0.1f);
            fizz = 1f;
            blowing = false;
            yield return Blow(1.3f);
        }

        IEnumerator FallInto(Transform mint, float seconds)
        {
            Vector3 from = mint.position;
            for (float t = 0f; t < seconds && mint; t += Time.deltaTime)
            {
                float k = t / seconds;
                mint.position = Vector3.Lerp(from, Pos(neck), k * k);
                yield return null;
            }
        }

        IEnumerator Blow(float strength)
        {
            if (blowing) yield break;
            blowing = true;
            hiss?.Stop(0.2f);
            hiss = null;
            // POP: the cap shoots up off the top of the frame and lands on the desk somewhere.
            Sfx.Play("pop", 1f, 0.05f);
            Sfx.Play("steam_hiss", 0.9f, 0.08f);
            rig.Bonk(0.3f);
            if (!capOff)
            {
                capOff = true;
                if (skin && capSlot >= 0 && invisible)
                {
                    Material[] mats = skin.sharedMaterials;
                    mats[capSlot] = invisible;
                    skin.sharedMaterials = mats;
                }
                Transform cap = Item(PrimitiveType.Cylinder, CapRed, new Vector3(0.06f, 0.02f, 0.06f));
                cap.position = Pos(neck) + Vector3.up * 0.03f;
                ToDesk(cap, new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(5f, 6.5f), Random.Range(-0.4f, 0f)), 0.03f, 0.012f);
            }
            float seconds = 3f * strength;
            // The geyser: a thick column of soda and foam straight up, swaying, raining back down everywhere.
            Run(Stream(() => Pos(neck), () => (Vector3.up + new Vector3(Random.Range(-0.18f, 0.18f), 0f, Random.Range(-0.2f, 0.08f))).normalized,
                Soda, seconds, 85f, new Vector2(3.4f, 6f) * Mathf.Sqrt(strength), new Vector2(0.014f, 0.026f), 0.045f));
            Run(Stream(() => Pos(neck), () => (Vector3.up + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.1f))).normalized,
                Foam, seconds * 0.85f, 45f, new Vector2(2.6f, 4.6f), new Vector2(0.016f, 0.03f), 0.05f));
            float until = Time.time + seconds, nextSound = 0f, nextGlass = Time.time + 0.7f;
            while (Time.time < until)
            {
                puffs?.Emit(Pos(neck) + Vector3.up * 0.05f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(1.6f, 2.6f), Random.Range(-0.2f, 0.1f)), Mist, Random.Range(0.08f, 0.14f), 1.2f, 3f, 1.2f);
                if (Time.time >= nextSound)
                {
                    nextSound = Time.time + Random.Range(0.4f, 0.7f);
                    Sfx.Play(Random.value < 0.5f ? "sprinklers" : "water_spill", 0.45f, 0.1f);
                }
                if (Time.time >= nextGlass)
                {
                    // One drop of the rain on the lens, off to the side (more than that is distracting).
                    nextGlass = float.MaxValue;
                    if (On("lens") && Random.value < 0.6f) GlassItem.Splash(Soda, GlassItem.EdgeSpot(), Random.Range(0.05f, 0.07f), Random.Range(4f, 6f));
                }
                rig.Bonk(0.03f);
                yield return null;
            }
            // Sticky puddles all round, and foam still dribbling down the bottle.
            if (Mess.Instance)
                for (int i = 0; i < 5; i++)
                    Mess.Instance.DeskStain(InFront(Random.Range(-0.5f, 0.5f), Random.Range(-0.1f, 0.35f)), Random.Range(0.06f, 0.14f), Sticky, 200f, 1f, true);
            for (int i = 0; i < 3; i++)
            {
                RunDown(Random.Range(0.86f, 0.94f), Random.Range(-0.5f, 0.5f), Foam, 0.026f, 0.08f);
                yield return new WaitForSeconds(0.3f);
            }
            fizz = 0f;
            blowing = false;
            yield return new WaitForSeconds(9f);
            // A fresh bottle, cap back on.
            if (skin && capSlot >= 0 && capMaterial)
            {
                Material[] mats = skin.sharedMaterials;
                mats[capSlot] = capMaterial;
                skin.sharedMaterials = mats;
            }
            capOff = false;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            hiss?.Stop(0.1f);
            hiss = null;
            blowing = false;
            if (capOff && skin && capSlot >= 0 && capMaterial)
            {
                Material[] mats = skin.sharedMaterials;
                mats[capSlot] = capMaterial;
                skin.sharedMaterials = mats;
                capOff = false;
            }
        }
    }
}
