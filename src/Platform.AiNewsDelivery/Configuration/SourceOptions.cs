using System.ComponentModel.DataAnnotations;

namespace Platform.AiNewsDelivery.Configuration;

public sealed class SourceOptions
{
    public const string SectionName = "Sources";

    public OpenRouterSourceOptions OpenRouter { get; init; } = new();
    public HuggingFaceSourceOptions HuggingFace { get; init; } = new();
    public ArtificialAnalysisSourceOptions ArtificialAnalysis { get; init; } = new();
    public GitHubSourceOptions GitHub { get; init; } = new();
}

public sealed class OpenRouterSourceOptions
{
    [Required]
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public bool Enabled { get; init; } = true;
}

public sealed class HuggingFaceSourceOptions
{
    [Required]
    public string BaseUrl { get; init; } = "https://huggingface.co/api";
    public string? Token { get; init; }
    public bool Enabled { get; init; } = true;
}

public sealed class ArtificialAnalysisSourceOptions
{
    [Required]
    public string BaseUrl { get; init; } = "https://api.artificialanalysis.ai";
    public string? ApiKey { get; init; }
    public bool Enabled { get; init; }
}

public sealed class GitHubSourceOptions
{
    public string? Token { get; init; }
    public bool Enabled { get; init; }
    public IReadOnlyList<string> TrackedRepos { get; init; } =
    [
        "meta-llama/llama-models",
        "QwenLM/Qwen2.5",
        "mistralai/mistral-inference"
    ];
}
