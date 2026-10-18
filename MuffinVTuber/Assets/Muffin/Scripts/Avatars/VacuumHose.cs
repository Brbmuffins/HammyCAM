using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Muffin
{
    /// <summary>
    /// The vacuum's hose: a long ribbed tube from the socket on top of the vacuum to a floor nozzle, simulated as
    /// a heavy rope (it hangs, drapes over the desk and drags along), not part of the model. Left alone, it cleans
    /// up by itself: the nozzle goes after the nearest mess on the desk within reach, sweeping over it, and sucks
    /// up whatever it passes over (sprinkles and bits fly into it, puddles and splats are slurped away), then
    /// lies still once the desk is clean. Mouse steering is optional (the menu, or a click on the hose): the nozzle
    /// then follows the desktop cursor while the mouse moves, and goes back to cleaning once it's been still a few
    /// seconds. It's off by default: the streamer's mouse is busy with everything else on their PC.
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
        [Header("Cleaning by itself")]
        [Tooltip("How fast the nozzle moves when it cleans by itself (m/s).")]
        public float huntSpeed = 0.4f;
        [Tooltip("With mouse steering on: seconds of a still mouse before it goes back to cleaning by itself.")]
        public float idleAfter = 3f;

        /// <summary>The nozzle follows the mouse while it moves (remembered; the menu and a click on the hose switch it).</summary>
        public static bool FollowMouse
        {
            get => PlayerPrefs.GetInt("muffin.hose.mouse.v2", 0) == 1;
            set => PlayerPrefs.SetInt("muffin.hose.mouse.v2", value ? 1 : 0);
        }

        /// <summary>With nobody steering it, the nozzle goes round the desk sucking up the mess (remembered; the menu switches it).</summary>
        public static bool AutoClean
        {
            get => PlayerPrefs.GetInt("muffin.hose.auto", 1) == 1;
            set => PlayerPrefs.SetInt("muffin.hose.auto", value ? 1 : 0);
        }

        /// <summary>Sprinkles and bits it has swallowed (the self-test reads it).</summary>
        public int Eaten { get; private set; }
        /// <summary>Cleaning by itself right now.</summary>
        public bool Hunting { get; private set; }

        BendRig rig;
        Transform socket, nozzle, tubeObject;
        Vector3[] p, previous;
        Mesh tube;
        Vector3[] ring;
        int[] triangles;
        float segment, nextSlurp;
        bool primed;
        Vector2 lastCursor;
        float lastCursorMove = -999f;
        // Cleaning by itself: where the nozzle is being led, the mess it's after, and mess it gave up on.
        Vector3 huntPoint, huntTarget;
        bool hasTarget;
        float nextLook, sweep, targetSince;
        readonly System.Collections.Generic.List<(Vector3 at, float until)> skipped = new System.Collections.Generic.List<(Vector3, float)>();
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

        /// <summary>
        /// Cleaning by itself: the nozzle is led to the nearest mess within reach at a steady pace, sweeping from side
        /// to side over it like someone working a vacuum over a spill, then on to the next. False once the desk is
        /// clean (the nozzle then just lies where it is).
        /// </summary>
        bool Hunt(float dt, bool wasHunting, out Vector3 target)
        {
            target = default;
            if (!wasHunting)
            {
                huntPoint = p[points - 1]; // carries on from wherever the nozzle is
                hasTarget = false;
            }
            if (!hasTarget || Time.time >= nextLook)
            {
                nextLook = Time.time + 0.4f;
                Vector3 before = huntTarget;
                hasTarget = FindMess(huntPoint, out huntTarget);
                if (!hasTarget || (huntTarget - before).sqrMagnitude > 0.05f * 0.05f) targetSince = Time.time;
                // Still after the same thing after a while (out of reach, or it won't come up): give up on it for now.
                if (hasTarget && Time.time - targetSince > 6f)
                {
                    skipped.Add((huntTarget, Time.time + 20f));
                    hasTarget = FindMess(huntPoint, out huntTarget);
                    targetSince = Time.time;
                }
            }
            if (!hasTarget) return false;

            // The mouth sits in front of the hose's end: lead the end so the mouth lands on the mess.
            Vector3 facing = nozzle ? Vector3.ProjectOnPlane(nozzle.forward, Vector3.up).normalized : Vector3.back;
            Vector3 goal = huntTarget - facing * 0.11f;
            Vector3 toGoal = new Vector3(goal.x - huntPoint.x, 0f, goal.z - huntPoint.z);
            huntPoint += Vector3.ClampMagnitude(toGoal, huntSpeed * dt);
            sweep += dt;
            Vector3 heading = toGoal.sqrMagnitude > 1e-4f ? toGoal.normalized : facing;
            Vector3 across = new Vector3(-heading.z, 0f, heading.x);
            target = huntPoint + across * (Mathf.Sin(sweep * 6f) * 0.035f);
            float ground = DeskScene.SurfaceHeight(target.x, target.z);
            target.y = (float.IsNegativeInfinity(ground) ? 0f : ground) + 0.035f;
            return true;
        }

        /// <summary>The nearest sprinkle, bit, splat or lump on the desk the hose can reach.</summary>
        bool FindMess(Vector3 from, out Vector3 at)
        {
            skipped.RemoveAll(s => Time.time > s.until);
            Vector3 piece = default, stain = default;
            bool bit = DeskDebris.Instance && DeskDebris.Instance.NearestResting(from, Reachable, out piece);
            bool splat = Mess.Instance && Mess.Instance.NearestOnDesk(from, Reachable, out stain);
            at = bit && (!splat || FlatDistance(piece, from) <= FlatDistance(stain, from)) ? piece : stain;
            return bit || splat;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>On the desk, in front of or beside the vacuum (in view), round its body and within the hose's reach.</summary>
        bool Reachable(Vector3 q)
        {
            Vector3 c = transform.position;
            float r = FlatDistance(q, c);
            if (r < 0.3f || r > 1.05f || q.z - c.z > 0.15f) return false;
            float ground = DeskScene.SurfaceHeight(q.x, q.z);
            if (float.IsNegativeInfinity(ground) || q.y > ground + 0.12f) return false;
            foreach (var s in skipped)
                if (FlatDistance(q, s.at) < 0.08f) return false;
            return true;
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
            // The mouse steers while it's being moved (with steering on); left alone, the hose cleans up by itself.
            Vector2 cursor = DeskInput.Instance ? DeskInput.Instance.Cursor01 : lastCursor;
            if ((cursor - lastCursor).sqrMagnitude > 1e-6f)
            {
                lastCursor = cursor;
                lastCursorMove = Time.time;
            }
            bool byMouse = FollowMouse && Time.time - lastCursorMove < idleAfter;
            Vector3 target = default;
            bool wasHunting = Hunting;
            Hunting = !byMouse && AutoClean && Hunt(dt, wasHunting, out target);
            if (byMouse) target = MouseTarget();
            bool steering = byMouse || Hunting;

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
                // The nozzle is drawn to the mouse's spot on the desk, or the mess it's after (softly, so it lags
                // and swings a little).
                if (steering) p[points - 1] = Vector3.Lerp(p[points - 1], target, 0.18f);
                // Every length of hose keeps its length (the first one is held in the socket; a steered nozzle
                // is held at its target, so the hose follows it rather than pulling it back).
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
        /// while it's working (steered or cleaning by itself), and slurps when it gets something.
        /// </summary>
        void Suck(bool steering)
        {
            Vector3 mouth = nozzle.TransformPoint(new Vector3(0f, -0.02f, 0.11f));
            float ground = DeskScene.SurfaceHeight(mouth.x, mouth.z);
            bool down = !float.IsNegativeInfinity(ground) && mouth.y - ground < 0.08f;
            if (down)
            {
                int swallowed = DeskDebris.Instance ? DeskDebris.Instance.Suck(mouth, 0.16f, 3f) : 0;
                Eaten += swallowed;
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
