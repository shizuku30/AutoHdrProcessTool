[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Platform = 'x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

trap {
    Write-Error $_
    exit 1
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repositoryRoot 'AutoHdrProcessTool'
$assemblyPath = Join-Path $projectRoot "bin\$Platform\$Configuration\AutoHdrProcessTool.exe"
$testsPassed = 0

function Assert-Equal {
    param(
        [AllowNull()]$Expected,
        [AllowNull()]$Actual,
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-Test {
    param(
        [string]$Name,
        [scriptblock]$Test
    )

    & $Test
    $script:testsPassed++
    Write-Host "[PASS] $Name"
}

function Get-ResourceMap {
    param([string]$Path)

    [xml]$document = Get-Content -Raw -LiteralPath $Path
    $resources = @{}
    foreach ($entry in $document.root.data) {
        $resources[[string]$entry.name] = [string]$entry.value
    }

    return $resources
}

function Invoke-StaticMethod {
    param(
        [Type]$Type,
        [string]$Name,
        [object[]]$Arguments
    )

    $flags = [Reflection.BindingFlags]'Static, Public, NonPublic'
    $method = $Type.GetMethod($Name, $flags)
    Assert-True ($null -ne $method) "Method '$($Type.FullName).$Name' was not found."
    return $method.Invoke($null, $Arguments)
}

Push-Location $repositoryRoot
try {
    Invoke-Test 'Release assembly exists' {
        Assert-True (Test-Path -LiteralPath $assemblyPath -PathType Leaf) `
            "Build output was not found at '$assemblyPath'."
    }

    $englishResources = Get-ResourceMap (Join-Path $projectRoot 'Localization\en-us.resx')
    $chineseResources = Get-ResourceMap (Join-Path $projectRoot 'Localization\zh-cn.resx')

    Invoke-Test 'Localized resource sets match and contain values' {
        $difference = @(Compare-Object @($englishResources.Keys | Sort-Object) @($chineseResources.Keys | Sort-Object))
        Assert-Equal 0 $difference.Count 'English and Chinese resource keys differ.'
        Assert-True ($englishResources.Count -gt 0) 'No localized resources were found.'

        foreach ($key in $englishResources.Keys) {
            Assert-True (-not [string]::IsNullOrWhiteSpace($englishResources[$key])) `
                "English resource '$key' is empty."
            Assert-True (-not [string]::IsNullOrWhiteSpace($chineseResources[$key])) `
                "Chinese resource '$key' is empty."
        }
    }

    Invoke-Test 'Every localized resource is referenced by source code' {
        $sourceFiles = @(Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
        $source = ($sourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
        $matches = [regex]::Matches($source, 'Strings\.(?:Get|Format)\(\s*"([^"]+)"')
        $referencedKeys = @($matches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $resourceKeys = @($englishResources.Keys | Sort-Object)
        $difference = @(Compare-Object $resourceKeys $referencedKeys)
        Assert-Equal 0 $difference.Count 'Resource keys and source references differ.'
    }

    Invoke-Test 'Localized text is kept out of C# source files' {
        $sourceFiles = @(Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
        $matches = @($sourceFiles | Select-String -Pattern '[\u4e00-\u9fff]')
        Assert-Equal 0 $matches.Count 'C# source contains hard-coded CJK text.'
    }

    Invoke-Test 'Source does not use process-memory or injection APIs' {
        $sourceFiles = @(Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
        $forbiddenPattern = '\b(ReadProcessMemory|WriteProcessMemory|VirtualAllocEx|CreateRemoteThread|NtCreateThreadEx)\b|Process\.MainModule'
        $matches = @($sourceFiles | Select-String -Pattern $forbiddenPattern)
        Assert-Equal 0 $matches.Count 'A forbidden process-memory or injection API was found.'
    }

    $assembly = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($assemblyPath))

    Invoke-Test 'Assembly is x64 and version metadata is consistent' {
        $peKind = [Reflection.PortableExecutableKinds]::NotAPortableExecutableImage
        $machine = [Reflection.ImageFileMachine]::I386
        $assembly.ManifestModule.GetPEKind([ref]$peKind, [ref]$machine)
        Assert-Equal ([Reflection.ImageFileMachine]::AMD64) $machine 'The executable machine type is not AMD64.'
        Assert-True (($peKind -band [Reflection.PortableExecutableKinds]::PE32Plus) -ne 0) `
            'The executable is not PE32+.'

        $assemblyInfoPath = Join-Path $projectRoot 'Properties\AssemblyInfo.cs'
        $assemblyInfo = Get-Content -Raw -LiteralPath $assemblyInfoPath
        $assemblyVersion = [regex]::Match($assemblyInfo, 'AssemblyVersion\("([0-9.]+)"\)').Groups[1].Value
        $fileVersion = [regex]::Match($assemblyInfo, 'AssemblyFileVersion\("([0-9.]+)"\)').Groups[1].Value
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath)

        Assert-True (-not [string]::IsNullOrWhiteSpace($assemblyVersion)) 'AssemblyVersion was not found.'
        Assert-Equal $assemblyVersion $fileVersion 'Source assembly and file versions differ.'
        Assert-Equal $assemblyVersion $assembly.GetName().Version.ToString() 'Compiled assembly version differs from source.'
        Assert-Equal $fileVersion $versionInfo.FileVersion 'Compiled file version differs from source.'
        Assert-Equal $fileVersion $versionInfo.ProductVersion 'Compiled product version differs from source.'
    }

    $stringsType = $assembly.GetType('AutoHdrProcessTool.Localization.Strings', $true)

    Invoke-Test 'Chinese Windows languages select Simplified Chinese' {
        foreach ($cultureName in @('zh-CN', 'zh-TW', 'zh-HK', 'zh-Hans', 'zh-Hant')) {
            $selected = Invoke-StaticMethod $stringsType 'SelectApplicationUiCulture' @(
                [Globalization.CultureInfo]::GetCultureInfo($cultureName)
            )
            Assert-Equal 'zh-CN' $selected.Name "Culture '$cultureName' mapped incorrectly."
        }
    }

    Invoke-Test 'Non-Chinese Windows languages select English' {
        foreach ($cultureName in @('en-US', 'ja-JP', 'de-DE', 'fr-FR')) {
            $selected = Invoke-StaticMethod $stringsType 'SelectApplicationUiCulture' @(
                [Globalization.CultureInfo]::GetCultureInfo($cultureName)
            )
            Assert-Equal 'en-US' $selected.Name "Culture '$cultureName' mapped incorrectly."
        }
    }

    Invoke-Test 'Selected resources return the expected language' {
        Invoke-StaticMethod $stringsType 'Initialize' @([Globalization.CultureInfo]::GetCultureInfo('zh-TW')) | Out-Null
        $chineseTitle = Invoke-StaticMethod $stringsType 'Get' @('WindowTitle')
        Assert-Equal 'Auto HDR 进程工具' $chineseTitle 'Traditional Chinese did not select the Simplified Chinese resources.'

        Invoke-StaticMethod $stringsType 'Initialize' @([Globalization.CultureInfo]::GetCultureInfo('en-US')) | Out-Null
        $englishTitle = Invoke-StaticMethod $stringsType 'Get' @('WindowTitle')
        Assert-Equal 'Auto HDR Process Tool' $englishTitle 'English resources were not selected.'
    }

    $profileType = $assembly.GetType('AutoHdrProcessTool.Services.AutoHdrProfileService', $true)

    Invoke-Test 'Auto HDR behavior merge preserves unrelated settings' {
        $cases = @(
            @{ Existing = $null; Enable = $true; TenBit = $false; Expected = 'BufferUpgradeOverride=1' },
            @{ Existing = ''; Enable = $true; TenBit = $true; Expected = 'BufferUpgradeOverride=1;BufferUpgradeEnable10Bit=1' },
            @{ Existing = 'Keep=7;BufferUpgradeOverride=0'; Enable = $true; TenBit = $false; Expected = 'Keep=7;BufferUpgradeOverride=1' },
            @{ Existing = 'Keep=7;BufferUpgradeOverride=1;BufferUpgradeEnable10Bit=1'; Enable = $false; TenBit = $false; Expected = 'Keep=7' },
            @{ Existing = 'bufferupgradeoverride=0;KEEP=1;bufferupgradeenable10bit=1'; Enable = $true; TenBit = $true; Expected = 'KEEP=1;BufferUpgradeOverride=1;BufferUpgradeEnable10Bit=1' }
        )

        foreach ($case in $cases) {
            $actual = Invoke-StaticMethod $profileType 'MergeBehaviors' @(
                $case.Existing,
                $case.Enable,
                $case.TenBit
            )
            Assert-Equal $case.Expected $actual "Unexpected merge result for '$($case.Existing)'."
        }
    }
}
finally {
    Pop-Location
}

Write-Host "All $testsPassed automated tests passed."

