using UnityEngine.InputSystem;

namespace MidtownMadness.Input
{
    /// <summary>
    /// DirectInput keyboard scan codes. Keyboard bindings store these raw, so
    /// config files from the original game stay readable.
    /// </summary>
    public static class Dik
    {
        public const int Escape = 0x01;
        public const int D1 = 0x02, D2 = 0x03, D3 = 0x04, D4 = 0x05, D5 = 0x06;
        public const int D6 = 0x07, D7 = 0x08, D8 = 0x09, D9 = 0x0A, D0 = 0x0B;
        public const int Minus = 0x0C, Equals = 0x0D, Back = 0x0E, Tab = 0x0F;
        public const int Q = 0x10, W = 0x11, E = 0x12, R = 0x13, T = 0x14;
        public const int Y = 0x15, U = 0x16, I = 0x17, O = 0x18, P = 0x19;
        public const int LBracket = 0x1A, RBracket = 0x1B, Return = 0x1C, LControl = 0x1D;
        public const int A = 0x1E, S = 0x1F, D = 0x20, F = 0x21, G = 0x22;
        public const int H = 0x23, J = 0x24, K = 0x25, L = 0x26;
        public const int Semicolon = 0x27, Apostrophe = 0x28, Grave = 0x29;
        public const int LShift = 0x2A, Backslash = 0x2B;
        public const int Z = 0x2C, X = 0x2D, C = 0x2E, V = 0x2F, B = 0x30, N = 0x31, M = 0x32;
        public const int Comma = 0x33, Period = 0x34, Slash = 0x35, RShift = 0x36;
        public const int Multiply = 0x37, LMenu = 0x38, Space = 0x39, Capital = 0x3A;
        public const int F1 = 0x3B, F2 = 0x3C, F3 = 0x3D, F4 = 0x3E, F5 = 0x3F;
        public const int F6 = 0x40, F7 = 0x41, F8 = 0x42, F9 = 0x43, F10 = 0x44;
        public const int NumLock = 0x45, Scroll = 0x46;
        public const int Numpad7 = 0x47, Numpad8 = 0x48, Numpad9 = 0x49, Subtract = 0x4A;
        public const int Numpad4 = 0x4B, Numpad5 = 0x4C, Numpad6 = 0x4D, Add = 0x4E;
        public const int Numpad1 = 0x4F, Numpad2 = 0x50, Numpad3 = 0x51;
        public const int Numpad0 = 0x52, Decimal = 0x53;
        public const int F11 = 0x57, F12 = 0x58;
        public const int NumpadEnter = 0x9C, RControl = 0x9D;
        public const int Divide = 0xB5, RMenu = 0xB8;
        public const int Home = 0xC7, Up = 0xC8, Prior = 0xC9;
        public const int Left = 0xCB, Right = 0xCD;
        public const int End = 0xCF, Down = 0xD0, Next = 0xD1;
        public const int Insert = 0xD2, Delete = 0xD3;
    }

    /// <summary>
    /// DIK scan code to Unity <see cref="Key"/>. Both are physical and
    /// layout-independent, so this is a straight 1:1 table.
    /// </summary>
    public static class DikKeyMap
    {
        private static readonly Key[] Table = BuildTable();

        public static Key ToKey(int dik)
            => (dik >= 0 && dik < Table.Length) ? Table[dik] : Key.None;

        public static int ToDik(Key key)
        {
            for (int i = 0; i < Table.Length; i++)
                if (Table[i] == key) return i;
            return 0;
        }

