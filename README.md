# ⚡ DevLauncher — Guide complet

Application WPF (.NET 8) qui remplace ton script PowerShell `launch-project.ps1`
par une interface graphique moderne.

---

## 📁 Structure du projet

```
DevLauncher/
│
├── DevLauncher.csproj       ← Fichier projet .NET (comme un package.json)
├── App.xaml                 ← Point d'entrée WPF + thème global (couleurs, styles)
├── App.xaml.cs              ← Code C# lié à App.xaml (quasi vide)
├── AppSettings.cs           ← Tous les chemins configurables (htdocs, xampp, mercure)
├── MainWindow.xaml          ← Interface graphique (XML/XAML)
├── MainWindow.xaml.cs       ← Logique de l'interface (clics, événements)
│
└── Services/
    ├── LaunchOptions.cs     ← Modèle de données : quelles options sont cochées ?
    ├── LaunchService.cs     ← Lance les processus (wt, code, apache_start.bat…)
    ├── ProjectScanner.cs    ← Lit htdocs, détecte si c'est un projet Symfony
    └── ServiceMonitor.cs    ← Surveille Apache/MySQL toutes les 3 secondes
```

### Rôle de chaque fichier en détail

| Fichier | Rôle |
|---|---|
| `DevLauncher.csproj` | Déclare : c'est une app WPF, .NET 8, sortie en .exe. Équivalent du `package.json` |
| `App.xaml` | Définit le thème visuel global : couleurs, styles des boutons, CheckBox… |
| `AppSettings.cs` | **Modifie ce fichier** si tes chemins sont différents de `C:\xampp\htdocs` |
| `MainWindow.xaml` | Le layout de la fenêtre. XAML = HTML pour WPF |
| `MainWindow.xaml.cs` | Les actions (clic sur "Lancer", sélection d'un projet, etc.) |
| `LaunchOptions.cs` | Un simple objet qui transporte les cases cochées vers le service |
| `LaunchService.cs` | Le "cerveau" : démarre VSCode, les .bat XAMPP, Windows Terminal |
| `ProjectScanner.cs` | Lit le dossier htdocs et vérifie si un projet a `symfony.lock` ou `bin/console` |
| `ServiceMonitor.cs` | Timer qui vérifie si `httpd.exe` et `mysqld.exe` tournent → met à jour les indicateurs |

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

Ouvre `AppSettings.cs` et modifie :

```csharp
public static string HtdocsPath { get; set; } = @"C:\xampp\htdocs";
public static string XamppDir   { get; set; } = @"C:\xampp";
public static string MercureDir { get; set; } = @"C:\mercure";
```

---

## ✨ Fonctionnalités

- 📁 Liste automatique de tous les projets dans `htdocs`
- 🔍 Barre de recherche pour filtrer les projets
- ✅ Détection automatique Symfony (`symfony.lock`, `bin/console`, `composer.json`)
- ⚡ Onglets Windows Terminal séparés : Symfony Server / Tailwind / Mercure
- 📡 Liste dynamique des scripts `start*.ps1` dans le dossier Mercure
- 🌐 Indicateurs Apache / MySQL en temps réel (vert = actif, rouge = inactif)
- ⏹ Bouton "Tout arrêter" pour stopper Apache et MySQL
- 📋 Journal de lancement horodaté

---

## 🔧 Idées d'améliorations futures

- [ ] Sauvegarder les préférences par projet dans un fichier JSON
- [ ] Ajouter d'autres types de projets (Laravel, Node.js, etc.)
- [ ] Lancer depuis le menu contextuel de l'Explorateur Windows
- [ ] Icône dans la barre des tâches (System Tray) pour accès rapide
- [ ] Historique des projets récemment ouverts
