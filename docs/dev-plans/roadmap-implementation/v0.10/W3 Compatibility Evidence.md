# W3 Compatibility Evidence

This records bounded results for the unreleased W3 branch. It supplements the [signature inventory](Async%20Signature%20Inventory%20and%20Compatibility%20Matrix.md); a row is not complete merely because one consumer passes.

## Candidate 78d1b3c4

Packed all six public packages using `publish-nuget.ps1 -PackOnly -Version 0.10.0-w3.78d1b3c4` from clean commit `78d1b3c4436da22975ab73856907a73a18721c18`. No packages were published. A clean non-incremental Dev CLI/DevTools build supplied the report runner. The baseline is the tracked, hash-locked NuGet.org 0.9.2 set, not a newly selected baseline.

The candidate directory is `artifacts/nuget-release/0.10.0-w3.78d1b3c4`. Report paths below are relative to the repository root. SHA-256 values identify the exact report bytes; generated absolute paths and timestamps make reports machine/run specific.

| Report | Result | SHA-256 |
| --- | --- | --- |
| `artifacts/release/v0.10/w3-78d1b3c4/packages/report.json` | Passed; six packages/symbol packages, no findings, valid clean candidate evidence | `64e95e3ec26b176c30fa129f06b4fcb12bf5638cd706c59ab4ea981e8d2375ef` |
| `artifacts/release/v0.10/w3-78d1b3c4/api/report.json` | Raw compatibility gate failed with the 13 diagnostics dispositioned below; valid clean runner evidence | `a3857c0d60aa36e080f86331227f7b47b94c38977485aceec2ded33e567a5293` |
| `artifacts/release/v0.10/w3-78d1b3c4/smoke-retry/report.json` | Passed isolated package restore, three builds/generated-source checks and net10 synchronous execution | `2c2f3d184ac1c6b1ea45fe882e441ec0c64cb81de5106677eb41428335be15f5` |
| `artifacts/w3-consumers/78d1b3c4/verified-final/report.json` | Nine async/old-binary/EF/Memory consumer executions passed on actual 8.0.31/9.0.20/10.0.12 runtimes | `c2f780c84243bdb66a23fbc2930505b9ed58df533c5d6526eba6b14a579532f0` |

The first package smoke restore failed on sandbox NuGet source access, without building or running consumers. An escalated isolated retry passed. New fixture restores hit the same source/TLS limitation and passed after escalation. Those infrastructure failures remain in their logs.

## ApiCompat findings

Pinned ApiCompat 10.0.400 captured 36 metadata surfaces across the six baseline/candidate packages and three TFMs. It reported 867 additive/compatible changes, two already locked protected-field divergences and no new cross-TFM mismatches. Full snapshots, exact arguments, stdout/stderr and generated suppression output remain under `artifacts/release/v0.10/w3-78d1b3c4/api`. No suppression was applied to make the report pass.

| Diagnostics | Exact boundary | Disposition |
| --- | --- | --- |
| Nine CP0002 findings | Removed `AsEnumerable()` on `IImmutableRelation<T>`, `ImmutableRelation<T,TKey>` and `ImmutableRelationMock<T>`, each on net8/9/10 | Approved AAPI-11 break. Pair consumers must migrate to `AsKeyValuePairs()` and recompile. Ordinary LINQ `AsEnumerable()` now yields rows. |
| Two CP0006 findings | Added abstract `IImmutableRelation<T>.AsKeyValuePairs()`, reported on net8/9 | Same approved rename. Old custom relation implementations require migration; these findings are not evidence that arbitrary old relation binaries remain compatible. |
| Two CP0006 findings | Added static `IImmutable<T>.GetByProviderKeyAsync<TKey>`, reported on net8/9 | Accepted K03 declaration. Candidate reflection verifies a static, non-abstract, non-virtual method with a body. An unchanged 0.9.2 implementation loads on each actual runtime without implementing the new member. This bounded old-binary evidence supports treating the diagnostic as a tool classification limitation for this exact member; the raw finding is retained. |

