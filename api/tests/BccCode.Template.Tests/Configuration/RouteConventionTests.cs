using System.Reflection;
using BccCode.Template.Notes;
using Wolverine.Http;

namespace BccCode.Template.Tests.Configuration;

/// <summary>
/// An enum in a route is taken by its name only: its route parameter has the alpha constraint. The binding would turn a number, or
/// a list of names, into a value that is not defined; with the constraint such a path is no route and answers 404, so no endpoint
/// checks that its value is defined. The template has no enum in a route yet, so a sample in this project proves the check finds one.
/// </summary>
public sealed class RouteConventionTests
{
    public enum SampleKind
    {
        First,
        Second,
    }

    public static class SampleEndpoints
    {
        [WolverineGet("/api/samples/{kind:alpha}")]
        public static string Constrained(SampleKind kind)
        {
            return kind.ToString();
        }

        [WolverineGet("/api/samples-unconstrained/{kind}")]
        public static string Unconstrained(SampleKind kind)
        {
            return kind.ToString();
        }
    }

    private static List<(string Template, ParameterInfo Parameter)> EnumRouteParameters(Assembly assembly)
    {
        return assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .SelectMany(m => m.GetCustomAttributes<WolverineHttpMethodAttribute>().SelectMany(a => m.GetParameters()
                .Where(p => p.ParameterType.IsEnum && a.Template.Contains($"{{{p.Name}", StringComparison.Ordinal))
                .Select(p => (a.Template, p))))
            .ToList();
    }

    private static List<string> WithoutAlpha(IEnumerable<(string Template, ParameterInfo Parameter)> parameters)
    {
        return parameters.Where(e => !e.Template.Contains($"{{{e.Parameter.Name}:alpha}}", StringComparison.Ordinal)).Select(e => e.Template).ToList();
    }

    [Fact]
    public void Every_enum_in_a_route_is_taken_by_its_name_only()
    {
        WithoutAlpha(EnumRouteParameters(typeof(CreateNoteEndpoint).Assembly)).Should().BeEmpty();
    }

    [Fact]
    public void The_check_finds_an_enum_route_parameter_without_the_constraint()
    {
        WithoutAlpha(EnumRouteParameters(typeof(RouteConventionTests).Assembly)).Should().Equal("/api/samples-unconstrained/{kind}");
    }

    [Fact]
    public void Every_name_of_an_enum_in_a_route_passes_the_alpha_constraint()
    {
        EnumRouteParameters(typeof(CreateNoteEndpoint).Assembly).Concat(EnumRouteParameters(typeof(RouteConventionTests).Assembly))
            .SelectMany(e => Enum.GetNames(e.Parameter.ParameterType)).Where(n => !n.All(char.IsAsciiLetter))
            .Should().BeEmpty("a name the constraint refuses could never be asked for");
    }
}
