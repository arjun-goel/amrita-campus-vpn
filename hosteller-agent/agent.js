/**
 * Amrita Campus Bridge — Hosteller Campus Relay Agent
 *
 * Run this script on a laptop connected to AMRITA COLLEGE WI-FI.
 * Connects outbound over standard RFC 6455 WebSocket to https://amrita-campus-vpn.onrender.com
 * (or localhost:3000) so it works through both College Wi-Fi firewalls and Render Cloud!
 */

const http = require('http');
const https = require('https');
const net = require('net');
const crypto = require('crypto');
const { URL } = require('url');

const DEFAULT_RENDER_URL = 'https://amrita-campus-vpn.onrender.com';

function parseArgs(argv) {
  const opts = {
    server: process.env.AMRITA_BROKER_URL || DEFAULT_RENDER_URL,
    name: process.env.HOSTELLER_NAME || 'Campus-Hosteller',
    duration: Number(process.env.DURATION_MINS || 30),
    session: '',
    request: ''
  };
  for (let i = 2; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--server' && argv[i + 1]) opts.server = argv[++i];
    else if (arg === '--name' && argv[i + 1]) opts.name = argv[++i];
    else if (arg === '--duration' && argv[i + 1]) opts.duration = Number(argv[++i]) || 30;
    else if (arg === '--session' && argv[i + 1]) opts.session = argv[++i];
    else if (arg === '--request' && argv[i + 1]) opts.request = argv[++i];
  }
  return opts;
}

const DEFAULT_ALLOWED_DOMAINS = [
  '*.amrita.edu',
  'amrita.edu',
  '*.amritavidyalayam.org',
  'localhost',
  '127.0.0.1'
];

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

// RFC 6455 Client-to-Server Masked Binary WebSocket Frame (using 0x00000000 mask key)
function encodeClientWsFrame(buffer) {
  const len = buffer.length;
  let header;
  if (len < 126) {
    header = Buffer.alloc(6);
    header[0] = 0x82; // FIN + Binary
    header[1] = 0x80 | len; // Masked
  } else if (len < 65536) {
    header = Buffer.alloc(8);
    header[0] = 0x82;
    header[1] = 0x80 | 126;
    header.writeUInt16BE(len, 2);
  } else {
    header = Buffer.alloc(14);
    header[0] = 0x82;
    header[1] = 0x80 | 127;
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
  const inner = Buffer.concat([lenPrefix, headerBuf, dataBuf]);
  try {
    socket.write(encodeClientWsFrame(inner));
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
        socket.destroy();
        return;
      } else if (opcode === 0x01 || opcode === 0x02 || opcode === 0x00) {
        innerBuf = Buffer.concat([innerBuf, frameData]);
        processInnerFrames();
      }
    }
  });
}

