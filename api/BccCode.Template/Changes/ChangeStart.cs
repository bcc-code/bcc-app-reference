using System.Security.Claims;
using BccCode.Template.Persistence;
using BccCode.Template.Security;

namespace BccCode.Template.Changes;

public static class ChangeStart
{
    /// <summary>
    /// The change an HTTP request makes: a new id, the signed-in person's actor alias and the time from the clock.
    /// <c>ChangeParameterStrategy</c> calls this once per request for an endpoint that takes a <see cref="Change"/> parameter.
    /// </summary>
    public static async Task<Change> ForRequestAsync(ClaimsPrincipal user, TemplateDb db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        Guid? actor = user.TryPersonUid(out var personUid) ? await Actor.AliasForAsync(db, personUid, cancellationToken) : null;
        return new Change(Guid.CreateVersion7(now), actor, now);
    }
}
