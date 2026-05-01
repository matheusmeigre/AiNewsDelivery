namespace Platform.AiNewsDelivery.Tests.Domain;

/// <summary>
/// Testes unitários do tipo Result{T}.
/// Validam o padrão Railway-Oriented Programming usado no domínio.
/// </summary>
public sealed class ResultTests
{
    // ── Factory: Ok ────────────────────────────────────────────────────────────

    [Fact]
    public void Ok_ShouldCreateSuccessResult_WithCorrectValue()
    {
        var result = Result<int>.Ok(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Ok_WithReferenceType_ShouldPreserveValue()
    {
        var list = new List<string> { "a", "b" };

        var result = Result<List<string>>.Ok(list);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(list);
    }

    // ── Factory: Fail ──────────────────────────────────────────────────────────

    [Fact]
    public void Fail_ShouldCreateFailureResult_WithCorrectError()
    {
        var result = Result<int>.Fail("timeout");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("timeout");
        result.Value.Should().Be(default(int));
    }

    [Fact]
    public void Fail_WithEmptyMessage_ShouldStillBeFailure()
    {
        var result = Result<string>.Fail(string.Empty);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeEmpty();
    }

    // ── Match ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Match_OnSuccess_ShouldExecuteOnSuccessBranch()
    {
        var result = Result<int>.Ok(10);

        var output = result.Match(
            onSuccess: v => $"value={v}",
            onFailure: e => $"error={e}");

        output.Should().Be("value=10");
    }

    [Fact]
    public void Match_OnFailure_ShouldExecuteOnFailureBranch()
    {
        var result = Result<int>.Fail("not found");

        var output = result.Match(
            onSuccess: v => $"value={v}",
            onFailure: e => $"error={e}");

        output.Should().Be("error=not found");
    }

    // ── Map ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Map_OnSuccess_ShouldTransformValue()
    {
        var result = Result<int>.Ok(5);

        var mapped = result.Map(v => v * 2);

        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(10);
    }

    [Fact]
    public void Map_OnFailure_ShouldPropagateError_WithoutCallingMapper()
    {
        var mapperCalled = false;
        var result = Result<int>.Fail("broken");

        var mapped = result.Map(v =>
        {
            mapperCalled = true;
            return v * 2;
        });

        mapped.IsSuccess.Should().BeFalse();
        mapped.Error.Should().Be("broken");
        mapperCalled.Should().BeFalse("mapper não deve ser chamado em resultado de falha");
    }
}
