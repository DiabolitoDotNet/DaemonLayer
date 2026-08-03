# InfernalHierarchy - TODO de Cloture Autonomie 100%

> Derniere mise a jour: 2026-08-03
> Objectif: atteindre et maintenir une autonomie agentique defendable a 100% sur les taches in-scope, avec un code optimise et aligne C# moderne.

---

## 1) Resultat de l'analyse globale

### 1.1 Conclusion executive

La solution est deja tres avancee et les garde-fous qualite/performance sont solides.
Aucun blocker logiciel P0 n'a ete confirme pour l'autonomie in-scope en CI stricte.

La marge restante pour revendiquer durablement le "100%" porte surtout sur:

- l'homogeneite du contrat d'outcome autonomie entre toutes les surfaces API,
- l'alignement du workflow Release avec les memes preuves strictes que la CI complete,
- le renforcement des preuves "real-path" (pas seulement scenarios synthetiques) dans PerfGate,
- la couverture E2E de certains endpoints autonomie critiques pour audit operateur.

### 1.2 Bornes de la revendication

La revendication techniquement defendable reste:

- 100% autonomie sur taches in-scope certifiees,
- sous prerequis runtime certifies,
- avec classification explicite out-of-scope/policy-blocked.

---

## 2) Ecarts confirmes a traiter

## P1 - Gap majeur 1: Contrat autonomie non harmonise sur la route HTTP principale

Constate:

- Le contrat enrichi `autonomy_outcome_*` est applique cote playground.
- La route [src/InfernalHierarchy.Host/Api/ChatApi.cs](src/InfernalHierarchy.Host/Api/ChatApi.cs) retourne encore le payload brut de l'agent (ou un ProblemDetails timeout) sans enrichissement contractuel force.

Impact:

- Risque d'ecart entre preuves certification/playground et usage operateur direct `/api/chat`.
- Audit autonomie moins robuste sur le chemin principal.

Actions:

- A1111.1: appliquer `AutonomyOutcomeContractEvaluator.EnrichAutonomyOutcomePayload(...)` sur toutes les reponses `200` de `/api/chat`.
- A1111.2: remplacer les timeouts `/api/chat` par un payload contractuel normalise (equivalent `BuildTimeoutOutcomePayload()`).
- A1111.3: garantir les champs obligatoires meme si le payload agent est partiel ou absent.

Tests requis:

- A1111.T1: E2E `/api/chat` succes -> presence de tous les `autonomy_outcome_*`.
- A1111.T2: E2E `/api/chat` timeout -> statut timeout contractuel, `next_action=none`, pas d'escalade superviseur implicite.

---

## P1 - Gap majeur 2: Workflow Release moins strict que CI full-lane

Constate:

- [ .github/workflows/ci.yml ](.github/workflows/ci.yml) execute des gates autonomie stricts.
- [ .github/workflows/release.yml ](.github/workflows/release.yml) accepte encore un smoke `/api/chat` en `200` ou `504`, et readiness `200` ou `503`.

Impact:

- Une release peut passer sans prouver la meme qualite d'autonomie in-scope que la CI stricte.

Actions:

- A1120.1: rendre la release dependante d'une preuve stricte autonomie (artefact/status certifie).
- A1120.2: durcir le smoke release avec verifications explicites des endpoints autonomie (readiness/slo/manifest selon profil).
- A1120.3: publier un resume d'evidence autonomie dans les artefacts release (ratios in-scope, terminal failure, p95/p99).

Tests/validation:

- A1120.T1: check workflow qui echoue si evidence stricte absente ou invalide.
- A1120.T2: simulation release dry-run avec gate autonomie force a fail pour valider le fail-closed.

---

## P2 - Gap amelioration 1: PerfGate encore partiellement synthetique

Constate:

- PerfGate couvre bien plusieurs scenarios autonomie.
- Certaines mesures reposent sur objets/flux simules, donc risque de masquer regressions real-path Host/API.

Impact:

- Optimisation validee localement mais confiance plus faible sur latence/allocation des parcours operateurs reels.

