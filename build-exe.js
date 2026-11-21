/**
 * Builds the standalone Native Windows Desktop Application: AmritaCampusBridge.exe
 *
 * Injects the embedded agent.js and day-scholar-bridge.js payloads into
 * desktop-app/AmritaCampusBridge.Template.cs and compiles a single standalone
 * Windows executable (AmritaCampusBridge.exe) using csc.exe.
 */

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const CSC_PATH = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';

const agentJsB64 = fs.readFileSync(path.join(__dirname, 'hosteller-agent', 'agent.js')).toString('base64');
const bridgeJsB64 = fs.readFileSync(path.join(__dirname, 'day-scholar-bridge.js')).toString('base64');

const templatePath = path.join(__dirname, 'desktop-app', 'AmritaCampusBridge.Template.cs');
const csFilePath = path.join(__dirname, 'desktop-app', 'AmritaCampusBridge.cs');

const csSource = fs
  .readFileSync(templatePath, 'utf8')
  .replace('__AGENT_B64__', agentJsB64)
  .replace('__BRIDGE_B64__', bridgeJsB64);

fs.writeFileSync(csFilePath, csSource, 'utf8');

const outExeRoot = path.join(__dirname, 'AmritaCampusBridge.exe');
const outExePublic = path.join(__dirname, 'server', 'public', 'AmritaCampusBridge.exe');

console.log('🔨 Compiling standalone Windows Desktop App (AmritaCampusBridge.exe)...');
execFileSync(
  CSC_PATH,
  [
    '/target:winexe',
    '/optimize+',
    `/out:${outExeRoot}`,
    '/r:System.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    csFilePath
  ],
  { stdio: 'inherit' }
);

fs.copyFileSync(outExeRoot, outExePublic);
const sizeKb = (fs.statSync(outExeRoot).size / 1024).toFixed(1);
console.log(`✅ Built AmritaCampusBridge.exe (${sizeKb} KB) in root and server/public/!`);
