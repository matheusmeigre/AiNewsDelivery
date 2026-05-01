using Platform.AiNewsDelivery.Infrastructure;

namespace Platform.AiNewsDelivery.Tests.Infrastructure;

/// <summary>Testes do utilitário DataHasher: determinismo, unicidade e formato.</summary>
public sealed class DataHasherTests
{
    [Fact]
    public void ComputeHash_SameInput_ShouldReturnSameHash()
    {
        // Arrange
        const string json = """{"id":"openai/gpt-4o","pricing":{"prompt":"0.000003"}}""";

        // Act
        var hash1 = DataHasher.ComputeHash(json);
        var hash2 = DataHasher.ComputeHash(json);

        // Assert
        hash1.Should().Be(hash2, "o mesmo input deve produzir o mesmo hash (determinismo)");
    }

    [Fact]
    public void ComputeHash_DifferentInput_ShouldReturnDifferentHash()
    {
        // Arrange
        const string json1 = """{"id":"openai/gpt-4o","pricing":{"prompt":"0.000003"}}""";
        const string json2 = """{"id":"openai/gpt-4o","pricing":{"prompt":"0.000005"}}""";

        // Act
        var hash1 = DataHasher.ComputeHash(json1);
        var hash2 = DataHasher.ComputeHash(json2);

        // Assert
        hash1.Should().NotBe(hash2, "inputs diferentes devem gerar hashes diferentes");
    }

    [Fact]
    public void ComputeHash_ShouldReturnLowercase64CharHex()
    {
        // Arrange
        const string json = """{"test": true}""";

        // Act
        var hash = DataHasher.ComputeHash(json);

        // Assert
        hash.Should().HaveLength(64, "SHA-256 produz 32 bytes = 64 caracteres hex");
        hash.Should().MatchRegex("^[0-9a-f]{64}$", "deve ser lowercase hex");
    }
}
