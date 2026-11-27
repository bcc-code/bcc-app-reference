namespace OidcMock;

/// <summary>
/// A synthetic person the picker offers. The person uids and names are made up and match no real person. Description is
/// shown under the name on the login page: the role and what is particular about the person. The person uid is what BCC Signon
/// issues (the <c>personUid</c> claim) and, for a Signon sign-in, the app keys its data by. The Entra subject and object id are what
/// the mock issues to the <c>template-entra</c> client for the same persona: a pairwise-looking <c>sub</c> (43 characters of
/// base64url, not a Guid) and an <c>oid</c> (a Guid); the app issues its own person id for an Entra person, so there is no person
/// uid in an Entra token. This file is the one place these ids are written: the mock, the seeder (<c>BccCode.Template.TestData</c>),
/// the integration tests, the E2E tests and the AppHost compile it in (a linked file, not a project reference, because the mock's
/// OpenIddict packages must not flow into them). Mfa is whether the person's account has a second factor: it decides whether the ID
/// token's <c>amr</c> has <c>mfa</c>, which the API requires of an administrator.
/// </summary>
public sealed record Persona(
    string Key, string PersonUid, string EntraSubject, string EntraObjectId, string Name, string Description, string[] Roles, bool Mfa = true)
{
    /// <summary>The mock's client for the Entra sign-in (the app's <c>Entra:ClientId</c>).</summary>
    public const string EntraClientId = "template-entra";

    /// <summary>The tenant the mock puts in the <c>tid</c> claim (the app's <c>Entra:TenantId</c>). Made up.</summary>
    public const string EntraTenantId = "5d1f1d7e-6c1b-4a0e-9c0e-0a1b2c3d4e5f";

    public static readonly Persona User = new("user", "a0000000-0000-4000-8000-000000000001",
        "JbttpJEHjZkPEETI-94u9RUSeHmWezDmn9C1kIL_ZGk", "e0000000-0000-4000-8000-000000000001", "Una User",
        "Member who owns the seeded notes (as a BCC Signon person).", []);

    public static readonly Persona User2 = new("user2", "a0000000-0000-4000-8000-000000000002",
        "yim6F3Su8bVmsmprMrCKc-rt-fItZASi7b2d0OpqQvQ", "e0000000-0000-4000-8000-000000000002", "Ola Second",
        "A second member with no notes of her own, for checking that notes of other people are not visible.", []);

    public static readonly Persona Admin = new("admin", "a0000000-0000-4000-8000-000000000003",
        "1tJawLHz4qSL1DgoBsCT57R3xicAWBdmZq81iK8BuDI", "e0000000-0000-4000-8000-000000000003", "Ada Admin",
        "Administrator (role admin) with a second factor: sees the note counts and the people on the admin page.", ["admin"]);

    public static readonly Persona AdminNoMfa = new("admin-no-mfa", "a0000000-0000-4000-8000-000000000004",
        "1RMzQDjjINMLyDVd_lkb6f1kRyfPlw5pT0CZ798Y3ZQ", "e0000000-0000-4000-8000-000000000004", "Aud Admin",
        "Administrator (role admin) whose account has no second factor: sign-in ends on the second-factor page.", ["admin"], Mfa: false);

    public static readonly IReadOnlyList<Persona> All = [User, User2, Admin, AdminNoMfa];

    public static Persona? Find(string? key)
    {
        return All.FirstOrDefault(p => p.Key == key);
    }
}
