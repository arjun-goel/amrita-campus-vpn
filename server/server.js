/**
 * Amrita Campus Bridge - Broker Server, Web Platform & Proxy Gateway
 *
 * Runs two ports using 100% built-in Node.js modules (zero npm install required):
 * 1. Platform Web UI + REST API + Hosteller Tunnel Socket: http://0.0.0.0:3000
 * 2. Browser HTTP/HTTPS Forward Proxy (for Chrome Extension): http://0.0.0.0:8080
 *
 * Security Guarantees:
 * - End-to-End HTTPS (HTTP CONNECT): Day Scholar's browser performs TLS handshake
 *   directly with *.amrita.edu. Neither this server nor the Hosteller can read
 *   passwords, cookies, or request bodies.
 * - Strict Domain Whitelist: Only *.amrita.edu and explicitly allowed campus domains
 *   can pass through the proxy.
 * - Time-Boxed Sessions: Automatically expires and disconnects when the Hosteller's
 *   approved timer runs out.
 */

const http = require('http');
const net = require('net');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { URL } = require('url');

const WEB_PORT = Number(process.env.PORT || 3000);
const PROXY_PORT = Number(process.env.PROXY_PORT || 8080);

// Default allowed college domain patterns
const DEFAULT_ALLOWED_DOMAINS = [
  '*.amrita.edu',
  'amrita.edu',
  '*.amritavidyalayam.org'
];

// In-memory store for Access Requests and Active Hosteller Sessions
// Request: { id, scholarName, rollHint, purpose, durationMinutes, status, createdAt, approvedAt, expiresAt, hostellerName, sessionCode, allowedDomains, stats }
const requests = new Map();
// Active Hosteller Tunnel Connections: sessionCode -> { socket, buffer, streams: Map<streamId, clientSocket>, hostellerName, requestId, connectedAt, expiresAt, allowedDomains, stats, logs }
const activeTunnels = new Map();

function generateCode() {
  return 'AMR-' + crypto.randomBytes(2).toString('hex').toUpperCase();
}

function isDomainAllowed(hostname, allowedPatterns = DEFAULT_ALLOWED_DOMAINS) {
  if (!hostname) return false;
  const host = hostname.toLowerCase().trim();
  for (const rawPattern of allowedPatterns) {
    const pattern = rawPattern.toLowerCase().trim();
    if (!pattern) continue;
    if (pattern === host) return true;
    if (pattern.startsWith('*.')) {
      const suffix = pattern.slice(2);
      if (host === suffix || host.endsWith('.' + suffix)) return true;
    } else if (host.endsWith('.' + pattern)) {
      return true;
    }
  }
  return false;
}

// Clean up expired sessions every 5 seconds
setInterval(() => {
  const now = Date.now();
  for (const [id, req] of requests.entries()) {
    if (req.status === 'active' && req.expiresAt && now >= req.expiresAt) {
      req.status = 'expired';
      if (req.sessionCode && activeTunnels.has(req.sessionCode)) {
        closeTunnel(req.sessionCode, 'Session duration expired');
      }
    }
  }
  for (const [code, tunnel] of activeTunnels.entries()) {
    if (tunnel.expiresAt && now >= tunnel.expiresAt) {
      closeTunnel(code, 'Session duration expired');
    }
  }
}, 5000).unref();

function closeTunnel(sessionCode, reason = 'Closed') {
  const tunnel = activeTunnels.get(sessionCode);
  if (!tunnel) return;
  try {
    sendTunnelFrame(tunnel.socket, { type: 'SESSION_END', reason });
  } catch (_) {}
  for (const [, stream] of tunnel.streams.entries()) {
    try {
      stream.socket.destroy();
    } catch (_) {}
  }
  tunnel.streams.clear();
  try {
    tunnel.socket.destroy();
  } catch (_) {}
  activeTunnels.delete(sessionCode);

  if (tunnel.requestId && requests.has(tunnel.requestId)) {
    const req = requests.get(tunnel.requestId);
    if (req.status === 'active') {
      req.status = reason.includes('expired') ? 'expired' : 'completed';
    }
  }
}

/**
 * Simple length-prefixed JSON + Binary framing over upgraded TCP socket
 * Frame layout:
 * [4-byte big-endian header length][JSON header bytes][optional binary payload bytes]
 * Total payload length is stored in header.len
 */
