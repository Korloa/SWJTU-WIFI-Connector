using System.Runtime.InteropServices;
using System.Text;

namespace SwjtuKeeper;

internal static class WifiConnection
{
    private const int CurrentConnection = 7;
    private const int Connected = 1;
    private const int ProfileMode = 0;
    private const int Infrastructure = 1;

    public static string? GetConnectedSsid()
    {
        using var client = new WlanClient();
        foreach (var adapter in client.Adapters)
        {
            if (adapter.State != Connected)
                continue;

            var adapterId = adapter.Id;
            var result = WlanQueryInterface(client.Handle, ref adapterId, CurrentConnection,
                IntPtr.Zero, out _, out var data, out _);
            if (result != 0)
                continue;

            try
            {
                var connection = Marshal.PtrToStructure<ConnectionAttributes>(data);
                var length = (int)Math.Min(connection.Association.Ssid.Length, 32);
                return Encoding.UTF8.GetString(connection.Association.Ssid.Bytes, 0, length);
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }

        return null;
    }

    public static void Connect(string profileName)
    {
        using var client = new WlanClient();
        if (client.Adapters.Length == 0)
            throw new InvalidOperationException("找不到无线网卡");

        var parameters = new ConnectionParameters
        {
            Mode = ProfileMode,
            Profile = profileName,
            BssType = Infrastructure
        };
        var adapterId = client.Adapters[0].Id;
        var result = WlanConnect(client.Handle, ref adapterId, ref parameters, IntPtr.Zero);
        if (result != 0)
            throw new InvalidOperationException($"连接 Wi-Fi 失败：{new System.ComponentModel.Win32Exception((int)result).Message}");
    }

    private sealed class WlanClient : IDisposable
    {
        public IntPtr Handle { get; }
        public Adapter[] Adapters { get; }

        public WlanClient()
        {
            var result = WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
            if (result != 0)
                throw new InvalidOperationException($"无法访问无线网卡：{new System.ComponentModel.Win32Exception((int)result).Message}");
            Handle = handle;

            result = WlanEnumInterfaces(Handle, IntPtr.Zero, out var list);
            if (result != 0)
            {
                Dispose();
                throw new InvalidOperationException($"无法枚举无线网卡：{new System.ComponentModel.Win32Exception((int)result).Message}");
            }

            try
            {
                var count = Marshal.ReadInt32(list);
                Adapters = new Adapter[count];
                const int offset = 8;
                var size = Marshal.SizeOf<Adapter>();
                for (var index = 0; index < count; index++)
                    Adapters[index] = Marshal.PtrToStructure<Adapter>(IntPtr.Add(list, offset + index * size));
            }
            finally
            {
                WlanFreeMemory(list);
            }
        }

        public void Dispose() => WlanCloseHandle(Handle, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Adapter
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AssociationAttributes
    {
        public Ssid Ssid;
        public uint BssType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public uint PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint ReceiveRate;
        public uint TransmitRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        [MarshalAs(UnmanagedType.Bool)] public bool OneXEnabled;
        public uint AuthAlgorithm;
        public uint CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionAttributes
    {
        public int State;
        public int Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public AssociationAttributes Association;
        public SecurityAttributes Security;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionParameters
    {
        public int Mode;
        [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
        public IntPtr Ssid;
        public IntPtr DesiredBssidList;
        public int BssType;
        public int Flags;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opcode,
        IntPtr reserved, out uint dataSize, out IntPtr data, out int opcodeValueType);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanConnect(IntPtr clientHandle, ref Guid interfaceGuid,
        ref ConnectionParameters connectionParameters, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
