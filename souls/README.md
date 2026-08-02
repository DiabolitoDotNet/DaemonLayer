# Persona Catalog

This folder is the base persona library used to create agents.

## Purpose

- Keep role identity and behavioral constraints separate from task-specific skill overlays.
- Provide reusable persona definitions for new IT projects.
- Work with the skill catalog in skills/ for runtime capability composition.

## Recommended Persona Responsibilities

- Supreme: orchestration, escalation, quality gate ownership.
- Prince: team coordination and cross-branch synthesis.
- Duke: specialist implementation and verification.
- Worker: focused execution under explicit scope.

## Governance

- Base persona is assigned at creation time by manager policy.
- Skill packs are layered on top via policy-driven assignment.
- Audit assignment decisions through events and memory traces.
