using BccCode.Template.IntegrationTests.Infrastructure;

// One Postgres container for the whole run. An assembly fixture rather than a collection fixture, so test classes are not forced into
// one collection and xUnit runs them in parallel; each class works in a database of its own, so they share nothing but the server.
[assembly: AssemblyFixture(typeof(PostgresFixture))]
