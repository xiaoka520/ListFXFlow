using System.Buffers.Binary;
using System.IO;

namespace ListFXFlow;

internal sealed record PtpHidMetadata(uint Flags,int DescriptorLength,int InputLength,uint Vendor,uint Product,ulong Dropped,ulong Observed,ulong Frequency) {
    internal bool Validated=>(Flags&1)!=0;
}
internal sealed record PtpHidReport(ulong Sequence,ulong Counter,byte[] Bytes);
internal static class PtpHidProtocol {
    internal const int Version=1,ReportLimit=4096,DescriptorLimit=65536,HeaderLength=32;
    internal const uint MetadataIoctl=0x226000,DescriptorIoctl=0x226004,ReportIoctl=0x226008;
    internal static readonly Guid Interface=new("3bcfd8e1-210c-41f5-b092-be38a2aa5d0b");
    internal static PtpHidMetadata Metadata(ReadOnlySpan<byte> bytes) {
        if(bytes.Length!=64 || U32(bytes,0)!=64 || U32(bytes,4)!=Version)throw new InvalidDataException("PTP driver metadata ABI mismatch");
        uint flags=U32(bytes,8),descriptor=U32(bytes,16),input=U32(bytes,20);
        if((flags&~3u)!=0 || (flags&2)==0 || U32(bytes,12)!=ReportLimit || descriptor>DescriptorLimit || input>ReportLimit || U32(bytes,36)!=64 || U64(bytes,56)==0)
            throw new InvalidDataException("Invalid PTP driver capabilities");
        if((flags&1)!=0 && (descriptor==0 || input==0))throw new InvalidDataException("Missing validated PTP descriptor");
        return new(flags,(int)descriptor,(int)input,U32(bytes,24),U32(bytes,28),U64(bytes,40),U64(bytes,48),U64(bytes,56));
    }
    internal static PtpHidReport Report(ReadOnlySpan<byte> bytes) {
        if(bytes.Length<HeaderLength || bytes.Length>HeaderLength+ReportLimit || U32(bytes,0)!=bytes.Length || U32(bytes,4)!=Version ||
           U32(bytes,24)==0 || U32(bytes,24)!=bytes.Length-HeaderLength || U32(bytes,28)!=0 || U64(bytes,8)==0)
            throw new InvalidDataException("Invalid raw PTP report frame");
        return new(U64(bytes,8),U64(bytes,16),bytes[HeaderLength..].ToArray());
    }
    private static uint U32(ReadOnlySpan<byte> b,int i)=>BinaryPrimitives.ReadUInt32LittleEndian(b[i..]);
    private static ulong U64(ReadOnlySpan<byte> b,int i)=>BinaryPrimitives.ReadUInt64LittleEndian(b[i..]);
}
