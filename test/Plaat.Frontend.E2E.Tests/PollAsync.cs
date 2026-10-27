namespace Plaat.Frontend.E2E.Tests;

/// <summary>
/// Playwright .NET (unlike the JS/Python bindings) has no generic <c>expect.poll</c> for an
/// arbitrary async value, only <c>Assertions.Expect(ILocator)</c>-style matchers. This fills that
/// gap for the handful of places tests need to wait on a JS-evaluated `&lt;audio&gt;` element
/// property (e.g. <c>duration</c>, <c>paused</c>) rather than DOM/text state.
/// </summary>
internal static class PollAsync
{
    public static async Task Until<T>(Func<Task<T>> getValue, Func<T, bool> isSatisfied, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(10));
        T last = default!;

        while (DateTime.UtcNow < deadline)
        {
            last = await getValue();
            if (isSatisfied(last))
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for {description}. Last observed value: {last}");
    }
}
