using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Principal;
using System.Threading.Tasks;
using HardwareMonitor.Controls;
using HardwareMonitor.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace HardwareMonitor;

public sealed partial class MainWindow : Window
{
    private const int MaxHistoryCount = 120;

    private static readonly SolidColorBrush TempNormalBrush = MakeSolidColor("#E8E8E8");
    private static readonly SolidColorBrush TempWarmBrush = MakeSolidColor("#FFB900");
    private static readonly SolidColorBrush TempHotBrush = MakeSolidColor("#FF6B6B");

    private readonly HardwareMonitorService _hardwareService = new();
    private readonly List<double> _cpuTempHistory = new();
    private readonly List<double> _gpuTempHistory = new();
    private readonly List<double> _batteryPowerHistory = new();
    private readonly DispatcherTimer _timer = new();

    private bool _updating;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Hardware Monitor";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1240, 720));

        _hardwareService.Start();

        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Closed += (_, _) =>
        {
            _timer.Stop();
            _hardwareService.Dispose();
        };

        if (!IsElevated())
            AdminInfoBar.IsOpen = true;

        _ = TickOnceAsync();
    }

    private void OnTimerTick(object? sender, object e) => _ = TickOnceAsync();

    private async Task TickOnceAsync()
    {
        if (_updating)
            return;
        _updating = true;
        try
        {
            var (hardware, battery) = await Task.Run(() => (_hardwareService.Read(), BatteryService.Read()));
            ApplySnapshot(hardware, battery);
            UpdateTimeText.Text = $"更新于 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            UpdateTimeText.Text = "更新失败：" + ex.Message;
        }
        finally
        {
            _updating = false;
        }
    }

    private void ApplySnapshot(HardwareSnapshot hw, BatterySnapshot battery)
    {
        // CPU
        CpuNameText.Text = string.IsNullOrEmpty(hw.CpuName) ? "未检测到 CPU" : hw.CpuName;
        PushHistory(_cpuTempHistory, hw.CpuTemp);
        CpuTempText.Text = Format(hw.CpuTemp, "0");
        CpuTempText.Foreground = TempBrush(hw.CpuTemp);
        CpuTempSpark.SetData(_cpuTempHistory);
        CpuPowerText.Text = Format(hw.CpuPower, "0.0") + " W";
        CpuLoadText.Text = Format(hw.CpuLoad, "0") + " %";
        CpuLoadBar.Value = Math.Clamp(hw.CpuLoad ?? 0, 0, 100);
        CpuClockText.Text = FormatClock(hw.CpuClock);

        // GPU
        GpuNameText.Text = string.IsNullOrEmpty(hw.GpuName) ? "未检测到 GPU" : hw.GpuName;
        PushHistory(_gpuTempHistory, hw.GpuTemp);
        GpuTempText.Text = Format(hw.GpuTemp, "0");
        GpuTempText.Foreground = TempBrush(hw.GpuTemp);
        GpuTempSpark.SetData(_gpuTempHistory);
        GpuPowerText.Text = Format(hw.GpuPower, "0.0") + " W";
        GpuLoadText.Text = Format(hw.GpuLoad, "0") + " %";
        GpuLoadBar.Value = Math.Clamp(hw.GpuLoad ?? 0, 0, 100);
        GpuClockText.Text = FormatClock(hw.GpuClock);

        // 电池
        BatteryStatusText.Text = battery.StateText;
        switch (battery.State)
        {
            case BatteryState.Charging:
                SetBatteryPill("#336CCB5F", "#6CCB5F", "充电中");
                break;
            case BatteryState.Discharging:
                SetBatteryPill("#33FFB900", "#FFB900", "放电中");
                break;
            case BatteryState.Idle:
                SetBatteryPill("#33808080", "#C8C8C8", "已接通电源");
                break;
            case BatteryState.NotPresent:
                SetBatteryPill("#33FF6B6B", "#FF6B6B", "未接入");
                break;
            default:
                SetBatteryPill("#33808080", "#C8C8C8", "未知");
                break;
        }
        PushHistory(_batteryPowerHistory, battery.PowerW);
        BatteryPowerText.Text = battery.PowerW is null
            ? "--"
            : (battery.PowerW.Value > 0 ? "+" : "") + battery.PowerW.Value.ToString("0.0", CultureInfo.InvariantCulture);
        BatteryPowerSpark.SetData(_batteryPowerHistory);
        BatteryBar.Value = Math.Clamp(battery.Percent ?? 0, 0, 100);
        BatteryPercentText.Text = battery.Percent is null
            ? "-- %"
            : battery.Percent.Value.ToString("0", CultureInfo.InvariantCulture) + " %";
        BatteryCapacityText.Text = battery.RemainingWh is null || battery.FullWh is null
            ? "-- Wh / -- Wh"
            : $"{battery.RemainingWh:0.#} Wh / {battery.FullWh:0.#} Wh";

        // 调试视图仅在展开时刷新，避免不必要的开销
        if (SensorExpander.IsExpanded)
            SensorList.ItemsSource = hw.AllSensors;
    }

    private static void PushHistory(List<double> history, double? value)
    {
        // 读数缺失时沿用上一次的值，保持曲线连续
        history.Add(value ?? (history.Count > 0 ? history[^1] : 0));
        if (history.Count > MaxHistoryCount)
            history.RemoveAt(0);
    }

    private static Brush TempBrush(double? temperature) => temperature switch
    {
        >= 80 => TempHotBrush,
        >= 60 => TempWarmBrush,
        _ => TempNormalBrush,
    };

    private static string Format(double? value, string format)
        => value is null or double.NaN ? "--" : value.Value.ToString(format, CultureInfo.InvariantCulture);

    private static string FormatClock(double? megahertz)
        => megahertz is null or double.NaN ? "--" : (megahertz.Value / 1000).ToString("0.00", CultureInfo.InvariantCulture);

    private void SetBatteryPill(string backgroundHex, string foregroundHex, string text)
    {
        BatteryPill.Background = MakeSolidColor(backgroundHex);
        BatteryPillText.Text = text;
        BatteryPillText.Foreground = MakeSolidColor(foregroundHex);
    }

    private void IntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_timer is null)
            return;
        var seconds = IntervalCombo.SelectedIndex switch { 1 => 2, 2 => 5, _ => 1 };
        _timer.Interval = TimeSpan.FromSeconds(seconds);
    }

    private void TopMostToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = TopMostToggle.IsChecked == true;
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static SolidColorBrush MakeSolidColor(string hex)
        => new(ParseHexColor(hex));

    private static Windows.UI.Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        var alpha = (byte)255;
        if (hex.Length == 8)
        {
            alpha = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            hex = hex[2..];
        }
        return Windows.UI.Color.FromArgb(
            alpha,
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}