The static-member disposition is consistent with the [C# default-interface specification](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-8.0/default-interface-methods.md): static methods with bodies do not create an abstract implementation requirement. It does not suppress other interface additions or establish why ApiCompat reports the finding only for two TFMs.

AAPI-16's required-reference correction is behavioral/generated-code compatibility work, not an ApiCompat pass: already compiled getters retain their old behavior until regenerated. The existing migration documentation and required/optional generator/runtime tests remain applicable.

## Consumer results and limits

The [fixtures and reproduction instructions](../../../../tests/fixtures/W3%20Consumers.md) live outside the friend test assemblies. Candidate fixtures consume exact package versions. The old fixture builds only against 0.9.2. Verification copies each old DLL once and loads those unchanged bytes into the candidate process. It records both package-cache/archive hashes and executed core identity.

| Old consumer TFM | Unchanged DLL SHA-256 |
| --- | --- |
| net8.0 | `04529a6b054deae295a2779e4794948caf6d17db8a1b49a2536351618c60666d` |
| net9.0 | `8533890cd1ee2bb3e82363a0804d23ed6366cca77d5ca7ab28b0e728f453156f` |
| net10.0 | `dbccb74680c9169678abcca44fa62bf5314ad4f3428a10457b17e409360f6ac4` |

- **B01/B11:** old generated SQLite insert/query/transaction execution passes. Old provider/access/transaction/source/factory implementations load and new defaults reject without touching their throwing synchronous methods. This is actual old-binary evidence for those shapes, not just recompilation of old source.
- **B04:** EF Core 9.0.5 and DataLinq imports produce only the expected CS0121 `CountAsync` ambiguity in the negative fixture on all TFMs. Static aliases bind independently and reject foreign providers. Positive scalar, nullable, interface, anonymous and DTO receivers compile; this does not expand supported query translation. Negative output is `artifacts/w3-consumers/78d1b3c4/ef-ambiguity.log`.
- **B08/B09/B12:** the packed generator performs typed edited insertion and key lookup; packed queries exercise scalar/numeric results and a typed async callback; the public raw reader opens/advances/disposes. Full nineteen-receiver, composite/converted key, navigation and diagnostic tests remain separate, not implied by these simple model cases.
- **B14:** actual restore graphs resolve `System.Linq.AsyncEnumerable/10.0.12` transitively on net8/net9, and omit it on net10. Existing non-friend public consumer sources execute through the packed host on all three runtimes.
- **B15:** the Memory-only graph contains no SQLite/MySQL/MariaDB dependency. Supported query/lookup and cancellation cases pass; Sum remains a `QueryBackendCapabilityException`. A separate empty graph mock passes without claiming Memory navigation support.

An initial Memory fixture omitted its generated required Guid field, then caught the wrong exception type for unsupported aggregation. Both fixture assumptions were corrected; runtime code was unchanged. All subsequent builds report zero warnings/errors.

The additional verifier checks prebuilt fixtures and records source hashes; it is not a clean source-to-binary attestation. The package/API reports bind the clean candidate, while the additional consumer fixtures were developed on a working tree. Full emitted-family reconciliation, broader generated/constructor consumer coverage, XML/usage documentation and final W3 head evidence remain open. B16 remains W4/W5; benchmarks remain deferred.

## Expanded Generated, Constructor And Manifest Capture

The expanded exact-package fixture passes all nine runtime combinations again in `artifacts/w3-consumers/78d1b3c4/reconciled-contracts/report.json` (SHA-256 `7f8c80df224772795115444f0a0184acb1d4338c478e12a1cb166267860a9371`). It compiles all nineteen generated mutation receiver forms and executes composite converted-key lookups on all three sources, required/optional navigation sharing synchronous state, and composite-key relation loading. Separate generator/provider suites retain nullable-disabled, DLG004 and full lifetime semantics coverage.

Identical old constructor-call source compiles against baseline and candidate packages. The unchanged old DLL instantiates an external SqlProvider subclass and receives default captured options. Abstract external subclasses compile the preserved generic/nongeneric base constructors; candidate compilation also covers each accepted required options forwarding form. The baseline experiment disproved an assumed SQLite untyped-null ambiguity: the two-argument `(connectionString, null)` call selects DataLinqLoggingConfiguration. Expression inspection verifies the same selection in old and candidate binaries on every runtime. No constructor change was needed.

Old foreign-key covariance survives without gaining async capability or reading the synchronous Value getter. An unchanged old binary calling the removed keyed AsEnumerable fails with the expected MissingMethodException, providing a concrete AAPI-11 migration boundary rather than claiming every old binary works.

Compiled manifests capture 63 selected types, including generated declarations and the explicit inherited private default disposal slot. The [tracked review projection and family map](../../../../test-infra/api-compatibility/w3/README.md) reconcile the accepted W3 inventory, including the protected AAPI-112 bridge. Original JSON hashes:

- net8: `3fcccfcbfc1820e9a2428589c4fa12ae029a3ed589faaa0b604c6e868ac9724e`.
- net9: `3755a9263810b1075007fa08368a7585f531450448514909a8fe45f427b0865d`.
- net10: `bf0b7da88e931079ade2451bbadaea42f893f8acd61c7bd66b7e60b94170d1fc`.

The only nullable interpretation difference is an existing Select constructor's nested generic argument under .NET 8 reflection. Raw metadata matches; the same net8 bytes reflect the net9/10 interpretation under an explicitly selected .NET 10 host. That diagnostic is not counted as actual net8 runtime evidence. No product change or suppression was made.

These fixtures/manifests extend the earlier clean 78d1b3c4 package candidate. Final clean-head integration verification remains required; the guide and metadata capture do not by themselves constitute release certification.
