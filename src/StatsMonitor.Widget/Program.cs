using System.Runtime.InteropServices;

namespace StatsMonitor.Widget;

internal static class Program
{
    private static readonly Guid ProviderClsid = new("7F3C2B6E-5D14-4A9B-9E21-3B8A1C0D4F52");

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid rclsid, [MarshalAs(UnmanagedType.IUnknown)] object pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    [STAThread]
    private static void Main(string[] args)
    {
        if (!args.Contains("-RegisterProcessAsComServer")) return;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var clsid = ProviderClsid;
        int hr = CoRegisterClassObject(ref clsid, new ProviderFactory(), 0x4 /* CLSCTX_LOCAL_SERVER */, 0x1 /* REGCLS_MULTIPLEUSE */, out var cookie);
        if (hr != 0) return;

        // The widget host keeps this process alive; exit once it stops using it.
        new ManualResetEvent(false).WaitOne();
        CoRevokeClassObject(cookie);
    }

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int LockServer(bool fLock);
    }

    [ComVisible(true)]
    private sealed class ProviderFactory : IClassFactory
    {
        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
            var provider = new WidgetProvider();
            ppvObject = WinRT.MarshalInspectable<object>.FromManaged(provider);
            return 0;
        }

        public int LockServer(bool fLock) => 0;
    }
}
