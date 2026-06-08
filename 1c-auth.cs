using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0")]
[assembly: System.Reflection.AssemblyTitle("1C Authorizator")]
[assembly: System.Reflection.AssemblyProduct("1C Authorizator")]
[assembly: System.Reflection.AssemblyCopyright("Copyright © 2026")]

public class Program
{
    public const string Version = "1.0.0";
    public static LoginForm FormInstance;

    [STAThread]
    public static void Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Mutex for single instance check (Local namespace to avoid UAC permission issues)
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\1C_Authorizator_Auth_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                // Launch form
                LoginFormInstance.RunApp();
            }
        }
        catch (Exception ex)
        {
            try
            {
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string errPath = Path.Combine(exeDir, "1c-authorizator.log");
                string logLine = string.Format("{0} [CRITICAL STARTUP ERROR] {1}", 
                    DateTime.Now.ToString("yyyy'/'MM'/'dd HH:mm:ss"), ex.ToString());
                File.AppendAllText(errPath, logLine + Environment.NewLine, Encoding.UTF8);
            }
            catch {}

            MessageBox.Show("Ошибка запуска приложения:\n" + ex.Message, "1C Authorizator", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

}

public class LoginFormInstance
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private static LowLevelKeyboardProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;

    // Capture state
    private static bool _capturing = false;
    private static StringBuilder _capturedBuffer = new StringBuilder();
    private static DateTime _lastKeyPressTime = DateTime.MinValue;
    
    // Config
    private static Config _config;
    
    // Threads & Sync
    private static Queue<string> _scanQueue = new Queue<string>();
    private static readonly object _queueLock = new object();
    private static AutoResetEvent _queueSignal = new AutoResetEvent(false);

    // Win32 API Imports
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);


    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public static void RunApp()
    {
        LoadConfiguration();
        Log("Application started. Version: " + Program.Version);

        // Start background worker thread to process scans
        Thread worker = new Thread(ProcessScanQueueWorker);
        worker.IsBackground = true;
        worker.Start();

        // Set hook
        try
        {
            _hookID = SetHook(_proc);
            if (_hookID == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                Log(string.Format("ERROR: Failed to install keyboard hook. Win32 Error code: {0}", errorCode));
            }
            else
            {
                Log(string.Format("Keyboard hook installed successfully. HookID: 0x{0:X}", _hookID.ToInt64()));
            }
        }
        catch (Exception ex)
        {
            Log("Exception during keyboard hook installation: " + ex.Message);
        }

        // Run Forms UI
        Program.FormInstance = new LoginForm(_config);
        Application.Run(Program.FormInstance);

        // Unhook on exit
        if (_hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookID);
            Log("Keyboard hook uninstalled.");
        }
        Log("Application stopped.");
    }


    private static void LoadConfiguration()
    {
        try
        {
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string configPath = Path.Combine(exeDir, "config.yaml");
            // Walk up to 5 parent directories looking for config.yaml
            int maxLevels = 5;
            for (int i = 0; i < maxLevels && !File.Exists(configPath); i++)
            {
                exeDir = Path.GetFullPath(Path.Combine(exeDir, ".."));
                configPath = Path.Combine(exeDir, "config.yaml");
            }
            _config = Config.Load(configPath);
        // Ensure version is set; fallback to compiled constant if missing
        if (string.IsNullOrEmpty(_config.appVersion))
        {
            _config.appVersion = Program.Version;
            Log("Config missing appVersion; using default constant: " + Program.Version);
        }
        }
        catch (Exception ex)
        {
            _config = new Config();
            Log("Error loading config, using defaults: " + ex.Message);
        }
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        IntPtr hMod = GetModuleHandle(null);
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, hMod, 0);
    }



    private static bool IsShiftPressed()
    {
        return (GetAsyncKeyState(0x10) & 0x8000) != 0; // 0x10 is VK_SHIFT
    }


    private static char VkCodeToChar(uint vkCode, bool shift)
    {
        // Layout-independent mapping matching US keyboard layout (typical barcode scanner output)
        if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
        {
            return (char)vkCode; // standard uppercase
        }
        if (vkCode >= 0x30 && vkCode <= 0x39) // 0-9
        {
            return (char)vkCode;
        }
        if (vkCode == 0xBB) // = or +
        {
            return shift ? '+' : '=';
        }
        if (vkCode == 0xBA) // ; or :
        {
            return shift ? ':' : ';';
        }
        if (vkCode >= 0x60 && vkCode <= 0x69) // Numpad 0-9
        {
            return (char)('0' + (vkCode - 0x60));
        }
        return '\0';
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {

            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool shift = IsShiftPressed();
                char c = VkCodeToChar((uint)vkCode, shift);

                // Reset capture state if timeout elapsed (e.g. scan aborted or lost focus)
                if (_capturing)
                {
                    double msSinceLast = (DateTime.Now - _lastKeyPressTime).TotalMilliseconds;
                    if (_lastKeyPressTime != DateTime.MinValue && msSinceLast > _config.scannerTimeoutMs)
                    {
                        Log(string.Format("Capture timeout ({0:F1}ms > {1}ms). Resetting capture state.", 
                            msSinceLast, _config.scannerTimeoutMs));
                        _capturing = false;
                        _capturedBuffer.Length = 0;
                        Program.FormInstance.UpdateStatus("Ожидание сканирования...", "Info");
                    }
                }

                LogDebug(string.Format("Hook: KeyDown vk={0} (0x{0:X2}), char='{1}', shift={2}, capturing={3}", 
                    vkCode, c == '\0' ? '?' : c, shift, _capturing));

                if (!_capturing)
                {
                    // Check if the typing speed is fast (potential scanner input)
                    double msSinceLast = (DateTime.Now - _lastKeyPressTime).TotalMilliseconds;
                    if (_capturedBuffer.Length > 0 && msSinceLast > _config.scannerTimeoutMs)
                    {
                        LogDebug(string.Format("Timeout between prefix keys ({0:F1}ms > {1}ms). Resetting prefix buffer: '{2}'", 
                            msSinceLast, _config.scannerTimeoutMs, _capturedBuffer.ToString()));
                        _capturedBuffer.Length = 0;
                    }

                    _lastKeyPressTime = DateTime.Now;

                    if (c != '\0')
                    {
                        _capturedBuffer.Append(c);
                        string current = _capturedBuffer.ToString();
                        LogDebug(string.Format("Prefix buffer: '{0}'", current));

                        // If it fully matches the prefix, start capturing contents
                        if (current.Equals(_config.scannerPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            Log("Prefix matched! Starting capture.");
                            _capturing = true;
                            _capturedBuffer.Length = 0; // Clear buffer to store the actual data
                            Program.FormInstance.UpdateStatus("Считывание QR-кода...", "Info");
                            return (IntPtr)1; // Block the prefix ending key (e.g. ':')
                        }

                        // If it matches a partial prefix, do NOT block the key to prevent blocking normal typing
                        if (_config.scannerPrefix.StartsWith(current, StringComparison.OrdinalIgnoreCase))
                        {
                            LogDebug("Matches partial prefix. Passing key through.");
                        }
                        else
                        {
                            LogDebug("Does not match prefix. Resetting buffer.");
                            _capturedBuffer.Length = 0; // Doesn't match prefix, reset
                        }
                    }
                }
                else
                {
                    // Capturing mode active! Block all keys and append to buffer
                    _lastKeyPressTime = DateTime.Now;

                    if (vkCode == 13) // Enter key
                    {
                        Log("Enter pressed. Completing capture.");
                        FinishCapture();
                        return (IntPtr)1;
                    }
                    if (vkCode == 27) // Escape key (cancel)
                    {
                        _capturing = false;
                        _capturedBuffer.Length = 0;
                        Log("Capture cancelled by Escape key.");
                        Program.FormInstance.UpdateStatus("Ожидание сканирования...", "Info");
                        return (IntPtr)1;
                    }

                    if (c != '\0')
                    {
                        _capturedBuffer.Append(c);
                        LogDebug(string.Format("Captured char: '{0}', total length: {1}", c, _capturedBuffer.Length));
                        
                        if (_capturedBuffer.Length > 500) // Safety overflow check
                        {
                            _capturing = false;
                            _capturedBuffer.Length = 0;
                            Log("Capture cancelled: buffer overflow (>500 chars).");
                            Program.FormInstance.UpdateStatus("Ошибка: Превышен размер буфера", "Error");
                        }
                    }
                    return (IntPtr)1; // Consume keypress
                }
            }
        }
        catch (Exception ex)
        {
            Log("Error in HookCallback: " + ex.ToString());
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }


    private static void FinishCapture()
    {
        _capturing = false;
        string text = _capturedBuffer.ToString();
        _capturedBuffer.Length = 0;

        if (!string.IsNullOrEmpty(text))
        {
            lock (_queueLock)
            {
                _scanQueue.Enqueue(_config.scannerPrefix + text);
            }
            _queueSignal.Set();
        }
    }

    public static void ResetHookState()
    {
        _capturing = false;
        _capturedBuffer.Length = 0;
        LogDebug("Keyboard hook buffer and capture state reset.");
    }

    private static void ProcessScanQueueWorker()
    {
        while (true)
        {
            _queueSignal.WaitOne();
            string rawScan = null;

            lock (_queueLock)
            {
                if (_scanQueue.Count > 0)
                {
                    rawScan = _scanQueue.Dequeue();
                }
            }

            if (rawScan != null)
            {
                ProcessScan(rawScan);
            }
        }
    }

    public static void ProcessScan(string rawScan)

    {
        try
        {
            Log("[SCANNER]  " + rawScan);

            string base32Part = rawScan;
            if (base32Part.StartsWith(_config.scannerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                base32Part = base32Part.Substring(_config.scannerPrefix.Length);
            }

            LogDebug("Raw base32 part: " + base32Part);

            // Translate Shift-desynchronized characters back to Base32 digits and clean padding
            StringBuilder cleanedSb = new StringBuilder();
            foreach (char ch in base32Part)
            {
                char mapped = ch;
                switch (ch)
                {
                    case '"': mapped = '2'; break;
                    case '№': mapped = '3'; break;
                    case ';': mapped = '4'; break;
                    case '%': mapped = '5'; break;
                    case ':': mapped = '6'; break;
                    case '?': mapped = '7'; break;
                }
                
                // Skip '0' (duplicated padding) and '=' (Base32 padding)
                if (mapped != '0' && mapped != '=')
                {
                    cleanedSb.Append(mapped);
                }
            }
            string cleanedBase32 = cleanedSb.ToString();
            LogDebug("Cleaned base32 part: " + cleanedBase32);
            Log(cleanedBase32);

            // Decode Base32
            byte[] jsonDataBytes;
            try
            {
                jsonDataBytes = Base32Decode(cleanedBase32);
            }
            catch (Exception ex)
            {
                Log("Ошибка чтения QR-кода: Base32: " + ex.Message);
                Program.FormInstance.UpdateStatus("Ошибка чтения QR: некорректный код", "Error");
                return;
            }

            string jsonStr = Encoding.UTF8.GetString(jsonDataBytes);
            LogDebug("Decoded JSON string: " + jsonStr);
            Log("[VALID]  " + rawScan);

            // Parse User JSON
            User u;
            try
            {
                u = User.ParseJson(jsonStr);
                if (string.IsNullOrEmpty(u.login))
                {
                    throw new Exception("Login is empty");
                }
            }
            catch (Exception ex)
            {
                Log("Ошибка чтения QR-кода: JSON parse error: " + ex.Message);
                Program.FormInstance.UpdateStatus("Ошибка: Неверная структура данных", "Error");
                return;
            }

            LogDebug(string.Format("Parsed User: login='{0}', password='{1}', server='{2}', database='{3}', ibName='{4}'", 
                u.login, string.IsNullOrEmpty(u.password) ? "" : "***", u.server, u.database, u.infobase_name));

            Program.FormInstance.UpdateStatus("Вход выполнен! Запуск 1С...", "Success");

            // Sleep shortly to let user see success state
            Thread.Sleep(1200);

            // Hide Form (run on UI thread)
            Program.FormInstance.BeginInvoke(new Action(() => Program.FormInstance.Hide()));

            // Kill active 1C processes if required
            if (_config.restart1C)
            {
                Log("Restarting 1C (killing active 1cv8.exe)...");
                foreach (string name in new[] { "1cv8", "1cv8c" })
                {
                    foreach (var process in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            process.Kill();
                            LogDebug(string.Format("Killed process {0} (PID: {1})", name, process.Id));
                            process.WaitForExit(3000);
                        }
                        catch (Exception ex)
                        {
                            LogDebug("Error killing process: " + ex.Message);
                        }
                    }
                }
            }

            // Launch 1C with arguments
            string args = Build1CArgs(u, _config);
            Log("Launching 1C: " + _config.pathTo1c + " " + args);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = _config.pathTo1c;
            startInfo.Arguments = args;
            startInfo.WorkingDirectory = Path.GetDirectoryName(_config.pathTo1c);
            
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log("Ошибка: " + ex.Message);
            Program.FormInstance.UpdateStatus("Ошибка запуска: " + ex.Message, "Error");
        }
    }

    private static string Build1CArgs(User u, Config c)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("ENTERPRISE ");

        string server = !string.IsNullOrEmpty(u.server) ? u.server : c.defaultServer;
        string db = !string.IsNullOrEmpty(u.database) ? u.database : c.defaultDatabase;
        string ibName = u.infobase_name;

        if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(db))
        {
            sb.AppendFormat("/S \"{0}\\{1}\" ", server, db);
        }
        else if (!string.IsNullOrEmpty(db) && (db.Contains(":") || db.Contains("\\")))
        {
            sb.AppendFormat("/F \"{0}\" ", db);
        }
        else if (!string.IsNullOrEmpty(ibName))
        {
            sb.AppendFormat("/IBName \"{0}\" ", ibName);
        }

        sb.AppendFormat("/N \"{0}\" ", u.login);
        if (!string.IsNullOrEmpty(u.password))
        {
            sb.AppendFormat("/P \"{0}\" ", u.password);
        }

        return sb.ToString().Trim();
    }

    private static readonly object _logLock = new object();

    public static void Log(string message)

    {
        string logPath = _config != null ? _config.logFile : "1c-authorizator.log";
        string timestamp = DateTime.Now.ToString("yyyy'/'MM'/'dd HH:mm:ss");
        string logLine = string.Format("{0} {1}", timestamp, message);

        try
        {
            if (!Path.IsPathRooted(logPath))
            {
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                logPath = Path.Combine(exeDir, logPath);
            }

            string dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            lock (_logLock)
            {
                File.AppendAllText(logPath, logLine + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Fallback to local directory if configured path fails (e.g. permission issues)
            try
            {
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string fallbackPath = Path.Combine(exeDir, "1c-authorizator.log");
                lock (_logLock)
                {
                    File.AppendAllText(fallbackPath, "[FALLBACK] " + logLine + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch {}
        }
    }


    public static void LogDebug(string message)
    {
        if (_config != null && _config.debug)
        {
            try
            {
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string debugLogPath = Path.Combine(exeDir, "1c-auth-debug.log");
                
                string timestamp = DateTime.Now.ToString("yyyy'/'MM'/'dd HH:mm:ss");
                string logLine = string.Format("{0} [DEBUG] {1}", timestamp, message);

                lock (_logLock)
                {
                    File.AppendAllText(debugLogPath, logLine + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch {}
        }
    }

    private static byte[] Base32Decode(string input)
    {
        input = input.TrimEnd('=').ToUpperInvariant();
        if (string.IsNullOrEmpty(input)) return new byte[0];

        string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        int outputLength = input.Length * 5 / 8;
        byte[] output = new byte[outputLength];

        int buffer = 0;
        int bufferLength = 0;
        int outputIndex = 0;

        for (int i = 0; i < input.Length; i++)
        {
            int charValue = alphabet.IndexOf(input[i]);
            if (charValue < 0)
            {
                throw new Exception("Illegal Base32 character: " + input[i]);
            }

            buffer = (buffer << 5) | charValue;
            bufferLength += 5;

            if (bufferLength >= 8)
            {
                if (outputIndex < output.Length)
                {
                    output[outputIndex++] = (byte)((buffer >> (bufferLength - 8)) & 0xFF);
                }
                bufferLength -= 8;
            }
        }
        return output;
    }
}

public class LoginForm : Form
{
    private Config _config;
    private NotifyIcon _notifyIcon;
    private ContextMenu _trayMenu;

    // Form Controls
    private Label _titleLabel;
    private Label _subtitleLabel;
    private Panel _statusPanel;
    private Label _statusTextLabel;
    private Label _closeButton;
    private Label _minimizeButton;
    private TextBox _barcodeTextBox;


    // Theme state and colors
    private bool _isDarkTheme = true;
    private Color _colorBgTop;
    private Color _colorBgBottom;
    private Color _colorOuterBorder;
    private Color _colorTitleText;
    private Color _colorSubtitleText;
    private Color _colorButtonNormal;
    private Color _colorButtonHoverMin;
    private Color _colorButtonHoverClose;
    private Color _colorStatusPanelBg;
    private Color _colorStatusPanelBorder;
    private Color _colorStatusTextNormal;
    private Color _colorStatusTextSuccess;
    private Color _colorStatusTextError;
    private Color _colorScannerHudFrame;
    private Color _colorScannerHudCorner;
    private Color _colorLaser;
    private Color _colorLaserGlow;

    // Animations & Styling
    private int _pulseAlpha = 100;
    private int _pulseDirection = 3;
    private System.Windows.Forms.Timer _animationTimer;
    private System.Windows.Forms.Timer _shakeTimer;
    private System.Windows.Forms.Timer _typingTimer;
    private int _shakeCount = 0;
    private Point _originalLocation;
    private string _statusType = "Info"; // Info, Success, Error
    private bool _reallyClose = false;

    // Win32 API Imports for layout styling and dragging
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    private static extern IntPtr CreateRoundRectRgn(
        int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    protected override CreateParams CreateParams
    {
        get
        {
            // Enable drop shadow on the form
            const int CS_DROPSHADOW = 0x20000;
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    public LoginForm(Config config)
    {
        _config = config;

        // Determine active theme
        string t = (_config.theme ?? "auto").ToLower();
        if (t == "light")
        {
            _isDarkTheme = false;
        }
        else if (t == "dark")
        {
            _isDarkTheme = true;
        }
        else
        {
            _isDarkTheme = IsSystemDarkTheme();
        }

        // Initialize colors
        if (_isDarkTheme)
        {
            _colorBgTop = Color.FromArgb(24, 28, 48);
            _colorBgBottom = Color.FromArgb(15, 12, 30);
            _colorOuterBorder = Color.FromArgb(60, 60, 90);
            _colorTitleText = Color.White;
            _colorSubtitleText = Color.FromArgb(160, 165, 195);
            _colorButtonNormal = Color.FromArgb(120, 120, 150);
            _colorButtonHoverMin = Color.White;
            _colorButtonHoverClose = Color.FromArgb(239, 68, 68);
            _colorStatusPanelBg = Color.FromArgb(30, 32, 54);
            _colorStatusPanelBorder = Color.FromArgb(50, 52, 74);
            _colorStatusTextNormal = Color.FromArgb(200, 200, 220);
            _colorStatusTextSuccess = Color.FromArgb(16, 185, 129);
            _colorStatusTextError = Color.FromArgb(239, 68, 68);
            _colorScannerHudFrame = Color.FromArgb(50, 50, 80);
            _colorScannerHudCorner = Color.FromArgb(99, 102, 241);
            _colorLaser = Color.FromArgb(239, 68, 68);
            _colorLaserGlow = Color.FromArgb(40, 239, 68, 68);
        }
        else
        {
            _colorBgTop = Color.FromArgb(243, 244, 246);
            _colorBgBottom = Color.FromArgb(255, 255, 255);
            _colorOuterBorder = Color.FromArgb(209, 213, 219);
            _colorTitleText = Color.FromArgb(17, 24, 39);
            _colorSubtitleText = Color.FromArgb(75, 85, 99);
            _colorButtonNormal = Color.FromArgb(156, 163, 175);
            _colorButtonHoverMin = Color.FromArgb(17, 24, 39);
            _colorButtonHoverClose = Color.FromArgb(220, 38, 38);
            _colorStatusPanelBg = Color.FromArgb(243, 244, 246);
            _colorStatusPanelBorder = Color.FromArgb(229, 231, 235);
            _colorStatusTextNormal = Color.FromArgb(55, 65, 81);
            _colorStatusTextSuccess = Color.FromArgb(5, 150, 105);
            _colorStatusTextError = Color.FromArgb(220, 38, 38);
            _colorScannerHudFrame = Color.FromArgb(209, 213, 219);
            _colorScannerHudCorner = Color.FromArgb(79, 70, 229);
            _colorLaser = Color.FromArgb(220, 38, 38);
            _colorLaserGlow = Color.FromArgb(30, 220, 38, 38);
        }

        // Initialize Windows Form parameters
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Size = new Size(480, 280);
        this.ShowInTaskbar = true;
        this.TopMost = _config.topMost;

        // Set Form Icon
        try
        {
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string formIconPath = Path.Combine(exeDir, "across.ico");
            if (File.Exists(formIconPath))
            {
                this.Icon = new Icon(formIconPath);
            }
        }
        catch {}

        // Enable Double Buffering to resolve lag and flicker
        this.DoubleBuffered = true;
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        this.UpdateStyles();

        // Set Form Rounded Corners
        this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 12, 12));

        InitializeControls();
        InitializeTray();

        // Enable Dragging by holding background
        this.MouseDown += Form_MouseDown;

        // Start laser animation timer
        _animationTimer = new System.Windows.Forms.Timer();
        _animationTimer.Interval = 30;
        _animationTimer.Tick += AnimationTimer_Tick;
        _animationTimer.Start();

        // Initialize Shake Timer
        _shakeTimer = new System.Windows.Forms.Timer();
        _shakeTimer.Interval = 20;
        _shakeTimer.Tick += ShakeTimer_Tick;

        // Initialize Typing Timer (150ms buffer for scanner)
        _typingTimer = new System.Windows.Forms.Timer();
        _typingTimer.Interval = 150;
        _typingTimer.Tick += TypingTimer_Tick;
    }


    private void InitializeControls()
    {
        // Custom Close Button
        _closeButton = new Label();
        _closeButton.Text = "×";
        _closeButton.Font = new Font("Segoe UI", 16, FontStyle.Regular);
        _closeButton.ForeColor = _colorButtonNormal;
        _closeButton.BackColor = Color.Transparent;
        _closeButton.Location = new Point(445, 5);
        _closeButton.Size = new Size(30, 30);
        _closeButton.Cursor = Cursors.Hand;
        _closeButton.TextAlign = ContentAlignment.MiddleCenter;
        _closeButton.MouseEnter += (s, e) => _closeButton.ForeColor = _colorButtonHoverClose;
        _closeButton.MouseLeave += (s, e) => _closeButton.ForeColor = _colorButtonNormal;
        _closeButton.Click += (s, e) => this.Close(); // Triggers hiding
        this.Controls.Add(_closeButton);

        // Custom Minimize Button
        _minimizeButton = new Label();
        _minimizeButton.Text = "–";
        _minimizeButton.Font = new Font("Segoe UI", 16, FontStyle.Regular);
        _minimizeButton.ForeColor = _colorButtonNormal;
        _minimizeButton.BackColor = Color.Transparent;
        _minimizeButton.Location = new Point(415, 5);
        _minimizeButton.Size = new Size(30, 30);
        _minimizeButton.Cursor = Cursors.Hand;
        _minimizeButton.TextAlign = ContentAlignment.MiddleCenter;
        _minimizeButton.MouseEnter += (s, e) => _minimizeButton.ForeColor = _colorButtonHoverMin;
        _minimizeButton.MouseLeave += (s, e) => _minimizeButton.ForeColor = _colorButtonNormal;
        _minimizeButton.Click += (s, e) => {
            this.WindowState = FormWindowState.Minimized;
        };
        this.Controls.Add(_minimizeButton);

        // Title Label
        _titleLabel = new Label();
        _titleLabel.Text = "АВТОРИЗАЦИЯ В ЛИС";
        _titleLabel.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        _titleLabel.ForeColor = _colorTitleText;
        _titleLabel.BackColor = Color.Transparent;
        _titleLabel.Location = new Point(24, 30);
        _titleLabel.AutoSize = true;
        _titleLabel.MouseDown += Form_MouseDown; // Enable drag
        this.Controls.Add(_titleLabel);

        // Subtitle Label
        _subtitleLabel = new Label();
        _subtitleLabel.Text = "Отсканируйте бейдж для входа";
        _subtitleLabel.Font = new Font("Segoe UI Semibold", 10, FontStyle.Regular);
        _subtitleLabel.ForeColor = _colorSubtitleText;
        _subtitleLabel.BackColor = Color.Transparent;
        _subtitleLabel.Location = new Point(24, 65);
        _subtitleLabel.Size = new Size(300, 20);
        _subtitleLabel.MouseDown += Form_MouseDown; // Enable drag
        this.Controls.Add(_subtitleLabel);

        // Bottom Status Panel
        _statusPanel = new Panel();
        _statusPanel.Location = new Point(20, 160);
        _statusPanel.Size = new Size(440, 80);
        _statusPanel.BackColor = Color.Transparent;
        _statusPanel.Paint += StatusPanel_Paint;
        _statusPanel.MouseDown += Form_MouseDown; // Enable drag
        EnableDoubleBuffer(_statusPanel);
        this.Controls.Add(_statusPanel);

        // Version Label
        Label versionLabel = new Label();
        versionLabel.Text = "Версия " + _config.appVersion;
        versionLabel.Font = new Font("Segoe UI", 8, FontStyle.Regular);
        versionLabel.ForeColor = _isDarkTheme ? Color.FromArgb(100, 100, 130) : Color.FromArgb(150, 150, 150);
        versionLabel.Location = new Point(24, 252);
        versionLabel.Size = new Size(150, 15);
        versionLabel.BackColor = Color.Transparent;
        versionLabel.MouseDown += Form_MouseDown; // Enable drag
        this.Controls.Add(versionLabel);

        // Status Text Label (inside Status Panel)
        _statusTextLabel = new Label();
        _statusTextLabel.Text = "Ожидание сканирования...";
        _statusTextLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _statusTextLabel.ForeColor = _colorStatusTextNormal;
        _statusTextLabel.BackColor = Color.Transparent;
        _statusTextLabel.Location = new Point(50, 10);
        _statusTextLabel.Size = new Size(380, 60);
        _statusTextLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusTextLabel.MouseDown += Form_MouseDown; // Enable drag
        _statusPanel.Controls.Add(_statusTextLabel);

        // Hidden input TextBox for reliable scanner capture when window is focused
        _barcodeTextBox = new TextBox();
        _barcodeTextBox.Location = new Point(-1000, -1000);
        _barcodeTextBox.Size = new Size(100, 20);
        _barcodeTextBox.KeyDown += BarcodeTextBox_KeyDown;
        _barcodeTextBox.TextChanged += BarcodeTextBox_TextChanged;
        this.Controls.Add(_barcodeTextBox);

        // Redirect clicks to keep input focus on the hidden TextBox
        this.Activated += (s, e) => {
            _barcodeTextBox.Focus();
            LoginFormInstance.ResetHookState();
        };
        this.Click += (s, e) => _barcodeTextBox.Focus();
        _titleLabel.Click += (s, e) => _barcodeTextBox.Focus();
        _subtitleLabel.Click += (s, e) => _barcodeTextBox.Focus();
        _statusPanel.Click += (s, e) => _barcodeTextBox.Focus();
        _statusTextLabel.Click += (s, e) => _barcodeTextBox.Focus();
    }



    private void InitializeTray()
    {
        _trayMenu = new ContextMenu();
        _trayMenu.MenuItems.Add("Открыть окно авторизации", (s, e) => ShowForm());
        _trayMenu.MenuItems.Add("-");
        _trayMenu.MenuItems.Add("Выход", (s, e) => {
            _reallyClose = true;
            Application.Exit();
        });

        _notifyIcon = new NotifyIcon();
        _notifyIcon.Text = "1C Authorizator";
        _notifyIcon.ContextMenu = _trayMenu;
        
        // Extract icon if present, otherwise use standard information icon
        string iconPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "across.ico");
        if (File.Exists(iconPath))
        {
            try { _notifyIcon.Icon = new Icon(iconPath); } catch { _notifyIcon.Icon = SystemIcons.Information; }
        }
        else
        {
            _notifyIcon.Icon = SystemIcons.Information;
        }

        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (s, e) => ShowForm();
    }

    public void ShowForm()
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
        UpdateStatus("Ожидание сканирования...", "Info");
    }

    public void UpdateStatus(string message, string type)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string, string>(UpdateStatus), message, type);
            return;
        }

        _statusType = type;
        _statusTextLabel.Text = message;

        if (type == "Error")
        {
            _statusTextLabel.ForeColor = _colorStatusTextError;
            StartShake();
        }
        else if (type == "Success")
        {
            _statusTextLabel.ForeColor = _colorStatusTextSuccess;
        }
        else
        {
            _statusTextLabel.ForeColor = _colorStatusTextNormal;
        }

        _statusPanel.Invalidate();
    }

    private void BarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            _typingTimer.Stop();
            ProcessTextBoxInput();
            e.SuppressKeyPress = true;
        }
    }

    private void BarcodeTextBox_TextChanged(object sender, EventArgs e)
    {
        // Restart the timer on every keystroke/change from scanner
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private void TypingTimer_Tick(object sender, EventArgs e)
    {
        _typingTimer.Stop();
        ProcessTextBoxInput();
    }

    private void ProcessTextBoxInput()
    {
        string rawText = _barcodeTextBox.Text;
        if (string.IsNullOrEmpty(rawText)) return;

        // Clear immediately
        _barcodeTextBox.Text = "";

        LoginFormInstance.LogDebug("[TEXTBOX INPUT] Raw: " + rawText);

        // Translate layout
        string translated = TranslateCyrillicToLatin(rawText);
        LoginFormInstance.LogDebug("[TEXTBOX INPUT] Translated: " + translated);

        string prefix = _config.scannerPrefix;
        if (translated.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            LoginFormInstance.Log(string.Format("[TEXTBOX SCAN] Prefix '{0}' matched. Length: {1}", prefix, translated.Length));
            
            Thread worker = new Thread(() => LoginFormInstance.ProcessScan(translated));
            worker.IsBackground = true;
            worker.Start();
        }
        else
        {
            LoginFormInstance.LogDebug(string.Format("[TEXTBOX INPUT] Ignored (missing prefix '{0}')", prefix));
        }
    }

    private string TranslateCyrillicToLatin(string input)
    {
        var map = new Dictionary<char, char>()
        {
            {'Й', 'Q'}, {'Ц', 'W'}, {'У', 'E'}, {'К', 'R'}, {'Е', 'T'}, {'Н', 'Y'}, {'Г', 'U'}, {'Ш', 'I'}, {'Щ', 'O'}, {'З', 'P'}, {'Х', '{'}, {'Ъ', '}'},
            {'Ф', 'A'}, {'Ы', 'S'}, {'В', 'D'}, {'А', 'F'}, {'П', 'G'}, {'Р', 'H'}, {'О', 'J'}, {'Л', 'K'}, {'Д', 'L'}, {'Ж', ':'}, {'Э', '"'},
            {'Я', 'Z'}, {'Ч', 'X'}, {'С', 'C'}, {'М', 'V'}, {'И', 'B'}, {'Т', 'N'}, {'Ь', 'M'}, {'Б', '<'}, {'Ю', '>'},
            {'й', 'q'}, {'ц', 'w'}, {'у', 'e'}, {'к', 'r'}, {'е', 't'}, {'н', 'y'}, {'г', 'u'}, {'ш', 'i'}, {'щ', 'o'}, {'з', 'p'}, {'х', '['}, {'ъ', ']'},
            {'ф', 'a'}, {'ы', 's'}, {'в', 'd'}, {'а', 'f'}, {'п', 'g'}, {'р', 'h'}, {'о', 'j'}, {'л', 'k'}, {'д', 'l'}, {'ж', ';'}, {'э', '\''},
            {'я', 'z'}, {'ч', 'x'}, {'с', 'c'}, {'м', 'v'}, {'и', 'b'}, {'т', 'n'}, {'ь', 'm'}, {'б', ','}, {'ю', '.'},
            {'.', '/'}, {',', '?'}
        };

        StringBuilder sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            char mapped;
            if (map.TryGetValue(c, out mapped))
            {
                sb.Append(mapped);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }


    private void StartShake()
    {
        if (_shakeCount > 0) return;
        _originalLocation = this.Location;
        _shakeCount = 10;
        _shakeTimer.Start();
    }

    private void ShakeTimer_Tick(object sender, EventArgs e)
    {
        if (_shakeCount > 0)
        {
            Random r = new Random();
            this.Location = new Point(
                _originalLocation.X + r.Next(-6, 7),
                _originalLocation.Y + r.Next(-6, 7)
            );
            _shakeCount--;
        }
        else
        {
            _shakeTimer.Stop();
            this.Location = _originalLocation;
        }
    }

    private void AnimationTimer_Tick(object sender, EventArgs e)
    {
        _pulseAlpha += _pulseDirection;
        if (_pulseAlpha >= 180)
        {
            _pulseAlpha = 180;
            _pulseDirection = -3;
        }
        else if (_pulseAlpha <= 40)
        {
            _pulseAlpha = 40;
            _pulseDirection = 3;
        }

        // Only invalidate the scanner visual frame rectangle to prevent high CPU utilization
        this.Invalidate(new Rectangle(345, 40, 110, 110));
    }


    private void Form_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0); // Drag form
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Draw Premium linear background gradient
        using (LinearGradientBrush brush = new LinearGradientBrush(
            ClientRectangle,
            _colorBgTop,
            _colorBgBottom,
            45F))
        {
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        // Draw Outer Thin Glow Border
        using (Pen borderPen = new Pen(_colorOuterBorder, 1))
        {
            e.Graphics.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // Draw Scanner HUD frame (Shifted down to y=45 to resolve button overlap)
        using (Pen framePen = new Pen(_colorScannerHudFrame, 2))
        {
            e.Graphics.DrawRectangle(framePen, 350, 45, 100, 100);
        }

        // Draw high-tech frame corners
        using (Pen cornerPen = new Pen(_colorScannerHudCorner, 3))
        {
            // Top-Left
            e.Graphics.DrawLine(cornerPen, 350, 45, 362, 45);
            e.Graphics.DrawLine(cornerPen, 350, 45, 350, 57);
            // Top-Right
            e.Graphics.DrawLine(cornerPen, 450, 45, 438, 45);
            e.Graphics.DrawLine(cornerPen, 450, 45, 450, 57);
            // Bottom-Left
            e.Graphics.DrawLine(cornerPen, 350, 145, 362, 145);
            e.Graphics.DrawLine(cornerPen, 350, 145, 350, 133);
            // Bottom-Right
            e.Graphics.DrawLine(cornerPen, 450, 145, 438, 145);
            e.Graphics.DrawLine(cornerPen, 450, 145, 450, 133);
        }

        // Draw pulsing QR code vector pattern inside HUD
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color pulseColor = Color.FromArgb(_pulseAlpha, _colorScannerHudCorner);
        using (Pen pulsePen = new Pen(pulseColor, 2))
        using (SolidBrush pulseBrush = new SolidBrush(pulseColor))
        {
            DrawLocator(e.Graphics, pulsePen, pulseBrush, 368, 63);
            DrawLocator(e.Graphics, pulsePen, pulseBrush, 416, 63);
            DrawLocator(e.Graphics, pulsePen, pulseBrush, 368, 111);

            // Draw simplified QR code pixels
            e.Graphics.FillRectangle(pulseBrush, 392, 63, 6, 6);
            e.Graphics.FillRectangle(pulseBrush, 402, 69, 6, 6);
            e.Graphics.FillRectangle(pulseBrush, 392, 75, 12, 6);
            e.Graphics.FillRectangle(pulseBrush, 368, 87, 6, 12);
            e.Graphics.FillRectangle(pulseBrush, 380, 93, 12, 6);
            e.Graphics.FillRectangle(pulseBrush, 398, 87, 6, 18);
            e.Graphics.FillRectangle(pulseBrush, 410, 87, 12, 6);
            e.Graphics.FillRectangle(pulseBrush, 416, 99, 6, 12);
            e.Graphics.FillRectangle(pulseBrush, 392, 111, 12, 6);
            e.Graphics.FillRectangle(pulseBrush, 392, 121, 6, 6);
            e.Graphics.FillRectangle(pulseBrush, 408, 111, 6, 12);
            e.Graphics.FillRectangle(pulseBrush, 420, 121, 12, 6);
        }
    }

    private void DrawLocator(Graphics g, Pen pen, Brush brush, int x, int y)
    {
        g.DrawRectangle(pen, x + 1, y + 1, 14, 14);
        g.FillRectangle(brush, x + 5, y + 5, 6, 6);
    }


    private void StatusPanel_Paint(object sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Draw Status panel background rounded box
        GraphicsPath path = new GraphicsPath();
        int radius = 8;
        Rectangle r = new Rectangle(0, 0, _statusPanel.Width - 1, _statusPanel.Height - 1);
        path.AddArc(r.X, r.Y, radius, radius, 180, 90);
        path.AddArc(r.X + r.Width - radius, r.Y, radius, radius, 270, 90);
        path.AddArc(r.X + r.Width - radius, r.Y + r.Height - radius, radius, radius, 0, 90);
        path.AddArc(r.X, r.Y + r.Height - radius, radius, radius, 90, 90);
        path.CloseAllFigures();

        using (SolidBrush bgBrush = new SolidBrush(_colorStatusPanelBg))
        {
            e.Graphics.FillPath(bgBrush, path);
        }

        using (Pen borderPen = new Pen(_colorStatusPanelBorder, 1))
        {
            e.Graphics.DrawPath(borderPen, path);
        }

        // Draw Status Icon based on type
        DrawStatusIcon(e.Graphics, 15, 30);
    }

    private void DrawStatusIcon(Graphics g, int x, int y)
    {
        if (_statusType == "Success")
        {
            using (Pen pen = new Pen(_colorStatusTextSuccess, 3))
            {
                g.DrawLines(pen, new[] {
                    new Point(x + 2, y + 10),
                    new Point(x + 8, y + 16),
                    new Point(x + 18, y + 4)
                });
            }
        }
        else if (_statusType == "Error")
        {
            using (SolidBrush brush = new SolidBrush(_colorStatusTextError))
            {
                g.FillPolygon(brush, new[] {
                    new Point(x + 10, y),
                    new Point(x, y + 18),
                    new Point(x + 20, y + 18)
                });
            }
            using (Pen pen = new Pen(_colorStatusPanelBg, 2))
            {
                g.DrawLine(pen, x + 10, y + 5, x + 10, y + 12);
                g.DrawEllipse(pen, x + 9, y + 14, 2, 2);
            }
        }
        else // Info / Scanning
        {
            Color infoColor = _isDarkTheme ? Color.FromArgb(59, 130, 246) : Color.FromArgb(79, 70, 229);
            using (Pen pen = new Pen(infoColor, 2))
            {
                g.DrawEllipse(pen, x, y, 18, 18);
                g.DrawLine(pen, x + 9, y + 8, x + 9, y + 14);
                g.DrawEllipse(pen, x + 8, y + 4, 2, 2);
            }
        }
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key != null)
                {
                    object val = key.GetValue("AppsUseLightTheme");
                    if (val != null)
                    {
                        return (int)val == 0;
                    }
                }
            }
        }
        catch {}
        return true; // Default to dark theme if detection fails
    }

    private void EnableDoubleBuffer(Control c)
    {
        try
        {
            var property = typeof(Control).GetProperty("DoubleBuffered", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (property != null)
            {
                property.SetValue(c, true, null);
            }
        }
        catch {}
    }


    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
            // Show tip in tray
            _notifyIcon.ShowBalloonTip(2000, "1C Authorizator", 
                "Приложение свернуто в трей. Отсканировать бейдж можно в любой момент.", ToolTipIcon.Info);
        }
        else
        {
            // Close notify icon on actual app exit
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.OnFormClosing(e);
    }
}

