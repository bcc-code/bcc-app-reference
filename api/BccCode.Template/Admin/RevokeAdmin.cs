using BccCode.Template.Persistence;
using BccCode.Template.Security;
using Wolverine.Persistence;

namespace BccCode.Template.Admin;

/// <summary>
/// Takes the admin role from a person and ends their sessions at once, so the role does not outlive the revoke. A set-state operation
/// without a client <c>version</c>, like granting: with no active grant it changes nothing (204). The caller cannot revoke themselves,
/// so the last administrator cannot lock everyone out by accident. The sessions are deleted as returned storage actions, in the
/// transaction that saves the revoke.
/// </summary>
public static class RevokeAdminEndpoint
{
    public static Task<AdminTarget?> LoadAsync(Guid personId, TemplateDb db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        return AdminPeopleRules.LoadAsync(personId, withSessions: true, db, configuration, cancellationToken);
    }

    public static NotFound? Permission(AdminTarget? target)
    {
        return AdminPeopleRules.Permission(target);
    }

    public static DomainError? Validate(Guid personId, CallerAccess access)
    {
        return personId == access.PersonId ? new DomainError(AdminPeopleRules.CannotRevokeSelfKey, AdminPeopleRules.CannotRevokeSelfTitle) : null;
    }

    [WolverineDelete("/api/admin/people/{personId}/admin"), EmptyResponse]
    public static (IStorageAction<RoleGrant>, UnitOfWork<AuthSessionRow>) Delete([NotBody] AdminTarget target, CallerAccess access, TimeProvider time)
    {
        if (target.Grant is not { IsActive: true } grant) return (Storage.Nothing<RoleGrant>(), []);
        grant.Revoke(time.GetUtcNow(), access.PersonId);
        UnitOfWork<AuthSessionRow> sessions = [];
        foreach (var session in target.Sessions) sessions.Delete(session);
        return (Storage.Update(grant), sessions);
    }
}
