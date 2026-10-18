using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Muffin.EditorTools
{
    /// <summary>
    /// Builds the motion prototype scene (camera, lights, input, the sunflower, HUD, self-test) and its
    /// materials, so the scene can be regenerated from code. Menu: Muffin → Create Motion Prototype Scene.
    /// Batch: -executeMethod Muffin.EditorTools.MuffinPrototypeSetup.CreateSceneBatch / BuildWindowsBatch.
    /// </summary>
    public static class MuffinPrototypeSetup
    {
        const string Root = "Assets/Muffin";
        const string MaterialsDir = Root + "/Materials";
        public const string ScenePath = Root + "/Scenes/MotionPrototype.unity";
        /// <summary>Where the Windows build goes; MUFFIN_BUILD_PATH overrides it (e.g. while the app is running).</summary>
        public static string BuildPath => System.Environment.GetEnvironmentVariable("MUFFIN_BUILD_PATH") ?? "Builds/Win64/MuffinMotionPrototype.exe";
        /// <summary>The downloaded OpenSeeFace release, next to the Unity project (not committed).</summary>
        const string TrackerSource = "../third-party/OpenSeeFace-v1.20.5";

        [MenuItem("Muffin/Create Motion Prototype Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory(MaterialsDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null || unlit == null) throw new System.InvalidOperationException("URP shaders not found: is this a URP project?");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var background = new Color(0.10f, 0.07f, 0.16f);
            var camera = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camera.AddComponent<Camera>();
            camera.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.fieldOfView = 26f;
            camera.transform.position = new Vector3(0f, 0.68f, -3.2f);
            camera.transform.LookAt(new Vector3(0f, 0.62f, 0f));

            var key = new GameObject("Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.05f;
            key.color = new Color(1f, 0.96f, 0.9f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.75f;
            key.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            var rim = new GameObject("Rim Light").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.5f;
            rim.color = new Color(0.78f, 0.62f, 1f);
            rim.transform.rotation = Quaternion.Euler(20f, 160f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Softer, fuller light from all round and much less of the sky reflected in everything: less glare, less
            // of the rubbery, plastic look, still cartoony.
            RenderSettings.ambientSkyColor = new Color(0.64f, 0.61f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.42f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.2f, 0.26f);
            RenderSettings.reflectionIntensity = 0.3f;

            var input = new GameObject("Input");
            input.AddComponent<OpenSee.OpenSee>();
            input.AddComponent<Muffin.CameraFeed>();
            var webcam = input.AddComponent<Muffin.WebcamHeadPoseSource>();
            var mouse = input.AddComponent<Muffin.MouseHeadPoseSource>();
            var scripted = input.AddComponent<Muffin.ScriptedHeadPoseSource>();
            scripted.enabled = false;
            var synthetic = input.AddComponent<Muffin.SyntheticTrackerSource>();

            // Sticker-face materials, shared by every avatar: unlit, so they read as flat cartoon stickers.
            var faceInk = MakeMaterial("Face Ink", unlit, new Color(0.10f, 0.07f, 0.12f), 0f);
            var faceShine = MakeMaterial("Face Shine", unlit, Color.white, 0f);
            var faceMouth = MakeMaterial("Face Mouth Inside", unlit, new Color(0.42f, 0.08f, 0.12f), 0f);
            var faceBlush = MakeMaterial("Face Blush", unlit, new Color(1f, 0.55f, 0.62f), 0f);
            Shader cutoutShader = Shader.Find("Muffin/FaceCutout");
            if (cutoutShader == null) throw new System.InvalidOperationException("Muffin/FaceCutout shader missing");
            var faceCutout = MakeMaterial("Face Cutout", cutoutShader, Color.white, 0f);

            var library = new GameObject("Avatars").AddComponent<Muffin.AvatarLibrary>();
            library.source = webcam; // falls back to the mouse when the tracker isn't available

            var sunflowerObject = new GameObject("Sunflower");
            sunflowerObject.transform.SetParent(library.transform, false);
            library.avatars.Add(sunflowerObject.AddComponent<Muffin.BendRig>());
            AddFace(sunflowerObject, faceInk, faceShine, faceMouth, faceBlush, faceCutout);
            var sunflower = sunflowerObject.AddComponent<Muffin.SunflowerAvatar>();
            sunflower.pot = MakeMaterial("Pot", lit, new Color(0.80f, 0.42f, 0.26f), 0.25f);
            sunflower.soil = MakeMaterial("Soil", lit, new Color(0.24f, 0.15f, 0.10f), 0.05f);
            sunflower.stem = MakeMaterial("Stem", lit, new Color(0.30f, 0.66f, 0.24f), 0.2f);
            sunflower.petals = MakeMaterial("Petals", lit, new Color(1.00f, 0.80f, 0.16f), 0.15f);
            sunflower.centre = MakeMaterial("Flower Centre", lit, new Color(0.40f, 0.23f, 0.10f), 0.2f);

            // Generated object avatars: any GLB in Avatars/Generated that has a spec below.
            var makers = new System.Collections.Generic.List<Muffin.MessMaker>();
            foreach (AvatarSpec spec in GeneratedAvatars)
            {
                var (mesh, materials) = BakeAvatar($"{GeneratedDir}/{spec.Id}.glb", spec);
                if (mesh == null) continue;
                var go = new GameObject(spec.Name);
                go.transform.SetParent(library.transform, false);
                var objectRig = go.AddComponent<Muffin.BendRig>();
                objectRig.maxBendDegrees = spec.MaxBend;
                objectRig.maxFlopDegrees = spec.FlopMax;
                objectRig.flopGravity = spec.FlopGravity;
                objectRig.flopFrequency = spec.FlopFrequency;
                objectRig.flopSideways = spec.Sideways;
                objectRig.flopDamping = spec.FlopDamping;
                objectRig.turnWhip = spec.TurnWhip;
                if (spec.Roll > 0f)
                {
                    objectRig.rollDegrees = spec.Roll;
                    objectRig.rollRadius = mesh.bounds.extents.x * 0.95f;
                    objectRig.rollDamping = spec.RollDamping;
                    objectRig.rollFrequency = spec.RollFrequency;
                    objectRig.rollJiggle = spec.Jiggle;
                    objectRig.breathing = 0f;
                }
                library.avatars.Add(objectRig);
                AddFace(go, faceInk, faceShine, faceMouth, faceBlush, faceCutout);
                var avatar = go.AddComponent<Muffin.MeshAvatar>();
                avatar.displayName = spec.Name;
                avatar.mesh = mesh;
                avatar.materials = materials;
                avatar.rootFraction = spec.Root;
                avatar.headFraction = spec.Head;
                avatar.tipFraction = spec.Tip;
                avatar.faceFraction = spec.Face;
                avatar.faceSize = spec.FaceSize;
                avatar.faceHasMouth = spec.Mouth;
                avatar.faceShiftX = spec.FaceX;
                if (spec.Extra == "pepperoni")
                {
                    var toppings = go.AddComponent<Muffin.Toppings>();
                    toppings.pepperoni = MakeMaterial("Pepperoni", lit, new Color(0.62f, 0.13f, 0.1f), 0.35f);
                }
                else if (spec.Extra is "egg" or "trash" or "juice" or "meatballs" or "vacuum" or "hotdog" or "popcorn" or "jello"
                         or "taco" or "mold" or "toaster" or "ketchup" or "coffee" or "toilet")
                {
                    var maker = go.AddComponent<Muffin.MessMaker>();
                    maker.kind = spec.Extra switch
                    {
                        "egg" => Muffin.MessKind.Egg,
                        "trash" => Muffin.MessKind.Trash,
                        "juice" => Muffin.MessKind.Juice,
                        "meatballs" => Muffin.MessKind.Meatballs,
                        "hotdog" => Muffin.MessKind.HotDog,
                        "popcorn" => Muffin.MessKind.Popcorn,
                        "jello" => Muffin.MessKind.Jello,
                        "taco" => Muffin.MessKind.Taco,
                        "mold" => Muffin.MessKind.Mold,
                        "toaster" => Muffin.MessKind.Toaster,
                        "ketchup" => Muffin.MessKind.Ketchup,
                        "coffee" => Muffin.MessKind.Coffee,
                        "toilet" => Muffin.MessKind.Toilet,
                        _ => Muffin.MessKind.Vacuum,
                    };
                    maker.lit = MakeMaterial("Mess Item", lit, Color.white, 0.3f);
                    makers.Add(maker);
                    // Without a floppy top, things come off the head bone, which sits well below the real top (a rim,
                    // an opening): lift them up to it.
                    if (spec.Extra == "toilet")
                    {
                        // The bowl's water: in front of the tank, a bit under halfway up (the model's own proportions).
                        Bounds tb = mesh.bounds;
                        maker.bowl = new Vector3(0f, tb.min.y + tb.size.y * spec.BowlHeight, tb.min.z + tb.size.z * 0.3f);
                    }
                    if (spec.Tip >= 0.999f)
                    {
                        float headY = mesh.bounds.min.y + mesh.bounds.size.y * Mathf.Min(spec.Head, spec.Face - 0.02f);
                        maker.topLift = Mathf.Max(0f, mesh.bounds.max.y - headY - 0.02f);
                    }
                    if (spec.Extra == "vacuum")
                    {
                        var hose = go.AddComponent<Muffin.VacuumHose>();
                        hose.socketHeight = spec.Height * spec.CutAbove - 0.01f;
                        hose.hoseMaterial = MakeMaterial("Vacuum Hose", lit, new Color(0.42f, 0.43f, 0.46f), 0.3f);
                        hose.nozzleMaterial = MakeMaterial("Vacuum Nozzle", lit, new Color(0.62f, 0.64f, 0.68f), 0.45f);
                    }
                    maker.topRadius = mesh.bounds.extents.x * 0.75f;
                    maker.topHeight = mesh.bounds.size.y * 0.12f;
                }
                else if (spec.Extra == "sprinkles")
                {
                    var shedder = go.AddComponent<Muffin.SprinkleShedder>();
                    shedder.topRadius = mesh.bounds.extents.x * 0.8f;
                    shedder.topHeight = mesh.bounds.size.y * 0.15f;
                }
            }

            var deskObject = new GameObject("Desk Scene");
            deskObject.AddComponent<Muffin.DeskInput>();
            var desk = deskObject.AddComponent<Muffin.DeskScene>();
            // The camera frames itself for any window shape, desk or no desk (and does the X zoom).
            var stage = camera.AddComponent<Muffin.StageCamera>();
            stage.library = library;
            stage.desk = desk;
            desk.wood = MakeMaterial("Desk Wood", lit, new Color(0.55f, 0.36f, 0.24f), 0.3f);
            desk.keyboardBody = MakeMaterial("Keyboard Body", lit, new Color(0.16f, 0.15f, 0.2f), 0.4f);
            desk.keycaps = MakeMaterial("Keycaps", lit, new Color(0.36f, 0.34f, 0.42f), 0.35f);
            desk.pad = MakeMaterial("Mouse Pad", lit, new Color(0.36f, 0.22f, 0.5f), 0.1f);
            desk.mouseBody = MakeMaterial("Mouse Body", lit, new Color(0.72f, 0.72f, 0.8f), 0.35f);
            desk.hands = MakeMaterial("Cartoon Hands", lit, new Color(1f, 1f, 1f), 0.2f);
            // What falls onto the desk (sprinkles, pepperoni), and R / K.
            var debris = deskObject.AddComponent<Muffin.DeskDebris>();
            debris.desk = desk;
            debris.library = library;
            var sprinkleColours = new[]
            {
                new Color(1f, 0.42f, 0.62f), new Color(1f, 0.85f, 0.25f), new Color(0.35f, 0.7f, 1f),
                new Color(0.45f, 0.88f, 0.45f), new Color(0.7f, 0.45f, 1f), new Color(1f, 0.97f, 0.92f),
            };
            debris.sprinkleMaterials = new Material[sprinkleColours.Length];
            for (int i = 0; i < sprinkleColours.Length; i++)
            {
                Material m = MakeMaterial($"Sprinkle {i + 1}", lit, sprinkleColours[i], 0.3f);
                m.enableInstancing = true; // hundreds of sprinkles, drawn in a few calls
                EditorUtility.SetDirty(m);
                debris.sprinkleMaterials[i] = m;
            }

            // The mess that stays (puddles, splats, trails), and the chaos set (viewer events from Twitch chat).
            var messObject = new GameObject("Mess");
            var mess = messObject.AddComponent<Muffin.Mess>();
            mess.desk = desk;
            mess.stainMaterial = Transparent(MakeMaterial("Stain", lit, new Color(1f, 1f, 1f, 0.8f), 0.5f));
            mess.blobMaterial = MakeMaterial("Mess Blob", lit, Color.white, 0.3f);
            mess.blobMaterial.enableInstancing = true;
            mess.stainMaterial.enableInstancing = true;
            var chaosObject = new GameObject("Chaos");
            var chaos = chaosObject.AddComponent<Muffin.ChaosDirector>();
            chaos.library = library;
            chaos.desk = desk;
            chaos.debris = debris;
            chaos.mess = mess;
            chaos.cam = cam;
            Shader flameShader = Shader.Find("Muffin/Flame"), smokeShader = Shader.Find("Muffin/Smoke");
            if (flameShader == null || smokeShader == null) throw new System.InvalidOperationException("Muffin/Flame or Muffin/Smoke shader missing");
            chaos.flame = MakeMaterial("Flame", flameShader, Color.white, 0f);
            chaos.flameYellow = MakeMaterial("Flame Yellow", unlit, new Color(1f, 0.85f, 0.35f), 0f);
            chaos.smoke = MakeMaterial("Smoke", smokeShader, new Color(0.35f, 0.35f, 0.38f, 0.55f), 0f);
            chaos.smoke.SetColor("_Color", new Color(0.35f, 0.35f, 0.38f, 0.55f));
            foreach (Muffin.MessMaker maker in makers) maker.smoke = chaos.smoke; // the vacuum's dust clouds
            // Sound effects (props-service batches/sfx-01.json → Assets/Muffin/Sounds).
            var bank = chaosObject.AddComponent<Muffin.SoundBank>();
            var sounds = new System.Collections.Generic.List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Muffin/Sounds" }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip) sounds.Add(clip);
            }
            bank.clips = sounds.ToArray();
            Shader bubbleShader = Shader.Find("Muffin/Bubble");
            if (bubbleShader == null) throw new System.InvalidOperationException("Muffin/Bubble shader missing");
            chaos.bubble = MakeMaterial("Soap Bubble", bubbleShader, Color.white, 0f);
            chaos.rain = Transparent(MakeMaterial("Rain", lit, new Color(0.7f, 0.85f, 1f, 0.55f), 0.9f));
            chaos.rain.enableInstancing = true;
            chaos.glass = Transparent(MakeMaterial("Glass", lit, new Color(0.85f, 0.95f, 1f, 0.25f), 0.95f));
            chaos.water = Transparent(MakeMaterial("Water", lit, new Color(0.4f, 0.65f, 1f, 0.6f), 0.95f));
            chaos.pieTin = MakeMaterial("Pie Crust", lit, new Color(0.86f, 0.6f, 0.3f), 0.2f); // the back of a pie reads as golden crust

            chaos.cream = MakeMaterial("Cream", lit, new Color(1f, 0.98f, 0.94f), 0.2f);
            chaos.tomato = MakeMaterial("Tomato", lit, new Color(0.85f, 0.1f, 0.06f), 0.55f);
            chaos.leaf = MakeMaterial("Leaf", lit, new Color(0.25f, 0.6f, 0.2f), 0.4f);
            chaos.canBody = MakeMaterial("Cream Can", lit, new Color(0.45f, 0.7f, 0.95f), 0.45f);
            Shader drip = Shader.Find("Muffin/Drip");
            if (drip == null) throw new System.InvalidOperationException("Muffin/Drip shader missing");
            chaos.slime = MakeMaterial("Slime", drip, new Color(0.4f, 0.55f, 0.09f, 0.94f), 0f);
            chaos.slime.SetColor("_Color", new Color(0.4f, 0.55f, 0.09f, 0.94f)); // booger green (the drip shader's own colour)

            var hudObject = new GameObject("HUD");
            var hud = hudObject.AddComponent<Muffin.PerfHud>();
            hud.library = library;
            hud.desk = desk;
            var diagnostics = hudObject.AddComponent<Muffin.Diagnostics>();
            diagnostics.library = library;
            hud.diagnostics = diagnostics;
            hud.webcam = webcam;
            hud.mouse = mouse;
            hud.cam = cam;
            hud.background = background;
            // Rooms behind the set: pictures in Avatars/../Backdrops (props-service batches/backdrops-01.json).
            var backdrop = hudObject.AddComponent<Muffin.Backdrop>();
            backdrop.cam = cam;
            backdrop.material = MakeMaterial("Backdrop", unlit, Color.white, 0f);
            var roomPictures = new System.Collections.Generic.List<Texture2D>();
            var tallPictures = new System.Collections.Generic.List<Texture2D>();
            var roomNames = new System.Collections.Generic.List<string>();
            Texture2D Picture(string path)
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && (importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp))
                {
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.maxTextureSize = 2048;
                    importer.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            foreach (var (id, name) in Rooms)
            {
                Texture2D picture = Picture($"{BackdropDir}/{id}.jpg");
                if (!picture) continue;
                roomPictures.Add(picture);
                tallPictures.Add(Picture($"{BackdropDir}/{id}_tall.jpg")); // may be missing: the wide one is cropped then
                roomNames.Add(name);
            }
            backdrop.pictures = roomPictures.ToArray();
            backdrop.tallPictures = tallPictures.ToArray();
            backdrop.names = roomNames.ToArray();
            hud.backdrop = backdrop;
            var test = hudObject.AddComponent<Muffin.SelfTest>();
            test.library = library;
            test.scripted = scripted;
            test.synthetic = synthetic;
            test.hud = hud;
            test.desk = desk;
            test.chaos = chaos;
            chaos.hud = hud;
            var panel = hudObject.AddComponent<Muffin.ControlPanel>();
            panel.library = library;
            panel.webcam = webcam;
            panel.hud = hud;
            panel.desk = desk;
            panel.debris = debris;
            panel.chaos = chaos;
            panel.diagnostics = diagnostics;
            var remote = hudObject.AddComponent<Muffin.RemoteControl>();
            remote.library = library;
            remote.webcam = webcam;
            remote.hud = hud;
            remote.desk = desk;
            remote.debris = debris;
            remote.chaos = chaos;
            remote.panel = panel;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "Muffin Motion Prototype";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.preserveFramebufferAlpha = true;
            KeepAlphaInRenderPipelines();
            AssetDatabase.SaveAssets();
            Debug.Log("[Muffin] Motion prototype scene saved to " + ScenePath);
        }

        const string BackdropDir = "Assets/Muffin/Backdrops";

        /// <summary>The rooms behind the set, in the order G cycles them: funny places to be at a desk.</summary>
        static readonly (string id, string name)[] Rooms =
        {
            ("vacuum_showroom", "Vacuum sales floor"), ("airport_terminal", "Airport gate"), ("construction_site", "Construction site"),
            ("laundromat", "Laundromat"), ("dentist_waiting_room", "Dentist's waiting room"), ("bowling_alley", "Bowling alley"),
            ("throne_room", "Castle throne room"), ("dmv_office", "DMV waiting room"),
        };

        const string GeneratedDir = "Assets/Muffin/Avatars/Generated";
        const string BakedDir = "Assets/Muffin/Avatars/Baked";

        /// <summary>How each generated model becomes an avatar (fractions are of its height).</summary>
        sealed class AvatarSpec
        {
            public string Id, Name;
            /// <summary>
            /// Tripo P1 models face glTF +X (as Death Muffin's docs note), which glTFast maps to Unity -X;
            /// -90 turns that toward the camera (-Z). SPAR3D models face glTF -Z, already the camera: 0.
            /// </summary>
            public float Yaw = -90f;
            public float Height = 1.15f;
            public float Root, Head, Face;
            public float FaceSize = 1f;
            public bool Mouth = true;
            /// <summary>Total bend at a full lean: chunky objects look best bending less than a thin stem.</summary>
            public float MaxBend = 35f;
            /// <summary>Above this fraction the avatar flops on its own (1 = no floppy tip).</summary>
            public float Tip = 1f;
            /// <summary>Bits that fall off onto the desk: "pepperoni" (Toppings) or "sprinkles" (SprinkleShedder).</summary>
            public string Extra;
            /// <summary>How far the floppy tip can flop, and how much gravity drags it (a wide top wants less).</summary>
            public float FlopMax = 140f, FlopGravity = 0.7f, FlopFrequency = 2f;
            /// <summary>Rigid: no bending or squashing, it rocks as one piece on its round bottom, this many degrees at a full lean (0 = bends).</summary>
            public float Roll;
            /// <summary>How quickly a rigid avatar settles after rocking (low: rocks a few times, like a weeble).</summary>
            public float RollDamping = 0.25f, RollFrequency = 2.2f;
            /// <summary>Moves the face sideways (metres): a mug's handle widens it, so the face is shifted back onto the body.</summary>
            public float FaceX;
            /// <summary>Toilet: the bowl's water height as a share of the height.</summary>
            public float BowlHeight = 0.42f;
            /// <summary>Rigid avatars only: jelly squash and stretch all over when jolted (0 = solid).</summary>
            public float Jiggle;
            /// <summary>Everything above this fraction of the height is cut off the model (1 = nothing).</summary>
            public float CutAbove = 1f;
            /// <summary>The floppy top only sways side to side, and how quickly its wobble dies down.</summary>
            public bool Sideways;
            public float FlopDamping = 0.16f;
            /// <summary>How hard starting or stopping a turn flings the floppy top.</summary>
            public float TurnWhip = 1.6f;
        }

        static readonly AvatarSpec[] GeneratedAvatars =
        {
            new AvatarSpec { Id = "tbone_steak", Name = "T-bone steak", Root = 0.1f, Head = 0.46f, Face = 0.6f, MaxBend = 35f },
            new AvatarSpec { Id = "potted_cactus", Name = "Potted cactus", Root = 0.3f, Head = 0.48f, Face = 0.62f, FaceSize = 0.9f, MaxBend = 30f },

            new AvatarSpec { Id = "soda_can", Name = "Soda can", Root = 0.06f, Head = 0.48f, Face = 0.6f, MaxBend = 35f },

            // The crust is the rooted base; above the face, the point flops on its own. The bend is spread up to just
            // under the face so the wide bottom doesn't stretch (its pepperoni were smearing).
            new AvatarSpec { Id = "pizza_slice", Name = "Pizza slice", Root = 0.1f, Head = 0.38f, Face = 0.42f, FaceSize = 0.9f, MaxBend = 26f, Tip = 0.6f, Extra = "pepperoni" },
            // The paper liner is the rooted base; the whipped swirl and its cherry wobble like jelly and shed sprinkles.
            // Mess-makers (demo-avatars-06): their messes are MessMaker kinds.
            // The egg is rigid: it rocks and rolls as one piece like a weeble (no bend, no squash, no floppy top).
            new AvatarSpec { Id = "big_egg", Name = "Egg", Root = 0.12f, Head = 0.35f, Face = 0.5f, FaceSize = 0.75f, Extra = "egg", Roll = 26f },
            // The lid stays solid (it waved about as a floppy top).
            new AvatarSpec { Id = "trash_can", Name = "Trash can", Root = 0.1f, Head = 0.38f, Face = 0.45f, FaceSize = 0.75f, MaxBend = 28f, Extra = "trash" },
            new AvatarSpec { Id = "juice_cup", Name = "Juice cup", Root = 0.08f, Head = 0.33f, Face = 0.45f, FaceSize = 0.75f, MaxBend = 30f, Tip = 0.74f, Extra = "juice", FlopMax = 50f, FlopGravity = 0.4f, FlopFrequency = 2.5f },
            // The model's stiff hook-shaped hose is cut off above its socket; a simulated hose (VacuumHose) replaces it.
            new AvatarSpec { Id = "retro_vacuum", Name = "Vacuum", Root = 0.08f, Head = 0.24f, Face = 0.36f, FaceSize = 0.8f, MaxBend = 26f, Extra = "vacuum", CutAbove = 0.66f },
            // The bun bends gently as one loaf (its top end flopping on its own looked wrong).
            new AvatarSpec { Id = "meatball_sub", Name = "Meatball sub", Root = 0.08f, Head = 0.42f, Face = 0.62f, FaceSize = 0.75f, MaxBend = 18f, Extra = "meatballs" },
            new AvatarSpec { Id = "spaghetti_bowl", Name = "Spaghetti", Root = 0.06f, Head = 0.16f, Face = 0.24f, FaceSize = 0.8f, Roll = 10f, RollDamping = 0.6f, RollFrequency = 3f, Tip = 0.55f, Extra = "meatballs", FlopMax = 22f, FlopGravity = 0.2f, FlopFrequency = 2.2f, FlopDamping = 0.6f, Sideways = true, TurnWhip = 0.4f },
            new AvatarSpec { Id = "cherry_cupcake", Name = "Cherry cupcake", Root = 0.28f, Head = 0.4f, Face = 0.535f, FaceSize = 0.8f, MaxBend = 30f, Tip = 0.64f, Extra = "sprinkles", FlopMax = 45f, FlopGravity = 0.3f, FlopFrequency = 3f },
            // demo-avatars-07: the sausage sticking out of the top flops, taking the top of the bun with it; condiments spill.
            new AvatarSpec { Id = "hot_dog", Name = "Hot dog", Root = 0.06f, Head = 0.3f, Face = 0.42f, FaceSize = 0.6f, MaxBend = 32f, Tip = 0.68f, Extra = "hotdog", FlopMax = 80f, FlopGravity = 0.6f, FlopFrequency = 2.2f },
            // demo-avatars-08, modelled with SPAR3D (free, local) rather than Tripo; SPAR3D models already face the camera.
            // The striped bucket is the body (face on its front); the popcorn heap above the rim wobbles and pops kernels.
            new AvatarSpec { Id = "popcorn_bucket", Name = "Popcorn", Yaw = 0f, Root = 0.06f, Head = 0.34f, Face = 0.4f, FaceSize = 0.8f, Extra = "popcorn", Roll = 8f, RollDamping = 0.5f, RollFrequency = 2.5f },
            // The jiggliest one: it bends a lot, and above the face it wobbles fast and for a long time.
// demo-avatars-09 (SPAR3D, face the camera): the user's picks from the brainstorm. All solid and rocking; the ketchup
            // bottle and the bread give a little (squeezy, spongy).
            new AvatarSpec { Id = "taco", Name = "Taco", Yaw = 0f, Root = 0.05f, Head = 0.3f, Face = 0.4f, FaceSize = 0.8f, Extra = "taco", Roll = 10f, RollDamping = 0.45f, RollFrequency = 2.4f },
            new AvatarSpec { Id = "moldy_bread", Name = "Mouldy bread", Yaw = 0f, Root = 0.05f, Head = 0.36f, Face = 0.46f, FaceSize = 0.8f, Extra = "mold", Roll = 10f, RollDamping = 0.35f, Jiggle = 0.25f },
            new AvatarSpec { Id = "toaster", Name = "Toaster", Yaw = 0f, Root = 0.08f, Head = 0.3f, Face = 0.4f, FaceSize = 0.75f, Extra = "toaster", Roll = 8f, RollDamping = 0.5f },
            new AvatarSpec { Id = "ketchup_bottle", Name = "Ketchup", Yaw = 0f, Root = 0.05f, Head = 0.34f, Face = 0.44f, FaceSize = 0.8f, Extra = "ketchup", Roll = 12f, RollDamping = 0.35f, Jiggle = 0.45f },
// The toilet: the face on the tank, talking sloshes the bowl.
            new AvatarSpec { Id = "toilet", Name = "Toilet", Yaw = 0f, Root = 0.05f, Head = 0.55f, Face = 0.68f, FaceSize = 0.75f, Extra = "toilet", Roll = 7f, RollDamping = 0.5f, BowlHeight = 0.42f },
            new AvatarSpec { Id = "coffee_mug", Name = "Coffee mug", Yaw = 0f, Root = 0.05f, Head = 0.32f, Face = 0.42f, FaceSize = 0.7f, Extra = "coffee", Roll = 8f, RollDamping = 0.5f },
            new AvatarSpec { Id = "jello_mold", Name = "Jello", Yaw = 0f, Root = 0.04f, Head = 0.4f, Face = 0.42f, FaceSize = 0.85f, Extra = "jello", Roll = 9f, RollDamping = 0.3f, RollFrequency = 2.8f, Jiggle = 0.5f }, // calmer: the user found it too jiggly
        };

        static void AddFace(GameObject go, Material ink, Material shine, Material mouth, Material blush, Material cutout)
        {
            var face = go.AddComponent<Muffin.DigitalFace>();
            face.ink = ink;
            face.shine = shine;
            face.mouthInside = mouth;
            face.blush = blush;
            face.cutout = cutout;
        }

        /// <summary>
        /// Merge an imported model's meshes into one, turn it to face the camera (-Z), scale it to the
        /// avatar height and plant it on y = 0, centred. Saved as a mesh asset so the app does no import work.
        /// </summary>
        static (Mesh, Material[]) BakeAvatar(string glbPath, AvatarSpec spec)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (model == null) return (null, null);
            var instance = Object.Instantiate(model);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                var combine = new System.Collections.Generic.List<CombineInstance>();
                var materials = new System.Collections.Generic.List<Material>();
                foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.sharedMesh || !mf.TryGetComponent(out MeshRenderer renderer)) continue;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    {
                        combine.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = mf.transform.localToWorldMatrix });
                        materials.Add(renderer.sharedMaterials[Mathf.Min(s, renderer.sharedMaterials.Length - 1)]);
                    }
                }
                var mesh = new Mesh { name = spec.Id, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(combine.ToArray(), false, true);

                Quaternion turn = Quaternion.Euler(0f, spec.Yaw, 0f);
                Vector3[] v = mesh.vertices;
                Vector3[] n = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                for (int i = 0; i < v.Length; i++)
                {
                    v[i] = turn * v[i];
                    if (i < n.Length) n[i] = turn * n[i];
                    if (i < tangents.Length)
                    {
                        Vector3 t = turn * (Vector3)tangents[i];
                        tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                    }
                }
                Bounds b = GeometryUtility.CalculateBounds(v, Matrix4x4.identity);
                float scale = spec.Height / Mathf.Max(1e-4f, b.size.y);
                var offset = new Vector3(-b.center.x, -b.min.y, -b.center.z);
                for (int i = 0; i < v.Length; i++) v[i] = (v[i] + offset) * scale;
                mesh.vertices = v;
                if (n.Length == v.Length) mesh.normals = n;
                if (tangents.Length == v.Length) mesh.tangents = tangents;
                if (spec.CutAbove < 0.999f)
                {
                    // Triangles reaching above the cut go (a stiff part replaced in code, like the vacuum's hose).
                    float cutY = spec.Height * spec.CutAbove;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        int[] tri = mesh.GetTriangles(s);
                        var kept = new System.Collections.Generic.List<int>(tri.Length);
                        for (int i = 0; i + 2 < tri.Length; i += 3)
                            if (v[tri[i]].y <= cutY && v[tri[i + 1]].y <= cutY && v[tri[i + 2]].y <= cutY)
                                kept.AddRange(new[] { tri[i], tri[i + 1], tri[i + 2] });
                        mesh.SetTriangles(kept, s);
                    }
                }
                mesh.RecalculateBounds();

                Directory.CreateDirectory(BakedDir);
                string path = $"{BakedDir}/{spec.Id}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                Debug.Log($"[Muffin] Baked {spec.Name}: {v.Length} vertices, {materials.Count} material(s)");
                return (mesh, materials.ToArray());
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Turn a URP Lit/Unlit material see-through (alpha blended; alpha kept for OBS transparency).</summary>
        static Material Transparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material MakeMaterial(string name, Shader shader, Color color, float smoothness)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The transparent background needs an alpha channel all the way to the screen. URP's HDR buffer
        /// (R11G11B10) has none, so HDR is turned off on every URP asset the project uses.
        /// </summary>
        static void KeepAlphaInRenderPipelines()
        {
            var assets = new System.Collections.Generic.List<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
            for (int i = 0; i < QualitySettings.names.Length; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
            foreach (RenderPipelineAsset asset in assets)
            {
                if (asset is UniversalRenderPipelineAsset urp && urp.supportsHDR)
                {
                    urp.supportsHDR = false;
                    EditorUtility.SetDirty(urp);
                }
            }
        }

        /// <summary>Copy the face tracker (exe, models, licences) next to the built app, under Tracker/.</summary>
        static void BundleTracker(string buildExe)
        {
            string source = Path.GetFullPath(TrackerSource);
            string target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(buildExe)), "Tracker");
            if (!Directory.Exists(source))
            {
                Debug.LogWarning($"[Muffin] {source} not found: the build will run with mouse input only.");
                return;
            }
            if (Directory.Exists(target)) FileUtil.DeleteFileOrDirectory(target);
            Directory.CreateDirectory(target);
            foreach (string part in new[] { "Binary", "models", "Licenses" })
                FileUtil.CopyFileOrDirectory(Path.Combine(source, part), Path.Combine(target, part));
            File.Copy(Path.Combine(source, "LICENSE"), Path.Combine(target, "LICENSE-OpenSeeFace.txt"));
            File.WriteAllText(Path.Combine(target, "README.txt"),
                "Face tracking: OpenSeeFace v1.20.5 by Emiliana (https://github.com/emilianavt/OpenSeeFace),\n" +
                "BSD 2-Clause licence (LICENSE-OpenSeeFace.txt). The libraries it bundles are listed with their\n" +
                "licences in Licenses/. It runs as a separate program (Binary/facetracker.exe) started by the app.\n");
            Debug.Log("[Muffin] Bundled the face tracker into " + target);
            BundleMediaPipe(buildExe);
        }

        const string MediaPipeSource = "../third-party/mediapipe";

        /// <summary>
        /// Copy the MediaPipe tracker (tracker/muffin_facetracker.py bundled by PyInstaller into
        /// third-party/mediapipe/dist) and its face model next to the built app, under TrackerMP/.
        /// </summary>
        static void BundleMediaPipe(string buildExe)
        {
            string helper = Path.GetFullPath(Path.Combine(MediaPipeSource, "dist", "muffin_facetracker"));
            string model = Path.GetFullPath(Path.Combine(MediaPipeSource, "models", "face_landmarker.task"));
            string target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(buildExe)), "TrackerMP");
            if (!Directory.Exists(helper) || !File.Exists(model))
            {
                Debug.LogWarning($"[Muffin] MediaPipe tracker not built ({helper}): the build will use OpenSeeFace.");
                return;
            }
            if (Directory.Exists(target)) FileUtil.DeleteFileOrDirectory(target);
            FileUtil.CopyFileOrDirectory(helper, target);
            File.Copy(model, Path.Combine(target, "face_landmarker.task"));
            File.WriteAllText(Path.Combine(target, "README.txt"),
                "Face tracking: Google MediaPipe Face Landmarker (mediapipe 1.1.1, Apache License 2.0, https://ai.google.dev/edge/mediapipe)\n" +
                "with its face_landmarker.task model (Apache License 2.0). muffin_facetracker.exe is the app's own\n" +
                "tracker/muffin_facetracker.py packaged with PyInstaller; it bundles Python (PSF licence), NumPy (BSD),\n" +
                "OpenCV (Apache 2.0) and matplotlib (matplotlib licence). Camera frames stay on this PC; MediaPipe may\n" +
                "send anonymous usage statistics (not images) to Google.\n");
            Debug.Log("[Muffin] Bundled the MediaPipe tracker into " + target);
        }

        public static void CreateSceneBatch() => RunBatch(CreateScene);

        public static void BuildWindowsBatch() => RunBatch(() =>
        {
            CreateScene();
            // The build time is the version, shown on the readout, so an old copy is easy to spot.
            PlayerSettings.bundleVersion = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.Exception($"Build {report.summary.result}: {report.summary.totalErrors} error(s)");
            BundleTracker(BuildPath);
            Debug.Log($"[Muffin] Built {BuildPath} ({report.summary.totalSize / (1024 * 1024)} MB)");
        });

        /// <summary>Batch-mode wrapper: exit code 0 on success, 1 on any exception, so scripts can tell.</summary>
        static void RunBatch(System.Action action)
        {
            try
            {
                action();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Muffin] " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
