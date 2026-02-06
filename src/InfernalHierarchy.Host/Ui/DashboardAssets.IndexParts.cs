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

    <nav class="nav">
      <a class="navLink" href="/ui">Dashboard</a>
      <a class="navLink" href="/ui/perf">Performance</a>
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
        <section class="card">
          <h2>Chat</h2>
          <div class="row">
            <input id="toAgentId" placeholder="to_agent_id (default: lucifer)" />
            <button id="connect">Connect WS</button>
            <button id="disconnect">Disconnect</button>
          </div>
          <textarea id="message" rows="3" placeholder="Type a task/message..."></textarea>
          <div class="row">
            <button id="sendTask">Send task (WS)</button>
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

        <section class="card">
          <h2>Live Stream</h2>
          <div class="row">
            <button id="clear">Clear</button>
          </div>
          <pre id="wsLog" class="log"></pre>
        </section>

        <section class="card">
          <h2>System</h2>
          <div class="row">
            <button id="refresh">Refresh</button>
          </div>
          <pre id="sys" class="log"></pre>
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
          <h2>Recent Traces</h2>
          <div class="row">
            <button id="perfTraceRefresh">Refresh</button>
            <button id="perfTraceDownload" disabled>Download JSON</button>
            <span id="perfTraceSelected" class="pill"></span>
            <span id="perfTraceCritical" class="pill"></span>
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
