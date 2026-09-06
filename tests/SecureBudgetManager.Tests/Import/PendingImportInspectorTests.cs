using SecureBudgetManager.Core.Household;
using SecureBudgetManager.Core.Import;
using SecureBudgetManager.Core.Storage;

namespace SecureBudgetManager.Tests.Import;

public sealed class PendingImportInspectorTests
{
    [Fact]
    public void InspectingAPayloadDoesNotRequireTheEncryptedStore()
    {
        var payload = new HouseholdImportPayload
        {
            HouseholdName = "Sample household",
            Members =
            [
                new HouseholdMember { Id = HouseholdImportMergerTests.Alex, Name = "Alex", IsDiscretionaryEligible = true },
                new HouseholdMember { Id = HouseholdImportMergerTests.Sam, Name = "Sam", IsDiscretionaryEligible = true }
            ]
        };

        var path = Path.Combine(Path.GetTempPath(), $"sbm-import-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(payload, HouseholdImportSerializer.Options));

        try
        {
            var offer = PendingHouseholdImportInspector.Inspect(path, new BudgetDocument(), new DateOnly(2026, 9, 4));

            Assert.Contains(offer.Preview.Created, line => line.Contains("Alex", StringComparison.Ordinal));
            Assert.Contains(offer.Preview.Created, line => line.Contains("Sam", StringComparison.Ordinal));
            Assert.Empty(offer.Preview.Updated);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
