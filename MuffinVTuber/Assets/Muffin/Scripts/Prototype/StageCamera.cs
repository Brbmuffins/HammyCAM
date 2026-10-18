using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Frames the scene for any window shape. Instead of a fixed zoom, it keeps a box in view: the avatar
    /// with room for its biggest stretch and lean (and the desk, keyboard and mouse when the desk is on).
    /// A wide window, a square one or a tall narrow one all show the whole avatar; nothing gets cut off at
    /// the top or sides.
    ///
    /// Hold X for the dramatic zoom: the camera swings in on the face (and DigitalFace blows the eyes and
    /// mouth up), springing back with a little overshoot when you let go.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class StageCamera : MonoBehaviour
    {
        public AvatarLibrary library;
        public DeskScene desk;

        [Header("Framing: these boxes stay in view whatever the window's shape")]
        public Vector3 deskPosition = new Vector3(0f, 1.1f, -3.1f);
        public Bounds deskBox = new Bounds(new Vector3(0f, 0.69f, -0.2f), new Vector3(1.44f, 1.46f, 0.76f));
        public Vector3 soloPosition = new Vector3(0f, 0.78f, -3.4f);
        public Bounds soloBox = new Bounds(new Vector3(0f, 0.72f, 0f), new Vector3(1.2f, 1.46f, 0.5f));
        [Header("A tall window (vertical, for TikTok): narrower boxes, so the avatar fills the picture")]
        public Bounds tallDeskBox = new Bounds(new Vector3(0.03f, 0.7f, -0.2f), new Vector3(0.92f, 1.5f, 0.76f));
        public Bounds tallSoloBox = new Bounds(new Vector3(0f, 0.72f, 0f), new Vector3(0.8f, 1.46f, 0.5f));
        [Tooltip("Extra room around the box, as a fraction of the view.")]
        [Range(0f, 0.3f)] public float margin = 0.03f;

        [Header("Dramatic zoom (hold X)")]
        [Tooltip("How much of the view height the face region fills when zoomed in (metres either side of it, per metre of face).")]
        [Range(0.05f, 1f)] public float punchHalfHeight = 0.34f;
        [Range(0.5f, 10f)] public float punchFrequency = 3.2f;
        [Range(0.1f, 1.5f)] public float punchDamping = 0.5f;

        /// <summary>0 normal view .. 1 zoomed in on the face (a little over 1 while it overshoots).</summary>
        public static float Punch { get; private set; }
        /// <summary>Zoom in as if X were held (the self-test, and later a stream hotkey).</summary>
        public static bool HoldZoom { get; set; }

        Camera cam;
        SpringFloat punchSpring;

        void Awake()
        {
            cam = GetComponent<Camera>();
            punchSpring = new SpringFloat(punchFrequency, punchDamping, 1f, 0f);
        }

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            bool deskOn = desk && desk.Shown;
            Vector3 position = deskOn ? deskPosition : soloPosition;
            bool tall = cam.aspect < DeskScene.TallAspect;
            Bounds box = deskOn ? (tall ? tallDeskBox : deskBox) : (tall ? tallSoloBox : soloBox);
            Quaternion look = Quaternion.LookRotation(box.center - position);
            float fov = FitFov(position, look, box, cam.aspect, margin);

            punchSpring.SetParameters(punchFrequency, punchDamping, 1f);
            Punch = Mathf.Clamp(punchSpring.Update(Keys.Held(MuffinKey.X) || HoldZoom ? 1f : 0f, dt), -0.15f, 1.3f);
            Quaternion rotation = look;
            Transform face = Face();
            if (face && Mathf.Abs(Punch) > 1e-3f)
            {
                Vector3 toFace = face.position - position;
                float faceFov = 2f * Mathf.Atan(punchHalfHeight * face.lossyScale.x / Mathf.Max(0.1f, toFace.magnitude)) * Mathf.Rad2Deg;
                rotation = Quaternion.SlerpUnclamped(look, Quaternion.LookRotation(toFace), Punch);
                fov = Mathf.LerpUnclamped(fov, faceFov, Punch);
            }
            transform.SetPositionAndRotation(position, rotation);
            cam.fieldOfView = Mathf.Clamp(fov, 4f, 110f);
        }

        Transform Face()
        {
            BendRig rig = library ? library.ActiveRig : null;
            if (!rig) return null;
            return rig.TryGetComponent(out DigitalFace face) && face.FaceAnchor ? face.FaceAnchor : rig.HeadBone;
        }

        /// <summary>The narrowest vertical field of view that shows every corner of the box.</summary>
        static float FitFov(Vector3 position, Quaternion rotation, Bounds box, float aspect, float margin)
        {
            Quaternion toCamera = Quaternion.Inverse(rotation);
            float need = 0.05f; // tan of half the vertical view
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? box.min.x : box.max.x,
                    (i & 2) == 0 ? box.min.y : box.max.y,
                    (i & 4) == 0 ? box.min.z : box.max.z);
                Vector3 c = toCamera * (corner - position);
                if (c.z < 0.05f) continue;
                need = Mathf.Max(need, Mathf.Abs(c.y) / c.z, Mathf.Abs(c.x) / (c.z * Mathf.Max(0.1f, aspect)));
            }
            return 2f * Mathf.Atan(need * (1f + margin)) * Mathf.Rad2Deg;
        }
    }
}
