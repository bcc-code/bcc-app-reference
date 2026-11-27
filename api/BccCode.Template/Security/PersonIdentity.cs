using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Security;

/// <summary>
/// One identity at one identity provider and the app's person id for it (ADR 0031). The key is the provider (<see cref="AuthProviders"/>)
/// and the subject (<c>sub</c>), so a person who signs in with both providers has two rows and two person ids: there is no linking.
/// For BCC Signon the person id is the <c>personUid</c>, so data keyed by it keeps its owner; for Entra the app issues a Guid the first
/// time the subject signs in. The setters are private: the state changes through <see cref="Create"/> and <see cref="RecordSignIn"/>,
/// which take the time and keep <see cref="Version"/> moving.
/// </summary>
public class PersonIdentity
{
    public const int MaxDisplayNameLength = 200;

    protected PersonIdentity()
    {
    }

    public string Provider { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    /// <summary>Not unique: it is the key of the person's data, and a later change could give two identities one person.</summary>
    public Guid PersonId { get; private set; }

    /// <summary>The <c>iss</c> of the ID token. With the subject it finds the person's sessions (<c>auth_sessions</c>) to end.</summary>
    public string Issuer { get; private set; } = string.Empty;

    /// <summary>The name claim of the last sign-in, cut to <see cref="MaxDisplayNameLength"/>. Empty when the token had none.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    public DateTimeOffset FirstSignInAt { get; private set; }

    public DateTimeOffset LastSignInAt { get; private set; }

    /// <summary>1 on insert, one more on every recorded sign-in. Not a concurrency token: two sign-ins at once both succeed and the later write wins.</summary>
    public int Version { get; private set; }

    public static PersonIdentity Create(string provider, string subject, Guid personId, string issuer, string? displayName, DateTimeOffset now)
    {
        return new PersonIdentity
        {
            Provider = provider,
            Subject = subject,
            PersonId = personId,
            Issuer = issuer,
            DisplayName = Clean(displayName),
            FirstSignInAt = now,
            LastSignInAt = now,
            Version = 1,
        };
    }

    public void RecordSignIn(string? displayName, string issuer, DateTimeOffset now)
    {
        (DisplayName, Issuer, LastSignInAt) = (Clean(displayName), issuer, now);
        Version++;
    }

    private static string Clean(string? displayName)
    {
        var name = displayName?.Trim() ?? string.Empty;
        return name.Length > MaxDisplayNameLength ? name[..MaxDisplayNameLength] : name;
    }
}

/// <summary>The table of <see cref="PersonIdentity"/>. It lives with the feature; <c>TemplateDb</c> picks it up by scanning the assembly.</summary>
public sealed class PersonIdentityConfiguration : IEntityTypeConfiguration<PersonIdentity>
{
    public void Configure(EntityTypeBuilder<PersonIdentity> builder)
    {
        builder.ToTable("person_identities");
        builder.HasKey(i => new { i.Provider, i.Subject });
        builder.Property(i => i.Provider).HasMaxLength(30);
        builder.Property(i => i.Subject).HasMaxLength(512);
        builder.Property(i => i.Issuer).HasMaxLength(512).IsRequired();
        builder.Property(i => i.DisplayName).HasMaxLength(PersonIdentity.MaxDisplayNameLength).IsRequired();
        // No concurrency token, unlike the notes: two sign-ins at once both just record a sign-in, and the later write may win.
        builder.HasIndex(i => i.PersonId);   // grants and the admin page look people up by it
    }
}