public class User
{
    public string login = "";
    public string password = "";
    public string server = "";
    public string database = "";
    public string platform = "";
    public string infobase_name = "";

    public static User ParseJson(string json)
    {
        User u = new User();
        var matches = System.Text.RegularExpressions.Regex.Matches(json, "\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            string key = m.Groups[1].Value;
            string val = m.Groups[2].Value;
            try
            {
                val = System.Text.RegularExpressions.Regex.Unescape(val);
            }
            catch {}

            switch (key.ToLower())
            {
                case "login": u.login = val; break;
                case "password": u.password = val; break;
                case "server": u.server = val; break;
                case "database": u.database = val; break;
                case "platform": u.platform = val; break;
                case "infobase_name": u.infobase_name = val; break;
            }
        }
        return u;
    }
}

public class Config
{
    public string pathTo1c = @"C:\Program Files (x86)\1cv8\8.3.27.1786\bin\1cv8.exe";
    public string platformVersion = "8.3";
    public string defaultServer = "";
    public string defaultDatabase = "";
    public string scannerPrefix = "QR:";
    public int scannerTimeoutMs = 1000;

    public int minBarcodeLen = 5;
    public string logFile = "1c-authorizator.log";
    public bool restart1C = true;
    public bool debug = false;
    public bool topMost = false;
    public string theme = "auto";
    public string appVersion = "1.0.0";