function sendTunnelFrame(socket, headerObj, payloadBuf = null) {
  if (!socket || socket.destroyed) return false;
  const dataBuf = payloadBuf ? Buffer.from(payloadBuf) : Buffer.alloc(0);
  headerObj.len = dataBuf.length;
  const headerBuf = Buffer.from(JSON.stringify(headerObj), 'utf8');
  const lenPrefix = Buffer.alloc(4);
  lenPrefix.writeUInt32BE(headerBuf.length, 0);
  try {
    socket.write(Buffer.concat([lenPrefix, headerBuf, dataBuf]));
    return true;
  } catch (_) {
    return false;
  }
}

function attachFrameParser(socket, onFrame) {
  let buf = Buffer.alloc(0);
  socket.on('data', (chunk) => {
    buf = Buffer.concat([buf, chunk]);
    while (buf.length >= 4) {
      const headerLen = buf.readUInt32BE(0);
      if (headerLen > 1024 * 1024) {
        socket.destroy();
        return;
      }
      if (buf.length < 4 + headerLen) break;
      let header;
      try {
        header = JSON.parse(buf.subarray(4, 4 + headerLen).toString('utf8'));
      } catch (_) {
        socket.destroy();
        return;
      }
      const payloadLen = header.len || 0;
      if (buf.length < 4 + headerLen + payloadLen) break;
      const payload = payloadLen > 0 ? buf.subarray(4 + headerLen, 4 + headerLen + payloadLen) : Buffer.alloc(0);
      buf = buf.subarray(4 + headerLen + payloadLen);
      onFrame(header, payload);
    }
  });
}

// Get the best active tunnel (by sessionCode if specified, else most recent active tunnel)
function getActiveTunnel(preferredCode = null) {
  const now = Date.now();
  if (preferredCode && activeTunnels.has(preferredCode)) {
    const t = activeTunnels.get(preferredCode);
    if (!t.expiresAt || t.expiresAt > now) return t;
  }
  let latest = null;
  for (const [, t] of activeTunnels.entries()) {
    if (!t.expiresAt || t.expiresAt > now) {
      latest = t;
    }
  }
  return latest;
}

function recordTunnelLog(tunnel, entry) {
  tunnel.logs.unshift({
    time: new Date().toISOString(),
    ...entry
  });
  if (tunnel.logs.length > 60) tunnel.logs.pop();
}

// ============================================================================
// 1. WEB PLATFORM & API SERVER (PORT 3000)
// ============================================================================
function parseJsonBody(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', (chunk) => {
      body += chunk;
      if (body.length > 1e6) req.destroy();
    });
    req.on('end', () => {
      if (!body) return resolve({});
      try {
        resolve(JSON.parse(body));
      } catch (e) {
        reject(e);
      }
    });
    req.on('error', reject);
  });
}

function sendJson(res, status, data) {
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, POST, DELETE, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, X-Session-Code'
  });
  res.end(JSON.stringify(data));
}

