[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $GatewaySourceRoot,

    [Parameter(Mandatory)]
    [string] $MigrationSourceRoot,

    [Parameter(Mandatory)]
    [string] $OutputRoot,

    [Parameter(Mandatory)]
    [string] $ExpectedAccountingCommit,

    [Parameter(Mandatory)]
    [string] $ExpectedGatewayCommit,

    [Parameter(Mandatory)]
    [string] $ExpectedMigrationCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ExistingDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Container)) {
        throw "$Name is not a directory: $Path"
    }

    return $resolved.Path
}

function Assert-GitCommit {
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string] $ExpectedCommit
    )

    $actualCommit = (& git -C $Repository rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the Git commit for $Repository."
    }

    if ($actualCommit -ne $ExpectedCommit) {
        throw "Unexpected Git commit for $Repository. Expected $ExpectedCommit, found $actualCommit."
    }

    return [ordered]@{
        Commit = $actualCommit
        Status = @(& git -C $Repository status --short)
    }
}

function Invoke-DotNetLogged {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [Parameter(Mandatory)]
        [string] $LogPath
    )

    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $LogPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed. See $LogPath."
    }
}

function Get-PublishManifest {
    param(
        [Parameter(Mandatory)]
        [string] $PublishRoot
    )

    return @(
        Get-ChildItem -LiteralPath $PublishRoot -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                [ordered]@{
                    Path = [IO.Path]::GetRelativePath($PublishRoot, $_.FullName).Replace('\\', '/')
                    Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                }
            }
    )
}

$accountingRoot = Resolve-ExistingDirectory -Path (Join-Path $PSScriptRoot '..\..') -Name 'Accounting source root'
$gatewayRoot = Resolve-ExistingDirectory -Path $GatewaySourceRoot -Name 'Gateway source root'
$migrationRoot = Resolve-ExistingDirectory -Path $MigrationSourceRoot -Name 'Migration source root'
$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$sessionRoot = Join-Path $resolvedOutputRoot ("familypro-cli-gateway-{0:yyyyMMdd-HHmmss}-{1}" -f [DateTime]::UtcNow, [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $sessionRoot 'publish'
$evidenceRoot = Join-Path $sessionRoot 'evidence'
New-Item -ItemType Directory -Path $publishRoot, $evidenceRoot -Force | Out-Null

$sourceIdentity = [ordered]@{
    Accounting = Assert-GitCommit -Repository $accountingRoot -ExpectedCommit $ExpectedAccountingCommit
    Gateway = Assert-GitCommit -Repository $gatewayRoot -ExpectedCommit $ExpectedGatewayCommit
    Migration = Assert-GitCommit -Repository $migrationRoot -ExpectedCommit $ExpectedMigrationCommit
}

$projects = [ordered]@{
    Accounting = Join-Path $accountingRoot 'HomeBudget.Accounting.Api\HomeBudget.Accounting.Api.csproj'
    Gateway = Join-Path $gatewayRoot 'HomeBudget.Backend.Gateway\HomeBudget.Backend.Gateway.csproj'
    Cli = Join-Path $migrationRoot 'FireBirdV25Client\FireBirdV25Client.csproj'
    Fixture = Join-Path $migrationRoot 'FireBirdV25Client.ReleaseFixture\FireBirdV25Client.ReleaseFixture.csproj'
}
$publishDirectories = [ordered]@{}
foreach ($projectName in $projects.Keys) {
    $publishDirectory = Join-Path $publishRoot $projectName.ToLowerInvariant()
    $publishDirectories[$projectName] = $publishDirectory
    Invoke-DotNetLogged `
        -Arguments @('publish', $projects[$projectName], '--configuration', 'Release', '--output', $publishDirectory) `
        -LogPath (Join-Path $sessionRoot "publish-$($projectName.ToLowerInvariant()).log")
}

$artifactIdentity = [ordered]@{}
foreach ($projectName in $publishDirectories.Keys) {
    $artifactIdentity[$projectName] = Get-PublishManifest -PublishRoot $publishDirectories[$projectName]
}

[ordered]@{
    CreatedAtUtc = [DateTime]::UtcNow.ToString('O')
    Source = $sourceIdentity
    Artifacts = $artifactIdentity
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $sessionRoot 'runtime-identity.json') -Encoding utf8

$env:FAMILYPRO_TEST_ACCOUNTING_DLL = Join-Path $publishDirectories.Accounting 'HomeBudget.Accounting.Api.dll'
$env:FAMILYPRO_TEST_ACCOUNTING_CONTENT_ROOT = $publishDirectories.Accounting
$env:FAMILYPRO_TEST_GATEWAY_DLL = Join-Path $publishDirectories.Gateway 'HomeBudget.Backend.Gateway.dll'
$env:FAMILYPRO_TEST_GATEWAY_CONTENT_ROOT = $publishDirectories.Gateway
$env:FAMILYPRO_TEST_CLI_DLL = Join-Path $publishDirectories.Cli 'FireBirdV25Client.dll'
$env:FAMILYPRO_TEST_FIXTURE_DLL = Join-Path $publishDirectories.Fixture 'FireBirdV25Client.ReleaseFixture.dll'
$env:FAMILYPRO_TEST_OUTPUT_ROOT = $evidenceRoot

$testStartedAtUtc = [DateTime]::UtcNow
$testLog = Join-Path $sessionRoot 'explicit-tests.log'
$testResults = Join-Path $sessionRoot 'test-results'
& dotnet test `
    (Join-Path $accountingRoot 'HomeBudget.Accounting.Api.IntegrationTests\HomeBudget.Accounting.Api.IntegrationTests.csproj') `
    --configuration Release `
    --filter 'FullyQualifiedName~HomeBudget.Accounting.MigrationVerification.FamilyProCliGatewayTests' `
    --logger "trx;LogFileName=familypro-cli-gateway.trx" `
    --results-directory $testResults `
    -- NUnit.ExplicitMode=Strict 2>&1 | Tee-Object -FilePath $testLog
$testExitCode = $LASTEXITCODE

[ordered]@{
    StartedAtUtc = $testStartedAtUtc.ToString('O')
    CompletedAtUtc = [DateTime]::UtcNow.ToString('O')
    ExitCode = $testExitCode
    Result = if ($testExitCode -eq 0) { 'PASS' } else { 'FAIL' }
    Log = $testLog
    Trx = Join-Path $testResults 'familypro-cli-gateway.trx'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sessionRoot 'verification-result.json') -Encoding utf8

Write-Host "Verification artifacts: $sessionRoot"
if ($testExitCode -ne 0) {
    throw "Family Pro CLI/Gateway verification failed. See $testLog."
}
