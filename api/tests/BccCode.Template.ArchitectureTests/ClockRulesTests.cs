namespace BccCode.Template.ArchitectureTests;

/// <summary>Time comes from <c>TimeProvider</c> (tests use a fake one), never from the instance clock (standard 11, rule 8).</summary>
public class ClockRulesTests
{
    private static readonly ForbiddenApi[] InstanceClock = [new("System", "UtcNow"), new("System", "Now"), new("System", "Today")];

    [Fact]
    public void The_application_never_reads_the_instance_clock() =>
        ForbiddenApiScanner.FindCalls(TemplateAssemblies.All, InstanceClock).Should().BeEmpty();

    [Fact]
    public void The_scanner_detects_a_forbidden_call() =>
        ForbiddenApiScanner.FindCalls([typeof(ScannerFixture).Assembly], InstanceClock)
            .Should().ContainSingle(hit => hit.Contains(nameof(ScannerFixture.ReadsTheInstanceClock), StringComparison.Ordinal));
}
