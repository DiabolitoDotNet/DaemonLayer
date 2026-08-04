namespace InfernalHierarchy.Host.Ui;

internal static partial class DashboardAssets
{
    internal const string LayoutPrefix = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>InfernalHierarchy UI</title>
  <link rel="stylesheet" href="/ui/styles.css" />
</head>
<body>
  <header>
    <h1>InfernalHierarchy</h1>
    <div class="sub">Local dashboard + WebSocket stream</div>

    <div class="row opAuth">
      <input id="operatorApiKey" type="password" placeholder="Operator API key (X-Infernal-Operator-Key)" autocomplete="off" />
      <button id="operatorApiKeySave" type="button">Save key</button>
      <button id="operatorApiKeyClear" type="button">Clear key</button>
    </div>

    <nav class="nav">
      <a class="navLink" href="/ui">Dashboard</a>
      <a class="navLink" href="/ui/ops">Operations</a>
      <a class="navLink" href="/ui/perf">Performance</a>
      <a class="navLink" href="/ui/timeline">Timeline</a>
      <a class="navLink" href="/ui/playground">Playground</a>
      <a class="navLink" href="/ui/personas">Personas</a>
      <a class="navLink" href="/ui/docs">Docs</a>
      <a class="navLink" href="/ui/migrate">Migrate</a>
    </nav>
  </header>

  <main>
""";

    internal const string LayoutSuffix = """
  </main>

  <script src="/ui/app.js"></script>
</body>
</html>
""";

    internal const string PageHome = """
    <div id="page-home" class="page">
      <div class="grid">
        <section class="card fullSpan">
          <h2>Hierarchy</h2>
          <div class="row">
            <button id="refresh">Refresh</button>
            <span id="sysSummary" class="pill"></span>
          </div>
          <div class="row systemControls">
            <select id="agentsViewMode">
              <option value="tree" selected>Hierarchy view</option>
              <option value="rank">Rank columns</option>
            </select>
            <select id="agentsSortMode">
              <option value="rank_name" selected>Sort: Rank then name</option>
              <option value="name">Sort: Name</option>
              <option value="status">Sort: Status priority</option>
            </select>
            <select id="agentsStatusFilter">
              <option value="all" selected>All statuses</option>
              <option value="idle">Idle</option>
              <option value="busy">Busy/Running</option>
              <option value="error">Error/Failed</option>
              <option value="stopped">Stopped/Suspended</option>
            </select>
            <select id="agentsFocus">
              <option value="">Focus: All agents</option>
            </select>
            <input id="agentsSearch" placeholder="Search agent (name/id/rank/status)" />
            <button id="agentsCollapseAll" type="button">Collapse all</button>
            <button id="agentsExpandAll" type="button">Expand all</button>
          </div>
          <div id="agentsLegend" class="systemLegend">
            <span class="legendItem"><span class="agentIcon">♛</span> Supreme</span>
            <span class="legendItem"><span class="agentIcon">♚</span> Prince</span>
            <span class="legendItem"><span class="agentIcon">♜</span> Duke</span>
            <span class="legendItem"><span class="agentIcon">⚙</span> Worker</span>
            <span class="legendItem"><span class="statusBadge statusIdle">Idle</span></span>
            <span class="legendItem"><span class="statusBadge statusBusy">Busy</span></span>
            <span class="legendItem"><span class="statusBadge statusError">Error</span></span>
            <span class="legendItem"><span class="statusBadge statusStopped">Stopped</span></span>
          </div>
          <div class="systemLayout">
            <div class="systemPanel">
              <h3>Agents Hierarchy</h3>
              <div id="agentsHierarchy" class="hierarchyBoard"></div>
            </div>
          </div>
          <pre id="sys" class="log small"></pre>
        </section>

        <section class="card fullSpan">
          <h2>Live Stream</h2>
          <div class="row">
            <button id="connect">Connect WS</button>
            <button id="disconnect">Disconnect</button>
            <button id="clear">Clear</button>
          </div>
          <pre id="wsLog" class="log"></pre>
        </section>
      </div>
    </div>

""";

    internal const string PageOps = """
    <div id="page-ops" class="page">
      <div class="grid">
        <section class="card">
          <h2>Chat</h2>
          <div class="row">
            <input id="toAgentId" placeholder="to_agent_id (default: lucifer)" />
            <input id="telegramChatId" placeholder="telegram_chat_id (optional)" />
          </div>
          <textarea id="message" rows="3" placeholder="Type a task/message..."></textarea>
          <div class="row">
            <button id="sendHttp">Send task (HTTP)</button>
          </div>
          <pre id="chatLog" class="log"></pre>
        </section>

