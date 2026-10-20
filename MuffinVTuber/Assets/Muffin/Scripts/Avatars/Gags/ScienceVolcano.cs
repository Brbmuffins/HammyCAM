using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The science-fair volcano: pressure builds while you talk (and slowly on its own): it rumbles, steam puffs out
    /// of the crater and the crater glows. Open your mouth wide once it's built up (or let it reach the top, or !shake)
    /// and it ERUPTS: red baking-soda foam fountains up out of the crater, comes down all over, runs down the slopes
    /// in rivers and floods across the desk in foamy puddles. The face slam blows it at once.
    /// </summary>
    public sealed class ScienceVolcano : GagAvatar
    {
        [Tooltip("The middle of the crater's opening (rest, avatar space); zero = the top of the model.")]
        public Vector3 crater;
        [Tooltip("The crater's radius (metres).")]
        public float craterRadius = 0.12f;
        [Tooltip("Pressure gained a second just sitting there, and while talking.")]
        public float buildIdle = 0.008f, buildTalking = 0.07f;

        static readonly Color Foam = new Color(0.93f, 0.2f, 0.16f, 0.95f), FoamPale = new Color(1f, 0.55f, 0.5f, 0.95f),
            Lava = new Color(0.98f, 0.38f, 0.1f, 0.95f), Steam = new Color(0.92f, 0.92f, 0.95f, 0.4f), Puddle = new Color(0.9f, 0.22f, 0.16f, 0.85f);

        Anchor top;
        Light glow;
        float pressure, nextRumble, steamOwed;
        bool erupting;

        public float Pressure => pressure;
        public override void TestMoment() => Run(Erupt(1f));
        public override string TestReport => $"pressure {pressure:0.00}";
        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "selfbuild", Label = "Pressure builds up by itself (and it erupts on its own)" });
            into.Add(new AvatarFeature { Key = "glow", Label = "The crater glows and lights the desk" });
        }

        public override string MessName => "a foaming eruption";

        protected override void Begin()
        {
            if (crater == Vector3.zero) crater = new Vector3(0f, body.max.y - 0.03f, 0f);
            top = Pin(crater);
            var light = new GameObject("Crater glow");
            light.transform.SetParent(top.Bone, false);
            light.transform.localPosition = top.Local + Vector3.up * 0.06f;
            glow = light.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.42f, 0.16f);
            glow.range = 0.9f;
            glow.intensity = 0f;
            pressure = Random.Range(0.1f, 0.3f);
        }

        protected override void Tick(float dt)
        {
            bool talking = mouth > 0.25f;
            if (!erupting) pressure = Mathf.Clamp01(pressure + ((On("selfbuild") ? buildIdle : 0f) + (talking ? buildTalking * Mathf.Clamp01(mouth * 1.6f) : 0f) + Mathf.Max(0f, meter.Shake - 10f) * 0.001f) * dt);

            // Steam, more the higher the pressure; rumbles that get closer together.
            steamOwed += (0.6f + pressure * 7f) * dt;
            for (int g = 0; steamOwed >= 1f && g < 4; g++)
            {
                steamOwed -= 1f;
                puffs?.Emit(Pos(top) + Random.insideUnitSphere * craterRadius * 0.6f, new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(0.25f, 0.45f), Random.Range(-0.05f, 0.02f)),
                    Steam, Random.Range(0.08f, 0.14f) * (1f + pressure), Random.Range(1.4f, 2.2f), 3f, 0.6f);
            }
            if (pressure > 0.45f && Time.time >= nextRumble && !erupting)
            {
                nextRumble = Time.time + Mathf.Lerp(2.2f, 0.45f, (pressure - 0.45f) / 0.55f);
                rig.Bonk(0.03f + pressure * 0.06f);
                rig.KickTip(new Vector3(Random.Range(-0.4f, 0.4f), 0f, 0f) * pressure);
                if (pressure > 0.7f) Sfx.Play("slam_thud", 0.25f + pressure * 0.2f, 0.25f);
            }
            if (glow) glow.intensity = !On("glow") ? 0f : Mathf.Lerp(glow.intensity, (erupting ? 3.2f : pressure * pressure * 2.2f) * (0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 7f, 0.3f)), 1f - Mathf.Exp(-dt * 8f));

            if (!erupting && (pressure >= 1f || (mouthJustOpened && mouth > 0.55f && pressure > 0.35f)))
                Run(Erupt(Mathf.Lerp(0.7f, 1.2f, pressure)));
        }

        public override void Burst()
        {
            pressure = Mathf.Max(pressure, 0.9f);
            if (!erupting) Run(Erupt(1.1f));
        }

        public override void OnSlam()
        {
            if (!erupting) Run(Erupt(1.3f));
        }

        IEnumerator Erupt(float strength)
        {
            erupting = true;
            Sfx.Play("slam_thud", 0.9f, 0.08f);
            Sfx.Play("steam_hiss", 0.8f, 0.1f);
            rig.Bonk(0.25f * strength);
            // A last deep rumble, then the cork comes out.
            for (int i = 0; i < 3; i++)
            {
                rig.KickTip(new Vector3(Random.Range(-0.8f, 0.8f), 0.3f, 0f));
                yield return new WaitForSeconds(0.12f);
            }
            Sfx.Play("squirt", 1f, 0.1f);
            Sfx.Play("slop", 0.9f, 0.1f);
            float seconds = 2.6f * strength;
            // The fountain: two streams of foam (deep red and pale) straight up with a wobble, raining down all round.
            Run(Stream(() => Pos(top), () => (Vector3.up + new Vector3(Random.Range(-0.35f, 0.35f), 0f, Random.Range(-0.4f, 0.1f))).normalized,
                Foam, seconds, 70f, new Vector2(2.2f, 3.8f) * Mathf.Sqrt(strength), new Vector2(0.016f, 0.03f), 0.055f));
            Run(Stream(() => Pos(top), () => (Vector3.up + new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.1f))).normalized,
                FoamPale, seconds * 0.8f, 40f, new Vector2(1.6f, 2.8f), new Vector2(0.014f, 0.024f), 0.05f));
            float until = Time.time + seconds;
            float nextRiver = 0f, nextBlob = 0f, nextSound = 0f;
            while (Time.time < until)
            {
                // Foam boiling over the rim and running down the slopes in rivers.
                if (Time.time >= nextRiver)
                {
                    nextRiver = Time.time + Random.Range(0.18f, 0.35f);
                    RunDown(Random.Range(0.8f, 0.92f), Random.Range(-0.7f, 0.7f), Random.value < 0.5f ? Foam : Lava, Random.Range(0.026f, 0.036f), Random.Range(0.12f, 0.2f));
                }
                // Lumps of foam slopping over and landing on the desk round the board.
                if (Time.time >= nextBlob && Mess.Instance)
                {
                    nextBlob = Time.time + Random.Range(0.1f, 0.22f);
                    Vector3 d = Outward();
                    Vector3 at = transform.position + d * (body.extents.x + Random.Range(0.02f, 0.25f)) + Vector3.back * Random.Range(0f, 0.15f);
                    float size = Random.Range(0.05f, 0.1f);
                    Mess.Instance.Blob(null, new Vector3(at.x, DeskScene.SurfaceHeight(at.x, at.z) + size * 0.25f, at.z), new Vector3(size, size * 0.45f, size), Random.value < 0.6f ? Foam : FoamPale, 120f, 0.25f);
                }
                if (Time.time >= nextSound)
                {
                    nextSound = Time.time + Random.Range(0.35f, 0.6f);
                    Sfx.Play(Random.value < 0.5f ? "slosh" : "squelch", 0.6f, 0.2f);
                }
                puffs?.Emit(Pos(top), new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.6f, 1f), Random.Range(-0.1f, 0.05f)), Steam, Random.Range(0.12f, 0.2f), Random.Range(1.2f, 2f), 3.2f, 0.8f);
                yield return null;
            }
            // The flood: a spreading foamy puddle round the base, and rivers still oozing down.
            if (Mess.Instance)
                for (int i = 0; i < 5; i++)
                {
                    Vector3 at = transform.position + new Vector3(Random.Range(-0.45f, 0.45f), 0f, body.min.z - Random.Range(0.02f, 0.3f));
                    Mess.Instance.DeskStain(at, Random.Range(0.08f, 0.16f), Puddle, 180f, 1.2f, true, new Vector2(Random.Range(-0.4f, 0.4f), -0.6f));
                }
            for (int i = 0; i < 4; i++)
            {
                RunDown(Random.Range(0.75f, 0.9f), Random.Range(-0.6f, 0.6f), Foam, 0.03f, 0.08f);
                yield return new WaitForSeconds(0.4f);
            }
            pressure = 0f;
            erupting = false;
        }
    }
}
