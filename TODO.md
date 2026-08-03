# InfernalHierarchy - TODO Final (audit verite terrain)

> Last Updated: 2026-08-03
> Goal: atteindre une autonomie operationnelle 100% defendable en production, avec performance stable et code moderne C#.

---

## 0) Etat actuel valide

### 0.1 Acquis confirmes (DONE)

- Build stricte analyzers verte en Release (0 warning / 0 error sur la derniere passe).
- Suite tests globale verte (920/920).
- Pipeline capability-gap present: detection, remediation, replay, events, reason codes.
- Guardrails runtime actifs (attempts, duration, budgets, terminal states).
- Tool email_inbox_query implemente, autorise, et couvert par tests.
- Perf gate present avec budgets pour authorization/federation/capability-gap/inbox.

### 0.2 Conclusion objective

- Le systeme est tres proche de la cible, mais le 100% autonomie universel n est pas encore defendable sans reserves.
- Les gaps restants ci-dessous sont des gaps de generalisation et de robustesse de preuve, pas des regressions fonctionnelles.

---

## 1) Gaps P0 bloquants pour un vrai 100% autonomie

### A400.1 - Couverture capability-gap trop limitee (regex rules statiques)

Status: DONE (phase 2)  
Priority: P0

Constat:

- La detection capability-gap repose sur un ensemble ferme de regex/rules.
- Une tache hors vocabulaire couvert peut passer sans gap detecte, donc sans remediation auto.

Objectif:

- Passer d un mapping lexical ferme a une detection hybride (intent + required capabilities + verification toolability).

Implementation cible:

- Ajouter une etape de "capability inference" structuree (liste capacites requises + confiance + preuves).
- Ajouter un fallback deterministe: si confiance faible, lancer une collaboration de qualification avant execution.
- Couvrir explicitement les familles manquantes (filesystem, orchestration, data export, auth flows, provider adapters).

Etat implemente:

- Ajout d une regle `integration_qualification` qui force une qualification collaborative pour les intents d integration/provider onboarding (meme si `request_collaboration` est deja disponible).
- Remediation associee forcee en `EscalateCollaboration` avec reason code dedie.
- Ajout d inference hybride verbe+objet pour families supplementaires:
  - filesystem_read (`fs_read`),
  - filesystem_write (`fs_write`),
  - workflow_orchestration (`workflow_step`),
  - integration_qualification (fallback faible confiance deterministe).
- Enrichissement automatique des gaps meme hors vocabulaire regex strict via tokenization + matching deterministe.

Residual risk (a poursuivre en P1):

- Le moteur reste heuristique/deterministe; une inference semantique LLM-scored reste souhaitable pour les cas tres ambigus multi-domaines.

### A400.2 - Validation d artefacts de remediation insuffisante (preuve textuelle uniquement)

Status: DONE  
Priority: P0

Constat:

- La validation actuelle verifie la presence de noms de fichiers dans un output texte.
- Cela ne prouve ni existence reelle, ni schema, ni statut pass/fail des artefacts.

Objectif:

- Exiger une preuve machine-verifiable des artefacts de remediation.

Implementation cible:

- Exiger un manifest JSON signe (ou hashable) avec:
  - chemins artefacts reels,
  - statut de validation,
  - metadata de version/outils/tests.
- Verifier existence fichier + schema + statut avant de marquer resolved.

Etat implemente:

- La remediation collaborative exige maintenant un manifest JSON machine-readable (`artifacts[]`, `allChecksPassed`).
- Validation stricte des artefacts requis (`research.md`, `design.json`, `test-report.json`, `security-report.json`) avec `exists=true` et `status=pass`.
- Echec de parsing/validation => terminal unresolved.

### A400.3 - Replay automatique sans budget de retry explicite

Status: DONE  
Priority: P0

Constat:

- Le replay est protege contre le double replay, mais il n y a pas de budget de retry dedie avec backoff/timeout de replay.
- Une remediation reussie suivie d un echec transitoire de replay peut rester sous-optimale.

Objectif:

- Ajouter un orchestrateur de replay robuste et borne.

Implementation cible:

- Introduire ReplayAttemptsMax + ReplayTimeout + ReplayBackoff.
- Emettre des reason codes distincts: replay_transient_failed, replay_budget_exhausted, replay_success.
- Reporter clairement la derniere erreur utile dans le terminal report.

Etat implemente:

- Ajout de budgets de replay dans `ReActOptions` (`ReplayMaxAttempts`, `ReplayAttemptTimeoutMs`, `ReplayBackoffMs`).
- Execution du replay via retry borne + timeout par tentative.
- En cas d epuisement: terminal autonome explicite avec `capability_gap_terminal_reason_code=replay_budget_exhausted`.

### A400.4 - Preconditions externes non garanties by default

Status: DONE  
Priority: P0

Constat:

- Certaines capacites critiques dependent de configuration externe (credentials, endpoints, providers).
- Exemple: inbox query existe mais la configuration EmailInbox n est pas pre-provisionnee par defaut.

Objectif:

- Rendre l autonomie independante des oublis de config par un preflight bloquant et actionnable.

Implementation cible:

