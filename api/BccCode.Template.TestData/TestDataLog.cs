namespace BccCode.Template.TestData;

/// <summary>Log messages of the seeder, source-generated with fixed templates.</summary>
public sealed partial class TestDataLog(ILogger<TestDataLog> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} notes")]
    public partial void Seeded(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted the test data")]
    public partial void Reset();
}
