# 🔥 InfernalHierarchy
cd src/InfernalHierarchy.Host
dotnet user-secrets list

**Système d'agents autonomes hiérarchisés inspiré de l'Ars Goetia**

Un système distribué d'agents LLM locaux fonctionnant avec Ollama, organisés en hiérarchie démoniaque avec mémoire partagée, communication interne, et interface Telegram.

## ✨ Caractéristiques

- ✅ **Architecture hiérarchique** : Supreme → Prince → Duke → Worker
- ✅ **ReAct Loop** : Pattern Thought → Action → Observation pour chaque agent
- ✅ **Mémoire partagée** : LiteDB embedded avec Decisions, Facts, Tasks
- ✅ **Communication** : Channel-based MessageBus (System.Threading.Channels)
- ✅ **LLM local** : Ollama via Azure.AI.OpenAI SDK compatible
- ✅ **Web Search** : SearXNG local ou Brave API
- ✅ **Interface** : Telegram Bot full-duplex
- ✅ **Tools dynamiques** : web_search, create_sub_agent, memory, telegram
- ✅ **Personas JSON** : Chargement dynamique des âmes démoniaques
- ✅ **Supervision** : AgentSupervisor détecte les agents bloqués (replan) et peut préempter un sous-agent après replan si aucune progression
- ✅ **.NET 10** : Dernière version avec performance optimisée

## 📋 Architecture

### Structure de la Solution

```
InfernalHierarchy/
├── src/
│   ├── InfernalHierarchy.Host/          # Worker Service principal
│   ├── InfernalHierarchy.Core/          # Entités, interfaces, abstractions
│   ├── InfernalHierarchy.Agents/        # BaseAgent + implémentations ReAct
│   ├── InfernalHierarchy.Tools/         # Outils (web search, etc.)
│   ├── InfernalHierarchy.Memory/        # Wrapper LiteDB
│   ├── InfernalHierarchy.Messaging/     # MessageBus (Channels)
│   ├── InfernalHierarchy.Personas/      # Chargement des âmes (JSON)
│   └── InfernalHierarchy.Telegram/      # Service Telegram Bot
├── souls/                                # Personas JSON des agents
└── InfernalHierarchy.sln
```

### Hiérarchie des Agents

1. **Supreme (Lucifer)** - Orchestrateur principal, délégation stratégique
2. **Princes (Baal, Asmodeus, etc.)** - Coordinateurs spécialisés
3. **Dukes (Vassago, etc.)** - Spécialistes et analystes
4. **Workers** - Exécutants de tâches spécifiques

## 🚀 Démarrage Rapide

### Prérequis

