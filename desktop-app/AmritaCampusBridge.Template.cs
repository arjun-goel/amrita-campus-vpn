using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AmritaCampusBridge
{
    public class RequestItem
    {
        public string Id;
        public string ScholarName;
        public string Purpose;
        public int DurationMinutes;
        public string Status;
        public string SessionCode;
    }

    public class MainForm : Form
    {
        private const string DEFAULT_SERVER_URL = "https://amrita-campus-vpn.onrender.com";
        private const string AGENT_B64 = "__AGENT_B64__";
        private const string BRIDGE_B64 = "__BRIDGE_B64__";

        private string workDir;
        private string agentScriptPath;
        private string bridgeScriptPath;
        private string serverUrl = DEFAULT_SERVER_URL;

        // UI Controls - Header
        private Label lblCloudStatus;
        private TabControl tabControl;

        // Hosteller Tab Controls
        private ListView lvRequests;
        private Button btnApproveSelected;
        private Button btnRefreshReqs;
        private TextBox txtHostName;
        private ComboBox cmbHostDuration;
        private Button btnStartInstantHost;
        private Label lblHostServerState;
        private Label lblHostSessionCode;
        private Label lblHostTimer;
        private CheckBox chkAutoCloseApp;
        private Button btnStopHostServer;
        private TextBox txtHostLog;

        // Day Scholar Tab Controls
        private TextBox txtScholarName;
        private ComboBox cmbScholarPurpose;
        private ComboBox cmbScholarDuration;
        private Button btnSendRequest;
        private Label lblScholarReqFeedback;
        private Label lblActiveHostStatus;
        private Label lblScholarBridgeState;
        private Button btnToggleScholarBridge;
        private TextBox txtScholarLog;

        // Runtime State
        private Process hostProcess = null;
        private bool isHostRunning = false;
        private int hostRemainingSeconds = 0;
        private string activeHostSessionCode = "";

        private Process bridgeProcess = null;
        private bool isBridgeRunning = false;

        private System.Windows.Forms.Timer countdownTimer;
        private System.Windows.Forms.Timer pollTimer;
        private List<RequestItem> currentPendingRequests = new List<RequestItem>();

        public MainForm()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            workDir = Path.Combine(Path.GetTempPath(), "AmritaCampusBridgeApp");
            Directory.CreateDirectory(workDir);
            agentScriptPath = Path.Combine(workDir, "agent.js");
            bridgeScriptPath = Path.Combine(workDir, "day-scholar-bridge.js");
            File.WriteAllBytes(agentScriptPath, Convert.FromBase64String(AGENT_B64));
            File.WriteAllBytes(bridgeScriptPath, Convert.FromBase64String(BRIDGE_B64));

            InitializeGui();

            countdownTimer = new System.Windows.Forms.Timer();
            countdownTimer.Interval = 1000;
            countdownTimer.Tick += CountdownTimer_Tick;
            countdownTimer.Start();

            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 3000;
            pollTimer.Tick += (s, e) => PollServerStatusAsync();
            pollTimer.Start();

            this.Shown += (s, e) => PollServerStatusAsync();
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeGui()
        {
            this.Text = "Amrita Campus Bridge — All-in-One Desktop App (Hosteller & Day Scholar)";
            this.Size = new Size(880, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(11, 15, 25);
            this.ForeColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f);

            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = Color.FromArgb(17, 24, 39)
            };
            this.Controls.Add(topBar);

            Label lblTitle = new Label
            {
                Text = "🎓 Amrita Campus Bridge",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(18, 8),
                Size = new Size(420, 26)
            };
            topBar.Controls.Add(lblTitle);

            Label lblSub = new Label
            {
                Text = "One-Time Desktop App • Approve Requests, Run Campus Server & Auto-Stop on Timer (No Terminal Needed)",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 35),
                Size = new Size(580, 20)
            };
            topBar.Controls.Add(lblSub);

            lblCloudStatus = new Label
            {
                Text = "🔄 Connecting to Cloud...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(600, 18),
                Size = new Size(250, 24)
            };
            topBar.Controls.Add(lblCloudStatus);

            tabControl = new TabControl
            {
                Location = new Point(14, 72),
                Size = new Size(846, 480),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            this.Controls.Add(tabControl);

            TabPage tabHosteller = new TabPage("🏠 Hosteller Mode (Share College Wi-Fi)")
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(248, 250, 252)
            };
            TabPage tabScholar = new TabPage("🎒 Day Scholar Mode (Access College Websites)")
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(248, 250, 252)
            };

            tabControl.TabPages.Add(tabHosteller);
            tabControl.TabPages.Add(tabScholar);

            BuildHostellerTab(tabHosteller);
            BuildScholarTab(tabScholar);
        }

        private void BuildHostellerTab(TabPage page)
        {
            Panel leftPanel = new Panel
            {
                Location = new Point(14, 14),
                Size = new Size(410, 422),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(leftPanel);

            Label lblReqHeader = new Label
            {
                Text = "1. Incoming Day Scholar Requests",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(14, 12),
                Size = new Size(280, 22)
            };
            leftPanel.Controls.Add(lblReqHeader);

            btnRefreshReqs = new Button
            {
                Text = "🔄 Refresh",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(310, 10),
                Size = new Size(86, 26)
            };
            btnRefreshReqs.Click += (s, e) => PollServerStatusAsync();
            leftPanel.Controls.Add(btnRefreshReqs);

            Label lblReqHint = new Label
            {
                Text = "Select a student request below and click Approve & Start Server:",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 36),
                Size = new Size(380, 18)
            };
            leftPanel.Controls.Add(lblReqHint);

            lvRequests = new ListView
            {
                Location = new Point(14, 58),
                Size = new Size(382, 165),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                BackColor = Color.FromArgb(11, 15, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            lvRequests.Columns.Add("Student", 115);
            lvRequests.Columns.Add("Time", 55);
            lvRequests.Columns.Add("Code", 78);
            lvRequests.Columns.Add("Purpose", 125);
            leftPanel.Controls.Add(lvRequests);

            btnApproveSelected = new Button
            {
                Text = "✅ Approve & Start Server for Selected Request",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(5, 46, 22),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(14, 230),
                Size = new Size(382, 36)
            };
            btnApproveSelected.Click += BtnApproveSelected_Click;
            leftPanel.Controls.Add(btnApproveSelected);

            Label lblInstantHeader = new Label
            {
                Text = "Or Start an Instant Open Relay Server:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(14, 278),
                Size = new Size(350, 20)
            };
            leftPanel.Controls.Add(lblInstantHeader);

            Label lblHName = new Label
            {
                Text = "Your Name / Hostel:",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 304),
                Size = new Size(190, 18)
            };
            leftPanel.Controls.Add(lblHName);

            txtHostName = new TextBox
            {
                Text = "Campus-Hosteller",
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 324),
                Size = new Size(210, 26)
            };
            leftPanel.Controls.Add(txtHostName);

            Label lblHDur = new Label
            {
                Text = "Auto-Stop Timer:",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(236, 304),
                Size = new Size(150, 18)
            };
            leftPanel.Controls.Add(lblHDur);

            cmbHostDuration = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(236, 324),
                Size = new Size(160, 26)
            };
            cmbHostDuration.Items.AddRange(new object[] { "15 Minutes", "30 Minutes", "60 Minutes" });
            cmbHostDuration.SelectedIndex = 1;
            leftPanel.Controls.Add(cmbHostDuration);

            btnStartInstantHost = new Button
            {
                Text = "▶️ Start Instant Campus Server",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(168, 28, 75),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(14, 362),
                Size = new Size(382, 36)
            };
            btnStartInstantHost.Click += BtnStartInstantHost_Click;
            leftPanel.Controls.Add(btnStartInstantHost);

            Panel rightPanel = new Panel
            {
                Location = new Point(436, 14),
                Size = new Size(388, 422),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(rightPanel);

            lblHostServerState = new Label
            {
                Text = "⚪ SERVER IDLE (Not Sharing)",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 14),
                Size = new Size(355, 24)
            };
            rightPanel.Controls.Add(lblHostServerState);

            lblHostSessionCode = new Label
            {
                Text = "Session Code: —",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 40),
                Size = new Size(355, 20)
            };
            rightPanel.Controls.Add(lblHostSessionCode);

            lblHostTimer = new Label
            {
                Text = "00:00",
                Font = new Font("Consolas", 30f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(12, 64),
                Size = new Size(210, 48)
            };
            rightPanel.Controls.Add(lblHostTimer);

            chkAutoCloseApp = new CheckBox
            {
                Text = "Also close this app window when timer hits 00:00",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Checked = false,
                Location = new Point(18, 118),
                Size = new Size(350, 22)
            };
            rightPanel.Controls.Add(chkAutoCloseApp);

            btnStopHostServer = new Button
            {
                Text = "🛑 STOP SERVER NOW",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(220, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Location = new Point(16, 146),
                Size = new Size(355, 40)
            };
            btnStopHostServer.Click += (s, e) => StopHostellerServer("Manually stopped by Hosteller");
            rightPanel.Controls.Add(btnStopHostServer);

            Label lblLogTitle = new Label
            {
                Text = "🔒 Live Encrypted Activity Log (*.amrita.edu only):",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 196),
                Size = new Size(355, 18)
            };
            rightPanel.Controls.Add(lblLogTitle);

            txtHostLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(9, 13, 22),
                ForeColor = Color.FromArgb(167, 243, 208),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(16, 218),
                Size = new Size(355, 188)
            };
            rightPanel.Controls.Add(txtHostLog);
            AppendHostLog("Ready. Make sure you are on Amrita College Wi-Fi, then approve a request or click Start.");
        }

        private void BuildScholarTab(TabPage page)
        {
            Panel leftPanel = new Panel
            {
                Location = new Point(14, 14),
                Size = new Size(400, 422),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(leftPanel);

            Label lblReqTitle = new Label
            {
                Text = "1. Ask a Hosteller for Campus Wi-Fi",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 14),
                Size = new Size(360, 24)
            };
            leftPanel.Controls.Add(lblReqTitle);

            Label lblSName = new Label
            {
                Text = "Your Name / Roll Number Hint:",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(16, 52),
                Size = new Size(350, 20)
            };
            leftPanel.Controls.Add(lblSName);

            txtScholarName = new TextBox
            {
                Text = "Day Scholar Student",
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f),
                Location = new Point(16, 75),
                Size = new Size(366, 28)
            };
            leftPanel.Controls.Add(txtScholarName);

            Label lblSPurpose = new Label
            {
                Text = "Purpose (Which college portal?):",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(16, 116),
                Size = new Size(350, 20)
            };
            leftPanel.Controls.Add(lblSPurpose);

            cmbScholarPurpose = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Location = new Point(16, 140),
                Size = new Size(366, 28)
            };
            cmbScholarPurpose.Items.AddRange(new object[] {
                "AUMS Login & Attendance Check",
                "Course Registration / Exam Results",
                "Amrita CMS / Library Portal",
                "Lab / Intranet Portal Access"
            });
            cmbScholarPurpose.SelectedIndex = 0;
            leftPanel.Controls.Add(cmbScholarPurpose);

            Label lblSDur = new Label
            {
                Text = "Duration Needed (Hosteller server auto-stops after this):",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(16, 182),
                Size = new Size(366, 20)
            };
            leftPanel.Controls.Add(lblSDur);

            cmbScholarDuration = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Location = new Point(16, 206),
                Size = new Size(366, 28)
            };
            cmbScholarDuration.Items.AddRange(new object[] { "15 Minutes", "30 Minutes", "60 Minutes" });
            cmbScholarDuration.SelectedIndex = 1;
            leftPanel.Controls.Add(cmbScholarDuration);

            btnSendRequest = new Button
            {
                Text = "🚀 Send Request to Hostellers",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(168, 28, 75),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(16, 250),
                Size = new Size(366, 42)
            };
            btnSendRequest.Click += BtnSendRequest_Click;
            leftPanel.Controls.Add(btnSendRequest);

            lblScholarReqFeedback = new Label
            {
                Text = "Your login ID & password remain 100% encrypted via HTTPS.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(167, 243, 208),
                Location = new Point(16, 306),
                Size = new Size(366, 95)
            };
            leftPanel.Controls.Add(lblScholarReqFeedback);

            Panel rightPanel = new Panel
            {
                Location = new Point(426, 14),
                Size = new Size(398, 422),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(rightPanel);

            Label lblConnTitle = new Label
            {
                Text = "2. Connect Your Browser to Campus Relay",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 14),
                Size = new Size(365, 24)
            };
            rightPanel.Controls.Add(lblConnTitle);

            lblActiveHostStatus = new Label
            {
                Text = "⏳ Checking if a Hosteller is currently online...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(252, 211, 77),
                Location = new Point(16, 48),
                Size = new Size(365, 42)
            };
            rightPanel.Controls.Add(lblActiveHostStatus);

            lblScholarBridgeState = new Label
            {
                Text = "Local Extension Bridge (127.0.0.1:8080): OFF",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 96),
                Size = new Size(365, 22)
            };
            rightPanel.Controls.Add(lblScholarBridgeState);

            btnToggleScholarBridge = new Button
            {
                Text = "🔌 START DAY SCHOLAR VPN BRIDGE",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(5, 46, 22),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(16, 126),
                Size = new Size(365, 44)
            };
            btnToggleScholarBridge.Click += BtnToggleScholarBridge_Click;
            rightPanel.Controls.Add(btnToggleScholarBridge);

            txtScholarLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(9, 13, 22),
                ForeColor = Color.FromArgb(186, 230, 253),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f),
                Location = new Point(16, 186),
                Size = new Size(365, 220)
            };
            rightPanel.Controls.Add(txtScholarLog);
            AppendScholarLog("Click 'START DAY SCHOLAR VPN BRIDGE' and turn ON the Chrome Extension to open *.amrita.edu sites.");
        }

        private void AppendHostLog(string msg)
        {
            if (txtHostLog.InvokeRequired)
            {
                txtHostLog.BeginInvoke(new Action<string>(AppendHostLog), msg);
                return;
            }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            txtHostLog.AppendText("[" + ts + "] " + msg + Environment.NewLine);
        }

        private void AppendScholarLog(string msg)
        {
            if (txtScholarLog.InvokeRequired)
            {
                txtScholarLog.BeginInvoke(new Action<string>(AppendScholarLog), msg);
                return;
            }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            txtScholarLog.AppendText("[" + ts + "] " + msg + Environment.NewLine);
        }

        private string EnsureNodeBinary()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("node", "-v")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(2000);
                    if (p.ExitCode == 0) return "node";
                }
            }
            catch { }

            string portableNode = Path.Combine(workDir, "node.exe");
            if (File.Exists(portableNode)) return portableNode;

            AppendHostLog("Downloading portable runtime once (~30MB)...");
            AppendScholarLog("Downloading portable runtime once (~30MB)...");
            Application.DoEvents();
            using (WebClient wc = new WebClient())
            {
                wc.DownloadFile("https://nodejs.org/dist/v20.18.0/win-x64/node.exe", portableNode);
            }
            AppendHostLog("Portable runtime ready!");
            return portableNode;
        }

        private int ParseDurationChoice(ComboBox cmb)
        {
            string txt = cmb.SelectedItem != null ? cmb.SelectedItem.ToString() : "30";
            if (txt.StartsWith("15")) return 15;
            if (txt.StartsWith("60")) return 60;
            return 30;
        }

        private void BtnApproveSelected_Click(object sender, EventArgs e)
        {
            if (lvRequests.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please click on a Day Scholar request in the list first, or use 'Start Instant Campus Server' below.", "Select a Request", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RequestItem req = (RequestItem)lvRequests.SelectedItems[0].Tag;
            StartHostellerServer(req.Id, req.SessionCode, req.DurationMinutes, req.ScholarName);
        }

        private void BtnStartInstantHost_Click(object sender, EventArgs e)
        {
            int mins = ParseDurationChoice(cmbHostDuration);
            StartHostellerServer("", "", mins, "Any Day Scholar");
        }

        private void StartHostellerServer(string requestId, string sessionCode, int durationMins, string scholarName)
        {
            StopHostellerServer(null);

            try
            {
                string nodeBin = EnsureNodeBinary();
                string hostName = string.IsNullOrWhiteSpace(txtHostName.Text) ? "Campus-Hosteller" : txtHostName.Text.Trim();

                StringBuilder args = new StringBuilder();
                args.AppendFormat("\"{0}\" --server \"{1}\" --name \"{2}\" --duration {3}",
                    agentScriptPath, serverUrl, hostName, durationMins);
                if (!string.IsNullOrEmpty(sessionCode))
                    args.AppendFormat(" --session \"{0}\"", sessionCode);
                if (!string.IsNullOrEmpty(requestId))
                    args.AppendFormat(" --request \"{0}\"", requestId);

                ProcessStartInfo psi = new ProcessStartInfo(nodeBin, args.ToString())
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                hostProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                hostProcess.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data))
                    {
                        string line = ev.Data.Trim();
                        AppendHostLog(line);
                        Match m = Regex.Match(line, @"Session Code:\s*\[\s*(AMR-[A-Z0-9]+)\s*\]");
                        if (m.Success)
                        {
                            activeHostSessionCode = m.Groups[1].Value;
                            this.BeginInvoke(new Action(() =>
                            {
                                lblHostSessionCode.Text = "Session Code: " + activeHostSessionCode + "  (Serving: " + scholarName + ")";
                            }));
                        }
                    }
                };
                hostProcess.ErrorDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data)) AppendHostLog("ERR: " + ev.Data.Trim());
                };

                hostProcess.Start();
                hostProcess.BeginOutputReadLine();
                hostProcess.BeginErrorReadLine();

                isHostRunning = true;
                activeHostSessionCode = sessionCode;
                hostRemainingSeconds = durationMins * 60;

                lblHostServerState.Text = "🟢 SERVER LIVE (Sharing *.amrita.edu)";
                lblHostServerState.ForeColor = Color.FromArgb(16, 185, 129);
                lblHostSessionCode.Text = "Session Code: " + (string.IsNullOrEmpty(sessionCode) ? "Assigning..." : sessionCode) + "  (Serving: " + scholarName + ")";
                btnStopHostServer.Enabled = true;

                AppendHostLog("Started Campus Wi-Fi Relay for " + durationMins + " minutes.");
                PollServerStatusAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start Hosteller Relay: " + ex.Message);
            }
        }

        private void StopHostellerServer(string reason)
        {
            isHostRunning = false;
            hostRemainingSeconds = 0;

            if (hostProcess != null)
            {
                try
                {
                    if (!hostProcess.HasExited) hostProcess.Kill();
                }
                catch { }
                hostProcess = null;
            }

            if (!string.IsNullOrEmpty(activeHostSessionCode))
            {
                string codeToRevoke = activeHostSessionCode;
                activeHostSessionCode = "";
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/sessions/" + codeToRevoke);
                        req.Method = "DELETE";
                        req.Timeout = 3000;
                        using (WebResponse res = req.GetResponse()) { }
                    }
                    catch { }
                });
            }

            lblHostServerState.Text = "⚪ SERVER IDLE (Not Sharing)";
            lblHostServerState.ForeColor = Color.FromArgb(148, 163, 184);
            lblHostTimer.Text = "00:00";
            btnStopHostServer.Enabled = false;

            if (!string.IsNullOrEmpty(reason))
            {
                AppendHostLog("🛑 " + reason);
                PollServerStatusAsync();
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (!isHostRunning) return;

            if (hostRemainingSeconds > 0)
            {
                hostRemainingSeconds--;
                int m = hostRemainingSeconds / 60;
                int s = hostRemainingSeconds % 60;
                lblHostTimer.Text = string.Format("{0:D2}:{1:D2}", m, s);

                if (hostRemainingSeconds == 0)
                {
                    bool shouldExit = chkAutoCloseApp.Checked;
                    StopHostellerServer("Approved duration finished! Server stopped automatically.");
                    if (shouldExit)
                    {
                        this.Close();
                    }
                }
            }
        }

        private void BtnSendRequest_Click(object sender, EventArgs e)
        {
            string sName = string.IsNullOrWhiteSpace(txtScholarName.Text) ? "Day Scholar" : txtScholarName.Text.Trim();
            string purpose = cmbScholarPurpose.SelectedItem != null ? cmbScholarPurpose.SelectedItem.ToString() : "AUMS Login";
            int duration = ParseDurationChoice(cmbScholarDuration);

            btnSendRequest.Enabled = false;
            lblScholarReqFeedback.Text = "Sending request to Hostellers...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/requests");
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    string json = string.Format("{{\"scholarName\":\"{0}\",\"purpose\":\"{1}\",\"durationMinutes\":{2}}}",
                        sName.Replace("\"", ""), purpose.Replace("\"", ""), duration);
                    byte[] body = Encoding.UTF8.GetBytes(json);
                    using (Stream st = req.GetRequestStream())
                    {
                        st.Write(body, 0, body.Length);
                    }
                    string respText;
                    using (WebResponse res = req.GetResponse())
                    using (StreamReader sr = new StreamReader(res.GetResponseStream()))
                    {
                        respText = sr.ReadToEnd();
                    }

                    Match m = Regex.Match(respText, @"""sessionCode""\s*:\s*""(AMR-[A-Z0-9]+)""");
                    string code = m.Success ? m.Groups[1].Value : "Created";

                    this.BeginInvoke(new Action(() =>
                    {
                        btnSendRequest.Enabled = true;
                        lblScholarReqFeedback.Text = "✅ Request Sent! Session Code: " + code + Environment.NewLine + "Waiting for a Hosteller to click Approve in their app...";
                        AppendScholarLog("Created request for " + duration + " mins (Session Code: " + code + ")");
                        if (!isBridgeRunning) ToggleScholarBridge(true);
                        PollServerStatusAsync();
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        btnSendRequest.Enabled = true;
                        lblScholarReqFeedback.Text = "❌ Error sending request: " + ex.Message;
                    }));
                }
            });
        }

        private void BtnToggleScholarBridge_Click(object sender, EventArgs e)
        {
            ToggleScholarBridge(!isBridgeRunning);
        }

        private void ToggleScholarBridge(bool start)
        {
            if (!start)
            {
                isBridgeRunning = false;
                if (bridgeProcess != null)
                {
                    try { if (!bridgeProcess.HasExited) bridgeProcess.Kill(); } catch { }
                    bridgeProcess = null;
                }
                lblScholarBridgeState.Text = "Local Extension Bridge (127.0.0.1:8080): OFF";
                lblScholarBridgeState.ForeColor = Color.FromArgb(148, 163, 184);
                btnToggleScholarBridge.Text = "🔌 START DAY SCHOLAR VPN BRIDGE";
                btnToggleScholarBridge.BackColor = Color.FromArgb(16, 185, 129);
                btnToggleScholarBridge.ForeColor = Color.FromArgb(5, 46, 22);
                AppendScholarLog("Stopped Day Scholar local proxy bridge.");
                return;
            }

            try
            {
                string nodeBin = EnsureNodeBinary();
                ProcessStartInfo psi = new ProcessStartInfo(nodeBin, "\"" + bridgeScriptPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.EnvironmentVariables["AMRITA_BROKER_URL"] = serverUrl;

                bridgeProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                bridgeProcess.OutputDataReceived += (s, ev) =>
                {
                    if (!string.IsNullOrEmpty(ev.Data)) AppendScholarLog(ev.Data.Trim());
                };
                bridgeProcess.Start();
                bridgeProcess.BeginOutputReadLine();

                isBridgeRunning = true;
                lblScholarBridgeState.Text = "🟢 Local Extension Bridge LIVE (127.0.0.1:8080)";
                lblScholarBridgeState.ForeColor = Color.FromArgb(16, 185, 129);
                btnToggleScholarBridge.Text = "🛑 STOP DAY SCHOLAR VPN BRIDGE";
                btnToggleScholarBridge.BackColor = Color.FromArgb(220, 38, 38);
                btnToggleScholarBridge.ForeColor = Color.White;
                AppendScholarLog("Bridge running on 127.0.0.1:8080 -> " + serverUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start Day Scholar Bridge: " + ex.Message);
            }
        }

        private void PollServerStatusAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/status");
                    req.Method = "GET";
                    req.Timeout = 4000;
                    string json;
                    using (WebResponse res = req.GetResponse())
                    using (StreamReader sr = new StreamReader(res.GetResponseStream()))
                    {
                        json = sr.ReadToEnd();
                    }

                    bool hasHost = json.Contains("\"hasActiveHosteller\":true");
                    string activeCode = "";
                    string activeHostName = "";
                    int remSecs = 0;

                    Match mActive = Regex.Match(json, @"""activeSession""\s*:\s*\{([^\}]+)\}");
                    if (mActive.Success)
                    {
                        string block = mActive.Groups[1].Value;
                        Match mc = Regex.Match(block, @"""sessionCode""\s*:\s*""([^""]+)""");
                        if (mc.Success) activeCode = mc.Groups[1].Value;
                        Match mh = Regex.Match(block, @"""hostellerName""\s*:\s*""([^""]+)""");
                        if (mh.Success) activeHostName = mh.Groups[1].Value;
                        Match mr = Regex.Match(block, @"""remainingSeconds""\s*:\s*(\d+)");
                        if (mr.Success) int.TryParse(mr.Groups[1].Value, out remSecs);
                    }

                    List<RequestItem> pending = new List<RequestItem>();
                    MatchCollection items = Regex.Matches(json, @"\{[^{}]*""id""\s*:\s*""REQ-[^""]+""[^{}]*\}");
                    foreach (Match it in items)
                    {
                        string obj = it.Value;
                        string st = ExtractJsonStr(obj, "status");
                        if (st == "pending")
                        {
                            int dur = 30;
                            Match md = Regex.Match(obj, @"""durationMinutes""\s*:\s*(\d+)");
                            if (md.Success) int.TryParse(md.Groups[1].Value, out dur);

                            pending.Add(new RequestItem
                            {
                                Id = ExtractJsonStr(obj, "id"),
                                ScholarName = ExtractJsonStr(obj, "scholarName"),
                                Purpose = ExtractJsonStr(obj, "purpose"),
                                DurationMinutes = dur,
                                Status = st,
                                SessionCode = ExtractJsonStr(obj, "sessionCode")
                            });
                        }
                    }

                    this.BeginInvoke(new Action(() =>
                    {
                        lblCloudStatus.Text = "🟢 Cloud Online (Render)";
                        lblCloudStatus.ForeColor = Color.FromArgb(16, 185, 129);

                        if (hasHost)
                        {
                            int rm = remSecs / 60;
                            int rs = remSecs % 60;
                            lblActiveHostStatus.Text = string.Format("🟢 Hosteller Online: {0} (Code: {1})" + Environment.NewLine + "Time Remaining: {2:D2}:{3:D2} — Ready for *.amrita.edu!", activeHostName, activeCode, rm, rs);
                            lblActiveHostStatus.ForeColor = Color.FromArgb(16, 185, 129);
                        }
                        else
                        {
                            lblActiveHostStatus.Text = "⏳ No Hosteller currently sharing." + Environment.NewLine + "Send a request on the left or ask a Hosteller to click Approve!";
                            lblActiveHostStatus.ForeColor = Color.FromArgb(252, 211, 77);
                        }

                        UpdateRequestsListView(pending);
                    }));
                }
                catch
                {
                    if (this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            lblCloudStatus.Text = "⚠️ Reconnecting to Cloud...";
                            lblCloudStatus.ForeColor = Color.FromArgb(245, 158, 11);
                        }));
                    }
                }
            });
        }

        private string ExtractJsonStr(string jsonObj, string key)
        {
            Match m = Regex.Match(jsonObj, "\"" + key + @"""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : "";
        }

        private void UpdateRequestsListView(List<RequestItem> pending)
        {
            string selectedId = lvRequests.SelectedItems.Count > 0
                ? ((RequestItem)lvRequests.SelectedItems[0].Tag).Id
                : null;

            bool same = pending.Count == currentPendingRequests.Count;
            if (same)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    if (pending[i].Id != currentPendingRequests[i].Id) { same = false; break; }
                }
            }
            if (same) return;

            currentPendingRequests = pending;
            lvRequests.BeginUpdate();
            lvRequests.Items.Clear();
            foreach (var r in pending)
            {
                ListViewItem lvi = new ListViewItem(r.ScholarName);
                lvi.SubItems.Add(r.DurationMinutes + "m");
                lvi.SubItems.Add(r.SessionCode);
                lvi.SubItems.Add(r.Purpose);
                lvi.Tag = r;
                if (r.Id == selectedId) lvi.Selected = true;
                lvRequests.Items.Add(lvi);
            }
            if (lvRequests.Items.Count > 0 && lvRequests.SelectedItems.Count == 0)
            {
                lvRequests.Items[0].Selected = true;
            }
            lvRequests.EndUpdate();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            countdownTimer.Stop();
            pollTimer.Stop();
            StopHostellerServer(null);
            ToggleScholarBridge(false);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
