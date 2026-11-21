/**
 * Generates extension PNG icons and runs a full End-to-End automated test of:
 * 1. Mock "Internal College Website" (simulating https/http *.amrita.edu portal that only opens inside campus)
 * 2. Amrita Campus Bridge Broker Server (Web API port + Proxy port)
 * 3. Day Scholar Access Request Creation (/api/requests)
 * 4. Hosteller Agent connecting outbound to approve & host the session (/tunnel)
 * 5. Day Scholar Browser Proxy routing HTTP and HTTPS CONNECT tunnel traffic through the Hosteller Agent
 * 6. Domain Guard verification (confirming non-college sites like example.com are blocked by the proxy while *.amrita.edu succeeds)
 */

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const http = require('http');
const net = require('net');
const { startServers } = require('./server/server');
const { startHostellerAgent } = require('./hosteller-agent/agent');

// ----------------------------------------------------------------------------
// 1. Generate valid PNG Icons for the Chrome Extension (16x16, 48x48, 128x128)
// ----------------------------------------------------------------------------
function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) {
    c ^= buf[i];
    for (let k = 0; k < 8; k++) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
  }
  return (c ^ 0xffffffff) >>> 0;
}

function pngChunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const typeBuf = Buffer.from(type, 'ascii');
  const crcBuf = Buffer.alloc(4);
  crcBuf.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])), 0);
  return Buffer.concat([len, typeBuf, data, crcBuf]);
}

function createIconPng(size) {
  const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // RGBA

  const rawRows = [];
  const center = size / 2;
  const radius = size * 0.44;

  for (let y = 0; y < size; y++) {
    const row = Buffer.alloc(1 + size * 4);
    row[0] = 0; // filter type 0
    for (let x = 0; x < size; x++) {
      const dx = x - center;
      const dy = y - center;
      const dist = Math.sqrt(dx * dx + dy * dy);
      const offset = 1 + x * 4;
      if (dist <= radius) {
        // Amrita Crimson -> Deep Purple gradient
        const t = (x + y) / (size * 2);
        row[offset] = Math.round(168 * (1 - t) + 109 * t);     // R
        row[offset + 1] = Math.round(28 * (1 - t) + 40 * t);   // G
        row[offset + 2] = Math.round(75 * (1 - t) + 217 * t);  // B
        row[offset + 3] = 255;                                 // A
      } else {
        row[offset + 3] = 0;
      }
    }
    rawRows.push(row);
  }

  const idatData = zlib.deflateSync(Buffer.concat(rawRows));
  return Buffer.concat([
    signature,
    pngChunk('IHDR', ihdr),
    pngChunk('IDAT', idatData),
    pngChunk('IEND', Buffer.alloc(0))
  ]);
}

function ensureIcons() {
  const iconsDir = path.join(__dirname, 'extension', 'icons');
  fs.mkdirSync(iconsDir, { recursive: true });
  for (const sz of [16, 48, 128]) {
    fs.writeFileSync(path.join(iconsDir, `icon${sz}.png`), createIconPng(sz));
  }
  console.log('✅ Generated Chrome Extension icons (16px, 48px, 128px)');
}

