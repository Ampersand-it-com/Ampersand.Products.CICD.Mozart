# Mozart-style Web Deploy for all Ampersand sites with *.pubxml profiles.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

function Get-PublishPassword([string]$pubxmlPath) {
    $userFile = "$pubxmlPath.user"
    if (-not (Test-Path $userFile)) { return $null }
    [xml]$doc = Get-Content -LiteralPath $userFile
    $encrypted = $doc.Project.PropertyGroup.EncryptedPassword
    if ([string]::IsNullOrWhiteSpace($encrypted)) { return $null }
    $blob = [Convert]::FromBase64String($encrypted)
    $entropies = @(
        [byte[]]@(),
        [Text.Encoding]::Unicode.GetBytes('Publish'),
        [Text.Encoding]::UTF8.GetBytes('Publish')
    )
    foreach ($entropy in $entropies) {
        try {
            $bytes = [Security.Cryptography.ProtectedData]::Unprotect(
                $blob,
                $(if ($entropy.Length -eq 0) { $null } else { $entropy }),
                [Security.Cryptography.DataProtectionScope]::CurrentUser)
            $text = [Text.Encoding]::Unicode.GetString($bytes).TrimEnd([char]0)
            if (-not [string]::IsNullOrWhiteSpace($text)) { return $text }
            $text = [Text.Encoding]::UTF8.GetString($bytes).TrimEnd([char]0)
            if (-not [string]::IsNullOrWhiteSpace($text)) { return $text }
        } catch { }
    }
    return $null
}

function Get-PubXmlUserName([string]$pubxmlPath) {
    [xml]$doc = Get-Content -LiteralPath $pubxmlPath
    return $doc.Project.PropertyGroup.UserName
}

function Invoke-MozartPublish {
    param(
        [string]$Name,
        [string]$Csproj,
        [string]$Profile,
        [string]$WorkDir
    )
    $pubxml = Join-Path (Split-Path $Csproj -Parent) "Properties\PublishProfiles\$Profile.pubxml"
    if (-not (Test-Path -LiteralPath $pubxml)) {
        Write-Host "SKIP $Name - profile not found: $pubxml" -ForegroundColor Yellow
        return $false
    }
    if (-not (Test-Path -LiteralPath $Csproj)) {
        Write-Host "SKIP $Name - csproj not found: $Csproj" -ForegroundColor Yellow
        return $false
    }

    $userName = Get-PubXmlUserName $pubxml
    $password = Get-PublishPassword $pubxml
    if ([string]::IsNullOrWhiteSpace($password)) {
        $settingsDir = Split-Path $Csproj -Parent
        $settings = Get-ChildItem -LiteralPath $settingsDir -Filter '*.publishsettings' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($settings) {
            [xml]$ps = Get-Content -LiteralPath $settings.FullName
            $profileNode = $ps.publishData.publishProfile | Where-Object { $_.profileName -eq $Profile } | Select-Object -First 1
            if ($profileNode -and $profileNode.userPWD) { $password = $profileNode.userPWD }
        }
    }
    if ([string]::IsNullOrWhiteSpace($password)) {
        Write-Host "FAIL $Name - cannot decrypt deploy password ($pubxml.user)" -ForegroundColor Red
        return $false
    }

    Write-Host "`n========== DEPLOY $Name ==========" -ForegroundColor Cyan
    Write-Host "Profile: $Profile"
    Write-Host "Project: $Csproj"

    $dotnetArgs = @(
        'publish', $Csproj,
        '-c', 'Release',
        '-v', 'm',
        '--nologo',
        "-p:PublishProfile=$Profile",
        "-p:PublishProfileFullPath=$pubxml",
        '-p:WebPublishMethod=MSDeploy',
        '-p:AllowUntrustedCertificate=true',
        "-p:UserName=$userName",
        "-p:Password=$password"
    )

    Push-Location $WorkDir
    try {
        & dotnet @dotnetArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAIL $Name - exit code $LASTEXITCODE" -ForegroundColor Red
            return $false
        }
        Write-Host "OK $Name" -ForegroundColor Green
        return $true
    }
    finally {
        Pop-Location
    }
}

