$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$buildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
if ($buildCommand) {
    $compilerPath = $buildCommand.Source
} else {
    $locator = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (!(Test-Path $locator)) { throw "Instala Visual Studio con Desarrollo de escritorio de .NET y el paquete de desarrollo .NET Framework 4.8." }
    $compilerPath = & $locator -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (!$compilerPath) { throw "No se encuentra MSBuild. Revisa la instalación de Visual Studio." }
}
& $compilerPath (Join-Path $projectRoot "ArmStudio.sln") /m /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw "La compilación ha fallado." }
& (Join-Path $projectRoot "ArmStudio.Tests\bin\Release\ArmStudio.Tests.exe")
if ($LASTEXITCODE -ne 0) { throw "Las verificaciones han fallado." }
$destination = Join-Path $projectRoot "dist\ArmStudio"
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($file in @("ArmStudio.exe", "ArmStudio.Core.dll", "ArmStudio.exe.config")) {
    Copy-Item (Join-Path $projectRoot "ArmStudio.WinForms\bin\Release\$file") $destination -Force
}
Copy-Item (Join-Path $projectRoot "examples") $destination -Recurse -Force
Copy-Item (Join-Path $projectRoot "README.md") $destination -Force
Write-Host "Aplicación preparada en $destination"
