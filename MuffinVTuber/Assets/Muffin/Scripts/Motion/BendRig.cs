using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Automatic bend rig for any object avatar. The bottom stays rooted, the top rides the streamer's
    /// head, and everything in between bends, squashes and stretches like a stem. Bones are generated at
    /// runtime from the mesh's height, so any model (Tripo GLB, STL, procedural) works without hand rigging.
    ///
    /// Zones: vertices below rootY bind to a fixed Root bone, vertices above headY to the Head bone, and
    /// the rest blend along a chain of Bend bones. Whole submeshes can be forced into the root or head zone
    /// (a flower's petals hang below its stem top but must stay rigid with the face).
    ///
    /// The bones are siblings, not a hierarchy, and each one's pose is computed directly along the curve,
    /// so squash-and-stretch scaling never shears down the chain.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BendRig : MonoBehaviour
    {
        [Header("Input")]
        public HeadPoseSource source;
        [Tooltip("Smooth tracker jitter with a One Euro filter. Turn off (F key) to see the raw input.")]
        public bool filterInput = true;
        [Tooltip("Lower = steadier when you hold still, but a little more lag on slow moves.")]
        [Range(0.05f, 5f)] public float minCutoff = 0.8f;
        [Tooltip("Higher = less lag on fast moves, but a little more jitter while moving.")]
        [Range(0f, 10f)] public float beta = 1.5f;
        [Tooltip("The same for turning and nodding.")]
        [Range(0.05f, 5f)] public float rotationMinCutoff = 0.8f;
        [Range(0f, 5f)] public float rotationBeta = 1.5f;
        // A source that smooths its own noise (the webcam) only gets a light pass that evens out its
        // 20-30 Hz steps, and a turn spring set by the Feel level ([ / ]) instead of turnFrequency.

        [Header("Chain")]
        [Range(3, 24)] public int chainBones = 10;

        [Header("Motion")]
        [Tooltip("Total stem bend at a full lean.")]
        [Range(0f, 120f)] public float maxBendDegrees = 70f;
        [Tooltip("How much leaning up/down stretches/scrunches the bend zone.")]
        [Range(0f, 0.8f)] public float stretchAmount = 0.35f;
        [Tooltip("How much of your head turn the face turns (1 = the same amount).")]
        [Range(0f, 2f)] public float headTurn = 1.1f;
        [Tooltip("Idle breathing so the avatar never looks frozen.")]
        [Range(0f, 0.05f)] public float breathing = 0.012f;

        [Header("Look-around physics")]
        [Tooltip("How much of a head turn the whole body twists along with (looking over your shoulder).")]
        [Range(0f, 1f)] public float bodyTwist = 0.7f;
        [Tooltip("How far the body leans toward where you look (lean units per radian of turn): a shoulder glance.")]
        [Range(0f, 1f)] public float glanceLean = 0.35f;
        [Tooltip("How far looking down hunches the body forward over the desk (looking up leans it back).")]
        [Range(0f, 2f)] public float lookBend = 0.9f;
        [Tooltip("Share of a nod the face adds on top of the hunch.")]
        [Range(0f, 1f)] public float headNod = 0.5f;
        [Tooltip("How fast the body catches up with the head (Hz). Lower = more drag behind the face.")]
        [Range(0.5f, 10f)] public float bodyFrequency = 3.8f;
        [Range(0.05f, 2f)] public float bodyDamping = 0.55f;

        [Header("Follow-through springs")]
        [Tooltip("How fast the top of the stem follows your head (Hz).")]
        [Range(0.5f, 15f)] public float tipFrequency = 7f;
        [Tooltip("How fast the bottom of the stem follows (Hz). Lower than the tip = the stem trails your head.")]
        [Range(0.5f, 15f)] public float baseFrequency = 3.2f;
        [Tooltip("Below 1 the stem overshoots and wobbles; 1 settles without overshoot.")]
        [Range(0.05f, 2f)] public float damping = 0.45f;
        [Range(0.5f, 15f)] public float stretchFrequency = 3.5f;
        [Range(0.05f, 2f)] public float stretchDamping = 0.3f;
        [Range(0.5f, 20f)] public float turnFrequency = 8f;
        [Range(0.05f, 2f)] public float turnDamping = 0.8f;

        [Header("Floppy tip (avatars built with a tip zone: a pizza slice's point, a leaf)")]
        [Tooltip("How fast the floppy tip springs back (Hz). Low = lazy and floppy.")]
        [Range(0.3f, 8f)] public float flopFrequency = 2f;
        [Tooltip("Below 1 it wobbles; low values keep it jiggling for a while after you stop.")]
        [Range(0.02f, 1.5f)] public float flopDamping = 0.16f;
        [Tooltip("How much gravity drags the tip further over once it's tipped (0 = none).")]
        [Range(0f, 2f)] public float flopGravity = 0.7f;
        [Tooltip("The tip's resting curl toward the camera, in degrees (negative curls it away).")]
        [Range(-60f, 60f)] public float flopDroop = 8f;
        [Range(0f, 179f)] public float maxFlopDegrees = 140f;
        [Range(2, 12)] public int flopBones = 6;
        [Tooltip("How hard starting or stopping a turn (or a nod) flings the tip.")]
        [Range(0f, 4f)] public float turnWhip = 1.6f;
        [Tooltip("The tip only sways side to side (no flopping forward and back): a pile of noodles.")]
        public bool flopSideways;

        /// <summary>The filtered pose this frame (what the face reads for mouth and blinks).</summary>
        public HeadPose Current { get; private set; } = HeadPose.Rest;
        public bool Tracking { get; private set; }
        public Transform HeadBone => head;
        /// <summary>Squash and stretch of the rigid head (the cartoon talk bounce sets this).</summary>
        public Vector3 HeadScale { get; set; } = Vector3.one;
        public bool HasFloppyTip => flop != null;
        /// <summary>The floppy tip's bones, base to point (null without a tip).</summary>
        public Transform[] FloppyBones => flop;
        /// <summary>
        /// 0..1: folds the avatar forward onto its desk, overriding your lean (the R "face slam"). The springs
        /// do the rest: it whips down, slaps, and wobbles back up when this returns to 0.
        /// </summary>
        public float Slam { get; set; }

        /// <summary>Squash it from above (something fell on its head): 0.3 is a good bonk.</summary>
        public void Bonk(float amount) => bonk = Mathf.Max(bonk, amount);

        /// <summary>
        /// Fling the floppy tip's end (m/s, the avatar's own space): a big flop on cue. A rigid avatar without a tip
        /// is nudged into a rock instead, and a jelly one wobbles all over.
        /// </summary>
        public void KickTip(Vector3 velocity)
        {
            if (flop != null) flopVelocity += velocity;
            else if (rollDegrees > 0f)
            {
                kickLean += new Vector3(velocity.x, 0f, velocity.z) * (0.08f + 0.25f * rollJiggle);
                jiggleVelocity -= velocity.magnitude * 0.8f * rollJiggle;
            }
        }
        float bonk;
        [Tooltip("How far the face slam folds the avatar forward, in degrees.")]
        [Range(30f, 120f)] public float slamDegrees = 86f;

        [Header("Rigid roll (an egg): no bending or squashing, it rocks as one piece on its round bottom")]
        [Tooltip("Rock at a full lean, in degrees. 0 = bend as usual.")]
        [Range(0f, 60f)] public float rollDegrees;
        [Tooltip("Radius of the round bottom it rolls on (metres): rocking moves it along by radius × angle.")]
        public float rollRadius = 0.2f;
        [Range(0.5f, 6f)] public float rollFrequency = 2.2f;
        [Tooltip("Low: it rocks back and forth a few times before it settles, like a weeble.")]
        [Range(0.05f, 1.5f)] public float rollDamping = 0.25f;
        [Tooltip("How much of your head turn the whole thing turns.")]
        [Range(0f, 1.5f)] public float rollTurn = 0.7f;
        [Tooltip("Jelly: the whole body squashes and stretches on a wobbly spring when it's jolted, bonked or kicked (0 = solid).")]
        [Range(0f, 1.5f)] public float rollJiggle;
        [Range(1f, 8f)] public float jiggleFrequency = 3.5f;
        [Range(0.03f, 1f)] public float jiggleDamping = 0.12f;
        SpringVector3 rollSpring;
        float jiggle, jiggleVelocity, lastRollDegrees, lastRollRate;
        Vector3 kickLean;

        Transform root, head;
        Transform[] chain;
        float segment;
        bool built, preFiltered;

        // Floppy tip: bones from tipY to the top, bent toward a simulated tip end that lags, swings and sags.
        Transform[] flop;
        float flopSegment, headToTip, flopLength;
        float rootRestY, chainStartY, headRestY, tipRestY;
        Vector3 flopEnd, flopVelocity, lastFacing, lastFacingVelocity;
        bool flopPrimed;

        /// <summary>How far the floppy tip is curled right now, in degrees (the self-test measures it).</summary>
        public float FlopDegrees { get; private set; }

        readonly OneEuroVector3 offsetFilter = new OneEuroVector3(1f, 1.5f);
        readonly OneEuroRotation rotationFilter = new OneEuroRotation(1f, 1.5f);
        SpringVector3[] leanSprings;
        SpringFloat stretchSpring;
        SpringVector3 turnSpring, bodySpring;

        /// <summary>
        /// Rig a mesh using fractions of its height for the rooted bottom and rigid top. (Named differently
        /// from Build on purpose: as an overload, C# picked it again for its own inner call.)
        /// </summary>
        public SkinnedMeshRenderer BuildFromFractions(Mesh mesh, Material[] materials, float rootFraction, float headFraction,
            float tipFraction = 1f)
        {
            Bounds b = mesh.bounds;
            float tipY = tipFraction < 0.99f ? b.min.y + b.size.y * tipFraction : float.PositiveInfinity;
            return Build(mesh, materials, b.min.y + b.size.y * rootFraction, b.min.y + b.size.y * headFraction, null, null, tipY);
        }

        /// <summary>
        /// Rig a mesh. rootY/headY are heights in the mesh's own space. With a tipY (above headY), everything
        /// above it is a floppy tip that hangs off the head and swings on its own.
        /// </summary>
        public SkinnedMeshRenderer Build(Mesh mesh, Material[] materials, float rootY, float headY,
            int[] rootSubmeshes = null, int[] headSubmeshes = null, float tipY = float.PositiveInfinity)
        {
            if (built) throw new System.InvalidOperationException("BendRig is already built.");
            chainBones = Mathf.Max(3, chainBones);
            headY = Mathf.Max(headY, rootY + 1e-3f);
            segment = (headY - rootY) / (chainBones - 1);
            float topY = mesh.bounds.max.y;
            bool floppy = tipY > headY + 1e-3f && tipY < topY - 1e-3f;

            root = MakeBone("Root", new Vector3(0f, mesh.bounds.min.y, 0f));
            rootRestY = mesh.bounds.min.y;
            chainStartY = rootY;
            headRestY = headY;
            tipRestY = tipY;
            chain = new Transform[chainBones];
            for (int i = 0; i < chainBones; i++) chain[i] = MakeBone($"Bend {i}", new Vector3(0f, rootY + segment * i, 0f));
            head = MakeBone("Head", new Vector3(0f, headY, 0f));
            if (floppy)
            {
                flopBones = Mathf.Max(2, flopBones);
                flopSegment = (topY - tipY) / (flopBones - 1);
                headToTip = tipY - headY;
                flopLength = topY - tipY;
                flop = new Transform[flopBones];
                for (int i = 0; i < flopBones; i++) flop[i] = MakeBone($"Tip {i}", new Vector3(0f, tipY + flopSegment * i, 0f));
            }

            var bones = new Transform[chainBones + 2 + (floppy ? flopBones : 0)];
            bones[0] = root;
            chain.CopyTo(bones, 1);
            int headIndex = chainBones + 1;
            bones[headIndex] = head;
            int tipIndex = headIndex + 1;
            if (floppy) flop.CopyTo(bones, tipIndex);
            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) bindposes[i] = bones[i].worldToLocalMatrix * transform.localToWorldMatrix;

            Mesh skinned = Instantiate(mesh);
            skinned.name = mesh.name + " (bend rig)";
            Vector3[] vertices = skinned.vertices;
            var forced = new Dictionary<int, int>(); // vertex → bone index
            ForceSubmeshes(skinned, rootSubmeshes, 0, forced);
            ForceSubmeshes(skinned, headSubmeshes, headIndex, forced);

            var weights = new BoneWeight[vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                float y = vertices[v].y;
                if (forced.TryGetValue(v, out int bone)) weights[v] = new BoneWeight { boneIndex0 = bone, weight0 = 1f };
                else if (floppy && y >= tipY)
                {
                    float f = (y - tipY) / flopSegment;
                    int j = Mathf.Min(Mathf.FloorToInt(f), flopBones - 2);
                    float w = Mathf.Clamp01(f - j);
                    weights[v] = new BoneWeight { boneIndex0 = tipIndex + j, weight0 = 1f - w, boneIndex1 = tipIndex + j + 1, weight1 = w };
                }
                else if (y <= rootY) weights[v] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                else if (y >= headY) weights[v] = new BoneWeight { boneIndex0 = headIndex, weight0 = 1f };
                else
                {
                    float f = (y - rootY) / segment;
                    int j = Mathf.Min(Mathf.FloorToInt(f), chainBones - 2);
                    float w = f - j;
                    weights[v] = new BoneWeight { boneIndex0 = 1 + j, weight0 = 1f - w, boneIndex1 = 2 + j, weight1 = w };
                }
            }
            skinned.boneWeights = weights;
            skinned.bindposes = bindposes;

            // TryGetComponent, not ??: Unity's "missing component" objects aren't real nulls in the editor.
            if (!TryGetComponent(out SkinnedMeshRenderer smr)) smr = gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = skinned;
            smr.bones = bones;
            smr.rootBone = transform;
            smr.sharedMaterials = materials;
            Bounds local = skinned.bounds;
            local.Expand(local.size.magnitude); // room for full bends and stretches
            smr.localBounds = local;

            leanSprings = new SpringVector3[chainBones - 1];
            for (int i = 0; i < leanSprings.Length; i++) leanSprings[i] = new SpringVector3(tipFrequency, damping, 1f, Vector3.zero);
            stretchSpring = new SpringFloat(stretchFrequency, stretchDamping, 1f, 1f);
            turnSpring = new SpringVector3(turnFrequency, turnDamping, 1f, Vector3.zero);
            bodySpring = new SpringVector3(bodyFrequency, bodyDamping, 1f, Vector3.zero);
            rollSpring = new SpringVector3(rollFrequency, rollDamping, 1f, Vector3.zero);
            built = true;
            return smr;
        }

        /// <summary>
        /// The bone that carries the mesh at height y (mesh space), and where that bone sits at rest: something
        /// stuck on the avatar there (a pepperoni) can ride along as its child, at (point - rest).
        /// </summary>
        public Transform BoneAt(float y, out Vector3 rest)
        {
            if (flop != null && y >= tipRestY)
            {
                int i = Mathf.Clamp(Mathf.RoundToInt((y - tipRestY) / flopSegment), 0, flop.Length - 1);
                rest = new Vector3(0f, tipRestY + flopSegment * i, 0f);
                return flop[i];
            }
            if (y >= headRestY)
            {
                rest = new Vector3(0f, headRestY, 0f);
                return head;
            }
            if (y <= chainStartY)
            {
                rest = new Vector3(0f, rootRestY, 0f);
                return root;
            }
            int c = Mathf.Clamp(Mathf.RoundToInt((y - chainStartY) / segment), 0, chain.Length - 1);
            rest = new Vector3(0f, chainStartY + segment * c, 0f);
            return chain[c];
        }

        // Switching back to this avatar: the floppy tip starts at rest instead of where it was left.
        void OnEnable() => flopPrimed = false;

        /// <summary>
        /// An extra bone for part of the mesh, say an animal's arm, so its own paws can move (a hamster typing along
        /// with you). Vertices within radius of centre (mesh space) follow it, fading out over the outer half; it hangs
        /// off the bone that carries pivot and turns about pivot. Only what lies in front of frontOf (z, mesh space) goes
        /// with it, so an arm held close against a belly takes the arm and not the belly. Call after Build; turn it
        /// with its localRotation.
        /// </summary>
        public Transform AddLimb(string name, Vector3 pivot, Vector3 centre, float radius, float frontOf = float.PositiveInfinity)
        {
            if (!built || !TryGetComponent(out SkinnedMeshRenderer smr) || !smr.sharedMesh) return null;
            Mesh mesh = smr.sharedMesh;
            Transform parent = BoneAt(pivot.y, out Vector3 rest);
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = pivot - rest;
            bone.localRotation = Quaternion.identity;

            var bones = new List<Transform>(smr.bones);
            int index = bones.Count;
            bones.Add(bone);
            // Every bone rests unturned in the avatar's own space, so its bind pose is just "move back from pivot".
            var bindposes = new List<Matrix4x4>(mesh.bindposes) { Matrix4x4.Translate(-pivot) };
            Vector3[] v = mesh.vertices;
            BoneWeight[] w = mesh.boneWeights;
            for (int i = 0; i < v.Length; i++)
            {
                float d = (v[i] - centre).magnitude;
                if (d >= radius) continue;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius, radius * 0.5f, d));
                if (!float.IsPositiveInfinity(frontOf)) k *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(frontOf + 0.015f, frontOf - 0.01f, v[i].z));
                if (k <= 0.001f) continue;
                BoneWeight bw = w[i];
                bw.weight0 *= 1f - k;
                bw.weight1 *= 1f - k;
                bw.boneIndex2 = index;
                bw.weight2 = k;
                bw.weight3 = 0f;
                w[i] = bw;
            }
            mesh.bindposes = bindposes.ToArray();
            mesh.boneWeights = w;
            smr.bones = bones.ToArray();
            return bone;
        }

        /// <summary>
        /// A stretchy cartoon arm made of the model's own arm piece (the separate mesh nearest <paramref name="paw"/>;
        /// AI models usually build arms that way, so nothing of the belly it rests on comes along). The arm turns
        /// from <paramref name="shoulder"/> toward wherever the paw is sent and stretches to reach it, the paw carried
        /// on the end; the end in the shoulder stays with the body. Points are the rest pose, avatar space. Returns
        /// null if the rig isn't built or there's no separate piece there.
        /// </summary>
        public StretchArm AddStretchArm(string name, Vector3 shoulder, Vector3 paw)
        {
            if (!built || !TryGetComponent(out SkinnedMeshRenderer smr) || !smr.sharedMesh) return null;
            Mesh mesh = smr.sharedMesh;
            Vector3[] v = mesh.vertices;
            int[] piece = MeshPieces.Find(mesh, v, out int count);
            int nearest = 0;
            for (int i = 1; i < v.Length; i++)
                if ((v[i] - paw).sqrMagnitude < (v[nearest] - paw).sqrMagnitude) nearest = i;
            int arm = piece[nearest];
            int size = 0;
            for (int i = 0; i < v.Length; i++) if (piece[i] == arm) size++;
            if (count < 2 || size > v.Length / 3) return null; // the body itself: no separate arm to move

            Vector3 axis = paw - shoulder;
            Quaternion along = Quaternion.LookRotation(axis.normalized, Vector3.up);
            Transform armParent = BoneAt(shoulder.y, out Vector3 armRest), pawParent = BoneAt(paw.y, out Vector3 pawRest);
            var upper = new GameObject(name).transform;
            upper.SetParent(armParent, false);
            upper.localPosition = shoulder - armRest;
            upper.localRotation = along;
            var hand = new GameObject(name + " paw").transform;
            hand.SetParent(pawParent, false);
            hand.localPosition = paw - pawRest;
            hand.localRotation = Quaternion.identity;

            var bones = new List<Transform>(smr.bones);
            int upperIndex = bones.Count, handIndex = bones.Count + 1;
            bones.Add(upper);
            bones.Add(hand);
            var bindposes = new List<Matrix4x4>(mesh.bindposes)
            {
                Matrix4x4.TRS(shoulder, along, Vector3.one).inverse,
                Matrix4x4.Translate(-paw),
            };
            BoneWeight[] w = mesh.boneWeights;
            float length2 = Mathf.Max(axis.sqrMagnitude, 1e-6f);
            for (int i = 0; i < v.Length; i++)
            {
                if (piece[i] != arm) continue;
                float t = Vector3.Dot(v[i] - shoulder, axis) / length2;
                // The end in the shoulder stays with the body; past the wrist it's all paw.
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.35f, t));
                float pawShare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 0.9f, t));
                if (k <= 0.001f) continue;
                BoneWeight bw = w[i];
                bw.weight0 *= 1f - k;
                bw.weight1 *= 1f - k;
                bw.boneIndex2 = upperIndex;
                bw.weight2 = k * (1f - pawShare);
                bw.boneIndex3 = handIndex;
                bw.weight3 = k * pawShare;
                w[i] = bw;
            }
            mesh.bindposes = bindposes.ToArray();
            mesh.boneWeights = w;
            smr.bones = bones.ToArray();
            smr.quality = SkinQuality.Bone4;
            return new StretchArm(upper, hand, shoulder - armRest, paw - pawRest, along, axis.normalized);
        }

        /// <summary>One stretchy arm (see AddStretchArm): send its paw somewhere each frame with Reach.</summary>
        public sealed class StretchArm
        {
            readonly Transform upper, hand;
            readonly Vector3 shoulderLocal, pawLocal, axis;
            readonly Quaternion along;
            /// <summary>How much the paw turns with the arm (0 keeps its rest angle, 1 turns as much as the arm).</summary>
            public float PawTurn = 0.35f;
            /// <summary>The longest it will stretch, times its rest length.</summary>
            public float MaxStretch = 3.2f;

            internal StretchArm(Transform upper, Transform hand, Vector3 shoulderLocal, Vector3 pawLocal, Quaternion along, Vector3 axis)
            {
                this.upper = upper;
                this.hand = hand;
                this.shoulderLocal = shoulderLocal;
                this.pawLocal = pawLocal;
                this.along = along;
                this.axis = axis;
            }

            /// <summary>Where the paw is at rest (world), as the body is posed now.</summary>
            public Vector3 RestPaw => hand && hand.parent ? hand.parent.TransformPoint(pawLocal) : Vector3.zero;

            /// <summary>
            /// The paw goes <paramref name="amount"/> of the way from its rest place to <paramref name="target"/>
            /// (world; past 1 overshoots), the arm stretching to reach. Call every frame (0 = at rest).
            /// </summary>
            public void Reach(Vector3 target, float amount, Vector3 nudge = default)
            {
                if (!upper || !hand) return;
                Transform armParent = upper.parent, pawParent = hand.parent;
                Vector3 shoulder = armParent.TransformPoint(shoulderLocal), rest = pawParent.TransformPoint(pawLocal);
                Vector3 goal = Vector3.LerpUnclamped(rest, target, amount) + nudge;
                Vector3 restAxis = armParent.TransformDirection(axis);
                float restLength = Mathf.Max((rest - shoulder).magnitude, 1e-4f);
                Vector3 to = goal - shoulder;
                float length = Mathf.Min(to.magnitude, restLength * MaxStretch);
                if (to.sqrMagnitude < 1e-8f) to = restAxis;
                goal = shoulder + to.normalized * length;
                Quaternion turn = Quaternion.FromToRotation(restAxis, to.normalized);
                upper.localPosition = shoulderLocal;
                upper.rotation = turn * armParent.rotation * along;
                upper.localScale = new Vector3(1f, 1f, length / restLength);
                hand.position = goal;
                hand.rotation = Quaternion.Slerp(Quaternion.identity, turn, PawTurn) * pawParent.rotation;
            }
        }

        Transform MakeBone(string name, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            t.localPosition = localPosition;
            return t;
        }

        static void ForceSubmeshes(Mesh mesh, int[] submeshes, int bone, Dictionary<int, int> forced)
        {
            if (submeshes == null) return;
            foreach (int s in submeshes)
            {
                if (s < 0 || s >= mesh.subMeshCount) continue;
                foreach (int v in mesh.GetTriangles(s)) forced[v] = bone;
            }
        }

        void Update()
        {
            if (!built) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            HeadPose raw = HeadPose.Rest;
            Tracking = source != null && source.isActiveAndEnabled && source.TryGetPose(out raw);
            preFiltered = source != null && source.PreFiltered;
            if (!Tracking) raw = HeadPose.Rest;

            HeadPose pose = raw;
            if (filterInput)
            {
                bool light = preFiltered;
                offsetFilter.MinCutoff = light ? Feel.StepCutoff : minCutoff;
                offsetFilter.Beta = light ? 1f : beta;
                rotationFilter.MinCutoff = light ? Feel.StepCutoff : rotationMinCutoff;
                rotationFilter.Beta = light ? Feel.StepBeta : rotationBeta;
                pose.Offset = offsetFilter.Filter(raw.Offset, dt);
                pose.Rotation = rotationFilter.Filter(raw.Rotation, dt);
            }
            else
            {
                offsetFilter.Reset();
                rotationFilter.Reset();
            }
            Current = pose;
            ApplyPose(pose, dt);
        }

        void ApplyPose(HeadPose pose, float dt)
        {
            float speed = Feel.SpringSpeed; // [ / ]: the jiggle stays, it just catches up faster or slower
            // Where the head looks, as a rotation vector (x: + looking up, y: turning).
            Vector3 look = RotationMath.ToVector(pose.Rotation);

            // The avatar faces -Z (toward the camera): x leans to screen right, +z leans toward the viewer.
            // Looking down hunches it forward over its desk and looking up leans it back; this goes through
            // the same springy segments as leaning, so it trails and jiggles the same way.
            // The body turns with the head when you look over your shoulder, a beat behind it (drag), and
            // leans a little toward what you're looking at (another screen, chat): a glance with the
            // shoulders, not a full turn. Turning by +yaw faces the avatar toward screen left (-x).
            bodySpring.SetParameters(bodyFrequency * speed, bodyDamping, 1f);
            Vector3 bodyLook = bodySpring.Update(look, dt);
            float twistDegrees = bodyLook.y * Mathf.Rad2Deg * bodyTwist;

            Vector3 lean = new Vector3(pose.Offset.x - bodyLook.y * glanceLean, 0f, -pose.Offset.z + look.x * lookBend * 1.15f);
            lean = Vector3.ClampMagnitude(lean, 1.25f);
            if (Slam > 0f) lean = Vector3.Lerp(lean, new Vector3(0f, 0f, -1f), Mathf.Clamp01(Slam)); // -z: toward the camera

            float breathe = breathing * Mathf.Sin(Time.time * Mathf.PI * 0.5f); // one breath every 4 s
            bonk *= Mathf.Exp(-dt * 9f); // a bonk on the head squashes it for a moment; the springs wobble it back
            if (rollDegrees > 0f)
            {
                ApplyRoll(lean, look, dt, speed);
                return;
            }
            float stretchTarget = 1f + Mathf.Clamp(pose.Offset.y, -1f, 1f) * stretchAmount + breathe - bonk;
            stretchSpring.SetParameters(stretchFrequency * speed, stretchDamping, 1f);
            float stretch = Mathf.Clamp(stretchSpring.Update(stretchTarget, dt), 0.3f, 2.2f);
            float thin = 1f / Mathf.Sqrt(stretch); // keep volume: stretched = thinner, squashed = fatter
            float length = segment * stretch;

            int segments = chainBones - 1;
            float degreesPerSegment = Mathf.Lerp(maxBendDegrees, slamDegrees, Mathf.Clamp01(Slam)) / segments;
            Quaternion rotation = Quaternion.identity;
            Vector3 position = chain[0].localPosition;
            chain[0].localRotation = Quaternion.identity;
            chain[0].localScale = new Vector3(1f, stretch, 1f);

            for (int i = 1; i < chainBones; i++)
            {
                // Segments near the head follow fast, segments near the base lag: the stem trails the face,
                // then catches up and settles, which is what reads as fluid instead of rigid.
                float t = segments > 1 ? (i - 1f) / (segments - 1f) : 1f;
                SpringVector3 spring = leanSprings[i - 1];
                spring.SetParameters(Mathf.Lerp(baseFrequency, tipFrequency, t) * speed, damping, 1f);
                Vector3 bend = spring.Update(lean, dt);

                Quaternion previous = rotation;
                float amount = bend.magnitude;
                if (amount > 1e-5f)
                {
                    Vector3 axis = Vector3.Cross(Vector3.up, bend / amount);
                    rotation = Quaternion.AngleAxis(amount * degreesPerSegment, axis) * rotation;
                }
                position += Quaternion.Slerp(previous, rotation, 0.5f) * (Vector3.up * length);

                float along = (float)i / segments;
                float thickness = Mathf.Lerp(1f, thin, along);
                chain[i].localPosition = position;
                // Twist grows up the body (none at the base), around the body's own axis.
                chain[i].localRotation = rotation * Quaternion.AngleAxis(twistDegrees * along, Vector3.up);
                chain[i].localScale = new Vector3(thickness, stretch, thickness);
            }

            // The face turns the rest of the way on top of the body's twist, and adds part of a nod on top
            // of the hunch (the hunch already tips the head forward).
            // Webcam: no "response" kick (r 0), which would turn every tracker step into a twitch.
            if (preFiltered) turnSpring.SetParameters(Feel.TurnFrequency, turnDamping, 0f);
            else turnSpring.SetParameters(turnFrequency * speed, turnDamping, 1f);
            Vector3 headLook = turnSpring.Update(look, dt) * headTurn;
            headLook.x *= headNod;
            headLook.y -= bodyLook.y * bodyTwist;
            head.localPosition = position;
            head.localRotation = rotation * Quaternion.AngleAxis(twistDegrees, Vector3.up) * RotationMath.FromVector(headLook);
            head.localScale = HeadScale;
            if (flop != null) ApplyFlop(dt);
        }

        /// <summary>
        /// Rigid roll: every bone moves as one piece. It tips toward your lean around the centre of its round
        /// bottom, rolling along by radius × angle like a ball, and turns with your head; a soft spring makes
        /// it rock past and back a few times. A bonk rocks it instead of squashing it.
        /// </summary>
        void ApplyRoll(Vector3 lean, Vector3 look, float dt, float speed)
        {
            rollSpring.SetParameters(rollFrequency * speed, rollDamping, 1f);
            kickLean *= Mathf.Exp(-dt * 10f);
            Vector3 b = rollSpring.Update(lean + kickLean + new Vector3(0f, 0f, -bonk * 1.5f), dt);
            float degrees = b.magnitude * Mathf.Lerp(rollDegrees, slamDegrees, Mathf.Clamp01(Slam));
            // Jelly: squash and stretch all over, about the base, on a wobbly spring. A bonk squashes it; starting or
            // stopping a rock jolts it.
            float rate = (degrees - lastRollDegrees) / dt;
            if (rollJiggle > 0f) jiggleVelocity -= Mathf.Abs(rate - lastRollRate) * 0.0015f * rollJiggle;
            lastRollDegrees = degrees;
            lastRollRate = rate;
            float omegaJ = 2f * Mathf.PI * jiggleFrequency, targetJ = -bonk * 0.6f * rollJiggle;
            int stepsJ = Mathf.Clamp(Mathf.CeilToInt(dt * 240f), 1, 8);
            for (int s = 0; s < stepsJ; s++)
            {
                float h = dt / stepsJ;
                jiggleVelocity += (-omegaJ * omegaJ * (jiggle - targetJ) - 2f * jiggleDamping * omegaJ * jiggleVelocity) * h;
                jiggle += jiggleVelocity * h;
            }
            jiggle = Mathf.Clamp(jiggle, -0.25f, 0.3f);
            float stretch = 1f + jiggle, thin = 1f / Mathf.Sqrt(stretch);
            float baseY = rootRestY;
            Vector3 direction = b.sqrMagnitude > 1e-10f ? b.normalized : Vector3.zero;
            Quaternion tip = degrees > 1e-4f ? Quaternion.AngleAxis(degrees, Vector3.Cross(Vector3.up, direction)) : Quaternion.identity;
            if (preFiltered) turnSpring.SetParameters(Feel.TurnFrequency, turnDamping, 0f);
            else turnSpring.SetParameters(turnFrequency * speed, turnDamping, 1f);
            Vector3 headLook = turnSpring.Update(look, dt);
            Quaternion r = tip * Quaternion.AngleAxis(headLook.y * Mathf.Rad2Deg * rollTurn, Vector3.up);
            var centre = new Vector3(0f, rootRestY + rollRadius, 0f);
            Vector3 along = direction * (rollRadius * degrees * Mathf.Deg2Rad);
            void Place(Transform bone, float restY)
            {
                // Squashed or stretched about the base (jelly), then rocked as one piece round the round bottom.
                var squashed = new Vector3(0f, baseY + (restY - baseY) * stretch, 0f);
                bone.localPosition = centre + along + r * (squashed - centre);
                bone.localRotation = r;
                bone.localScale = new Vector3(thin, stretch, thin);
            }
            Place(root, rootRestY);
            for (int i = 0; i < chainBones; i++) Place(chain[i], chainStartY + segment * i);
            Place(head, headRestY);
            if (flop != null) ApplyFlop(dt);
        }

        /// <summary>
        /// The floppy tip: its far end is a weight on a springy rod (rig space). When the head moves, the end
        /// lags behind and swings; when the head tips over, gravity drags it further. The tip bones then curl
        /// from the head's direction toward that end, more and more toward the point.
        /// </summary>
        void ApplyFlop(float dt)
        {
            Quaternion headRotation = head.localRotation;
            Vector3 up = headRotation * Vector3.up;
            // A rigid avatar (roll mode) doesn't stretch when it talks, so neither does the base of its floppy top.
            float headStretch = rollDegrees > 0f ? 1f : HeadScale.y;
            Vector3 basePosition = head.localPosition + headRotation * new Vector3(0f, headToTip * headStretch, 0f);
            Vector3 restDirection = headRotation * (Quaternion.AngleAxis(-flopDroop, Vector3.right) * Vector3.up);
            Vector3 rest = basePosition + restDirection * flopLength;
            Vector3 facing = headRotation * Vector3.back; // the face looks toward -z
            if (!flopPrimed)
            {
                flopEnd = rest;
                flopVelocity = Vector3.zero;
                lastFacing = facing;
                lastFacingVelocity = Vector3.zero;
                flopPrimed = true;
            }
            // Whip: when a turn or nod starts, the tip's end is left behind, and when it stops the end carries
            // on past (a change in how fast the head turns kicks the end the other way). Turning on the spot
            // flops it, not just leaning.
            Vector3 facingVelocity = (facing - lastFacing) / dt;
            Vector3 facingKick = facingVelocity - lastFacingVelocity;
            flopVelocity -= facingKick * (flopLength * turnWhip);
            lastFacing = facing;
            lastFacingVelocity = facingVelocity;


            float omega = 2f * Mathf.PI * flopFrequency;
            Vector3 gravity = Vector3.down * (flopGravity * omega * omega * flopLength * 0.5f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 240f), 1, 8);
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                Vector3 accel = omega * omega * (rest - flopEnd) - 2f * flopDamping * omega * flopVelocity + gravity;
                flopVelocity += accel * h;
                flopEnd += flopVelocity * h;
                // It's a rod, not a rubber band: keep its length, and drop any speed along it.
                Vector3 d = flopEnd - basePosition;
                float m = d.magnitude;
                if (m < 1e-5f) continue;
                Vector3 n = d / m;
                if (flopSideways)
                {
                    // Held to the side-to-side plane: no swinging toward or away from the camera.
                    Vector3 forward = headRotation * Vector3.forward;
                    n = (n - Vector3.Dot(n, forward) * forward).normalized;
                    flopVelocity -= Vector3.Dot(flopVelocity, forward) * forward;
                }
                flopEnd = basePosition + n * flopLength;
                flopVelocity -= Vector3.Dot(flopVelocity, n) * n;
            }

            Vector3 toEnd = (flopEnd - basePosition).normalized;
            float angle = Mathf.Min(Vector3.Angle(up, toEnd) * 2f, maxFlopDegrees); // a curl's chord is half its turn
            FlopDegrees = angle;
            Vector3 axis = Vector3.Cross(up, toEnd);
            Quaternion previous = headRotation;
            Vector3 position = basePosition;
            for (int i = 0; i < flop.Length; i++)
            {
                float t = flop.Length > 1 ? (float)i / (flop.Length - 1) : 1f;
                // More curl toward the point: the base of the tip stays with the head, the point flops.
                Quaternion rotation = axis.sqrMagnitude > 1e-8f
                    ? Quaternion.AngleAxis(angle * Mathf.Pow(t, 1.4f), axis.normalized) * headRotation
                    : headRotation;
                if (i > 0) position += Quaternion.Slerp(previous, rotation, 0.5f) * (Vector3.up * flopSegment);
                flop[i].localPosition = position;
                flop[i].localRotation = rotation;
                flop[i].localScale = Vector3.one;
                previous = rotation;
            }
        }
    }
}
