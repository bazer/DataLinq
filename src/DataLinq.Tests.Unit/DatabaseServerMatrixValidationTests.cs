using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;

namespace DataLinq.Tests.Unit;

public sealed class DatabaseServerMatrixValidationTests
{
    [Test]
    public async Task Validate_RejectsMissingAmbiguousAndProfileMismatchedDefaults()
    {
        var targets = DatabaseServerMatrix.Targets.ToArray();
        var profiles = DatabaseServerMatrix.Profiles.ToArray();

        var missingMySqlDefault = targets
            .Select(static target => target.Family == DatabaseServerFamily.MySql
                ? target with { IsDefault = false }
                : target)
            .ToArray();
        var missing = Capture(() => DatabaseServerMatrix.Validate(missingMySqlDefault, profiles, "missing-default"));

        var ambiguousMySqlDefault = targets
            .Select(static target => target.Id == "mysql-8.4"
                ? target with { IsDefault = true }
                : target)
            .ToArray();
        var ambiguous = Capture(() => DatabaseServerMatrix.Validate(ambiguousMySqlDefault, profiles, "ambiguous-default"));

        var mismatchedProfiles = profiles
            .Select(profile => profile.IsDefault
                ? DatabaseServerProfile.Create(
                    profile.Id,
                    profile.DisplayName,
                    profile.IsDefault,
                    [
                        targets.Single(static target => target.Id == "mysql-8.4"),
                        targets.Single(static target => target.Id == "mariadb-12.3")
                    ])
                : profile)
            .ToArray();
        var mismatch = Capture(() => DatabaseServerMatrix.Validate(targets, mismatchedProfiles, "profile-mismatch"));

        await Assert.That(missing).IsTypeOf<InvalidOperationException>();
        await Assert.That(missing!.Message).Contains("exactly one default target for family 'MySql'; found 0");
        await Assert.That(ambiguous).IsTypeOf<InvalidOperationException>();
        await Assert.That(ambiguous!.Message).Contains("exactly one default target for family 'MySql'; found 2");
        await Assert.That(mismatch).IsTypeOf<InvalidOperationException>();
        await Assert.That(mismatch!.Message).Contains("must contain exactly the explicitly configured family defaults");
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
