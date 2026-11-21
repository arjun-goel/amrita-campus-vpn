/**
 * Builds the 100% Pure Managed C# Native Windows Desktop Application: AmritaCampusBridge.exe
 *
 * Why v2.0 passes Antivirus cleanly & fixes request visibility:
 * 1. ZERO embedded Base64 scripts, ZERO files written to %TEMP%, ZERO Process.Start() or node.exe downloads!
 *    All WebSocket and TCP relay networking runs natively in managed .NET C#.
 * 2. Uses .NET's built-in System.Web.Extensions.dll (JavaScriptSerializer) to parse /api/status JSON
 *    including nested objects so Day Scholar requests always appear immediately in the Hosteller table!
 */

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const CSC_PATH = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';
const csFilePath = path.join(__dirname, 'desktop-app', 'AmritaCampusBridge.cs');
const outExeRoot = path.join(__dirname, 'AmritaCampusBridge.exe');
const outExePublic = path.join(__dirname, 'server', 'public', 'AmritaCampusBridge.exe');

console.log('🔨 Compiling Pure C# Windows Desktop App v2.0 (AmritaCampusBridge.exe)...');
execFileSync(
  CSC_PATH,
  [
    '/target:winexe',
    '/optimize+',
    `/out:${outExeRoot}`,
    '/r:System.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll',
    csFilePath
  ],
  { stdio: 'inherit' }
);

fs.copyFileSync(outExeRoot, outExePublic);
const sizeKb = (fs.statSync(outExeRoot).size / 1024).toFixed(1);
console.log(`✅ Built Pure Managed C# AmritaCampusBridge.exe (${sizeKb} KB) in root and server/public/!`);
