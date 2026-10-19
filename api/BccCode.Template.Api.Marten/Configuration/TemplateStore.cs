using BccCode.Template.Api.Marten.Features.Notes;
using BccCode.Template.Api.Marten.Infrastructure;
using JasperFx.Events.Projections;
using Marten;

namespace BccCode.Template.Api.Marten.Configuration;

/// <summary>
/// Every Marten document and projection registration, in one place. The API host and the schema step
/// (<c>resources setup</c>) both build the store through <see cref="MartenConfiguration"/>, so they agree on the schema.
/// </summary>
public static class TemplateStore
{
    public static void Configure(StoreOptions options)
    {
        options.Schema.For<AuthSessionDocument>();
        options.Schema.For<DataProtectionKeyDocument>();

        // Inline: the snapshot is stored in the same transaction as the events, so a write is readable at once and a
        // concurrency failure aborts both. No async daemon: it needs an always-on host, and nothing here needs it.
        options.Projections.Snapshot<Note>(SnapshotLifecycle.Inline);
    }
}
