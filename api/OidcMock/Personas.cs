namespace OidcMock;

/// <summary>
/// The people the mock can sign in as. The subjects are fixed so seeded data and tests can refer to them. The seeder reads
/// the subject of "user" from Seed:UserSubject (appsettings.Development.json of the API): keep the two equal.
/// </summary>
public sealed record Persona(string Key, string Subject, string Name, string[] Roles)
{
    public static readonly IReadOnlyList<Persona> All =
    [
        new("user", "0198f5a0-0000-7000-8000-000000000001", "Una User", []),
        new("user2", "0198f5a0-0000-7000-8000-000000000002", "Ola Second", []),
        new("admin", "0198f5a0-0000-7000-8000-000000000003", "Ada Admin", ["admin"]),
    ];

    public static Persona? Find(string? key) => All.FirstOrDefault(p => p.Key == key);
}
