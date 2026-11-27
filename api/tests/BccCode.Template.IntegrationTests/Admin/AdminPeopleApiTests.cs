using BccCode.Template.Admin;
using BccCode.Template.Api.Auth;
using BccCode.Template.IntegrationTests.Infrastructure;
using BccCode.Template.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BccCode.Template.IntegrationTests.Admin;

/// <summary>The people list and the grant and revoke of the admin role (ADR 0031), through the real host and Postgres.</summary>
public class AdminPeopleApiTests(AppFixture fixture) : AppTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SignInPeople SignIns => Host.Services.GetRequiredService<SignInPeople>();

    // A Signon person whose session (sub = person id, iss = the test issuer) is the one TestSessions makes, so a revoke finds it.
    private Task<SignedInPerson> SignedInAsync(TestUser user)
    {
        return SignIns.RecordAsync(new SignInRequest(AuthProviders.BccSignon, TestOidc.Issuer, user.PersonId.ToString(), user.Name, user.PersonId, user.PersonId.ToString()), Ct);
    }

    private async Task<(TestUser Admin, TestUser Target)> AdminAndTargetAsync(string targetName = "Target Person")
    {
        var admin = TestUser.New("Admin Caller", "admin");
        var target = TestUser.New(targetName);
        await SignedInAsync(admin);
        await SignedInAsync(target);
        return (admin, target);
    }

    private Task<IScenarioResult> SendAsync(TestUser caller, HttpMethod method, string url, int status)
    {
        return Host.Scenario(s =>
        {
            s.As(caller);
            if (method == HttpMethod.Put) s.Put.Url(url);
            else if (method == HttpMethod.Delete) s.Delete.Url(url);
            else s.Get.Url(url);
            s.StatusCodeShouldBe(status);
        });
    }

    private async Task<IReadOnlyList<PersonItem>> ListAsync(TestUser caller, string query = "")
    {
        var result = await SendAsync(caller, HttpMethod.Get, "/api/admin/people" + query, 200);
        return (await result.ReadAsJsonAsync<ListPeopleResponse>())!.People;
    }

    private static string AdminUrl(Guid personId)
    {
        return $"/api/admin/people/{personId}/admin";
    }

    private async Task<RoleGrant?> GrantOfAsync(Guid personId)
    {
        await using var db = Fixture.NewDb();
        return await db.RoleGrants.AsNoTracking().SingleOrDefaultAsync(g => g.PersonId == personId, Ct);
    }

    private async Task<int> GrantCountAsync()
    {
        await using var db = Fixture.NewDb();
        return await db.RoleGrants.CountAsync(Ct);
    }

    [Fact]
    public async Task The_list_shows_the_people_who_signed_in_by_name_with_their_provider_and_whether_they_are_administrators()
    {
        var (admin, target) = await AdminAndTargetAsync("Bea Target");
        await SignIns.RecordAsync(new SignInRequest(AuthProviders.Entra, TestEntra.Issuer, "entra-sub", "Ann Entra", null, null), Ct);
        await SendAsync(admin, HttpMethod.Put, AdminUrl(target.PersonId), 204);

        var people = await ListAsync(admin);

        people.Select(p => p.DisplayName).Should().Equal("Admin Caller", "Ann Entra", "Bea Target");
        var bea = people.Single(p => p.DisplayName == "Bea Target");
        (bea.PersonId, bea.Provider, bea.IsAdmin).Should().Be((target.PersonId, "bcc-signon", true));
        bea.LastSignInAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
        people.Single(p => p.DisplayName == "Ann Entra").Should().Match<PersonItem>(p => p.Provider == "entra" && !p.IsAdmin);
        people.Single(p => p.DisplayName == "Admin Caller").IsAdmin.Should().BeFalse("only a stored grant counts, not the role of the session");
    }

    [Fact]
    public async Task The_search_matches_part_of_a_name_without_regard_to_case_and_takes_wildcards_literally()
    {
        var (admin, _) = await AdminAndTargetAsync("Bea Target");
        await SignedInAsync(TestUser.New("Carl 100% Real"));

        (await ListAsync(admin, "?search=bEa")).Select(p => p.DisplayName).Should().Equal("Bea Target");
        (await ListAsync(admin, "?search=%20ARG")).Select(p => p.DisplayName).Should().Equal("Bea Target");
        (await ListAsync(admin, "?search=100%25")).Select(p => p.DisplayName).Should().Equal("Carl 100% Real");
        (await ListAsync(admin, "?search=%25")).Select(p => p.DisplayName).Should().Equal("Carl 100% Real");
        (await ListAsync(admin, "?search=nobody")).Should().BeEmpty();
        (await ListAsync(admin, "?search=%20")).Should().HaveCount(3, "a blank search is no filter");
    }

    [Fact]
    public async Task The_list_holds_at_most_100_people()
    {
        var (admin, _) = await AdminAndTargetAsync();
        await using (var db = Fixture.NewDb())
        {
            for (var i = 0; i < 105; i++)
                db.PersonIdentities.Add(PersonIdentity.Create(AuthProviders.Entra, $"bulk-{i}", Guid.NewGuid(), TestEntra.Issuer, $"Bulk {i:000}", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        (await ListAsync(admin)).Should().HaveCount(ListPeopleEndpoint.MaxResults);
    }

    [Fact]
    public async Task A_grant_gives_the_role_at_the_next_sign_in_and_a_repeat_changes_nothing()
    {
        var (admin, target) = await AdminAndTargetAsync();
        (await SignedInAsync(target)).Roles.Should().BeEmpty();

        await SendAsync(admin, HttpMethod.Put, AdminUrl(target.PersonId), 204);
        var first = await GrantOfAsync(target.PersonId);
        await SendAsync(admin, HttpMethod.Put, AdminUrl(target.PersonId), 204);

        (await SignedInAsync(target)).Roles.Should().Equal("admin");
        var after = await GrantOfAsync(target.PersonId);
        (first!.GrantedBy, first.Role, first.Version).Should().Be((admin.PersonId, "admin", 1));
        (after!.Version, after.GrantedAt).Should().Be((1, first.GrantedAt), "an active grant is left alone");
    }

    [Fact]
    public async Task A_revoke_flips_the_flag_ends_the_targets_sessions_and_leaves_the_callers_alone()
    {
        var (admin, target) = await AdminAndTargetAsync();
        await SendAsync(admin, HttpMethod.Put, AdminUrl(target.PersonId), 204);
        var targetCookie = await TestSessions.CreateAsync(Host, target);
        await Host.Scenario(s => { s.WithRequestHeader("Cookie", targetCookie); s.Get.Url("/api/me"); s.StatusCodeShouldBeOk(); });

        await SendAsync(admin, HttpMethod.Delete, AdminUrl(target.PersonId), 204);

        await Host.Scenario(s => { s.WithRequestHeader("Cookie", targetCookie); s.Get.Url("/api/me"); s.StatusCodeShouldBe(401); });
        await SendAsync(admin, HttpMethod.Get, "/api/me", 200);
        (await ListAsync(admin)).Single(p => p.PersonId == target.PersonId).IsAdmin.Should().BeFalse();
        var grant = await GrantOfAsync(target.PersonId);
        (grant!.RevokedBy, grant.RevokedAt is not null, grant.Version).Should().Be((admin.PersonId, true, 2));
        (await SignedInAsync(target)).Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task A_revoked_person_can_be_granted_again()
    {
        var (admin, target) = await AdminAndTargetAsync();
        await SendAsync(admin, HttpMethod.Put, AdminUrl(target.PersonId), 204);
        await SendAsync(admin, HttpMethod.Delete, AdminUrl(target.PersonId), 204);

        await SendAsync(admin, HttpMethod.Put, AdminUrl(target.PersonId), 204);

        var grant = await GrantOfAsync(target.PersonId);
        (grant!.RevokedAt, grant.RevokedBy, grant.GrantedBy, grant.Version).Should().Be((null, null, admin.PersonId, 3));
        (await SignedInAsync(target)).Roles.Should().Equal("admin");
        (await GrantCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_revoke_of_a_person_without_an_active_grant_changes_nothing_and_keeps_the_sessions()
    {
        var (admin, target) = await AdminAndTargetAsync();
        var targetCookie = await TestSessions.CreateAsync(Host, target);

        await SendAsync(admin, HttpMethod.Delete, AdminUrl(target.PersonId), 204);

        (await GrantCountAsync()).Should().Be(0);
        await Host.Scenario(s => { s.WithRequestHeader("Cookie", targetCookie); s.Get.Url("/api/me"); s.StatusCodeShouldBeOk(); });
    }

    [Fact]
    public async Task An_unknown_person_is_404_without_a_body_for_grant_and_revoke_and_nothing_is_written()
    {
        var (admin, _) = await AdminAndTargetAsync();

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        {
            var result = await SendAsync(admin, method, AdminUrl(Guid.NewGuid()), 404);

            (await result.ReadAsTextAsync()).Should().BeEmpty();
        }

        (await GrantCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Revoking_yourself_is_a_400_with_a_key_and_changes_nothing()
    {
        var (admin, _) = await AdminAndTargetAsync();
        await SendAsync(admin, HttpMethod.Put, AdminUrl(admin.PersonId), 204);

        var result = await SendAsync(admin, HttpMethod.Delete, AdminUrl(admin.PersonId), 400);

        var problem = (await result.ReadAsJsonAsync<ProblemDetails>())!;
        problem.Extensions["key"]!.ToString().Should().Be("errors.admin.cannotRevokeSelf");
        (await GrantOfAsync(admin.PersonId))!.RevokedAt.Should().BeNull();
        await SendAsync(admin, HttpMethod.Get, "/api/me", 200);
    }

    [Fact]
    public async Task Only_an_administrator_with_a_second_factor_may_use_the_routes_and_a_refused_call_writes_nothing()
    {
        var (_, target) = await AdminAndTargetAsync();
        var member = TestUser.New();
        var withoutMfa = TestUser.NewAdminWithoutMfa();
        await SignedInAsync(member);

        foreach (var (caller, status) in new[] { (member, 403), (withoutMfa, 401) })
        {
            await SendAsync(caller, HttpMethod.Get, "/api/admin/people", status);
            await SendAsync(caller, HttpMethod.Put, AdminUrl(target.PersonId), status);
            await SendAsync(caller, HttpMethod.Delete, AdminUrl(target.PersonId), status);
        }

        foreach (var (method, url) in new[] { (HttpMethod.Get, "/api/admin/people"), (HttpMethod.Put, AdminUrl(target.PersonId)), (HttpMethod.Delete, AdminUrl(target.PersonId)) })
        {
            await Host.Scenario(s =>
            {
                if (method == HttpMethod.Put) s.Put.Url(url);
                else if (method == HttpMethod.Delete) s.Delete.Url(url);
                else s.Get.Url(url);
                s.StatusCodeShouldBe(401);
            });
        }

        (await GrantCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task With_no_admin_role_configured_the_routes_deny_everyone()
    {
        await using var host = await Fixture.StartHostAsync(new Dictionary<string, string?> { ["Auth:AdminRole"] = "" });

        await host.Scenario(s => { s.As(TestUser.NewAdmin()); s.Get.Url("/api/admin/people"); s.StatusCodeShouldBe(403); });
    }
}
