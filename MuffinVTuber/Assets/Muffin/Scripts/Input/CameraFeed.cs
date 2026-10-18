using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Muffin
{
    /// <summary>
    /// The app's own camera. It shows your real eyes and mouth on the avatar and in the preview, and hands
    /// a small copy of every frame to the face tracker. Many virtual cameras (Iriun among them) allow only
    /// one program to read them, so the tracker no longer opens the camera itself.
    ///
    /// Frames for the tracker are 1280×720, top row first, RGB (the tracker's writer thread swaps them to
    /// the BGR that OpenSeeFace's raw input expects). A streamer sitting back from a phone camera is a small
    /// face in the picture: the bigger frame gives the tracker twice the detail, so its face points jitter
    /// less. The copy is made on the GPU and read back asynchronously, so the main thread never waits.
    /// `-muffin-testface &lt;png&gt;` feeds a still image instead of a camera (for the automated test), and
    /// `-muffin-testface-move` slides it from side to side like a fast-moving head.
    ///
    /// The last few frames sent to the tracker are kept on the GPU, numbered: the tracker's face points
    /// arrive a frame or two after the picture they were measured on, so the real-face cut-outs use that
    /// same frame (FrameById). Cut-outs from the live picture would sit where your eyes were a moment ago,
    /// showing cheek and forehead whenever you move.
    /// </summary>
    public sealed class CameraFeed : MonoBehaviour
    {
        public static CameraFeed Instance { get; private set; }

        public int trackWidth = 1280;
        public int trackHeight = 720;
        [Tooltip("Asked of the camera (it picks its nearest mode): the sharper, the crisper big real eyes look.")]
        public int requestWidth = 1920;
        public int requestHeight = 1080;
        public int requestFps = 30;

        /// <summary>The live camera image (for the preview).</summary>
        public Texture Texture => testImage ? (testFrame ? (Texture)testFrame : testImage) : webcam;
        public bool Playing => testImage || (webcam && webcam.isPlaying && webcam.width > 16);
        public bool VerticallyMirrored => !testImage && webcam && webcam.videoVerticallyMirrored;
        public int RotationAngle => !testImage && webcam ? webcam.videoRotationAngle : 0;
        public string DeviceName { get; private set; } = "";
        public int FramesSent { get; private set; }

        /// <summary>Raised on the main thread with each tracker frame (RGB, top row first) and its number. The buffer is reused.</summary>
        public event Action<byte[], int> FrameReady;

        /// <summary>How many recent frames are kept (at 30 fps, about a quarter of a second).</summary>
        const int HistoryFrames = 8;
        readonly RenderTexture[] history = new RenderTexture[HistoryFrames];
        readonly int[] historyIds = new int[HistoryFrames];
        int nextId = 1;
        RenderTexture testFrame;
        bool moveTest;

        /// <summary>The camera picture numbered id, if it's still kept; null if it's gone (use Texture).</summary>
        public Texture FrameById(int id)
        {
            if (testImage && !testFrame) return testImage;
            if (id <= 0) return null;
            for (int i = 0; i < HistoryFrames; i++)
                if (historyIds[i] == id && history[i]) return history[i];
            return null;
        }

        WebCamTexture webcam;
        Texture2D testImage;
        // Up to three frames on their way back from the GPU at once: with only one, every camera frame that
        // arrived while the last was still copying back was skipped (a 30 fps camera gave the tracker ~20).
        const int InFlight = 3;
        readonly RenderTexture[] small = new RenderTexture[InFlight];
        int pending, nextSmall, newestDelivered;
        byte[] frame;
        float nextTestFrame;

        void Awake()
        {
            Instance = this;
            string[] args = Environment.GetCommandLineArgs();
            // `-muffin-track 640x360`: a different tracker frame size (for measuring speed against steadiness).
            int t = Array.IndexOf(args, "-muffin-track");
            if (t >= 0 && t + 1 < args.Length)
            {
                string[] wh = args[t + 1].Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int tw) && int.TryParse(wh[1], out int th))
                {
                    trackWidth = tw;
                    trackHeight = th;
                }
            }
            int i = Array.IndexOf(args, "-muffin-testface");
            if (i >= 0 && i + 1 < args.Length && File.Exists(args[i + 1]))
            {
                testImage = new Texture2D(2, 2, TextureFormat.RGB24, false);
                testImage.LoadImage(File.ReadAllBytes(args[i + 1]));
                DeviceName = "test image";
                moveTest = Array.IndexOf(args, "-muffin-testface-move") >= 0;
                testImage.wrapMode = TextureWrapMode.Clamp;
            }
        }

        public static string[] DeviceNames()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-muffin-testface") >= 0) return new[] { "test image" };
            WebCamDevice[] devices = WebCamTexture.devices;
            var names = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++) names[i] = devices[i].name;
            return names;
        }

        /// <summary>Open the named camera (closing any previous one).</summary>
        public void Open(string deviceName)
        {
            if (testImage) return;
            Close();
            DeviceName = deviceName;
            webcam = new WebCamTexture(deviceName, requestWidth, requestHeight, requestFps);
            webcam.Play();
        }

        public void Close()
        {
            if (webcam)
            {
                webcam.Stop();
                Destroy(webcam);
                webcam = null;
            }
        }

        void OnDestroy()
        {
            Close();
            foreach (RenderTexture rt in small)
                if (rt) rt.Release();
            foreach (RenderTexture rt in history)
                if (rt) rt.Release();
            if (testFrame) testFrame.Release();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!Playing || pending >= InFlight) return;
            if (testImage)
            {
                // A still image still has to arrive as a stream of frames, ~30 a second.
                if (Time.unscaledTime < nextTestFrame) return;
                // A steady 30 a second (scheduled from the last tick, not from now, which drifted to ~22).
                nextTestFrame = Mathf.Max(nextTestFrame + 1f / 30f, Time.unscaledTime - 0.1f);
                if (moveTest)
                {
                    // A head swinging side to side, fast (peak ~0.45 picture widths a second).
                    if (!testFrame) testFrame = new RenderTexture(testImage.width, testImage.height, 0, RenderTextureFormat.ARGB32);
                    float dx = 0.12f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI * 0.6f);
                    Graphics.Blit(testImage, testFrame, Vector2.one, new Vector2(dx, 0f));
                }
            }
            else if (!webcam.didUpdateThisFrame) return;

            int slot = nextSmall;
            nextSmall = (nextSmall + 1) % InFlight;
            if (!small[slot]) small[slot] = new RenderTexture(trackWidth, trackHeight, 0, RenderTextureFormat.ARGB32);
            // GPU textures start at the bottom row; the tracker wants the top row first, so flip while
            // shrinking (twice-flipped if the camera already delivers its image upside down).
            bool flip = !VerticallyMirrored;
            Graphics.Blit(Texture, small[slot], new Vector2(1f, flip ? -1f : 1f), new Vector2(0f, flip ? 1f : 0f));
            int id = nextId++;
            Keep(id);
            pending++;
            AsyncGPUReadback.Request(small[slot], 0, TextureFormat.RGB24, request => OnReadback(request, id));
        }

        /// <summary>Copy the frame being sent into the history (GPU to GPU), under its number.</summary>
        void Keep(int id)
        {
            if (testImage && !testFrame) return; // a still picture never changes
            Texture source = Texture;
            int slot = id % HistoryFrames;
            RenderTexture rt = history[slot];
            if (!rt || rt.width != source.width || rt.height != source.height)
            {
                if (rt) rt.Release();
                rt = history[slot] = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }
            Graphics.Blit(source, rt);
            historyIds[slot] = id;
        }

        void OnReadback(AsyncGPUReadbackRequest request, int id)
        {
            pending--;
            // Never hand the tracker an older frame after a newer one.
            if (request.hasError || this == null || id <= newestDelivered) return;
            newestDelivered = id;
            NativeArray<byte> data = request.GetData<byte>();
            if (frame == null || frame.Length != data.Length) frame = new byte[data.Length];
            data.CopyTo(frame);
            FramesSent++;
            FrameReady?.Invoke(frame, id);
        }
    }
}
