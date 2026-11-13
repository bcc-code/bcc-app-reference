namespace BccCode.Template.Changes;

/// <summary>
/// A row with history (ADR 0028). Every change to it goes through <see cref="Record"/>, which names the step, raises
/// <see cref="Version"/> and points the row at the change. The <c>history_track</c> trigger then copies the old row to
/// the table's <c>_history</c> twin, and refuses an update that skipped this (raw SQL, <c>ExecuteUpdate</c>).
/// </summary>
public abstract class Audited
{
    public Guid Id { get; protected set; }

    /// <summary>1 on insert, one more on every change. EF's concurrency token: a stale save fails and answers 409.</summary>
    public int Version { get; private set; }

    public Guid ChangeId { get; private set; }

    /// <summary>Set by <see cref="Record"/>, so EF inserts a new change before the row that points to it. Not loaded on reads.</summary>
    public Change? Change { get; private set; }

    /// <summary>The case the row belongs to, for the change: its own id for a root row, the parent's for a child row.</summary>
    protected abstract Guid SubjectId { get; }

    protected void Record(Change change, string type, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (ChangeId == change.Id)
            throw new InvalidOperationException($"{GetType().Name} {Id} was already changed by change {change.Id}.");
        change.Name(type, SubjectId, reason);
        Version++;
        ChangeId = change.Id;
        Change = change;
    }
}

/// <summary>A row of a <c>_history</c> table: an earlier version of an <see cref="Audited"/> row, and what replaced it.</summary>
public interface IReplacedVersion
{
    DateTimeOffset ReplacedAt { get; }

    /// <summary>The change that replaced this version; null when the row was deleted (erasure only).</summary>
    Guid? ReplacedBy { get; }
}