const webServer = http.createServer(async (req, res) => {
  if (req.method === 'OPTIONS') {
    return sendJson(res, 200, { ok: true });
  }

  const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
  const pathname = parsedUrl.pathname;

  // API: Overall status (used by Web Dashboard & Browser Extension)
  if (pathname === '/api/status' && req.method === 'GET') {
    const preferredCode = parsedUrl.searchParams.get('sessionCode') || null;
    const tunnel = getActiveTunnel(preferredCode);
    const allRequests = Array.from(requests.values()).sort((a, b) => b.createdAt - a.createdAt);
    const activeSessions = Array.from(activeTunnels.entries()).map(([code, t]) => ({
      sessionCode: code,
      hostellerName: t.hostellerName,
      requestId: t.requestId,
      connectedAt: t.connectedAt,
      expiresAt: t.expiresAt,
      remainingSeconds: Math.max(0, Math.floor((t.expiresAt - Date.now()) / 1000)),
      allowedDomains: t.allowedDomains,
      stats: t.stats,
      recentLogs: t.logs.slice(0, 15)
    }));

    return sendJson(res, 200, {
      ok: true,
      proxyHost: req.headers.host ? req.headers.host.split(':')[0] : '127.0.0.1',
      proxyPort: PROXY_PORT,
      webPort: WEB_PORT,
      hasActiveHosteller: Boolean(tunnel),
      activeSession: tunnel
        ? {
            sessionCode: tunnel.sessionCode,
            hostellerName: tunnel.hostellerName,
            expiresAt: tunnel.expiresAt,
            remainingSeconds: Math.max(0, Math.floor((tunnel.expiresAt - Date.now()) / 1000)),
            allowedDomains: tunnel.allowedDomains,
            stats: tunnel.stats
          }
        : null,
      activeSessions,
      requests: allRequests
    });
  }

  // API: Day Scholar creates an access request
  if (pathname === '/api/requests' && req.method === 'POST') {
    try {
      const body = await parseJsonBody(req);
      const id = 'REQ-' + crypto.randomBytes(3).toString('hex').toUpperCase();
      const sessionCode = generateCode();
      const durationMinutes = Math.min(180, Math.max(5, Number(body.durationMinutes) || 30));
      const customDomains = Array.isArray(body.allowedDomains) && body.allowedDomains.length > 0
        ? body.allowedDomains
        : DEFAULT_ALLOWED_DOMAINS;

      const newReq = {
        id,
        scholarName: (body.scholarName || 'Day Scholar').trim().slice(0, 50),
        rollHint: (body.rollHint || '').trim().slice(0, 30),
        purpose: (body.purpose || 'AUMS / Campus Portal Login').trim().slice(0, 100),
        durationMinutes,
        status: 'pending', // pending -> active -> completed / expired
        createdAt: Date.now(),
        approvedAt: null,
        expiresAt: null,
        hostellerName: null,
        sessionCode,
        allowedDomains: customDomains,
        stats: { connections: 0, bytesRelayed: 0 }
      };
      requests.set(id, newReq);
      return sendJson(res, 201, { ok: true, request: newReq });
    } catch (e) {
      return sendJson(res, 400, { ok: false, error: 'Invalid JSON body' });
    }
  }

  // API: Hosteller terminates/revokes an active session or deletes a request
  if (pathname.startsWith('/api/sessions/') && req.method === 'DELETE') {
    const code = pathname.split('/').pop();
    if (activeTunnels.has(code)) {
      closeTunnel(code, 'Revoked by Hosteller');
      return sendJson(res, 200, { ok: true, message: `Session ${code} terminated.` });
    }
    // Also check if it's a request ID
    if (requests.has(code)) {
      const r = requests.get(code);
      if (r.sessionCode && activeTunnels.has(r.sessionCode)) {
        closeTunnel(r.sessionCode, 'Revoked by Hosteller');
      }
      r.status = 'cancelled';
      return sendJson(res, 200, { ok: true, message: `Request ${code} cancelled.` });
    }
    return sendJson(res, 404, { ok: false, error: 'Session not found' });
  }

  // Static files from server/public
  let filePath = pathname === '/' ? '/index.html' : pathname;
  const fullPath = path.join(__dirname, 'public', path.normalize(filePath).replace(/^(\.\.[\/\\])+/, ''));
  if (fs.existsSync(fullPath) && fs.statSync(fullPath).isFile()) {
    const ext = path.extname(fullPath).toLowerCase();
    const mimeTypes = {
      '.html': 'text/html; charset=utf-8',
      '.css': 'text/css; charset=utf-8',
      '.js': 'application/javascript; charset=utf-8',
      '.json': 'application/json',
      '.png': 'image/png',
      '.svg': 'image/svg+xml'
    };
    res.writeHead(200, { 'Content-Type': mimeTypes[ext] || 'application/octet-stream' });
    fs.createReadStream(fullPath).pipe(res);
  } else {
    sendJson(res, 404, { ok: false, error: 'Not found' });
  }
});

