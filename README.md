# 🎓 Amrita Campus Bridge — Day Scholar ↔ Hosteller Peer VPN & Chrome Extension

A complete **Split-Tunnel Browser VPN Extension + Peer-to-Peer Hosteller Relay Platform** built for Amrita students. Day scholars outside campus can request a time-boxed relay session (15 / 30 / 60 minutes) from a hosteller connected to college Wi-Fi and log into `*.amrita.edu` portals **without ever sharing their login ID or password**.

---

## 🔒 How Privacy & Zero-Password-Sharing Works

1. **End-to-End TLS Encryption (`HTTP CONNECT`)**:
   When a Day Scholar opens `https://aums.amrita.edu` (or any `https://*.amrita.edu` portal), Chrome performs the TLS handshake **directly** with the college server through an encrypted TCP tunnel. The Hosteller's laptop only relays encrypted bytes and **can never read** usernames, passwords, cookies, or grades.
2. **Double-Sided Split-Tunnel Domain Guard**:
   - **Browser Extension PAC Script**: Only routes `*.amrita.edu` (and custom campus IPs you configure) through the proxy. All other tabs (YouTube, Google, etc.) use the Day Scholar's own internet (`DIRECT`).
   - **Hosteller Agent Guard**: Even if someone tried to send non-college traffic through the tunnel, the Hosteller's agent rejects any domain outside the `*.amrita.edu` whitelist with `403 Forbidden`.
3. **Time-Boxed & Instant Revocation**:
   Sessions automatically terminate when the approved timer expires, or whenever the Hosteller clicks **Stop Sharing** / presses `Ctrl+C`.

---

## 📂 Project Structure

- [extension/manifest.json](file:///c:/Users/goela/Desktop/Amrita_VPN/extension/manifest.json) — Manifest V3 Chrome / Edge / Brave Extension
- [extension/background.js](file:///c:/Users/goela/Desktop/Amrita_VPN/extension/background.js) — PAC Split-Tunnel Proxy Engine & Auto-Expiry Watchdog
- [extension/popup.html](file:///c:/Users/goela/Desktop/Amrita_VPN/extension/popup.html) / [extension/popup.js](file:///c:/Users/goela/Desktop/Amrita_VPN/extension/popup.js) — Extension UI (Connect, Ask Hosteller, Split-Tunnel Domains, Config)
- [server/server.js](file:///c:/Users/goela/Desktop/Amrita_VPN/server/server.js) — Coordination Platform (`:3000`) & Forward Proxy Gateway (`:8080`)
- [server/public/index.html](file:///c:/Users/goela/Desktop/Amrita_VPN/server/public/index.html) — Web Dashboard for Day Scholars & Hostellers
- [hosteller-agent/agent.js](file:///c:/Users/goela/Desktop/Amrita_VPN/hosteller-agent/agent.js) — Lightweight zero-dependency Node.js Relay Agent for Hostellers on College Wi-Fi
- [Start-Platform-Server.bat](file:///c:/Users/goela/Desktop/Amrita_VPN/Start-Platform-Server.bat) & [Start-Hosteller-Relay.bat](file:///c:/Users/goela/Desktop/Amrita_VPN/Start-Hosteller-Relay.bat) — 1-click Windows launchers

---

## 🚀 Step-by-Step Setup & Network Switching Guide

### Step 1: Load the Browser Extension in Chrome / Edge / Brave
1. Open your browser and go to `chrome://extensions` (or `edge://extensions`).
2. Enable **Developer mode** (toggle in the top-right corner).
3. Click **Load unpacked** and select the folder:
   `c:\Users\goela\Desktop\Amrita_VPN\extension`
4. Pin **Amrita Campus Bridge VPN** to your browser toolbar.

### Step 2: Start the Coordination & Proxy Platform Server
Run the server locally (for testing on your laptop) or deploy `server/server.js` to any cloud host (Railway, Render, VPS):
```powershell
npm start
# Or double-click Start-Platform-Server.bat
```
- **Web Dashboard**: `http://localhost:3000`
- **Browser Proxy Gateway**: `http://localhost:8080`

### Step 3: How to Test Switching Between Networks on Your Own Laptop Right Now
You mentioned you are currently on your regular network and can switch to the college network when instructed:

1. **While Connected to College Wi-Fi (Simulating the Hosteller)**:
   - Connect your Wi-Fi to **Amrita College Wi-Fi**.
   - Run the Hosteller Relay Agent:
     ```powershell
     npm run hosteller
     # Or double-click Start-Hosteller-Relay.bat
     ```
   - Note the generated **Session Code** (e.g., `AMR-4F9A`) shown in the terminal or on `http://localhost:3000`.
2. **Using the Extension (Simulating the Day Scholar)**:
   - Click the **Amrita Campus VPN** extension icon in your browser.
   - You can either:
     - Go to the **🙋 Ask Hosteller** tab to request a 15 / 30 / 60 minute slot, **OR**
     - Enter the **Session Code** on the **🔌 Connect** tab and click the big **Power (⏻) Button**.
   - The badge turns green (`ON`), and all `*.amrita.edu` websites now route through the active Hosteller tunnel!
3. **When Hosting Across Two Separate Laptops (Real Day Scholar at Home + Hosteller in Hostel)**:
   - Host `server/server.js` on a public URL (e.g., Railway / Render / Cloudflare Tunnel / Tailscale IP).
   - **Hosteller (on College Wi-Fi)** runs:
     ```powershell
     node hosteller-agent/agent.js --server https://your-broker-url.up.railway.app --name "Hostel-Friend" --duration 30
     ```
   - **Day Scholar (on Home Wi-Fi)** sets the **Coordination Platform URL** and **Proxy Host/Port** in the extension's **⚙️ Config** tab and clicks **Connect**!