function startHostellerAgent(options = {}) {
  const cliOpts = { ...parseArgs(process.argv), ...options };
  const serverUrl = new URL(cliOpts.server);
  const isTls = serverUrl.protocol === 'https:' || serverUrl.protocol === 'wss:';
  const httpModule = isTls ? https : http;

  const tunnelPath =
    `/tunnel?hostellerName=${encodeURIComponent(cliOpts.name)}` +
    `&duration=${encodeURIComponent(cliOpts.duration)}` +
    (cliOpts.session ? `&sessionCode=${encodeURIComponent(cliOpts.session)}` : '') +
    (cliOpts.request ? `&requestId=${encodeURIComponent(cliOpts.request)}` : '');

  console.log('====================================================================');
  console.log(' 🏠 AMRITA CAMPUS BRIDGE — HOSTELLER RELAY AGENT');
  console.log('====================================================================');
  console.log(` 📡 Connecting to Cloud Broker  : ${cliOpts.server}`);
  console.log(` 👤 Hosteller Identifier        : ${cliOpts.name}`);
  console.log(` ⏱️  Approved Duration          : ${cliOpts.duration} minutes`);
  console.log('====================================================================');

  const wsKey = crypto.randomBytes(16).toString('base64');

  const req = httpModule.request({
    hostname: serverUrl.hostname,
    port: serverUrl.port || (isTls ? 443 : 80),
    path: tunnelPath,
    method: 'GET',
    headers: {
      Host: serverUrl.host,
      Connection: 'Upgrade',
      Upgrade: 'websocket',
      'Sec-WebSocket-Version': '13',
      'Sec-WebSocket-Key': wsKey
    }
  });

  const activeSockets = new Map();
  let allowedDomains = [...DEFAULT_ALLOWED_DOMAINS];
  let expiryTimer = null;
  let pingInterval = null;

  req.on('upgrade', (res, tunnelSocket, head) => {
    tunnelSocket.setNoDelay(true);
    const assignedCode = res.headers['x-session-code'] || cliOpts.session || 'ACTIVE';

    console.log(` ✅ CAMPUS RELAY LIVE! Session Code: [ ${assignedCode} ]`);
    console.log(' 🔒 Day Scholars can now enter this code in the Amrita VPN Chrome Extension.');
    console.log(' 🛡️  Hosteller Domain Guard Active: Only *.amrita.edu traffic is allowed.');
    console.log(' 🛑 Press Ctrl+C anytime to immediately stop sharing your connection.\n');

    pingInterval = setInterval(() => {
      sendTunnelFrame(tunnelSocket, { type: 'PING', ts: Date.now() });
    }, 15000).unref();

    attachFrameParser(tunnelSocket, (header, payload) => {
      if (header.type === 'WELCOME') {
        if (header.sessionCode) {
          console.log(` 🎫 Confirmed Session Code: [ ${header.sessionCode} ]`);
        }
        if (Array.isArray(header.allowedDomains)) {
          allowedDomains = Array.from(new Set([...header.allowedDomains, ...DEFAULT_ALLOWED_DOMAINS]));
        }
        const remainingMs = Math.max(1000, (header.expiresAt || Date.now() + cliOpts.duration * 60000) - Date.now());
        if (expiryTimer) clearTimeout(expiryTimer);
        expiryTimer = setTimeout(() => {
          console.log('\n ⏰ Approved session duration reached. Closing Campus Relay automatically...');
          tunnelSocket.destroy();
          process.exit(0);
        }, remainingMs);
        expiryTimer.unref();
      } else if (header.type === 'SESSION_END') {
        console.log(`\n 🛑 Session ended by platform: ${header.reason || 'Closed'}`);
        for (const [, s] of activeSockets) s.destroy();
        activeSockets.clear();
        tunnelSocket.destroy();
        if (require.main === module) process.exit(0);
      } else if (header.type === 'CONNECT') {
        const { streamId, host, port, encrypted } = header;

        if (!isDomainAllowed(host, allowedDomains)) {
          console.log(` 🚫 [BLOCKED] Rejected non-college target: ${host}:${port}`);
          sendTunnelFrame(tunnelSocket, {
            type: 'CONNECT_ERR',
            streamId,
            error: `Hosteller policy blocked non-college domain: ${host}`
          });
          return;
        }

        console.log(` 🔗 [RELAY #${streamId}] ${encrypted ? '🔒 HTTPS Tunnel' : '🌐 HTTP'} -> ${host}:${port}`);

        const targetSocket = net.connect({ host, port: Number(port) || 443 }, () => {
          targetSocket.setNoDelay(true);
          sendTunnelFrame(tunnelSocket, { type: 'CONNECT_OK', streamId });
        });

        activeSockets.set(streamId, targetSocket);

        targetSocket.on('data', (chunk) => {
          sendTunnelFrame(tunnelSocket, { type: 'DATA', streamId }, chunk);
        });

        targetSocket.on('end', () => {
          sendTunnelFrame(tunnelSocket, { type: 'END', streamId });
          activeSockets.delete(streamId);
        });

        targetSocket.on('error', (err) => {
          sendTunnelFrame(tunnelSocket, {
            type: 'CONNECT_ERR',
            streamId,
            error: err.message
          });
          activeSockets.delete(streamId);
        });

        targetSocket.on('close', () => {
          activeSockets.delete(streamId);
        });
      } else if (header.type === 'DATA') {
        const targetSocket = activeSockets.get(header.streamId);
        if (targetSocket && !targetSocket.destroyed && payload.length > 0) {
          try {
            targetSocket.write(payload);
          } catch (_) {}
        }
      } else if (header.type === 'END') {
        const targetSocket = activeSockets.get(header.streamId);
        if (targetSocket) {
          try {
            targetSocket.end();
          } catch (_) {}
          activeSockets.delete(header.streamId);
        }
      }
    });

    if (head && head.length > 0) {
      tunnelSocket.emit('data', head);
    }

    tunnelSocket.on('close', () => {
      if (pingInterval) clearInterval(pingInterval);
      for (const [, s] of activeSockets) s.destroy();
      activeSockets.clear();
      console.log(' 🔌 Disconnected from Broker Server.');
    });
  });

  req.on('response', (res) => {
    console.error(` ❌ Server responded with HTTP ${res.statusCode} instead of WebSocket Upgrade.`);
  });

  req.on('error', (err) => {
    console.error(` ❌ Failed to connect to Broker Server (${cliOpts.server}): ${err.message}`);
  });

  req.end();
  return req;
}

if (require.main === module) {
  startHostellerAgent();
}

module.exports = { startHostellerAgent };
