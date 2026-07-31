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
  append(chatLog, `[me ÔåÆ ${toAgentId}] ${message}`);
  if (messageInput) messageInput.value = '';
};

if (sendHttpBtn) sendHttpBtn.onclick = async () => {
  const toAgentId = ((toAgentIdInput && toAgentIdInput.value) || 'lucifer').trim();
  const message = ((messageInput && messageInput.value) || '').trim();
  if (!message) return;
  append(chatLog, `[http ÔåÆ ${toAgentId}] ${message}`);
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
const perfReqProfiling = qs('perfReqProfiling');
const perfReqRefresh = qs('perfReqRefresh');
const perfReqClear = qs('perfReqClear');
const perfReqStats = qs('perfReqStats');
const perfReqList = qs('perfReqList');
const perfReqDetail = qs('perfReqDetail');
const perfTraceRefresh = qs('perfTraceRefresh');
const perfTraceAuto = qs('perfTraceAuto');
const perfTraceAutoSec = qs('perfTraceAutoSec');
const perfTraceClear = qs('perfTraceClear');
const perfTraceDownload = qs('perfTraceDownload');
const perfTraceCapture = qs('perfTraceCapture');
const perfTraceSelected = qs('perfTraceSelected');
const perfTraceCritical = qs('perfTraceCritical');
const perfTraceListFilter = qs('perfTraceListFilter');
const perfTraceListMinMs = qs('perfTraceListMinMs');
const perfTraceListErrorsOnly = qs('perfTraceListErrorsOnly');
const perfTraceListSort = qs('perfTraceListSort');
const perfTraceList = qs('perfTraceList');
const perfTraceDetail = qs('perfTraceDetail');
const perfTraceSummary = qs('perfTraceSummary');
const perfSpanDetail = qs('perfSpanDetail');
const perfTraceTree = qs('perfTraceTree');
const perfTraceWaterfall = qs('perfTraceWaterfall');
const perfTraceFilter = qs('perfTraceFilter');
const perfTraceErrorsOnly = qs('perfTraceErrorsOnly');
const perfTraceHighlightCritical = qs('perfTraceHighlightCritical');
const perfTraceCollapseAll = qs('perfTraceCollapseAll');
const perfTraceExpandAll = qs('perfTraceExpandAll');

let perfPinnedMetric = '';
const perfSeries = []; // { t, ws, cpu }
let perfTracesInitialized = false;
let perfSelectedTraceId = '';
let perfSelectedSpanId = '';
let perfTraceTreeJson = null;
let perfTraceCollapsed = new Set();
let perfTraceCriticalIds = new Set();
let perfSpanById = new Map();
let perfWaterfallScroll = 0;
let perfTraceAutoTimer = null;
let perfTraceListItemsRaw = [];
let perfReqProfilingEnabled = false;
let perfReqItemsRaw = [];

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
    ctx.fillText('waiting for samplesÔÇª', 10, 18);
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
      await refreshPerfRequests();
    }

    // Best-effort init (safe if disabled).
    await refreshRequestProfilingStatus();

    if (perfUpdated) perfUpdated.textContent = `updated ${new Date().toLocaleTimeString()}`;
  } catch (e) {
    if (perfSnapshot) perfSnapshot.textContent = String(e);
  }
}

async function refreshRequestProfilingStatus() {
  if (!perfReqProfiling) return;

  try {
    const res = await fetch('/api/perf/request-profiling');
    if (!res.ok) {
      perfReqProfiling.textContent = '';
      perfReqProfilingEnabled = false;
      return;
    }

    const json = await res.json();
    perfReqProfilingEnabled = !!(json && json.enabled);

    if (!perfReqProfilingEnabled) {
      perfReqProfiling.textContent = 'request profiling: off';
      return;
    }

    const count = (json && typeof json.requestsStored === 'number') ? json.requestsStored : 0;
    perfReqProfiling.textContent = `request profiling: on (${count})`;
  } catch {
    perfReqProfiling.textContent = '';
    perfReqProfilingEnabled = false;
  }
}

