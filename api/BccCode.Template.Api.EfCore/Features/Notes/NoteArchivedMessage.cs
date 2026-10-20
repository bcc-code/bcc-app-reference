using BccCode.Template.Web.Messaging;

namespace BccCode.Template.Api.EfCore.Features.Notes;

/// <summary>
/// The sample integration message: it leaves the archive slice through the outbox, goes out on the Pub/Sub topic and comes back as a
/// push to <c>/internal/pubsub/messages</c>. A thin notification that names the note and nothing else (standard 13, rule 1). An app
/// that has no use for it deletes this file and the line in <c>ArchiveNote</c>; the pipeline stays.
/// </summary>
public sealed record NoteArchivedMessage(Guid NoteId) : IIntegrationMessage;

/// <summary>
/// Handles the message when Pub/Sub pushes it back. Delivery is at least once, so a handler must be idempotent (rule 8): this one only
/// logs. It logs the note id, which is not personal data, and never the owner.
/// </summary>
public static class NoteArchivedHandler
{
    public static void Handle(NoteArchivedMessage message, NotesLog log) => log.Archived(message.NoteId);
}
