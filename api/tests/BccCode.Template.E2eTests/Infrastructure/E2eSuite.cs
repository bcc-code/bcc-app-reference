namespace BccCode.Template.E2eTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class E2eSuite : ICollectionFixture<AspireManager>
{
    public const string Name = "E2E";
}
