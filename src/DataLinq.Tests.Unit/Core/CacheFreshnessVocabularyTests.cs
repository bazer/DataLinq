using System;
using System.Threading.Tasks;
using DataLinq.Cache;

namespace DataLinq.Tests.Unit.Core;

public class CacheFreshnessVocabularyTests
{
    [Test]
    public async Task CacheFreshnessStateNames_ReturnStableTelemetryNames()
    {
        await Assert.That(CacheFreshnessStateNames.GetName(CacheFreshnessState.Unknown)).IsEqualTo("unknown");
        await Assert.That(CacheFreshnessStateNames.GetName(CacheFreshnessState.AssumedFresh)).IsEqualTo("assumed_fresh");
        await Assert.That(CacheFreshnessStateNames.GetName(CacheFreshnessState.ExternallyInvalidated)).IsEqualTo("externally_invalidated");
        await Assert.That(CacheFreshnessStateNames.GetName(CacheFreshnessState.FreshnessChecked)).IsEqualTo("freshness_checked");
        await Assert.That(CacheFreshnessStateNames.GetName(CacheFreshnessState.Stale)).IsEqualTo("stale");
    }

    [Test]
    public async Task CacheFreshnessStateNames_RejectUnknownEnumValues()
    {
        var threw = false;

        try
        {
            _ = CacheFreshnessStateNames.GetName((CacheFreshnessState)999);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }
}
