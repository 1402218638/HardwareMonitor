using System;
using Windows.Devices.Power;
using Windows.System.Power;

namespace HardwareMonitor.Services;

public enum BatteryState
{
    Unknown,
    NotPresent,
    Idle,
    Discharging,
    Charging,
}

public sealed class BatterySnapshot
{
    public BatteryState State { get; init; } = BatteryState.Unknown;
    public string StateText { get; init; } = "未知";

    /// <summary>充放电功率（W）：正值为充电，负值为放电。</summary>
    public double? PowerW { get; init; }

    public double? Percent { get; init; }
    public double? RemainingWh { get; init; }
    public double? FullWh { get; init; }
}

/// <summary>
/// 电池读取服务：使用 Windows 系统电源 API（聚合电池报告），比内核驱动更可靠。
/// </summary>
public static class BatteryService
{
    public static BatterySnapshot Read()
    {
        try
        {
            var report = Battery.AggregateBattery.GetReport();

            if (report.Status == BatteryStatus.NotPresent)
                return new BatterySnapshot { State = BatteryState.NotPresent, StateText = "未检测到电池" };

            var (state, stateText) = report.Status switch
            {
                BatteryStatus.Charging => (BatteryState.Charging, "充电中"),
                BatteryStatus.Discharging => (BatteryState.Discharging, "放电中"),
                BatteryStatus.Idle => (BatteryState.Idle, "已接通电源"),
                _ => (BatteryState.Unknown, "未知"),
            };

            double? powerW = null;
            // ChargeRateInMilliwatts：充电为正，放电为负，空闲为 0
            if (report.ChargeRateInMilliwatts is int rateMw && rateMw != 0)
                powerW = rateMw / 1000.0;

            double? percent = null, remainingWh = null, fullWh = null;
            if (report.RemainingCapacityInMilliwattHours is int remainingMWh)
            {
                remainingWh = remainingMWh / 1000.0;
                if (report.FullChargeCapacityInMilliwattHours is int fullMWh && fullMWh > 0)
                {
                    fullWh = fullMWh / 1000.0;
                    percent = 100.0 * remainingMWh / fullMWh;
                }
            }

            return new BatterySnapshot
            {
                State = state,
                StateText = stateText,
                PowerW = powerW,
                Percent = percent,
                RemainingWh = remainingWh,
                FullWh = fullWh,
            };
        }
        catch (Exception ex)
        {
            App.Log("BatteryService: read FAILED: " + ex);
            return new BatterySnapshot { State = BatteryState.Unknown, StateText = "不可用" };
        }
    }
}
