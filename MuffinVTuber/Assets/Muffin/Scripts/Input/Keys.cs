using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Muffin
{
    /// <summary>The keys the app uses, read through whichever input system the project has enabled.</summary>
    public enum MuffinKey { F5, F6, F7, F9, Space, B, C, D, E, F, G, H, J, K, L, M, N, O, P, R, S, T, V, W, X, Z, Up, Down, Minus, Plus, LeftBracket, RightBracket, F1, F2, F3, F4, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8 }

    public static class Keys
    {
        /// <summary>While you type in a text box (the chaos panel's channel name), hotkeys stay quiet.</summary>
        public static bool Suppressed { get; set; }

        public static bool Held(MuffinKey key)
        {
            if (Suppressed) return false;
#if ENABLE_INPUT_SYSTEM
            return Control(key)?.isPressed ?? false;
#else
            return Input.GetKey(Legacy(key));
#endif
        }

        public static bool Pressed(MuffinKey key)
        {
            if (Suppressed) return false;
#if ENABLE_INPUT_SYSTEM
            return Control(key)?.wasPressedThisFrame ?? false;
#else
            return Input.GetKeyDown(Legacy(key));
#endif
        }

        /// <summary>Mouse position from the screen centre, -1..1 on each axis.</summary>
        public static Vector2 MouseFromCentre()
        {
#if ENABLE_INPUT_SYSTEM
            Vector2 px = Mouse.current?.position.ReadValue() ?? new Vector2(Screen.width / 2f, Screen.height / 2f);
#else
            Vector2 px = Input.mousePosition;
#endif
            return new Vector2(
                Mathf.Clamp(px.x / Mathf.Max(1, Screen.width) * 2f - 1f, -1f, 1f),
                Mathf.Clamp(px.y / Mathf.Max(1, Screen.height) * 2f - 1f, -1f, 1f));
        }

#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.KeyControl Control(MuffinKey key)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return null;
            return key switch
            {
                MuffinKey.Space => keyboard.spaceKey,
                MuffinKey.B => keyboard.bKey,
                MuffinKey.C => keyboard.cKey,
                MuffinKey.D => keyboard.dKey,
                MuffinKey.F => keyboard.fKey,
                MuffinKey.G => keyboard.gKey,
                MuffinKey.H => keyboard.hKey,
                MuffinKey.J => keyboard.jKey,
                MuffinKey.M => keyboard.mKey,
                MuffinKey.N => keyboard.nKey,
                MuffinKey.P => keyboard.pKey,
                MuffinKey.T => keyboard.tKey,
                MuffinKey.E => keyboard.eKey,
                MuffinKey.L => keyboard.lKey,
                MuffinKey.F5 => keyboard.f5Key,
                MuffinKey.F6 => keyboard.f6Key,
                MuffinKey.F7 => keyboard.f7Key,
                MuffinKey.K => keyboard.kKey,
                MuffinKey.O => keyboard.oKey,
                MuffinKey.S => keyboard.sKey,
                MuffinKey.X => keyboard.xKey,
                MuffinKey.Z => keyboard.zKey,
                MuffinKey.V => keyboard.vKey,
                MuffinKey.Up => keyboard.upArrowKey,
                MuffinKey.Down => keyboard.downArrowKey,
                MuffinKey.Minus => keyboard.minusKey,
                MuffinKey.Plus => keyboard.equalsKey,
                MuffinKey.LeftBracket => keyboard.leftBracketKey,
                MuffinKey.RightBracket => keyboard.rightBracketKey,
                MuffinKey.W => keyboard.wKey,
                MuffinKey.F1 => keyboard.f1Key,
                MuffinKey.F2 => keyboard.f2Key,
                MuffinKey.F3 => keyboard.f3Key,
                MuffinKey.F4 => keyboard.f4Key,
                MuffinKey.F9 => keyboard.f9Key,
                MuffinKey.Digit1 => keyboard.digit1Key,
                MuffinKey.Digit2 => keyboard.digit2Key,
                MuffinKey.Digit3 => keyboard.digit3Key,
                MuffinKey.Digit4 => keyboard.digit4Key,
                MuffinKey.Digit5 => keyboard.digit5Key,
                MuffinKey.Digit6 => keyboard.digit6Key,
                MuffinKey.Digit7 => keyboard.digit7Key,
                MuffinKey.Digit8 => keyboard.digit8Key,
                _ => keyboard.rKey,
            };
        }
#else
        static KeyCode Legacy(MuffinKey key) => key switch
        {
            MuffinKey.Space => KeyCode.Space,
            MuffinKey.B => KeyCode.B,
            MuffinKey.C => KeyCode.C,
            MuffinKey.D => KeyCode.D,
            MuffinKey.F => KeyCode.F,
            MuffinKey.G => KeyCode.G,
            MuffinKey.H => KeyCode.H,
            MuffinKey.J => KeyCode.J,
            MuffinKey.M => KeyCode.M,
            MuffinKey.N => KeyCode.N,
            MuffinKey.P => KeyCode.P,
            MuffinKey.T => KeyCode.T,
            MuffinKey.E => KeyCode.E,
            MuffinKey.L => KeyCode.L,
            MuffinKey.F5 => KeyCode.F5,
            MuffinKey.F6 => KeyCode.F6,
            MuffinKey.F7 => KeyCode.F7,
            MuffinKey.K => KeyCode.K,
            MuffinKey.O => KeyCode.O,
            MuffinKey.S => KeyCode.S,
            MuffinKey.X => KeyCode.X,
            MuffinKey.Z => KeyCode.Z,
            MuffinKey.V => KeyCode.V,
            MuffinKey.Up => KeyCode.UpArrow,
            MuffinKey.Down => KeyCode.DownArrow,
            MuffinKey.Minus => KeyCode.Minus,
            MuffinKey.Plus => KeyCode.Equals,
            MuffinKey.LeftBracket => KeyCode.LeftBracket,
            MuffinKey.RightBracket => KeyCode.RightBracket,
            MuffinKey.W => KeyCode.W,
            MuffinKey.F1 => KeyCode.F1,
            MuffinKey.F2 => KeyCode.F2,
            MuffinKey.F3 => KeyCode.F3,
            MuffinKey.F4 => KeyCode.F4,
            MuffinKey.F9 => KeyCode.F9,
            MuffinKey.Digit1 => KeyCode.Alpha1,
            MuffinKey.Digit2 => KeyCode.Alpha2,
            MuffinKey.Digit3 => KeyCode.Alpha3,
            MuffinKey.Digit4 => KeyCode.Alpha4,
            MuffinKey.Digit5 => KeyCode.Alpha5,
            MuffinKey.Digit6 => KeyCode.Alpha6,
            MuffinKey.Digit7 => KeyCode.Alpha7,
            MuffinKey.Digit8 => KeyCode.Alpha8,
            _ => KeyCode.R,
        };
#endif
    }
}
