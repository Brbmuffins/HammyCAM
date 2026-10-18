using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// One place for everything (M, for menu): avatars and emotions, the face, tracking, the desk and its mess, the
    /// chaos set and its Twitch chat, and stream output. Every button does what a hotkey does; the hotkeys
    /// stay as shortcuts. Typing in a box (the channel name) mutes the hotkeys.
    /// </summary>
    public sealed class ControlPanel : MonoBehaviour
    {
        public AvatarLibrary library;
        public WebcamHeadPoseSource webcam;
        public PerfHud hud;
        public DeskScene desk;
        public DeskDebris debris;
        public ChaosDirector chaos;
        public Diagnostics diagnostics;

        public bool Open { get; set; }

        static readonly string[] Tabs = { "Avatar", "Face", "Tracking", "Desk & mess", "Chaos", "Stream" };
        int tab;
        Rect window = new Rect(0f, 0f, 520f, 620f);
        Vector2 scroll;
        string channelField;
        GUIStyle title, small, wrap;

        void Start()
        {
            window.x = Screen.width - window.width - 16f;
            window.y = 16f;
            tab = PlayerPrefs.GetInt("muffin.panel.tab", 0);
            channelField = chaos ? chaos.channel : "";
        }

        void Update()
        {
            if (Keys.Pressed(MuffinKey.M) || Keys.Pressed(MuffinKey.F6)) Open = !Open; // M for menu (F6 still works)
            Keys.Suppressed = Open && GUIUtility.keyboardControl != 0;
            if (!Open) StageCamera.HoldZoom = false;
        }

        void OnGUI()
        {
            if (!Open) return;
            title ??= new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            window.width = Mathf.Min(520f, Screen.width - 20f);
            window.height = Mathf.Min(620f, Screen.height - 20f);
            // Kept on screen when the window changes shape (going vertical for TikTok).
            window.x = Mathf.Clamp(window.x, 0f, Mathf.Max(0f, Screen.width - window.width));
            window.y = Mathf.Clamp(window.y, 0f, Mathf.Max(0f, Screen.height - window.height));
            window = GUI.Window(77, window, Draw, "Muffin menu (M)");
        }

        void Draw(int id)
        {
            int picked = GUILayout.Toolbar(tab, Tabs);
            if (picked != tab)
            {
                tab = picked;
                PlayerPrefs.SetInt("muffin.panel.tab", tab);
                GUIUtility.keyboardControl = 0;
            }
            scroll = GUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: AvatarTab(); break;
                case 1: FaceTab(); break;
                case 2: TrackingTab(); break;
                case 3: DeskTab(); break;
                case 4: ChaosTab(); break;
                default: StreamTab(); break;
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close")) Open = false;
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        // ---------- tabs ----------

        void AvatarTab()
        {
            if (!library) return;
            Heading("Avatar (↓ / ↑)");
            int columns = 3;
            for (int i = 0; i < library.avatars.Count; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + columns, library.avatars.Count); j++)
                {
                    bool active = j == library.ActiveIndex;
                    if (GUILayout.Toggle(active, library.NameOf(j), GUI.skin.button) && !active) library.Activate(j);
                }
                GUILayout.EndHorizontal();
            }
            Heading("Emotion (F1-F4)");
            GUILayout.BeginHorizontal();
            foreach (Emotion e in new[] { Emotion.Neutral, Emotion.Happy, Emotion.Sad, Emotion.Angry, Emotion.Surprised })
                if (GUILayout.Toggle(library.Emotion == e, e.ToString(), GUI.skin.button) && library.Emotion != e) library.SetEmotion(e);
            GUILayout.EndHorizontal();
        }

        void FaceTab()
        {
            if (!library) return;
            Heading("Face (V)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(DigitalFace.Style == FaceStyle.Cartoon, "Cartoon", GUI.skin.button)) library.SetFaceStyle(FaceStyle.Cartoon);
            if (GUILayout.Toggle(DigitalFace.Style == FaceStyle.Real, "Your real eyes and mouth", GUI.skin.button)) library.SetFaceStyle(FaceStyle.Real);
            GUILayout.EndHorizontal();
            Heading("How much of you shows (E)");
            int crop = GUILayout.Toolbar((int)DigitalFace.Crop, DigitalFace.CropNames);
            if (crop != (int)DigitalFace.Crop) library.SetCrop((DigitalFace.RealCrop)crop);
            Heading($"Zoom into the eyes: ×{DigitalFace.EyeSize:0.00} (Z)");
            int eyePreset = DigitalFace.PresetOf(DigitalFace.EyeSize);
            int eyes = GUILayout.Toolbar(eyePreset, DigitalFace.SizeNames);
            if (eyes != eyePreset && eyes >= 0) library.SetEyeZoom(eyes);
            float eyeSize = GUILayout.HorizontalSlider(DigitalFace.EyeSize, DigitalFace.MinSize, DigitalFace.MaxSize);
            if (Mathf.Abs(eyeSize - DigitalFace.EyeSize) > 0.005f) library.SetEyeSize(Mathf.Round(eyeSize * 100f) / 100f);
            Heading($"Zoom into the mouth: ×{DigitalFace.MouthSize:0.00} (O)");
            int mouthPreset = DigitalFace.PresetOf(DigitalFace.MouthSize);
            int mouth = GUILayout.Toolbar(mouthPreset, DigitalFace.SizeNames);
            if (mouth != mouthPreset && mouth >= 0) library.SetMouthZoom(mouth);
            float mouthSize = GUILayout.HorizontalSlider(DigitalFace.MouthSize, DigitalFace.MinSize, DigitalFace.MaxSize);
            if (Mathf.Abs(mouthSize - DigitalFace.MouthSize) > 0.005f) library.SetMouthSize(Mathf.Round(mouthSize * 100f) / 100f);
            Heading("Real-face edges (S)");
            int edges = GUILayout.Toolbar(DigitalFace.EdgeLevel, DigitalFace.EdgeNames);
            if (edges != DigitalFace.EdgeLevel) library.SetEdges(edges);
            if (library.ActiveRig && library.ActiveRig.TryGetComponent(out DigitalFace fitFace))
            {
                Heading($"Place the face on the {library.ActiveName}");
                DigitalFace.Placement fit = fitFace.Fit;
                float x = Slider("Left / right", fit.X, -0.15f, 0.15f);
                float y = Slider("Down / up", fit.Y, -0.15f, 0.15f);
                float size = Slider($"Size ×{fit.Size:0.00}", fit.Size, 0.6f, 1.6f);
                float eyesUp = Slider("Eyes down / up", fit.EyesUp, -0.03f, 0.03f);
                float apart = Slider("Eyes closer / apart", fit.EyesApart, 0.7f, 1.4f);
                float mouthUp = Slider("Mouth down / up", fit.MouthUp, -0.04f, 0.03f);
                if (GUILayout.Button("Back where it started")) { x = y = eyesUp = mouthUp = 0f; size = apart = 1f; }
                var moved = new DigitalFace.Placement { X = x, Y = y, Size = size, EyesUp = eyesUp, EyesApart = apart, MouthUp = mouthUp };
                if (!moved.Equals(fit)) fitFace.SetFit(moved);
            }
            Heading("Your real eyes and mouth: look");
            float b = Slider($"Brightness {DigitalFace.CutBrightness:+0.00;-0.00;0}", DigitalFace.CutBrightness, -0.5f, 0.5f);
            float c = Slider($"Contrast ×{DigitalFace.CutContrast:0.00}", DigitalFace.CutContrast, 0.5f, 2f);
            float s = Slider($"Colour ×{DigitalFace.CutSaturation:0.00}", DigitalFace.CutSaturation, 0f, 2f);
            if (GUILayout.Button("As the camera sees it")) { b = 0f; c = 1f; s = 1f; }
            if (Mathf.Abs(b - DigitalFace.CutBrightness) > 0.004f || Mathf.Abs(c - DigitalFace.CutContrast) > 0.004f || Mathf.Abs(s - DigitalFace.CutSaturation) > 0.004f)
                library.SetCutoutLook(Mathf.Round(b * 100f) / 100f, Mathf.Round(c * 100f) / 100f, Mathf.Round(s * 100f) / 100f);
            Heading("Dramatic zoom (hold X)");
            StageCamera.HoldZoom = GUILayout.RepeatButton("Hold here for the dramatic zoom");
        }

        void TrackingTab()
        {
            if (!hud || !webcam) return;
            Heading("Driven by (W)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(hud.UsingWebcam, "Webcam", GUI.skin.button) && !hud.UsingWebcam) hud.UseWebcam(true);
            if (GUILayout.Toggle(!hud.UsingWebcam, "Mouse", GUI.skin.button) && hud.UsingWebcam) hud.UseWebcam(false);
            GUILayout.EndHorizontal();
            GUILayout.Label($"{webcam.Label} · {webcam.TrackerFps:0} updates/s", small);
            Heading("Face tracker (F5)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(webcam.Running == TrackerKind.MediaPipe, "MediaPipe", GUI.skin.button)) webcam.SetTracker(TrackerKind.MediaPipe);
            if (GUILayout.Toggle(webcam.Running == TrackerKind.OpenSeeFace, "OpenSeeFace", GUI.skin.button)) webcam.SetTracker(TrackerKind.OpenSeeFace);
            GUILayout.EndHorizontal();
            Heading("Control (L)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(webcam.control == ControlMode.HeadTurns, "Head turns (tilt to lean)", GUI.skin.button)) webcam.SetControl(ControlMode.HeadTurns);
            if (GUILayout.Toggle(webcam.control == ControlMode.BodyLean, "Body lean", GUI.skin.button)) webcam.SetControl(ControlMode.BodyLean);
            GUILayout.EndHorizontal();
            Heading($"Feel: {Feel.Name} ([ / ])");
            int feel = GUILayout.Toolbar(Feel.Level - 1, new[] { "1 smoothest", "2", "3 balanced", "4", "5 snappiest" }) + 1;
            if (feel != Feel.Level) PerfHud.SetFeel(feel);
            Heading($"Lean sensitivity: {webcam.sensitivity * 100f:0}% (- / +)");
            float s = GUILayout.HorizontalSlider(webcam.sensitivity, 0.2f, 2f);
            if (Mathf.Abs(s - webcam.sensitivity) > 0.04f) webcam.SetSensitivity(s);
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-centre (C)")) webcam.Recentre();
            bool mirror = GUILayout.Toggle(webcam.mirror, "Mirror", GUI.skin.button);
            if (mirror != webcam.mirror) webcam.SetMirror(mirror);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Next camera (N)")) webcam.NextCamera();
            if (GUILayout.Button("Follow next person (T)")) webcam.NextPerson();
            webcam.ShowPreview = GUILayout.Toggle(webcam.ShowPreview, "Camera view (P)", GUI.skin.button);
            GUILayout.EndHorizontal();
            BendRig rig = library ? library.ActiveRig : null;
            if (rig) rig.filterInput = GUILayout.Toggle(rig.filterInput, "Smoothing on (F)");
            if (rig && hud.UsingWebcam)
            {
                // Live numbers for tuning: about ±1 is a full lean; angles in degrees.
                HeadPose p = rig.Current;
                Vector3 e = p.Rotation.eulerAngles;
                GUILayout.Label($"Live: turn {Mathf.DeltaAngle(0f, e.y):+0;-0}° · nod {Mathf.DeltaAngle(0f, e.x):+0;-0}° · tilt {Mathf.DeltaAngle(0f, e.z):+0;-0}° · " +
                                $"lean {p.Offset.x:+0.00;-0.00} · mouth {p.MouthOpen:0.00} · smile {p.Smile:+0.00;-0.00} · brows {p.BrowRaise:+0.00;-0.00}", small);
            }
            else GUILayout.Label("Mouse: move to lean, stretch and scrunch · Space: talk · B: blink · J: pretend webcam", small);
        }

        void DeskTab()
        {
            if (desk)
            {
                Heading("Desk (D)");
                bool shown = GUILayout.Toggle(desk.Shown, desk.Shown ? "Desk shown" : "Desk hidden", GUI.skin.button);
                if (shown != desk.Shown) desk.Show(shown);
            }
            Heading("The avatar's own mess");
            if (chaos)
            {
                if (GUILayout.Button($"Shake it: {chaos.ShakeName}")) chaos.Fire(ChaosEvent.Shake);
                if (GUILayout.Button("Face slam (R)")) chaos.Fire(ChaosEvent.Slam);
            }
            Heading("The vacuum's hose");
            bool auto = GUILayout.Toggle(VacuumHose.AutoClean, VacuumHose.AutoClean
                ? "Cleans up the desk by itself" : "Doesn't clean by itself", GUI.skin.button);
            if (auto != VacuumHose.AutoClean) VacuumHose.AutoClean = auto;
            bool follow = GUILayout.Toggle(VacuumHose.FollowMouse, VacuumHose.FollowMouse
                ? "Your mouse steers it while you move it (or click the hose)" : "Mouse steering off (click the hose to turn it on)", GUI.skin.button);
            if (follow != VacuumHose.FollowMouse) VacuumHose.FollowMouse = follow;
            Heading("Clean up");
            GUILayout.BeginHorizontal();
            if (chaos && GUILayout.Button("Scrub it clean (K): bucket and sponge")) chaos.ScrubClean();
            if (debris && GUILayout.Button("Clean instantly")) debris.Clean();
            GUILayout.EndHorizontal();
            if (debris) GUILayout.Label($"On the desk: {debris.SprinkleCount} sprinkles, {debris.PieceCount} bits{(Mess.Instance ? $", {Mess.Instance.StainCount} stains" : "")}", small);
            Heading("Sounds");
            bool sound = GUILayout.Toggle(Sound.On, Sound.On ? "Sounds on (splats, sizzles, bonks, the vacuum's rude noises)" : "Sounds off", GUI.skin.button);
            if (sound != Sound.On) Sound.On = sound;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Volume {SoundBank.Volume * 100f:0}%", GUILayout.Width(110f));
            float volume = GUILayout.HorizontalSlider(SoundBank.Volume, 0f, 1f);
            if (Mathf.Abs(volume - SoundBank.Volume) > 0.005f) SoundBank.Volume = volume;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Test a sound")) Sfx.Play("splat");
        }

        void ChaosTab()
        {
            if (!chaos) return;
            Heading("Chaos set");
            bool on = GUILayout.Toggle(chaos.chaosOn, chaos.chaosOn ? "Viewers can trigger chaos: ON (F7)" : "Viewers can trigger chaos: OFF (F7)", GUI.skin.button);
            if (on != chaos.chaosOn)
            {
                chaos.chaosOn = on;
                chaos.Save();
            }
            Heading("Twitch chat (read-only, no login)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Channel", GUILayout.Width(60f));
            channelField = GUILayout.TextField(channelField ?? "", GUILayout.MinWidth(160f));
            if (GUILayout.Button(chaos.Chat.Connected ? "Reconnect" : "Connect", GUILayout.Width(90f)))
            {
                GUIUtility.keyboardControl = 0;
                chaos.ConnectChat(channelField);
            }
            if (GUILayout.Button("Disconnect", GUILayout.Width(90f))) chaos.Chat.Disconnect();
            GUILayout.EndHorizontal();
            GUILayout.Label(chaos.Chat.Status + (chaos.LastViewerEvent.Length > 0 ? $" · last: {chaos.LastViewerEvent}" : ""), small);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Per viewer: {chaos.perViewer:0} s", GUILayout.Width(130f));
            float viewer = GUILayout.HorizontalSlider(chaos.perViewer, 0f, 300f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Gap between any: {chaos.gap:0} s", GUILayout.Width(130f));
            float gap = GUILayout.HorizontalSlider(chaos.gap, 0f, 60f);
            GUILayout.EndHorizontal();
            bool captions = GUILayout.Toggle(chaos.showCaptions, "Show who did it on screen");
            if (Mathf.Abs(viewer - chaos.perViewer) > 0.5f || Mathf.Abs(gap - chaos.gap) > 0.5f || captions != chaos.showCaptions)
            {
                chaos.perViewer = Mathf.Round(viewer);
                chaos.gap = Mathf.Round(gap);
                chaos.showCaptions = captions;
                chaos.Save();
            }
            Heading("Events (on · chat commands · cooldown · fire it now)");
            int key = 1;
            foreach (ChaosDirector.EventInfo e in chaos.Events)
            {
                GUILayout.BeginHorizontal();
                bool enabled = GUILayout.Toggle(e.Enabled, "", GUILayout.Width(18f));
                GUILayout.Label($"{e.Label}\n<size=10>{string.Join(" ", e.Commands)}</size>", wrap, GUILayout.Width(170f));
                float cooldown = GUILayout.HorizontalSlider(e.Cooldown, 0f, 300f, GUILayout.Width(110f));
                float left = chaos.CooldownLeft(e.Event);
                GUILayout.Label(left > 0f ? $"{e.Cooldown:0}s ({left:0})" : $"{e.Cooldown:0}s", GUILayout.Width(62f));
                if (GUILayout.Button(key <= 8 ? $"Go ({key})" : "Go", GUILayout.Width(62f))) chaos.Fire(e.Event, "You");
                GUILayout.EndHorizontal();
                if (enabled != e.Enabled || Mathf.Abs(cooldown - e.Cooldown) > 0.5f)
                {
                    e.Enabled = enabled;
                    e.Cooldown = Mathf.Round(cooldown);
                    chaos.Save();
                }
                key++;
            }
            GUILayout.Label("With this panel open, keys 1-8 fire the first eight. The streamer's buttons skip the cooldowns.", small);
            // Keys 1-8 while the panel is open and no box has focus.
            var digits = new[] { MuffinKey.Digit1, MuffinKey.Digit2, MuffinKey.Digit3, MuffinKey.Digit4, MuffinKey.Digit5, MuffinKey.Digit6, MuffinKey.Digit7, MuffinKey.Digit8 };
            if (Event.current.type == EventType.Repaint)
                for (int i = 0; i < digits.Length; i++)
                    if (Keys.Pressed(digits[i])) chaos.Fire(chaos.Events[i].Event, "You");
        }

        void StreamTab()
        {
            if (!hud) return;
            Heading("Background (G)");
            int current = hud.Background == BackgroundMode.Room ? -1 : (int)hud.Background;
            int bg = GUILayout.Toolbar(current, new[] { "Preview", "Green screen", "Transparent" });
            if (bg != current && bg >= 0) hud.SetBackground((BackgroundMode)bg);
            Backdrop rooms = hud.backdrop;
            if (rooms && rooms.Count > 0)
            {
                Heading("Rooms behind the set");
                for (int i = 0; i < rooms.Count; i += 2)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = i; j < Mathf.Min(i + 2, rooms.Count); j++)
                    {
                        bool on = hud.Background == BackgroundMode.Room && hud.Room == j;
                        if (GUILayout.Toggle(on, rooms.NameOf(j), GUI.skin.button) && !on) hud.SetBackground(BackgroundMode.Room, j);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Label("OBS: green screen → Window Capture + Chroma Key. Transparent → Game Capture with \"Allow Transparency\".", small);
            Heading("Window shape");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Wide 16:9")) SetWindow(false);
            if (GUILayout.Button("Vertical 9:16 (TikTok)")) SetWindow(true);
            GUILayout.EndHorizontal();
            GUILayout.Label(DeskScene.Compact
                ? "Vertical: the avatar fills the picture, the desk is laid out to fit, captions sit up high clear of TikTok's comments."
                : "Wide. For TikTok LIVE Studio choose Vertical and capture this window in a portrait scene.", small);
            Heading("Readout (H)");
            hud.Visible = GUILayout.Toggle(hud.Visible, hud.Visible ? "Readout shown" : "Readout hidden", GUI.skin.button);
            Heading("Sound");
            bool muted = GUILayout.Toggle(!Sound.On, !Sound.On ? "Muted: no sound at all (click to unmute)" : "Mute all sound", GUI.skin.button);
            if (muted == Sound.On) Sound.On = !muted;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Volume {SoundBank.Volume * 100f:0}%", GUILayout.Width(110f));
            float level = GUILayout.HorizontalSlider(SoundBank.Volume, 0f, 1f);
            if (Mathf.Abs(level - SoundBank.Volume) > 0.005f) SoundBank.Volume = level;
            GUILayout.EndHorizontal();
            Heading("Troubleshooting");
            double hz = Screen.currentResolution.refreshRateRatio.value;
            GUILayout.Label($"{hud.Fps:0} fps (monitor {hz:0} Hz) · slowest frame {hud.WorstMs:0.0} ms · build {Application.version}", small);
            if (diagnostics && GUILayout.Button("Save diagnostics (F9)")) diagnostics.Save();
        }

        /// <summary>Resize the window: 16:9, or 9:16 as tall as the screen allows (vertical, for TikTok).</summary>
        static void SetWindow(bool vertical)
        {
            int screenH = Display.main.systemHeight > 0 ? Display.main.systemHeight : 1080;
            if (vertical)
            {
                int h = Mathf.Min(1920, screenH - 100);
                Screen.SetResolution(Mathf.RoundToInt(h * 9f / 16f), h, FullScreenMode.Windowed);
            }
            else Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        }

        /// <summary>A labelled slider on one line.</summary>
        float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

        void Heading(string text)
        {
            GUILayout.Space(6f);
            GUILayout.Label(text, title);
        }
    }
}
