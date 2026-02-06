namespace InfernalHierarchy.Host.Ui;

internal static class DashboardAssets
{
    public const string IndexHtml = """
<!doctype html>
<html lang=\"en\">
<head>
  <meta charset=\"utf-8\" />
  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />
  <title>InfernalHierarchy UI</title>
  <link rel=\"stylesheet\" href=\"/ui/styles.css\" />
</head>
<body>
  <header>
    <h1>InfernalHierarchy</h1>
    <div class=\"sub\">Local dashboard + WebSocket stream</div>
  </header>

  <main>
    <section class=\"card\">
      <h2>Chat</h2>
      <div class=\"row\">
        <input id=\"toAgentId\" placeholder=\"to_agent_id (default: lucifer)\" />
        <button id=\"connect\">Connect WS</button>
        <button id=\"disconnect\">Disconnect</button>
      </div>
      <textarea id=\"message\" rows=\"3\" placeholder=\"Type a task/message...\"></textarea>
      <div class=\"row\">
        <button id=\"sendTask\">Send task (WS)</button>
        <button id=\"sendHttp\">Send task (HTTP)</button>
      </div>
      <pre id=\"chatLog\" class=\"log\"></pre>
    </section>

    <section class=\"card\">
      <h2>Voice</h2>
      <div class=\"row\">
        <input id=\"audioFile\" type=\"file\" accept=\"audio/*\" />
        <button id=\"transcribe\">Transcribe</button>
      </div>
      <textarea id=\"transcript\" rows=\"3\" placeholder=\"Transcript will appear here...\" readonly></textarea>
      <div class=\"row\">
        <input id=\"ttsText\" placeholder=\"Text to speak...\" />
        <button id=\"speak\">Speak</button>
      </div>
      <audio id=\"ttsAudio\" controls></audio>
      <pre id=\"voiceLog\" class=\"log\"></pre>
    </section>

    <section class=\"card\">
      <h2>Live Stream</h2>
      <div class=\"row\">
        <button id=\"clear\">Clear</button>
      </div>
      <pre id=\"wsLog\" class=\"log\"></pre>
    </section>

    <section class=\"card\">
      <h2>System</h2>
      <div class=\"row\">
        <button id=\"refresh\">Refresh</button>
      </div>
      <pre id=\"sys\" class=\"log\"></pre>
    </section>
  </main>

  <script src=\"/ui/app.js\"></script>
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
main { padding:16px 20px; display:grid; gap:16px; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); }
.card { background:var(--card); border:1px solid var(--border); border-radius:12px; padding:14px; }
.card h2 { margin:0 0 10px 0; font-size:14px; color:var(--accent); }
.row { display:flex; gap:8px; margin-bottom:8px; }
input, textarea { width:100%; padding:10px; border-radius:10px; border:1px solid var(--border); background:#0f1420; color:var(--text); }
button { padding:10px 12px; border-radius:10px; border:1px solid var(--border); background:#0f1420; color:var(--text); cursor:pointer; }
button:hover { border-color: var(--accent); }
.log { height:320px; overflow:auto; background:#0a0e16; border:1px solid var(--border); border-radius:10px; padding:10px; white-space:pre-wrap; }
audio { width: 100%; margin-top: 8px; }
""";

