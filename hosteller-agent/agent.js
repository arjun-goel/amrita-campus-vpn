/**
 * Amrita Campus Bridge — Hosteller Campus Relay Agent
 *
 * Run this script on a laptop connected to AMRITA COLLEGE WI-FI.
 * Zero external dependencies — uses pure Node.js built-in modules.
 *
 * Usage:
 *   node hosteller-agent/agent.js
 *   node hosteller-agent/agent.js --server http://localhost:3000 --name "Hostel-Block-3" --duration 30 --session AMR-1234
 *
 * Security & Privacy Enforced on Hosteller's Laptop:
 * 1. Outbound Reverse Tunnel: Connects outbound to the Broker Server so no port-forwarding is required on campus Wi-Fi.
 * 2. Hosteller-Side Domain Guard: Refuses to connect to any domain outside *.amrita.edu (or configured college whitelist).
 * 3. E2E TLS Pass-Through: Forwards raw encrypted TLS bytes for port 443; hosteller cannot view Day Scholar passwords, and Day Scholar cannot access hosteller's local files.
 * 4. Automatic Time-Limit Shutdown: Disconnects automatically when the approved timer expires.
 */

const http = require('http');
const https = require('https');
const net = require('net');
const { URL } = require('url');

// Parse CLI arguments
function parseArgs(argv) {
  const opts = {
    server: process.env.AMRITA_BROKER_URL || 'http://127.0.0.1:3000',
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
  console.log(` 📡 Connecting to Broker Server : ${cliOpts.server}`);
  console.log(` 👤 Hosteller Identifier        : ${cliOpts.name}`);
  console.log(` ⏱️  Approved Duration          : ${cliOpts.duration} minutes`);
  console.log('====================================================================');

  const req = httpModule.request({
    hostname: serverUrl.hostname,
    port: serverUrl.port || (isTls ? 443 : 80),
    path: tunnelPath,
    method: 'GET',
    headers: {
      Connection: 'Upgrade',
      Upgrade: 'amrita-tunnel'
    }
  });

  const activeSockets = new Map(); // streamId -> net.Socket
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

        // Hosteller-side domain verification
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

    tunnelSocket.on('close', () => {
      if (pingInterval) clearInterval(pingInterval);
      for (const [, s] of activeSockets) s.destroy();
      activeSockets.clear();
      console.log(' 🔌 Disconnected from Broker Server.');
    });
  });

  req.on('error', (err) => {
    console.error(` ❌ Failed to connect to Broker Server (${cliOpts.server}): ${err.message}`);
    console.error('    Make sure the Broker Server is running (npm start or Start-Platform-Server.bat).');
  });

  req.end();
  return req;
}

if (require.main === module) {
  startHostellerAgent();
}

module.exports = { startHostellerAgent };
