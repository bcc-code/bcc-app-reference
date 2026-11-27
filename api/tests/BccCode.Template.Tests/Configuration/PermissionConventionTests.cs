using System.Reflection;
using BccCode.Template.Notes;
using Wolverine.Http;

namespace BccCode.Template.Tests.Configuration;

/// <summary>
/// Every endpoint class has a static Permission method (who may see it, who may act), unless it is listed here with the
/// reason (standard 09.2, 15). A new endpoint fails this test until it has one or is listed.
/// </summary>
public sealed class PermissionConventionTests
{
    // Any signed-in person may call these: the data is their own, or the policy of the route already decides.
    private static readonly Dictionary<string, string> NoPermission = new()
    {
        ["GetMeEndpoint"] = "Returns the caller's own profile.",
        ["ListNotesEndpoint"] = "Lists only the caller's own notes.",
        ["CreateNoteEndpoint"] = "Anyone signed in adds a note of their own.",
        ["GetStatsEndpoint"] = "Under /api/admin: the admin policy applies by route prefix, and the counts are the same for every administrator.",
        ["ListPeopleEndpoint"] = "Under /api/admin: the admin policy applies by route prefix, and every administrator sees the same list. Names only, no email.",
    };

    private static List<Type> EndpointClasses()
    {
        return typeof(CreateNoteEndpoint).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: true, IsSealed: true }
                && t.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.GetCustomAttributes<WolverineHttpMethodAttribute>().Any()))
            .ToList();
    }

    private static bool HasPermission(Type type)
    {
        return type.GetMethod("Permission", BindingFlags.Public | BindingFlags.Static) is not null;
    }

    [Fact]
    public void Every_endpoint_has_a_Permission_method_or_is_allowlisted()
    {
        var endpoints = EndpointClasses();

        endpoints.Select(t => t.Name).Should().Contain(nameof(EditNoteEndpoint), "the sweep must find the endpoints");
        endpoints.Where(t => !HasPermission(t) && !NoPermission.ContainsKey(t.Name)).Select(t => t.Name).Should().BeEmpty();
    }

    [Fact]
    public void Allowlisted_endpoints_exist_and_have_no_Permission_method()
    {
        var endpoints = EndpointClasses();

        NoPermission.Keys.Except(endpoints.Select(t => t.Name)).Should().BeEmpty("a removed endpoint must leave the allowlist");
        endpoints.Where(t => NoPermission.ContainsKey(t.Name) && HasPermission(t)).Select(t => t.Name).Should().BeEmpty("an endpoint with a Permission method is not allowlisted");
    }
}
