// SFP+ DDM 诊断工具：通过 CH341 USB 转 I2C 适配器读取 SFF-8472 光模块诊断信息
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SfpDdm
{
    /// <summary>
    /// CH341 USB 转 I2C 适配器封装，自动选用 32 位 / 64 位动态库。
    /// 需要系统已安装 CH341 驱动（CH341DLLA64.DLL / CH341DLL.DLL）。
    /// </summary>
    internal static class Ch341I2c
    {
        private static readonly object Sync = new object();
        private static readonly bool Is64Bit = IntPtr.Size == 8;
        private static bool opened;
        private static int openedIndex = -1;
        private static int currentSpeed = 1;

        // ---------- 64 位 DLL ----------
        [DllImport("CH341DLLA64.DLL", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr CH341OpenDevice(uint iIndex);

        [DllImport("CH341DLLA64.DLL", CallingConvention = CallingConvention.StdCall)]
        private static extern void CH341CloseDevice(uint iIndex);

        [DllImport("CH341DLLA64.DLL", CallingConvention = CallingConvention.StdCall)]
        private static extern uint CH341SetStream(uint iIndex, uint iMode);

        [DllImport("CH341DLLA64.DLL", CallingConvention = CallingConvention.StdCall)]
        private static extern bool CH341StreamI2C(uint iIndex, uint iWriteLength, byte[] iWriteBuffer,
            uint iReadLength, [Out] byte[] oReadBuffer);

        [DllImport("CH341DLLA64.DLL", CallingConvention = CallingConvention.StdCall)]
        private static extern uint CH341GetVersion();

        // ---------- 32 位 DLL ----------
        [DllImport("CH341DLL.DLL", EntryPoint = "CH341OpenDevice", CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr CH341OpenDevice32(uint iIndex);

        [DllImport("CH341DLL.DLL", EntryPoint = "CH341CloseDevice", CallingConvention = CallingConvention.StdCall)]
        private static extern void CH341CloseDevice32(uint iIndex);

        [DllImport("CH341DLL.DLL", EntryPoint = "CH341SetStream", CallingConvention = CallingConvention.StdCall)]
        private static extern uint CH341SetStream32(uint iIndex, uint iMode);

        [DllImport("CH341DLL.DLL", EntryPoint = "CH341StreamI2C", CallingConvention = CallingConvention.StdCall)]
        private static extern bool CH341StreamI2C32(uint iIndex, uint iWriteLength, byte[] iWriteBuffer,
            uint iReadLength, [Out] byte[] oReadBuffer);

        [DllImport("CH341DLL.DLL", EntryPoint = "CH341GetVersion", CallingConvention = CallingConvention.StdCall)]
        private static extern uint CH341GetVersion32();

        /// <summary>驱动 DLL 版本号，读取失败时返回 0</summary>
        public static uint DllVersion
        {
            get
            {
                try { return Is64Bit ? CH341GetVersion() : CH341GetVersion32(); }
                catch { return 0; }
            }
        }

        /// <summary>打开指定序号的 CH341 设备并设置 I2C 速率</summary>
        /// <param name="deviceIndex">设备序号，通常为 0</param>
        /// <param name="speedMode">0=20kHz，1=100kHz，2=400kHz</param>
        public static void Open(int deviceIndex, int speedMode)
        {
            lock (Sync)
            {
                if (opened && openedIndex == deviceIndex)
                {
                    currentSpeed = speedMode;
                    SetSpeedInternal(speedMode);
                    return;
                }

                OpenInternal(deviceIndex, speedMode);
            }
        }

        /// <summary>关闭并重新初始化 CH341（保持当前设备序号和 I2C 速率）</summary>
        public static void Reopen()
        {
            lock (Sync)
            {
                if (openedIndex < 0) return;
                OpenInternal(openedIndex, currentSpeed);
            }
        }

        private static void OpenInternal(int deviceIndex, int speedMode)
        {
            CloseInternal();

            IntPtr handle;
            try
            {
                handle = Is64Bit
                    ? CH341OpenDevice((uint)deviceIndex)
                    : CH341OpenDevice32((uint)deviceIndex);
            }
            catch (DllNotFoundException)
            {
                throw new InvalidOperationException("未找到 CH341 动态库，请先安装 CH341 USB 驱动");
            }
            catch (BadImageFormatException)
            {
                throw new InvalidOperationException("CH341 动态库位数与程序不匹配，请使用对应的驱动版本");
            }

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                throw new InvalidOperationException(
                    "未检测到 CH341 设备（设备序号 " + deviceIndex + "），请检查 USB 连接与驱动状态");

            opened = true;
            openedIndex = deviceIndex;
            currentSpeed = speedMode;
            SetSpeedInternal(speedMode);
        }

        public static void Close()
        {
            lock (Sync) CloseInternal();
        }

        /// <summary>设置 I2C 速率：0=20kHz，1=100kHz，2=400kHz</summary>
        public static void SetSpeed(int speedMode)
        {
            lock (Sync) SetSpeedInternal(speedMode);
        }

        private static void SetSpeedInternal(int speedMode)
        {
            if (!opened) return;
            if (Is64Bit) CH341SetStream((uint)openedIndex, (uint)speedMode);
            else CH341SetStream32((uint)openedIndex, (uint)speedMode);
        }

        private static void CloseInternal()
        {
            if (!opened) return;
            try
            {
                if (Is64Bit) CH341CloseDevice((uint)openedIndex);
                else CH341CloseDevice32((uint)openedIndex);
            }
            catch { }
            opened = false;
            openedIndex = -1;
        }

        /// <summary>从 I2C 从机读取数据（自动完成写偏移地址 + 重复起始位 + 读取）</summary>
        /// <param name="deviceAddress">8 位写地址，如 A0h 存储器为 0xA0</param>
        /// <param name="startOffset">起始寄存器地址</param>
        /// <param name="length">读取字节数</param>
        public static byte[] Read(byte deviceAddress, byte startOffset, int length)
        {
            lock (Sync)
            {
                if (!opened)
                    throw new InvalidOperationException("CH341 设备尚未打开");

                int index = openedIndex;
                int speed = currentSpeed;
                Exception last = null;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0)
                    {
                        // 读取失败后重新初始化 CH341，再发起下一次读取
                        try
                        {
                            OpenInternal(index, speed);
                        }
                        catch (Exception ex)
                        {
                            last = ex;
                            Thread.Sleep(50);
                            continue;
                        }
                    }

                    byte[] write = new byte[] { deviceAddress, startOffset };
                    byte[] read = new byte[length];
                    try
                    {
                        bool ok = Is64Bit
                            ? CH341StreamI2C((uint)index, 2, write, (uint)length, read)
                            : CH341StreamI2C32((uint)index, 2, write, (uint)length, read);
                        if (ok) return read;
                        last = new IOException("CH341 I2C 传输失败");
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                    }
                    Thread.Sleep(20);
                }

                throw new IOException("I2C 读取失败（地址 0x" + deviceAddress.ToString("X2") + "）：" + last.Message);
            }
        }
    }
}
