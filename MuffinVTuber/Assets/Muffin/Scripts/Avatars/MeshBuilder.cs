using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// Small procedural mesh helper for placeholder avatars: lathed shapes (pots, stems, spheres) and flat
    /// ellipses (petals, leaves, discs), one submesh per material.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int>[] triangles;

        public MeshBuilder(int submeshes)
        {
            triangles = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) triangles[i] = new List<int>();
        }

        /// <summary>
        /// Spins a profile (x = radius, y = height) around the Y axis, then applies `transform`
        /// (positive scales only). Faces point outward for a profile listed bottom to top.
        /// </summary>
        public void Lathe(int submesh, IList<Vector2> profile, int sides, Matrix4x4 transform)
        {
            int start = vertices.Count;
            foreach (Vector2 p in profile)
            {
                for (int s = 0; s < sides; s++)
                {
                    float a = s * 2f * Mathf.PI / sides;
                    vertices.Add(transform.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x)));
                }
            }
            for (int r = 0; r < profile.Count - 1; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = start + r * sides + s;
                    int b = start + r * sides + (s + 1) % sides;
                    int c = a + sides;
                    int d = b + sides;
                    triangles[submesh].AddRange(new[] { a, c, b, b, c, d });
                }
            }
        }

        /// <summary>A UV-sphere-like ellipsoid: a lathed semicircle scaled by `radii`.</summary>
        public void Ellipsoid(int submesh, Vector3 centre, Vector3 radii, int sides = 24, int rings = 14)
        {
            var profile = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, (float)i / rings) * Mathf.Deg2Rad;
                profile[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            Lathe(submesh, profile, sides, Matrix4x4.TRS(centre, Quaternion.identity, radii));
        }

        /// <summary>
        /// A flat ellipse with half-axes u and v. Double-sided by default (separate back vertices, so each
        /// side lights correctly); pass `faceOnly` for a single side facing that direction.
        /// </summary>
        public void Ellipse(int submesh, Vector3 centre, Vector3 u, Vector3 v, int segments, Vector3? faceOnly = null)
        {
            Vector3 facing = faceOnly ?? Vector3.Cross(u, v);
            Fan(submesh, centre, u, v, segments, facing);
            if (faceOnly == null) Fan(submesh, centre, u, v, segments, -facing);
        }

        void Fan(int submesh, Vector3 centre, Vector3 u, Vector3 v, int segments, Vector3 facing)
        {
            int c = vertices.Count;
            vertices.Add(centre);
            for (int i = 0; i < segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                vertices.Add(centre + u * Mathf.Cos(a) + v * Mathf.Sin(a));
            }
            for (int i = 0; i < segments; i++) Triangle(submesh, c, c + 1 + i, c + 1 + (i + 1) % segments, facing);
        }

        /// <summary>Adds a triangle wound so its front face points along `facing`.</summary>
        void Triangle(int submesh, int a, int b, int c, Vector3 facing)
        {
            // Unity's front face is the side Cross(b - a, c - a) points to.
            Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (Vector3.Dot(n, facing) < 0f) (b, c) = (c, b);
            triangles[submesh].Add(a);
            triangles[submesh].Add(b);
            triangles[submesh].Add(c);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
