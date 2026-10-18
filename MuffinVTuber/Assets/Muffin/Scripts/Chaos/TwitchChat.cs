using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;

namespace Muffin
{
    /// <summary>
    /// Reads a Twitch channel's chat without logging in: Twitch lets anyone read public chat anonymously
    /// (as "justinfan" plus a number) over its IRC interface, with no account or token. Read-only: it can't
    /// post. Messages arrive on a background thread and are queued for the main thread. Reconnects on its
    /// own (waiting longer each time) if the connection drops.
    /// </summary>
    public sealed class TwitchChat : IDisposable
    {
        const string Host = "irc.chat.twitch.tv";
        const int Port = 6697; // TLS

        public string Channel { get; private set; } = "";
        /// <summary>Human-readable connection state, for the chaos panel.</summary>
        public string Status { get; private set; } = "not connected";
        public bool Connected { get; private set; }

        readonly ConcurrentQueue<(string user, string text)> messages = new ConcurrentQueue<(string, string)>();
        Thread thread;
        TcpClient client;
        volatile bool running;

        public void Connect(string channel)
        {
            Disconnect();
            Channel = (channel ?? "").Trim().TrimStart('#').ToLowerInvariant();
            if (Channel.Length == 0)
            {
                Status = "type your channel name";
                return;
            }
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "Twitch chat" };
            thread.Start();
        }

        public void Disconnect()
        {
            running = false;
            try
            {
                client?.Close();
            }
            catch (Exception)
            {
                // Already closed.
            }
            thread?.Join(1000);
            thread = null;
            Connected = false;
            Status = "not connected";
        }

        public void Dispose() => Disconnect();

        public bool TryNext(out string user, out string text)
        {
            if (messages.TryDequeue(out var m))
            {
                user = m.user;
                text = m.text;
                return true;
            }
            user = text = null;
            return false;
        }

        void Run()
        {
            int failures = 0;
            var random = new Random();
            while (running)
            {
                try
                {
                    Status = $"connecting to #{Channel}…";
                    using (client = new TcpClient())
                    {
                        client.Connect(Host, Port);
                        using (var tls = new SslStream(client.GetStream()))
                        {
                            tls.AuthenticateAsClient(Host);
                            var writer = new StreamWriter(tls) { NewLine = "\r\n", AutoFlush = true };
                            var reader = new StreamReader(tls);
                            // Tags give display names; the anonymous login needs no password.
                            writer.WriteLine("CAP REQ :twitch.tv/tags");
                            writer.WriteLine("PASS SCHMOOPIIE");
                            writer.WriteLine($"NICK justinfan{random.Next(10000, 99999)}");
                            writer.WriteLine($"JOIN #{Channel}");
                            string line;
                            while (running && (line = reader.ReadLine()) != null)
                            {
                                if (line.StartsWith("PING", StringComparison.Ordinal))
                                {
                                    writer.WriteLine("PONG" + line.Substring(4));
                                    continue;
                                }
                                if (line.Contains($" 366 ") || line.Contains($"JOIN #{Channel}"))
                                {
                                    Connected = true;
                                    failures = 0;
                                    Status = $"reading #{Channel}'s chat";
                                }
                                if (Parse(line, out string user, out string text)) messages.Enqueue((user, text));
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    if (running) Status = $"lost the connection ({e.GetType().Name}); retrying";
                }
                Connected = false;
                if (!running) break;
                // Back off: 2, 4, 8 … 30 s.
                int wait = Math.Min(30, 2 << Math.Min(failures++, 4));
                for (int i = 0; i < wait * 10 && running; i++) Thread.Sleep(100);
            }
        }

        /// <summary>
        /// A chat line from Twitch's IRC: "@tags :nick!nick@nick.tmi.twitch.tv PRIVMSG #channel :message".
        /// Gives the sender (display name when the tags carry one) and the message.
        /// </summary>
        public static bool Parse(string line, out string user, out string text)
        {
            user = text = null;
            if (string.IsNullOrEmpty(line)) return false;
            string tags = "";
            string rest = line;
            if (rest.StartsWith("@", StringComparison.Ordinal))
            {
                int space = rest.IndexOf(' ');
                if (space < 0) return false;
                tags = rest.Substring(1, space - 1);
                rest = rest.Substring(space + 1);
            }
            if (!rest.StartsWith(":", StringComparison.Ordinal)) return false;
            int privmsg = rest.IndexOf(" PRIVMSG #", StringComparison.Ordinal);
            if (privmsg < 0) return false;
            int bang = rest.IndexOf('!');
            string nick = bang > 1 && bang < privmsg ? rest.Substring(1, bang - 1) : rest.Substring(1, privmsg - 1);
            int colon = rest.IndexOf(" :", privmsg, StringComparison.Ordinal);
            if (colon < 0) return false;
            text = rest.Substring(colon + 2);
            user = nick;
            foreach (string tag in tags.Split(';'))
                if (tag.StartsWith("display-name=", StringComparison.Ordinal) && tag.Length > 13)
                    user = tag.Substring(13).Replace("\\s", " ");
            return true;
        }
    }
}
