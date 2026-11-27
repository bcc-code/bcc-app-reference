using BccCode.Template.Security;

namespace BccCode.Template.Notes;

/// <summary>Decisions shared by the note slices. Pure: everything they need is a parameter.</summary>
public static class NoteRules
{
    public const string StaleVersionKey = "errors.note.staleVersion";

    /// <summary>A note belongs to the person who created it, and nobody else may read or change it.</summary>
    public static bool CanUse(Note note, CallerAccess access)
    {
        return note.OwnerId == access.PersonId;
    }

    /// <summary>
    /// 404 when there is no such note for anyone (unknown id, or archived: the query filter hides it), 403 when it exists and
    /// belongs to someone else, else null (standard 15.8).
    /// </summary>
    public static Results<NotFound, Forbidden>? Permission(Note? note, CallerAccess access)
    {
        if (note is null) return TypedResults.NotFound();
        if (!CanUse(note, access)) return Forbidden.Result;
        return null;
    }

    /// <summary>409 when the client edited an older version than the stored one, else null.</summary>
    public static StaleVersion? Freshness(Note note, int version)
    {
        return note.Version == version ? null : StaleVersion.Problem(note.Version, StaleVersionKey);
    }
}
