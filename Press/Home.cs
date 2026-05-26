using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Press.Model;
using Press.Properties;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq; 

namespace Press
{
    public partial class Home : XtraForm
    {
        private readonly LrXCyclicReader _reader = new();
        private readonly LrXMessageWriter _writer;
        private readonly LrXData[] _lastData = new LrXData[9];       // index 1..8
        private readonly double[] _lastDisplayed = new double[9];   // index 1..8
        private readonly int[] _zeroRawCount = new int[9];          // đếm raw == 0 liên tục
        private readonly int[] _goodRawCount = new int[9];          // đếm raw != 0 liên tục để thoát lỗi

        private const int ZERO_RAW_THRESHOLD = 8;                   // ~1.6 giây (timer 200ms)
        private const int GOOD_RAW_THRESHOLD = 5;                   // ~1 giây - cần 5 mẫu tốt liên tục để thoát NaN

        private readonly Control[] _panels;
        private const double HYSTERESIS_MM = 0.3;

        private readonly Timer _timer = new Timer();
        private int _debugCounter;

        public Home()
        {
            InitializeComponent();
            DoubleBuffered = true;

            _writer = new LrXMessageWriter(_reader.Client);

            for (int i = 1; i <= 8; i++)
            {
                _lastData[i] = LrXData.Invalid;
                _lastDisplayed[i] = double.NaN;
                _zeroRawCount[i] = 0;
                _goodRawCount[i] = 0;  
            }

            _panels = new Control[]
            {
                pn_mam2, pn_mam5, pn_mam9, pn_mam12,
                pn_mam15, pn_mam18, pn_mam22, pn_mam25
            };

            foreach (var pnl in _panels)
                pnl.Paint += DrawDistancePanel_Paint;

            _timer.Interval = 200;
            _timer.Tick += Timer_Tick;
            _timer.Start();

            TryConnect();
        }

        private void TryConnect()
        {
            try { _reader.Connect(); }
            catch { }
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_reader.IsConnected)
            {
                _reader.TryReconnect();
                ResetAllPorts();
            }
            else
            {
                try
                {
                    bool needRefresh = false;
                    for (int port = 1; port <= 8; port++)
                    {
                        var newData = _reader.ReadPort(port);
                        if (UpdatePortState(port, newData))
                            needRefresh = true;
                    }

                    if (needRefresh)
                    {
                        foreach (var pnl in _panels)
                            SafeInvalidate(pnl);
                    }

                    if (++_debugCounter % 10 == 0)
                        _reader.DebugDumpBuffer();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Timer] Lỗi đọc: {ex.Message}");
                    _reader.Disconnect();
                }
            }

