using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BccCode.Template.Security;

/// <summary>
/// A role stored for a person (ADR 0031): at sign-in an active grant adds the role to the session. A revoke keeps the row, so
/// a person who was revoked is not granted the role again by the bootstrap list. The setters are private: the state changes through
/// <see cref="Grant"/>, <see cref="Revoke"/> and <see cref="Restore"/>, which take the time and the actor and keep
/// <see cref="Version"/> moving. The actor is the person id of the administrator, or null for the configuration bootstrap.
/// </summary>
public class RoleGrant
{
    public const int MaxRoleLength = 100;

    protected RoleGrant()
    {
    }

    public Guid PersonId { get; private set; }

    public string Role { get; private set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>Null when the <c>Auth:BootstrapAdmins</c> setting made the grant.</summary>
    public Guid? GrantedBy { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public int Version { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static RoleGrant Grant(Guid personId, string role, DateTimeOffset now, Guid? grantedBy)
    {
        return new RoleGrant { PersonId = personId, Role = role, GrantedAt = now, GrantedBy = grantedBy, Version = 1 };
    }

    public void Revoke(DateTimeOffset now, Guid revokedBy)
    {
        (RevokedAt, RevokedBy) = (now, revokedBy);
        Version++;
    }

    /// <summary>Grants a revoked role again: the grant is new, so who and when are those of the new grant.</summary>
    public void Restore(DateTimeOffset now, Guid grantedBy)
    {
        (GrantedAt, GrantedBy, RevokedAt, RevokedBy) = (now, grantedBy, null, null);
        Version++;
    }
}

/// <summary>The table of <see cref="RoleGrant"/>.</summary>
public sealed class RoleGrantConfiguration : IEntityTypeConfiguration<RoleGrant>
{
    public void Configure(EntityTypeBuilder<RoleGrant> builder)
    {
        builder.ToTable("role_grants");
        builder.HasKey(g => new { g.PersonId, g.Role });
        builder.Property(g => g.Role).HasMaxLength(RoleGrant.MaxRoleLength);
        builder.Property(g => g.Version).IsConcurrencyToken();
        builder.Ignore(g => g.IsActive);
    }
}
