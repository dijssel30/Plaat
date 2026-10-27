namespace Plaat.Frontend;

public static class Timeouts
{
    private static readonly TimeSpan DebugTimeout = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan NormalTimeout =
#if DEBUG
        DebugTimeout;
#else
        TimeSpan.FromSeconds(10);
#endif

    public static readonly TimeSpan LongTimeout =
#if DEBUG
        DebugTimeout;
#else
        TimeSpan.FromSeconds(30);
#endif
}
