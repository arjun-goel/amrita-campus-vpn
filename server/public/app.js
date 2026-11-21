// Amrita Campus Bridge — Web Platform Frontend Logic

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

// Modal logic for Hosteller Approve & Host command
const hostModal = document.getElementById('hostModal');
const hostModalDesc = document.getElementById('hostModalDesc');
const hostModalCmd = document.getElementById('hostModalCmd');

window.openApproveModal = function (reqId, scholarName, duration, sessionCode) {
  const serverOrigin = window.location.origin;
  hostModalDesc.textContent = `Approving ${scholarName}'s request for ${duration} minutes (Session Code: ${sessionCode}). Run this command on your laptop connected to Amrita College Wi-Fi:`;
  hostModalCmd.textContent = `node hosteller-agent/agent.js --server ${serverOrigin} --request ${reqId} --session ${sessionCode} --duration ${duration}`;
  hostModal.classList.remove('hidden');
};

window.revokeSession = async function (code) {
  await fetch(`/api/sessions/${encodeURIComponent(code)}`, { method: 'DELETE' });
  refreshStatus();
};

document.getElementById('closeModalBtn').addEventListener('click', () => {
  hostModal.classList.add('hidden');
});

document.getElementById('copyModalCmdBtn').addEventListener('click', () => {
  navigator.clipboard.writeText(hostModalCmd.textContent);
  document.getElementById('copyModalCmdBtn').textContent = 'Copied!';
  setTimeout(() => (document.getElementById('copyModalCmdBtn').textContent = 'Copy'), 1500);
});

document.getElementById('copyInstantCmdBtn').addEventListener('click', () => {
  const cmd = document.getElementById('instantHostCmd').textContent;
  navigator.clipboard.writeText(cmd);
  document.getElementById('copyInstantCmdBtn').textContent = 'Copied!';
  setTimeout(() => (document.getElementById('copyInstantCmdBtn').textContent = 'Copy Command'), 1500);
});

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
      txt.textContent = `No Active Hosteller Connected (Proxy Port ${data.proxyPort} Standby)`;
    }

    // Update Scholar Active Box
    const scholarBox = document.getElementById('scholarActiveBox');
    if (data.hasActiveHosteller && data.activeSession) {
      const s = data.activeSession;
      scholarBox.innerHTML = `
        <div style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:0.6rem;">
          <div>
            <span class="badge active">● Campus Relay Ready</span>
            <h4 style="margin-top:0.35rem;">Hosted by ${s.hostellerName} — Code: <span class="code-chip">${s.sessionCode}</span></h4>
            <p class="hint" style="margin-top:0.2rem;">Time Remaining: <b>${formatDuration(s.remainingSeconds)}</b> • Proxy: <code>127.0.0.1:${data.proxyPort}</code></p>
          </div>
          <div>
            <span class="code-chip">Domains: ${s.allowedDomains.join(', ')}</span>
          </div>
        </div>
      `;
    } else {
      scholarBox.innerHTML = `
        <p class="muted" style="margin:0;">⏳ Waiting for a Hosteller on College Wi-Fi to approve your request or start <code>Start-Hosteller-Relay.bat</code>.</p>
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
                ? `<button class="btn btn-sm" onclick="openApproveModal('${r.id}', '${r.scholarName.replace(/'/g, '')}', ${r.durationMinutes}, '${r.sessionCode}')">Host Commands</button>`
                : r.status === 'active'
                ? `<span class="badge active">Connected</span>`
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
      pendingList.innerHTML = `<p class="hint">No pending Day Scholar requests right now. You can still start an Instant Open Relay below!</p>`;
    } else {
      pendingList.innerHTML = pendingReqs
        .map(
          (r) => `
        <div class="req-item">
          <div class="req-meta">
            <h4>${r.scholarName} <span class="code-chip">${r.sessionCode}</span></h4>
            <p>${r.purpose} • Requested <b>${r.durationMinutes} mins</b></p>
          </div>
          <div style="display:flex;gap:0.4rem;">
            <button class="btn btn-sm btn-primary" onclick="openApproveModal('${r.id}', '${r.scholarName.replace(/'/g, '')}', ${r.durationMinutes}, '${r.sessionCode}')">✅ Approve &amp; Host</button>
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
      logConsole.innerHTML = `<div class="log-empty">Waiting for a Hosteller agent to connect...</div>`;
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
