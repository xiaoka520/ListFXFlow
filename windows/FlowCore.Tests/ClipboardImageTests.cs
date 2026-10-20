using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using ListFXFlow;

internal static class ClipboardImageTests
{
    // A real, incompressible RGBA PNG larger than a wire frame; no WPF or system clipboard.
    public static byte[] Fixture() {
        using var output=new MemoryStream(); output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string kind,byte[] data) {
            byte[] type=System.Text.Encoding.ASCII.GetBytes(kind),length=new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length,data.Length); output.Write(length); output.Write(type); output.Write(data);
            uint crc=0xffffffff;
            foreach(byte b in type.Concat(data)) { crc^=b; for(int i=0;i<8;i++) crc=(crc&1)!=0 ? 0xedb88320^(crc>>1) : crc>>1; }
            BinaryPrimitives.WriteUInt32BigEndian(length,~crc); output.Write(length);
        }
        byte[] header=new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header,720); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),480); header[8]=8; header[9]=6; Chunk("IHDR",header);
        using var compressed=new MemoryStream();
        using(var z=new ZLibStream(compressed,CompressionLevel.Fastest,true)) {
            byte[] row=new byte[720*4+1];
            for(int y=0;y<480;y++) { RandomNumberGenerator.Fill(row.AsSpan(1)); z.Write(row); }
        }
        Chunk("IDAT",compressed.ToArray()); Chunk("IEND",[]); return output.ToArray();
    }
    public static void Run(Action<bool,string> check,Action<Action,string> reject) {
        string cache=Path.Combine(Path.GetTempPath(),"ListFXFlow-image-"+Guid.NewGuid());
        try {
            using var inbox=new ClipboardInbox(cache); string id=Guid.NewGuid().ToString("D"); byte[] image=Fixture();
            check(image.Length>1048576,"image fixture exceeds a single encrypted frame");
            ClipboardPacket Begin()=>new("begin",id) { Roots=["Clipboard.png"],Count=1,Total=image.Length,ImageFormat="png" };
            var bad=Begin(); bad.ImageFormat="jpeg"; reject(bad.Validate,"unknown image representation rejected");
            bad=Begin(); bad.Total=134217729; reject(bad.Validate,"image payload bounded to 128 MiB");
            bad=Begin(); bad.Count=2; reject(bad.Validate,"image transfer requires exactly one entry");
            bad=Begin(); bad.Total=0; reject(bad.Validate,"empty image rejected");
            inbox.Receive(Begin());
            reject(()=>inbox.Receive(new("entry",id) { Path="Clipboard.png",Directory=true,Size=0 }),"image cannot be a directory");
            inbox.Abort(); inbox.Receive(Begin());
            inbox.Receive(new("entry",id) { Path="Clipboard.png",Directory=false,Size=image.Length });
            reject(()=>inbox.Receive(new("commit",id)),"image cannot publish before chunks and hash");
            for(int offset=0;offset<image.Length;offset+=196608) inbox.Receive(new("data",id) { Bytes=image[offset..Math.Min(offset+196608,image.Length)] });
            inbox.Receive(new("fileEnd",id) { Hash=Convert.ToHexString(SHA256.HashData(image)) });
            var result=inbox.Receive(new("commit",id));
            check(result is { Image:not null,Files:null,Text:null } && File.ReadAllBytes(result.Image).SequenceEqual(image),"multi-chunk PNG publishes as image, not file-only or text");
            inbox.Abort(); check(File.Exists(result!.Image),"published image survives clipboard disconnection");
        } finally { if(Directory.Exists(cache)) Directory.Delete(cache,true); }
    }
}
