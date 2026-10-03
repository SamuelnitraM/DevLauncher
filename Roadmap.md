# 🗺️ DevLauncher — Roadmap

Ce document liste les évolutions proposées, regroupées par thème, avec une estimation d'effort
(**S** ≈ quelques heures, **M** ≈ 1 à 3 jours, **L** ≈ une semaine ou plus) et un ordre d'implémentation suggéré.
Rien n'est figé : chaque bloc est à valider avant d'être développé.

---

## ✅ État actuel

- 📁 Liste des projets du dossier `htdocs`, recherche, détection Symfony / Tailwind bundle
- 💾 Profils par projet (création, renommage, suppression, dernier profil mémorisé)
- 💻 VSCode / Visual Studio, services Symfony lancés par DevLauncher (ou dans les tâches VSCode en option)
- 🗄️ Apache / MySQL / FileZilla / panneau XAMPP, indicateurs temps réel événementiels (WMI)
- 🌍 Ouverture du navigateur quand le serveur répond sur son port
- ⏹ Arrêt ciblé de ce qui a été lancé, fermeture propre des fenêtres d'éditeur du projet
- ⚙️ Fenêtre de paramètres (chemins, ports)
- 🗂️ Données dans `%APPDATA%\DevLauncher` avec reprise des anciennes données (0.3)
- 🤖 Compilation automatique GitHub Actions, exe en artefact et en release sur tag `v*` (0.5, partie CI)
- ⭐ Projets récents en tête de liste, mis à jour au lancement uniquement (4.2, partie récents)
- 🧩 Catalogue d'outils générique + architecture MVVM (0.1, 0.2) : l'interface des options est générée depuis le catalogue,
  profils au nouveau format avec conversion automatique des anciens

---

## 🧱 Phase 0 — Fondations (prérequis des grosses features)

| # | Évolution | Effort | Pourquoi |
|---|---|---|---|
| 0.1 | ✅ **Modèle d'outils générique** : un outil = nom, icône, commande, arguments avec variables (`{projectPath}`, `{projectName}`, `{port}`…), stratégie de disponibilité (port, processus, aucun), stratégie d'arrêt. Les profils deviennent une liste d'outils activés avec leurs options. | L | Aujourd'hui chaque outil est codé en dur (cases à cocher, champs du profil, branches dans `LaunchService`). Ajouter Claude, Docker, Laravel… sans ce socle = copier-coller à l'infini. |
| 0.2 | ✅ **Passage en MVVM** (bindings, `ICommand`, ViewModels) | M | Supprime le code-behind qui recopie l'UI dans le profil et inversement ; indispensable pour une liste d'outils dynamique. |
| 0.3 | ✅ **Données dans `%APPDATA%\DevLauncher`** avec migration automatique des fichiers existants | S | `settings.json` et `Profiles/` sont à côté de l'exe : perdus à chaque changement Debug/Release/publication, et non inscriptibles si l'exe est dans `Program Files`. |
| 0.4 | ✅ **Profils versionnables dans le projet** (`.devlauncher.json` à la racine, optionnel) | S | Partager la config d'un projet avec l'équipe via git. |
| 0.5 | ✅ **Tests unitaires** (scanner, profils, paramètres, génération `tasks.json`) + ✅ **CI GitHub Actions** (build + publication de l'exe en artefact de release) | M | Le projet compile aussi sous Linux (`EnableWindowsTargeting`) : la CI est triviale à mettre en place. |
| 0.6 | **Option « Lancer les applications sans élévation »** (par outil) : DevLauncher reste administrateur, mais peut démarrer VSCode, le navigateur ou une IA avec le jeton de l'utilisateur standard | S | Les droits administrateur sont conservés (accès total au système, traces WMI). Les applications enfants en héritent : VSCode affiche « Administrator », le navigateur tourne élevé, le glisser-déposer depuis l'Explorateur vers ces fenêtres est bloqué. L'option permet de choisir outil par outil. |
| 0.7 | ✅ **Journal persistant** (fichier tournant) + niveau de détail | S | Diagnostiquer un lancement raté après coup. |
| 0.8 | ✅ **Validation des paramètres** : pastille rouge sur les chemins introuvables, bouton « Détecter automatiquement » (registre, `where code`, emplacements XAMPP usuels) | S | Évite de découvrir un mauvais chemin au moment du lancement. |

---

## 🤖 Phase 1 — Intégrations IA (Claude, ChatGPT, Gemini, autres)

Objectif : l'IA apparaît dans la liste des outils à lancer, comme un éditeur. Deux modes distincts,
choisis par assistant et modifiables par profil.

### 1.1 ✅ Catalogue d'assistants dans les paramètres — M

- Onglet **« Assistants IA »** : Claude, ChatGPT, Gemini, Mistral, Perplexity… plus ajout d'un assistant personnalisé.
- Pour chaque assistant : activation, **mode par défaut** (Navigateur ou Application), URL web,
  chemin de l'application de bureau (détecté automatiquement quand c'est possible).
