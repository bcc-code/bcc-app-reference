using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BccCode.Template.Web.Http;

/// <summary>
/// RFC 9457 problems with a stable <c>type</c> URI and the i18n key in the <c>key</c> extension (standard 09, rule 11).
/// The SPA translates <c>key</c>, or the keys in <c>errors</c> for validation problems. <c>detail</c> is never set, so a
/// problem cannot carry personal data or exception text.
/// </summary>
public static class Problems
{
    public const string TypePrefix = "urn:bcc:template:";

    public static class Keys
    {
        public const string Unauthorized = "errors.unauthorized";
        public const string Forbidden = "errors.forbidden";
        public const string NotFound = "errors.notFound";
        public const string Conflict = "errors.conflict";
        public const string Validation = "errors.validation";
        public const string Csrf = "errors.csrf";
        public const string TooManyRequests = "errors.tooManyRequests";
        public const string Unexpected = "errors.unexpected";
    }

    public static ProblemDetails NotFound() => Create(StatusCodes.Status404NotFound, Keys.NotFound, "Not found");

    public static ProblemDetails Conflict() => Create(StatusCodes.Status409Conflict, Keys.Conflict, "Conflict");

    /// <summary>A 400 whose <c>errors</c> map a field name to i18n keys.</summary>
    public static HttpValidationProblemDetails Validation(IDictionary<string, string[]> errors) =>
        Decorate(new HttpValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest, Title = "Invalid input" }, Keys.Validation);

    public static ProblemDetails Create(int status, string key, string title) =>
        Decorate(new ProblemDetails { Status = status, Title = title }, key);

    /// <summary>For <c>AddProblemDetails</c>: problems the framework writes (401, 403, 404, 500) get a type and key too.</summary>
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        if (problem.Extensions.ContainsKey("key")) return;
        var key = problem.Status switch
        {
            StatusCodes.Status401Unauthorized => Keys.Unauthorized,
            StatusCodes.Status403Forbidden => Keys.Forbidden,
            StatusCodes.Status404NotFound => Keys.NotFound,
            StatusCodes.Status409Conflict => Keys.Conflict,
            StatusCodes.Status429TooManyRequests => Keys.TooManyRequests,
            _ => Keys.Unexpected,
        };
        Decorate(problem, key);
    }

    private static T Decorate<T>(T problem, string key) where T : ProblemDetails
    {
        problem.Type = TypePrefix + key;
        problem.Extensions["key"] = key;
        return problem;
    }
}