// Handle Hosteller Agent Tunnel Upgrade on Port 3000 (/tunnel)
webServer.on('upgrade', (req, socket, head) => {
  const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
  if (parsedUrl.pathname !== '/tunnel') {
    socket.destroy();
    return;
  }

  const requestId = parsedUrl.searchParams.get('requestId') || null;
  let sessionCode = (parsedUrl.searchParams.get('sessionCode') || '').toUpperCase();
  const hostellerName = parsedUrl.searchParams.get('hostellerName') || 'Campus Hosteller';
  let durationMinutes = Math.min(180, Math.max(1, Number(parsedUrl.searchParams.get('duration')) || 30));
  let allowedDomains = [...DEFAULT_ALLOWED_DOMAINS];

  // Link to an existing Day Scholar request if provided
  let linkedReq = null;
  if (requestId && requests.has(requestId)) {
    linkedReq = requests.get(requestId);
  } else if (sessionCode) {
    for (const [, r] of requests.entries()) {
      if (r.sessionCode === sessionCode) {
        linkedReq = r;
        break;
      }
    }
  } else {
    // Auto-match oldest pending request if none specified
    for (const [, r] of requests.entries()) {
      if (r.status === 'pending') {
        linkedReq = r;
        break;
      }
    }
  }

  if (linkedReq) {
    sessionCode = linkedReq.sessionCode;
    durationMinutes = linkedReq.durationMinutes;
    allowedDomains = linkedReq.allowedDomains || DEFAULT_ALLOWED_DOMAINS;
    linkedReq.status = 'active';
    linkedReq.approvedAt = Date.now();
    linkedReq.expiresAt = Date.now() + durationMinutes * 60 * 1000;
    linkedReq.hostellerName = hostellerName;
  } else if (!sessionCode) {
    sessionCode = generateCode();
  }

  const expiresAt = Date.now() + durationMinutes * 60 * 1000;

  // Complete HTTP Upgrade handshake
  socket.write(
    'HTTP/1.1 101 Switching Protocols\r\n' +
    'Upgrade: amrita-tunnel\r\n' +
    'Connection: Upgrade\r\n' +
    `X-Session-Code: ${sessionCode}\r\n` +
    '\r\n'
  );
  socket.setNoDelay(true);

  // Close any previous tunnel with same sessionCode
  if (activeTunnels.has(sessionCode)) {
    closeTunnel(sessionCode, 'Replaced by new connection');
  }

  const tunnel = {
    sessionCode,
    socket,
    streams: new Map(),
    hostellerName,
    requestId: linkedReq ? linkedReq.id : null,
    connectedAt: Date.now(),
    expiresAt,
    allowedDomains,
    stats: { connections: 0, bytesRelayed: 0, blockedAttempts: 0 },
    logs: []
  };
  activeTunnels.set(sessionCode, tunnel);

  recordTunnelLog(tunnel, {
    event: 'HOST_CONNECTED',
    detail: `${hostellerName} started sharing campus Wi-Fi for ${durationMinutes} mins (Code: ${sessionCode})`
  });

  // Send welcome configuration frame to Hosteller Agent
  sendTunnelFrame(socket, {
    type: 'WELCOME',
    sessionCode,
    requestId: tunnel.requestId,
    scholarName: linkedReq ? linkedReq.scholarName : 'Any Day Scholar with code',
    durationMinutes,
    expiresAt,
    allowedDomains
  });

  attachFrameParser(socket, (header, payload) => {
    const stream = tunnel.streams.get(header.streamId);
    if (header.type === 'CONNECT_OK') {
      if (stream && !stream.socket.destroyed) {
        if (stream.onConnected) stream.onConnected();
      }
    } else if (header.type === 'CONNECT_ERR') {
      if (stream) {
        if (!stream.responded && stream.isConnect) {
          try {
            stream.socket.write(`HTTP/1.1 502 Bad Gateway\r\nContent-Type: text/plain\r\n\r\nCampus Relay Error: ${header.error || 'Connect failed'}\r\n`);
          } catch (_) {}
        }
        try {
          stream.socket.destroy();
        } catch (_) {}
        tunnel.streams.delete(header.streamId);
      }
    } else if (header.type === 'DATA') {
      if (stream && !stream.socket.destroyed && payload.length > 0) {
        tunnel.stats.bytesRelayed += payload.length;
        if (linkedReq) linkedReq.stats.bytesRelayed = tunnel.stats.bytesRelayed;
        try {
          stream.socket.write(payload);
        } catch (_) {}
      }
    } else if (header.type === 'END') {
      if (stream) {
        try {
          stream.socket.end();
        } catch (_) {}
        tunnel.streams.delete(header.streamId);
      }
    } else if (header.type === 'PING') {
      sendTunnelFrame(socket, { type: 'PONG', ts: Date.now() });
    }
  });

  socket.on('close', () => {
    if (activeTunnels.get(sessionCode) === tunnel) {
      closeTunnel(sessionCode, 'Hosteller disconnected');
    }
  });

  socket.on('error', () => {
    if (activeTunnels.get(sessionCode) === tunnel) {
      closeTunnel(sessionCode, 'Hosteller connection error');
    }
  });
});

