$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$package = Get-ChildItem -Path $root -Filter "TacoInjector_*_x64.msix" |
    Select-Object -First 1

if (-not $package) {
    throw "Could not find TacoInjector x64 MSIX package in $root"
}

$dependencyPaths = @()

$x64Dependencies = Join-Path $root "Dependencies\x64"
if (Test-Path $x64Dependencies) {
    $dependencyPaths += Get-ChildItem `
        -Path $x64Dependencies `
        -Include *.appx,*.msix,*.appxbundle,*.msixbundle `
        -File `
        -Recurse |
        Select-Object -ExpandProperty FullName
}

$neutralDependencies = Join-Path $root "Dependencies\neutral"
if (Test-Path $neutralDependencies) {
    $dependencyPaths += Get-ChildItem `
        -Path $neutralDependencies `
        -Include *.appx,*.msix,*.appxbundle,*.msixbundle `
        -File `
        -Recurse |
        Select-Object -ExpandProperty FullName
}

$dependencyPaths = $dependencyPaths | Sort-Object -Unique

$arguments = @{
    Path = $package.FullName
    AllowUnsigned = $true
    ForceApplicationShutdown = $true
}

if ($dependencyPaths.Count -gt 0) {
    $arguments.DependencyPath = $dependencyPaths
}

Add-AppxPackage @arguments