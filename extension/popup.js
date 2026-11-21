// Amrita Campus Bridge VPN — Extension Popup Controller

let currentState = null;
let latestBrokerData = null;

// Tab navigation
document.querySelectorAll('.nav-tab').forEach((tab) => {
  tab.addEventListener('click', () => {
    document.querySelectorAll('.nav-tab').forEach((t) => t.classList.remove('active'));
    document.querySelectorAll('.panel').forEach((p) => p.classList.remove('active'));
    tab.classList.add('active');
    document.getElementById(`panel-${tab.dataset.panel}`).classList.add('active');
  });
});

function formatTime(sec) {
  const s = Math.max(0, Number(sec) || 0);
  const m = Math.floor(s / 60);
  const r = s % 60;
  return `${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}`;
}

function sendMsg(msg) {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage(msg, (res) => resolve(res || { ok: false }));
  });
}

function renderState(state) {
  currentState = state;
  const powerCard = document.getElementById('powerCard');
  const vpnBadge = document.getElementById('vpnBadge');
  const headline = document.getElementById('connectionHeadline');
  const subtext = document.getElementById('connectionSubtext');
  const timerPill = document.getElementById('timerPill');
  const timerText = document.getElementById('timerText');
  const hostellerLabel = document.getElementById('hostellerLabel');

  if (state.vpnEnabled) {
    powerCard.classList.add('connected');
    vpnBadge.textContent = 'CONNECTED';
    vpnBadge.className = 'status-badge on';
    headline.textContent = 'Campus Wi-Fi Tunnel ON';
    subtext.textContent =
      state.routingMode === 'split'
        ? `Routing ${state.allowedDomains.length} college domain rules via ${state.proxyHost}:${state.proxyPort}`
        : `Routing all browser traffic via ${state.proxyHost}:${state.proxyPort}`;

    if (state.expiresAt) {
      const rem = Math.max(0, Math.floor((state.expiresAt - Date.now()) / 1000));
      timerPill.classList.remove('hidden');
      timerText.textContent = formatTime(rem);
      hostellerLabel.textContent = state.hostellerName || state.sessionCode || 'Campus Host';
    } else {
      timerPill.classList.add('hidden');
    }
  } else {
    powerCard.classList.remove('connected');
    vpnBadge.textContent = 'DISCONNECTED';
    vpnBadge.className = 'status-badge off';
    headline.textContent = 'Campus Proxy is OFF';
    subtext.textContent = 'Using your regular network directly';
    timerPill.classList.add('hidden');
  }

  // Populate inputs if not actively focused
  const codeInput = document.getElementById('sessionCodeInput');
  if (document.activeElement !== codeInput) {
    codeInput.value = state.sessionCode || '';
  }

  const routingSelect = document.getElementById('routingModeSelect');
  routingSelect.value = state.routingMode || 'split';

  const brokerInput = document.getElementById('brokerUrlInput');
  if (document.activeElement !== brokerInput) brokerInput.value = state.brokerUrl || 'https://amrita-campus-vpn.onrender.com';

  const hostInput = document.getElementById('proxyHostInput');
  if (document.activeElement !== hostInput) hostInput.value = state.proxyHost || '127.0.0.1';

  const portInput = document.getElementById('proxyPortInput');
  if (document.activeElement !== portInput) portInput.value = state.proxyPort || 8080;

  document.getElementById('openDashboardLink').href = state.brokerUrl || 'https://amrita-campus-vpn.onrender.com';
  const dlLink = document.getElementById('downloadDesktopAppLink');
  if (dlLink) {
    const base = (state.brokerUrl || 'https://amrita-campus-vpn.onrender.com').replace(/\/$/, '');
    dlLink.href = `${base}/AmritaCampusBridge.exe`;
  }

  renderDomains(state.allowedDomains || []);
}

function renderDomains(domains) {
  const list = document.getElementById('domainList');
  list.innerHTML = domains
    .map(
      (d, idx) => `
      <li class="domain-item">
        <span>${d}</span>
        <button class="domain-remove" data-idx="${idx}" title="Remove">✕</button>
      </li>
    `
    )
    .join('');

  list.querySelectorAll('.domain-remove').forEach((btn) => {
    btn.addEventListener('click', async () => {
      const idx = Number(btn.dataset.idx);
      const updated = (currentState.allowedDomains || []).filter((_, i) => i !== idx);
      const res = await sendMsg({ type: 'UPDATE_SETTINGS', patch: { allowedDomains: updated } });
      if (res.ok) renderState(res.state);
    });
  });
}

// Poll Broker Server status
async function checkBrokerStatus() {
  if (!currentState) return;
  const dot = document.getElementById('brokerDot');
  const title = document.getElementById('brokerStatusTitle');
  const detail = document.getElementById('brokerStatusDetail');

  try {
    const code = document.getElementById('sessionCodeInput').value.trim() || currentState.sessionCode || '';
    const baseUrl = (currentState.brokerUrl || 'http://127.0.0.1:3000').replace(/\/$/, '');
    const res = await fetch(`${baseUrl}/api/status?sessionCode=${encodeURIComponent(code)}`);
    const data = await res.json();
    latestBrokerData = data;

    if (data.ok && data.hasActiveHosteller && data.activeSession) {
      const s = data.activeSession;
      dot.className = 'dot online';
      title.textContent = `Hosteller Online: ${s.hostellerName} (${s.sessionCode})`;
      detail.textContent = `Ready to tunnel *.amrita.edu • ${formatTime(s.remainingSeconds)} left on session.`;
    } else {
      dot.className = 'dot offline';
      title.textContent = 'Platform Online — Waiting for Hosteller';
      detail.textContent = 'Use "Ask Hosteller" tab or start Start-Hosteller-Relay.bat on campus Wi-Fi.';
    }
  } catch (err) {
    dot.className = 'dot offline';
    title.textContent = 'Broker Server Unreachable';
    detail.textContent = `Run "npm start" or Start-Platform-Server.bat (${currentState.brokerUrl}).`;
  }
}

