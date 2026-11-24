using System.Collections.Concurrent;
using System.Text.Json;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Notes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Wolverine.Runtime.Serialization;
using Wolverine.Util;

namespace BccCode.Template.IntegrationTests;

/// <summary>
/// The messaging pipeline as a local run has it (Messaging:UsePubSub off): the archive slice writes its message to the outbox in
/// the request's transaction, the message goes to the durable local inbox, and the durability pass runs the handler. The Google
/// Pub/Sub push path (<c>POST /internal/pubsub/{subscription}</c>) runs in GCP only, so the pushes here are posted in the shape
/// Pub/Sub sends them.
/// </summary>
public class MessagingTests(AppFixture fixture) : AppTest(fixture)
{
    private const string PUSH_URL = "/internal/pubsub/messages";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_archived_note_is_stored_in_the_inbox_and_the_durability_pass_runs_its_handler()
    {
        var logs = new CapturedLogs();
        await using var host = await Fixture.StartHostAsync(services: s => s.AddSingleton<ILoggerProvider>(logs));
        var note = await CreateNoteAsync(host, TestUser.New());

        await ArchiveAsync(host, note);

        logs.Lines.Should().NotContain($"Note {note.Id} was archived", "the handler runs in the pass, not in the request");
        await RunPassesAsync(host);
        logs.Lines.Should().Contain($"Note {note.Id} was archived");
    }

    [Fact]
    public async Task A_refused_archive_stores_no_message()
    {
        var logs = new CapturedLogs();
        await using var host = await Fixture.StartHostAsync(services: s => s.AddSingleton<ILoggerProvider>(logs));
        var note = await CreateNoteAsync(host, TestUser.New());
        await ArchiveAsync(host, note);
        await RunPassesAsync(host);
        logs.Lines.Count(l => l == $"Note {note.Id} was archived").Should().Be(1);

        // Archiving twice is a 404 and a stranger's archive a 403: the transaction of the second request writes no message.
        await host.Scenario(s => { s.As(note.Owner); s.Post.Json(new ArchiveNoteRequest(note.Response.Version + 1)).ToUrl($"/api/notes/{note.Id}/archive"); s.StatusCodeShouldBe(404); });
        await RunPassesAsync(host);

        logs.Lines.Count(l => l == $"Note {note.Id} was archived").Should().Be(1);
    }

    [Fact]
    public async Task A_pushed_message_is_handled_inside_the_request_and_acknowledged()
    {
        var logs = new CapturedLogs();
        await using var host = await Fixture.StartHostAsync(services: s => s.AddSingleton<ILoggerProvider>(logs));
        var noteId = Guid.NewGuid();

        await PushAsync(host, Body(NoteArchivedJson(noteId), new() { ["message-type"] = typeof(NoteArchivedMessage).ToMessageTypeName() }), StatusCodes.Status204NoContent);

        logs.Lines.Should().Contain($"Note {noteId} was archived");
    }

    [Fact]
    public async Task The_same_push_delivered_twice_runs_the_handler_once()
    {
        var logs = new CapturedLogs();
        await using var host = await Fixture.StartHostAsync(services: s => s.AddSingleton<ILoggerProvider>(logs));
        var noteId = Guid.NewGuid();
        var body = Body(NoteArchivedJson(noteId), new() { ["message-type"] = typeof(NoteArchivedMessage).ToMessageTypeName() }, messageId: Guid.NewGuid().ToString("N"));

        await PushAsync(host, body, StatusCodes.Status204NoContent);
        await PushAsync(host, body, StatusCodes.Status204NoContent);

        logs.Lines.Count(l => l == $"Note {noteId} was archived").Should().Be(1, "a redelivery of a handled message is acknowledged without running");
    }

