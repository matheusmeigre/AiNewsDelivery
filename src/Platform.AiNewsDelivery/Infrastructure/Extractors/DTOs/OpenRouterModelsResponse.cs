using System.Text.Json.Serialization;

namespace Platform.AiNewsDelivery.Infrastructure.Extractors.DTOs;

/// <summary>
/// Resposta da API <c>GET /api/v1/models</c> do OpenRouter.
/// Shape raiz: <c>{ "data": [...] }</c>.
/// </summary>
public sealed record OpenRouterModelsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<OpenRouterModel> Data
);

/// <summary>
/// Representa um modelo individual na resposta do OpenRouter.
/// Campos mapeados via snake_case → PascalCase usando <see cref="JsonPropertyNameAttribute"/>.
/// </summary>
public sealed record OpenRouterModel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("context_length")] int? ContextLength,
    [property: JsonPropertyName("pricing")] OpenRouterPricing? Pricing,
    [property: JsonPropertyName("architecture")] OpenRouterArchitecture? Architecture,
    [property: JsonPropertyName("top_provider")] OpenRouterTopProvider? TopProvider,
    [property: JsonPropertyName("created")] long? Created
);

/// <summary>
/// Preços do OpenRouter — retornados como <c>string</c> representando USD por token.
/// Exemplo: <c>"0.000003"</c> = $3.00 por milhão de tokens de input.
/// </summary>
public sealed record OpenRouterPricing(
    [property: JsonPropertyName("prompt")] string? Prompt,
    [property: JsonPropertyName("completion")] string? Completion,
    [property: JsonPropertyName("image")] string? Image,
    [property: JsonPropertyName("request")] string? Request
);

/// <summary>Arquitetura do modelo: modalidade (text→text, text+image→text) e tokenizer.</summary>
public sealed record OpenRouterArchitecture(
    [property: JsonPropertyName("modality")] string? Modality,
    [property: JsonPropertyName("tokenizer")] string? Tokenizer,
    [property: JsonPropertyName("instruct_type")] string? InstructType
);

/// <summary>Informações do provider principal (limites de contexto e completions).</summary>
public sealed record OpenRouterTopProvider(
    [property: JsonPropertyName("context_length")] int? ContextLength,
    [property: JsonPropertyName("max_completion_tokens")] int? MaxCompletionTokens,
    [property: JsonPropertyName("is_moderated")] bool? IsModerated
);
