namespace BccCode.Template.Persistence;

public static class PostgresTime
{
    /// <summary>
    /// The current time from the clock, cut to microseconds. Postgres keeps microseconds and .NET ticks are 100 ns, so cutting
    /// the time before a write makes what the write returns equal to what a later read returns, and a client can compare the two.
    /// Endpoints use this instead of <see cref="TimeProvider.GetUtcNow"/> for the time they store.
    /// </summary>
    public static DateTimeOffset Now(TimeProvider time)
    {
        var now = time.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, now.Offset);
    }
}
