# One-time setup: generates the solution and project files around the hand-written sources.
# Safe to re-run; steps that are already done are skipped.
# Requires the .NET 10 SDK. Run from anywhere:  ./scripts/bootstrap.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

Write-Host "Using .NET SDK $(dotnet --version)"

# Solution
if (-not (Test-Path 'Spellbound.slnx')) {
    Invoke-Dotnet new sln -n Spellbound --format slnx
}

# Domain: pure game rules (sources already exist, only the project file is generated)
if (-not (Test-Path 'src/Spellbound.Domain/Spellbound.Domain.csproj')) {
    Invoke-Dotnet new classlib -n Spellbound.Domain -o src/Spellbound.Domain -f net10.0
    Remove-Item 'src/Spellbound.Domain/Class1.cs' -ErrorAction SilentlyContinue
}

# Domain tests (sources already exist, only the project file is generated)
if (-not (Test-Path 'tests/Spellbound.Domain.Tests/Spellbound.Domain.Tests.csproj')) {
    Invoke-Dotnet new xunit -n Spellbound.Domain.Tests -o tests/Spellbound.Domain.Tests -f net10.0
    Remove-Item 'tests/Spellbound.Domain.Tests/UnitTest1.cs' -ErrorAction SilentlyContinue
    Invoke-Dotnet add tests/Spellbound.Domain.Tests reference src/Spellbound.Domain
}

# Web: Blazor Web App, interactive server rendering, Individual accounts.
# Note: the template generates EF Core + SQLite; the project has since moved to RavenDB (see docs/architecture.md).
if (-not (Test-Path 'src/Spellbound.Web/Spellbound.Web.csproj')) {
    Invoke-Dotnet new blazor -n Spellbound.Web -o src/Spellbound.Web -f net10.0 `
        --interactivity Server --all-interactive --auth Individual
    Invoke-Dotnet add src/Spellbound.Web reference src/Spellbound.Domain
}

Invoke-Dotnet sln Spellbound.slnx add src/Spellbound.Domain src/Spellbound.Web tests/Spellbound.Domain.Tests

if (-not (Test-Path '.git')) {
    git init
}

Invoke-Dotnet build
Invoke-Dotnet test --no-build

Write-Host ""
Write-Host "Bootstrap done. Next: dotnet run --project src/Spellbound.Web" -ForegroundColor Green
