using BccCode.Template.Api.Auth;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Notes;
using BccCode.Template.Security;
using BccCode.Template.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OidcMock;

namespace BccCode.Template.IntegrationTests.TestData;

/// <summary>The seeder against a real database: it seeds the notes of the "user" persona once, reports healthy afterwards, and resets.</summary>
public class SeederTests(AppFixture fixture) : AppTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeding_adds_the_notes_once_and_the_archived_one_is_hidden()
    {
        var owner = Guid.NewGuid();

        await using var db = Fixture.NewDb();
        var first = await SeedNotes.RunAsync(db, owner, Ct);
        await using var again = Fixture.NewDb();
        var second = await SeedNotes.RunAsync(again, owner, Ct);

        first.Should().Be(3);
        second.Should().Be(0, "the seed ids are fixed, and the archived note is found although the query filter hides it");
        await using var read = Fixture.NewDb();
        (await read.Notes.IgnoreQueryFilters().CountAsync(n => n.OwnerId == owner && n.ArchivedAt != null, Ct)).Should().Be(1);
        (await read.Notes.CountAsync(n => n.OwnerId == owner, Ct)).Should().Be(2, "the archived note is hidden");
    }

    [Fact]
    public async Task The_seeder_host_seeds_the_user_persona_before_it_is_healthy_and_a_reset_deletes_everything_else()
    {
        var builder = TestDataApp.CreateBuilder(["--environment=Development", $"--ConnectionStrings:{TestDataApp.ConnectionName}={Fixture.ConnectionString}"]);
        await using var app = builder.Build();
        await app.StartAsync(Ct);
        var seeder = app.Services.GetRequiredService<TestDataSeeder>();
        var health = app.Services.GetRequiredService<HealthCheckService>();

        (await health.CheckHealthAsync(r => r.Name == "seeded", Ct)).Status.Should().Be(HealthStatus.Healthy);
        var owner = Guid.Parse(Persona.User.PersonUid);
        await using (var db = Fixture.NewDb())
        {
            (await db.Notes.CountAsync(n => n.OwnerId == owner, Ct)).Should().Be(2);
        }

        var stranger = TestUser.New();
        await Host.Scenario(s => { s.As(stranger); s.Post.Json(new CreateNoteRequest("Mine", "")).ToUrl("/api/notes"); s.StatusCodeShouldBe(201); });
        await Host.Services.GetRequiredService<SignInPeople>().RecordAsync(
            new SignInRequest(AuthProviders.Entra, TestEntra.Issuer, "reset-sub", "Reset Person", null, null), Ct);
        await seeder.ResetAsync(Ct);

        await using var after = Fixture.NewDb();
        (await after.Notes.CountAsync(n => n.OwnerId == stranger.PersonId, Ct)).Should().Be(0);
        (await after.Notes.CountAsync(n => n.OwnerId == owner, Ct)).Should().Be(2, "the seed is applied again");
        (await after.AuthSessions.CountAsync(Ct)).Should().BeGreaterThan(0, "a reset does not sign anybody out");
        (await after.PersonIdentities.CountAsync(i => i.Subject == "reset-sub", Ct)).Should().Be(1, "the identity of a session that stays must stay too");
        await app.StopAsync(Ct);
    }

    [Fact]
    public async Task The_seeded_notes_are_what_the_user_persona_sees_through_the_api()
    {
        await using var db = Fixture.NewDb();
        await SeedNotes.RunAsync(db, SeedNotes.Owner, Ct);

        var result = await AsUser(s => s.Get.Url("/api/notes"));

        (await result.ReadAsJsonAsync<ListNotesResponse>())!.Notes.Should().HaveCount(2);
    }
}

public class SeedingGateTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("dev")]
    [InlineData("sandbox")]
    [InlineData("Preview")]
    public void Disposable_environments_may_be_seeded(string environment)
    {
        SeedingGate.IsAllowed(environment).Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Prod")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("Developmnt")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused_including_a_misspelled_name(string? environment)
    {
        SeedingGate.IsAllowed(environment).Should().BeFalse();
    }

    [Fact]
    public void The_seed_plan_has_three_notes_and_one_of_them_is_archived()
    {
        var plan = SeedNotes.Plan();

        plan.Should().HaveCount(3);
        plan.Count(n => n.ArchivedAt is not null).Should().Be(1);
        plan.Select(n => n.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_seed_plan_is_the_same_every_time_so_the_snapshot_is_reproducible()
    {
        SeedNotes.Plan().Should().Equal(SeedNotes.Plan());
        SeedNotes.Plan().Select(n => n.CreatedAt.Offset).Should().OnlyContain(o => o == TimeSpan.Zero);
    }

    [Fact]
    public void The_seeded_owner_is_the_user_persona_of_the_oidc_mock()
    {
        SeedNotes.Owner.Should().Be(Guid.Parse(Persona.User.PersonUid));
    }
}
