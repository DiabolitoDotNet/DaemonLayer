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
- ✅ **Skill Catalog JSON** : Packs de compétences réutilisables assignés par politique
- ✅ **Supervision** : AgentSupervisor détecte les agents bloqués (replan) et peut préempter un sous-agent après replan si aucune progression
- ✅ **Incident response autonome** : mitigation automatique des spikes timeout/rejets queue avec replan, préemption contrôlée et réduction temporaire du débit tool
- ✅ **Collaboration fédérée** : collecte et agrégation cross-instance des réponses avec provenance des instances distantes
- ✅ **Agrégation fédérée cohérente par stratégie** : Voting/WeightedVoting/Consensus/HighestConfidence/Hierarchical avec fallback structuré si participation insuffisante
- ✅ **Saga compensation autonome** : retries bornés de compensation + escalade superviseur structurée si épuisement
- ✅ **Profils d'exécution alignés runtime** : outils Build/Deploy critiques activés par défaut + diagnostic de dérive profil/permissions au démarrage/reload
- ✅ **Santé fédération fiable** : un heartbeat échoué ne maintient plus une instance en état healthy
- ✅ **Adjudication autonome exécutable** : les conflits collaboration/fédération non résolus sont tranchés automatiquement (plus de fin action-token-only)
- ✅ **Custom tools sans gate humain bloquant** : création/chargement ne s'arrête plus sur une approbation manuelle requise
- ✅ **Perf gate avec headroom mesuré** : `federationAggregation` 0.156ms/op, 31877B/op (budget 35000B)
- ✅ **.NET 10** : Dernière version avec performance optimisée

## ✅ Delivery Snapshot (Aug 2026)

- Strict autonomy runtime blockers are closed.
- Structured terminal autonomy outcome contract is implemented and test-covered (`autonomy_outcome_*`).
- Production readiness blocking and certification profile are available for autonomy-critical flows.
- Perf gate includes autonomy-focused evidence (`readinessScale`, `autonomyScorecardReport`, `capabilityGapRemediationConcurrent`, `autonomySoakStability`).

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
├── skills/                               # Skill packs JSON (catalogue réutilisable)
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
2. **Docker Desktop + Docker Compose** - Pour lancer SearXNG/Qdrant en local (Ollama est deja installe et lance en local sur la machine hote).
  ```bash
  curl.exe -s http://localhost:11434/api/tags
  powershell -ExecutionPolicy Bypass -File .\scripts\setup-local-docker.ps1
  ```
  Le profil local recommande est `docker-compose.local.yml` (ports bindes en localhost + optimisation poste local).
  Tu peux aussi lancer manuellement:
  ```bash
  docker compose -f docker-compose.local.yml up -d --build
  ```
  Ajoute des overrides seulement si nécessaire:
  - `docker-compose.voice.yml` pour STT/TTS et l'API voice
  - `docker-compose.onnx.yml` pour les embeddings ONNX locaux
  - `docker-compose.automation.yml` pour supervisor, memory learning, Brave fallback et GitHub publisher
3. **Ollama local (requis pour cette config)** - endpoint attendu: `http://localhost:11434`.
   - Depuis le conteneur, l'endpoint hote est `http://host.docker.internal:11434/v1`.
   - Depuis ta machine (hors Docker), utilise `http://localhost:11434`.
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

### Skill Catalog et Gouvernance d'Assignation

Les skill packs se trouvent dans `skills/*.json`.

Modèle de gouvernance par défaut:

- **Manager (Lucifer)** assigne la base persona + skill packs initiaux selon le rang.
- **Agent** peut demander des skill packs temporaires pour une tâche.
- **Policy service** approuve/refuse/escalade selon le rang et le niveau de risque.
- **Autonomie complète**: les escalades de skills peuvent être auto-approuvées par l'agent principal (`lucifer`) sans validation humaine.

Configuration associée dans `appsettings.json`:

