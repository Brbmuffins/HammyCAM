using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The washing machine: socks and shirts tumble round behind its porthole. Talking (and shaking it) speeds the
    /// drum up until it's on a full spin cycle, rattling and shuddering; keep it going and the suds build up until
    /// foam pours out of the door, piles up on the desk in bubbly heaps and flings socks out. !shake overloads it at
    /// once; the face slam bursts the door: a suds avalanche and the whole wash on the desk.
    /// </summary>
    public sealed class WashingMachine : GagAvatar
    {
        [Tooltip("The middle of the porthole's window and its inside radius (rest, avatar space).")]
        public Vector3 porthole;
        public float portholeRadius = 0.16f;
        [Tooltip("How far behind the glass the clothes tumble.")]
        public float drumDepth = 0.12f;

        static readonly Color[] Clothes =
        {
            new Color(0.9f, 0.2f, 0.25f), new Color(0.2f, 0.45f, 0.9f), new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.75f, 0.4f),
            new Color(0.95f, 0.95f, 0.95f), new Color(0.65f, 0.35f, 0.85f),
        };
        static readonly Color Foam = new Color(0.97f, 0.98f, 1f), Soapy = new Color(0.85f, 0.92f, 1f, 0.6f);

        Anchor door;
        readonly List<(Transform t, float angle, float radius, float spin)> wash = new List<(Transform, float, float, float)>();
        float drum = 0.4f, angle, suds, nextFoam, nextShudder;
        bool overflowing;

        public float Suds => suds;
        public override string MessName => "a suds avalanche";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "overflow", Label = "Suds build up and pour out of the door" });
            into.Add(new AvatarFeature { Key = "socks", Label = "It flings socks out" });
        }

        public override void TestMoment() => Run(Overflow(1f));
        public override string TestReport => $"suds {suds:0.00}, drum {drum:0.00}";

        protected override void Begin()
        {
            if (porthole == Vector3.zero) porthole = new Vector3(0f, body.size.y * 0.32f, body.min.z + 0.05f);
            door = Pin(porthole);
            // The wash: a few lumpy socks and shirts behind the glass.
            for (int i = 0; i < 7; i++)
            {
                Transform piece = Lump(Clothes[i % Clothes.Length], new Vector3(Random.Range(0.08f, 0.13f), Random.Range(0.04f, 0.06f), Random.Range(0.06f, 0.1f)));
                piece.SetParent(door.Bone, false);
                wash.Add((piece, i / 7f * 360f, portholeRadius * Random.Range(0.25f, 0.75f), Random.Range(-200f, 200f)));
            }
        }

        protected override void Tick(float dt)
        {
            // Talking and shaking speed the drum up; it winds down by itself.
            float target = 0.35f + Mathf.Clamp01(mouth * 1.6f) * 0.9f + Mathf.Clamp01((meter.Shake - 8f) / 40f) * 0.6f;
            drum = Mathf.MoveTowards(drum, Mathf.Clamp(target, 0.3f, 1.5f), dt * (target > drum ? 0.5f : 0.25f));
            angle += drum * 300f * dt;
            for (int i = 0; i < wash.Count; i++)
            {
                var (t, a, r, spin) = wash[i];
                if (!t) continue;
                // They tumble: carried up the drum and dropped, round and round.
                float k = (angle + a) * Mathf.Deg2Rad;
                float fall = Mathf.Max(0f, Mathf.Sin(k)) * drum * 0.3f;
                t.localPosition = door.Local + new Vector3(Mathf.Cos(k) * r, Mathf.Sin(k) * r - fall * r, drumDepth);
                t.localRotation = Quaternion.Euler(0f, 0f, (angle + a) * spin / 200f);
            }
            if (drum > 1.05f && Time.time >= nextShudder)
            {
                // Spin cycle: it rattles and walks about.
                nextShudder = Time.time + Random.Range(0.08f, 0.16f);
                rig.Bonk(0.04f * drum);
                rig.KickTip(new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.2f, 0.2f)) * drum);
            }
            if (On("overflow") && !overflowing)
            {
                suds = Mathf.Clamp01(suds + Mathf.Max(0f, drum - 0.9f) * 0.12f * dt - 0.01f * dt);
                if (suds >= 1f) Run(Overflow(0.8f));
            }
        }

        /// <summary>The door can't hold it: foam bulges out, pours down and heaps up on the desk; socks come with it.</summary>
        IEnumerator Overflow(float strength)
        {
            if (overflowing) yield break;
            overflowing = true;
            Sfx.Play("bubbles", 0.9f, 0.1f);
            Sfx.Play("slosh", 0.7f, 0.1f);
            float until = Time.time + 3.5f * strength;
            int socks = On("socks") ? Scaled(Mathf.RoundToInt(4 * strength)) : 0;
            while (Time.time < until)
            {
                if (Time.time >= nextFoam)
                {
                    nextFoam = Time.time + 0.06f;
                    FoamBlob(Pos(door) + Random.insideUnitSphere * portholeRadius * 0.6f, strength);
                }
                if (socks > 0 && Random.value < 2f * Time.deltaTime)
                {
                    socks--;
                    Sock();
                }
                yield return null;
            }
            for (; socks > 0; socks--) Sock();
            suds = 0f;
            overflowing = false;
        }

        /// <summary>A heap of foam: bubbly blobs (the soap-bubble shader), and a soapy patch on the desk under it.</summary>
        void FoamBlob(Vector3 at, float strength)
        {
            if (!Keep()) return;
            Material bubble = Chaos ? Chaos.bubble : null;
            float size = Random.Range(0.05f, 0.12f) * Mathf.Sqrt(strength);
            Vector3 land = at + new Vector3(Random.Range(-0.25f, 0.25f), 0f, -Random.Range(0.05f, 0.3f));
            land.y = Mathf.Max(0f, DeskScene.SurfaceHeight(land.x, land.z)) + size * 0.3f;
            if (Mess.Instance)
            {
                Transform heap = Mess.Instance.Blob(null, land, new Vector3(size, size * 0.6f, size), Foam, 90f, 0.4f);
                if (heap && bubble && Random.value < 0.4f) heap.GetComponent<MeshRenderer>().sharedMaterial = bubble;
                if (Random.value < 0.15f) Mess.Instance.DeskStain(land, size * 1.4f, Soapy, 80f, 0.6f, true);
            }
            if (Random.value < 0.3f) puffs?.Emit(at, Vector3.up * 0.2f, new Color(1f, 1f, 1f, 0.3f), size, 1.2f, 2f, 1f);
        }

        void Sock()
        {
            Transform sock = Lump(Clothes[Random.Range(0, Clothes.Length)], new Vector3(0.1f, 0.035f, 0.05f));
            sock.position = Pos(door);
            ToDesk(sock, new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(0.6f, 1.4f), -Random.Range(0.6f, 1.2f)), 0.05f, 0.017f);
            Sfx.Play("flop", 0.35f, 0.25f);
        }

        public override void Burst()
        {
            drum = 1.4f;
            Run(Overflow(1f));
        }

        public override void OnSlam()
        {
            Sfx.Play("pan_bonk", 0.5f, 0.1f);
            Run(Overflow(1.6f));
        }
    }
}