            if (!_reader.IsConnected)
            {
                foreach (var pnl in _panels)
                    SafeInvalidate(pnl);
            }
        }

        private bool UpdatePortState(int port, LrXData newData)
        {
            ref var last = ref _lastData[port];
            ref var disp = ref _lastDisplayed[port];
            ref var zeroCount = ref _zeroRawCount[port];
            ref var goodCount = ref _goodRawCount[port];

            last = newData;

            bool displayChanged = false;

            // Dữ liệu không hợp lệ ngay từ đầu
            if (!newData.IsValid || double.IsNaN(newData.DistanceMm))
            {
                if (!double.IsNaN(disp))
                {
                    disp = double.NaN;
                    displayChanged = true;
                }
                zeroCount = ZERO_RAW_THRESHOLD;
                goodCount = 0;
                return displayChanged;
            }

            short estimatedRaw = (short)Math.Round(newData.DistanceMm / NqConfig.Resolution);

            if (estimatedRaw == 0)
            {
                zeroCount++;

                // Vào trạng thái lỗi khi đủ ngưỡng
                if (zeroCount >= ZERO_RAW_THRESHOLD && !double.IsNaN(disp))
                {
                    disp = double.NaN;
                    displayChanged = true;
                }

                goodCount = 0;  // Reset đếm tốt mỗi khi thấy 0
            }
            else
            {
                // Giá trị hợp lệ → tăng đếm tốt
                goodCount++;

                // Reset zero ngay khi thấy !=0
                zeroCount = 0;

                // Chỉ cập nhật hiển thị khi đủ mẫu tốt liên tục
                bool canDisplayValue = goodCount >= GOOD_RAW_THRESHOLD;

                if (canDisplayValue)
                {
                    double candidate = newData.DistanceMm;

                    if (double.IsNaN(disp) || Math.Abs(candidate - disp) >= HYSTERESIS_MM)
                    {
                        disp = candidate;
                        displayChanged = true;
                    }
                }
                // Nếu chưa đủ GOOD_RAW_THRESHOLD → giữ nguyên NaN (không thay đổi)
            }

            return displayChanged;
        }

        private void ResetAllPorts()
        {
            for (int i = 1; i <= 8; i++)
            {
                _lastData[i] = LrXData.Invalid;
                _lastDisplayed[i] = double.NaN;
                _zeroRawCount[i] = 0;
                _goodRawCount[i] = 0;  // Reset đếm mẫu tốt
            }
        }

        private void SafeInvalidate(Control ctrl)
        {
            if (ctrl?.IsHandleCreated != true) return;
            if (ctrl.InvokeRequired)
                ctrl.BeginInvoke(() => ctrl.Invalidate());
            else
                ctrl.Invalidate();
        }

        private void DrawDistancePanel_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not Control panel) return;

            int portIndex = Array.IndexOf(_panels, panel);
            if (portIndex < 0) return;

            int port = portIndex + 1;

            var data = _lastData[port];
            var value = _lastDisplayed[port];

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            string mainText;
            string subText = "mm";
            Brush textBrush;
            Brush shadowBrush = Brushes.Gray;
            Font mainFont;
            Font subFont;

            if (!_reader.IsConnected)
            {
                // Mất kết nối EtherNet/IP
                mainText = "KHÔNG KẾT NỐI";
                textBrush = Brushes.DarkRed;
                mainFont = new Font("Segoe UI", 22f, FontStyle.Bold);
                subFont = new Font("Segoe UI", 14f, FontStyle.Regular);
                subText = "EtherNet/IP";
            }
            else if (double.IsNaN(value) || _zeroRawCount[port] >= ZERO_RAW_THRESHOLD)
            {
                // Trường hợp lỗi cảm biến → hiển thị 0.0 mm màu ĐEN như yêu cầu
                mainText = "0.0";
                textBrush = Brushes.Black;           // ← Đổi thành màu đen
                mainFont = new Font("Segoe UI", 34f, FontStyle.Bold);
                subFont = new Font("Segoe UI", 22f, FontStyle.Bold);
            }
            else
            {
                // Giá trị bình thường
                double displayVal = -value;
                mainText = displayVal.ToString("F1");

                textBrush = displayVal switch
                {
                    < 5 => Brushes.OrangeRed,
                    > 150 => Brushes.DarkOrange,
                    _ => Brushes.Navy
                };

                mainFont = new Font("Segoe UI", 34f, FontStyle.Bold);
                subFont = new Font("Segoe UI", 22f, FontStyle.Bold);
            }

            // ====================== VẼ TEXT ======================
            var mainSize = g.MeasureString(mainText, mainFont);
            var subSize = g.MeasureString(subText, subFont);

            float totalHeight = mainSize.Height + (string.IsNullOrEmpty(subText) ? 0 : subSize.Height + 4);
            float startY = (panel.Height - totalHeight) / 2f;

            float mainX = (panel.Width - mainSize.Width) / 2f;
            float subX = (panel.Width - subSize.Width) / 2f;

            float mainY = startY;
            float subY = mainY + mainSize.Height + 4;

            // Shadow nhẹ
            g.DrawString(mainText, mainFont, shadowBrush, mainX + 1, mainY + 1);
            if (!string.IsNullOrEmpty(subText))
                g.DrawString(subText, subFont, shadowBrush, subX + 1, subY + 1);

            // Text chính
            g.DrawString(mainText, mainFont, textBrush, mainX, mainY);
            if (!string.IsNullOrEmpty(subText))
                g.DrawString(subText, subFont, textBrush, subX, subY);

            mainFont.Dispose();
            subFont.Dispose();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                _timer?.Stop();
                _timer?.Dispose();
                _reader?.Dispose();
            }
            catch { }

            base.OnFormClosing(e);
        }

        private void btn_close_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void btn_reset_Click(object sender, EventArgs e)
        {
            string password = Microsoft.VisualBasic.Interaction.InputBox(
                "Nhập mật khẩu để thực hiện Reset ",
                "Xác nhận Reset",
                "",
                -1, -1);

            if (string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            if (password.Trim().ToLower() != "1234")
            {
                MessageBox.Show("Mật khẩu không đúng!\nHành động Reset bị hủy.",
                                "Lỗi xác thực",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                return;
            }

            if (!_reader.IsConnected)
            {
                MessageBox.Show("Chưa kết nối EtherNet/IP. Vui lòng kiểm tra kết nối!",
                                "Lỗi kết nối",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                return;
            }

            btn_reset.Enabled = false;
            this.Cursor = Cursors.WaitCursor;

            try
            {
                for (byte port = 1; port <= 8; port++)
                {
                    bool success = await _writer.SendZeroShiftAsync(port);
                    if (!success)
                    {
                        System.Diagnostics.Debug.WriteLine($"Zero shift port {port} thất bại");
                    }
                    await Task.Delay(80);
                }

                ResetAllPorts();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi thực hiện reset: {ex.Message}",
                                "Lỗi hệ thống",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }
            finally
            {
                btn_reset.Enabled = true;
                this.Cursor = Cursors.Default;
            }
        }
    }
}