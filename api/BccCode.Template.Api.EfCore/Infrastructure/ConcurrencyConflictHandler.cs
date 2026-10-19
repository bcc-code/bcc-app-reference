using BccCode.Template.Api.EfCore.Features.Notes;
using BccCode.Template.Web.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace BccCode.Template.Api.EfCore.Infrastructure;

/// <summary>
/// Two writers racing on one note: the second update finds the version changed and EF throws. This answers 409 instead of 500,
/// the same as the pre-check in the slices does for a version that was stale already when the request arrived. The only
/// concurrency token is the note's version, so the stale-version key is the right one.
/// </summary>
public sealed class ConcurrencyConflictHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException) return false;

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = Problems.Create(StatusCodes.Status409Conflict, NoteRules.StaleVersionKey, "The note was changed since it was read"),
        });
    }
}
