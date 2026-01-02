using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopIconToggler;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}

// --- 1. THE MAIN CONTROLLER ---
public class TrayContext : ApplicationContext
{
    private NotifyIcon trayIcon;
    private HotkeyWindow hotkeyHandler = new();
    private Keys currentHotkey = Keys.D; // Default: Ctrl + D
    private const int HOTKEY_ID = 1;

    public TrayContext()
    {
        // Setup Menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle Icons Now", null, (s, e) => DesktopManager.ToggleIcons());
        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Run at Startup") { CheckOnClick = true };
        startupItem.Checked = CheckStartup();
        startupItem.Click += (s, e) => SetStartup(startupItem.Checked);
        menu.Items.Add(startupItem);

        menu.Items.Add("Change Hotkey (Ctrl + ?)", null, (s, e) => ShowHotkeySettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Exit());

        // Setup Tray Icon
        trayIcon = new NotifyIcon()
        {
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
            Text = "Desktop Toggler (Ctrl+D)"
        };

        // Listen for the hotkey press
        hotkeyHandler.HotkeyPressed += DesktopManager.ToggleIcons;
        RegisterKey();
    }

    private void RegisterKey()
    {
        hotkeyHandler.Unregister(HOTKEY_ID);
        // MOD_CONTROL = 0x0002
        hotkeyHandler.Register(HOTKEY_ID, 0x0002, (uint)currentHotkey);
        trayIcon.Text = $"Desktop Toggler (Ctrl + {currentHotkey})";
    }

    private void ShowHotkeySettings()
    {
        using var form = new HotkeySettingsForm(currentHotkey);
        if (form.ShowDialog() == DialogResult.OK)
        {
            currentHotkey = form.SelectedKey;
            RegisterKey();
        }
    }

    private bool CheckStartup() => Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")?.GetValue("DesktopToggler") != null;

    private void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        if (enable) key?.SetValue("DesktopToggler", Application.ExecutablePath);
        else key?.DeleteValue("DesktopToggler", false);
    }

    private void Exit()
    {
        trayIcon.Visible = false;
        Application.Exit();
    }
}

// --- 2. THE WIN32 TOGGLE LOGIC ---
public static class DesktopManager
{
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr h1, IntPtr h2, string lpsz1, string? lpsz2);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static void ToggleIcons()
    {
        IntPtr handle = FindWindow("Progman", "Program Manager");
        handle = FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (handle == IntPtr.Zero) // Windows 10/11 multi-monitor fix
        {
            IntPtr workerW = IntPtr.Zero;
            while ((workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null)) != IntPtr.Zero)
            {
                handle = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (handle != IntPtr.Zero) break;
            }
        }
        SendMessage(handle, 0x111, (IntPtr)0x7402, IntPtr.Zero);
    }
}

// --- 3. THE GLOBAL HOTKEY HANDLER ---
public class HotkeyWindow : NativeWindow
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public event Action? HotkeyPressed;
    public HotkeyWindow() => CreateHandle(new CreateParams());

    public void Register(int id, uint mod, uint key) => RegisterHotKey(Handle, id, mod, key);
    public void Unregister(int id) => UnregisterHotKey(Handle, id);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312) HotkeyPressed?.Invoke(); // WM_HOTKEY
        base.WndProc(ref m);
    }
}

// --- 4. THE SETTINGS GUI ---
public class HotkeySettingsForm : Form
{
    public Keys SelectedKey { get; private set; }
    public HotkeySettingsForm(Keys current)
    {
        this.Text = "Settings"; this.Size = new Size(250, 120);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        this.KeyPreview = true;
        SelectedKey = current;

        var lbl = new Label { Text = $"Press a key (Ctrl + ...)\nCurrent: {current}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
        var btn = new Button { Text = "Save", Dock = DockStyle.Bottom };

        this.KeyDown += (s, e) => { if (e.KeyCode != Keys.ControlKey) { SelectedKey = e.KeyCode; lbl.Text = $"New Hotkey: Ctrl + {SelectedKey}"; } };
        btn.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };

        this.Controls.Add(lbl); this.Controls.Add(btn);
    }
}