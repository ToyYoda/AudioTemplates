using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AudioTemplates
{
    enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
    }

    // Only the parts of PROPVARIANT we read (vt + pointer); padded to the x64 size.
    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
        [FieldOffset(16)] public IntPtr padding;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    // Undocumented interface Windows itself uses to change the default endpoint.
    // Only SetDefaultEndpoint is called; the other entries just keep the vtable order.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility();
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigClientComObject { }

    class DeviceInfo
    {
        public string Id;
        public string Name;
    }

    static class AudioDevices
    {
        const int DeviceStateActive = 1;
        const ushort VtLpwstr = 31;
        static readonly Guid FriendlyNameFmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pv);

        static IMMDeviceEnumerator CreateEnumerator()
        {
            return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }

        public static List<DeviceInfo> List(EDataFlow flow)
        {
            var result = new List<DeviceInfo>();
            var enumerator = CreateEnumerator();
            try
            {
                IMMDeviceCollection collection;
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out collection));
                try
                {
                    int count;
                    Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                    for (int i = 0; i < count; i++)
                    {
                        IMMDevice device;
                        if (collection.Item(i, out device) != 0) continue;
                        try
                        {
                            string id;
                            if (device.GetId(out id) != 0) continue;
                            result.Add(new DeviceInfo { Id = id, Name = GetFriendlyName(device) ?? id });
                        }
                        finally { Marshal.ReleaseComObject(device); }
                    }
                }
                finally { Marshal.ReleaseComObject(collection); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return result;
        }

        public static string GetDefaultId(EDataFlow flow)
        {
            var enumerator = CreateEnumerator();
            try
            {
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out device) != 0) return null;
                try
                {
                    string id;
                    return device.GetId(out id) == 0 ? id : null;
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }

        public static void SetDefault(string id)
        {
            var enumerator = CreateEnumerator();
            try
            {
                IMMDevice device;
                if (enumerator.GetDevice(id, out device) != 0 || device == null)
                    throw new InvalidOperationException("device not found");
                int state;
                device.GetState(out state);
                Marshal.ReleaseComObject(device);
                if (state != DeviceStateActive)
                    throw new InvalidOperationException("device is not connected or is disabled");
            }
            finally { Marshal.ReleaseComObject(enumerator); }

            var policy = (IPolicyConfig)new PolicyConfigClientComObject();
            try
            {
                foreach (ERole role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                    Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
            }
            finally { Marshal.ReleaseComObject(policy); }
        }

        static string GetFriendlyName(IMMDevice device)
        {
            IPropertyStore store;
            if (device.OpenPropertyStore(0 /* STGM_READ */, out store) != 0) return null;
            try
            {
                var key = new PropertyKey { fmtid = FriendlyNameFmtid, pid = 14 };
                PropVariant value;
                if (store.GetValue(ref key, out value) != 0) return null;
                try
                {
                    return value.vt == VtLpwstr ? Marshal.PtrToStringUni(value.pointerValue) : null;
                }
                finally { PropVariantClear(ref value); }
            }
            finally { Marshal.ReleaseComObject(store); }
        }
    }
}
