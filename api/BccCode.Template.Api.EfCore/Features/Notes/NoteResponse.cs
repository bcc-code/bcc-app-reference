namespace BccCode.Template.Api.EfCore.Features.Notes;

/// <summary>What the API returns for a note. No owner and no other internal field: the contract is not the stored document.</summary>
public sealed record NoteResponse(Guid Id, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Version)
{
    public static NoteResponse From(Note note) => new(note.Id, note.Title, note.Body, note.CreatedAt, note.UpdatedAt, note.Version);
}
