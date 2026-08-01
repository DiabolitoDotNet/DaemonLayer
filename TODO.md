# InfernalHierarchy – TODO (Pending Work Only)

> **Last Updated:** August 1, 2026
> **Scope:** This file lists only work that is still open, still relevant, or still under-documented.
> **Completed items moved to:** `COMPLETED.md`

---

## 📋 Table of Contents
1. [Now (Next Sprint)](#-now---next-sprint)
2. [Active Gaps / Known Limitations](#-active-gaps--known-limitations)
3. [Operational Runbooks](#-operational-runbooks)
4. [Should Have (Production Readiness)](#-should-have---production-readiness)
5. [Could Have (Future Enhancements)](#-could-have---future-enhancements)

---

## 🔴 NOW - Next Sprint

No immediate next-sprint items are currently tracked here.

Promote the next highest-priority work from the sections below when needed.

---

## 🔧 Active Gaps / Known Limitations

No additional active gaps are currently tracked here.

---

## 🧰 Operational Runbooks

No additional operational runbooks are currently required.

---

## 🎯 SHOULD HAVE - Production Readiness

No additional `Should Have` items are currently open.

---

## 💡 COULD HAVE - Future Enhancements

### Tool Ecosystem
- [ ] **API integration tools**
  - GraphQL-first client + auth helpers (REST is already covered by `http_request`).

- [ ] **Database query tools**
  - Read-only SQL query execution.

- [ ] **Meta tools complémentaires pour custom tools**
  - `custom_tool_list`
  - `custom_tool_delete`
  - Goal: operate custom tools without opening LiteDB manually.

### LLM Enhancements
- [ ] **Vision model support**
  - Image analysis with multimodal models.

### UI & Interfaces
- [ ] **Improve French TTS voice quality (accent/language)**
  - Current French output can still sound too US-accented.
  - Goal: select a French-capable voice/model or replace the backend.

- [ ] **Voice sidecar services (later)**
  - Keep STT/TTS models hot and isolate CPU/RAM from the Host.
  - Preserve the current local/in-container path as fallback.

### Developer Experience
- [ ] **Agent playground**
  - Interactive testing environment.

- [ ] **Debugging tools**
  - Better step-through visibility into agent reasoning and tool decisions.

- [ ] **Plugin SDK**
  - Third-party tool development kit.

