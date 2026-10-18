using System.Runtime.InteropServices;
using UnityEngine;

namespace Muffin
{
    /// <summary>
    /// What the streamer is doing at their real desk, read globally so it works while a game has focus.
    ///
    /// Privacy by design: it only counts key presses and never records, stores or exposes which key was
    /// pressed. Anti-cheat friendly: it polls the keyboard and cursor state each frame (GetAsyncKeyState,
    /// GetCursorPos) instead of installing system-wide keyboard or mouse hooks.
    ///
    /// Limits of polling: games that lock and hide the cursor (most shooters) don't move it, so mouse
    /// motion isn't seen there (clicks still are); a game running as administrator hides its input from
    /// a non-administrator app. Raw Input can lift the first limit later.
    /// </summary>
    public sealed class DeskInput : MonoBehaviour
    {
        public static DeskInput Instance { get; private set; }

        [Tooltip("Pretend to type and move the mouse (demo, self-test).")]
        public bool simulate;

        /// <summary>New key presses this frame (count only).</summary>
        public int KeyPresses { get; private set; }
        /// <summary>Cursor movement this frame, in screen pixels (x right, y up).</summary>
        public Vector2 MouseDelta { get; private set; }
        public bool LeftButton { get; private set; }
        public bool RightButton { get; private set; }
        public bool Clicked { get; private set; }
        /// <summary>Seconds since the mouse last moved or clicked / a key was last pressed.</summary>
        public float SinceMouse => Time.time - lastMouse;
        public float SinceKey => Time.time - lastKey;
        /// <summary>Recent typing speed, key presses per second.</summary>
        public float KeysPerSecond { get; private set; }
        /// <summary>Where the cursor is across all the screens, 0..1 (x right, y up): the vacuum's nozzle follows it.</summary>
        public Vector2 Cursor01 { get; private set; } = new Vector2(0.5f, 0.5f);

        readonly bool[] wasDown = new bool[256];
        Vector2Int lastCursor;
        bool haveCursor;
        float lastMouse = -10f, lastKey = -10f;
        float nextFakeKey;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            KeyPresses = 0;
            MouseDelta = Vector2.zero;
            Clicked = false;
            if (simulate) Simulate();
            else Poll();

            if (KeyPresses > 0) lastKey = Time.time;
            if (MouseDelta.sqrMagnitude > 0.5f || Clicked) lastMouse = Time.time;
            float rate = KeyPresses / Mathf.Max(Time.deltaTime, 1e-4f);
            KeysPerSecond = Mathf.Lerp(KeysPerSecond, rate, 1f - Mathf.Exp(-Time.deltaTime * 2f));
        }

        void Poll()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            for (int vk = 0x08; vk <= 0xFE; vk++)
            {
                // Skip mouse buttons (read below) and the left/right duplicates of Shift, Ctrl and Alt,
                // which Windows also reports as the generic keys.
                if (vk >= 0xA0 && vk <= 0xA5) continue;
                bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                if (down && !wasDown[vk]) KeyPresses++;
                wasDown[vk] = down;
            }
            bool left = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            bool right = (GetAsyncKeyState(0x02) & 0x8000) != 0;
            Clicked = (left && !LeftButton) || (right && !RightButton);
            LeftButton = left;
            RightButton = right;
            if (GetCursorPos(out Point p))
            {
                var cursor = new Vector2Int(p.X, p.Y);
                if (haveCursor) MouseDelta = new Vector2(cursor.x - lastCursor.x, lastCursor.y - cursor.y);
                lastCursor = cursor;
                haveCursor = true;
                // The whole desktop (all monitors), so the cursor anywhere on any screen counts.
                int screenX = GetSystemMetrics(76), screenY = GetSystemMetrics(77), width = GetSystemMetrics(78), height = GetSystemMetrics(79);
                if (width > 0 && height > 0)
                    Cursor01 = new Vector2(Mathf.Clamp01((float)(p.X - screenX) / width), 1f - Mathf.Clamp01((float)(p.Y - screenY) / height));
            }
#endif
        }

        void Simulate()
        {
            // Bursts of typing, a slow circling mouse and the odd click.
            float t = Time.time;
            bool typing = Mathf.Repeat(t, 6f) < 3.5f;
            if (typing && t >= nextFakeKey)
            {
                KeyPresses = 1;
                nextFakeKey = t + Random.Range(0.08f, 0.22f);
            }
            if (!typing) MouseDelta = new Vector2(Mathf.Cos(t * 2.3f), Mathf.Sin(t * 1.7f)) * 14f;
            Cursor01 = new Vector2(0.5f + 0.38f * Mathf.Sin(t * 0.7f), 0.45f + 0.3f * Mathf.Sin(t * 1.1f + 1f));
            bool click = !typing && Mathf.Repeat(t, 1.3f) < Time.deltaTime;
            Clicked = click;
            LeftButton = !typing && Mathf.Repeat(t, 1.3f) < 0.12f;
            RightButton = false;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        struct Point { public int X, Y; }

        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
#endif
    }
}
