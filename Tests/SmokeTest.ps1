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

function Find-DialogWindow {
    param([string] $NamePart, [int] $TimeoutSeconds = 10)
    # An owned dialog is a child of the main window in the automation tree, or a top-level window : both are looked at.
    $windowCondition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Window)
    $rootElement = [System.Windows.Automation.AutomationElement]::RootElement
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $candidateWindows = @($rootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCondition))
        try { $candidateWindows += @($mainWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $windowCondition)) }
        catch { Write-Host "Fenêtre principale indisponible : $($_.Exception.Message)" }
        foreach ($candidateWindow in $candidateWindows) {
            if ($candidateWindow.Current.Name -like "*$NamePart*") { return $candidateWindow }
        }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Close-DialogWindow {
    param([string] $NamePart)
    $dialogWindow = Find-DialogWindow -NamePart $NamePart -TimeoutSeconds 1
    if ($null -ne $dialogWindow) { $dialogWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
}

function Invoke-DialogButton {
    param([string] $DialogNamePart, [string] $ButtonAutomationId, [int] $TimeoutSeconds = 10)
    $dialogWindow = Find-DialogWindow -NamePart $DialogNamePart -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $dialogWindow) { return $false }
    $buttonCondition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), $ButtonAutomationId
    $dialogButton = $dialogWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
    if ($null -eq $dialogButton) { return $false }
    $dialogButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    return $true
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
$toolProjectPath = Join-Path $projectsRootPath 'outil'
New-Item -ItemType Directory -Force -Path $projectPath, $laravelProjectPath, $toolProjectPath, (Join-Path $projectsRootPath 'dashboard') | Out-Null
Set-Content -Path (Join-Path $laravelProjectPath 'artisan') -Value ''
Set-Content -Path (Join-Path $laravelProjectPath 'package.json') -Value '{"scripts":{"dev":"vite","build":"vite build"}}' 
Set-Content -Path (Join-Path $projectPath 'symfony.lock') -Value '{}'
Set-Content -Path (Join-Path $projectPath 'composer.json') -Value '{"require":{"symfony/framework-bundle":"7.*","symfonycasts/tailwind-bundle":"*"}}'

$dataDirectory = Join-Path $env:APPDATA 'DevLauncher'
New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null
$settings = @{
    projectRoots = @($projectsRootPath)
    mcpServerEnabled = $true
    theme = 'light'
    mcpServerPort = 8765
    assistants = @(
        @{ id = 'claude'; isEnabled = $true; defaultMode = 'browser'; webUrl = 'https://claude.ai/new'; applicationTarget = '';
           projects = @(@{ name = 'highlightforge'; url = 'https://claude.ai/project/smoke-test' }) }
    )
}
$settings | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $dataDirectory 'settings.json') -Encoding UTF8
# Profile of the « outil » project : only a pre-launch command writing a file, so that a whole launch can run on the runner.
New-Item -ItemType Directory -Force -Path (Join-Path $dataDirectory 'Profiles') | Out-Null
$toolProfiles = @(@{ name = 'Défaut'; projectType = 'Other'; tools = @{ 'pre-launch' = @{ isEnabled = $true; options = @{ custom = @('echo devlauncher-smoke> smoke.txt') } } } })
ConvertTo-Json -InputObject $toolProfiles -Depth 8 | Set-Content -Path (Join-Path $dataDirectory 'Profiles\outil.json') -Encoding UTF8

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

# ── Favorites and quick actions ──
$favoriteButton = Find-AutomationElement $mainWindow 'Favori' ([System.Windows.Automation.ControlType]::Button) 5
Write-CheckResult 'Bouton favori' ($null -ne $favoriteButton)
if ($null -ne $favoriteButton) {
    $favoriteButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $favoritesList = Find-AutomationElement $mainWindow 'Projets favoris' ([System.Windows.Automation.ControlType]::List) 5
    $favoriteItem = if ($null -ne $favoritesList) { Find-AutomationElement $favoritesList 'highlightforge' ([System.Windows.Automation.ControlType]::ListItem) 5 } else { $null }
    Write-CheckResult 'Projet épinglé dans les favoris' ($null -ne $favoriteItem)
    $favoritesFilePath = Join-Path $dataDirectory 'favorite-projects.json'
    Write-CheckResult 'Favoris enregistrés' ((Test-Path $favoritesFilePath) -and ((Get-Content -Path $favoritesFilePath -Raw) -like '*highlightforge*'))
}
Write-CheckResult 'Bouton contexte IA' ($null -ne (Find-AutomationElement $mainWindow 'Contexte IA' ([System.Windows.Automation.ControlType]::Button) 5))

