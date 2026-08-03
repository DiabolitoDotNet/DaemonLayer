# InfernalHierarchy - TODO Final (objectif 100% autonomie)

> Last Updated: 2026-08-03
> Goal: atteindre une autonomie operationnelle 100% (pas de re-demande utilisateur pour terminer la tache), avec garde-fous cout/securite, observabilite complete, et performance stable.

---

## 0) Bilan objectif de la passe finale

### 0.1 Acquis verifies (DONE)

- Capability-gap detection + structured report/plan en place.
- Sensitive input guard actif (secret reference requise).
- Tool inbox read-only email_inbox_query implemente, autorise et teste.
- Telemetry capability-gap exposee (detected/attempt/success/duration).
- Perf gate capabilityGapPlanning actif et vert.
- Build Release solution verte, tests verts.

### 0.2 Gaps restants qui bloquent le vrai 100%

- Aucun gap bloquant restant identifie sur les parcours cibles de l audit final.

---

## 1) P0 - Cloture autonomie fonctionnelle

### A300.1 - Replay automatique deterministe du message initial

Status: DONE  
Priority: P0

Objectif:

- Quand remediation est succes, re-executer automatiquement la tache originale sans nouvelle action utilisateur.

Implementation cible:

- Ajouter un runner de replay base sur original_intent + correlation id.
- Appliquer idempotence guard (anti double replay).
- Ajouter retry budget local (tentatives + timeout) pour replay.

Acceptance:

- Aucun message utilisateur supplementaire requis apres remediation succes.
- Retour terminal explicite si replay echoue apres budget.

Validation:

- E2E: gap -> remediation -> replay -> report final success.
- E2E: gap -> remediation success mais replay fail -> terminal explicite.

### A300.2 - Validation stricte des artefacts de remediation

Status: DONE  
Priority: P0

Objectif:

- Ne pas marquer un gap "resolved" sans preuves d execution des etapes critiques.

Implementation cible:

- Definir un contrat artefact minimal par capability:
  - research.md
  - design.json
  - test-report.json
  - security-report.json
- Valider presence + schema minimal + statut pass/fail.
- Basculer en unresolved_terminal si artefacts invalides/incomplets.

Acceptance:

- workflow_state=capability_gap_resolved_retrying_original_intent seulement si artefacts valides.

Validation:

- Tests unitaires sur validateur d artefacts.
- Test integration remediation sans artefact -> unresolved_terminal.

### A300.3 - Enforcement runtime des budgets de plan

Status: DONE  
Priority: P0

Objectif:

- Limiter blast radius et garantir terminaison autonome sous contrainte.

Implementation cible:

- Appliquer MaxAttempts et MaxDurationSeconds pendant ExecuteAsync.
- Arret deterministic avec reason_code explicite (budget_exhausted / duration_exhausted).
- Tracer les depassements dans events + metrics.

Acceptance:

- Aucune remediation ne depasse ses limites configurees.

Validation:

- Tests integration avec budget bas forcent une terminaison propre.

---

## 2) P1 - Observabilite et gouvernance exploitables

### A301.1 - Vue workflow-first par gap_workflow_id

Status: DONE  
Priority: P1

Objectif:

- Permettre un audit operateur simple d un cas capability-gap du debut a la fin.

Implementation cible:

- Ajouter endpoint timeline filtre par gap_workflow_id.
- Inclure etapes: detection -> planning -> remediation actions -> replay -> terminal state.
- Ajouter resume compact: status final, duree, action dominante, reason_code final.

Acceptance:

- Un seul id permet de reconstruire 100% de la chronologie utile.

Validation:

- Test API de timeline ciblee + verification payload.

### A301.2 - Guardrails multi-dimension (cout/agent/time)

Status: DONE  
Priority: P1

Objectif:

- Eviter derive cout et explosion de branches pendant remediation.

Implementation cible:

- Caps sur nombre de sub-agents, tours de collaboration, et tool calls remediation.
- Coupe-circuit quand seuil depasse, avec retour autonome terminal.
- Telemetry dediee: guardrail_triggered_total par reason_code.

Acceptance:

- Les workflows hors budget se terminent proprement et predictiblement.

Validation:

- Tests de surcharge simulant derive et verifiant la coupure.

---

## 3) P1 - Couverture tests de fermeture

### A302.1 - Suite capability-gap de bout en bout

Status: DONE  
Priority: P1

Objectif:

- Couvrir les chemins qui conditionnent l autonomie reelle.

Scenarios minimum:

- nominal: gap detecte -> remediation valide -> replay auto -> success.
- policy blocked: gap detecte -> blocage securite/policy -> terminal explicite.
- remediation failed: echec action/tool -> unresolved_terminal.
- budget exhausted: coupe-circuit propre.

Acceptance:

- Les 4 scenarios passent en CI.

### A302.2 - E2E Telegram mailbox nominal

Status: DONE  
Priority: P1

Objectif:

- Verifier le parcours reel cote transport Telegram (pas uniquement HTTP chat API).

Acceptance:

- Requete Telegram "mail from X" produit une reponse terminale autonome et tracable.

---

## 4) P2 - Optimisation et pratiques C# modernes

### A303.1 - Gate qualite C# en build CI

Status: DONE  
Priority: P2

Constat:

- Les analyzers sont configures mais desactives pendant le build (RunAnalyzersDuringBuild=false).

Action:

- Activer analyzers en CI (au moins sur src) avec baseline progressive.
- Promouvoir un sous-ensemble critique en warning-as-error (nullable/perf/securite).

Etat actuel:

- CI build lance deja `RunAnalyzersDuringBuild=true`.
- CI build promeut explicitement un sous-ensemble critique en erreurs (`CS8600`, `CS8602`, `CS8603`, `CS8604`, `CA2000`, `CA2016`, `CA2100`).

### A303.2 - Micro-optimisations hot path remediation/inbox

Status: DONE  
Priority: P2

Action:

- Mesurer allocations sur chemins remediation et inbox query.
- Eviter allocations evitables (materialisations inutiles, parsing repetitif, logging boxe).
- Completer par benchmarks cibles si regression suspectee.

---

## 5) Definition of Done 100% autonomie

Le niveau cible est atteint si:

- Toute tache capability-gap remediable va jusqu au report final sans relance utilisateur.
- Toute tache non remediable termine de facon autonome explicite et auditable.
- Les budgets runtime sont appliques et prouves par tests.
- Le workflow est observable de bout en bout par gap_workflow_id.
- Les scenarios E2E critiques (dont Telegram mailbox) sont verts en CI.
- Perf gate reste PASS et la build Release reste propre.
