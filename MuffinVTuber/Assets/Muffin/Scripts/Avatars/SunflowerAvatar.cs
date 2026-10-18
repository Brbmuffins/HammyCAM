using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Builds the placeholder potted sunflower at startup: the procedural mesh, its bend rig (pot rooted,
    /// petals and face rigid with the head, stem bending between) and the digital face.
    /// </summary>
    [RequireComponent(typeof(BendRig), typeof(DigitalFace))]
    public sealed class SunflowerAvatar : MonoBehaviour
    {
        [Header("Body (pot, soil, stem, petals, centre)")]
        public Material pot;
        public Material soil;
        public Material stem;
        public Material petals;
        public Material centre;

        void Awake()
        {
            var rig = GetComponent<BendRig>();
            Mesh mesh = SunflowerMesh.Build();
            rig.Build(mesh, new[] { pot, soil, stem, petals, centre },
                SunflowerMesh.RootY, SunflowerMesh.HeadY,
                rootSubmeshes: new[] { SunflowerMesh.Pot, SunflowerMesh.Soil },
                headSubmeshes: new[] { SunflowerMesh.Petals, SunflowerMesh.Centre });

            // On the front of the flower's dome, which sits FlowerY - HeadY above the head pivot, a little bigger
            // than the standard face so real eyes and a mouth fit; the parts follow the dome's curve.
            const float scale = 1.2f;
            var face = GetComponent<DigitalFace>();
            face.Build(rig, rig.HeadBone, new Vector3(0f, SunflowerMesh.FlowerY - SunflowerMesh.HeadY, SunflowerMesh.FaceZ), scale);
            face.Surface = FrontSurface.Build(mesh, new Vector3(0f, SunflowerMesh.FlowerY, SunflowerMesh.FaceZ), scale, new Rect(-0.4f, -0.32f, 0.8f, 0.58f), 0.008f);
        }
    }
}