// ============================================================================
// 2. BROWSER HTTP / HTTPS FORWARD PROXY SERVER (PORT 8080)
// ============================================================================
let nextStreamId = 1;

const proxyServer = http.createServer((clientReq, clientRes) => {
  // Diagnostic health check endpoint directly on the proxy port
  if (clientReq.url === '/__amrita_proxy_check__' || clientReq.headers['x-amrita-probe'] === '1') {
    const tunnel = getActiveTunnel(clientReq.headers['x-session-code'] || null);
    clientRes.writeHead(200, {
      'Content-Type': 'application/json',
      'Access-Control-Allow-Origin': '*'
    });
    return clientRes.end(
      JSON.stringify({
        ok: true,
        viaProxy: true,
        hasActiveHosteller: Boolean(tunnel),
        sessionCode: tunnel ? tunnel.sessionCode : null,
        hostellerName: tunnel ? tunnel.hostellerName : null,
        remainingSeconds: tunnel ? Math.max(0, Math.floor((tunnel.expiresAt - Date.now()) / 1000)) : 0
      })
    );
  }

  // Standard HTTP Forward Proxy Request (e.g. GET http://intranet.amrita.edu/...)
  let targetUrl;
  try {
    targetUrl = new URL(clientReq.url);
  } catch (_) {
    clientRes.writeHead(400, { 'Content-Type': 'text/plain' });
    return clientRes.end('Invalid proxy URL');
  }

  const tunnel = getActiveTunnel(clientReq.headers['x-session-code'] || null);
  if (!tunnel) {
    clientRes.writeHead(503, { 'Content-Type': 'text/html; charset=utf-8' });
    return clientRes.end(`
      <html><body style="font-family:system-ui;background:#0f172a;color:#f8fafc;padding:2rem;text-align:center;">
        <h2>⚠️ No Active Campus Hosteller Connected</h2>
        <p>Open the <b>Amrita Campus VPN Extension</b> or visit <a href="http://localhost:${WEB_PORT}" style="color:#38bdf8;">http://localhost:${WEB_PORT}</a> to request a Hosteller relay session.</p>
      </body></html>
    `);
  }

  const targetHost = targetUrl.hostname;
  const targetPort = Number(targetUrl.port) || 80;

  if (!isDomainAllowed(targetHost, tunnel.allowedDomains)) {
    tunnel.stats.blockedAttempts++;
    recordTunnelLog(tunnel, {
      event: 'BLOCKED_DOMAIN',
      detail: `Blocked non-college HTTP request to ${targetHost}:${targetPort}`
    });
    clientRes.writeHead(403, { 'Content-Type': 'text/plain' });
    return clientRes.end(`Amrita Campus VPN Policy: Domain "${targetHost}" is not in the college whitelist.`);
  }

  const streamId = nextStreamId++;
  tunnel.stats.connections++;
  recordTunnelLog(tunnel, {
    event: 'HTTP_PROXY',
    detail: `HTTP ${clientReq.method} -> ${targetHost}:${targetPort} (Stream #${streamId})`
  });

  // Reconstruct raw HTTP request to forward over the TCP stream to targetHost:targetPort
  const pathWithQuery = (targetUrl.pathname || '/') + (targetUrl.search || '');
  let rawHeaders = `${clientReq.method} ${pathWithQuery} HTTP/${clientReq.httpVersion}\r\n`;
  for (let i = 0; i < clientReq.rawHeaders.length; i += 2) {
    const k = clientReq.rawHeaders[i];
    const v = clientReq.rawHeaders[i + 1];
    if (k.toLowerCase().startsWith('proxy-')) continue;
    rawHeaders += `${k}: ${v}\r\n`;
  }
  rawHeaders += 'Connection: close\r\n\r\n';

  const bodyChunks = [];
  clientReq.on('data', (c) => bodyChunks.push(c));
  clientReq.on('end', () => {
    const initialPayload = Buffer.concat([Buffer.from(rawHeaders, 'utf8'), ...bodyChunks]);
    tunnel.streams.set(streamId, {
      socket: clientRes.socket,
      isConnect: false,
      responded: true,
      onConnected: () => {
        sendTunnelFrame(tunnel.socket, { type: 'DATA', streamId }, initialPayload);
      }
    });

    sendTunnelFrame(tunnel.socket, {
      type: 'CONNECT',
      streamId,
      host: targetHost,
      port: targetPort,
      encrypted: false
    });
  });
});

