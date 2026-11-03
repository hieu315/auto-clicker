using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SwiftMacroPro
{
    internal static class NativeMethods
    {
        // Chữ ký định danh sự kiện do chính SwiftMacro Pro phát ra ("SMP1")
        // Dùng để lọc trong Low-Level Hook, tránh ghi lặp lại sự kiện của chính tool
        public static readonly IntPtr SELF_INJECTED_SIGNATURE = new IntPtr(0x534D5031);

        // Hằng số Hook
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        // Hằng số thông điệp chuột
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;

        // Cờ trạng thái nút chuột trong PostMessage (wParam)
        public const uint MK_LBUTTON = 0x0001;
        public const uint MK_RBUTTON = 0x0002;
        public const uint MK_MBUTTON = 0x0010;

        // Hằng số thông điệp bàn phím
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        // Cờ kiểm tra sự kiện Injected trong Hook
        public const uint LLMHF_INJECTED = 0x00000001;
        public const uint LLKHF_INJECTED = 0x00000010;

        // Hằng số SendInput
        public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;

        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        // Hằng số GetSystemMetrics cho Virtual Screen
        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;
        public const int SM_CMONITORS = 80;

        // Hằng số Global Hotkey
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_NONE = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // DPI Awareness Contexts
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();

        /// <summary>
        /// Thiết lập PerMonitorV2 DPI Awareness ở mức API (bổ trợ cho app.manifest).
        /// </summary>
        public static void InitializeDpiAwareness()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }

        /// <summary>
        /// Quy đổi tọa độ vật lý (x, y) trên Virtual Desktop sang hệ chuẩn hóa [0..65535]
        /// phục vụ cờ MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK.
        /// Hỗ trợ đầy đủ màn hình phụ có tọa độ âm.
        /// </summary>
        public static void NormalizeVirtualDesktopCoordinates(int physicalX, int physicalY, out int normalizedX, out int normalizedY)
        {
            int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (vWidth <= 1) vWidth = 1920;
            if (vHeight <= 1) vHeight = 1080;

            long nx = ((long)(physicalX - vLeft) * 65536L) / vWidth + 1;
            long ny = ((long)(physicalY - vTop) * 65536L) / vHeight + 1;

            if (nx < 0) nx = 0;
            if (nx > 65535) nx = 65535;
            if (ny < 0) ny = 0;
            if (ny > 65535) ny = 65535;

            normalizedX = (int)nx;
            normalizedY = (int)ny;
        }

        /// <summary>
        /// Gửi sự kiện chuột tại tọa độ Virtual Desktop kèm chữ ký SELF_INJECTED_SIGNATURE.
        /// </summary>
        public static void SendMouseVirtualDesk(int x, int y, uint mouseEventFlags, int mouseData = 0)
        {
            int normX, normY;
            NormalizeVirtualDesktopCoordinates(x, y, out normX, out normY);

            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dx = normX;
            inputs[0].u.mi.dy = normY;
            inputs[0].u.mi.mouseData = unchecked((uint)mouseData);
            inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | mouseEventFlags;
            inputs[0].u.mi.time = 0;
            inputs[0].u.mi.dwExtraInfo = SELF_INJECTED_SIGNATURE;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Gửi riêng cờ nhả nút chuột tại vị trí hiện tại (dùng trong khối finally khi dọn dẹp trạng thái giữ).
        /// </summary>
        public static void SendMouseReleaseOnly(uint releaseFlags)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dx = 0;
            inputs[0].u.mi.dy = 0;
            inputs[0].u.mi.mouseData = 0;
            inputs[0].u.mi.dwFlags = releaseFlags;
            inputs[0].u.mi.time = 0;
            inputs[0].u.mi.dwExtraInfo = SELF_INJECTED_SIGNATURE;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Gửi sự kiện bàn phím kèm chữ ký SELF_INJECTED_SIGNATURE.
        /// </summary>
        public static void SendKeyInput(ushort vkCode, bool isKeyUp)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vkCode;
            inputs[0].u.ki.wScan = 0;
            inputs[0].u.ki.dwFlags = isKeyUp ? KEYEVENTF_KEYUP : 0u;
            inputs[0].u.ki.time = 0;
            inputs[0].u.ki.dwExtraInfo = SELF_INJECTED_SIGNATURE;

            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static IntPtr MakeLParam(int lowWord, int highWord)
        {
            int value = ((highWord & 0xFFFF) << 16) | (lowWord & 0xFFFF);
            return new IntPtr(value);
        }

        /// <summary>
        /// Gửi sự kiện chuột chạy ngầm qua PostMessage vào cửa sổ đích tại tọa độ (screenX, screenY)
        /// mà không di chuyển con trỏ chuột vật lý. Trả về HWND của cửa sổ đích.
        /// </summary>
        public static IntPtr SendBackgroundMouseMessage(int screenX, int screenY, uint message, uint wParamFlags, int wheelDelta = 0)
        {
            POINT screenPt = new POINT { x = screenX, y = screenY };
            IntPtr hWnd = WindowFromPoint(screenPt);
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;

            if (message == WM_MOUSEWHEEL)
            {
                IntPtr wParamWheel = MakeLParam((int)wParamFlags, wheelDelta);
                IntPtr lParamScreen = MakeLParam(screenX, screenY);
                PostMessage(hWnd, WM_MOUSEWHEEL, wParamWheel, lParamScreen);
                return hWnd;
            }

            POINT clientPt = screenPt;
            ScreenToClient(hWnd, ref clientPt);
            IntPtr lParamClient = MakeLParam(clientPt.x, clientPt.y);

            // Gửi trước WM_MOUSEMOVE tại tọa độ Client để cửa sổ đích cập nhật vị trí hover nội bộ
            if (message != WM_MOUSEMOVE)
            {
                PostMessage(hWnd, WM_MOUSEMOVE, new IntPtr((int)wParamFlags), lParamClient);
            }
            PostMessage(hWnd, message, new IntPtr((int)wParamFlags), lParamClient);
            return hWnd;
        }

        /// <summary>
        /// Gửi sự kiện phím chạy ngầm qua PostMessage tới HWND đích.
        /// </summary>
        public static void SendBackgroundKeyMessage(IntPtr targetHwnd, ushort vkCode, bool isKeyUp)
        {
            if (targetHwnd == IntPtr.Zero)
            {
                POINT pt;
                if (GetCursorPos(out pt))
                {
                    targetHwnd = WindowFromPoint(pt);
                }
            }
            if (targetHwnd == IntPtr.Zero) return;

            uint msg = isKeyUp ? (uint)WM_KEYUP : (uint)WM_KEYDOWN;
            IntPtr lParam = isKeyUp ? new IntPtr(unchecked((int)0xC0000001)) : new IntPtr(0x00000001);
            PostMessage(targetHwnd, msg, new IntPtr(vkCode), lParam);
        }
    }
}
