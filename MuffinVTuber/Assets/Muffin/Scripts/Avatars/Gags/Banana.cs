using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The banana: the top of its peel is four strips, built here and hinged on the torn edge of the model's peel.
    /// Shut, they wrap the fruit up to the stalk, so it looks like a whole banana; open your mouth and they peel down
    /// and droop like a peeled banana (springy, a stalk on one strip, wobbling as it moves); close it and they fold
    /// back up. !shake drops a banana skin on the desk; the face slam squishes it into mush, peel thrown wide open.
    /// </summary>
    public sealed class Banana : GagAvatar
    {
        [Tooltip("The middle of the peel's torn top edge and the bare fruit's tip (rest, avatar space; the fruit leans), the peel's outer radius at the edge and the fruit's radius there.")]
        public Vector3 hinge = new Vector3(-0.032f, 0.741f, 0.009f), fruitTip = new Vector3(0.025f, 1.15f, 0.009f);
        public float hingeRadius = 0.144f, fruitRadius = 0.114f;
        [Tooltip("The fruit's radius (times fruitRadius) at 0, 25, 50, 75, 90 and 100% of the way up it: the shut strips hug it.")]
        public float[] fruitProfile = { 1f, 0.947f, 0.86f, 0.70f, 0.465f, 0f };
        [Tooltip("The peel strips' thickness (metres).")]
        public float peelThickness = 0.014f;
        public Color peel = new Color(0.98f, 0.831f, 0.165f), inner = new Color(0.98f, 0.957f, 0.831f);

        static readonly Color Mush = new Color(0.97f, 0.9f, 0.62f), Brown = new Color(0.45f, 0.3f, 0.12f), Stalk = new Color(0.36f, 0.42f, 0.14f);
        static readonly float[] ProfileAt = { 0f, 0.25f, 0.5f, 0.75f, 0.9f, 1f };
        const int Rows = 16, Cols = 8, Strips = 4;
        const float HalfAngle = 43.5f * Mathf.Deg2Rad; // a little short of a quarter: the splits between strips show
        [Tooltip("How far the front strip swings round to the side as it peels (degrees), so it doesn't hang over the face.")]
        public float frontSwing = 70f;

        sealed class Strip
        {
            public Transform T;
            public Mesh Mesh;
            public float Open, Velocity, Delay, Around;
            // How far round the banana it swings as it opens (degrees), and how far down its end hangs.
            public float Swing, HangAngle = 168f;
            public Vector3[] Points;
            // Shut, the strip's middle line (its hinge's radial plane: x up, y out), its radius from the fruit's
            // axis, and how far the leaning axis has moved sideways across the strip; each step's length.
            public Vector2[] Shut;
            public float[] ShutRadius, ShutSideways, StepLength;
        }

        readonly List<Strip> strips = new List<Strip>();
        Anchor top;
        Transform stalk;
        float open, nextPeelDrop, holdOpenUntil;

        public override string MessName => "banana mush and a banana skin";

        protected override void DescribeMore(List<AvatarFeature> into)
        {
            into.Add(new AvatarFeature { Key = "peel", Label = "Opening your mouth peels it open" });
        }

        public override void TestMoment()
        {
            open = 1f;
            holdOpenUntil = Time.time + 1.5f;
        }

        public override string TestReport => $"peel {(strips.Count > 0 ? strips[0].Open : 0f):0.00} open";

        protected override void Begin()
        {
            top = Pin(hinge);
            Material outside = Matte(peel, 0.25f), inside = Matte(inner, 0.15f);
            for (int i = 0; i < Strips; i++)
            {
                // Front, sides and back, each wrapping a quarter of the fruit (the torn edge splits at the diagonals).
                float around = -90f + i * 90f;
                var t = new GameObject($"Peel strip {i}").transform;
                t.SetParent(top.Bone, false);
                float a = around * Mathf.Deg2Rad;
                t.localPosition = top.Local + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * hingeRadius;
                t.localRotation = Quaternion.Euler(0f, -around + 90f, 0f); // local +Z points out from the middle
                var mesh = new Mesh { name = "Peel strip" };
                mesh.MarkDynamic();
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = t.gameObject.AddComponent<MeshRenderer>();
                if (outside && inside) mr.sharedMaterials = new[] { outside, inside };
                var strip = new Strip { T = t, Mesh = mesh, Delay = Random.Range(0f, 0.12f), Around = around, Points = new Vector3[(Rows + 1) * (Cols + 1)] };
                if (i == 0)
                {
                    strip.Swing = frontSwing;
                    strip.HangAngle = 150f;
                }
                BuildShutLine(strip);
                strips.Add(strip);
                Shape(strip);
            }
            // The stalk rides the tip of the back strip (it stays on that one when the banana's peeled).
            stalk = Item(PrimitiveType.Cylinder, Stalk, Vector3.one);
            stalk.localScale = new Vector3(0.05f, 0.035f, 0.05f);
            Transform cap = Item(PrimitiveType.Sphere, Brown, Vector3.one);
            cap.SetParent(stalk, false);
            cap.localPosition = new Vector3(0f, 1f, 0f);
            cap.localScale = new Vector3(1.15f, 0.5f, 1.15f);
            stalk.SetParent(strips[2].T, false);
        }

        Material Matte(Color c, float smoothness)
        {
            Material m = Tint(c);
            if (!m) return null;
            m = new Material(m);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        float FruitRadiusAt(float s)
        {
            s = Mathf.Clamp01(s);
            for (int k = 1; k < ProfileAt.Length; k++)
                if (s <= ProfileAt[k])
                {
                    float f = Mathf.InverseLerp(ProfileAt[k - 1], ProfileAt[k], s);
                    return fruitRadius * Mathf.Lerp(fruitProfile[k - 1], fruitProfile[k], Mathf.SmoothStep(0f, 1f, f) * 0.5f + f * 0.5f);
                }
            return 0f;
        }

        /// <summary>
        /// The shut strip hugs the fruit: from the peel's edge up over the rounded tip to the middle, following the
        /// fruit's axis as it leans (so the strip on the side it leans toward slopes out, the other in).
        /// </summary>
        void BuildShutLine(Strip strip)
        {
            strip.Shut = new Vector2[Rows + 1];
            strip.ShutRadius = new float[Rows + 1];
            strip.ShutSideways = new float[Rows + 1];
            strip.StepLength = new float[Rows];
            Vector3 axis = fruitTip - hinge;
            float rise = Mathf.Max(axis.y, 0.05f);
            Vector3 lean = new Vector3(axis.x, 0f, axis.z); // sideways over the whole length
            // This strip's out and across directions (in the hinge's space).
            Quaternion turn = Quaternion.Euler(0f, -strip.Around + 90f, 0f);
            Vector3 outward = turn * Vector3.forward, across = turn * Vector3.right;
            for (int j = 0; j <= Rows; j++)
            {
                float s = j / (float)Rows;
                // More rows near the tip, where it curls over.
                float u = 1f - (1f - s) * (1f - s) * 0.35f - (1f - s) * 0.65f;
                float r = Mathf.Lerp(hingeRadius, FruitRadiusAt(u) + peelThickness + 0.003f, Mathf.SmoothStep(0f, 1f, u / 0.25f));
                Vector3 shift = lean * u;
                strip.Shut[j] = new Vector2(u * rise, r - hingeRadius + Vector3.Dot(shift, outward));
                strip.ShutRadius[j] = r;
                strip.ShutSideways[j] = Vector3.Dot(shift, across);
            }
            for (int j = 0; j < Rows; j++) strip.StepLength[j] = (strip.Shut[j + 1] - strip.Shut[j]).magnitude;
        }

        /// <summary>
        /// One strip's shape for how open it is: its middle line turns from hugging the fruit to arching out and
        /// hanging down (each step's angle eased between the two, so it peels over rather than cutting through the
        /// fruit), and its cross-section goes from wrapping round the banana to a gentle open cup.
        /// </summary>
        void Shape(Strip strip)
        {
            float o = strip.Open;
            Vector3[] p = strip.Points;
            Vector2[] shut = strip.Shut;
            float[] stepLength = strip.StepLength;
            Vector2 c = Vector2.zero, cShut = Vector2.zero;
            float rhoOpen = hingeRadius * 1.6f;
            for (int j = 0; j <= Rows; j++)
            {
                float s = j / (float)Rows;
                if (j > 0)
                {
                    Vector2 d = shut[j] - shut[j - 1];
                    float shutAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; // from up toward out
                    // Open: out and a little up off the edge, curling over to hang down along the banana.
                    float openAngle = Mathf.Lerp(62f, strip.HangAngle, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 0.05f) / 0.55f)));
                    float angle = Mathf.LerpUnclamped(shutAngle, openAngle, o) * Mathf.Deg2Rad;
                    c += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * stepLength[j - 1];
                    cShut = shut[j];
                }
                // The strip's direction here (for the open cross-section's frame).
                Vector2 dir;
                if (j < Rows)
                {
                    Vector2 d = shut[j + 1] - shut[j];
                    float shutAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    float openAngle = Mathf.Lerp(62f, strip.HangAngle, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s + 1f / Rows - 0.05f) / 0.55f)));
                    float angle = Mathf.LerpUnclamped(shutAngle, openAngle, o) * Mathf.Deg2Rad;
                    dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                else dir = new Vector2(-1f, 0.2f).normalized;
                Vector3 normal = new Vector3(0f, -dir.y, dir.x); // out of the strip's outside (y, z plane)
                float rShut = strip.ShutRadius[j], sideways = strip.ShutSideways[j], outShift = cShut.y - (rShut - hingeRadius);
                float width = Mathf.Max(rShut, 0f) * HalfAngle; // half the strip's width (arc length)
                // Real strips taper to the stalk; keep a little width at the very end.
                width = Mathf.Max(width, hingeRadius * HalfAngle * 0.12f * (1f - s * 0.5f));
                float rho = Mathf.Lerp(Mathf.Max(rShut, 0.01f), rhoOpen, Mathf.Clamp01(o));
                for (int i = 0; i <= Cols; i++)
                {
                    float f = i / (float)Cols * 2f - 1f;
                    // Shut: round the banana's axis (hingeRadius behind the hinge).
                    float phi = f * width / Mathf.Max(rShut, 0.01f);
                    phi = Mathf.Clamp(phi, -HalfAngle, HalfAngle);
                    // (Round the fruit's leaning axis: moved out by outShift and across by sideways here.)
                    Vector3 shutPoint = new Vector3(Mathf.Sin(phi) * rShut + sideways, cShut.x, Mathf.Cos(phi) * rShut - hingeRadius + outShift);
                    Vector3 shutMiddle = new Vector3(0f, cShut.x, cShut.y);
                    // Open: an arc of the same width across the strip, cupped inward.
                    float psi = f * width / rho;
                    Vector3 openOffset = Vector3.right * Mathf.Sin(psi) * rho - normal * rho * (1f - Mathf.Cos(psi));
                    Vector3 offset = Vector3.LerpUnclamped(shutPoint - shutMiddle, openOffset, Mathf.Clamp01(o));
                    p[j * (Cols + 1) + i] = new Vector3(0f, c.x, c.y) + offset;
                }
            }
            BuildMesh(strip);
        }

        /// <summary>The strip as a thick sheet: yellow outside and edges, cream inside.</summary>
        void BuildMesh(Strip strip)
        {
            Vector3[] p = strip.Points;
            int n = (Rows + 1) * (Cols + 1);
            // The outside's normals, to push the inside in by the peel's thickness.
            var outN = new Vector3[n];
            for (int j = 0; j <= Rows; j++)
                for (int i = 0; i <= Cols; i++)
                {
                    Vector3 di = p[j * (Cols + 1) + Mathf.Min(i + 1, Cols)] - p[j * (Cols + 1) + Mathf.Max(i - 1, 0)];
                    Vector3 dj = p[Mathf.Min(j + 1, Rows) * (Cols + 1) + i] - p[Mathf.Max(j - 1, 0) * (Cols + 1) + i];
                    Vector3 nn = Vector3.Cross(di, dj);
                    outN[j * (Cols + 1) + i] = nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.forward;
                }
            var v = new List<Vector3>(n * 2 + (Rows + 1) * 4);
            var outside = new List<int>();
            var inside = new List<int>();
            for (int k = 0; k < n; k++) v.Add(p[k]);
            for (int k = 0; k < n; k++)
            {
                // Thinner toward the tip.
                float s = (k / (Cols + 1)) / (float)Rows;
                v.Add(p[k] - outN[k] * peelThickness * (1f - 0.5f * s));
            }
            for (int j = 0; j < Rows; j++)
                for (int i = 0; i < Cols; i++)
                {
                    int a = j * (Cols + 1) + i, b = a + 1, c = a + Cols + 2, d = a + Cols + 1;
                    outside.AddRange(new[] { a, b, c, a, c, d });
                    inside.AddRange(new[] { n + a, n + c, n + b, n + a, n + d, n + c });
                }
            // The two long edges (their own points, so they shade crisply).
            foreach (int i in new[] { 0, Cols })
            {
                int start = v.Count;
                for (int j = 0; j <= Rows; j++)
                {
                    v.Add(v[j * (Cols + 1) + i]);
                    v.Add(v[n + j * (Cols + 1) + i]);
                }
                for (int j = 0; j < Rows; j++)
                {
                    int o0 = start + j * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3;
                    if (i == 0) outside.AddRange(new[] { o0, o1, i1, o0, i1, i0 });
                    else outside.AddRange(new[] { o0, i1, o1, o0, i0, i1 });
                }
            }
            Mesh mesh = strip.Mesh;
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(outside, 0);
            mesh.SetTriangles(inside, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        protected override void Tick(float dt)
        {
            float target = (On("peel") && mouth > MouthThreshold) || Time.time < holdOpenUntil ? 1f : 0f;
            if (Time.time > nextPeelDrop - 9f) open = Mathf.MoveTowards(open, target, dt * 3f);
            float step = Mathf.Min(dt, 1f / 30f);
            foreach (Strip f in strips)
            {
                // A floppy spring toward open or shut, kicked about by the banana's movement.
                float goal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(open * 1.15f - f.Delay));
                f.Velocity += ((goal - f.Open) * 90f - f.Velocity * 8f) * step;
                f.Velocity += Mathf.Sin(Time.time * 9f + f.Around) * meter.Speed * 0.3f * step * Mathf.Clamp01(f.Open * 3f);
                float before = f.Open;
                f.Open = Mathf.Clamp(f.Open + f.Velocity * step, -0.03f, 1.12f);
                if (Mathf.Abs(f.Open - before) > 1e-4f || f.Open > 0.02f) Shape(f);
                if (f.Swing != 0f)
                {
                    // Round the torn edge to the side as it opens, sitting just outside the strip it swings onto.
                    float opened = Mathf.Clamp01(f.Open), around = f.Around + f.Swing * opened, a = around * Mathf.Deg2Rad;
                    f.T.localPosition = top.Local + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (hingeRadius + 0.005f * opened);
                    f.T.localRotation = Quaternion.Euler(0f, -around + 90f, 0f);
                }
            }
            if (stalk && strips.Count > 2)
            {
                // On the back strip's tip, pointing on along it.
                Vector3[] p = strips[2].Points;
                Vector3 tip = (p[Rows * (Cols + 1) + Cols / 2] + p[Rows * (Cols + 1) + Cols / 2 - 1] + p[Rows * (Cols + 1) + Cols / 2 + 1]) / 3f;
                Vector3 before = p[(Rows - 1) * (Cols + 1) + Cols / 2];
                Vector3 along = (tip - before).sqrMagnitude > 1e-8f ? (tip - before).normalized : Vector3.up;
                // Shut, the strips meet at the top: the stalk stands up out of the middle.
                along = Vector3.Slerp(Vector3.up, along, Mathf.Clamp01(strips[2].Open * 1.5f));
                stalk.localPosition = tip + along * 0.03f;
                stalk.localRotation = Quaternion.FromToRotation(Vector3.up, along);
            }
        }

        public override void Burst()
        {
            // Throws the peel open and drops a banana skin on the desk: a slip waiting to happen.
            open = 1f;
            nextPeelDrop = Time.time + 10f;
            Sfx.Play("flop", 0.6f, 0.15f);
            Transform skin = Item(PrimitiveType.Sphere, peel, Vector3.one * 0.14f);
            skin.GetComponent<MeshFilter>().sharedMesh = JunkMeshes.BananaPeel;
            Transform stem = Item(PrimitiveType.Cylinder, Brown, Vector3.one);
            stem.SetParent(skin, false);
            stem.localPosition = new Vector3(0f, 0.41f, 0f);
            stem.localScale = new Vector3(0.07f, 0.05f, 0.07f);
            skin.position = Pos(top) + Vector3.up * 0.1f;
            ToDesk(skin, new Vector3(Random.Range(-0.8f, 0.8f), 1.2f, -0.9f), 0.06f, 0.002f, upright: true);
            Splodge(Scaled(4));
        }

        public override void OnSlam()
        {
            open = 1f;
            nextPeelDrop = Time.time + 10f;
            Sfx.Play("squelch", 1f, 0.1f);
            Sfx.Play("pie_splat", 0.6f, 0.1f);
            Splodge(Scaled(10));
            if (Mess.Instance) Mess.Instance.DeskStain(InFront(0f, 0.1f), 0.12f, new Color(Mush.r, Mush.g, Mush.b, 0.9f), 150f, 0.2f, false, new Vector2(0f, -1f));
        }

        /// <summary>Squashed banana: soft pale lumps flung onto the desk.</summary>
        void Splodge(int lumps)
        {
            for (int i = 0; i < lumps; i++)
            {
                Transform lump = Lump(Mush, Vector3.one * Random.Range(0.035f, 0.06f));
                lump.position = Pos(top) + Vector3.up * Random.Range(0.05f, 0.3f);
                ToDesk(lump, Outward() * Random.Range(0.4f, 1f) + Vector3.up * Random.Range(0.5f, 1.2f), 0.025f, 0.018f);
            }
        }
    }
}
