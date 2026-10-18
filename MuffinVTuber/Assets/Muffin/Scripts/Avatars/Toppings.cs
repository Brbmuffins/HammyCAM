using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// Pepperoni stuck on a pizza slice avatar: real discs riding the bend rig (the ones on the floppy point
    /// flop with it). Now and then one slides off, and a hard shake can knock one loose; it falls onto the
    /// desk (DeskDebris), where the hands push it around. The face slam (R) picks them all back up: they fly
    /// back to their spots. K puts them back at once.
    /// </summary>
    [RequireComponent(typeof(BendRig), typeof(MeshAvatar))]
    public sealed class Toppings : MonoBehaviour, IMessy
    {
        public Material pepperoni;
        [Tooltip("Spots on the front: x as a share of the half-width there (-1..1), y as a share of the height.")]
        public Vector2[] spots =
        {
            new Vector2(-0.45f, 0.18f), new Vector2(0.5f, 0.2f),
            new Vector2(-0.35f, 0.64f), new Vector2(0.3f, 0.7f), new Vector2(0f, 0.82f),
        };
        public float diameter = 0.11f;
        public float thickness = 0.008f;
        [Tooltip("Shake (m/s²) that can knock one loose; more shake, more likely.")]
        public float shakeToFall = 14f;
        [Tooltip("One slides off on its own every this many seconds (random in between).")]
        public Vector2 fallsEvery = new Vector2(18f, 45f);

        sealed class Slot
        {
            public Transform Disc, Bone;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public bool On = true, Flying;
            public Vector3 LastPosition, LastVelocity;
            public float Shake;
            public float HoldUntil; // just put back: it holds on through the bounce
        }

        readonly List<Slot> slots = new List<Slot>();
        float nextFall;

        public int OnCount
        {
            get
            {
                int n = 0;
                foreach (Slot s in slots) if (s.On) n++;
                return n;
            }
        }

        void Start()
        {
            var rig = GetComponent<BendRig>();
            Mesh mesh = GetComponent<MeshAvatar>().mesh;
            if (!mesh || !pepperoni) return;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Bounds b = mesh.bounds;
            foreach (Vector2 spot in spots)
            {
                float y = b.min.y + b.size.y * spot.y;
                // Width of the slice at that height, then the frontmost point near the spot.
                float minX = float.MaxValue, maxX = float.MinValue;
                foreach (Vector3 v in vertices)
                    if (Mathf.Abs(v.y - y) < b.size.y * 0.02f)
                    {
                        minX = Mathf.Min(minX, v.x);
                        maxX = Mathf.Max(maxX, v.x);
                    }
                if (maxX <= minX) continue;
                float x = (minX + maxX) * 0.5f + spot.x * (maxX - minX) * 0.5f * 0.8f;
                int best = -1;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    if (Mathf.Abs(v.x - x) > 0.035f || Mathf.Abs(v.y - y) > 0.035f) continue;
                    if (best < 0 || v.z < vertices[best].z) best = i;
                }
                if (best < 0) continue;
                Vector3 normal = normals.Length == vertices.Length ? normals[best].normalized : Vector3.back;
                if (normal.z > -0.3f) normal = Vector3.back; // the front faces the camera (-z)
                Vector3 point = vertices[best] + normal * (thickness * 0.5f + 0.002f);

                Transform bone = rig.BoneAt(point.y, out Vector3 rest);
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Pepperoni";
                Destroy(disc.GetComponent<Collider>());
                var renderer = disc.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = pepperoni;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                // Not parented to the bone: the bend bones squash and stretch, which would skew a disc riding
                // them. It follows the bone's place and turn each frame instead, and keeps its own shape.
                disc.transform.SetParent(transform, false);
                // A cylinder is 2 tall and 1 wide: flatten it into a disc lying on the surface.
                disc.transform.localScale = new Vector3(diameter, thickness * 0.5f, diameter);
                var slot = new Slot
                {
                    Disc = disc.transform,
                    Bone = bone,
                    LocalPosition = point - rest,
                    LocalRotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                };
                Place(slot);
                slots.Add(slot);
                // The face parts go over the slice, not behind it.
            }
            nextFall = Time.time + Random.Range(fallsEvery.x, fallsEvery.y);
        }

        bool returnPending;

        // Switching to another avatar: this one's fallen pepperoni leave the desk (they'd litter the next one's)
        // and go back on when it's back. Not re-parented here: Unity refuses while objects are switching on or off.
        void OnDisable()
        {
            foreach (Slot s in slots)
                if (!s.On && s.Disc)
                {
                    DeskDebris.Instance?.Take(s.Disc);
                    s.Disc.gameObject.SetActive(false);
                    returnPending = true;
                }
        }

        void OnEnable()
        {
            foreach (Slot s in slots) s.LastPosition = s.Disc ? s.Disc.position : Vector3.zero;
        }

        void Update()
        {
            if (returnPending)
            {
                returnPending = false;
                ReturnAll();
            }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            foreach (Slot s in slots)
            {
                if (!s.On || !s.Disc) continue;
                Vector3 position = s.Disc.position;
                Vector3 velocity = (position - s.LastPosition) / dt;
                Vector3 acceleration = (velocity - s.LastVelocity) / dt;
                s.LastPosition = position;
                s.LastVelocity = velocity;
                s.Shake = Mathf.Lerp(s.Shake, Mathf.Min(acceleration.magnitude, 200f), 1f - Mathf.Exp(-dt * 10f));
                // A hard shake can knock it loose (the chance grows with the shake), and so can swinging about
                // fast (the floppy point whipping round when you turn).
                float chance = Mathf.Max(0f, s.Shake - shakeToFall) * 0.05f + Mathf.Max(0f, velocity.magnitude - 1f) * 0.02f;
                if (Time.time > s.HoldUntil && Random.value < chance * dt) Drop(s);
            }
            if (Time.time >= nextFall)
            {
                nextFall = Time.time + Random.Range(fallsEvery.x, fallsEvery.y);
                DropOne();
            }
        }

        // After the rig has posed the bones this frame.
        void LateUpdate()
        {
            foreach (Slot s in slots)
                if (s.On && !s.Flying && s.Disc) Place(s);
        }

        /// <summary>On its spot on the bone (following the bend and the stretch of the surface), its own shape.</summary>
        void Place(Slot s) => s.Disc.SetPositionAndRotation(s.Bone.TransformPoint(s.LocalPosition), s.Bone.rotation * s.LocalRotation);

        /// <summary>
        /// The slices that are on, as bumps on the face's surface map (face space), so the face parts ride over
        /// them: a slice tilted on a slope sticks out further at one edge, which the bump allows for.
        /// </summary>
        public void AddBumps(FrontSurface surface, Transform face)
        {
            float faceScale = Mathf.Max(1e-4f, face.lossyScale.x);
            foreach (Slot s in slots)
            {
                if (!s.On || s.Flying || !s.Disc) continue;
                Vector3 centre = face.InverseTransformPoint(s.Disc.position);
                Vector3 normal = face.InverseTransformDirection(s.Disc.up).normalized;
                float radius = diameter * 0.5f / faceScale;
                float reach = radius * Mathf.Sqrt(Mathf.Max(0f, 1f - normal.z * normal.z)) + thickness / faceScale;
                surface.AddBump(centre, radius * 1.2f, centre.z - reach);
            }
        }

        public string MessName => "pepperoni";

        /// <summary>!shake: a couple of pepperoni fly off.</summary>
        public void Burst()
        {
            DropOne();
            DropOne();
        }

        /// <summary>Knock one (still stuck on) off.</summary>
        public void DropOne()
        {
            var on = slots.FindAll(s => s.On && !s.Flying);
            if (on.Count > 0) Drop(on[Random.Range(0, on.Count)]);
        }

        void Drop(Slot s)
        {
            DeskDebris debris = DeskDebris.Instance;
            if (!debris || !s.On) return;
            s.On = false;
            Sfx.Play("plop", 0.7f, 0.15f);
            // Slides off forward (toward the camera) and a little sideways, with the pizza's own motion.
            Vector3 kick = s.Disc.up * 0.4f + new Vector3(Random.Range(-0.3f, 0.3f), 0.2f, -0.2f);
            debris.AddPiece(s.Disc, s.LastVelocity * 0.8f + kick, diameter * 0.5f, thickness * 0.5f);
        }

        /// <summary>Fly every fallen pepperoni back to its spot over `seconds` (the face slam).</summary>
        public void PickUp(float seconds)
        {
            foreach (Slot s in slots)
                if (!s.On && !s.Flying && s.Disc) StartCoroutine(Fly(s, seconds * Random.Range(0.8f, 1.2f)));
        }

        IEnumerator Fly(Slot s, float seconds)
        {
            s.Flying = true;
            DeskDebris.Instance?.Take(s.Disc);
            s.Disc.gameObject.SetActive(true);
            Vector3 from = s.Disc.position;
            Quaternion fromRotation = s.Disc.rotation;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                Vector3 to = s.Bone.TransformPoint(s.LocalPosition);
                // An arc: up off the desk, then onto the pizza.
                s.Disc.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.18f;
                s.Disc.rotation = Quaternion.Slerp(fromRotation, s.Bone.rotation * s.LocalRotation, k);
                yield return null;
            }
            Attach(s);
        }

        /// <summary>Everything back on, at once (K, or a pepperoni that fell off the desk).</summary>
        public void ReturnAll()
        {
            foreach (Slot s in slots)
                if (!s.On && s.Disc)
                {
                    DeskDebris.Instance?.Take(s.Disc);
                    Attach(s);
                }
        }

        /// <summary>A fallen pepperoni went over the desk's edge: it quietly reappears in its spot.</summary>
        public void Lost(Transform disc)
        {
            foreach (Slot s in slots)
                if (s.Disc == disc) Attach(s);
        }

        void Attach(Slot s)
        {
            s.Flying = false;
            s.On = true;
            s.Disc.gameObject.SetActive(true);
            s.Disc.SetParent(transform, false);
            s.Disc.localScale = new Vector3(diameter, thickness * 0.5f, diameter);
            Place(s);
            s.LastPosition = s.Disc.position;
            s.LastVelocity = Vector3.zero;
            s.Shake = 0f;
            s.HoldUntil = Time.time + 2.5f;
        }
    }
}
