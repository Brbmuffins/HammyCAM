using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The phone remote: a little web page the app serves on the home network, so the streamer runs the show from
    /// their phone (or a browser on another screen) and the menu never has to be on stream. On a phone on the same
    /// Wi-Fi, open http://(this PC):8642 and type the PIN once (menu → Stream shows both). It switches avatars and
    /// emotions, fires the chaos, makes and cleans up the mess, sets the face, rooms, window shape and sound, and
    /// shows or hides what's on screen. Only devices on the local network get in, and only with the PIN (five wrong
    /// tries lock that device out for a minute); it can be switched off. The server runs on its own thread and
    /// hands everything it's asked to do to the main thread; the page polls a snapshot of the app's state.
    /// </summary>
    public sealed class RemoteControl : MonoBehaviour
    {
        public AvatarLibrary library;
        public WebcamHeadPoseSource webcam;
        public PerfHud hud;
        public DeskScene desk;
        public DeskDebris debris;
        public ChaosDirector chaos;
        public ControlPanel panel;
        [Tooltip("The first port tried; the next few are tried if it's taken.")]
        public int port = 8642;

        public static RemoteControl Instance { get; private set; }

        /// <summary>Serving the remote on the home network (remembered; the menu switches it).</summary>
        public static bool On
        {
            get => PlayerPrefs.GetInt("muffin.remote.on", 1) == 1;
            set => PlayerPrefs.SetInt("muffin.remote.on", value ? 1 : 0);
        }

        /// <summary>The 4-digit PIN a phone needs (made once and remembered; New PIN makes another).</summary>
        public string Pin { get; private set; }
        public int Port { get; private set; }
        /// <summary>What to type into the phone, e.g. http://192.168.1.23:8642 (one per network this PC is on).</summary>
        public IReadOnlyList<string> Addresses => addresses;
        public bool Running => listener != null;
        /// <summary>A phone has been in touch in the last few seconds.</summary>
        public bool Connected => DateTime.UtcNow.Ticks - Interlocked.Read(ref lastSeenTicks) < TimeSpan.TicksPerSecond * 4;
        /// <summary>A phone has connected since the app started.</summary>
        public bool EverConnected { get; private set; }
        public string Status { get; private set; } = "off";

        TcpListener listener;
        volatile bool stopping;
        volatile string state = "{}", page = "", pinCopy = "";
        long lastSeenTicks;
        bool loopbackOnly;
        float nextState, zoomUntil;
        readonly List<string> addresses = new List<string>();
        readonly ConcurrentQueue<KeyValuePair<string, string>> inbox = new ConcurrentQueue<KeyValuePair<string, string>>();
        readonly Dictionary<string, int> fails = new Dictionary<string, int>();
        readonly Dictionary<string, DateTime> lockedUntil = new Dictionary<string, DateTime>();

        void Awake() => Instance = this;

        void Start()
        {
            Pin = PlayerPrefs.GetString("muffin.remote.pin", "");
            if (Pin.Length != 4) NewPin();
            pinCopy = Pin;
            var asset = Resources.Load<TextAsset>("MuffinRemote");
            page = asset ? asset.text : "<h1>The remote page is missing from this build.</h1>";
            // The self-test checks the remote over this PC only: no firewall question for a test build.
            loopbackOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-muffin-selftest") >= 0;
            if (On) StartServer();
        }

        public void NewPin()
        {
            Pin = UnityEngine.Random.Range(0, 10000).ToString("0000");
            PlayerPrefs.SetString("muffin.remote.pin", Pin);
            pinCopy = Pin;
        }

        public void SetOn(bool on)
        {
            On = on;
            if (on) StartServer();
            else StopServer();
        }

        void StartServer()
        {
            if (listener != null) return;
            for (int p = port; p < port + 6 && listener == null; p++)
            {
                try
                {
                    var l = new TcpListener(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, p);
                    l.Start();
                    listener = l;
                    Port = p;
                }
                catch (SocketException) { }
            }
            if (listener == null)
            {
                Status = $"couldn't start: ports {port}-{port + 5} are all in use";
                return;
            }
            stopping = false;
            addresses.Clear();
            if (loopbackOnly) addresses.Add($"http://127.0.0.1:{Port}");
            else
                foreach (string ip in LocalAddresses())
                    addresses.Add($"http://{ip}:{Port}");
            if (addresses.Count == 0) addresses.Add($"http://localhost:{Port}");
            Status = "waiting for a phone";
            new Thread(Serve) { IsBackground = true, Name = "Muffin remote" }.Start(listener);
        }

        void StopServer()
        {
            stopping = true;
            try { listener?.Stop(); } catch (Exception) { }
            listener = null;
            Status = "off";
        }

        void OnDestroy() => StopServer();

        void Update()
        {
            while (inbox.TryDequeue(out var job))
            {
                try { Apply(job.Key, job.Value); }
                catch (Exception e) { Debug.LogWarning($"[Muffin] remote: '{job.Key}' failed: {e.Message}"); }
            }
            // A held zoom lets go by itself if the phone never says so (it went to sleep mid-press).
            if (zoomUntil > 0f && Time.unscaledTime > zoomUntil)
            {
                StageCamera.HoldZoom = false;
                zoomUntil = 0f;
            }
            if (!Running || Time.unscaledTime < nextState) return;
            nextState = Time.unscaledTime + 0.25f;
            state = BuildState();
            if (Connected) EverConnected = true;
            Status = Connected ? "phone connected" : EverConnected ? "phone gone quiet" : "waiting for a phone";
        }

        // ---------- what the phone can do (main thread) ----------

        void Apply(string action, string value)
        {
            bool yes = value == "1" || value == "true";
            float number = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;
            int index = Mathf.RoundToInt(number);
            switch (action)
            {
                case "avatar": if (library && index >= 0 && index < library.avatars.Count) library.Activate(index); break;
                case "emotion": if (library && Enum.TryParse(value, out Emotion emotion)) library.SetEmotion(emotion); break;
                case "face": if (library) library.SetFaceStyle(value == "real" ? FaceStyle.Real : FaceStyle.Cartoon); break;
                case "eyes": if (library) library.SetEyeSize(Mathf.Round(Mathf.Clamp(number, DigitalFace.MinSize, DigitalFace.MaxSize) * 100f) / 100f); break;
                case "mouth": if (library) library.SetMouthSize(Mathf.Round(Mathf.Clamp(number, DigitalFace.MinSize, DigitalFace.MaxSize) * 100f) / 100f); break;
                case "zoom":
                    StageCamera.HoldZoom = yes;
                    zoomUntil = yes ? Time.unscaledTime + 8f : 0f;
                    break;
                case "recentre": if (webcam) webcam.Recentre(); break;
                case "chaos": if (chaos && Enum.TryParse(value, out ChaosEvent fire)) chaos.Fire(fire, "You"); break;
                case "chaosOn":
                    if (!chaos) break;
                    chaos.chaosOn = yes;
                    chaos.Save();
                    break;
                case "shake": if (chaos) chaos.Fire(ChaosEvent.Shake); break;
                case "slam": if (chaos) chaos.Fire(ChaosEvent.Slam); break;
                case "whack": if (chaos) chaos.Fire(ChaosEvent.Whack); break;
                case "scrub": if (chaos) chaos.ScrubClean(); break;
                case "clean": if (debris) debris.Clean(); break;
                case "hoseAuto": VacuumHose.AutoClean = yes; break;
                case "hoseMouse": VacuumHose.FollowMouse = yes; break;
                case "background":
                    if (hud && Enum.TryParse(value, out BackgroundMode mode) && mode != BackgroundMode.Room) hud.SetBackground(mode);
                    break;
                case "room": if (hud && hud.backdrop && index >= 0 && index < hud.backdrop.Count) hud.SetBackground(BackgroundMode.Room, index); break;
                case "window": ControlPanel.SetWindow(value == "vertical"); break;
                case "sound": Sound.On = yes; break;
                case "volume": SoundBank.Volume = Mathf.Clamp01(number); break;
                case "menu": if (panel) panel.Open = yes; break;
                case "readout": if (hud) hud.Visible = yes; break;
                case "desk": if (desk) desk.Show(yes); break;
            }
            nextState = 0f; // the phone sees the result on its next look
        }

        /// <summary>A snapshot of everything the page shows, as JSON.</summary>
        string BuildState()
        {
            var s = new StringBuilder(2048);
            s.Append("{\"app\":\"HammyCAM\"");
            if (library)
            {
                s.Append(",\"avatars\":[");
                for (int i = 0; i < library.avatars.Count; i++) s.Append(i > 0 ? "," : "").Append(Str(library.NameOf(i)));
                s.Append("],\"avatar\":").Append(library.ActiveIndex);
                s.Append(",\"emotion\":").Append(Str(library.Emotion.ToString()));
                s.Append(",\"hasHose\":").Append(Bool(library.ActiveRig && library.ActiveRig.GetComponent<VacuumHose>()));
            }
            s.Append(",\"face\":").Append(Str(DigitalFace.Style == FaceStyle.Real ? "real" : "cartoon"));
            s.Append(",\"eyes\":").Append(Num(DigitalFace.EyeSize)).Append(",\"mouth\":").Append(Num(DigitalFace.MouthSize));
            s.Append(",\"sizeMin\":").Append(Num(DigitalFace.MinSize)).Append(",\"sizeMax\":").Append(Num(DigitalFace.MaxSize));
            if (chaos)
            {
                s.Append(",\"chaosOn\":").Append(Bool(chaos.chaosOn)).Append(",\"events\":[");
                for (int i = 0; i < chaos.Events.Length; i++)
                {
                    ChaosDirector.EventInfo e = chaos.Events[i];
                    s.Append(i > 0 ? "," : "").Append("{\"id\":").Append(Str(e.Event.ToString())).Append(",\"label\":").Append(Str(e.Label))
                        .Append(",\"enabled\":").Append(Bool(e.Enabled)).Append(",\"left\":").Append(Num(chaos.CooldownLeft(e.Event))).Append('}');
                }
                s.Append(']');
            }
            if (hud)
            {
                s.Append(",\"background\":").Append(Str(hud.Background.ToString())).Append(",\"room\":").Append(hud.Room);
                s.Append(",\"rooms\":[");
                if (hud.backdrop)
                    for (int i = 0; i < hud.backdrop.Count; i++) s.Append(i > 0 ? "," : "").Append(Str(hud.backdrop.NameOf(i)));
                s.Append("],\"readout\":").Append(Bool(hud.Visible)).Append(",\"fps\":").Append(Num(hud.Fps));
            }
            s.Append(",\"vertical\":").Append(Bool(DeskScene.Compact));
            s.Append(",\"sound\":").Append(Bool(Sound.On)).Append(",\"volume\":").Append(Num(SoundBank.Volume));
            s.Append(",\"hoseAuto\":").Append(Bool(VacuumHose.AutoClean)).Append(",\"hoseMouse\":").Append(Bool(VacuumHose.FollowMouse));
            s.Append(",\"menu\":").Append(Bool(panel && panel.Open));
            if (desk) s.Append(",\"desk\":").Append(Bool(desk.Shown));
            if (debris) s.Append(",\"sprinkles\":").Append(debris.SprinkleCount).Append(",\"bits\":").Append(debris.PieceCount);
            if (Mess.Instance) s.Append(",\"stains\":").Append(Mess.Instance.StainCount);
            s.Append('}');
            return s.ToString();
        }

        static string Bool(bool b) => b ? "true" : "false";
        static string Num(float f) => float.IsNaN(f) || float.IsInfinity(f) ? "0" : f.ToString("0.###", CultureInfo.InvariantCulture);

        static string Str(string text)
        {
            var s = new StringBuilder(text.Length + 2).Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') s.Append('\\').Append(c);
                else if (c < ' ') s.Append("\\u").Append(((int)c).ToString("x4"));
                else s.Append(c);
            }
            return s.Append('"').ToString();
        }

        // ---------- the web server (its own thread) ----------

        void Serve(object state)
        {
            var l = (TcpListener)state;
            while (!stopping)
            {
                TcpClient client;
                try { client = l.AcceptTcpClient(); }
                catch (Exception)
                {
                    if (stopping) return;
                    Thread.Sleep(100);
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = client.SendTimeout = 3000;
                    IPAddress ip = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                    NetworkStream stream = client.GetStream();
                    if (!IsLocal(ip))
                    {
                        Reply(stream, 403, "text/plain", "Only devices on this network.");
                        return;
                    }
                    if (!ReadRequest(stream, out string method, out string path, out Dictionary<string, string> headers, out string body)) return;
                    string route = path.Split('?')[0];
                    if (method == "GET" && (route == "/" || route == "/index.html"))
                    {
                        Reply(stream, 200, "text/html; charset=utf-8", page);
                        return;
                    }
                    if (!route.StartsWith("/api/"))
                    {
                        Reply(stream, 404, "text/plain", "Not here.");
                        return;
                    }
                    string who = ip.ToString();
                    if (LockedOut(who))
                    {
                        Reply(stream, 429, "application/json", "{\"error\":\"too many wrong PINs: wait a minute\"}");
                        return;
                    }
                    headers.TryGetValue("x-muffin-pin", out string pin);
                    if (pin != pinCopy)
                    {
                        Failed(who);
                        Reply(stream, 401, "application/json", "{\"error\":\"wrong PIN\"}");
                        return;
                    }
                    Succeeded(who);
                    Interlocked.Exchange(ref lastSeenTicks, DateTime.UtcNow.Ticks);
                    if (method == "GET" && route == "/api/state")
                    {
                        Reply(stream, 200, "application/json", state);
                        return;
                    }
                    if (method == "POST" && route == "/api/do")
                    {
                        Dictionary<string, string> form = ParseForm(body);
                        if (!form.TryGetValue("action", out string action) || action.Length == 0)
                        {
                            Reply(stream, 400, "application/json", "{\"error\":\"no action\"}");
                            return;
                        }
                        form.TryGetValue("value", out string value);
                        inbox.Enqueue(new KeyValuePair<string, string>(action, value ?? ""));
                        Reply(stream, 200, "application/json", "{\"ok\":true}");
                        return;
                    }
                    Reply(stream, 404, "application/json", "{\"error\":\"not here\"}");
                }
                catch (Exception) { } // a phone that went away mid-request
            }
        }

        static bool ReadRequest(NetworkStream stream, out string method, out string path, out Dictionary<string, string> headers, out string body)
        {
            method = path = body = "";
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buffer = new byte[8192];
            int length = 0, end = -1;
            while (end < 0)
            {
                if (length == buffer.Length) return false;
                int n = stream.Read(buffer, length, buffer.Length - length);
                if (n <= 0) return false;
                length += n;
                for (int i = Math.Max(0, length - n - 3); i + 3 < length; i++)
                    if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                    {
                        end = i;
                        break;
                    }
            }
            string[] lines = Encoding.ASCII.GetString(buffer, 0, end).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return false;
            method = first[0];
            path = first[1];
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            int want = 0;
            if (headers.TryGetValue("content-length", out string declared) && !int.TryParse(declared, out want)) return false;
            if (want < 0 || want > 4096) return false;
            var bytes = new byte[want];
            int start = end + 4, have = Math.Min(want, length - start);
            Array.Copy(buffer, start, bytes, 0, have);
            while (have < want)
            {
                int n = stream.Read(bytes, have, want - have);
                if (n <= 0) return false;
                have += n;
            }
            body = Encoding.UTF8.GetString(bytes);
            return true;
        }

        static void Reply(NetworkStream stream, int code, string type, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            string reason = code switch
            {
                200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 429 => "Too Many Requests", _ => "Error",
            };
            byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                                                  "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
        }

        static Dictionary<string, string> ParseForm(string body)
        {
            var form = new Dictionary<string, string>();
            foreach (string pair in body.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) form[Unescape(pair.Substring(0, eq))] = Unescape(pair.Substring(eq + 1));
            }
            return form;
        }

        static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        bool LockedOut(string who)
        {
            lock (fails) return lockedUntil.TryGetValue(who, out DateTime until) && DateTime.UtcNow < until;
        }

        void Failed(string who)
        {
            lock (fails)
            {
                fails.TryGetValue(who, out int n);
                if (++n >= 5)
                {
                    lockedUntil[who] = DateTime.UtcNow.AddMinutes(1);
                    n = 0;
                }
                fails[who] = n;
            }
        }

        void Succeeded(string who)
        {
            lock (fails) fails.Remove(who);
        }

        /// <summary>Loopback, or a private home/office network address (no one from the internet).</summary>
        static bool IsLocal(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        /// <summary>This PC's addresses on its real networks (not virtual adapters, VPN bridges or Bluetooth).</summary>
        static List<string> LocalAddresses()
        {
            var found = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    string name = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    if (name.Contains("virtual") || name.Contains("vethernet") || name.Contains("hyper-v") || name.Contains("vmware") ||
                        name.Contains("vbox") || name.Contains("wsl") || name.Contains("bluetooth") || name.Contains("tap-")) continue;
                    foreach (UnicastIPAddressInformation address in ni.GetIPProperties().UnicastAddresses)
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork && IsLocal(address.Address) && !IPAddress.IsLoopback(address.Address))
                            found.Add(address.Address.ToString());
                }
            }
            catch (Exception) { } // no network information: the menu falls back to localhost
            return found;
        }
    }
}
