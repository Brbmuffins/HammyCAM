using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// F9 saves a snapshot for troubleshooting into a Diagnostics folder next to the app: a screenshot, the
    /// camera picture with the tracker's face points (green) and the real-face windows (red) drawn on it,
    /// the exact crop each window shows on the avatar, and a text file with the camera's settings. These
    /// files contain the camera picture, so they're only written when you press F9.
    /// </summary>
    public sealed class Diagnostics : MonoBehaviour
    {
        public AvatarLibrary library;
        public string LastMessage { get; private set; } = "";
        float messageUntil;

        public bool ShowingMessage => Time.unscaledTime < messageUntil;

        void Update()
        {
            if (Keys.Pressed(MuffinKey.F9)) Save();
        }

        public void Save(string dir = null)
        {
            dir ??= Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Diagnostics");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{stamp}-screen.png"));

            var text = new StringBuilder();
            CameraFeed feed = CameraFeed.Instance;
            WebcamHeadPoseSource tracker = WebcamHeadPoseSource.Instance;
            DigitalFace face = library && library.ActiveRig ? library.ActiveRig.GetComponent<DigitalFace>() : null;
            text.AppendLine($"time: {DateTime.Now:O}  build: {Application.version}");
            text.AppendLine($"avatar: {(library ? library.ActiveName : "-")}  face style: {DigitalFace.Style}");
            if (feed)
            {
                text.AppendLine($"camera: '{feed.DeviceName}' playing={feed.Playing} texture={feed.Texture?.width}x{feed.Texture?.height} " +
                                $"verticallyMirrored={feed.VerticallyMirrored} rotation={feed.RotationAngle} framesToTracker={feed.FramesSent}");
            }
            if (tracker)
            {
                text.AppendLine($"tracker: status='{tracker.Status}' hasFace={tracker.HasFace} image={tracker.ImageSize} mirror={tracker.mirror} sensitivity={tracker.sensitivity:0.0} " +
                                $"faces={tracker.FaceCount} following={tracker.FollowedId} pickedByHand={tracker.PickedByHand} trackerFps={tracker.TrackerFps:0.0} " +
                                $"tracker={tracker.Running} control={tracker.control} threads={tracker.trackerThreads} model={tracker.model}");
                text.AppendLine($"frame sync: {tracker.SyncedShare * 100f:0}% of updates matched to their camera frame, clock gap {tracker.SyncGapMs:0.0} ms, " +
                                $"points from frame {tracker.PointsFrameId} (kept: {(feed && feed.FrameById(tracker.PointsFrameId) != null ? "yes" : "no")})");
                text.AppendLine($"crop: {DigitalFace.CropNames[(int)DigitalFace.Crop]}, edges {DigitalFace.EdgeNames[DigitalFace.EdgeLevel]}");
            }

            Color32[] pixels = null;
            int w = 0, h = 0;
            if (feed && feed.Texture is WebCamTexture webcam && webcam.width > 16)
            {
                pixels = webcam.GetPixels32();
                w = webcam.width;
                h = webcam.height;
            }
            else if (feed && feed.Texture is Texture2D still && still.isReadable)
            {
                pixels = still.GetPixels32();
                w = still.width;
                h = still.height;
            }

            if (pixels != null && face != null)
            {
                foreach (var win in face.RealFaceWindows())
                {
                    text.AppendLine($"window {win.name}: centreUV=({win.centre.x:0.000},{win.centre.y:0.000}) sizePx=({win.size.x:0.0},{win.size.y:0.0}) " +
                                    $"image=({win.image.x:0},{win.image.y:0}) angle={win.angle * Mathf.Rad2Deg:0.0}° mirror={win.mirror}");
                    SaveCrop(pixels, w, h, win, Path.Combine(dir, $"{stamp}-crop-{win.name}.png"));
                    Outline(pixels, w, h, win, new Color32(255, 40, 40, 255));
                }
            }
            if (pixels != null && tracker && tracker.Points != null && tracker.ImageSize.x > 1f)
            {
                bool upsideDown = feed.VerticallyMirrored;
                void DrawFace(Vector2[] points, Color32 colour)
                {
                    foreach (Vector2 p in points)
                    {
                        float u = p.x / tracker.ImageSize.x;
                        float v = upsideDown ? p.y / tracker.ImageSize.y : 1f - p.y / tracker.ImageSize.y;
                        Dot(pixels, w, h, Mathf.RoundToInt(u * w), Mathf.RoundToInt(v * h), colour, 2);
                    }
                }
                // The followed face in green, other people in yellow.
                foreach (Vector2[] other in tracker.OtherFaces) DrawFace(other, new Color32(255, 220, 40, 255));
                DrawFace(tracker.Points, new Color32(40, 255, 90, 255));
            }
            if (pixels != null)
            {
                var image = new Texture2D(w, h, TextureFormat.RGB24, false);
                image.SetPixels32(pixels);
                File.WriteAllBytes(Path.Combine(dir, $"{stamp}-camera.png"), image.EncodeToPNG());
                Destroy(image);
            }
            else text.AppendLine("camera picture: not available");

            File.WriteAllText(Path.Combine(dir, $"{stamp}-info.txt"), text.ToString());
            Debug.Log("[Muffin] Diagnostics saved\n" + text);
            LastMessage = $"Saved diagnostics to {dir}";
            messageUntil = Time.unscaledTime + 5f;
        }

        /// <summary>Where window coordinates q (-0.5..0.5 across it) sample the camera image: the shader's maths.</summary>
        static Vector2 SampleUV((string name, Vector2 centre, Vector2 size, Vector2 image, float angle, bool mirror) win, Vector2 q)
        {
            if (win.mirror) q.x = -q.x;
            Vector2 px = new Vector2(q.x * win.size.x, q.y * win.size.y);
            float s = Mathf.Sin(win.angle), c = Mathf.Cos(win.angle);
            px = new Vector2(px.x * c - px.y * s, px.x * s + px.y * c);
            return win.centre + new Vector2(px.x / win.image.x, px.y / win.image.y);
        }

        static void SaveCrop(Color32[] pixels, int w, int h, (string, Vector2, Vector2, Vector2, float, bool) win, string path)
        {
            const int cw = 192;
            int ch = Mathf.Max(8, Mathf.RoundToInt(cw * Mathf.Abs(win.Item3.y) / Mathf.Max(1f, win.Item3.x)));
            var crop = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            var outPixels = new Color32[cw * ch];
            for (int j = 0; j < ch; j++)
                for (int i = 0; i < cw; i++)
                {
                    Vector2 uv = SampleUV(win, new Vector2((i + 0.5f) / cw - 0.5f, (j + 0.5f) / ch - 0.5f));
                    int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * w), 0, w - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * h), 0, h - 1);
                    outPixels[j * cw + i] = pixels[y * w + x];
                }
            crop.SetPixels32(outPixels);
            File.WriteAllBytes(path, crop.EncodeToPNG());
            Destroy(crop);
        }

        static void Outline(Color32[] pixels, int w, int h, (string, Vector2, Vector2, Vector2, float, bool) win, Color32 colour)
        {
            for (int k = 0; k < 400; k++)
            {
                float t = k / 100f;
                Vector2 q = t < 1f ? new Vector2(t - 0.5f, -0.5f)
                    : t < 2f ? new Vector2(0.5f, t - 1.5f)
                    : t < 3f ? new Vector2(2.5f - t, 0.5f)
                    : new Vector2(-0.5f, 3.5f - t);
                Vector2 uv = SampleUV(win, q);
                Dot(pixels, w, h, Mathf.RoundToInt(uv.x * w), Mathf.RoundToInt(uv.y * h), colour, 2);
            }
        }

        static void Dot(Color32[] pixels, int w, int h, int x, int y, Color32 colour, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px >= 0 && px < w && py >= 0 && py < h) pixels[py * w + px] = colour;
                }
        }
    }
}
