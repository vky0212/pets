$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $projectRoot 'src\Program.cs'
$updateSource = Join-Path $projectRoot 'src\UpdateManager.cs'
$updaterSource = Join-Path $projectRoot 'src\Updater.cs'
$manifest = Join-Path $projectRoot 'src\app.manifest'
$sprite = Join-Path $projectRoot 'assets\spritesheet.png'
$icon = Join-Path $projectRoot 'assets\LittleRedWitch.ico'
$buildDir = Join-Path $projectRoot 'build'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Windows .NET Framework C# compiler was not found.'
}

if (-not (Test-Path -LiteralPath $sprite)) {
    throw 'assets\spritesheet.png was not found.'
}

New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$output = Join-Path $buildDir 'LittleRedWitch.exe'
$updaterOutput = Join-Path $buildDir 'LittleRedWitch.Updater.exe'
$frameworkDir = Split-Path -Parent $compiler
$presentationCore = 'C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll'
$presentationFramework = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll'
$windowsBase = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll'
$systemXaml = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll'
$system = Join-Path $frameworkDir 'System.dll'
$systemCore = Join-Path $frameworkDir 'System.Core.dll'
$systemIoCompression = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.IO.Compression\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.dll'
$systemIoCompressionFileSystem = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.IO.Compression.FileSystem\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.FileSystem.dll'

$compilerArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:x64'
    '/optimize+'
    '/codepage:65001'
    "/win32manifest:$manifest"
    "/win32icon:$icon"
    "/out:$output"
    "/reference:$presentationCore"
    "/reference:$presentationFramework"
    "/reference:$windowsBase"
    "/reference:$systemXaml"
    "/reference:$system"
    "/reference:$systemCore"
    "/reference:$systemIoCompression"
    "/reference:$systemIoCompressionFileSystem"
    "/resource:$sprite,LittleRedWitch.Resources.spritesheet.png"
    $source
    $updateSource
)

& $compiler $compilerArgs

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$updaterArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:x64'
    '/optimize+'
    '/codepage:65001'
    "/win32manifest:$manifest"
    "/win32icon:$icon"
    "/out:$updaterOutput"
    "/reference:$system"
    "/reference:$systemCore"
    $updaterSource
)

& $compiler $updaterArgs

if ($LASTEXITCODE -ne 0) {
    throw "Updater compilation failed with exit code $LASTEXITCODE"
}

Get-Item -LiteralPath $output, $updaterOutput