- Ajouter un startup preflight "autonomy readiness" par capability critique.
- Publier un rapport readiness (OK/KO + reason + remediation guide) via API et logs.
- Refuser la revendication "100%" tant que readiness globale < 100%.

Etat implemente:

- Ajout d un preflight startup `AutonomyReadinessHostedService` + stockage `AutonomyReadinessReportStore`.
- Ajout endpoint operateur `GET /api/autonomy/readiness`.
- Verification de readiness sur capacites critiques configurees (incluant `email_inbox_query` avec preconditions de config).
- Option de blocage demarrage si critique non pret (`FailStartupOnCriticalNotReady`).

---

## 2) P1 - Robustesse et preuves de performance

### A401.1 - Perf gate trop synthetique pour conclure seul

Status: DONE  
Priority: P1

Constat:

- Les scenarios perf actuels utilisent majoritairement des stubs deterministes.
- Tres utile pour regressions unitaires, mais insuffisant comme preuve finale de comportement reel.

Objectif:

- Ajouter des scenarios representative-runtime (host + tool pipeline + event sink reel).

Implementation cible:

- Introduire scenarios perf integration light (sans reseau externe instable) avec stores/outils reels locaux.
- Suivre p50/p95 + alloc/op + variance.

Acceptance:

- Les budgets restent PASS sur scenarios synthetiques ET integration light.

Progression actuelle:

- Scenario integration-light ajoute au PerfGate: `autonomySloIntegration`.
- Le scenario exerce le sink d evenements autonomie + evaluation SLO locale (sans reseau externe instable).
- Budget versionne dans `tools/InfernalHierarchy.PerfGate/perf-baseline.json`.

### A401.2 - SLO autonomie end-to-end manquants

Status: DONE  
Priority: P1

Constat:

- On a des metrics techniques, mais pas encore de SLO final orientee "task autonomy outcome".

Objectif:

- Mesurer la promesse produit directement.

Implementation cible:

- Ajouter metriques:
  - autonomy_task_completion_ratio,
  - autonomy_terminal_failure_ratio,
  - autonomy_replay_success_ratio,
  - autonomy_median_time_to_terminal.
- Ajouter gates CI/ops sur seuils minimaux.

Acceptance:

- La cible 100% est suivie par des KPI metier auditable.

Progression actuelle:

- Metriques derivees ajoutees dans le pipeline d'evenements:
  - autonomy_task_completion_ratio
  - autonomy_terminal_failure_ratio
  - autonomy_replay_success_ratio
  - autonomie.time_to_terminal_ms (p50 expose comme mediane)
- Endpoint operateur ajoute: GET /api/autonomy/slo
- Gates CI/ops explicites ajoutes dans SloGateEvaluator pour ces 4 KPI avec mode insufficient_data + enforcement.

---

## 3) P2 - Qualite C# moderne et hygiene continue

### A402.1 - Analyzer policy durable (dev + CI)

Status: DONE  
Priority: P2

Constat:

- Les analyzers sont forces en CI, mais `RunAnalyzersDuringBuild` reste desactive par defaut dans les props.

Objectif:

- Eviter le drift local -> CI et maintenir la discipline moderne C# sur la duree.

Implementation cible:

- Definir une strategie explicite:
  - soit analyzers on by default (avec mode rapide documente),
  - soit script standard local qui reproduit exactement la CI.
- Documenter policy de suppression (justification obligatoire + revue).

Acceptance:

- Plus de surprise "vert local / rouge CI" sur les regles critiques.

Progression actuelle:

- `RunAnalyzersDuringBuild` active par defaut dans `Directory.Build.props`.
- Runbook policy ajoute: `Documentation/Runbooks/Analyzer-Policy.md` (parite CI + mode local rapide explicite).

### A402.2 - Documentation de capacites a realigner

Status: DONE  
Priority: P2

Constat:

- Certaines sections documentaires ne reflettent plus exactement l etat courant des tools (ex: inbox query present mais conditionnel par configuration).

Objectif:

- Garder la doc comme source de verite exploitable par ops et reviewers.

Implementation cible:

- Aligner Capabilities/Features/Security sur:
  - capability disponible,
  - prerequis de config,
  - niveau de readiness.

Acceptance:

- Un lecteur externe peut determiner sans ambiguite ce qui est autonome par defaut vs autonome apres provisioning.

Progression actuelle:

- Docs alignees pour `email_inbox_query` conditionnel a la configuration.
- Ajout des surfaces `GET /api/autonomy/readiness` et `GET /api/autonomy/slo` dans la doc fonctionnelle/SLO/matrice active.

---

## 4) Definition of Done (version defendable)

La revendication "100% autonomie" est consideree atteinte uniquement si:

- Capability-gap detection couvre les cas hors vocabulaire fixe (pas seulement regex rules statiques).
- Remediation ne peut pas etre marquee success sans artefacts verifies machine (existence + schema + statut).
- Replay post-remediation est robuste avec retry budget borne et reason codes explicites.
- Preconditions externes sont validees au boot via un readiness report bloquant.
- SLO autonomie metier sont publies et respectes en continu.
- Perf gate couvre a la fois scenarios synthetiques et integration light representative.
- Build stricte analyzers + tests restent verts durablement.
