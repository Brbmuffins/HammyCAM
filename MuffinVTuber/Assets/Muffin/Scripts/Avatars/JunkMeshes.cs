using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Bits of garbage, shaped in code once (about 1 across; scale them to size): a crumpled paper ball, a banana
    /// peel with its flaps splayed and curling, a crushed soda can and a curved pizza crust.
    /// </summary>
    public static class JunkMeshes
    {
        static Mesh paper, peel, can, crust;

        public static Mesh CrumpledPaper => paper ? paper : paper = MakePaper(7);
        public static Mesh BananaPeel => peel ? peel : peel = MakePeel(3);
        public static Mesh CrushedCan => can ? can : can = MakeCan(5);
        public static Mesh Crust => crust ? crust : crust = MakeCrust(9);

        /// <summary>Every triangle its own corners, so each face is lit flat: creased, crumpled, crushed.</summary>
        static Mesh Faceted(string name, List<Vector3> v, List<int> triangles)
        {
            var corners = new List<Vector3>(triangles.Count);
            var flat = new List<int>(triangles.Count);
            foreach (int i in triangles)
            {
                flat.Add(corners.Count);
                corners.Add(v[i]);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(corners);
            mesh.SetTriangles(flat, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A ball of paper: an icosphere with every corner pushed in or out at random, lit facet by facet.</summary>
        static Mesh MakePaper(int seed)
        {
            var random = new System.Random(seed);
            Mesh ball = Mess.Icosphere(1);
            var v = new List<Vector3>(ball.vertices);
            for (int i = 0; i < v.Count; i++) v[i] *= 0.5f * (0.72f + 0.42f * (float)random.NextDouble());
            return Faceted("Crumpled paper", v, new List<int>(ball.triangles));
        }

        /// <summary>
        /// A banana peel as it lands in cartoons: the stalk on top, three flaps arching down from it to the desk
        /// and curling up at their ends, each tapered and curved like the skin it was. Both sides are drawn.
        /// The bottom (the flap ends) is at y = 0, the stalk end at about 0.38.
        /// </summary>
        static Mesh MakePeel(int seed)
        {
            var random = new System.Random(seed);
            float R() => (float)random.NextDouble();
            const int steps = 10, flaps = 3, across = 4;
            const float top = 0.38f;
            var front = new List<Vector3>();
            var triangles = new List<int>();
            for (int f = 0; f < flaps; f++)
            {
                float angle = f * Mathf.PI * 2f / flaps + (R() - 0.5f) * 0.5f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var side = new Vector3(-outward.z, 0f, outward.x);
                float length = 0.42f + R() * 0.1f, curl = 0.05f + R() * 0.06f;
                int start = front.Count;
                for (int i = 0; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    // Out from the stalk and down to the desk (an arch), the end curling up off it.
                    float reach = 0.05f + Mathf.Sin(t * Mathf.PI * 0.5f) * length;
                    float height = top * Mathf.Pow(1f - t, 1.6f) + curl * Mathf.SmoothStep(0.8f, 1f, t);
                    Vector3 centre = outward * reach + Vector3.up * height;
                    float half = 0.105f * (1f - 0.7f * t) + 0.012f;
                    for (int k = 0; k <= across; k++)
                    {
                        float u = (float)k / across * 2f - 1f; // -1 .. 1 across the flap
                        // Curved like the skin of the banana: the middle bulges outward, the edges turn in.
                        Vector3 bulge = (outward * 0.7f + Vector3.up * 0.3f) * (0.035f * (1f - u * u) * (1f - t * 0.6f));
                        front.Add(centre + side * (u * half) + bulge);
                    }
                }
                int row = across + 1;
                for (int i = 0; i < steps; i++)
                for (int k = 0; k < across; k++)
                {
                    int a = start + i * row + k, b = a + 1, c = a + row, d = c + 1;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            }
            // The other side: the same corners again, wound the other way, so both sides are lit.
            var v = new List<Vector3>(front);
            int n = front.Count;
            v.AddRange(front);
            var both = new List<int>(triangles);
            for (int i = 0; i < triangles.Count; i += 3)
                both.AddRange(new[] { triangles[i] + n, triangles[i + 2] + n, triangles[i + 1] + n });
            var mesh = new Mesh { name = "Banana peel" };
            mesh.SetVertices(v);
            mesh.SetTriangles(both, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A soda can stamped on: pinched in the middle with three dents round it, knocked sideways, creased.</summary>
        static Mesh MakeCan(int seed)
        {
            var random = new System.Random(seed);
            const int rings = 8, segments = 14;
            var v = new List<Vector3>();
            var triangles = new List<int>();
            float phase = (float)random.NextDouble() * 6.28f;
            for (int i = 0; i <= rings; i++)
            {
                float h = (float)i / rings;
                float crush = Mathf.Sin(h * Mathf.PI);
                for (int j = 0; j < segments; j++)
                {
                    float a = (float)j / segments * Mathf.PI * 2f;
                    float r = 0.5f * (1f - 0.32f * crush * (0.65f + 0.35f * Mathf.Cos(3f * a + phase))) * (0.95f + 0.1f * (float)random.NextDouble());
                    v.Add(new Vector3(Mathf.Cos(a) * r + 0.14f * h * h, h - 0.5f, Mathf.Sin(a) * r));
                }
            }
            int Index(int ring, int segment) => ring * segments + segment % segments;
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < segments; j++)
                triangles.AddRange(new[] { Index(i, j), Index(i + 1, j), Index(i, j + 1), Index(i, j + 1), Index(i + 1, j), Index(i + 1, j + 1) });
            int bottom = v.Count;
            v.Add(new Vector3(0f, -0.5f, 0f));
            int lid = v.Count;
            v.Add(new Vector3(0.14f, 0.5f, 0f));
            for (int j = 0; j < segments; j++)
            {
                triangles.AddRange(new[] { bottom, Index(0, j), Index(0, j + 1) });
                triangles.AddRange(new[] { lid, Index(rings, j + 1), Index(rings, j) });
            }
            return Faceted("Crushed can", v, triangles);
        }

        /// <summary>A bitten-off pizza crust: a lumpy tube bent round in an arc, flat underneath, ends rounded off.</summary>
        static Mesh MakeCrust(int seed)
        {
            var random = new System.Random(seed);
            const int along = 12, around = 10;
            var v = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= along; i++)
            {
                float t = (float)i / along;
                float arc = Mathf.Lerp(-0.75f, 0.75f, t);
                var centre = new Vector3(Mathf.Sin(arc) * 0.45f, 0f, Mathf.Cos(arc) * 0.45f - 0.3f);
                var outward = new Vector3(Mathf.Sin(arc), 0f, Mathf.Cos(arc));
                float thick = 0.13f * Mathf.Sqrt(Mathf.Sin(Mathf.Lerp(0.15f, 0.85f, t) * Mathf.PI)) * (0.9f + 0.2f * (float)random.NextDouble());
                for (int j = 0; j < around; j++)
                {
                    float a = (float)j / around * Mathf.PI * 2f;
                    float y = Mathf.Sin(a) * thick;
                    v.Add(centre + outward * (Mathf.Cos(a) * thick) + Vector3.up * (y < 0f ? y * 0.35f : y * 0.8f));
                }
            }
            int Index(int ring, int segment) => ring * around + segment % around;
            for (int i = 0; i < along; i++)
            for (int j = 0; j < around; j++)
                triangles.AddRange(new[] { Index(i, j), Index(i + 1, j), Index(i, j + 1), Index(i, j + 1), Index(i + 1, j), Index(i + 1, j + 1) });
            for (int end = 0; end < 2; end++)
            {
                int ring = end == 0 ? 0 : along, centre = v.Count;
                Vector3 c = Vector3.zero;
                for (int j = 0; j < around; j++) c += v[Index(ring, j)];
                v.Add(c / around);
                for (int j = 0; j < around; j++)
                    triangles.AddRange(end == 0 ? new[] { centre, Index(ring, j), Index(ring, j + 1) } : new[] { centre, Index(ring, j + 1), Index(ring, j) });
            }
            var mesh = new Mesh { name = "Pizza crust" };
            mesh.SetVertices(v);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
