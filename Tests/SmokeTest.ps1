<#
.SYNOPSIS
    End-to-end test of DevLauncher : starts the published executable with a prepared environment
    and drives its interface through UI Automation.
.PARAMETER ExecutablePath
    Path of the published DevLauncher.exe.
.PARAMETER ScreenshotDirectory
    Folder receiving the screenshots taken during the test.
.PARAMETER ExpectClaudeDesktopDetected
    Requires the settings window to report Claude Desktop as detected.
#>
param(
    [Parameter(Mandatory = $true)] [string] $ExecutablePath,
    [Parameter(Mandatory = $true)] [string] $ScreenshotDirectory,
    [switch] $ExpectClaudeDesktopDetected
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

$script:FailedCheckCount = 0

function Write-CheckResult {
    param([string] $CheckName, [bool] $IsSuccessful, [string] $Details = '')
    if ($IsSuccessful) {
        Write-Host "PASS : $CheckName $Details"
    }
    else {
        Write-Host "FAIL : $CheckName $Details"
        $script:FailedCheckCount++
    }
}

function Find-AutomationElement {
    param($RootElement, [string] $NamePart, $ControlType = $null, [int] $TimeoutSeconds = 15)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $descendants = $RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($descendant in $descendants) {
            $isMatchingName = $descendant.Current.Name -like "*$NamePart*"
            $isMatchingType = ($null -eq $ControlType) -or ($descendant.Current.ControlType -eq $ControlType)
            if ($isMatchingName -and $isMatchingType) { return $descendant }
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Find-TopLevelWindow {
    param([string] $NamePart, [int] $TimeoutSeconds = 15)
    $rootElement = [System.Windows.Automation.AutomationElement]::RootElement
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $topLevelWindows = $rootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($topLevelWindow in $topLevelWindows) {
            if ($topLevelWindow.Current.Name -like "*$NamePart*") { return $topLevelWindow }
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Save-WindowScreenshot {
    param($WindowElement, [string] $FileName)
    try {
        $windowBounds = $WindowElement.Current.BoundingRectangle
        $screenshot = New-Object System.Drawing.Bitmap ([int]$windowBounds.Width), ([int]$windowBounds.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($screenshot)
        $graphics.CopyFromScreen([int]$windowBounds.X, [int]$windowBounds.Y, 0, 0, $screenshot.Size)
        $screenshot.Save((Join-Path $ScreenshotDirectory $FileName), [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $screenshot.Dispose()
    }
    catch {
        Write-Host "Screenshot $FileName impossible : $($_.Exception.Message)"
    }
}

function Get-ComboBoxSelectionName {
    param($ComboBoxElement)
    $selectionPattern = $ComboBoxElement.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    $selectedItems = $selectionPattern.Current.GetSelection()
    if ($selectedItems.Count -eq 0) { return '' }
    return $selectedItems[0].Current.Name
}

# ── Prepared environment : a Symfony project with its Claude project link, a Laravel project, an excluded XAMPP folder ──
New-Item -ItemType Directory -Force -Path $ScreenshotDirectory | Out-Null
$testRootPath = Join-Path $env:RUNNER_TEMP 'DevLauncherSmoke'
$projectsRootPath = Join-Path $testRootPath 'htdocs'
$projectPath = Join-Path $projectsRootPath 'highlightforge'
$laravelProjectPath = Join-Path $projectsRootPath 'boutique'
New-Item -ItemType Directory -Force -Path $projectPath, $laravelProjectPath, (Join-Path $projectsRootPath 'dashboard') | Out-Null
Set-Content -Path (Join-Path $laravelProjectPath 'artisan') -Value ''
Set-Content -Path (Join-Path $laravelProjectPath 'package.json') -Value '{"scripts":{"dev":"vite","build":"vite build"}}' 
Set-Content -Path (Join-Path $projectPath 'symfony.lock') -Value '{}'
Set-Content -Path (Join-Path $projectPath 'composer.json') -Value '{"require":{"symfony/framework-bundle":"7.*","symfonycasts/tailwind-bundle":"*"}}'

$dataDirectory = Join-Path $env:APPDATA 'DevLauncher'
New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null
$settings = @{
    projectRoots = @($projectsRootPath)
    assistants = @(
        @{ id = 'claude'; isEnabled = $true; defaultMode = 'browser'; webUrl = 'https://claude.ai/new'; applicationTarget = '';
           projects = @(@{ name = 'highlightforge'; url = 'https://claude.ai/project/smoke-test' }) }
    )
}
$settings | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $dataDirectory 'settings.json') -Encoding UTF8

# ── Start ──
$devLauncherProcess = Start-Process -FilePath $ExecutablePath -PassThru
$mainWindow = Find-TopLevelWindow -NamePart 'Dev Launcher' -TimeoutSeconds 40
Write-CheckResult 'Fenêtre principale ouverte' ($null -ne $mainWindow)
if ($null -eq $mainWindow) {
    Write-Host "DevLauncher a quitté : $($devLauncherProcess.HasExited)"
    exit 1
}
Save-WindowScreenshot $mainWindow '1-demarrage.png'

# ── Project selection ──
$projectItem = Find-AutomationElement $mainWindow 'highlightforge' ([System.Windows.Automation.ControlType]::ListItem)
Write-CheckResult 'Projet listé' ($null -ne $projectItem)
$excludedItem = Find-AutomationElement $mainWindow 'dashboard' ([System.Windows.Automation.ControlType]::ListItem) 2
Write-CheckResult 'Dossier XAMPP ignoré' ($null -eq $excludedItem)
if ($null -ne $projectItem) {
    $projectItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
$symfonyBadge = Find-AutomationElement $mainWindow 'Symfony détecté'
Write-CheckResult 'Symfony détecté' ($null -ne $symfonyBadge)
$assistantsCard = Find-AutomationElement $mainWindow 'Assistants IA'
Write-CheckResult 'Carte Assistants IA visible' ($null -ne $assistantsCard)

# ── Claude : the project named like the folder is proposed ──
$claudeCheckBox = Find-AutomationElement $mainWindow 'Claude' ([System.Windows.Automation.ControlType]::CheckBox)
Write-CheckResult 'Case Claude' ($null -ne $claudeCheckBox)
if ($null -ne $claudeCheckBox) {
    $claudeCheckBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 500
}
$comboBoxCondition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ComboBox)
$comboBoxSelections = @($mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboBoxCondition) | ForEach-Object { Get-ComboBoxSelectionName $_ })
Write-Host "Listes déroulantes : $($comboBoxSelections -join ' | ')"
Write-CheckResult 'Projet Claude présélectionné' (@($comboBoxSelections | Where-Object { $_ -like '*highlightforge*' }).Count -gt 0)

# ── Laravel project : detected, with its development server and its npm scripts ──
$laravelItem = Find-AutomationElement $mainWindow 'boutique' ([System.Windows.Automation.ControlType]::ListItem)
Write-CheckResult 'Projet Laravel listé' ($null -ne $laravelItem)
if ($null -ne $laravelItem) {
    $laravelItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
Write-CheckResult 'Laravel détecté' ($null -ne (Find-AutomationElement $mainWindow 'Laravel détecté'))
$laravelServerCheckBox = Find-AutomationElement $mainWindow 'Laravel Serve' ([System.Windows.Automation.ControlType]::CheckBox)
$isLaravelServerChecked = ($null -ne $laravelServerCheckBox) -and ($laravelServerCheckBox.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On')
Write-CheckResult 'Laravel Serve coché par défaut' $isLaravelServerChecked
$laravelComboBoxSelections = @($mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboBoxCondition) | ForEach-Object { Get-ComboBoxSelectionName $_ })
Write-Host "Listes déroulantes Laravel : $($laravelComboBoxSelections -join ' | ')"
Write-CheckResult 'Script npm run dev proposé' (@($laravelComboBoxSelections | Where-Object { $_ -eq 'npm run dev' }).Count -gt 0)
Save-WindowScreenshot $mainWindow '3-assistants.png'

# ── Settings window : Claude Desktop detection ──
$settingsButton = Find-AutomationElement $mainWindow 'Paramètres' ([System.Windows.Automation.ControlType]::Button) 5
if ($null -eq $settingsButton) {
    $settingsButton = Find-AutomationElement $mainWindow '⚙' ([System.Windows.Automation.ControlType]::Button) 5
}
Write-CheckResult 'Bouton Paramètres' ($null -ne $settingsButton)
if ($null -ne $settingsButton) {
    $settingsButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $settingsWindow = Find-AutomationElement $mainWindow 'Configure les chemins' $null 10
    $settingsRoot = if ($null -ne $settingsWindow) { $mainWindow } else { Find-TopLevelWindow 'Paramètres' 10 }
    $detectionHint = Find-AutomationElement $settingsRoot 'tect' ([System.Windows.Automation.ControlType]::Text) 10
    $claudeHint = @($settingsRoot.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -like '*Détectée automatiquement*' -or $_ -like '*non détectée*' })
    Write-Host "Détection des applications : $($claudeHint -join ' | ')"
    Write-CheckResult 'Fenêtre des paramètres ouverte' ($null -ne $detectionHint)
    if ($ExpectClaudeDesktopDetected) {
        Write-CheckResult 'Claude Desktop détecté' ((@($claudeHint)[0]) -like '*Détectée automatiquement*') (@($claudeHint)[0])
    }
    Save-WindowScreenshot $settingsRoot '4-parametres.png'
    $cancelButton = Find-AutomationElement $settingsRoot 'Annuler' ([System.Windows.Automation.ControlType]::Button) 5
    if ($null -ne $cancelButton) { $cancelButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Seconds 1
}

# ── Closing ──
Write-CheckResult 'DevLauncher toujours actif' (-not $devLauncherProcess.HasExited)
$mainWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
$hasExited = $devLauncherProcess.WaitForExit(15000)
Write-CheckResult 'Fermeture propre' $hasExited "(code $(if ($hasExited) { $devLauncherProcess.ExitCode } else { 'en cours' }))"
if (-not $hasExited) { $devLauncherProcess.Kill() }

Write-Host "Contrôles en échec : $script:FailedCheckCount"
exit $script:FailedCheckCount
