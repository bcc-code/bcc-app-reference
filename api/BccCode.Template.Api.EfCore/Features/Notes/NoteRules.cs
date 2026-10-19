using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Mvc;

namespace BccCode.Template.Api.EfCore.Features.Notes;

/// <summary>Decisions shared by the note slices. Pure: everything they need is a parameter.</summary>
public static class NoteRules
{
    public const string StaleVersionKey = "errors.note.staleVersion";

    /// <summary>
    /// A note exists for a caller only when they own it and it is not archived. Anything else answers 404, so a foreign or
    /// archived note cannot be told from one that never existed.
    /// </summary>
    public static bool IsVisibleTo(Note? note, string subject) => note is { Archived: false } && note.OwnerId == subject;

    /// <summary>404 for a note the caller cannot see, else null.</summary>
    public static ProblemDetails? Visibility(Note? note, string subject) => IsVisibleTo(note, subject) ? null : Problems.NotFound();

    /// <summary>409 when the client edited an older version than the stored one, else null.</summary>
    public static ProblemDetails? Freshness(Note note, int version) =>
        note.Version == version
            ? null
            : Problems.Create(StatusCodes.Status409Conflict, StaleVersionKey, "The note was changed since it was read");
}
