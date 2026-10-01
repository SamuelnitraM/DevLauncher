# ⚡ DevLauncher — Guide complet

Application WPF (.NET 8) qui remplace ton script PowerShell `launch-project.ps1`
par une interface graphique moderne.

---

## 📁 Structure du projet

Architecture MVVM : les vues ne contiennent que de l'affichage, la logique est dans les ViewModels et les services.
Chaque outil (éditeur, serveur, terminal, navigateur…) est une classe du catalogue d'outils :
l'interface des options est générée à partir de ce catalogue.

```
DevLauncher/
│
├── DevLauncher.csproj              ← Projet .NET 8 WPF (CommunityToolkit.Mvvm, System.Management)
├── app.manifest                    ← Droits administrateur (traces WMI des processus)
├── App.xaml / App.xaml.cs          ← Thème global + point de composition (création des services et de la fenêtre)
├── AppSettings.cs                  ← Valeurs par défaut des chemins et ports
│
├── Views/                          ← Affichage uniquement
│   ├── MainWindow                  ← Fenêtre principale, liée à MainViewModel
│   ├── SettingsWindow              ← Paramètres (chemins, ports)
│   ├── ProfileNameDialog           ← Saisie d'un nom de profil
│   ├── UserInteractionService.cs   ← Boîtes de dialogue et presse-papiers demandés par les ViewModels
│   └── Converters/                 ← Convertisseurs de binding
│
├── ViewModels/                     ← État et actions de l'interface
│   ├── MainViewModel.cs            ← Projets, profils, lancement / arrêt, journal, indicateurs
│   ├── ToolCategoryViewModel.cs    ← Carte d'outils (catégorie exclusive = boutons radio)
│   ├── ToolViewModel.cs            ← Outil activable et ses options
│   └── ToolOptionViewModel.cs      ← Option d'outil (liste déroulante ou cases à cocher)
│
├── Models/
│   ├── ProjectProfile.cs           ← Profil : type de projet + état de chaque outil (= requête de lancement)
│   ├── ToolSelection.cs            ← État d'un outil dans un profil (activé, valeurs des options)
│   ├── ToolIds.cs                  ← Identifiants stables des outils et de leurs options
│   └── …                           ← Type de projet, détection, entrée de liste, ligne de journal
│
└── Services/
    ├── LaunchService.cs            ← Orchestrateur : démarre les outils étape par étape, arrête ce qu'il a lancé
    ├── Tools/                      ← Catalogue d'outils (un fichier par famille)
    │   ├── LaunchTool.cs           ← Classe de base : étape, portée, options, démarrage, arrêt
    │   ├── EditorTools.cs          ← VSCode, Visual Studio
    │   ├── SymfonyTools.cs         ← Symfony Server, Tailwind, Mercure
    │   ├── XamppComponentTool.cs   ← Apache, MySQL, FileZilla, panneau XAMPP
    │   ├── AssistantTools.cs       ← Assistants IA (navigateur / application)
    │   ├── FrameworkTools.cs       ← Laravel, script npm, Django, dotnet watch
    │   ├── UtilityTools.cs         ← Terminal, navigateur
    │   └── ToolCatalog.cs          ← Liste des outils connus
    ├── Assistants/                 ← Catalogue des assistants IA, détection de leurs applications
    ├── Hosting/                    ← Hébergement des services : processus gérés par DevLauncher (un onglet de journal
    │                                 par service, arrêt de toute l'arborescence) ou tâches VSCode (option)
    ├── ProcessLauncher.cs          ← Démarrage / arrêt / fermeture de processus, avec journalisation
    ├── ProcessEventWatcher.cs      ← Événements WMI de démarrage / arrêt des processus
    ├── ServiceMonitor.cs           ← État Apache / MySQL / FileZilla en temps réel
    ├── ProfileService.cs           ← Profils par projet (JSON), conversion des anciens profils
    ├── SettingsService.cs          ← Paramètres (settings.json)
    ├── StoragePaths.cs             ← Dossier de données %APPDATA%\DevLauncher
    └── …                           ← Scanner de projets, récents, sonde de port, fenêtres natives
```

### Ajouter un outil

1. Créer une classe dérivée de `LaunchTool` (ou `ServiceTool` pour une commande longue durée) dans `Services/Tools/`
2. Lui donner un identifiant dans `ToolIds`, une catégorie, une étape de lancement et, si besoin, des options
3. L'ajouter à `ToolCatalog`

L'interface (case à cocher, options, sauvegarde dans les profils) suit automatiquement.

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

## 🤖 Compilation automatique (GitHub Actions)

Chaque push sur `master` compile l'application sur un runner Windows et publie `DevLauncher.exe`
dans les artefacts du workflow **Build** (onglet *Actions* du dépôt).
Un tag `v*` (ex. `v1.2.0`) crée en plus une release GitHub avec l'exécutable attaché.

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
Les paramètres, profils et projets récents sont enregistrés dans `%APPDATA%\DevLauncher`,
partagés par toutes les copies de l'exécutable (Debug, Release, version publiée).
Au premier démarrage, les anciens `settings.json` et `Profiles\` situés à côté de l'exécutable y sont copiés.

---

## ✨ Fonctionnalités

- 📁 Liste automatique des projets, avec recherche
- ⭐ Bloc « Récemment lancés » distinct, au-dessus de la liste complète (5 derniers projets lancés)
- ✅ Détection automatique Symfony (`symfony.lock`, `bin/console`, `composer.json`) et Tailwind bundle
- 💾 Profils de lancement par projet (création, renommage, suppression, mémorisation du dernier utilisé)
- 💻 VSCode / Visual Studio, avec services Symfony dans les terminaux intégrés de VSCode
  en option (le `tasks.json` du projet est préservé et restauré)
- ⚡ Par défaut, services (Symfony Server, Tailwind, Mercure) lancés par DevLauncher : un onglet de journal par service,
  boutons Arrêter / Redémarrer, détection immédiate d'un arrêt inattendu, arrêt complet avec les processus enfants
- 🌍 Ouverture du navigateur dès que le serveur répond réellement sur son port
- 🌐 Indicateurs Apache / MySQL / FileZilla en temps réel, sans polling
- ⏹ « Tout arrêter » : n'arrête que ce que DevLauncher a lancé, ferme proprement les fenêtres d'éditeur du projet
- 🤖 Assistants IA dans les outils : Claude, ChatGPT, Gemini, Mistral, Perplexity en mode Navigateur
  (nouvelle discussion ou projet enregistré, celui nommé comme le dossier proposé par défaut) ou Application
  (application de bureau détectée automatiquement)
- 🧭 Plusieurs dossiers de projets, projets ajoutés un par un, dossiers ignorés (pages XAMPP par défaut)
- 🧩 Détection Symfony, Laravel, WordPress, Node.js, Django, .NET avec leurs serveurs de développement ;
  le navigateur s'ouvre sur l'adresse annoncée par le serveur dans son journal
- 📋 Journal de lancement horodaté

---

## 🗺️ Évolutions

Voir [Roadmap.md](Roadmap.md).
