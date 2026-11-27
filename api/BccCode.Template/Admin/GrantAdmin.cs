using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Wolverine.Persistence;

namespace BccCode.Template.Admin;

/// <summary>
/// Gives a person the admin role (a new grant, or a revoked one restored). The role reaches the person's session at their next sign-in.
/// A set-state operation without a client <c>version</c>: the administrator wants the end state "is an administrator", and the answer
/// to a repeat, or to two administrators granting at once, is the same. The save still asserts the grant's version, so a revoke and a
/// grant racing over the same row cannot both win (the loser is answered 409 by <c>ConflictPolicy</c>).
/// </summary>
public static class GrantAdminEndpoint
{
    public static Task<AdminTarget?> LoadAsync(Guid personId, TemplateDb db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        return AdminPeopleRules.LoadAsync(personId, withSessions: false, db, configuration, cancellationToken);
    }

    public static NotFound? Permission(AdminTarget? target)
    {
        return AdminPeopleRules.Permission(target);
    }

    [WolverinePut("/api/admin/people/{personId}/admin"), EmptyResponse]
    public static IStorageAction<RoleGrant> Put([NotBody] AdminTarget target, CallerAccess access, TimeProvider time)
    {
        var now = time.GetUtcNow();
        if (target.Grant is null) return Storage.Insert(RoleGrant.Grant(target.PersonId, target.Role, now, grantedBy: access.PersonId));
        // The grant is tracked: a restore is saved by the transaction. An active grant stays as it is.
        if (!target.Grant.IsActive) target.Grant.Restore(now, access.PersonId);
        return Storage.Nothing<RoleGrant>();
    }
}
