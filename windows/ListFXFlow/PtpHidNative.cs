using System.Runtime.InteropServices;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace ListFXFlow;

/// All native HID parsing occurs in the isolated worker/probe, never a mouse hook.
internal static class PtpHidNative {
    internal const int Success=0x00110000;
    [StructLayout(LayoutKind.Explicit,Size=64)] internal struct Caps {
        [FieldOffset(0)]internal ushort Usage;[FieldOffset(2)]internal ushort UsagePage;
        [FieldOffset(4)]internal ushort InputLength;[FieldOffset(46)]internal ushort ButtonCount;
        [FieldOffset(48)]internal ushort ValueCount;[FieldOffset(50)]internal ushort DataCount;
    }
    [StructLayout(LayoutKind.Explicit,Size=72)] internal struct ValueCaps {
        [FieldOffset(0)]internal ushort Page;[FieldOffset(2)]internal byte Report;
        [FieldOffset(6)]internal ushort Link;[FieldOffset(12)]internal byte Range;
        [FieldOffset(18)]internal ushort Bits;
        [FieldOffset(40)]internal int Minimum;[FieldOffset(44)]internal int Maximum;
        [FieldOffset(56)]internal ushort Usage;[FieldOffset(58)]internal ushort UsageMax;
        [FieldOffset(68)]internal ushort Index;[FieldOffset(70)]internal ushort IndexMax;
    }
    [StructLayout(LayoutKind.Explicit,Size=72)] internal struct ButtonCaps {
        [FieldOffset(0)]internal ushort Page;[FieldOffset(2)]internal byte Report;
        [FieldOffset(6)]internal ushort Link;[FieldOffset(12)]internal byte Range;
        [FieldOffset(56)]internal ushort Usage;[FieldOffset(58)]internal ushort UsageMax;
        [FieldOffset(68)]internal ushort Index;[FieldOffset(70)]internal ushort IndexMax;
    }
    [StructLayout(LayoutKind.Explicit,Size=8)] internal struct HidData {
        [FieldOffset(0)]internal ushort Index;[FieldOffset(4)]internal uint Raw;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterface {
        internal uint Size;internal Guid Class;internal uint Flags;internal nint Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo {
        internal uint Size;internal Guid Class;internal uint Instance;internal nint Reserved;
    }
    [DllImport("hid.dll")]internal static extern int HidP_GetCaps(nint preparsed,out Caps caps);
    [DllImport("hid.dll")]internal static extern int HidP_GetValueCaps(int type,[Out]ValueCaps[] caps,ref ushort count,nint preparsed);
    [DllImport("hid.dll")]internal static extern int HidP_GetButtonCaps(int type,[Out]ButtonCaps[] caps,ref ushort count,nint preparsed);
    [DllImport("hid.dll")]internal static extern int HidP_GetData(int type,[Out]HidData[] data,ref uint count,nint preparsed,byte[] report,uint length);
    [DllImport("hid.dll")]private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]private static extern bool HidD_GetPreparsedData(SafeFileHandle device,out nint preparsed);
    [DllImport("hid.dll")][return:MarshalAs(UnmanagedType.U1)]private static extern bool HidD_FreePreparsedData(nint preparsed);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern SafeFileHandle CreateFile(string path,uint access,uint share,nint security,uint disposition,uint flags,nint template);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeviceIoControl(SafeFileHandle file,uint code,nint input,uint inputLength,[Out]byte[] output,uint outputLength,out uint returned,nint overlapped);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint SetupDiGetClassDevs(ref Guid guid,nint enumerator,nint parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetupDiEnumDeviceInterfaces(nint set,nint device,ref Guid guid,uint index,ref DeviceInterface detail);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetupDiGetDeviceInterfaceDetail(nint set,ref DeviceInterface item,nint detail,uint bytes,out uint required,ref DeviceInfo device);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetupDiGetDeviceRegistryProperty(nint set,ref DeviceInfo device,uint property,out uint type,[Out]byte[] buffer,uint size,out uint required);
    [DllImport("setupapi.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    internal sealed record Device(string Path,string[] HardwareIds);
    internal static Device[] Devices(Guid guid) {
        if(!OperatingSystem.IsWindows())return [];
        nint set=SetupDiGetClassDevs(ref guid,0,0,0x12);
        if(set==-1)throw new Win32Exception(Marshal.GetLastWin32Error());
        var devices=new List<Device>();
        try {
            for(uint i=0;i<64;i++) {
                var item=new DeviceInterface {Size=(uint)Marshal.SizeOf<DeviceInterface>()};
                if(!SetupDiEnumDeviceInterfaces(set,0,ref guid,i,ref item)) {
                    int error=Marshal.GetLastWin32Error();if(error==259)break;throw new Win32Exception(error);
                }
                var info=new DeviceInfo {Size=(uint)Marshal.SizeOf<DeviceInfo>()};
                SetupDiGetDeviceInterfaceDetail(set,ref item,0,0,out uint required,ref info);
                if(required is <8 or >65536)throw new InvalidDataException("Invalid device interface size");
                nint detail=Marshal.AllocHGlobal((int)required);
                try {
                    Marshal.WriteInt32(detail,IntPtr.Size==8 ? 8:6);
                    if(!SetupDiGetDeviceInterfaceDetail(set,ref item,detail,required,out _,ref info))throw new Win32Exception(Marshal.GetLastWin32Error());
                    string path=Marshal.PtrToStringUni(detail+4)??throw new InvalidDataException("Missing device path");
                    byte[] ids=new byte[8192];string[] hardware=[];
                    if(SetupDiGetDeviceRegistryProperty(set,ref info,1,out uint kind,ids,(uint)ids.Length,out uint used) && kind==7 && used<=ids.Length)
                        hardware=System.Text.Encoding.Unicode.GetString(ids,0,(int)used).Split('\0',StringSplitOptions.RemoveEmptyEntries);
                    devices.Add(new(path,hardware));
                } finally {Marshal.FreeHGlobal(detail);}
            }
        } finally {SetupDiDestroyDeviceInfoList(set);}
        return devices.ToArray();
    }
    internal static object[] PhysicalInventory() {
        HidD_GetHidGuid(out var guid);var devices=new List<object>();
        foreach(var device in Devices(guid)) {
            using var file=CreateFile(device.Path,0,3,0,3,0,0);
            if(file.IsInvalid || !HidD_GetPreparsedData(file,out var p))continue;
            try { if(HidP_GetCaps(p,out var caps)==Success && caps.UsagePage==13 && caps.Usage==5)
                devices.Add(new {UsagePage=13,Usage=5,InputReportLength=caps.InputLength,device.HardwareIds}); }
            finally {HidD_FreePreparsedData(p);}
        }
        return devices.ToArray();
    }
    internal static byte[] Ioctl(SafeFileHandle file,uint code,int capacity,bool allowEmpty=false) {
        byte[] buffer=new byte[capacity];
        if(!DeviceIoControl(file,code,0,0,buffer,(uint)buffer.Length,out uint returned,0)) {
            int error=Marshal.GetLastWin32Error();if(allowEmpty && error==259)return [];throw new Win32Exception(error);
        }
        if(returned>buffer.Length)throw new InvalidDataException("Driver returned oversized data");
        return buffer.AsSpan(0,(int)returned).ToArray();
    }
}

internal sealed class PtpHidDecoder : IDisposable {
    private nint preparsed;
    private readonly PtpHidNative.HidData[] data;
    private readonly Dictionary<byte,Field[]> reports;
    internal sealed record Field(byte Report,ushort Link,ushort Page,ushort Usage,ushort Index,int Minimum,int Maximum,int Bits);
    internal PtpHidDecoder(byte[] descriptor) {
        if(descriptor.Length is 0 or >PtpHidProtocol.DescriptorLimit)throw new InvalidDataException("Invalid preparsed HID descriptor size");
        preparsed=Marshal.AllocHGlobal(descriptor.Length);Marshal.Copy(descriptor,0,preparsed,descriptor.Length);
        try {
            if(PtpHidNative.HidP_GetCaps(preparsed,out var caps)!=PtpHidNative.Success || caps.UsagePage!=13 || caps.Usage!=5 || caps.DataCount is 0 or >2048)
                throw new NotSupportedException("Driver did not provide a supported PTP collection");
            var values=new PtpHidNative.ValueCaps[caps.ValueCount];ushort count=caps.ValueCount;
            if(PtpHidNative.HidP_GetValueCaps(0,values,ref count,preparsed)!=PtpHidNative.Success)throw new InvalidDataException("Cannot read PTP value capabilities");
            var fields=new List<Field>();
            foreach(var v in values.Take(count))Add(fields,v.Report,v.Link,v.Page,v.Usage,v.UsageMax,v.Index,v.IndexMax,v.Range!=0,v.Minimum,v.Maximum,v.Bits);
            var buttons=new PtpHidNative.ButtonCaps[caps.ButtonCount];count=caps.ButtonCount;
            if(PtpHidNative.HidP_GetButtonCaps(0,buttons,ref count,preparsed)!=PtpHidNative.Success)throw new InvalidDataException("Cannot read PTP button capabilities");
            foreach(var b in buttons.Take(count))Add(fields,b.Report,b.Link,b.Page,b.Usage,b.UsageMax,b.Index,b.IndexMax,b.Range!=0,0,1,1);
            reports=fields.GroupBy(f=>f.Report).ToDictionary(g=>g.Key,g=>g.ToArray());data=new PtpHidNative.HidData[caps.DataCount];
            if(!reports.Values.Any(f=>f.Any(x=>x.Page==13 && x.Usage==0x54)))throw new NotSupportedException("PTP descriptor lacks Contact Count");
        } catch {Dispose();throw;}
    }
    private static void Add(List<Field> fields,byte report,ushort link,ushort page,ushort usage,ushort usageMax,ushort index,ushort indexMax,bool range,int min,int max,int bits) {
        int length=range ? usageMax-usage+1:1;
        if(length is <=0 or >1024 || range && indexMax-index+1!=length || fields.Count+length>2048)throw new InvalidDataException("Unsupported HID capability range");
        for(int i=0;i<length;i++)fields.Add(new(report,link,page,(ushort)(usage+i),(ushort)(index+i),min,max,bits));
    }
    internal PtpHidFragment? Decode(byte[] report) {
        ObjectDisposedException.ThrowIf(preparsed==0,this);
        if(report.Length==0 || !reports.TryGetValue(report[0],out var fields))return null;
        uint count=(uint)data.Length;
        if(PtpHidNative.HidP_GetData(0,data,ref count,preparsed,report,(uint)report.Length)!=PtpHidNative.Success || count>data.Length)throw new InvalidDataException("Cannot parse raw PTP report");
        var values=data.Take((int)count).ToDictionary(d=>d.Index,d=>d.Raw);
        return DecodeData(fields,values);
    }
    internal static PtpHidFragment? DecodeData(Field[] fields,IReadOnlyDictionary<ushort,uint> values) {
        uint Value(Field f)=>values.GetValueOrDefault(f.Index);
        Field? Top(ushort page,ushort usage)=>fields.FirstOrDefault(f=>f.Page==page && f.Usage==usage);
        var contactCount=Top(13,0x54);var time=Top(13,0x56);
        if(contactCount==null || time==null)return null;
        if(!values.ContainsKey(contactCount.Index) || !values.ContainsKey(time.Index))throw new InvalidDataException("PTP report lacks frame metadata");
        var contacts=new List<PtpHidContact>();
        foreach(var group in fields.GroupBy(f=>f.Link).OrderBy(g=>g.Key)) {
            Field? Get(ushort page,ushort usage)=>group.FirstOrDefault(f=>f.Page==page && f.Usage==usage);
            var id=Get(13,0x51);if(id==null)continue;
            var tip=Get(13,0x42);var confidence=Get(13,0x47);var x=Get(1,0x30);var y=Get(1,0x31);
            if(tip==null || confidence==null || x==null || y==null)throw new NotSupportedException("Incomplete PTP finger collection");
            bool touching=Value(tip)!=0,confident=Value(confidence)!=0;
            if(touching && confident && (!values.ContainsKey(id.Index) || !values.ContainsKey(x.Index) || !values.ContainsKey(y.Index)))
                throw new InvalidDataException("Active PTP contact lacks identity or coordinates");
            contacts.Add(new(Value(id),touching,confident,touching && confident ? Coordinate(Value(x),x):0,touching && confident ? Coordinate(Value(y),y):0));
        }
        if(contacts.Count==0)return null;
        return new(checked((ushort)Value(time)),checked((int)Value(contactCount)),contacts.ToArray());
    }
    internal static double Coordinate(uint raw,Field field) {
        if(field.Maximum<=field.Minimum || field.Bits is <=0 or >32)throw new InvalidDataException("Invalid PTP axis range");
        long value=raw;
        if(field.Minimum<0) { int shift=32-field.Bits;value=(int)(raw<<shift)>>shift; }
        double result=(value-(double)field.Minimum)/(field.Maximum-(double)field.Minimum);
        if(result is <0 or >1)throw new InvalidDataException("PTP axis lies outside its logical range");return result;
    }
    public void Dispose() { if(preparsed!=0) {Marshal.FreeHGlobal(preparsed);preparsed=0;} }
}
