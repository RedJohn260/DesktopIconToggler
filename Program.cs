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
    private readonly Version currentVersion = new Version("1.0.4");
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
        menu.Items.Add("About", null, (s, e) => {
            using var about = new AboutWindow(currentVersion.ToString());
            about.ShowDialog();
        });
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

        using var progressForm = new UpdateProgressForm();
        progressForm.Show();

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, progressForm.CTS.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync(progressForm.CTS.Token);
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int read;

            var sw = Stopwatch.StartNew();
            long lastTickBytes = 0;
            double lastTickTime = 0;
            double smoothedSpeed = 0;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, progressForm.CTS.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, progressForm.CTS.Token);
                totalRead += read;

                if (sw.Elapsed.TotalMilliseconds - lastTickTime > 500)
                {
                    double timeDelta = (sw.Elapsed.TotalMilliseconds - lastTickTime) / 1000.0;
                    long byteDelta = totalRead - lastTickBytes;
                    double currentSpeed = (byteDelta / 1024.0) / timeDelta;

                    // Weighted Moving Average to fix speed jittering
                    smoothedSpeed = (smoothedSpeed == 0) ? currentSpeed : (smoothedSpeed * 0.7) + (currentSpeed * 0.3);

                    int percent = totalBytes != -1 ? (int)((totalRead * 100) / totalBytes) : 0;
                    string stats = $"{(totalRead / 1024.0 / 1024.0):F2} MB / {(totalBytes / 1024.0 / 1024.0):F2} MB ({smoothedSpeed:F1} KB/s)";

                    progressForm.UpdateProgress(percent, stats);

                    lastTickTime = sw.Elapsed.TotalMilliseconds;
                    lastTickBytes = totalRead;
                }
            }

            sw.Stop();
            fileStream.Close();

            if (progressForm.CTS.IsCancellationRequested) return;

            progressForm.SetStatus("Download Complete!");
            MessageBox.Show("Download successful. Press OK to apply the update and restart.", "Update Ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Update failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (File.Exists(tempPath)) File.Delete(tempPath);
            return;
        }

        // Improved Batch Script with a retry loop to prevent 'File In Use' errors
        string batchScript = $@"
            @echo off
            timeout /t 1 /nobreak > nul
            :loop
            del ""{currentPath}""
            if exist ""{currentPath}"" (
                timeout /t 1 /nobreak > nul
                goto loop
            )
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
public class UpdateProgressForm : Form
{
    private ProgressBar pb;
    private Label lblStatus;
    private Label lblStats;
    private Button btnCancel;
    public CancellationTokenSource CTS { get; private set; }

    public UpdateProgressForm()
    {
        CTS = new CancellationTokenSource();
        this.Text = "Updating Desktop Icon Toggler";
        this.Size = new Size(400, 200);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = true; // Allows minimizing
        this.TopMost = false;

        lblStatus = new Label { Text = "Downloading...", Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.BottomCenter };
        lblStats = new Label { Text = "Initializing...", Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.TopCenter, Font = new Font(this.Font.FontFamily, 8) };

        pb = new ProgressBar { Width = 340, Height = 25, Location = new Point(25, 75), Maximum = 100 };

        btnCancel = new Button { Text = "Cancel Update", Width = 120, Height = 30, Location = new Point(135, 115) };
        btnCancel.Click += (s, e) => { CTS.Cancel(); this.Close(); };

        // Handle the 'X' close button as a cancellation
        this.FormClosing += (s, e) => { if (!CTS.IsCancellationRequested) CTS.Cancel(); };

        this.Controls.Add(pb);
        this.Controls.Add(btnCancel);
        this.Controls.Add(lblStats);
        this.Controls.Add(lblStatus);
    }

    public void UpdateProgress(int percent, string stats)
    {
        if (this.IsDisposed) return;
        if (this.InvokeRequired) { this.Invoke(() => UpdateProgress(percent, stats)); return; }
        pb.Value = percent;
        lblStats.Text = stats;
    }

    public void SetStatus(string text)
    {
        if (this.IsDisposed) return;
        if (this.InvokeRequired) { this.Invoke(() => SetStatus(text)); return; }
        lblStatus.Text = text;
    }
}
public class AboutWindow : Form
{
    public AboutWindow(string version)
    {
        this.Text = "About Desktop Icon Toggler";
        this.Size = new Size(300, 180);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        var lblName = new Label
        {
            Text = "Created by RedJohn260",
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(this.Font, FontStyle.Bold)
        };

        var lblVersion = new Label
        {
            Text = $"Version: {version}",
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var link = new LinkLabel
        {
            Text = "GitHub Repository",
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter
        };
        link.LinkClicked += (s, e) => Process.Start(new ProcessStartInfo("https://github.com/RedJohn260/DesktopIconToggler") { UseShellExecute = true });

        var btnOk = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Dock = DockStyle.Bottom,
            Height = 30
        };

        this.Controls.Add(link);
        this.Controls.Add(lblVersion);
        this.Controls.Add(lblName);
        this.Controls.Add(btnOk);
    }
}