# 🗺️ DevLauncher — Roadmap

Ce document liste les évolutions proposées, regroupées par thème, avec une estimation d'effort
(**S** ≈ quelques heures, **M** ≈ 1 à 3 jours, **L** ≈ une semaine ou plus) et un ordre d'implémentation suggéré.
Rien n'est figé : chaque bloc est à valider avant d'être développé.

---

## ✅ État actuel

- 📁 Liste des projets du dossier `htdocs`, recherche, détection Symfony / Tailwind bundle
- 💾 Profils par projet (création, renommage, suppression, dernier profil mémorisé)
- 💻 VSCode / Visual Studio, services Symfony dans les tâches VSCode ou dans Windows Terminal
- 🗄️ Apache / MySQL / FileZilla / panneau XAMPP, indicateurs temps réel événementiels (WMI)
- 🌍 Ouverture du navigateur quand le serveur répond sur son port
- ⏹ Arrêt ciblé de ce qui a été lancé, fermeture propre des fenêtres d'éditeur du projet
- ⚙️ Fenêtre de paramètres (chemins, ports)

---

## 🧱 Phase 0 — Fondations (prérequis des grosses features)

| # | Évolution | Effort | Pourquoi |
|---|---|---|---|
| 0.1 | **Modèle d'outils générique** : un outil = nom, icône, commande, arguments avec variables (`{projectPath}`, `{projectName}`, `{port}`…), stratégie de disponibilité (port, processus, aucun), stratégie d'arrêt. Les profils deviennent une liste d'outils activés avec leurs options. | L | Aujourd'hui chaque outil est codé en dur (cases à cocher, champs du profil, branches dans `LaunchService`). Ajouter Claude, Docker, Laravel… sans ce socle = copier-coller à l'infini. |
| 0.2 | **Passage en MVVM** (bindings, `ICommand`, ViewModels) | M | Supprime le code-behind qui recopie l'UI dans le profil et inversement ; indispensable pour une liste d'outils dynamique. |
| 0.3 | **Données dans `%APPDATA%\DevLauncher`** avec migration automatique des fichiers existants | S | `settings.json` et `Profiles/` sont à côté de l'exe : perdus à chaque changement Debug/Release/publication, et non inscriptibles si l'exe est dans `Program Files`. |
| 0.4 | **Profils versionnables dans le projet** (`.devlauncher.json` à la racine, optionnel) | S | Partager la config d'un projet avec l'équipe via git. |
| 0.5 | **Tests unitaires** (scanner, profils, paramètres, génération `tasks.json`) + **CI GitHub Actions** (build + publication de l'exe en artefact de release) | M | Le projet compile aussi sous Linux (`EnableWindowsTargeting`) : la CI est triviale à mettre en place. |
| 0.6 | **Réduction des droits administrateur** : lancer les outils enfants (VSCode, navigateur, terminal) en utilisateur standard | M | Tout hérite aujourd'hui du jeton admin : VSCode affiche « Administrator », Chrome tourne élevé, le glisser-déposer depuis l'Explorateur est bloqué. Seules les traces WMI ont besoin de l'élévation. |
| 0.7 | **Journal persistant** (fichier tournant) + niveau de détail | S | Diagnostiquer un lancement raté après coup. |
| 0.8 | **Validation des paramètres** : pastille rouge sur les chemins introuvables, bouton « Détecter automatiquement » (registre, `where code`, emplacements XAMPP usuels) | S | Évite de découvrir un mauvais chemin au moment du lancement. |

---

## 🤖 Phase 1 — Intégrations IA (Claude, ChatGPT, autres)

Objectif : un projet DevLauncher est relié à son espace de travail IA, et l'IA apparaît dans la liste
des outils à lancer comme n'importe quel éditeur.

### 1.1 Connexion dans les paramètres — M

- Nouvel onglet **« Assistants IA »** dans les paramètres : Claude, ChatGPT, Gemini, Mistral…
- Pour chaque assistant : activation, mode d'ouverture (navigateur, application de bureau), et
  éventuelle clé API (stockée chiffrée via DPAPI, jamais en clair dans `settings.json`).

### 1.2 Outil « Claude » dans la liste des outils, avec liste déroulante des projets — M

- Case **« Claude »** dans la carte Outils ; une fois cochée, une liste déroulante propose les projets Claude.
- **Contrainte à connaître** : claude.ai n'expose pas d'API publique pour lister les *Projects* d'un compte
  (même constat côté ChatGPT). Deux approches réalistes :
  1. **Bibliothèque de projets IA gérée dans DevLauncher** : on enregistre une fois chaque projet
     (nom + URL `https://claude.ai/project/…`), la liste déroulante les propose ensuite pour tous les projets
     de dev. Ajout rapide par copier-coller de l'URL, avec récupération automatique du nom si possible.
  2. **Liaison automatique par convention** : si un projet Claude porte le même nom que le dossier
     (`templateSite`), il est proposé en premier.
- Au lancement : ouverture du projet Claude dans le navigateur choisi ou dans l'application de bureau
  (lien profond à vérifier selon la version de l'application).
- Même mécanique pour **ChatGPT Projects**, **Gemini Gems**, etc. : un seul modèle « assistant web » générique.

### 1.3 Claude Code / agents en ligne de commande — M

Ici, contrairement au web, la liste est **réellement automatique** :

- Outil **« Claude Code »** : ouvre un onglet de terminal dans le projet et lance `claude`.
- Liste déroulante des **sessions existantes du projet** (lues depuis `~/.claude/projects/<chemin du projet>/`),
  avec date et premier message → reprise via `claude --resume <id>`, ou « Continuer la dernière » (`claude --continue`),
  ou « Nouvelle session ».
- Même principe pour **Codex CLI**, **Gemini CLI**, **Aider** selon ce qui est installé (détection dans le `PATH`).
- Indicateur de présence d'un `CLAUDE.md` / `AGENTS.md`, avec bouton « Générer » (lance `claude /init`).

### 1.4 DevLauncher pilotable par l'IA (serveur MCP) — L

- DevLauncher expose un **serveur MCP local** : `list_projects`, `launch_project(profile)`, `stop_all`,
  `service_status`, `read_launch_log`.
- Claude (Desktop ou Code) peut alors démarrer l'environnement, lire les erreurs de lancement,
  redémarrer un service… directement depuis la conversation.

### 1.5 Contexte projet pour l'IA — S

- Bouton « Copier le contexte » : arborescence, stack détectée, commandes utiles, ports, URL locale —
  prêt à coller dans une conversation ou à déposer dans les fichiers d'un projet Claude.

---

## 🧩 Phase 2 — Plus de stacks et de services

| # | Évolution | Effort |
|---|---|---|
| 2.1 | **Détection multi-stack** : Laravel (`artisan serve`, Vite), Node (scripts `dev` / `start` de `package.json`), Vite / Next / Nuxt, Python (venv, `manage.py runserver`, `uvicorn`), .NET (`dotnet watch`), WordPress | M |
| 2.2 | **Plusieurs dossiers racines** (pas seulement `htdocs`) + ajout manuel d'un projet n'importe où + liste d'exclusions (`dashboard`, `img`, `xampp`… de XAMPP) | S |
| 2.3 | **Docker Compose** : détection de `compose.yaml`, `docker compose up -d` / `down`, état des conteneurs | M |
| 2.4 | **Virtual hosts Apache automatiques** (`templateSite.test`) avec entrée dans `hosts` | M |
| 2.5 | **Détection des conflits de ports** avant lancement (qui occupe le 80 / 3306 / 8000 ?) avec proposition d'action | S |
| 2.6 | **Base de données par projet** : création si absente (d'après `DATABASE_URL` du `.env`), import d'un dump, ouverture de phpMyAdmin / Adminer / HeidiSQL | M |
| 2.7 | **Symfony avancé** : détection HTTP/HTTPS et port réel via `symfony server:status`, workers Messenger, `symfony proxy`, Mailpit / Mailer | S |
| 2.8 | **Commandes « pré-lancement »** par profil : `composer install`, `npm install`, `doctrine:migrations:migrate`, `git pull` | S |

---

## 🖥️ Phase 3 — Terminaux et supervision intégrés

| # | Évolution | Effort |
|---|---|---|
| 3.1 | **Terminaux intégrés à DevLauncher** (ConPTY) : un onglet par service, sortie en direct, bouton redémarrer / arrêter par service | L |
| 3.2 | **Tableau de bord des services** : état, PID, port, uptime, dernière erreur ; détection des crashs avec notification | M |
| 3.3 | **Fermeture des onglets Windows Terminal** créés par DevLauncher lors de l'arrêt (aujourd'hui ils restent ouverts, services arrêtés) | S |
| 3.4 | **Relance automatique des tâches VSCode** quand le dossier est déjà ouvert (les tâches `folderOpen` ne se déclenchent qu'à l'ouverture) | S |

---

## 🎨 Phase 4 — Confort et ergonomie

| # | Évolution | Effort |
|---|---|---|
| 4.1 | **Icône dans la zone de notification** : lancer / arrêter un projet sans ouvrir la fenêtre | M |
| 4.2 | **Projets favoris et récents** en tête de liste, tri par date de dernier lancement | S |
| 4.3 | **Jump list de la barre des tâches** : clic droit sur l'icône → projets récents | S |
| 4.4 | **Raccourci clavier global** + palette de commandes (`Ctrl+K` : « lancer templateSite ») | M |
| 4.5 | **Notifications Windows** : « Environnement prêt », « MySQL s'est arrêté » | S |
| 4.6 | **Barre de progression du lancement** étape par étape | S |
| 4.7 | **Informations git** dans la liste : branche, modifications en attente, retard sur le distant | S |
| 4.8 | **Actions rapides** : ouvrir dans l'Explorateur, copier le chemin, ouvrir l'URL locale | S |
| 4.9 | **Thème clair / sombre** (suivi du thème Windows) | S |
| 4.10 | **Démarrage avec Windows** et **instance unique** (réactive la fenêtre existante) | S |

---

## ⚙️ Phase 5 — Automatisation

| # | Évolution | Effort |
|---|---|---|
| 5.1 | **Ligne de commande** : `DevLauncher.exe --project templateSite --profile Front` | S |
| 5.2 | **Protocole `devlauncher://launch/templateSite`** (liens dans un README, un bookmark, une note) | S |
| 5.3 | **Menu contextuel de l'Explorateur** : « Lancer avec DevLauncher » sur un dossier | S |
| 5.4 | **Import / export des profils et paramètres** (changement de machine) | S |
| 5.5 | **Statistiques** : temps de démarrage par projet, projets les plus utilisés | S |

---

## 🔢 Ordre d'implémentation suggéré

1. **0.3 Données dans `%APPDATA%`** — rapide, évite de perdre des profils à chaque build
2. **0.1 + 0.2 Modèle d'outils générique + MVVM** — le socle de tout le reste
3. **1.1 → 1.3 Intégrations IA** (bibliothèque de projets Claude / ChatGPT, Claude Code avec reprise de session)
4. **2.2 + 2.1 Plusieurs racines + multi-stack** — l'outil sort du seul cadre XAMPP / Symfony
5. **4.1 + 4.2 + 4.5 Zone de notification, favoris, notifications**
6. **3.1 + 3.2 Terminaux intégrés et tableau de bord** — le gros morceau qui rend Windows Terminal optionnel
7. **1.4 Serveur MCP** — une fois les actions centralisées, l'exposer à l'IA devient simple
8. Le reste selon l'usage réel

---

## ❓ Points à trancher ensemble

- Liste des projets IA : bibliothèque gérée à la main (fiable) ou tentative de récupération automatique
  (fragile, non documentée, susceptible de casser à chaque mise à jour du site) ?
- Ouverture de Claude : navigateur, application de bureau, ou au choix par projet ?
- Terminaux : garder Windows Terminal, ou investir dans les terminaux intégrés (3.1) ?
- Droits administrateur : acceptables tels quels, ou priorité à 0.6 ?
