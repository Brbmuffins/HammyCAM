using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// Everything that falls off an avatar onto its desk: sugar sprinkles (hundreds, drawn instanced) and a
    /// few bigger pieces (pepperoni). Simple physics of its own: gravity, bounces, sliding to a stop on
    /// whatever is below (desk, keyboard, mouse pad), and off the edge. The floating hands and the mini mouse
    /// shove anything in their way and typing taps scatter it, so over a stream the sugar piles up and gets
    /// pushed around the desk. K sweeps the desk; R makes the avatar slam its face into the desk.
    /// </summary>
    public sealed class DeskDebris : MonoBehaviour
    {
        public static DeskDebris Instance { get; private set; }

        public DeskScene desk;
        public AvatarLibrary library;
        [Tooltip("One material per sprinkle colour (GPU instancing on).")]
        public Material[] sprinkleMaterials;
        [Tooltip("Oldest sprinkles disappear beyond this many.")]
        public int maxSprinkles = 6000;
        [Tooltip("Scale of the capsule each sprinkle is drawn with (about 2.4 cm long).")]
        public Vector3 sprinkleSize = new Vector3(0.008f, 0.012f, 0.008f);
        [Tooltip("Cartoon gravity (m/s²): a little stronger than real, so things drop snappily.")]
        public float gravity = 14f;

        struct Sprinkle
        {
            public Vector3 Position, Velocity, Spin;
            public Quaternion Rotation;
            public int Colour;
            public bool Resting;
        }

        sealed class Piece
        {
            public Transform Transform;
            public Vector3 Velocity, Spin;
            public float Radius, HalfThickness;
            public bool Resting;
            /// <summary>Thrown away when swept (K) or lost over the edge (chaos props), not returned to an avatar.</summary>
            public bool Disposable;
            /// <summary>Rolls instead of lying flat (meatballs, tomato chunks).</summary>
            public bool Ball;
            /// <summary>Leaves a trail of this colour as it rolls (sauce), when HasTrail.</summary>
            public Color Trail;
            public bool HasTrail;
            public Vector3 LastTrail;
            /// <summary>Always comes to rest standing up (a banana peel), never on its back.</summary>
            public bool Upright;
        }

        readonly List<Sprinkle> sprinkles = new List<Sprinkle>();
        readonly List<Piece> pieces = new List<Piece>();
        Mesh capsule;
        Matrix4x4[][] batches;
        int[] batchCounts;
        RenderParams[] renderParams;
        Vector3 lastLeft, lastRight, lastMouse;
        bool primed;

        public int SprinkleCount => sprinkles.Count;
        public int PieceCount => pieces.Count;
        public bool Slamming { get; private set; }

        void Awake()
        {
            Instance = this;
            // The built-in capsule, borrowed from a throwaway primitive.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
            int colours = sprinkleMaterials != null ? sprinkleMaterials.Length : 0;
            batches = new Matrix4x4[colours][];
            batchCounts = new int[colours];
            renderParams = new RenderParams[colours];
            for (int c = 0; c < colours; c++)
            {
                batches[c] = new Matrix4x4[1023];
                renderParams[c] = new RenderParams(sprinkleMaterials[c]) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Drop one sprinkle into the world at position, moving at velocity.</summary>
        public void EmitSprinkle(Vector3 position, Vector3 velocity)
        {
            if (sprinkleMaterials == null || sprinkleMaterials.Length == 0) return;
            if (sprinkles.Count >= maxSprinkles) sprinkles.RemoveAt(0); // the oldest go first
            sprinkles.Add(new Sprinkle
            {
                Position = position,
                Velocity = velocity,
                Spin = Random.onUnitSphere * Random.Range(6f, 18f),
                Rotation = Random.rotation,
                Colour = Random.Range(0, sprinkleMaterials.Length),
            });
        }

        /// <summary>
        /// A bigger piece falls onto the desk (a pepperoni, a pie tin, a meatball): it's simulated here until
        /// taken back. Disposable pieces are thrown away when swept; balls roll; a trail colour leaves a trail.
        /// </summary>
        public void AddPiece(Transform piece, Vector3 velocity, float radius, float halfThickness,
            bool disposable = false, bool ball = false, Color? trail = null, bool upright = false)
        {
            piece.SetParent(transform, true);
            pieces.Add(new Piece
            {
                Transform = piece,
                Velocity = velocity,
                Spin = Random.onUnitSphere * Random.Range(4f, 11f),
                Radius = radius,
                HalfThickness = halfThickness,
                Disposable = disposable,
                Ball = ball,
                Trail = trail ?? default,
                HasTrail = trail.HasValue,
                LastTrail = piece.position,
                Upright = upright,
            });
            // Too many throwaway pieces: the oldest goes.
            int disposables = 0;
            foreach (Piece p in pieces) if (p.Disposable) disposables++;
            if (disposables > 60)
            {
                Piece oldest = pieces.Find(p => p.Disposable);
                if (oldest.Transform) Destroy(oldest.Transform.gameObject);
                pieces.Remove(oldest);
            }
        }

        /// <summary>Rain on the desk: everything lying there gets jostled.</summary>
        public void Agitate(float strength)
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < sprinkles.Count; i++)
            {
                if (Random.value > strength * dt * 6f) continue;
                Sprinkle s = sprinkles[i];
                s.Velocity += new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0.1f, 0.5f), Random.Range(-0.25f, 0.25f)) * strength;
                s.Resting = false;
                sprinkles[i] = s;
            }
            foreach (Piece p in pieces)
                if (Random.value < strength * dt * 2f)
                {
                    p.Velocity += new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.05f, 0.2f), Random.Range(-0.15f, 0.15f)) * strength;
                    p.Resting = false;
                }
        }

        /// <summary>Things on the desk within radius of centre (flowing water, heat) are carried along at velocity.</summary>
        public void PushArea(Vector3 centre, float radius, Vector3 velocity)
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime * 8f);
            for (int i = 0; i < sprinkles.Count; i++)
            {
                Sprinkle s = sprinkles[i];
                if (new Vector2(s.Position.x - centre.x, s.Position.z - centre.z).sqrMagnitude > radius * radius || s.Position.y > centre.y + 0.15f) continue;
                s.Velocity = Vector3.Lerp(s.Velocity, velocity, k);
                s.Resting = false;
                sprinkles[i] = s;
            }
            foreach (Piece p in pieces)
            {
                Vector3 q = p.Transform ? p.Transform.position : Vector3.zero;
                if (new Vector2(q.x - centre.x, q.z - centre.z).sqrMagnitude > radius * radius || q.y > centre.y + 0.15f) continue;
                p.Velocity = Vector3.Lerp(p.Velocity, velocity, k * 0.6f);
                p.Resting = false;
            }
        }

        /// <summary>
        /// A vacuum nozzle at point: things on the desk below it, within radius, fly up into it and are gone
        /// (sprinkles and throwaway pieces; toppings go home). Returns how many it swallowed.
        /// </summary>
        public int Suck(Vector3 point, float radius, float pull)
        {
            int swallowed = 0;
            float dt = Time.deltaTime;
            for (int i = sprinkles.Count - 1; i >= 0; i--)
            {
                Sprinkle s = sprinkles[i];
                Vector3 d = point - s.Position;
                if (new Vector2(d.x, d.z).magnitude > radius) continue;
                if (d.magnitude < 0.04f)
                {
                    sprinkles.RemoveAt(i);
                    swallowed++;
                    continue;
                }
                s.Velocity = Vector3.Lerp(s.Velocity, d.normalized * pull, 1f - Mathf.Exp(-dt * 6f));
                s.Velocity += Vector3.up * gravity * dt; // carried, not falling
                s.Resting = false;
                sprinkles[i] = s;
            }
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                Piece p = pieces[i];
                if (!p.Transform) continue;
                Vector3 d = point - p.Transform.position;
                if (new Vector2(d.x, d.z).magnitude > radius) continue;
                if (d.magnitude < 0.06f)
                {
                    pieces.RemoveAt(i);
                    swallowed++;
                    if (p.Disposable) Destroy(p.Transform.gameObject);
                    else ReturnHome(p.Transform);
                    continue;
                }
                p.Velocity = Vector3.Lerp(p.Velocity, d.normalized * pull * 0.7f, 1f - Mathf.Exp(-dt * 4f));
                p.Velocity += Vector3.up * gravity * dt;
                p.Resting = false;
            }
            return swallowed;
        }

        /// <summary>
        /// The nearest sprinkle or bit lying still (seen from above) that passes the test, if any: what the vacuum's
        /// hose goes after when nobody steers it.
        /// </summary>
        public bool NearestResting(Vector3 from, System.Func<Vector3, bool> allowed, out Vector3 at)
        {
            at = default;
            float best = float.MaxValue;
            foreach (Sprinkle s in sprinkles)
            {
                if (!s.Resting) continue;
                float d = new Vector2(s.Position.x - from.x, s.Position.z - from.z).sqrMagnitude;
                if (d >= best || !allowed(s.Position)) continue;
                best = d;
                at = s.Position;
            }
            foreach (Piece p in pieces)
            {
                if (!p.Transform || !p.Resting) continue;
                Vector3 q = p.Transform.position;
                float d = new Vector2(q.x - from.x, q.z - from.z).sqrMagnitude;
                if (d >= best || !allowed(q)) continue;
                best = d;
                at = q;
            }
            return best < float.MaxValue;
        }

        /// <summary>K was pressed (everything goes) / the sponge wiped round (x, z): mess kept elsewhere (the pillow's feathers) goes too.</summary>
        public static event System.Action Cleaned;
        public static event System.Action<Vector3, float> AreaCleaned;

        /// <summary>The sponge passes over (x, z): sprinkles and bits there are wiped away (toppings go home).</summary>
        public void CleanArea(Vector3 centre, float radius)
        {
            AreaCleaned?.Invoke(centre, radius);
            for (int i = sprinkles.Count - 1; i >= 0; i--)
                if (new Vector2(sprinkles[i].Position.x - centre.x, sprinkles[i].Position.z - centre.z).magnitude < radius) sprinkles.RemoveAt(i);
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                Piece p = pieces[i];
                if (!p.Transform) continue;
                Vector3 q = p.Transform.position;
                if (new Vector2(q.x - centre.x, q.z - centre.z).magnitude > radius + p.Radius) continue;
                pieces.RemoveAt(i);
                if (p.Disposable) Destroy(p.Transform.gameObject);
                else ReturnHome(p.Transform);
            }
        }

        void ReturnHome(Transform piece)
        {
            foreach (BendRig rig in library ? library.avatars : new List<BendRig>())
                if (rig && rig.TryGetComponent(out Toppings toppings)) toppings.Lost(piece);
        }

        /// <summary>Stop simulating a piece (it's being picked back up).</summary>
        public void Take(Transform piece) => pieces.RemoveAll(p => p.Transform == piece);

        /// <summary>K: sweep the sugar off the desk, and put fallen toppings back where they belong.</summary>
        public void Clean()
        {
            Cleaned?.Invoke();
            sprinkles.Clear();
            for (int i = pieces.Count - 1; i >= 0; i--)
                if (pieces[i].Disposable)
                {
                    if (pieces[i].Transform) Destroy(pieces[i].Transform.gameObject);
                    pieces.RemoveAt(i);
                }
            foreach (BendRig rig in library ? library.avatars : new List<BendRig>())
                if (rig && rig.TryGetComponent(out Toppings toppings)) toppings.ReturnAll();
            if (Mess.Instance) Mess.Instance.Clean();
        }

        /// <summary>R: the active avatar slams its face into the desk, shakes things loose and picks its toppings back up.</summary>
        public void Slam()
        {
            BendRig rig = library ? library.ActiveRig : null;
            if (rig && !Slamming) StartCoroutine(SlamRoutine(rig));
        }

        IEnumerator SlamRoutine(BendRig rig)
        {
            Slamming = true;
            // Fold down fast; the springs whip it into the desk.
            for (float t = 0f; t < 0.1f; t += Time.deltaTime)
            {
                rig.Slam = t / 0.1f;
                yield return null;
            }
            rig.Slam = 1f;
            yield return new WaitForSeconds(0.22f);
            // Face planted: sugar shakes off, and the toppings fly back on.
            Sfx.Play("slam_thud", 1f, 0.06f);
            if (rig.TryGetComponent(out SprinkleShedder _)) Sfx.Play("sprinkle_rain", 0.6f);
            if (rig.TryGetComponent(out SprinkleShedder shedder)) shedder.Burst(90);
            if (rig.TryGetComponent(out Toppings toppings)) toppings.PickUp(0.5f);
            if (rig.TryGetComponent(out MessMaker maker)) maker.OnSlam();
            if (rig.TryGetComponent(out FlourBag flour)) flour.OnSlam();
            if (rig.TryGetComponent(out GagAvatar gag)) gag.OnSlam();
            yield return new WaitForSeconds(0.45f);
            // Let go: it springs back up and wobbles.
            for (float t = 0f; t < 0.15f; t += Time.deltaTime)
            {
                rig.Slam = 1f - t / 0.15f;
                yield return null;
            }
            rig.Slam = 0f;
            Slamming = false;
        }

        void Update()
        {
            if (Keys.Pressed(MuffinKey.R)) Slam();
            // K: the bucket and sponge (instant if the chaos set is missing).
            if (Keys.Pressed(MuffinKey.K)) { if (ChaosDirector.Instance) ChaosDirector.Instance.ScrubClean(); else Clean(); }

            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 1f / 30f);
            bool deskOn = desk && desk.Shown;

            // The hands and the mouse, and how fast they're moving.
            Vector3 left = deskOn ? desk.LeftHandPosition : new Vector3(0f, -10f, 0f);
            Vector3 right = deskOn ? desk.RightHandPosition : new Vector3(0f, -10f, 0f);
            Vector3 mouse = deskOn ? desk.MousePosition : new Vector3(0f, -10f, 0f);
            Vector3 vLeft = primed ? (left - lastLeft) / dt : Vector3.zero;
            Vector3 vRight = primed ? (right - lastRight) / dt : Vector3.zero;
            Vector3 vMouse = primed ? (mouse - lastMouse) / dt : Vector3.zero;
            lastLeft = left;
            lastRight = right;
            lastMouse = mouse;
            primed = true;
            // Wet stains under a moving hand get smeared, blobs lying there get squished.
            if (deskOn && Mess.Instance)
            {
                Mess.Instance.Smear(left, 0.06f, vLeft);
                Mess.Instance.Smear(right, 0.06f, vRight);
                Mess.Instance.Smear(mouse, 0.045f, vMouse);
            }

            // The avatar's own body: nothing settles inside its base.
            Vector3 body = Vector3.zero;
            float bodyRadius = 0f;
            BendRig active = library ? library.ActiveRig : null;
            if (active && active.TryGetComponent(out MeshAvatar avatar) && avatar.mesh)
            {
                body = active.transform.position;
                bodyRadius = Mathf.Min(avatar.mesh.bounds.extents.x, avatar.mesh.bounds.extents.z) * 0.75f;
            }

            for (int i = sprinkles.Count - 1; i >= 0; i--)
            {
                Sprinkle s = sprinkles[i];
                if (!s.Resting) Fall(ref s.Position, ref s.Velocity, ref s.Spin, ref s.Rotation, ref s.Resting, 0.004f, false, dt);
                if (s.Position.y < -1.5f)
                {
                    sprinkles.RemoveAt(i);
                    continue;
                }
                if (deskOn)
                {
                    Push(ref s.Position, ref s.Velocity, ref s.Resting, left, vLeft, 0.075f, 0.033f, 0f);
                    Push(ref s.Position, ref s.Velocity, ref s.Resting, right, vRight, 0.075f, 0.033f, 0f);
                    Push(ref s.Position, ref s.Velocity, ref s.Resting, mouse, vMouse, 0.055f, 0.021f, 0f);
                }
                if (bodyRadius > 0f) KeepOut(ref s.Position, ref s.Resting, body, bodyRadius);
                sprinkles[i] = s;
            }

            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                Piece p = pieces[i];
                if (!p.Transform)
                {
                    pieces.RemoveAt(i);
                    continue;
                }
                Vector3 position = p.Transform.position;
                Quaternion rotation = p.Transform.rotation;
                if (!p.Resting)
                {
                    if (p.Ball) Roll(p, ref position, ref rotation, dt);
                    else Fall(ref position, ref p.Velocity, ref p.Spin, ref rotation, ref p.Resting, p.HalfThickness, true, dt, p.Upright);
                }
                if (deskOn)
                {
                    Push(ref position, ref p.Velocity, ref p.Resting, left, vLeft, 0.075f, 0.033f, p.Radius * 0.6f);
                    Push(ref position, ref p.Velocity, ref p.Resting, right, vRight, 0.075f, 0.033f, p.Radius * 0.6f);
                    Push(ref position, ref p.Velocity, ref p.Resting, mouse, vMouse, 0.055f, 0.021f, p.Radius * 0.6f);
                }
                if (bodyRadius > 0f) KeepOut(ref position, ref p.Resting, body, bodyRadius + p.Radius * 0.5f);
                p.Transform.SetPositionAndRotation(position, rotation);
                p.Transform.gameObject.SetActive(deskOn || !p.Resting);
                if (position.y < -1.5f)
                {
                    // Fell off the desk: thrown away, or (a topping) back to its slot, unseen.
                    pieces.RemoveAt(i);
                    if (p.Disposable) Destroy(p.Transform.gameObject);
                    else ReturnHome(p.Transform);
                }
            }
        }

        /// <summary>
        /// A ball (meatball, tomato chunk): falls, bounces, then rolls along the desk, slowing gently, spinning
        /// with its roll, and leaving a trail (sauce) behind it if it has one.
        /// </summary>
        void Roll(Piece p, ref Vector3 position, ref Quaternion rotation, float dt)
        {
            float before = position.y;
            p.Velocity += Vector3.down * gravity * dt;
            position += p.Velocity * dt;
            float ground = DeskScene.SurfaceHeight(position.x, position.z) + p.Radius;
            bool crossed = before >= ground - 1e-3f && position.y < ground;
            bool stepped = position.y < ground && position.y > ground - 0.06f;
            if (crossed || stepped)
            {
                position.y = ground;
                if (p.Velocity.y < -0.7f) p.Velocity = new Vector3(p.Velocity.x * 0.8f, -p.Velocity.y * 0.35f, p.Velocity.z * 0.8f);
                else
                {
                    p.Velocity.y = 0f;
                    p.Velocity *= Mathf.Exp(-1.6f * dt); // rolling: slows gently
                    if (p.HasTrail && Mess.Instance && Vector3.Distance(position, p.LastTrail) > 0.022f)
                    {
                        p.LastTrail = position;
                        Mess.Instance.DeskStain(position, p.Radius * Random.Range(0.45f, 0.7f), p.Trail, 150f, 0.05f, false,
                            new Vector2(p.Velocity.x, p.Velocity.z) * 2.5f); // smeared the way it rolls
                    }
                    if (p.Velocity.sqrMagnitude < 0.0004f)
                    {
                        p.Velocity = Vector3.zero;
                        p.Resting = true;
                    }
                }
            }
            Vector3 flat = new Vector3(p.Velocity.x, 0f, p.Velocity.z);
            if (flat.sqrMagnitude > 1e-6f)
                rotation = Quaternion.AngleAxis(flat.magnitude * dt / Mathf.Max(0.005f, p.Radius) * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, flat.normalized)) * rotation;
        }

        /// <summary>
        /// Gravity, tumbling, and landing on whatever's below: a hard landing bounces, a soft one slides to a
        /// stop and lies flat (sprinkles on their side, discs face up or down).
        /// </summary>
        void Fall(ref Vector3 position, ref Vector3 velocity, ref Vector3 spin, ref Quaternion rotation, ref bool resting,
            float halfThickness, bool disc, float dt, bool upright = false)
        {
            float before = position.y;
            velocity += Vector3.down * gravity * dt;
            position += velocity * dt;
            float angle = spin.magnitude * dt * Mathf.Rad2Deg;
            if (angle > 1e-3f) rotation = Quaternion.AngleAxis(angle, spin.normalized) * rotation;

            // Landed: crossed the surface this frame (however fast), or slid onto a step a little higher
            // (desk onto keyboard). Otherwise it's in the air, or has gone over an edge and below.
            float ground = DeskScene.SurfaceHeight(position.x, position.z) + halfThickness;
            bool crossed = before >= ground - 1e-3f && position.y < ground;
            bool stepped = position.y < ground && position.y > ground - 0.06f;
            if (!crossed && !stepped) return;
            position.y = ground;
            if (velocity.y < -0.7f)
            {
                velocity = new Vector3(velocity.x * 0.6f, -velocity.y * 0.3f, velocity.z * 0.6f);
                spin *= 0.5f;
                return;
            }
            velocity.y = 0f;
            velocity *= Mathf.Exp(-9f * dt);
            spin *= Mathf.Exp(-12f * dt);
            rotation = Quaternion.Slerp(rotation, Flat(rotation, disc, upright), 1f - Mathf.Exp(-14f * dt));
            if (velocity.sqrMagnitude < 0.0006f)
            {
                velocity = Vector3.zero;
                spin = Vector3.zero;
                rotation = Flat(rotation, disc, upright);
                resting = true;
            }
        }

        /// <summary>Lying down: a sprinkle on its side, a disc face up (or face down, whichever is nearer).</summary>
        static Quaternion Flat(Quaternion rotation, bool disc, bool upright = false)
        {
            if (upright) return Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            Vector3 axis = rotation * Vector3.up;
            if (disc) return Quaternion.FromToRotation(axis, axis.y >= 0f ? Vector3.up : Vector3.down) * rotation;
            Vector3 level = new Vector3(axis.x, 0f, axis.z);
            if (level.sqrMagnitude < 1e-6f) level = Vector3.right;
            return Quaternion.FromToRotation(axis, level.normalized) * rotation;
        }

        /// <summary>
        /// A hand or the mouse (centre, velocity, radius, half height) shoves what's under it out of the way,
        /// carrying it along; a quick downward tap pops it up and scatters it.
        /// </summary>
        static void Push(ref Vector3 position, ref Vector3 velocity, ref bool resting, Vector3 centre, Vector3 speed,
            float radius, float halfHeight, float extra)
        {
            float reach = radius + extra;
            if (centre.y - halfHeight > position.y + 0.02f || centre.y + halfHeight < position.y - 0.02f) return;
            Vector2 d = new Vector2(position.x - centre.x, position.z - centre.z);
            float distance = d.magnitude;
            if (distance >= reach) return;
            Vector2 away = distance > 1e-4f ? d / distance : Random.insideUnitCircle.normalized;
            position.x = centre.x + away.x * reach;
            position.z = centre.z + away.y * reach;
            Vector3 kick = new Vector3(speed.x, 0f, speed.z) * 0.9f + new Vector3(away.x, 0f, away.y) * 0.25f;
            if (speed.y < -0.3f) kick.y += Mathf.Min(-speed.y * 0.35f, 1.2f);
            velocity += kick;
            resting = false;
        }

        /// <summary>Nothing rests inside the avatar's base: low things get nudged out to its edge.</summary>
        static void KeepOut(ref Vector3 position, ref bool resting, Vector3 centre, float radius)
        {
            if (position.y > centre.y + 0.25f) return;
            Vector2 d = new Vector2(position.x - centre.x, position.z - centre.z);
            float distance = d.magnitude;
            if (distance >= radius) return;
            Vector2 away = distance > 1e-4f ? d / distance : Vector2.down;
            position.x = centre.x + away.x * radius;
            position.z = centre.z + away.y * radius;
            resting = false;
        }

        void LateUpdate()
        {
            if (batches == null || batches.Length == 0 || sprinkles.Count == 0 || !desk || !desk.Shown) return;
            for (int c = 0; c < batchCounts.Length; c++) batchCounts[c] = 0;
            foreach (Sprinkle s in sprinkles)
            {
                int c = s.Colour;
                batches[c][batchCounts[c]++] = Matrix4x4.TRS(s.Position, s.Rotation, sprinkleSize);
                if (batchCounts[c] == 1023)
                {
                    Graphics.RenderMeshInstanced(renderParams[c], capsule, 0, batches[c], 1023);
                    batchCounts[c] = 0;
                }
            }
            for (int c = 0; c < batches.Length; c++)
                if (batchCounts[c] > 0) Graphics.RenderMeshInstanced(renderParams[c], capsule, 0, batches[c], batchCounts[c]);
        }
    }
}
