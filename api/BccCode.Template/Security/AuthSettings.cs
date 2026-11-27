namespace BccCode.Template.Security;

/// <summary>Configuration keys the features and the host both read.</summary>
public static class AuthSettings
{
    /// <summary>The role that makes a person an administrator. Empty names no role: nobody is an administrator (standard 15.5).</summary>
    public const string AdminRoleKey = "Auth:AdminRole";
}
