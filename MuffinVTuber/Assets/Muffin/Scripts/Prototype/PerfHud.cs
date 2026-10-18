using UnityEngine;

namespace Muffin
{
    /// <summary>How the area around the avatar is drawn.</summary>
    public enum BackgroundMode
    {
        /// <summary>Brand purple, for previewing.</summary>
        Preview,
        /// <summary>Pure green, for OBS Chroma Key over Window Capture.</summary>
        GreenScreen,
        /// <summary>Fully transparent, for OBS Game Capture with "Allow Transparency" (shows black in the window itself).</summary>
        Transparent,
        /// <summary>A painted room behind the set (Backdrop): the vacuum sales floor, an airport gate…</summary>
        Room,
    }

    /// <summary>
    /// On-screen frame-rate readout and prototype controls. Frames are paced to the monitor's refresh
    /// (vsync): steady frame timing matters more for "smooth" than a big but uneven fps number.
    /// </summary>
    public sealed class PerfHud : MonoBehaviour
    {
        public AvatarLibrary library;
        public DeskScene desk;
        public Diagnostics diagnostics;
        public WebcamHeadPoseSource webcam;
        public MouseHeadPoseSource mouse;
        public Camera cam;
        public Backdrop backdrop;
        public Color background = new Color(0.10f, 0.07f, 0.16f);

        public bool Visible { get; set; } = true;
        public float Fps { get; private set; }
        public float WorstMs { get; private set; }
        public BackgroundMode Background { get; private set; }

        float windowTime, windowWorst;
        int windowFrames;
        bool fellBack;
        GUIStyle style;

        BendRig rig => library ? library.ActiveRig : null;

        void Start()
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            Feel.Level = Mathf.Clamp(PlayerPrefs.GetInt("muffin.feel", Feel.Default), 1, Feel.Levels);
            SetBackground((BackgroundMode)PlayerPrefs.GetInt("muffin.background", (int)BackgroundMode.Preview), PlayerPrefs.GetInt("muffin.room", 0));
        }

        /// <summary>Drive the avatar from the webcam (true) or the mouse (W).</summary>
        public void UseWebcam(bool on)
        {
            if (!library || !webcam || !mouse) return;
            if (on)
            {
                webcam.Restart();
                library.SetSource(webcam);
                fellBack = false;
            }
            else library.SetSource(mouse);
        }

        public bool UsingWebcam => rig && webcam && rig.source == (HeadPoseSource)webcam;

        /// <summary>The Feel level ([ / ]), remembered.</summary>
        public static void SetFeel(int level)
        {
            Feel.Level = Mathf.Clamp(level, 1, Feel.Levels);
            PlayerPrefs.SetInt("muffin.feel", Feel.Level);
        }

        /// <summary>Which room shows when the background is Room.</summary>
        public int Room { get; private set; }

        public void SetBackground(BackgroundMode mode, int room = -1)
        {
            if (mode == BackgroundMode.Room && (!backdrop || backdrop.Count == 0)) mode = BackgroundMode.Preview;
            Background = mode;
            if (room >= 0 && backdrop) Room = Mathf.Clamp(room, 0, Mathf.Max(0, backdrop.Count - 1));
            PlayerPrefs.SetInt("muffin.background", (int)mode);
            PlayerPrefs.SetInt("muffin.room", Room);
            if (backdrop) backdrop.Show(mode == BackgroundMode.Room ? Room : -1);
            if (!cam) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = mode switch
            {
                BackgroundMode.GreenScreen => new Color(0f, 1f, 0f, 1f),
                BackgroundMode.Transparent => new Color(0f, 0f, 0f, 0f),
                _ => background,
            };
        }

        /// <summary>G: preview, green screen, transparent, then each room in turn, and round again.</summary>
        public void NextBackground()
        {
            int rooms = backdrop ? backdrop.Count : 0;
            if (Background == BackgroundMode.Room)
            {
                if (Room + 1 < rooms) SetBackground(BackgroundMode.Room, Room + 1);
                else SetBackground(BackgroundMode.Preview);
            }
            else if (Background == BackgroundMode.Transparent) SetBackground(rooms > 0 ? BackgroundMode.Room : BackgroundMode.Preview, 0);
            else SetBackground(Background + 1);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            windowTime += dt;
            windowFrames++;
            windowWorst = Mathf.Max(windowWorst, dt);
            if (windowTime >= 0.5f)
            {
                Fps = windowFrames / windowTime;
                WorstMs = windowWorst * 1000f;
                windowTime = windowWorst = 0f;
                windowFrames = 0;
            }

            // No tracker or no camera (missing files, no webcam, self-test): fall back to the mouse.
            if (!fellBack && rig && webcam && rig.source == webcam && webcam.Started && (!webcam.Available || webcam.NoCamera))
            {
                fellBack = true;
                if (mouse) library.SetSource(mouse);
            }

            if (Keys.Pressed(MuffinKey.H)) Visible = !Visible;
            if (Keys.Pressed(MuffinKey.LeftBracket) || Keys.Pressed(MuffinKey.RightBracket))
            {
                Feel.Level = Mathf.Clamp(Feel.Level + (Keys.Pressed(MuffinKey.RightBracket) ? 1 : -1), 1, Feel.Levels);
                PlayerPrefs.SetInt("muffin.feel", Feel.Level);
            }
            if (Keys.Pressed(MuffinKey.F) && rig) rig.filterInput = !rig.filterInput;
            if (Keys.Pressed(MuffinKey.J) && mouse) mouse.simulateWebcam = !mouse.simulateWebcam;
            if (Keys.Pressed(MuffinKey.G)) NextBackground();
            if (Keys.Pressed(MuffinKey.W) && rig && webcam && mouse)
            {
                if (rig.source == (HeadPoseSource)webcam) library.SetSource(mouse);
                else
                {
                    // Switching to the webcam looks for cameras again, so a newly plugged-in one is found.
                    webcam.Restart();
                    library.SetSource(webcam);
                    fellBack = false;
                }
            }
        }