function renderPerfRequests() {
  if (!perfReqList) return;
  perfReqList.textContent = '';

  const items = perfReqItemsRaw || [];
  if (perfReqStats) perfReqStats.textContent = items.length ? `${items.length} shown` : '';

  if (!items || items.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'pill';
    empty.textContent = perfReqProfilingEnabled ? 'No request profiles yet.' : 'Request profiling is disabled.';
    perfReqList.appendChild(empty);
    return;
  }

  for (const r of items) {
    const div = document.createElement('div');
    div.className = 'listItem';
    const dur = formatNum(r.durationMs);
    const method = (r.method || '').toUpperCase();
    const status = r.statusCode || 0;
    const route = r.routeTemplate || '';
    const path = r.path || '';
    const traceId = r.traceId || '';
    const title = route ? route : path;

    const traceLink = traceId ? `<a href="#" data-trace="${traceId}" class="mono">trace</a>` : '';
    div.innerHTML = `<span class="mono">${dur}ms</span> | <span class="mono">${method}</span> ${status} ÔÇö ${title} ${traceLink ? ' | ' + traceLink : ''}`;

    div.onclick = async () => {
      if (!perfReqDetail) return;
      perfReqDetail.textContent = 'Loading...';
      try {
        const detail = await fetch(`/api/perf/requests/${encodeURIComponent(r.id)}`).then(x => x.json());
        perfReqDetail.textContent = pretty(detail);
      } catch (e) {
        perfReqDetail.textContent = String(e);
      }
    };

    const a = div.querySelector('a[data-trace]');
    if (a) {
      a.onclick = async (evt) => {
        evt.preventDefault();
        evt.stopPropagation();
        const tid = a.getAttribute('data-trace') || '';
        if (!tid) return;
        await selectPerfTraceById(tid);
      };
    }

    perfReqList.appendChild(div);
  }
}

async function refreshPerfRequests() {
  if (!perfReqList) return;
  perfReqItemsRaw = [];
  renderPerfRequests();

  try {
    const json = await fetch('/api/perf/requests?limit=50').then(r => r.json());
    perfReqItemsRaw = (json && json.items) ? json.items : [];
  } catch {
    perfReqItemsRaw = [];
  }

  await refreshRequestProfilingStatus();
  renderPerfRequests();
}

async function clearPerfRequests() {
  const yes = confirm('Clear recent request profiles?');
  if (!yes) return;
  try {
    await fetch('/api/perf/requests/clear', { method: 'POST' });
  } catch {
  }
  perfReqItemsRaw = [];
  if (perfReqDetail) perfReqDetail.textContent = '';
  await refreshPerfRequests();
}

async function selectPerfTraceById(traceId) {
  if (!traceId) return;
  perfSelectedTraceId = traceId;
  perfSelectedSpanId = '';
  if (perfTraceSelected) perfTraceSelected.textContent = `selected: ${traceId}`;
  if (perfTraceDownload) perfTraceDownload.disabled = false;
  renderPerfTraceList();
  await loadPerfTraceDetail(traceId);
  await loadPerfTraceTree(traceId);
}

if (perfRefresh) perfRefresh.onclick = refreshPerf;
if (perfReqRefresh) perfReqRefresh.onclick = () => refreshPerfRequests();
if (perfReqClear) perfReqClear.onclick = () => clearPerfRequests();
if (perfTraceRefresh) perfTraceRefresh.onclick = () => refreshPerfTraces(true);
if (perfTraceDownload) perfTraceDownload.onclick = () => downloadSelectedTrace();
if (perfTraceClear) perfTraceClear.onclick = () => clearPerfTraces();
if (perfTraceAuto) perfTraceAuto.onchange = () => updatePerfTraceAutoRefresh();
if (perfTraceAutoSec) perfTraceAutoSec.onchange = () => updatePerfTraceAutoRefresh();
if (perfTraceListFilter) perfTraceListFilter.oninput = () => renderPerfTraceList();
if (perfTraceListMinMs) perfTraceListMinMs.oninput = () => renderPerfTraceList();
if (perfTraceListErrorsOnly) perfTraceListErrorsOnly.onchange = () => renderPerfTraceList();
if (perfTraceListSort) perfTraceListSort.onchange = () => renderPerfTraceList();
if (perfTraceFilter) perfTraceFilter.oninput = () => renderPerfTraceViews();
if (perfTraceErrorsOnly) perfTraceErrorsOnly.onchange = () => renderPerfTraceViews();
if (perfTraceHighlightCritical) perfTraceHighlightCritical.onchange = () => renderPerfTraceViews();
if (perfTraceCollapseAll) perfTraceCollapseAll.onclick = () => collapseAllTraceNodes();
if (perfTraceExpandAll) perfTraceExpandAll.onclick = () => expandAllTraceNodes();

