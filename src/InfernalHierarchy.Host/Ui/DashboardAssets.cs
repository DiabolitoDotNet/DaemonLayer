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
          <pre id="perfSnapshot" class="log"></pre>
        </section>

        <section class="card">
          <h2>Latency Histograms</h2>
          <pre id="perfHist" class="log"></pre>
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
input, textarea { width:100%; padding:10px; border-radius:10px; border:1px solid var(--border); background:#0f1420; color:var(--text); }
button { padding:10px 12px; border-radius:10px; border:1px solid var(--border); background:#0f1420; color:var(--text); cursor:pointer; }
button:hover { border-color: var(--accent); }
.log { height:320px; overflow:auto; background:#0a0e16; border:1px solid var(--border); border-radius:10px; padding:10px; white-space:pre-wrap; }
audio { width: 100%; margin-top: 8px; }
  .pill { font-size: 12px; color: var(--muted); align-self: center; }
  .list { display:flex; flex-direction: column; gap: 6px; max-height: 520px; overflow:auto; }
  .listItem { padding:10px; border:1px solid var(--border); border-radius:10px; background:#0f1420; cursor:pointer; }
  .listItem:hover { border-color: var(--accent); }
  textarea#personaJson { min-height: 420px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
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
const perfRefresh = qs('perfRefresh');
const perfUpdated = qs('perfUpdated');

async function refreshPerf() {
  try {
    const [snap, h] = await Promise.all([
      fetch('/api/perf/snapshot').then(r => r.json()),
      fetch('/api/perf/histograms').then(r => r.json()),
    ]);
    if (perfSnapshot) perfSnapshot.textContent = pretty(snap);
    if (perfHist) perfHist.textContent = pretty(h);
    if (perfUpdated) perfUpdated.textContent = `updated ${new Date().toLocaleTimeString()}`;
  } catch (e) {
    if (perfSnapshot) perfSnapshot.textContent = String(e);
  }
}

if (perfRefresh) perfRefresh.onclick = refreshPerf;

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

// boot
setActiveNav();
refreshSystem();
refreshPersonaList();

setInterval(() => {
  if (currentPage() === 'perf') refreshPerf();
}, 2000);

refreshSystem();
""";
}
