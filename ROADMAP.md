# 🗺️ DevLauncher — Roadmap

## ✅ Fonctionnalités implémentées

- 📁 Liste automatique des projets dans `htdocs` avec barre de recherche
- ✅ Détection automatique Symfony (`symfony.lock`, `bin/console`, `composer.json`)
- 🚀 Lancement des services XAMPP sans fenêtre (Apache, MySQL, FileZilla)
- 🔒 Sécurité anti-double lancement des services
- ⚡ Onglets Windows Terminal séparés (Symfony Server / Tailwind / Mercure)
- 💻 Intégration VSCode — tâches automatiques dans le terminal intégré
- 🟣 Choix de l'éditeur (VSCode / Visual Studio / Aucun)
- 🌍 Choix du navigateur (Défaut / Chrome / Firefox) avec support multi-navigateurs
- 🌐 Indicateurs Apache / MySQL / FileZilla en temps réel (vert/rouge)
- ⏹ Bouton "Tout arrêter" intelligent (arrête uniquement ce qui a été lancé)
- 📋 Journal de lancement horodaté avec messages d'erreur en rouge
- 🔍 Vérification des prérequis avant ouverture du navigateur (Symfony)
- 💾 Système de profils par projet (sauvegarde / chargement / suppression)
- 🎛️ Bouton de lancement splitté avec accès aux préréglages
- 🗑️ Nettoyage automatique du `tasks.json` après lancement VSCode

---

## 🔄 En cours / À corriger

- [ ] **Profils** — Quand on change de projet, charger le dernier profil utilisé
- [ ] **Profils** — Renommer un profil existant
- [ ] **Tout arrêter** — Confirmation avant d'arrêter (éviter les clics accidentels)

---

## 🚀 Prochaines fonctionnalités

### 🔧 Priorité haute

- [ ] **Page de paramètres** — Modifier les chemins (`AppSettings`) depuis
      l'interface sans toucher au code :
      - Chemins XAMPP, htdocs, Mercure
      - Chemins des navigateurs
      - Chemins des éditeurs
      - Port Symfony
      
- [ ] **Historique des projets récents** — Afficher les 5 derniers projets
      ouverts en haut de la liste avec accès rapide

### 🎨 Priorité moyenne

- [ ] **Thème clair / sombre** — Toggle dans le header pour basculer entre
      les deux thèmes

- [ ] **Notifications Windows** — Toast natif Windows quand l'environnement
      est prêt (tous les services démarrés)

- [ ] **Indicateur de progression** — Spinner / barre de progression sur le
      bouton "Lancer" pendant le démarrage des services

### ⚙️ Priorité basse

- [ ] **Détection d'autres frameworks** — Laravel, Node.js, React, Vue
      avec services adaptés pour chacun

- [ ] **Icône System Tray** — Accès rapide depuis la barre des tâches
      sans ouvrir la fenêtre principale

- [ ] **Raccourci clavier global** — Ouvrir DevLauncher depuis n'importe
      où avec un raccourci (ex: `Ctrl+Alt+D`)

- [ ] **Lancer au démarrage de Windows** — Option pour démarrer DevLauncher
      automatiquement avec Windows

---

## 💡 Idées futures

- [ ] **Export / Import de profils** — Partager ses configurations entre
      machines ou collègues
- [ ] **Statistiques d'utilisation** — Projets les plus lancés, temps moyen
      de démarrage
- [ ] **Mode silencieux** — Lancer l'environnement sans ouvrir la fenêtre
      principale (depuis le System Tray)
- [ ] **Support Docker** — Détecter et lancer des containers Docker associés
      au projet

---

## 🔢 Ordre suggéré d'implémentation

1. **Page de paramètres** — Prioritaire car évite de modifier le code pour
   chaque changement de configuration
2. **Confirmation "Tout arrêter"** — Rapide à implémenter, évite les erreurs
3. **Historique des projets récents** — Améliore le confort au quotidien
4. **Thème clair / sombre** — Touche finale sur l'interface
5. **Notifications Windows** — Confort supplémentaire
6. **System Tray** — Pour les utilisateurs avancés