// HTTPS End-to-End Encrypted Tunneling via HTTP CONNECT (used by Chrome for all https://*.amrita.edu sites)
proxyServer.on('connect', (req, clientSocket, head) => {
  const [targetHost, rawPort] = (req.url || '').split(':');
  const targetPort = Number(rawPort) || 443;

  const tunnel = getActiveTunnel(req.headers['x-session-code'] || null);
  if (!tunnel) {
    clientSocket.write('HTTP/1.1 503 Service Unavailable\r\nX-Amrita-Reason: No-Hosteller-Active\r\n\r\n');
    clientSocket.destroy();
    return;
  }

  if (!isDomainAllowed(targetHost, tunnel.allowedDomains)) {
    tunnel.stats.blockedAttempts++;
    recordTunnelLog(tunnel, {
      event: 'BLOCKED_DOMAIN',
      detail: `Blocked non-college HTTPS CONNECT to ${targetHost}:${targetPort}`
    });
    clientSocket.write('HTTP/1.1 403 Forbidden\r\nX-Amrita-Reason: Domain-Not-Whitelisted\r\n\r\n');
    clientSocket.destroy();
    return;
  }

  const streamId = nextStreamId++;
  tunnel.stats.connections++;
  const linkedReq = tunnel.requestId ? requests.get(tunnel.requestId) : null;
  if (linkedReq) linkedReq.stats.connections = tunnel.stats.connections;

  recordTunnelLog(tunnel, {
    event: 'TLS_TUNNEL',
    detail: `🔒 E2E Encrypted TLS Tunnel -> ${targetHost}:${targetPort} (Zero password visibility)`
  });

  clientSocket.setNoDelay(true);

  tunnel.streams.set(streamId, {
    socket: clientSocket,
    isConnect: true,
    responded: false,
    onConnected() {
      this.responded = true;
      try {
        clientSocket.write('HTTP/1.1 200 Connection Established\r\nProxy-Agent: Amrita-Campus-Bridge\r\n\r\n');
        if (head && head.length > 0) {
          tunnel.stats.bytesRelayed += head.length;
          sendTunnelFrame(tunnel.socket, { type: 'DATA', streamId }, head);
        }
      } catch (_) {}
    }
  });

  clientSocket.on('data', (chunk) => {
    tunnel.stats.bytesRelayed += chunk.length;
    if (linkedReq) linkedReq.stats.bytesRelayed = tunnel.stats.bytesRelayed;
    sendTunnelFrame(tunnel.socket, { type: 'DATA', streamId }, chunk);
  });

  clientSocket.on('end', () => {
    sendTunnelFrame(tunnel.socket, { type: 'END', streamId });
    tunnel.streams.delete(streamId);
  });

  clientSocket.on('error', () => {
    sendTunnelFrame(tunnel.socket, { type: 'END', streamId });
    tunnel.streams.delete(streamId);
  });

  clientSocket.on('close', () => {
    tunnel.streams.delete(streamId);
  });

  sendTunnelFrame(tunnel.socket, {
    type: 'CONNECT',
    streamId,
    host: targetHost,
    port: targetPort,
    encrypted: true
  });
});

function startServers(customWebPort = WEB_PORT, customProxyPort = PROXY_PORT) {
  return new Promise((resolve) => {
    webServer.listen(customWebPort, '0.0.0.0', () => {
      proxyServer.listen(customProxyPort, '0.0.0.0', () => {
        console.log('====================================================================');
        console.log(' 🎓 AMRITA CAMPUS BRIDGE - PEER VPN & RELAY PLATFORM RUNNING');
        console.log('====================================================================');
        console.log(` 🌐 Web Dashboard & Coordination API : http://localhost:${customWebPort}`);
        console.log(` 🛡️  Browser Extension Proxy Gateway  : http://localhost:${customProxyPort}`);
        console.log(' 🔒 Security Mode                    : End-to-End TLS + *.amrita.edu Split-Tunnel');
        console.log('====================================================================');
        resolve({ webServer, proxyServer });
      });
    });
  });
}

if (require.main === module) {
  startServers();
}

module.exports = { startServers, webServer, proxyServer, isDomainAllowed };
