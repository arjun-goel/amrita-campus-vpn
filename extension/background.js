/**
 * Amrita Campus Bridge VPN — Manifest V3 Background Service Worker
 *
 * Manages chrome.proxy.settings using a dynamic PAC (Proxy Auto-Config) script
 * so only whitelisted college websites (*.amrita.edu, intranet IPs) route through
 * the Hosteller Campus Relay while personal browsing goes DIRECT.
 */

const DEFAULT_STATE = {
  vpnEnabled: false,
  routingMode: 'split', // 'split' (only college domains) | 'all'
  brokerUrl: 'http://127.0.0.1:3000',
  proxyHost: '127.0.0.1',
  proxyPort: 8080,
  sessionCode: '',
  allowedDomains: [
    '*.amrita.edu',
    'amrita.edu',
    '*.amritavidyalayam.org'
  ],
  connectedSince: null,
  expiresAt: null,
  hostellerName: null
};

async function getState() {
  const stored = await chrome.storage.local.get(Object.keys(DEFAULT_STATE));
  return { ...DEFAULT_STATE, ...stored };
}

async function setState(patch) {
  await chrome.storage.local.set(patch);
  return getState();
}

/**
 * Generates a PAC script that routes only matching domains through the campus proxy
 */
function buildPacScript(proxyHost, proxyPort, allowedDomains, routingMode) {
  const cleanHost = (proxyHost || '127.0.0.1').replace(/[^a-zA-Z0-9.\-_]/g, '');
  const cleanPort = Number(proxyPort) || 8080;

  if (routingMode === 'all') {
    return `
      function FindProxyForURL(url, host) {
        if (isPlainHostName(host) || host === "localhost" || host === "127.0.0.1") {
          return "DIRECT";
        }
        return "PROXY ${cleanHost}:${cleanPort}; DIRECT";
      }
    `;
  }

  const patternsJson = JSON.stringify(
    (allowedDomains || DEFAULT_STATE.allowedDomains).map((d) => d.toLowerCase().trim()).filter(Boolean)
  );

  return `
    function FindProxyForURL(url, host) {
      host = (host || "").toLowerCase();
      var patterns = ${patternsJson};
      for (var i = 0; i < patterns.length; i++) {
        var p = patterns[i];
        if (!p) continue;
        if (host === p) {
          return "PROXY ${cleanHost}:${cleanPort}";
        }
        if (p.indexOf("*.") === 0) {
          var suffix = p.substring(2);
          if (host === suffix || shExpMatch(host, "*." + suffix)) {
            return "PROXY ${cleanHost}:${cleanPort}";
          }
        } else if (shExpMatch(host, p)) {
          return "PROXY ${cleanHost}:${cleanPort}";
        }
      }
      return "DIRECT";
    }
  `;
}

async function applyProxySettings(state) {
  if (!state.vpnEnabled) {
    await chrome.proxy.settings.clear({ scope: 'regular' });
    await chrome.action.setBadgeText({ text: 'OFF' });
    await chrome.action.setBadgeBackgroundColor({ color: '#475569' });
    return;
  }

  const pacData = buildPacScript(
    state.proxyHost,
    state.proxyPort,
    state.allowedDomains,
    state.routingMode
  );

  await chrome.proxy.settings.set({
    value: {
      mode: 'pac_script',
      pacScript: {
        data: pacData
      }
    },
    scope: 'regular'
  });

  await chrome.action.setBadgeText({ text: 'ON' });
  await chrome.action.setBadgeBackgroundColor({ color: '#10b981' });
}

// Periodic health check alarm every 30 seconds to auto-disconnect if Hosteller session expires
chrome.alarms.create('amrita_session_check', { periodInMinutes: 0.5 });

chrome.alarms.onAlarm.addListener(async (alarm) => {
  if (alarm.name !== 'amrita_session_check') return;
  const state = await getState();
  if (!state.vpnEnabled) return;

  // Check local expiration timestamp
  if (state.expiresAt && Date.now() >= state.expiresAt) {
    const next = await setState({
      vpnEnabled: false,
      connectedSince: null,
      expiresAt: null,
      hostellerName: null
    });
    await applyProxySettings(next);
    return;
  }

  // Also query Broker Server status if reachable
  try {
    const url = `${state.brokerUrl.replace(/\/$/, '')}/api/status?sessionCode=${encodeURIComponent(state.sessionCode || '')}`;
    const res = await fetch(url);
    const data = await res.json();
    if (data.ok && !data.hasActiveHosteller) {
      const next = await setState({
        vpnEnabled: false,
        connectedSince: null,
        expiresAt: null,
        hostellerName: null
      });
      await applyProxySettings(next);
    } else if (data.ok && data.activeSession) {
      await setState({
        expiresAt: data.activeSession.expiresAt,
        hostellerName: data.activeSession.hostellerName,
        sessionCode: data.activeSession.sessionCode
      });
    }
  } catch (_) {
    // Keep current proxy settings if offline or using direct proxy mode
  }
});

chrome.runtime.onInstalled.addListener(async () => {
  const state = await getState();
  await setState(state);
  await applyProxySettings(state);
});

chrome.runtime.onStartup.addListener(async () => {
  const state = await getState();
  await applyProxySettings(state);
});

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    if (msg.type === 'GET_STATE') {
      const state = await getState();
      sendResponse({ ok: true, state });
    } else if (msg.type === 'TOGGLE_VPN') {
      const current = await getState();
      const targetEnabled = typeof msg.enabled === 'boolean' ? msg.enabled : !current.vpnEnabled;

      const patch = {
        vpnEnabled: targetEnabled,
        ...(msg.patch || {})
      };

      if (targetEnabled) {
        patch.connectedSince = Date.now();
      } else {
        patch.connectedSince = null;
      }

      const updated = await setState(patch);
      await applyProxySettings(updated);
      sendResponse({ ok: true, state: updated });
    } else if (msg.type === 'UPDATE_SETTINGS') {
      const updated = await setState(msg.patch || {});
      if (updated.vpnEnabled) {
        await applyProxySettings(updated);
      }
      sendResponse({ ok: true, state: updated });
    } else {
      sendResponse({ ok: false, error: 'Unknown message type' });
    }
  })();
  return true;
});
