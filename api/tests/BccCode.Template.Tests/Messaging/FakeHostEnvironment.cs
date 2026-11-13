using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BccCode.Template.Tests.Messaging;

internal sealed class FakeHostEnvironment(string name) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = name;

    public string ApplicationName { get; set; } = "test";

    public string ContentRootPath { get; set; } = ".";

    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