async function refreshTraceCaptureStatus() {
  if (!perfTraceCapture) return;
  try {
    const json = await fetch('/api/perf/trace-capture').then(r => r.json());
    const enabled = !!json.enabled;
    const traces = json.tracesStored || 0;
    const spans = json.spansStored || 0;
    perfTraceCapture.textContent = enabled
      ? `capture: on | traces=${traces} spans=${spans}`
      : 'capture: off';
  } catch {
    perfTraceCapture.textContent = '';
  }
}

function updatePerfTraceAutoRefresh() {
  if (perfTraceAutoTimer) {
    clearInterval(perfTraceAutoTimer);
    perfTraceAutoTimer = null;
  }

  const enabled = !!(perfTraceAuto && perfTraceAuto.checked);
  if (!enabled) return;

  const sec = Math.max(1, parseInt((perfTraceAutoSec && perfTraceAutoSec.value) || '5', 10) || 5);
  perfTraceAutoTimer = setInterval(() => {
    refreshPerfTraces(true);
  }, sec * 1000);
}

function traceListQuery() {
  return ((perfTraceListFilter && perfTraceListFilter.value) || '').trim().toLowerCase();
}

function traceListMinMs() {
  const raw = ((perfTraceListMinMs && perfTraceListMinMs.value) || '').trim();
  if (!raw) return 0;
  const n = parseFloat(raw);
  return Number.isFinite(n) && n > 0 ? n : 0;
}

function traceListSortKey() {
  return ((perfTraceListSort && perfTraceListSort.value) || 'start_desc').trim();
}

function matchesTrace(t, q, minMs, errorsOnly) {
  if (!t) return false;
  if (errorsOnly && !(t.errorCount > 0)) return false;
  if (minMs > 0 && !((t.durationMs || 0) >= minMs)) return false;
  if (!q) return true;
  const s = `${t.traceId || ''} ${t.rootSpanName || ''}`.toLowerCase();
  return s.includes(q);
}

function renderPerfTraceList() {
  if (!perfTraceList) return;
  perfTraceList.textContent = '';

  const q = traceListQuery();
  const minMs = traceListMinMs();
  const errorsOnly = !!(perfTraceListErrorsOnly && perfTraceListErrorsOnly.checked);
  const sort = traceListSortKey();

  let items = (perfTraceListItemsRaw || []).filter(t => matchesTrace(t, q, minMs, errorsOnly));
  if (sort === 'duration_desc') {
    items = items.slice().sort((a, b) => ((b.durationMs || 0) - (a.durationMs || 0)) || ((b.errorCount || 0) - (a.errorCount || 0)));
  } else if (sort === 'errors_desc') {
    items = items.slice().sort((a, b) => ((b.errorCount || 0) - (a.errorCount || 0)) || ((b.durationMs || 0) - (a.durationMs || 0)));
  } else {
    // Default is newest-first; the API already returns newest-first.
  }

  if (!items || items.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'pill';
    empty.textContent = 'No traces match the current filter.';
    perfTraceList.appendChild(empty);
    return;
  }

  for (const t of items) {
    const div = document.createElement('div');
    const isSelected = !!(perfSelectedTraceId && t.traceId === perfSelectedTraceId);
    div.className = `listItem${isSelected ? ' selected' : ''}`;
    const err = (t.errorCount || 0);
    const badge = err > 0 ? ` | errors=${err}` : '';
    div.innerHTML = `<span class="mono">${t.traceId}</span> ÔÇö ${t.rootSpanName || '(root)'} | ${formatNum(t.durationMs)}ms | spans=${t.spanCount}${badge}`;
    div.onclick = async () => {
      perfSelectedTraceId = t.traceId;
      perfSelectedSpanId = '';
      if (perfTraceSelected) perfTraceSelected.textContent = `selected: ${t.traceId}`;
      if (perfTraceDownload) perfTraceDownload.disabled = false;
      renderPerfTraceList();
      await loadPerfTraceDetail(t.traceId);
      await loadPerfTraceTree(t.traceId);
    };
    perfTraceList.appendChild(div);
  }
}

