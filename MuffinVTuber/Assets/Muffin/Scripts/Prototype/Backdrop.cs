using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// A painted room behind the set (a vacuum sales floor, an airport gate, a construction site…): a picture on a
    /// big quad far behind everything, filling the camera's view whatever the zoom. Chosen with the background
    /// (G cycles through them, M → Stream lists them).
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the stage camera has framed this frame
    public sealed class Backdrop : MonoBehaviour
    {
        public Camera cam;
        [Tooltip("Unlit material the pictures are shown with.")]
        public Material material;
        public Texture2D[] pictures;
        [Tooltip("The same rooms composed for a tall (vertical, TikTok) window; used there when present.")]
        public Texture2D[] tallPictures;
        public string[] names;
        [Tooltip("A touch darker than the picture, so the avatar stands out.")]
        public Color tint = new Color(0.88f, 0.88f, 0.88f, 1f);
        public float distance = 30f;

        Transform quad;
        Material instance;
        Texture2D shown;

        public int Count => pictures != null ? pictures.Length : 0;
        public int Current { get; private set; } = -1;
        public string NameOf(int i) => names != null && i >= 0 && i < names.Length ? names[i] : $"Room {i + 1}";

        void Awake()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Backdrop";
            Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            instance = material ? new Material(material) : null;
            r.sharedMaterial = instance;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            quad = go.transform;
            if (cam) quad.SetParent(cam.transform, false);
            go.SetActive(false);
        }

        /// <summary>Show room i (or none, with -1).</summary>
        public void Show(int i)
        {
            Current = i >= 0 && i < Count ? i : -1;
            if (!quad) return;
            quad.gameObject.SetActive(Current >= 0);
            if (Current < 0 || !instance) return;
            shown = null; // picked (wide or tall) on the next frame
            instance.SetColor("_BaseColor", tint);
        }

        /// <summary>Room i's picture for this window: the tall one in a tall window, if there is one.</summary>
        Texture2D PictureFor(int i)
        {
            bool tall = cam && cam.aspect < DeskScene.TallAspect;
            if (tall && tallPictures != null && i < tallPictures.Length && tallPictures[i]) return tallPictures[i];
            return pictures[i];
        }

        void LateUpdate()
        {
            if (Current < 0 || !cam || !quad) return;
            // Far behind everything, covering the whole view (cropping the picture's edges rather than leaving gaps).
            float d = Mathf.Min(distance, cam.farClipPlane * 0.9f);
            float height = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float width = height * cam.aspect;
            Texture2D picture = PictureFor(Current);
            if (picture != shown && instance)
            {
                instance.SetTexture("_BaseMap", picture);
                shown = picture;
            }
            float pictureAspect = picture ? (float)picture.width / picture.height : 16f / 9f;
            if (width / height > pictureAspect) height = width / pictureAspect;
            else width = height * pictureAspect;
            quad.localPosition = new Vector3(0f, 0f, d);
            quad.localRotation = Quaternion.identity;
            quad.localScale = new Vector3(width * 1.01f, height * 1.01f, 1f);
        }
    }
}
