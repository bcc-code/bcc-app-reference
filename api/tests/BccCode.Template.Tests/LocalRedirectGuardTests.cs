using BccCode.Template.Api.Configuration;

namespace BccCode.Template.Tests;

public class LocalRedirectGuardTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/notes")]
    [InlineData("/notes/123?tab=edit")]
    public void A_path_on_this_site_is_local(string url)
    {
        LocalRedirectGuard.IsLocal(url).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("notes")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("/\t/evil.example")]
    [InlineData("/notes\r\nSet-Cookie: x=1")]
    public void Anything_else_is_not_local(string? url)
    {
        LocalRedirectGuard.IsLocal(url).Should().BeFalse();
    }

    [Fact]
    public void SafeOrRoot_falls_back_to_the_root()
    {
        LocalRedirectGuard.SafeOrRoot("//evil.example").Should().Be("/");
    }
}
