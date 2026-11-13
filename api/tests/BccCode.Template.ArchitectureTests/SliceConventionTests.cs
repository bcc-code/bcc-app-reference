using System.Reflection;
using BccCode.Template.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using Wolverine;
using Wolverine.Http;

namespace BccCode.Template.ArchitectureTests;

public class SliceConventionTests
{
    private static IEnumerable<MethodInfo> EndpointMethods()
    {
        return TemplateAssemblies.Features.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes().Any(a => a is WolverineHttpMethodAttribute));
    }

    [Fact]
    public void Endpoints_exist_so_the_rules_below_are_not_vacuous()
    {
        EndpointMethods().Should().HaveCountGreaterThan(5);
    }

    [Fact]
    public void Endpoints_are_static_classes()
    {
        Types.InAssembly(TemplateAssemblies.Features).That().HaveNameEndingWith("Endpoint").Should().BeStatic().GetResult().FailingTypes
            .Select(t => t.FullName).Should().BeNullOrEmpty();
    }

    [Fact]
    public void No_services_or_repositories()
    {
        Types.InAssemblies(TemplateAssemblies.All).That().HaveNameEndingWith("Service").Or().HaveNameEndingWith("Repository").GetTypes()
            .Should().BeEmpty();
    }

    [Fact]
    public void Every_endpoint_class_lives_in_a_feature_folder()
    {
        // BccCode.Template.<Feature>.<Name>Endpoint: one namespace segment below the project's root, and no further plumbing namespace.
        Types.InAssembly(TemplateAssemblies.Features).That().HaveNameEndingWith("Endpoint").GetTypes()
            .Select(t => t.FullName!).Where(name => name.Split('.').Length != 4).Should().BeEmpty();
    }

    /// <summary>
    /// A request or a response is its own sealed record: an endpoint never returns a stored document or entity, because that
    /// would expose every stored field and tie the API to the schema (standard 09, rule 7).
    /// </summary>
    [Fact]
    public void Endpoints_return_sealed_records_and_not_stored_types()
    {
        var offenders = EndpointMethods()
            .Select(m => (Method: m, Response: Unwrap(m.ReturnType)))
            .Select(x => (x.Method, Response: IsValueTuple(x.Response) ? x.Response.GetGenericArguments()[0] : x.Response))
            .Where(x => !IsStorageAction(x.Response))   // an endpoint with no response body returns only the side effect, which names the stored type on purpose
            .Select(x => (x.Method, Response: x.Response.IsGenericType ? x.Response.GetGenericArguments()[0] : x.Response))
            .Where(x => TemplateAssemblies.Features == x.Response.Assembly)
            .Where(x => !x.Response.IsSealed || x.Response.GetMethod("<Clone>$") is null)
            .Select(x => $"{x.Method.DeclaringType!.Name}.{x.Method.Name} returns {x.Response.Name}");

        offenders.Should().BeEmpty();
    }

    private static readonly string[] SideEffectNames = ["ISideEffect", "IResult", "ProblemDetails"];

    private static IEnumerable<MethodInfo> HandlerMethods()
    {
        return TemplateAssemblies.Features.GetTypes()
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.StartsWith("Handle", StringComparison.Ordinal) || m.Name.StartsWith("Consume", StringComparison.Ordinal));
    }

    [Fact]
    public void Handlers_exist_so_the_rule_below_is_not_vacuous()
    {
        HandlerMethods().Should().NotBeEmpty();
    }

    /// <summary>
    /// Every message an endpoint or handler returns is an <see cref="IInternalMessage"/> (standard 10, rule 9). Only those types
    /// are routed to the topic (<c>TemplateMessaging</c>), and local routing is off, so any other cascaded type would fail at send time.
    /// Everything after the response in an endpoint's return tuple, and everything a handler returns, must be a side effect or such a
    /// message. <c>OutgoingMessages</c> is refused because its content cannot be checked here.
    /// </summary>
    [Fact]
    public void Every_cascaded_message_is_an_internal_message()
    {
        var offenders = new List<string>();
        foreach (var method in EndpointMethods())
        {
            var returned = Unwrap(method.ReturnType);
            var items = IsValueTuple(returned) ? returned.GetGenericArguments() : [returned];
            // The first item is the response, unless the endpoint has none: then everything it returns is cascaded.
            var cascaded = method.GetCustomAttribute<EmptyResponseAttribute>() is not null ? items : items.Skip(1);
            offenders.AddRange(cascaded.Where(t => t != typeof(void) && t != typeof(Task) && t != typeof(ValueTask) && !IsAllowedCascade(t))
                .Select(t => $"{method.DeclaringType!.Name}.{method.Name} cascades {t.Name}"));
        }
        foreach (var method in HandlerMethods())
        {
            var returned = Unwrap(method.ReturnType);
            var cascaded = IsValueTuple(returned) ? returned.GetGenericArguments() : [returned];
            offenders.AddRange(cascaded.Where(t => t != typeof(void) && t != typeof(Task) && t != typeof(ValueTask) && !IsAllowedCascade(t))
                .Select(t => $"{method.DeclaringType!.Name}.{method.Name} returns {t.Name}"));
        }

        offenders.Should().BeEmpty("a returned message must implement IInternalMessage so that it has a route");
    }

    /// <summary>Messages are sealed records (standard 13, rule 2): immutable, with value equality, and nothing derives from them.</summary>
    [Fact]
    public void Internal_messages_are_sealed_records()
    {
        var messages = TemplateAssemblies.All.SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true } && typeof(IInternalMessage).IsAssignableFrom(t)).ToList();

        messages.Should().NotBeEmpty("the template has a sample message");
        messages.Where(t => !t.IsSealed || t.GetMethod("<Clone>$") is null).Select(t => t.FullName).Should().BeEmpty();
    }

    private static bool IsAllowedCascade(Type type)
    {
        return typeof(IInternalMessage).IsAssignableFrom(type)
        || IsSideEffect(type)
        || (type.IsGenericType && type.GetGenericArguments() is [var element] && typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && IsAllowedCascade(element));
    }

    private static bool IsStorageAction(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition().Name == "IStorageAction`1";
    }

    private static bool IsSideEffect(Type type)
    {
        return SideEffectNames.Contains(type.IsGenericType ? type.GetGenericTypeDefinition().Name : type.Name)
        || typeof(ISideEffect).IsAssignableFrom(type) || typeof(IResult).IsAssignableFrom(type) || typeof(ProblemDetails).IsAssignableFrom(type);
    }

    private static bool IsValueTuple(Type type)
    {
        return type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple", StringComparison.Ordinal);
    }

    private static Type Unwrap(Type type)
    {
        return type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? type.GetGenericArguments()[0]
            : type;
    }
}
