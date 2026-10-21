using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ListFXFlow;

internal static class PtpHidTests {
    internal static void Run(Action<bool,string> check,Action<Action,string> reject) {
        check(Marshal.SizeOf<PtpHidNative.Caps>()==64 && Marshal.SizeOf<PtpHidNative.ValueCaps>()==72 && Marshal.SizeOf<PtpHidNative.ButtonCaps>()==72 && Marshal.SizeOf<PtpHidNative.HidData>()==8,"HID parser x64 marshalling matches Windows SDK layouts");
        check((int)Marshal.OffsetOf<PtpHidNative.ValueCaps>("Index")==68 && (int)Marshal.OffsetOf<PtpHidNative.ValueCaps>("Minimum")==40,"native HID logical bounds and data indices use the SDK offsets");
        byte[] meta=new byte[64];void U32(int offset,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(offset),value);
        U32(0,64);U32(4,1);U32(8,3);U32(12,4096);U32(16,512);U32(20,64);U32(24,0x1234);U32(28,0xabcd);U32(36,64);
        BinaryPrimitives.WriteUInt64LittleEndian(meta.AsSpan(56),10000000);
        var caps=PtpHidProtocol.Metadata(meta);check(caps.Validated && caps.InputLength==64 && caps.DescriptorLength==512 && caps.Vendor==0x1234,"driver metadata preserves descriptor size, input report size and source identity");
        U32(8,1);reject(()=>PtpHidProtocol.Metadata(meta),"reader rejects an ABI that does not explicitly declare observe-only input behavior");U32(8,3);
        U32(16,65537);reject(()=>PtpHidProtocol.Metadata(meta),"driver descriptors are bounded before allocation");U32(16,512);
        byte[] report=new byte[35];BinaryPrimitives.WriteUInt32LittleEndian(report,35);BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(4),1);
        BinaryPrimitives.WriteUInt64LittleEndian(report.AsSpan(8),7);BinaryPrimitives.WriteUInt64LittleEndian(report.AsSpan(16),1234);BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(24),3);
        report[32]=5;report[33]=10;report[34]=15;
        var raw=PtpHidProtocol.Report(report);check(raw.Sequence==7 && raw.Counter==1234 && raw.Bytes.SequenceEqual(new byte[]{5,10,15}),"raw report payload is copied without source-byte changes");
        report[28]=1;reject(()=>PtpHidProtocol.Report(report),"unknown report flags are rejected");report[28]=0;
        reject(()=>PtpHidProtocol.Report(report.AsSpan(0,34)),"truncated driver report cannot reach HID parser");
        var gate=new PtpHidStrokeGate();
        check(!gate.Accept(3),"raw observation cannot forward a pre-existing Windows-owned gesture");
        gate.Pressed();check(!gate.Accept(1) && !gate.Accept(2) && gate.Accept(3),"SDK ownership authorizes a new raw three-finger stroke without stealing two-finger input");
        check(gate.Accept(2) && !gate.Accept(3) && gate.Accept(0),"partial lift ends raw output and full lift clears ownership");
        gate.Pressed();gate.Reset();check(!gate.Accept(3),"focus and capture generation resets revoke raw stroke ownership");
        var assembler=new PtpHidFrames();
        var first=new PtpHidContact(80000,true,true,.1,.2);var second=new PtpHidContact(90000,true,true,.4,.5);var third=new PtpHidContact(100000,true,true,.7,.8);
        check(assembler.Push(1,new(65535,3,[first]))==null && assembler.Push(2,new(65535,0,[second]))==null,"hybrid HID fragments do not expose partial contact frames");
        var frame=assembler.Push(3,new(65535,0,[third]))!;
        check(frame.Length==3 && frame[0].Id==0 && frame[2].Id==2 && frame[1].X==.4,"full raw frame normalizes large hardware contact IDs to stable wire slots");
        var moved=assembler.Push(4,new(0,3,[third with {X=.8},first,second]))!;
        check(moved[2].X==.8 && moved[0].X==.1,"scan-time wrap and reordered contact records preserve finger identities");
        var released=assembler.Push(5,new(1,3,[first with {Tip=false},second,third with {Confidence=false}]))!;
        check(released is [{Id:1}],"raw confidence and tip switches distinguish palms and explicit lifts");
        check(assembler.Push(6,new(2,0,[])) is {Length:0},"zero-contact raw frame releases all contacts");
        assembler.Reset();reject(()=>assembler.Push(0,new(0,0,[])),"zero driver report sequence is invalid");
        assembler.Reset();assembler.Push(1,new(1,3,[first]));reject(()=>assembler.Push(3,new(1,0,[second,third])),"missing HID report cancels the incomplete frame");
        assembler.Reset();assembler.Push(1,new(1,3,[first]));reject(()=>assembler.Push(2,new(2,0,[second,third])),"mixed scan times cannot be merged into one raw gesture frame");
        assembler.Reset();reject(()=>assembler.Push(1,new(1,2,[first,first])),"duplicate hardware contacts are rejected before slot allocation");
        assembler.Reset();reject(()=>assembler.Push(1,new(1,1,[first with {X=double.NaN}])),"non-finite raw contact coordinate is rejected");
        var axis=new PtpHidDecoder.Field(1,1,1,0x30,0,-32768,32767,16);
        check(PtpHidDecoder.Coordinate(0x8000,axis)==0 && PtpHidDecoder.Coordinate(0x7fff,axis)==1,"HID signed coordinate axes are sign-extended before normalization");
        reject(()=>PtpHidDecoder.Coordinate(65536,axis with {Minimum=0,Maximum=32767}),"out-of-range device coordinates are not silently clamped");
        var fields=new List<PtpHidDecoder.Field> {new(1,0,13,0x54,0,0,5,8),new(1,0,13,0x56,1,0,65535,16)};
        var values=new Dictionary<ushort,uint> {{0,3},{1,42}};
        for(ushort i=1;i<=3;i++) {
            ushort index=(ushort)(2+(i-1)*5);
            fields.AddRange([new(1,i,13,0x51,index,0,255,8),new(1,i,13,0x42,(ushort)(index+1),0,1,1),new(1,i,13,0x47,(ushort)(index+2),0,1,1),new(1,i,1,0x30,(ushort)(index+3),0,1000,16),new(1,i,1,0x31,(ushort)(index+4),0,500,16)]);
            values[index]=i;values[(ushort)(index+1)]=1;values[(ushort)(index+2)]=1;values[(ushort)(index+3)]=(uint)(i*200);values[(ushort)(index+4)]=250;
        }
        var decoded=PtpHidDecoder.DecodeData(fields.ToArray(),values)!;
        check(decoded.ContactCount==3 && decoded.ScanTime==42 && decoded.Contacts[1] is {Id:2,Tip:true,Confidence:true,X:.4,Y:.5},"HID capability mapping decodes per-finger fields using actual device axes");
        values.Remove(4);decoded=PtpHidDecoder.DecodeData(fields.ToArray(),values)!;
        check(!decoded.Contacts[0].Confidence,"an absent inactive HID button value is false, not a stale prior value");
        reject(()=>PtpHidDecoder.DecodeData(fields.Where(f=>!(f.Link==1 && f.Usage==0x42)).ToArray(),values),"incomplete finger layouts cannot masquerade as valid raw input");
        var timed=new Message("touchpad") {Phase="begin",GestureEpoch=1,GestureTime=1234.5,Contacts=[new(0,.2,.3),new(1,.4,.5),new(2,.6,.7)]};
        timed.Validate();check(timed.GestureTime==1234.5,"source sample clock travels with the normalized gesture frame");
        timed.GestureTime=double.PositiveInfinity;reject(timed.Validate,"non-finite source sample time is rejected");timed.GestureTime=-1;reject(timed.Validate,"negative source sample time is rejected");
    }
}
