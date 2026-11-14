using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("macos controls")]
[assembly: AssemblyProduct("macos controls")]

namespace AltQTray
{
    internal static class Program
    {
        internal const string Name = "macos controls";

        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\AltQTray.SingleInstance", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Программа уже запущена. Ищите значок в трее.", Name,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var tray = new TrayApplication())
                    Application.Run(tray);
            }
        }
    }

    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly NotifyIcon icon;
        private readonly Icon trayImage;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem toggle;
        private readonly ToolStripMenuItem gestureToggle;
        private readonly HotkeyWindow hotkey;
        private readonly DesktopGesture gesture;

        internal TrayApplication()
        {
            hotkey = new HotkeyWindow(CloseForegroundApplication);
            gesture = new DesktopGesture();
            menu = new ContextMenuStrip();
            toggle = new ToolStripMenuItem();
            toggle.Click += (sender, args) => SetActive(!hotkey.Active);
            menu.Items.Add(toggle);
            gestureToggle = new ToolStripMenuItem();
            gestureToggle.Click += (sender, args) => SetGestureActive(!gesture.Active);
            menu.Items.Add(gestureToggle);
            var taskViewToggle = new ToolStripMenuItem("Отключить жесты представления задач")
            {
                CheckOnClick = true
            };
            taskViewToggle.CheckedChanged += (sender, args) => gesture.TaskViewEnabled = !taskViewToggle.Checked;
            menu.Items.Add(taskViewToggle);
            menu.Items.Add("Выход", null, (sender, args) => ExitThread());
            using (Stream image = typeof(TrayApplication).Assembly.GetManifestResourceStream("AltQTray.ico"))
            {
                trayImage = image == null ? (Icon)SystemIcons.Application.Clone()
                    : new Icon(image, SystemInformation.SmallIconSize);
            }
            icon = new NotifyIcon
            {
                Icon = trayImage,
                ContextMenuStrip = menu,
                Text = Program.Name,
                Visible = true
            };
            SetActive(true);
            SetGestureActive(true);
        }

        private void SetActive(bool active)
        {
            int error = hotkey.SetActive(active);
            toggle.Text = hotkey.Active ? "Приостановить Alt+Q" : "Включить Alt+Q";
            if (error != 0)
            {
                string details = error == 1409
                    ? "Сочетание уже занято. Освободите его и выберите «Включить Alt+Q» в трее."
                    : new Win32Exception(error).Message;
                MessageBox.Show(details, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetGestureActive(bool active)
        {
            int error = gesture.SetActive(active);
            gestureToggle.Text = "Жесты колёсиком";
            gestureToggle.Checked = gesture.Active;
            if (error != 0)
            {
                MessageBox.Show("Не удалось включить жест мыши. " + new Win32Exception(error).Message,
                    Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CloseForegroundApplication()
        {
            bool accessDenied = ApplicationWindows.RequestClose(Native.GetForegroundWindow());
            if (accessDenied)
            {
                icon.ShowBalloonTip(3000, Program.Name,
                    "У приложения более высокие права. Windows не передала запрос на закрытие.",
                    ToolTipIcon.Info);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                gesture.Dispose();
                hotkey.Dispose();
                icon.Visible = false;
                icon.Dispose();
                trayImage.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal enum MouseAction
    {
        None,
        MiddleClick,
        DesktopLeft,
        DesktopRight,
        OpenTaskView,
        CloseTaskView
    }

    internal sealed class DesktopGesture : NativeWindow, IDisposable
    {
        private const int WhMouseLowLevel = 14;
        private const int WmMiddleDown = 0x0207;
        private const int WmMiddleUp = 0x0208;
        private const int WmGesture = 0x8001;
        private const int SwipeDistance = 56;
        private const uint InjectedMouseEvent = 1;
        private const uint ExtendedKey = 0x0001;
        private const uint KeyUp = 0x0002;
        private static readonly UIntPtr ClickMarker = new UIntPtr(0x514D4753u);
        private readonly MouseHookCallback callback;
        private IntPtr hook;
        private bool tracking;
        private Point start;

        internal DesktopGesture()
        {
            callback = OnMouseEvent;
            TaskViewEnabled = true;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        }

        internal bool Active { get; private set; }
        internal bool TaskViewEnabled { get; set; }

        internal int SetActive(bool active)
        {
            if (active && hook == IntPtr.Zero)
            {
                hook = SetWindowsHookEx(WhMouseLowLevel, callback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                    return Marshal.GetLastWin32Error();
            }
            Active = active;
            return 0;
        }

        private IntPtr OnMouseEvent(int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            if (code < 0 || (!Active && !tracking) || (kind != WmMiddleDown && kind != WmMiddleUp))
                return CallNextHookEx(hook, code, message, data);

            var mouse = (MouseEvent)Marshal.PtrToStructure(data, typeof(MouseEvent));
            if ((mouse.Flags & InjectedMouseEvent) != 0 && mouse.ExtraInfo == ClickMarker)
                return CallNextHookEx(hook, code, message, data);

            if (kind == WmMiddleDown && Active && !ModifiersDown())
            {
                if (!tracking)
                    start = mouse.Position;
                tracking = true;
                return new IntPtr(1);
            }

            if (kind == WmMiddleUp && tracking)
            {
                tracking = false;
                MouseAction action = Classify(start, mouse.Position);
                if (action == MouseAction.MiddleClick || (Active && action != MouseAction.None))
                    Native.PostMessage(Handle, WmGesture, new IntPtr((int)action), IntPtr.Zero);
                return new IntPtr(1);
            }

            return CallNextHookEx(hook, code, message, data);
        }

        internal static MouseAction Classify(Point start, Point end)
        {
            int dx = end.X - start.X;
            int dy = end.Y - start.Y;
            int horizontal = Math.Abs(dx);
            int vertical = Math.Abs(dy);
            if (horizontal >= SwipeDistance && horizontal >= 1.5 * vertical)
                return dx < 0 ? MouseAction.DesktopRight : MouseAction.DesktopLeft;
            if (vertical >= SwipeDistance && vertical >= 1.5 * horizontal)
                return dy < 0 ? MouseAction.OpenTaskView : MouseAction.CloseTaskView;
            if (horizontal <= 12 && vertical <= 12)
                return MouseAction.MiddleClick;
            return MouseAction.None;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmGesture)
            {
                var action = (MouseAction)message.WParam.ToInt32();
                if (action != MouseAction.MiddleClick && (!Active || ModifiersDown()))
                    return;

                switch (action)
                {
                    case MouseAction.MiddleClick:
                        Send(MouseButton(0x0020), MouseButton(0x0040)); // Middle down, middle up.
                        break;
                    case MouseAction.DesktopLeft:
                        SwitchDesktop(Keys.Left);
                        break;
                    case MouseAction.DesktopRight:
                        SwitchDesktop(Keys.Right);
                        break;
                    case MouseAction.OpenTaskView:
                        if (TaskViewEnabled && !TaskViewIsForeground())
                            Send(Key(Keys.LWin, ExtendedKey), Key(Keys.Tab, 0),
                                Key(Keys.Tab, KeyUp), Key(Keys.LWin, ExtendedKey | KeyUp));
                        break;
                    case MouseAction.CloseTaskView:
                        if (TaskViewEnabled && TaskViewIsForeground())
                            Send(Key(Keys.Escape, 0), Key(Keys.Escape, KeyUp));
                        break;
                }
                return;
            }
            base.WndProc(ref message);
        }

        private static void SwitchDesktop(Keys direction)
        {
            Send(Key(Keys.LWin, ExtendedKey), Key(Keys.LControlKey, 0), Key(direction, ExtendedKey),
                Key(direction, ExtendedKey | KeyUp), Key(Keys.LControlKey, KeyUp), Key(Keys.LWin, ExtendedKey | KeyUp));
        }

        internal static bool TaskViewIsForeground()
        {
            IntPtr window = Native.GetForegroundWindow();
            if (window == IntPtr.Zero)
                return false;

            // Windows 10 uses a dedicated window class. Newer builds identify the shell thread.
            if (Native.ClassName(window) == "MultitaskingViewFrame")
                return true;

            uint process;
            uint threadId = Native.GetWindowThreadProcessId(window, out process);
            if (threadId == 0 || process != Native.ProcessId(Native.GetShellWindow()))
                return false;

            const uint queryLimitedInformation = 0x0800;
            IntPtr thread = OpenThread(queryLimitedInformation, false, threadId);
            if (thread == IntPtr.Zero)
                return false;

            IntPtr description = IntPtr.Zero;
            try
            {
                return GetThreadDescription(thread, out description) >= 0
                    && Marshal.PtrToStringUni(description) == "MultitaskingView";
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            finally
            {
                if (description != IntPtr.Zero)
                    LocalFree(description);
                CloseHandle(thread);
            }
        }

        private static bool ModifiersDown()
        {
            return IsDown(Keys.ShiftKey) || IsDown(Keys.ControlKey) || IsDown(Keys.Menu)
                || IsDown(Keys.LWin) || IsDown(Keys.RWin);
        }

        private static bool IsDown(Keys key)
        {
            return GetAsyncKeyState((int)key) < 0;
        }

        private static Input MouseButton(uint flags)
        {
            return new Input { Data = new InputData { Mouse = new MouseInput { Flags = flags, ExtraInfo = ClickMarker } } };
        }

        private static Input Key(Keys key, uint flags)
        {
            return new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = (ushort)key, Flags = flags } } };
        }

        private static void Send(params Input[] inputs)
        {
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input)));
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
            if (Handle != IntPtr.Zero)
                DestroyHandle();
            Active = false;
        }

        private delegate IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseEvent
        {
            internal Point Position;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            internal uint Type;
            internal InputData Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] internal MouseInput Mouse;
            [FieldOffset(0)] internal KeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            internal int X;
            internal int Y;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            internal ushort Key;
            internal ushort ScanCode;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenThread(uint access, bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll")]
        private static extern int GetThreadDescription(IntPtr thread, out IntPtr description);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int kind, MouseHookCallback callback, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HotkeyId = 1;
        private const int WmHotkey = 0x0312;
        private readonly Action invoke;

        internal bool Active { get; private set; }

        internal HotkeyWindow(Action invoke)
        {
            this.invoke = invoke;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        }

        internal int SetActive(bool active)
        {
            if (active == Active)
                return 0;

            const uint modAlt = 0x0001;
            const uint modNoRepeat = 0x4000;
            bool changed = active
                ? Native.RegisterHotKey(Handle, HotkeyId, modAlt | modNoRepeat, (uint)Keys.Q)
                : Native.UnregisterHotKey(Handle, HotkeyId);
            if (!changed)
                return Marshal.GetLastWin32Error();

            Active = active;
            return 0;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && message.WParam == new IntPtr(HotkeyId))
            {
                if (Active)
                    invoke();
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero)
                return;
            SetActive(false);
            DestroyHandle();
        }
    }

    internal static class ApplicationWindows
    {
        private const int WmClose = 0x0010;
        private const int Owner = 4;
        private const int ExtendedStyle = -20;
        private const int ToolWindow = 0x0080;
        private const int AppWindow = 0x00040000;

        internal static bool RequestClose(IntPtr active)
        {
            uint process = Native.ProcessId(active);
            if (process == 0 || process == Native.GetCurrentProcessId() || !IsMainWindow(active))
                return false;

            string activeClass = Native.ClassName(active);
            bool explorer = process == Native.ProcessId(Native.GetShellWindow());
            if (explorer && !IsFolderWindow(activeClass))
                return false;

            var windows = new List<IntPtr> { active };
            // The packaged-app host can own windows from several unrelated apps.
            if (activeClass != "ApplicationFrameWindow")
            {
                Native.EnumWindows((window, unused) =>
                {
                    if (window != active && Native.ProcessId(window) == process
                        && IsMainWindow(window) && (!explorer || IsFolderWindow(Native.ClassName(window))))
                        windows.Add(window);
                    return true;
                }, IntPtr.Zero);
            }

            bool accessDenied = false;
            foreach (IntPtr window in windows)
            {
                // A window can disappear or become disabled after enumeration.
                if (Native.ProcessId(window) != process || !IsMainWindow(window))
                    continue;
                if (!Native.PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero)
                    && Marshal.GetLastWin32Error() == 5)
                    accessDenied = true;
            }
            return accessDenied;
        }

        private static bool IsMainWindow(IntPtr window)
        {
            if (!Native.IsWindowVisible(window) || !Native.IsWindowEnabled(window))
                return false;

            int style = Native.GetWindowLong(window, ExtendedStyle);
            return (style & ToolWindow) == 0
                && ((style & AppWindow) != 0 || Native.GetWindow(window, Owner) == IntPtr.Zero);
        }

        private static bool IsFolderWindow(string windowClass)
        {
            return windowClass == "CabinetWClass" || windowClass == "ExploreWClass";
        }
    }

    internal static class Native
    {
        internal delegate bool EnumWindowCallback(IntPtr window, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentProcessId();

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowCallback callback, IntPtr data);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowEnabled(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int maxLength);

        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        internal static uint ProcessId(IntPtr window)
        {
            uint process;
            return window != IntPtr.Zero && GetWindowThreadProcessId(window, out process) != 0 ? process : 0;
        }

        internal static string ClassName(IntPtr window)
        {
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            return name.ToString();
        }
    }
}
