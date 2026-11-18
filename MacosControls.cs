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
            menu.Items.Add("Настройки…", null, (sender, args) =>
            {
                using (var settings = new ActivationSettings(gesture))
                    settings.ShowDialog();
            });
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

    internal sealed class ActivationSettings : Form
    {
        private readonly DesktopGesture gesture;
        private readonly Label status;
        private HashSet<int> selected;

        internal ActivationSettings(DesktopGesture gesture)
        {
            this.gesture = gesture;
            selected = new HashSet<int>(gesture.ActivationButtons);
            Text = "Настройки — macos controls";
            ClientSize = new Size(540, 260);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            var description = new Label { Text = "Сочетание активации жестов\nНажми «Записать», зажми клавиши и/или кнопки мыши одновременно и отпусти.", Location = new Point(20, 18), Size = new Size(500, 70) };
            status = new Label { Text = ButtonNames(selected), Location = new Point(20, 100), Size = new Size(500, 45) };
            var record = new Button { Text = "Записать", Location = new Point(20, 160), Size = new Size(130, 35) };
            record.Click += (sender, args) => { status.Text = "Ожидаю сочетание…"; gesture.Recording = true; };
            var reset = new Button { Text = "Колёсико", Location = new Point(165, 160), Size = new Size(130, 35) };
            reset.Click += (sender, args) => { gesture.Recording = false; selected = new HashSet<int> { 4 }; status.Text = ButtonNames(selected); };
            var save = new Button { Text = "Сохранить", Location = new Point(310, 160), Size = new Size(130, 35) };
            save.Click += (sender, args) =>
            {
                try { gesture.SaveActivation(selected); Close(); }
                catch (IOException error) { MessageBox.Show(error.Message, Text); }
                catch (UnauthorizedAccessException error) { MessageBox.Show(error.Message, Text); }
            };
            Controls.AddRange(new Control[] { description, status, record, reset, save });
            gesture.ButtonsRecorded += OnRecorded;
        }

        private void OnRecorded(HashSet<int> buttons)
        {
            BeginInvoke((Action)(() =>
            {
                selected = buttons;
                status.Text = ButtonNames(buttons);
            }));
        }

        private static string ButtonNames(HashSet<int> buttons)
        {
            var names = new List<string>();
            string[] labels = { "Левая", "Правая", "Колёсико", "Дополнительная 1", "Дополнительная 2" };
            for (int i = 0; i < labels.Length; i++)
                if (buttons.Contains(1 << i))
                    names.Add(labels[i]);
            foreach (int button in buttons)
                if (button >= 256)
                    names.Add(((Keys)(button - 256)).ToString());
            return string.Join(" + ", names.ToArray());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                gesture.Recording = false;
                gesture.ButtonsRecorded -= OnRecorded;
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
        private const int WmGesture = 0x8001;
        private const int SwipeDistance = 56;
        private const uint InjectedMouseEvent = 1;
        private const uint ExtendedKey = 0x0001;
        private const uint KeyUp = 0x0002;
        private static readonly UIntPtr ClickMarker = new UIntPtr(0x514D4753u);
        private readonly MouseHookCallback callback;
        private readonly MouseHookCallback keyboardCallback;
        private IntPtr keyboardHook;
        private IntPtr hook;
        private MouseAction enabledActions;
        private bool tracking;
        private Point start;
        private readonly HashSet<int> pressed = new HashSet<int>();
        private readonly HashSet<int> captured = new HashSet<int>();
        private readonly List<Input> pending = new List<Input>();
        internal HashSet<int> ActivationButtons { get; set; }
        internal bool Recording { get; set; }
        internal event Action<HashSet<int>> ButtonsRecorded;
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MacosControls", "activation.txt");

        internal void SaveActivation(HashSet<int> buttons)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            string temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, "v2:" + string.Join(",", buttons));
            if (File.Exists(SettingsPath))
                File.Replace(temporary, SettingsPath, null);
            else
                File.Move(temporary, SettingsPath);
            ActivationButtons = new HashSet<int>(buttons);
        }

        internal DesktopGesture()
        {
            callback = OnMouseEvent;
            keyboardCallback = OnKeyboardEvent;
            ActivationButtons = new HashSet<int> { 4 };
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string saved = File.ReadAllText(SettingsPath);
                    var parsed = new HashSet<int>();
                    if (saved.StartsWith("v2:"))
                    {
                        foreach (string token in saved.Substring(3).Split(','))
                        {
                            int value;
                            if (!int.TryParse(token, out value) || !(value == 1 || value == 2 || value == 4 || value == 8 || value == 16 || (value > 256 && value < 511)))
                                throw new IOException("Invalid activation setting");
                            parsed.Add(value);
                        }
                    }
                    else
                    {
                        int mask;
                        if (int.TryParse(saved, out mask))
                            foreach (int button in new[] { 1, 2, 4, 8, 16 })
                                if ((mask & button) != 0) parsed.Add(button);
                    }
                    if (parsed.Count != 0) ActivationButtons = parsed;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
            keyboardHook = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
            if (keyboardHook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
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

        internal static int CountButtons(int buttons)
        {
            int count = 0;
            for (; buttons != 0; buttons &= buttons - 1)
                count++;
            return count;
        }

        internal static int DecodeButton(int message, uint data, out bool down)
        {
            down = message == 0x201 || message == 0x204 || message == 0x207 || message == 0x20B;
            switch (message)
            {
                case 0x201: case 0x202: return 1;
                case 0x204: case 0x205: return 2;
                case 0x207: case 0x208: return 4;
                case 0x20B: case 0x20C: return (data >> 16) == 1 ? 8 : 16;
                default: return 0;
            }
        }

        private static Input ButtonInput(int button, bool down)
        {
            uint flag = button == 1 ? 0x2u : button == 2 ? 0x8u : button == 4 ? 0x20u : 0x80u;
            Input input = MouseButton(down ? flag : flag * 2);
            input.Data.Mouse.MouseData = button == 8 ? 1u : button == 16 ? 2u : 0u;
            return input;
        }

        private IntPtr OnMouseEvent(int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            if (code < 0)
                return CallNextHookEx(hook, code, message, data);

            var mouse = (MouseEvent)Marshal.PtrToStructure(data, typeof(MouseEvent));
            if ((mouse.Flags & InjectedMouseEvent) != 0 && mouse.ExtraInfo == ClickMarker)
                return CallNextHookEx(hook, code, message, data);

            bool down;
            int button = DecodeButton(kind, mouse.MouseData, out down);
            if (button == 0)
                return CallNextHookEx(hook, code, message, data);

            return ProcessActivation(button, down, mouse.Position, ButtonInput(button, down))
                ? new IntPtr(1) : CallNextHookEx(hook, code, message, data);
        }

        private IntPtr OnKeyboardEvent(int code, IntPtr message, IntPtr data)
        {
            if (code < 0)
                return CallNextHookEx(keyboardHook, code, message, data);
            var key = (KeyboardEvent)Marshal.PtrToStructure(data, typeof(KeyboardEvent));
            if ((key.Flags & 0x10) != 0)
                return CallNextHookEx(keyboardHook, code, message, data);
            bool down = message.ToInt32() == 0x100 || message.ToInt32() == 0x104;
            Point cursor;
            GetCursorPos(out cursor);
            Input input = Key((Keys)key.Key, (key.Flags & 1) | (down ? 0u : KeyUp));
            return ProcessActivation(256 + (int)key.Key, down, cursor, input)
                ? new IntPtr(1) : CallNextHookEx(keyboardHook, code, message, data);
        }

        private bool ProcessActivation(int button, bool down, Point position, Input input)
        {
            if (Recording)
            {
                if (down) pressed.Add(button); else pressed.Remove(button);
                if (down)
                    captured.Add(button);
                if (pressed.Count == 0 && captured.Count != 0)
                {
                    var result = new HashSet<int>(captured);
                    captured.Clear();
                    Recording = false;
                    if (ButtonsRecorded != null)
                        ButtonsRecorded(result);
                }
                return true;
            }

            if (!ActivationButtons.Contains(button) || (enabledActions == MouseAction.None && pending.Count == 0))
                return false;
            if (!down && !pressed.Contains(button))
                return false;
            if (down && pressed.Contains(button))
                return true;

            pending.Add(input);
            if (down) pressed.Add(button); else pressed.Remove(button);
            if (down && pressed.SetEquals(ActivationButtons) && !tracking)
            {
                tracking = true;
                start = position;
            }
            if (pressed.Count == 0)
            {
                MouseAction action = tracking ? Classify(start, position) : MouseAction.MiddleClick;
                tracking = false;
                if (action == MouseAction.MiddleClick)
                    Send(pending.ToArray());
                else if (IsEnabled(action))
                    Native.PostMessage(Handle, WmGesture, new IntPtr((int)action), IntPtr.Zero);
                pending.Clear();
            }
            return true;
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
            if (pending.Count > 0)
                Send(pending.ToArray());
            if (keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(keyboardHook);
                keyboardHook = IntPtr.Zero;
            }
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
        private struct KeyboardEvent
        {
            internal uint Key, ScanCode, Flags, Time;
            internal UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point position);

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
