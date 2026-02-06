namespace InfernalHierarchy.Host.Ui;

internal static class DashboardAssets
{
    public const string IndexHtml = """
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
          </div>
          <div id="perfTraceList" class="list"></div>
          <pre id="perfTraceDetail" class="log"></pre>
        </section>
      </div>
    </div>

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
  </main>

  <script src="/ui/app.js"></script>
</body>
</html>
""";

    public const string StylesCss = """
:root { --bg:#0b0f14; --card:#121826; --text:#e7eefc; --muted:#a9b4c7; --accent:#7aa2ff; --border:#253046; }
* { box-sizing: border-box; }
body { margin:0; font-family: ui-sans-serif, system-ui, Segoe UI, Roboto, Arial; background:var(--bg); color:var(--text); }
header { padding:16px 20px; border-bottom:1px solid var(--border); background:rgba(18,24,38,.6); position:sticky; top:0; backdrop-filter: blur(8px); }
h1 { margin:0; font-size:18px; }
.sub { color:var(--muted); font-size:12px; margin-top:4px; }
  .nav { display:flex; gap:10px; margin-top:10px; flex-wrap:wrap; }
  .navLink { text-decoration:none; color:var(--text); font-size:13px; padding:8px 10px; border-radius:10px; border:1px solid var(--border); background:#0f1420; }
  .navLink.active { border-color: var(--accent); color: var(--accent); }

  main { padding:16px 20px; }
  .grid { display:grid; gap:16px; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); }
  .page { display:none; }
  .page.active { display:block; }
  .split { display:grid; gap:16px; grid-template-columns: 360px 1fr; }
  @media (max-width: 900px) { .split { grid-template-columns: 1fr; } }
.card { background:var(--card); border:1px solid var(--border); border-radius:12px; padding:14px; }
.card h2 { margin:0 0 10px 0; font-size:14px; color:var(--accent); }
.row { display:flex; gap:8px; margin-bottom:8px; }
input, textarea, select { width:100%; padding:10px; border-radius:10px; border:1px solid var(--border); background:#0f1420; color:var(--text); }
button { padding:10px 12px; border-radius:10px; border:1px solid var(--border); background:#0f1420; color:var(--text); cursor:pointer; }
button:hover { border-color: var(--accent); }
.log { height:320px; overflow:auto; background:#0a0e16; border:1px solid var(--border); border-radius:10px; padding:10px; white-space:pre-wrap; }
audio { width: 100%; margin-top: 8px; }
  .pill { font-size: 12px; color: var(--muted); align-self: center; }
  .list { display:flex; flex-direction: column; gap: 6px; max-height: 520px; overflow:auto; }
  .listItem { padding:10px; border:1px solid var(--border); border-radius:10px; background:#0f1420; cursor:pointer; }
  .listItem:hover { border-color: var(--accent); }
  textarea#personaJson { min-height: 420px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
  .charts { display:grid; gap:12px; margin-bottom:10px; }
  .chart { border: 1px solid var(--border); border-radius: 10px; padding: 10px; background: #0a0e16; }
  .chartTitle { font-size: 12px; color: var(--muted); margin-bottom: 6px; }
  canvas { width: 100%; height: 120px; }
  .table { display:flex; flex-direction: column; gap: 6px; }
  .tableRow { display:grid; grid-template-columns: 1fr 72px 72px 72px 72px; gap: 8px; align-items:center; padding: 8px 10px; border:1px solid var(--border); border-radius: 10px; background:#0f1420; }
  .tableRow.head { background: transparent; border-style: dashed; color: var(--muted); }
  .tableRow.clickable { cursor: pointer; }
  .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12px; }
  .right { text-align: right; }
""";

