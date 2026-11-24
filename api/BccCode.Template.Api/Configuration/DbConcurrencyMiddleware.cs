using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// Two requests that write the same row at once: the second fails on EF's concurrency token, or on a unique
/// key when both insert it. Both are the conflict a stale version is: 409 (<see cref="ConflictPolicy"/>), with the i18n key
/// <see cref="Problems.Keys.Conflict"/>. Any other failed save stays a 500.
/// </summary>
public static class DbConcurrencyMiddleware
{
    public static ProblemDetails OnException(DbUpdateException ex)
    {
        if (ex is DbUpdateConcurrencyException || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            return Problems.Conflict();
        ExceptionDispatchInfo.Throw(ex);
        throw ex;
    }
}
