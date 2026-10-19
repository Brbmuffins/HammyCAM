using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Muffin
{
    /// <summary>How your head drives the avatar.</summary>
    public enum ControlMode
    {
        /// <summary>
        /// Turning, nodding and tilting your head (Face Object Stream's rule): where your head sits in the
        /// picture doesn't matter, so you never lean out of frame to move the avatar. Tilting leans it.
        /// </summary>
        HeadTurns,
        /// <summary>Also moving your head around in the picture: lean sideways, rise, lean in.</summary>
        BodyLean,
    }

    /// <summary>Which face tracker reads the webcam.</summary>
    public enum TrackerKind { MediaPipe, OpenSeeFace }

    /// <summary>
    /// Webcam face tracking. The app's CameraFeed opens the camera and streams frames into a tracker program
    /// on stdin; the tracker sends back face points, and this turns them into a HeadPose. Two trackers:
    ///
    /// - MediaPipe Face Landmarker (default; Apache-2.0), run by tracker/muffin_facetracker.py bundled as
    ///   TrackerMP/muffin_facetracker.exe: 478 points, 52 expression scores (blendshapes) and the head's
    ///   rotation. Ported from the Face Object Stream handoff: yaw and pitch from MediaPipe's transformation
    ///   matrix, roll from the eye line, wrapped angles against a neutral pose, adaptive smoothing.
    /// - OpenSeeFace (BSD-2), the bundled facetracker.exe: 68 points; turn and nod read from the nose.
    ///
    /// F5 switches tracker, L switches control (head turns / body lean), C re-centres, M mirrors, N next
    /// camera, P shows what the camera sees, T follows the next person.
    /// </summary>
    [RequireComponent(typeof(OpenSee.OpenSee), typeof(CameraFeed))]
    public sealed class WebcamHeadPoseSource : HeadPoseSource
    {
        [Header("Trackers")]
        public TrackerKind tracker = TrackerKind.MediaPipe;
        [Tooltip("MediaPipe helper. Relative paths are tried next to the app first, then in the project's third-party folder.")]
        public string mediaPipeExe = "TrackerMP/muffin_facetracker.exe";
        public string mediaPipeModel = "TrackerMP/face_landmarker.task";
        public int mediaPipePort = 11574;
        [Tooltip("facetracker.exe (OpenSeeFace).")]
        public string trackerExe = "Tracker/Binary/facetracker.exe";
        public int cameraIndex;
        [Tooltip("OpenSeeFace model: 3 is the best quality, -3..2 are faster.")]
        [Range(-3, 4)] public int model = 3;
        [Tooltip("CPU threads OpenSeeFace may use.")]
        [Range(1, 8)] public int trackerThreads = 4;

        [Header("Control")]
        public ControlMode control = ControlMode.HeadTurns;
        [Tooltip("Head tilt (degrees) for a full sideways lean, in head-turn control.")]
        public float tiltForFullLean = 24f;
        [Tooltip("In head-turn control, how much of your sideways sway still leans the avatar (1 = as much as body lean).")]
        [Range(0f, 1f)] public float headTurnSway = 0.5f;

        [Header("Range (body lean, in eye-widths; one is roughly 9 cm)")]
        [Tooltip("How far you lean sideways for a full bend.")]
        public float leanRange = 1.1f;
        [Tooltip("How far you move up or down for a full stretch or scrunch.")]
        public float heightRange = 0.7f;
        [Tooltip("How much bigger your face gets (leaning in) for a full forward bend, as a fraction.")]
        public float depthRange = 0.35f;
        [Header("Turn and nod")]
        [Tooltip("MediaPipe measures true angles; a little extra reads better on a cartoon.")]
        public float mediaPipeRotationGain = 1.25f;
        public float yawGain = 150f; // OpenSeeFace: the averaged nose moves less per degree than the tip alone
        public float pitchGain = 140f;
        [Tooltip("Largest head turn / nod / tilt passed on, in degrees.")]
        public float maxTurn = 70f;
        public float maxNod = 45f;
        public float maxTilt = 30f;
        [Header("Expressions")]
        public float mouthGain = 2.0f;
        [Tooltip("OpenSeeFace reports these -1..1 against your own neutral face.")]
        public float expressionGain = 1.3f;
        [Tooltip("Flip if smiling shows as a frown on the readout (OpenSeeFace).")]
        public bool invertSmile = true;
        [Tooltip("Flip if raising your brows lowers the avatar's (OpenSeeFace).")]
        public bool invertBrows;

        [Header("Direction")]
        [Tooltip("Mirror: lean to your right and the avatar leans to the right of the screen, like a mirror.")]
        public bool mirror = true;

        [Header("Sensitivity")]
        [Tooltip("Scales leaning (not looking around). - and + adjust it live; it's remembered.")]
        [Range(0.2f, 2f)] public float sensitivity = 1f;

        [Header("People")]
        [Tooltip("How many faces the tracker watches. The avatar follows one: the biggest, most centred face, " +
                 "and keeps following that person; T switches to the next person.")]
        [Range(1, 4)] public int maxFaces = 3;

        public static WebcamHeadPoseSource Instance { get; private set; }
        public string Status { get; private set; } = "starting";
        public string CameraName => cameraIndex < cameraNames.Length ? cameraNames[cameraIndex] : $"camera {cameraIndex}";
        /// <summary>False when no tracker program is found or this is a self-test run (the app falls back to the mouse).</summary>
        public bool Available { get; private set; }
        public bool Started { get; private set; }
        /// <summary>The camera list came back empty: nothing to track (the app falls back to the mouse).</summary>
        public bool NoCamera => camerasListed && cameraNames.Length == 0;
        /// <summary>The tracker actually running (MediaPipe falls back to OpenSeeFace if it isn't installed).</summary>
        public TrackerKind Running { get; private set; }
        public override string Label => $"Webcam: {(NoCamera ? "none" : CameraName)} · {Running} · {Status}";
        public override bool PreFiltered => true;
        // A new key: the old one stored the 60% default from before the gains were raised.
        const string SensitivityKey = "muffin.sensitivity.v2";
        public bool Calibrating => recalibrate;

        /// <summary>Latest usable face points (image pixels, x right / y down; see Layout) and that image's size.</summary>
        public Vector2[] Points { get; private set; }
        /// <summary>Which point is which in Points.</summary>
        public FaceLayout Layout { get; private set; } = FaceLayout.MediaPipe;
        public Vector2 ImageSize { get; private set; }
        public bool HasFace { get; private set; }
        /// <summary>Faces in the newest frame, the followed one included.</summary>
        public int FaceCount { get; private set; }
        /// <summary>Points of the other people in view (for the preview and diagnostics).</summary>
        public List<Vector2[]> OtherFaces { get; } = new List<Vector2[]>();
        /// <summary>The id of the person the avatar follows, -1 before anyone is found.</summary>
        public int FollowedId { get; private set; } = -1;
        /// <summary>The person was picked with T (kept until they leave), rather than automatically.</summary>
        public bool PickedByHand { get; private set; }
        /// <summary>Face updates per second from the tracker (the camera's frame rate when all is well).</summary>
        public float TrackerFps { get; private set; }
        /// <summary>The number of the camera frame Points were measured on (-1 unknown): see CameraFeed.FrameById.</summary>
        public int PointsFrameId { get; private set; } = -1;
        double pointsReadTime;
        int healthRows, healthFramesSent;
        float healthEyeSpan;
        Vector2 healthCentre;
        int healthFaceId = -1;
        bool healthPrimed;
        float healthLastUpdate, healthWorstGap;
        /// <summary>The camera picture Points belong to (the live one if that frame is gone): cut-outs use this.</summary>
        public Texture PointsImage => (feed && !NoSync ? feed.FrameById(PointsFrameId) : null) ?? (feed ? feed.Texture : null);
        // `-muffin-nosync`: cut from the live picture as before (to compare in tests).
        static bool NoSync => HasArg("-muffin-nosync");
        /// <summary>Share of tracker updates matched to their frame, and the average clock gap (ms), for diagnostics.</summary>
        public float SyncedShare => syncUpdates > 0 ? (float)syncMatched / syncUpdates : 0f;
        public float SyncGapMs => syncMatched > 0 ? (float)(syncGapSum / syncMatched * 1000.0) : 0f;
        /// <summary>Whether the in-app camera preview is shown (P).</summary>
        public bool ShowPreview { get; set; }
        /// <summary>The followed face's expression scores, when the tracker gives them (MediaPipe), for the readout.</summary>
        public float[] Blendshapes { get; private set; }

        /// <summary>One face in one tracker update, whichever tracker sent it.</summary>
        sealed class TrackedFace
        {
            public int Id;
            public double Time;
            public Vector2 Resolution;
            public Vector2[] Points;
            public OpenSee.OpenSee.OpenSeeData Osf; // OpenSeeFace's extras (its features and eye openness)
            public float[] Scores;                   // MediaPipe's blendshapes
            public Matrix4x4 Matrix;                 // MediaPipe's transformation matrix
        }

        OpenSee.OpenSee receiver;
        readonly MediaPipeReceiver mediaPipe = new MediaPipeReceiver();
        CameraFeed feed;
        Process process;
        string osfExe, mpExe, mpModel;
        string[] cameraNames = Array.Empty<string>();
        bool camerasListed;
        float trackerStartedAt;
        double lastPacketTime;
        int lastMediaPipeCount;
        float lastPacketAt = -10f;
        bool recalibrate = true;
        HeadPose last = HeadPose.Rest;
        readonly List<TrackedFace> faces = new List<TrackedFace>();
        // MediaPipe doesn't number faces: a face keeps its number while it stays close to where it was.
        readonly List<(int id, Vector2 centre, float width)> previousFaces = new List<(int, Vector2, float)>();
        int nextFaceId;
        bool switchPerson;
        Vector2 followedCentre;
        int challengerId = -1;
        float challengerSince;
        readonly PoseFilter poseFilter = new PoseFilter();
        int syncUpdates, syncMatched;
        double syncGapSum;
        double filterFrom;
        int packetsThisSecond;
        float rateFrom;

        // Frames travel to the tracker on a background thread: the newest frame wins, older ones are dropped.
        readonly object frameLock = new object();
        readonly AutoResetEvent frameSignal = new AutoResetEvent(false);
        byte[] pendingFrame, writeFrame;
        int pendingId, writeId;
        bool hasPending;
        volatile bool swapToBgr;
        // Which camera frame the tracker read when: frame number and the clock time its write finished (the
        // frame is bigger than the pipe, so the write only finishes once the tracker has read it all).
        readonly (int id, double at)[] written = new (int, double)[16];
        int writtenCount;
        readonly object writtenLock = new object();
        Thread writer;
        volatile bool writerRunning;

        // Neutral pose: the average of the first good frames, again whenever C is pressed. One frame
        // alone can catch you mid-blink or mid-move, so a short average is steadier.
        const int CalibrationFrames = 15;
        /// <summary>Seconds the last pose is held after the face is lost.</summary>
        const float HoldWhenLost = 1.5f;
        int calibrationCount;
        Vector2 sumCentre;
        float sumEyeWidth, sumRoll;
        Vector2 sumYaw, sumPitch; // MediaPipe's angles are averaged as directions (sin, cos), so they can't wrap
        Vector2 restCentre;
        float restEyeWidth = 1f, restYaw, restPitch, restRoll;

        static bool HasArg(string a) => Array.IndexOf(Environment.GetCommandLineArgs(), a) >= 0;
        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        // The self-test runs without a camera, unless it brings a test face to track.
        static bool SelfTestRun => (HasArg("-muffin-selftest") && !HasArg("-muffin-testface")) || HasArg("-no-webcam");

        void Awake()
        {
            Instance = this;
            receiver = GetComponent<OpenSee.OpenSee>();
            feed = GetComponent<CameraFeed>();
            mirror = PlayerPrefs.GetInt("muffin.mirror", mirror ? 1 : 0) == 1;
            sensitivity = PlayerPrefs.GetFloat(SensitivityKey, sensitivity);
            cameraIndex = PlayerPrefs.GetInt("muffin.camera", cameraIndex);
            tracker = (TrackerKind)Mathf.Clamp(PlayerPrefs.GetInt("muffin.tracker", (int)tracker), 0, 1);
            control = (ControlMode)Mathf.Clamp(PlayerPrefs.GetInt("muffin.control", (int)control), 0, 1);
            // Overrides for tests and measurements: -muffin-tracker mediapipe|openseeface, -muffin-control
            // turns|lean, -muffin-faces N, -muffin-threads N, -muffin-model M.
            string t = Arg("-muffin-tracker");
            if (t != null) tracker = t.StartsWith("open", StringComparison.OrdinalIgnoreCase) ? TrackerKind.OpenSeeFace : TrackerKind.MediaPipe;
            string c = Arg("-muffin-control");
            if (c != null) control = c.StartsWith("lean", StringComparison.OrdinalIgnoreCase) ? ControlMode.BodyLean : ControlMode.HeadTurns;
            if (int.TryParse(Arg("-muffin-faces"), out int f)) maxFaces = Mathf.Clamp(f, 1, 4);
            if (int.TryParse(Arg("-muffin-threads"), out int th)) trackerThreads = Mathf.Clamp(th, 1, 8);
            if (int.TryParse(Arg("-muffin-model"), out int mo)) model = Mathf.Clamp(mo, -3, 4);
        }

        void Start()
        {
            if (SelfTestRun)
            {
                Status = "off (self-test)";
                Started = true;
                return;
            }
            osfExe = Resolve(trackerExe, Path.Combine("..", "third-party", "OpenSeeFace-v1.20.5", "Binary", "facetracker.exe"));
            mpExe = Resolve(mediaPipeExe, Path.Combine("..", "third-party", "mediapipe", "dist", "muffin_facetracker", "muffin_facetracker.exe"));
            mpModel = Resolve(mediaPipeModel, Path.Combine("..", "third-party", "mediapipe", "models", "face_landmarker.task"));
            Started = true;
            if (osfExe == null && (mpExe == null || mpModel == null))
            {
                Status = "no face tracker found";
                return;
            }
            Available = true;
            Restart();
        }

        /// <summary>Look for cameras again and start tracking on the chosen one (after plugging a webcam in).</summary>
        public void Restart()
        {
            if (!Available || feed == null) return;
            StopTracker();
            cameraNames = CameraFeed.DeviceNames();
            camerasListed = true;
            if (NoCamera)
            {
                feed.Close();
                Status = "no webcam found: plug one in, then press W";
                return;
            }
            if (cameraIndex >= cameraNames.Length) cameraIndex = 0;
            feed.Open(cameraNames[cameraIndex]);
            StartTracker();
        }

        void Update()
        {
            // Keep the points fresh even when the mouse drives the avatar: the real face and the preview
            // still need them.
            Poll();
            if (Keys.Pressed(MuffinKey.C)) Recentre();
            // Mirror lives in the menu (M opens the menu).
            if (Keys.Pressed(MuffinKey.N) && cameraNames.Length > 1)
            {
                cameraIndex = (cameraIndex + 1) % cameraNames.Length;
                PlayerPrefs.SetInt("muffin.camera", cameraIndex);
                Restart();
            }
            if (Keys.Pressed(MuffinKey.F5))
            {
                tracker = tracker == TrackerKind.MediaPipe ? TrackerKind.OpenSeeFace : TrackerKind.MediaPipe;
                PlayerPrefs.SetInt("muffin.tracker", (int)tracker);
                Restart();
            }
            if (Keys.Pressed(MuffinKey.L))
            {
                control = control == ControlMode.HeadTurns ? ControlMode.BodyLean : ControlMode.HeadTurns;
                PlayerPrefs.SetInt("muffin.control", (int)control);
            }
            if (Keys.Pressed(MuffinKey.P)) ShowPreview = !ShowPreview;
            if (Keys.Pressed(MuffinKey.T)) switchPerson = true;
            if (Keys.Pressed(MuffinKey.Minus) || Keys.Pressed(MuffinKey.Plus))
            {
                float step = Keys.Pressed(MuffinKey.Plus) ? 0.1f : -0.1f;
                sensitivity = Mathf.Clamp(Mathf.Round((sensitivity + step) * 10f) / 10f, 0.2f, 2f);
                PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
            }
        }

        /// <summary>Take the current pose as the new neutral (C).</summary>
        public void Recentre()
        {
            recalibrate = true;
            calibrationCount = 0;
        }

        // The webcam settings, as the keys set them, for the control panel (each remembered).
        public void SetMirror(bool on)
        {
            mirror = on;
            PlayerPrefs.SetInt("muffin.mirror", mirror ? 1 : 0);
        }

        public void NextCamera()
        {
            if (cameraNames.Length < 2) return;
            cameraIndex = (cameraIndex + 1) % cameraNames.Length;
            PlayerPrefs.SetInt("muffin.camera", cameraIndex);
            Restart();
        }

        public void SetTracker(TrackerKind kind)
        {
            if (tracker == kind && Running == kind) return;
            tracker = kind;
            PlayerPrefs.SetInt("muffin.tracker", (int)tracker);
            Restart();
        }

        public void SetControl(ControlMode mode)
        {
            control = mode;
            PlayerPrefs.SetInt("muffin.control", (int)control);
        }

        public void SetSensitivity(float value)
        {
            sensitivity = Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 0.2f, 2f);
            PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
        }

        /// <summary>Follow the next person in view (T).</summary>
        public void NextPerson() => switchPerson = true;

        void OnDestroy()
        {
            StopTracker();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() => StopTracker();

        /// <summary>Take in the newest tracker update, if there is one (safe to call more than once a frame).</summary>
        void Poll()
        {
            bool fresh = Running == TrackerKind.MediaPipe ? CollectMediaPipe() : CollectOpenSeeFace();
            if (fresh)
            {
                packetsThisSecond++;
                FaceCount = faces.Count;
                bool requestedSwitch = switchPerson;
                TrackedFace data = Choose();
                OtherFaces.Clear();
                foreach (var d in faces)
                    if (d != data) OtherFaces.Add(d.Points);
                if (data != null)
                {
                    if (healthLastUpdate > 0f)
                        healthWorstGap = Mathf.Max(healthWorstGap, Time.unscaledTime - healthLastUpdate);
                    healthLastUpdate = Time.unscaledTime;
                    float span = EyeWidth(data.Points);
                    Vector2 centre = EyeCentre(data.Points);
                    if (healthPrimed)
                    {
                        float shift = Vector2.Distance(centre, healthCentre) / Mathf.Max(1f, healthEyeSpan);
                        float ratio = span / Mathf.Max(1f, healthEyeSpan);
                        if (data.Id != healthFaceId || shift > 0.3f || ratio < 0.75f || ratio > 1.33f)
                            UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                                "[MappingJump] t={0:F3} id={1}->{2} faces={3} centreShift={4:F2}eyeWidths spanRatio={5:F2} ageMs={6:F0}",
                                Time.unscaledTime, healthFaceId, data.Id, FaceCount, shift, ratio, (UnixNow() - data.Time) * 1000.0);
                    }
                    healthPrimed = true;
                    healthEyeSpan = span;
                    healthCentre = centre;
                    healthFaceId = data.Id;
                    if (data.Id != FollowedId)
                    {
                        // Preserve calibration when the same person returns after a brief dropout.
                        bool reacquired = FollowedId >= 0 && !requestedSwitch &&
                            Time.unscaledTime - lastPacketAt < HoldWhenLost &&
                            Vector2.Distance(EyeCentre(data.Points), followedCentre) < 0.75f * EyeWidth(data.Points);
                        FollowedId = data.Id;
                        if (!reacquired) Recentre();
                    }
                    followedCentre = EyeCentre(data.Points);
                    lastPacketAt = Time.unscaledTime;
                    last = Convert(data);
                    Points = data.Points;
                    ImageSize = data.Resolution;
                    Blendshapes = data.Scores;
                    PointsFrameId = MatchFrame(data.Time);
                    pointsReadTime = data.Time;
                }
            }
            HasFace = Time.unscaledTime - lastPacketAt < 0.5f;
            if (Time.unscaledTime - rateFrom >= 1f)
            {
                TrackerFps = packetsThisSecond / (Time.unscaledTime - rateFrom);
                // Temporary bounded telemetry for intermittent mapping failures. No camera images are saved.
                if (healthRows++ < 600)
                {
                    int sent = feed ? feed.FramesSent : 0;
                    bool retained = feed && feed.FrameById(PointsFrameId);
                    float eyePixels = Points != null ? EyeWidth(Points) : 0f;
                    double ageMs = pointsReadTime > 0 ? (UnixNow() - pointsReadTime) * 1000.0 : -1;
                    UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", $"[MappingHealth] t={Time.unscaledTime:F2} tracker={TrackerFps:F1}/s camera={(sent - healthFramesSent) / (Time.unscaledTime - rateFrom):F1}/s " +
                        $"faces={FaceCount} id={FollowedId} hasFace={HasFace} calibrating={recalibrate} " +
                        $"ageMs={ageMs:F0} sourceAgeMs={(feed ? feed.FrameAgeMs(PointsFrameId) : -1):F0} worstGapMs={healthWorstGap * 1000f:F0} frame={PointsFrameId} retained={retained} matchPct={SyncedShare * 100f:F0} eyeSpanPx={eyePixels:F1} renderMs={Time.unscaledDeltaTime * 1000f:F1}");
                    healthFramesSent = sent;
                }
                healthWorstGap = 0f;
                packetsThisSecond = 0;
                rateFrom = Time.unscaledTime;
            }
            UpdateStatus();
        }

        /// <summary>OpenSeeFace: every face in its newest update (its receiver also keeps people who have left).</summary>
        bool CollectOpenSeeFace()
        {
            OpenSee.OpenSee.OpenSeeData[] all = receiver.trackingData;
            double newest = NewestTime(all);
            if (newest <= lastPacketTime) return false;
            lastPacketTime = newest;
            faces.Clear();
            foreach (var d in all)
                if (d != null && newest - d.time < 0.1 && Usable(d))
                    faces.Add(new TrackedFace { Id = d.id, Time = d.time, Resolution = d.cameraResolution, Points = d.points, Osf = d });
            return true;
        }

        /// <summary>MediaPipe: the newest frame's faces, in pixels, numbered by where they were last time.</summary>
        bool CollectMediaPipe()
        {
            int count = mediaPipe.Received;
            MediaPipeReceiver.Frame frame = mediaPipe.Latest;
            if (frame == null || count == lastMediaPipeCount) return false;
            lastMediaPipeCount = count;
            faces.Clear();
            var size = new Vector2(frame.Width, frame.Height);
            var taken = new List<int>();
            var current = new List<(int, Vector2, float)>();
            foreach (MediaPipeReceiver.Face face in frame.Faces)
            {
                var points = new Vector2[face.Points.Length];
                for (int i = 0; i < points.Length; i++) points[i] = new Vector2(face.Points[i].x * size.x, face.Points[i].y * size.y);
                Vector2 centre = EyeCentre(points, FaceLayout.MediaPipe);
                float width = EyeWidth(points, FaceLayout.MediaPipe);
                int id = -1;
                float best = float.MaxValue;
                foreach (var (pid, pc, pw) in previousFaces)
                {
                    float gap = Vector2.Distance(pc, centre);
                    if (gap < 1.5f * Mathf.Max(pw, width) && gap < best && !taken.Contains(pid))
                    {
                        best = gap;
                        id = pid;
                    }
                }
                if (id < 0) id = nextFaceId++;
                taken.Add(id);
                current.Add((id, centre, width));
                faces.Add(new TrackedFace { Id = id, Time = frame.Time, Resolution = size, Points = points, Scores = face.Scores, Matrix = face.Matrix });
            }
            // A single missed detection must not erase everyone's identity: that used to assign a new
            // ID on return and trigger 15 frames of neutral-pose calibration (visible as a freeze/jump).
            if (current.Count > 0 || Time.unscaledTime - lastPacketAt >= HoldWhenLost)
            {
                previousFaces.Clear();
                previousFaces.AddRange(current);
            }
            return true;
        }

        /// <summary>
        /// Which face drives the avatar when more than one person is in view: the same person as before,
        /// for as long as the tracker keeps them; T moves on to the next person; otherwise the main face,
        /// the biggest and most centred one, which is normally whoever sits at the computer.
        /// </summary>
        TrackedFace Choose()
        {
            if (faces.Count == 0) return null;
            if (switchPerson)
            {
                switchPerson = false;
                if (faces.Count > 1)
                {
                    // Left to right as the preview shows them (mirrored when mirror is on), wrapping round.
                    var order = new List<TrackedFace>(faces);
                    order.Sort((a, b) => (mirror ? -1 : 1) * EyeCentre(a.Points).x.CompareTo(EyeCentre(b.Points).x));
                    int at = order.FindIndex(d => d.Id == FollowedId);
                    PickedByHand = true;
                    return order[(at + 1) % order.Count];
                }
            }
            TrackedFace main = MainFace();
            TrackedFace followed = faces.Find(d => d.Id == FollowedId);
            if (followed != null)
            {
                if (PickedByHand || main == followed || MainScore(main) < MainScore(followed) * 1.4f)
                {
                    challengerId = -1;
                    return followed;
                }
                // Automatic choice: move to the main face only once it has clearly been the main one for a
                // moment, so whoever the tracker happened to find first doesn't keep the avatar, and someone
                // leaning in for a second doesn't steal it.
                if (challengerId != main.Id)
                {
                    challengerId = main.Id;
                    challengerSince = Time.unscaledTime;
                }
                return Time.unscaledTime - challengerSince > 2.5f ? main : followed;
            }
            if (FollowedId >= 0 && Time.unscaledTime - lastPacketAt < HoldWhenLost)
            {
                // The tracker lost them for a moment; when it finds them again it may give them a new id.
                // Take a face where they just were, otherwise hold the last pose and wait.
                foreach (var d in faces)
                    if (Vector2.Distance(EyeCentre(d.Points), followedCentre) < 1.5f * EyeWidth(d.Points)) return d;
                return null;
            }
            PickedByHand = false;
            return main;
        }

        /// <summary>The biggest, most centred face: normally whoever sits at the computer.</summary>
        TrackedFace MainFace()
        {
            TrackedFace best = null;
            float bestScore = float.MinValue;
            foreach (var d in faces)
            {
                float score = MainScore(d);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = d;
                }
            }
            return best;
        }

        /// <summary>Face size, strongly discounted toward the edges, where people lean in from the side.</summary>
        float MainScore(TrackedFace d)
        {
            float across = d.Resolution.x > 1f ? EyeCentre(d.Points).x / d.Resolution.x : 0.5f;
            return EyeWidth(d.Points) * Mathf.Max(0.05f, 1f - 1.6f * Mathf.Abs(across - 0.5f));
        }

        FaceLayout CurrentLayout => Running == TrackerKind.MediaPipe ? FaceLayout.MediaPipe : FaceLayout.OpenSeeFace;
        Vector2 EyeCentre(Vector2[] p) => EyeCentre(p, CurrentLayout);
        float EyeWidth(Vector2[] p) => EyeWidth(p, CurrentLayout);
        static Vector2 EyeCentre(Vector2[] p, FaceLayout l) => (p[l.OuterLeft] + p[l.InnerLeft] + p[l.InnerRight] + p[l.OuterRight]) / 4f;
        static float EyeWidth(Vector2[] p, FaceLayout l) => Mathf.Max(1f, (p[l.OuterRight] - p[l.OuterLeft]).magnitude);

        /// <summary>Runs every frame (through Poll), whichever input is driving the avatar.</summary>
        void UpdateStatus()
        {
            if (NoCamera || !camerasListed) return;
            if (process != null && !process.HasExited)
            {
                bool noVideo = feed && !feed.Playing && Time.unscaledTime - trackerStartedAt > 4f;
                Status = noVideo ? "the camera isn't sending video (is another app using it?)"
                    : !HasFace ? (ShowPreview ? "looking for your face" : "looking for your face: press P to see the camera")
                    : recalibrate ? "hold still: finding your neutral pose" : "tracking";
            }
            else if (process != null)
                Status = "the tracker stopped: press W twice to restart it";
        }

        public override bool TryGetPose(out HeadPose pose)
        {
            Poll();
            pose = last;
            if (NoCamera || !camerasListed) return false;
            // A big look over the shoulder can turn your face out of the tracker's view: hold the last pose
            // for a moment instead of snapping back, then let the avatar ease home.
            return Time.unscaledTime - lastPacketAt < HoldWhenLost;
        }

        HeadPose Convert(TrackedFace d)
        {
            FaceLayout l = CurrentLayout;
            Vector2[] p = d.Points;
            bool mp = d.Scores != null;
            Vector2 centre = EyeCentre(p, l);
            Vector2 eyeLine = p[l.OuterRight] - p[l.OuterLeft];
            float eyeWidth = Mathf.Max(1f, eyeLine.magnitude);
            // Tilt from the eye line, for both trackers (steadier for the cut-outs, per the handoff).
            float roll = Mathf.Atan2(eyeLine.y, eyeLine.x) * Mathf.Rad2Deg;
            float yaw, pitch;
            if (mp)
            {
                // MediaPipe's own 3D fit: the rotation part of its transformation matrix, as YXZ angles.
                // Yaw + = turning to your left, pitch + = looking down.
                Matrix4x4 m = d.Matrix;
                pitch = Mathf.Asin(-Mathf.Clamp(m.m12, -1f, 1f)) * Mathf.Rad2Deg;
                yaw = Mathf.Atan2(m.m02, m.m22) * Mathf.Rad2Deg;
            }
            else
            {
                // OpenSeeFace: the nose against the eyes, in eye widths (the tip averaged with the points
                // under it: the tip alone jitters by a pixel or two, which a big gain makes into shake).
                Vector2 across = eyeLine / eyeWidth;
                Vector2 down = new Vector2(-across.y, across.x);
                Vector2 nose = NoseOf(p, l);
                yaw = Vector2.Dot(nose - centre, across) / eyeWidth;
                pitch = Vector2.Dot(nose - centre, down) / eyeWidth;
            }

            if (recalibrate)
            {
                if (calibrationCount == 0)
                {
                    sumCentre = sumYaw = sumPitch = Vector2.zero;
                    sumEyeWidth = sumRoll = 0f;
                }
                sumCentre += centre;
                sumEyeWidth += eyeWidth;
                sumYaw += mp ? Direction(yaw) : new Vector2(yaw, 0f);
                sumPitch += mp ? Direction(pitch) : new Vector2(pitch, 0f);
                sumRoll += roll;
                if (++calibrationCount < CalibrationFrames) return HeadPose.Rest;
                restCentre = sumCentre / calibrationCount;
                restEyeWidth = sumEyeWidth / calibrationCount;
                restYaw = mp ? Mathf.Atan2(sumYaw.y, sumYaw.x) * Mathf.Rad2Deg : sumYaw.x / calibrationCount;
                restPitch = mp ? Mathf.Atan2(sumPitch.y, sumPitch.x) * Mathf.Rad2Deg : sumPitch.x / calibrationCount;
                restRoll = sumRoll / calibrationCount;
                calibrationCount = 0;
                recalibrate = false;
                poseFilter.Reset(); // a new neutral pose: start the smoothing fresh
                filterFrom = 0.0;
            }

            // Everything relative to the neutral pose (angles subtracted with wraparound), and capped to what
            // a head comfortably does. Mirror mode turns the avatar the way your reflection would.
            float side = mirror ? 1f : -1f;
            float yawDegrees, pitchDegrees;
            if (mp)
            {
                yawDegrees = Mathf.Clamp(Mathf.DeltaAngle(restYaw, yaw) * mediaPipeRotationGain, -maxTurn, maxTurn) * side;
                pitchDegrees = Mathf.Clamp(-Mathf.DeltaAngle(restPitch, pitch) * mediaPipeRotationGain, -maxNod, maxNod);
            }
            else
            {
                yawDegrees = Mathf.Clamp((yaw - restYaw) * yawGain, -maxTurn, maxTurn) * side;
                pitchDegrees = Mathf.Clamp(-(pitch - restPitch) * pitchGain, -maxNod, maxNod);
            }
            float rollDegrees = Mathf.Clamp(Mathf.DeltaAngle(restRoll, roll), -maxTilt, maxTilt) * side;

            Vector3 offset;
            if (control == ControlMode.HeadTurns)
            {
                // Head turns: tilting your head leans the avatar the same way (a positive tilt tips its top
                // toward screen left). Where your head is in the picture mostly doesn't matter, but a share of
                // your sideways sway still leans it, so the floppy tips get the swing they had in body lean.
                Vector2 moved = (centre - restCentre) / restEyeWidth;
                offset = new Vector3(-rollDegrees / Mathf.Max(1f, tiltForFullLean) - side * moved.x / leanRange * headTurnSway, 0f, 0f);
            }
            else
            {
                // Body lean: you appear mirrored-left in the picture when you move right; mirror mode flips that.
                Vector2 moved = (centre - restCentre) / restEyeWidth;
                offset = new Vector3(-side * moved.x / leanRange, -moved.y / heightRange, (eyeWidth / restEyeWidth - 1f) / depthRange);
            }

            // Smooth once per tracker update with the real time between updates (see PoseFilter).
            // Looking around is not scaled by sensitivity: big looks over the shoulder should read big.
            Vector3 lean = offset * sensitivity;
            var degrees = new Vector3(pitchDegrees, yawDegrees, rollDegrees);
            poseFilter.Adaptive = mp;
            poseFilter.Filter(ref lean, ref degrees, filterFrom > 0.0 ? (float)(d.Time - filterFrom) : 1f / 30f);
            filterFrom = d.Time;

            var pose = new HeadPose { Offset = lean, Rotation = Quaternion.Euler(degrees.x, degrees.y, degrees.z) };
            if (mp) Expressions(ref pose, d.Scores);
            else
            {
                var o = d.Osf;
                pose.MouthOpen = Mathf.Clamp01(o.features.MouthOpen * mouthGain);
                pose.BlinkLeft = 1f - Mathf.InverseLerp(0.2f, 0.6f, o.leftEyeOpen);
                pose.BlinkRight = 1f - Mathf.InverseLerp(0.2f, 0.6f, o.rightEyeOpen);
                // Measured in the tracker's face-model space (y up): corners rising read as a smaller
                // "upper lip minus corner" value, hence the default invertSmile.
                pose.Smile = Mathf.Clamp((o.features.MouthCornerUpDownLeft + o.features.MouthCornerUpDownRight) * 0.5f
                                         * expressionGain * (invertSmile ? -1f : 1f), -1f, 1f);
                pose.BrowRaise = Mathf.Clamp((o.features.EyebrowUpDownLeft + o.features.EyebrowUpDownRight) * 0.5f
                                             * expressionGain * (invertBrows ? -1f : 1f), -1f, 1f);
                pose.MouthWide = Mathf.Clamp(o.features.MouthWide * expressionGain, -1f, 1f);
                // Corners measured separately make a smirk; mirroring swaps which side of the screen it shows on.
                pose.Smirk = Mathf.Clamp((o.features.MouthCornerUpDownRight - o.features.MouthCornerUpDownLeft) * 0.5f
                                         * expressionGain * (invertSmile ? -1f : 1f) * side, -1f, 1f);
            }
            return pose;
        }

        static Vector2 Direction(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        // MediaPipe's blendshape order (tracker/muffin_facetracker.py sends them in this order).
        const int BrowDownLeft = 1, BrowDownRight = 2, BrowInnerUp = 3, BrowOuterUpLeft = 4, BrowOuterUpRight = 5,
            EyeBlinkLeft = 9, EyeBlinkRight = 10, JawOpen = 25, MouthFrownLeft = 30, MouthFrownRight = 31,
            MouthFunnel = 32, MouthPucker = 38, MouthSmileLeft = 44, MouthSmileRight = 45,
            MouthStretchLeft = 46, MouthStretchRight = 47;

        /// <summary>
        /// MediaPipe's expression scores (0..1 each, like ARKit's) become the avatar's: jaw open, each blink,
        /// smile minus frown, each smile corner for a smirk, brows up minus down, stretch minus pucker.
        /// "Left"/"Right" are your own left and right.
        /// </summary>
        void Expressions(ref HeadPose pose, float[] s)
        {
            pose.MouthOpen = Mathf.Clamp01(s[JawOpen] * 1.4f);
            pose.BlinkLeft = s[EyeBlinkLeft];
            pose.BlinkRight = s[EyeBlinkRight];
            float smileL = s[MouthSmileLeft], smileR = s[MouthSmileRight];
            pose.Smile = Mathf.Clamp((smileL + smileR) * 0.8f - (s[MouthFrownLeft] + s[MouthFrownRight]) * 0.7f, -1f, 1f);
            // Your right corner shows on the screen's right in mirror view.
            pose.Smirk = Mathf.Clamp((smileR - smileL) * 1.6f * (mirror ? 1f : -1f), -1f, 1f);
            pose.BrowRaise = Mathf.Clamp(s[BrowInnerUp] * 0.8f + (s[BrowOuterUpLeft] + s[BrowOuterUpRight]) * 0.3f
                                         - (s[BrowDownLeft] + s[BrowDownRight]) * 0.6f, -1f, 1f);
            pose.MouthWide = Mathf.Clamp((s[MouthStretchLeft] + s[MouthStretchRight]) * 0.75f + (smileL + smileR) * 0.25f
                                         - s[MouthPucker] * 1.3f - s[MouthFunnel] * 0.8f, -1f, 1f);
        }

        /// <summary>The nose point(s) for the nose-based turn reading, averaged.</summary>
        public static Vector2 NoseOf(Vector2[] p, FaceLayout l)
        {
            Vector2 sum = Vector2.zero;
            foreach (int i in l.Nose) sum += p[i];
            return sum / l.Nose.Length;
        }

        static bool Usable(OpenSee.OpenSee.OpenSeeData d)
        {
            if (d.points == null || d.points.Length < 68 || d.confidence == null) return false;
            float confidence = (d.confidence[30] + d.confidence[36] + d.confidence[39] + d.confidence[42] + d.confidence[45]) / 5f;
            return confidence > 0.3f;
        }

        static double UnixNow() => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

        /// <summary>
        /// The camera frame a tracker update was measured on: the tracker stamps each update with the clock
        /// time it read the frame, and the writer noted when each frame was read. -1 when nothing is close.
        /// </summary>
        int MatchFrame(double readTime)
        {
            int best = -1;
            double bestGap = double.MaxValue, gap = 0.0;
            lock (writtenLock)
            {
                int n = Math.Min(writtenCount, written.Length);
                for (int k = 0; k < n; k++)
                {
                    double g = written[k].at - readTime;
                    if (Math.Abs(g) < bestGap)
                    {
                        bestGap = Math.Abs(g);
                        best = written[k].id;
                        gap = g;
                    }
                }
            }
            syncUpdates++;
            if (bestGap > 0.05) return -1;
            syncMatched++;
            syncGapSum += gap;
            return best;
        }

        static double NewestTime(OpenSee.OpenSee.OpenSeeData[] all)
        {
            double newest = double.MinValue;
            if (all == null) return newest;
            foreach (var d in all)
                if (d != null && d.time > newest) newest = d.time;
            return newest;
        }

        void StartTracker()
        {
            StopTracker();
            bool useMediaPipe = tracker == TrackerKind.MediaPipe && mpExe != null && mpModel != null;
            if (!useMediaPipe && osfExe == null) useMediaPipe = mpExe != null && mpModel != null;
            Running = useMediaPipe ? TrackerKind.MediaPipe : TrackerKind.OpenSeeFace;
            Layout = CurrentLayout;
            Points = null;
            previousFaces.Clear();
            string exe = useMediaPipe ? mpExe : osfExe;
            string dir = Path.GetDirectoryName(exe);
            string args;
            if (useMediaPipe)
            {
                // RGB as it comes; the helper sends results to our port.
                args = $"--width {feed.trackWidth} --height {feed.trackHeight} --port {mediaPipePort} --model \"{mpModel}\" --faces {maxFaces}";
                mediaPipe.Start(mediaPipePort);
                lastMediaPipeCount = mediaPipe.Received;
            }
            else
            {
                string models = Path.GetFullPath(Path.Combine(dir, "..", "models"));
                // -F 0: no frame pacing. The tracker would otherwise sleep to pad each frame to 1/30 s, and
                // Windows oversleeps (~45 ms a frame, 22 a second); the pipe already waits for the next frame.
                args = $"--raw-rgb 1 -W {feed.trackWidth} -H {feed.trackHeight} -F 0 " +
                       $"--ip 127.0.0.1 --port {receiver.listenPort} --model {model} --model-dir \"{models}\" " +
                       // Keep tracking existing people every frame. Search for newcomers less often so
                       // multi-person detection does not compete with landmark inference every third frame.
                       $"--faces {maxFaces} --scan-every {(maxFaces > 1 ? 15 : 0)} " +
                       // Real eye cut-outs use the lid landmarks, not the separately inferred pupils.
                       // Skip the extra gaze network while retaining landmark-based blink/expression tracking.
                       $"--gaze-tracking 0 --silent 1 --max-threads {trackerThreads} --discard-after 0 --no-3d-adapt 1 --max-feature-updates 900";
            }
            swapToBgr = !useMediaPipe; // OpenSeeFace's raw input is BGR
            try
            {
                process = Process.Start(new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    WorkingDirectory = dir,
                });
                trackerStartedAt = Time.unscaledTime;
                Status = "starting the camera";
                Recentre();
                Stream stdin = process.StandardInput.BaseStream;
                writerRunning = true;
                writer = new Thread(() => WriteFrames(stdin)) { IsBackground = true, Name = "Tracker frames" };
                writer.Start();
                feed.FrameReady += OnFrame;
            }
            catch (Exception e)
            {
                Status = "could not start the tracker: " + e.Message;
                process = null;
            }
        }

        void OnFrame(byte[] frame, int id)
        {
            lock (frameLock)
            {
                pendingId = id;
                if (pendingFrame == null || pendingFrame.Length != frame.Length) pendingFrame = new byte[frame.Length];
                Buffer.BlockCopy(frame, 0, pendingFrame, 0, frame.Length);
                hasPending = true;
            }
            frameSignal.Set();
        }

        void WriteFrames(Stream stdin)
        {
            int awaitingPackets = -1;
            var sinceWrite = new Stopwatch();
            while (writerRunning)
            {
                frameSignal.WaitOne(200);
                // A pipe write can block while OpenSeeFace infers the previous frame. Selecting a frame
                // before that wait makes it old by the time it is read. Wait for the previous result
                // FIRST, then take the newest pending camera frame. The receiver runs independently of
                // Unity's render loop. No-face frames send no UDP packet, so always allow a timed retry.
                if (swapToBgr && awaitingPackets >= 0)
                    while (writerRunning && Volatile.Read(ref receiver.receivedPackets) == awaitingPackets
                           && sinceWrite.ElapsedMilliseconds < 100)
                        Thread.Sleep(1);
                if (!writerRunning) break;
                lock (frameLock)
                {
                    if (!hasPending) continue;
                    (pendingFrame, writeFrame) = (writeFrame, pendingFrame);
                    (pendingId, writeId) = (writeId, pendingId);
                    hasPending = false;
                }
                // RGB → BGR for OpenSeeFace, here off the main thread (a 1280×720 frame is 2.7 MB).
                byte[] f = writeFrame;
                if (swapToBgr)
                    for (int p = 0; p + 2 < f.Length; p += 3) (f[p], f[p + 2]) = (f[p + 2], f[p]);
                try
                {
                    awaitingPackets = Volatile.Read(ref receiver.receivedPackets);
                    stdin.Write(writeFrame, 0, writeFrame.Length);
                    stdin.Flush();
                    sinceWrite.Restart();
                    double readAt = UnixNow();
                    lock (writtenLock) written[writtenCount++ % written.Length] = (writeId, readAt);
                }
                catch (Exception)
                {
                    writerRunning = false; // the tracker exited
                }
            }
        }

        void StopTracker()
        {
            if (feed) feed.FrameReady -= OnFrame;
            writerRunning = false;
            frameSignal.Set();
            mediaPipe.Stop();
            if (process == null) return;
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (Exception)
            {
                // Already gone.
            }
            writer?.Join(500);
            writer = null;
            lock (frameLock) hasPending = false;
            process.Dispose();
            process = null;
        }

        /// <summary>A file next to the built app, or (in the editor) relative to the project folder.</summary>
        static string Resolve(string relative, string projectFallback)
        {
            if (Path.IsPathRooted(relative)) return File.Exists(relative) ? relative : null;
            string appRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (string c in new[] { Path.Combine(appRoot, relative), Path.Combine(appRoot, projectFallback) })
                if (File.Exists(c)) return Path.GetFullPath(c);
            return null;
        }
    }
}