```json
{
  "SkillsCatalog": {
    "DirectoryPath": "skills"
  },
  "AgentSkillAssignment": {
    "Enabled": true,
    "AutoAssignBaseSkills": true,
    "AutoApproveEscalationsByMainAgent": true,
    "MainAgentId": "lucifer",
    "AllowSelfServiceSkillRequests": true,
    "EscalateRiskLevelAtOrAbove": "High"
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
- **request_skill_pack** - Demande d'activation temporaire d'un skill pack (policy: approve / deny / escalate)
- **graphql_request** - Requêtes GraphQL en mode sécurisé (allowlist hôtes + guardrails read-only)
- **sql_query_readonly** - Requêtes SQL read-only avec garde-fous stricts (single statement + mots-clés interdits)
- **custom_tool_list / custom_tool_delete** - Gestion des custom tools persistés (inventaire / suppression + unregister runtime)
- **vision_describe** - Analyse d'images locales avec modèle multimodal (garde-fous chemin/extensions/taille)
- **prompt_ab_test** - A/B testing de prompts (comparaison de variantes + rapport JSON)

### Voice Sidecar + UI Opérateur

- Mode sidecar optionnel pour `audio_transcribe` et `tts_speak` (délégation HTTP configurable, fallback local conservé).
- Dashboard étendu avec pages `/ui/timeline` (raisonnement + outils) et `/ui/playground` (scénarios/replay agents).

### Plugin SDK

- Starter SDK pour contributeurs tiers: `templates/plugin-sdk`
- Guide d'intégration: `Documentation/Plugin-SDK.md`

### Mémoire Partagée (LiteDB)

Trois collections principales :
- **Decisions** - Décisions prises par les agents
- **Facts** - Faits et connaissances collectées
- **Tasks** - Tâches et leur statut

### Communication Interne

- **MessageBus** basé sur `System.Threading.Channels`
- Messages typés (Task, Report, Query, Command, Notification)
- Support broadcast fan-out (chaque abonné actif reçoit chaque broadcast une fois)
- Files bornées configurables + politique d'overflow (`Block`, `DropOldest`, `Reject`)
- Métriques exposées: profondeur des files ciblées/broadcast et compteurs drop/reject

Exemple de configuration:

```json
{
  "MessageBus": {
    "QueueCapacity": 1000,
    "OverflowPolicy": "Block"
  }
}
```

### Limites d'exécution Runtime

- Les exécutions d'outils passent par une limite de concurrence + budget de timeout appliqués par `ResourceLimitService`.
- En cas de dépassement, la réponse d'outil est explicite (`resource_limit_timeout=true`) et journalisée.
- Les tests de charge valident l'absence de croissance non bornée de la concurrence d'exécution.

### Résilience I/O externe

- Les appels Ollama, recherche web (SearXNG/Brave) et `http_request` appliquent une stratégie retry/backoff pour erreurs transitoires.
- Les envois email passent par un décorateur résilient (`ResilientEmailSender`) avec retry policy.
- Les erreurs permanentes (ex: requêtes HTTP 4xx non transitoires, erreurs d'argument) ne sont pas retraitées inutilement.

### Dead-letter et replay sécurisé

- Les échecs de publication de messages (ex: overflow policy `Reject`) et les échecs d'exécution d'outils sont persistés dans un store dead-letter avec `reason_code`.
- Chaque entrée dead-letter inclut un budget de replay (`RetryBudget`) et un compteur d'essais pour éviter les boucles infinies de relecture.
- API opérateur:
  - `GET /api/ops/deadletters` pour consulter les entrées et stats.
  - `POST /api/ops/deadletters/{id}/replay` pour rejouer de manière contrôlée.
- Métriques exposées:
  - compteurs `deadletter.created.*`, `deadletter.replay.attempt`, `deadletter.replay.succeeded`, `deadletter.replay.failed.*`
  - gauges `deadletter.total`, `deadletter.pending`, `deadletter.replayed`, `deadletter.replay_failed`

### Incident response autonome (P0.3)

- Le service `AutonomousIncidentResponseService` surveille en continu les signaux critiques:
  - spike des timeouts d'outils (`tools.timeout.total`),
  - croissance des rejets de queue (`message_bus.messages.rejected`),
  - détection de branches bloquées/bouclées (`supervisor.detected.*`).
- Actions de mitigation automatiques:
  - demande de replan au root agent,
  - préemption d'une branche non-root en cas de boucle persistante,
  - réduction temporaire du débit d'exécution des tools à risque via un throttle incident.
- Auditabilité:
  - événements `DecisionMade` catégorie `incident.response` avec `reason_code`, action et cible,
  - métriques `incident_response.actions.*` pour pilotage opérateur.

Exemple de configuration:

```json
{
  "AutonomousIncidentResponse": {
    "Enabled": true,
    "PollInterval": "00:00:10",
    "ActionCooldown": "00:00:30",
    "ToolTimeoutSpikeThreshold": 3,
    "QueueRejectGrowthThreshold": 5,
    "StalledBranchDetectionThreshold": 2,
    "LoopingBranchDetectionThreshold": 2,
    "EnableBranchPreemption": true,
    "EnableTemporaryRateReduction": true,
    "RateReductionDuration": "00:01:00",
    "DeferredToolNames": ["request_collaboration", "create_sub_agent", "send_agent_message"]
  }
}
```

### Pipeline de synthèse capacité/tool (P1.2)

- `create_custom_tool` suit une chaîne standardisée:
  - synthèse (LLM ou template),
  - policy scan sécurité,
  - compilation,
  - persistance,
  - enregistrement runtime.
- En cas d'échec de compilation pendant un overwrite, un rollback automatique restaure la définition précédente persistée.

### Runtime skills persistants et skillbook (P1.3)

- Les grants runtime de skills sont persistés en LiteDB (`agent-skill-runtime.db`) et survivent aux redémarrages.
- Les outcomes de capacités sont consolidés dans un skillbook versionné (`skills/runtime/*.json`) avec provenance:
  - `source_task`,
  - `risk_level`,
  - `success_count`,
  - `last_validated_date`.
- La promotion automatique est pilotée par `SkillbookPublishing:PromotionMinSuccessCount` pour éviter le bruit.

### Collaboration renforcée (P1)

- Les sessions de collaboration sont persistées dans la mémoire partagée avec un `collaboration_id` (démarrage, réponses, résultat final).
- Le protocole de conflit est explicite dans les résultats: `conflict_class`, `conflict_reason_code`, `next_action`, `needs_supervisor_intervention`.
- Les templates de collaboration supportés par `request_collaboration`:
  - `parallel_research_adjudicate`
  - `debate_then_synthesize`
  - `hierarchical_risk_review`
- Références templates versionnées: `templates/collaboration/`.

### Checkpoints ReAct (plan/exécution/vérification)

- Les branches ReAct émettent des checkpoints sémantiques (`plan`, `execution`, `verification`).
- Les checkpoints sont persistés dans la mémoire partagée (`category=react.checkpoint`) avec:
  - `branch_id` (id du message de tâche)
  - `collaboration_id` (si présent)
- Cela permet au superviseur de distinguer une progression réelle d'une boucle stérile.

### Corrélation et causalité

- Les messages internes portent désormais des champs explicites `CorrelationId` et `CausationId`.
- Les entrées HTTP et WebSocket créent ou propagent un `X-Correlation-Id` stable.
- Les réponses agents et projections internes conservent la chaîne de causalité via l'enveloppe `AgentMessage`.

### Métriques P2 supplémentaires

- Santé des files du bus: `message_bus.queue.*`, `message_bus.channels.active`, `message_bus.messages.dropped`, `message_bus.messages.rejected`.
- Supervision: `supervisor.interventions.*`, `supervisor.detected.stalled`, `supervisor.detected.looping`.
- Timeouts d'outils: `tools.timeout.total`.

### Readiness exploitable

- `/health/ready` expose désormais un résumé actionnable avec `failingDependencies` et `hint` par dépendance dégradée ou indisponible.

### P2 documentation opérationnelle

- Matrice active des fonctionnalités: `Documentation/Active-Feature-Matrix.md`
- SLOs: `Documentation/SLOs.md`
- Alert playbooks: `Documentation/Alert-Playbooks.md`

### CI/CD - Fast lane et Full lane

- Workflow GitHub Actions: `.github/workflows/ci.yml`
- **Fast lane**: restore, build, build avec analyzers, tests ciblés (`Core`, `Messaging`, `Tools`).
- **Full lane**: build + suite complète `dotnet test InfernalHierarchy.sln -c Release`.
- En cas d'échec, publication automatique des artefacts (`*.trx`, `TestResults`, sorties build Release) pour diagnostic.

### Release workflow + smoke container

- Workflow GitHub Actions: `.github/workflows/release.yml`
- Déclenchement: `push` sur tags `v*` et `workflow_dispatch`.
- Étapes clés:
  - build image Docker
  - démarrage conteneur smoke
  - vérification `/health/ready`
  - smoke `/api/chat` (200 ou timeout contrôlé 504)
  - upload artefacts en cas d'échec + logs conteneur

### Commande locale qualité

- Script standard: `scripts/quality.ps1`
- Chaîne exécutée: restore -> build release -> analyzers -> fast tests -> full tests.
- Option: `-SkipFullTests` pour boucle locale plus rapide.

### Statut GraphQL

- Décision P1: GraphQL est classé archive/expérimental et hors surface runtime supportée.
- ADR associée: `Documentation/ADRs/0007-graphql-surface-status.md`.

### Auth des endpoints opérationnels

- En mode `LocalOnly=true`, les endpoints opérationnels restent accessibles uniquement en loopback.
- En mode `LocalOnly=false`, les endpoints opérationnels (`/api/chat`, `/api/tools`, `/api/events`, `/metrics`, `/ws`) exigent la clé d'opérateur via header `X-Infernal-Operator-Key`.

```json
{
  "OperatorApi": {
    "ApiKey": "change-me"
  }
}
```

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

### État Actuel et Références

Les composants principaux sont déjà implémentés et validés par les tests:

- `InfernalHierarchy.Memory` : mémoire LiteDB + cache de tools
- `InfernalHierarchy.Messaging` : bus inter-agents basé sur `System.Threading.Channels`
- `InfernalHierarchy.Personas` : chargement de personas JSON
- `InfernalHierarchy.Tools` : pipeline d'exécution, authorization, rate limiting, cache, tools dynamiques
- `InfernalHierarchy.Agents` : boucle ReAct, collaboration, critique, factory/orchestration
- `InfernalHierarchy.Telegram` : interface Telegram
- `InfernalHierarchy.Host` : composition root, observabilité, endpoints HTTP/UI/voice

Pour le backlog réel et les écarts encore ouverts:

- Voir [TODO.md](TODO.md)
- Voir [NEXT_STEPS.md](NEXT_STEPS.md)
- Voir [Documentation/README.md](Documentation/README.md)

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