    public const string AppJs = """
const qs = (id) => document.getElementById(id);

function append(el, line) {
  if (!el) return;
  el.textContent += line + '\n';
  el.scrollTop = el.scrollHeight;
}

function pretty(obj) {
  return JSON.stringify(obj, null, 2);
}

function wsUrl() {
  const proto = location.protocol === 'https:' ? 'wss' : 'ws';
  return `${proto}://${location.host}/ws`;
}

function currentPage() {
  const p = (location.pathname || '/ui').toLowerCase();
  if (p.endsWith('/ui/perf')) return 'perf';
  if (p.endsWith('/ui/personas')) return 'personas';
  if (p.endsWith('/ui/docs')) return 'docs';
  if (p.endsWith('/ui/migrate')) return 'migrate';
  return 'home';
}

function setActiveNav() {
  const page = currentPage();
  const links = Array.from(document.querySelectorAll('a.navLink'));
  for (const a of links) {
    a.classList.remove('active');
    const href = (a.getAttribute('href') || '').toLowerCase();
    if ((page === 'home' && href === '/ui') || href.endsWith(`/ui/${page}`)) {
      a.classList.add('active');
    }
  }

  const pages = {
    home: qs('page-home'),
    perf: qs('page-perf'),
    personas: qs('page-personas'),
    docs: qs('page-docs'),
    migrate: qs('page-migrate'),
  };
  for (const k of Object.keys(pages)) {
    if (pages[k]) pages[k].classList.toggle('active', k === page);
  }
}

// Home: WS + chat + voice
const wsLog = qs('wsLog');
const chatLog = qs('chatLog');
const sys = qs('sys');

const connectBtn = qs('connect');
const disconnectBtn = qs('disconnect');
const clearBtn = qs('clear');
const refreshBtn = qs('refresh');

const toAgentIdInput = qs('toAgentId');
const messageInput = qs('message');
const sendTaskBtn = qs('sendTask');
const sendHttpBtn = qs('sendHttp');

const audioFileInput = qs('audioFile');
const transcribeBtn = qs('transcribe');
const transcriptEl = qs('transcript');
const ttsTextInput = qs('ttsText');
const speakBtn = qs('speak');
const ttsAudio = qs('ttsAudio');
const voiceLog = qs('voiceLog');

let socket = null;

async function refreshSystem() {
  try {
    const [agents, tools] = await Promise.all([
      fetch('/api/agents').then(r => r.json()),
      fetch('/api/tools').then(r => r.json()),
    ]);
    if (sys) sys.textContent = pretty({ agents, tools });
  } catch (e) {
    if (sys) sys.textContent = String(e);
  }
}

if (connectBtn) connectBtn.onclick = () => {
  if (socket && socket.readyState === WebSocket.OPEN) return;
  socket = new WebSocket(wsUrl());
  socket.onopen = () => append(wsLog, `[ws] connected ${wsUrl()}`);
  socket.onclose = () => append(wsLog, `[ws] disconnected`);
  socket.onerror = (e) => append(wsLog, `[ws] error ${String(e)}`);
  socket.onmessage = (evt) => append(wsLog, evt.data);
};

if (disconnectBtn) disconnectBtn.onclick = () => { if (socket) socket.close(); };
if (clearBtn) clearBtn.onclick = () => { if (wsLog) wsLog.textContent = ''; if (chatLog) chatLog.textContent = ''; };
if (refreshBtn) refreshBtn.onclick = refreshSystem;

if (sendTaskBtn) sendTaskBtn.onclick = () => {
  if (!socket || socket.readyState !== WebSocket.OPEN) { append(chatLog, '[chat] connect WS first'); return; }
  const toAgentId = ((toAgentIdInput && toAgentIdInput.value) || 'lucifer').trim();
  const message = ((messageInput && messageInput.value) || '').trim();
  if (!message) return;
  socket.send(JSON.stringify({ type: 'task', toAgentId, content: message }));
  append(chatLog, `[me → ${toAgentId}] ${message}`);
  if (messageInput) messageInput.value = '';
};

if (sendHttpBtn) sendHttpBtn.onclick = async () => {
  const toAgentId = ((toAgentIdInput && toAgentIdInput.value) || 'lucifer').trim();
  const message = ((messageInput && messageInput.value) || '').trim();
  if (!message) return;
  append(chatLog, `[http → ${toAgentId}] ${message}`);
  const res = await fetch('/api/chat', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ message, toAgentId, timeoutMs: 60000 }),
  });
  append(chatLog, `[http] ${res.status} ${await res.text()}`);
  if (messageInput) messageInput.value = '';
};

if (transcribeBtn) transcribeBtn.onclick = async () => {
  if (!audioFileInput || !audioFileInput.files || audioFileInput.files.length === 0) { append(voiceLog, '[voice] pick an audio file first'); return; }
  const file = audioFileInput.files[0];
  const form = new FormData();
  form.append('file', file, file.name);
  append(voiceLog, `[stt] uploading ${file.name} (${file.size} bytes)`);
  const res = await fetch('/api/voice/transcribe', { method: 'POST', body: form });
  if (res.status === 404) { append(voiceLog, '[stt] voice API is disabled (Voice:Enabled=false)'); return; }
  const bodyText = await res.text();
  if (!res.ok) { append(voiceLog, `[stt] ${res.status} ${bodyText}`); return; }
  try {
    const json = JSON.parse(bodyText);
    if (transcriptEl) transcriptEl.value = json.transcript || '';
    append(voiceLog, '[stt] ok');
  } catch {
    append(voiceLog, `[stt] ok (non-json): ${bodyText}`);
  }
};

if (speakBtn) speakBtn.onclick = async () => {
  const text = ((ttsTextInput && ttsTextInput.value) || '').trim();
  if (!text) return;
  append(voiceLog, `[tts] speaking ${text.length} chars`);
  const res = await fetch('/api/voice/speak', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ text }),
  });
  if (res.status === 404) { append(voiceLog, '[tts] voice API is disabled (Voice:Enabled=false)'); return; }
  if (!res.ok) { append(voiceLog, `[tts] ${res.status} ${await res.text()}`); return; }
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  if (ttsAudio) ttsAudio.src = url;
  try { if (ttsAudio) await ttsAudio.play(); } catch { }
  append(voiceLog, '[tts] ok');
};

// Performance
const perfSnapshot = qs('perfSnapshot');
const perfHist = qs('perfHist');
const perfHistTable = qs('perfHistTable');
const perfRefresh = qs('perfRefresh');
const perfUpdated = qs('perfUpdated');
const perfPinned = qs('perfPinned');
const perfHttpTable = qs('perfHttpTable');
const perfSpanTable = qs('perfSpanTable');
const perfChartMem = qs('perfChartMem');
const perfChartCpu = qs('perfChartCpu');
const perfTraceRefresh = qs('perfTraceRefresh');
const perfTraceDownload = qs('perfTraceDownload');
const perfTraceSelected = qs('perfTraceSelected');
const perfTraceList = qs('perfTraceList');
const perfTraceDetail = qs('perfTraceDetail');

let perfPinnedMetric = '';
const perfSeries = []; // { t, ws, cpu }
let perfTracesInitialized = false;
let perfSelectedTraceId = '';

function formatNum(n) {
  if (n === null || n === undefined || Number.isNaN(n)) return '-';
  return (Math.round(n * 100) / 100).toFixed(2);
}

function renderLineChart(canvas, series, valueSelector, opts) {
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  const w = canvas.width;
  const h = canvas.height;

  ctx.clearRect(0, 0, w, h);

  // background
  ctx.fillStyle = '#0a0e16';
  ctx.fillRect(0, 0, w, h);

  const data = series.slice(-120);
  if (data.length < 2) {
    ctx.fillStyle = '#8b93a7';
    ctx.font = '12px ui-monospace, Menlo, Consolas, monospace';
    ctx.fillText('waiting for samples…', 10, 18);
    return;
  }

  const values = data.map(valueSelector).filter(v => typeof v === 'number' && !Number.isNaN(v));
  if (values.length < 2) return;

  const min = Math.min(...values);
  const max = Math.max(...values);
  const pad = (max - min) * 0.1 || 1;
  const yMin = min - pad;
  const yMax = max + pad;

  // grid
  ctx.strokeStyle = 'rgba(255,255,255,0.06)';
  ctx.lineWidth = 1;
  for (let i = 1; i <= 4; i++) {
    const y = (h * i) / 5;
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(w, y);
    ctx.stroke();
  }

  // line
  ctx.strokeStyle = (opts && opts.color) ? opts.color : '#6aa7ff';
  ctx.lineWidth = 2;
  ctx.beginPath();
  for (let i = 0; i < data.length; i++) {
    const v = valueSelector(data[i]);
    const x = (i / (data.length - 1)) * (w - 1);
    const y = h - ((v - yMin) / (yMax - yMin)) * (h - 1);
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.stroke();

  // label
  const last = valueSelector(data[data.length - 1]);
  ctx.fillStyle = '#8b93a7';
  ctx.font = '12px ui-monospace, Menlo, Consolas, monospace';
  ctx.fillText(`${opts && opts.label ? opts.label : 'value'}: ${formatNum(last)}`, 10, 18);
}

function clearTable(el) {
  if (!el) return;
  el.textContent = '';
}

function addTableHead(el) {
  if (!el) return;
  const head = document.createElement('div');
  head.className = 'tableRow head mono';
  head.innerHTML = `<div>metric</div><div class="right">p50</div><div class="right">p95</div><div class="right">p99</div><div class="right">n</div>`;
  el.appendChild(head);
}

function addTableRow(el, metric, stats, clickable) {
  if (!el) return;
  const row = document.createElement('div');
  row.className = `tableRow mono${clickable ? ' clickable' : ''}`;
  row.innerHTML = `<div title="${metric}">${metric}</div>` +
    `<div class="right">${formatNum(stats.p50)}</div>` +
    `<div class="right">${formatNum(stats.p95)}</div>` +
    `<div class="right">${formatNum(stats.p99)}</div>` +
    `<div class="right">${stats.count || 0}</div>`;
  if (clickable) {
    row.onclick = () => {
      perfPinnedMetric = metric;
      if (perfPinned) perfPinned.textContent = `pinned: ${metric}`;
      if (perfHist) perfHist.textContent = pretty({ metric, stats });
    };
  }
  el.appendChild(row);
}

async function refreshPerf() {
  try {
    const [snap, h] = await Promise.all([
      fetch('/api/perf/snapshot').then(r => r.json()),
      fetch('/api/perf/histograms').then(r => r.json()),
    ]);
    if (perfSnapshot) perfSnapshot.textContent = pretty(snap);
    // timeline sampling + charts
    perfSeries.push({
      t: Date.now(),
      ws: snap.workingSetMB,
      cpu: snap.cpuUsagePercent,
    });
    if (perfSeries.length > 200) perfSeries.splice(0, perfSeries.length - 200);
    renderLineChart(perfChartMem, perfSeries, x => x.ws, { label: 'MB', color: '#8aefc2' });
    renderLineChart(perfChartCpu, perfSeries, x => x.cpu, { label: '%', color: '#6aa7ff' });

    // histogram table + pinned view
    const stats = (h && h.stats) ? h.stats : {};
    const rows = Object.keys(stats).map(k => ({ metric: k, stats: stats[k] || {} }));
    rows.sort((a, b) => ((b.stats.p95 || 0) - (a.stats.p95 || 0)) || ((b.stats.count || 0) - (a.stats.count || 0)));

    clearTable(perfHistTable);
    addTableHead(perfHistTable);
    for (const r of rows.slice(0, 50)) {
      addTableRow(perfHistTable, r.metric, {
        p50: r.stats.p50,
        p95: r.stats.p95,
        p99: r.stats.p99,
        count: r.stats.count,
      }, true);
    }

    if (perfPinnedMetric && stats[perfPinnedMetric]) {
      if (perfHist) perfHist.textContent = pretty({ metric: perfPinnedMetric, stats: stats[perfPinnedMetric] });
    } else if (perfHist) {
      // show a useful default (global http latency, if present)
      if (stats['http.latency.ms']) perfHist.textContent = pretty({ metric: 'http.latency.ms', stats: stats['http.latency.ms'] });
      else perfHist.textContent = pretty({ hint: 'Click a metric above to pin its details.' });
    }

    // HTTP table
    try {
      const http = await fetch('/api/perf/http').then(r => r.json());
      const items = (http && http.items) ? http.items : [];
      clearTable(perfHttpTable);
      addTableHead(perfHttpTable);
      for (const it of items.slice(0, 30)) {
        const s = it.stats || {};
        addTableRow(perfHttpTable, it.metric, {
          p50: s.p50,
          p95: s.p95,
          p99: s.p99,
          count: s.count,
        }, true);
      }
    } catch {
      // ignore (endpoint may be disabled if HTTP is off)
    }

    // Span table
    try {
      const spans = await fetch('/api/perf/spans').then(r => r.json());
      const items = (spans && spans.items) ? spans.items : [];
      clearTable(perfSpanTable);
      addTableHead(perfSpanTable);
      for (const it of items.slice(0, 30)) {
        const s = it.stats || {};
        addTableRow(perfSpanTable, it.metric, {
          p50: s.p50,
          p95: s.p95,
          p99: s.p99,
          count: s.count,
        }, true);
      }
    } catch {
      // ignore
    }

    if (!perfTracesInitialized) {
      perfTracesInitialized = true;
      await refreshPerfTraces();
    }

    if (perfUpdated) perfUpdated.textContent = `updated ${new Date().toLocaleTimeString()}`;
  } catch (e) {
    if (perfSnapshot) perfSnapshot.textContent = String(e);
  }
}

if (perfRefresh) perfRefresh.onclick = refreshPerf;
if (perfTraceRefresh) perfTraceRefresh.onclick = () => refreshPerfTraces();
if (perfTraceDownload) perfTraceDownload.onclick = () => downloadSelectedTrace();

async function refreshPerfTraces() {
  if (!perfTraceList) return;
  perfTraceList.textContent = '';
  if (perfTraceDetail) perfTraceDetail.textContent = '';
  if (perfTraceSelected) perfTraceSelected.textContent = '';
  perfSelectedTraceId = '';
  if (perfTraceDownload) perfTraceDownload.disabled = true;

  try {
    const json = await fetch('/api/perf/traces?limit=30').then(r => r.json());
    const items = (json && json.items) ? json.items : [];
    if (!items || items.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'pill';
      empty.textContent = 'No traces captured yet.';
      perfTraceList.appendChild(empty);
      return;
    }

    for (const t of items) {
      const div = document.createElement('div');
      div.className = 'listItem';
      div.innerHTML = `<span class="mono">${t.traceId}</span> — ${t.rootSpanName || '(root)'} | ${formatNum(t.durationMs)}ms | spans=${t.spanCount} errors=${t.errorCount}`;
      div.onclick = async () => {
        perfSelectedTraceId = t.traceId;
        if (perfTraceSelected) perfTraceSelected.textContent = `selected: ${t.traceId}`;
        if (perfTraceDownload) perfTraceDownload.disabled = false;
        await loadPerfTraceDetail(t.traceId);
      };
      perfTraceList.appendChild(div);
    }
  } catch (e) {
    const err = document.createElement('div');
    err.className = 'pill';
    err.textContent = `Error loading traces: ${String(e)}`;
    perfTraceList.appendChild(err);
  }
}

async function loadPerfTraceDetail(traceId) {
  if (!perfTraceDetail) return;
  perfTraceDetail.textContent = 'Loading...';
  try {
    const json = await fetch(`/api/perf/traces/${encodeURIComponent(traceId)}`).then(r => r.json());
    perfTraceDetail.textContent = pretty(json);
  } catch (e) {
    perfTraceDetail.textContent = String(e);
  }
}

async function downloadSelectedTrace() {
  if (!perfSelectedTraceId) return;
  try {
    const res = await fetch(`/api/perf/traces/${encodeURIComponent(perfSelectedTraceId)}/download`);
    if (!res.ok) { append(perfTraceDetail, `[trace] download failed: ${res.status}`); return; }
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `trace_${perfSelectedTraceId}.json`;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  } catch (e) {
    append(perfTraceDetail, `[trace] download error: ${String(e)}`);
  }
}

// Personas
const personaRefresh = qs('personaRefresh');
const personaName = qs('personaName');
const personaList = qs('personaList');
const personaLoad = qs('personaLoad');
const personaValidate = qs('personaValidate');
const personaSave = qs('personaSave');
const personaJson = qs('personaJson');
const personaLog = qs('personaLog');

function personaCurrentName() {
  return ((personaName && personaName.value) || '').trim();
}

function personaSetName(name) {
  if (personaName) personaName.value = name;
}

async function refreshPersonaList() {
  if (!personaList) return;
  personaList.textContent = '';
  try {
    const items = await fetch('/api/personas').then(r => r.json());
    for (const p of items) {
      const div = document.createElement('div');
      div.className = 'listItem';
      div.textContent = `${p.name}`;
      div.onclick = async () => {
        personaSetName(p.name);
        await loadPersona();
      };
      personaList.appendChild(div);
    }
  } catch (e) {
    append(personaLog, `[personas] ${String(e)}`);
  }
}

async function loadPersona() {
  const name = personaCurrentName();
  if (!name) { append(personaLog, '[personas] enter a name'); return; }
  const res = await fetch(`/api/personas/${encodeURIComponent(name)}`);
  const text = await res.text();
  if (!res.ok) { append(personaLog, `[load] ${res.status} ${text}`); return; }
  const json = JSON.parse(text);
  if (personaJson) personaJson.value = json.json || '';
  append(personaLog, `[load] ok: ${name}`);
}

async function validatePersona() {
  const name = personaCurrentName();
  const raw = (personaJson && personaJson.value) ? personaJson.value : '';
  if (!name) { append(personaLog, '[validate] enter a name'); return; }
  const res = await fetch(`/api/personas/${encodeURIComponent(name)}/validate`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ json: raw }),
  });
  const text = await res.text();
  if (!res.ok) { append(personaLog, `[validate] ${res.status} ${text}`); return; }
  append(personaLog, `[validate] ok`);
}

async function savePersona() {
  const name = personaCurrentName();
  const raw = (personaJson && personaJson.value) ? personaJson.value : '';
  if (!name) { append(personaLog, '[save] enter a name'); return; }
  const res = await fetch(`/api/personas/${encodeURIComponent(name)}`, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ json: raw }),
  });
  const text = await res.text();
  if (!res.ok) { append(personaLog, `[save] ${res.status} ${text}`); return; }
  append(personaLog, `[save] ok`);
  await refreshPersonaList();
}

if (personaRefresh) personaRefresh.onclick = refreshPersonaList;
if (personaLoad) personaLoad.onclick = loadPersona;
if (personaValidate) personaValidate.onclick = validatePersona;
if (personaSave) personaSave.onclick = savePersona;

// Docs
const docsGenerate = qs('docsGenerate');
const docsDownload = qs('docsDownload');
const docsOut = qs('docsOut');
let lastDocs = '';

async function generateDocs() {
  try {
    const md = await fetch('/api/docs/markdown').then(r => r.text());
    lastDocs = md;
    if (docsOut) docsOut.textContent = md;
  } catch (e) {
    if (docsOut) docsOut.textContent = String(e);
  }
}

if (docsGenerate) docsGenerate.onclick = generateDocs;
if (docsDownload) docsDownload.onclick = () => {
  const blob = new Blob([lastDocs || ''], { type: 'text/markdown;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'infernalhierarchy-docs.md';
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
};

// Migration
const migRefreshAgents = qs('migRefreshAgents');
const migAgentSelect = qs('migAgentSelect');
const migExport = qs('migExport');
const migExportLog = qs('migExportLog');
const migFile = qs('migFile');
const migPersonaName = qs('migPersonaName');
const migRank = qs('migRank');
const migParent = qs('migParent');
const migStart = qs('migStart');
const migFacts = qs('migFacts');
const migTasks = qs('migTasks');
const migDecisions = qs('migDecisions');
const migOverwrite = qs('migOverwrite');
const migImport = qs('migImport');
const migImportLog = qs('migImportLog');

async function refreshMigrationAgents() {
  if (!migAgentSelect) return;
  migAgentSelect.textContent = '';
  try {
    const agents = await fetch('/api/agents').then(r => r.json());
    if (!agents || agents.length === 0) {
      const opt = document.createElement('option');
      opt.textContent = '(no agents)';
      migAgentSelect.appendChild(opt);
      return;
    }

    for (const a of agents) {
      const opt = document.createElement('option');
      opt.value = a.id;
      opt.textContent = `${a.name} (${a.rank}) - ${a.status} [${a.id}]`;
      migAgentSelect.appendChild(opt);
    }
  } catch (e) {
    const opt = document.createElement('option');
    opt.textContent = `(error loading agents: ${String(e)})`;
    migAgentSelect.appendChild(opt);
  }
}

async function exportMigrationBundle() {
  if (!migAgentSelect || !migExportLog) return;
  const agentId = (migAgentSelect.value || '').trim();
  if (!agentId) { migExportLog.textContent = 'Pick an agent.'; return; }
  migExportLog.textContent = 'Exporting...';

  const res = await fetch(`/api/agents/${encodeURIComponent(agentId)}/export`);
  const body = await res.text();
  if (!res.ok) { migExportLog.textContent = `[export] ${res.status}\n${body}`; return; }

  try {
    const parsed = JSON.parse(body);
    const summary = {
      bundleId: parsed.bundleId,
      exportedAtUtc: parsed.exportedAtUtc,
      personaName: parsed.personaName,
      agentRank: parsed.agentRank,
      facts: (parsed.facts || []).length,
      tasks: (parsed.tasks || []).length,
      decisions: (parsed.decisions || []).length,
      signed: !!(parsed.signature && parsed.signature.value),
      signatureAlgorithm: parsed.signature ? parsed.signature.algorithm : null,
    };
    migExportLog.textContent = pretty({ ok: true, summary, note: 'Downloading bundle...' });
  } catch {
    // ignore; still downloadable
  }

  const blob = new Blob([body], { type: 'application/json;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `agent_bundle_${agentId}.json`;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  migExportLog.textContent += '\nDownloaded.';
}

async function importMigrationBundle() {
  if (!migFile || !migImportLog) return;
  if (!migFile.files || migFile.files.length === 0) { migImportLog.textContent = 'Pick a bundle JSON file first.'; return; }
  const text = await migFile.files[0].text();

  let preview = null;
  try {
    const parsed = JSON.parse(text);
    preview = {
      formatVersion: parsed.formatVersion,
      bundleId: parsed.bundleId,
      exportedAtUtc: parsed.exportedAtUtc,
      personaName: parsed.personaName,
      agentRank: parsed.agentRank,
      facts: (parsed.facts || []).length,
      tasks: (parsed.tasks || []).length,
      decisions: (parsed.decisions || []).length,
      signed: !!(parsed.signature && parsed.signature.value),
      signatureAlgorithm: parsed.signature ? parsed.signature.algorithm : null,
      source: parsed.source || null,
    };

    if (!preview.bundleId || !preview.personaName || !parsed.personaJson) {
      migImportLog.textContent = pretty({ ok: false, error: 'Bundle JSON is missing required fields (bundleId/personaName/personaJson).' });
      return;
    }
  } catch (e) {
    migImportLog.textContent = pretty({ ok: false, error: 'Invalid JSON file.', detail: String(e) });
    return;
  }

  const personaOverrideRaw = (migPersonaName && migPersonaName.value) ? migPersonaName.value.trim() : '';
  const rankOverrideRaw = (migRank && migRank.value) ? String(migRank.value).trim() : '';
  const parentAgentIdRaw = (migParent && migParent.value) ? migParent.value.trim() : '';
  const overwritePersona = !!(migOverwrite && migOverwrite.checked);

  const confirmText = [
    'Import this bundle?',
    '',
    `Persona: ${personaOverrideRaw || preview.personaName}`,
    `Rank: ${rankOverrideRaw || preview.agentRank}`,
    `Facts: ${preview.facts} | Tasks: ${preview.tasks} | Decisions: ${preview.decisions}`,
    `Signed: ${preview.signed ? 'yes' : 'no'}`,
    overwritePersona ? 'Persona overwrite: yes' : 'Persona overwrite: no',
  ].join('\n');

  migImportLog.textContent = pretty({ preview });
  if (!confirm(confirmText)) {
    migImportLog.textContent += '\nCancelled.';
    return;
  }

  migImportLog.textContent = 'Importing...';
  const res = await fetch('/api/agents/import', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      bundleJson: text,
      personaNameOverride: personaOverrideRaw.length ? personaOverrideRaw : null,
      agentRankOverride: rankOverrideRaw.length ? rankOverrideRaw : null,
      parentAgentId: parentAgentIdRaw.length ? parentAgentIdRaw : null,
      startAgent: !!(migStart && migStart.checked),
      importFacts: !!(migFacts && migFacts.checked),
      importTasks: !!(migTasks && migTasks.checked),
      importDecisions: !!(migDecisions && migDecisions.checked),
      overwritePersona,
    }),
  });

  const body = await res.text();
  try {
    const json = JSON.parse(body);
    migImportLog.textContent = pretty({ ok: res.ok, response: json });
  } catch {
    migImportLog.textContent = body;
  }

  if (res.ok) {
    await refreshMigrationAgents();
  }
}

if (migRefreshAgents) migRefreshAgents.onclick = refreshMigrationAgents;
if (migExport) migExport.onclick = exportMigrationBundle;
if (migImport) migImport.onclick = importMigrationBundle;

// boot
setActiveNav();
refreshSystem();
refreshPersonaList();
refreshMigrationAgents();

setInterval(() => {
  if (currentPage() === 'perf') refreshPerf();
}, 2000);

refreshSystem();
""";
}
