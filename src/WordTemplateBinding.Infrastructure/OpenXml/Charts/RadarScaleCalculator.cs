using WordTemplateBinding.Core.Models;

namespace WordTemplateBinding.Infrastructure.OpenXml.Charts;

/// <summary>
/// Resolves a shared, readable indicator range for a radar chart.
/// </summary>
internal static class RadarScaleCalculator
{
    internal static (decimal Minimum, decimal Maximum) Resolve(
        IReadOnlyList<ChartSeriesSnapshot> series,
        decimal? explicitMinimum,
        decimal? explicitMaximum,
        ICollection<ChartDiagnosticItem> diagnostics)
    {
        List<decimal> values = series
            .SelectMany(item => item.Values)
            .Select(point => point.Value)
            .OfType<decimal>()
            .ToList();

        bool explicitRangeValid =
            explicitMinimum is null ||
            explicitMaximum is null ||
            explicitMaximum > explicitMinimum;
        if (!explicitRangeValid)
        {
            diagnostics.Add(Diagnostic(
                "radar_invalid_axis_range",
                $"雷达图显式轴范围无效：最小值 {explicitMinimum}，最大值 {explicitMaximum}；已根据数据重新推导。"));
            explicitMinimum = null;
            explicitMaximum = null;
        }

        if (values.Count == 0)
        {
            diagnostics.Add(Diagnostic(
                "radar_empty_values",
                "雷达图没有可用于推导轴范围的数值，使用安全范围 0–100。"));
            return ResolveNonDegenerate(
                explicitMinimum ?? 0m,
                explicitMaximum ?? 100m);
        }

        decimal dataMinimum = values.Min();
        decimal dataMaximum = values.Max();

        decimal minimum = explicitMinimum
            ?? (dataMinimum < 0m ? NiceFloor(dataMinimum) : 0m);
        decimal maximum = explicitMaximum
            ?? NiceCeiling(dataMaximum > minimum ? dataMaximum : minimum + 1m);

        if (maximum <= minimum)
        {
            (minimum, maximum) = ResolveNonDegenerate(minimum, maximum);
        }

        return (minimum, maximum);
    }

    private static (decimal Minimum, decimal Maximum) ResolveNonDegenerate(
        decimal minimum,
        decimal maximum)
    {
        if (maximum > minimum)
        {
            return (minimum, maximum);
        }

        decimal padding = minimum == 0m
            ? 1m
            : NiceCeiling(Math.Abs(minimum) * 0.1m);
        return (minimum - padding, maximum + padding);
    }

    private static decimal NiceCeiling(decimal value)
    {
        if (value <= 0m)
        {
            return 0m;
        }

        double raw = (double)value;
        double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(raw)));
        double normalized = raw / magnitude;
        double nice = normalized <= 1d ? 1d
            : normalized <= 2d ? 2d
            : normalized <= 6d ? 6d
            : 10d;
        return (decimal)(nice * magnitude);
    }

    private static decimal NiceFloor(decimal value) =>
        value >= 0m ? 0m : -NiceCeiling(Math.Abs(value));

    private static ChartDiagnosticItem Diagnostic(string code, string message) => new()
    {
        Code = code,
        Level = "warning",
        Message = message,
        Recoverable = true,
    };
}
