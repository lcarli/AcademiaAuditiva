namespace AcademiaAuditiva.UnitTests;

/// <summary>
/// Smoke test that fails fast if the test infrastructure isn't wired correctly.
/// Real unit tests will replace this once we start refactoring services.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void TestHarness_IsWired()
    {
        true.Should().BeTrue();
    }
}
