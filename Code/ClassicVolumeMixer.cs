using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("Classic Volume Mixer")]
[assembly: AssemblyProduct("Classic Volume Mixer")]
[assembly: AssemblyVersion("0.2.3.2")]
[assembly: AssemblyFileVersion("0.2.3.2")]

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
        string user;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) user = identity.User.Value;
        bool created;
        using (var instance = new Mutex(true, @"Local\ClassicVolumeMixer-" + user, out created))
        {
            if (!created)
            {
                instance.Dispose(); // A secondary notice must not keep the single-instance name alive.
                MessageBox.Show("Classic Volume Mixer is already running. Look for its speaker icon in the tray or the hidden-icons menu.", "Classic Volume Mixer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var tray = new MixerTray())
                {
                    tray.ShowWelcomeIfNeeded();
                    Application.Run(tray);
                }
            }
            finally { instance.ReleaseMutex(); }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();
}

internal sealed class MixerTray : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly Icon icon;
    private readonly System.Windows.Forms.Timer placement = new System.Windows.Forms.Timer { Interval = 80 };
    private readonly string mixerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sndvol.exe");
    private Process mixerProcess;
    private Rectangle targetArea;
    private int placementStarted;
    private int placementPasses;
    private readonly int sessionId;
    private bool foregroundAttempted;

    public MixerTray()
    {
        icon = MakeIcon();
        using (Process current = Process.GetCurrentProcess()) sessionId = current.SessionId;
        placement.Tick += delegate { PlaceMixer(); };
        menu = new ContextMenuStrip();
        menu.Items.Add("Open Mixer", null, delegate { OpenMixer(); });
        menu.Items.Add("Keep speaker visible...", null, delegate { ShowVisibilityPage(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, delegate { ExitThread(); });
        tray = new NotifyIcon
        {
            Text = "Classic Volume Mixer",
            Icon = icon,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.MouseClick += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) OpenMixer();
        };
    }

    private void OpenMixer()
    {
        try
        {
            targetArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (mixerProcess != null && mixerProcess.HasExited)
            {
                mixerProcess.Dispose();
                mixerProcess = null;
            }
            if (mixerProcess == null)
            {
                foreach (Process candidate in Process.GetProcessesByName("sndvol"))
                {
                    if (mixerProcess == null && IsSystemMixer(candidate) && candidate.MainWindowHandle != IntPtr.Zero
                        && IsWindowVisible(candidate.MainWindowHandle) && !IsHungAppWindow(candidate.MainWindowHandle)) mixerProcess = candidate;
                    else candidate.Dispose();
                }
            }
            if (mixerProcess == null)
            {
                if (!File.Exists(mixerPath)) throw new FileNotFoundException("Windows' classic volume mixer was not found.", mixerPath);
                mixerProcess = Process.Start(new ProcessStartInfo(mixerPath) { UseShellExecute = false });
            }
            placementStarted = Environment.TickCount;
            placementPasses = 0;
            foregroundAttempted = false;
            placement.Start();
        }
        catch (Exception error)
        {
            ClearPending();
            MessageBox.Show("Could not open the classic mixer.\n\n" + error.Message, "Classic Volume Mixer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool IsSystemMixer(Process process)
    {
        try
        {
            return !process.HasExited && process.SessionId == sessionId
                && string.Equals(process.MainModule.FileName, mixerPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private void PlaceMixer()
    {
        try
        {
            if (unchecked(Environment.TickCount - placementStarted) > 3000)
            {
                ClearPending();
                tray.ShowBalloonTip(4000, "Classic Volume Mixer", "The mixer did not become ready or finish positioning. Close any unresponsive mixer window and click the tray icon to retry.", ToolTipIcon.Warning);
                return;
            }
            if (mixerProcess != null && mixerProcess.HasExited)
            {
                ClearPending();
                tray.ShowBalloonTip(4000, "Classic Volume Mixer", "The mixer closed before it was ready. Click to try again.", ToolTipIcon.Warning);
                return;
            }
            if (mixerProcess == null || !IsSystemMixer(mixerProcess)) return;
            mixerProcess.Refresh();
            IntPtr window = mixerProcess.MainWindowHandle;
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (window == IntPtr.Zero || owner != (uint)mixerProcess.Id || !IsWindowVisible(window)) return;
            if (IsIconic(window)) { ShowWindowAsync(window, 9); return; }
            if (!foregroundAttempted) { SetForegroundWindow(window); foregroundAttempted = true; }
            NativeRect bounds;
            if (!GetWindowRect(window, out bounds)) return;
            NativeRect visible;
            // GetWindowRect includes invisible resize borders. Align the visible frame.
            if (DwmGetWindowAttribute(window, 9, out visible, Marshal.SizeOf(typeof(NativeRect))) != 0)
                visible = bounds;
            int x = Math.Max(targetArea.Left - (visible.Left - bounds.Left), bounds.Left + targetArea.Right - visible.Right);
            int y = Math.Max(targetArea.Top - (visible.Top - bounds.Top), bounds.Top + targetArea.Bottom - visible.Bottom);
            if (placementPasses > 0 && bounds.Left == x && bounds.Top == y)
            {
                ClearPending();
                return;
            }
            if (!SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x4000))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            // A monitor change can resize a DPI-aware window. Correct using its new bounds.
            ++placementPasses;
        }
        catch (Exception error)
        {
            ClearPending();
            tray.ShowBalloonTip(4000, "Classic Volume Mixer", "Could not position the mixer: " + error.Message, ToolTipIcon.Warning);
        }
    }

    private void ClearPending()
    {
        placement.Stop();
        if (mixerProcess != null) { mixerProcess.Dispose(); mixerProcess = null; }
    }

    internal void ShowWelcomeIfNeeded()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicVolumeMixer");
        string marker = Path.Combine(directory, "welcome-v3");
        if (File.Exists(marker)) return;
        ShowVisibilityPage();
        try { Directory.CreateDirectory(directory); File.WriteAllText(marker, "Shown"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ShowVisibilityPage()
    {
        using (var baseFont = new Font("Segoe UI", 10))
        using (var titleFont = new Font("Segoe UI", 17, FontStyle.Bold))
        using (var hintFont = new Font("Segoe UI", 9))
        using (var page = new Form())
        {
            page.Text = "Classic Volume Mixer setup";
            page.Icon = icon;
            page.ClientSize = new Size(500, 252);
            page.Font = baseFont;
            page.FormBorderStyle = FormBorderStyle.FixedDialog;
            page.MaximizeBox = false;
            page.MinimizeBox = false;
            page.StartPosition = FormStartPosition.CenterScreen;
            page.AutoScaleMode = AutoScaleMode.Dpi;
            page.BackColor = Color.White;
            var title = new Label { Text = "Keep the speaker visible", AutoSize = true,
                Location = new Point(26, 24), Font = titleFont };
            var description = new Label { Text = "Keep the blue speaker beside the clock.",
                Location = new Point(28, 73), Size = new Size(444, 40) };
            var action = new Button { Text = "Show my speaker icon", Location = new Point(28, 123),
                Size = new Size(444, 48), BackColor = Color.FromArgb(0, 103, 192), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, TabIndex = 0 };
            action.FlatAppearance.BorderSize = 0;
            var hint = new Label { Text = "One click. No Windows Settings.",
                Location = new Point(28, 185), Size = new Size(360, 43), Font = hintFont };
            var later = new Button { Text = "Later", Location = new Point(402, 198), Size = new Size(70, 30),
                DialogResult = DialogResult.Cancel, TabIndex = 1 };
            action.Click += async delegate
            {
                action.Enabled = false;
                hint.Text = "Saving...";
                bool verified = false;
                try
                {
                    if (SetIconPreference(Application.ExecutablePath))
                    {
                        tray.Visible = false;
                        tray.Visible = true;
                        for (int attempt = 0; attempt < 15; attempt++)
                        {
                            await Task.Delay(100);
                            if (page.IsDisposed) return;
                            if (VerifyIconPreference(Application.ExecutablePath)) { verified = true; break; }
                        }
                    }
                    if (page.IsDisposed) return;
                    hint.Text = verified ? "Preference saved. Look for the blue speaker by the clock."
                        : "Windows hasn't saved this icon yet. Try again.";
                }
                catch (Exception error)
                {
                    if (page.IsDisposed) return;
                    hint.Text = "Windows couldn't save this preference. Try again.";
                    Debug.WriteLine(error);
                }
                action.Enabled = true;
                action.Text = verified ? "Show my speaker icon" : "Try again";
                later.Text = "Done";
            };
            page.Controls.AddRange(new Control[] { title, description, action, hint, later });
            page.AcceptButton = action;
            page.CancelButton = later;
            page.ShowDialog();
        }
    }

    // Windows 11 implementation detail. Only an explicit user click changes it.
    internal static bool SetIconPreference(string executable)
    {
        bool found = false;
        string expected = CanonicalLocalFile(executable);
        if (expected == null) return false;
        using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", false))
        {
            if (root == null) return false;
            foreach (string name in root.GetSubKeyNames())
            {
                using (RegistryKey record = root.OpenSubKey(name, false))
                {
                    if (!MatchesIcon(record, executable, expected)) continue;
                }
                using (RegistryKey writable = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings\" + name, true))
                {
                    // Explorer can remove/reassign a record between the two opens.
                    if (!MatchesIcon(writable, executable, expected)) continue;
                    if (!IsPromoted(writable)) writable.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    found = true;
                }
            }
        }
        return found;
    }

    internal static bool VerifyIconPreference(string executable)
    {
        bool found = false;
        string expected = CanonicalLocalFile(executable);
        if (expected == null) return false;
        using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", false))
        {
            if (root == null) return false;
            foreach (string name in root.GetSubKeyNames())
            using (RegistryKey record = root.OpenSubKey(name, false))
            {
                if (!MatchesIcon(record, executable, expected)) continue;
                found = true;
                if (!IsPromoted(record)) return false;
            }
        }
        return found;
    }

    private static bool IsPromoted(RegistryKey record)
    {
        object value = record.GetValue("IsPromoted");
        return value is int && (int)value == 1 && record.GetValueKind("IsPromoted") == RegistryValueKind.DWord;
    }

    private static bool MatchesIcon(RegistryKey record, string executable, string expected)
    {
        if (record == null) return false;
        string path = record.GetValue("ExecutablePath") as string;
        if (string.IsNullOrEmpty(path)) return false;
        // Never open registry-supplied files: a local symlink could lead to a network share.
        string candidate = LocalPathSpelling(path);
        string original = LocalPathSpelling(executable);
        string canonical = expected.StartsWith(@"\\?\", StringComparison.Ordinal) ? expected.Substring(4) : expected;
        // Exact spelling avoids selecting a distinct file in a case-sensitive directory.
        return candidate != null && (string.Equals(candidate, original, StringComparison.Ordinal)
            || string.Equals(candidate, canonical, StringComparison.Ordinal));
    }

    private static string LocalPathSpelling(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 3 || !char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\') return null;
        // Even GetFullPath can access the filesystem for short names on Framework.
        // Registry candidates are compared as raw strings, with no filesystem queries.
        return path;
    }

    private static string CanonicalLocalFile(string path)
    {
        // Called only for our trusted executable, never for a registry-supplied candidate.
        try
        {
            if (string.IsNullOrEmpty(path) || path.Length < 3 || !char.IsLetter(path[0]) || path[1] != ':' || path[2] != '\\') return null;
            string full = Path.GetFullPath(path);
            if (new DriveInfo(Path.GetPathRoot(full)).DriveType == DriveType.Network) return null;
            using (FileStream file = File.Open(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buffer = new StringBuilder(1024);
                uint length = GetFinalPathNameByHandle(file.SafeFileHandle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity) return null;
                return buffer.ToString();
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect bounds, int size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr window);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            placement.Stop();
            placement.Dispose();
            if (mixerProcess != null) mixerProcess.Dispose();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Icon MakeIcon()
    {
        using (var bitmap = new Bitmap(32, 32))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var fill = new SolidBrush(Color.FromArgb(65, 160, 235)))
        using (var outline = new Pen(Color.FromArgb(20, 65, 100), 1.3f))
        using (var waves = new Pen(Color.FromArgb(65, 160, 235), 2.8f))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Point[] speaker = { new Point(3, 12), new Point(9, 12), new Point(17, 5), new Point(17, 27), new Point(9, 20), new Point(3, 20) };
            graphics.FillPolygon(fill, speaker);
            graphics.DrawPolygon(outline, speaker);
            graphics.DrawArc(waves, 16, 8, 10, 16, -65, 130);
            graphics.DrawArc(waves, 17, 3, 14, 26, -65, 130);
            IntPtr handle = bitmap.GetHicon();
            try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
            finally { DestroyIcon(handle); }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
