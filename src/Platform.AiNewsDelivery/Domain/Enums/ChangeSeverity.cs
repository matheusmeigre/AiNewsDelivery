namespace Platform.AiNewsDelivery.Domain.Enums;

/// <summary>
/// Severidade de uma mudança detectada pelo motor de comparação.
/// Determina o canal de saída e a urgência do alerta enviado ao MSEMC.
/// </summary>
public enum ChangeSeverity
{
    /// <summary>Mudança significativa: Δ(Elo) > 5%, novo modelo SOTA, ou novo top-10 inédito.</summary>
    Breaking = 0,

    /// <summary>Mudança relevante: Δ(Preço) > 10%, Δ(Tokens/seg) > 15%.</summary>
    Digest = 1,

    /// <summary>Mudança abaixo dos thresholds. Apenas persistida, sem disparo de alerta.</summary>
    Silent = 2
}
