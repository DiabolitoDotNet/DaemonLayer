# Active Feature Matrix

| Feature | Owning project(s) | Runtime/API surface | Primary tests |
|---|---|---|---|
| REST chat | src/InfernalHierarchy.Host, src/InfernalHierarchy.Agents | `/api/chat` | tests/InfernalHierarchy.Host.Tests/E2E/ChatApiE2ETests.cs |
| WebSocket UI bridge | src/InfernalHierarchy.Host, src/InfernalHierarchy.Messaging | `/ws` | tests/InfernalHierarchy.Host.Tests/E2E/UiAndWebSocketE2ETests.cs |
| Internal message bus | src/InfernalHierarchy.Messaging | in-process `IMessageBus` | tests/InfernalHierarchy.Messaging.Tests/ChannelMessageBusTests.cs |
| ReAct agents | src/InfernalHierarchy.Agents | agent task processing | tests/InfernalHierarchy.Agents.Tests/DefaultReActLoopRunnerTests.cs |
| LLM model routing policy | src/InfernalHierarchy.Tools, src/InfernalHierarchy.Host | task/latency-aware model routing (`IModelRoutingLlmClient`) | tests/InfernalHierarchy.Tools.Tests/OllamaModelRoutingPolicyTests.cs |
| Vision model support | src/InfernalHierarchy.Tools, src/InfernalHierarchy.Core, src/InfernalHierarchy.Host | `vision_describe`, image-capable LLM contract (`IImageLlmClient`) | tests/InfernalHierarchy.Tools.Tests/VisionDescribeToolTests.cs |
| Shared memory | src/InfernalHierarchy.Memory, src/InfernalHierarchy.Core | `ISharedMemory`, memory tools | tests/InfernalHierarchy.Host.Tests/HealthChecksTests.cs |
| Collaboration orchestration | src/InfernalHierarchy.Agents, src/InfernalHierarchy.Tools | `request_collaboration` | tests/InfernalHierarchy.Agents.Tests/AgentCollaborationServiceTests.cs, tests/InfernalHierarchy.Tools.Tests/RequestCollaborationToolTests.cs |
| Autonomous collaboration adjudication | src/InfernalHierarchy.Agents, src/InfernalHierarchy.Messaging | executable supervisor adjudication workflow for unresolved outcomes | tests/InfernalHierarchy.Agents.Tests/AgentCollaborationServiceTests.cs, tests/InfernalHierarchy.Messaging.Tests/FederationServiceTests.cs |
| Skill governance | src/InfernalHierarchy.Agents, src/InfernalHierarchy.Personas, src/InfernalHierarchy.Tools | `request_skill_pack`, runtime overlays | tests/InfernalHierarchy.Agents.Tests/DefaultAgentSkillAssignmentPolicyTests.cs, tests/InfernalHierarchy.Tools.Tests/RequestSkillPackToolTests.cs |
| GraphQL request tooling | src/InfernalHierarchy.Tools, src/InfernalHierarchy.Host | `graphql_request` | tests/InfernalHierarchy.Tools.Tests/GraphQlRequestToolTests.cs |
| SQL read-only querying | src/InfernalHierarchy.Tools, src/InfernalHierarchy.Host | `sql_query_readonly` | tests/InfernalHierarchy.Tools.Tests/SqlReadOnlyQueryToolTests.cs |
| Custom tool management | src/InfernalHierarchy.Tools, src/InfernalHierarchy.Memory | `custom_tool_list`, `custom_tool_delete` | tests/InfernalHierarchy.Tools.Tests/CustomToolManagementToolsTests.cs |
| Autonomous custom tool create/reload lane | src/InfernalHierarchy.Tools, src/InfernalHierarchy.Host | `create_custom_tool` compile/register flow + startup reload without manual blocking gate | tests/InfernalHierarchy.Tools.Tests/CreateCustomToolToolTests.cs, tests/InfernalHierarchy.Host.Tests/CustomToolsStartupServiceTests.cs |
| Tool execution pipeline | src/InfernalHierarchy.Tools, src/InfernalHierarchy.Host | all tools via registry | tests/InfernalHierarchy.Tools.Tests/DefaultToolExecutionPipelineTests.cs |
| Dead-letter and replay | src/InfernalHierarchy.Host, src/InfernalHierarchy.Tools, src/InfernalHierarchy.Messaging | `/api/ops/deadletters` | tests/InfernalHierarchy.Host.Tests/DeadLetterReplayServiceTests.cs |
| Health and readiness | src/InfernalHierarchy.Host | `/health`, `/health/ready`, `/health/live` | tests/InfernalHierarchy.Host.Tests/HealthChecksTests.cs |
| Observability and metrics | src/InfernalHierarchy.Host | `/metrics`, `/api/perf/*` | tests/InfernalHierarchy.Host.Tests/MetricsServiceTests.cs |
| Performance gate harness | tools/InfernalHierarchy.PerfGate | budgeted perf validation for authorization/federation plus autonomy readiness scale, scorecard volume, concurrent remediation, soak stability, in-scope compliance gating, dependency-degraded bounded refusal, certification tail latency, and release-over-release trend drift enforcement | tools/InfernalHierarchy.PerfGate/perf-baseline.json |
| Telegram interface | src/InfernalHierarchy.Telegram, src/InfernalHierarchy.Host | Telegram bot polling + notifications | tests/InfernalHierarchy.Host.Tests/TelegramHealthCheckTests.cs |
| Voice interface | src/InfernalHierarchy.Host, src/InfernalHierarchy.Tools | voice endpoints/tools when enabled (`audio_transcribe`, `tts_speak`) | tests/InfernalHierarchy.Host.Tests/StartupFeatureReportServiceTests.cs, tests/InfernalHierarchy.Tools.Tests/TextToSpeechLanguageRoutingTests.cs |
| Voice sidecar mode | src/InfernalHierarchy.Tools, src/InfernalHierarchy.Host | STT/TTS delegation via sidecar HTTP endpoints + `voice_sidecar` health check | tests/InfernalHierarchy.Host.Tests/VoiceAndVisionOptionsValidatorTests.cs |
| Agent playground | src/InfernalHierarchy.Host | `/api/playground/scenarios*`, `/api/playground/runs/*/replay`, `/ui/playground` | tests/InfernalHierarchy.Host.Tests/IntegrationTests.cs |
| Autonomy scorecard release gate | src/InfernalHierarchy.Host | scorecard grading from real playground scenario runs | tests/InfernalHierarchy.Host.Tests/AutonomyScorecardGateTests.cs |
| Autonomy readiness report | src/InfernalHierarchy.Host | `/api/autonomy/readiness` | tests/InfernalHierarchy.Host.Tests/AutonomyReadinessHostedServiceTests.cs |
| Autonomy SLO metrics API | src/InfernalHierarchy.Host | `/api/autonomy/slo`, SLO gate evaluation inputs | tests/InfernalHierarchy.Host.Tests/E2E/PerfPersonaDocsE2ETests.cs, tests/InfernalHierarchy.Host.Tests/SloGateEvaluatorTests.cs |
| Reasoning/tool timeline view | src/InfernalHierarchy.Host, src/InfernalHierarchy.Agents | `/api/perf/timeline`, `/ui/timeline` | tests/InfernalHierarchy.Host.Tests/IntegrationTests.cs |
| Plugin SDK starter | templates/plugin-sdk, Documentation | starter scaffold + onboarding guide (`Documentation/Plugin-SDK.md`) | tests/InfernalHierarchy.Host.Tests/ToolMarketplaceHostedServiceTests.cs |
| GraphQL surface | src/InfernalHierarchy.GraphQL | archived/experimental, not supported in P1/P2 runtime | Documentation/ADRs/0007-graphql-surface-status.md |

## Notes

- This matrix reflects the currently supported runtime surface.
- Add a row whenever a new external surface or operator-visible capability is introduced.