- Les assistants activés apparaissent dans la carte **Outils** de la fenêtre principale.

### 1.2 ✅ Mode Navigateur — S

- DevLauncher ouvre le navigateur choisi sur la page de l'IA (`https://claude.ai`, `https://chatgpt.com`,
  `https://gemini.google.com`…), c'est-à-dire une nouvelle discussion.
- Option par projet : une URL précise (projet Claude, projet ChatGPT, Gem…) collée une fois dans le profil,
  ouverte à la place de la page d'accueil.

### 1.3 ✅ Mode Application — M

- DevLauncher lance l'application de bureau de l'IA.
- **Si l'application permet de connaître ses projets** (API locale, fichiers de données lisibles, lien profond) :
  liste déroulante des projets dans la carte Outils, et ouverture directe du projet choisi.
- **Sinon** : l'application s'ouvre simplement sur une nouvelle discussion. Pas de bricolage fragile.
- Chaque assistant déclare ce qu'il sait faire (lister des projets, ouvrir un projet, ouvrir une discussion vierge) ;
  l'interface n'affiche la liste déroulante que lorsque c'est supporté.
- État actuel connu : ni Claude Desktop ni ChatGPT Desktop n'exposent de moyen documenté de lister les projets.
  Ils démarreront donc sur une nouvelle discussion, et la liste s'activera d'elle-même si un éditeur l'ouvre un jour.

### 1.4 Agents en ligne de commande (Claude Code, Codex CLI, Gemini CLI, Aider) — M

> Essayé avec Claude Code puis retiré : il suppose l'outil en ligne de commande installé, ce qui n'est pas le cas
> sur le poste cible. À reprendre avec une vérification de présence dans le PATH et un lien d'installation.

### 1.5 DevLauncher pilotable par l'IA (serveur MCP) — L

- DevLauncher expose un **serveur MCP local** : `list_projects`, `launch_project(profile)`, `stop_all`,
  `service_status`, `read_service_logs`, `restart_service`.
- Une IA peut alors démarrer l'environnement, lire les erreurs d'un service et le redémarrer depuis la conversation.

### 1.6 Contexte projet pour l'IA — S

- Bouton « Copier le contexte » : arborescence, stack détectée, commandes utiles, ports, URL locale —
  prêt à coller dans une discussion ou à déposer dans les fichiers d'un projet IA.

---

## 🧩 Phase 2 — Plus de stacks et de services