async function refreshPerfTraces(keepSelection) {
  if (!perfTraceList) return;
  perfTraceList.textContent = '';

  const prevTraceId = perfSelectedTraceId;
  const keep = !!keepSelection && !!prevTraceId;
  if (!keep) {
    if (perfTraceDetail) perfTraceDetail.textContent = '';
    if (perfTraceSummary) perfTraceSummary.textContent = '';
    if (perfSpanDetail) perfSpanDetail.textContent = '';
    if (perfTraceTree) perfTraceTree.textContent = '';
    if (perfTraceSelected) perfTraceSelected.textContent = '';
    if (perfTraceCritical) perfTraceCritical.textContent = '';
    perfSelectedTraceId = '';
    perfSelectedSpanId = '';
    perfTraceTreeJson = null;
    perfTraceCollapsed = new Set();
    perfTraceCriticalIds = new Set();
    perfSpanById = new Map();
    perfWaterfallScroll = 0;
    if (perfTraceDownload) perfTraceDownload.disabled = true;
  }

  perfTraceListItemsRaw = [];

  try {
    const json = await fetch('/api/perf/traces?limit=30').then(r => r.json());
    const items = (json && json.items) ? json.items : [];
    perfTraceListItemsRaw = items;
    await refreshTraceCaptureStatus();

    if (!items || items.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'pill';
      empty.textContent = 'No traces captured yet.';
      perfTraceList.appendChild(empty);

      if (perfTraceDownload) perfTraceDownload.disabled = true;
      if (perfTraceSelected) perfTraceSelected.textContent = '';
      if (perfTraceCritical) perfTraceCritical.textContent = '';
      return;
    }

    // If we kept selection, ensure the selected trace still exists.
    if (keep) {
      const stillThere = items.some(t => t && t.traceId === prevTraceId);
      if (!stillThere) {
        perfSelectedTraceId = '';
        perfSelectedSpanId = '';
        perfTraceTreeJson = null;
        perfTraceCollapsed = new Set();
        perfTraceCriticalIds = new Set();
        perfSpanById = new Map();
        perfWaterfallScroll = 0;
        if (perfTraceDownload) perfTraceDownload.disabled = true;
        if (perfTraceDetail) perfTraceDetail.textContent = '';
        if (perfTraceSummary) perfTraceSummary.textContent = '';
        if (perfSpanDetail) perfSpanDetail.textContent = '';
        if (perfTraceTree) perfTraceTree.textContent = '';
        if (perfTraceSelected) perfTraceSelected.textContent = '';
        if (perfTraceCritical) perfTraceCritical.textContent = '';
      }
    }

    renderPerfTraceList();
    updatePerfTraceAutoRefresh();
  } catch (e) {
    const err = document.createElement('div');
    err.className = 'pill';
    err.textContent = `Error loading traces: ${String(e)}`;
    perfTraceList.appendChild(err);
  }
}