// ----------------------------------------------------------------------------
// 2. End-to-End Relay Verification Test
// ----------------------------------------------------------------------------
async function runE2ETest() {
  ensureIcons();

  // Start a mock "Campus-Only Intranet Server" on localhost:19443
  const mockCampusServer = http.createServer((req, res) => {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(
      JSON.stringify({
        portal: 'AUMS Amrita Campus Intranet',
        authenticatedTunnel: true,
        hostHeader: req.headers.host,
        message: 'Welcome to Amrita Internal Campus Network!'
      })
    );
  });

  await new Promise((r) => mockCampusServer.listen(19443, '127.0.0.1', r));

  // Start Broker Server on test ports (Web: 13000, Proxy: 18080)
  const { webServer, proxyServer } = await startServers(13000, 18080);

  try {
    // Step A: Day Scholar posts an access request
    const reqRes = await fetch('http://127.0.0.1:13000/api/requests', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        scholarName: 'Test Day Scholar',
        purpose: 'AUMS Attendance',
        durationMinutes: 15,
        allowedDomains: ['*.amrita.edu', '127.0.0.1']
      })
    });
    const reqData = await reqRes.json();
    if (!reqData.ok || !reqData.request.sessionCode) {
      throw new Error('Failed to create Day Scholar request');
    }
    const sessionCode = reqData.request.sessionCode;
    console.log(`✅ Day Scholar request created with Session Code: ${sessionCode}`);

    // Step B: Hosteller Agent connects outbound to approve & host the session
    startHostellerAgent({
      server: 'http://127.0.0.1:13000',
      name: 'Hostel-Test-Laptop',
      duration: 15,
      session: sessionCode,
      request: reqData.request.id
    });

    // Wait briefly for tunnel handshake
    await new Promise((r) => setTimeout(r, 350));

    // Verify status endpoint shows active hosteller
    const statusRes = await fetch(`http://127.0.0.1:13000/api/status?sessionCode=${sessionCode}`);
    const statusData = await statusRes.json();
    if (!statusData.hasActiveHosteller) {
      throw new Error('Hosteller tunnel did not register as active');
    }
    console.log(`✅ Hosteller tunnel active (${statusData.activeSession.hostellerName})`);

    // Step C: Test HTTP CONNECT Tunnel through Proxy (Port 18080) to Mock Campus Server (127.0.0.1:19443)
    const tunnelResponse = await new Promise((resolve, reject) => {
      const client = net.connect(18080, '127.0.0.1', () => {
        client.write(
          `CONNECT 127.0.0.1:19443 HTTP/1.1\r\n` +
          `Host: 127.0.0.1:19443\r\n` +
          `X-Session-Code: ${sessionCode}\r\n\r\n`
        );
      });

      let established = false;
      let responseBuf = '';

      client.on('data', (chunk) => {
        const str = chunk.toString('utf8');
        if (!established) {
          if (str.includes('200 Connection Established')) {
            established = true;
            // Now send an HTTP request inside the established tunnel (simulating encrypted TLS stream)
            client.write(
              `GET /aums/login HTTP/1.1\r\n` +
              `Host: aums.amrita.edu\r\n` +
              `Connection: close\r\n\r\n`
            );
          } else {
            reject(new Error(`Unexpected proxy CONNECT response: ${str}`));
          }
        } else {
          responseBuf += str;
        }
      });

      client.on('end', () => resolve(responseBuf));
      client.on('error', reject);
    });

    if (!tunnelResponse.includes('Welcome to Amrita Internal Campus Network!')) {
      throw new Error(`Tunnel payload mismatch: ${tunnelResponse}`);
    }
    console.log('✅ End-to-End CONNECT Tunnel relayed campus portal response accurately!');

    // Step D: Verify Non-College Domain is BLOCKED (e.g. youtube.com:443)
    const blockedStatus = await new Promise((resolve, reject) => {
      const client = net.connect(18080, '127.0.0.1', () => {
        client.write(`CONNECT youtube.com:443 HTTP/1.1\r\nHost: youtube.com:443\r\n\r\n`);
      });
      client.on('data', (chunk) => {
        resolve(chunk.toString('utf8'));
        client.destroy();
      });
      client.on('error', reject);
    });

    if (!blockedStatus.includes('403 Forbidden')) {
      throw new Error(`Expected 403 Forbidden for non-college domain, got: ${blockedStatus}`);
    }
    console.log('✅ Domain Guard verified: Non-college domain (youtube.com) blocked with 403 Forbidden!');
    console.log('\n🎉 ALL E2E TESTS PASSED SUCCESSFULLY!');
  } finally {
    mockCampusServer.close();
    webServer.close();
    proxyServer.close();
    setTimeout(() => process.exit(0), 100);
  }
}

runE2ETest().catch((err) => {
  console.error('❌ E2E Test Failed:', err);
  process.exit(1);
});