    [Fact]
    public async Task A_push_of_a_type_that_is_not_an_internal_message_is_refused_and_not_handled()
    {
        var logs = new CapturedLogs();
        await using var host = await Fixture.StartHostAsync(services: s => s.AddSingleton<ILoggerProvider>(logs));
        var noteId = Guid.NewGuid();
        var json = NoteArchivedJson(noteId);

        // The same payload under the name of a type that is not an internal message, as a single JSON message and as a batch.
        var otherType = typeof(ArchiveNoteRequest).ToMessageTypeName();
        var other = new Envelope { MessageType = otherType, ContentType = "application/json", Data = json };
        await PushAsync(host, Body(json, new() { ["message-type"] = otherType }), StatusCodes.Status400BadRequest);
        await PushAsync(host, Body(EnvelopeSerializer.Serialize([other]), new() { ["batched"] = "1" }), StatusCodes.Status400BadRequest);

        // A batch that hides such an envelope behind an allowed one is refused whole.
        var allowed = new Envelope { MessageType = typeof(NoteArchivedMessage).ToMessageTypeName(), ContentType = "application/json", Data = json };
        await PushAsync(host, Body(EnvelopeSerializer.Serialize([allowed, other]), new() { ["batched"] = "1" }), StatusCodes.Status400BadRequest);

        logs.Lines.Should().NotContain($"Note {noteId} was archived");
    }

    [Fact]
    public async Task A_handler_failure_on_a_push_is_retried_inside_the_request_and_then_answers_500_so_that_pub_sub_redelivers()
    {
        await using var host = await Fixture.StartHostAsync(services: s => s.ConfigureWolverine(o => o.Discovery.IncludeType(typeof(FailingNoteArchivedHandler))));
        var before = await DeadLettersAsync(host);

        await PushAsync(host, Body(NoteArchivedJson(Guid.NewGuid()), new() { ["message-type"] = typeof(NoteArchivedMessage).ToMessageTypeName() }),
            StatusCodes.Status500InternalServerError);

        (await DeadLettersAsync(host)).Should().Be(before, "Pub/Sub redelivers it and moves it to its own dead-letter topic");
    }

    [Fact]
    public async Task An_unreadable_payload_is_dead_lettered_and_acknowledged()
    {
        var before = await DeadLettersAsync(Fixture.Host);

        await PushAsync(Fixture.Host, Body(System.Text.Encoding.UTF8.GetBytes("{\"noteId\": [not json"),
            new() { ["message-type"] = typeof(NoteArchivedMessage).ToMessageTypeName() }), StatusCodes.Status204NoContent);

        (await DeadLettersAsync(Fixture.Host)).Should().Be(before + 1);
    }

