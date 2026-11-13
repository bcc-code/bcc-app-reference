namespace BccCode.Template.Changes;

/// <summary>
/// One business step (ADR 0028): which step, who took it, when and why. One per HTTP request, built by
/// <c>ChangeParameterStrategy</c> when an endpoint takes a <see cref="Change"/> parameter. Every row the step writes points
/// to it, and its history rows say which change replaced them. A step that writes no row is a change on its own: name it
/// and add it to the <c>TemplateDb</c>.
/// </summary>
public sealed class Change
{
    private Change()
    {
    }

    public Change(Guid id, Guid? actorId, DateTimeOffset at)
    {
        Id = id;
        ActorId = actorId;
        // Postgres keeps microseconds and .NET ticks are 100 ns. Cutting the time here makes what a write returns equal to what
        // a later read returns, so a client can compare the two.
        At = new DateTimeOffset(at.Ticks - at.Ticks % 10, at.Offset);
    }

    public Guid Id { get; private set; }

    /// <summary>The business step, past tense: <c>NoteEdited</c>. Empty until the first row records it.</summary>
    public string Type { get; private set; } = "";

    /// <summary>The <see cref="Actor"/> alias of the person who took the step; null for work no person started.</summary>
    public Guid? ActorId { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>Free text from the request. Erased with the subject; it must not name other persons.</summary>
    public string? Reason { get; private set; }

    /// <summary>The case the step was about (a note). Erasure and the timeline find changes by it.</summary>
    public Guid? SubjectId { get; private set; }

    /// <summary>Insert order, set by the database. Orders the changes of one subject when two have the same time.</summary>
    public long Position { get; private set; }

    /// <summary>
    /// Names the step. A change has one name: naming it again with another step, subject or reason throws, because a
    /// request that is two steps is two requests.
    /// </summary>
    public Change Name(string type, Guid subjectId, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (Type.Length > 0 && (Type != type || SubjectId != subjectId || Reason != reason))
            throw new InvalidOperationException($"Change {Id} is already {Type} of {SubjectId}, not {type} of {subjectId}.");
        Type = type;
        SubjectId = subjectId;
        Reason = reason;
        return this;
    }
}
