# Packed application testing consumer

This executable references the exact candidate version of `DataLinq.Testing`. Its .NET 8 target also pins the Microsoft DI container to 8.0.1 and verifies that a conflicting factory leaves a borrowed fixture usable after host disposal. Its generated model lives in the consumer assembly and includes a converted composite key, a required parent and an optional parent. It provisions the fixture, replaces a keyed registration, seeds both rows, checks relation cache freshness, and looks up the child again after clearing the cache.

Pack the release projects locally, then run the verifier with the candidate version and archive directory. Supply a .NET 9 host when it lives outside the default runtime installation.

```powershell
.\publish-nuget.ps1 -PackOnly -Version $version -PackageOutputPath $feed
.\tests\fixtures\W6Consumer\Verify.ps1 -CandidateDirectory $feed -CandidateVersion $version -DotNet9 $dotnet9
```

The verifier uses an isolated package cache, maps DataLinq IDs to the candidate feed, compares restored archive hashes and checks each target's dependency graph. It builds and executes on the actual .NET 8, 9 and 10 runtimes. `-DependencySource` accepts a local NuGet feed for offline external dependencies. Build outputs and restore assets are retained under `artifacts/w6-consumers/`.
