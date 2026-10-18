using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Muffin
{
    /// <summary>What kind of mess an avatar makes.</summary>
    public enum MessKind { Egg, Trash, Juice, Meatballs, Vacuum, HotDog, Popcorn, Jello, Taco, Mold, Toaster, Ketchup, Coffee, Toilet }

    /// <summary>App-wide sound switch (the control panel's "Sounds").</summary>
    public static class Sound
    {
        public static bool On
        {
            get => PlayerPrefs.GetInt("muffin.sound", 1) == 1;
            set => PlayerPrefs.SetInt("muffin.sound", value ? 1 : 0);
        }
    }

    /// <summary>
    /// An avatar's own mess, onto the desk (DeskDebris, Mess): whenever it moves (turning counts, see
    /// MotionMeter), more when it's shaken, now and then on its own, on !shake (Burst) and on the face slam:
    /// - Egg: things fall from the ceiling (a frying pan, BONK) and it cracks: yolk and shell on the desk, and
    ///   for a while after it leaks yolk as it moves.
    /// - Trash: garbage tumbles out of the top (paper balls roll).
    /// - Juice: juice sloshes over the rim and puddles, and runs down the cup's front.
    /// - Meatballs: meatballs fall out and roll, leaving sauce trails.
    /// - Vacuum: (its hose, VacuumHose, sucks things up) every so often it blows a dust cloud
    ///   (and, sounds on, a rude noise).
    /// - Hot dog: mustard and ketchup fly off and drip down the bun, relish bits scatter, and now and then the
    ///   sausage (and the top of the bun with it) flops right over, flinging condiments.
    /// - Popcorn: kernels keep popping out of the top (more as it moves), now and then a popping fit, and they
    ///   pile up on the desk.
    /// - Jello: it shivers and jiggles on its own, and moving flings jelly chunks and blobs of cream off it.
    /// - Taco: the filling falls out as it moves (lettuce, cheese, beef crumbles, tomato) and salsa drips.
    /// - Mouldy bread: puffs of spores (more as it moves), stale crumbs, faint spore marks on the desk.
    /// - Toaster: close your eyes hard (a long blink) and the toast pops out onto the desk; crumbs as it moves.
    /// - Ketchup bottle: open your mouth and it squirts (squashing as it does), dribbling while it stays open.
    /// - Coffee mug: it steams all the time and sloshes coffee over the rim as it moves.
    /// - Toilet: talking sloshes water up out of the bowl; it slops as it moves and flushes now and then.
    /// </summary>
    [RequireComponent(typeof(BendRig))]
    public sealed class MessMaker : MonoBehaviour, IMessy
    {
        public MessKind kind;
        [Tooltip("Lit material, tinted per item (garbage, meatballs, shell, the pan).")]
        public Material lit;
        [Tooltip("Smoke shader material (the vacuum's dust clouds).")]
        public Material smoke;
        [Tooltip("The top's radius and height (metres): where things come off.")]
        public float topRadius = 0.25f, topHeight = 0.15f;
        [Tooltip("How far the real top is above the bone things come off from (rigid avatars: the head bone sits well below the rim).")]
        public float topLift;
        [Tooltip("Toilet: the middle of the bowl's water, in the avatar's own (mesh) space.")]
        public Vector3 bowl;
        Transform bowlBone;
        Vector3 bowlLocal;
        [Tooltip("Shake (m/s² at the top) above which things come off faster.")]
        public float shakeThreshold = 6f;
        [Tooltip("Something comes off on its own every this many seconds (random in between).")]
        public Vector2 idleEvery = new Vector2(20f, 45f);

        static readonly Color Yolk = new Color(1f, 0.72f, 0.08f, 0.95f), White = new Color(1f, 1f, 0.94f, 0.55f),
            Juice = new Color(1f, 0.55f, 0.05f, 0.85f), Sauce = new Color(0.62f, 0.07f, 0.03f, 0.9f), Dust = new Color(0.55f, 0.5f, 0.42f, 0.35f),
            Mustard = new Color(1f, 0.8f, 0.05f, 0.95f), Ketchup = new Color(0.8f, 0.06f, 0.04f, 0.95f), Relish = new Color(0.35f, 0.62f, 0.15f),
            PopcornWhite = new Color(1f, 0.97f, 0.86f), PopcornButter = new Color(1f, 0.86f, 0.45f),
            Jelly = new Color(0.52f, 0.9f, 0.16f), JellySplat = new Color(0.52f, 0.9f, 0.16f, 0.7f), Cream = new Color(1f, 0.98f, 0.94f, 0.95f),
            Salsa = new Color(0.72f, 0.13f, 0.06f, 0.92f), Coffee = new Color(0.24f, 0.12f, 0.05f, 0.93f),
            Spores = new Color(0.58f, 0.68f, 0.56f, 0.5f), SporeMark = new Color(0.45f, 0.58f, 0.45f, 0.22f), Steam = new Color(0.95f, 0.95f, 0.97f, 0.32f),
            Toast = new Color(0.82f, 0.56f, 0.26f), Crumb = new Color(0.78f, 0.6f, 0.36f),
            ToiletWater = new Color(0.55f, 0.76f, 0.95f, 0.6f);

        BendRig rig;
        readonly MotionMeter meter = new MotionMeter();
        float owed, nextIdle, nextFlop, leakingUntil, extraOwed, steamOwed, shutFor, nextPop, nextSqueezeSound;
        bool mouthWasOpen;
        AudioSource audioSource;
        AudioClip[] farts;

        public string MessName => kind switch
        {
            MessKind.Egg => "a falling frying pan and egg yolk",
            MessKind.Trash => "garbage",
            MessKind.Juice => "juice",
            MessKind.Meatballs => "meatballs and sauce",
            MessKind.HotDog => "mustard, ketchup and relish",
            MessKind.Popcorn => "popcorn",
            MessKind.Jello => "jelly chunks and cream",
            MessKind.Taco => "taco filling and salsa",
            MessKind.Mold => "a cloud of spores",
            MessKind.Toaster => "flying toast",
            MessKind.Ketchup => "ketchup",
            MessKind.Coffee => "coffee",
            MessKind.Toilet => "toilet water",
            _ => "a dust cloud",
        };

        void Awake()
        {
            rig = GetComponent<BendRig>();
            if (kind == MessKind.Egg) idleEvery = new Vector2(40f, 80f); // a pan on the head is an event, not a habit
            if (kind == MessKind.Juice || kind == MessKind.HotDog || kind == MessKind.Meatballs) idleEvery = new Vector2(8f, 18f); // runs and drips
            if (kind == MessKind.Popcorn) idleEvery = new Vector2(14f, 28f); // a popping fit
            if (kind == MessKind.Taco || kind == MessKind.Ketchup || kind == MessKind.Coffee) idleEvery = new Vector2(9f, 20f); // drips
            if (kind == MessKind.Mold) idleEvery = new Vector2(6f, 12f);      // a puff of spores
            if (kind == MessKind.Toaster) idleEvery = new Vector2(25f, 45f);  // toast's up
            if (kind == MessKind.Toilet) idleEvery = new Vector2(30f, 60f);   // a flush
            nextIdle = Time.time + Random.Range(idleEvery.x, idleEvery.y);
            nextFlop = Time.time + Random.Range(kind == MessKind.Jello ? 3f : 6f, 14f);
            if (kind == MessKind.Vacuum)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
                farts = new[] { MakeFart(1), MakeFart(2), MakeFart(3) };
            }
        }

        void OnEnable() => meter.Reset();

        void Start()
        {
            if (kind == MessKind.Toilet && rig)
            {
                bowlBone = rig.BoneAt(bowl.y, out Vector3 rest);
                bowlLocal = bowl - rest;
            }
        }

        /// <summary>The toilet bowl's water, riding along as the toilet rocks.</summary>
        Vector3 BowlPoint => bowlBone ? bowlBone.TransformPoint(bowlLocal) : transform.TransformPoint(bowl);

        Transform Top => rig.HasFloppyTip ? rig.FloppyBones[rig.FloppyBones.Length / 2] : rig.HeadBone;
        /// <summary>The middle of the avatar's real top (its rim, its opening), riding along as it moves.</summary>
        Vector3 TopPoint => Top.position + Top.up * topLift;
        Transform Tip => rig.HasFloppyTip ? rig.FloppyBones[rig.FloppyBones.Length - 1] : rig.HeadBone;
        Material Tint(Color c) => Mess.Instance ? Mess.Instance.BlobMaterial(c) : lit;
        static ChaosDirector Chaos => ChaosDirector.Instance;

        void Update()
        {
            Transform top = rig ? Top : null;
            if (!top || !DeskDebris.Instance) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            meter.Update(top, topRadius, dt);
            float speed = meter.Speed, over = Mathf.Max(0f, meter.Shake - shakeThreshold);

            switch (kind)
            {
                case MessKind.Trash:
                    owed += (speed * 0.9f + over * 0.08f) * dt;
                    break;
                case MessKind.Meatballs:
                    owed += (speed * 0.9f + over * 0.1f) * dt;     // meatballs
                    extraOwed += (0.4f + speed * 7f) * dt;          // sauce flicked off
                    break;
                case MessKind.Juice:
                    owed += (speed * 6f + Mathf.Max(0f, meter.Shake - 5f) * 1.5f) * dt; // sloshes as it moves
                    break;
                case MessKind.HotDog:
                    owed += (0.3f + speed * 5f + over * 0.5f) * dt;      // condiments
                    extraOwed += (speed * 0.8f + over * 0.1f) * dt;      // relish bits
                    break;
                case MessKind.Egg:
                    if (Time.time < leakingUntil) owed += (0.4f + speed * 4f) * dt; // cracked: it leaks as it moves
                    break;
                case MessKind.Popcorn:
                    owed += (0.3f + speed * 2.5f + over * 0.25f) * dt; // kernels pop all the time, more as it moves
                    break;
                case MessKind.Jello:
                    owed += (speed * 1.2f + over * 0.15f) * dt;     // jelly chunks flung off
                    extraOwed += (speed * 0.35f + over * 0.05f) * dt; // cream
                    break;
                case MessKind.Vacuum:
                    break; // its hose (VacuumHose) does the hoovering
                case MessKind.Taco:
                    owed += (0.25f + speed * 3f + over * 0.3f) * dt;   // filling falls out
                    extraOwed += (speed * 0.8f + over * 0.1f) * dt;    // salsa
                    break;
                case MessKind.Mold:
                    owed += (0.12f + speed * 1.2f + over * 0.15f) * dt; // spore puffs and crumbs
                    break;
                case MessKind.Toaster:
                    owed += (speed * 1.5f + over * 0.2f) * dt;          // crumbs
                    LongBlinkPopsToast(dt);
                    break;
                case MessKind.Ketchup:
                    owed += (speed * 1.5f + over * 0.2f) * dt;          // gobs flicked off the nozzle
                    MouthSquirts(dt);
                    break;
                case MessKind.Toilet:
                    owed += (speed * 1.3f + over * 0.25f) * dt;         // waves slop over the rim as it moves
                    MouthSloshes(dt);
                    break;
                case MessKind.Coffee:
                    owed += (speed * 1.6f + Mathf.Max(0f, meter.Shake - 4f) * 0.4f) * dt; // slops over the rim
                    steamOwed += dt * 2.6f;                                              // always steaming
                    for (int g = 0; steamOwed >= 1f && g < 3; g++)
                    {
                        steamOwed -= 1f;
                        StartCoroutine(Cloud(TopPoint + Random.insideUnitSphere * topRadius * 0.4f, 1, Steam, Random.Range(0.08f, 0.14f), 1.8f,
                            new Vector3(Random.Range(-0.03f, 0.03f), Random.Range(0.12f, 0.2f), 0f), 0.02f));
                    }
                    break;
            }
            for (int guard = 0; owed >= 1f && guard < 10; guard++)
            {
                owed -= 1f;
                Spill(1);
            }
            for (int guard = 0; extraOwed >= 1f && guard < 5; guard++)
            {
                extraOwed -= 1f;
                if (kind == MessKind.Meatballs) SauceFlick();
                else if (kind == MessKind.Jello) CreamFlick();
                else if (kind == MessKind.Taco) Splash(Salsa, 0.012f, 0.03f);
                else RelishBit(1f);
            }
            if (kind == MessKind.HotDog && Time.time >= nextFlop)
            {
                nextFlop = Time.time + Random.Range(9f, 20f);
                BigFlop(1f);
            }
            if (kind == MessKind.Jello && Time.time >= nextFlop)
            {
                nextFlop = Time.time + Random.Range(5f, 12f);
                Jiggle(Random.Range(0.6f, 1.1f)); // it shivers on its own
            }
            if (Time.time >= nextIdle)
            {
                nextIdle = Time.time + Random.Range(idleEvery.x, idleEvery.y);
                switch (kind)
                {
                    case MessKind.Vacuum: StartCoroutine(DustCloud(false)); break;
                    case MessKind.Juice: RunDown(Juice, 0.028f); break;
                    case MessKind.HotDog: RunDown(Random.value < 0.5f ? Mustard : Ketchup, 0.024f); break;
                    case MessKind.Meatballs:
                        RunDown(Sauce, 0.028f);
                        Spill(1);
                        break;
                    case MessKind.Egg: StartCoroutine(FryingPan()); break;
                    case MessKind.Popcorn: StartCoroutine(PoppingFit(Random.Range(6, 11), 1.6f)); break;
                    case MessKind.Taco: RunDown(Salsa, 0.024f); break;
                    case MessKind.Ketchup: RunDown(Ketchup, 0.026f); break;
                    case MessKind.Coffee: RunDown(Coffee, 0.026f); break;
                    case MessKind.Mold: SporePuff(false); break;
                    case MessKind.Toaster: PopToast(Random.Range(1, 3)); break;
                    case MessKind.Toilet: StartCoroutine(Flush()); break;
                    default: Spill(1); break;
                }
            }
        }

        /// <summary>!shake: a good amount of this avatar's mess.</summary>
        public void Burst()
        {
            switch (kind)
            {
                case MessKind.Egg: StartCoroutine(FryingPan()); break;
                case MessKind.Vacuum: StartCoroutine(DustCloud(true)); break;
                case MessKind.Juice:
                    Spill(28);
                    RunDown(Juice, 0.03f);
                    break;
                case MessKind.Trash: Spill(4); break;
                case MessKind.HotDog: BigFlop(1.6f); break;
                case MessKind.Meatballs:
                    Spill(4);
                    for (int i = 0; i < 14; i++) SauceFlick();
                    RunDown(Sauce, 0.03f);
                    break;
                case MessKind.Popcorn: StartCoroutine(PoppingFit(30, 2.5f)); break;
                case MessKind.Jello:
                    Jiggle(2.2f);
                    Spill(8);
                    for (int i = 0; i < 3; i++) CreamFlick();
                    break;
                case MessKind.Taco:
                    Spill(25);
                    for (int i = 0; i < 6; i++) Splash(Salsa, 0.014f, 0.035f);
                    break;
                case MessKind.Mold:
                    SporePuff(true);
                    for (int i = 0; i < 6; i++) CrumbBit();
                    break;
                case MessKind.Toaster: PopToast(2); break;
                case MessKind.Ketchup:
                    Squirt(30);
                    StartCoroutine(After(0.85f, () => Squirt(24)));
                    break;
                case MessKind.Toilet:
                    Heave(1.8f, true);
                    StartCoroutine(Geyser(0.7f));
                    break;
                case MessKind.Coffee: CoffeeHeave(); break;
                default: Spill(2); break;
            }
        }

        /// <summary>The face slam: it lands face-first in its own mess.</summary>
        public void OnSlam()
        {
            switch (kind)
            {
                case MessKind.Egg: Crack(); break;
                case MessKind.Vacuum: StartCoroutine(DustCloud(true)); break;
                case MessKind.Juice: Spill(40); break;
                case MessKind.Trash: Spill(6); break;
                case MessKind.HotDog:
                    Spill(30);
                    for (int i = 0; i < 6; i++) RelishBit(1.5f);
                    if (Mess.Instance)
                    {
                        Vector3 front = transform.position + new Vector3(0f, 0f, -topRadius - 0.12f);
                        Mess.Instance.DeskStain(front + new Vector3(-0.04f, 0f, 0f), 0.06f, Mustard, 150f, 0.1f, false, new Vector2(0f, -1.5f));
                        Mess.Instance.DeskStain(front + new Vector3(0.05f, 0f, 0.02f), 0.055f, Ketchup, 150f, 0.12f, false, new Vector2(0.3f, -1.5f));
                    }
                    break;
                case MessKind.Meatballs:
                    Spill(5);
                    for (int i = 0; i < 18; i++) SauceFlick();
                    break;
                case MessKind.Popcorn: Spill(14); break;
                case MessKind.Taco: Spill(30); break;
                case MessKind.Mold:
                    SporePuff(true);
                    for (int i = 0; i < 8; i++) CrumbBit();
                    break;
                case MessKind.Toaster:
                    PopToast(2);
                    for (int i = 0; i < 10; i++) CrumbBit();
                    break;
                case MessKind.Ketchup:
                    Squirt(24);
                    if (Mess.Instance) Mess.Instance.DeskStain(transform.position + new Vector3(0f, 0f, -topRadius - 0.1f), 0.08f, Ketchup, 150f, 0.1f, false, new Vector2(0f, -1.6f));
                    break;
                case MessKind.Coffee:
                    CoffeeHeave();
                    if (Mess.Instance) Mess.Instance.DeskStain(transform.position + new Vector3(0f, 0f, -topRadius - 0.12f), 0.09f, Coffee, 150f, 0.15f, true, new Vector2(0f, -1.2f));
                    break;
                case MessKind.Toilet:
                    Heave(1.6f, true);
                    if (Mess.Instance) Mess.Instance.DeskStain(transform.position + new Vector3(0f, 0f, -topRadius - 0.12f), 0.12f, ToiletWater, 120f, 0.2f, true);
                    break;
                case MessKind.Jello:
                    Jiggle(1.5f);
                    Spill(10);
                    if (Mess.Instance) Mess.Instance.DeskStain(transform.position + new Vector3(0f, 0f, -topRadius - 0.1f), 0.08f, JellySplat, 120f, 0.15f);
                    break;
                default: Spill(3); break;
            }
        }

        void Spill(int count)
        {
            for (int i = 0; i < count; i++)
            {
                switch (kind)
                {
                    case MessKind.Trash: Garbage(); break;
                    case MessKind.Juice: JuiceDrop(); break;
                    case MessKind.Meatballs: Meatball(); break;
                    case MessKind.HotDog: Condiment(); break;
                    case MessKind.Egg: Leak(); break;
                    case MessKind.Popcorn: Kernel(); break;
                    case MessKind.Jello: JellyChunk(); break;
                    case MessKind.Taco: TacoBit(); break;
                    case MessKind.Mold: SporeOrCrumb(); break;
                    case MessKind.Toaster: CrumbBit(); break;
                    case MessKind.Ketchup: Splash(Ketchup, Random.Range(0.014f, 0.02f), 0.03f); break;
                    case MessKind.Coffee: CoffeeSlop(1f); break;
                    case MessKind.Toilet: Heave(0.4f, false); break;
                }
            }
        }

        /// <summary>A point around the rim of the top, and which way is out from there.</summary>
        Vector3 RimPoint(out Vector3 outward)
        {
            Vector2 around = Random.insideUnitCircle.normalized;
            // Mostly toward the camera and the sides, so it lands on the desk in view.
            if (around.y > 0.3f) around.y = -around.y;
            outward = new Vector3(around.x, 0f, around.y);
            return TopPoint + outward * topRadius * Random.Range(0.7f, 1f) + Vector3.up * Random.Range(-0.3f, 0.5f) * topHeight;
        }

        Transform Item(PrimitiveType type, Color colour, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = Tint(colour);
            go.transform.localScale = scale;
            return go.transform;
        }

        /// <summary>A lumpy item (crumpled paper, a meatball) when the mess has its lumpy shapes, else a ball.</summary>
        Transform Lump(Color colour, Vector3 scale, int shape = -1)
        {
            Transform t = Item(PrimitiveType.Sphere, colour, scale);
            if (Mess.Instance) t.GetComponent<MeshFilter>().sharedMesh = shape >= 0 ? Mess.Instance.Lumps[shape] : Mess.Instance.RandomLump;
            t.rotation = Random.rotation;
            return t;
        }

        /// <summary>A piece of garbage with its own shape (JunkMeshes), at a random angle.</summary>
        Transform Junk(Mesh shape, Color colour, Vector3 scale)
        {
            Transform t = Item(PrimitiveType.Sphere, colour, scale);
            t.GetComponent<MeshFilter>().sharedMesh = shape;
            t.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            return t;
        }

        /// <summary>A drip runs down the avatar's front from near its top, and drops onto the desk.</summary>
        void RunDown(Color colour, float size)
        {
            if (Chaos && Chaos.library && Chaos.library.ActiveRig == rig)
                Chaos.DripDown(Random.Range(0.72f, 0.85f), Random.Range(-0.6f, 0.6f), colour, size, Random.Range(0.06f, 0.1f));
        }

        void Garbage()
        {
            Sfx.Play("trash_clatter", 0.55f, 0.15f);
            Vector3 at = RimPoint(out Vector3 outward);
            Vector3 v = meter.Velocity * 0.8f + outward * Random.Range(0.4f, 1f) + Vector3.up * Random.Range(0.3f, 1f);
            float pick = Random.value;
            if (pick < 0.35f)
            {
                // A crumpled ball of paper, which rolls.
                Transform paper = Junk(JunkMeshes.CrumpledPaper, new Color(0.95f, 0.94f, 0.9f), Vector3.one * Random.Range(0.06f, 0.08f));
                paper.position = at;
                DeskDebris.Instance.AddPiece(paper, v, 0.035f, 0.03f, disposable: true, ball: true);
            }
            else if (pick < 0.6f)
            {
                // A banana peel, flaps splayed, with its brown stalk.
                Transform peel = Junk(JunkMeshes.BananaPeel, new Color(1f, 0.83f, 0.18f), Vector3.one * Random.Range(0.12f, 0.15f));
                Transform stalk = Item(PrimitiveType.Cylinder, new Color(0.35f, 0.25f, 0.12f), new Vector3(0.12f, 0.12f, 0.12f));
                stalk.SetParent(peel, false);
                stalk.localPosition = new Vector3(0f, 0.41f, 0f);
                stalk.localScale = new Vector3(0.07f, 0.05f, 0.07f);
                peel.position = at;
                DeskDebris.Instance.AddPiece(peel, v, 0.06f, 0.002f, disposable: true, upright: true); // lands on its flaps, stalk up
            }
            else if (pick < 0.8f)
            {
                // A soda can that's been stamped on (silver lid on top).
                Transform can = Junk(JunkMeshes.CrushedCan, new Color(0.85f, 0.12f, 0.15f), new Vector3(0.065f, 0.07f, 0.065f));
                Transform lid = Item(PrimitiveType.Cylinder, new Color(0.8f, 0.8f, 0.84f), Vector3.one);
                lid.SetParent(can, false);
                lid.localPosition = new Vector3(0.14f, 0.5f, 0f);
                lid.localScale = new Vector3(0.8f, 0.02f, 0.8f);
                can.position = at;
                DeskDebris.Instance.AddPiece(can, v, 0.035f, 0.035f, disposable: true);
            }
            else
            {
                // A pizza crust somebody didn't finish.
                Transform crust = Junk(JunkMeshes.Crust, new Color(0.86f, 0.6f, 0.3f), Vector3.one * Random.Range(0.11f, 0.14f));
                crust.position = at;
                DeskDebris.Instance.AddPiece(crust, v, 0.05f, 0.012f, disposable: true);
            }
        }

        float nextSlosh;

        void JuiceDrop()
        {
            if (!Mess.Instance) return;
            if (Time.time >= nextSlosh)
            {
                nextSlosh = Time.time + Random.Range(0.6f, 1.2f);
                Sfx.Play("slosh", 0.5f, 0.12f);
            }
            Vector3 at = RimPoint(out Vector3 outward);
            Mess.Instance.Drop(at, meter.Velocity * 0.8f + outward * Random.Range(0.3f, 0.8f) + Vector3.up * Random.Range(0.1f, 0.5f), Juice, 0.016f, 0.04f);
        }

        /// <summary>A flick of tomato sauce off the top, splatting on the desk.</summary>
        void SauceFlick()
        {
            if (!Mess.Instance) return;
            Vector3 at = RimPoint(out Vector3 outward);
            Mess.Instance.Drop(at, meter.Velocity * 0.9f + outward * Random.Range(0.3f, 1.1f) + Vector3.up * Random.Range(0.1f, 0.6f),
                Sauce, Random.Range(0.01f, 0.018f), Random.Range(0.022f, 0.04f));
        }

        void Meatball()
        {
            Vector3 at = RimPoint(out Vector3 outward);
            Sfx.Play("slop", 0.7f, 0.12f);
            Transform ball = Lump(new Color(0.45f, 0.22f, 0.12f), Vector3.one * 0.08f);
            ball.position = at;
            // Off it goes, rolling toward the front of the desk, leaving sauce behind.
            Vector3 v = meter.Velocity * 0.7f + outward * Random.Range(0.5f, 1.1f) + new Vector3(0f, Random.Range(0.2f, 0.6f), -Random.Range(0.3f, 0.8f));
            DeskDebris.Instance.AddPiece(ball, v, 0.04f, 0.04f, disposable: true, ball: true, trail: Sauce);
            if (Mess.Instance) Mess.Instance.Drop(at, v * 0.6f, Sauce, 0.012f, 0.03f);
        }

        /// <summary>A gob of mustard or ketchup flies off the sausage.</summary>
        void Condiment()
        {
            if (!Mess.Instance) return;
            Transform tip = Tip;
            Vector2 around = Random.insideUnitCircle.normalized;
            if (around.y > 0.3f) around.y = -around.y;
            var outward = new Vector3(around.x, 0f, around.y);
            Vector3 at = Vector3.Lerp(Top.position, tip.position, Random.value) + outward * topRadius * 0.35f;
            Mess.Instance.Drop(at, meter.Velocity * 0.9f + outward * Random.Range(0.3f, 1f) + Vector3.up * Random.Range(0.1f, 0.6f),
                Random.value < 0.5f ? Mustard : Ketchup, Random.Range(0.012f, 0.02f), Random.Range(0.025f, 0.045f));
        }

        void RelishBit(float fling)
        {
            Vector3 at = Vector3.Lerp(Top.position, Tip.position, Random.value);
            Transform bit = Item(PrimitiveType.Cube, Relish, Vector3.one * Random.Range(0.014f, 0.022f));
            bit.position = at;
            bit.rotation = Random.rotation;
            Vector2 d = Random.insideUnitCircle.normalized;
            DeskDebris.Instance.AddPiece(bit, meter.Velocity * 0.8f + new Vector3(d.x, Random.Range(0.3f, 1f), -Mathf.Abs(d.y)) * fling, 0.01f, 0.008f, disposable: true);
        }

        /// <summary>The sausage flops right over (taking the top of the bun with it), flinging condiments.</summary>
        void BigFlop(float strength)
        {
            Sfx.Play("flop", 0.8f, 0.1f);
            Sfx.Play("squirt", 0.7f, 0.12f);
            float side = Random.value < 0.5f ? -1f : 1f;
            rig.KickTip(new Vector3(side * Random.Range(1.6f, 2.4f), 0.3f, -Random.Range(0.2f, 0.8f)) * strength);
            int gobs = Mathf.RoundToInt(10 * strength);
            for (int i = 0; i < gobs; i++) Condiment();
            for (int i = 0; i < Mathf.RoundToInt(3 * strength); i++) RelishBit(1.3f);
            if (strength > 1.2f) RunDown(Mustard, 0.026f);
        }

        float nextBitSound;

        /// <summary>A sound for a bit coming off, at most a few a second (a spill of ten would play ten at once).</summary>
        void BitSound(string name, float volume)
        {
            if (Time.time < nextBitSound) return;
            nextBitSound = Time.time + 0.08f;
            Sfx.Play(name, volume, 0.3f);
        }

        /// <summary>A kernel pops out of the top: a fluffy white-and-butter puff that flies up and lands on the desk.</summary>
        void Kernel()
        {
            BitSound("pop", 0.35f);
            Vector2 around = Random.insideUnitCircle * topRadius * 0.6f;
            Vector3 at = Tip.position + new Vector3(around.x, Random.Range(-0.5f, 0.2f) * topHeight, around.y);
            float size = Random.Range(0.026f, 0.036f);
            Transform kernel = Lump(Random.value < 0.7f ? PopcornWhite : PopcornButter, Vector3.one * size);
            for (int i = 0; i < 2; i++)
            {
                // Two more puffs stuck on: popped corn is knobbly, not a ball.
                Transform puff = Lump(Random.value < 0.6f ? PopcornWhite : PopcornButter, Vector3.one);
                puff.SetParent(kernel, false);
                puff.localPosition = Random.onUnitSphere * 0.38f;
                puff.localScale = Vector3.one * Random.Range(0.6f, 0.8f);
            }
            kernel.position = at;
            Vector2 d = Random.insideUnitCircle.normalized;
            if (d.y > 0.3f) d.y = -d.y; // mostly toward the camera and the sides, so it lands in view
            Vector3 v = meter.Velocity * 0.5f + new Vector3(d.x * Random.Range(0.25f, 0.8f), Random.Range(1.6f, 2.6f), d.y * Random.Range(0.25f, 0.8f));
            DeskDebris.Instance.AddPiece(kernel, v, size * 0.6f, size * 0.45f, disposable: true);
        }

        /// <summary>A popping fit: count kernels pop out over about this many seconds, the heap hopping as they go.</summary>
        IEnumerator PoppingFit(int count, float seconds)
        {
            for (int i = 0; i < count; i++)
            {
                Kernel();
                if (i % 3 == 0) rig.KickTip(new Vector3(Random.Range(-0.4f, 0.4f), 0.6f, 0f));
                yield return new WaitForSeconds(seconds / count * Random.Range(0.4f, 1.6f));
            }
        }

        /// <summary>A wobbly chunk of jelly flies off the top and lands on the desk.</summary>
        void JellyChunk()
        {
            BitSound("squelch", 0.45f);
            Vector3 at = RimPoint(out Vector3 outward);
            float size = Random.Range(0.035f, 0.055f);
            Transform chunk = Lump(Jelly, new Vector3(size, size * 0.8f, size));
            chunk.position = at;
            Vector3 v = meter.Velocity * 0.9f + outward * Random.Range(0.4f, 1f) + Vector3.up * Random.Range(0.3f, 0.9f);
            DeskDebris.Instance.AddPiece(chunk, v, size * 0.5f, size * 0.35f, disposable: true);
        }

        /// <summary>A blob of whipped cream flicks off the top and splats on the desk.</summary>
        void CreamFlick()
        {
            if (!Mess.Instance) return;
            Vector3 at = Vector3.Lerp(Top.position, Tip.position, 0.8f) + Random.insideUnitSphere * topRadius * 0.3f;
            Vector2 d = Random.insideUnitCircle.normalized;
            if (d.y > 0.3f) d.y = -d.y;
            Mess.Instance.Drop(at, meter.Velocity * 0.9f + new Vector3(d.x, 0f, d.y) * Random.Range(0.3f, 0.9f) + Vector3.up * Random.Range(0.2f, 0.6f),
                Cream, Random.Range(0.014f, 0.022f), Random.Range(0.03f, 0.05f));
        }

        /// <summary>The jello shivers: its top wobbles side to side and the whole thing squashes a little.</summary>
        void Jiggle(float strength)
        {
            Sfx.Play("squelch", 0.35f * Mathf.Min(1f, strength), 0.25f);
            float side = Random.value < 0.5f ? -1f : 1f;
            rig.KickTip(new Vector3(side * Random.Range(1.1f, 1.6f), 0.15f, Random.Range(-0.3f, 0.3f)) * strength);
            rig.Bonk(0.12f * strength);
        }

        // ---------- taco, mouldy bread, toaster, ketchup, coffee ----------

        /// <summary>A drop of something off the top (salsa, ketchup), flicked out and down onto the desk.</summary>
        void Splash(Color colour, float size, float puddle)
        {
            if (!Mess.Instance) return;
            Vector3 at = RimPoint(out Vector3 outward);
            Mess.Instance.Drop(at, meter.Velocity * 0.9f + outward * Random.Range(0.3f, 1f) + Vector3.up * Random.Range(0.1f, 0.5f), colour, size, puddle);
        }

        /// <summary>A bit of taco filling drops out: lettuce, cheese, a beef crumble or a piece of tomato.</summary>
        void TacoBit()
        {
            Vector3 at = RimPoint(out Vector3 outward);
            Vector3 v = meter.Velocity * 0.8f + outward * Random.Range(0.2f, 0.8f) + Vector3.up * Random.Range(0.2f, 0.7f);
            Transform bit;
            float radius, half;
            switch (Random.Range(0, 4))
            {
                case 0: bit = Item(PrimitiveType.Cube, new Color(0.45f, 0.75f, 0.25f), new Vector3(0.035f, 0.004f, 0.016f)); radius = 0.018f; half = 0.002f; break; // lettuce
                case 1: bit = Item(PrimitiveType.Capsule, new Color(1f, 0.62f, 0.12f), new Vector3(0.007f, 0.018f, 0.007f)); radius = 0.012f; half = 0.0035f; break; // cheese
                case 2: bit = Lump(new Color(0.4f, 0.22f, 0.12f), Vector3.one * Random.Range(0.016f, 0.024f)); radius = 0.01f; half = 0.008f; break; // beef
                default: bit = Item(PrimitiveType.Cube, new Color(0.85f, 0.15f, 0.1f), Vector3.one * 0.016f); radius = 0.01f; half = 0.008f; break; // tomato
            }
            bit.position = at;
            bit.rotation = Random.rotation;
            DeskDebris.Instance.AddPiece(bit, v, radius, half, disposable: true);
        }

        /// <summary>A stale crumb falls off.</summary>
        void CrumbBit()
        {
            Vector3 at = RimPoint(out Vector3 outward);
            Transform crumb = Lump(Crumb, Vector3.one * Random.Range(0.01f, 0.018f));
            crumb.position = at;
            DeskDebris.Instance.AddPiece(crumb, meter.Velocity * 0.7f + outward * Random.Range(0.1f, 0.5f) + Vector3.up * Random.Range(0.1f, 0.4f), 0.007f, 0.006f, disposable: true);
        }

        void SporeOrCrumb()
        {
            if (Random.value < 0.6f) SporePuff(false);
            else CrumbBit();
        }

        /// <summary>Spores puff off the mouldy bread and drift; where they settle the desk goes faintly furry.</summary>
        void SporePuff(bool big)
        {
            Sfx.Play("spore_puff", big ? 0.7f : 0.35f, 0.15f);
            Vector3 at = TopPoint + new Vector3(Random.Range(-1f, 1f) * topRadius * 0.6f, -topHeight * Random.Range(0f, 2f), -topRadius * 0.4f);
            StartCoroutine(Cloud(at, big ? 14 : 3, Spores, big ? 0.3f : 0.16f, big ? 2.8f : 2f,
                new Vector3(Random.Range(-0.12f, 0.12f), 0.08f, Random.Range(-0.2f, 0.02f)) * (big ? 1.6f : 1f), big ? 0.12f : 0.04f));
            if (Mess.Instance && Random.value < (big ? 1f : 0.3f))
                for (int i = 0; i < (big ? 4 : 1); i++)
                    Mess.Instance.DeskStain(transform.position + new Vector3(Random.Range(-0.45f, 0.45f), 0f, Random.Range(-0.6f, -0.3f)), Random.Range(0.05f, 0.1f), SporeMark, 90f, 1.2f, false, default, false);
        }

        /// <summary>
        /// The toaster's eyes: close them hard (both shut for a third of a second) and up comes the toast. Ordinary
        /// blinks are too quick to count.
        /// </summary>
        void LongBlinkPopsToast(float dt)
        {
            HeadPose pose = rig.Current;
            bool shut = pose.BlinkLeft > 0.7f && pose.BlinkRight > 0.7f;
            shutFor = shut ? shutFor + dt : 0f;
            if (shutFor > 0.33f && Time.time >= nextPop)
            {
                PopToast(Random.Range(1, 3));
                shutFor = -10f; // once per long blink
            }
        }

        /// <summary>DING: slices of toast fly up out of the slots, flip, and land on the desk with a few crumbs.</summary>
        void PopToast(int slices)
        {
            nextPop = Time.time + 2.5f;
            Sfx.Play("toaster_pop", 0.9f, 0.06f);
            rig.Bonk(0.15f);
            for (int i = 0; i < slices; i++)
            {
                Transform slice = Item(PrimitiveType.Cube, Toast, new Vector3(0.11f, 0.105f, 0.016f));
                slice.position = TopPoint + new Vector3((i - (slices - 1) * 0.5f) * 0.06f, 0.04f, 0f);
                slice.rotation = Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f);
                Vector3 v = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(2.2f, 3f), Random.Range(-0.9f, -0.4f));
                DeskDebris.Instance.AddPiece(slice, v, 0.055f, 0.008f, disposable: true);
            }
            for (int i = 0; i < 4; i++) CrumbBit();
        }

        IEnumerator After(float seconds, System.Action then)
        {
            yield return new WaitForSeconds(seconds);
            then();
        }

        /// <summary>
        /// A stream (a squirt of ketchup, a gush of water): drops in quick succession along one aim with a slow wobble,
        /// so they read as one rope of liquid arcing through the air and land in a line of splats that run together.
        /// Strongest at the start, tailing off. aim is asked every frame (it can follow the avatar, or spray about).
        /// </summary>
        IEnumerator Stream(System.Func<Vector3> from, System.Func<Vector3> aim, Color colour, float seconds, float perSecond,
            Vector2 speed, Vector2 size, float puddle)
        {
            float owedDrops = 0f, t = 0f, seed = Random.value * 10f;
            while (t < seconds && Mess.Instance)
            {
                t += Time.deltaTime;
                owedDrops += perSecond * Time.deltaTime;
                float pace = Mathf.Lerp(speed.y, speed.x, t / seconds);
                Vector3 wobble = new Vector3(Mathf.PerlinNoise(seed, t * 3f) - 0.5f, Mathf.PerlinNoise(seed + 3f, t * 3f) - 0.5f, 0f) * 0.2f;
                Vector3 direction = (aim() + wobble).normalized;
                for (; owedDrops >= 1f; owedDrops -= 1f)
                    Mess.Instance.Drop(from() + Random.insideUnitSphere * 0.006f, direction * pace * Random.Range(0.95f, 1.05f) + meter.Velocity * 0.5f,
                        colour, Random.Range(size.x, size.y), puddle);
                yield return null;
            }
        }

        /// <summary>
        /// Which way something leaves over the rim: the way the avatar's moving, else out to a side and a bit toward
        /// the camera (straight at the camera it's foreshortened and hardly reads).
        /// </summary>
        Vector3 SpillDirection(float still = 0.04f)
        {
            Vector3 flat = new Vector3(meter.Velocity.x, 0f, meter.Velocity.z);
            Vector3 dir = flat.sqrMagnitude > still ? flat.normalized : new Vector3(Random.value < 0.5f ? -1f : 1f, 0f, -Random.Range(0.3f, 0.9f)).normalized;
            if (dir.z > 0.2f) dir.z = -dir.z; // onto the desk in view
            return dir.normalized;
        }

        /// <summary>A drip runs down the avatar's front from `up` (share of its height), `across` (-1..1) from its middle.</summary>
        void RunDownAt(float up, float across, Color colour, float size)
        {
            if (Chaos && Chaos.library && Chaos.library.ActiveRig == rig)
                Chaos.DripDown(up, Mathf.Clamp(across, -0.7f, 0.7f), colour, size, Random.Range(0.08f, 0.12f));
        }

        float squeezeOwed, squeezeSince;

        Vector3 Nozzle => TopPoint + Top.up * 0.03f;
        /// <summary>Out to a side (side -1..1) so the arc shows in profile, a little toward the camera.</summary>
        Vector3 KetchupAim(float side) => (Top.up * 0.5f + Vector3.right * side * 0.8f + Vector3.back * 0.4f).normalized;

        /// <summary>
        /// The ketchup bottle's mouth: opening yours squirts it; while it stays open it keeps squeezing out a thick
        /// stream that sweeps about (turn your head to draw it across the desk); closing it ends in a sputter.
        /// </summary>
        void MouthSquirts(float dt)
        {
            float open = rig.Current.MouthOpen;
            bool isOpen = open > 0.45f;
            if (isOpen && !mouthWasOpen)
            {
                Squirt(14);
                squeezeSince = Time.time;
            }
            else if (isOpen && Time.time - squeezeSince > 0.45f && Mess.Instance)
            {
                squeezeOwed += open * 40f * dt;
                Vector3 aim = KetchupAim(Mathf.Sin(Time.time * 1.2f)); // sweeps side to side: turn your head to steer it
                for (; squeezeOwed >= 1f; squeezeOwed -= 1f)
                    Mess.Instance.Drop(Nozzle + Random.insideUnitSphere * 0.005f, aim * Random.Range(1.4f, 2.1f) * Mathf.Lerp(0.85f, 1.15f, open) + meter.Velocity * 0.5f,
                        Ketchup, Random.Range(0.013f, 0.02f), 0.028f);
                if (Time.time >= nextSqueezeSound)
                {
                    nextSqueezeSound = Time.time + Random.Range(0.5f, 0.9f);
                    Sfx.Play("squirt", 0.45f, 0.15f);
                }
            }
            if (!isOpen && mouthWasOpen && Time.time - squeezeSince > 0.8f) Sputter();
            mouthWasOpen = isOpen;
        }

        /// <summary>SPLURT: a thick rope of ketchup out of the nozzle, arcing onto the desk in a line; the bottle squashes as it's squeezed, and it ends in a sputter.</summary>
        void Squirt(int strength)
        {
            if (!Mess.Instance) return;
            Sfx.Play("squirt", 0.9f, 0.1f);
            rig.Bonk(0.25f);
            float seconds = Mathf.Lerp(0.25f, 0.6f, Mathf.InverseLerp(8f, 30f, strength));
            float side = (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.7f, 1f);
            StartCoroutine(Stream(() => Nozzle, () => KetchupAim(side), Ketchup, seconds, 75f, new Vector2(1.5f, 2.5f), new Vector2(0.018f, 0.028f), 0.032f));
            StartCoroutine(After(seconds + 0.05f, Sputter));
        }

        /// <summary>The bottle runs dry for a moment: a wet fart of a sputter, a spray of little specks, and a drool from the nozzle.</summary>
        void Sputter()
        {
            if (!Mess.Instance) return;
            Sfx.Play("fart", 0.4f, 0.2f);
            rig.Bonk(0.1f);
            for (int i = 0; i < 10; i++)
                Mess.Instance.Drop(Nozzle, (KetchupAim(Random.Range(-0.6f, 0.6f)) + Random.insideUnitSphere * 0.5f).normalized * Random.Range(0.6f, 1.6f),
                    Ketchup, Random.Range(0.006f, 0.011f), 0.014f);
            if (Random.value < 0.5f) RunDownAt(Random.Range(0.9f, 0.96f), Random.Range(-0.3f, 0.3f), Ketchup, 0.024f);
        }

        float gushOwed;

        /// <summary>
        /// The toilet's mouth: opening yours heaves a wave of water up out of the bowl; while it stays open the bowl
        /// gushes up like a fountain and splashes all over the desk.
        /// </summary>
        void MouthSloshes(float dt)
        {
            float open = rig.Current.MouthOpen;
            bool isOpen = open > 0.4f;
            if (isOpen && !mouthWasOpen)
            {
                Heave(1f, Random.value < 0.35f);
                squeezeSince = Time.time;
            }
            else if (isOpen && Time.time - squeezeSince > 0.3f && Mess.Instance)
            {
                gushOwed += open * 38f * dt;
                Vector3 aim = (Vector3.up * 0.85f + Vector3.back * 0.25f + new Vector3(Mathf.Sin(Time.time * 1.6f) * 0.7f, 0f, 0f)).normalized;
                for (; gushOwed >= 1f; gushOwed -= 1f)
                    Mess.Instance.Drop(BowlPoint + Random.insideUnitSphere * topRadius * 0.15f,
                        aim * Random.Range(1.5f, 2.3f) * Mathf.Lerp(0.85f, 1.15f, open) + Random.insideUnitSphere * 0.25f + meter.Velocity * 0.6f,
                        ToiletWater, Random.Range(0.013f, 0.022f), Random.Range(0.035f, 0.05f));
                if (Time.time >= nextSqueezeSound)
                {
                    nextSqueezeSound = Time.time + Random.Range(0.6f, 1f);
                    Sfx.Play("slosh", 0.5f, 0.15f);
                }
            }
            mouthWasOpen = isOpen;
        }

        /// <summary>
        /// A wave of water heaves up out of the bowl and comes down over the rim in a sheet, splashing across the desk
        /// the way the toilet's moving (with a soggy wad of toilet paper, sometimes).
        /// </summary>
        void Heave(float strength, bool paper)
        {
            if (!Mess.Instance) return;
            if (strength > 0.7f || Time.time >= nextSqueezeSound)
            {
                nextSqueezeSound = Time.time + 0.5f;
                Sfx.Play(strength > 0.7f ? "water_spill" : "slosh", Mathf.Clamp(0.35f + strength * 0.35f, 0.3f, 0.95f), 0.1f);
            }
            rig.Bonk(0.06f * strength);
            Vector3 dir = SpillDirection();
            float push = Mathf.Sqrt(strength);
            int drops = Mathf.RoundToInt(24 * strength);
            for (int i = 0; i < drops; i++)
            {
                Vector3 d = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * dir;
                Mess.Instance.Drop(BowlPoint + d * topRadius * Random.Range(0.2f, 0.5f),
                    d * Random.Range(0.35f, 0.9f) * push + Vector3.up * Random.Range(0.6f, 1.4f) * push + meter.Velocity * 0.7f,
                    ToiletWater, Random.Range(0.013f, 0.022f), Random.Range(0.035f, 0.055f));
            }
            if (paper) SoggyPaper(dir);
        }

        /// <summary>A soggy wad of toilet paper flops out onto the desk and rolls, leaving a wet trail.</summary>
        void SoggyPaper(Vector3 dir)
        {
            Transform wad = Junk(JunkMeshes.CrumpledPaper, new Color(0.9f, 0.9f, 0.86f), Vector3.one * Random.Range(0.045f, 0.06f));
            wad.position = BowlPoint + Vector3.up * 0.02f;
            Vector3 v = dir * Random.Range(0.5f, 0.9f) + Vector3.up * Random.Range(1f, 1.6f) + meter.Velocity * 0.6f;
            DeskDebris.Instance.AddPiece(wad, v, 0.026f, 0.022f, disposable: true, ball: true, trail: ToiletWater);
            Sfx.Play("plop", 0.5f, 0.15f);
        }

        /// <summary>The bowl erupts: a column of water straight up that rains down all round.</summary>
        IEnumerator Geyser(float seconds)
        {
            rig.Bonk(0.12f);
            Sfx.Play("water_spill", 0.8f, 0.08f);
            yield return Stream(() => BowlPoint, () => (Vector3.up + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.45f, 0f))).normalized,
                ToiletWater, seconds, 60f, new Vector2(1.8f, 3f), new Vector2(0.014f, 0.024f), 0.05f);
        }

        /// <summary>It flushes on its own: the roar, a shudder, then a geyser out of the bowl, and a gurgle as it drains.</summary>
        IEnumerator Flush()
        {
            Sfx.Play("toilet_flush", 0.85f, 0.04f);
            for (int i = 0; i < 4; i++)
            {
                rig.Bonk(0.05f);
                yield return new WaitForSeconds(0.15f);
            }
            if (Random.value < 0.6f) SoggyPaper(new Vector3(Random.Range(-0.6f, 0.6f), 0f, -1f).normalized);
            yield return Geyser(0.9f);
            Sfx.Play("slurp", 0.5f, 0.1f);
        }

        float nextCoffeeSlosh;

        /// <summary>
        /// Coffee slops over the rim the way the mug's moving: a sheet of it over one side that pours down the outside
        /// (running down the front when it's the side facing the camera) and puddles round the foot of the mug.
        /// </summary>
        void CoffeeSlop(float strength)
        {
            if (!Mess.Instance) return;
            if (Time.time >= nextCoffeeSlosh)
            {
                nextCoffeeSlosh = Time.time + Random.Range(0.5f, 1f);
                Sfx.Play("slosh", 0.55f, 0.12f);
            }
            Vector3 dir = SpillDirection(0.01f);
            int n = Mathf.RoundToInt(Random.Range(5, 8) * strength);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * dir;
                Mess.Instance.Drop(TopPoint + d * topRadius * 0.95f,
                    d * Random.Range(0.12f, 0.45f) * strength + Vector3.up * Random.Range(0f, 0.3f) * strength + meter.Velocity * 0.6f,
                    Coffee, Random.Range(0.014f, 0.022f), Random.Range(0.035f, 0.05f));
            }
            if (dir.z < -0.2f && Random.value < 0.45f * strength) RunDownAt(Random.Range(0.86f, 0.94f), dir.x, Coffee, 0.026f);
        }

        /// <summary>A big jolt: the coffee heaves up out of the mug and comes down all round, runs down the sides and spreads into a steaming puddle.</summary>
        void CoffeeHeave()
        {
            if (!Mess.Instance) return;
            Sfx.Play("water_spill", 0.7f, 0.1f);
            Sfx.Play("steam_hiss", 0.35f, 0.1f);
            rig.Bonk(0.12f);
            for (int i = 0; i < 34; i++)
            {
                Vector2 around = Random.insideUnitCircle;
                if (around.y > 0.3f) around.y = -around.y;
                Mess.Instance.Drop(TopPoint + new Vector3(around.x, 0f, around.y) * topRadius * 0.6f,
                    new Vector3(around.x * Random.Range(0.3f, 0.9f), Random.Range(0.8f, 1.6f), around.y * Random.Range(0.3f, 0.9f)) + meter.Velocity * 0.5f,
                    Coffee, Random.Range(0.015f, 0.024f), Random.Range(0.04f, 0.06f));
            }
            for (int i = 0; i < 3; i++) RunDownAt(Random.Range(0.86f, 0.94f), (i - 1) * 0.5f + Random.Range(-0.15f, 0.15f), Coffee, 0.028f);
            StartCoroutine(Cloud(TopPoint, 6, Steam, 0.16f, 2f, new Vector3(0f, 0.25f, 0f), 0.06f));
            Mess.Instance.DeskStain(transform.position + new Vector3(Random.Range(-0.05f, 0.05f), 0f, -topRadius - 0.04f), 0.07f, Coffee, 150f, 0.6f, true);
        }

        /// <summary>
        /// A drifting cloud (spores, steam): soft camera-facing puffs that rise, swell and thin out. `spread` is how
        /// far apart the puffs start.
        /// </summary>
        IEnumerator Cloud(Vector3 at, int puffs, Color colour, float size, float life, Vector3 drift, float spread)
        {
            if (!smoke) yield break;
            var parts = new List<(Transform t, Renderer r, Vector3 drift, float size, float seed, float delay)>();
            Camera cam = Camera.main;
            for (int i = 0; i < puffs; i++)
            {
                Transform c = Item(PrimitiveType.Quad, Color.white, Vector3.zero);
                var renderer = c.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.sharedMaterial = smoke;
                c.position = at + Random.insideUnitSphere * spread;
                parts.Add((c, renderer, drift + Random.insideUnitSphere * drift.magnitude * 0.4f, size * Random.Range(0.7f, 1.3f), Random.Range(0f, 50f), Random.Range(0f, 0.2f)));
            }
            var block = new MaterialPropertyBlock();
            for (float t = 0f; t < life + 0.2f; t += Time.deltaTime)
            {
                foreach (var (c, r, d, s, seed, delay) in parts)
                {
                    if (!c) continue;
                    float k = Mathf.Clamp01((t - delay) / life);
                    c.position += d * Time.deltaTime * (1f - k * 0.5f);
                    c.localScale = Vector3.one * s * Mathf.Lerp(0.35f, 1.5f, Mathf.Sqrt(k));
                    if (cam) c.rotation = Quaternion.LookRotation(c.position - cam.transform.position);
                    Color tint = colour;
                    tint.a *= Mathf.SmoothStep(0f, 1f, k * 5f) * (1f - Mathf.SmoothStep(0.4f, 1f, k));
                    block.Clear();
                    block.SetColor("_Color", tint);
                    block.SetFloat("_Seed", seed);
                    r.SetPropertyBlock(block);
                }
                yield return null;
            }
            foreach (var part in parts) if (part.t) Destroy(part.t.gameObject);
        }

        /// <summary>A cracked egg leaks: yolk and white dribble out of the crack low on its front.</summary>
        void Leak()
        {
            if (!Mess.Instance) return;
            Vector3 crack = transform.TransformPoint(new Vector3(Random.Range(-0.08f, 0.08f), topHeight * 1.2f, -topRadius * 0.95f));
            bool yolk = Random.value < 0.45f;
            Mess.Instance.Drop(crack, meter.Velocity * 0.5f + new Vector3(Random.Range(-0.15f, 0.15f), -0.1f, -Random.Range(0.1f, 0.35f)),
                yolk ? Yolk : White, yolk ? 0.016f : 0.02f, yolk ? 0.035f : 0.05f);
        }

        /// <summary>The egg cracks on the desk: yolk and white spread out, bits of shell fly, and it leaks for a while.</summary>
        void Crack()
        {
            Sfx.Play("egg_crack");
            leakingUntil = Time.time + 90f;
            Vector3 front = transform.position + new Vector3(Random.Range(-0.1f, 0.1f), 0f, -topRadius - 0.1f);
            if (Mess.Instance)
            {
                Mess.Instance.DeskStain(front, 0.17f, White, 150f, 0.5f, true);
                Mess.Instance.DeskStain(front + new Vector3(0.02f, 0f, 0.01f), 0.07f, Yolk, 150f, 0.3f, true);
                // The yolk itself, a glossy dome sitting in it (the hands squish it).
                Mess.Instance.Blob(null, front + new Vector3(0.02f, 0.012f, 0.01f), new Vector3(0.075f, 0.03f, 0.075f), new Color(1f, 0.68f, 0.05f), 150f, 0.3f);
            }
            for (int i = 0; i < 8; i++)
            {
                Transform shell = Item(PrimitiveType.Sphere, new Color(0.98f, 0.94f, 0.86f), new Vector3(Random.Range(0.03f, 0.06f), 0.008f, Random.Range(0.025f, 0.05f)));
                shell.position = front + Vector3.up * 0.1f;
                shell.rotation = Random.rotation;
                DeskDebris.Instance.AddPiece(shell, new Vector3(Random.Range(-1f, 1f), Random.Range(0.6f, 1.6f), Random.Range(-1f, 0.3f)), 0.025f, 0.004f, disposable: true);
            }
            if (Chaos && Chaos.library && Chaos.library.ActiveRig == rig)
                for (int i = 0; i < 2; i++) Chaos.DripDown(0.42f, Random.Range(-0.4f, 0.4f), Yolk, 0.024f, 0.06f);
        }

        /// <summary>Things fall on the egg: a frying pan drops from the ceiling, BONK, and it cracks.</summary>
        IEnumerator FryingPan()
        {
            Transform top = Tip;
            var pan = new GameObject("Frying pan").transform;
            Transform dish = Item(PrimitiveType.Cylinder, new Color(0.12f, 0.12f, 0.14f), new Vector3(0.34f, 0.025f, 0.34f));
            dish.SetParent(pan, false);
            Transform handle = Item(PrimitiveType.Cube, new Color(0.25f, 0.15f, 0.08f), new Vector3(0.3f, 0.025f, 0.04f));
            handle.SetParent(pan, false);
            handle.localPosition = new Vector3(0.3f, 0f, 0f);
            Vector3 start = top.position + Vector3.up * 1.6f;
            pan.position = start;
            float v = 0f;
            while (pan.position.y > top.position.y + 0.05f)
            {
                v += 14f * Time.deltaTime;
                pan.position += Vector3.down * v * Time.deltaTime;
                pan.position = new Vector3(top.position.x, pan.position.y, top.position.z);
                pan.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 20f) * 6f, 30f, 0f);
                yield return null;
            }
            Sfx.Play("pan_bonk", 1f, 0.06f);
            rig.Bonk(0.35f);
            Crack();
            // The pan bounces off and clatters onto the desk.
            if (DeskDebris.Instance) DeskDebris.Instance.AddPiece(pan, new Vector3(Random.Range(0.6f, 1f) * (Random.value < 0.5f ? -1f : 1f), 1.2f, -0.6f), 0.17f, 0.015f, disposable: true);
            else Destroy(pan.gameObject);
        }

        /// <summary>A cloud of dust out of the vacuum's back (and, sounds on, a rude noise); a big one blows harder.</summary>
        IEnumerator DustCloud(bool big)
        {
            if (SoundBank.Instance && SoundBank.Instance.Has("fart")) Sfx.Play("fart", big ? 1f : 0.7f, 0.12f);
            else if (Sound.On && audioSource && farts != null)
            {
                audioSource.pitch = Random.Range(0.85f, 1.2f);
                audioSource.PlayOneShot(farts[Random.Range(0, farts.Length)], big ? 1f : 0.7f);
            }
            Vector3 vent = transform.TransformPoint(new Vector3(Random.Range(-0.1f, 0.1f), 0.14f, topRadius * 0.6f));
            int puffs = big ? 18 : 10;
            var clouds = new List<(Transform t, Renderer r, Vector3 drift, float size, float seed, float delay)>();
            Camera cam = Camera.main;
            for (int i = 0; i < puffs; i++)
            {
                Transform c = Item(PrimitiveType.Quad, Color.white, Vector3.zero);
                var renderer = c.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (smoke) renderer.sharedMaterial = smoke;
                c.position = vent + Random.insideUnitSphere * 0.04f;
                clouds.Add((c, renderer, new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(0.05f, 0.3f), Random.Range(-0.55f, 0.05f)) * (big ? 1.3f : 1f),
                    Random.Range(0.2f, 0.36f) * (big ? 1.4f : 1f), Random.Range(0f, 50f), Random.Range(0f, 0.25f)));
            }
            if (Mess.Instance)
                for (int i = 0; i < (big ? 5 : 3); i++)
                    Mess.Instance.DeskStain(vent + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.35f, 0f)), Random.Range(0.05f, 0.1f), Dust, 60f, 1f);
            var block = new MaterialPropertyBlock();
            const float span = 2.6f;
            for (float t = 0f; t < span + 0.25f; t += Time.deltaTime)
            {
                foreach (var (c, r, drift, size, seed, delay) in clouds)
                {
                    float k = Mathf.Clamp01((t - delay) / span);
                    c.position += drift * Time.deltaTime * (1f - k) * (t > delay ? 1f : 0f);
                    c.localScale = Vector3.one * size * Mathf.Lerp(0.3f, 1.4f, Mathf.Sqrt(k));
                    if (cam) c.rotation = Quaternion.LookRotation(c.position - cam.transform.position);
                    Color colour = new Color(0.6f, 0.55f, 0.46f, 0.6f * Mathf.SmoothStep(0f, 1f, k * 6f) * (1f - Mathf.SmoothStep(0.4f, 1f, k)));
                    block.Clear();
                    block.SetColor("_Color", colour);
                    block.SetFloat("_Seed", seed);
                    r.SetPropertyBlock(block);
                }
                yield return null;
            }
            foreach (var cloud in clouds) Destroy(cloud.t.gameObject);
        }

        /// <summary>
        /// A cartoon fart, synthesized: a raspy buzz whose pitch flutters and sags, with breathy noise, shaped
        /// to start sharp and trail off. Different seeds give different ones.
        /// </summary>
        static AudioClip MakeFart(int seed)
        {
            const int rate = 44100;
            var random = new System.Random(seed);
            float duration = 0.5f + (float)random.NextDouble() * 0.6f;
            int n = (int)(rate * duration);
            var data = new float[n];
            float f0 = 70f + (float)random.NextDouble() * 50f, flutter = 14f + (float)random.NextDouble() * 10f;
            float phase = 0f, smooth = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float f = f0 * (1.25f - 0.55f * t / duration) * (1f + 0.3f * Mathf.Sin(2f * Mathf.PI * flutter * t + 2f * Mathf.Sin(2f * Mathf.PI * 3f * t)));
                phase += 2f * Mathf.PI * f / rate;
                float buzz = Mathf.Sign(Mathf.Sin(phase)) * 0.55f + Mathf.Sin(phase) * 0.45f;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                float envelope = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((duration - t) / 0.2f) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 6f * t));
                float sample = (buzz * 0.75f + noise * 0.25f) * envelope;
                smooth += (sample - smooth) * 0.18f; // soften the rasp a little
                data[i] = smooth * 0.6f;
            }
            AudioClip clip = AudioClip.Create($"Fart {seed}", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
