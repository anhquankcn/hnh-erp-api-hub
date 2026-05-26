using ERPApiHub.Application.Errors;
using Xunit;

namespace ERPApiHub.Tests;

public class ProblemDetailsTests
{
    [Fact]
    public void Validation_Returns400WithCorrectType()
    {
        var problem = ProblemDetailsHelper.Validation("Invalid input", "/api/v1/test", "req-123");

        Assert.Equal("https://api.hnhtravel.work/errors/validation", problem.Type);
        Assert.Equal("Validation Failed", problem.Title);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid input", problem.Detail);
        Assert.Equal("/api/v1/test", problem.Instance);
        Assert.Equal("req-123", problem.RequestId);
        Assert.Null(problem.RetryAfter);
    }

    [Fact]
    public void Validation_WithFieldErrors_IncludesErrorsArray()
    {
        var fieldErrors = new List<FieldError>
        {
            new() { Field = "name", Message = "Required", Code = "REQUIRED" }
        };

        var problem = ProblemDetailsHelper.Validation("Validation failed", errors: fieldErrors);

        Assert.NotNull(problem.Errors);
        Assert.Single(problem.Errors);
        Assert.Equal("name", problem.Errors[0].Field);
        Assert.Equal("Required", problem.Errors[0].Message);
        Assert.Equal("REQUIRED", problem.Errors[0].Code);
    }

    [Fact]
    public void Unauthorized_Returns401()
    {
        var problem = ProblemDetailsHelper.Unauthorized();

        Assert.Equal("https://api.hnhtravel.work/errors/unauthorized", problem.Type);
        Assert.Equal(401, problem.Status);
        Assert.Equal("Authentication required", problem.Detail);
    }

    [Fact]
    public void Forbidden_Returns403()
    {
        var problem = ProblemDetailsHelper.Forbidden();

        Assert.Equal("https://api.hnhtravel.work/errors/forbidden", problem.Type);
        Assert.Equal(403, problem.Status);
    }

    [Fact]
    public void NotFound_Returns404()
    {
        var problem = ProblemDetailsHelper.NotFound("Resource gone", "/api/v1/test/123");

        Assert.Equal("https://api.hnhtravel.work/errors/not-found", problem.Type);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Resource gone", problem.Detail);
    }

    [Fact]
    public void Conflict_Returns409()
    {
        var problem = ProblemDetailsHelper.Conflict();

        Assert.Equal("https://api.hnhtravel.work/errors/conflict", problem.Type);
        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public void RateLimited_Returns429WithRetryAfter()
    {
        var problem = ProblemDetailsHelper.RateLimited("Too many requests", 45);

        Assert.Equal("https://api.hnhtravel.work/errors/rate-limited", problem.Type);
        Assert.Equal(429, problem.Status);
        Assert.Equal(45, problem.RetryAfter);
    }

    [Fact]
    public void ErpNextError_Returns502()
    {
        var problem = ProblemDetailsHelper.ErpNextError("ERPNext connection refused");

        Assert.Equal("https://api.hnhtravel.work/errors/erpnext-error", problem.Type);
        Assert.Equal(502, problem.Status);
        Assert.Equal("ERPNext connection refused", problem.Detail);
    }

    [Fact]
    public void ErpNextTimeout_Returns504()
    {
        var problem = ProblemDetailsHelper.ErpNextTimeout();

        Assert.Equal("https://api.hnhtravel.work/errors/erpnext-timeout", problem.Type);
        Assert.Equal(504, problem.Status);
    }

    [Fact]
    public void InternalError_Returns500()
    {
        var problem = ProblemDetailsHelper.InternalError();

        Assert.Equal("https://api.hnhtravel.work/errors/internal-error", problem.Type);
        Assert.Equal(500, problem.Status);
        Assert.Equal("An unexpected error occurred", problem.Detail);
    }

    [Fact]
    public void ProblemDetails_TimestampIsUtc()
    {
        var before = DateTimeOffset.UtcNow;
        var problem = ProblemDetailsHelper.NotFound();
        var after = DateTimeOffset.UtcNow;

        Assert.True(problem.Timestamp >= before);
        Assert.True(problem.Timestamp <= after);
    }

    [Fact]
    public void ProblemDetails_SerializationExcludesNullErrors()
    {
        var problem = ProblemDetailsHelper.NotFound();
        var json = System.Text.Json.JsonSerializer.Serialize(problem);

        Assert.DoesNotContain("\"errors\"", json);
        Assert.DoesNotContain("\"retry_after\"", json);
    }

    [Fact]
    public void ProblemDetails_SerializationIncludesErrorsWhenPresent()
    {
        var problem = ProblemDetailsHelper.Validation("fail",
            errors: [new FieldError { Field = "x", Message = "bad", Code = "INV" }]);
        var json = System.Text.Json.JsonSerializer.Serialize(problem);

        Assert.Contains("\"errors\"", json);
        Assert.Contains("\"field\"", json);
        Assert.Contains("\"x\"", json);
    }
}