        <section class="card">
          <h2>Voice</h2>
          <div class="row">
            <input id="audioFile" type="file" accept="audio/*" />
            <button id="transcribe">Transcribe</button>
          </div>
          <textarea id="transcript" rows="3" placeholder="Transcript will appear here..." readonly></textarea>
          <div class="row">
            <input id="ttsText" placeholder="Text to speak..." />
            <button id="speak">Speak</button>
          </div>
          <audio id="ttsAudio" controls></audio>
          <pre id="voiceLog" class="log"></pre>
        </section>

        <section class="card fullSpan">
          <h2>Tools</h2>
          <div class="row">
            <button id="refreshOps">Refresh tools/system</button>
          </div>
          <div id="toolsCards" class="toolsCards"></div>
        </section>
      </div>
    </div>

""";

    internal const string PagePerf = """
    <div id="page-perf" class="page">
      <div class="grid">
        <section class="card">
          <h2>Runtime Snapshot</h2>
          <div class="row">
            <button id="perfRefresh">Refresh</button>
            <span id="perfUpdated" class="pill"></span>
            <span id="perfReqProfiling" class="pill"></span>
          </div>
          <div class="charts">
            <div class="chart">
              <div class="chartTitle">Working Set (MB)</div>
              <canvas id="perfChartMem" width="760" height="120"></canvas>
            </div>
            <div class="chart">
              <div class="chartTitle">CPU Usage (%)</div>
              <canvas id="perfChartCpu" width="760" height="120"></canvas>
            </div>
          </div>
          <pre id="perfSnapshot" class="log"></pre>
        </section>

        <section class="card">
          <h2>Latency Histograms</h2>
          <div class="row">
            <span class="pill">Click a metric to pin</span>
            <span id="perfPinned" class="pill"></span>
          </div>
          <div id="perfHistTable" class="table"></div>
          <pre id="perfHist" class="log"></pre>
        </section>

        <section class="card">
          <h2>HTTP Latency (Top p95)</h2>
          <div class="row">
            <span class="pill">By route template</span>
          </div>
          <div id="perfHttpTable" class="table"></div>
        </section>

        <section class="card">
          <h2>Spans (Top p95)</h2>
          <div class="row">
            <span class="pill">Activity-based summaries</span>
          </div>
          <div id="perfSpanTable" class="table"></div>
        </section>

        <section class="card">
          <h2>Recent Requests</h2>
          <div class="row">
            <button id="perfReqRefresh">Refresh</button>
            <button id="perfReqClear" type="button">Clear</button>
            <span id="perfReqStats" class="pill"></span>
          </div>
          <div id="perfReqList" class="list"></div>
          <pre id="perfReqDetail" class="log small"></pre>
        </section>

