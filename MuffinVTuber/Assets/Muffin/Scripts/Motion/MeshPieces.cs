using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The separate pieces a model is made of (an AI model's arms, ears and tail are often their own little meshes
    /// pushed into the body): points that touch, or sit in the same place (seams split them), are one piece.
    /// </summary>
    public static class MeshPieces
    {
        /// <summary>The piece number of every point (0, 1, 2… in no special order) and how many pieces there are.</summary>
        public static int[] Find(Mesh mesh, Vector3[] points, out int count)
        {
            int n = points.Length;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Root(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            void Join(int a, int b)
            {
                a = Root(a);
                b = Root(b);
                if (a != b) parent[b] = a;
            }
            // Same place (to half a millimetre of the model's own units, scaled to its size) = same point.
            Bounds bounds = GeometryUtility.CalculateBounds(points, Matrix4x4.identity);
            float cell = Mathf.Max(bounds.size.magnitude, 1e-4f) / 4000f;
            var seen = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n; i++)
            {
                Vector3 p = points[i] / cell;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (seen.TryGetValue(key, out int first)) Join(first, i);
                else seen[key] = i;
            }
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tri = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    Join(tri[i], tri[i + 1]);
                    Join(tri[i], tri[i + 2]);
                }
            }
            var number = new Dictionary<int, int>();
            var piece = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = Root(i);
                if (!number.TryGetValue(r, out int k)) number[r] = k = number.Count;
                piece[i] = k;
            }
            count = number.Count;
            return piece;
        }

        /// <summary>
        /// Long thin pieces (a stray spike of a tail: much longer than it is thick, and long) — true for each piece
        /// to drop. Measured in the model's own units against its height.
        /// </summary>
        public static bool[] Thin(Vector3[] points, int[] piece, int count, float minLengthOfHeight = 0.22f, float ratio = 12f)
        {
            var lo = new Vector3[count];
            var hi = new Vector3[count];
            for (int k = 0; k < count; k++)
            {
                lo[k] = Vector3.one * float.MaxValue;
                hi[k] = Vector3.one * float.MinValue;
            }
            for (int i = 0; i < points.Length; i++)
            {
                lo[piece[i]] = Vector3.Min(lo[piece[i]], points[i]);
                hi[piece[i]] = Vector3.Max(hi[piece[i]], points[i]);
            }
            float height = GeometryUtility.CalculateBounds(points, Matrix4x4.identity).size.y;
            var thin = new bool[count];
            for (int k = 0; k < count; k++)
            {
                Vector3 e = hi[k] - lo[k];
                float longest = Mathf.Max(e.x, Mathf.Max(e.y, e.z)), shortest = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
                thin[k] = longest > height * minLengthOfHeight && longest > ratio * Mathf.Max(shortest, 1e-6f);
            }
            return thin;
        }
    }
}