// Toggle VPN Button
document.getElementById('toggleVpnBtn').addEventListener('click', async () => {
  if (!currentState) return;
  const code = document.getElementById('sessionCodeInput').value.trim().toUpperCase();
  const patch = { sessionCode: code };

  if (!currentState.vpnEnabled && latestBrokerData && latestBrokerData.activeSession) {
    patch.expiresAt = latestBrokerData.activeSession.expiresAt;
    patch.hostellerName = latestBrokerData.activeSession.hostellerName;
    if (!patch.sessionCode) patch.sessionCode = latestBrokerData.activeSession.sessionCode;
  }

  const res = await sendMsg({
    type: 'TOGGLE_VPN',
    enabled: !currentState.vpnEnabled,
    patch
  });
  if (res.ok) {
    renderState(res.state);
    checkBrokerStatus();
  }
});

// Verify Tunnel Button
document.getElementById('testRouteBtn').addEventListener('click', async () => {
  const btn = document.getElementById('testRouteBtn');
  btn.textContent = 'Checking...';
  await checkBrokerStatus();
  if (latestBrokerData && latestBrokerData.hasActiveHosteller) {
    btn.textContent = '✅ Relay Ready!';
  } else {
    btn.textContent = '⚠️ No Host';
  }
  setTimeout(() => (btn.textContent = 'Verify Tunnel'), 1800);
});

// Send Request to Hostellers right from Extension
document.getElementById('sendRequestBtn').addEventListener('click', async () => {
  const scholarName = document.getElementById('reqName').value.trim() || 'Day Scholar';
  const purpose = document.getElementById('reqPurpose').value;
  const durationMinutes = Number(document.getElementById('reqDuration').value) || 30;
  const feedback = document.getElementById('reqFeedback');

  try {
    const baseUrl = (currentState.brokerUrl || 'http://127.0.0.1:3000').replace(/\/$/, '');
    const res = await fetch(`${baseUrl}/api/requests`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        scholarName,
        purpose,
        durationMinutes,
        allowedDomains: currentState.allowedDomains
      })
    });
    const data = await res.json();
    if (data.ok) {
      const code = data.request.sessionCode;
      const updateRes = await sendMsg({
        type: 'UPDATE_SETTINGS',
        patch: { sessionCode: code }
      });
      if (updateRes.ok) renderState(updateRes.state);

      feedback.classList.remove('hidden');
      feedback.innerHTML = `✅ Request Sent! Your Session Code is <b>${code}</b>.<br/>As soon as a Hosteller clicks Approve, click the Power button to connect!`;
    }
  } catch (e) {
    feedback.classList.remove('hidden');
    feedback.textContent = '❌ Could not reach Broker Server. Make sure npm start is running.';
  }
});

// Add custom domain
document.getElementById('addDomainBtn').addEventListener('click', async () => {
  const input = document.getElementById('newDomainInput');
  const val = input.value.trim().toLowerCase();
  if (!val || !currentState) return;
  const list = Array.from(new Set([...(currentState.allowedDomains || []), val]));
  input.value = '';
  const res = await sendMsg({ type: 'UPDATE_SETTINGS', patch: { allowedDomains: list } });
  if (res.ok) renderState(res.state);
});

// Routing mode change
document.getElementById('routingModeSelect').addEventListener('change', async (e) => {
  const res = await sendMsg({ type: 'UPDATE_SETTINGS', patch: { routingMode: e.target.value } });
  if (res.ok) renderState(res.state);
});

// Save Network Settings
document.getElementById('saveSettingsBtn').addEventListener('click', async () => {
  const brokerUrl = document.getElementById('brokerUrlInput').value.trim() || 'http://127.0.0.1:3000';
  const proxyHost = document.getElementById('proxyHostInput').value.trim() || '127.0.0.1';
  const proxyPort = Number(document.getElementById('proxyPortInput').value) || 8080;

  const res = await sendMsg({
    type: 'UPDATE_SETTINGS',
    patch: { brokerUrl, proxyHost, proxyPort }
  });
  if (res.ok) {
    renderState(res.state);
    const btn = document.getElementById('saveSettingsBtn');
    btn.textContent = '✅ Saved!';
    setTimeout(() => (btn.textContent = '💾 Save Network Settings'), 1500);
    checkBrokerStatus();
  }
});

// Initialize popup
(async () => {
  const res = await sendMsg({ type: 'GET_STATE' });
  if (res.ok) {
    renderState(res.state);
    await checkBrokerStatus();
  }
  setInterval(async () => {
    const r = await sendMsg({ type: 'GET_STATE' });
    if (r.ok) renderState(r.state);
    checkBrokerStatus();
  }, 2000);
})();
