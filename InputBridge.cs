using UnityEngine;
using UnityEngine.InputSystem;

namespace ForeverGolfCart
{
    // The game ships with Unity's "Active Input Handling" set to InputSystem-only, which makes
    // the legacy UnityEngine.Input class throw InvalidOperationException at every call. This
    // bridge takes the BepInEx config-friendly KeyCode and resolves it through Keyboard.current
    // / Mouse.current instead.
    internal static class InputBridge
    {
        internal static bool WasPressedThisFrame(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.Mouse0: return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
                case KeyCode.Mouse1: return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
                case KeyCode.Mouse2: return Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame;
                case KeyCode.Mouse3: return Mouse.current != null && Mouse.current.backButton.wasPressedThisFrame;
                case KeyCode.Mouse4: return Mouse.current != null && Mouse.current.forwardButton.wasPressedThisFrame;
            }
            Key key = TranslateKey(keyCode);
            if (key == Key.None || Keyboard.current == null) return false;
            return Keyboard.current[key].wasPressedThisFrame;
        }

        internal static bool IsHeld(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.Mouse0: return Mouse.current != null && Mouse.current.leftButton.isPressed;
                case KeyCode.Mouse1: return Mouse.current != null && Mouse.current.rightButton.isPressed;
                case KeyCode.Mouse2: return Mouse.current != null && Mouse.current.middleButton.isPressed;
                case KeyCode.Mouse3: return Mouse.current != null && Mouse.current.backButton.isPressed;
                case KeyCode.Mouse4: return Mouse.current != null && Mouse.current.forwardButton.isPressed;
            }
            Key key = TranslateKey(keyCode);
            if (key == Key.None || Keyboard.current == null) return false;
            return Keyboard.current[key].isPressed;
        }

        // KeyCode and Key enums don't share a numeric layout, so map case-by-case for the
        // values we actually expect users to bind. Anything outside this set returns Key.None
        // and silently no-ops; the config docs steer users to the supported values.
        private static Key TranslateKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.A: return Key.A;
                case KeyCode.B: return Key.B;
                case KeyCode.C: return Key.C;
                case KeyCode.D: return Key.D;
                case KeyCode.E: return Key.E;
                case KeyCode.F: return Key.F;
                case KeyCode.G: return Key.G;
                case KeyCode.H: return Key.H;
                case KeyCode.I: return Key.I;
                case KeyCode.J: return Key.J;
                case KeyCode.K: return Key.K;
                case KeyCode.L: return Key.L;
                case KeyCode.M: return Key.M;
                case KeyCode.N: return Key.N;
                case KeyCode.O: return Key.O;
                case KeyCode.P: return Key.P;
                case KeyCode.Q: return Key.Q;
                case KeyCode.R: return Key.R;
                case KeyCode.S: return Key.S;
                case KeyCode.T: return Key.T;
                case KeyCode.U: return Key.U;
                case KeyCode.V: return Key.V;
                case KeyCode.W: return Key.W;
                case KeyCode.X: return Key.X;
                case KeyCode.Y: return Key.Y;
                case KeyCode.Z: return Key.Z;
                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Alpha1: return Key.Digit1;
                case KeyCode.Alpha2: return Key.Digit2;
                case KeyCode.Alpha3: return Key.Digit3;
                case KeyCode.Alpha4: return Key.Digit4;
                case KeyCode.Alpha5: return Key.Digit5;
                case KeyCode.Alpha6: return Key.Digit6;
                case KeyCode.Alpha7: return Key.Digit7;
                case KeyCode.Alpha8: return Key.Digit8;
                case KeyCode.Alpha9: return Key.Digit9;
                case KeyCode.F1: return Key.F1;
                case KeyCode.F2: return Key.F2;
                case KeyCode.F3: return Key.F3;
                case KeyCode.F4: return Key.F4;
                case KeyCode.F5: return Key.F5;
                case KeyCode.F6: return Key.F6;
                case KeyCode.F7: return Key.F7;
                case KeyCode.F8: return Key.F8;
                case KeyCode.F9: return Key.F9;
                case KeyCode.F10: return Key.F10;
                case KeyCode.F11: return Key.F11;
                case KeyCode.F12: return Key.F12;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Delete: return Key.Delete;
                default: return Key.None;
            }
        }
    }
}
