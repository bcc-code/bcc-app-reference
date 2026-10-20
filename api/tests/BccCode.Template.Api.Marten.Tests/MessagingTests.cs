using System.Text.Json;
using BccCode.Template.Api.Marten.Features.Notes;
using BccCode.Template.Api.Marten.Tests.Infrastructure;
using BccCode.Template.Web.Messaging;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Runtime.Serialization;
using Wolverine.Util;

namespace BccCode.Template.Api.Marten.Tests;

/// <summary>
/// The messaging pipeline as production runs it: the archive slice writes its message to the outbox in the request's
/// transaction, sends it to Pub/Sub after the commit, and Pub/Sub pushes it back to <c>POST /internal/pubsub/messages</c>, where
/// the handler runs inside the request. The emulator pushes nothing, so the tests pull from a subscription of their own and post
/// to the receiver themselves, as a push subscription would.
/// </summary>
[Collection(AppCollectionDefinition.Name)]
public class MessagingTests(AppFixture app) : IAsyncLifetime
{
    private const string PushUrl = "/internal/pubsub/messages";
    private const string Schema = "public";
    private static readonly TopicName Topic = TopicName.FromProjectTopic(PubSubEmulator.ProjectId, MessagingOptions.DefaultTopic);

    private PublisherServiceApiClient _publisher = null!;
    private SubscriberServiceApiClient _subscriber = null!;

    public async ValueTask InitializeAsync()
    {
        _publisher = await new PublisherServiceApiClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOnly }.BuildAsync();
        _subscriber = await new SubscriberServiceApiClientBuilder { EmulatorDetection = EmulatorDetection.EmulatorOnly }.BuildAsync();
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void The_host_runs_serverless_with_the_outbox_and_the_settings_of_the_decision()
    {
        var options = app.Host.Services.GetRequiredService<IWolverineRuntime>().Options;

        options.Durability.Mode.Should().Be(DurabilityMode.ServerlessWithOutbox);
        options.DefaultExecutionTimeout.Should().Be(TimeSpan.FromSeconds(570), "just under the 600 s ack deadline and request timeout");
        options.Durability.DeadLetterQueueExpirationEnabled.Should().BeTrue();
        options.Durability.DeadLetterQueueExpiration.Should().Be(TimeSpan.FromDays(7));
        options.Durability.EnableOutgoingTimestampIndex.Should().BeTrue("the durability pass finds stale rows by it");
    }

    [Fact]
    public async Task An_archived_note_is_published_and_the_pushed_message_is_handled()
    {
        var logs = new CapturedLogs();
        await using var host = await app.StartHostAsync(services: s => s.AddSingleton<ILoggerProvider>(logs));
        var subscription = await SubscribeAsync();
        var note = await CreateNoteAsync(host, TestUser.New());

        await ArchiveAsync(host, note);

        var published = await PullAsync(subscription, note.Id);
        logs.Lines.Should().NotContain($"Note {note.Id} was archived", "the handler runs when the message is pushed, not in the request");

        await PushAsync(host, PushBody(published), StatusCodes.Status204NoContent);

        logs.Lines.Should().Contain($"Note {note.Id} was archived");
    }

    [Fact]
    public async Task A_push_of_a_type_that_is_not_an_integration_message_is_refused_and_not_handled()
    {
        var logs = new CapturedLogs();
        await using var host = await app.StartHostAsync(services: s => s.AddSingleton<ILoggerProvider>(logs));
        var noteId = Guid.NewGuid();
        var json = JsonSerializer.SerializeToUtf8Bytes(new NoteArchivedMessage(noteId), JsonSerializerOptions.Web);

        // The same payload under the name of a type that is not an integration message, as a single JSON message and as a batch.
        var otherType = typeof(ArchiveNote).ToMessageTypeName();
        var other = new Envelope { MessageType = otherType, ContentType = "application/json", Data = json };
        await PushAsync(host, Body(json, new() { ["message-type"] = otherType }), StatusCodes.Status400BadRequest);
        await PushAsync(host, Body(EnvelopeSerializer.Serialize([other]), new() { ["batched"] = "1" }), StatusCodes.Status400BadRequest);

        // A batch that hides such an envelope behind an allowed one is refused whole.
        var allowed = new Envelope { MessageType = typeof(NoteArchivedMessage).ToMessageTypeName(), ContentType = "application/json", Data = json };
        await PushAsync(host, Body(EnvelopeSerializer.Serialize([allowed, other]), new() { ["batched"] = "1" }), StatusCodes.Status400BadRequest);

        logs.Lines.Should().NotContain($"Note {noteId} was archived");
    }

