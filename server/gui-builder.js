/**
 * Amrita Campus Bridge — Zero-Terminal Windows Desktop GUI App Generator
 *
 * Dynamically packages the Hosteller Relay Agent or Day Scholar Cloud Bridge
 * into a single 1-click downloadable Windows file (.bat) that launches a
 * native dark-themed Windows Forms Desktop GUI App (via PowerShell .NET)
 * with:
 * - Zero visible terminal / command prompt window
 * - Pre-configured Session Code, Hosteller/Scholar Name, and Duration
 * - Live Digital Countdown Timer + Progress Bar
 * - Automatic self-termination & closure when the Day Scholar's duration expires
 * - Big "STOP SHARING & CLOSE" button for 1-click manual stop anytime
 * - Automatic portable Node.js download fallback if the student's PC doesn't have Node installed
 */

const fs = require('fs');
const path = require('path');

function buildHostellerGuiBat({
  serverUrl = 'https://amrita-campus-vpn.onrender.com',
  sessionCode = '',
  requestId = '',
  hostellerName = 'Campus-Hosteller',
  scholarName = 'Day Scholar',
  durationMinutes = 30
}) {
  const agentJsPath = path.join(__dirname, '..', 'hosteller-agent', 'agent.js');
  const agentJsB64 = fs.readFileSync(agentJsPath).toString('base64');

  const safeServer = String(serverUrl).replace(/["'`$]/g, '');
  const safeSession = String(sessionCode).replace(/[^A-Za-z0-9\-_]/g, '');
  const safeReqId = String(requestId).replace(/[^A-Za-z0-9\-_]/g, '');
  const safeHostName = String(hostellerName).replace(/["'`$]/g, '').slice(0, 40) || 'Campus-Hosteller';
  const safeScholar = String(scholarName).replace(/["'`$]/g, '').slice(0, 40) || 'Day Scholar';
  const safeDuration = Math.min(180, Math.max(1, Number(durationMinutes) || 30));

  const psScript = `
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$WorkDir = Join-Path $env:TEMP "AmritaCampusBridge"
if (!(Test-Path $WorkDir)) { New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null }

$AgentPath = Join-Path $WorkDir "hosteller-agent.js"
$AgentB64 = "${agentJsB64}"
[System.IO.File]::WriteAllBytes($AgentPath, [System.Convert]::FromBase64String($AgentB64))

$ServerUrl = "${safeServer}"
$SessionCode = "${safeSession}"
$RequestId = "${safeReqId}"
$HostellerName = "${safeHostName}"
$ScholarName = "${safeScholar}"
$DurationMins = ${safeDuration}
$RemainingSecs = $DurationMins * 60
$Script:AgentProc = $null
$Script:IsRunning = $false

# Create Main GUI Window
$Form = New-Object System.Windows.Forms.Form
$Form.Text = "Amrita Campus Bridge — Hosteller Wi-Fi Relay App"
$Form.Size = New-Object System.Drawing.Size(480, 460)
$Form.StartPosition = "CenterScreen"
$Form.FormBorderStyle = "FixedSingle"
$Form.MaximizeBox = $false
$Form.BackColor = [System.Drawing.Color]::FromArgb(15, 23, 42)
$Form.ForeColor = [System.Drawing.Color]::FromArgb(248, 250, 252)

$FontTitle = New-Object System.Drawing.Font("Segoe UI", 13, [System.Drawing.FontStyle]::Bold)
$FontSub = New-Object System.Drawing.Font("Segoe UI", 9)
$FontTimer = New-Object System.Drawing.Font("Consolas", 24, [System.Drawing.FontStyle]::Bold)
$FontBtn = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$FontLog = New-Object System.Drawing.Font("Consolas", 8.5)

$LblHeader = New-Object System.Windows.Forms.Label
$LblHeader.Text = "🎓 Amrita Campus Bridge (Hosteller App)"
$LblHeader.Font = $FontTitle
$LblHeader.ForeColor = [System.Drawing.Color]::FromArgb(56, 189, 248)
$LblHeader.Location = New-Object System.Drawing.Point(20, 16)
$LblHeader.Size = New-Object System.Drawing.Size(430, 28)
$Form.Controls.Add($LblHeader)

$LblSub = New-Object System.Windows.Forms.Label
$LblSub.Text = "Sharing *.amrita.edu access with: $ScholarName  |  Zero password visibility"
$LblSub.Font = $FontSub
$LblSub.ForeColor = [System.Drawing.Color]::FromArgb(148, 163, 184)
$LblSub.Location = New-Object System.Drawing.Point(22, 44)
$LblSub.Size = New-Object System.Drawing.Size(430, 20)
$Form.Controls.Add($LblSub)

# Status Card Panel
$PanelCard = New-Object System.Windows.Forms.Panel
$PanelCard.Location = New-Object System.Drawing.Point(20, 72)
$PanelCard.Size = New-Object System.Drawing.Size(425, 110)
$PanelCard.BackColor = [System.Drawing.Color]::FromArgb(30, 41, 59)
$Form.Controls.Add($PanelCard)

$LblStatus = New-Object System.Windows.Forms.Label
$LblStatus.Text = "🟢 Preparing Campus Wi-Fi Relay..."
$LblStatus.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$LblStatus.ForeColor = [System.Drawing.Color]::FromArgb(16, 185, 129)
$LblStatus.Location = New-Object System.Drawing.Point(14, 12)
$LblStatus.Size = New-Object System.Drawing.Size(395, 22)
$PanelCard.Controls.Add($LblStatus)

$LblCode = New-Object System.Windows.Forms.Label
$CodeDisplay = if ($SessionCode) { $SessionCode } else { "Auto-Assigning..." }
$LblCode.Text = "Session Code: $CodeDisplay   •   Limit: $DurationMins mins"
$LblCode.Font = $FontSub
$LblCode.ForeColor = [System.Drawing.Color]::FromArgb(203, 213, 225)
$LblCode.Location = New-Object System.Drawing.Point(14, 36)
$LblCode.Size = New-Object System.Drawing.Size(395, 20)
$PanelCard.Controls.Add($LblCode)

$LblTimer = New-Object System.Windows.Forms.Label
$LblTimer.Text = ("{0:D2}:{1:D2}" -f [math]::Floor($RemainingSecs / 60), ($RemainingSecs % 60))
$LblTimer.Font = $FontTimer
$LblTimer.ForeColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$LblTimer.Location = New-Object System.Drawing.Point(14, 58)
$LblTimer.Size = New-Object System.Drawing.Size(220, 42)
$PanelCard.Controls.Add($LblTimer)

$LblAutoClose = New-Object System.Windows.Forms.Label
$LblAutoClose.Text = "Auto-closes at 00:00"
$LblAutoClose.Font = $FontSub
$LblAutoClose.ForeColor = [System.Drawing.Color]::FromArgb(148, 163, 184)
$LblAutoClose.Location = New-Object System.Drawing.Point(270, 74)
$LblAutoClose.Size = New-Object System.Drawing.Size(145, 20)
$PanelCard.Controls.Add($LblAutoClose)

# Live Log Box
$TxtLog = New-Object System.Windows.Forms.TextBox
$TxtLog.Multiline = $true
$TxtLog.ReadOnly = $true
$TxtLog.ScrollBars = "Vertical"
$TxtLog.BackColor = [System.Drawing.Color]::FromArgb(9, 13, 22)
$TxtLog.ForeColor = [System.Drawing.Color]::FromArgb(167, 243, 208)
$TxtLog.Font = $FontLog
$TxtLog.Location = New-Object System.Drawing.Point(20, 195)
$TxtLog.Size = New-Object System.Drawing.Size(425, 145)
$Form.Controls.Add($TxtLog)

function Append-Log($msg) {
  if ($TxtLog.IsDisposed) { return }
  $ts = (Get-Date).ToString("HH:mm:ss")
  $TxtLog.AppendText("[$ts] $msg\`r\`n")
}

# Stop & Close Button
$BtnStop = New-Object System.Windows.Forms.Button
$BtnStop.Text = "🛑 STOP SHARING & CLOSE APP NOW"
$BtnStop.Font = $FontBtn
$BtnStop.BackColor = [System.Drawing.Color]::FromArgb(220, 38, 38)
$BtnStop.ForeColor = [System.Drawing.Color]::White
$BtnStop.FlatStyle = "Flat"
$BtnStop.Location = New-Object System.Drawing.Point(20, 355)
$BtnStop.Size = New-Object System.Drawing.Size(425, 46)
$Form.Controls.Add($BtnStop)

function Stop-Relay($reason) {
  $Script:IsRunning = $false
  if ($Script:AgentProc -and !$Script:AgentProc.HasExited) {
    try { $Script:AgentProc.Kill() } catch {}
  }
  if ($SessionCode) {
    try {
      Invoke-RestMethod -Uri "$ServerUrl/api/sessions/$SessionCode" -Method Delete -TimeoutSec 3 | Out-Null
    } catch {}
  }
}

$BtnStop.Add_Click({
  $LblStatus.Text = "🛑 Stopping Campus Relay..."
  Stop-Relay "Stopped manually by Hosteller"
  $Form.Close()
})

# Find or Download Portable Node.js
function Get-NodeBinary {
  $cmd = Get-Command node -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $PortableNode = Join-Path $WorkDir "node.exe"
  if (Test-Path $PortableNode) { return $PortableNode }
  Append-Log "Node.js not found on PC. Downloading portable runtime (one-time, ~30MB)..."
  $LblStatus.Text = "⬇️ Setting up portable runtime (10 seconds)..."
  [System.Windows.Forms.Application]::DoEvents()
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  Invoke-WebRequest -Uri "https://nodejs.org/dist/v20.18.0/win-x64/node.exe" -OutFile $PortableNode
  Append-Log "Portable runtime ready!"
  return $PortableNode
}

$LogFile = Join-Path $WorkDir "relay-output.log"
$Script:LastLogPos = 0

function Start-RelayProcess {
  try {
    $NodeBin = Get-NodeBinary
    if (Test-Path $LogFile) { Remove-Item $LogFile -Force -ErrorAction SilentlyContinue }
    $ArgsList = @("\`"$AgentPath\`"", "--server", "\`"$ServerUrl\`"", "--name", "\`"$HostellerName\`"", "--duration", "$DurationMins")
    if ($SessionCode) { $ArgsList += @("--session", "\`"$SessionCode\`"") }
    if ($RequestId) { $ArgsList += @("--request", "\`"$RequestId\`"") }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $NodeBin
    $psi.Arguments = ($ArgsList -join " ")
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $Script:AgentProc = New-Object System.Diagnostics.Process
    $Script:AgentProc.StartInfo = $psi
    $Script:AgentProc.Start() | Out-Null
    $Script:IsRunning = $true

    $LblStatus.Text = "🟢 LIVE — Sharing College Wi-Fi (*.amrita.edu only)"
    Append-Log "Connected to $ServerUrl"
    Append-Log "Only *.amrita.edu traffic is allowed. Passwords are E2E TLS encrypted."
  } catch {
    $LblStatus.Text = "❌ Error starting relay"
    Append-Log ("Error: " + $_.Exception.Message)
  }
}

# 1-Second UI Countdown & Log Poller Timer
$UiTimer = New-Object System.Windows.Forms.Timer
$UiTimer.Interval = 1000
$UiTimer.Add_Tick({
  if (!$Script:IsRunning) { return }

  # Drain async stdout lines if available
  while ($Script:AgentProc -and !$Script:AgentProc.StandardOutput.EndOfStream -and $Script:AgentProc.StandardOutput.Peek() -ge 0) {
    $line = $Script:AgentProc.StandardOutput.ReadLine()
    if ($line) {
      Append-Log $line.Trim()
      if ($line -match "Session Code:\\s*\\[\\s*(AMR-[A-Z0-9]+)\\s*\\]") {
        $Script:SessionCode = $Matches[1]
        $LblCode.Text = "Session Code: $Script:SessionCode   •   Limit: $DurationMins mins"
      }
    }
  }

  $Script:RemainingSecs--
  if ($Script:RemainingSecs -le 0) {
    $UiTimer.Stop()
    $LblTimer.Text = "00:00"
    $LblStatus.Text = "⏰ Session Time Expired — Disconnected!"
    Append-Log "Approved duration finished. Closing automatically..."
    Stop-Relay "Timer expired"
    Start-Sleep -Seconds 2
    $Form.Close()
    return
  }

  $mins = [math]::Floor($Script:RemainingSecs / 60)
  $secs = $Script:RemainingSecs % 60
  $LblTimer.Text = ("{0:D2}:{1:D2}" -f $mins, $secs)

  if ($Script:AgentProc -and $Script:AgentProc.HasExited) {
    $UiTimer.Stop()
    $LblStatus.Text = "🛑 Session Ended"
    Append-Log "Relay connection closed."
    $Script:IsRunning = $false
  }
})

$Form.Add_Shown({
  Start-RelayProcess
  $UiTimer.Start()
})

$Form.Add_FormClosing({
  $UiTimer.Stop()
  Stop-Relay "Window closed by Hosteller"
})

[void]$Form.ShowDialog()
`;

  const psB64 = Buffer.from(psScript, 'utf16le').toString('base64');

  return [
    '@echo off',
    'title Launching Amrita Campus Bridge GUI App...',
    'echo Starting Amrita Campus Bridge Desktop App (this terminal window will close automatically)...',
    `start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand ${psB64}`,
    'exit'
  ].join('\r\n');
}

function buildDayScholarGuiBat({
  serverUrl = 'https://amrita-campus-vpn.onrender.com',
  sessionCode = ''
}) {
  const bridgeJsPath = path.join(__dirname, '..', 'day-scholar-bridge.js');
  const bridgeJsB64 = fs.readFileSync(bridgeJsPath).toString('base64');

  const safeServer = String(serverUrl).replace(/["'`$]/g, '');
  const safeSession = String(sessionCode).replace(/[^A-Za-z0-9\-_]/g, '');

  const psScript = `
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$WorkDir = Join-Path $env:TEMP "AmritaCampusBridge"
if (!(Test-Path $WorkDir)) { New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null }

$BridgePath = Join-Path $WorkDir "day-scholar-bridge.js"
$BridgeB64 = "${bridgeJsB64}"
[System.IO.File]::WriteAllBytes($BridgePath, [System.Convert]::FromBase64String($BridgeB64))

$ServerUrl = "${safeServer}"
$SessionCode = "${safeSession}"
$Script:BridgeProc = $null

$Form = New-Object System.Windows.Forms.Form
$Form.Text = "Amrita Campus Bridge — Day Scholar Connector App"
$Form.Size = New-Object System.Drawing.Size(460, 340)
$Form.StartPosition = "CenterScreen"
$Form.FormBorderStyle = "FixedSingle"
$Form.MaximizeBox = $false
$Form.BackColor = [System.Drawing.Color]::FromArgb(15, 23, 42)
$Form.ForeColor = [System.Drawing.Color]::FromArgb(248, 250, 252)

$LblHeader = New-Object System.Windows.Forms.Label
$LblHeader.Text = "🎒 Amrita Campus Bridge (Day Scholar App)"
$LblHeader.Font = New-Object System.Drawing.Font("Segoe UI", 13, [System.Drawing.FontStyle]::Bold)
$LblHeader.ForeColor = [System.Drawing.Color]::FromArgb(56, 189, 248)
$LblHeader.Location = New-Object System.Drawing.Point(20, 18)
$LblHeader.Size = New-Object System.Drawing.Size(410, 28)
$Form.Controls.Add($LblHeader)

$PanelCard = New-Object System.Windows.Forms.Panel
$PanelCard.Location = New-Object System.Drawing.Point(20, 58)
$PanelCard.Size = New-Object System.Drawing.Size(405, 155)
$PanelCard.BackColor = [System.Drawing.Color]::FromArgb(30, 41, 59)
$Form.Controls.Add($PanelCard)

$LblStatus = New-Object System.Windows.Forms.Label
$LblStatus.Text = "🟢 Cloud Tunnel Active on 127.0.0.1:8080"
$LblStatus.Font = New-Object System.Drawing.Font("Segoe UI", 10.5, [System.Drawing.FontStyle]::Bold)
$LblStatus.ForeColor = [System.Drawing.Color]::FromArgb(16, 185, 129)
$LblStatus.Location = New-Object System.Drawing.Point(14, 14)
$LblStatus.Size = New-Object System.Drawing.Size(375, 24)
$PanelCard.Controls.Add($LblStatus)

$LblHostInfo = New-Object System.Windows.Forms.Label
$LblHostInfo.Text = "Checking active Hosteller session on Render..."
$LblHostInfo.Font = New-Object System.Drawing.Font("Segoe UI", 9.5)
$LblHostInfo.ForeColor = [System.Drawing.Color]::FromArgb(226, 232, 240)
$LblHostInfo.Location = New-Object System.Drawing.Point(14, 46)
$LblHostInfo.Size = New-Object System.Drawing.Size(375, 44)
$PanelCard.Controls.Add($LblHostInfo)

$LblHint = New-Object System.Windows.Forms.Label
$LblHint.Text = "👉 Turn ON the Amrita Campus VPN Chrome Extension in your browser to open *.amrita.edu websites!"
$LblHint.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$LblHint.ForeColor = [System.Drawing.Color]::FromArgb(148, 163, 184)
$LblHint.Location = New-Object System.Drawing.Point(14, 98)
$LblHint.Size = New-Object System.Drawing.Size(375, 44)
$PanelCard.Controls.Add($LblHint)

$BtnStop = New-Object System.Windows.Forms.Button
$BtnStop.Text = "🛑 DISCONNECT & CLOSE APP"
$BtnStop.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$BtnStop.BackColor = [System.Drawing.Color]::FromArgb(220, 38, 38)
$BtnStop.ForeColor = [System.Drawing.Color]::White
$BtnStop.FlatStyle = "Flat"
$BtnStop.Location = New-Object System.Drawing.Point(20, 232)
$BtnStop.Size = New-Object System.Drawing.Size(405, 44)
$Form.Controls.Add($BtnStop)

function Stop-Bridge {
  if ($Script:BridgeProc -and !$Script:BridgeProc.HasExited) {
    try { $Script:BridgeProc.Kill() } catch {}
  }
}

$BtnStop.Add_Click({
  Stop-Bridge
  $Form.Close()
})

function Get-NodeBinary {
  $cmd = Get-Command node -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $PortableNode = Join-Path $WorkDir "node.exe"
  if (Test-Path $PortableNode) { return $PortableNode }
  $LblStatus.Text = "⬇️ Downloading portable runtime (10s)..."
  [System.Windows.Forms.Application]::DoEvents()
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  Invoke-WebRequest -Uri "https://nodejs.org/dist/v20.18.0/win-x64/node.exe" -OutFile $PortableNode
  return $PortableNode
}

$PollTimer = New-Object System.Windows.Forms.Timer
$PollTimer.Interval = 3000
$PollTimer.Add_Tick({
  try {
    $st = Invoke-RestMethod -Uri "$ServerUrl/api/status" -Method Get -TimeoutSec 3
    if ($st.hasActiveHosteller -and $st.activeSession) {
      $remM = [math]::Floor($st.activeSession.remainingSeconds / 60)
      $remS = $st.activeSession.remainingSeconds % 60
      $LblHostInfo.Text = ("Hosteller Connected: {0} (Code: {1})\`r\`nTime Remaining: {2:D2}:{3:D2}" -f $st.activeSession.hostellerName, $st.activeSession.sessionCode, $remM, $remS)
    } else {
      $LblHostInfo.Text = "⏳ Waiting for a Hosteller on College Wi-Fi to start their Hosteller App..."
    }
  } catch {}
})

$Form.Add_Shown({
  try {
    $NodeBin = Get-NodeBinary
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $NodeBin
    $psi.Arguments = "\`"$BridgePath\`""
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables["AMRITA_BROKER_URL"] = $ServerUrl
    $Script:BridgeProc = New-Object System.Diagnostics.Process
    $Script:BridgeProc.StartInfo = $psi
    $Script:BridgeProc.Start() | Out-Null
    $LblStatus.Text = "🟢 Cloud Bridge Running (127.0.0.1:8080)"
    $PollTimer.Start()
  } catch {
    $LblStatus.Text = "❌ Error: " + $_.Exception.Message
  }
})

$Form.Add_FormClosing({
  $PollTimer.Stop()
  Stop-Bridge
})

[void]$Form.ShowDialog()
`;

  const psB64 = Buffer.from(psScript, 'utf16le').toString('base64');

  return [
    '@echo off',
    'title Launching Amrita Day Scholar Connector App...',
    `start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand ${psB64}`,
    'exit'
  ].join('\r\n');
}

module.exports = {
  buildHostellerGuiBat,
  buildDayScholarGuiBat
};
