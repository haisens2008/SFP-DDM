// SFF-8472（SFP/SFP+）EEPROM 与 DDM 数字诊断数据解析
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SfpDdm
{
    internal enum DdmStatus
    {
        Normal,
        HighWarning,
        LowWarning,
        HighAlarm,
        LowAlarm,
        NoSignal,
        Invalid
    }

    internal struct DdmValue
    {
        public ushort Raw;
        public double Value;
        public bool Valid;
    }

    /// <summary>告警 / 警告阈值，A2h 0~39 字节，每种参数 4 个阈值（高告警、低告警、高警告、低警告）</summary>
    internal sealed class DdmThresholds
    {
        public readonly ushort[] Raw = new ushort[20];

        public void Parse(byte[] a2)
        {
            for (int i = 0; i < 20; i++)
                Raw[i] = (ushort)((a2[i * 2] << 8) | a2[i * 2 + 1]);
        }

        public double Value(int metric, int kind)
        {
            return SfpModule.ConvertMetric(metric, Raw[metric * 4 + kind]);
        }
    }

    internal sealed class SfpModule
    {
        public const int MetricTemp = 0;
        public const int MetricVoltage = 1;
        public const int MetricBias = 2;
        public const int MetricTxPower = 3;
        public const int MetricRxPower = 4;

        public byte[] A0 = new byte[128];
        public byte[] A2 = new byte[128];

        public bool CcBaseOk;
        public bool CcExtOk;
        public bool A2ChecksumOk;

        public string IdentifierText = "";
        public string ConnectorText = "";
        public string EncodingText = "";
        public string ComplianceText = "";
        public string DistanceText = "";
        public string Vendor = "";
        public string Oui = "";
        public string PartNumber = "";
        public string Revision = "";
        public string SerialNumber = "";
        public string DateCode = "";
        public int WavelengthNm;
        public double BitRateGbps;

        public bool DdmImplemented;
        public bool InternallyCalibrated;
        public bool ExternallyCalibrated;
        public bool AverageRxPower;
        public string SffVersionText = "";
        public string EnhancedOptionsText = "";

        public DdmThresholds Thresholds = new DdmThresholds();
        public DdmValue Temperature;
        public DdmValue Voltage;
        public DdmValue Bias;
        public DdmValue TxPower;
        public DdmValue RxPower;

        public byte StatusControl;
        public bool DataReady;
        public bool TxFault;
        public bool RxLos;
        public bool SoftTxDisable;

        public static SfpModule Parse(byte[] a0, byte[] a2)
        {
            var m = new SfpModule();
            m.A0 = a0;
            m.A2 = a2;

            m.CcBaseOk = Checksum(a0, 0, 62, 63);
            m.CcExtOk = Checksum(a0, 64, 94, 95);
            m.A2ChecksumOk = Checksum(a2, 0, 94, 95);

            // ---- A0h 基本信息（SFF-8472 Table 9-4 / 9-5）----
            m.IdentifierText = IdentifierName(a0[0]);
            m.ConnectorText = ConnectorName(a0[2]);
            m.ComplianceText = ComplianceTextOf(a0[3], a0[7]);
            m.EncodingText = EncodingName(a0[11]);
            m.BitRateGbps = a0[12] * 0.1;   // 单位 100 MBd，换算为 Gbps
            m.DistanceText = DistanceTextOf(a0);
            m.Vendor = Ascii(a0, 20, 16);
            m.Oui = string.Format("{0:X2}-{1:X2}-{2:X2}", a0[37], a0[38], a0[39]);
            m.PartNumber = Ascii(a0, 40, 16);
            m.Revision = Ascii(a0, 56, 4);
            m.WavelengthNm = (a0[60] << 8) | a0[61];
            m.SerialNumber = Ascii(a0, 68, 16);
            m.DateCode = FormatDate(Ascii(a0, 84, 8));

            byte diag = a0[92];
            m.DdmImplemented = (diag & 0x40) != 0;
            m.InternallyCalibrated = (diag & 0x20) != 0;
            m.ExternallyCalibrated = (diag & 0x10) != 0;
            m.AverageRxPower = (diag & 0x08) != 0;
            m.EnhancedOptionsText = EnhancedOptions(a0[93]);
            m.SffVersionText = SffVersion(a0[94]);

            // ---- A2h DDM 数据 ----
            m.Thresholds.Parse(a2);
            m.Temperature = ValueOf(a2, 96, MetricTemp);
            m.Voltage = ValueOf(a2, 98, MetricVoltage);
            m.Bias = ValueOf(a2, 100, MetricBias);
            m.TxPower = ValueOf(a2, 102, MetricTxPower);
            m.RxPower = ValueOf(a2, 104, MetricRxPower);

            // 未声明内部校准时，按外部校准系数换算
            if (!m.InternallyCalibrated && m.ExternallyCalibrated)
                ApplyExternalCalibration(m, a2);

            // A2h 110 字节：状态 / 控制位
            m.StatusControl = a2[110];
            m.DataReady = (m.StatusControl & 0x80) == 0;      // Data_Ready_Bar 取反
            m.SoftTxDisable = (m.StatusControl & 0x40) != 0;
            m.TxFault = (m.StatusControl & 0x04) != 0;
            m.RxLos = (m.StatusControl & 0x02) != 0;

            return m;
        }

        /// <summary>原始 ADC 值按 SFF-8472 内部校准比例换算</summary>
        public static double ConvertMetric(int metric, ushort raw)
        {
            switch (metric)
            {
                case MetricTemp: return (short)raw / 256.0;     // 1/256 °C，有符号
                case MetricVoltage: return raw * 0.0001;         // 100 µV
                case MetricBias: return raw * 0.002;             // 2 µA → mA
                default: return raw * 0.0001;                    // 0.1 µW → mW
            }
        }

        private static DdmValue ValueOf(byte[] a2, int offset, int metric)
        {
            ushort raw = (ushort)((a2[offset] << 8) | a2[offset + 1]);
            return new DdmValue { Raw = raw, Value = ConvertMetric(metric, raw), Valid = true };
        }

        /// <summary>
        /// 外部校准（SFF-8472 §9.3）：
        /// 斜率（76/80/84/88 字节）为无符号 16 位定点数，LSB = 1/256，是无量纲增益，
        /// 因此公式为：实测值 = (斜率 / 256) × 内部校准值 + 偏移；
        /// 偏移（78/82/86/90 字节）为有符号 16 位，LSB 分别为 2 µA / 0.1 µW / 1/256 °C / 100 µV。
        /// RX 光功率为 4 阶多项式，输入 X 为内部校准后的 mW 值，系数为 32 位 IEEE 浮点数，结果单位 mW。
        /// </summary>
        private static void ApplyExternalCalibration(SfpModule m, byte[] a2)
        {
            double tSlope = U16(a2, 84) / 256.0;
            double tOffset = (short)U16(a2, 86) / 256.0;
            SetValue(ref m.Temperature,
                tSlope * ConvertMetric(MetricTemp, m.Temperature.Raw) + tOffset);

            double vSlope = U16(a2, 88) / 256.0;
            double vOffset = (short)U16(a2, 90) * 0.0001;
            SetValue(ref m.Voltage,
                Math.Max(0.0, vSlope * ConvertMetric(MetricVoltage, m.Voltage.Raw) + vOffset));

            double iSlope = U16(a2, 76) / 256.0;
            double iOffset = (short)U16(a2, 78) * 0.002;
            SetValue(ref m.Bias,
                Math.Max(0.0, iSlope * ConvertMetric(MetricBias, m.Bias.Raw) + iOffset));

            double pSlope = U16(a2, 80) / 256.0;
            double pOffset = (short)U16(a2, 82) * 0.0001;
            SetValue(ref m.TxPower,
                Math.Max(0.0, pSlope * ConvertMetric(MetricTxPower, m.TxPower.Raw) + pOffset));

            double c4 = BigEndianFloat(a2, 56);
            double c3 = BigEndianFloat(a2, 60);
            double c2 = BigEndianFloat(a2, 64);
            double c1 = BigEndianFloat(a2, 68);
            double c0 = BigEndianFloat(a2, 72);
            double x = ConvertMetric(MetricRxPower, m.RxPower.Raw);   // 内部校准值，单位 mW
            double rx = c4 * x * x * x * x + c3 * x * x * x + c2 * x * x + c1 * x + c0;
            SetValue(ref m.RxPower,
                (double.IsNaN(rx) || double.IsInfinity(rx) || rx < 0) ? 0 : rx);
        }

        private static void SetValue(ref DdmValue value, double newValue)
        {
            value.Value = newValue;
        }

        /// <summary>比较实时值与阈值，得出当前状态</summary>
        public DdmStatus Evaluate(int metric, DdmValue value)
        {
            if (!value.Valid)
                return DdmStatus.Invalid;

            if ((metric == MetricTxPower || metric == MetricRxPower) && value.Raw == 0)
                return DdmStatus.NoSignal;

            double ha = Thresholds.Value(metric, 0);
            double la = Thresholds.Value(metric, 1);
            double hw = Thresholds.Value(metric, 2);
            double lw = Thresholds.Value(metric, 3);

            // 模块未烧写阈值时不做判断
            if (ha == 0 && la == 0 && hw == 0 && lw == 0)
                return DdmStatus.Normal;

            if (value.Value >= ha) return DdmStatus.HighAlarm;
            if (value.Value <= la) return DdmStatus.LowAlarm;
            if (value.Value >= hw) return DdmStatus.HighWarning;
            if (value.Value <= lw) return DdmStatus.LowWarning;
            return DdmStatus.Normal;
        }

        /// <summary>格式化实时值</summary>
        public static string FormatValue(int metric, DdmValue value)
        {
            switch (metric)
            {
                case MetricTemp:
                    return value.Value.ToString("F2", CultureInfo.InvariantCulture) + " °C";
                case MetricVoltage:
                    return value.Value.ToString("F4", CultureInfo.InvariantCulture) + " V";
                case MetricBias:
                    return value.Value.ToString("F3", CultureInfo.InvariantCulture) + " mA";
                case MetricTxPower:
                    return FormatPower(value.Value, "无输出");
                default:
                    return FormatPower(value.Value, "无光");
            }
        }

        /// <summary>格式化阈值</summary>
        public static string FormatThreshold(int metric, double value)
        {
            switch (metric)
            {
                case MetricTemp:
                    return value.ToString("F2", CultureInfo.InvariantCulture) + " °C";
                case MetricVoltage:
                    return value.ToString("F4", CultureInfo.InvariantCulture) + " V";
                case MetricBias:
                    return value.ToString("F3", CultureInfo.InvariantCulture) + " mA";
                default:
                    return value.ToString("F4", CultureInfo.InvariantCulture) + " mW";
            }
        }

        private static string FormatPower(double mW, string zeroText)
        {
            if (mW <= 0)
                return "0 mW (" + zeroText + ")";
            double dbm = 10.0 * Math.Log10(mW);
            return mW.ToString("F4", CultureInfo.InvariantCulture)
                + " mW (" + dbm.ToString("F2", CultureInfo.InvariantCulture) + " dBm)";
        }

        // ---------- 基础读取与解码辅助 ----------

        private static int U16(byte[] data, int offset)
        {
            return (data[offset] << 8) | data[offset + 1];
        }

        private static bool Checksum(byte[] data, int start, int end, int checkIndex)
        {
            int sum = 0;
            for (int i = start; i <= end; i++) sum += data[i];
            return (sum & 0xFF) == data[checkIndex];
        }

        private static string Ascii(byte[] data, int offset, int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char c = (char)data[offset + i];
                if (c == '\0') break;
                if (c >= 0x20 && c < 0x7F) sb.Append(c);
                else sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        private static string FormatDate(string raw)
        {
            if (raw.Length >= 6)
            {
                int yy, mm, dd;
                if (int.TryParse(raw.Substring(0, 2), out yy) &&
                    int.TryParse(raw.Substring(2, 2), out mm) &&
                    int.TryParse(raw.Substring(4, 2), out dd) &&
                    mm >= 1 && mm <= 12 && dd >= 1 && dd <= 31)
                {
                    return "20" + yy.ToString("D2") + "-" + mm.ToString("D2") + "-" + dd.ToString("D2");
                }
            }
            return raw;
        }

        private static string IdentifierName(byte id)
        {
            switch (id)
            {
                case 0x01: return "GBIC";
                case 0x03: return "SFP / SFP+";
                case 0x0C: return "QSFP";
                case 0x0D: return "QSFP+";
                case 0x11: return "QSFP28";
                default: return "未知 (0x" + id.ToString("X2") + ")";
            }
        }

        private static string ConnectorName(byte code)
        {
            switch (code)
            {
                case 0x00: return "未知";
                case 0x01: return "SC";
                case 0x07: return "LC";
                case 0x0C: return "MPO";
                case 0x21: return "铜缆尾纤";
                case 0x22: return "RJ45";
                default: return "其他 (0x" + code.ToString("X2") + ")";
            }
        }

        private static string EncodingName(byte code)
        {
            switch (code)
            {
                case 0x00: return "未指定";
                case 0x01: return "8B/10B";
                case 0x02: return "4B/5B";
                case 0x03: return "NRZ";
                case 0x04: return "Manchester";
                case 0x05: return "SONET 扰码";
                case 0x06: return "64B/66B";
                default: return "其他 (0x" + code.ToString("X2") + ")";
            }
        }

        private static string ComplianceTextOf(byte ethernet10G, byte ethernet1G)
        {
            var list = new List<string>();
            if ((ethernet10G & 0x80) != 0) list.Add("10GBASE-ER");
            if ((ethernet10G & 0x40) != 0) list.Add("10GBASE-LRM");
            if ((ethernet10G & 0x20) != 0) list.Add("10GBASE-LR");
            if ((ethernet10G & 0x10) != 0) list.Add("10GBASE-SR");
            if ((ethernet10G & 0x08) != 0) list.Add("10GBASE-LX4");
            if ((ethernet10G & 0x02) != 0) list.Add("10GBASE-SW");
            if ((ethernet10G & 0x01) != 0) list.Add("10GBASE-LW");
            if ((ethernet1G & 0x08) != 0) list.Add("1000BASE-T");
            if ((ethernet1G & 0x04) != 0) list.Add("1000BASE-CX");
            if ((ethernet1G & 0x02) != 0) list.Add("1000BASE-LX");
            if ((ethernet1G & 0x01) != 0) list.Add("1000BASE-SX");
            return list.Count > 0 ? string.Join(" / ", list) : "未声明";
        }

        private static string DistanceTextOf(byte[] a0)
        {
            var list = new List<string>();
            if (a0[14] > 0) list.Add(a0[14] + " km (单模)");
            if (a0[15] > 0) list.Add(a0[15] * 100 + " m (单模)");
            if (a0[16] > 0) list.Add(a0[16] * 10 + " m (OM2)");
            if (a0[17] > 0) list.Add(a0[17] * 10 + " m (OM1)");
            if (a0[18] > 0) list.Add(a0[18] + " m (铜缆)");
            return list.Count > 0 ? string.Join(" / ", list) : "未声明";
        }

        private static string EnhancedOptions(byte value)
        {
            var list = new List<string>();
            if ((value & 0x80) != 0) list.Add("告警标志");
            if ((value & 0x40) != 0) list.Add("软 TX 禁用");
            if ((value & 0x20) != 0) list.Add("软 RS(1) 选择");
            if ((value & 0x10) != 0) list.Add("应用选择");
            if ((value & 0x08) != 0) list.Add("软速率选择");
            if ((value & 0x04) != 0) list.Add("软 RS(0) 选择");
            return list.Count > 0 ? string.Join(" / ", list) : "无";
        }

        private static string SffVersion(byte code)
        {
            switch (code)
            {
                case 0x00: return "Rev 9.0";
                case 0x01: return "Rev 9.3";
                case 0x02: return "Rev 9.5";
                case 0x03: return "Rev 10.4";
                case 0x04: return "Rev 11.0";
                case 0x05: return "Rev 12.0";
                default: return "Rev " + code;
            }
        }

        private static double BigEndianFloat(byte[] data, int offset)
        {
            var tmp = new byte[] { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            float f = BitConverter.ToSingle(tmp, 0);
            return float.IsNaN(f) || float.IsInfinity(f) ? 0.0 : f;
        }
    }
}