        void OnGUI()
        {
            if (!Visible) return;
            style ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                richText = true,
                padding = new RectOffset(12, 12, 10, 10),
            };
            // Just what matters at a glance; every setting and shortcut lives in the menu (M).
            bool usingWebcam = rig && webcam && rig.source == (HeadPoseSource)webcam;
            string input = usingWebcam
                ? $"webcam · {webcam.Running} {webcam.TrackerFps:0}/s" + (rig.Tracking && webcam.HasFace ? "" : " · <color=#ff8080>no face found</color>")
                : rig && rig.source ? rig.source.Label.ToLowerInvariant() : "no input";
            string notes = "";
            if (usingWebcam && webcam.FaceCount > 1)
                notes += $"\n<color=#ffd080>{webcam.FaceCount} people in view: following {(webcam.PickedByHand ? "the one you picked" : "the main face")} · T: switch</color>";
            if (fellBack) notes += "\n<color=#ffd080>No camera or tracker: driving with the mouse (M → Tracking)</color>";
            double hz = Screen.currentResolution.refreshRateRatio.value;
            if (Fps > 0f && Fps < hz * 0.9f) notes += $"\n<color=#ff8080>{Fps:0} fps (monitor {hz:0} Hz)</color>";
            // Where the phone remote is, for the first minute or until a phone connects (the PIN stays in the menu).
            RemoteControl remote = RemoteControl.Instance;
            if (remote && remote.Running && !remote.EverConnected && Time.realtimeSinceStartup < 60f && remote.Addresses.Count > 0)
                notes += $"\n<color=#a0d0ff>Phone remote: {remote.Addresses[0]} (PIN: M → Stream)</color>";
            var content = new GUIContent(
                $"<b>Muffin Dynamic VTuber</b> · <b>M: menu</b> · H: hide\n" +
                $"{(library ? library.ActiveName : "")} · {input}{notes}\n" +
                $"<size=11><color=#a0a0b0>build {Application.version}</color></size>");
            Vector2 size = style.CalcSize(content);
            GUI.Box(new Rect(12f, 12f, size.x, size.y), content, style);
            if (webcam && webcam.ShowPreview) DrawCameraPreview();
            if (diagnostics && diagnostics.ShowingMessage)
                GUI.Box(new Rect(12f, Screen.height - 40f, Screen.width - 24f, 28f), diagnostics.LastMessage);
        }

        /// <summary>
        /// What the camera sees, top right, with the tracker's face points on it. Shown like a selfie
        /// (mirrored) when mirror mode is on, so moving left moves left.
        /// </summary>
        void DrawCameraPreview()
        {
            CameraFeed feed = CameraFeed.Instance;
            float w = Mathf.Min(384f, Screen.width * 0.45f), h = w * 9f / 16f; // fits a narrow (vertical) window too
            var rect = new Rect(Screen.width - w - 12f, 12f, w, h);
            GUI.Box(new Rect(rect.x - 4f, rect.y - 4f, w + 8f, h + 26f), GUIContent.none);
            if (feed && feed.Playing)
            {
                Rect uv = webcam.mirror ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
                GUI.DrawTextureWithTexCoords(rect, feed.Texture, uv);
            }
            Vector2 image = webcam.ImageSize;
            void DrawPoints(Vector2[] points, Color colour, float size)
            {
                if (points == null || image.x <= 1f) return;
                Color old = GUI.color;
                GUI.color = colour;
                foreach (Vector2 p in points)
                {
                    float u = p.x / image.x;
                    if (webcam.mirror) u = 1f - u;
                    GUI.DrawTexture(new Rect(rect.x + u * w - size / 2f, rect.y + p.y / image.y * h - size / 2f, size, size), Texture2D.whiteTexture);
                }
                GUI.color = old;
            }
            // The person the avatar follows in green, anyone else in view in grey.
            if (webcam.HasFace)
            {
                foreach (Vector2[] other in webcam.OtherFaces) DrawPoints(other, new Color(1f, 1f, 1f, 0.45f), 2f);
                DrawPoints(webcam.Points, new Color(0.3f, 1f, 0.5f, 0.9f), 3f);
            }
            string caption = !feed || !feed.Playing ? "no video from the camera"
                : !webcam.HasFace ? "no face found: face the camera, light in front of you"
                : webcam.FaceCount > 1 ? $"{webcam.FaceCount} people: the avatar follows the green one · T: switch"
                : "face found";
            GUI.Label(new Rect(rect.x, rect.y + h + 2f, w, 20f), $"Camera view (P) · {caption}");
        }
    }
}
