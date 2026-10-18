using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Automated check, only when launched with `-muffin-selftest &lt;folder&gt;`: plays the scripted
    /// movement, saves screenshots at rest and mid-motion, records every frame time, writes stats.json
    /// (fps, frame-time percentiles, hitches, errors) and quits. Normal launches ignore it.
    /// </summary>
    public sealed class SelfTest : MonoBehaviour
    {
        public AvatarLibrary library;
        public ScriptedHeadPoseSource scripted;
        public SyntheticTrackerSource synthetic;
        public ChaosDirector chaos;
        public PerfHud hud;
        public DeskScene desk;

        readonly List<float> frameMs = new List<float>();
        bool recording;
        int errors;
        float cornerAlpha = -1f, avatarAlpha = -1f;
        bool? testFaceTracked;
        string pepperoni = "not run";
        string chatTest = "not run", chaosTest = "not run", hoseTest = "not run", remoteTest = "not run";
        string flopReport = "not run", headTurnMess = "not run";
        int rooms = -1;
        string vertical = "not run";
        int sprinkleCount = -1;
        string firstError;

        IEnumerator Start()
        {
            // `-muffin-snapshot <file.png>`: a normal launch (real inputs) photographed after 6 s, then quit.
            string snapshot = Arg("-muffin-snapshot");
            if (snapshot != null)
            {
                yield return new WaitForSecondsRealtime(6f);
                yield return Shot(Path.GetDirectoryName(Path.GetFullPath(snapshot)), Path.GetFileName(snapshot));
                Application.Quit();
                yield break;
            }

            // `-muffin-diagnose <folder>`: a normal launch with the real camera in real-face mode; once a face
            // is found (or after 30 s) it saves the F9 diagnostics plus a screenshot into the folder, then quits.
            string diagnose = Arg("-muffin-diagnose");
            if (diagnose != null)
            {
                Directory.CreateDirectory(diagnose);
                var webcam = WebcamHeadPoseSource.Instance;
                DigitalFace.Style = FaceStyle.Real;
                if (webcam) library.SetSource(webcam);
                float from = Time.realtimeSinceStartup;
                yield return new WaitUntil(() => (webcam && webcam.HasFace) || Time.realtimeSinceStartup - from > 30f);
                yield return new WaitForSecondsRealtime(3f);
                if (webcam) webcam.ShowPreview = true;
                if (Arg("-muffin-testface") != null)
                {
                    // Test face (moving with -muffin-testface-move): big real eyes and mouth, a few shots in a row.
                    DigitalFace.EyeLevel = DigitalFace.MouthLevel = 2;
                    yield return new WaitForSecondsRealtime(1.5f);
                    for (int k = 1; k <= 4; k++)
                    {
                        yield return Shot(diagnose, $"move-{k}.png");
                        yield return new WaitForSecondsRealtime(0.37f);
                    }
                }
                if (webcam && webcam.HasFace) yield return MeasureJitter(webcam, Path.Combine(diagnose, "jitter.txt"), 8f);
                yield return Shot(diagnose, "app.png");
                if (TryGetComponent(out Diagnostics diagnostics)) diagnostics.Save(diagnose);
                yield return new WaitForSecondsRealtime(1f);
                Application.Quit();
                yield break;
            }

            string outDir = Arg("-muffin-selftest");
            if (outDir == null) yield break;
            Directory.CreateDirectory(outDir);
            AudioListener.volume = 0f; // the test plays every splat and bonk: silently
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception) return;
                errors++;
                firstError ??= message;
            };

            scripted.startDelay = 1.5f;
            scripted.enabled = true;
            int startAvatar = library.ActiveIndex;
            DigitalFace.Style = FaceStyle.Cartoon; // whatever the saved setting, the first phases test the cartoon face
            // The desk is on, with pretend typing and mousing (never the real keyboard) so the hands move.
            if (desk) desk.Show(true);
            if (DeskInput.Instance) DeskInput.Instance.simulate = true;
            library.Activate(0);
            library.SetSource(scripted);
            library.SetEmotion(Emotion.Neutral);
            float t0 = Time.realtimeSinceStartup;
            float Elapsed() => Time.realtimeSinceStartup - t0;

            yield return new WaitUntil(() => Elapsed() >= 1.2f);
            yield return Shot(outDir, "1-rest.png");
            yield return new WaitUntil(() => Elapsed() >= 2.0f);
            recording = true;
            foreach (var (at, name) in new[] { (2.7f, "2-motion.png"), (3.6f, "3-motion.png"), (4.6f, "4-motion.png") })
            {
                yield return new WaitUntil(() => Elapsed() >= at);
                yield return Shot(outDir, name);
            }
            yield return new WaitUntil(() => Elapsed() >= 8f);
            recording = false;

            // Transparent background: the corner must come out see-through and the avatar solid.
            BackgroundMode before = hud.Background;
            hud.Visible = false;
            hud.SetBackground(BackgroundMode.Transparent);
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            cornerAlpha = frame.GetPixel(4, frame.height - 5).a; // top-left: the desk fills the bottom of the frame
            avatarAlpha = MaxAlphaInCentre(frame);
            File.WriteAllBytes(Path.Combine(outDir, "5-transparent.png"), frame.EncodeToPNG());
            Destroy(frame);
            hud.SetBackground(before);

            // Every emotion on the first avatar, at rest so the face looks straight at the camera.
            hud.Visible = true;
            library.SetSource(null);
            foreach (Emotion e in new[] { Emotion.Neutral, Emotion.Happy, Emotion.Sad, Emotion.Angry, Emotion.Surprised })
            {
                library.SetEmotion(e);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot(outDir, $"6-emotion-{e}.png");
            }
            // Every avatar with a different emotion: at rest, then mid-sway.
            Emotion[] cycle = { Emotion.Happy, Emotion.Angry, Emotion.Sad, Emotion.Surprised, Emotion.Neutral };
            for (int i = 0; i < library.avatars.Count; i++)
            {
                library.SetSource(null);
                library.Activate(i);
                library.SetEmotion(cycle[i % cycle.Length]);
                yield return new WaitForSecondsRealtime(1.3f);
                yield return Shot(outDir, $"7-avatar-{i + 1}-rest.png");
                library.SetSource(scripted);
                yield return new WaitForSecondsRealtime(1.1f);
                yield return Shot(outDir, $"7-avatar-{i + 1}-sway.png");
            }
            library.SetEmotion(Emotion.Neutral);

            // The desk: one shot mid-typing burst, one while the pretend mouse circles (DeskInput.Simulate
            // types for 3.5 s of every 6, then mouses).
            library.Activate(0);
            library.SetSource(scripted);
            hud.Visible = true;
            yield return new WaitUntil(() => Mathf.Repeat(Time.time, 6f) is > 1.5f and < 2f);
            yield return Shot(outDir, "9-desk-typing.png");
            yield return new WaitUntil(() => Mathf.Repeat(Time.time, 6f) is > 4.6f and < 5f);
            yield return Shot(outDir, "9-desk-mousing.png");

            // Giant features (Z) and the dramatic zoom (X), on the sunflower at rest.
            library.Activate(0);
            library.SetSource(null);
            library.SetEmotion(Emotion.Happy);
            DigitalFace.EyeLevel = DigitalFace.MouthLevel = DigitalFace.Sizes.Length - 1;
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Shot(outDir, "10-giant-features.png");
            StageCamera.HoldZoom = true;
            yield return new WaitForSecondsRealtime(1.6f);
            yield return Shot(outDir, "10-dramatic-zoom.png");
            StageCamera.HoldZoom = false;
            DigitalFace.EyeLevel = DigitalFace.MouthLevel = 0;
            library.SetEmotion(Emotion.Neutral);
            yield return new WaitForSecondsRealtime(1f);

            // A square window (like a small OBS capture): the tallest avatar mid-sway must still fit.
            int width = Screen.width, height = Screen.height;
            Screen.SetResolution(height, height, FullScreenMode.Windowed);
            library.Activate(library.avatars.Count - 1);
            library.SetSource(scripted);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot(outDir, "11-square-a.png");
            yield return new WaitForSecondsRealtime(0.9f);
            yield return Shot(outDir, "11-square-b.png");
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.5f);

            // A floppy tip, if any avatar has one: a quick burst of frames while it sways, to see it flop.
            int floppy = library.avatars.FindIndex(r => r.HasFloppyTip);
            if (floppy < 0)
            {
                // Avatars build themselves when first shown: show each once to find out.
                for (int i = 0; i < library.avatars.Count && floppy < 0; i++)
                {
                    library.Activate(i);
                    yield return null;
                    if (library.avatars[i].HasFloppyTip) floppy = i;
                }
            }
            if (floppy >= 0)
            {
                library.Activate(floppy);
                library.SetSource(scripted);
                yield return new WaitForSecondsRealtime(1.2f);
                for (int k = 0; k < 6; k++)
                {
                    yield return Shot(outDir, $"12-flop-{k}.png");
                    yield return new WaitForSecondsRealtime(0.15f);
                }
            }

            // Desk antics: the pizza loses two pepperoni, then the face slam picks them back up; the muffin
            // sheds sprinkles as it sways (the pretend typing scatters them), then slams.
            DeskDebris debris = DeskDebris.Instance;
            int pizza = library.avatars.FindIndex(r => r.GetComponent<Toppings>() != null);
            if (debris && pizza >= 0)
            {
                library.Activate(pizza);
                library.SetSource(scripted);
                yield return new WaitForSecondsRealtime(1f);
                var toppings = library.avatars[pizza].GetComponent<Toppings>();
                int startedWith = toppings.OnCount;
                toppings.DropOne();
                toppings.DropOne();
                yield return new WaitForSecondsRealtime(1.6f);
                yield return Shot(outDir, "13-pepperoni-fallen.png");
                pepperoni = $"{startedWith} on, {toppings.OnCount} left after two fell";
                debris.Slam();
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot(outDir, "13-slam.png");
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot(outDir, "13-picked-up.png");
                pepperoni += $", {toppings.OnCount} on after the slam";
            }
            int muffin = library.avatars.FindIndex(r => r.GetComponent<SprinkleShedder>() != null);
            if (debris && muffin >= 0)
            {
                library.Activate(muffin);
                library.SetSource(scripted);
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot(outDir, "14-sprinkles-3s.png");
                yield return new WaitForSecondsRealtime(5f);
                yield return Shot(outDir, "14-sprinkles-8s.png");
                sprinkleCount = debris.SprinkleCount;
                debris.Slam();
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot(outDir, "14-muffin-slam.png");
                yield return new WaitForSecondsRealtime(1.6f);
                yield return Shot(outDir, "14-after-slam.png");
                debris.Clean();
            }

            // Head-turn control: the pizza's point against the first edition's body-lean sway, and what every
            // mess-maker sheds when you only turn, nod and tilt.
            yield return MotionAudit(outDir);

            // Every room behind the set, with the hot dog (or whoever is last) in front.
            if (hud.backdrop && hud.backdrop.Count > 0)
            {
                BackgroundMode was = hud.Background;
                int wasRoom = hud.Room;
                library.Activate(library.avatars.Count - 1);
                Script(true);
                for (int i = 0; i < hud.backdrop.Count; i++)
                {
                    hud.SetBackground(BackgroundMode.Room, i);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot(outDir, $"20-room-{i + 1}.png");
                }
                hud.SetBackground(was, wasRoom);
                rooms = hud.backdrop.Count;
            }

            // Vertical (TikTok): the window turned tall. The framing, the compact desk, a room and a caption.
            {
                BackgroundMode wasTall = hud.Background;
                int wasTallRoom = hud.Room;
                Screen.SetResolution(540, 960, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(1.5f);
                int tallPizza = library.avatars.FindIndex(r => r.GetComponent<Toppings>() != null);
                library.Activate(tallPizza >= 0 ? tallPizza : 0);
                Script(true);
                hud.SetBackground(BackgroundMode.GreenScreen);
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot(outDir, "21-vertical-desk.png");
                if (hud.backdrop && hud.backdrop.Count > 0)
                {
                    hud.SetBackground(BackgroundMode.Room, 0);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot(outDir, "21-vertical-room.png");
                }
                if (chaos)
                {
                    chaos.Fire(ChaosEvent.Fruit, "SelfTest");
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Shot(outDir, "21-vertical-caption.png");
                    float fruitFrom = Time.realtimeSinceStartup;
                    yield return new WaitUntil(() => chaos.Running == 0 || Time.realtimeSinceStartup - fruitFrom > 10f);
                }
                vertical = $"{Screen.width}x{Screen.height}, compact desk {DeskScene.Compact}";
                hud.SetBackground(wasTall, wasTallRoom);
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(1f);
            }

            // Real face, when launched with -muffin-testface <photo>: the photo goes through the whole
            // camera → tracker → cut-out path, exactly like a live camera would.
            var tracker = WebcamHeadPoseSource.Instance;
            if (Arg("-muffin-testface") != null && tracker)
            {
                DigitalFace.Style = FaceStyle.Real;
                library.SetSource(tracker);
                float waitFrom = Time.realtimeSinceStartup;
                yield return new WaitUntil(() => tracker.HasFace || Time.realtimeSinceStartup - waitFrom > 25f);
                testFaceTracked = tracker.HasFace;
                tracker.ShowPreview = true;
                foreach (int i in new[] { 0, 1, 4 })
                {
                    if (i >= library.avatars.Count) continue;
                    library.Activate(i);
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Shot(outDir, $"8-realface-{i + 1}.png");
                }
                // Z at its biggest: zoomed into the real eyes and mouth.
                library.Activate(1);
                DigitalFace.EyeLevel = DigitalFace.MouthLevel = DigitalFace.Sizes.Length - 1;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot(outDir, "8-realface-giant.png");
                // Mouth zoomed on its own (O), with sharp outlined edges (S).
                DigitalFace.EyeLevel = 0;
                DigitalFace.MouthLevel = 2;
                DigitalFace.EdgeLevel = 2;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot(outDir, "8-realface-mouth-outlined.png");
                DigitalFace.EdgeLevel = 1;
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot(outDir, "8-realface-mouth-sharp.png");
                // Curved fronts: big real eyes and mouth wrapping round the soda can, the egg and the hot dog.
                DigitalFace.EyeSize = DigitalFace.MouthSize = 2.2f;
                foreach (string name in new[] { "Soda can", "Egg", "Hot dog", "Pizza slice" })
                {
                    int index = library.avatars.FindIndex(r => r && r.name == name);
                    if (index < 0) continue;
                    library.Activate(index);
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Shot(outDir, $"8-realface-curved-{name.Replace(' ', '-').ToLowerInvariant()}.png");
                }
                // The look sliders: grey and contrasty, then bright, on the cut-outs only.
                DigitalFace.CutSaturation = 0f;
                DigitalFace.CutContrast = 1.6f;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot(outDir, "8-realface-look-grey.png");
                DigitalFace.CutSaturation = 1.5f;
                DigitalFace.CutContrast = 1f;
                DigitalFace.CutBrightness = 0.2f;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot(outDir, "8-realface-look-bright.png");
                DigitalFace.CutBrightness = 0f;
                DigitalFace.CutContrast = DigitalFace.CutSaturation = 1f;
                DigitalFace.EyeLevel = DigitalFace.MouthLevel = DigitalFace.EdgeLevel = 0;
                yield return new WaitForSecondsRealtime(0.8f);
                // The F9 diagnostics, as a user would press it.
                if (TryGetComponent(out Diagnostics diagnostics)) diagnostics.Save();
                yield return new WaitForSecondsRealtime(0.5f);
                tracker.ShowPreview = false;
                DigitalFace.Style = FaceStyle.Cartoon;
            }
            // The chaos set: chat commands and cooldowns, every event, each avatar's own mess, the scrub.
            if (chaos) yield return ChaosTest(outDir);
            yield return RemoteTest();

            // Feel: lag and shake through the real avatar, old settings against new (feel.txt).
            if (synthetic) yield return FeelTest(Path.Combine(outDir, "feel.txt"));

            library.Activate(startAvatar < 0 ? 0 : startAvatar);

            File.WriteAllText(Path.Combine(outDir, "stats.json"), Stats());
            Application.Quit();
        }

        void Update()
        {
            if (recording) frameMs.Add(Time.unscaledDeltaTime * 1000f);
        }

        /// <summary>Restart the scripted movement (from rest), body lean or head turns only.</summary>
        void Script(bool headTurns)
        {
            scripted.headTurnsOnly = headTurns;
            scripted.startDelay = 0f;
            scripted.enabled = false;
            scripted.enabled = true;
            library.SetSource(scripted);
        }

        /// <summary>
        /// The pizza's floppy point under the first edition's body-lean sway and under head turns (how far it
        /// curls: typical and most), then 8 s of head turns on each avatar that makes a mess, from a clean desk.
        /// </summary>
        IEnumerator MotionAudit(string outDir)
        {
            DeskDebris debris = DeskDebris.Instance;
            if (!debris) yield break;
            int pizza = library.avatars.FindIndex(r => r.GetComponent<Toppings>() != null);
            if (pizza >= 0)
            {
                BendRig rig = library.avatars[pizza];
                library.Activate(pizza);
                var lean = new List<float>();
                var turns = new List<float>();
                Script(false);
                yield return Sample(rig, lean, 8f, null, null);
                Script(true);
                yield return Sample(rig, turns, 8f, outDir, "18-pizza-head-turns.png");
                float Rms(List<float> a) => a.Count > 0 ? Mathf.Sqrt(a.Sum(x => x * x) / a.Count) : 0f;
                flopReport = $"body lean (first edition): typical {Rms(lean):0} deg, most {(lean.Count > 0 ? lean.Max() : 0f):0} deg; " +
                             $"head turns: typical {Rms(turns):0} deg, most {(turns.Count > 0 ? turns.Max() : 0f):0} deg";
                debris.Clean();
            }

            var parts = new List<string>();
            for (int i = 0; i < library.avatars.Count; i++)
            {
                BendRig avatar = library.avatars[i];
                bool messy = avatar.GetComponent<SprinkleShedder>() || avatar.GetComponent<Toppings>() ||
                             (avatar.TryGetComponent(out MessMaker maker) && maker.kind != MessKind.Vacuum);
                if (!messy) continue;
                debris.Clean();
                library.Activate(i);
                Script(true);
                yield return new WaitForSecondsRealtime(8f);
                string name = library.NameOf(i);
                parts.Add($"{name}: {debris.SprinkleCount} sprinkles, {debris.PieceCount} bits, {(Mess.Instance ? Mess.Instance.StainCount : 0)} stains");
                yield return Shot(outDir, $"19-head-turns-{name.Replace(' ', '-').ToLowerInvariant()}.png");
            }
            headTurnMess = string.Join("; ", parts);
            debris.Clean();
            scripted.headTurnsOnly = false;
        }

        IEnumerator Sample(BendRig rig, List<float> into, float seconds, string outDir, string shot)
        {
            yield return new WaitForSecondsRealtime(1f); // past the ramp-up
            float from = Time.realtimeSinceStartup;
            bool shotTaken = shot == null;
            while (Time.realtimeSinceStartup - from < seconds)
            {
                into.Add(rig.FlopDegrees);
                if (!shotTaken && Time.realtimeSinceStartup - from > seconds * 0.5f)
                {
                    shotTaken = true;
                    yield return Shot(outDir, shot);
                }
                yield return null;
            }
        }

        /// <summary>
        /// Chat parsing and the viewer rules (no network: lines as Twitch would send them), then every chaos
        /// event fired in turn with a picture mid-way, each avatar's own mess (!shake), and the scrub clean-up.
        /// </summary>
        IEnumerator ChaosTest(string outDir)
        {
            // Chat: a tagged line and a plain one parse; the rules let the first !pie through and stop the rest.
            bool parsedTagged = TwitchChat.Parse("@badge-info=;display-name=Pie\\sFan;emotes= :piefan!piefan@piefan.tmi.twitch.tv PRIVMSG #muffin :!pie please", out string u1, out string m1);
            bool parsedPlain = TwitchChat.Parse(":bob!bob@bob.tmi.twitch.tv PRIVMSG #muffin :hello there", out string u2, out string m2);
            bool wasOn = chaos.chaosOn;
            chaos.chaosOn = true;
            ChaosEvent? first = chaos.HandleChat(u1, m1);
            ChaosEvent? spam = chaos.HandleChat("someone", "!pie");      // within the gap and the pie's cooldown
            ChaosEvent? chatter = chaos.HandleChat(u2, m2);              // not a command
            chaos.chaosOn = false;
            ChaosEvent? off = chaos.HandleChat("late", "!fire");         // master switch off
            chaos.chaosOn = wasOn;
            chatTest = $"tagged={(parsedTagged ? u1 + ": " + m1 : "FAIL")} plain={(parsedPlain ? u2 : "FAIL")} " +
                       $"first={first?.ToString() ?? "none"} spam={spam?.ToString() ?? "blocked"} chatter={chatter?.ToString() ?? "ignored"} off={off?.ToString() ?? "blocked"}";
            yield return new WaitUntil(() => chaos.Running == 0);

            int pizza = library.avatars.FindIndex(r => r.GetComponent<Toppings>() != null);
            library.Activate(pizza >= 0 ? pizza : 0);
            library.SetSource(scripted);
            yield return new WaitForSecondsRealtime(1f);
            var shots = new (ChaosEvent e, float at, string name)[]
            {
                (ChaosEvent.Sprinklers, 3f, "15-sprinklers"), (ChaosEvent.Fire, 3f, "15-fire"),
                (ChaosEvent.Slime, 3f, "15-slime"), (ChaosEvent.Pie, 0.75f, "15-pie"), (ChaosEvent.Fruit, 1.3f, "15-tomatoes"),
                (ChaosEvent.WhippedCream, 2.6f, "15-whipped-cream"), (ChaosEvent.WaterSpill, 2.6f, "15-water-spill"),
            };
            int fired = 0;
            foreach (var (e, at, name) in shots)
            {
                chaos.Fire(e, "SelfTest");
                fired++;
                yield return new WaitForSecondsRealtime(at);
                yield return Shot(outDir, name + ".png");
                float waitFrom = Time.realtimeSinceStartup;
                yield return new WaitUntil(() => chaos.Running == 0 || Time.realtimeSinceStartup - waitFrom > 16f);
            }
            yield return Shot(outDir, "15-the-mess.png");
            int stains = Mess.Instance ? Mess.Instance.StainCount : -1;

            // Each mess-making avatar shakes out its own mess.
            int shaken = 0;
            for (int i = 0; i < library.avatars.Count; i++)
            {
                if (!library.avatars[i].GetComponent<MessMaker>()) continue;
                library.Activate(i);
                yield return new WaitForSecondsRealtime(0.8f);
                chaos.Fire(ChaosEvent.Shake);
                shaken++;
                yield return new WaitForSecondsRealtime(1.4f);
                yield return Shot(outDir, $"16-shake-{library.NameOf(i).Replace(' ', '-').ToLowerInvariant()}.png");
                yield return new WaitForSecondsRealtime(1f);
            }

            // The vacuum's hose, left alone on the messy desk, cleans up by itself.
            for (int i = 0; i < library.avatars.Count; i++)
            {
                var hose = library.avatars[i].GetComponent<VacuumHose>();
                if (!hose) continue;
                bool follow = VacuumHose.FollowMouse, auto = VacuumHose.AutoClean;
                VacuumHose.FollowMouse = false;
                VacuumHose.AutoClean = true;
                library.Activate(i);
                int eatenFrom = hose.Eaten, stainsFrom = Mess.Instance ? Mess.Instance.StainCount : -1;
                int bitsFrom = DeskDebris.Instance ? DeskDebris.Instance.SprinkleCount + DeskDebris.Instance.PieceCount : -1;
                int hunting = 0, frames = 0;
                float from = Time.realtimeSinceStartup;
                bool shot = false;
                while (Time.realtimeSinceStartup - from < 12f)
                {
                    frames++;
                    if (hose.Hunting) hunting++;
                    if (!shot && Time.realtimeSinceStartup - from > 5f)
                    {
                        shot = true;
                        yield return Shot(outDir, "16-hose-cleaning.png");
                    }
                    yield return null;
                }
                hoseTest = $"in 12 s left alone it swallowed {hose.Eaten - eatenFrom} sprinkles and bits (desk {bitsFrom} -> " +
                           $"{(DeskDebris.Instance ? DeskDebris.Instance.SprinkleCount + DeskDebris.Instance.PieceCount : -1)}), " +
                           $"stains {stainsFrom} -> {(Mess.Instance ? Mess.Instance.StainCount : -1)}, cleaning {100 * hunting / Mathf.Max(1, frames)}% of the time";
                VacuumHose.FollowMouse = follow;
                VacuumHose.AutoClean = auto;
                break;
            }

            // The bucket and sponge.
            chaos.ScrubClean();
            yield return new WaitForSecondsRealtime(1.6f);
            yield return Shot(outDir, "17-scrub.png");
            float scrubFrom = Time.realtimeSinceStartup;
            yield return new WaitUntil(() => ((DeskDebris.Instance == null || DeskDebris.Instance.SprinkleCount == 0) && (Mess.Instance == null || Mess.Instance.StainCount == 0))
                                             || Time.realtimeSinceStartup - scrubFrom > 12f);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot(outDir, "17-clean.png");
            chaosTest = $"fired {fired} events, {stains} stains after them, {shaken} avatars shaken, " +
                        $"after the scrub: {(Mess.Instance ? Mess.Instance.StainCount : -1)} stains, " +
                        $"{(DeskDebris.Instance ? DeskDebris.Instance.SprinkleCount : -1)} sprinkles, {(DeskDebris.Instance ? DeskDebris.Instance.PieceCount : -1)} bits";
        }

        /// <summary>
        /// The phone remote, over this PC (the self-test serves it on 127.0.0.1 only): the page loads, a wrong PIN is
        /// turned away, the state comes back with the PIN, and an avatar switch sent from the "phone" happens.
        /// </summary>
        IEnumerator RemoteTest()
        {
            RemoteControl remote = RemoteControl.Instance;
            if (!remote || !remote.Running || !library)
            {
                remoteTest = remote ? $"not running ({remote.Status})" : "no remote";
                yield break;
            }
            string page = Http(remote.Port, "GET", "/", null, null, out int pageCode);
            Http(remote.Port, "GET", "/api/state", "wrong", null, out int wrongCode);
            string json = Http(remote.Port, "GET", "/api/state", remote.Pin, null, out int stateCode);
            int before = library.ActiveIndex, target = (before + 1) % library.avatars.Count;
            Http(remote.Port, "POST", "/api/do", remote.Pin, $"action=avatar&value={target}", out int doCode);
            yield return null;
            yield return null;
            bool switched = library.ActiveIndex == target;
            library.Activate(before);
            remoteTest = $"page {pageCode} ({page.Length} chars), wrong PIN {wrongCode}, state {stateCode} " +
                         $"({(json.Contains("\\\"avatars\\\"") || json.Contains("\"avatars\"") ? "has the avatars" : "NO avatars")}), " +
                         $"avatar switch {doCode}: {(switched ? "switched" : "NOT switched")}";
        }

        /// <summary>One HTTP request to this PC (blocking; the remote's server runs on its own thread).</summary>
        static string Http(int port, string method, string path, string pin, string body, out int code)
        {
            code = 0;
            try
            {
                using var client = new System.Net.Sockets.TcpClient("127.0.0.1", port);
                client.ReceiveTimeout = 3000;
                var stream = client.GetStream();
                byte[] content = body != null ? System.Text.Encoding.UTF8.GetBytes(body) : new byte[0];
                string head = $"{method} {path} HTTP/1.1\r\nHost: 127.0.0.1\r\n" + (pin != null ? $"X-Muffin-Pin: {pin}\r\n" : "") +
                              (body != null ? $"Content-Type: application/x-www-form-urlencoded\r\nContent-Length: {content.Length}\r\n" : "") + "Connection: close\r\n\r\n";
                byte[] headBytes = System.Text.Encoding.ASCII.GetBytes(head);
                stream.Write(headBytes, 0, headBytes.Length);
                stream.Write(content, 0, content.Length);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                string all = reader.ReadToEnd();
                int space = all.IndexOf(' ');
                if (space > 0 && all.Length >= space + 4) int.TryParse(all.Substring(space + 1, 3), out code);
                int at = all.IndexOf("\r\n\r\n", System.StringComparison.Ordinal);
                return at >= 0 ? all.Substring(at + 4) : "";
            }
            catch (System.Exception e)
            {
                return "error: " + e.Message;
            }
        }

        /// <summary>
        /// Shake, as the RMS of the second difference (smooth motion scores near zero, frame-to-frame
        /// wobble scores high). Fed once per rendered frame, or once per tracker update.
        /// </summary>
        sealed class Jitter
        {
            float a, b, sum;
            int n, count;
            public void Add(float x)
            {
                if (count >= 2)
                {
                    float d = x - 2f * b + a;
                    sum += d * d;
                    n++;
                }
                a = b;
                b = x;
                count++;
            }
            public float Rms => n > 0 ? Mathf.Sqrt(sum / n) : 0f;
            public override string ToString() => Rms.ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Lag and shake of the sunflower's head for a pretend tracker (noisy, ~22 updates a second, like the
        /// webcam here): 1 s holding still, then a sudden 30° turn and lean. Each setup gets the same noise.
        /// "lag" is the time to cover 63% / 90% of the move; "shake" the frame-to-frame wobble while still.
        /// </summary>
        IEnumerator FeelTest(string path)
        {
            BendRig rig = library.avatars[0];
            library.SetSource(null);
            library.Activate(0);
            library.SetEmotion(Emotion.Neutral);
            var saved = (rig.rotationMinCutoff, rig.rotationBeta, rig.turnFrequency, rig.bodyFrequency, rig.baseFrequency, Feel.Level);
            var report = new System.Text.StringBuilder();
            report.AppendLine("setup | turn lag 63% / 90% (s) | overshoot | lean lag 63% (s) | shake while still (deg/frame)");

            // Old setups ran their springs without the Feel multiplier: divide it back out at level 3.
            var setups = new (string name, bool poseFilter, float rotCutoff, float rotBeta, float turn, float body, float stemBase, int feel)[]
            {
                ("first webcam build", false, 0.8f, 1.5f, 8f, 3.2f, 2.8f, 0),
                ("last build (heavy)", false, 0.55f, 0.3f, 5.5f, 3.2f, 2.8f, 0),
                ("new, feel 1 smoothest", true, 0.8f, 1.5f, 8f, 3.8f, 3.2f, 1),
                ("new, feel 3 balanced", true, 0.8f, 1.5f, 8f, 3.8f, 3.2f, 3),
                ("new, feel 5 snappiest", true, 0.8f, 1.5f, 8f, 3.8f, 3.2f, 5),
            };
            foreach (var s in setups)
            {
                Feel.Level = s.feel == 0 ? 3 : s.feel;
                float undo = s.feel == 0 ? 1f / Feel.SpringSpeed : 1f;
                rig.rotationMinCutoff = s.rotCutoff;
                rig.rotationBeta = s.rotBeta;
                rig.turnFrequency = s.turn * undo;
                rig.bodyFrequency = s.body * undo;
                rig.baseFrequency = s.stemBase * undo;
                synthetic.usePoseFilter = s.poseFilter;
                synthetic.Begin(7);
                library.SetSource(synthetic);
                var times = new List<float>();
                var yaws = new List<float>();
                var leans = new List<float>();
                float start = Time.unscaledTime;
                while (Time.unscaledTime - start < 4f)
                {
                    yield return null;
                    Vector3 f = rig.HeadBone.localRotation * Vector3.forward;
                    times.Add(Time.unscaledTime - start);
                    yaws.Add(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
                    leans.Add(rig.HeadBone.localPosition.x);
                }
                library.SetSource(null);

                float Mean(List<float> v, float from, float to)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int i = 0; i < v.Count; i++)
                        if (times[i] >= from && times[i] < to) { sum += v[i]; n++; }
                    return n > 0 ? sum / n : 0f;
                }
                float Reach(List<float> v, float share, float from, float to)
                {
                    float a = Mean(v, from - 1f, from), b = Mean(v, to - 0.5f, to);
                    for (int i = 0; i < v.Count; i++)
                        if (times[i] >= from && Mathf.Abs(b - a) > 1e-5f && (v[i] - a) / (b - a) >= share) return times[i] - from;
                    return float.NaN;
                }
                float step = synthetic.stepAt;
                float baseYaw = Mean(yaws, step - 1f, step), finalYaw = Mean(yaws, step + 1f, step + 1.5f);
                float peak = 0f;
                for (int i = 0; i < yaws.Count; i++)
                    if (times[i] >= step && Mathf.Abs(finalYaw - baseYaw) > 1e-5f) peak = Mathf.Max(peak, (yaws[i] - baseYaw) / (finalYaw - baseYaw));
                var shake = new Jitter();
                for (int i = 0; i < yaws.Count; i++)
                    if (times[i] >= step - 1f && times[i] < step) shake.Add(yaws[i]);
                string F(float v) => float.IsNaN(v) ? "never" : v.ToString("0.00", CultureInfo.InvariantCulture);
                report.AppendLine($"{s.name} | {F(Reach(yaws, 0.63f, step, step + 1.5f))} / {F(Reach(yaws, 0.9f, step, step + 1.5f))} | " +
                                  $"{Mathf.Max(0f, peak - 1f) * 100f:0}% | {F(Reach(leans, 0.63f, step, step + 1.5f))} | {shake}");
            }

            (rig.rotationMinCutoff, rig.rotationBeta, rig.turnFrequency, rig.bodyFrequency, rig.baseFrequency, Feel.Level) = saved;
            synthetic.usePoseFilter = true;
            File.WriteAllText(path, report.ToString());
            Debug.Log("[Muffin] Feel\n" + report);
        }

        /// <summary>
        /// Live camera check: how much the tracker and the avatar's head shake on your real face, plus the old
        /// and new ways of reading the head turn and of framing the real eye. The avatar's head
        /// shake is what viewers see; the noise estimate is what the Feel levels were tuned against (1.5°).
        /// </summary>
        IEnumerator MeasureJitter(WebcamHeadPoseSource webcam, string path, float seconds)
        {
            Jitter rawYaw = new Jitter(), headYaw = new Jitter();
            Jitter tipYaw = new Jitter(), noseYaw = new Jitter();
            Jitter oldWindow = new Jitter(), newWindow = new Jitter();
            float tipSum = 0f, tipSq = 0f, noseSum = 0f, noseSq = 0f;
            int packets = 0;
            Vector2 oldCentre = Vector2.zero;
            bool oldPrimed = false;
            Vector2[] lastPoints = null;
            DigitalFace face = library.ActiveRig ? library.ActiveRig.GetComponent<DigitalFace>() : null;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                webcam.TryGetPose(out HeadPose raw);
                float Yaw(Quaternion q) => Mathf.DeltaAngle(0f, q.eulerAngles.y);
                rawYaw.Add(Yaw(raw.Rotation));
                // What the viewer sees: the avatar's head, after the smoothing and springs (Feel level as set).
                if (library.ActiveRig)
                {
                    Vector3 fwd = library.ActiveRig.HeadBone.localRotation * Vector3.forward;
                    headYaw.Add(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
                }

                Vector2[] p = webcam.Points;
                Vector2 size = webcam.ImageSize;
                if (p != null && size.x > 1f)
                {
                    if (p != lastPoints)
                    {
                        // Once per tracker update: the turn read from the nose tip alone vs the averaged nose
                        // (the same for MediaPipe, whose nose reference is a single point).
                        lastPoints = p;
                        FaceLayout l = webcam.Layout;
                        Vector2 centre = (p[l.OuterLeft] + p[l.InnerLeft] + p[l.InnerRight] + p[l.OuterRight]) / 4f;
                        Vector2 line = p[l.OuterRight] - p[l.OuterLeft];
                        float eye = Mathf.Max(1f, line.magnitude);
                        Vector2 across = line / eye;
                        float tip = Vector2.Dot(p[l.Nose[0]] - centre, across) / eye * webcam.yawGain;
                        float nose = Vector2.Dot(WebcamHeadPoseSource.NoseOf(p, l) - centre, across) / eye * webcam.yawGain;
                        tipYaw.Add(tip);
                        noseYaw.Add(nose);
                        tipSum += tip; tipSq += tip * tip; noseSum += nose; noseSq += nose * nose;
                        packets++;
                    }
                    // The old real-eye window: eye points nudged toward the brow, smoothed at 22/s.
                    bool m = webcam.mirror;
                    FaceLayout lay = webcam.Layout;
                    Vector2 eyeMean = Vector2.zero, browMean = Vector2.zero;
                    int[] eyePoints = m ? lay.RightEye : lay.LeftEye, browPoints = m ? lay.RightBrow : lay.LeftBrow;
                    foreach (int i in eyePoints) eyeMean += p[i];
                    foreach (int i in browPoints) browMean += p[i];
                    Vector2 target = Vector2.Lerp(eyeMean / eyePoints.Length, browMean / browPoints.Length, 0.35f);
                    oldCentre = oldPrimed ? Vector2.Lerp(oldCentre, target, 1f - Mathf.Exp(-dt * 22f)) : target;
                    oldPrimed = true;
                    oldWindow.Add(oldCentre.x);
                    if (face != null)
                        foreach (var w in face.RealFaceWindows())
                            if (w.name == "left-eye") newWindow.Add(w.centre.x * w.image.x);
                }
                yield return null;
            }
            float Std(float s, float sq) => packets > 1 ? Mathf.Sqrt(Mathf.Max(0f, sq / packets - (s / packets) * (s / packets))) : 0f;
            string report =
                $"seconds {seconds}, tracker {webcam.TrackerFps:0.0} fps, image {webcam.ImageSize}, faces {webcam.FaceCount}\n" +
                $"feel {Feel.Level} ({Feel.Name}); head turn shake per frame (deg): tracker {rawYaw} · avatar head {headYaw}\n" +
                $"tracker turn noise estimate (deg, if you held still): averaged nose {noseYaw.Rms / 2.45f:0.00}\n" +
                $"turn reading shake per tracker update (deg): nose tip {tipYaw} · averaged nose {noseYaw}\n" +
                $"turn reading spread (deg, how much it moved): nose tip {Std(tipSum, tipSq):0.00} · averaged nose {Std(noseSum, noseSq):0.00}\n" +
                $"real-eye window shake per frame (px, horizontal): old {oldWindow} · new {newWindow}\n";
            File.WriteAllText(path, report);
            Debug.Log("[Muffin] Jitter\n" + report);
        }

        static IEnumerator Shot(string dir, string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name));
            yield return null;
            yield return null;
        }

        string Stats()
        {
            var sorted = frameMs.OrderBy(x => x).ToList();
            float P(float q) => sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
            float median = P(0.5f);
            int hitches = frameMs.Count(ms => ms > median * 1.5f);
            float seconds = frameMs.Sum() / 1000f;
            string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            string error = firstError == null ? "null" : "\"" + firstError.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            return "{\n" +
                   $"  \"frames\": {frameMs.Count},\n" +
                   $"  \"avgFps\": {F(seconds > 0 ? frameMs.Count / seconds : 0)},\n" +
                   $"  \"monitorHz\": {F((float)Screen.currentResolution.refreshRateRatio.value)},\n" +
                   $"  \"frameMs\": {{ \"p50\": {F(median)}, \"p95\": {F(P(0.95f))}, \"p99\": {F(P(0.99f))}, \"max\": {F(sorted.LastOrDefault())} }},\n" +
                   $"  \"hitchesOver1_5xMedian\": {hitches},\n" +
                   $"  \"transparentCornerAlpha\": {F(cornerAlpha)},\n" +
                   $"  \"transparentAvatarAlpha\": {F(avatarAlpha)},\n" +
                   $"  \"testFaceTracked\": {(testFaceTracked == null ? "null" : testFaceTracked.Value ? "true" : "false")},\n" +
                   $"  \"pepperoni\": \"{pepperoni}\",\n" +
                   $"  \"sprinklesOnDeskAfter8s\": {sprinkleCount},\n" +
                   $"  \"rooms\": {rooms},\n" +
                   $"  \"vertical\": \"{vertical}\",\n" +
                   $"  \"sounds\": {(SoundBank.Instance ? SoundBank.Instance.Count : -1)},\n" +
                   $"  \"pizzaFlop\": \"{flopReport}\",\n" +
                   $"  \"headTurnMess8s\": \"{headTurnMess}\",\n" +
                   $"  \"messFlungOffAvatar\": {(Mess.Instance ? Mess.Instance.Flung : -1)},\n" +
                   $"  \"messSmearedByHands\": {(Mess.Instance ? Mess.Instance.Smeared : -1)},\n" +
                   $"  \"chat\": \"{chatTest}\",\n" +
                   $"  \"chaos\": \"{chaosTest}\",\n" +
                   $"  \"hoseAutoClean\": \"{hoseTest}\",\n" +
                   $"  \"remote\": \"{remoteTest}\",\n" +
                   $"  \"errors\": {errors},\n" +
                   $"  \"firstError\": {error}\n" +
                   "}\n";
        }

        /// <summary>Most opaque pixel in the middle of the frame, where the avatar stands.</summary>
        static float MaxAlphaInCentre(Texture2D t)
        {
            float best = 0f;
            for (int y = t.height / 4; y < t.height * 3 / 4; y += 8)
                for (int x = t.width / 3; x < t.width * 2 / 3; x += 8)
                    best = Mathf.Max(best, t.GetPixel(x, y).a);
            return best;
        }

        static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
