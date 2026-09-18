using System.Runtime.InteropServices;

namespace NewCosmos.Services.Utilities;

/// <summary>
/// 打印机双面打印辅助类
/// 通过 Windows Print API 设置打印机 DEVMODE 的 dmDuplex 字段
/// 兼容 Office 和 WPS
/// </summary>
internal static class PrinterDuplexHelper
{
    private const int DM_DUPLEX = 0x1000;

    private const short DMDUP_SIMPLEX = 1;
    private const short DMDUP_VERTICAL = 2;

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool GetPrinter(IntPtr hPrinter, int dwLevel, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool SetPrinter(IntPtr hPrinter, int dwLevel, IntPtr pPrinter, int dwCommand);

    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTER_DEFAULTS
    {
        public IntPtr pDatatype;
        public IntPtr pDevMode;
        public int DesiredAccess;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    /// <summary>
    /// 设置打印机双面打印模式
    /// </summary>
    /// <param name="printerName">打印机名称</param>
    /// <param name="duplex">是否双面打印</param>
    public static void SetPrinterDuplex(string printerName, bool duplex)
    {
        if (string.IsNullOrEmpty(printerName))
            return;

        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pPrinterInfo = IntPtr.Zero;

        try
        {
            var defaults = new PRINTER_DEFAULTS
            {
                pDatatype = IntPtr.Zero,
                pDevMode = IntPtr.Zero,
                DesiredAccess = 0x00000008
            };

            if (!OpenPrinter(printerName, out hPrinter, ref defaults))
                return;

            int bytesNeeded;
            GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out bytesNeeded);

            if (bytesNeeded <= 0)
                return;

            pPrinterInfo = Marshal.AllocHGlobal(bytesNeeded);

            if (!GetPrinter(hPrinter, 2, pPrinterInfo, bytesNeeded, out _))
                return;

            var printerInfo = Marshal.PtrToStructure<PRINTER_INFO_2>(pPrinterInfo);

            if (printerInfo.pDevMode == IntPtr.Zero)
                return;

            var devMode = Marshal.PtrToStructure<DEVMODE>(printerInfo.pDevMode);

            devMode.dmFields |= DM_DUPLEX;
            devMode.dmDuplex = duplex ? DMDUP_VERTICAL : DMDUP_SIMPLEX;

            Marshal.StructureToPtr(devMode, printerInfo.pDevMode, false);

            printerInfo.pSecurityDescriptor = IntPtr.Zero;
            Marshal.StructureToPtr(printerInfo, pPrinterInfo, false);

            SetPrinter(hPrinter, 2, pPrinterInfo, 0);
        }
        catch
        {
        }
        finally
        {
            if (pPrinterInfo != IntPtr.Zero)
                Marshal.FreeHGlobal(pPrinterInfo);

            if (hPrinter != IntPtr.Zero)
                ClosePrinter(hPrinter);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTER_INFO_2
    {
        public IntPtr pServerName;
        public IntPtr pPrinterName;
        public IntPtr pShareName;
        public IntPtr pPortName;
        public IntPtr pDriverName;
        public IntPtr pComment;
        public IntPtr pLocation;
        public IntPtr pDevMode;
        public IntPtr pSepFile;
        public IntPtr pPrintProcessor;
        public IntPtr pDatatype;
        public IntPtr pParameters;
        public IntPtr pSecurityDescriptor;
        public int Attributes;
        public int Priority;
        public int DefaultPriority;
        public int StartTime;
        public int UntilTime;
        public int Status;
        public int cJobs;
        public int AveragePPM;
    }
}