    [Fact]
    public async Task A_message_whose_handler_keeps_failing_is_retried_scheduled_and_at_last_dead_lettered_with_a_fault()
    {
        await using var host = await Fixture.StartHostAsync(services: s => s.ConfigureWolverine(o =>
        {
            o.Discovery.IncludeType(typeof(FlakyMessageHandler));
            o.Discovery.IncludeType(typeof(FaultObserver));
        }));
        var message = new FlakyMessage(Guid.NewGuid(), FailuresBeforeSuccess: int.MaxValue);
        FaultObserver.Faults.Clear();
        using (var scope = host.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message);

        // First pass: the attempt, the immediate retry and the 5 second cooldown retry run inside it; the next retry is scheduled a
        // minute ahead and the row waits in the inbox.
        await RunPassesAsync(host);
        FlakyMessageHandler.Attempts[message.Id].Should().Be(3);
        (await CountAsync("select count(*) from wolverine_incoming_envelopes where status = 'Scheduled'")).Should().BeGreaterThanOrEqualTo(1);

        // A scheduled retry is not due before its time: the pass leaves it alone.
        await RunPassesAsync(host);
        FlakyMessageHandler.Attempts[message.Id].Should().Be(3);

        // Time passes (the schedule is 1 minute, then 15 minutes): make the scheduled row due, run the pass, repeat.
        await MakeScheduledDueAsync();
        await RunPassesAsync(host);
        FlakyMessageHandler.Attempts[message.Id].Should().Be(4);

        await MakeScheduledDueAsync();
        await RunPassesAsync(host);
        FlakyMessageHandler.Attempts[message.Id].Should().Be(5);

        // The retries are used up: the envelope is in the dead letter table, kept for 7 days, and a Fault<T> reached its handler.
        (await CountAsync("select count(*) from wolverine_dead_letters where message_type like '%FlakyMessage' and expires between now() + interval '6 days 23 hours' and now() + interval '7 days 1 hour'"))
            .Should().Be(1);
        await RunPassesAsync(host);
        FaultObserver.Faults.Should().ContainSingle().Which.Message.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task A_handler_that_fails_once_succeeds_on_the_immediate_retry()
    {
        await using var host = await Fixture.StartHostAsync(services: s => s.ConfigureWolverine(o => o.Discovery.IncludeType(typeof(FlakyMessageHandler))));
        var message = new FlakyMessage(Guid.NewGuid(), FailuresBeforeSuccess: 1);

        using (var scope = host.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message);
        await RunPassesAsync(host);

        FlakyMessageHandler.Attempts[message.Id].Should().Be(2, "one failure and the inline retry");
    }

    private sealed record CreatedNote(CreateNoteResponse Response, TestUser Owner)
    {
        public Guid Id => Response.Id;
    }

    private static async Task<CreatedNote> CreateNoteAsync(IAlbaHost host, TestUser owner)
    {
        var result = await host.Scenario(s =>
        {
            s.As(owner);
            s.Post.Json(new CreateNoteRequest("Title", "Body")).ToUrl("/api/notes");
            s.StatusCodeShouldBe(201);
        });
        return new CreatedNote((await result.ReadAsJsonAsync<CreateNoteResponse>())!, owner);
    }

    private static Task ArchiveAsync(IAlbaHost host, CreatedNote note)
    {
        return host.Scenario(s =>
        {
            s.As(note.Owner);
            s.Post.Json(new ArchiveNoteRequest(note.Response.Version)).ToUrl($"/api/notes/{note.Id}/archive");
            s.StatusCodeShouldBe(204);
        });
    }

    private static byte[] NoteArchivedJson(Guid noteId)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new NoteArchivedMessage(noteId), JsonSerializerOptions.Web);
    }

    /// <summary>A push request as a push subscription posts it.</summary>
    private static string Body(byte[] data, Dictionary<string, string> attributes, string? messageId = null)
    {
        return JsonSerializer.Serialize(new
        {
            message = new { data = Convert.ToBase64String(data), attributes, messageId = messageId ?? Guid.NewGuid().ToString(), publishTime = DateTimeOffset.UtcNow },
            subscription = "projects/template-test/subscriptions/template-messages-push",
            deliveryAttempt = 1,
        });
    }

    private static Task PushAsync(IAlbaHost host, string body, int expectedStatus)
    {
        return host.Scenario(s =>
        {
            s.Post.Text(body).ToUrl(PUSH_URL);
            s.StatusCodeShouldBe(expectedStatus);
        });
    }

    /// <summary>Passes until one finds nothing to do: a message a handler cascades is stored for the next pass.</summary>
    private static async Task RunPassesAsync(IAlbaHost host)
    {
        for (var pass = 0; pass < 20; pass++)
        {
            var report = await host.RunDurabilityPassAsync(new DurabilityPassOptions(), Ct);
            if (!report.HasMoreWork && report.IncomingRecovered + report.OutgoingSent + report.ScheduledReleased + report.OtherProcessed == 0) return;
        }

        throw new InvalidOperationException("The durability pass still had work after 20 passes.");
    }

    private static async Task<long> DeadLettersAsync(IAlbaHost host)
    {
        return (await host.Services.GetRequiredService<IWolverineRuntime>().Storage.Admin.FetchCountsAsync()).DeadLetter;
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task MakeScheduledDueAsync()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("update wolverine_incoming_envelopes set execution_time = now() - interval '1 second' where status = 'Scheduled'", connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}

/// <summary>Added to one test host: a second handler for the message that always fails.</summary>
public static class FailingNoteArchivedHandler
{
    public static void Handle(NoteArchivedMessage message)
    {
        throw new InvalidOperationException("The handler failed");
    }
}

/// <summary>A message whose handler fails on the first attempts, to see what the failure policy does with it.</summary>
public sealed record FlakyMessage(Guid Id, int FailuresBeforeSuccess) : BccCode.Template.Messaging.IInternalMessage;

public static class FlakyMessageHandler
{
    public static readonly ConcurrentDictionary<Guid, int> Attempts = new();

    public static void Handle(FlakyMessage message)
    {
        var attempt = Attempts.AddOrUpdate(message.Id, 1, (_, count) => count + 1);
        if (attempt <= message.FailuresBeforeSuccess) throw new InvalidOperationException("The handler fails on purpose.");
    }
}

public static class FaultObserver
{
    public static readonly ConcurrentQueue<Fault<FlakyMessage>> Faults = new();

    public static void Handle(Fault<FlakyMessage> fault)
    {
        Faults.Enqueue(fault);
    }
}
