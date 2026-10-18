using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The avatar's own little desk: the avatar sits on it like a desk toy, with a mini keyboard, mouse and
    /// pad in front of it and two floating cartoon hands that copy the streamer. Each real key press is a
    /// tap on the mini keyboard; while the mouse is in use the right hand stays on it and steers it around
    /// its pad (the left hand types, as with WASD), and clicks squish it. D toggles the desk.
    ///
    /// Everything is built from code. The avatar faces the camera, so "away from the avatar" is toward the
    /// viewer (-Z), and the scene is mirrored like the face: move your mouse right, the mini mouse goes right.
    /// </summary>
    public sealed class DeskScene : MonoBehaviour
    {
        public Material wood, keyboardBody, keycaps, pad, mouseBody, hands;
        [Tooltip("How far the mini mouse moves per real cursor pixel, in metres.")]
        public float mouseScale = 0.00022f;

        public bool Shown { get; private set; } = true;

        /// <summary>The floating hands and the mini mouse (world space), for things on the desk to bump into.</summary>
        public Vector3 LeftHandPosition => leftHand ? leftHand.position : Vector3.zero;
        public Vector3 RightHandPosition => rightHand ? rightHand.position : Vector3.zero;
        public Vector3 MousePosition => mouse ? mouse.position : Vector3.zero;

        /// <summary>The desk's extent (x, z) in metres, around the avatar.</summary>
        public const float DeskMinX = -1.1f, DeskMaxX = 1.1f, DeskMinZ = -0.75f, DeskMaxZ = 0.35f;

        /// <summary>
        /// Height of whatever's at (x, z) on the desk: the keyboard's keys, the mouse pad, or the desk top
        /// (y 0). Off the desk: -infinity, so things fall off the edge.
        /// </summary>
        public static float SurfaceHeight(float x, float z)
        {
            if (x < DeskMinX || x > DeskMaxX || z < DeskMinZ || z > DeskMaxZ) return float.NegativeInfinity;
            if (Mathf.Abs(x - KeyboardCentre.x) < KeyboardWidth / 2f && Mathf.Abs(z - KeyboardCentre.z) < KeyboardDepth / 2f) return 0.049f;
            if (Mathf.Abs(x - PadCentre.x) < PadHalfX && Mathf.Abs(z - PadCentre.z) < PadHalfZ) return 0.006f;
            return 0f;
        }

        // Layout (metres). Desk top is y = 0, where the avatar stands. A tall window (vertical, for TikTok) shows
        // less of the desk's width, so there the keyboard is smaller and the mouse pad tucked in beside it.
        static Vector3 KeyboardCentre = new Vector3(-0.16f, 0f, -0.44f);
        static float KeyboardWidth = 0.7f, KeyboardDepth = 0.24f;
        static Vector3 PadCentre = new Vector3(0.5f, 0f, -0.44f);
        static float PadHalfX = 0.15f, PadHalfZ = 0.12f;
        const float HoverY = 0.11f;

        /// <summary>The compact layout for a tall window is in use.</summary>
        public static bool Compact { get; private set; }

        /// <summary>A window this much taller than wide (width / height below it) counts as vertical.</summary>
        public const float TallAspect = 0.8f;

        static void Layout(bool compact)
        {
            Compact = compact;
            KeyboardCentre = compact ? new Vector3(-0.08f, 0f, -0.42f) : new Vector3(-0.16f, 0f, -0.44f);
            KeyboardWidth = compact ? 0.5f : 0.7f;
            KeyboardDepth = compact ? 0.2f : 0.24f;
            PadCentre = compact ? new Vector3(0.3f, 0f, -0.42f) : new Vector3(0.5f, 0f, -0.44f);
            PadHalfX = compact ? 0.1f : 0.15f;
            PadHalfZ = compact ? 0.1f : 0.12f;
        }

        Transform root, leftHand, rightHand, mouse;
        Vector2 mouseOffset, mouseVelocity;
        Vector3 leftTarget, rightTarget;
        SpringVector3 leftSpring, rightSpring;
        float leftTap, rightTap, rightSquish;
        bool nextTapLeft = true;

        void Start()
        {
            Layout((float)Screen.width / Mathf.Max(1, Screen.height) < TallAspect);
            Build();
            leftTarget = LeftRest();
            rightTarget = MouseTop();
            leftSpring = new SpringVector3(9f, 0.75f, 1f, leftTarget);
            rightSpring = new SpringVector3(11f, 0.8f, 1f, rightTarget);
            Show(PlayerPrefs.GetInt("muffin.desk", 1) == 1);
        }

        /// <summary>Show or hide the desk (the StageCamera reframes to match).</summary>
        public void Show(bool on)
        {
            Shown = on;
            PlayerPrefs.SetInt("muffin.desk", on ? 1 : 0);
            if (root) root.gameObject.SetActive(on);
        }

        void Update()
        {
            if (Keys.Pressed(MuffinKey.D)) Show(!Shown);
            // The window turned tall (or wide): lay the desk out again to suit.
            bool tall = (float)Screen.width / Mathf.Max(1, Screen.height) < TallAspect;
            if (tall != Compact && root)
            {
                Layout(tall);
                Destroy(root.gameObject);
                Build();
                root.gameObject.SetActive(Shown);
                mouseOffset = Vector2.zero;
            }
            if (!Shown || root == null) return;
            DeskInput input = DeskInput.Instance;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            bool mousing = input && input.SinceMouse < 1.2f;

            // The mini mouse: real cursor motion pushes it, and it drifts back to the middle of its pad so
            // it never wanders off, however far the real cursor goes.
            Vector2 delta = input ? input.MouseDelta : Vector2.zero;
            Vector2 push = new Vector2(delta.x, -delta.y) * mouseScale; // mouse "up" = away from the avatar
            mouseOffset += push;
            mouseOffset = Vector2.Lerp(mouseOffset, Vector2.zero, 1f - Mathf.Exp(-dt * 1.2f));
            mouseOffset.x = Mathf.Clamp(mouseOffset.x, -PadHalfX + 0.04f, PadHalfX - 0.04f);
            mouseOffset.y = Mathf.Clamp(mouseOffset.y, -PadHalfZ + 0.055f, PadHalfZ - 0.055f);
            mouseVelocity = Vector2.Lerp(mouseVelocity, push / dt, 1f - Mathf.Exp(-dt * 10f));
            mouse.localPosition = PadCentre + new Vector3(mouseOffset.x, 0.018f, mouseOffset.y);
            mouse.localRotation = Quaternion.Euler(0f, Mathf.Clamp(mouseVelocity.x * 60f, -14f, 14f), 0f);

            // Typing: every key press is a tap. While mousing, only the left hand types.
            int presses = input ? input.KeyPresses : 0;
            for (int i = 0; i < presses; i++)
            {
                bool left = mousing || nextTapLeft;
                nextTapLeft = !nextTapLeft;
                if (left)
                {
                    leftTarget = KeyPosition(left: true);
                    leftTap = 1f;
                }
                else
                {
                    rightTarget = KeyPosition(left: false);
                    rightTap = 1f;
                }
            }
            bool typing = input && input.SinceKey < 0.6f;
            if (!typing) leftTarget = Vector3.Lerp(leftTarget, LeftRest(), 1f - Mathf.Exp(-dt * 3f));
            // The right hand lives on the mouse unless it's busy typing.
            if (mousing || !typing || input.SinceKey > input.SinceMouse) rightTarget = MouseTop();
            if (input && input.Clicked) rightSquish = 1f;

            leftTap = Mathf.MoveTowards(leftTap, 0f, dt / 0.09f);
            rightTap = Mathf.MoveTowards(rightTap, 0f, dt / 0.09f);
            rightSquish = Mathf.MoveTowards(rightSquish, 0f, dt / 0.14f);
            float idle = Mathf.Sin(Time.time * 2.1f) * 0.004f;

            Vector3 l = leftSpring.Update(leftTarget, dt);
            Vector3 r = rightSpring.Update(rightTarget, dt);
            leftHand.localPosition = l + Vector3.up * (idle - Tap(leftTap) * 0.06f);
            rightHand.localPosition = r + Vector3.up * (-idle - Tap(rightTap) * 0.06f - rightSquish * 0.012f);
            rightHand.localScale = new Vector3(1f, 1f - rightSquish * 0.25f, 1f);
        }

        /// <summary>Quick down-and-up: 0 → 1 → 0 as the tap timer runs 1 → 0.</summary>
        static float Tap(float t) => Mathf.Sin(t * Mathf.PI);

        Vector3 LeftRest() => KeyboardCentre + new Vector3(-KeyboardWidth * 0.22f, HoverY, 0.01f);
        Vector3 MouseTop() => PadCentre + new Vector3(mouseOffset.x, 0.075f, mouseOffset.y + 0.015f);

        /// <summary>A random key on the left or right half of the mini keyboard.</summary>
        static Vector3 KeyPosition(bool left)
        {
            float x = left ? Random.Range(-0.48f, -0.04f) : Random.Range(0.04f, 0.46f);
            float z = Random.Range(-0.35f, 0.3f);
            return KeyboardCentre + new Vector3(x * KeyboardWidth, HoverY - 0.01f, z * KeyboardDepth);
        }

        void Build()
        {
            root = new GameObject("Desk").transform;
            root.SetParent(transform, false);

            Box("Desk top", new Vector3(0f, -0.04f, -0.2f), new Vector3(2.2f, 0.08f, 1.1f), wood);
            Box("Keyboard", KeyboardCentre + new Vector3(0f, 0.016f, 0f), new Vector3(KeyboardWidth, 0.032f, KeyboardDepth), keyboardBody);
            const int columns = 12, rows = 4;
            float pitchX = (KeyboardWidth - 0.04f) / columns, pitchZ = (KeyboardDepth - 0.04f) / (rows + 1);
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    float x = -KeyboardWidth / 2f + 0.02f + pitchX * (col + 0.5f) + (row % 2) * pitchX * 0.25f;
                    if (x > KeyboardWidth / 2f - 0.025f) continue;
                    // The avatar is the typist, sitting behind the keyboard: the top row is toward the camera, the
                    // space bar on the avatar's side.
                    float z = -KeyboardDepth / 2f + 0.02f + pitchZ * (row + 0.5f);
                    Box("Key", KeyboardCentre + new Vector3(x, 0.04f, z), new Vector3(pitchX * 0.8f, 0.018f, pitchZ * 0.75f), keycaps);
                }
            Box("Space bar", KeyboardCentre + new Vector3(0f, 0.04f, KeyboardDepth / 2f - 0.02f - pitchZ * 0.5f),
                new Vector3(KeyboardWidth * 0.42f, 0.018f, pitchZ * 0.75f), keycaps);

            Box("Mouse pad", PadCentre + new Vector3(0f, 0.003f, 0f), new Vector3(PadHalfX * 2f, 0.006f, PadHalfZ * 2f), pad);
            mouse = Blob("Mouse", PadCentre, new Vector3(0.08f, 0.042f, 0.125f), mouseBody);

            // Carved into the desk like a school desk (a dark groove with a lighter cut edge), and printed small
            // on the mouse pad.
            Carve("HAMMY", new Vector3(KeyboardCentre.x - 0.02f, 0.0006f, -0.655f), -7f, 0.0068f,
                new Color(0.16f, 0.08f, 0.04f, 1f), new Color(0.8f, 0.6f, 0.44f, 0.75f));
            Carve("+", new Vector3(KeyboardCentre.x + 0.13f, 0.0006f, -0.665f), 12f, 0.004f,
                new Color(0.2f, 0.11f, 0.06f, 0.9f), new Color(0.78f, 0.58f, 0.42f, 0.6f));
            Print("Hammy", PadCentre + new Vector3(PadHalfX - 0.05f, 0.0068f, -PadHalfZ + 0.02f), 0.0022f, new Color(0.72f, 0.62f, 0.95f, 0.9f));
            leftHand = Hand("Left hand", thumbSide: +1f);
            rightHand = Hand("Right hand", thumbSide: -1f);
        }

        static Font font;

        /// <summary>Lettering lying flat on the desk, readable from the camera.</summary>
        TextMesh Lettering(string text, Vector3 position, float yaw, float size, Color colour, int order = 0)
        {
            font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject($"Lettering {text}");
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(90f, yaw, 0f);
            var lettering = go.AddComponent<TextMesh>();
            lettering.text = text;
            lettering.font = font;
            lettering.fontSize = 64;
            lettering.characterSize = size;
            lettering.anchor = TextAnchor.MiddleCenter;
            lettering.fontStyle = FontStyle.Bold;
            lettering.color = colour;
            // Its own copy of the font's material, so layered lettering draws in a fixed order (a groove over its edge).
            go.GetComponent<MeshRenderer>().sharedMaterial = new Material(font.material) { renderQueue = font.material.renderQueue + order };
            return lettering;
        }

        /// <summary>Carved into the wood: a dark groove, its lit far edge peeking out just behind it.</summary>
        void Carve(string text, Vector3 position, float yaw, float size, Color groove, Color edge)
        {
            Lettering(text, position + new Vector3(0.0012f, 0f, 0.0016f), yaw, size, edge, 1);
            Lettering(text, position + Vector3.up * 0.0008f, yaw, size, groove, 2);
        }

        void Print(string text, Vector3 position, float size, Color colour) => Lettering(text, position, 0f, size, colour);

        Transform Hand(string name, float thumbSide)
        {
            // A cartoon mitten: a rounded palm and a thumb, floating (no arm), palm down.
            var hand = new GameObject(name).transform;
            hand.SetParent(root, false);
            Blob("Palm", Vector3.zero, new Vector3(0.13f, 0.065f, 0.15f), hands, hand);
            Blob("Thumb", new Vector3(0.066f * thumbSide, -0.006f, 0.02f), new Vector3(0.05f, 0.042f, 0.072f), hands, hand);
            return hand;
        }

        Transform Box(string name, Vector3 position, Vector3 size, Material material) =>
            Primitive(PrimitiveType.Cube, name, position, size, material, root);

        Transform Blob(string name, Vector3 position, Vector3 size, Material material, Transform parent = null) =>
            Primitive(PrimitiveType.Sphere, name, position, size, material, parent ? parent : root);

        static Transform Primitive(PrimitiveType type, string name, Vector3 position, Vector3 size, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }
    }
}
