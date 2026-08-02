# Skill Catalog

This folder contains reusable skill packs that can be assigned to agents.

## Purpose

- Provide a project-agnostic baseline library for IT tasks.
- Keep persona identity stable while allowing capability overlays.
- Support manager-assigned baseline skills and policy-gated temporary requests.

## File Schema (JSON)

Each skill pack file should define:

- id
- name
- version
- description
- riskLevel: Low | Medium | High | Critical
- enabled
- priority
- tags
- allowedRanks
- additionalTools
- additionalSpecializations
- promptFragments
- customInstructions

## Governance Model

- Manager/Lucifer assigns baseline packs at agent creation time.
- Agents may request temporary packs per task.
- Policy service approves, denies, or escalates requests based on rank/risk.
