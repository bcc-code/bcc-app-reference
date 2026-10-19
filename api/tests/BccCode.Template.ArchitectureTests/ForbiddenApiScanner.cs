using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace BccCode.Template.ArchitectureTests;

public sealed record ForbiddenApi(string NamespacePrefix, string MemberName)
{
    public bool Matches(MemberReference member) =>
        (member.Name == MemberName || member.Name == "get_" + MemberName || member.Name == "set_" + MemberName)
        && (member.DeclaringType?.Namespace ?? string.Empty).StartsWith(NamespacePrefix, StringComparison.Ordinal);
}

/// <summary>Finds call sites in IL. NetArchTest checks type dependencies, but these rules are about specific methods.</summary>
public static class ForbiddenApiScanner
{
    public static IReadOnlyList<string> FindCalls(IEnumerable<Assembly> assemblies, IReadOnlyCollection<ForbiddenApi> forbidden)
    {
        var hits = new List<string>();
        foreach (var assembly in assemblies)
        {
            using var module = ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in module.GetTypes())
                foreach (var method in type.Methods.Where(m => m.HasBody))
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode.OperandType == OperandType.InlineMethod
                            && instruction.Operand is MethodReference called
                            && forbidden.Any(f => f.Matches(called)))
                            hits.Add($"{type.FullName}.{method.Name} calls {called.DeclaringType.FullName}.{called.Name}");
                    }
        }
        return hits;
    }
}
