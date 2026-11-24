using Microsoft.Extensions.Configuration;

namespace BccCode.Template.Tests.Persistence;

public sealed class PersistenceSchemaTests
{
    [Fact]
    public void Read_without_a_value_is_public()
    {
        PersistenceSchema.Read(Configure(null)).Should().Be("public");
    }

    [Fact]
    public void Read_returns_a_valid_name()
    {
        PersistenceSchema.Read(Configure("pr123")).Should().Be("pr123");
    }

    [Theory]
    [InlineData("PR12")]
    [InlineData("pr-12")]
    [InlineData("1pr")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("pr 1")]
    public void Read_refuses_an_invalid_name(string value)
    {
        var read = () => PersistenceSchema.Read(Configure(value));

        read.Should().Throw<InvalidOperationException>().WithMessage("*Persistence:Schema*");
    }

    private static IConfiguration Configure(string? value)
    {
        var values = value is null ? [] : new Dictionary<string, string?> { [PersistenceSchema.Key] = value };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
