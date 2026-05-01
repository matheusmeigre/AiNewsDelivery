using System.Text.Json;

namespace Platform.AiNewsDelivery.Infrastructure;

internal static class JsonConstants
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