    public const string AppJs = """
const wsLog = document.getElementById('wsLog');
const chatLog = document.getElementById('chatLog');
const sys = document.getElementById('sys');

const connectBtn = document.getElementById('connect');
const disconnectBtn = document.getElementById('disconnect');
const clearBtn = document.getElementById('clear');
const refreshBtn = document.getElementById('refresh');

const toAgentIdInput = document.getElementById('toAgentId');
const messageInput = document.getElementById('message');
const sendTaskBtn = document.getElementById('sendTask');
const sendHttpBtn = document.getElementById('sendHttp');

const audioFileInput = document.getElementById('audioFile');
const transcribeBtn = document.getElementById('transcribe');
const transcriptEl = document.getElementById('transcript');
const ttsTextInput = document.getElementById('ttsText');
const speakBtn = document.getElementById('speak');
const ttsAudio = document.getElementById('ttsAudio');
const voiceLog = document.getElementById('voiceLog');

let socket = null;

function append(el, line) {
  el.textContent += line + '\n';
  el.scrollTop = el.scrollHeight;
}

function wsUrl() {
  const proto = location.protocol === 'https:' ? 'wss' : 'ws';
  return `${proto}://${location.host}/ws`;
}

async function refreshSystem() {
  try {
    const [agents, tools] = await Promise.all([
      fetch('/api/agents').then(r => r.json()),
      fetch('/api/tools').then(r => r.json()),
    ]);
    sys.textContent = JSON.stringify({ agents, tools }, null, 2);
  } catch (e) {
    sys.textContent = String(e);
  }
}

connectBtn.onclick = () => {
  if (socket && socket.readyState === WebSocket.OPEN) return;

  socket = new WebSocket(wsUrl());
  socket.onopen = () => append(wsLog, `[ws] connected ${wsUrl()}`);
  socket.onclose = () => append(wsLog, `[ws] disconnected`);
  socket.onerror = (e) => append(wsLog, `[ws] error ${String(e)}`);
  socket.onmessage = (evt) => {
    append(wsLog, evt.data);
  };
};

disconnectBtn.onclick = () => {
  if (!socket) return;
  socket.close();
};

clearBtn.onclick = () => {
  wsLog.textContent = '';
  chatLog.textContent = '';
};

refreshBtn.onclick = refreshSystem;

sendTaskBtn.onclick = () => {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    append(chatLog, '[chat] connect WS first');
    return;
  }

  const toAgentId = (toAgentIdInput.value || 'lucifer').trim();
  const message = (messageInput.value || '').trim();
  if (!message) return;

  const payload = {
    type: 'task',
    toAgentId,
    content: message,
  };

  socket.send(JSON.stringify(payload));
  append(chatLog, `[me → ${toAgentId}] ${message}`);
  messageInput.value = '';
};

sendHttpBtn.onclick = async () => {
  const toAgentId = (toAgentIdInput.value || 'lucifer').trim();
  const message = (messageInput.value || '').trim();
  if (!message) return;

  append(chatLog, `[http → ${toAgentId}] ${message}`);

  const res = await fetch('/api/chat', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ message, toAgentId, timeoutMs: 60000 }),
  });

  const body = await res.text();
  append(chatLog, `[http] ${res.status} ${body}`);
  messageInput.value = '';
};

transcribeBtn.onclick = async () => {
  if (!audioFileInput || !audioFileInput.files || audioFileInput.files.length === 0) {
    append(voiceLog, '[voice] pick an audio file first');
    return;
  }

  const file = audioFileInput.files[0];
  const form = new FormData();
  form.append('file', file, file.name);

  append(voiceLog, `[stt] uploading ${file.name} (${file.size} bytes)`);
  const res = await fetch('/api/voice/transcribe', { method: 'POST', body: form });

  if (res.status === 404) {
    append(voiceLog, '[stt] voice API is disabled (Voice:Enabled=false)');
    return;
  }

  const bodyText = await res.text();
  if (!res.ok) {
    append(voiceLog, `[stt] ${res.status} ${bodyText}`);
    return;
  }

  try {
    const json = JSON.parse(bodyText);
    if (transcriptEl) transcriptEl.value = json.transcript || '';
    append(voiceLog, '[stt] ok');
  } catch {
    append(voiceLog, `[stt] ok (non-json): ${bodyText}`);
  }
};

speakBtn.onclick = async () => {
  const text = (ttsTextInput && ttsTextInput.value ? ttsTextInput.value : '').trim();
  if (!text) return;

  append(voiceLog, `[tts] speaking ${text.length} chars`);
  const res = await fetch('/api/voice/speak', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ text }),
  });

  if (res.status === 404) {
    append(voiceLog, '[tts] voice API is disabled (Voice:Enabled=false)');
    return;
  }

  if (!res.ok) {
    append(voiceLog, `[tts] ${res.status} ${await res.text()}`);
    return;
  }

  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  if (ttsAudio) ttsAudio.src = url;
  try { if (ttsAudio) await ttsAudio.play(); } catch { }
  append(voiceLog, '[tts] ok');
};

refreshSystem();
""";
}
