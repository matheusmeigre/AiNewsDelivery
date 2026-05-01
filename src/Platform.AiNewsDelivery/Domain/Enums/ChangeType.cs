namespace Platform.AiNewsDelivery.Domain.Enums;

/// <summary>Tipo de mudança detectada em um modelo entre dois ciclos de coleta.</summary>
public enum ChangeType
{
    NewModel = 0,
    PriceChange = 1,
    EloChange = 2,
    BenchmarkUpdate = 3,
    ContextWindowChange = 4,
    AvailabilityChange = 5,
    ThroughputChange = 6,
    TtftChange = 7,
    TestingOpportunity = 8
}
