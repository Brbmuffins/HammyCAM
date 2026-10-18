using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The avatar's front around its face as a depth map in the face's own space (x right, y up, z away from the
    /// camera): how far back the surface is at each point, so the face parts can follow it, a little in front of
    /// it, wrapping round a curved front and riding over bumps instead of sinking in. Made from the mesh's
    /// triangles (not its corners: a long smooth part has hardly any corners where the face goes), at rest to
    /// start with, then again from the avatar as it's posed (it bends under the face too), see Rasterize.
    /// </summary>
    public sealed class FrontSurface
    {
        readonly float[] depth, nearest; // NaN where nothing is in front (off the avatar's edge)
        readonly int nx, ny;
        readonly float x0, y0, step;
        readonly int[] triangles; // the mesh's triangles near the face (corner indices)

        FrontSurface(int nx, int ny, float x0, float y0, float step, int[] triangles)
        {
            this.nx = nx;
            this.ny = ny;
            this.x0 = x0;
            this.y0 = y0;
            this.step = step;
            this.triangles = triangles;
            depth = new float[nx * ny];
            nearest = new float[nx * ny];
        }

        /// <summary>
        /// From the mesh (in its own space) and where the face sits on it: the face's origin in mesh space and its
        /// scale (face units to metres). Covers `region` of the face (face units) at `step` spacing.
        /// </summary>
        public static FrontSurface Build(Mesh mesh, Vector3 faceOrigin, float faceScale, Rect region, float step)
        {
            Vector3[] v = mesh.vertices;
            var face = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) face[i] = (v[i] - faceOrigin) / faceScale;
            // Only the triangles near the face (with room for the avatar bending under it) are ever needed.
            Rect near = new Rect(region.xMin - 0.2f, region.yMin - 0.2f, region.width + 0.4f, region.height + 0.4f);
            var picked = new List<int>();
            int[] all = mesh.triangles;
            for (int t = 0; t + 2 < all.Length; t += 3)
            {
                Vector3 a = face[all[t]], b = face[all[t + 1]], c = face[all[t + 2]];
                if (Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < near.xMin || Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > near.xMax) continue;
                if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < near.yMin || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > near.yMax) continue;
                picked.Add(all[t]);
                picked.Add(all[t + 1]);
                picked.Add(all[t + 2]);
            }
            int nx = Mathf.CeilToInt(region.width / step) + 1, ny = Mathf.CeilToInt(region.height / step) + 1;
            var map = new FrontSurface(nx, ny, region.xMin, region.yMin, step, picked.ToArray());
            map.Rasterize(face);
            return map;
        }

        /// <summary>
        /// Redo the map from the avatar as it is now: `face` holds all its mesh's corners in the face's space (the
        /// ones near the face are used). Then add bumps for anything stuck on the front (AddBump).
        /// </summary>
        public void Rasterize(Vector3[] face)
        {
            for (int i = 0; i < depth.Length; i++) depth[i] = float.PositiveInfinity;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = face[triangles[t]], b = face[triangles[t + 1]], c = face[triangles[t + 2]];
                float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(d) < 1e-12f) continue;
                // The grid points the triangle covers seen from the front: the nearest surface wins.
                int i0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - x0) / step));
                int i1 = Mathf.Min(nx - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - x0) / step));
                int j0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - y0) / step));
                int j1 = Mathf.Min(ny - 1, Mathf.FloorToInt((Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - y0) / step));
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float x = x0 + i * step, y = y0 + j * step;
                    float wa = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / d;
                    float wb = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / d;
                    float wc = 1f - wa - wb;
                    if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                    float z = wa * a.z + wb * b.z + wc * c.z;
                    int k = j * nx + i;
                    if (z < depth[k]) depth[k] = z;
                }
            }
            // Each point takes the nearest surface around it too, so a bump between two samples can't poke through.
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                float best = float.PositiveInfinity;
                for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii < 0 || jj < 0 || ii >= nx || jj >= ny) continue;
                    best = Mathf.Min(best, depth[jj * nx + ii]);
                }
                nearest[j * nx + i] = float.IsPositiveInfinity(depth[j * nx + i]) ? float.NaN : best;
            }
            System.Array.Copy(nearest, depth, depth.Length);
        }

        /// <summary>
        /// Something stuck on the front (a pepperoni slice), in face space: its centre, radius and how far forward
        /// its nearest edge is. The face parts ride over it too instead of hiding behind it.
        /// </summary>
        public void AddBump(Vector3 centre, float radius, float frontZ)
        {
            float r = radius + step;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((centre.x - r - x0) / step)), i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((centre.x + r - x0) / step));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((centre.y - r - y0) / step)), j1 = Mathf.Min(ny - 1, Mathf.CeilToInt((centre.y + r - y0) / step));
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                float x = x0 + i * step - centre.x, y = y0 + j * step - centre.y;
                if (x * x + y * y > r * r) continue;
                int k = j * nx + i;
                depth[k] = float.IsNaN(depth[k]) ? frontZ : Mathf.Min(depth[k], frontZ);
            }
        }

        /// <summary>The surface's depth (face units) at (x, y) on the face, if the avatar is there.</summary>
        public bool TryDepth(float x, float y, out float z)
        {
            z = 0f;
            float fx = (x - x0) / step, fy = (y - y0) / step;
            int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fy);
            if (i < 0 || j < 0 || i >= nx - 1 || j >= ny - 1) return false;
            float u = fx - i, w = fy - j;
            float a = depth[j * nx + i], b = depth[j * nx + i + 1], c = depth[(j + 1) * nx + i], d = depth[(j + 1) * nx + i + 1];
            bool fa = !float.IsNaN(a), fb = !float.IsNaN(b), fc = !float.IsNaN(c), fd = !float.IsNaN(d);
            if (fa && fb && fc && fd)
            {
                z = Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), w);
                return true;
            }
            // At the avatar's edge: the nearest of the samples that are on it.
            float best = float.PositiveInfinity;
            if (fa) best = Mathf.Min(best, a);
            if (fb) best = Mathf.Min(best, b);
            if (fc) best = Mathf.Min(best, c);
            if (fd) best = Mathf.Min(best, d);
            if (float.IsPositiveInfinity(best)) return false;
            z = best;
            return true;
        }
    }
}