    [Fact]
    public async Task A_handler_failure_answers_500_so_that_pub_sub_redelivers_and_nothing_is_dead_lettered_here()
    {
        await using var host = await app.StartHostAsync(services: s => s.ConfigureWolverine(o => o.Discovery.IncludeType(typeof(FailingNoteArchivedHandler))));
        var before = await DeadLettersAsync(host);

        await PushAsync(host, Body(JsonSerializer.SerializeToUtf8Bytes(new NoteArchivedMessage(Guid.NewGuid()), JsonSerializerOptions.Web),
            new() { ["message-type"] = typeof(NoteArchivedMessage).ToMessageTypeName() }), StatusCodes.Status500InternalServerError);

        (await DeadLettersAsync(host)).Should().Be(before, "Pub/Sub redelivers it and moves it to its own dead-letter topic");
    }

    [Fact]
    public async Task An_unreadable_payload_is_dead_lettered_and_acknowledged()
    {
        var before = await DeadLettersAsync(app.Host);

        await PushAsync(app.Host, Body(System.Text.Encoding.UTF8.GetBytes("{\"noteId\": [not json"),
            new() { ["message-type"] = typeof(NoteArchivedMessage).ToMessageTypeName() }), StatusCodes.Status204NoContent);

        (await DeadLettersAsync(app.Host)).Should().Be(before + 1);
    }

    [Fact]
    public async Task A_message_whose_send_failed_stays_in_the_outbox_until_the_durability_pass_sends_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var note = await CreateNoteAsync(app.Host, TestUser.New());

        // Without the topic the send after the commit fails; the request has committed, so it still succeeds.
        await _publisher.DeleteTopicAsync(Topic, ct);
        try { await ArchiveAsync(app.Host, note); }
        finally { await _publisher.CreateTopicAsync(Topic, ct); }

        (await OutgoingCountAsync()).Should().BeGreaterThan(0);
        var subscription = await SubscribeAsync();

        // A row younger than OutboxStaleTime is left alone: the request that wrote it may still be sending.
        await PassAsync();
        (await OutgoingCountAsync()).Should().BeGreaterThan(0);

        await ExecuteAsync($"update {Schema}.wolverine_outgoing_envelopes set timestamp = now() - interval '10 minutes'");
        await PassAsync();

