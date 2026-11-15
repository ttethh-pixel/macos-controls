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

namespace MacosControls
{
    internal static class Program
    {
        internal const string Name = "macos controls";

        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            // Keep the same mutex as the first release so both versions cannot run together.
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
        private readonly ToolStripMenuItem hotkeyToggle;
        private readonly ToolStripMenuItem upToggle;
        private readonly ToolStripMenuItem downToggle;
        private readonly ToolStripMenuItem desktopsToggle;
        private readonly HotkeyWindow hotkey;
        private readonly DesktopGesture gesture;

        internal TrayApplication()
        {
            hotkey = new HotkeyWindow(CloseForegroundApplication);
            gesture = new DesktopGesture();
            menu = new ContextMenuStrip();
            hotkeyToggle = new ToolStripMenuItem("Откл. Alt+Q") { CheckOnClick = true };
            hotkeyToggle.Click += (sender, args) => SetHotkeyActive(!hotkeyToggle.Checked);
            menu.Items.Add(hotkeyToggle);
            upToggle = AddGestureToggle("Откл. жест вверх");
            downToggle = AddGestureToggle("Откл. жест вниз");
            desktopsToggle = AddGestureToggle("Откл. переключение столов");
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateHelpMenu());
            menu.Items.Add("Выход", null, (sender, args) => ExitThread());
            using (Stream image = typeof(TrayApplication).Assembly.GetManifestResourceStream("MacosControls.ico"))
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
            SetHotkeyActive(true);
            UpdateGestures();
        }

        internal const string HelpText =
            "Закрытие окна — Alt+Q.\n\n" +
            "Переключение рабочих столов — жесты вправо и влево с зажатым колёсиком.\n\n" +
            "Представление задач — жест вверх с зажатым колёсиком. Повтори жест для выхода.\n\n" +
            "Показать рабочий стол — жест вниз с зажатым колёсиком. Повтори жест, чтобы вернуть окна. В «Представлении задач» жест вниз возвращает к предыдущему окну.";

        internal static ToolStripMenuItem CreateHelpMenu()
        {
            return new ToolStripMenuItem("Мини-справка", null, (sender, args) =>
                MessageBox.Show(HelpText, "Мини-справка — macos controls", MessageBoxButtons.OK));
        }

        private ToolStripMenuItem AddGestureToggle(string label)
        {
            var toggle = new ToolStripMenuItem(label) { CheckOnClick = true };
            toggle.Click += (sender, args) => UpdateGestures();
            menu.Items.Add(toggle);
            return toggle;
        }

        private void SetHotkeyActive(bool active)
        {
            int error = hotkey.SetActive(active);
            hotkeyToggle.Checked = !hotkey.Active;
            if (error != 0)
            {
                string details = error == 1409
                    ? "Сочетание уже занято. Освободите его и снимите галочку «Откл. Alt+Q» в трее."
                    : new Win32Exception(error).Message;
                MessageBox.Show(details, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateGestures()
        {
            MouseAction actions = MouseAction.None;
            if (!upToggle.Checked)
                actions |= MouseAction.ToggleTaskView;
            if (!downToggle.Checked)
                actions |= MouseAction.ToggleDesktop;
            if (!desktopsToggle.Checked)
                actions |= MouseAction.DesktopLeft | MouseAction.DesktopRight;

            int error = gesture.SetEnabledActions(actions);
            if (error != 0)
            {
                upToggle.Checked = true;
                downToggle.Checked = true;
                desktopsToggle.Checked = true;
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

    [Flags]
    internal enum MouseAction
    {
        None = 0,
        MiddleClick = 1,
        DesktopLeft = 2,
        DesktopRight = 4,
        ToggleTaskView = 8,
        ToggleDesktop = 16
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
        private MouseAction enabledActions;
        private bool tracking;
        private Point start;

        internal DesktopGesture()
        {
            callback = OnMouseEvent;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        }

        internal int SetEnabledActions(MouseAction actions)
        {
            if (actions != MouseAction.None && hook == IntPtr.Zero)
            {
                hook = SetWindowsHookEx(WhMouseLowLevel, callback, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero)
                    return Marshal.GetLastWin32Error();
            }
            enabledActions = actions;
            return 0;
        }

        internal bool IsEnabled(MouseAction action)
        {
            return (enabledActions & action) != MouseAction.None;
        }

        private IntPtr OnMouseEvent(int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            bool active = enabledActions != MouseAction.None;
            if (code < 0 || (!active && !tracking) || (kind != WmMiddleDown && kind != WmMiddleUp))
                return CallNextHookEx(hook, code, message, data);

            var mouse = (MouseEvent)Marshal.PtrToStructure(data, typeof(MouseEvent));
            if ((mouse.Flags & InjectedMouseEvent) != 0 && mouse.ExtraInfo == ClickMarker)
                return CallNextHookEx(hook, code, message, data);

            if (kind == WmMiddleDown && active && !ModifiersDown())
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
                if (action == MouseAction.MiddleClick || IsEnabled(action))
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
                return dy < 0 ? MouseAction.ToggleTaskView : MouseAction.ToggleDesktop;
            if (horizontal <= 12 && vertical <= 12)
                return MouseAction.MiddleClick;
            return MouseAction.None;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmGesture)
            {
                var action = (MouseAction)message.WParam.ToInt32();
                if (action != MouseAction.MiddleClick && (!IsEnabled(action) || ModifiersDown()))
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
                    case MouseAction.ToggleTaskView:
                        SendWindowsShortcut(Keys.Tab);
                        break;
                    case MouseAction.ToggleDesktop:
                        SendWindowsShortcut(TaskViewIsForeground() ? Keys.Tab : Keys.D);
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

        private static void SendWindowsShortcut(Keys key)
        {
            Send(Key(Keys.LWin, ExtendedKey), Key(key, 0),
                Key(key, KeyUp), Key(Keys.LWin, ExtendedKey | KeyUp));
        }

        private static bool TaskViewIsForeground()
        {
            IntPtr foreground = Native.GetForegroundWindow();
            uint shell = Native.ProcessId(Native.GetShellWindow());
            return foreground != IntPtr.Zero && IsTaskViewWindow(
                shell != 0 && Native.ProcessId(foreground) == shell,
                Native.ClassName(foreground), Native.WindowTitle(foreground));
        }

        internal static bool IsTaskViewWindow(bool belongsToShell, string windowClass, string title)
        {
            return windowClass == "MultitaskingViewFrame"
                || (belongsToShell && (windowClass == "Windows.UI.Core.CoreWindow"
                    || windowClass == "ForegroundStaging"
                    || (windowClass == "XamlExplorerHostIslandWindow"
                        && (title == "Task View" || title == "Представление задач"))));
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
            enabledActions = MouseAction.None;
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
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxLength);

        internal static string WindowTitle(IntPtr window)
        {
            var title = new StringBuilder(128);
            GetWindowText(window, title, title.Capacity);
            return title.ToString();
        }

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
