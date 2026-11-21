/**
 * Amrita Campus Bridge - Broker Server, Web Platform & Proxy Gateway
 * Hosted on Render: https://amrita-campus-vpn.onrender.com
 *
 * Uses 100% standard RFC 6455 WebSocket connections (/tunnel for Hostellers,
 * /scholar for Day Scholar Local Proxy Bridge) so it works seamlessly through
 * Render / Cloudflare load balancers as well as locally!
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { URL } = require('url');
const { buildHostellerGuiBat, buildDayScholarGuiBat } = require('./gui-builder');

const WEB_PORT = Number(process.env.PORT || 3000);
const PROXY_PORT = Number(process.env.PROXY_PORT || 8080);
const IS_RENDER = Boolean(process.env.RENDER);
const WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';

const DEFAULT_ALLOWED_DOMAINS = [
  '*.amrita.edu',
  'amrita.edu',
  '*.amritavidyalayam.org'
];

const requests = new Map();
const activeTunnels = new Map(); // sessionCode -> tunnel object

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

// ============================================================================
// RFC 6455 WebSocket Handshake & Binary Framing (Works over Render / Cloudflare)
// ============================================================================
function completeWebSocketHandshake(req, socket, extraHeaders = {}) {
  const key = req.headers['sec-websocket-key'];
  let headers =
    'HTTP/1.1 101 Switching Protocols\r\n' +
    'Upgrade: websocket\r\n' +
    'Connection: Upgrade\r\n';
  if (key) {
    const accept = crypto.createHash('sha1').update(key + WS_GUID).digest('base64');
    headers += `Sec-WebSocket-Accept: ${accept}\r\n`;
  }
  for (const [k, v] of Object.entries(extraHeaders)) {
    headers += `${k}: ${v}\r\n`;
  }
  headers += '\r\n';
  socket.write(headers);
  socket.setNoDelay(true);
}

function encodeWsBinaryFrame(buffer) {
  const len = buffer.length;
  let header;
  if (len < 126) {
    header = Buffer.alloc(2);
    header[0] = 0x82; // FIN + Binary
    header[1] = len;
  } else if (len < 65536) {
    header = Buffer.alloc(4);
    header[0] = 0x82;
    header[1] = 126;
    header.writeUInt16BE(len, 2);
  } else {
    header = Buffer.alloc(10);
    header[0] = 0x82;
    header[1] = 127;
    header.writeBigUInt64BE(BigInt(len), 2);
  }
  return Buffer.concat([header, buffer]);
}

function sendTunnelFrame(socket, headerObj, payloadBuf = null) {
  if (!socket || socket.destroyed) return false;
  const dataBuf = payloadBuf ? Buffer.from(payloadBuf) : Buffer.alloc(0);
  headerObj.len = dataBuf.length;
  const headerBuf = Buffer.from(JSON.stringify(headerObj), 'utf8');
  const lenPrefix = Buffer.alloc(4);
  lenPrefix.writeUInt32BE(headerBuf.length, 0);
  const innerFrame = Buffer.concat([lenPrefix, headerBuf, dataBuf]);
  try {
    socket.write(encodeWsBinaryFrame(innerFrame));
    return true;
  } catch (_) {
    return false;
  }
}

function attachFrameParser(socket, onFrame) {
  let wsBuf = Buffer.alloc(0);
  let innerBuf = Buffer.alloc(0);

  function processInnerFrames() {
    while (innerBuf.length >= 4) {
      const headerLen = innerBuf.readUInt32BE(0);
      if (headerLen > 1024 * 1024) {
        socket.destroy();
        return;
      }
      if (innerBuf.length < 4 + headerLen) break;
      let header;
      try {
        header = JSON.parse(innerBuf.subarray(4, 4 + headerLen).toString('utf8'));
      } catch (_) {
        socket.destroy();
        return;
      }
      const payloadLen = header.len || 0;
      if (innerBuf.length < 4 + headerLen + payloadLen) break;
      const payload =
        payloadLen > 0
          ? innerBuf.subarray(4 + headerLen, 4 + headerLen + payloadLen)
          : Buffer.alloc(0);
      innerBuf = innerBuf.subarray(4 + headerLen + payloadLen);
      onFrame(header, payload);
    }
  }

  socket.on('data', (chunk) => {
    wsBuf = Buffer.concat([wsBuf, chunk]);
    while (wsBuf.length >= 2) {
      const firstByte = wsBuf[0];
      const secondByte = wsBuf[1];
      const opcode = firstByte & 0x0f;
      const isMasked = Boolean(secondByte & 0x80);
      let payloadLen = secondByte & 0x7f;
      let offset = 2;

      if (payloadLen === 126) {
        if (wsBuf.length < 4) return;
        payloadLen = wsBuf.readUInt16BE(2);
        offset = 4;
      } else if (payloadLen === 127) {
        if (wsBuf.length < 10) return;
        payloadLen = Number(wsBuf.readBigUInt64BE(2));
        offset = 10;
      }

      const maskLen = isMasked ? 4 : 0;
      if (wsBuf.length < offset + maskLen + payloadLen) return;

      let frameData = wsBuf.subarray(offset + maskLen, offset + maskLen + payloadLen);
      if (isMasked) {
        const maskKey = wsBuf.subarray(offset, offset + 4);
        const unmasked = Buffer.allocUnsafe(frameData.length);
        for (let i = 0; i < frameData.length; i++) {
          unmasked[i] = frameData[i] ^ maskKey[i & 3];
        }
        frameData = unmasked;
      }

      wsBuf = wsBuf.subarray(offset + maskLen + payloadLen);

      if (opcode === 0x08) {
        // WebSocket Close
        socket.destroy();
        return;
      } else if (opcode === 0x09) {
        // Ping -> Pong
        socket.write(Buffer.from([0x8a, 0x00]));
      } else if (opcode === 0x01 || opcode === 0x02 || opcode === 0x00) {
        innerBuf = Buffer.concat([innerBuf, frameData]);
        processInnerFrames();
      }
    }
  });
}

// ============================================================================
// Session & Tunnel Lifecycle
// ============================================================================
setInterval(() => {
  const now = Date.now();
  for (const [, req] of requests.entries()) {
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
      if (stream.socket) stream.socket.destroy();
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

// ============================================================================
// 1. WEB PLATFORM & API SERVER (PORT 3000 / Render PORT)
// ============================================================================
let nextStreamId = 1;

const webServer = http.createServer(async (req, res) => {
  if (req.method === 'OPTIONS') {
    return sendJson(res, 200, { ok: true });
  }

  const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost'}`);
  const pathname = parsedUrl.pathname;

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
      proxyHost: '127.0.0.1',
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

  if (pathname === '/api/requests' && req.method === 'POST') {
    try {
      const body = await parseJsonBody(req);
      const id = 'REQ-' + crypto.randomBytes(3).toString('hex').toUpperCase();
      const sessionCode = generateCode();
      const durationMinutes = Math.min(180, Math.max(5, Number(body.durationMinutes) || 30));
      const customDomains =
        Array.isArray(body.allowedDomains) && body.allowedDomains.length > 0
          ? body.allowedDomains
          : DEFAULT_ALLOWED_DOMAINS;

      const newReq = {
        id,
        scholarName: (body.scholarName || 'Day Scholar').trim().slice(0, 50),
        rollHint: (body.rollHint || '').trim().slice(0, 30),
        purpose: (body.purpose || 'AUMS / Campus Portal Login').trim().slice(0, 100),
        durationMinutes,
        status: 'pending',
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

  if (pathname.startsWith('/api/sessions/') && req.method === 'DELETE') {
    const code = pathname.split('/').pop();
    if (activeTunnels.has(code)) {
      closeTunnel(code, 'Revoked by Hosteller');
      return sendJson(res, 200, { ok: true, message: `Session ${code} terminated.` });
    }
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

  // API: Download 1-Click Zero-Terminal Hosteller Desktop GUI App (.bat)
  if (pathname === '/api/download/hosteller-app' && req.method === 'GET') {
    const requestId = parsedUrl.searchParams.get('requestId') || '';
    let sessionCode = (parsedUrl.searchParams.get('sessionCode') || '').toUpperCase();
    let durationMinutes = Number(parsedUrl.searchParams.get('duration')) || 30;
    let scholarName = parsedUrl.searchParams.get('scholar') || 'Day Scholar';
    const hostellerName = parsedUrl.searchParams.get('name') || 'Campus-Hosteller';

    if (requestId && requests.has(requestId)) {
      const r = requests.get(requestId);
      sessionCode = r.sessionCode;
      durationMinutes = r.durationMinutes;
      scholarName = r.scholarName;
    }

    const proto = req.headers['x-forwarded-proto'] || 'http';
    const serverOrigin = `${proto}://${req.headers.host || `localhost:${WEB_PORT}`}`;

    const batContent = buildHostellerGuiBat({
      serverUrl: serverOrigin,
      sessionCode,
      requestId,
      hostellerName,
      scholarName,
      durationMinutes
    });

    const fileName = sessionCode
      ? `Amrita-Hosteller-App-${sessionCode}.bat`
      : `Amrita-Hosteller-App-${durationMinutes}min.bat`;

    res.writeHead(200, {
      'Content-Type': 'application/octet-stream',
      'Content-Disposition': `attachment; filename="${fileName}"`
    });
    return res.end(batContent);
  }

  // API: Download 1-Click Zero-Terminal Day Scholar Desktop GUI App (.bat)
  if (pathname === '/api/download/scholar-app' && req.method === 'GET') {
    const sessionCode = (parsedUrl.searchParams.get('sessionCode') || '').toUpperCase();
    const proto = req.headers['x-forwarded-proto'] || 'http';
    const serverOrigin = `${proto}://${req.headers.host || `localhost:${WEB_PORT}`}`;

    const batContent = buildDayScholarGuiBat({
      serverUrl: serverOrigin,
      sessionCode
    });

    res.writeHead(200, {
      'Content-Type': 'application/octet-stream',
      'Content-Disposition': 'attachment; filename="Amrita-DayScholar-Connector.bat"'
    });
    return res.end(batContent);
  }

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

// WebSocket Upgrade Handler:
// 1. /tunnel  -> Hosteller Agent on College Wi-Fi
// 2. /scholar -> Day Scholar Local Proxy Bridge (connects Day Scholar's localhost:8080 to Render Cloud!)
webServer.on('upgrade', (req, socket) => {
  const parsedUrl = new URL(req.url, `http://${req.headers.host || 'localhost'}`);

  // --------------------------------------------------------------------------
  // ROUTE A: Hosteller Agent connecting from College Wi-Fi (/tunnel)
  // --------------------------------------------------------------------------
  if (parsedUrl.pathname === '/tunnel') {
    const requestId = parsedUrl.searchParams.get('requestId') || null;
    let sessionCode = (parsedUrl.searchParams.get('sessionCode') || '').toUpperCase();
    const hostellerName = parsedUrl.searchParams.get('hostellerName') || 'Campus Hosteller';
    let durationMinutes = Math.min(180, Math.max(1, Number(parsedUrl.searchParams.get('duration')) || 30));
    let allowedDomains = [...DEFAULT_ALLOWED_DOMAINS];

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
    completeWebSocketHandshake(req, socket, { 'X-Session-Code': sessionCode });

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
        if (stream && stream.onConnected) stream.onConnected();
      } else if (header.type === 'CONNECT_ERR') {
        if (stream) {
          if (stream.onError) stream.onError(header.error || 'Connect failed');
          tunnel.streams.delete(header.streamId);
        }
      } else if (header.type === 'DATA') {
        if (stream && payload.length > 0) {
          tunnel.stats.bytesRelayed += payload.length;
          if (linkedReq) linkedReq.stats.bytesRelayed = tunnel.stats.bytesRelayed;
          if (stream.onData) stream.onData(payload);
        }
      } else if (header.type === 'END') {
        if (stream) {
          if (stream.onEnd) stream.onEnd();
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
    return;
  }

  // --------------------------------------------------------------------------
  // ROUTE B: Day Scholar Local Proxy Bridge connecting over WebSocket (/scholar)
  // Allows Day Scholar's localhost:8080 to tunnel through Render Cloud to Hosteller!
  // --------------------------------------------------------------------------
  if (parsedUrl.pathname === '/scholar') {
    const preferredCode = (parsedUrl.searchParams.get('sessionCode') || '').toUpperCase();
    completeWebSocketHandshake(req, socket);

    // Map scholar's local streamId -> server global streamId
    const streamMap = new Map();

    attachFrameParser(socket, (header, payload) => {
      const tunnel = getActiveTunnel(header.sessionCode || preferredCode || null);
      if (header.type === 'PING') {
        sendTunnelFrame(socket, { type: 'PONG', ts: Date.now() });
        return;
      }

      if (header.type === 'CONNECT') {
        const clientStreamId = header.streamId;
        if (!tunnel) {
          sendTunnelFrame(socket, {
            type: 'CONNECT_ERR',
            streamId: clientStreamId,
            status: 503,
            error: 'No active Hosteller connected right now'
          });
          return;
        }

        if (!isDomainAllowed(header.host, tunnel.allowedDomains)) {
          tunnel.stats.blockedAttempts++;
          recordTunnelLog(tunnel, {
            event: 'BLOCKED_DOMAIN',
            detail: `Blocked non-college domain ${header.host}:${header.port}`
          });
          sendTunnelFrame(socket, {
            type: 'CONNECT_ERR',
            streamId: clientStreamId,
            status: 403,
            error: `Domain ${header.host} is not in the college whitelist`
          });
          return;
        }

        const serverStreamId = nextStreamId++;
        streamMap.set(clientStreamId, { serverStreamId, tunnel });
        tunnel.stats.connections++;
        const linkedReq = tunnel.requestId ? requests.get(tunnel.requestId) : null;
        if (linkedReq) linkedReq.stats.connections = tunnel.stats.connections;

        recordTunnelLog(tunnel, {
          event: header.encrypted ? 'TLS_TUNNEL' : 'HTTP_PROXY',
          detail: `${header.encrypted ? '🔒 E2E Encrypted TLS Tunnel' : '🌐 HTTP Proxy'} -> ${header.host}:${header.port}`
        });

        tunnel.streams.set(serverStreamId, {
          socket,
          onConnected() {
            sendTunnelFrame(socket, { type: 'CONNECT_OK', streamId: clientStreamId });
          },
          onError(errMsg) {
            sendTunnelFrame(socket, { type: 'CONNECT_ERR', streamId: clientStreamId, status: 502, error: errMsg });
          },
          onData(chunk) {
            sendTunnelFrame(socket, { type: 'DATA', streamId: clientStreamId }, chunk);
          },
          onEnd() {
            sendTunnelFrame(socket, { type: 'END', streamId: clientStreamId });
          }
        });

        sendTunnelFrame(tunnel.socket, {
          type: 'CONNECT',
          streamId: serverStreamId,
          host: header.host,
          port: header.port,
          encrypted: Boolean(header.encrypted)
        });
      } else if (header.type === 'DATA') {
        const entry = streamMap.get(header.streamId);
        if (entry && entry.tunnel) {
          entry.tunnel.stats.bytesRelayed += payload.length;
          sendTunnelFrame(entry.tunnel.socket, { type: 'DATA', streamId: entry.serverStreamId }, payload);
        }
      } else if (header.type === 'END') {
        const entry = streamMap.get(header.streamId);
        if (entry && entry.tunnel) {
          sendTunnelFrame(entry.tunnel.socket, { type: 'END', streamId: entry.serverStreamId });
          entry.tunnel.streams.delete(entry.serverStreamId);
          streamMap.delete(header.streamId);
        }
      }
    });

    socket.on('close', () => {
      for (const [, entry] of streamMap.entries()) {
        if (entry.tunnel) {
          sendTunnelFrame(entry.tunnel.socket, { type: 'END', streamId: entry.serverStreamId });
          entry.tunnel.streams.delete(entry.serverStreamId);
        }
      }
      streamMap.clear();
    });
    return;
  }

  socket.destroy();
});

// ============================================================================
// 2. DIRECT BROWSER HTTP / HTTPS FORWARD PROXY SERVER (PORT 8080 - Local Mode)
// ============================================================================
const proxyServer = http.createServer((clientReq, clientRes) => {
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
        <p>Open the <b>Amrita Campus VPN Extension</b> or visit the Web Dashboard to request a Hosteller relay session.</p>
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
      onConnected: () => {
        sendTunnelFrame(tunnel.socket, { type: 'DATA', streamId }, initialPayload);
      },
      onError: () => {
        try { clientRes.socket.destroy(); } catch (_) {}
      },
      onData: (chunk) => {
        try { clientRes.socket.write(chunk); } catch (_) {}
      },
      onEnd: () => {
        try { clientRes.socket.end(); } catch (_) {}
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
    onConnected() {
      try {
        clientSocket.write('HTTP/1.1 200 Connection Established\r\nProxy-Agent: Amrita-Campus-Bridge\r\n\r\n');
        if (head && head.length > 0) {
          tunnel.stats.bytesRelayed += head.length;
          sendTunnelFrame(tunnel.socket, { type: 'DATA', streamId }, head);
        }
      } catch (_) {}
    },
    onError(errMsg) {
      try {
        clientSocket.write(`HTTP/1.1 502 Bad Gateway\r\n\r\n${errMsg}\r\n`);
        clientSocket.destroy();
      } catch (_) {}
    },
    onData(chunk) {
      try { clientSocket.write(chunk); } catch (_) {}
    },
    onEnd() {
      try { clientSocket.end(); } catch (_) {}
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
      if (IS_RENDER) {
        console.log('====================================================================');
        console.log(' 🎓 AMRITA CAMPUS BRIDGE - CLOUD SERVER LIVE ON RENDER');
        console.log(` 🌐 Listening on PORT ${customWebPort}`);
        console.log('====================================================================');
        return resolve({ webServer, proxyServer: null });
      }
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