$root = 'C:\Users\PerviroKim\Documents\MEGA\Synced'
$oneRing = Join-Path $root 'Ampersand.OneRing'

# Order: Storage → Identity → OAuth clients
$deploys = @(
    @{ Name = 'Storage'; Csproj = Join-Path $oneRing 'Ampersand.Storage\Ampersand.Storage.csproj'; Profile = 'storage.ampersand-it.com - Web Deploy'; WorkDir = $oneRing },
    @{ Name = 'Identity'; Csproj = Join-Path $oneRing 'Ampersand.Identity.WEB\Ampersand.Identity.WEB.csproj'; Profile = 'auth.ampersand-it.com - Web Deploy'; WorkDir = $oneRing },
    @{ Name = 'FamilyJourney'; Csproj = Join-Path $oneRing 'Ampersand.FamilyJourney\Ampersand.FamilyJourney.csproj'; Profile = 'familyjourney.ampersand-it.com - Web Deploy'; WorkDir = $oneRing },
    @{ Name = 'DiscordBot'; Csproj = Join-Path $oneRing 'Ampersand.DiscordBot\Ampersand.DiscordBot.csproj'; Profile = 'discord.ampersand-it.com - Web Deploy'; WorkDir = $oneRing },
    @{ Name = 'AI'; Csproj = Join-Path $oneRing 'Ampersand.AI.WEB\Ampersand.AI.WEB.csproj'; Profile = 'ai.ampersand-it.com - Web Deploy'; WorkDir = $oneRing },
    @{ Name = 'BotManager'; Csproj = Join-Path $root 'Ampersand.Products.BotManager\Ampersand.BotManager.csproj'; Profile = 'tw.ampersand-it.com - Web Deploy'; WorkDir = Join-Path $root 'Ampersand.Products.BotManager' },
    @{ Name = 'EmailService'; Csproj = Join-Path $root 'Ampersand.Products.EmailService\OneRing.Ampersand.EmailService.csproj'; Profile = 'email.ampersand-it.com - Web Deploy'; WorkDir = Join-Path $root 'Ampersand.Products.EmailService' },
    @{ Name = 'Pegasus'; Csproj = Join-Path $root 'Ampersand.Products.Planner.Pegasus\src\Ampersand.Pegasus.WEB\Ampersand.Pegasus.WEB.csproj'; Profile = 'pegasus.ampersand-it.com - Web Deploy'; WorkDir = Join-Path $root 'Ampersand.Products.Planner.Pegasus' },
    @{ Name = 'TerraRPG'; Csproj = Join-Path $oneRing 'Ampersand.TerraRPG.WEB\Ampersand.TerraRPG.csproj'; Profile = 'terra.ampersand-it.com - Web Deploy'; WorkDir = $oneRing },
    @{ Name = 'DocumentationWizard'; Csproj = Join-Path $root 'Ampersand.Products.Documentation.Wizard\Ampersand.Products.Documentation.Wizard.csproj'; Profile = 'documentation.ampersand-it.com - Web Deploy'; WorkDir = Join-Path $root 'Ampersand.Products.Documentation.Wizard' },
    @{ Name = 'AmpersandIT'; Csproj = Join-Path $root 'Ampersand.Products.IT\Ampersand.IT.Mvc.csproj'; Profile = 'ampersand-it.com - Web Deploy'; WorkDir = Join-Path $root 'Ampersand.Products.IT' }
)

$ok = 0
$fail = 0
$skipped = 0
foreach ($d in $deploys) {
    $result = Invoke-MozartPublish @d
    if ($result) { $ok++ } elseif ($result -eq $false -and $?) { $fail++ } else { $skipped++ }
}

Write-Host "`n========== SUMMARY ==========" -ForegroundColor Cyan
Write-Host "Succeeded: $ok"
Write-Host "Failed: $fail"
if ($fail -gt 0) { exit 1 }
