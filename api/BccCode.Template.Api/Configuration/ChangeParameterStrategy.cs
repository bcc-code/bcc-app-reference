using System.Reflection;
using BccCode.Template.Changes;
using JasperFx;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Wolverine.Http;
using Wolverine.Http.CodeGen;

namespace BccCode.Template.Api.Configuration;

/// <summary>
/// Supplies a <see cref="Change"/> parameter of an endpoint method from <see cref="ChangeStart.ForRequestAsync"/>, once
/// per request (ADR 0028). Without this, Wolverine would read the parameter from the request body.
/// </summary>
public sealed class ChangeParameterStrategy : IParameterStrategy
{
    private static readonly MethodInfo _start = typeof(ChangeStart).GetMethod(nameof(ChangeStart.ForRequestAsync))!;

    public bool TryMatch(HttpChain chain, IServiceContainer container, ParameterInfo parameter, out Variable? variable)
    {
        variable = null;
        if (parameter.ParameterType != typeof(Change)) return false;

        var call = chain.Middleware.OfType<MethodCall>().FirstOrDefault(m => m.Method == _start);
        if (call is null)
        {
            call = new MethodCall(typeof(ChangeStart), _start);
            chain.Middleware.Insert(0, call);
        }

        variable = call.ReturnVariable;
        return true;
    }
}
