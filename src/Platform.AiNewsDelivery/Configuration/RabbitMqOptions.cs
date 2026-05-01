namespace Platform.AiNewsDelivery.Configuration;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";
    public string? Host { get; init; }
    public string Username { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public ushort Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "/";
}
