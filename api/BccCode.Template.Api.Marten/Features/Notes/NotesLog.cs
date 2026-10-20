namespace BccCode.Template.Api.Marten.Features.Notes;

/// <summary>Log messages of the notes slices, source-generated with fixed templates. Registered in <c>TemplateLogs</c>. Never log the owner's <c>sub</c> or note text.</summary>
public sealed partial class NotesLog(ILogger<NotesLog> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Note {NoteId} was archived")]
    public partial void Archived(Guid noteId);
}
