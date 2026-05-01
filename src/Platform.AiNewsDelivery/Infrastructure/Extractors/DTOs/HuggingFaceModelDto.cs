using System.Text.Json.Serialization;

namespace Platform.AiNewsDelivery.Infrastructure.Extractors.DTOs;

/// <summary>
/// Representa um modelo individual na resposta da API
/// <c>GET https://huggingface.co/api/models</c>.
/// A API retorna um array de objetos — não possui wrapper raiz.
/// </summary>
public sealed record HuggingFaceModelDto(
    [property: JsonPropertyName("modelId")] string ModelId,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("pipeline_tag")] string? PipelineTag,
    [property: JsonPropertyName("downloads")] int Downloads,
    [property: JsonPropertyName("likes")] int Likes,
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags,
    [property: JsonPropertyName("library_name")] string? LibraryName,
    [property: JsonPropertyName("createdAt")] DateTime? CreatedAt,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("private")] bool? IsPrivate,
    [property: JsonPropertyName("gated")] object? Gated
);
