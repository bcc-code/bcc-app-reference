namespace OidcMock;

/// <summary>
/// A synthetic person the picker offers. The person uids and names are made up and match no real person. Description is
/// shown under the name on the login page: the role and what is particular about the person. The person uid is what the app
/// keys its data by (the BCC <c>personUid</c> claim). This file is the one place the uids are written: the mock, the seeder
/// (<c>BccCode.Template.TestData</c>), the integration tests and the E2E tests compile it in (a linked file, not a project
/// reference, because the mock's OpenIddict packages must not flow into them). Mfa is whether the person's account has a second
/// factor: it decides whether the ID token's <c>amr</c> has <c>mfa</c>, which the API requires of an administrator.
/// </summary>
public sealed record Persona(string Key, string PersonUid, string Name, string Description, string[] Roles, bool Mfa = true)
{
    public static readonly Persona User = new("user", "a0000000-0000-4000-8000-000000000001", "Una User",
        "Member who owns the seeded notes.", []);

    public static readonly Persona User2 = new("user2", "a0000000-0000-4000-8000-000000000002", "Ola Second",
        "A second member with no notes of her own, for checking that notes of other people are not visible.", []);

    public static readonly Persona Admin = new("admin", "a0000000-0000-4000-8000-000000000003", "Ada Admin",
        "Administrator (role admin) with a second factor: sees the note counts on the admin page.", ["admin"]);

    public static readonly Persona AdminNoMfa = new("admin-no-mfa", "a0000000-0000-4000-8000-000000000004", "Aud Admin",
        "Administrator (role admin) whose account has no second factor: sign-in ends on the second-factor page.", ["admin"], Mfa: false);

    public static readonly IReadOnlyList<Persona> All = [User, User2, Admin, AdminNoMfa];

    public static Persona? Find(string? key)
    {
        return All.FirstOrDefault(p => p.Key == key);
    }
}
