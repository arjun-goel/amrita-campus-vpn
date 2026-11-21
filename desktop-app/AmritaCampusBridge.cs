using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Amrita Campus Bridge Desktop Client")]
[assembly: AssemblyDescription("Peer-to-Peer Campus Wi-Fi Split-Tunnel Relay for Amrita Students")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Amrita Campus Bridge Project")]
[assembly: AssemblyProduct("Amrita Campus Bridge")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Amrita Campus Bridge")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

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

    /// <summary>
    /// Pure Managed C# RFC 6455 WebSocket + Multiplexed Tunnel Client.
    /// Zero external binaries, zero temp files, zero child processes (passes Antivirus cleanly).
    /// </summary>
    public class WsTunnelClient : IDisposable
    {
        private TcpClient tcp;
        private Stream netStream;
        private readonly object writeLock = new object();
        private volatile bool closed = false;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public event Action<Dictionary<string, object>, byte[]> OnFrame;
        public event Action<string> OnDisconnected;

        public void Connect(string serverBaseUrl, string pathAndQuery)
        {
            Uri uri = new Uri(serverBaseUrl);
            bool isTls = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                         uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
            int port = uri.Port > 0 && !uri.IsDefaultPort ? uri.Port : (isTls ? 443 : 80);

            tcp = new TcpClient();
            tcp.NoDelay = true;
            tcp.Connect(uri.Host, port);

            Stream s = tcp.GetStream();
            if (isTls)
            {
                SslStream ssl = new SslStream(s, false);
                ssl.AuthenticateAsClient(uri.Host, null, SslProtocols.Tls12, false);
                s = ssl;
            }
            netStream = s;

            byte[] keyBytes = new byte[16];
            new Random().NextBytes(keyBytes);
            string wsKey = Convert.ToBase64String(keyBytes);

            string reqHeaders =
                "GET " + pathAndQuery + " HTTP/1.1\r\n" +
                "Host: " + uri.Authority + "\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Version: 13\r\n" +
                "Sec-WebSocket-Key: " + wsKey + "\r\n\r\n";

            byte[] reqBytes = Encoding.ASCII.GetBytes(reqHeaders);
            netStream.Write(reqBytes, 0, reqBytes.Length);
            netStream.Flush();

            // Read HTTP 101 response header byte-by-byte until \r\n\r\n
            List<byte> headerBuf = new List<byte>();
            int matchState = 0;
            while (true)
            {
                int b = netStream.ReadByte();
                if (b == -1) throw new IOException("Server closed connection during handshake.");
                headerBuf.Add((byte)b);
                if ((matchState == 0 || matchState == 2) && b == '\r') matchState++;
                else if ((matchState == 1 || matchState == 3) && b == '\n') matchState++;
                else matchState = 0;

                if (matchState == 4) break;
                if (headerBuf.Count > 16384) throw new IOException("Handshake header too large.");
            }

            string respHeader = Encoding.ASCII.GetString(headerBuf.ToArray());
            if (!respHeader.Contains("101"))
            {
                throw new IOException("WebSocket upgrade failed: " + respHeader.Split('\r')[0]);
            }

            Thread readerThread = new Thread(ReadLoop) { IsBackground = true };
            readerThread.Start();
        }

        public void SendFrame(Dictionary<string, object> headerObj, byte[] payload = null)
        {
            if (closed || netStream == null) return;
            byte[] dataBuf = payload ?? new byte[0];
            headerObj["len"] = dataBuf.Length;
            string headerJson = json.Serialize(headerObj);
            byte[] headerBytes = Encoding.UTF8.GetBytes(headerJson);

            int innerLen = 4 + headerBytes.Length + dataBuf.Length;
            byte[] inner = new byte[innerLen];
            inner[0] = (byte)((headerBytes.Length >> 24) & 0xFF);
            inner[1] = (byte)((headerBytes.Length >> 16) & 0xFF);
            inner[2] = (byte)((headerBytes.Length >> 8) & 0xFF);
            inner[3] = (byte)(headerBytes.Length & 0xFF);
            Buffer.BlockCopy(headerBytes, 0, inner, 4, headerBytes.Length);
            if (dataBuf.Length > 0)
            {
                Buffer.BlockCopy(dataBuf, 0, inner, 4 + headerBytes.Length, dataBuf.Length);
            }

            // Encode RFC 6455 Client Binary Frame (FIN+Binary = 0x82, Masked with 0x00000000)
            byte[] wsFrame;
            if (innerLen < 126)
            {
                wsFrame = new byte[6 + innerLen];
                wsFrame[0] = 0x82;
                wsFrame[1] = (byte)(0x80 | innerLen);
                Buffer.BlockCopy(inner, 0, wsFrame, 6, innerLen);
            }
            else if (innerLen < 65536)
            {
                wsFrame = new byte[8 + innerLen];
                wsFrame[0] = 0x82;
                wsFrame[1] = 0x80 | 126;
                wsFrame[2] = (byte)((innerLen >> 8) & 0xFF);
                wsFrame[3] = (byte)(innerLen & 0xFF);
                Buffer.BlockCopy(inner, 0, wsFrame, 8, innerLen);
            }
            else
            {
                wsFrame = new byte[14 + innerLen];
                wsFrame[0] = 0x82;
                wsFrame[1] = 0x80 | 127;
                long l = innerLen;
                for (int i = 0; i < 8; i++)
                {
                    wsFrame[9 - i] = (byte)(l & 0xFF);
                    l >>= 8;
                }
                Buffer.BlockCopy(inner, 0, wsFrame, 14, innerLen);
            }

            lock (writeLock)
            {
                if (closed) return;
                try
                {
                    netStream.Write(wsFrame, 0, wsFrame.Length);
                    netStream.Flush();
                }
                catch
                {
                    Close("Write error");
                }
            }
        }

        private byte[] ReadExact(int count)
        {
            byte[] buf = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = netStream.Read(buf, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
            return buf;
        }

        private void ReadLoop()
        {
            MemoryStream innerStream = new MemoryStream();
            try
            {
                while (!closed)
                {
                    byte[] h2 = ReadExact(2);
                    int opcode = h2[0] & 0x0F;
                    bool isMasked = (h2[1] & 0x80) != 0;
                    long payloadLen = h2[1] & 0x7F;

                    if (payloadLen == 126)
                    {
                        byte[] ext = ReadExact(2);
                        payloadLen = (ext[0] << 8) | ext[1];
                    }
                    else if (payloadLen == 127)
                    {
                        byte[] ext = ReadExact(8);
                        payloadLen = 0;
                        for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | ext[i];
                    }

                    byte[] maskKey = isMasked ? ReadExact(4) : null;
                    byte[] frameData = payloadLen > 0 ? ReadExact((int)payloadLen) : new byte[0];

                    if (isMasked && maskKey != null)
                    {
                        for (int i = 0; i < frameData.Length; i++)
                            frameData[i] ^= maskKey[i & 3];
                    }

                    if (opcode == 0x08) // Close
                    {
                        Close("Server closed WebSocket");
                        return;
                    }
                    else if (opcode == 0x01 || opcode == 0x02 || opcode == 0x00)
                    {
                        long oldPos = innerStream.Position;
                        innerStream.Seek(0, SeekOrigin.End);
                        innerStream.Write(frameData, 0, frameData.Length);
                        innerStream.Position = oldPos;

                        // Parse inner frames
                        while (innerStream.Length - innerStream.Position >= 4)
                        {
                            long startPos = innerStream.Position;
                            byte[] lenBytes = new byte[4];
                            innerStream.Read(lenBytes, 0, 4);
                            int headerLen = (lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3];

                            if (innerStream.Length - innerStream.Position < headerLen)
                            {
                                innerStream.Position = startPos;
                                break;
                            }

                            byte[] headerBytes = new byte[headerLen];
                            innerStream.Read(headerBytes, 0, headerLen);
                            string headerStr = Encoding.UTF8.GetString(headerBytes);
                            var headerObj = json.Deserialize<Dictionary<string, object>>(headerStr);
                            int pLen = headerObj.ContainsKey("len") ? Convert.ToInt32(headerObj["len"]) : 0;

                            if (innerStream.Length - innerStream.Position < pLen)
                            {
                                innerStream.Position = startPos;
                                break;
                            }

                            byte[] payload = new byte[pLen];
                            if (pLen > 0) innerStream.Read(payload, 0, pLen);

                            if (OnFrame != null) OnFrame(headerObj, payload);
                        }

                        if (innerStream.Position == innerStream.Length)
                        {
                            innerStream.SetLength(0);
                            innerStream.Position = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Close(ex.Message);
            }
        }

        public void Close(string reason = "Closed")
        {
            if (closed) return;
            closed = true;
            try { if (netStream != null) netStream.Close(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
            if (OnDisconnected != null) OnDisconnected(reason);
        }

        public void Dispose()
        {
            Close("Disposed");
        }
    }

    public class MainForm : Form
    {
        private const string DEFAULT_SERVER_URL = "https://amrita-campus-vpn.onrender.com";
        private string serverUrl = DEFAULT_SERVER_URL;
        private readonly JavaScriptSerializer jsonSerializer = new JavaScriptSerializer();

        // UI Controls - Header
        private Label lblCloudStatus;
        private TabControl tabControl;

        // Hosteller Tab Controls
        private ListView lvRequests;
        private Button btnApproveSelected;
        private Button btnRefreshReqs;
        private TextBox txtManualCode;
        private Button btnApproveByCode;
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

        // Pure C# Hosteller Relay Runtime State
        private WsTunnelClient hostWs = null;
        private ConcurrentDictionary<int, TcpClient> hostTargetSockets = new ConcurrentDictionary<int, TcpClient>();
        private volatile bool isHostRunning = false;
        private int hostRemainingSeconds = 0;
        private string activeHostSessionCode = "";

        // Pure C# Day Scholar Local Proxy Bridge Runtime State
        private TcpListener scholarProxyListener = null;
        private WsTunnelClient scholarWs = null;
        private ConcurrentDictionary<int, Action<string, int, string>> scholarConnectCallbacks = new ConcurrentDictionary<int, Action<string, int, string>>();
        private ConcurrentDictionary<int, Stream> scholarBrowserStreams = new ConcurrentDictionary<int, Stream>();
        private volatile bool isBridgeRunning = false;
        private int nextScholarStreamId = 1;

        private System.Windows.Forms.Timer countdownTimer;
        private System.Windows.Forms.Timer pollTimer;
        private System.Windows.Forms.Timer wsPingTimer;

        public MainForm()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.DefaultConnectionLimit = 50;

            InitializeGui();

            countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            countdownTimer.Tick += CountdownTimer_Tick;
            countdownTimer.Start();

            pollTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            pollTimer.Tick += (s, e) => PollServerStatusAsync();
            pollTimer.Start();

            wsPingTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            wsPingTimer.Tick += (s, e) =>
            {
                if (isHostRunning && hostWs != null)
                {
                    var ping = new Dictionary<string, object> { { "type", "PING" } };
                    ThreadPool.QueueUserWorkItem(_ => hostWs.SendFrame(ping));
                }
                if (isBridgeRunning && scholarWs != null)
                {
                    var ping = new Dictionary<string, object> { { "type", "PING" } };
                    ThreadPool.QueueUserWorkItem(_ => scholarWs.SendFrame(ping));
                }
            };
            wsPingTimer.Start();

            this.Shown += (s, e) => PollServerStatusAsync();
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeGui()
        {
            this.Text = "Amrita Campus Bridge v2.0 — Standalone Desktop App (Hosteller & Day Scholar)";
            this.Size = new Size(900, 625);
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
                Text = "🎓 Amrita Campus Bridge v2.0",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                Location = new Point(18, 8),
                Size = new Size(420, 26)
            };
            topBar.Controls.Add(lblTitle);

            Label lblSub = new Label
            {
                Text = "100% Native C# Engine • Approve Requests Inside App • Auto-Stop on Timer • Zero Antivirus Flags",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(20, 35),
                Size = new Size(600, 20)
            };
            topBar.Controls.Add(lblSub);

            lblCloudStatus = new Label
            {
                Text = "🔄 Connecting to Cloud...",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(620, 18),
                Size = new Size(250, 24)
            };
            topBar.Controls.Add(lblCloudStatus);

            tabControl = new TabControl
            {
                Location = new Point(14, 72),
                Size = new Size(860, 504),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            this.Controls.Add(tabControl);

            TabPage tabHosteller = new TabPage("🏠 Hosteller Mode (Share College Wi-Fi)")
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(248, 250, 252)
            };
            TabPage tabScholar = new TabPage("🎒 Day Scholar Mode (Request & Connect)")
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
                Size = new Size(420, 446),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(leftPanel);

            Label lblReqHeader = new Label
            {
                Text = "1. Live Day Scholar Requests",
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
                Location = new Point(318, 10),
                Size = new Size(88, 26)
            };
            btnRefreshReqs.Click += (s, e) => PollServerStatusAsync();
            leftPanel.Controls.Add(btnRefreshReqs);

            lvRequests = new ListView
            {
                Location = new Point(14, 42),
                Size = new Size(392, 160),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                BackColor = Color.FromArgb(11, 15, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            lvRequests.Columns.Add("Student Name", 120);
            lvRequests.Columns.Add("Time", 52);
            lvRequests.Columns.Add("Code", 82);
            lvRequests.Columns.Add("Purpose", 130);
            leftPanel.Controls.Add(lvRequests);

            btnApproveSelected = new Button
            {
                Text = "✅ Approve & Start Server for Selected Request",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.FromArgb(5, 46, 22),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(14, 208),
                Size = new Size(392, 36)
            };
            btnApproveSelected.Click += BtnApproveSelected_Click;
            leftPanel.Controls.Add(btnApproveSelected);

            // Quick Approve by Session Code
            Label lblByCode = new Label
            {
                Text = "Or Enter Friend's Session Code Directly:",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(203, 213, 225),
                Location = new Point(14, 254),
                Size = new Size(260, 18)
            };
            leftPanel.Controls.Add(lblByCode);

            txtManualCode = new TextBox
            {
                Text = "",
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(56, 189, 248),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                Location = new Point(14, 274),
                Size = new Size(210, 26)
            };
            leftPanel.Controls.Add(txtManualCode);

            btnApproveByCode = new Button
            {
                Text = "🔗 Host This Code",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(234, 273),
                Size = new Size(172, 28)
            };
            btnApproveByCode.Click += BtnApproveByCode_Click;
            leftPanel.Controls.Add(btnApproveByCode);

            // Instant Relay Section
            Label lblInstantHeader = new Label
            {
                Text = "Or Start an Instant Open Relay Server:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(14, 314),
                Size = new Size(350, 18)
            };
            leftPanel.Controls.Add(lblInstantHeader);

            Label lblHName = new Label
            {
                Text = "Your Name / Hostel:",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(14, 336),
                Size = new Size(190, 16)
            };
            leftPanel.Controls.Add(lblHName);

            txtHostName = new TextBox
            {
                Text = "Campus-Hosteller",
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(14, 354),
                Size = new Size(210, 26)
            };
            leftPanel.Controls.Add(txtHostName);

            Label lblHDur = new Label
            {
                Text = "Auto-Stop Timer:",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(236, 336),
                Size = new Size(150, 16)
            };
            leftPanel.Controls.Add(lblHDur);

            cmbHostDuration = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f),
                Location = new Point(236, 354),
                Size = new Size(170, 26)
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
                Location = new Point(14, 392),
                Size = new Size(392, 36)
            };
            btnStartInstantHost.Click += BtnStartInstantHost_Click;
            leftPanel.Controls.Add(btnStartInstantHost);

            // Right Panel: Server Status & Timer
            Panel rightPanel = new Panel
            {
                Location = new Point(446, 14),
                Size = new Size(392, 446),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(rightPanel);

            lblHostServerState = new Label
            {
                Text = "⚪ SERVER IDLE (Not Sharing)",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 14),
                Size = new Size(360, 24)
            };
            rightPanel.Controls.Add(lblHostServerState);

            lblHostSessionCode = new Label
            {
                Text = "Session Code: —",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 40),
                Size = new Size(360, 20)
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
                Size = new Size(355, 22)
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
                Size = new Size(360, 40)
            };
            btnStopHostServer.Click += (s, e) => StopHostellerServer("Manually stopped by Hosteller");
            rightPanel.Controls.Add(btnStopHostServer);

            Label lblLogTitle = new Label
            {
                Text = "🔒 Live Encrypted Activity Log (*.amrita.edu only):",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 196),
                Size = new Size(360, 18)
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
                Size = new Size(360, 212)
            };
            rightPanel.Controls.Add(txtHostLog);
            AppendHostLog("Ready. Connect to Amrita College Wi-Fi, then select a Day Scholar request and click Approve!");
        }

        private void BuildScholarTab(TabPage page)
        {
            Panel leftPanel = new Panel
            {
                Location = new Point(14, 14),
                Size = new Size(406, 446),
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
                Size = new Size(372, 28)
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
                Size = new Size(372, 28)
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
                Size = new Size(372, 20)
            };
            leftPanel.Controls.Add(lblSDur);

            cmbScholarDuration = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
                Location = new Point(16, 206),
                Size = new Size(372, 28)
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
                Size = new Size(372, 42)
            };
            btnSendRequest.Click += BtnSendRequest_Click;
            leftPanel.Controls.Add(btnSendRequest);

            lblScholarReqFeedback = new Label
            {
                Text = "Your login ID & password remain 100% encrypted via HTTPS.",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 243, 208),
                Location = new Point(16, 306),
                Size = new Size(372, 115)
            };
            leftPanel.Controls.Add(lblScholarReqFeedback);

            Panel rightPanel = new Panel
            {
                Location = new Point(432, 14),
                Size = new Size(406, 446),
                BackColor = Color.FromArgb(23, 33, 53)
            };
            page.Controls.Add(rightPanel);

            Label lblConnTitle = new Label
            {
                Text = "2. Connect Your Browser to Campus Relay",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(16, 14),
                Size = new Size(370, 24)
            };
            rightPanel.Controls.Add(lblConnTitle);

            lblActiveHostStatus = new Label
            {
                Text = "⏳ Checking if a Hosteller is currently online...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(252, 211, 77),
                Location = new Point(16, 48),
                Size = new Size(370, 42)
            };
            rightPanel.Controls.Add(lblActiveHostStatus);

            lblScholarBridgeState = new Label
            {
                Text = "Local Extension Bridge (127.0.0.1:8080): OFF",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(16, 96),
                Size = new Size(370, 22)
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
                Size = new Size(370, 44)
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
                Size = new Size(370, 244)
            };
            rightPanel.Controls.Add(txtScholarLog);
            AppendScholarLog("Click 'Send Request to Hostellers' or 'START DAY SCHOLAR VPN BRIDGE', then turn ON the Chrome Extension.");
        }

        private void AppendHostLog(string msg)
        {
            if (this.IsDisposed) return;
            if (txtHostLog.InvokeRequired)
            {
                try { txtHostLog.BeginInvoke(new Action<string>(AppendHostLog), msg); } catch { }
                return;
            }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            txtHostLog.AppendText("[" + ts + "] " + msg + Environment.NewLine);
        }

        private void AppendScholarLog(string msg)
        {
            if (this.IsDisposed) return;
            if (txtScholarLog.InvokeRequired)
            {
                try { txtScholarLog.BeginInvoke(new Action<string>(AppendScholarLog), msg); } catch { }
                return;
            }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            txtScholarLog.AppendText("[" + ts + "] " + msg + Environment.NewLine);
        }

        private static bool IsDomainAllowed(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string h = host.ToLowerInvariant().Trim();
            if (h == "amrita.edu" || h.EndsWith(".amrita.edu")) return true;
            if (h == "amritavidyalayam.org" || h.EndsWith(".amritavidyalayam.org")) return true;
            if (h == "localhost" || h == "127.0.0.1") return true;
            return false;
        }

        private int ParseDurationChoice(ComboBox cmb)
        {
            string txt = cmb.SelectedItem != null ? cmb.SelectedItem.ToString() : "30";
            if (txt.StartsWith("15")) return 15;
            if (txt.StartsWith("60")) return 60;
            return 30;
        }

        // ====================================================================
        // HOSTELLER PURE C# RELAY SERVER ENGINE
        // ====================================================================
        private void BtnApproveSelected_Click(object sender, EventArgs e)
        {
            if (lvRequests.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please click on a Day Scholar request in the table first, or enter a Session Code below.", "Select a Request", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RequestItem req = (RequestItem)lvRequests.SelectedItems[0].Tag;
            StartHostellerServer(req.Id, req.SessionCode, req.DurationMinutes, req.ScholarName);
        }

        private void BtnApproveByCode_Click(object sender, EventArgs e)
        {
            string code = txtManualCode.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                MessageBox.Show("Enter a Session Code (e.g. AMR-4F9A) first.", "Enter Code", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int mins = ParseDurationChoice(cmbHostDuration);
            StartHostellerServer("", code, mins, "Session " + code);
        }

        private void BtnStartInstantHost_Click(object sender, EventArgs e)
        {
            int mins = ParseDurationChoice(cmbHostDuration);
            StartHostellerServer("", "", mins, "Any Day Scholar");
        }

        private void StartHostellerServer(string requestId, string sessionCode, int durationMins, string scholarName)
        {
            StopHostellerServer(null);

            string hostName = string.IsNullOrWhiteSpace(txtHostName.Text) ? "Campus-Hosteller" : txtHostName.Text.Trim();
            string path = "/tunnel?hostellerName=" + Uri.EscapeDataString(hostName) +
                          "&duration=" + durationMins +
                          (!string.IsNullOrEmpty(sessionCode) ? "&sessionCode=" + Uri.EscapeDataString(sessionCode) : "") +
                          (!string.IsNullOrEmpty(requestId) ? "&requestId=" + Uri.EscapeDataString(requestId) : "");

            lblHostServerState.Text = "🔄 Connecting to Cloud Tunnel...";
            lblHostServerState.ForeColor = Color.FromArgb(252, 211, 77);
            btnStopHostServer.Enabled = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    WsTunnelClient ws = new WsTunnelClient();
                    ws.OnFrame += (header, payload) => HandleHostellerFrame(ws, header, payload);
                    ws.OnDisconnected += (reason) =>
                    {
                        if (isHostRunning)
                        {
                            this.BeginInvoke(new Action(() => StopHostellerServer("Cloud connection closed: " + reason)));
                        }
                    };

                    ws.Connect(serverUrl, path);
                    hostWs = ws;
                    isHostRunning = true;
                    activeHostSessionCode = sessionCode;
                    hostRemainingSeconds = durationMins * 60;

                    this.BeginInvoke(new Action(() =>
                    {
                        lblHostServerState.Text = "🟢 SERVER LIVE (Sharing *.amrita.edu)";
                        lblHostServerState.ForeColor = Color.FromArgb(16, 185, 129);
                        lblHostSessionCode.Text = "Session Code: " + (string.IsNullOrEmpty(sessionCode) ? "Assigning..." : sessionCode) + "  (For: " + scholarName + ")";
                        AppendHostLog("✅ CAMPUS RELAY LIVE for " + durationMins + " mins!");
                        PollServerStatusAsync();
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        StopHostellerServer("Failed to connect: " + ex.Message);
                    }));
                }
            });
        }

        private void HandleHostellerFrame(WsTunnelClient ws, Dictionary<string, object> header, byte[] payload)
        {
            string type = header.ContainsKey("type") ? Convert.ToString(header["type"]) : "";
            if (type == "WELCOME")
            {
                string assigned = header.ContainsKey("sessionCode") ? Convert.ToString(header["sessionCode"]) : "";
                if (!string.IsNullOrEmpty(assigned))
                {
                    activeHostSessionCode = assigned;
                    this.BeginInvoke(new Action(() =>
                    {
                        lblHostSessionCode.Text = "Session Code: " + assigned;
                        AppendHostLog("🎫 Confirmed Session Code: [ " + assigned + " ]");
                    }));
                }
            }
            else if (type == "SESSION_END")
            {
                string reason = header.ContainsKey("reason") ? Convert.ToString(header["reason"]) : "Ended";
                this.BeginInvoke(new Action(() => StopHostellerServer("Session ended: " + reason)));
            }
            else if (type == "CONNECT")
            {
                int streamId = Convert.ToInt32(header["streamId"]);
                string targetHost = Convert.ToString(header["host"]);
                int targetPort = Convert.ToInt32(header["port"]);
                bool encrypted = header.ContainsKey("encrypted") && Convert.ToBoolean(header["encrypted"]);

                if (!IsDomainAllowed(targetHost))
                {
                    AppendHostLog("🚫 Blocked non-college domain: " + targetHost);
                    ws.SendFrame(new Dictionary<string, object> {
                        { "type", "CONNECT_ERR" },
                        { "streamId", streamId },
                        { "error", "Blocked non-college domain: " + targetHost }
                    });
                    return;
                }

                AppendHostLog((encrypted ? "🔒 HTTPS Tunnel -> " : "🌐 HTTP -> ") + targetHost + ":" + targetPort);

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        TcpClient target = new TcpClient();
                        target.NoDelay = true;
                        target.Connect(targetHost, targetPort);
                        hostTargetSockets[streamId] = target;

                        ws.SendFrame(new Dictionary<string, object> {
                            { "type", "CONNECT_OK" },
                            { "streamId", streamId }
                        });

                        NetworkStream ts = target.GetStream();
                        byte[] buf = new byte[16384];
                        while (isHostRunning && target.Connected)
                        {
                            int n = ts.Read(buf, 0, buf.Length);
                            if (n <= 0) break;
                            byte[] chunk = new byte[n];
                            Buffer.BlockCopy(buf, 0, chunk, 0, n);
                            ws.SendFrame(new Dictionary<string, object> {
                                { "type", "DATA" },
                                { "streamId", streamId }
                            }, chunk);
                        }
                    }
                    catch (Exception ex)
                    {
                        ws.SendFrame(new Dictionary<string, object> {
                            { "type", "CONNECT_ERR" },
                            { "streamId", streamId },
                            { "error", ex.Message }
                        });
                    }
                    finally
                    {
                        TcpClient removed;
                        if (hostTargetSockets.TryRemove(streamId, out removed))
                        {
                            try { removed.Close(); } catch { }
                        }
                        ws.SendFrame(new Dictionary<string, object> {
                            { "type", "END" },
                            { "streamId", streamId }
                        });
                    }
                });
            }
            else if (type == "DATA")
            {
                int streamId = Convert.ToInt32(header["streamId"]);
                TcpClient target;
                if (hostTargetSockets.TryGetValue(streamId, out target) && payload != null && payload.Length > 0)
                {
                    try
                    {
                        NetworkStream ts = target.GetStream();
                        ts.Write(payload, 0, payload.Length);
                        ts.Flush();
                    }
                    catch { }
                }
            }
            else if (type == "END")
            {
                int streamId = Convert.ToInt32(header["streamId"]);
                TcpClient target;
                if (hostTargetSockets.TryRemove(streamId, out target))
                {
                    try { target.Close(); } catch { }
                }
            }
        }

        private void StopHostellerServer(string reason)
        {
            isHostRunning = false;
            hostRemainingSeconds = 0;

            if (hostWs != null)
            {
                var wsToClose = hostWs;
                hostWs = null;
                wsToClose.Close("Stopped");
            }

            foreach (var kv in hostTargetSockets)
            {
                try { kv.Value.Close(); } catch { }
            }
            hostTargetSockets.Clear();

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

        // ====================================================================
        // DAY SCHOLAR REQUEST & PURE C# LOCAL PROXY BRIDGE (127.0.0.1:8080)
        // ====================================================================
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

                    var payloadObj = new Dictionary<string, object> {
                        { "scholarName", sName },
                        { "purpose", purpose },
                        { "durationMinutes", duration }
                    };
                    byte[] body = Encoding.UTF8.GetBytes(jsonSerializer.Serialize(payloadObj));
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

                    var respDict = jsonSerializer.Deserialize<Dictionary<string, object>>(respText);
                    string code = "Created";
                    if (respDict != null && respDict.ContainsKey("request"))
                    {
                        var rObj = respDict["request"] as Dictionary<string, object>;
                        if (rObj != null && rObj.ContainsKey("sessionCode"))
                            code = Convert.ToString(rObj["sessionCode"]);
                    }

                    this.BeginInvoke(new Action(() =>
                    {
                        btnSendRequest.Enabled = true;
                        lblScholarReqFeedback.Text = "✅ Request Sent! Session Code: " + code + Environment.NewLine +
                                                     "Ask your Hosteller friend to click 'Approve & Start Server' in their app!";
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
                try { if (scholarProxyListener != null) scholarProxyListener.Stop(); } catch { }
                scholarProxyListener = null;
                try { if (scholarWs != null) scholarWs.Close("Bridge stopped"); } catch { }
                scholarWs = null;

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
                scholarProxyListener = new TcpListener(IPAddress.Loopback, 8080);
                scholarProxyListener.Start();
                isBridgeRunning = true;

                lblScholarBridgeState.Text = "🟢 Local Extension Bridge LIVE (127.0.0.1:8080)";
                lblScholarBridgeState.ForeColor = Color.FromArgb(16, 185, 129);
                btnToggleScholarBridge.Text = "🛑 STOP DAY SCHOLAR VPN BRIDGE";
                btnToggleScholarBridge.BackColor = Color.FromArgb(220, 38, 38);
                btnToggleScholarBridge.ForeColor = Color.White;

                ThreadPool.QueueUserWorkItem(_ => ConnectScholarCloudWs());
                ThreadPool.QueueUserWorkItem(_ => AcceptBrowserProxyClients());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not bind port 8080 (another proxy bridge may already be running): " + ex.Message);
            }
        }

        private void ConnectScholarCloudWs()
        {
            while (isBridgeRunning)
            {
                try
                {
                    WsTunnelClient ws = new WsTunnelClient();
                    ws.OnFrame += (header, payload) =>
                    {
                        string type = header.ContainsKey("type") ? Convert.ToString(header["type"]) : "";
                        int streamId = header.ContainsKey("streamId") ? Convert.ToInt32(header["streamId"]) : 0;

                        if (type == "CONNECT_OK" || type == "CONNECT_ERR")
                        {
                            Action<string, int, string> cb;
                            if (scholarConnectCallbacks.TryRemove(streamId, out cb))
                            {
                                int st = header.ContainsKey("status") ? Convert.ToInt32(header["status"]) : 502;
                                string err = header.ContainsKey("error") ? Convert.ToString(header["error"]) : "Failed";
                                cb(type, st, err);
                            }
                        }
                        else if (type == "DATA")
                        {
                            Stream bStream;
                            if (scholarBrowserStreams.TryGetValue(streamId, out bStream) && payload != null && payload.Length > 0)
                            {
                                try
                                {
                                    bStream.Write(payload, 0, payload.Length);
                                    bStream.Flush();
                                }
                                catch { }
                            }
                        }
                        else if (type == "END")
                        {
                            Stream bStream;
                            if (scholarBrowserStreams.TryRemove(streamId, out bStream))
                            {
                                try { bStream.Close(); } catch { }
                            }
                        }
                    };

                    ws.Connect(serverUrl, "/scholar");
                    scholarWs = ws;
                    AppendScholarLog("✅ Connected 127.0.0.1:8080 bridge to Render Cloud!");
                    break;
                }
                catch (Exception ex)
                {
                    AppendScholarLog("Waiting for cloud server: " + ex.Message);
                    Thread.Sleep(3000);
                }
            }
        }

        private void AcceptBrowserProxyClients()
        {
            while (isBridgeRunning && scholarProxyListener != null)
            {
                try
                {
                    TcpClient client = scholarProxyListener.AcceptTcpClient();
                    client.NoDelay = true;
                    ThreadPool.QueueUserWorkItem(_ => HandleBrowserProxyClient(client));
                }
                catch
                {
                    if (!isBridgeRunning) break;
                }
            }
        }

        private void HandleBrowserProxyClient(TcpClient client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                List<byte> reqHead = new List<byte>();
                int state = 0;
                while (true)
                {
                    int b = stream.ReadByte();
                    if (b == -1) { client.Close(); return; }
                    reqHead.Add((byte)b);
                    if ((state == 0 || state == 2) && b == '\r') state++;
                    else if ((state == 1 || state == 3) && b == '\n') state++;
                    else state = 0;
                    if (state == 4) break;
                    if (reqHead.Count > 32768) { client.Close(); return; }
                }

                string headerStr = Encoding.ASCII.GetString(reqHead.ToArray());
                string firstLine = headerStr.Split('\r')[0];
                string[] parts = firstLine.Split(' ');
                if (parts.Length < 2) { client.Close(); return; }

                string method = parts[0].ToUpperInvariant();
                string target = parts[1];

                if (scholarWs == null)
                {
                    byte[] err = Encoding.ASCII.GetBytes("HTTP/1.1 503 Service Unavailable\r\n\r\nCloud tunnel connecting...\r\n");
                    stream.Write(err, 0, err.Length);
                    client.Close();
                    return;
                }

                int streamId = Interlocked.Increment(ref nextScholarStreamId);
                scholarBrowserStreams[streamId] = stream;

                if (method == "CONNECT")
                {
                    string[] hp = target.Split(':');
                    string host = hp[0];
                    int port = hp.Length > 1 ? int.Parse(hp[1]) : 443;

                    ManualResetEventSlim connectedEvt = new ManualResetEventSlim(false);
                    bool ok = false;
                    int errStatus = 502;
                    string errMsg = "";

                    scholarConnectCallbacks[streamId] = (resType, st, msg) =>
                    {
                        ok = (resType == "CONNECT_OK");
                        errStatus = st;
                        errMsg = msg;
                        connectedEvt.Set();
                    };

                    scholarWs.SendFrame(new Dictionary<string, object> {
                        { "type", "CONNECT" },
                        { "streamId", streamId },
                        { "host", host },
                        { "port", port },
                        { "encrypted", true }
                    });

                    connectedEvt.Wait(10000);
                    if (!ok)
                    {
                        byte[] resp = Encoding.ASCII.GetBytes("HTTP/1.1 " + errStatus + " Error\r\n\r\n" + errMsg + "\r\n");
                        stream.Write(resp, 0, resp.Length);
                        client.Close();
                        return;
                    }

                    AppendScholarLog("🔒 Tunneling https://" + host);
                    byte[] okResp = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                    stream.Write(okResp, 0, okResp.Length);
                    stream.Flush();

                    byte[] buf = new byte[16384];
                    while (isBridgeRunning && client.Connected)
                    {
                        int n = stream.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        byte[] chunk = new byte[n];
                        Buffer.BlockCopy(buf, 0, chunk, 0, n);
                        scholarWs.SendFrame(new Dictionary<string, object> {
                            { "type", "DATA" },
                            { "streamId", streamId }
                        }, chunk);
                    }
                }
            }
            catch { }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        // ====================================================================
        // PROPER JSON POLLING USING .NET JavaScriptSerializer (Fixes Request List!)
        // ====================================================================
        private void PollServerStatusAsync()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(serverUrl + "/api/status?t=" + DateTime.UtcNow.Ticks);
                    req.Method = "GET";
                    req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                    req.Timeout = 4500;

                    string json;
                    using (WebResponse res = req.GetResponse())
                    using (StreamReader sr = new StreamReader(res.GetResponseStream()))
                    {
                        json = sr.ReadToEnd();
                    }

                    var root = jsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    if (root == null) return;

                    bool hasHost = root.ContainsKey("hasActiveHosteller") && Convert.ToBoolean(root["hasActiveHosteller"]);
                    string activeCode = "";
                    string activeHostName = "";
                    int remSecs = 0;

                    if (root.ContainsKey("activeSession") && root["activeSession"] is Dictionary<string, object>)
                    {
                        var act = (Dictionary<string, object>)root["activeSession"];
                        if (act.ContainsKey("sessionCode")) activeCode = Convert.ToString(act["sessionCode"]);
                        if (act.ContainsKey("hostellerName")) activeHostName = Convert.ToString(act["hostellerName"]);
                        if (act.ContainsKey("remainingSeconds")) remSecs = Convert.ToInt32(act["remainingSeconds"]);
                    }

                    List<RequestItem> pending = new List<RequestItem>();
                    if (root.ContainsKey("requests") && root["requests"] is IEnumerable)
                    {
                        foreach (object itemObj in (IEnumerable)root["requests"])
                        {
                            var rDict = itemObj as Dictionary<string, object>;
                            if (rDict == null) continue;
                            string st = rDict.ContainsKey("status") ? Convert.ToString(rDict["status"]) : "";
                            if (st == "pending")
                            {
                                pending.Add(new RequestItem
                                {
                                    Id = rDict.ContainsKey("id") ? Convert.ToString(rDict["id"]) : "",
                                    ScholarName = rDict.ContainsKey("scholarName") ? Convert.ToString(rDict["scholarName"]) : "Day Scholar",
                                    Purpose = rDict.ContainsKey("purpose") ? Convert.ToString(rDict["purpose"]) : "Campus Access",
                                    DurationMinutes = rDict.ContainsKey("durationMinutes") ? Convert.ToInt32(rDict["durationMinutes"]) : 30,
                                    Status = st,
                                    SessionCode = rDict.ContainsKey("sessionCode") ? Convert.ToString(rDict["sessionCode"]) : ""
                                });
                            }
                        }
                    }

                    if (!this.IsHandleCreated || this.IsDisposed) return;

                    this.BeginInvoke(new Action(() =>
                    {
                        lblCloudStatus.Text = "🟢 Cloud Online (" + pending.Count + " pending reqs)";
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
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            lblCloudStatus.Text = "⚠️ Waking up Render Cloud...";
                            lblCloudStatus.ForeColor = Color.FromArgb(245, 158, 11);
                        }));
                    }
                }
            });
        }

        private void UpdateRequestsListView(List<RequestItem> pending)
        {
            string selectedId = lvRequests.SelectedItems.Count > 0
                ? ((RequestItem)lvRequests.SelectedItems[0].Tag).Id
                : null;

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
            wsPingTimer.Stop();
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
