using Selficient.Domain.Primitives;

namespace Selficient.Domain.Tests.Primitives;

public sealed class ResultTests
{
    private static readonly Fault Boom = new("test.boom", "The thing did not work.");

    [Fact]
    public void A_successful_result_carries_its_value_and_no_error()
    {
        Result<int> result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
        result.Error.ShouldBe(Fault.None);
    }

    [Fact]
    public void A_failed_result_carries_its_error_and_refuses_to_give_a_value()
    {
        Result<int> result = Result.Failure<int>(Boom);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Boom);
        InvalidOperationException thrown = Should.Throw<InvalidOperationException>(() => result.Value);
        thrown.Message.ShouldBe("The result failed with 'test.boom', so it has no value to read.");
    }

    [Fact]
    public void Match_runs_the_success_branch_for_a_success()
    {
        string outcome = Result.Success(2).Match(v => $"ok {v}", e => $"no {e.Code}");

        outcome.ShouldBe("ok 2");
    }

    [Fact]
    public void Match_runs_the_failure_branch_for_a_failure()
    {
        string outcome = Result.Failure<int>(Boom).Match(v => $"ok {v}", e => $"no {e.Code}");

        outcome.ShouldBe("no test.boom");
    }

    [Fact]
    public void Match_rejects_missing_branches()
    {
        Result<int> result = Result.Success(1);

        Should.Throw<ArgumentNullException>(() => result.Match(null!, _ => 0));
        Should.Throw<ArgumentNullException>(() => result.Match(_ => 0, null!));
    }

    [Fact]
    public void Map_transforms_a_success_and_carries_a_failure_through()
    {
        Result.Success(2).Map(v => v * 10).Value.ShouldBe(20);

        Result<int> mapped = Result.Failure<int>(Boom).Map(v => v * 10);
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBe(Boom);
    }

    [Fact]
    public void Map_rejects_a_missing_function()
    {
        Should.Throw<ArgumentNullException>(() => Result.Success(1).Map<int>(null!));
    }

    [Fact]
    public void Bind_chains_a_success_and_short_circuits_a_failure()
    {
        Result.Success(2).Bind(v => Result.Success(v + 1)).Value.ShouldBe(3);
        Result.Success(2).Bind(_ => Result.Failure<int>(Boom)).Error.ShouldBe(Boom);

        Result<int> bound = Result.Failure<int>(Boom).Bind(v => Result.Success(v + 1));
        bound.IsFailure.ShouldBeTrue();
        bound.Error.ShouldBe(Boom);
    }

    [Fact]
    public void Bind_rejects_a_missing_function()
    {
        Should.Throw<ArgumentNullException>(() => Result.Success(1).Bind<int>(null!));
    }

    [Fact]
    public void The_none_error_is_empty()
    {
        Fault.None.Code.ShouldBeEmpty();
        Fault.None.Message.ShouldBeEmpty();
    }
}
