using UnityEngine;
#if LOOKSMITH_DEMO_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PaleFernStudio.Looksmith.Demo
{
    /// <summary>The keys the demo uses.</summary>
    public enum DemoKey
    {
        Forward, Back, Left, Right, Run, Jump, Up, Down,
        PrevLook, NextLook, PrevSky, NextSky, Bypass, Split, DebugView, Panel, Fly, Release,
        TimeOfDay, HourBack, HourForward, Mood, Wind, Atmosphere, Grass,
        MoodA, MoodB, Tune, TuneUp, TuneDown, TuneLess, TuneMore, TuneApply, TuneReset, Quality,
    }

    /// <summary>
    /// Keyboard and mouse for the demo, through the Input System when the project uses it and the old Input Manager
    /// otherwise, so the scene plays in any project without setup.
    /// </summary>
    public static class DemoInput
    {
#if LOOKSMITH_DEMO_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        static Key ToKey(DemoKey k)
        {
            switch (k)
            {
                case DemoKey.Forward: return Key.W;
                case DemoKey.Back: return Key.S;
                case DemoKey.Left: return Key.A;
                case DemoKey.Right: return Key.D;
                case DemoKey.Run: return Key.LeftShift;
                case DemoKey.Jump: return Key.Space;
                case DemoKey.Up: return Key.Space;
                case DemoKey.Down: return Key.LeftCtrl;
                case DemoKey.PrevLook: return Key.Q;
                case DemoKey.NextLook: return Key.E;
                case DemoKey.PrevSky: return Key.Z;
                case DemoKey.NextSky: return Key.C;
                case DemoKey.Bypass: return Key.B;
                case DemoKey.Split: return Key.V;
                case DemoKey.DebugView: return Key.G;
                case DemoKey.Panel: return Key.Tab;
                case DemoKey.Fly: return Key.F;
                case DemoKey.Release: return Key.Escape;
                case DemoKey.TimeOfDay: return Key.T;
                case DemoKey.HourBack: return Key.LeftBracket;
                case DemoKey.HourForward: return Key.RightBracket;
                case DemoKey.Mood: return Key.M;
                case DemoKey.Wind: return Key.R;
                case DemoKey.Atmosphere: return Key.U;
                case DemoKey.Grass: return Key.H;
                case DemoKey.MoodA: return Key.Digit1;
                case DemoKey.MoodB: return Key.Digit2;
                case DemoKey.Tune: return Key.Y;
                case DemoKey.TuneUp: return Key.UpArrow;
                case DemoKey.TuneDown: return Key.DownArrow;
                case DemoKey.TuneLess: return Key.LeftArrow;
                case DemoKey.TuneMore: return Key.RightArrow;
                case DemoKey.TuneApply: return Key.Enter;
                case DemoKey.TuneReset: return Key.Backspace;
                case DemoKey.Quality: return Key.P;
            }
            return Key.None;
        }

        public static bool Held(DemoKey k) => !LooksmithTweaksHooks.typing && Keyboard.current != null && Keyboard.current[ToKey(k)].isPressed;
        public static bool Pressed(DemoKey k) => !LooksmithTweaksHooks.typing && !Shortcut && Keyboard.current != null && Keyboard.current[ToKey(k)].wasPressedThisFrame;
        // Ctrl+Z and the like belong to the editor, not the demo
        static bool Shortcut => Keyboard.current != null &&
                                (Keyboard.current.ctrlKey.isPressed || Keyboard.current.leftMetaKey.isPressed || Keyboard.current.rightMetaKey.isPressed);
        public static Vector2 MouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
        public static bool Click => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        static KeyCode ToKey(DemoKey k)
        {
            switch (k)
            {
                case DemoKey.Forward: return KeyCode.W;
                case DemoKey.Back: return KeyCode.S;
                case DemoKey.Left: return KeyCode.A;
                case DemoKey.Right: return KeyCode.D;
                case DemoKey.Run: return KeyCode.LeftShift;
                case DemoKey.Jump: return KeyCode.Space;
                case DemoKey.Up: return KeyCode.Space;
                case DemoKey.Down: return KeyCode.LeftControl;
                case DemoKey.PrevLook: return KeyCode.Q;
                case DemoKey.NextLook: return KeyCode.E;
                case DemoKey.PrevSky: return KeyCode.Z;
                case DemoKey.NextSky: return KeyCode.C;
                case DemoKey.Bypass: return KeyCode.B;
                case DemoKey.Split: return KeyCode.V;
                case DemoKey.DebugView: return KeyCode.G;
                case DemoKey.Panel: return KeyCode.Tab;
                case DemoKey.Fly: return KeyCode.F;
                case DemoKey.Release: return KeyCode.Escape;
                case DemoKey.TimeOfDay: return KeyCode.T;
                case DemoKey.HourBack: return KeyCode.LeftBracket;
                case DemoKey.HourForward: return KeyCode.RightBracket;
                case DemoKey.Mood: return KeyCode.M;
                case DemoKey.Wind: return KeyCode.R;
                case DemoKey.Atmosphere: return KeyCode.U;
                case DemoKey.Grass: return KeyCode.H;
                case DemoKey.MoodA: return KeyCode.Alpha1;
                case DemoKey.MoodB: return KeyCode.Alpha2;
                case DemoKey.Tune: return KeyCode.Y;
                case DemoKey.TuneUp: return KeyCode.UpArrow;
                case DemoKey.TuneDown: return KeyCode.DownArrow;
                case DemoKey.TuneLess: return KeyCode.LeftArrow;
                case DemoKey.TuneMore: return KeyCode.RightArrow;
                case DemoKey.TuneApply: return KeyCode.Return;
                case DemoKey.TuneReset: return KeyCode.Backspace;
                case DemoKey.Quality: return KeyCode.P;
            }
            return KeyCode.None;
        }

        public static bool Held(DemoKey k) => !LooksmithTweaksHooks.typing && Input.GetKey(ToKey(k));
        public static bool Pressed(DemoKey k) => !LooksmithTweaksHooks.typing && !Shortcut && Input.GetKeyDown(ToKey(k));
        // Ctrl+Z and the like belong to the editor, not the demo
        static bool Shortcut => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                                Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
        public static Vector2 MouseDelta => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        public static bool Click => Input.GetMouseButtonDown(0);
        public static Vector2 MousePosition => Input.mousePosition;
#endif

        /// <summary>The label a key shows on screen.</summary>
        public static string Label(DemoKey k)
        {
            switch (k)
            {
                case DemoKey.PrevLook: return "Q";
                case DemoKey.NextLook: return "E";
                case DemoKey.PrevSky: return "Z";
                case DemoKey.NextSky: return "C";
                case DemoKey.Bypass: return "B";
                case DemoKey.Split: return "V";
                case DemoKey.DebugView: return "G";
                case DemoKey.Panel: return "Tab";
                case DemoKey.Fly: return "F";
                case DemoKey.Release: return "Esc";
                case DemoKey.TimeOfDay: return "T";
                case DemoKey.HourBack: return "[";
                case DemoKey.HourForward: return "]";
                case DemoKey.Mood: return "M";
                case DemoKey.Wind: return "R";
                case DemoKey.Atmosphere: return "U";
                case DemoKey.Grass: return "H";
                case DemoKey.Quality: return "P";
                case DemoKey.Run: return "Shift";
                case DemoKey.Jump: return "Space";
                case DemoKey.MoodA: return "1";
                case DemoKey.MoodB: return "2";
                case DemoKey.Tune: return "Y";
                case DemoKey.TuneUp: return "↑";
                case DemoKey.TuneDown: return "↓";
                case DemoKey.TuneLess: return "←";
                case DemoKey.TuneMore: return "→";
                case DemoKey.TuneApply: return "Enter";
                case DemoKey.TuneReset: return "Backspace";
            }
            return k.ToString();
        }
    }
}
