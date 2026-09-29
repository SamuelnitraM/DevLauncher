# ⚡ DevLauncher — Guide complet

Application WPF (.NET 8) qui remplace ton script PowerShell `launch-project.ps1`
par une interface graphique moderne.

---

## 📁 Structure du projet

```
DevLauncher/
│
├── DevLauncher.csproj          ← Fichier projet .NET (WPF, .NET 8, System.Management)
├── app.manifest                ← Demande des droits administrateur (traces WMI des processus)
├── App.xaml / App.xaml.cs      ← Point d'entrée : thème global + chargement de settings.json
├── AppSettings.cs              ← Valeurs par défaut des chemins et ports
├── MainWindow.xaml / .cs       ← Fenêtre principale (projets, options, profils, journal)
│
├── Views/
│   ├── SettingsWindow          ← Fenêtre des paramètres (chemins, ports)
│   └── ProfileNameDialog       ← Saisie du nom d'un profil (création / renommage)
│
├── Models/
│   ├── ProjectProfile.cs       ← Profil de lancement d'un projet (= requête de lancement)
│   └── ProjectDetection.cs     ← Technologies détectées dans un projet
│
└── Services/
    ├── LaunchService.cs        ← Lance / arrête éditeurs, XAMPP, Symfony, terminal, navigateur
    ├── ProjectScanner.cs       ← Liste les projets et détecte Symfony / Tailwind
    ├── ProfileService.cs       ← Persistance des profils par projet (JSON)
    ├── SettingsService.cs      ← Persistance des paramètres (settings.json)
    ├── ProcessEventWatcher.cs  ← Événements WMI de démarrage / arrêt des processus
    ├── ServiceMonitor.cs       ← État Apache / MySQL / FileZilla en temps réel (événementiel)
    ├── ProcessHelper.cs        ← Utilitaires communs sur les processus
    └── NativeWindowService.cs  ← Fermeture propre (WM_CLOSE) des fenêtres d'un projet
```

---

## 🛠️ Installation & compilation

### Prérequis

- **Visual Studio 2022** (Community = gratuit) avec le workload **.NET Desktop Development**
  → https://visualstudio.microsoft.com/fr/
- OU **VS Code** + SDK .NET 8 : https://dotnet.microsoft.com/download

### Avec Visual Studio (recommandé)

1. Ouvre Visual Studio
2. **Fichier → Ouvrir → Projet/Solution**
3. Sélectionne `DevLauncher.csproj`
4. Appuie sur **F5** pour tester, ou **Ctrl+Shift+B** pour compiler
5. Le `.exe` se trouve dans `bin\Debug\net8.0-windows\DevLauncher.exe`

### Avec la ligne de commande

```powershell
# Depuis le dossier DevLauncher/
dotnet run                        # Lance directement
dotnet build                      # Compile en mode Debug
dotnet publish -c Release         # Crée un .exe Release dans bin\Release\
```

---

## 📦 Créer un .exe portable (un seul fichier)

```powershell
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

Le `.exe` final se trouve dans :
`bin\Release\net8.0-windows\win-x64\publish\DevLauncher.exe`

Tu peux le copier où tu veux et créer un raccourci sur le Bureau.

> **Note** : `--self-contained false` signifie que .NET 8 doit être installé sur la machine
> (il l'est sur tout Windows 10/11 à jour). Pour une version 100% autonome,
> utilise `--self-contained true` (le .exe fera ~60 Mo).

---

## ⚙️ Personnaliser les chemins

Tout se règle depuis le bouton **⚙️ Paramètres** : dossier des projets, exécutables XAMPP,
dossier Mercure, éditeurs, navigateurs, ports Symfony et Apache.
Les valeurs sont enregistrées dans `settings.json`, à côté de l'exécutable.

---

## ✨ Fonctionnalités

- 📁 Liste automatique des projets du dossier `htdocs`, avec recherche
- ✅ Détection automatique Symfony (`symfony.lock`, `bin/console`, `composer.json`) et Tailwind bundle
- 💾 Profils de lancement par projet (création, renommage, suppression, mémorisation du dernier utilisé)
- 💻 VSCode / Visual Studio, avec services Symfony dans les terminaux intégrés de VSCode
  (le `tasks.json` du projet est préservé et restauré)
- ⚡ Sinon, onglets Windows Terminal dédiés (fenêtre « DevLauncher ») : Symfony Server / Tailwind / Mercure
- 🌍 Ouverture du navigateur dès que le serveur répond réellement sur son port
- 🌐 Indicateurs Apache / MySQL / FileZilla en temps réel, sans polling
- ⏹ « Tout arrêter » : n'arrête que ce que DevLauncher a lancé, ferme proprement les fenêtres d'éditeur du projet
- 📋 Journal de lancement horodaté

---

## 🗺️ Évolutions

Voir [Roadmap.md](Roadmap.md).
