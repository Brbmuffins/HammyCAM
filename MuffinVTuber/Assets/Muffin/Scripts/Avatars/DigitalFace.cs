using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    public enum Emotion { Neutral, Happy, Sad, Angry, Surprised }

    /// <summary>Cartoon stickers, or your real eyes and mouth from the camera (V toggles).</summary>
    public enum FaceStyle { Cartoon, Real }

    /// <summary>
    /// The "digital face": flat cartoon stickers (eyes, brows, mouth, teeth, blush) on any avatar's head.
    /// Each feature is a small mesh reshaped every frame, so expressions are drawn rather than just scaled:
    /// eyes squint into "^ ^" arcs, slant when angry and droop when sad; the mouth curves into smiles and
    /// frowns and opens into "ah", "ooh" and grin shapes with teeth.
    ///
    /// Two drivers mix: the tracked face (blinks, mouth open/wide, smile, brows) and an emotion picked by
    /// hotkey or, later, by the mood layer. Every value runs through a spring, so expressions pop in with a
    /// little overshoot instead of switching.
    /// </summary>
    public sealed class DigitalFace : MonoBehaviour
    {
        [Header("Sticker materials (unlit)")]
        public Material ink;
        public Material shine;
        public Material mouthInside;
        public Material blush;

        [Header("Behaviour")]
        [Tooltip("Blink on its own every few seconds, like a person does.")]
        public bool autoBlink = true;

        [Tooltip("Cartoon talk: the head stretches up as the mouth opens and squashes back.")]
        [Range(0f, 2f)] public float talkBounce = 1f;

        [Header("Real face (your eyes and mouth from the camera)")]
        [Tooltip("Muffin/FaceCutout material; each feature gets its own copy.")]
        public Material cutout;
        [Tooltip("Width of each real eye on the avatar, in face units: bigger than life on purpose.")]
        [Range(0.03f, 0.2f)] public float realEyeWidth = 0.09f;
        [Range(0.03f, 0.25f)] public float realMouthWidth = 0.12f;

        /// <summary>Shared by every avatar, so switching avatars keeps the style.</summary>
        public static FaceStyle Style = FaceStyle.Cartoon;
        /// <summary>How much of your face the real-face windows show (E).</summary>
        public enum RealCrop { Features, Eyes, EyesAndBrows }
        /// <summary>Default: cut along the eye openings and the lips, so only eyeballs and lips show.</summary>
        public static RealCrop Crop = RealCrop.Features;
        public static readonly string[] CropNames = { "just eyeballs and lips", "eyes and mouth", "eyes with brows" };
        /// <summary>How big the eyes and the mouth are (× normal), on both faces: they grow, for fun and drama. Any size
        /// from MinSize to MaxSize (the menu's sliders); Z and O step through the presets in Sizes.</summary>
        public static float EyeSize = 1f, MouthSize = 1f;
        public const float MinSize = 0.6f, MaxSize = 3.2f;
        public static readonly float[] Sizes = { 1f, 1.45f, 1.9f, 2.5f };
        /// <summary>The preset nearest the eye size (setting one picks that preset's size).</summary>
        public static int EyeLevel { get => Nearest(EyeSize); set => EyeSize = Sizes[Level(value)]; }
        public static int MouthLevel { get => Nearest(MouthSize); set => MouthSize = Sizes[Level(value)]; }
        /// <summary>Which preset a size is exactly (-1 if it's in between).</summary>
        public static int PresetOf(float size) => System.Array.FindIndex(Sizes, s => Mathf.Abs(s - size) < 0.01f);
        static int Nearest(float size)
        {
            int best = 0;
            for (int i = 1; i < Sizes.Length; i++) if (Mathf.Abs(Sizes[i] - size) < Mathf.Abs(Sizes[best] - size)) best = i;
            return best;
        }
        public static readonly string[] SizeNames = { "normal", "big", "huge", "giant" };
        /// <summary>How your real eyes and mouth look in their windows: brightness (-0.5..0.5), contrast and colour
        /// (saturation), 1 = as the camera sees them. Only the cut-outs change, never the rest of the picture.</summary>
        public static float CutBrightness, CutContrast = 1f, CutSaturation = 1f;
        /// <summary>Real-face edges (S): soft, sharp, or sharp with a cartoon outline.</summary>
        public static int EdgeLevel;
        public static readonly string[] EdgeNames = { "soft", "sharp", "outlined" };
        static readonly float[] EdgeFeather = { 0.3f, 0f, 0f };
        static readonly float[] EdgeOutline = { 0f, 0f, 0.07f };

        static int Level(int level) => Mathf.Clamp(level, 0, Sizes.Length - 1);

        public Emotion Emotion { get; private set; }
        /// <summary>The face's own transform on the avatar's head (the dramatic zoom aims at it).</summary>
        public Transform FaceAnchor { get; private set; }

        /// <summary>
        /// The streamer's own placement of this avatar's face (menu → Face), remembered per avatar: the whole face
        /// moved (face units, about 26 cm) and resized, the eyes raised and spread, the mouth raised.
        /// </summary>
        public struct Placement
        {
            public float X, Y, Size, EyesUp, EyesApart, MouthUp;
            public static Placement Default => new Placement { Size = 1f, EyesApart = 1f };
        }
        public Placement Fit { get; private set; } = Placement.Default;
        Vector3 anchorHome;
        float anchorScale = 1f;

        string FitKey => "muffin.facefit." + gameObject.name;

        /// <summary>Move, resize or nudge the face's parts on this avatar, and remember it.</summary>
        public void SetFit(Placement fit)
        {
            fit.X = Mathf.Clamp(fit.X, -0.15f, 0.15f);
            fit.Y = Mathf.Clamp(fit.Y, -0.15f, 0.15f);
            fit.Size = Mathf.Clamp(fit.Size, 0.6f, 1.6f);
            fit.EyesUp = Mathf.Clamp(fit.EyesUp, -0.03f, 0.03f);
            fit.EyesApart = Mathf.Clamp(fit.EyesApart, 0.7f, 1.4f);
            fit.MouthUp = Mathf.Clamp(fit.MouthUp, -0.04f, 0.03f);
            Fit = fit;
            PlayerPrefs.SetString(FitKey, string.Join(",", new[] { fit.X, fit.Y, fit.Size, fit.EyesUp, fit.EyesApart, fit.MouthUp }
                .Select(v => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))));
            if (FaceAnchor)
            {
                FaceAnchor.localPosition = anchorHome + new Vector3(fit.X, fit.Y, 0f) * anchorScale;
                FaceAnchor.localScale = Vector3.one * anchorScale * fit.Size;
            }
        }

        void LoadFit()
        {
            string saved = PlayerPrefs.GetString(FitKey, "");
            Placement fit = Placement.Default;
            string[] parts = saved.Split(',');
            if (parts.Length == 6)
            {
                float F(int i) => float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
                fit = new Placement { X = F(0), Y = F(1), Size = F(2), EyesUp = F(3), EyesApart = F(4), MouthUp = F(5) };
            }
            SetFit(fit);
        }

        /// <summary>The avatar's front around the face (MeshAvatar makes it): the face parts follow it.</summary>
        public FrontSurface Surface { get; set; }
        [Tooltip("Gap between the avatar's surface and the face parts (metres): enough that they never cut into it or flicker behind it.")]
        public float surfaceGap = 0.006f;
        readonly List<Sticker> stickers = new List<Sticker>();
        SkinnedMeshRenderer skin;
        Toppings toppings;
        Mesh posedMesh;
        readonly List<Vector3> posedCorners = new List<Vector3>();
        Vector3[] faceCorners;
        float nextSurface;

        // Layout in face units (1 = the sunflower's 26 cm face); the whole face is scaled per avatar.
        const float EyeX = 0.046f, EyeY = 0.024f, EyeRx = 0.018f, EyeRy = 0.025f;
        const float BrowY = 0.062f, BrowLength = 0.022f, BrowThickness = 0.0075f;
        const float MouthY = -0.036f, MouthHalfWidth = 0.033f, Lip = 0.0075f;

        BendRig rig;
        Sticker leftEye, rightEye, mouth, teeth;
        Transform leftShine, rightShine, leftBrow, rightBrow, leftBlush, rightBlush;
        Transform tear, sweat; // the little signs that say the emotion (sad, surprised)
        Transform[] steam; // angry: puffs of steam off the head
        Cutout realLeftEye, realRightEye, realMouth; // left/right as seen on screen
        float nextBlink, blinkUntil;
        float lastOpen;
        bool showingReal;
        float inViewFor, outOfViewFor;

        // The steadied face frame behind the oval windows (see UpdateRealFace).
        readonly OneEuroVector3 originFilter = new OneEuroVector3(2.5f, 0.02f);
        readonly OneEuroVector3 angleFilter = new OneEuroVector3(2f, 0.3f);
        bool framePrimed;
        float frameScale, eyeWidthL, mouthWidthL, mouthOpenL;
        Vector2 leftEyeL, rightEyeL, mouthL;

        /// <summary>Where one real feature is in the camera picture, worked out once per tracker update.</summary>
        sealed class Window
        {
            public Vector2 Centre;  // UV
            public Vector2 Size;    // pixels; y is negative for upside-down cameras
            public float Angle;     // radians
            public readonly Vector4[] Poly = new Vector4[16]; // outline, 2 points per entry, window units (x times Aspect)
            public int Count;       // 0 = oval
            public float Aspect = 1f;
        }
        readonly Window leftWindow = new Window(), rightWindow = new Window(), mouthWindow = new Window(); // image left/right
        Vector2[] windowsFrom;
        float windowsAt;
        readonly Vector2[] outline = new Vector2[32], corners = new Vector2[32];

        // Feature size: springy, so a new size lands with a boing.
        readonly SpringFloat eyeSizeSpring = new SpringFloat(4.5f, 0.32f, 1f, 1f);
        readonly SpringFloat mouthSizeSpring = new SpringFloat(4.5f, 0.32f, 1f, 1f);

        readonly SpringFloat blinkSpring = new SpringFloat(14f, 1f, 1f, 0f);
        // Bouncy on purpose: the mouth overshoots as it opens and shuts, so talking reads big and alive.
        readonly SpringFloat openSpring = new SpringFloat(13f, 0.35f, 1f, 0f);
        readonly SpringFloat smileSpring = new SpringFloat(7f, 0.45f, 1f, 0f);
        readonly SpringFloat smirkSpring = new SpringFloat(7f, 0.5f, 1f, 0f);
        readonly SpringFloat browSpring = new SpringFloat(7f, 0.55f, 1f, 0f);
        readonly SpringFloat wideSpring = new SpringFloat(8f, 0.7f, 1f, 0f);
        // One spring per emotion, slightly under-damped so a new expression overshoots and settles.
        readonly SpringFloat[] emotion =
        {
            new SpringFloat(5f, 0.5f, 1f, 1f), new SpringFloat(5f, 0.5f, 1f, 0f), new SpringFloat(5f, 0.5f, 1f, 0f),
            new SpringFloat(5f, 0.5f, 1f, 0f), new SpringFloat(5f, 0.5f, 1f, 0f),
        };

        readonly float[] weights = new float[5];

        /// <summary>The real-face windows as (name, centre UV, size px, image size px, angle, mirrored), for diagnostics.</summary>
        public System.Collections.Generic.IEnumerable<(string name, Vector2 centre, Vector2 size, Vector2 image, float angle, bool mirror)> RealFaceWindows()
        {
            foreach (var (name, cut) in new[] { ("left-eye", realLeftEye), ("right-eye", realRightEye), ("mouth", realMouth) })
                if (cut != null && cut.Describe(out Vector2 c, out Vector2 s, out Vector2 img, out float a, out bool m))
                    yield return (name, c, s, img, a, m);
        }

        public void SetEmotion(Emotion e) => Emotion = e;

        /// <summary>
        /// Attach the face to `anchor` (the rig's head bone) at `centre` in the anchor's space. `scale` sizes
        /// it for the avatar; `withMouth` false for avatars that have their own, like a duck's beak.
        /// </summary>
        public void Build(BendRig source, Transform anchor, Vector3 centre, float scale = 1f, bool withMouth = true)
        {
            rig = source;
            var face = new GameObject("Digital Face").transform;
            face.SetParent(anchor, false);
            face.localPosition = centre;
            face.localScale = Vector3.one * scale;
            FaceAnchor = face;
            anchorHome = centre;
            anchorScale = scale;

            leftBlush = Flat("Blush L", face, new Vector3(-0.074f, -0.012f, 0.002f), blush, 0.02f, 0.011f);
            rightBlush = Flat("Blush R", face, new Vector3(0.074f, -0.012f, 0.002f), blush, 0.02f, 0.011f);
            leftEye = Keep(new Sticker("Eye L", face, new Vector3(-EyeX, EyeY, 0f), ink));
            rightEye = Keep(new Sticker("Eye R", face, new Vector3(EyeX, EyeY, 0f), ink));
            leftShine = Flat("Shine L", leftEye.Transform, new Vector3(-0.006f, 0.008f, -0.0015f), shine, 0.0055f, 0.0055f);
            rightShine = Flat("Shine R", rightEye.Transform, new Vector3(-0.006f, 0.008f, -0.0015f), shine, 0.0055f, 0.0055f);
            leftBrow = Flat("Brow L", face, new Vector3(-EyeX, BrowY, 0f), ink, BrowLength, BrowThickness);
            rightBrow = Flat("Brow R", face, new Vector3(EyeX, BrowY, 0f), ink, BrowLength, BrowThickness);
            if (withMouth)
            {
                mouth = Keep(new Sticker("Mouth", face, new Vector3(0f, MouthY, 0f), mouthInside));
                teeth = Keep(new Sticker("Teeth", face, new Vector3(0f, MouthY, -0.001f), shine));
            }
            if (cutout)
            {
                realLeftEye = new Cutout("Real Eye L", face, new Vector3(-EyeX * 1.1f, EyeY + 0.008f, -0.002f), cutout, realEyeWidth);
                realRightEye = new Cutout("Real Eye R", face, new Vector3(EyeX * 1.1f, EyeY + 0.008f, -0.002f), cutout, realEyeWidth);
                if (withMouth) realMouth = new Cutout("Real Mouth", face, new Vector3(0f, MouthY - 0.006f, -0.002f), cutout, realMouthWidth);
            }
            // Signs, small like a stop-motion or South Park face: a tear, a sweat drop, steam off the head.
            var water = new Material(shine);
            water.SetColor("_BaseColor", new Color(0.55f, 0.82f, 1f));
            var vapour = new Material(shine);
            vapour.SetColor("_BaseColor", new Color(0.93f, 0.93f, 0.96f));
            tear = Flat("Tear", face, new Vector3(-EyeX, EyeY - 0.035f, -0.002f), water, 0.006f, 0.009f);
            sweat = Flat("Sweat drop", face, new Vector3(EyeX * 2f, EyeY + 0.03f, -0.002f), water, 0.007f, 0.011f);
            steam = new Transform[4];
            for (int k = 0; k < steam.Length; k++)
            {
                steam[k] = Flat("Steam", face, Vector3.zero, vapour, 0.017f, 0.013f);
                steam[k].gameObject.SetActive(false);
            }
            tear.gameObject.SetActive(false);
            sweat.gameObject.SetActive(false);
            LoadFit();
            nextBlink = Time.time + Random.Range(2f, 4f);
        }

        /// <summary>
        /// Real-face mode: frame your eyes and mouth in the camera image from the face landmarks.
        /// Returns false when there's no face or no camera, so the cartoon face shows instead.
        ///
        /// In step with the tracker: the windows are worked out once per tracker update, and cut from the very
        /// camera frame those face points were measured on (WebcamHeadPoseSource.PointsImage). Cut from
        /// the live picture instead, they'd sit where your eyes were a moment ago and show cheek and forehead
        /// whenever you move.
        ///
        /// Default crop (E): along each eye opening and the lips' outline, so only eyeballs and lips show.
        /// The oval crops hang off a lightly steadied "face frame" from the four eye corners.
        /// </summary>
        bool UpdateRealFace(float dt)
        {
            var tracker = WebcamHeadPoseSource.Instance;
            var feed = CameraFeed.Instance;
            if (Style != FaceStyle.Real || realLeftEye == null || tracker == null || feed == null
                || !tracker.HasFace || !feed.Playing || tracker.Points == null)
            {
                framePrimed = false;
                windowsFrom = null;
                return false;
            }

            Vector2[] p = tracker.Points;
            Vector2 size = tracker.ImageSize.x > 1f ? tracker.ImageSize : new Vector2(feed.trackWidth, feed.trackHeight);

            // A face cut off by the edge of the picture (eyes above the top, half a face at the side) makes
            // a broken crop, so the cartoon face stands in until the whole face is back in view. A short
            // delay each way stops it flickering when you sit right at the edge.
            bool inView = FaceInView(p, size, tracker.Layout);
            inViewFor = inView ? inViewFor + dt : 0f;
            outOfViewFor = inView ? 0f : outOfViewFor + dt;
            if (showingReal && outOfViewFor > 0.25f) showingReal = false;
            if (!showingReal && inViewFor > 0.3f) showingReal = true;
            if (!showingReal)
            {
                framePrimed = false;
                windowsFrom = null;
                return false;
            }

            FaceLayout layout = tracker.Layout;
            if (p != windowsFrom)
            {
                float since = windowsFrom == null ? 1f / 30f : Mathf.Max(1e-3f, Time.unscaledTime - windowsAt);
                windowsFrom = p;
                windowsAt = Time.unscaledTime;
                if (Crop == RealCrop.Features) FeatureWindows(p, size, feed.VerticallyMirrored, layout);
                else OvalWindows(p, size, feed.VerticallyMirrored, since, layout);
            }

            // Mirror view: the screen's left shows the image's right (your left eye), flipped like a mirror.
            Texture image = tracker.PointsImage;
            bool mirror = tracker.mirror;
            bool features = Crop == RealCrop.Features;
            // Eyeballs and lips are cut tight, so they're shown bigger, and the eyes taller: cartoon-round.
            // Their proportions are kept near a natural shape as your head turns (Face Object Stream's limits).
            float eyeWidthScale = features ? 1.3f : 1f, eyeStretch = features ? 1.4f : 1f;
            float eyeAspect = features ? 1.71f : 0f, mouthAspect = features ? 1.82f : 0f;
            realLeftEye.Track(image, mirror ? rightWindow : leftWindow, size, mirror, eyeWidthScale, eyeStretch, eyeAspect);
            realRightEye.Track(image, mirror ? leftWindow : rightWindow, size, mirror, eyeWidthScale, eyeStretch, eyeAspect);
            if (realMouth != null)
            {
                realMouth.Track(image, mouthWindow, size, mirror, features ? 1.25f : 1f, features ? 1.2f : 1f, mouthAspect);
                // Exaggerate the real mouth too: it stretches taller as it opens.
                realMouth.Stretch(lastOpen);
            }
            return true;
        }

        /// <summary>Bigger sizes also crop tighter, so they zoom into the feature instead of just enlarging it.</summary>
        static float Zoom(float size) => Mathf.Lerp(1f, 0.82f, Mathf.InverseLerp(1f, Sizes[Sizes.Length - 1], size));

        /// <summary>
        /// Windows cut along each eye opening and the lips' outline (whichever tracker's points: 6 an eye from
        /// OpenSeeFace, 16 from MediaPipe): straight from this update's points, since the picture they're cut
        /// from is the same frame.
        /// </summary>
        void FeatureWindows(Vector2[] p, Vector2 size, bool upsideDown, FaceLayout l)
        {
            float upSign = upsideDown ? -1f : 1f;
            Vector2 Up(Vector2 a) => new Vector2(a.x, upsideDown ? a.y : size.y - a.y);
            Vector2 eyeLine = Up(p[l.OuterRight]) - Up(p[l.OuterLeft]);
            float angle = Mathf.Atan2(eyeLine.y, eyeLine.x);
            float eyeWidth = ((Up(p[l.InnerLeft]) - Up(p[l.OuterLeft])).magnitude + (Up(p[l.OuterRight]) - Up(p[l.InnerRight])).magnitude) * 0.5f;
            // The eye further left in the picture goes in leftWindow, whatever the tracker calls it.
            bool swap = MeanX(p, l.LeftEye) > MeanX(p, l.RightEye);
            Outline(leftWindow, p, swap ? l.RightEye : l.LeftEye, true, l, eyeWidth, angle, size, upSign, Up, Zoom(EyeSize));
            Outline(rightWindow, p, swap ? l.LeftEye : l.RightEye, true, l, eyeWidth, angle, size, upSign, Up, Zoom(EyeSize));
            Outline(mouthWindow, p, l.Mouth, false, l, eyeWidth, angle, size, upSign, Up, Zoom(MouthSize));
        }

        static float MeanX(Vector2[] p, int[] indices)
        {
            float sum = 0f;
            foreach (int i in indices) sum += p[i].x;
            return sum / indices.Length;
        }

        void Outline(Window w, Vector2[] p, int[] indices, bool eye, FaceLayout l, float eyeWidth, float angle, Vector2 size,
            float upSign, System.Func<Vector2, Vector2> up, float zoom)
        {
            // The feature's points, in pixels, around their middle and turned level with the eyes (the
            // counter-rotation: the avatar's head gets your tilt, so the picture mustn't carry it too).
            int count = Mathf.Min(indices.Length, corners.Length);
            Vector2 middle = Vector2.zero;
            for (int k = 0; k < count; k++) middle += up(p[indices[k]]);
            middle /= count;
            for (int k = 0; k < count; k++)
            {
                Vector2 q = Rotate(up(p[indices[k]]) - middle, -angle);
                if (eye)
                {
                    // A little room for the lids and lashes (more for a coarse outline), so a shut eye keeps a
                    // lash line instead of vanishing. Points above the corner line are upper lid.
                    q.x *= l.EyeExpandX;
                    q.y *= l.EyeExpandY;
                    float taper = Mathf.Clamp01(Mathf.Abs(q.y) / (0.08f * eyeWidth));
                    if (q.y * upSign > 0f) q.y += upSign * eyeWidth * l.UpperLidPad * taper;
                    else q.y -= upSign * eyeWidth * l.LowerLidPad * taper;
                }
                else q *= l.MouthExpand;
                corners[k] = q * zoom;
            }

            // A smooth closed curve through them (Catmull-Rom): coarse outlines get extra points.
            int per = Mathf.Max(1, 24 / count), n = Mathf.Min(per * count, outline.Length);
            for (int k = 0; k < count; k++)
            {
                Vector2 a = corners[(k - 1 + count) % count], b = corners[k], c = corners[(k + 1) % count], d = corners[(k + 2) % count];
                for (int s = 0; s < per && k * per + s < n; s++)
                {
                    float t = s / (float)per, t2 = t * t, t3 = t2 * t;
                    outline[k * per + s] = 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3);
                }
            }
            if (n % 2 == 1) outline[n++] = outline[0]; // the shader takes points in pairs

            Vector2 min = outline[0], max = outline[0];
            for (int k = 1; k < n; k++)
            {
                min = Vector2.Min(min, outline[k]);
                max = Vector2.Max(max, outline[k]);
            }
            Vector2 half = (max - min) * 0.5f * 1.06f;
            half.y = Mathf.Max(half.y, half.x * 0.22f);
            Vector2 mid = (min + max) * 0.5f;
            Vector2 centre = middle + Rotate(mid, angle);

            w.Centre = new Vector2(centre.x / size.x, centre.y / size.y);
            w.Size = new Vector2(half.x * 2f, half.y * 2f * upSign);
            w.Angle = angle;
            w.Aspect = half.x / half.y;
            w.Count = n;
            for (int k = 0; k < n; k += 2)
            {
                Vector2 a = Window01(outline[k]), b = Window01(outline[k + 1]);
                w.Poly[k / 2] = new Vector4(a.x, a.y, b.x, b.y);
            }

            // Window units, x times the aspect, so the shader measures edges in window heights.
            Vector2 Window01(Vector2 q) => new Vector2((q.x - mid.x) / (half.x * 2f) * w.Aspect, (q.y - mid.y) / (half.y * 2f));
        }

        /// <summary>
        /// Oval windows (E: "eyes and mouth", "eyes with brows"), hung off a lightly steadied face frame
        /// from the four eye corners, which don't move when you blink. Run once per tracker update.
        /// </summary>
        void OvalWindows(Vector2[] p, Vector2 size, bool upsideDown, float dt, FaceLayout l)
        {
            float upSign = upsideDown ? -1f : 1f;
            Vector2 Up(Vector2 a) => new Vector2(a.x, upsideDown ? a.y : size.y - a.y);
            Vector2 Mean(int[] indices)
            {
                Vector2 m = Vector2.zero;
                foreach (int i in indices) m += Up(p[i]);
                return m / indices.Length;
            }
            float Rate(float hz) => 1f - Mathf.Exp(-dt * 2f * Mathf.PI * hz);

            // This update's face frame and features, in face units (1 = outer eye corner to outer eye corner).
            Vector2 l0 = Up(p[l.OuterLeft]), l1 = Up(p[l.InnerLeft]), r0 = Up(p[l.InnerRight]), r1 = Up(p[l.OuterRight]);
            Vector2 origin = (l0 + l1 + r0 + r1) * 0.25f;
            Vector2 eyeLine = r1 - l0;
            float scale = Mathf.Max(1f, eyeLine.magnitude);
            float angle = Mathf.Atan2(eyeLine.y, eyeLine.x);
            Vector2 Local(Vector2 q) => Rotate(q - origin, -angle) / scale;

            bool brows = Crop == RealCrop.EyesAndBrows;
            float eyeWidth = ((l1 - l0).magnitude + (r1 - r0).magnitude) * 0.5f / scale;
            Vector2 leftEye = Local((l0 + l1) * 0.5f), rightEye = Local((r0 + r1) * 0.5f);
            if (brows)
            {
                // Eyes with brows: the window reaches up so raised or furrowed brows come along.
                leftEye = Vector2.Lerp(leftEye, Local(Mean(l.LeftBrow)), 0.35f);
                rightEye = Vector2.Lerp(rightEye, Local(Mean(l.RightBrow)), 0.35f);
            }
            else
            {
                // Just the eyes: the lids arc higher above the corner line than below it.
                leftEye.y += upSign * eyeWidth * 0.08f;
                rightEye.y += upSign * eyeWidth * 0.08f;
            }
            Vector2 mouth = Local(Mean(l.Mouth));
            float mouthWidth = (Up(p[l.MouthRight]) - Up(p[l.MouthLeft])).magnitude / scale;
            float mouthOpen = (Up(p[l.LipTop]) - Up(p[l.LipBottom])).magnitude / scale;

            if (!framePrimed)
            {
                originFilter.Reset();
                angleFilter.Reset();
                frameScale = scale;
                leftEyeL = leftEye;
                rightEyeL = rightEye;
                eyeWidthL = eyeWidth;
                mouthL = mouth;
                mouthWidthL = mouthWidth;
                mouthOpenL = mouthOpen;
                framePrimed = true;
            }
            // Light: the picture is the same frame as the points, so the window needn't trail them.
            originFilter.Beta = 3f / frameScale; // per face-width per second, whatever your distance
            Vector2 o = originFilter.Filter(origin, dt);
            float a = angleFilter.Filter(new Vector3(angle, 0f, 0f), dt).x;
            frameScale = Mathf.Lerp(frameScale, scale, Rate(2f));
            leftEyeL = Vector2.Lerp(leftEyeL, leftEye, Rate(8f));
            rightEyeL = Vector2.Lerp(rightEyeL, rightEye, Rate(8f));
            eyeWidthL = Mathf.Lerp(eyeWidthL, eyeWidth, Rate(2f));
            mouthL = Vector2.Lerp(mouthL, mouth, Rate(10f));
            mouthWidthL = Mathf.Lerp(mouthWidthL, mouthWidth, Rate(8f));
            mouthOpenL = Mathf.Lerp(mouthOpenL, mouthOpen, Rate(14f));

            Vector2 ToUV(Vector2 local)
            {
                Vector2 q = o + Rotate(local * frameScale, a);
                return new Vector2(q.x / size.x, q.y / size.y);
            }
            void Set(Window w, Vector2 centre, Vector2 px)
            {
                w.Centre = ToUV(centre);
                w.Size = px;
                w.Angle = a;
                w.Count = 0;
                w.Aspect = Mathf.Abs(px.x / Mathf.Max(1e-3f, Mathf.Abs(px.y)));
            }

            float eyeW = eyeWidthL * frameScale * (brows ? 2.1f : 1.7f) * Zoom(EyeSize);
            var eyeSize = new Vector2(eyeW, eyeW * (brows ? 0.8f : 0.62f) * upSign);
            Set(leftWindow, leftEyeL, eyeSize);
            Set(rightWindow, rightEyeL, eyeSize);
            float mouthW = mouthWidthL * frameScale * 1.45f * Zoom(MouthSize);
            float mouthH = Mathf.Max(mouthOpenL * frameScale * 1.7f * Zoom(MouthSize), mouthW * 0.5f);
            Set(mouthWindow, mouthL, new Vector2(mouthW, mouthH * upSign));
        }

        static Vector2 Rotate(Vector2 v, float angle)
        {
            float s = Mathf.Sin(angle), c = Mathf.Cos(angle);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>Eyes and mouth inside the picture, with a little room around them.</summary>
        static bool FaceInView(Vector2[] p, Vector2 size, FaceLayout l)
        {
            float margin = (p[l.InnerLeft] - p[l.OuterLeft]).magnitude * 0.2f;
            foreach (int[] feature in new[] { l.LeftEye, l.RightEye, l.Mouth })
                foreach (int i in feature)
                    if (p[i].x < margin || p[i].y < margin || p[i].x > size.x - margin || p[i].y > size.y - margin) return false;
            return true;
        }

        void LateUpdate()
        {
            if (rig == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            HeadPose pose = rig.Current;

            // Feature size (Z) and the dramatic zoom (hold X): eyes and mouth spring to their new size with a
            // boing, spreading apart as they grow so they don't pile on top of each other.
            float punch = 1f + 0.5f * Mathf.Max(0f, StageCamera.Punch);
            float grow = Mathf.Max(0.3f, eyeSizeSpring.Update(EyeSize * punch, dt));
            float mouthGrow = Mathf.Max(0.3f, mouthSizeSpring.Update(MouthSize * punch, dt));
            float spread = 1f + (grow - 1f) * 0.5f;
            float lift = (grow - 1f) * 0.012f; // eyes move up as they grow, and the mouth down out of their way
            float drop = lift * 0.7f + (mouthGrow - 1f) * 0.026f;
            Vector3 EyeSpot(Vector3 home) => new Vector3(home.x * spread * Fit.EyesApart, home.y + lift + Fit.EyesUp, home.z);
            Vector3 MouthSpot(Vector3 home) => new Vector3(home.x, home.y - drop + Fit.MouthUp, home.z);
            foreach (Sticker eye in new[] { leftEye, rightEye })
            {
                eye.Transform.localPosition = EyeSpot(eye.Home);
                eye.Transform.localScale = Vector3.one * grow;
            }
            if (mouth != null)
            {
                foreach (Sticker part in new[] { mouth, teeth })
                {
                    part.Transform.localPosition = MouthSpot(part.Home);
                    part.Transform.localScale = Vector3.one * mouthGrow;
                }
            }
            foreach (Cutout cut in new[] { realLeftEye, realRightEye })
            {
                if (cut == null) continue;
                cut.Size = grow;
                cut.Transform.localPosition = EyeSpot(cut.Home);
            }
            if (realMouth != null)
            {
                realMouth.Size = mouthGrow;
                realMouth.Transform.localPosition = MouthSpot(realMouth.Home);
            }

            // Real face when the camera can see you; the cartoon face otherwise.
            bool real = UpdateRealFace(dt);
            realLeftEye?.SetVisible(real);
            realRightEye?.SetVisible(real);
            realMouth?.SetVisible(real);
            leftEye.Transform.gameObject.SetActive(!real);
            rightEye.Transform.gameObject.SetActive(!real);
            leftBrow.gameObject.SetActive(!real);
            rightBrow.gameObject.SetActive(!real);
            if (mouth != null) mouth.Transform.gameObject.SetActive(!real);

            // Emotion weights (0..1, springy).
            float[] w = weights;
            for (int i = 0; i < 5; i++) w[i] = Mathf.Clamp(emotion[i].Update((int)Emotion == i ? 1f : 0f, dt), -0.2f, 1.2f);
            float happy = w[1], sad = w[2], angry = w[3], surprised = w[4];

            // Blinks: tracked, plus natural automatic ones.
            float blinkTarget = Mathf.Max(pose.BlinkLeft, pose.BlinkRight);
            if (autoBlink)
            {
                if (Time.time >= nextBlink)
                {
                    blinkUntil = Time.time + 0.11f;
                    nextBlink = Time.time + Random.Range(2.5f, 5.5f);
                }
                if (Time.time < blinkUntil) blinkTarget = 1f;
            }
            float blink = Mathf.Clamp01(blinkSpring.Update(blinkTarget, dt));

            // Tracked expression mixed with the emotion. The picked emotions (F1-F4) stay small, the way stop-motion
            // and South Park faces do it: the eyes never change, the brows tilt a little, the mouth curves a little,
            // and a little sign says the rest (blush, a tear, steam, a sweat drop).
            float trackedSmile = smileSpring.Update(pose.Smile, dt);
            float smile = Mathf.Clamp(trackedSmile * 1.3f + happy * 0.55f - sad * 0.5f - angry * 0.3f, -1.3f, 1.3f);
            float smirk = Mathf.Clamp(smirkSpring.Update(pose.Smirk, dt) * 1.3f, -1f, 1f);
            float brow = Mathf.Clamp(browSpring.Update(pose.BrowRaise, dt) + surprised * 0.55f + sad * 0.1f - angry * 0.3f, -1f, 1.2f);
            float browSlant = sad * 0.8f - angry * 0.9f; // + inner ends up (worried), - inner ends down (cross)
            // Small real openings are boosted (1 - (1-x)^2) so even quiet talking reads; the bouncy spring
            // can overshoot past 1 a little, which is the cartoon "overcompensation".
            // A small dead zone first, so a closed mouth's tracker noise doesn't flap it.
            float mouthIn = Mathf.Clamp01((pose.MouthOpen - 0.06f) / 0.94f);
            float boosted = 1f - (1f - mouthIn) * (1f - mouthIn);
            float open = Mathf.Clamp(openSpring.Update(boosted, dt) + surprised * 0.3f, 0f, 1.25f);
            lastOpen = open;
            // The whole head stretches with the mouth (the face and real cut-outs ride along): cartoon talking.
            float talk = open * talkBounce;
            rig.HeadScale = new Vector3(1f - talk * 0.05f, 1f + talk * 0.1f, 1f - talk * 0.05f);
            float wide = Mathf.Clamp(wideSpring.Update(pose.MouthWide, dt) * 1.4f - surprised * 0.5f, -1f, 1f);
            float squint = Mathf.Clamp01(Mathf.Max(0f, trackedSmile - 0.55f) * 2f); // only your own big smile closes the eyes into arcs
            float eyeSize = 1f + surprised * 0.08f;
            float lid = Mathf.Clamp01(blink);
            float lidSlant = 0f;
            float blushAmount = Mathf.Clamp01(happy * 0.75f + Mathf.Max(0f, trackedSmile - 0.5f) * 1.5f);
            Signs(sad, angry, surprised, spread, grow);

            leftEye.Eye(EyeRx * eyeSize, EyeRy * eyeSize, lid, lidSlant, squint, innerSide: +1f);
            rightEye.Eye(EyeRx * eyeSize, EyeRy * eyeSize, lid, lidSlant, squint, innerSide: -1f);
            bool shineOn = lid < 0.5f && squint < 0.5f;
            leftShine.gameObject.SetActive(shineOn);
            rightShine.gameObject.SetActive(shineOn);

            float browRaise = brow * 0.016f;
            float browAngle = browSlant * 16f;
            float browY = EyeY + lift + Fit.EyesUp + (BrowY - EyeY + browRaise) * grow;
            leftBrow.localPosition = new Vector3(-EyeX * spread * Fit.EyesApart, browY, 0f);
            rightBrow.localPosition = new Vector3(EyeX * spread * Fit.EyesApart, browY, 0f);
            leftBrow.localScale = rightBrow.localScale = Vector3.one * grow;
            leftBrow.localRotation = Quaternion.Euler(0f, 0f, browAngle);   // inner end is on its right
            rightBrow.localRotation = Quaternion.Euler(0f, 0f, -browAngle); // inner end is on its left

            leftBlush.gameObject.SetActive(blushAmount > 0.03f);
            rightBlush.gameObject.SetActive(blushAmount > 0.03f);
            leftBlush.localScale = rightBlush.localScale = Vector3.one * (blushAmount * Mathf.Sqrt(grow));
            leftBlush.localPosition = new Vector3(-0.074f * spread, -0.012f, 0.002f);
            rightBlush.localPosition = new Vector3(0.074f * spread, -0.012f, 0.002f);

            if (mouth != null)
            {
                bool showTeeth = open > 0.15f && smile > 0.4f;
                mouth.Mouth(MouthHalfWidth, smile, smirk, open, wide, Lip, out float teethHalfWidth);
                teeth.Transform.gameObject.SetActive(showTeeth && !real);
                if (showTeeth) teeth.Teeth(mouth, teethHalfWidth);
            }
            ConformToSurface();
        }

        /// <summary>
        /// The little signs: sad, a tear wells under an eye and runs down, again and again; angry, puffs of steam
        /// rise off the sides of the head; surprised, a sweat drop slides down the side of the head. Shown with
        /// either face.
        /// </summary>
        void Signs(float sad, float angry, float surprised, float spread, float grow)
        {
            float t = Time.time;
            tear.gameObject.SetActive(sad > 0.3f);
            if (sad > 0.3f)
            {
                float k = Mathf.Repeat(t / 2.4f, 1f);
                tear.localPosition = new Vector3(-EyeX * spread, EyeY - (0.03f + 0.04f * k * k) * grow, -0.002f);
                tear.localScale = Vector3.one * Mathf.Sqrt(grow) * Mathf.Min(1f, k * 5f) * (1f - Mathf.SmoothStep(0.8f, 1f, k));
            }
            for (int k = 0; k < steam.Length; k++)
            {
                bool fuming = angry > 0.3f;
                steam[k].gameObject.SetActive(fuming);
                if (!fuming) continue;
                // Two puffs a side, one after the other: each rises and drifts out, swelling, then shrinks away.
                float side = k % 2 == 0 ? -1f : 1f;
                float phase = Mathf.Repeat(t / 1.1f + k * 0.27f, 1f);
                steam[k].localPosition = new Vector3(side * (EyeX * 1.7f + 0.014f * phase) * spread, BrowY + (0.012f + 0.05f * phase) * grow, -0.002f);
                steam[k].localScale = Vector3.one * Mathf.Sqrt(grow) * Mathf.Min(1f, angry) * Mathf.Sin(phase * Mathf.PI) * (0.7f + 0.6f * phase);
            }
            sweat.gameObject.SetActive(surprised > 0.3f);
            if (surprised > 0.3f)
            {
                float k = Mathf.Repeat(t / 3f, 1f);
                sweat.localPosition = new Vector3(EyeX * 2f * spread, EyeY + (0.03f - 0.03f * k) * grow, -0.002f);
                sweat.localScale = Vector3.one * Mathf.Sqrt(grow) * Mathf.Min(1f, k * 6f) * (1f - Mathf.SmoothStep(0.85f, 1f, k));
            }
        }

        /// <summary>A static flat ellipse (brows, blush, eye shine).</summary>
        Transform Flat(string name, Transform parent, Vector3 position, Material material, float rx, float ry)
        {
            var sticker = Keep(new Sticker(name, parent, position, material));
            sticker.Ellipse(rx, ry);
            return sticker.Transform;
        }

        Sticker Keep(Sticker sticker)
        {
            stickers.Add(sticker);
            return sticker;
        }

        /// <summary>
        /// Every face part's corners onto the avatar's front, `surfaceGap` in front of the surface under each one
        /// (bumps included): big eyes wrap round a curved front, nothing sinks into the avatar or flickers behind it.
        /// After everything has been placed and shaped this frame.
        /// </summary>
        void ConformToSurface()
        {
            if (Surface == null || !FaceAnchor) return;
            RefreshSurface();
            float gap = surfaceGap / Mathf.Max(1e-4f, FaceAnchor.lossyScale.x);
            Matrix4x4 faceFromWorld = FaceAnchor.worldToLocalMatrix;
            foreach (Sticker s in stickers)
                if (s.Transform.gameObject.activeInHierarchy) s.Conform(faceFromWorld, Surface, gap);
            foreach (Cutout c in new[] { realLeftEye, realRightEye, realMouth })
                if (c != null && c.Transform.gameObject.activeInHierarchy) c.Conform(faceFromWorld, Surface, gap);
        }

        /// <summary>
        /// The surface map from the avatar as it's posed right now (30 times a second): it bends and turns under the
        /// face, and a pepperoni slice moves with it, so a map made at rest would let them poke through.
        /// </summary>
        void RefreshSurface()
        {
            if (Time.time < nextSurface) return;
            nextSurface = Time.time + 1f / 30f;
            if (!skin && !TryGetComponent(out skin)) return;
            if (!posedMesh) posedMesh = new Mesh();
            skin.BakeMesh(posedMesh, true);
            posedMesh.GetVertices(posedCorners);
            if (faceCorners == null || faceCorners.Length != posedCorners.Count) faceCorners = new Vector3[posedCorners.Count];
            Matrix4x4 toFace = FaceAnchor.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            for (int i = 0; i < faceCorners.Length; i++) faceCorners[i] = toFace.MultiplyPoint3x4(posedCorners[i]);
            Surface.Rasterize(faceCorners);
            if (toppings || TryGetComponent(out toppings)) toppings.AddBumps(Surface, FaceAnchor);
        }

        /// <summary>The nearest surface (smallest depth) anywhere along the line from a to b on the face, sampled `samples` times.</summary>
        static bool Nearest(FrontSurface surface, Vector3 a, Vector3 b, int samples, out float z)
        {
            z = float.PositiveInfinity;
            for (int k = 0; k < samples; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, samples > 1 ? (float)k / (samples - 1) : 0.5f);
                if (surface.TryDepth(p.x, p.y, out float d) && d < z) z = d;
            }
            return !float.IsPositiveInfinity(z);
        }

        /// <summary>Corners from a part's own space to the surface in the face's space, and back.</summary>
        static void OntoSurface(Transform part, Matrix4x4 faceFromWorld, FrontSurface surface, float gap, Vector3[] flat, Vector3[] into)
        {
            Matrix4x4 toFace = faceFromWorld * part.localToWorldMatrix;
            Matrix4x4 fromFace = toFace.inverse;
            for (int i = 0; i < flat.Length; i++)
            {
                Vector3 p = toFace.MultiplyPoint3x4(flat[i]);
                // p.z is the part's own small layer offset (an eye's shine sits just in front of the eye): kept.
                if (surface.TryDepth(p.x, p.y, out float z)) p.z += z - gap;
                into[i] = fromFace.MultiplyPoint3x4(p);
            }
        }

        /// <summary>
        /// One real feature: a soft-edged window (Muffin/FaceCutout) onto the camera image, kept at the
        /// right shape on the avatar and smoothly following the feature's position, size and angle.
        /// </summary>
        sealed class Cutout
        {
            const int Columns = 12, Rows = 8;
            static readonly int MainTexId = Shader.PropertyToID("_MainTex");
            static readonly int CenterId = Shader.PropertyToID("_Center");
            static readonly int SizeId = Shader.PropertyToID("_Size");
            static readonly int ImageSizeId = Shader.PropertyToID("_ImageSize");
            static readonly int AngleId = Shader.PropertyToID("_Angle");
            static readonly int FlipId = Shader.PropertyToID("_FlipX");
            static readonly int FeatherId = Shader.PropertyToID("_Feather");
            static readonly int OutlineId = Shader.PropertyToID("_Outline");
            static readonly int PolyCountId = Shader.PropertyToID("_PolyCount");
            static readonly int AspectId = Shader.PropertyToID("_Aspect");
            static readonly int PolyId = Shader.PropertyToID("_Poly");
            static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
            static readonly int ContrastId = Shader.PropertyToID("_Contrast");
            static readonly int SaturationId = Shader.PropertyToID("_Saturation");
            readonly Mesh mesh;
            readonly Vector3[] flat, shaped;

            public readonly Transform Transform;
            /// <summary>Where it sits on the face at normal feature size.</summary>
            public readonly Vector3 Home;
            /// <summary>Feature size multiplier (Z / dramatic zoom).</summary>
            public float Size = 1f;
            readonly Transform transform;
            readonly Material material;
            readonly float width;
            Vector2 centre, size;
            float angle;
            bool primed;

            public Cutout(string name, Transform parent, Vector3 position, Material template, float width)
            {
                mesh = MakeGrid(out flat);
                shaped = new Vector3[flat.Length];
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = position;
                Home = position;
                Transform = go.transform;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                material = new Material(template);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                transform = go.transform;
                this.width = width;
                go.SetActive(false);
            }

            public void SetVisible(bool visible)
            {
                if (transform.gameObject.activeSelf != visible) transform.gameObject.SetActive(visible);
                if (!visible) primed = false; // jump straight to the face when it reappears
            }

            /// <summary>
            /// Show this window of the camera image: an oval, or cut along the feature's outline. The quad is
            /// widthScale times the usual width and stretchY times taller than the window's own shape. With a
            /// baseAspect (width / height of the feature's natural shape), the quad keeps near that shape as the
            /// crop changes with your head turning: its height follows the crop only within 0.48-1.12 of it.
            /// </summary>
            public void Track(Texture image, Window w, Vector2 imageSize, bool mirror, float widthScale = 1f, float stretchY = 1f,
                float baseAspect = 0f)
            {
                centre = w.Centre;
                size = w.Size;
                angle = w.Angle;
                primed = true;
                material.SetTexture(MainTexId, image);
                material.SetVector(CenterId, centre);
                material.SetVector(SizeId, size);
                material.SetVector(ImageSizeId, imageSize);
                material.SetFloat(AngleId, angle);
                material.SetFloat(FlipId, mirror ? 1f : 0f);
                material.SetFloat(PolyCountId, w.Count);
                material.SetFloat(AspectId, w.Aspect);
                if (w.Count > 0) material.SetVectorArray(PolyId, w.Poly);
                material.SetFloat(BrightnessId, CutBrightness);
                material.SetFloat(ContrastId, CutContrast);
                material.SetFloat(SaturationId, CutSaturation);
                int edge = Mathf.Clamp(EdgeLevel, 0, EdgeFeather.Length - 1);
                material.SetFloat(FeatherId, EdgeFeather[edge]);
                material.SetFloat(OutlineId, EdgeOutline[edge]);
                // Same shape as the framed region, so the feature isn't squashed (size.y is negative for
                // upside-down cameras: that flips the sampling, not the quad).
                float w0 = width * widthScale;
                float cropAspect = Mathf.Max(1f, size.x) / Mathf.Max(1f, Mathf.Abs(size.y));
                float h0 = baseAspect > 0f
                    ? w0 / baseAspect * Mathf.Clamp(baseAspect / cropAspect, 0.48f, 1.12f)
                    : w0 / cropAspect;
                baseScale = new Vector3(w0, h0 * stretchY, 1f) * Size;
                transform.localScale = baseScale;
                lastImage = image;
                lastImageSize = imageSize;
                lastMirror = mirror;
            }

            Texture lastImage;
            Vector2 lastImageSize;
            bool lastMirror;
            Vector3 baseScale = Vector3.one;

            /// <summary>Stretch the window taller (and a little wider) by `amount` (0..~1.25).</summary>
            public void Stretch(float amount) =>
                transform.localScale = new Vector3(baseScale.x * (1f + 0.12f * amount), baseScale.y * (1f + 0.8f * amount), 1f);

            /// <summary>Where this window currently samples the camera image (for the F9 diagnostics).</summary>
            public bool Describe(out Vector2 centreUV, out Vector2 sizePx, out Vector2 imageSize, out float angleRad, out bool mirrored)
            {
                centreUV = centre;
                sizePx = size;
                imageSize = lastImageSize;
                angleRad = angle;
                mirrored = lastMirror;
                return primed && lastImage != null;
            }

            /// <summary>This window laid onto the avatar's surface (see ConformToSurface): each corner clears any bump
            /// within a grid cell of it, so nothing pokes through between corners.</summary>
            public void Conform(Matrix4x4 faceFromWorld, FrontSurface surface, float gap)
            {
                Matrix4x4 toFace = faceFromWorld * transform.localToWorldMatrix;
                Matrix4x4 fromFace = toFace.inverse;
                Vector3 across = toFace.MultiplyVector(new Vector3(1f / Columns, 0f, 0f)), up = toFace.MultiplyVector(new Vector3(0f, 1f / Rows, 0f));
                for (int i = 0; i < flat.Length; i++)
                {
                    Vector3 p = toFace.MultiplyPoint3x4(flat[i]);
                    bool a = Nearest(surface, p - across, p + across, 5, out float za), b = Nearest(surface, p - up, p + up, 5, out float zb);
                    if (a || b) p.z += (a && b ? Mathf.Min(za, zb) : a ? za : zb) - gap;
                    shaped[i] = fromFace.MultiplyPoint3x4(p);
                }
                mesh.vertices = shaped;
                mesh.RecalculateBounds();
            }

            /// <summary>A flat square (-0.5..0.5) cut into a grid, so it can bend round a curved front.</summary>
            static Mesh MakeGrid(out Vector3[] corners)
            {
                var m = new Mesh { name = "Cutout grid" };
                m.MarkDynamic();
                int w = Columns + 1, h = Rows + 1;
                corners = new Vector3[w * h];
                var uv = new Vector2[w * h];
                var normals = new Vector3[w * h];
                for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float u = (float)i / Columns, v = (float)j / Rows;
                    corners[j * w + i] = new Vector3(u - 0.5f, v - 0.5f, 0f);
                    uv[j * w + i] = new Vector2(u, v);
                    normals[j * w + i] = Vector3.back;
                }
                var triangles = new int[Columns * Rows * 6];
                int k = 0;
                for (int j = 0; j < Rows; j++)
                for (int i = 0; i < Columns; i++)
                {
                    int a = j * w + i, b = a + 1, c = a + w, d = c + 1;
                    triangles[k++] = a; triangles[k++] = c; triangles[k++] = b;
                    triangles[k++] = b; triangles[k++] = c; triangles[k++] = d;
                }
                m.vertices = corners;
                m.uv = uv;
                m.normals = normals;
                m.triangles = triangles;
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>
        /// One reshapeable flat sticker: a strip between a top and a bottom edge sampled across its width,
        /// facing the camera (-Z). Reshaping only moves vertices; nothing is allocated per frame.
        /// </summary>
        sealed class Sticker
        {
            const int N = 24;
            public readonly Transform Transform;
            readonly Mesh mesh;
            readonly Vector3[] vertices = new Vector3[(N + 1) * 2];
            readonly Vector3[] shaped = new Vector3[(N + 1) * 2];
            public readonly float[] Top = new float[N + 1], Bottom = new float[N + 1];
            public float HalfWidth { get; private set; }

            /// <summary>Where it sits on the face at normal feature size.</summary>
            public readonly Vector3 Home;

            public Sticker(string name, Transform parent, Vector3 position, Material material)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = position;
                Home = position;
                Transform = go.transform;
                mesh = new Mesh { name = name };
                mesh.MarkDynamic();
                var normals = new Vector3[vertices.Length];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
                var triangles = new int[N * 6];
                for (int i = 0; i < N; i++)
                {
                    int a = i * 2, b = a + 1, c = a + 2, d = a + 3; // a/c top, b/d bottom
                    triangles[i * 6 + 0] = a; triangles[i * 6 + 1] = c; triangles[i * 6 + 2] = b;
                    triangles[i * 6 + 3] = b; triangles[i * 6 + 4] = c; triangles[i * 6 + 5] = d;
                }
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.triangles = triangles;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            static float T(int i) => -1f + 2f * i / N;

            /// <summary>
            /// This sticker's flat shape laid onto the avatar's surface (see ConformToSurface). A sticker only has
            /// corners along its top and bottom edges, so each column clears the highest bump anywhere between the
            /// two (a blob of cheese in the middle of an eye would poke through otherwise).
            /// </summary>
            public void Conform(Matrix4x4 faceFromWorld, FrontSurface surface, float gap)
            {
                Matrix4x4 toFace = faceFromWorld * Transform.localToWorldMatrix;
                Matrix4x4 fromFace = toFace.inverse;
                for (int i = 0; i <= N; i++)
                {
                    Vector3 top = toFace.MultiplyPoint3x4(vertices[i * 2]), bottom = toFace.MultiplyPoint3x4(vertices[i * 2 + 1]);
                    if (Nearest(surface, top, bottom, 6, out float z))
                    {
                        top.z += z - gap;
                        bottom.z += z - gap;
                    }
                    shaped[i * 2] = fromFace.MultiplyPoint3x4(top);
                    shaped[i * 2 + 1] = fromFace.MultiplyPoint3x4(bottom);
                }
                mesh.vertices = shaped;
                mesh.RecalculateBounds();
            }

            void Apply(float halfWidth)
            {
                HalfWidth = halfWidth;
                for (int i = 0; i <= N; i++)
                {
                    float top = Mathf.Max(Top[i], Bottom[i]);
                    vertices[i * 2] = new Vector3(T(i) * halfWidth, top, 0f);
                    vertices[i * 2 + 1] = new Vector3(T(i) * halfWidth, Bottom[i], 0f);
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }

            public void Ellipse(float rx, float ry)
            {
                for (int i = 0; i <= N; i++)
                {
                    float s = Mathf.Sqrt(Mathf.Max(0f, 1f - T(i) * T(i)));
                    Top[i] = ry * s;
                    Bottom[i] = -ry * s;
                }
                Apply(rx);
            }

            /// <summary>
            /// Cartoon eye: an oval cut by an upper lid (lid 0 open .. 1 shut; slant tilts it, + = inner side
            /// higher) and pushed up from below by `squint` until it becomes a happy "^" arc.
            /// </summary>
            public void Eye(float rx, float ry, float lid, float slant, float squint, float innerSide)
            {
                float line = ry * 0.2f;
                float shut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, lid));
                for (int i = 0; i <= N; i++)
                {
                    float t = T(i);
                    float s = Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                    float lidLine = Mathf.Lerp(ry, -ry * 0.35f, lid) + slant * innerSide * t * ry * 0.45f;
                    float top = Mathf.Min(ry * s, lidLine);
                    // Shutting brings the lower edge up to a thin line under the lid.
                    float bottom = Mathf.Lerp(-ry * s, top - line, shut);
                    // Squinting pushes the lower edge up into an arc under the top curve: "^ ^".
                    bottom = Mathf.Lerp(bottom, top - ry * 0.4f * (0.35f + 0.65f * s), squint);
                    bottom = Mathf.Min(bottom, top - line * 0.6f);
                    // Squinting also lifts the eye a little, like cheeks pushing it up.
                    float lift = squint * ry * 0.25f * s;
                    Top[i] = top + lift;
                    Bottom[i] = bottom + lift;
                }
                Apply(rx);
            }

            /// <summary>
            /// Cartoon mouth: a curved line (smile + corners up, - down) that opens into a lens (wide "ah",
            /// puckered round "ooh", or a "D" grin where the top lip stays put and the jaw drops).
            /// </summary>
            public void Mouth(float halfWidth, float smile, float smirk, float open, float wide, float lip, out float teethHalfWidth)
            {
                float pucker = Mathf.Max(0f, -wide);
                float hw = halfWidth * (1f + 0.5f * wide) * (1f - 0.3f * open * pucker);
                float height = halfWidth * 2.3f * open;
                float topShare = Mathf.Lerp(0.3f, 0.06f, Mathf.Max(0f, smile));
                for (int i = 0; i <= N; i++)
                {
                    float t = T(i);
                    float profile = Mathf.Lerp(1f - t * t, Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)), pucker);
                    // Smile/frown bends both corners; a smirk tilts the line so one corner rises (+ = screen right).
                    float centre = smile * hw * 0.85f * t * t + smirk * hw * 0.35f * t;
                    Top[i] = centre + height * profile * topShare + lip * 0.5f;
                    Bottom[i] = centre - height * profile * (1f - topShare) - lip * 0.5f;
                }
                Apply(hw);
                teethHalfWidth = hw * 0.86f;
            }

            /// <summary>Upper teeth: a white band hanging from the mouth's top edge.</summary>
            public void Teeth(Sticker mouth, float halfWidth)
            {
                for (int i = 0; i <= N; i++)
                {
                    // Sample the mouth's edges at the same x (the teeth are a little narrower).
                    float x = T(i) * halfWidth / Mathf.Max(1e-5f, mouth.HalfWidth);
                    float f = (x + 1f) * 0.5f * N;
                    int j = Mathf.Clamp(Mathf.FloorToInt(f), 0, N - 1);
                    float k = f - j;
                    float top = Mathf.Lerp(mouth.Top[j], mouth.Top[j + 1], k) - 0.0015f;
                    float bottom = Mathf.Lerp(mouth.Bottom[j], mouth.Bottom[j + 1], k);
                    Top[i] = top;
                    Bottom[i] = Mathf.Max(bottom + 0.002f, top - (top - bottom) * 0.38f);
                }
                Apply(halfWidth);
            }
        }
    }
}