        private static Key[] BuildTable()
        {
            var t = new Key[256];

            t[Dik.Escape] = Key.Escape;
            t[Dik.D1] = Key.Digit1; t[Dik.D2] = Key.Digit2; t[Dik.D3] = Key.Digit3;
            t[Dik.D4] = Key.Digit4; t[Dik.D5] = Key.Digit5; t[Dik.D6] = Key.Digit6;
            t[Dik.D7] = Key.Digit7; t[Dik.D8] = Key.Digit8; t[Dik.D9] = Key.Digit9;
            t[Dik.D0] = Key.Digit0;
            t[Dik.Minus] = Key.Minus; t[Dik.Equals] = Key.Equals;
            t[Dik.Back] = Key.Backspace; t[Dik.Tab] = Key.Tab;

            t[Dik.Q] = Key.Q; t[Dik.W] = Key.W; t[Dik.E] = Key.E; t[Dik.R] = Key.R;
            t[Dik.T] = Key.T; t[Dik.Y] = Key.Y; t[Dik.U] = Key.U; t[Dik.I] = Key.I;
            t[Dik.O] = Key.O; t[Dik.P] = Key.P;
            t[Dik.LBracket] = Key.LeftBracket; t[Dik.RBracket] = Key.RightBracket;
            t[Dik.Return] = Key.Enter; t[Dik.LControl] = Key.LeftCtrl;

            t[Dik.A] = Key.A; t[Dik.S] = Key.S; t[Dik.D] = Key.D; t[Dik.F] = Key.F;
            t[Dik.G] = Key.G; t[Dik.H] = Key.H; t[Dik.J] = Key.J; t[Dik.K] = Key.K;
            t[Dik.L] = Key.L;
            t[Dik.Semicolon] = Key.Semicolon; t[Dik.Apostrophe] = Key.Quote;
            t[Dik.Grave] = Key.Backquote; t[Dik.LShift] = Key.LeftShift;
            t[Dik.Backslash] = Key.Backslash;

            t[Dik.Z] = Key.Z; t[Dik.X] = Key.X; t[Dik.C] = Key.C; t[Dik.V] = Key.V;
            t[Dik.B] = Key.B; t[Dik.N] = Key.N; t[Dik.M] = Key.M;
            t[Dik.Comma] = Key.Comma; t[Dik.Period] = Key.Period; t[Dik.Slash] = Key.Slash;
            t[Dik.RShift] = Key.RightShift; t[Dik.Multiply] = Key.NumpadMultiply;
            t[Dik.LMenu] = Key.LeftAlt; t[Dik.Space] = Key.Space; t[Dik.Capital] = Key.CapsLock;

            t[Dik.F1] = Key.F1; t[Dik.F2] = Key.F2; t[Dik.F3] = Key.F3; t[Dik.F4] = Key.F4;
            t[Dik.F5] = Key.F5; t[Dik.F6] = Key.F6; t[Dik.F7] = Key.F7; t[Dik.F8] = Key.F8;
            t[Dik.F9] = Key.F9; t[Dik.F10] = Key.F10; t[Dik.F11] = Key.F11; t[Dik.F12] = Key.F12;

            t[Dik.NumLock] = Key.NumLock; t[Dik.Scroll] = Key.ScrollLock;
            t[Dik.Numpad7] = Key.Numpad7; t[Dik.Numpad8] = Key.Numpad8; t[Dik.Numpad9] = Key.Numpad9;
            t[Dik.Subtract] = Key.NumpadMinus;
            t[Dik.Numpad4] = Key.Numpad4; t[Dik.Numpad5] = Key.Numpad5; t[Dik.Numpad6] = Key.Numpad6;
            t[Dik.Add] = Key.NumpadPlus;
            t[Dik.Numpad1] = Key.Numpad1; t[Dik.Numpad2] = Key.Numpad2; t[Dik.Numpad3] = Key.Numpad3;
            t[Dik.Numpad0] = Key.Numpad0; t[Dik.Decimal] = Key.NumpadPeriod;
            t[Dik.NumpadEnter] = Key.NumpadEnter; t[Dik.RControl] = Key.RightCtrl;
            t[Dik.Divide] = Key.NumpadDivide; t[Dik.RMenu] = Key.RightAlt;

            t[Dik.Home] = Key.Home; t[Dik.Up] = Key.UpArrow; t[Dik.Prior] = Key.PageUp;
            t[Dik.Left] = Key.LeftArrow; t[Dik.Right] = Key.RightArrow;
            t[Dik.End] = Key.End; t[Dik.Down] = Key.DownArrow; t[Dik.Next] = Key.PageDown;
            t[Dik.Insert] = Key.Insert; t[Dik.Delete] = Key.Delete;

            return t;
        }
    }
}