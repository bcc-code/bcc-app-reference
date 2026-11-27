using BccCode.Template.Api.Auth;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BccCode.Template.IntegrationTests.Auth;

/// <summary>Recording a sign-in (ADR 0031): the person id per provider and subject, the bootstrap list and the stored roles, on the real database.</summary>
public class SignInPeopleTests(AppFixture fixture) : AppTest(fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new(Start);

    private SignInPeople People(string? adminRole = "admin", (string Provider, string Id)[]? bootstrap = null)
    {
        var settings = new Dictionary<string, string?> { ["Auth:AdminRole"] = adminRole };
        // The one string setting Secret Manager holds: <provider>:<id> entries.
        settings["Auth:BootstrapAdmins"] = string.Join(';', (bootstrap ?? []).Select(b => $"{b.Provider}:{b.Id}"));

        return new SignInPeople(
            Fixture.Host.Services.GetRequiredService<IDbContextFactory<TemplateDb>>(), _time, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static SignInRequest Entra(string subject = "entra-sub-1", string? name = "Eva Entra", string? oid = null)
    {
        return new SignInRequest(AuthProviders.Entra, TestEntra.Issuer, subject, name, SignonPersonUid: null, BootstrapId: oid);
    }

    private static SignInRequest Signon(Guid personUid, string subject = "signon-sub-1")
    {
        return new SignInRequest(AuthProviders.BccSignon, TestOidc.Issuer, subject, "Sam Signon", personUid, BootstrapId: personUid.ToString());
    }

    [Fact]
    public async Task An_entra_subject_gets_an_issued_person_id_once_and_the_same_one_at_every_later_sign_in()
    {
        var people = People();

        var first = await people.RecordAsync(Entra(), Ct);
        _time.Advance(TimeSpan.FromHours(3));
        var second = await people.RecordAsync(Entra(name: "Eva E."), Ct);

        first.PersonId.Should().NotBeEmpty();
        second.PersonId.Should().Be(first.PersonId);
        await using var db = Fixture.NewDb();
        var row = await db.PersonIdentities.AsNoTracking().SingleAsync(Ct);
        (row.Provider, row.Subject, row.PersonId, row.Issuer).Should().Be((AuthProviders.Entra, "entra-sub-1", first.PersonId, TestEntra.Issuer));
        (row.DisplayName, row.FirstSignInAt, row.LastSignInAt, row.Version).Should().Be(("Eva E.", Start, Start.AddHours(3), 2));
    }

    [Fact]
    public async Task A_signon_person_keeps_the_person_uid_as_the_person_id()
    {
        var personUid = Guid.NewGuid();

        var result = await People().RecordAsync(Signon(personUid), Ct);

        result.PersonId.Should().Be(personUid);
        await using var db = Fixture.NewDb();
        (await db.PersonIdentities.AsNoTracking().SingleAsync(Ct)).PersonId.Should().Be(personUid);
    }

    [Fact]
    public async Task The_same_subject_at_the_two_providers_is_two_people_and_nothing_is_linked()
    {
        var people = People();
        var personUid = Guid.NewGuid();

        var signon = await people.RecordAsync(Signon(personUid, subject: "shared-sub"), Ct);
        var entra = await people.RecordAsync(Entra(subject: "shared-sub"), Ct);

        entra.PersonId.Should().NotBe(signon.PersonId);
        await using var db = Fixture.NewDb();
        (await db.PersonIdentities.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Many_first_sign_ins_at_once_make_one_row_and_every_caller_gets_the_same_person_id()
    {
        var people = People();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => people.RecordAsync(Entra(subject: "racing-sub"), Ct), Ct)));

        results.Select(r => r.PersonId).Distinct().Should().ContainSingle();
        await using var db = Fixture.NewDb();
        var row = await db.PersonIdentities.AsNoTracking().SingleAsync(Ct);
        row.PersonId.Should().Be(results[0].PersonId);
    }

    [Fact]
    public async Task A_listed_person_is_bootstrapped_as_an_administrator_by_the_configuration()
    {
        var oid = Guid.NewGuid().ToString();

        var result = await People(bootstrap: [(AuthProviders.Entra, oid.ToUpperInvariant())]).RecordAsync(Entra(oid: oid), Ct);

        result.Roles.Should().Equal("admin");
        await using var db = Fixture.NewDb();
        var grant = await db.RoleGrants.AsNoTracking().SingleAsync(Ct);
        (grant.PersonId, grant.Role, grant.GrantedBy, grant.GrantedAt, grant.RevokedAt, grant.Version).Should().Be((result.PersonId, "admin", (Guid?)null, Start, (DateTimeOffset?)null, 1));
    }

    [Fact]
    public async Task A_signon_person_is_bootstrapped_by_the_person_uid_and_a_provider_that_does_not_match_grants_nothing()
    {
        var personUid = Guid.NewGuid();

        var listedForEntra = await People(bootstrap: [(AuthProviders.Entra, personUid.ToString())]).RecordAsync(Signon(personUid), Ct);
        var listedForSignon = await People(bootstrap: [(AuthProviders.BccSignon, personUid.ToString())]).RecordAsync(Signon(personUid), Ct);

        listedForEntra.Roles.Should().BeEmpty("the entry names the other provider");
        listedForSignon.Roles.Should().Equal("admin");
    }

    [Fact]
    public async Task A_person_who_is_not_listed_gets_no_role()
    {
        var result = await People(bootstrap: [(AuthProviders.Entra, Guid.NewGuid().ToString())]).RecordAsync(Entra(oid: Guid.NewGuid().ToString()), Ct);

        result.Roles.Should().BeEmpty();
        await using var db = Fixture.NewDb();
        (await db.RoleGrants.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task A_revoked_grant_is_not_given_again_while_the_person_stays_in_the_list()
    {
        var oid = Guid.NewGuid().ToString();
        var people = People(bootstrap: [(AuthProviders.Entra, oid)]);
        var first = await people.RecordAsync(Entra(oid: oid), Ct);
        await using (var db = Fixture.NewDb())
        {
            var grant = await db.RoleGrants.SingleAsync(Ct);
            grant.Revoke(_time.GetUtcNow(), Guid.NewGuid());
            await db.SaveChangesAsync(Ct);
        }

        var again = await people.RecordAsync(Entra(oid: oid), Ct);

        again.PersonId.Should().Be(first.PersonId);
        again.Roles.Should().BeEmpty("the revoked row is the truth");
        await using var after = Fixture.NewDb();
        (await after.RoleGrants.AsNoTracking().SingleAsync(Ct)).RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task The_stored_roles_of_a_person_are_added_and_those_of_another_person_are_not()
    {
        var people = People();
        var person = await people.RecordAsync(Entra(subject: "granted"), Ct);
        var other = await people.RecordAsync(Entra(subject: "plain"), Ct);
        await using (var db = Fixture.NewDb())
        {
            db.RoleGrants.Add(RoleGrant.Grant(person.PersonId, "admin", _time.GetUtcNow(), grantedBy: Guid.NewGuid()));
            await db.SaveChangesAsync(Ct);
        }

        (await people.RecordAsync(Entra(subject: "granted"), Ct)).Roles.Should().Equal("admin");
        (await people.RecordAsync(Entra(subject: "plain"), Ct)).Roles.Should().BeEmpty();
        other.PersonId.Should().NotBe(person.PersonId);
    }

    [Fact]
    public async Task Listed_people_signing_in_at_once_get_one_grant_and_all_of_them_get_the_role()
    {
        var oid = Guid.NewGuid().ToString();
        var people = People(bootstrap: [(AuthProviders.Entra, oid)]);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => people.RecordAsync(Entra(subject: "racing-admin", oid: oid), Ct), Ct)));

        results.Should().OnlyContain(r => r.Roles.Count == 1 && r.Roles[0] == "admin");
        await using var db = Fixture.NewDb();
        (await db.RoleGrants.CountAsync(Ct)).Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task With_no_admin_role_configured_nothing_is_granted_or_bootstrapped_and_a_stored_grant_gives_nothing(string? adminRole)
    {
        var oid = Guid.NewGuid().ToString();
        var withRole = People();
        var person = await withRole.RecordAsync(Entra(oid: oid), Ct);
        await using (var db = Fixture.NewDb())
        {
            db.RoleGrants.Add(RoleGrant.Grant(person.PersonId, "admin", _time.GetUtcNow(), grantedBy: null));
            await db.SaveChangesAsync(Ct);
        }

        var result = await People(adminRole, [(AuthProviders.Entra, oid)]).RecordAsync(Entra(oid: oid), Ct);
        var listedNewcomer = await People(adminRole, [(AuthProviders.Entra, "newcomer")]).RecordAsync(Entra(subject: "newcomer-sub", oid: "newcomer"), Ct);

        result.Roles.Should().BeEmpty();
        listedNewcomer.Roles.Should().BeEmpty();
        await using var after = Fixture.NewDb();
        (await after.RoleGrants.CountAsync(Ct)).Should().Be(1, "only the grant the test made itself exists");
    }

    [Fact]
    public async Task A_name_longer_than_the_column_is_cut_and_a_missing_name_is_stored_as_empty()
    {
        var people = People();

        await people.RecordAsync(Entra(subject: "long", name: new string('x', 500)), Ct);
        await people.RecordAsync(Entra(subject: "none", name: null), Ct);

        await using var db = Fixture.NewDb();
        (await db.PersonIdentities.AsNoTracking().SingleAsync(i => i.Subject == "long", Ct)).DisplayName.Should().HaveLength(PersonIdentity.MaxDisplayNameLength);
        (await db.PersonIdentities.AsNoTracking().SingleAsync(i => i.Subject == "none", Ct)).DisplayName.Should().BeEmpty();
    }

    [Fact]
    public async Task A_signon_identity_that_comes_back_with_another_person_uid_is_brought_into_line_with_it()
    {
        var people = People();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        await people.RecordAsync(Signon(first, subject: "moved-sub"), Ct);

        var result = await people.RecordAsync(Signon(second, subject: "moved-sub"), Ct);

        result.PersonId.Should().Be(second);
        await using var db = Fixture.NewDb();
        var row = await db.PersonIdentities.AsNoTracking().SingleAsync(Ct);
        (row.PersonId, row.Version).Should().Be((second, 2));
    }

    [Fact]
    public async Task An_entra_identity_keeps_the_person_id_it_was_issued()
    {
        var people = People();
        var issued = (await people.RecordAsync(Entra(subject: "stable-sub"), Ct)).PersonId;

        var again = await people.RecordAsync(Entra(subject: "stable-sub"), Ct);

        again.PersonId.Should().Be(issued);
    }
}