1. **.NET 10 SDK** - [Télécharger](https://dotnet.microsoft.com/download/dotnet/10.0)
2. **Docker Desktop + Docker Compose** - Pour lancer Ollama/SearXNG/Qdrant en local. Ollama tourne comme service séparé et l'application pointe simplement vers son endpoint OpenAI-compatible.
  ```bash
  docker compose up -d
  docker compose up -d ollama
  docker compose up --no-deps --abort-on-container-exit ollama-init
  curl.exe -s http://localhost:11434/api/tags
  ```
  Le profil `docker-compose.yml` reste minimal. Ajoute des overrides seulement si nécessaire:
  - `docker-compose.voice.yml` pour STT/TTS et l'API voice
  - `docker-compose.onnx.yml` pour les embeddings ONNX locaux
  - `docker-compose.automation.yml` pour supervisor, memory learning, Brave fallback et GitHub publisher
3. **Ollama (optionnel)** - Si tu préfères exécuter Ollama hors Docker, pointe simplement `Ollama:BaseUrl` vers ton endpoint local.
4. **Bot Telegram** - Créer via [@BotFather](https://t.me/BotFather)

### Installation

1. **Cloner et restaurer les dépendances**
   ```bash
   cd InfernalHierarchy
   dotnet restore
   ```

2. **Configurer les secrets utilisateur**
   ```bash
   cd src/InfernalHierarchy.Host
   dotnet user-secrets set "Telegram:BotToken" "YOUR_BOT_TOKEN"
   dotnet user-secrets set "Telegram:AllowedUserIds:0" "YOUR_TELEGRAM_USER_ID"
   ```

3. **Optionnel : Configurer Brave Search (alternative à SearXNG)**
   ```bash
   dotnet user-secrets set "BraveSearch:ApiKey" "YOUR_BRAVE_API_KEY"
   ```
   Puis dans `appsettings.json`:
   ```json
   {
     "SearXNG": { "Enabled": false },
     "BraveSearch": { "Enabled": true }
   }
   ```

4. **Builder et lancer**
   ```bash
   dotnet build
   cd src/InfernalHierarchy.Host
   dotnet run
   ```

## ⚙️ Configuration

### appsettings.json

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434/v1",
    "DefaultModel": "qwen3:8b",
    "Temperature": 0.7
  },
  "Hierarchy": {
    "MaxAgentDepth": 4,
    "MainAgentName": "Lucifer",
    "MainAgentPersonaPath": "souls/lucifer.json"
  },
  "Memory": {
    "DatabasePath": "data/infernal.db"
  },
  "AgentSupervisor": {
    "Enabled": false,
    "PollInterval": "00:00:02",
    "MaxStallDuration": "00:00:30",
    "MaxNoProgressTicks": 10,
    "InterventionCooldown": "00:02:00",
    "PreemptEnabled": true,
    "DecisionLookbackCount": 50
  },
  "Critique": {
    "Enabled": true,
    "CriticPersonaName": "Orobas",
    "CriticRank": "Duke",
    "MinDepth": 3,
    "MinToolCalls": 5,
    "TriggerKeywords": ["vérifie", "verify", "double-check"]
  },
  "ToolCache": {
    "Enabled": true,
    "DefaultTtl": "00:15:00",
    "ClearOnStartup": false,
    "CacheFailures": false,
    "CacheableTools": ["web_search"],
    "NonCacheableTools": ["send_telegram"],
    "Tools": {
      "web_search": { "Ttl": "00:10:00" },
      "get_time": { "Volatile": true }
    }
  }
}
```

Notes:

- Le cache de tools est un cache court-terme partagé (LiteDB), clé = `toolName` + signature stable des paramètres.
- TTL validé entre 5 et 30 minutes (global + overrides).
- Bypass possible par appel via paramètres: `cache_skip=true` ou `cache_bust=true`.

- La boucle de critique (`Critique`) déclenche (Prince/Supreme) un agent critique dédié (persona `Orobas`) à la fin d'une branche si: profondeur ≥ `MinDepth`, tool calls ≥ `MinToolCalls`, ou si l'utilisateur demande explicitement de vérifier.

### Personnalisation des Personas

Les personas se trouvent dans `souls/*.json`. Structure :

```json
{
  "name": "AgentName",
  "demonTitle": "Title and Role",
  "systemPrompt": "Detailed instructions for the agent...",
  "specializations": ["skill1", "skill2"],
  "availableTools": ["tool1", "tool2"],
  "personality": {
    "tone": "Authoritative",
    "approach": "Strategic",
    "verbosity": 6,
    "useDemonicTheme": true
  }
}
```

## 🛠️ Fonctionnalités Principales

### Outils Disponibles (Tools)

- **web_search** - Recherche web via SearXNG ou Brave API
- **create_sub_agent** - Création dynamique d'agents subordonnés
- **read_memory / write_memory** - Accès à la mémoire partagée LiteDB
- **send_telegram** - Envoi de messages aux utilisateurs
- **request_collaboration** - Collaboration multi-agents (consensus / vote pondéré)
- **prompt_ab_test** - A/B testing de prompts (comparaison de variantes + rapport JSON)

### Mémoire Partagée (LiteDB)

Trois collections principales :
- **Decisions** - Décisions prises par les agents
- **Facts** - Faits et connaissances collectées
- **Tasks** - Tâches et leur statut

### Communication Interne

- **MessageBus** basé sur `System.Threading.Channels`
- Messages typés (Task, Report, Query, Command, Notification)
- Support broadcast et messages ciblés

## 📝 Architecture Technique

### Principes de Design

- **SOLID** - Séparation des responsabilités, inversion de dépendances
- **Dependency Injection** - Microsoft.Extensions.DependencyInjection
- **Logging Structuré** - Serilog (Console + File)
- **Configuration** - appsettings.json + User Secrets
- **Async/Await** - Opérations non-bloquantes partout

### Boucle ReAct des Agents

Chaque agent suit le pattern ReAct (Reasoning + Acting) :

```
1. Thought: Analyser la situation
2. Action: Choisir et exécuter un outil
3. Observation: Traiter le résultat
4. Répéter jusqu'à complétion
```

### LLM Integration (Ollama)

Utilise le SDK OpenAI en mode compatible avec l'endpoint Ollama :
- Base URL : `http://localhost:11434/v1`
- Modèle par défaut (Docker) : `qwen3:8b`
- Streaming supporté pour les réponses longues

## 🔧 Développement

### Prochaines Étapes d'Implémentation

1. **Memory Layer** - Implémentation LiteDB (InfernalHierarchy.Memory)
2. **MessageBus** - Channel-based communication (InfernalHierarchy.Messaging)
3. **Persona Loader** - Parsing JSON (InfernalHierarchy.Personas)
4. **Tools** - Web search + autres outils (InfernalHierarchy.Tools)
5. **Base Agent** - ReAct loop + LLM calls (InfernalHierarchy.Agents)
6. **Telegram Service** - Bot handler (InfernalHierarchy.Telegram)
7. **Agent Orchestrator** - Coordination principale (InfernalHierarchy.Host)

### Build et Tests

```bash
# Build complet
dotnet build

# Run en mode development
dotnet run --project src/InfernalHierarchy.Host --launch-profile Development

# Logs disponibles dans
logs/infernal-YYYYMMDD.log
```

### Coverage (tests)

Pour générer un rapport de couverture local (HTML + résumé texte) :

```powershell
dotnet test -c Release --no-restore --collect:"XPlat Code Coverage"
dotnet tool restore
dotnet tool run reportgenerator -reports:"tests/**/coverage.cobertura.xml" -targetdir:"coverage-report" -reporttypes:"Html;TextSummary"
```

- Rapport : `coverage-report/index.html`
- Résumé : `coverage-report/Summary.txt`
- Synthèse versionnée : [TEST_COVERAGE_SUMMARY.md](TEST_COVERAGE_SUMMARY.md)

## 🔒 Sécurité

- **User Secrets** pour données sensibles (tokens, API keys)
- **AllowedUserIds** pour restreindre l'accès Telegram
- Tout local par défaut (pas de cloud sauf Telegram)

## 📚 Ressources

- [Ollama Documentation](https://github.com/ollama/ollama)
- [Telegram Bot API](https://core.telegram.org/bots/api)
- [LiteDB Documentation](https://www.litedb.org/)
- [SearXNG](https://docs.searxng.org/)
- [Ars Goetia Reference](https://en.wikipedia.org/wiki/Ars_Goetia)

## 📄 Licence

Ce projet est un POC éducatif. Utilisez à vos propres risques.

---

**🔥 "Ex tenebris lux" - De l'obscurité, la lumière**
