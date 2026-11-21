// Amrita Campus Bridge — Web Platform Frontend Logic (1-Click GUI App Downloads)

const tabs = document.querySelectorAll('.tab-btn');
const panels = document.querySelectorAll('.tab-panel');

tabs.forEach((btn) => {
  btn.addEventListener('click', () => {
    tabs.forEach((b) => b.classList.remove('active'));
    panels.forEach((p) => p.classList.remove('active'));
    btn.classList.add('active');
    document.getElementById(`tab-${btn.dataset.tab}`).classList.add('active');
  });
});

function formatDuration(sec) {
  const s = Math.max(0, Number(sec) || 0);
  const m = Math.floor(s / 60);
  const r = s % 60;
  return `${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}`;
}

function formatBytes(bytes) {
  const b = Number(bytes) || 0;
  if (b < 1024) return `${b} B`;
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB`;
  return `${(b / (1024 * 1024)).toFixed(2)} MB`;
}

// Submit Day Scholar Access Request
document.getElementById('requestForm').addEventListener('submit', async (e) => {
  e.preventDefault();
  const scholarName = document.getElementById('scholarName').value.trim();
  const purpose = document.getElementById('purpose').value;
  const durationMinutes = Number(document.getElementById('durationMinutes').value);
  const allowedDomains = document
    .getElementById('customDomains')
    .value.split(',')
    .map((d) => d.trim())
    .filter(Boolean);

  const res = await fetch('/api/requests', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ scholarName, purpose, durationMinutes, allowedDomains })
  });
  const data = await res.json();
  if (data.ok) {
    localStorage.setItem('amrita_last_session_code', data.request.sessionCode);
    refreshStatus();
  }
});

// Download pre-configured Hosteller GUI App for a specific Day Scholar request
window.downloadHostAppForRequest = function (reqId, scholarName, duration, sessionCode) {
  const hostName = (document.getElementById('instantHostName')?.value || 'Campus-Hosteller').trim();
  const url =
    `/api/download/hosteller-app?requestId=${encodeURIComponent(reqId)}` +
    `&sessionCode=${encodeURIComponent(sessionCode)}` +
    `&duration=${encodeURIComponent(duration)}` +
    `&scholar=${encodeURIComponent(scholarName)}` +
    `&name=${encodeURIComponent(hostName)}`;
  window.location.href = url;
};

// Download Instant Hosteller GUI App
document.getElementById('downloadInstantHostAppBtn').addEventListener('click', () => {
  const hostName = (document.getElementById('instantHostName').value || 'Campus-Hosteller').trim();
  const duration = Number(document.getElementById('instantHostDuration').value) || 30;
  window.location.href = `/api/download/hosteller-app?name=${encodeURIComponent(hostName)}&duration=${encodeURIComponent(duration)}`;
});

window.revokeSession = async function (code) {
  await fetch(`/api/sessions/${encodeURIComponent(code)}`, { method: 'DELETE' });
  refreshStatus();
};

async function refreshStatus() {
  try {
    const res = await fetch('/api/status');
    const data = await res.json();

    const dot = document.getElementById('globalStatusDot');
    const txt = document.getElementById('globalStatusText');

    if (data.hasActiveHosteller && data.activeSession) {
      dot.className = 'dot online';
      txt.textContent = `Campus Relay LIVE (${data.activeSession.sessionCode} • ${formatDuration(data.activeSession.remainingSeconds)} left)`;
    } else {
      dot.className = 'dot offline';
      txt.textContent = `No Active Hosteller Connected Yet`;
    }

    // Update Scholar Active Box
    const scholarBox = document.getElementById('scholarActiveBox');
    if (data.hasActiveHosteller && data.activeSession) {
      const s = data.activeSession;
      scholarBox.innerHTML = `
        <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:0.6rem;">
          <div>
            <span class="badge active">● Campus Relay LIVE</span>
            <h4 style="margin-top:0.35rem;">Hosted by ${s.hostellerName} — Session Code: <span class="code-chip">${s.sessionCode}</span></h4>
            <p class="hint" style="margin-top:0.2rem;">Auto-Closes in: <b>${formatDuration(s.remainingSeconds)}</b> • Turn ON your Chrome Extension &amp; Day Scholar App!</p>
          </div>
          <div>
            <span class="code-chip">Allowed: ${s.allowedDomains.join(', ')}</span>
          </div>
        </div>
      `;
    } else {
      scholarBox.innerHTML = `
        <p class="muted" style="margin:0;">⏳ Waiting for a Hosteller on College Wi-Fi to click <b>"Approve &amp; Download GUI App"</b> and run it.</p>
      `;
    }

    // Render Scholar Requests List
    const scholarList = document.getElementById('scholarRequestsList');
    if (!data.requests || data.requests.length === 0) {
      scholarList.innerHTML = `<p class="hint">No requests posted yet.</p>`;
    } else {
      scholarList.innerHTML = data.requests
        .map(
          (r) => `
        <div class="req-item">
          <div class="req-meta">
            <h4>
              <span>${r.scholarName}</span>
              <span class="badge ${r.status}">${r.status}</span>
              <span class="code-chip">${r.sessionCode}</span>
            </h4>
            <p>${r.purpose} • ${r.durationMinutes} mins</p>
          </div>
          <div>
            ${
              r.status === 'pending'
                ? `<button class="btn btn-sm btn-primary" onclick="downloadHostAppForRequest('${r.id}', '${r.scholarName.replace(/'/g, '')}', ${r.durationMinutes}, '${r.sessionCode}')">⬇️ Host GUI App (${r.durationMinutes}m)</button>`
                : r.status === 'active'
                ? `<span class="badge active">🟢 Live Now</span>`
                : ''
            }
          </div>
        </div>
      `
        )
        .join('');
    }

    // Render Hosteller Pending List
    const pendingList = document.getElementById('hostellerPendingList');
    const pendingReqs = (data.requests || []).filter((r) => r.status === 'pending');
    if (pendingReqs.length === 0) {
      pendingList.innerHTML = `<p class="hint">No pending Day Scholar requests right now. You can download an Instant Hosteller GUI App below!</p>`;
    } else {
      pendingList.innerHTML = pendingReqs
        .map(
          (r) => `
        <div class="req-item">
          <div class="req-meta">
            <h4>${r.scholarName} <span class="code-chip">${r.sessionCode}</span></h4>
            <p>${r.purpose} • Auto-close timer: <b>${r.durationMinutes} mins</b></p>
          </div>
          <div style="display:flex;gap:0.4rem;">
            <button class="btn btn-sm btn-primary" onclick="downloadHostAppForRequest('${r.id}', '${r.scholarName.replace(/'/g, '')}', ${r.durationMinutes}, '${r.sessionCode}')">⬇️ Approve &amp; Download GUI App</button>
            <button class="btn btn-sm btn-danger" onclick="revokeSession('${r.id}')">Dismiss</button>
          </div>
        </div>
      `
        )
        .join('');
    }

    // Render Active Hosteller Sessions & Audit Log
    const activeContainer = document.getElementById('activeHostSessions');
    const logConsole = document.getElementById('tunnelAuditLog');

    if (!data.activeSessions || data.activeSessions.length === 0) {
      activeContainer.innerHTML = `<div class="status-box"><p class="muted" style="margin:0;">No Hosteller laptop is currently sharing Campus Wi-Fi.</p></div>`;
      logConsole.innerHTML = `<div class="log-empty">Waiting for a Hosteller Desktop App to connect...</div>`;
    } else {
      activeContainer.innerHTML = data.activeSessions
        .map(
          (s) => `
        <div class="req-item" style="margin-bottom:0.6rem;">
          <div class="req-meta">
            <h4>
              <span>🏠 ${s.hostellerName}</span>
              <span class="code-chip">${s.sessionCode}</span>
              <span class="badge active">⏱ ${formatDuration(s.remainingSeconds)} left</span>
            </h4>
            <p>Connections: <b>${s.stats.connections}</b> • Traffic Relayed: <b>${formatBytes(s.stats.bytesRelayed)}</b> • Blocked Non-College: <b>${s.stats.blockedAttempts || 0}</b></p>
          </div>
          <button class="btn btn-sm btn-danger" onclick="revokeSession('${s.sessionCode}')">🛑 Stop Sharing</button>
        </div>
      `
        )
        .join('');

      const allLogs = data.activeSessions.flatMap((s) => s.recentLogs || []);
      if (allLogs.length > 0) {
        logConsole.innerHTML = allLogs
          .map(
            (l) => `
          <div class="log-line">
            <span class="log-time">[${new Date(l.time).toLocaleTimeString()}]</span>
            <span>${l.detail}</span>
          </div>
        `
          )
          .join('');
      }
    }
  } catch (err) {
    console.error('Status poll failed:', err);
  }
}

refreshStatus();
setInterval(refreshStatus, 2000);
