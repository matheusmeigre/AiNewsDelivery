using System.ComponentModel.DataAnnotations;

namespace Platform.AiNewsDelivery.Configuration;

/// <summary>Opções de configuração do Worker Service.</summary>
public sealed class NewsWorkerOptions
{
    public const string SectionName = "Worker";

    [Required]
    public string CronSchedule { get; init; } = "0 0 */12 * * *";

    [Required]
    public string DatabasePath { get; init; } = "news-delivery.db";

    [Range(1, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    public ThresholdOptions Thresholds { get; init; } = new();
}

public sealed class ThresholdOptions
{
    [Range(0.1, 100.0)]
    public double EloChangePercent { get; init; } = 5.0;

    [Range(0.1, 100.0)]
    public double PriceChangePercent { get; init; } = 10.0;

    [Range(0.1, 100.0)]
    public double TokensPerSecChangePercent { get; init; } = 15.0;

    [Range(0.1, 100.0)]
    public double PriceDropSignificantPercent { get; init; } = 20.0;

    [Range(0, int.MaxValue)]
    public int HuggingFaceMinDownloads { get; init; } = 10_000;

    [Range(0.1, 100.0)]
    public double TtftImprovementPercent { get; init; } = 15.0;
}
