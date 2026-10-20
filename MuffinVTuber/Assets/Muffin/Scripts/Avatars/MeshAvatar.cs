using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// A bendy object avatar made from any model (a generated GLB, an STL, anything with a mesh). The model
    /// is merged, turned to face the camera, scaled and planted on the ground once in the editor (see
    /// MuffinPrototypeSetup.BakeAvatar); at startup this hands that mesh to the automatic bend rig and puts
    /// the digital face on its front surface at faceFraction of its height.
    /// </summary>
    [RequireComponent(typeof(BendRig), typeof(DigitalFace))]
    public sealed class MeshAvatar : MonoBehaviour, IAvatarFeatures
    {
        public string displayName = "Avatar";
        [Tooltip("Its model's id: its settings are kept under it (AvatarSettings).")]
        public string id;
        [Tooltip("The pack it comes in (AvatarPacks): packs can be switched off in the menu.")]
        public string pack;
        [Tooltip("Baked mesh: facing -Z, base on y = 0, centred on x/z.")]
        public Mesh mesh;
        public Material[] materials;

        [Header("Bend zones (fractions of the height)")]
        [Tooltip("Below this the avatar never moves (its base, pot or stand).")]
        [Range(0f, 0.9f)] public float rootFraction = 0.12f;
        [Tooltip("Above this the avatar moves rigidly with your head. Keep it below the face.")]
        [Range(0.1f, 1f)] public float headFraction = 0.55f;
        [Tooltip("Above this the avatar flops on its own, like a pizza slice's point (1 = no floppy tip). Keep it above the face.")]
        [Range(0.1f, 1f)] public float tipFraction = 1f;

        [Header("Face")]
        [Range(0f, 1f)] public float faceFraction = 0.7f;
        [Tooltip("Face size relative to the avatar's width at the face's height.")]
        [Range(0.2f, 2f)] public float faceSize = 1f;
        public bool faceHasMouth = true;
        [Tooltip("Gap between the surface and the face stickers, in metres.")]
        public float faceGap = 0.004f;
        [Tooltip("Moves the face sideways (metres), e.g. back onto a mug's body when its handle makes it look off-centre.")]
        public float faceShiftX;

        [Tooltip("A second place for the face the streamer can pick on its page (-1 = none): fraction, size, and the switch's words.")]
        public float altFaceFraction = -1f, altFaceSize = 0.8f;
        public bool altFaceHasMouth = true;
        public string altFaceLabel;

        public void Describe(System.Collections.Generic.List<AvatarFeature> into)
        {
            if (altFaceFraction >= 0f)
                into.Add(new AvatarFeature { Key = "altface", Label = (altFaceLabel ?? "Face in its other place") + " (after a restart)", Default = false });
        }

        void Awake()
        {
            var rig = GetComponent<BendRig>();
            Bounds b = mesh.bounds;
            if (altFaceFraction >= 0f && AvatarSettings.On(AvatarSettings.IdOf(this), "altface", false))
            {
                faceFraction = altFaceFraction;
                faceSize = altFaceSize;
                faceHasMouth = altFaceHasMouth;
            }
            // The face rides the rigid head, or a floppy tip when it sits up there (a rubber chicken's head on its neck).
            bool onTip = tipFraction < 0.99f && faceFraction >= tipFraction;
            float head = onTip ? Mathf.Min(headFraction, tipFraction - 0.02f) : Mathf.Min(headFraction, faceFraction - 0.02f);
            rig.BuildFromFractions(mesh, materials, rootFraction, head, tipFraction);

            float faceY = b.min.y + b.size.y * faceFraction;
            Transform anchor = rig.BoneAt(faceY, out Vector3 rest);
            FrontAt(mesh, faceY, b.size.y * 0.035f, faceShiftX, out float frontZ, out float width);
            // The sticker layout is drawn for a 26 cm-wide face; fit it to ~80% of the avatar's width there.
            float scale = faceSize * width * 0.8f / 0.26f;
            var face = GetComponent<DigitalFace>();
            face.Build(rig, anchor, new Vector3(faceShiftX, faceY - rest.y, frontZ - faceGap), scale, faceHasMouth);
            face.TipSteady = onTip ? 0.6f : 0f;
            // The front around the face, so the face parts follow its curves and bumps (a gap in front of them).
            face.Surface = FrontSurface.Build(mesh, new Vector3(faceShiftX, faceY, frontZ - faceGap), scale, new Rect(-0.4f, -0.32f, 0.8f, 0.58f), 0.008f);
        }

        /// <summary>The nearest point of the surface seen from the front (lowest z) straight in at (x, y), if any.</summary>
        static bool SurfaceFront(Vector3[] v, int[] triangles, float x, float y, out float z)
        {
            z = float.MaxValue;
            bool hit = false;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = v[triangles[i]], b = v[triangles[i + 1]], c = v[triangles[i + 2]];
                // Is (x, y) inside the triangle seen from the front? Then its depth there.
                float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(d) < 1e-12f) continue;
                float wa = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / d;
                float wb = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / d;
                float wc = 1f - wa - wb;
                if (wa < 0f || wb < 0f || wc < 0f) continue;
                float depth = wa * a.z + wb * b.z + wc * c.z;
                if (depth < z)
                {
                    z = depth;
                    hit = true;
                }
            }
            return hit;
        }

        /// <summary>The front surface (lowest z, the side facing the camera) and the width at height y.</summary>
        static void FrontAt(Mesh mesh, float y, float band, float shiftX, out float frontZ, out float width)
        {
            Vector3[] v = mesh.vertices;
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (Vector3 p in v)
            {
                if (Mathf.Abs(p.y - y) > band) continue;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
            }
            width = maxX > minX ? maxX - minX : mesh.bounds.size.x;
            float centreX = shiftX != 0f ? shiftX : maxX > minX ? (minX + maxX) * 0.5f : 0f;
            frontZ = 0f;
            float bandFront = float.MaxValue;
            bool found = false;
            foreach (Vector3 p in v)
            {
                if (Mathf.Abs(p.y - y) > band) continue;
                bandFront = Mathf.Min(bandFront, p.z);
                if (Mathf.Abs(p.x - centreX) > width * 0.15f) continue;
                if (!found || p.z < frontZ) frontZ = p.z;
                found = true;
            }
            if (!found)
            {
                frontZ = mesh.bounds.min.z;
                return;
            }
            // The surface itself can be further forward than any corner near the middle: a long smooth part (a
            // sausage) is a few long triangles with hardly a corner where the face goes. Look along +z for the
            // first triangle at a few points around the face's middle.
            int[] triangles = mesh.triangles;
            foreach (float dx in new[] { 0f, -0.08f, 0.08f })
            foreach (float dy in new[] { 0f, -0.5f, 0.5f })
                if (SurfaceFront(v, triangles, centreX + dx * width, y + dy * band, out float z) && z < frontZ) frontZ = z;
            // A middle set back between parts that stick out at the sides (a sausage between bun halves): the
            // face goes on the middle and is sized to it, or its eyes would end up inside the sides.
            if (frontZ - bandFront > 0.05f)
            {
                float left = float.MaxValue, right = float.MinValue;
                foreach (Vector3 p in v)
                {
                    if (Mathf.Abs(p.y - y) > band || p.z < frontZ - 0.01f || p.z > frontZ + 0.03f || Mathf.Abs(p.x - centreX) > width * 0.3f) continue;
                    left = Mathf.Min(left, p.x);
                    right = Mathf.Max(right, p.x);
                }
                if (right > left) width = Mathf.Min(width, right - left);
            }
        }
    }
}
