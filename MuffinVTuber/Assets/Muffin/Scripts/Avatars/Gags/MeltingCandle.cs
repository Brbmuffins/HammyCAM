using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The melting candle: a real flickering flame on its wick that lights the desk and leans as it moves. Pucker up
    /// and blow (your real lips: an "ooh" held for a moment) and the flame goes out in a curl of smoke; a few seconds
    /// later it relights itself with a fwump. Open wide and it flares up tall. While it burns, wax drips run down
    /// its sides and pool round its foot, building up over the stream. !shake flares it and spatters hot wax; the
    /// face slam snuffs it out in a splat of wax.
    /// </summary>
    public sealed class MeltingCandle : GagAvatar
    {
        [Tooltip("The wick's tip and the middle of the melted pool on top (rest, avatar space); zero = the model's top.")]
        public Vector3 wickTip, pool;
        [Tooltip("The top's radius (metres): where the wax drips over.")]
        public float rimRadius = 0.2f;

        static readonly Color Wax = new Color(0.98f, 0.94f, 0.84f, 0.96f), WaxPuddle = new Color(0.96f, 0.92f, 0.8f, 0.92f),
            Smoke = new Color(0.45f, 0.44f, 0.46f, 0.45f);

        Anchor wick, top;
        readonly List<(Transform t, MeshRenderer r, float w, float h, float seed)> tongues = new List<(Transform, MeshRenderer, float, float, float)>();
        MaterialPropertyBlock block;
        Light glow;
        float size = 1f, target = 1f, flareUntil, relightAt, nextDrip, nextPuddle, puddleRadius = 0.05f;
        Vector3 lean;
        bool lit = true;

        public bool Lit => lit;
        public override void TestMoment() => BlowOut();
        public override string TestReport => lit ? "lit" : "blown out";
        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "light", Label = "The flame lights up the desk" });
            into.Add(new AvatarFeature { Key = "wax", Label = "Wax drips down and pools at its foot" });
        }

        public override string MessName => "hot wax";

        protected override void Begin()
        {
            if (wickTip == Vector3.zero) wickTip = new Vector3(0f, body.max.y, 0f);
            if (pool == Vector3.zero) pool = wickTip + Vector3.down * 0.06f;
            wick = Pin(wickTip);
            top = Pin(pool);
            block = new MaterialPropertyBlock();
            Material flame = Chaos ? Chaos.flame : null;
            if (flame)
            {
                void Tongue(float w, float h)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(go.GetComponent<Collider>());
                    go.name = "Candle flame";
                    var r = go.GetComponent<MeshRenderer>();
                    r.sharedMaterial = flame;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.transform.SetParent(transform, false);
                    tongues.Add((go.transform, r, w, h, Random.Range(0f, 50f)));
                }
                Tongue(0.14f, 0.32f);
                Tongue(0.1f, 0.23f);
                Tongue(0.08f, 0.18f);
            }
            var light = new GameObject("Candle light");
            light.transform.SetParent(transform, false);
            glow = light.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.68f, 0.32f);
            glow.range = 1.8f;
            nextDrip = Time.time + Random.Range(4f, 9f);
            nextPuddle = Time.time + 6f;
        }

        protected override void Tick(float dt)
        {
            // Blow it out: a pucker held for a moment.
            if (lit && puckerFor > 0.22f) BlowOut();
            if (lit && mouthJustOpened && mouth > 0.6f) Flare(0.9f);
            if (!lit && Time.time >= relightAt) Relight();

            target = !lit ? 0f : Time.time < flareUntil ? 2.1f : 1f;
            size = Mathf.MoveTowards(size, target, dt * (target > size ? 3f : 6f));
            // The flame trails behind the way the candle moves.
            Vector3 v = meter.Velocity;
            lean = Vector3.Lerp(lean, Vector3.ClampMagnitude(-new Vector3(v.x, 0f, v.z) * 0.12f, 0.12f), 1f - Mathf.Exp(-dt * 6f));
            Vector3 at = Pos(wick);
            float t = Time.time;
            foreach (var (tr, r, w, h, seed) in tongues)
            {
                float flicker = 0.85f + 0.3f * Mathf.PerlinNoise(t * 5f + seed, seed);
                float s = size * flicker;
                tr.gameObject.SetActive(s > 0.02f);
                tr.localScale = new Vector3(w * s, h * s, 1f);
                tr.position = at + Vector3.up * h * s * 0.45f + lean * s;
                FaceCamera(tr, true);
                tr.rotation *= Quaternion.Euler(0f, 0f, Mathf.Clamp(-lean.x * 300f, -35f, 35f) + (Mathf.PerlinNoise(seed, t * 3f) - 0.5f) * 8f);
                block.Clear();
                block.SetFloat("_Seed", seed);
                block.SetFloat("_Intensity", Mathf.Clamp01(size * 1.1f));
                r.SetPropertyBlock(block);
            }
            if (glow)
            {
                glow.transform.position = at + Vector3.up * 0.06f + Vector3.back * 0.05f;
                glow.intensity = !On("light") ? 0f : 1.6f * size * (0.75f + 0.5f * Mathf.PerlinNoise(t * 12f, 0.2f));
            }

            if (!lit || !On("wax")) return;
            // Wax: drips run down the side now and then (more when it flares), and the puddle at its foot grows.
            if (Time.time >= nextDrip)
            {
                nextDrip = Time.time + Random.Range(7f, 15f) / (Time.time < flareUntil ? 3f : 1f);
                RunDown(Random.Range(0.86f, 0.92f), Random.Range(-0.6f, 0.6f), Wax, Random.Range(0.022f, 0.03f), Random.Range(0.04f, 0.07f));
            }
            if (Time.time >= nextPuddle && Mess.Instance)
            {
                nextPuddle = Time.time + Random.Range(18f, 30f);
                puddleRadius = Mathf.Min(0.16f, puddleRadius + 0.012f);
                Mess.Instance.DeskStain(InFront(Random.Range(-0.12f, 0.12f), Random.Range(0.01f, 0.06f)), puddleRadius, WaxPuddle, 900f, 2f, false, default, false);
            }
        }

        void BlowOut()
        {
            lit = false;
            relightAt = Time.time + Random.Range(2.8f, 4f);
            Sfx.Play("spore_puff", 0.5f, 0.15f);
            Run(SmokeCurl());
        }

        void Relight()
        {
            lit = true;
            Sfx.Play("fire_ignite", 0.55f, 0.1f);
            size = 0.2f;
            Flare(0.35f);
        }

        void Flare(float seconds)
        {
            flareUntil = Time.time + seconds;
            Sfx.Play("fire_ignite", 0.4f, 0.15f);
            Spatter(6);
        }

        /// <summary>The thin ribbon of smoke off a snuffed wick, curling up and away.</summary>
        IEnumerator SmokeCurl()
        {
            for (int i = 0; i < 16 && puffs != null; i++)
            {
                Vector3 at = Pos(wick);
                float sway = Mathf.Sin(i * 0.8f) * 0.04f;
                puffs.Emit(at, new Vector3(sway, Random.Range(0.18f, 0.26f), 0f), Smoke, Random.Range(0.035f, 0.05f), Random.Range(1.8f, 2.6f), 3.5f, 0.3f, 0.02f);
                yield return new WaitForSeconds(0.07f);
            }
        }

        /// <summary>Hot wax flicked off the pool: it lands on the desk in little hard drops.</summary>
        void Spatter(int count)
        {
            Vector3 at = Pos(top);
            for (int i = 0; i < count; i++)
                Drop(at + Random.insideUnitSphere * rimRadius * 0.5f, Outward() * Random.Range(0.3f, 0.9f) + Vector3.up * Random.Range(0.6f, 1.3f) + meter.Velocity * 0.5f,
                    Wax, Random.Range(0.01f, 0.016f), Random.Range(0.018f, 0.03f));
        }

        public override void Burst()
        {
            if (!lit) Relight();
            Flare(1.6f);
            Spatter(22);
            for (int i = 0; i < 3; i++) RunDown(Random.Range(0.86f, 0.92f), (i - 1) * 0.5f, Wax, 0.028f, 0.08f);
        }

        public override void OnSlam()
        {
            if (lit) BlowOut();
            Spatter(18);
            Sfx.Play("splat", 0.6f, 0.1f);
            if (Mess.Instance) Mess.Instance.DeskStain(InFront(0f, 0.12f), 0.12f, WaxPuddle, 900f, 0.3f, false, default, false);
        }
    }
}