    public static Config Load(string path)
    {
        Config c = new Config();
        if (!File.Exists(path)) return c;

        string[] lines = File.ReadAllLines(path);
        string currentSection = "";

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            // Reset section if the line is not indented (root-level key)
            bool isIndented = line.StartsWith(" ") || line.StartsWith("\t");
            if (!isIndented)
            {
                currentSection = "";
            }

            if (trimmed.EndsWith(":") && !trimmed.Contains("\""))
            {
                currentSection = trimmed.Substring(0, trimmed.Length - 1).Trim().ToLower();
                continue;
            }

            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) continue;

            string key = trimmed.Substring(0, colonIdx).Trim().ToLower();
            string val = trimmed.Substring(colonIdx + 1).Trim();

            int hashIdx = val.IndexOf('#');
            if (hashIdx >= 0) val = val.Substring(0, hashIdx).Trim();

            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                val = val.Substring(1, val.Length - 2);
            else if (val.StartsWith("'") && val.EndsWith("'") && val.Length >= 2)
                val = val.Substring(1, val.Length - 2);

            if (currentSection == "scanner")
            {
                if (key == "prefix") c.scannerPrefix = val;
                else if (key == "timeout_ms") int.TryParse(val, out c.scannerTimeoutMs);
                else if (key == "minbarcodelen") int.TryParse(val, out c.minBarcodeLen);
            }
            else
            {
                if (key == "pathto1c") c.pathTo1c = val;
                else if (key == "platformversion") c.platformVersion = val;
                else if (key == "defaultserver") c.defaultServer = val;
                else if (key == "defaultdatabase") c.defaultDatabase = val;
                else if (key == "logfile") c.logFile = val;
                else if (key == "restart1c") c.restart1C = val.ToLower() == "true" || val == "1";
                else if (key == "debug") c.debug = val.ToLower() == "true" || val == "1";
                else if (key == "topmost") c.topMost = val.ToLower() == "true" || val == "1";
                else if (key == "theme") c.theme = val.ToLower();
                else if (key == "appversion") c.appVersion = val;
            }

        }
        return c;
    }
}
