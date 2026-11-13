namespace BccCode.Template.Api.Configuration;

public static class CodeGeneration
{
    /// <summary>The prefix every JasperFx command needs: <c>-- jasper db-apply</c>, <c>-- jasper codegen write</c>.</summary>
    public const string JasperFxPrefix = "jasper";

    /// <summary>
    /// True for a JasperFx command, which must start with <c>jasper</c>. It serves no requests and sends no messages, so it needs
    /// none of the sign-in, Pub/Sub or <c>Internal</c> settings (the Cloud Run schema job has none). Any other arguments start
    /// the host.
    /// </summary>
    public static bool IsJasperFxCommand(IReadOnlyList<string> args)
    {
        return args.Count > 0 && args[0].Equals(JasperFxPrefix, StringComparison.Ordinal);
    }

    /// <summary>The same check on this process's arguments (element 0 of the command line is the program).</summary>
    public static bool IsJasperFxCommand()
    {
        return IsJasperFxCommand(Environment.GetCommandLineArgs().Skip(1).ToArray());
    }

    /// <summary>
    /// True while the OpenAPI document or the Wolverine code is generated (<c>-- jasper openapi ...</c>, <c>-- jasper codegen ...</c>).
    /// The host is built but never started then, so nothing may need infrastructure or a built frontend, and generation
    /// compiles at run time because no code is pre-generated yet.
    /// </summary>
    public static bool IsRunningGeneration()
    {
        return IsJasperFxCommand() && Environment.GetCommandLineArgs().Any(arg =>
            arg.Equals("codegen", StringComparison.OrdinalIgnoreCase) || arg.Equals("openapi", StringComparison.OrdinalIgnoreCase));
    }
}
