using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// The link to the streamer's own relay on their VPS (docs/vps-relay.md): it brings TikTok LIVE in (comments and
    /// gifts use a dedicated paid-event path; likes and follows use the same cooldowns as Twitch chat) and
    /// carries the relay's phone dashboard: its buttons arrive as remote actions (the same ones the home-Wi-Fi phone
    /// remote uses) and the app's state goes back so the dashboard can show it. The app connects OUT to the relay
    /// (wss, with the relay's token), so nothing needs opening on the streamer's network. Set up in the menu
    /// (Chaos → Relay). Network work runs on a background task; everything it asks for happens on the main thread.
    /// </summary>
    public sealed class RelayClient : MonoBehaviour
    {
        public ChaosDirector chaos;
        public RemoteControl remote;

        public static RelayClient Instance { get; private set; }

        /// <summary>wss://... of the relay (the menu's box).</summary>
        public static string Url
        {
            get => PlayerPrefs.GetString("muffin.relay.url", "");
            set => PlayerPrefs.SetString("muffin.relay.url", value.Trim());
        }

        /// <summary>The relay's app token. Kept on this PC only; never shown or logged.</summary>
        public static string Token
        {
            get => PlayerPrefs.GetString("muffin.relay.token", "");
            set => PlayerPrefs.SetString("muffin.relay.token", value.Trim());
        }

        public static bool On
        {
            get => PlayerPrefs.GetInt("muffin.relay.on", 0) == 1;
            set => PlayerPrefs.SetInt("muffin.relay.on", value ? 1 : 0);
        }

        public string Status { get; private set; } = "off";
        public bool Connected { get; private set; }
        /// <summary>What the relay last said about TikTok: "live (123 watching)", "not live", ...</summary>
        public string TikTok { get; private set; } = "";

        [Serializable]
        sealed class Message
        {
            public string type, platform, user, text, command, reason, action, value, room;
            public bool connected, live;
            public int viewers;
            public int count = 1;
        }

        [Serializable]
        sealed class Hello
        {
            public string type = "hello", token, app = "HammyCAM", version;
        }

        readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
        CancellationTokenSource cancel;
        Task loop;
        string lastState;
        float nextState;

        void Awake() => Instance = this;

        void Start()
        {
            if (On) Connect();
        }

        void OnDestroy() => Disconnect();

        /// <summary>(Re)connects with the saved address and token, and keeps reconnecting until switched off.</summary>
        public void Connect()
        {
            Disconnect();
            string url = Url, token = Token;
            if (!url.StartsWith("wss://") && !url.StartsWith("ws://"))
            {
                Status = "needs the relay's address (wss://...)";
                return;
            }
            if (token.Length < 16)
            {
                Status = "needs the relay's token";
                return;
            }
            cancel = new CancellationTokenSource();
            string version = Application.version;
            loop = Task.Run(() => Run(new Uri(url), token, version, cancel.Token));
            Status = "connecting…";
        }

        public void Disconnect()
        {
            cancel?.Cancel();
            cancel = null;
            Connected = false;
            Status = "off";
            TikTok = "";
        }

        async Task Run(Uri uri, string token, string version, CancellationToken stop)
        {
            int wait = 2;
            while (!stop.IsCancellationRequested)
            {
                using (var socket = new ClientWebSocket())
                {
                    try
                    {
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                        await socket.ConnectAsync(uri, stop);
                        await Send(socket, JsonUtility.ToJson(new Hello { token = token, version = version }), stop);
                        inbox.Enqueue("{\"type\":\"_open\"}");
                        wait = 2;
                        var buffer = new byte[16 * 1024];
                        var text = new StringBuilder();
                        Task<WebSocketReceiveResult> receive = socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop);
                        while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
                        {
                            // Send whatever the main thread queued, then wait a little for the next message.
                            while (outbox.TryDequeue(out string message)) await Send(socket, message, stop);
                            Task done = await Task.WhenAny(receive, Task.Delay(100, stop));
                            if (done != receive) continue;
                            WebSocketReceiveResult r = await receive;
                            if (r.MessageType == WebSocketMessageType.Close)
                            {
                                inbox.Enqueue(r.CloseStatus == (WebSocketCloseStatus)4001
                                    ? "{\"type\":\"_closed\",\"reason\":\"the relay refused the token\"}"
                                    : "{\"type\":\"_closed\",\"reason\":\"the relay closed the connection\"}");
                                break;
                            }
                            text.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                            if (r.EndOfMessage)
                            {
                                if (text.Length < 64 * 1024) inbox.Enqueue(text.ToString());
                                text.Clear();
                            }
                            receive = socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception e)
                    {
                        // Only the kind of failure, never the address or the token.
                        inbox.Enqueue("{\"type\":\"_closed\",\"reason\":\"can't reach the relay (" + e.GetType().Name + ")\"}");
                    }
                }
                if (stop.IsCancellationRequested) return;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(wait), stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                wait = Math.Min(30, wait * 2);
            }
        }

        static Task Send(ClientWebSocket socket, string text, CancellationToken stop) =>
            socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, stop);

        void Update()
        {
            for (int guard = 0; guard < 50 && inbox.TryDequeue(out string json); guard++) Handle(json);
            // The app's state, for the dashboard: on connect, then when it changes (at most every 2 s).
            if (Connected && remote && Time.unscaledTime >= nextState)
            {
                nextState = Time.unscaledTime + 2f;
                string state = remote.BuildState();
                if (state != lastState)
                {
                    lastState = state;
                    outbox.Enqueue("{\"type\":\"state\",\"state\":" + state + "}");
                }
            }
        }

        void Handle(string json)
        {
            Message m;
            try
            {
                m = JsonUtility.FromJson<Message>(json);
            }
            catch (Exception)
            {
                return;
            }
            if (m == null || string.IsNullOrEmpty(m.type)) return;
            switch (m.type)
            {
                case "_open":
                    Connected = true;
                    Status = "connected";
                    lastState = null;
                    nextState = 0f;
                    break;
                case "_closed":
                    Connected = false;
                    Status = (m.reason ?? "disconnected") + ", trying again…";
                    break;
                case "welcome":
                    Status = "connected";
                    break;
                case "ping":
                    outbox.Enqueue("{\"type\":\"pong\"}");
                    break;
                case "status":
                    TikTok = !m.connected ? "not connected to TikTok" : m.live ? $"live ({m.viewers} watching)" : "not live";
                    break;
                case "chat":
                    if (chaos && !string.IsNullOrEmpty(m.text)) chaos.HandleChat(Who(m), m.text);
                    break;
                case "command":
                    if (chaos && !string.IsNullOrEmpty(m.command))
                    {
                        if ((m.reason ?? "").StartsWith("gift:", StringComparison.OrdinalIgnoreCase)) chaos.HandleGift(Who(m), m.command, m.count);
                        else chaos.HandleChat(Who(m), m.command);
                    }
                    break;
                case "gift":
                    if (chaos) chaos.HandleGift(Who(m), m.command, m.count);
                    break;
                case "remote":
                    if (remote && !string.IsNullOrEmpty(m.action)) remote.Apply(m.action, m.value ?? "");
                    break;
            }
        }

        /// <summary>The viewer's name for the caption, short and clean.</summary>
        static string Who(Message m)
        {
            string name = string.IsNullOrEmpty(m.user) ? "Someone" : m.user;
            var s = new StringBuilder(name.Length);
            foreach (char c in name) if (!char.IsControl(c)) s.Append(c);
            name = s.ToString().Trim();
            if (name.Length > 24) name = name.Substring(0, 24);
            return name.Length == 0 ? "Someone" : name;
        }
    }
}