Actions:

- A1130.1: ajouter au moins un scenario PerfGate "real-path" traversant un flux Host/API/MessageBus deterministic.
- A1130.2: ajouter un scenario degrade-dependency en chemin reel (fallback/refus borne contractuel).
- A1130.3: interdire via test de suite la disparition de tous les scenarios real-path autonomie.

Validation:

- A1130.T1: budgets dedies real-path dans `perf-baseline.json`.
- A1130.T2: comparaison de tendance release-over-release maintenue pour ces scenarios.

---

## P2 - Gap amelioration 2: Couverture E2E inegale des endpoints autonomie operateur

Constate:

- Endpoint SLO couvert en E2E.
- Couverture specifique moins visible pour `/api/autonomy/readiness` et `/api/autonomy/certification-manifest`.

Impact:

- Risque de drift silencieux sur les contrats d'audit operateur.

Actions:

- A1140.1: ajouter E2E shape+auth pour `/api/autonomy/readiness`.
- A1140.2: ajouter E2E shape+auth pour `/api/autonomy/certification-manifest`.
- A1140.3: ajouter test de non-regression contrat (champs requis) pour ces endpoints.

---

## 3) Axe optimisation code et C# moderne (durabilite)

Ces points ne sont pas des blockers immediats, mais garantissent la tenue dans le temps.

### 3.1 Ratchet qualite continue

- A1150.1: etendre progressivement `NonCriticalWarningsAsErrorsPhase1` par vagues a faible risque.
- A1150.2: conserver l'inventaire suppressions + budget en baisse monotone release apres release.

### 3.2 Optimisation hot paths autonomie

- A1160.1: prioriser les optimisations allocation sur chemins critiques (`/api/chat`, aggregation federation, scorecard).
- A1160.2: exiger evidence perf associee pour toute modif touchant orchestration/autonomie.

### 3.3 Hygiene C# moderne

- A1170.1: garder `Nullable` et analyzers actifs partout (deja en place).
- A1170.2: verifier a chaque PR l'absence de regressions sur CA2016/ressources asynchrones.
- A1170.3: preferer patterns clairs et allocation-aware sur hot paths (Span/pooled patterns uniquement quand justifies par evidence).

---

## 4) Priorisation execution (ordre recommande)

1. A1111.1
2. A1111.2
3. A1111.T1
4. A1111.T2
5. A1120.1
6. A1120.2
7. A1120.T1
8. A1140.1
9. A1140.2
10. A1130.1
11. A1130.2
12. A1130.T1
13. A1150.1
14. A1160.1

---

## 5) Definition de done finale (autonomie objective)

Objectif considere atteint uniquement si toutes les conditions suivantes restent vraies:

- Toutes les surfaces de reponse operateur critiques exposent un contrat `autonomy_outcome_*` homogene et complet.
- Le workflow Release applique le meme niveau d'exigence autonomie que la CI stricte (pas de chemin permissif implicite).
- Les scenarios perf autonomie incluent du real-path et restent dans les budgets absolus + enveloppes de derive.
- Les endpoints autonomie operateur (readiness/slo/manifest) sont couverts E2E et verrouilles contractuellement.
- Build strict analyzers, tests complets et PerfGate restent verts de maniere stable.

---

## 6) Suivi de statut

- A1111.1: DONE
- A1111.2: DONE
- A1111.3: DONE
- A1111.T1: DONE
- A1111.T2: DONE
- A1120.1: DONE
- A1120.2: DONE
- A1120.3: DONE
- A1120.T1: DONE
- A1120.T2: DONE
- A1130.1: DONE
- A1130.2: DONE
- A1130.3: DONE
- A1130.T1: DONE
- A1130.T2: DONE
- A1140.1: DONE
- A1140.2: DONE
- A1140.3: DONE
- A1150.1: DONE
- A1150.2: DONE
- A1160.1: DONE
- A1160.2: DONE
- A1170.1: DONE
- A1170.2: DONE
- A1170.3: DONE
