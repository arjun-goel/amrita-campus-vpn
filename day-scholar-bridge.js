/**
 * Amrita Campus Bridge — Day Scholar Cloud Bridge (127.0.0.1:8080 -> Render Cloud)
 *
 * Why this exists:
 * Render only exposes HTTPS/WebSockets on port 443 (https://amrita-campus-vpn.onrender.com),
 * while Chrome's proxy API expects a local HTTP/HTTPS CONNECT proxy port (127.0.0.1:8080).
 *
 * This lightweight local bridge runs on the Day Scholar's laptop (127.0.0.1:8080) and
 * tunnels every *.amrita.edu browser request over a secure WebSocket (wss://) through
 * https://amrita-campus-vpn.onrender.com directly to the Hosteller's laptop on College Wi-Fi!
 */

const http = require('http');
const https = require('https');
const crypto = require('crypto');
const { URL } = require('url');

const CLOUD_SERVER = process.env.AMRITA_BROKER_URL || 'https://amrita-campus-vpn.onrender.com';
const LOCAL_PROXY_PORT = Number(process.env.PROXY_PORT || 8080);

function encodeClientWsFrame(buffer) {
  const len = buffer.length;
  let header;
  if (len < 126) {
    header = Buffer.alloc(6);
    header[0] = 0x82;
    header[1] = 0x80 | len;
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

let cloudSocket = null;
const pendingStreams = new Map();
let nextStreamId = 1;

function connectToRenderCloud(serverBaseUrl = CLOUD_SERVER) {
  const parsed = new URL(serverBaseUrl);
  const isTls = parsed.protocol === 'https:' || parsed.protocol === 'wss:';
  const mod = isTls ? https : http;

  const req = mod.request({
    hostname: parsed.hostname,
    port: parsed.port || (isTls ? 443 : 80),
    path: '/scholar',
    method: 'GET',
    headers: {
      Host: parsed.host,
      Connection: 'Upgrade',
      Upgrade: 'websocket',
      'Sec-WebSocket-Version': '13',
      'Sec-WebSocket-Key': crypto.randomBytes(16).toString('base64')
    }
  });

  req.on('upgrade', (res, socket, head) => {
    socket.setNoDelay(true);
    cloudSocket = socket;
    console.log(` ✅ Connected to Render Cloud Server (${serverBaseUrl})`);

    const pingTimer = setInterval(() => {
      sendTunnelFrame(socket, { type: 'PING' });
    }, 15000).unref();

    attachFrameParser(socket, (header, payload) => {
      const stream = pendingStreams.get(header.streamId);
      if (!stream) return;

      if (header.type === 'CONNECT_OK') {
        if (stream.onConnected) stream.onConnected();
      } else if (header.type === 'CONNECT_ERR') {
        if (stream.onError) stream.onError(header.status || 502, header.error || 'Relay failed');
        pendingStreams.delete(header.streamId);
      } else if (header.type === 'DATA') {
        if (payload.length > 0 && !stream.socket.destroyed) {
          try { stream.socket.write(payload); } catch (_) {}
        }
      } else if (header.type === 'END') {
        try { stream.socket.end(); } catch (_) {}
        pendingStreams.delete(header.streamId);
      }
    });

    if (head && head.length > 0) socket.emit('data', head);

    socket.on('close', () => {
      clearInterval(pingTimer);
      cloudSocket = null;
      for (const [, s] of pendingStreams) {
        try { s.socket.destroy(); } catch (_) {}
      }
      pendingStreams.clear();
      console.log(' 🔄 Cloud socket closed. Reconnecting in 2s...');
      setTimeout(() => connectToRenderCloud(serverBaseUrl), 2000);
    });

    socket.on('error', () => {});
  });

  req.on('error', (err) => {
    console.error(` ⚠️ Waiting for Cloud Server (${serverBaseUrl}): ${err.message}`);
    setTimeout(() => connectToRenderCloud(serverBaseUrl), 3000);
  });

  req.end();
}

const localProxy = http.createServer((clientReq, clientRes) => {
  let targetUrl;
  try {
    targetUrl = new URL(clientReq.url);
  } catch (_) {
    clientRes.writeHead(400);
    return clientRes.end('Invalid URL');
  }

  if (!cloudSocket || cloudSocket.destroyed) {
    clientRes.writeHead(503, { 'Content-Type': 'text/plain' });
    return clientRes.end('Amrita Cloud Bridge is connecting to Render...');
  }

  const streamId = nextStreamId++;
  const targetHost = targetUrl.hostname;
  const targetPort = Number(targetUrl.port) || 80;
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
    pendingStreams.set(streamId, {
      socket: clientRes.socket,
      onConnected() {
        sendTunnelFrame(cloudSocket, { type: 'DATA', streamId }, initialPayload);
      },
      onError(status, msg) {
        clientRes.writeHead(status, { 'Content-Type': 'text/plain' });
        clientRes.end(msg);
      }
    });

    sendTunnelFrame(cloudSocket, {
      type: 'CONNECT',
      streamId,
      host: targetHost,
      port: targetPort,
      encrypted: false
    });
  });
});

localProxy.on('connect', (req, clientSocket, head) => {
  const [targetHost, rawPort] = (req.url || '').split(':');
  const targetPort = Number(rawPort) || 443;

  if (!cloudSocket || cloudSocket.destroyed) {
    clientSocket.write('HTTP/1.1 503 Service Unavailable\r\n\r\n');
    clientSocket.destroy();
    return;
  }

  const streamId = nextStreamId++;
  clientSocket.setNoDelay(true);

  pendingStreams.set(streamId, {
    socket: clientSocket,
    onConnected() {
      try {
        clientSocket.write('HTTP/1.1 200 Connection Established\r\n\r\n');
        if (head && head.length > 0) {
          sendTunnelFrame(cloudSocket, { type: 'DATA', streamId }, head);
        }
      } catch (_) {}
    },
    onError(status, msg) {
      try {
        clientSocket.write(`HTTP/1.1 ${status} Error\r\n\r\n${msg}\r\n`);
        clientSocket.destroy();
      } catch (_) {}
    }
  });

  clientSocket.on('data', (chunk) => {
    sendTunnelFrame(cloudSocket, { type: 'DATA', streamId }, chunk);
  });

  clientSocket.on('end', () => {
    sendTunnelFrame(cloudSocket, { type: 'END', streamId });
    pendingStreams.delete(streamId);
  });

  clientSocket.on('error', () => {
    sendTunnelFrame(cloudSocket, { type: 'END', streamId });
    pendingStreams.delete(streamId);
  });

  sendTunnelFrame(cloudSocket, {
    type: 'CONNECT',
    streamId,
    host: targetHost,
    port: targetPort,
    encrypted: true
  });
});

if (require.main === module) {
  localProxy.listen(LOCAL_PROXY_PORT, '127.0.0.1', () => {
    console.log('====================================================================');
    console.log(' 🎒 AMRITA CAMPUS BRIDGE — DAY SCHOLAR CLOUD BRIDGE');
    console.log('====================================================================');
    console.log(` 🛡️  Local Chrome Extension Proxy : http://127.0.0.1:${LOCAL_PROXY_PORT}`);
    console.log(` ☁️  Render Cloud Broker URL      : ${CLOUD_SERVER}`);
    console.log('====================================================================');
    connectToRenderCloud(CLOUD_SERVER);
  });
}

module.exports = { localProxy, connectToRenderCloud };
