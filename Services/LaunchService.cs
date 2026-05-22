using System.Diagnostics;
using System.IO;

namespace DevLauncher.Services;

public class LaunchService
{
    private readonly string _xamppDir;
    private readonly string _mercureDir;

    public event Action<string>? LogMessage;
    public event Action<string>? LogError;

    // ── État : services lancés ─────────────────────────────
    private bool _apacheStarted;
    private bool _mysqlStarted;
    private bool _filezillaStarted;
    private bool _xamppPanelStarted;
    private bool _symfonyStarted;
    private bool _tailwindStarted;
    private bool _mercureStarted;
    private bool _vscodeStarted;
    private bool _visualStudioStarted;

    public LaunchService(string xamppDir, string mercureDir)
    {
        _xamppDir = xamppDir;
        _mercureDir = mercureDir;
    }

    // ════════════════════════════════════════════════════════
    //  LANCEMENT
    // ════════════════════════════════════════════════════════

    public async Task LaunchAsync(string projectPath, LaunchOptions opts)
    {
        // 1. Éditeur
        if (opts.OpenVSCode)
        {
            Log("💻 Ouverture de VSCode…");
            StartVSCode(projectPath);
            _vscodeStarted = true;
        }

        if (opts.OpenVisualStudio)
        {
            Log("🟣 Ouverture de Visual Studio…");
            var slnFiles = Directory.GetFiles(projectPath, "*.sln");
            var target = slnFiles.Length > 0 ? slnFiles[0] : projectPath;
            Log(slnFiles.Length > 0
                ? $"   → Solution : {Path.GetFileName(slnFiles[0])}"
                : "   → Pas de .sln, ouverture du dossier…");
            StartSilent(AppSettings.VisualStudioExecutable, $"\"{target}\"");
            _visualStudioStarted = true;
        }

        // 2. Panneau XAMPP
        if (opts.ShowXamppPanel)
        {
            if (IsProcessRunning("xampp-control"))
                Log("🖥️ Panneau XAMPP déjà ouvert — ignoré");
            else
            {
                Log("🖥️ Ouverture du panneau XAMPP…");
                StartSilent(AppSettings.XamppPanel);
                _xamppPanelStarted = true;
            }
        }

        // 3. Services XAMPP
        if (opts.StartApache)
        {
            if (IsProcessRunning("httpd"))
                Log("🌐 Apache déjà en cours — ignoré");
            else
            {
                Log("🌐 Démarrage d'Apache…");
                StartBackground(AppSettings.ApacheExe);
                _apacheStarted = true;
            }
        }

        if (opts.StartMySQL)
        {
            if (IsProcessRunning("mysqld"))
                Log("🗃️ MySQL déjà en cours — ignoré");
            else
            {
                Log("🗃️ Démarrage de MySQL…");
                StartBackground(AppSettings.MySQLExe, $"--defaults-file=\"{AppSettings.MySQLConfig}\"");
                _mysqlStarted = true;
            }
        }

        if (opts.StartFileZilla)
        {
            if (IsProcessRunning("FileZillaServer"))
                Log("📂 FileZilla déjà en cours — ignoré");
            else
            {
                Log("📂 Démarrage de FileZilla FTP Server…");
                StartBackground(AppSettings.FileZillaExe, "-compat -start");
                _filezillaStarted = true;
            }
        }

        if (opts.StartApache || opts.StartMySQL || opts.StartFileZilla)
        {
            Log("⏳ Attente démarrage des services (2s)…");
            await Task.Delay(2000);
        }

        // 4. Terminal optionnel
        if (opts.OpenTerminal)
        {
            Log("🖥️ Ouverture d'un terminal…");
            var projectName = Path.GetFileName(projectPath);
            try
            {
                var psi = new ProcessStartInfo("cmd.exe")
                {
                    Arguments = $"/c wt -w 0 new-tab --title \"{projectName}\" -d \"{projectPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
                Log("   ✅ Terminal ouvert");
            }
            catch (Exception ex)
            {
                Log($"⚠️ Terminal : {ex.Message}");
            }
        }

        // 5. Services Symfony
        if (opts.IsSymfony)
        {
            Log("⚡ Lancement des services Symfony…");
            LaunchSymfonyServices(projectPath, opts);
            ScheduleTasksJsonCleanup(projectPath);
        }

        // 6. Navigateur
        if (opts.OpenBrowser)
            await OpenBrowserAsync(projectPath, opts);
    }

    // ════════════════════════════════════════════════════════
    //  ARRÊT
    // ════════════════════════════════════════════════════════

    public async Task StopAllAsync()
    {
        // Services Symfony + Windows Terminal
        if (_symfonyStarted || _tailwindStarted || _mercureStarted)
        {
            if (_symfonyStarted) { Log("⏹ Arrêt du serveur Symfony…"); KillProcess("symfony"); _symfonyStarted = false; }
            if (_tailwindStarted) { Log("⏹ Arrêt de Tailwind…"); KillProcess("node"); _tailwindStarted = false; }
            if (_mercureStarted) { Log("⏹ Arrêt de Mercure…"); KillProcess("mercure"); _mercureStarted = false; }

            Log("⏹ Fermeture de Windows Terminal…");
            KillProcess("WindowsTerminal");
        }

        // Attendre avant de fermer l'éditeur (tâches VSCode en cours)
        if (_vscodeStarted || _visualStudioStarted)
        {
            Log("⏳ Attente fermeture des services…");
            await Task.Delay(2000);
        }

        // Éditeurs
        if (_vscodeStarted) { Log("⏹ Fermeture de VSCode…"); KillProcess("Code"); _vscodeStarted = false; }
        if (_visualStudioStarted) { Log("⏹ Fermeture de Visual Studio…"); KillProcess("devenv"); _visualStudioStarted = false; }

        // Services XAMPP
        if (_apacheStarted) { Log("⏹ Arrêt d'Apache…"); KillProcess("httpd"); _apacheStarted = false; }
        if (_mysqlStarted) { Log("⏹ Arrêt de MySQL…"); KillProcess("mysqld"); _mysqlStarted = false; }
        if (_filezillaStarted) { Log("⏹ Arrêt de FileZilla…"); KillProcess("FileZillaServer"); _filezillaStarted = false; }
        if (_xamppPanelStarted) { Log("⏹ Fermeture du panneau XAMPP…"); KillProcess("xampp-control"); _xamppPanelStarted = false; }

        Log("✅ Tout est arrêté !");
    }

    // ════════════════════════════════════════════════════════
    //  SYMFONY — TERMINAUX
    // ════════════════════════════════════════════════════════

    private void LaunchSymfonyServices(string projectPath, LaunchOptions opts)
    {
        // VSCode sélectionné → tâches intégrées
        if (opts.OpenVSCode)
        {
            GenerateVSCodeTasks(projectPath, opts);
            Log("   → Les terminaux s'ouvriront automatiquement dans VSCode");
            Log("   → Si demandé, accepte 'Exécuter les tâches automatiques'");
            return;
        }

        // Sinon → Windows Terminal
        var args = new List<string> { "-w", "0" };
        bool firstTab = true;

        void AddTab(string title, string command)
        {
            if (!firstTab) args.Add(";");
            args.AddRange(new[] { "new-tab", "--title", title, "-d", projectPath, "powershell", "-NoExit", "-Command", command });
            firstTab = false;
            Log($"   → onglet {title}");
        }

        if (opts.StartSymfonyServer) { AddTab("Symfony Server", "symfony server:start"); _symfonyStarted = true; }
        if (opts.StartTailwind) { AddTab("Tailwind Watch", "symfony console tailwind:build --watch"); _tailwindStarted = true; }

        if (opts.StartMercure && !string.IsNullOrEmpty(opts.MercureScript))
        {
            if (!firstTab) args.Add(";");
            var scriptPath = Path.Combine(_mercureDir, opts.MercureScript!);
            args.AddRange(new[] { "new-tab", "--title", "Mercure", "-d", _mercureDir, "powershell", "-NoExit", "-File", $"\"{scriptPath}\"" });
            Log($"   → onglet Mercure ({opts.MercureScript})");
            _mercureStarted = true;
            firstTab = false;
        }

        if (!firstTab)
        {
            var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            Process.Start(psi);
        }
    }

    private void GenerateVSCodeTasks(string projectPath, LaunchOptions opts)
    {
        var tasks = new List<string>();

        if (opts.StartSymfonyServer)
        {
            tasks.Add("""
            {
                "label": "Symfony Server",
                "type": "shell",
                "command": "symfony server:start",
                "presentation": { "panel": "new", "reveal": "always" },
                "runOptions": { "runOn": "folderOpen" }
            }
            """);
            _symfonyStarted = true;
        }

        if (opts.StartTailwind)
        {
            tasks.Add("""
            {
                "label": "Tailwind Watch",
                "type": "shell",
                "command": "symfony console tailwind:build --watch",
                "presentation": { "panel": "new", "reveal": "always" },
                "runOptions": { "runOn": "folderOpen" }
            }
            """);
            _tailwindStarted = true;
        }

        if (opts.StartMercure && !string.IsNullOrEmpty(opts.MercureScript))
        {
            var scriptPath = Path.Combine(_mercureDir, opts.MercureScript!).Replace("\\", "\\\\");
            var mercureDir = _mercureDir.Replace("\\", "\\\\");
            tasks.Add($$$"""
            {
                "label": "Mercure",
                "type": "shell",
                "command": "powershell -NoExit -File \"{{{scriptPath}}}\"",
                "options": { "cwd": "{{{mercureDir}}}" },
                "presentation": { "panel": "new", "reveal": "always" },
                "runOptions": { "runOn": "folderOpen" }
            }
            """);
            _mercureStarted = true;
        }

        if (tasks.Count == 0) return;

        var vscodePath = Path.Combine(projectPath, ".vscode");
        Directory.CreateDirectory(vscodePath);

        var json = $$"""
        {
            "version": "2.0.0",
            "tasks": [
                {{string.Join(",\n        ", tasks)}}
            ]
        }
        """;

        File.WriteAllText(Path.Combine(vscodePath, "tasks.json"), json);
        Log("   → .vscode/tasks.json généré");
    }

    /// <summary>Supprime tasks.json dès que symfony est détecté comme actif.</summary>
    private void ScheduleTasksJsonCleanup(string projectPath)
    {
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);
                if (!IsProcessRunning("symfony")) continue;

                await Task.Delay(3000);
                try
                {
                    var tasksFile = Path.Combine(projectPath, ".vscode", "tasks.json");
                    var vscodeDir = Path.Combine(projectPath, ".vscode");

                    if (File.Exists(tasksFile)) { File.Delete(tasksFile); Log("🗑️ tasks.json supprimé"); }

                    if (Directory.Exists(vscodeDir)
                        && Directory.GetFiles(vscodeDir).Length == 0
                        && Directory.GetDirectories(vscodeDir).Length == 0)
                    {
                        Directory.Delete(vscodeDir);
                        Log("🗑️ Dossier .vscode supprimé");
                    }
                }
                catch (Exception ex) { Log($"⚠️ Suppression tasks.json : {ex.Message}"); }
                break;
            }
        });
    }

    // ════════════════════════════════════════════════════════
    //  NAVIGATEUR
    // ════════════════════════════════════════════════════════

    private async Task OpenBrowserAsync(string projectPath, LaunchOptions opts)
    {
        var url = opts.IsSymfony
            ? $"http://localhost:{AppSettings.SymfonyPort}"
            : $"http://localhost/{Path.GetFileName(projectPath)}";

        // Projet non-Symfony → ouverture directe
        if (!opts.IsSymfony)
        {
            Log($"🌍 Ouverture du navigateur : {url}");
            OpenInBrowsers(opts, url);
            return;
        }

        // Symfony → vérifier que les 4 services sont cochés
        var notSelected = new List<string>();
        if (!opts.StartSymfonyServer) notSelected.Add("Symfony Server");
        if (!opts.StartApache) notSelected.Add("Apache");
        if (!opts.StartMySQL) notSelected.Add("MySQL");
        if (!opts.StartFileZilla) notSelected.Add("FileZilla");

        if (notSelected.Count > 0)
        {
            foreach (var s in notSelected)
                LogErr($"❌ {s} n'a pas été sélectionné. La page du navigateur ne peut pas s'ouvrir.");
            return;
        }

        // Attendre que les 4 services soient actifs
        Log("⏳ Attente du démarrage des services…");
        var required = new Dictionary<string, string>
        {
            { "symfony",         "Symfony Server" },
            { "httpd",           "Apache"         },
            { "mysqld",          "MySQL"          },
            { "FileZillaServer", "FileZilla"      },
        };

        for (int elapsed = 1; elapsed <= 30; elapsed++)
        {
            await Task.Delay(1000);

            var notReady = required
                .Where(s => !IsProcessRunning(s.Key))
                .Select(s => s.Value)
                .ToList();

            if (notReady.Count == 0)
            {
                Log($"✅ Tous les services sont prêts ({elapsed}s)");
                Log($"🌍 Ouverture du navigateur : {url}");
                OpenInBrowsers(opts, url);
                return;
            }

            Log($"   [{elapsed}s] En attente : {string.Join(", ", notReady)}…");

            if (elapsed == 30)
                foreach (var s in notReady)
                    LogErr($"❌ {s} n'a pas démarré après 30s. La page du navigateur ne peut pas s'ouvrir.");
        }
    }

    private void OpenInBrowsers(LaunchOptions opts, string url)
    {
        if (opts.BrowserDefault) StartSilent(url);
        if (opts.BrowserChrome)
        {
            if (File.Exists(AppSettings.ChromeExe)) StartSilent(AppSettings.ChromeExe, url);
            else LogErr("❌ Chrome introuvable");
        }
        if (opts.BrowserFirefox)
        {
            if (File.Exists(AppSettings.FirefoxExe)) StartSilent(AppSettings.FirefoxExe, url);
            else LogErr("❌ Firefox introuvable");
        }
    }

    // ════════════════════════════════════════════════════════
    //  HELPERS — PROCESSUS
    // ════════════════════════════════════════════════════════

    /// <summary>Lance un .exe en arrière-plan total, aucune fenêtre.</summary>
    private void StartBackground(string fileName, string? args = null)
    {
        try
        {
            if (!File.Exists(fileName)) { Log($"⚠️ Introuvable : {fileName}"); return; }

            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            if (args is not null) psi.Arguments = args;
            Process.Start(psi);
            Log($"   ✅ {Path.GetFileName(fileName)} lancé");
        }
        catch (Exception ex) { Log($"⚠️ {Path.GetFileName(fileName)} : {ex.Message}"); }
    }

    /// <summary>Lance un .exe normalement (panneau XAMPP, navigateur, Visual Studio…).</summary>
    private void StartSilent(string fileName, string? args = null)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                CreateNoWindow = true,
            };
            if (args is not null) psi.Arguments = args;
            Process.Start(psi);
        }
        catch (Exception ex) { Log($"⚠️ {fileName} : {ex.Message}"); }
    }

    /// <summary>Lance VSCode via cmd pour éviter la fenêtre console parasite.</summary>
    private void StartVSCode(string projectPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c code \"{projectPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex) { Log($"⚠️ VSCode : {ex.Message}"); }
    }

    /// <summary>Tue tous les processus portant ce nom (insensible à la casse).</summary>
    private void KillProcess(string processName)
    {
        try
        {
            var list = Process.GetProcesses()
                .Where(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (list.Count == 0) { Log($"   ℹ️ {processName} n'était pas en cours"); return; }

            foreach (var p in list) { p.Kill(); Log($"   ✅ {processName} arrêté"); }
        }
        catch (Exception ex) { Log($"⚠️ Arrêt {processName} : {ex.Message}"); }
    }

    /// <summary>Vérifie si un processus est actif.</summary>
    private static bool IsProcessRunning(string processName)
    {
        try { return Process.GetProcessesByName(processName).Length > 0; }
        catch { return false; }
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);
    private void LogErr(string msg) => LogError?.Invoke(msg);
}