| # | Évolution | Effort |
|---|---|---|
| 2.1 | ✅ **Détection multi-stack** : Laravel (`artisan serve`, Vite), Node (scripts `dev` / `start` de `package.json`), Vite / Next / Nuxt, Python (venv, `manage.py runserver`, `uvicorn`), .NET (`dotnet watch`), WordPress | M |
| 2.2 | ✅ **Plusieurs dossiers racines** (pas seulement `htdocs`) + ajout manuel d'un projet n'importe où + liste d'exclusions (`dashboard`, `img`, `xampp`… de XAMPP) | S |
| 2.3 | **Docker Compose** : détection de `compose.yaml`, `docker compose up -d` / `down`, état des conteneurs | M |
| 2.4 | **Virtual hosts Apache automatiques** (`templateSite.test`) avec entrée dans `hosts` | M |
| 2.5 | **Détection des conflits de ports** avant lancement (qui occupe le 80 / 3306 / 8000 ?) avec proposition d'action | S |
| 2.6 | **Base de données par projet** : création si absente (d'après `DATABASE_URL` du `.env`), import d'un dump, ouverture de phpMyAdmin / Adminer / HeidiSQL | M |
| 2.7 | **Symfony avancé** : détection HTTP/HTTPS et port réel via `symfony server:status`, workers Messenger, `symfony proxy`, Mailpit / Mailer | S |
| 2.8 | **Commandes « pré-lancement »** par profil : `composer install`, `npm install`, `doctrine:migrations:migrate`, `git pull` | S |

---

## 🖥️ Phase 3 — Services supervisés par DevLauncher

Décision : les **services** (Symfony Server, Tailwind, Mercure, Vite, workers…) sont lancés directement par
DevLauncher, sortie capturée. Les **terminaux interactifs** restent dans Windows Terminal, qui fait ça très bien.

| # | Évolution | Effort |
|---|---|---|
| 3.1 | ✅ **Lancement direct des services** (processus enfants, sortie standard et erreur redirigées, entrée standard maintenue ouverte pour les `--watch`) : DevLauncher connaît chaque PID, arrêt exact, fin du `tasks.json` temporaire et des onglets Windows Terminal orphelins | M |
| 3.2 | ✅ (base) **Panneau de journaux par service** : un onglet par service, copie, erreurs en rouge — reste : couleurs ANSI, recherche | M |
| 3.3 | ✅ **Contrôle par service** : démarrer / arrêter / redémarrer individuellement, détection immédiate d'un crash (événement de fin de processus) avec notification | S |
| 3.4 | **Disponibilité lue dans les journaux** (« Listening on… », « Done in… ») en complément du test de port : ouverture du navigateur au bon moment, port et schéma HTTP/HTTPS réels | S |
| 3.5 | **Terminal interactif** : bouton « Ouvrir un terminal » conservé dans Windows Terminal (onglet dans le dossier du projet, profil PowerShell / CMD / Git Bash au choix) | S |
| 3.7 | ✅ **Objet Job Windows** : les services meurent avec DevLauncher même en cas de plantage ou d'arrêt du débogage ; les instances laissées par une session précédente sont arrêtées avant un nouveau lancement | S |
| 3.6 | ✅ **Mode « services dans VSCode »** conservé en option pour ceux qui préfèrent les terminaux intégrés de l'éditeur | S |

---

## 🎨 Phase 4 — Confort et ergonomie

| # | Évolution | Effort |
|---|---|---|
| 4.1 | **Icône dans la zone de notification** : lancer / arrêter un projet sans ouvrir la fenêtre | M |
| 4.2 | ✅ **Projets récents** en tête de liste (mis à jour au lancement, jamais au clic : la liste ne bouge plus sous le curseur) — reste : **favoris épinglés** | S |
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
| 5.4 | ✅ **Import / export des profils et paramètres** (changement de machine) | S |
| 5.5 | **Statistiques** : temps de démarrage par projet, projets les plus utilisés | S |

---

## 🔢 Ordre d'implémentation suggéré

1. ✅ **0.3 Données dans `%APPDATA%`**, ✅ **CI**, ✅ **projets récents**
2. ✅ **0.1 + 0.2 Modèle d'outils générique + MVVM** — le socle de tout le reste
3. ✅ **3.1 → 3.3 Services supervisés** — supprime les rustines actuelles (`tasks.json`, onglets orphelins, arrêt par nom de processus)
4. **1.1 → 1.4 Intégrations IA** (modes Navigateur / Application, agents CLI avec sessions)
5. ✅ **2.2 + 2.1 Plusieurs racines + multi-stack**
6. **4.1 + 4.2 + 4.5 Zone de notification, favoris, notifications**
7. **1.5 Serveur MCP** — une fois les actions centralisées, l'exposer à l'IA devient simple
8. Le reste selon l'usage réel

---

## 📌 Décisions prises

- **IA** : deux modes. Navigateur → page de l'IA (ou URL de projet enregistrée). Application → projet choisi
  si l'application sait lister ses projets, sinon nouvelle discussion.
- **Terminaux** : services lancés et supervisés par DevLauncher ; Windows Terminal conservé pour les terminaux interactifs.
- **Droits administrateur** : conservés. Option de lancement sans élévation, outil par outil (0.6).