# ── Profiles shared with the project ──
Write-CheckResult 'Bouton de partage des profils' ($null -ne (Find-AutomationElement $mainWindow 'Partage des profils' ([System.Windows.Automation.ControlType]::Button) 5))

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

# ── Whole launch : pre-launch command, progress, statistics ──
# The projects list is virtualized : the search narrows it so that the project is displayed.
$projectSearchBox = Find-AutomationElement $mainWindow 'Rechercher un projet' ([System.Windows.Automation.ControlType]::Edit) 5
if ($null -ne $projectSearchBox) { $projectSearchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('outil') }
$toolItem = Find-AutomationElement $mainWindow 'outil' ([System.Windows.Automation.ControlType]::ListItem)
Write-CheckResult 'Projet outil listé' ($null -ne $toolItem)
if ($null -ne $toolItem) {
    $toolItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Write-CheckResult 'Commande personnalisée affichée' ($null -ne (Find-AutomationElement $mainWindow 'Commande personnalisée' ([System.Windows.Automation.ControlType]::Edit) 5))
    Write-CheckResult 'Outil base de données proposé' ($null -ne (Find-AutomationElement $mainWindow 'Base de données du projet' ([System.Windows.Automation.ControlType]::CheckBox) 5))
    $launchButton = Find-AutomationElement $mainWindow "Lancer l'environnement" ([System.Windows.Automation.ControlType]::Button) 5
    Write-CheckResult 'Bouton de lancement' ($null -ne $launchButton)
    if ($null -ne $launchButton) {
        $launchButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $smokeFilePath = Join-Path $toolProjectPath 'smoke.txt'
        $deadline = (Get-Date).AddSeconds(30)
        while (-not (Test-Path $smokeFilePath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Write-CheckResult 'Commande avant lancement exécutée' ((Test-Path $smokeFilePath) -and ((Get-Content -Path $smokeFilePath -Raw) -like '*devlauncher-smoke*'))
        $statisticsFilePath = Join-Path $dataDirectory 'launch-statistics.json'
        $deadline = (Get-Date).AddSeconds(15)
        while (-not (Test-Path $statisticsFilePath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        Write-CheckResult 'Lancement compté dans les statistiques' ((Test-Path $statisticsFilePath) -and ((Get-Content -Path $statisticsFilePath -Raw) -like '*outil*'))
        Save-WindowScreenshot $mainWindow '5-lancement.png'
    }
}
if ($null -ne $projectSearchBox) { $projectSearchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('') }
$statisticsButton = Find-AutomationElement $mainWindow 'Statistiques' ([System.Windows.Automation.ControlType]::Button) 5
Write-CheckResult 'Bouton statistiques' ($null -ne $statisticsButton)
if ($null -ne $statisticsButton) {
    $statisticsButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $statisticsRoot = Find-DialogWindow 'Statistiques' 10
    if ($null -eq $statisticsRoot) { $statisticsRoot = $mainWindow }
    $statisticsTitle = Find-AutomationElement $statisticsRoot 'Statistiques de lancement' $null 10
    $statisticsRow = Find-AutomationElement $statisticsRoot 'lancement(s) sur' $null 5
    Write-CheckResult 'Fenêtre des statistiques' (($null -ne $statisticsTitle) -and ($null -ne $statisticsRow))
    Save-WindowScreenshot $statisticsRoot '6-statistiques.png'
    $closeButton = Find-AutomationElement $statisticsRoot 'Fermer' ([System.Windows.Automation.ControlType]::Button) 5
    if ($null -ne $closeButton) { $closeButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Seconds 1
    # A statistics window still open would keep the main window disabled for the next steps.
    Close-DialogWindow 'Statistiques'
}

# ── MCP server : an AI lists the projects and the services over HTTP ──
$mcpEndpoint = 'http://127.0.0.1:8765/mcp'
try {
    $initializeResponse = Invoke-RestMethod -Method Post -Uri $mcpEndpoint -ContentType 'application/json' -Body '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke-test","version":"1"}}}'
    Write-CheckResult 'Serveur MCP initialisé' ($initializeResponse.result.serverInfo.name -eq 'devlauncher')
    $projectsResponse = Invoke-RestMethod -Method Post -Uri $mcpEndpoint -ContentType 'application/json' -Body '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_projects","arguments":{}}}'
    Write-CheckResult 'Projets listés par MCP' ($projectsResponse.result.content[0].text -like '*highlightforge*')
    $logsResponse = Invoke-RestMethod -Method Post -Uri $mcpEndpoint -ContentType 'application/json' -Body '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"read_service_logs","arguments":{"service":"lancement","lines":50}}}'
    Write-CheckResult 'Journal de lancement lu par MCP' ($logsResponse.result.content[0].text -like '*outil*')
}
catch {
    Write-CheckResult 'Serveur MCP joignable' $false $_.Exception.Message
}

# ── Single instance : a second start hands its request over to the open window ──
$secondProcess = Start-Process -FilePath $ExecutablePath -ArgumentList '--open', 'boutique' -PassThru
Write-CheckResult 'Seconde instance transmise puis fermée' ($secondProcess.WaitForExit(30000))
Write-CheckResult 'Projet demandé ouvert dans la fenêtre existante' ($null -ne (Find-AutomationElement $mainWindow 'Laravel détecté' $null 10))

# ── Command palette ──
$paletteButton = Find-AutomationElement $mainWindow 'Palette de commandes' ([System.Windows.Automation.ControlType]::Button) 5
Write-CheckResult 'Bouton palette de commandes' ($null -ne $paletteButton)
if ($null -ne $paletteButton) {
    $paletteButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $paletteWindow = Find-DialogWindow 'Palette de commandes' 10
    if ($null -eq $paletteWindow) { $paletteWindow = $mainWindow }
    $paletteSearchBox = Find-AutomationElement $paletteWindow 'Rechercher une commande' ([System.Windows.Automation.ControlType]::Edit) 10
    Write-CheckResult 'Palette de commandes ouverte' ($null -ne $paletteSearchBox)
    if ($null -ne $paletteSearchBox) {
        $paletteSearchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('lancer boutique')
        Write-CheckResult 'Palette filtrée' ($null -ne (Find-AutomationElement $paletteWindow 'Lancer boutique' ([System.Windows.Automation.ControlType]::ListItem) 5))
        Save-WindowScreenshot $paletteWindow '8-palette.png'
    }
    Close-DialogWindow 'Palette de commandes'
    Start-Sleep -Seconds 1
}

# ── Log search ──
$logSearchBox = Find-AutomationElement $mainWindow 'Rechercher dans le journal' ([System.Windows.Automation.ControlType]::Edit) 5
Write-CheckResult 'Recherche dans le journal' ($null -ne $logSearchBox)
if ($null -ne $logSearchBox) {
    $logSearchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Lancement')
    Write-CheckResult 'Résumé du filtre du journal' ($null -ne (Find-AutomationElement $mainWindow 'ligne(s)' ([System.Windows.Automation.ControlType]::Text) 5))
    Save-WindowScreenshot $mainWindow '7-recherche-journal.png'
    $logSearchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
}

# ── Settings window : Claude Desktop detection ──
$settingsButton = Find-AutomationElement $mainWindow 'Paramètres' ([System.Windows.Automation.ControlType]::Button) 5
if ($null -eq $settingsButton) {
    $settingsButton = Find-AutomationElement $mainWindow '⚙' ([System.Windows.Automation.ControlType]::Button) 5
}
Write-CheckResult 'Bouton Paramètres' ($null -ne $settingsButton)
if ($null -ne $settingsButton) {
    # The click is retried once : a dialog closed just before can still hold the focus.
    $settingsRoot = $null
    for ($attempt = 1; $attempt -le 2 -and $null -eq $settingsRoot; $attempt++) {
        $mainWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
        $settingsButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $settingsRoot = Find-DialogWindow 'Paramètres' 10
    }
}
if ($null -eq $settingsRoot) {
    Write-CheckResult 'Fenêtre des paramètres ouverte' $false
    Save-WindowScreenshot $mainWindow '4-parametres-introuvables.png'
}
else {
    $detectionHint = Find-AutomationElement $settingsRoot 'tect' ([System.Windows.Automation.ControlType]::Text) 10
    $claudeHint = @($settingsRoot.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -like '*Détectée automatiquement*' -or $_ -like '*non détectée*' })
    Write-Host "Détection des applications : $($claudeHint -join ' | ')"
    Write-CheckResult 'Fenêtre des paramètres ouverte' ($null -ne $detectionHint)
    if ($ExpectClaudeDesktopDetected) {
        Write-CheckResult 'Claude Desktop détecté' ((@($claudeHint)[0]) -like '*Détectée automatiquement*') (@($claudeHint)[0])
    }
    Write-CheckResult 'Bouton de détection automatique' ($null -ne (Find-AutomationElement $settingsRoot 'Détecter automatiquement' ([System.Windows.Automation.ControlType]::Button) 5))
    Write-CheckResult 'Section import / export' ($null -ne (Find-AutomationElement $settingsRoot 'Exporter' ([System.Windows.Automation.ControlType]::Button) 5))
    Save-WindowScreenshot $settingsRoot '4-parametres.png'
    # Light theme : set before the start, shown in the settings and kept when they are saved.
    $themeComboBox = Find-AutomationElement $settingsRoot 'Thème' ([System.Windows.Automation.ControlType]::ComboBox) 5
    $isThemeSaved = $false
    if ($null -ne $themeComboBox) {
        $selectedThemeName = Get-ComboBoxSelectionName $themeComboBox
        Write-Host "Thème affiché : $selectedThemeName"
        $saveButton = Find-AutomationElement $settingsRoot 'Sauvegarder' ([System.Windows.Automation.ControlType]::Button) 5
        if ($selectedThemeName -like '*Clair*' -and $null -ne $saveButton) {
            $saveButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Seconds 2
            $isThemeSaved = (Get-Content -Path (Join-Path $dataDirectory 'settings.json') -Raw) -like '*"theme": "light"*'
        }
    }
    Write-CheckResult 'Thème clair affiché et conservé' $isThemeSaved
    Save-WindowScreenshot $mainWindow '9-theme-clair.png'
    $remainingSettingsWindow = Find-DialogWindow 'Paramètres' 1
    if ($null -ne $remainingSettingsWindow) {
        $cancelButton = Find-AutomationElement $remainingSettingsWindow 'Annuler' ([System.Windows.Automation.ControlType]::Button) 5
        if ($null -ne $cancelButton) { $cancelButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    }
    Start-Sleep -Seconds 1
}

# ── Persistent log ──
$todayLogPath = Join-Path $dataDirectory ("Logs\devlauncher-{0}.log" -f (Get-Date -Format 'yyyy-MM-dd'))
$isLogWritten = (Test-Path $todayLogPath) -and ((Get-Content -Path $todayLogPath -Raw -Encoding UTF8) -like '*DevLauncher*')
Write-CheckResult 'Journal persistant écrit' $isLogWritten $todayLogPath

# ── Closing ──
Write-CheckResult 'DevLauncher toujours actif' (-not $devLauncherProcess.HasExited)
$mainWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
# The launch above left an environment : the exit question is answered « Yes » (button 6 of a message box).
$isExitQuestionAnswered = Invoke-DialogButton -DialogNamePart 'Quitter DevLauncher' -ButtonAutomationId '6'
Write-CheckResult "Question de fermeture de l'environnement" $isExitQuestionAnswered
$hasExited = $devLauncherProcess.WaitForExit(15000)
Write-CheckResult 'Fermeture propre' $hasExited "(code $(if ($hasExited) { $devLauncherProcess.ExitCode } else { 'en cours' }))"
if (-not $hasExited) { $devLauncherProcess.Kill() }

Write-Host "Contrôles en échec : $script:FailedCheckCount"
exit $script:FailedCheckCount
