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
        private readonly HotkeyWindow hotkey;

        internal TrayApplication()
        {
            hotkey = new HotkeyWindow(CloseForegroundApplication);
            menu = new ContextMenuStrip();
            toggle = new ToolStripMenuItem();
            toggle.Click += (sender, args) => SetActive(!hotkey.Active);
            menu.Items.Add(toggle);
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
        }

        private void SetActive(bool active)
        {
            int error = hotkey.SetActive(active);
            toggle.Text = hotkey.Active ? "Приостановить Alt+Q" : "Включить Alt+Q";
            icon.Text = hotkey.Active ? Program.Name + " — Alt+Q" : Program.Name + " — на паузе";
            if (error != 0)
            {
                string details = error == 1409
                    ? "Сочетание уже занято. Освободите его и выберите «Включить Alt+Q» в трее."
                    : new Win32Exception(error).Message;
                MessageBox.Show(details, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                hotkey.Dispose();
                icon.Visible = false;
                icon.Dispose();
                trayImage.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }
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
