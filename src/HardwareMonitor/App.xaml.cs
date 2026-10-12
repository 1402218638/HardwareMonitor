using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace HardwareMonitor;

public partial class App : Application
{
    private static readonly object LogLock = new();
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HardwareMonitor");
    private static readonly string LogPath = Path.Combine(LogDir, "startup.log");

    private Window? _window;

    public App()
    {
        Log($"App ctor: begin (OS={Environment.OSVersion.VersionString}, 64bit={Environment.Is64BitProcess})");
        InitializeComponent();
        Log("App ctor: InitializeComponent done");

        UnhandledException += (_, e) =>
        {
            Log("XAML UnhandledException: " + e.Message + Environment.NewLine + e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("AppDomain UnhandledException: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("UnobservedTaskException: " + e.Exception);
            e.SetObserved();
        };

        Log("App ctor: end");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched: begin");
        try
        {
            _window = new MainWindow();
            _window.Activate();
            Log("OnLaunched: window activated");
        }
        catch (Exception ex)
        {
            Log("OnLaunched FAILED: " + ex);
            throw;
        }
    }

    /// <summary>把启动与崩溃信息写入 %LOCALAPPDATA%\HardwareMonitor\startup.log，便于远程诊断。</summary>
    internal static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写入失败不影响应用运行
        }
    }
}
