using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// A placeholder potted sunflower built from code (about 1.2 units tall, facing -Z), so the motion
    /// can be tested before any generated model exists. Swapping in a Tripo model later only changes
    /// the mesh handed to BendRig.
    /// </summary>
    public static class SunflowerMesh
    {
        // Submesh (and material) order.
        public const int Pot = 0, Soil = 1, Stem = 2, Petals = 3, Centre = 4, SubmeshCount = 5;

        /// <summary>Top of the soil: the pot below it never moves.</summary>
        public const float RootY = 0.25f;
        /// <summary>Head pivot, inside the flower: everything above turns with the face.</summary>
        public const float HeadY = 0.86f;
        /// <summary>Centre of the flower (where the face goes).</summary>
        public const float FlowerY = 0.92f;
        /// <summary>Radius of the flower's centre, the face's dome (big enough for real eyes and a mouth).</summary>
        public const float CentreRadius = 0.17f;
        /// <summary>The dome's front, where the face sits.</summary>
        public const float FaceZ = -0.082f;

        public static Mesh Build()
        {
            var mb = new MeshBuilder(SubmeshCount);

            // Terracotta pot with a rolled rim, listed bottom to top so faces point outward.
            mb.Lathe(Pot, new[]
            {
                V(0f, 0f), V(0.15f, 0f), V(0.2f, 0.205f), V(0.235f, 0.21f), V(0.242f, 0.245f),
                V(0.235f, 0.28f), V(0.205f, 0.28f), V(0.2f, RootY),
            }, 40, Matrix4x4.identity);
            mb.Ellipse(Soil, new Vector3(0f, RootY, 0f), new Vector3(0f, 0f, 0.2f), new Vector3(0.2f, 0f, 0f), 40, Vector3.up);

            // Stem: many rings so it bends smoothly instead of kinking.
            var stem = new List<Vector2>();
            const int rings = 32;
            for (int i = 0; i <= rings; i++) stem.Add(V(0.032f, Mathf.Lerp(RootY - 0.02f, HeadY + 0.04f, (float)i / rings)));
            mb.Lathe(Stem, stem, 12, Matrix4x4.Translate(new Vector3(0f, 0f, 0.012f)));

            // Two leaves, tilted toward the camera so they read from the front.
            Leaf(mb, 0.45f, +1f);
            Leaf(mb, 0.57f, -1f);

            // Flower: a squashed dome for the face, sixteen petals around it.
            mb.Ellipsoid(Centre, new Vector3(0f, FlowerY, -0.02f), new Vector3(CentreRadius, CentreRadius, 0.06f));
            const int petals = 16;
            for (int k = 0; k < petals; k++)
            {
                float a = k * 2f * Mathf.PI / petals;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                var side = new Vector3(-dir.y, dir.x, 0f);
                // Alternate depths so overlapping petals don't flicker; tips cup slightly toward the camera.
                var centre = new Vector3(0f, FlowerY, k % 2 == 0 ? 0.004f : 0.014f) + dir * (CentreRadius + 0.06f);
                mb.Ellipse(Petals, centre, dir * 0.075f + new Vector3(0f, 0f, -0.012f), side * 0.034f, 14);
            }

            Mesh mesh = mb.ToMesh("Sunflower");
            return mesh;
        }

        static void Leaf(MeshBuilder mb, float y, float side)
        {
            var along = new Vector3(Mathf.Cos(30f * Mathf.Deg2Rad) * side, Mathf.Sin(30f * Mathf.Deg2Rad), 0f);
            var across = (new Vector3(-along.y, along.x, 0f) * 0.6f + new Vector3(0f, 0f, -0.8f)).normalized;
            var basePoint = new Vector3(0.028f * side, y, 0.012f);
            mb.Ellipse(Stem, basePoint + along * 0.09f, along * 0.09f, across * 0.036f, 16);
        }

        static Vector2 V(float radius, float y) => new Vector2(radius, y);
    }
}
