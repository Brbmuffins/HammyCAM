using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Muffin
{
    /// <summary>
    /// The vacuum's hose: a long ribbed tube from the socket on top of the vacuum to a floor nozzle, simulated as
    /// a heavy rope (it hangs, drapes over the desk and drags along), not part of the model. Its nozzle follows the
    /// streamer's mouse over the desk (the same desktop cursor the mini mouse follows) and sucks up whatever it
    /// passes over: sprinkles and bits fly into it, puddles and splats are slurped away. Click the hose in the
    /// app (or use the menu) to switch the mouse steering off and on; off, the nozzle just lies where it was.
    /// </summary>
    [RequireComponent(typeof(BendRig))]
    public sealed class VacuumHose : MonoBehaviour
    {
        public Material hoseMaterial, nozzleMaterial;
        [Tooltip("Where the hose leaves the vacuum: the top of its socket, mesh space (metres).")]
        public float socketHeight = 0.76f;
        public float length = 1.4f, radius = 0.032f;
        [Range(8, 48)] public int points = 28;
        public float gravity = 9.8f;

        /// <summary>The nozzle follows the mouse (remembered; the menu and a click on the hose switch it).</summary>
        public static bool FollowMouse
        {
            get => PlayerPrefs.GetInt("muffin.hose.mouse", 1) == 1;
            set => PlayerPrefs.SetInt("muffin.hose.mouse", value ? 1 : 0);
        }

        BendRig rig;
        Transform socket, nozzle, tubeObject;
        Vector3[] p, previous;
        Mesh tube;
        Vector3[] ring;
        int[] triangles;
        float segment, nextSlurp;
        bool primed;
        SoundLoop hum;
        const int Sides = 10, Sub = 2; // ring corners, rings per segment (alternate rings are ribs)

        void Start()
        {
            rig = GetComponent<BendRig>();
            Transform bone = rig.BoneAt(socketHeight, out Vector3 rest);
            socket = new GameObject("Hose socket").transform;
            socket.SetParent(bone, false);
            socket.localPosition = new Vector3(0f, socketHeight, 0f) - rest;
            segment = length / (points - 1);
            p = new Vector3[points];
            previous = new Vector3[points];

            tubeObject = new GameObject("Hose").transform;
            tubeObject.SetParent(transform, false);
            var filter = tubeObject.gameObject.AddComponent<MeshFilter>();
            var renderer = tubeObject.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = hoseMaterial;
            tube = new Mesh { name = "Hose" };
            tube.MarkDynamic();
            int rings = (points - 1) * Sub + 1;
            ring = new Vector3[rings * Sides];
            triangles = new int[(rings - 1) * Sides * 6];
            int k = 0;
            for (int r = 0; r < rings - 1; r++)
            for (int s = 0; s < Sides; s++)
            {
                int a = r * Sides + s, b = r * Sides + (s + 1) % Sides, c = a + Sides, d = b + Sides;
                triangles[k++] = a; triangles[k++] = c; triangles[k++] = b;
                triangles[k++] = b; triangles[k++] = c; triangles[k++] = d;
            }
            tube.vertices = ring;
            tube.triangles = triangles;
            filter.sharedMesh = tube;

            // The floor nozzle: a neck and a wide flat head.
            nozzle = new GameObject("Nozzle").transform;
            nozzle.SetParent(transform, false);
            Part(PrimitiveType.Cylinder, nozzle, new Vector3(0f, 0f, 0.05f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.05f, 0.05f, 0.05f), nozzleMaterial);
            Part(PrimitiveType.Cube, nozzle, new Vector3(0f, -0.012f, 0.11f), Quaternion.identity, new Vector3(0.16f, 0.03f, 0.06f), nozzleMaterial);
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void OnEnable() => primed = false;

        void OnDisable()
        {
            hum?.Stop(0.2f);
            hum = null;
        }

        /// <summary>Where the mouse points on the desk: the desktop cursor's left-right across the desk, its up-down over the
        /// desk in front of the vacuum (top of the screen just in front of it, bottom the desk's front edge).</summary>
        static Vector3 MouseTarget()
        {
            Vector2 c = DeskInput.Instance ? DeskInput.Instance.Cursor01 : new Vector2(0.5f, 0.5f);
            float x = Mathf.Lerp(-0.95f, 0.95f, c.x), z = Mathf.Lerp(-0.72f, -0.3f, c.y);
            float ground = DeskScene.SurfaceHeight(x, z);
            return new Vector3(x, (float.IsNegativeInfinity(ground) ? 0f : ground) + 0.035f, z);
        }

        void LateUpdate()
        {
            if (p == null) return;
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 1f / 30f);
            Vector3 start = socket.position, up = socket.up;
            if (!primed)
            {
                // Out of the socket, up a little, then draped down to the desk in front and to the side.
                Vector3 end = new Vector3(0.45f, 0.04f, -0.4f);
                for (int i = 0; i < points; i++)
                {
                    float t = (float)i / (points - 1);
                    p[i] = previous[i] = Vector3.Lerp(start + up * 0.12f, end, t) + up * (0.12f * Mathf.Sin(t * Mathf.PI));
                }
                primed = true;
            }

            ClickToToggle();
            bool steering = FollowMouse && enabled;
            Vector3 target = MouseTarget();

            // A heavy rope: carry on moving, a bit of drag, gravity.
            for (int i = 2; i < points; i++)
            {
                Vector3 v = (p[i] - previous[i]) * 0.97f;
                previous[i] = p[i];
                p[i] += v + Vector3.down * gravity * dt * dt;
            }
            float bodyRadius = 0.235f;
            Vector3 centre = transform.position;
            for (int iteration = 0; iteration < 14; iteration++)
            {
                p[0] = start;
                p[1] = start + up * segment;
                // The nozzle is drawn to the mouse's spot on the desk (softly, so it lags and swings a little).
                if (steering) p[points - 1] = Vector3.Lerp(p[points - 1], target, 0.18f);
                // Every length of hose keeps its length (the first one is held in the socket; a steered nozzle
                // is held at the mouse, so the hose follows it rather than pulling it back).
                for (int i = 2; i < points; i++)
                {
                    Vector3 d = p[i] - p[i - 1];
                    float m = d.magnitude;
                    if (m < 1e-6f) continue;
                    Vector3 fix = d * ((m - segment) / m);
                    if (i - 1 <= 1) p[i] -= fix;
                    else if (steering && i == points - 1) p[i - 1] += fix;
                    else
                    {
                        p[i] -= fix * 0.5f;
                        p[i - 1] += fix * 0.5f;
                    }
                }
                for (int i = 2; i < points; i++)
                {
                    // Lies on the desk (and the keyboard), never sinks in.
                    float ground = DeskScene.SurfaceHeight(p[i].x, p[i].z);
                    if (!float.IsNegativeInfinity(ground) && p[i].y < ground + radius) p[i].y = ground + radius;
                    // Goes round the vacuum's body, not through it.
                    if (p[i].y < start.y - 0.02f)
                    {
                        var flat = new Vector2(p[i].x - centre.x, p[i].z - centre.z);
                        if (flat.magnitude < bodyRadius)
                        {
                            Vector2 out2 = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.down;
                            p[i].x = centre.x + out2.x * bodyRadius;
                            p[i].z = centre.z + out2.y * bodyRadius;
                        }
                    }
                }
            }

            BuildTube();
            PlaceNozzle();
            Suck(steering);
        }

        void BuildTube()
        {
            int rings = (points - 1) * Sub + 1;
            Vector3 normal = Vector3.Cross(socket.forward, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(socket.forward, Vector3.up).normalized : Vector3.right;
            Vector3 previousTangent = Vector3.up;
            for (int r = 0; r < rings; r++)
            {
                float f = (float)r / Sub;
                int i = Mathf.Min(Mathf.FloorToInt(f), points - 2);
                float u = f - i;
                Vector3 centre = Vector3.Lerp(p[i], p[i + 1], u);
                Vector3 tangent = (p[i + 1] - p[i]).normalized;
                if (tangent.sqrMagnitude < 1e-6f) tangent = previousTangent;
                // Carry the ring's orientation along the hose so it doesn't twist.
                normal = Vector3.ProjectOnPlane(normal, tangent).normalized;
                if (normal.sqrMagnitude < 1e-6f) normal = Vector3.ProjectOnPlane(Vector3.right, tangent).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal);
                float rib = r % 2 == 0 ? 1f : 0.86f; // corrugated
                for (int s = 0; s < Sides; s++)
                {
                    float a = s * Mathf.PI * 2f / Sides;
                    Vector3 world = centre + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * radius * rib;
                    ring[r * Sides + s] = transform.InverseTransformPoint(world);
                }
                previousTangent = tangent;
            }
            tube.vertices = ring;
            tube.RecalculateNormals();
            tube.RecalculateBounds();
        }

        void PlaceNozzle()
        {
            Vector3 end = p[points - 1], along = (end - p[points - 3]);
            Vector3 flatAlong = new Vector3(along.x, 0f, along.z);
            if (flatAlong.sqrMagnitude < 1e-6f) flatAlong = Vector3.back;
            // The head lies flat on the desk, pointing the way the hose comes in.
            nozzle.position = end;
            nozzle.rotation = Quaternion.LookRotation(flatAlong.normalized, Vector3.up);
        }

        /// <summary>
        /// The nozzle eats what's under it: sprinkles and bits fly into it, splats and puddles shrink away. It hums
        /// while it's being moved, and slurps when it gets something.
        /// </summary>
        void Suck(bool steering)
        {
            Vector3 mouth = nozzle.TransformPoint(new Vector3(0f, -0.02f, 0.11f));
            float ground = DeskScene.SurfaceHeight(mouth.x, mouth.z);
            bool down = !float.IsNegativeInfinity(ground) && mouth.y - ground < 0.08f;
            if (down)
            {
                int swallowed = DeskDebris.Instance ? DeskDebris.Instance.Suck(mouth, 0.16f, 3f) : 0;
                if (Mess.Instance) Mess.Instance.CleanArea(mouth, 0.06f);
                if (swallowed > 0 && Time.time >= nextSlurp)
                {
                    nextSlurp = Time.time + 0.4f;
                    Sfx.Play("slurp", 0.6f, 0.12f);
                }
            }
            // Hums while it's being steered around, quieter when still.
            float speed = (p[points - 1] - previous[points - 1]).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            if (steering && hum == null) hum = Sfx.Loop("vacuum_hum", 0f);
            if (!steering && hum != null)
            {
                hum.Stop(0.4f);
                hum = null;
            }
            hum?.SetVolume(Mathf.Clamp01(0.15f + speed * 0.6f) * 0.5f);
        }

        /// <summary>A click on the hose (or its nozzle) in the app's window switches the mouse steering.</summary>
        void ClickToToggle()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || !Application.isFocused || Keys.Suppressed) return;
            Camera cam = Camera.main;
            if (!cam) return;
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            float closest = float.MaxValue;
            foreach (Vector3 q in p) closest = Mathf.Min(closest, Vector3.Cross(ray.direction, q - ray.origin).magnitude);
            closest = Mathf.Min(closest, Vector3.Cross(ray.direction, nozzle.position - ray.origin).magnitude);
            if (closest > radius * 2.5f) return;
            FollowMouse = !FollowMouse;
            Sfx.Play("pop", 0.6f, 0.05f);
#endif
        }
    }
}
