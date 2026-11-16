// SFP+ DDM 诊断工具主界面
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SfpDdm
{
    internal sealed class BufferedDataGridView : DataGridView
    {
        public BufferedDataGridView()
        {
            DoubleBuffered = true;
        }
    }

    internal sealed class MainForm : Form
    {
        private ComboBox cboIndex;
        private ComboBox cboSpeed;
        private ComboBox cboInterval;
        private Button btnRead;
        private CheckBox chkAuto;
        private Label lblState;

        private DataGridView dgvDdm;
        private DataGridView dgvInfo;
        private Label lblTxFault;
        private Label lblRxLos;
        private Label lblReady;
        private Label lblTxDisable;

        private Label lblA0;
        private Label lblA2;
        private RichTextBox txtA0;
        private RichTextBox txtA2;

        private Timer autoTimer;
        private bool busy;

        private static readonly Color ColorNormal = Color.FromArgb(0, 128, 64);
        private static readonly Color ColorWarning = Color.FromArgb(190, 110, 0);
        private static readonly Color ColorAlarm = Color.FromArgb(200, 30, 30);
        private static readonly Color ColorMuted = Color.FromArgb(120, 120, 120);

        public MainForm()
        {
            Text = "SFP+ DDM 诊断工具 20261116 V1.0.0（CH341 USB-I2C）© 2026 Haisen All rights reserved ";
            ClientSize = new Size(1000, 680);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();

            autoTimer = new Timer { Interval = 2000 };
            autoTimer.Tick += async (s, e) =>
            {
                if (!busy) await RefreshAllAsync(false);
            };

            cboInterval.SelectedIndexChanged += (s, e) =>
            {
                if (autoTimer != null) autoTimer.Interval = IntervalMs;
            };
            chkAuto.CheckedChanged += async (s, e) =>
            {
                if (chkAuto.Checked)
                {
                    autoTimer.Start();
                    if (!busy) await RefreshAllAsync(false);
                }
                else
                {
                    autoTimer.Stop();
                }
            };

            FormClosing += (s, e) =>
            {
                autoTimer.Stop();
                Ch341I2c.Close();
            };
            Shown += async (s, e) => await RefreshAllAsync(false);

            chkAuto.Checked = true;
        }

        private int IntervalMs
        {
            get
            {
                if (cboInterval.SelectedIndex == 0) return 1000;
                if (cboInterval.SelectedIndex == 2) return 5000;
                return 2000;
            }
        }

        // ---------------- 界面构建 ----------------

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            root.Controls.Add(BuildTopBar(), 0, 0);

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(14, 6)
            };
            tabs.TabPages.Add(BuildDdmPage());
            tabs.TabPages.Add(BuildInfoPage());
            tabs.TabPages.Add(BuildRawPage());
            root.Controls.Add(tabs, 0, 1);

            Controls.Add(root);
        }

        private Control BuildTopBar()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 1,
                Padding = new Padding(10, 6, 10, 6)
            };
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            cboIndex = new ComboBox
            {
                Anchor = AnchorStyles.None,
                Width = 58,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboIndex.Items.AddRange(new object[] { 0, 1, 2, 3 });
            cboIndex.SelectedIndex = 0;
            cboIndex.SelectedIndexChanged += (s, e) => Ch341I2c.Close();

            cboSpeed = new ComboBox
            {
                Anchor = AnchorStyles.None,
                Width = 104,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboSpeed.Items.AddRange(new object[] { "20 kHz", "100 kHz", "400 kHz" });
            cboSpeed.SelectedIndex = 1;
            cboSpeed.SelectedIndexChanged += (s, e) => Ch341I2c.Close();

            btnRead = new Button
            {
                Text = "读取全部信息",
                Anchor = AnchorStyles.None,
                Size = new Size(126, 30)
            };
            btnRead.Click += async (s, e) => await RefreshAllAsync(true);

            chkAuto = new CheckBox
            {
                Text = "自动刷新",
                Anchor = AnchorStyles.None,
                AutoSize = true
            };

            cboInterval = new ComboBox
            {
                Anchor = AnchorStyles.None,
                Width = 90,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboInterval.Items.AddRange(new object[] { "1 秒", "2 秒", "5 秒" });
            cboInterval.SelectedIndex = 1;

            lblState = new Label
            {
                Dock = DockStyle.Fill,
                Text = "未连接",
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = ColorMuted,
                AutoEllipsis = true
            };

            bar.Controls.Add(BarLabel("设备序号"), 0, 0);
            bar.Controls.Add(cboIndex, 1, 0);
            bar.Controls.Add(BarLabel("I2C 速率"), 2, 0);
            bar.Controls.Add(cboSpeed, 3, 0);
            bar.Controls.Add(btnRead, 4, 0);
            bar.Controls.Add(chkAuto, 5, 0);
            bar.Controls.Add(cboInterval, 6, 0);
            bar.Controls.Add(lblState, 7, 0);
            return bar;
        }

        private static Label BarLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
        }

        private TabPage BuildDdmPage()
        {
            var page = new TabPage("实时诊断 (DDM)");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            dgvDdm = CreateGrid();
            dgvDdm.Columns.Add(MakeColumn("name", "参数", 70F));
            dgvDdm.Columns.Add(MakeColumn("current", "当前值", 165F));
            dgvDdm.Columns.Add(MakeColumn("ha", "高告警-(故障)", 105F));
            dgvDdm.Columns.Add(MakeColumn("la", "低告警-(故障)", 105F));
            dgvDdm.Columns.Add(MakeColumn("hw", "高警告-(报警)", 105F));
            dgvDdm.Columns.Add(MakeColumn("lw", "低警告-(报警)", 105F));
            dgvDdm.Columns.Add(MakeColumn("status", "状态", 95F));
            dgvDdm.RowTemplate.Height = 38;
            dgvDdm.ColumnHeadersHeight = 36;

            string[] names = { "温度", "供电电压", "TX 偏置电流", "TX 光功率", "RX 光功率" };
            foreach (string name in names)
            {
                int row = dgvDdm.Rows.Add(name, "--", "--", "--", "--", "--", "--");
                dgvDdm.Rows[row].Cells[0].Style.Font = new Font(Font, FontStyle.Bold);
                dgvDdm.Rows[row].Cells[0].Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            }

            int attenuationRow = dgvDdm.Rows.Add("链路衰减", "--", "--", "--", "--", "--", "--");
            dgvDdm.Rows[attenuationRow].Cells[0].Style.Font = new Font(Font, FontStyle.Bold);
            dgvDdm.Rows[attenuationRow].Cells[0].Style.Alignment = DataGridViewContentAlignment.MiddleLeft;

            var stat = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(0, 8, 0, 0)
            };
            for (int i = 0; i < 4; i++)
                stat.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            lblTxFault = StatusLabel("TX_FAULT：--");
            lblRxLos = StatusLabel("RX_LOS：--");
            lblReady = StatusLabel("数据就绪：--");
            lblTxDisable = StatusLabel("TX 使能：--");
            stat.Controls.Add(lblTxFault, 0, 0);
            stat.Controls.Add(lblRxLos, 1, 0);
            stat.Controls.Add(lblReady, 2, 0);
            stat.Controls.Add(lblTxDisable, 3, 0);

            layout.Controls.Add(dgvDdm, 0, 0);
            layout.Controls.Add(stat, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildInfoPage()
        {
            var page = new TabPage("模块信息");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                Padding = new Padding(10)
            };

            dgvInfo = CreateGrid();
            dgvInfo.SelectionMode = DataGridViewSelectionMode.CellSelect;
            var valueColumn = MakeColumn("value", "值", 70F);
            valueColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgvInfo.Columns.Add(MakeColumn("item", "项目", 30F));
            dgvInfo.Columns.Add(valueColumn);
            dgvInfo.RowTemplate.Height = 30;

            layout.Controls.Add(dgvInfo, 0, 0);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildRawPage()
        {
            var page = new TabPage("原始数据");
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            lblA0 = new Label
            {
                Text = "A0h 基本信息（I2C 0x50）",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold)
            };
            lblA2 = new Label
            {
                Text = "A2h 数字诊断 DDM（I2C 0x51）",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold)
            };

            txtA0 = MakeHexBox();
            txtA0.Margin = new Padding(0, 0, 6, 0);
            txtA2 = MakeHexBox();
            txtA2.Margin = new Padding(6, 0, 0, 0);

            layout.Controls.Add(lblA0, 0, 0);
            layout.Controls.Add(lblA2, 1, 0);
            layout.Controls.Add(txtA0, 0, 1);
            layout.Controls.Add(txtA2, 1, 1);
            page.Controls.Add(layout);
            return page;
        }

        private DataGridView CreateGrid()
        {
            var grid = new BufferedDataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 34,
                GridColor = Color.FromArgb(224, 224, 224)
            };
            grid.RowTemplate.Height = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 242, 247);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 233, 248);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            return grid;
        }

        private static DataGridViewTextBoxColumn MakeColumn(string name, string header, float fillWeight)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FillWeight = fillWeight,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                }
            };
        }

        private static Label StatusLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(247, 247, 247),
                ForeColor = ColorMuted,
                Margin = new Padding(3, 0, 3, 0),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
        }

        private static RichTextBox MakeHexBox()
        {
            return new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                Font = new Font("Consolas", 9.5F),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                DetectUrls = false
            };
        }

        // ---------------- 数据读取 ----------------

        private async Task RefreshAllAsync(bool manual)
        {
            if (busy) return;
            busy = true;
            if (manual)
            {
                btnRead.Enabled = false;
                Cursor = Cursors.WaitCursor;
            }

            try
            {
                int deviceIndex = Convert.ToInt32(cboIndex.SelectedItem);
                int speed = cboSpeed.SelectedIndex;
                SfpModule module = await Task.Run(() => ReadModule(deviceIndex, speed));

                UpdateUi(module);
                SetState("已连接  |  " + module.Vendor + " " + module.PartNumber
                    + "  |  " + DateTime.Now.ToString("HH:mm:ss"), ColorNormal);
            }
            catch (Exception ex)
            {
                SetState("读取失败：" + ex.Message, ColorAlarm);
                if (manual)
                    MessageBox.Show(this, ex.Message, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                busy = false;
                if (manual)
                {
                    btnRead.Enabled = true;
                    Cursor = Cursors.Default;
                }
            }
        }

        private static SfpModule ReadModule(int deviceIndex, int speed)
        {
            Ch341I2c.Open(deviceIndex, speed);
            byte[] a0 = Ch341I2c.Read(0xA0, 0, 128);
            byte[] a2 = Ch341I2c.Read(0xA2, 0, 128);
            SfpModule module = SfpModule.Parse(a0, a2);

            if (!module.CcBaseOk)
            {
                // 数据校验失败也视为读取失败：重新初始化 CH341 后重读一次
                Ch341I2c.Reopen();
                a0 = Ch341I2c.Read(0xA0, 0, 128);
                a2 = Ch341I2c.Read(0xA2, 0, 128);
                module = SfpModule.Parse(a0, a2);
            }

            if (!module.CcBaseOk)
                throw new IOException(
                    "读取到无效数据（A0h 校验和错误）。请检查SPF光模块是否插入并锁定，SPF电源开关是否打开。");

            return module;
        }

        // ---------------- 界面刷新 ----------------

        private void UpdateUi(SfpModule module)
        {
            UpdateDdmRows(module);
            UpdateStatusLabels(module);
            UpdateInfoGrid(module);
            UpdateRawText(module);
        }

        private void UpdateDdmRows(SfpModule module)
        {
            int[] metrics =
            {
                SfpModule.MetricTemp,
                SfpModule.MetricVoltage,
                SfpModule.MetricBias,
                SfpModule.MetricTxPower,
                SfpModule.MetricRxPower
            };
            DdmValue[] values = { module.Temperature, module.Voltage, module.Bias, module.TxPower, module.RxPower };

            for (int row = 0; row < metrics.Length; row++)
            {
                int metric = metrics[row];
                DdmValue value = values[row];
                DdmStatus status = module.Evaluate(metric, value);
                DataGridViewCellCollection cells = dgvDdm.Rows[row].Cells;

                cells[1].Value = SfpModule.FormatValue(metric, value);
                cells[2].Value = SfpModule.FormatThreshold(metric, module.Thresholds.Value(metric, 0));
                cells[3].Value = SfpModule.FormatThreshold(metric, module.Thresholds.Value(metric, 1));
                cells[4].Value = SfpModule.FormatThreshold(metric, module.Thresholds.Value(metric, 2));
                cells[5].Value = SfpModule.FormatThreshold(metric, module.Thresholds.Value(metric, 3));
                cells[6].Value = StatusText(status);

                Color color = StatusColor(status);
                cells[6].Style.ForeColor = color;
                cells[6].Style.BackColor = StatusBackColor(status);
                cells[1].Style.ForeColor = status == DdmStatus.Normal ? Color.Black : color;
            }

            UpdateAttenuation(module);

            dgvDdm.CurrentCell = null;
            dgvDdm.ClearSelection();
        }

        /// <summary>链路衰减 = RX 光功率(dBm) − TX 光功率(dBm)，显示负值表示光功率损耗</summary>
        private void UpdateAttenuation(SfpModule module)
        {
            DataGridViewRow row = dgvDdm.Rows[dgvDdm.Rows.Count - 1];
            DataGridViewCell valueCell = row.Cells[1];
            DataGridViewCell statusCell = row.Cells[6];

            bool hasOpticalPower = module.TxPower.Raw > 0 && module.RxPower.Raw > 0
                && module.TxPower.Value > 0 && module.RxPower.Value > 0;

            if (hasOpticalPower)
            {
                double attenuation = 10.0 * Math.Log10(module.RxPower.Value / module.TxPower.Value);
                valueCell.Value = attenuation.ToString("F2") + " dB";
                valueCell.Style.ForeColor = Color.Black;
                statusCell.Value = "—";
            }
            else
            {
                valueCell.Value = "--";
                valueCell.Style.ForeColor = Color.Black;
                statusCell.Value = "无光";
            }

            statusCell.Style.ForeColor = ColorMuted;
            statusCell.Style.BackColor = Color.FromArgb(245, 245, 245);
        }

        private void UpdateStatusLabels(SfpModule module)
        {
            lblTxFault.Text = "TX_FAULT：" + (module.TxFault ? "故障" : "正常");
            lblTxFault.ForeColor = module.TxFault ? ColorAlarm : ColorNormal;

            lblRxLos.Text = "RX_LOS：" + (module.RxLos ? "无光" : "有信号");
            lblRxLos.ForeColor = module.RxLos ? ColorAlarm : ColorNormal;

            lblReady.Text = "数据就绪：" + (module.DataReady ? "是" : "否");
            lblReady.ForeColor = module.DataReady ? ColorNormal : ColorWarning;

            lblTxDisable.Text = "TX 使能：" + (module.SoftTxDisable ? "已禁用" : "开启");
            lblTxDisable.ForeColor = module.SoftTxDisable ? ColorWarning : ColorNormal;
        }

        private void UpdateInfoGrid(SfpModule module)
        {
            dgvInfo.SuspendLayout();
            dgvInfo.Rows.Clear();

            AddInfo("模块类型", module.IdentifierText);
            AddInfo("连接器类型", module.ConnectorText);
            AddInfo("传输协议", module.ComplianceText);
            AddInfo("传输速率", module.BitRateGbps.ToString("F1") + " Gbps");
            AddInfo("波长", module.WavelengthNm + " nm");
            AddInfo("传输距离", module.DistanceText);
            AddInfo("编码方式", module.EncodingText);
            AddInfo("厂商名称", module.Vendor);
            AddInfo("厂商 OUI", module.Oui);
            AddInfo("产品型号", module.PartNumber);
            AddInfo("产品版本", module.Revision);
            AddInfo("序列号", module.SerialNumber);
            AddInfo("生产日期", module.DateCode);
            AddInfo("DDM 监控", module.DdmImplemented ? "已实现" : "未实现");
            AddInfo("校准方式", module.InternallyCalibrated
                ? "内部校准"
                : (module.ExternallyCalibrated ? "外部校准（使用 A2h 标定系数）" : "未声明"));
            AddInfo("RX 功率测量", module.AverageRxPower ? "平均光功率" : "OMA");
            AddInfo("增强功能", module.EnhancedOptionsText);
            AddInfo("SFF-8472 版本", module.SffVersionText);
            AddInfo("A0h 校验（0~62）", module.CcBaseOk ? "通过" : "失败", !module.CcBaseOk);
            AddInfo("A0h 校验（64~94）", module.CcExtOk ? "通过" : "失败", !module.CcExtOk);
            AddInfo("A2h 校验（0~94）", module.A2ChecksumOk ? "通过" : "失败", !module.A2ChecksumOk);

            dgvInfo.ResumeLayout();
            dgvInfo.CurrentCell = null;
            dgvInfo.ClearSelection();
        }

        private void AddInfo(string key, string value)
        {
            AddInfo(key, value, false);
        }

        private void AddInfo(string key, string value, bool isBad)
        {
            int index = dgvInfo.Rows.Add(key, value);
            dgvInfo.Rows[index].Cells[0].Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgvInfo.Rows[index].Cells[0].Style.Font = new Font(Font, FontStyle.Bold);
            dgvInfo.Rows[index].Cells[1].Style.ForeColor = isBad ? ColorAlarm : Color.FromArgb(40, 40, 40);
        }

        private void UpdateRawText(SfpModule module)
        {
            lblA0.Text = "A0h 基本信息（I2C 0x50）　校验："
                + (module.CcBaseOk && module.CcExtOk ? "通过" : "失败");
            lblA0.ForeColor = module.CcBaseOk && module.CcExtOk ? ColorNormal : ColorAlarm;

            lblA2.Text = "A2h 数字诊断 DDM（I2C 0x51）　校验："
                + (module.A2ChecksumOk ? "通过" : "失败");
            lblA2.ForeColor = module.A2ChecksumOk ? ColorNormal : ColorAlarm;

            SetHexText(txtA0, HexDump(module.A0, 128));
            SetHexText(txtA2, HexDump(module.A2, 128));
        }

        private static void SetHexText(RichTextBox box, string text)
        {
            if (box.Text == text) return;
            int start = box.SelectionStart;
            box.Text = text;
            if (start > 0 && start < box.TextLength)
            {
                box.SelectionStart = start;
                box.ScrollToCaret();
            }
        }

        private static string HexDump(byte[] data, int length)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < length; i += 16)
            {
                sb.Append(i.ToString("X4")).Append("  ");
                for (int j = 0; j < 16 && i + j < length; j++)
                    sb.Append(data[i + j].ToString("X2")).Append(' ');
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void SetState(string text, Color color)
        {
            lblState.Text = text;
            lblState.ForeColor = color;
        }

        private static string StatusText(DdmStatus status)
        {
            switch (status)
            {
                case DdmStatus.Normal: return "正常";
                case DdmStatus.HighAlarm: return "高告警-(故障)";
                case DdmStatus.LowAlarm: return "低告警-(故障)";
                case DdmStatus.HighWarning: return "高警告-(报警)";
                case DdmStatus.LowWarning: return "低警告-(报警)";
                case DdmStatus.NoSignal: return "无信号";
                default: return "无效";
            }
        }

        private static Color StatusColor(DdmStatus status)
        {
            switch (status)
            {
                case DdmStatus.Normal: return ColorNormal;
                case DdmStatus.HighAlarm:
                case DdmStatus.LowAlarm: return ColorAlarm;
                case DdmStatus.HighWarning:
                case DdmStatus.LowWarning: return ColorWarning;
                default: return ColorMuted;
            }
        }

        private static Color StatusBackColor(DdmStatus status)
        {
            switch (status)
            {
                case DdmStatus.Normal: return Color.FromArgb(232, 245, 233);
                case DdmStatus.HighAlarm:
                case DdmStatus.LowAlarm: return Color.FromArgb(255, 235, 238);
                case DdmStatus.HighWarning:
                case DdmStatus.LowWarning: return Color.FromArgb(255, 243, 224);
                default: return Color.FromArgb(245, 245, 245);
            }
        }
    }
}
