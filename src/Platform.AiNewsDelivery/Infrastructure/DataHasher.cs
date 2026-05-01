using System.Security.Cryptography;
using System.Text;

namespace Platform.AiNewsDelivery.Infrastructure;

/// <summary>
/// Utilitário para geração de hashes determinísticos a partir de dados brutos.
/// Usado para detecção de mudanças O(1) no campo <c>DataHash</c> do <c>ModelSnapshot</c>:
/// se o hash não mudou desde a última coleta, o modelo não precisa ser comparado.
/// </summary>
internal static class DataHasher
{
    /// <summary>
    /// Computa o SHA-256 hex-encoded (lowercase, 64 chars) de uma string.
    /// O input deve ser o JSON bruto normalizado do modelo.
    /// </summary>
    /// <param name="rawJson">Conteúdo a ser hasheado (tipicamente o JSON serializado do modelo).</param>
    /// <returns>Hash SHA-256 lowercase de 64 caracteres.</returns>
    public static string ComputeHash(string rawJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawJson));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