async function clearPerfTraces() {
  const yes = confirm('Clear all captured traces?');
  if (!yes) return;
  try {
    const res = await fetch('/api/perf/traces/clear', { method: 'POST' });
    if (!res.ok) {
      append(perfTraceDetail, `[trace] clear failed: ${res.status}`);
      return;
    }
  } catch (e) {
    append(perfTraceDetail, `[trace] clear error: ${String(e)}`);
    return;
  }

  // Hard reset after a clear.
  perfSelectedTraceId = '';
  perfSelectedSpanId = '';
  perfTraceTreeJson = null;
  perfTraceCollapsed = new Set();
  perfTraceCriticalIds = new Set();
  perfSpanById = new Map();
  perfWaterfallScroll = 0;
  if (perfTraceDownload) perfTraceDownload.disabled = true;
  if (perfTraceDetail) perfTraceDetail.textContent = '';
  if (perfTraceSummary) perfTraceSummary.textContent = '';
  if (perfSpanDetail) perfSpanDetail.textContent = '';
  if (perfTraceTree) perfTraceTree.textContent = '';
  if (perfTraceSelected) perfTraceSelected.textContent = '';
  if (perfTraceCritical) perfTraceCritical.textContent = '';

  await refreshPerfTraces(false);
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

function collapseAllTraceNodes() {
  if (!perfTraceTreeJson || !perfTraceTreeJson.roots) return;
  const ids = new Set();
  const stack = [].concat(perfTraceTreeJson.roots);
  while (stack.length) {
    const n = stack.pop();
    if (!n) continue;
    const kids = n.children || [];
    if (kids.length > 0) {
      ids.add(n.spanId);
      for (const c of kids) stack.push(c);
    }
  }
  perfTraceCollapsed = ids;
  renderPerfTraceViews();
}

function expandAllTraceNodes() {
  perfTraceCollapsed = new Set();
  renderPerfTraceViews();
}

function initDefaultCollapse(roots) {
  // Expand the first level; collapse deeper nodes for readability.
  const ids = new Set();
  const stack = roots.map(r => ({ node: r, depth: 0 }));
  while (stack.length) {
    const cur = stack.pop();
    if (!cur || !cur.node) continue;
    const kids = cur.node.children || [];
    if (kids.length > 0 && cur.depth >= 1) {
      ids.add(cur.node.spanId);
    }
    for (const c of kids) stack.push({ node: c, depth: cur.depth + 1 });
  }
  perfTraceCollapsed = ids;
}

function spanEndOffsetMs(node) {
  const eo = (node && typeof node.endOffsetMs === 'number') ? node.endOffsetMs : null;
  if (eo !== null && !Number.isNaN(eo)) return eo;
  const so = (node && typeof node.startOffsetMs === 'number') ? node.startOffsetMs : 0;
  const d = (node && typeof node.durationMs === 'number') ? node.durationMs : 0;
  return so + d;
}

function buildSpanIndex(roots) {
  const map = new Map();
  const stack = [].concat(roots || []);
  while (stack.length) {
    const n = stack.pop();
    if (!n || !n.spanId) continue;
    map.set(n.spanId, n);
    const kids = n.children || [];
    for (const c of kids) stack.push(c);
  }
  return map;
}

function tryRenderSelectedSpanDetails() {
  if (!perfSpanDetail) return;
  if (!perfSelectedSpanId) { perfSpanDetail.textContent = pretty({ hint: 'Click a span in the tree/waterfall to see details.' }); return; }
  const n = perfSpanById.get(perfSelectedSpanId);
  if (!n) { perfSpanDetail.textContent = pretty({ spanId: perfSelectedSpanId, hint: 'Span not found in current trace.' }); return; }
  perfSpanDetail.textContent = pretty({
    spanId: n.spanId,
    name: n.name,
    kind: n.kind,
    status: n.status,
    durationMs: n.durationMs,
    startOffsetMs: n.startOffsetMs,
    endOffsetMs: spanEndOffsetMs(n),
    tags: n.tags || {},
  });
}

function nodeText(node) {
  let s = `${node.name || ''} ${node.kind || ''} ${node.status || ''} ${node.spanId || ''}`;
  const tags = node.tags || {};
  for (const k of Object.keys(tags)) {
    s += ` ${k} ${tags[k]}`;
  }
  return s.toLowerCase();
}

function filterNode(node, q, errorsOnly) {
  const kids = node.children || [];
  const filteredKids = [];
  for (const c of kids) {
    const f = filterNode(c, q, errorsOnly);
    if (f) filteredKids.push(f);
  }

  const matchesQ = !q || nodeText(node).includes(q);
  const isError = String(node.status || '').toLowerCase() === 'error';
  const matchesError = !errorsOnly || isError;
  const include = (matchesQ && matchesError) || filteredKids.length > 0;
  if (!include) return null;

  // Shallow clone with filtered children.
  return Object.assign({}, node, { children: filteredKids });
}

function renderPerfTraceViews() {
  renderPerfTraceTree();
  renderPerfTraceWaterfall();
  renderPerfTraceSummary();
  tryRenderSelectedSpanDetails();
}

function ensureSelectedSpanVisibleInWaterfall() {
  if (!perfTraceWaterfall) return;
  if (!perfTraceTreeJson || !perfTraceTreeJson.roots) return;
  if (!perfSelectedSpanId) return;

  const roots = getFilteredRootsForWaterfall();
  const rows = flattenForWaterfall(roots);
  const idx = rows.findIndex(r => r && r.node && r.node.spanId === perfSelectedSpanId);
  if (idx < 0) return;

  const topPad = 18;
  const rowH = 14;
  const visibleRows = Math.max(1, Math.floor((perfTraceWaterfall.height - topPad - 6) / rowH));
  if (idx < perfWaterfallScroll) perfWaterfallScroll = idx;
  else if (idx >= perfWaterfallScroll + visibleRows) perfWaterfallScroll = Math.max(0, idx - visibleRows + 1);
}

function renderPerfTraceTree() {
  if (!perfTraceTree) return;
  perfTraceTree.textContent = '';

  if (!perfTraceTreeJson || !perfTraceTreeJson.roots) {
    const hint = document.createElement('div');
    hint.className = 'pill';
    hint.textContent = 'Select a trace to view span tree.';
    perfTraceTree.appendChild(hint);
    return;
  }

  const q = ((perfTraceFilter && perfTraceFilter.value) || '').trim().toLowerCase();
  const errorsOnly = !!(perfTraceErrorsOnly && perfTraceErrorsOnly.checked);
  const highlightCritical = !!(perfTraceHighlightCritical && perfTraceHighlightCritical.checked);
  const collapseEnabled = !(q || errorsOnly);

  const roots = perfTraceTreeJson.roots || [];
  const filteredRoots = roots
    .map(r => filterNode(r, q, errorsOnly))
    .filter(x => !!x);

  if (filteredRoots.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'pill';
    empty.textContent = 'No spans match the current filter.';
    perfTraceTree.appendChild(empty);
    return;
  }

  function renderNode(node, container) {
    const kids = node.children || [];
    const hasKids = kids.length > 0;
    const isError = String(node.status || '').toLowerCase() === 'error';
    const isCritical = highlightCritical && perfTraceCriticalIds && perfTraceCriticalIds.has(node.spanId);
    const collapsed = collapseEnabled && hasKids && perfTraceCollapsed.has(node.spanId);

    const row = document.createElement('div');
    const isSelected = !!(perfSelectedSpanId && node.spanId === perfSelectedSpanId);
    row.className = `traceNode${isError ? ' error' : ''}${isCritical ? ' critical' : ''}${isSelected ? ' selected' : ''}`;

    const twisty = document.createElement('span');
    twisty.className = 'twisty mono';
    twisty.textContent = hasKids ? (collapsed ? 'Ôû©' : 'Ôû¥') : '┬À';

    const name = document.createElement('span');
    name.className = 'name mono';
    name.textContent = node.name || '(span)';

    const meta = document.createElement('span');
    meta.className = 'meta mono';
    meta.textContent = `${formatNum(node.durationMs)}ms @+${formatNum(node.startOffsetMs)}ms`;

    row.appendChild(twisty);
    row.appendChild(name);
    row.appendChild(meta);

    // Click row to select span; click twisty to collapse/expand.
    row.onclick = () => {
      perfSelectedSpanId = node.spanId;
      ensureSelectedSpanVisibleInWaterfall();
      renderPerfTraceViews();
    };
    if (hasKids && collapseEnabled) {
      twisty.style.cursor = 'pointer';
      twisty.onclick = (evt) => {
        evt.stopPropagation();
        if (perfTraceCollapsed.has(node.spanId)) perfTraceCollapsed.delete(node.spanId);
        else perfTraceCollapsed.add(node.spanId);
        renderPerfTraceViews();
      };
    }

    container.appendChild(row);

    if (hasKids && !collapsed) {
      const indent = document.createElement('div');
      indent.className = 'traceIndent';
      container.appendChild(indent);
      for (const c of kids) {
        renderNode(c, indent);
      }
    }
  }

  for (const r of filteredRoots) {
    renderNode(r, perfTraceTree);
  }

  // Keep selection in view.
  try {
    const sel = perfTraceTree.querySelector('.traceNode.selected');
    if (sel && sel.scrollIntoView) sel.scrollIntoView({ block: 'nearest' });
  } catch {
  }
}

async function loadPerfTraceTree(traceId) {
  if (!perfTraceTree) return;
  perfTraceTree.textContent = 'Loading tree...';

  try {
    const json = await fetch(`/api/perf/traces/${encodeURIComponent(traceId)}/tree`).then(r => r.json());
    perfTraceTreeJson = json;
    const roots = (json && json.roots) ? json.roots : [];
    initDefaultCollapse(roots);
    perfTraceCriticalIds = computeCriticalPathIds(roots);
    perfSpanById = buildSpanIndex(roots);
    if (perfTraceCritical) perfTraceCritical.textContent = perfTraceCriticalIds.size > 0 ? `critical: ${perfTraceCriticalIds.size} spans` : '';
    renderPerfTraceViews();
  } catch (e) {
    perfTraceTreeJson = null;
    perfTraceCollapsed = new Set();
    perfTraceCriticalIds = new Set();
    perfSpanById = new Map();
    if (perfTraceCritical) perfTraceCritical.textContent = '';
    perfTraceTree.textContent = `Error loading tree: ${String(e)}`;
  }
}

function computeCriticalPathIds(roots) {
  const set = new Set();
  if (!roots || roots.length === 0) return set;

  const bestEndById = new Map();
  const bestChildById = new Map();

  function compute(node) {
    let bestEnd = spanEndOffsetMs(node);
    let bestChild = null;

    const kids = (node && node.children) ? node.children : [];
    for (const c of kids) {
      const childEnd = compute(c);
      if (childEnd > bestEnd) {
        bestEnd = childEnd;
        bestChild = c;
      }
    }

    bestEndById.set(node.spanId, bestEnd);
    if (bestChild && bestChild.spanId) {
      bestChildById.set(node.spanId, bestChild.spanId);
    }

    return bestEnd;
  }

  let bestRoot = roots[0];
  let bestRootEnd = -1;
  for (const r of roots) {
    const e = compute(r);
    if (e > bestRootEnd) {
      bestRootEnd = e;
      bestRoot = r;
    }
  }

  let cur = bestRoot && bestRoot.spanId ? bestRoot.spanId : null;
  while (cur) {
    set.add(cur);
    cur = bestChildById.get(cur) || null;
  }

  return set;
}

function getFilteredRootsForWaterfall() {
  if (!perfTraceTreeJson || !perfTraceTreeJson.roots) return [];
  const roots = perfTraceTreeJson.roots || [];
  const q = ((perfTraceFilter && perfTraceFilter.value) || '').trim().toLowerCase();
  const errorsOnly = !!(perfTraceErrorsOnly && perfTraceErrorsOnly.checked);
  return roots
    .map(r => filterNode(r, q, errorsOnly))
    .filter(x => !!x);
}

function flattenForWaterfall(roots) {
  // Pre-order; honor collapse state (when not actively filtering).
  const q = ((perfTraceFilter && perfTraceFilter.value) || '').trim().toLowerCase();
  const errorsOnly = !!(perfTraceErrorsOnly && perfTraceErrorsOnly.checked);
  const collapseEnabled = !(q || errorsOnly);

  const rows = [];
  const stack = (roots || []).slice().reverse().map(r => ({ node: r, depth: 0 }));
  while (stack.length) {
    const cur = stack.pop();
    if (!cur || !cur.node) continue;
    const n = cur.node;
    rows.push({ node: n, depth: cur.depth });
    const kids = n.children || [];
    const hasKids = kids.length > 0;
    const collapsed = collapseEnabled && hasKids && perfTraceCollapsed.has(n.spanId);
    if (!collapsed) {
      for (let i = kids.length - 1; i >= 0; i--) {
        stack.push({ node: kids[i], depth: cur.depth + 1 });
      }
    }
  }
  return rows;
}

function renderPerfTraceSummary() {
  if (!perfTraceSummary) return;
  if (!perfTraceTreeJson || !perfTraceTreeJson.roots) {
    perfTraceSummary.textContent = pretty({ hint: 'Select a trace to see summary.' });
    return;
  }

  const roots = perfTraceTreeJson.roots || [];
  const index = buildSpanIndex(roots);
  let maxEnd = 0;
  for (const n of index.values()) {
    maxEnd = Math.max(maxEnd, spanEndOffsetMs(n));
  }
  const spans = Array.from(index.values()).map(n => ({
    spanId: n.spanId,
    name: n.name,
    durationMs: n.durationMs,
    startOffsetMs: n.startOffsetMs,
    status: n.status,
  }));
  spans.sort((a, b) => (b.durationMs || 0) - (a.durationMs || 0));
  const top = spans.slice(0, 10);
  const errors = spans.filter(s => String(s.status || '').toLowerCase() === 'error').length;

  perfTraceSummary.textContent = pretty({
    traceId: perfSelectedTraceId,
    spanCount: index.size,
    traceDurationMs: maxEnd,
    errors,
    topSpansByDuration: top,
  });
}

function renderPerfTraceWaterfall() {
  if (!perfTraceWaterfall) return;
  const ctx = perfTraceWaterfall.getContext('2d');
  if (!ctx) return;

  const w = perfTraceWaterfall.width;
  const h = perfTraceWaterfall.height;
  ctx.clearRect(0, 0, w, h);
  ctx.fillStyle = '#0a0e16';
  ctx.fillRect(0, 0, w, h);

  const roots = getFilteredRootsForWaterfall();
  if (!roots || roots.length === 0) {
    ctx.fillStyle = '#8b93a7';
    ctx.font = '12px ui-monospace, Menlo, Consolas, monospace';
    ctx.fillText('Select a trace (or adjust filters)ÔÇª', 10, 18);
    return;
  }

  const rows = flattenForWaterfall(roots);
  const index = buildSpanIndex(roots);
  let maxEnd = 0;
  for (const n of index.values()) {
    maxEnd = Math.max(maxEnd, spanEndOffsetMs(n));
  }
  if (maxEnd <= 0) maxEnd = 1;

  const leftPad = 170;
  const topPad = 18;
  const rowH = 14;
  const visibleRows = Math.max(1, Math.floor((h - topPad - 6) / rowH));
  const maxScroll = Math.max(0, rows.length - visibleRows);
  if (perfWaterfallScroll > maxScroll) perfWaterfallScroll = maxScroll;
  if (perfWaterfallScroll < 0) perfWaterfallScroll = 0;

  // axis
  ctx.strokeStyle = 'rgba(255,255,255,0.08)';
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(leftPad, topPad - 8);
  ctx.lineTo(w - 8, topPad - 8);
  ctx.stroke();
  ctx.fillStyle = '#8b93a7';
  ctx.font = '11px ui-monospace, Menlo, Consolas, monospace';
  ctx.fillText('0ms', leftPad, 12);
  ctx.fillText(`${Math.round(maxEnd)}ms`, w - 90, 12);

  const barW = (w - leftPad - 12);
  const slice = rows.slice(perfWaterfallScroll, perfWaterfallScroll + visibleRows);
  for (let i = 0; i < slice.length; i++) {
    const r = slice[i];
    const n = r.node;
    const y = topPad + i * rowH;
    const start = (typeof n.startOffsetMs === 'number') ? n.startOffsetMs : 0;
    const dur = (typeof n.durationMs === 'number') ? n.durationMs : 0;
    const x1 = leftPad + (start / maxEnd) * barW;
    const x2 = leftPad + ((start + dur) / maxEnd) * barW;
    const bw = Math.max(1, x2 - x1);

    const isError = String(n.status || '').toLowerCase() === 'error';
    const isCritical = perfTraceCriticalIds && perfTraceCriticalIds.has(n.spanId);
    const isSelected = !!(perfSelectedSpanId && n.spanId === perfSelectedSpanId);

    // label
    ctx.fillStyle = isSelected ? '#7aa2ff' : '#c7d2ea';
    const indent = Math.min(120, r.depth * 10);
    const label = (n.name || '(span)').slice(0, 22);
    ctx.fillText(label, 8 + indent, y + 10);

    // bar
    ctx.fillStyle = isError ? 'rgba(255,122,122,0.85)'
      : isCritical ? 'rgba(255,211,122,0.85)'
      : 'rgba(106,167,255,0.75)';
    ctx.fillRect(x1, y + 2, bw, 10);

    if (isSelected) {
      ctx.strokeStyle = 'rgba(122,162,255,0.65)';
      ctx.strokeRect(x1 - 1, y + 1, bw + 2, 12);
    }
  }

  // scrolling hint
  if (rows.length > visibleRows) {
    ctx.fillStyle = '#8b93a7';
    ctx.fillText(`scroll: ${perfWaterfallScroll}/${maxScroll}`, w - 140, h - 6);
  }
}

if (perfTraceWaterfall) {
  perfTraceWaterfall.addEventListener('wheel', (evt) => {
    evt.preventDefault();
    const delta = evt.deltaY > 0 ? 3 : -3;
    perfWaterfallScroll += delta;
    renderPerfTraceViews();
  }, { passive: false });

  perfTraceWaterfall.addEventListener('click', (evt) => {
    if (!perfTraceTreeJson || !perfTraceTreeJson.roots) return;
    const rect = perfTraceWaterfall.getBoundingClientRect();
    const y = evt.clientY - rect.top;
    const topPad = 18;
    const rowH = 14;
    const idx = Math.floor((y - topPad) / rowH);
    if (idx < 0) return;

    const roots = getFilteredRootsForWaterfall();
    const rows = flattenForWaterfall(roots);
    const visibleRows = Math.max(1, Math.floor((perfTraceWaterfall.height - topPad - 6) / rowH));
    const row = rows.slice(perfWaterfallScroll, perfWaterfallScroll + visibleRows)[idx];
    if (!row || !row.node || !row.node.spanId) return;
    perfSelectedSpanId = row.node.spanId;
    renderPerfTraceViews();
  });
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