        await PullAsync(subscription, note.Id);
        (await OutgoingCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_refused_archive_publishes_nothing()
    {
        var subscription = await SubscribeAsync();
        var note = await CreateNoteAsync(app.Host, TestUser.New());
        await ArchiveAsync(app.Host, note);
        await PullAsync(subscription, note.Id);

        // Archiving twice is a 404: the transaction of the second request writes no message.
        await app.Host.Scenario(s =>
        {
            s.As(note.Owner);
            s.Post.Json(new ArchiveNote(note.Response.Version + 1)).ToUrl($"/api/notes/{note.Id}/archive");
            s.StatusCodeShouldBe(404);
        });

        await Task.Delay(500, TestContext.Current.CancellationToken);
        (await _subscriber.PullAsync(subscription, maxMessages: 10, TestContext.Current.CancellationToken)).ReceivedMessages.Should().BeEmpty();
    }

    private sealed record CreatedNote(NoteResponse Response, TestUser Owner)
    {
        public Guid Id => Response.Id;
    }

    private static async Task<CreatedNote> CreateNoteAsync(IAlbaHost host, TestUser owner)
    {
        var result = await host.Scenario(s =>
        {
            s.As(owner);
            s.Post.Json(new CreateNote("Title", "Body")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(201);
        });
        return new CreatedNote((await result.ReadAsJsonAsync<NoteResponse>())!, owner);
    }

    private static Task ArchiveAsync(IAlbaHost host, CreatedNote note) =>
        host.Scenario(s =>
        {
            s.As(note.Owner);
            s.Post.Json(new ArchiveNote(note.Response.Version)).ToUrl($"/api/notes/{note.Id}/archive");
            s.StatusCodeShouldBe(204);
        });

    private async Task<SubscriptionName> SubscribeAsync()
    {
        var subscription = SubscriptionName.FromProjectSubscription(PubSubEmulator.ProjectId, $"test-{Guid.NewGuid():N}");
        await _subscriber.CreateSubscriptionAsync(subscription, Topic, pushConfig: null, ackDeadlineSeconds: 60, TestContext.Current.CancellationToken);
        return subscription;
    }

    /// <summary>Pulls and acknowledges until the message for this note has arrived; other tests' messages are skipped.</summary>
    private async Task<PubsubMessage> PullAsync(SubscriptionName subscription, Guid noteId)
    {
        var ct = TestContext.Current.CancellationToken;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var typeName = typeof(NoteArchivedMessage).ToMessageTypeName();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (true)
        {
            var pulled = await _subscriber.PullAsync(subscription, maxMessages: 100, ct);
            if (pulled.ReceivedMessages.Count > 0)
                await _subscriber.AcknowledgeAsync(subscription, pulled.ReceivedMessages.Select(m => m.AckId), ct);
            foreach (var message in pulled.ReceivedMessages.Select(m => m.Message))
                if (Messages(message).Any(wire => wire.Type == typeName
                                                   && JsonSerializer.Deserialize<NoteArchivedMessage>(wire.Data, JsonSerializerOptions.Web)!.NoteId == noteId))
                    return message;

            if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException($"No NoteArchivedMessage for {noteId} was published within the timeout.");
            await timer.WaitForNextTickAsync(ct);
        }
    }

    /// <summary>The Wolverine messages a Pub/Sub message carries: a durable sender writes a binary batch.</summary>
    private static IEnumerable<(string? Type, byte[] Data)> Messages(PubsubMessage message) =>
        message.Attributes.ContainsKey("batched")
            ? EnvelopeSerializer.ReadMany(message.Data.ToByteArray()).Select(envelope => (envelope.MessageType, envelope.Data ?? []))
            : [(message.Attributes.GetValueOrDefault("message-type"), message.Data.ToByteArray())];

    private static string PushBody(PubsubMessage message) => Body(message.Data.ToByteArray(), message.Attributes.ToDictionary(), message.MessageId);

    /// <summary>A push request as a push subscription posts it.</summary>
    private static string Body(byte[] data, Dictionary<string, string> attributes, string? messageId = null) =>
        JsonSerializer.Serialize(new
        {
            message = new { data = Convert.ToBase64String(data), attributes, messageId = messageId ?? Guid.NewGuid().ToString(), publishTime = DateTimeOffset.UtcNow },
            subscription = $"projects/{PubSubEmulator.ProjectId}/subscriptions/template-messages-push",
            deliveryAttempt = 1,
        });

    private static Task PushAsync(IAlbaHost host, string body, int expectedStatus) =>
        host.Scenario(s =>
        {
            s.Post.Text(body).ToUrl(PushUrl);
            s.StatusCodeShouldBe(expectedStatus);
        });

    private Task PassAsync() =>
        app.Host.Scenario(s =>
        {
            s.Post.Url("/internal/durability/pass");
            s.StatusCodeShouldBeOk();
        });

    private static async Task<long> DeadLettersAsync(IAlbaHost host) =>
        (await host.Services.GetRequiredService<IWolverineRuntime>().Storage.Admin.FetchCountsAsync()).DeadLetter;

    private async Task<long> OutgoingCountAsync() =>
        (await app.Host.Services.GetRequiredService<IWolverineRuntime>().Storage.Admin.FetchCountsAsync()).Outgoing;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        (await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().BeGreaterThan(0);
    }
}

/// <summary>Added to one test host: a second handler for the message that always fails.</summary>
public static class FailingNoteArchivedHandler
{
    public static void Handle(NoteArchivedMessage message) => throw new InvalidOperationException("The handler failed");
}
