using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BccCode.Template.Tests;

/// <summary>The results a Validate step returns (standard 09.11): a status, a stable type URI and the i18n key as an extension.</summary>
public class ProblemResultTests
{
    private sealed record NoteTooBig(string Title, int MaxLength) : ConflictProblem<NoteTooBig>(Title);

    private static async Task<(int Status, JsonElement Body)> RunAsync(IResult result)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, text.Length == 0 ? default : JsonElement.Parse(text));
    }

    [Fact]
    public async Task A_domain_error_is_a_400_with_the_key_as_extension_and_a_stable_type()
    {
        var (status, body) = await RunAsync(new DomainError("errors.note.closed", "The note is closed."));

        status.Should().Be(400);
        body.GetProperty("key").GetString().Should().Be("errors.note.closed");
        body.GetProperty("type").GetString().Should().Be("urn:bcc:template:errors.note.closed");
        body.TryGetProperty("detail", out _).Should().BeFalse("a problem carries no detail");
    }

    [Fact]
    public async Task A_stale_version_is_a_409_with_the_key_it_was_given()
    {
        var (status, body) = await RunAsync(StaleVersion.Problem(4, "errors.note.staleVersion"));

        status.Should().Be(409);
        body.GetProperty("key").GetString().Should().Be("errors.note.staleVersion");
        StaleVersion.Problem(4).Key.Should().Be(Problems.Keys.Conflict, "the generic conflict when the caller names no key");
        StaleVersion.Problem(4).CurrentVersion.Should().Be(4);
    }

    [Fact]
    public async Task A_typed_problem_derives_key_and_type_from_its_name_and_carries_its_data()
    {
        var (status, body) = await RunAsync(new NoteTooBig("Too big", 200));

        status.Should().Be(409);
        body.GetProperty("key").GetString().Should().Be("errors.noteTooBig");
        body.GetProperty("type").GetString().Should().Be("urn:bcc:template:errors.noteTooBig");
        body.GetProperty("maxLength").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task Forbidden_is_a_403_without_a_body()
    {
        var (status, body) = await RunAsync(Forbidden.Result);

        status.Should().Be(403);
        body.ValueKind.Should().Be(JsonValueKind.Undefined);
    }
}
