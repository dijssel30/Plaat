using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Plaat.Api.Tests;

/// <summary>
/// The filter's edge paths that can't be provoked through a real request: an aborted client, a
/// response already on the wire, and problem results without a detail or title.
/// </summary>
public sealed class ExceptionLoggingEndpointFilterUnitTests
{
    private readonly ILogger<ExceptionLoggingEndpointFilter> _logger = Substitute.For<ILogger<ExceptionLoggingEndpointFilter>>();
    private readonly ExceptionLoggingEndpointFilter _filter;

    public ExceptionLoggingEndpointFilterUnitTests()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _filter = new ExceptionLoggingEndpointFilter(_logger);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted
        {
            get
            {
                return true;
            }
        }
    }

    private ValueTask<object?> Invoke(HttpContext httpContext, Func<object?> endpoint)
    {
        return _filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(httpContext),
            _ => ValueTask.FromResult(endpoint()));
    }

    private int LoggedAt(LogLevel level)
    {
        return _logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log) && (LogLevel)c.GetArguments()[0]! == level);
    }

    [Fact]
    public async Task CancellationFromAnAbortedClient_IsRethrownWithoutLogging()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var httpContext = new DefaultHttpContext { RequestAborted = aborted.Token };

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Invoke(httpContext, () => throw new OperationCanceledException()));

        LoggedAt(LogLevel.Error).ShouldBe(0);
    }

    [Fact]
    public async Task CancellationWithoutAnAbortedClient_IsLoggedAndTurnedIntoA500()
    {
        var result = await Invoke(new DefaultHttpContext(), () => throw new OperationCanceledException());

        result.ShouldBeAssignableTo<IStatusCodeHttpResult>()!.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        LoggedAt(LogLevel.Error).ShouldBe(1);
    }

    [Fact]
    public async Task ExceptionAfterTheResponseStarted_IsLoggedAndRethrown()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Invoke(httpContext, () => throw new InvalidOperationException("mid-stream")));

        LoggedAt(LogLevel.Error).ShouldBe(1);
    }

    [Theory]
    [InlineData("detail", "title")]
    [InlineData(null, "title")]
    [InlineData(null, null)]
    public async Task ProblemResults_AreLoggedAsWarnings(string? detail, string? title)
    {
        var problem = TypedResults.Problem(new ProblemDetails { Status = 503, Detail = detail, Title = title });

        var result = await Invoke(new DefaultHttpContext(), () => problem);

        result.ShouldBeSameAs(problem);
        LoggedAt(LogLevel.Warning).ShouldBe(1);
    }

    [Fact]
    public async Task ServerErrorWithoutAMessageBody_IsStillLogged()
    {
        await Invoke(new DefaultHttpContext(), () => Results.StatusCode(StatusCodes.Status502BadGateway));

        LoggedAt(LogLevel.Warning).ShouldBe(1);
    }

    [Fact]
    public void GetUserId_WithoutSubClaim_Throws()
    {
        Should.Throw<InvalidOperationException>(() => new ClaimsPrincipal(new ClaimsIdentity()).GetUserId());
    }

    [Fact]
    public void GetUserId_ReadsTheSubClaim()
    {
        new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "42")])).GetUserId().ShouldBe(42);
    }
}
