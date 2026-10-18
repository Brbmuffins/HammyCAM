using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// Receives the MediaPipe helper's results (tracker/muffin_facetracker.py) on a background thread and keeps
    /// the newest. One packet per camera frame: 'MPF1', the frame's read time, its size, then per face 478
    /// points (fractions of the image, with depth), 52 blendshape scores and the 4x4 transformation matrix.
    /// </summary>
    public sealed class MediaPipeReceiver : IDisposable
    {
        public const int Landmarks = 478, Blendshapes = 52;
        const int HeaderBytes = 24, FaceBytes = Landmarks * 12 + Blendshapes * 4 + 64;

        public sealed class Face
        {
            /// <summary>x, y as fractions of the image (y down), z depth (MediaPipe's units).</summary>
            public readonly Vector3[] Points = new Vector3[Landmarks];
            public readonly float[] Scores = new float[Blendshapes];
            public Matrix4x4 Matrix;
        }

        public sealed class Frame
        {
            /// <summary>When the helper read the frame (Unix seconds, the PC's clock).</summary>
            public double Time;
            public int Width, Height;
            public Face[] Faces;
        }

        readonly object gate = new object();
        Frame latest;
        Socket socket;
        Thread thread;
        volatile bool running;
        int received;

        /// <summary>Packets received so far (a frame each, faces or not).</summary>
        public int Received => Volatile.Read(ref received);

        public Frame Latest
        {
            get
            {
                lock (gate) return latest;
            }
        }

        public void Start(int port)
        {
            Stop();
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveTimeout = 250 };
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            running = true;
            thread = new Thread(Receive) { IsBackground = true, Name = "MediaPipe results" };
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            try
            {
                socket?.Close();
            }
            catch (Exception)
            {
                // Already closed.
            }
            thread?.Join(500);
            thread = null;
            socket = null;
            lock (gate) latest = null;
        }

        public void Dispose() => Stop();

        void Receive()
        {
            var buffer = new byte[65536];
            while (running)
            {
                int n;
                try
                {
                    n = socket.Receive(buffer);
                }
                catch (SocketException)
                {
                    continue; // timeout (lets Stop end the thread) or a hiccup
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                Frame frame = Parse(buffer, n);
                if (frame == null) continue;
                lock (gate) latest = frame;
                Interlocked.Increment(ref received);
            }
        }

        static Frame Parse(byte[] b, int n)
        {
            if (n < HeaderBytes || b[0] != (byte)'M' || b[1] != (byte)'P' || b[2] != (byte)'F' || b[3] != (byte)'1') return null;
            int count = BitConverter.ToInt32(b, 20);
            if (count < 0 || n < HeaderBytes + count * FaceBytes) return null;
            var frame = new Frame
            {
                Time = BitConverter.ToDouble(b, 4),
                Width = BitConverter.ToInt32(b, 12),
                Height = BitConverter.ToInt32(b, 16),
                Faces = new Face[count],
            };
            int o = HeaderBytes;
            for (int f = 0; f < count; f++)
            {
                var face = new Face();
                for (int i = 0; i < Landmarks; i++, o += 12)
                    face.Points[i] = new Vector3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));
                for (int i = 0; i < Blendshapes; i++, o += 4) face.Scores[i] = BitConverter.ToSingle(b, o);
                var m = new Matrix4x4();
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++, o += 4)
                        m[r, c] = BitConverter.ToSingle(b, o); // sent row by row
                face.Matrix = m;
                frame.Faces[f] = face;
            }
            return frame;
        }
    }
}
