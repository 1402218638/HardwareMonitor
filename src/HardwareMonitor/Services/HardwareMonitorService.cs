using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor.Services;

/// <summary>一次硬件采样快照（在后台线程生成，回到 UI 线程渲染）。</summary>
public sealed class HardwareSnapshot
{
    public string CpuName { get; init; } = "";
    public double? CpuTemp { get; init; }
    public double? CpuPower { get; init; }
    public double? CpuLoad { get; init; }
    public double? CpuClock { get; init; }

    public string GpuName { get; init; } = "";
    public double? GpuTemp { get; init; }
    public double? GpuPower { get; init; }
    public double? GpuLoad { get; init; }
    public double? GpuClock { get; init; }

    public IReadOnlyList<string> AllSensors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 基于 LibreHardwareMonitorLib（OpenHardwareMonitor 的活跃分支）的传感器读取服务。
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private Computer? _computer;

    public void Start()
    {
        if (_computer is not null)
            return;

        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsBatteryEnabled = true,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsControllerEnabled = false,
            IsPsuEnabled = false,
        };

        try
        {
            computer.Open();
            _computer = computer;
            App.Log($"HardwareMonitorService: computer opened, hardware count={computer.Hardware.Length}");
        }
        catch (Exception ex)
        {
            App.Log("HardwareMonitorService: computer.Open FAILED: " + ex);
        }
    }

    public HardwareSnapshot Read()
    {
        double? cpuTemp = null, cpuPower = null, cpuLoad = null, cpuClock = null;
        double? gpuTemp = null, gpuPower = null, gpuLoad = null, gpuClock = null;
        string cpuName = "", gpuName = "";
        var lines = new List<string>();

        if (_computer is not null)
        {
            foreach (var hardware in _computer.Hardware)
            {
                try
                {
                    hardware.Update();
                }
                catch
                {
                    // 单个硬件读取失败不影响其余设备
                }
            }

            var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu is not null)
            {
                cpuName = cpu.Name;
                var sensors = cpu.Sensors;

                // 不同平台传感器命名不同，按优先级匹配，找不到再取候选集合的最大值
                cpuTemp = Pick(sensors, SensorType.Temperature,
                    new[] { "CPU Package", "Core Max", "Tctl/Tdie", "Tctl", "Tdie" },
                    n => n.Contains("Core") || n.Contains("Tctl") || n.Contains("Tdie"));
                cpuPower = Pick(sensors, SensorType.Power,
                    new[] { "CPU Package", "Package Power", "Package", "CPU PPT", "PPT" },
                    n => !n.Contains("Core #") && !n.Contains("I/O"));
                cpuLoad = Pick(sensors, SensorType.Load,
                    new[] { "CPU Total", "Total" }, null);
                cpuClock = Pick(sensors, SensorType.Clock,
                    Array.Empty<string>(), n => n.Contains("Core"));
            }

            // 多显卡时优先独立显卡（非 Intel），再按传感器丰富程度挑选
            var gpu = _computer.Hardware
                .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                .OrderByDescending(h => h.HardwareType != HardwareType.GpuIntel)
                .ThenByDescending(h => h.Sensors.Count(s =>
                    s.SensorType is SensorType.Temperature or SensorType.Load or SensorType.Power or SensorType.Clock))
                .FirstOrDefault();

            if (gpu is not null)
            {
                gpuName = gpu.Name;
                var sensors = gpu.Sensors;
                gpuTemp = Pick(sensors, SensorType.Temperature,
                    new[] { "GPU Core" }, null);
                gpuPower = Pick(sensors, SensorType.Power,
                    new[] { "GPU Power", "GPU Package", "Board Power", "GPU PPT", "PPT", "Package" }, null);
                gpuLoad = Pick(sensors, SensorType.Load,
                    new[] { "GPU Core", "D3D" }, n => !n.Contains("Memory"));
                gpuClock = Pick(sensors, SensorType.Clock,
                    new[] { "GPU Core" }, n => !n.Contains("Memory"));
            }

            CollectSensorLines(_computer.Hardware, lines);
        }

        return new HardwareSnapshot
        {
            CpuName = cpuName,
            CpuTemp = cpuTemp,
            CpuPower = cpuPower,
            CpuLoad = cpuLoad,
            CpuClock = cpuClock,
            GpuName = gpuName,
            GpuTemp = gpuTemp,
            GpuPower = gpuPower,
            GpuLoad = gpuLoad,
            GpuClock = gpuClock,
            AllSensors = lines,
        };
    }

    /// <summary>按名称优先级挑选传感器，失败时在候选集合（或全部同类传感器）中取最大值。</summary>
    private static double? Pick(ISensor[] sensors, SensorType type, string[] preferredNames, Func<string, bool>? candidateFilter)
    {
        var pool = sensors.Where(s => s.SensorType == type && s.Value.HasValue).ToArray();

        foreach (var name in preferredNames)
        {
            var match = pool.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Value;
        }

        var candidates = candidateFilter is null ? pool : pool.Where(s => candidateFilter(s.Name)).ToArray();
        if (candidates.Length == 0)
            candidates = pool;

        return candidates.Length == 0 ? null : candidates.Max(s => s.Value!.Value);
    }

    private static void CollectSensorLines(IEnumerable<IHardware> hardwares, List<string> lines)
    {
        foreach (var hardware in hardwares)
        {
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.Value is null)
                    continue;
                var hardwareName = Truncate(hardware.Name, 24).PadRight(24);
                var sensorName = Truncate(sensor.Name, 32).PadRight(32);
                lines.Add($"{hardwareName}  {sensorName}  {FormatSensor(sensor)}");
            }

            CollectSensorLines(hardware.SubHardware, lines);
        }
    }

    private static string FormatSensor(ISensor sensor)
    {
        var unit = sensor.SensorType switch
        {
            SensorType.Voltage => "V",
            SensorType.Current => "A",
            SensorType.Power => "W",
            SensorType.Clock => "MHz",
            SensorType.Temperature => "°C",
            SensorType.Load => "%",
            SensorType.Frequency => "Hz",
            SensorType.Level => "%",
            SensorType.Control => "%",
            SensorType.Data => "GB",
            SensorType.SmallData => "MB",
            SensorType.Energy => "mWh",
            SensorType.Noise => "dBA",
            SensorType.Humidity => "%",
            SensorType.Flow => "L/h",
            SensorType.Throughput => "B/s",
            _ => "",
        };

        return $"{sensor.Value:0.###} {unit}".TrimEnd();
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    public void Dispose()
    {
        if (_computer is null)
            return;
        try
        {
            _computer.Close();
        }
        catch
        {
            // 忽略关闭时的驱动卸载异常
        }
        _computer = null;
    }
}
