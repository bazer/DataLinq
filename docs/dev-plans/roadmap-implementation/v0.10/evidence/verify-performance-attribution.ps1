$ErrorActionPreference = 'Stop'
$expectedW1 = 'a435d0b428937040bfd02210e67eb1219c9ed6f7'
$expectedW2 = '49176d7a50d51a4de1848b501652e82215793b98'
$receipts = [Collections.Generic.List[object]]::new()
function Assert([bool] $condition, [string] $message) { if (-not $condition) { throw $message } }
function Receipt([string] $path) {
    $file = Get-Item -LiteralPath $path
    $receipts.Add([pscustomobject]@{ Path=$path; Length=$file.Length; Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() })
}
function VerifyDependencies($dependencies, [string] $commit) {
    $harness = @($dependencies | Where-Object Name -EQ 'DataLinq.Benchmark')
    Assert ($harness.Count -eq 1 -and $harness[0].Metadata.DataLinqRepositoryBuildState -eq 'clean') 'Harness is not clean'
    foreach ($dependency in $dependencies) {
        if ($dependency.Name.StartsWith('DataLinq')) {
            Assert ($dependency.InformationalVersion.EndsWith($commit)) ('Wrong version: ' + $dependency.Name)
        }
        $verificationPath = $dependency.Path
        $originalCandidateOutput = 'D:\git\DataLinq\src\DataLinq.Benchmark\bin\Release\net10.0\'
        if ($commit -eq $expectedW2 -and $verificationPath.StartsWith($originalCandidateOutput, [StringComparison]::OrdinalIgnoreCase)) {
            $verificationPath = Join-Path 'artifacts/benchmarks/bin-snapshots/w2-49176d7a' $verificationPath.Substring($originalCandidateOutput.Length)
        }
        Assert ((Get-FileHash -LiteralPath $verificationPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq $dependency.Sha256) ('Changed retained dependency: ' + $verificationPath)
    }
}
$allocationRows = foreach ($case in @('update', 'crud-small', 'crud-batch')) {
    $pair = foreach ($runtime in @('w1', 'w2')) {
        $prefix = 'artifacts/w2-mutation-' + $runtime + '-' + $case
        $data = Get-Content -LiteralPath ($prefix + '.json') -Raw | ConvertFrom-Json
        $commit = if ($runtime -eq 'w1') { $expectedW1 } else { $expectedW2 }
        Assert ($data.AssemblyVersion.EndsWith($commit)) 'Wrong allocation runtime'
        VerifyDependencies $data.DependenciesBefore $commit
        VerifyDependencies $data.DependenciesAfter $commit
        Assert ($data.Iterations -eq 10 -and $data.Warmups -eq 20 -and $data.InvocationAllocatedBytes.Count -eq 10) 'Wrong invocation count'
        Assert ($data.TotalOperations -eq $data.Iterations * $data.OperationsPerInvoke -and $data.SampleTicks -gt 0) 'Missing operations or trace'
        Assert ([math]::Abs(($data.InvocationAllocatedBytes | Measure-Object -Sum).Sum / $data.TotalOperations - $data.WorkloadThreadBytesPerOperation) -lt 0.00001) 'Allocation arithmetic mismatch'
        Receipt ($prefix + '.json')
        Receipt ($prefix + '.nettrace')
        $data
    }
    $a, $b = $pair
    Assert ($a.Checksum -eq $b.Checksum -and $a.TotalOperations -eq $b.TotalOperations) 'Result/operation mismatch'
    Assert (($a.TelemetryReplay | ConvertTo-Json -Compress -Depth 10) -eq ($b.TelemetryReplay | ConvertTo-Json -Compress -Depth 10)) 'Telemetry mismatch'
    Assert ($a.ProbeAssemblySha256 -eq $b.ProbeAssemblySha256) 'Different allocation probe'
    $scopePattern = 'ExecutionFailureScope|System.Threading.ExecutionContext|AsyncLocalValueMap'
    $scopeA = ($a.Types | Where-Object Type -Match $scopePattern | Measure-Object SampledBytes -Sum).Sum / $a.TotalOperations
    $scopeB = ($b.Types | Where-Object Type -Match $scopePattern | Measure-Object SampledBytes -Sum).Sum / $b.TotalOperations
    $types = foreach ($name in (@($a.Types.Type + $b.Types.Type) | Sort-Object -Unique)) {
        $first = $a.Types | Where-Object Type -EQ $name
        $second = $b.Types | Where-Object Type -EQ $name
        [pscustomobject]@{ Type=$name; W1SampledBytesPerOperation=$first.SampledBytes / $a.TotalOperations; W2SampledBytesPerOperation=$second.SampledBytes / $b.TotalOperations }
    }
    [pscustomobject]@{ Method=$a.Method; W1Bytes=$a.WorkloadThreadBytesPerOperation; W2Bytes=$b.WorkloadThreadBytesPerOperation; AddedBytes=$b.WorkloadThreadBytesPerOperation-$a.WorkloadThreadBytesPerOperation; W1ScopeSample=$scopeA; W2ScopeSample=$scopeB; ScopeSampleAdded=$scopeB-$scopeA; W1SampleTicks=$a.SampleTicks; W2SampleTicks=$b.SampleTicks; Checksum=$a.Checksum; TotalOperations=$a.TotalOperations; Types=$types }
}
$timingRuns = foreach ($name in @('w1-forward', 'w2-forward', 'w2-reverse', 'w1-reverse')) {
    $path = 'artifacts/w2-mutation-timing-' + $name + '.json'
    $data = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $commit = if ($name.StartsWith('w1')) { $expectedW1 } else { $expectedW2 }
    VerifyDependencies $data.Dependencies $commit
    Assert ($data.AssemblyVersion.EndsWith($commit) -and $data.Rows.Count -eq 3) 'Wrong mutation timing run'
    foreach ($row in $data.Rows) {
        Assert ($row.Warmups -ge 64 -and $row.MeasuredInvocations -ge 64 -and $row.TotalMeasuredWorkSeconds -ge 0.25) 'Short timing run'
        Assert ($row.InvocationChecksums.Count -eq $row.MeasuredInvocations -and $row.InvocationTicks.Count -eq $row.MeasuredInvocations) 'Missing samples'
        for ($i = 0; $i -lt $row.MeasuredInvocations; $i++) {
            $expected = [long]$row.InitialChecksum + ([long]$row.Warmups + $i) * $row.ChecksumIncrementPerInvocation
            $expected = (($expected + 2147483648L) % 4294967296L) - 2147483648L
            Assert ($row.InvocationChecksums[$i] -eq $expected) 'Checksum mismatch'
        }
    }
    Receipt $path
    [pscustomobject]@{ Name=$name; Data=$data }
}
$timingRows = foreach ($method in @('UpdateEmployees', 'CrudWorkflowSmall', 'CrudWorkflowBatch')) {
    $samples = foreach ($run in $timingRuns) { $row = $run.Data.Rows | Where-Object Method -EQ $method; [pscustomobject]@{ Run=$run.Name; Row=$row } }
    $firstTelemetry = $samples[0].Row.TelemetryReplay | ConvertTo-Json -Compress -Depth 10
    foreach ($sample in $samples) {
        Assert (($sample.Row.TelemetryReplay | ConvertTo-Json -Compress -Depth 10) -eq $firstTelemetry) 'Timing telemetry mismatch'
        Assert ($sample.Row.InitialChecksum -eq $samples[0].Row.InitialChecksum) 'Initial checksum mismatch'
    }
    [pscustomobject]@{ Method=$method; Samples=$samples }
}
$remainingRuns = foreach ($name in @('w1-forward', 'w2-forward', 'w2-reverse', 'w1-reverse')) {
    $path = 'artifacts/w2-remaining-timing-' + $name + '.json'
    $data = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $commit = if ($name.StartsWith('w1')) { $expectedW1 } else { $expectedW2 }
    VerifyDependencies $data.Dependencies $commit
    Assert ($data.AssemblyVersion.EndsWith($commit) -and $data.Rows.Count -eq 11) 'Wrong remaining timing run'
    foreach ($row in $data.Rows) {
        Assert ($row.Warmups -ge 64 -and $row.WarmupElapsedSeconds -ge 2 -and $row.MeasuredInvocations -ge 64 -and $row.TotalMeasuredWorkSeconds -ge 0.25) 'Short remaining timing run'
        Assert ($row.InvocationChecksums.Count -eq $row.MeasuredInvocations -and $row.InvocationTicks.Count -eq $row.MeasuredInvocations) 'Missing remaining samples'
        Assert (@($row.InvocationChecksums | Where-Object { $_ -ne $row.InitialChecksum }).Count -eq 0) 'Remaining checksum changed'
    }
    Receipt $path
    [pscustomobject]@{ Name=$name; Data=$data }
}
$remainingRows = foreach ($firstRow in $remainingRuns[0].Data.Rows) {
    $samples = foreach ($run in $remainingRuns) { $row = $run.Data.Rows | Where-Object { $_.Method -eq $firstRow.Method -and $_.Provider -eq $firstRow.Provider }; [pscustomobject]@{ Run=$run.Name; Row=$row } }
    $firstTelemetry = $firstRow.TelemetryReplay | ConvertTo-Json -Compress -Depth 10
    foreach ($sample in $samples) {
        Assert (($sample.Row.TelemetryReplay | ConvertTo-Json -Compress -Depth 10) -eq $firstTelemetry) 'Remaining timing telemetry mismatch'
        if ($firstRow.Method -ne 'BinaryCanonicalKeyPropagation') { Assert ($sample.Row.InitialChecksum -eq $firstRow.InitialChecksum) 'Remaining initial checksum mismatch' }
    }
    [pscustomobject]@{ Method=$firstRow.Method; Provider=$firstRow.Provider; Samples=$samples }
}
Assert (@($timingRuns.Data.ProbeAssemblySha256 | Sort-Object -Unique).Count -eq 1) 'Mutation probe changed'
Assert (@($remainingRuns.Data.ProbeAssemblySha256 | Sort-Object -Unique).Count -eq 1) 'Remaining probe changed'
$report = [pscustomobject]@{ W1=$expectedW1; W2=$expectedW2; Purpose='Supplemental diagnostic attribution; not strict release evidence or an equivalence test'; AllocationRows=$allocationRows; TimingRows=$timingRows; RemainingTimingRows=$remainingRows; Receipts=$receipts }
$report | ConvertTo-Json -Depth 25 | Set-Content 'artifacts/w2-performance-attribution-verification.json' -Encoding utf8
$allocationRows | Select-Object Method,W1Bytes,W2Bytes,AddedBytes,ScopeSampleAdded | Format-Table
foreach ($row in $timingRows) { $row.Samples | Select-Object Run, @{n='Method';e={$row.Method}}, @{n='MeanUs';e={$_.Row.MeanMicrosecondsPerOperation}}, @{n='MedianUs';e={$_.Row.MedianMicrosecondsPerOperation}}, @{n='Bytes';e={$_.Row.ManagedThreadBytesPerOperation}} | Format-Table }
foreach ($row in $remainingRows) { $row.Samples | Select-Object Run, @{n='Method';e={$row.Method}}, @{n='MeanUs';e={$_.Row.MeanMicrosecondsPerOperation}}, @{n='MedianUs';e={$_.Row.MedianMicrosecondsPerOperation}}, @{n='Bytes';e={$_.Row.ManagedThreadBytesPerOperation}} | Format-Table }
