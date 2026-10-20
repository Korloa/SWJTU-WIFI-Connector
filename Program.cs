using System.Drawing.Drawing2D;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace SwjtuKeeper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Local\\SWJTU-Keeper-8BE7A19D", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("SWJTU 保活已在运行，请查看任务栏折叠图标。", "SWJTU 保活");
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new KeeperContext());
    }
}

internal sealed class KeeperSettings
{
    public bool Enabled { get; set; } = true;
    public string WifiName { get; set; } = "SWJTU-WiFi";
    public int IntervalSeconds { get; set; } = 30;
}

internal static class SettingsStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SWJTU-Keeper");
    private static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly string LogPath = Path.Combine(DirectoryPath, "keeper.log");

    public static KeeperSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<KeeperSettings>(File.ReadAllText(SettingsPath)) ?? new KeeperSettings();
        }
        catch (IOException) { return new KeeperSettings(); }
        catch (JsonException) { return new KeeperSettings(); }
    }

    public static void Save(KeeperSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 262144)
                File.Delete(LogPath);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class KeeperContext : ApplicationContext
{
    private static readonly Uri Portal = new("http://192.168.189.136/");
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseCookies = false,
        UseProxy = false
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem enabledItem;
    private readonly ToolStripMenuItem startupItem;
    private readonly ToolStripMenuItem statusItem;
    private readonly System.Windows.Forms.Timer timer;
    private KeeperSettings settings = SettingsStore.Load();
    private SettingsForm? settingsForm;
    private CancellationTokenSource? checkCancellation;
    private bool checking;
    private bool stopping;
    private bool authNotified;
    private int authFailureCount;
    private string status = "正在启动";
    private Color iconColor = Color.DarkOrange;

    public KeeperContext()
    {
        if (string.IsNullOrWhiteSpace(settings.WifiName))
            settings.WifiName = "SWJTU-WiFi";
        settings.IntervalSeconds = Math.Clamp(settings.IntervalSeconds, 30, 300);

        statusItem = new ToolStripMenuItem(status) { Enabled = false };
        enabledItem = new ToolStripMenuItem("自动连接与保活") { Checked = settings.Enabled, CheckOnClick = false };
        enabledItem.Click += (_, _) => SetEnabled(!settings.Enabled);
        startupItem = new ToolStripMenuItem("开机自动启动") { Checked = Startup.IsEnabled(), CheckOnClick = false };
        startupItem.Click += (_, _) =>
        {
            try { SetStartWithWindows(!startupItem.Checked); }
            catch (Exception error)
            {
                MessageBox.Show($"修改开机启动失败：{error.Message}", "SWJTU 保活", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(enabledItem);
        menu.Items.Add(startupItem);
        menu.Items.Add("立即检查", null, async (_, _) => await CheckAsync());
        menu.Items.Add("设置与状态", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());

        tray = new NotifyIcon
        {
            Icon = CreateIcon(iconColor),
            Text = "SWJTU 保活：正在启动",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowSettings();

        timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += async (_, _) => await CheckAsync();
        if (settings.Enabled)
            timer.Start();
        else
            SetStatus("已暂停", Color.Gray);

        SettingsStore.Log("程序启动");
    }

    public string Status => status;
    public KeeperSettings Settings => settings;

    public void SetEnabled(bool enabled)
    {
        settings.Enabled = enabled;
        enabledItem.Checked = enabled;
        SaveSettings();
        if (enabled)
        {
            SetStatus("正在检查", Color.DarkOrange);
            timer.Interval = 1000;
            timer.Start();
        }
        else
        {
            timer.Stop();
            checkCancellation?.Cancel();
            authFailureCount = 0;
            authNotified = false;
            SetStatus("已暂停", Color.Gray);
        }
        settingsForm?.RefreshState();
    }

    public void UpdateSettings(string wifiName, int intervalSeconds, bool startWithWindows)
    {
        SetStartWithWindows(startWithWindows);
        settings.WifiName = wifiName.Trim();
        settings.IntervalSeconds = intervalSeconds;
        SaveSettings();
        if (settings.Enabled)
        {
            timer.Interval = 1000;
            timer.Start();
        }
    }

    private void SetStartWithWindows(bool enabled)
    {
        Startup.SetEnabled(enabled);
        startupItem.Checked = Startup.IsEnabled();
        settingsForm?.SyncStartWithWindows(startupItem.Checked);
    }

    private void SaveSettings()
    {
        try { SettingsStore.Save(settings); }
        catch (Exception error) { SettingsStore.Log($"保存设置失败：{error.Message}"); }
    }

    private void ShowSettings()
    {
        if (settingsForm is null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(this);
            settingsForm.FormClosed += (_, _) => settingsForm = null;
        }
        settingsForm.Show();
        settingsForm.Activate();
    }

    private async Task CheckAsync()
    {
        if (checking || !settings.Enabled || stopping)
            return;

        checking = true;
        timer.Stop();
        using var cancellation = new CancellationTokenSource();
        checkCancellation = cancellation;
        var nextInterval = 20000;

        try
        {
            var wifiName = settings.WifiName;
            var connectedSsid = WifiConnection.GetConnectedSsid();
            if (connectedSsid is not null && !string.Equals(connectedSsid, wifiName, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"当前为 {connectedSsid}，等待 {wifiName}", Color.DarkOrange);
                return;
            }

            if (connectedSsid is null)
            {
                SetStatus($"正在连接 {wifiName}", Color.DarkOrange);
                WifiConnection.Connect(wifiName);
                for (var attempt = 0; attempt < 15; attempt++)
                {
                    await Task.Delay(1000, cancellation.Token);
                    if (string.Equals(WifiConnection.GetConnectedSsid(), wifiName, StringComparison.OrdinalIgnoreCase))
                        break;
                }
                if (!string.Equals(WifiConnection.GetConnectedSsid(), wifiName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"未能连接 {wifiName}，请确认已保存该 Wi-Fi 配置文件");
            }

            SetStatus("正在访问认证页", Color.DarkOrange);
            using var request = new HttpRequestMessage(HttpMethod.Get, Portal);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            var finalUri = response.RequestMessage?.RequestUri;
            if (response.IsSuccessStatusCode && finalUri?.AbsolutePath.Contains("/eportal/success.jsp", StringComparison.OrdinalIgnoreCase) == true)
            {
                authNotified = false;
                authFailureCount = 0;
                SetStatus($"{wifiName} 已连接 · {DateTime.Now:HH:mm:ss}", Color.ForestGreen);
                nextInterval = settings.IntervalSeconds * 1000;
            }
            else
            {
                authFailureCount++;
                SettingsStore.Log($"认证页返回 {(int)response.StatusCode} {finalUri?.AbsolutePath}");
                if (authFailureCount == 1)
                {
                    SetStatus("认证未恢复，正在重试", Color.DarkOrange);
                    nextInterval = 5000;
                }
                else
                {
                    SetStatus("需要网页认证", Color.Firebrick);
                    if (!authNotified)
                    {
                        authNotified = true;
                        tray.ShowBalloonTip(4000, "SWJTU 需要网页认证", "请自行访问校园网认证地址完成登录。", ToolTipIcon.Warning);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            SetStatus($"连接失败：{error.Message}", Color.Firebrick);
        }
        finally
        {
            checkCancellation = null;
            checking = false;
            if (settings.Enabled && !stopping)
            {
                timer.Interval = nextInterval;
                timer.Start();
            }
        }
    }

    private void SetStatus(string message, Color color)
    {
        if (status == message && iconColor == color)
            return;

        status = message;
        statusItem.Text = message;
        tray.Text = "SWJTU 保活：" + message[..Math.Min(message.Length, 48)];
        if (iconColor != color)
        {
            var oldIcon = tray.Icon;
            tray.Icon = CreateIcon(color);
            oldIcon?.Dispose();
            iconColor = color;
        }
        settingsForm?.RefreshState();
        SettingsStore.Log(message);
    }

    protected override void ExitThreadCore()
    {
        stopping = true;
        timer.Stop();
        checkCancellation?.Cancel();
        tray.Visible = false;
        tray.Icon?.Dispose();
        tray.Dispose();
        timer.Dispose();
        settingsForm?.Close();
        SettingsStore.Log("程序退出");
        base.ExitThreadCore();
    }

    private static Icon CreateIcon(Color color)
    {
        using var image = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(image))
        using (var brush = new SolidBrush(color))
        using (var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            TextRenderer.DrawText(graphics, "W", font, new Rectangle(2, 4, 28, 23), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        var handle = image.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}

internal static class Startup
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SWJTU-Keeper";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunPath);
        if (enabled)
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
