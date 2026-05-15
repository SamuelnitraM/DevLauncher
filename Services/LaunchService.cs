using System.Diagnostics;
using System.IO;

namespace DevLauncher.Services;

public class LaunchService
{
    private readonly string _xamppDir;
    private readonly string _mercureDir;

    public event Action<string>? LogMessage;

    public LaunchService(string xamppDir, string mercureDir)
    {
        _xamppDir = xamppDir;
        _mercureDir = mercureDir;
    }

    public async Task LaunchAsync(string projectPath, LaunchOptions opts)
    {
        // 1. Éditeur
        if (opts.OpenVSCode)
        {
            Log("💻 Ouverture de VSCode…");
            StartSilent(AppSettings.VSCodeExecutable, $"\"{projectPath}\"");
        }

        if (opts.OpenVisualStudio)
        {
            Log("💻 Ouverture de Visual Studio…");
            var slnFiles = Directory.GetFiles(projectPath, "*.sln");
            if (slnFiles.Length > 0)
            {
                Log($"   → Solution : {Path.GetFileName(slnFiles[0])}");
                StartSilent(AppSettings.VisualStudioExecutable, $"\"{slnFiles[0]}\"");
            }
            else
            {
                Log("   → Pas de .sln, ouverture du dossier…");
                StartSilent(AppSettings.VisualStudioExecutable, $"\"{projectPath}\"");
            }
        }

        // 2. Panneau XAMPP (optionnel, juste pour surveiller)
        if (opts.ShowXamppPanel)
        {
            if (IsProcessRunning("xampp-control"))
                Log("🖥️ Panneau XAMPP déjà ouvert — ignoré");
            else
            {
                Log("🖥️ Ouverture du panneau XAMPP…");
                StartSilent(AppSettings.XamppPanel);
            }
        }

        // 3. Services XAMPP — lancement direct des .exe, aucune fenêtre
        if (opts.StartApache)
        {
            if (IsProcessRunning("httpd"))
                Log("🌐 Apache déjà en cours — ignoré");
            else
            {
                Log("🌐 Démarrage d'Apache…");
                StartBackground(AppSettings.ApacheExe);
            }
        }

        if (opts.StartMySQL)
        {
            if (IsProcessRunning("mysqld"))
                Log("🗃️ MySQL déjà en cours — ignoré");
            else
            {
                Log("🗃️ Démarrage de MySQL…");
                StartBackground(AppSettings.MySQLExe,
                    $"--defaults-file=\"{AppSettings.MySQLConfig}\"");
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
            }
        }

        // Pause pour laisser le temps aux services de démarrer
        if (opts.StartApache || opts.StartMySQL || opts.StartFileZilla)
        {
            Log("⏳ Attente démarrage des services (2s)…");
            await Task.Delay(2000);
        }

        // 4. Terminal optionnel
        if (!opts.IsSymfony && opts.OpenTerminal)
        {
            Log("🖥️ Ouverture d'un terminal…");
            var projectName = Path.GetFileName(projectPath);
            StartSilent("wt", $"-w 0 new-tab --title \"{projectName}\" -d \"{projectPath}\"");
        }

        // 5. Onglets Symfony
        if (opts.IsSymfony)
        {
            Log("⚡ Lancement des services Symfony…");
            LaunchWindowsTerminalSymfony(projectPath, opts);
        }

        // 6. Navigateur
        if (opts.OpenBrowser)
        {
            var url = opts.IsSymfony
                ? $"http://localhost:{AppSettings.SymfonyPort}"
                : $"http://localhost/{Path.GetFileName(projectPath)}";

            Log($"🌍 Ouverture du/des navigateur(s) : {url}");
            await Task.Delay(1000);

            if (opts.BrowserDefault)
                StartSilent(url);

            if (opts.BrowserChrome)
            {
                if (File.Exists(AppSettings.ChromeExe))
                    StartSilent(AppSettings.ChromeExe, url);
                else
                    Log("⚠️ Chrome introuvable");
            }

            if (opts.BrowserFirefox)
            {
                if (File.Exists(AppSettings.FirefoxExe))
                    StartSilent(AppSettings.FirefoxExe, url);
                else
                    Log("⚠️ Firefox introuvable");
            }
        }
    }

    public void StopAll()
    {
        Log("⏹ Arrêt d'Apache…");
        KillProcess("httpd");

        Log("⏹ Arrêt de MySQL…");
        KillProcess("mysqld");

        Log("⏹ Arrêt de FileZilla…");
        KillProcess("FileZillaServer");

        Log("⏹ Fermeture du panneau XAMPP…");
        KillProcess("xampp-control");
    }

    private void LaunchWindowsTerminalSymfony(string projectPath, LaunchOptions opts)
    {
        var args = new List<string> { "-w", "0" };
        bool firstTab = true;

        void AddTab(string title, string command)
        {
            if (!firstTab) args.Add(";");
            args.AddRange(new[]
            {
                "new-tab", "--title", title,
                "-d", $"\"{projectPath}\"",
                "powershell", "-NoExit", "-Command", command
            });
            firstTab = false;
            Log($"   → onglet {title}");
        }

        if (opts.StartSymfonyServer) AddTab("Symfony Server", "symfony server:start");
        if (opts.StartTailwind) AddTab("Tailwind Watch", "symfony console tailwind:build --watch");

        if (opts.StartMercure && !string.IsNullOrEmpty(opts.MercureScript))
        {
            if (!firstTab) args.Add(";");
            var scriptPath = Path.Combine(_mercureDir, opts.MercureScript!);
            args.AddRange(new[]
            {
                "new-tab", "--title", "Mercure",
                "-d", $"\"{_mercureDir}\"",
                "powershell", "-NoExit", "-File", $"\"{scriptPath}\""
            });
            Log($"   → onglet Mercure ({opts.MercureScript})");
            firstTab = false;
        }

        if (!firstTab)
        {
            var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            Process.Start(psi);
        }
    }

    // ── Lance un .exe sans aucune fenêtre ──────────────────
    private void StartBackground(string fileName, string? args = null)
    {
        try
        {
            if (!File.Exists(fileName))
            {
                Log($"⚠️ Introuvable : {fileName}");
                return;
            }

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
        catch (Exception ex)
        {
            Log($"⚠️ {Path.GetFileName(fileName)} : {ex.Message}");
        }
    }

    // ── Lance un .exe normalement (VSCode, panneau XAMPP…) ──
    private void StartSilent(string fileName, string? args = null)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
            };
            if (args is not null) psi.Arguments = args;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log($"⚠️ {fileName} : {ex.Message}");
        }
    }

    // ── Tue un processus par nom ────────────────────────────
    private void KillProcess(string processName)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                p.Kill();
                Log($"   ✅ {processName} arrêté");
            }
        }
        catch (Exception ex)
        {
            Log($"⚠️ Arrêt {processName} : {ex.Message}");
        }
    }

    // ── Vérifie si un processus tourne déjà ────────────────
    private static bool IsProcessRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);
}