        <section class="card">
          <h2>Recent Traces</h2>
          <div class="row">
            <button id="perfTraceRefresh">Refresh</button>
            <label class="pill"><input id="perfTraceAuto" type="checkbox" /> auto</label>
            <select id="perfTraceAutoSec">
              <option value="2">2s</option>
              <option value="5" selected>5s</option>
              <option value="10">10s</option>
              <option value="30">30s</option>
            </select>
            <button id="perfTraceClear" type="button">Clear</button>
            <button id="perfTraceDownload" disabled>Download JSON</button>
            <span id="perfTraceCapture" class="pill"></span>
            <span id="perfTraceSelected" class="pill"></span>
            <span id="perfTraceCritical" class="pill"></span>
          </div>
          <div class="row">
            <input id="perfTraceListFilter" placeholder="Filter traces (id/root)..." />
            <input id="perfTraceListMinMs" placeholder="min ms" />
            <label class="pill"><input id="perfTraceListErrorsOnly" type="checkbox" /> errors only</label>
            <select id="perfTraceListSort">
              <option value="start_desc" selected>sort: newest</option>
              <option value="duration_desc">sort: duration</option>
              <option value="errors_desc">sort: errors</option>
            </select>
          </div>
          <div class="row">
            <input id="perfTraceFilter" placeholder="Filter spans (name/tag/value)..." />
            <label class="pill"><input id="perfTraceErrorsOnly" type="checkbox" /> errors only</label>
            <label class="pill"><input id="perfTraceHighlightCritical" type="checkbox" checked /> highlight critical</label>
            <button id="perfTraceCollapseAll" type="button">Collapse</button>
            <button id="perfTraceExpandAll" type="button">Expand</button>
          </div>
          <div id="perfTraceList" class="list"></div>
          <div id="perfTraceTree" class="traceTree"></div>
          <div class="chart" style="margin-top: 10px;">
            <div class="chartTitle">Waterfall (relative timing)</div>
            <canvas id="perfTraceWaterfall" width="760" height="260"></canvas>
          </div>
          <pre id="perfTraceSummary" class="log small"></pre>
          <pre id="perfSpanDetail" class="log small"></pre>
          <pre id="perfTraceDetail" class="log"></pre>
        </section>
      </div>
    </div>

""";

    internal const string PageTimeline = """
    <div id="page-timeline" class="page">
      <div class="grid">
        <section class="card">
          <h2>Reasoning and Tool Timeline</h2>
          <div class="row">
            <button id="timelineRefresh">Refresh</button>
            <input id="timelineMinutes" placeholder="minutes (default 60)" />
            <input id="timelineLimit" placeholder="limit (default 500)" />
            <span id="timelineSummary" class="pill"></span>
          </div>
          <div id="timelineList" class="list"></div>
          <pre id="timelineDetail" class="log small"></pre>
        </section>
      </div>
    </div>

""";

    internal const string PagePlayground = """
    <div id="page-playground" class="page">
      <div class="grid">
        <section class="card">
          <h2>Agent Playground</h2>
          <div class="row">
            <input id="pgName" placeholder="Scenario name" />
            <input id="pgAgent" placeholder="to_agent_id (default: lucifer)" />
            <input id="pgTimeout" placeholder="timeout ms (default 180000)" />
          </div>
          <textarea id="pgPrompt" rows="4" placeholder="Prompt for scenario..." spellcheck="false"></textarea>
          <div class="row">
            <button id="pgCreateRun">Create + Run</button>
            <button id="pgRefresh">Refresh Scenarios</button>
          </div>
          <div id="pgScenarios" class="list"></div>
          <pre id="pgRuns" class="log small"></pre>
        </section>
      </div>
    </div>

""";

    internal const string PagePersonas = """
    <div id="page-personas" class="page">
      <div class="split">
        <section class="card">
          <h2>Persona Files</h2>
          <div class="row">
            <button id="personaRefresh">Refresh</button>
            <input id="personaName" placeholder="name (letters/numbers/_/-)" />
          </div>
          <div id="personaList" class="list"></div>
        </section>

        <section class="card">
          <h2>Editor</h2>
          <div class="row">
            <button id="personaLoad">Load</button>
            <button id="personaValidate">Validate</button>
            <button id="personaSave">Save</button>
          </div>
          <textarea id="personaJson" rows="18" spellcheck="false" placeholder="Persona JSON..."></textarea>
          <pre id="personaLog" class="log"></pre>
        </section>
      </div>
    </div>

""";

    internal const string PageDocs = """
    <div id="page-docs" class="page">
      <div class="grid">
        <section class="card">
          <h2>Documentation Generator</h2>
          <div class="row">
            <button id="docsGenerate">Generate</button>
            <button id="docsDownload">Download .md</button>
          </div>
          <pre id="docsOut" class="log"></pre>
        </section>
      </div>
    </div>

""";

    internal const string PageMigrate = """
    <div id="page-migrate" class="page">
      <div class="grid">
        <section class="card">
          <h2>Agent Migration</h2>
          <div class="row">
            <button id="migRefreshAgents">Refresh agents</button>
            <span class="pill">Export/import bundle JSON</span>
          </div>

          <h3 style="margin:10px 0 6px 0;">Export</h3>
          <div class="row">
            <select id="migAgentSelect"></select>
            <button id="migExport">Download bundle</button>
          </div>
          <pre id="migExportLog" class="log"></pre>

          <h3 style="margin:10px 0 6px 0;">Import</h3>
          <div class="row">
            <input id="migFile" type="file" accept="application/json,.json" />
          </div>
          <div class="row">
            <input id="migPersonaName" placeholder="persona name override (optional)" />
          </div>
          <div class="row">
            <select id="migRank">
              <option value="">rank override (optional)</option>
              <option>Supreme</option>
              <option>Prince</option>
              <option>Duke</option>
              <option>Worker</option>
            </select>
            <input id="migParent" placeholder="parent agent id (optional)" />
          </div>
          <div class="row">
            <label class="pill"><input id="migStart" type="checkbox" checked /> start agent</label>
            <label class="pill"><input id="migFacts" type="checkbox" checked /> facts</label>
            <label class="pill"><input id="migTasks" type="checkbox" checked /> tasks</label>
            <label class="pill"><input id="migDecisions" type="checkbox" /> decisions</label>
            <label class="pill"><input id="migOverwrite" type="checkbox" /> overwrite persona</label>
          </div>
          <div class="row">
            <button id="migImport">Import bundle</button>
          </div>
          <pre id="migImportLog" class="log"></pre>
        </section>
      </div>
    </div>
""";
}
