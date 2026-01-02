using Microsoft.Win32;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

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
    private readonly Version currentVersion = new Version("1.0.3");
    private readonly string repoUrl = "https://api.github.com/repos/RedJohn260/DesktopIconToggler/releases/latest";

    private NotifyIcon trayIcon;
    private HotkeyWindow hotkeyHandler = new();

    // Hotkey State
    private Keys currentHotkey = Keys.D;
    private uint currentModifier = 0x0002; // Default: MOD_CONTROL
    private const int HOTKEY_ID = 1;

    // Win32 Modifier Constants
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private ToolStripMenuItem hotkeyMenuItem;

    public TrayContext()
    {
        LoadHotkeySettings(); // Load from Registry

        // Setup Menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Toggle Desktop Icons", null, (s, e) => DesktopManager.ToggleIcons());
        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Run at Windows Startup") { CheckOnClick = true };
        startupItem.Checked = CheckStartup();
        startupItem.Click += (s, e) => SetStartup(startupItem.Checked);
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());

        // Dynamic Hotkey Menu Item
        hotkeyMenuItem = new ToolStripMenuItem("Change Hotkey", null, (s, e) => ShowHotkeySettings());
        menu.Items.Add(hotkeyMenuItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Check for Updates", null, async (s, e) => await CheckForUpdates(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => Exit());

        // Setup Tray Icon
        trayIcon = new NotifyIcon()
        {
            Icon = Properties.Resources.Icon, // Resources icon!
            ContextMenuStrip = menu,
            Visible = true,
            Text = "Desktop Icon Toggler"
        };

        hotkeyHandler.HotkeyPressed += DesktopManager.ToggleIcons;
        RegisterKey(); // Sets up the hotkey and updates the UI strings

        _ = CheckForUpdates(silent: true);
    }

    private string GetModifierName()
    {
        return currentModifier switch
        {
            MOD_CONTROL => "Ctrl",
            MOD_ALT => "Alt",
            MOD_SHIFT => "Shift",
            MOD_WIN => "Win",
            _ => "Ctrl"
        };
    }

    private void RegisterKey()
    {
        hotkeyHandler.Unregister(HOTKEY_ID);
        hotkeyHandler.Register(HOTKEY_ID, currentModifier, (uint)currentHotkey);

        string display = $"{GetModifierName()} + {currentHotkey}";
        if (hotkeyMenuItem != null) hotkeyMenuItem.Text = $"Change Hotkey ({display})";
        trayIcon.Text = $"Desktop Icon Toggler ({display})";
    }

    private void ShowHotkeySettings()
    {
        using var form = new HotkeySettingsForm(currentHotkey, currentModifier);
        if (form.ShowDialog() == DialogResult.OK)
        {
            currentHotkey = form.SelectedKey;
            currentModifier = form.SelectedModifier;
            SaveHotkeySettings();
            RegisterKey();
        }
    }

    // --- REGISTRY PERSISTENCE ---
    private void SaveHotkeySettings()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\DesktopIconToggler");
        key.SetValue("Hotkey", (int)currentHotkey);
        key.SetValue("Modifier", (int)currentModifier);
    }

    private void LoadHotkeySettings()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\DesktopIconToggler");
        if (key != null)
        {
            currentHotkey = (Keys)Convert.ToInt32(key.GetValue("Hotkey") ?? Keys.D);
            currentModifier = Convert.ToUInt32(key.GetValue("Modifier") ?? 0x0002);
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

    // --- UPDATER LOGIC ---
    private async Task CheckForUpdates(bool silent = true)
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "DesktopIconToggler-Updater");

            var response = await client.GetFromJsonAsync<JsonElement>(repoUrl);
            string latestTag = response.GetProperty("tag_name").GetString()?.Replace("v", "") ?? "0.0.0";
            Version latestVersion = new Version(latestTag);

            if (latestVersion > currentVersion)
            {
                var downloadUrl = response.GetProperty("assets")[0].GetProperty("browser_download_url").GetString();
                if (MessageBox.Show($"New version {latestTag} available! Download now?", "DesktopIconToggler: Update Found", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    await PerformUpdate(downloadUrl!);
                }
            }
            else if (!silent) MessageBox.Show("Up to date!", "DesktopIconToggler");
        }
        catch { if (!silent) MessageBox.Show("Could not check for updates.", "DesktopIconToggler"); }
    }

    private async Task PerformUpdate(string url)
    {
        string currentPath = Environment.ProcessPath!;
        string tempPath = currentPath + ".new";
        using (var client = new HttpClient())
        {
            var data = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(tempPath, data);
        }

        string batchScript = $@"
        @echo off
        timeout /t 1 /nobreak > nul
        del ""{currentPath}""
        move ""{tempPath}"" ""{currentPath}""
        start """" ""{currentPath}""
        del ""%~f0""";
        string batchPath = Path.Combine(Path.GetTempPath(), "update_toggler.bat");
        File.WriteAllText(batchPath, batchScript);
        Process.Start(new ProcessStartInfo { FileName = batchPath, CreateNoWindow = true, UseShellExecute = false });
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

        if (handle == IntPtr.Zero)
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
        if (m.Msg == 0x0312) HotkeyPressed?.Invoke();
        base.WndProc(ref m);
    }
}

// --- 4. THE SETTINGS GUI ---
public class HotkeySettingsForm : Form
{
    public Keys SelectedKey { get; private set; }
    public uint SelectedModifier { get; private set; }

    public HotkeySettingsForm(Keys currentKey, uint currentMod)
    {
        this.Text = "Hotkey Settings"; this.Size = new Size(300, 150);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
        this.KeyPreview = true;
        this.TopMost = true;

        SelectedKey = currentKey;
        SelectedModifier = currentMod;

        var lbl = new Label
        {
            Text = "Hold a Modifier (Ctrl/Alt/Shift) \nand press a Key to assign.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        var btn = new Button { Text = "Save Settings", Dock = DockStyle.Bottom };

        this.KeyDown += (s, e) => {
            uint mod = 0;
            if (e.Control) mod = 0x0002;
            else if (e.Alt) mod = 0x0001;
            else if (e.Shift) mod = 0x0004;

            if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu && e.KeyCode != Keys.ShiftKey)
            {
                SelectedKey = e.KeyCode;
                SelectedModifier = mod != 0 ? mod : 0x0002; // Default to Ctrl
                lbl.Text = $"New Hotkey: {e.Modifiers} + {SelectedKey}";
            }
        };

        btn.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
        this.Controls.Add(lbl); this.Controls.Add(btn);
    }
}