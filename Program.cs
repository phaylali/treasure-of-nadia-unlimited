using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Security.Principal;

namespace AutoClicker
{
    public class Program
    {
        [STAThread]
        static void Main()
        {
            // Check if running as administrator
            bool isElevated;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }

            if (!isElevated)
            {
                MessageBox.Show("This application works better when run as administrator.\nSome windows might not receive inputs otherwise.",
                    "Admin Rights Recommended", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OverlayForm());
        }
    }

    public class OverlayForm : Form
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
            public static int Size => Marshal.SizeOf(typeof(INPUT));
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const ushort VK_UP = 0x26;
        private const ushort VK_DOWN = 0x28;

        private Button playButton;
        private Button stopButton;
        private ComboBox windowSelector;
        private Dictionary<string, IntPtr> windowHandles = new Dictionary<string, IntPtr>();
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private bool isRunning = false;

        public OverlayForm()
        {
            InitializeComponents();
            RefreshWindowList();
        }

        private void InitializeComponents()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(64, 64, 64);
            this.Size = new Size(300, 120);
            this.StartPosition = FormStartPosition.CenterScreen;

            windowSelector = new ComboBox
            {
                Size = new Size(280, 30),
                Location = new Point(10, 10),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.White
            };
            this.Controls.Add(windowSelector);

            var refreshButton = new Button
            {
                Text = "⟳",
                Size = new Size(30, 23),
                Location = new Point(260, 10),
                BackColor = Color.LightBlue,
                FlatStyle = FlatStyle.Flat
            };
            refreshButton.Click += (s, e) => RefreshWindowList();
            this.Controls.Add(refreshButton);

            playButton = new Button
            {
                Text = "Play",
                Size = new Size(80, 30),
                Location = new Point(10, 45),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat
            };
            playButton.Click += PlayButton_Click;
            this.Controls.Add(playButton);

            stopButton = new Button
            {
                Text = "Stop",
                Size = new Size(80, 30),
                Location = new Point(100, 45),
                BackColor = Color.LightPink,
                Enabled = false,
                FlatStyle = FlatStyle.Flat
            };
            stopButton.Click += StopButton_Click;
            this.Controls.Add(stopButton);

            var closeButton = new Button
            {
                Text = "X",
                Size = new Size(20, 20),
                Location = new Point(this.Width - 25, 0),
                BackColor = Color.Red,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            closeButton.Click += (s, e) => {
                if (isRunning) StopClicking();
                this.Close();
            };
            this.Controls.Add(closeButton);

            this.MouseDown += Form_MouseDown;
        }

        private void ForceActivateWindow(IntPtr hWnd)
        {
            uint foreThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            uint appThread = GetCurrentThreadId();
            bool threadsAttached = false;

            try
            {
                threadsAttached = AttachThreadInput(foreThread, appThread, true);
                SetForegroundWindow(hWnd);
                System.Threading.Thread.Sleep(100); // Give it some time to focus
            }
            finally
            {
                if (threadsAttached)
                {
                    AttachThreadInput(foreThread, appThread, false);
                }
            }
        }

        private void SendKey(ushort keyCode, bool keyUp)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = keyCode;
            inputs[0].U.ki.dwFlags = keyUp ? KEYEVENTF_KEYUP : 0;
            inputs[0].U.ki.time = 0;
            inputs[0].U.ki.wScan = 0;
            inputs[0].U.ki.dwExtraInfo = IntPtr.Zero;

            SendInput(1, inputs, INPUT.Size);
        }

        private void RefreshWindowList()
        {
            windowHandles.Clear();
            windowSelector.Items.Clear();

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    StringBuilder title = new StringBuilder(256);
                    GetWindowText(hWnd, title, 256);
                    string windowTitle = title.ToString().Trim();

                    if (!string.IsNullOrEmpty(windowTitle))
                    {
                        string displayTitle = $"{windowTitle} (0x{hWnd.ToInt64():X})";
                        windowHandles[displayTitle] = hWnd;
                        windowSelector.Items.Add(displayTitle);
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (windowSelector.Items.Count > 0)
                windowSelector.SelectedIndex = 0;
        }

        private async void PlayButton_Click(object sender, EventArgs e)
        {
            if (isRunning || windowSelector.SelectedItem == null) return;

            string selectedWindow = windowSelector.SelectedItem.ToString();
            if (!windowHandles.ContainsKey(selectedWindow))
            {
                MessageBox.Show("Please select a valid window.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            IntPtr targetWindow = windowHandles[selectedWindow];
            ForceActivateWindow(targetWindow);

            isRunning = true;
            playButton.Enabled = false;
            stopButton.Enabled = true;
            windowSelector.Enabled = false;

            if (cancellationTokenSource == null || cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource = new CancellationTokenSource();
            }

            try
            {
                await Task.Run(async () =>
                {
                    while (!cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        // Ensure window is focused
                        this.Invoke((Action)(() => ForceActivateWindow(targetWindow)));

                        // Press UP key
                        SendKey(VK_UP, false);
                        await Task.Delay(1000);
                        SendKey(VK_UP, true);

                        await Task.Delay(100);

                        // Press DOWN key
                        SendKey(VK_DOWN, false);
                        await Task.Delay(1000);
                        SendKey(VK_DOWN, true);

                        await Task.Delay(100);
                    }
                }, cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            StopClicking();
        }

        private void StopClicking()
        {
            isRunning = false;

            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
            }

            // Release both keys
            SendKey(VK_UP, true);
            SendKey(VK_DOWN, true);

            playButton.Enabled = true;
            stopButton.Enabled = false;
            windowSelector.Enabled = true;
        }

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isRunning)
            {
                StopClicking();
            }
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Dispose();
            }
            base.OnFormClosing(e);
        }
    }
}