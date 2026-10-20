using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The toxic waste barrel: it glows. Bubbles of luminous green ooze swell up out of the bung and pop, spitting
    /// goo; ooze dribbles down its sides into glowing puddles on the desk; the whole set is lit green, pulsing, and
    /// the more you talk the harder it bubbles and the brighter it glows. !shake slops ooze over the lid; the face
    /// slam splats a huge glowing pool across the desk with a flash.
    /// </summary>
    public sealed class ToxicBarrel : GagAvatar
    {
        [Tooltip("The bung on the lid, and the lid's middle (rest, avatar space); zero = the model's top.")]
        public Vector3 bung, lid;
        [Tooltip("The lid's radius (metres).")]
        public float lidRadius = 0.25f;

        static readonly Color Ooze = new Color(0.45f, 1f, 0.22f, 0.95f), OozePuddle = new Color(0.4f, 1f, 0.2f, 0.85f), Fume = new Color(0.55f, 0.95f, 0.35f, 0.3f);

        Anchor bungAnchor, lidAnchor;
        Light glow, deskGlow;
        Material glowing;
        readonly List<(Transform t, float born, float life, float size)> bubbles = new List<(Transform, float, float, float)>();
        float bubbleOwed, nextDrip, nextGurgle, flash;

        public override void TestMoment() => Run(SlopOver(1f));
        public override string TestReport => $"{bubbles.Count} bubbles";
        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "light", Label = "It lights the set green" });
            into.Add(new AvatarFeature { Key = "drips", Label = "Ooze dribbles down its sides by itself" });
        }

        public override string MessName => "glowing toxic ooze";

        protected override void Begin()
        {
            if (lid == Vector3.zero) lid = new Vector3(0f, body.max.y - 0.01f, 0f);
            if (bung == Vector3.zero) bung = lid + new Vector3(body.extents.x * 0.4f, 0f, 0f);
            bungAnchor = Pin(bung);
            lidAnchor = Pin(lid);
            // Unlit, so the ooze in flight and the bubbles shine in any light (URP Unlit ships with the face stickers).
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit)
            {
                glowing = new Material(unlit) { name = "Glowing ooze" };
                glowing.SetColor("_BaseColor", new Color(0.55f, 1f, 0.3f));
            }
            glow = NewLight("Toxic glow", new Color(0.45f, 1f, 0.3f), 1.8f);
            deskGlow = NewLight("Toxic puddle glow", new Color(0.4f, 1f, 0.25f), 1.1f);
            nextDrip = Time.time + Random.Range(3f, 6f);
        }

        Light NewLight(string label, Color colour, float range)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = colour;
            l.range = range;
            return l;
        }

        protected override void Tick(float dt)
        {
            float talk = Mathf.Clamp01(mouth * 1.6f);
            float t = Time.time;
            flash = Mathf.MoveTowards(flash, 0f, dt * 1.5f);
            if (glow)
            {
                glow.transform.position = Pos(lidAnchor) + Vector3.up * 0.12f + Vector3.back * 0.1f;
                glow.intensity = !On("light") ? 0f : (0.9f + 0.35f * Mathf.Sin(t * 2.4f) + 1.6f * talk + 4f * flash) * (0.9f + 0.2f * Mathf.PerlinNoise(t * 6f, 0.1f));
            }
            if (deskGlow)
            {
                deskGlow.transform.position = transform.position + new Vector3(0f, 0.08f, body.min.z - 0.2f);
                deskGlow.intensity = !On("light") ? 0f : 0.7f + 0.25f * Mathf.Sin(t * 1.7f + 1f) + 2f * flash;
            }

            // Bubbles swell out of the bung and pop.
            bubbleOwed += (0.7f + talk * 4f + meter.Speed * 0.8f) * dt;
            for (int g = 0; bubbleOwed >= 1f && g < 3; g++)
            {
                bubbleOwed -= 1f;
                NewBubble();
            }
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var (b, born, life, size) = bubbles[i];
                float k = (t - born) / life;
                if (!b)
                {
                    bubbles.RemoveAt(i);
                    continue;
                }
                if (k >= 1f)
                {
                    Pop(b.position, size);
                    Destroy(b.gameObject);
                    bubbles.RemoveAt(i);
                    continue;
                }
                float s = size * Mathf.SmoothStep(0.1f, 1f, k) * (1f + 0.06f * Mathf.Sin(t * 20f + i));
                b.localScale = new Vector3(s, s * 0.9f, s);
                b.position = Pos(bungAnchor) + Vector3.up * s * 0.4f;
            }
            if (talk > 0.3f && Time.time >= nextGurgle)
            {
                nextGurgle = Time.time + Random.Range(0.8f, 1.5f);
                Sfx.Play("bubbles", 0.35f + talk * 0.3f, 0.2f);
            }

            // Ooze dribbles down the sides now and then (and slops over when it moves hard).
            if (On("drips") && (Time.time >= nextDrip || meter.Shake > 40f && Time.time >= nextDrip - 3f))
            {
                nextDrip = Time.time + Random.Range(6f, 12f);
                RunDown(Random.Range(0.88f, 0.94f), Random.Range(-0.7f, 0.7f), Ooze, Random.Range(0.024f, 0.032f), Random.Range(0.05f, 0.08f));
                if (Random.value < 0.5f) Puddle(Random.Range(0.05f, 0.09f));
            }
        }

        void NewBubble()
        {
            if (bubbles.Count > 6) return;
            Transform b = Item(PrimitiveType.Sphere, Ooze, Vector3.zero);
            if (glowing) b.GetComponent<MeshRenderer>().sharedMaterial = glowing;
            b.SetParent(transform, true);
            bubbles.Add((b, Time.time, Random.Range(0.6f, 1.3f), Random.Range(0.06f, 0.12f)));
        }

        /// <summary>POP: goo spits out and a wisp of green fume drifts up.</summary>
        void Pop(Vector3 at, float size)
        {
            BitSound("pop", 0.25f + size * 3f);
            int n = Mathf.RoundToInt(size * 90f);
            for (int i = 0; i < n; i++)
                Drop(at, Outward() * Random.Range(0.2f, 0.7f) + Vector3.up * Random.Range(0.5f, 1.2f), Ooze, Random.Range(0.008f, 0.014f), Random.Range(0.02f, 0.035f));
            puffs?.Emit(at, Vector3.up * 0.2f, Fume, size * 2.5f, 1.6f, 3f, 0.8f);
        }

        void Puddle(float radius)
        {
            if (!Mess.Instance) return;
            Mess.Instance.DeskStain(InFront(Random.Range(-0.3f, 0.3f), Random.Range(0.02f, 0.2f)), radius, OozePuddle, 240f, 1.5f, true, new Vector2(Random.Range(-0.3f, 0.3f), -0.4f));
        }

        public override void Burst() => Run(SlopOver(1f));

        public override void OnSlam()
        {
            flash = 1f;
            Sfx.Play("slime_splat", 0.9f, 0.08f);
            Sfx.Play("splat", 0.8f, 0.1f);
            if (Mess.Instance)
            {
                Mess.Instance.DeskStain(InFront(0f, 0.12f), 0.22f, OozePuddle, 300f, 0.3f, true, new Vector2(0f, -1.2f));
                for (int i = 0; i < 4; i++) Puddle(Random.Range(0.06f, 0.11f));
            }
            Run(SlopOver(1.4f));
        }

        /// <summary>A wave of ooze heaves over the lid's rim, down the sides and onto the desk.</summary>
        IEnumerator SlopOver(float strength)
        {
            Sfx.Play("slop", 0.8f, 0.1f);
            Sfx.Play("bubbles", 0.7f, 0.1f);
            flash = Mathf.Max(flash, 0.5f);
            rig.Bonk(0.15f * strength);
            Vector3 dir = SpillDirection();
            for (int i = 0; i < Mathf.RoundToInt(36 * strength); i++)
            {
                Vector3 d = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * dir;
                Drop(Pos(lidAnchor) + d * lidRadius * 0.9f, d * Random.Range(0.3f, 0.9f) * strength + Vector3.up * Random.Range(0.3f, 1f), Ooze,
                    Random.Range(0.014f, 0.024f), Random.Range(0.04f, 0.06f));
                if (i % 6 == 0) yield return null;
            }
            for (int i = 0; i < 4; i++)
            {
                RunDown(Random.Range(0.88f, 0.94f), Random.Range(-0.7f, 0.7f), Ooze, 0.03f, 0.1f);
                NewBubble();
                yield return new WaitForSeconds(0.15f);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            foreach (var (b, _, _, _) in bubbles) if (b) Destroy(b.gameObject);
            bubbles.Clear();
        }
    }
}
