# Change-Impact Test Matrix

Use this matrix to select the first suites to run based on touched projects.

| Changed area | First suites to run | Follow-up |
|---|---|---|
| src/InfernalHierarchy.Core | Core.Tests, Messaging.Tests, Tools.Tests | Full solution tests |
| src/InfernalHierarchy.Messaging | Messaging.Tests, Host.Tests | Full solution tests |
| src/InfernalHierarchy.Tools | Tools.Tests, Host.Tests | Full solution tests |
| src/InfernalHierarchy.Agents | Agents.Tests, Host.Tests | Full solution tests |
| src/InfernalHierarchy.Host | Host.Tests, Tools.Tests | Full solution tests |
| src/InfernalHierarchy.Personas | Personas.Tests, Agents.Tests | Full solution tests |
| src/InfernalHierarchy.Memory | Memory.Tests, Host.Tests | Full solution tests |
| src/InfernalHierarchy.Telegram | Telegram.Tests, Host.Tests | Full solution tests |
| skills/, souls/, templates/ | Host.Tests, Agents.Tests | Full solution tests |
| CI/workflows/scripts/docs only | CI syntax check + one representative fast suite | Optional full solution tests |

## CI Alignment

- PR gate runs fast lane first and full lane after fast lane success.
- Release pipeline adds container smoke checks on top of full validation.
