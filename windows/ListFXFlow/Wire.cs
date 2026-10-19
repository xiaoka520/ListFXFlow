using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ListFXFlow;

public sealed class Message(string type)
{
    public string Type { get; set; } = type;
    public string? Active { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Dx { get; set; }
    public double? Dy { get; set; }
    public int? Key { get; set; }
    public int? Button { get; set; }
    public bool? Down { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? Side { get; set; }
    public double? Offset { get; set; }
    public string? Nonce { get; set; }
    public string? Auth { get; set; }
    public string? PublicKey { get; set; }
    public int? Version { get; set; }
    public string[]? Features { get; set; }
    public TouchpadContact[]? Contacts { get; set; }
    public string? Phase { get; set; }
    public long? GestureEpoch {get;set;}
    public long? Frames {get;set;}
    public long? Events {get;set;}
    public string? Backend {get;set;}
    public void Validate()
    {
        bool finite = new[] { X,Y,Dx,Dy,Width,Height,Offset }.All(v => !v.HasValue || double.IsFinite(v.Value));
        bool valid = finite && Type switch {
            "pointer" => Active is "mac" or "windows" && X is >= 0 and <= 1 && Y is >= 0 and <= 1,
            "key" => Key.HasValue && KeyMap.Entries.Any(k => k.Hid == Key) && Down.HasValue,
            "button" => Button is >= 0 and <= 4 && Down.HasValue,
            "caps" => Down.HasValue,
            "gameModeRequest" => Down.HasValue,
            "gameMode" => Active=="windows" && X is >=0 and <=1 && Y is >=0 and <=1 &&
                (Down==true && Phase is "automatic" or "manual" || Down==false && Phase=="off"),
            "touchpadControl" => Down.HasValue,
            "touchpad" => (GestureEpoch is null or >=0) && Contacts is { Length: <=5 } && Contacts.All(c=>c!=null && c.Valid) && Contacts.Select(c=>c.Id).Distinct().Count()==Contacts.Length &&
                (Phase is "begin" or "change" ? Contacts.Length>=2 : Phase is "end" or "cancel" && Contacts.Length==0),
            "touchpadFeedback" => GestureEpoch is >=0 && Frames is >=0 and <=1000000000 && Events is >=0 and <=1000000000 &&
                Phase is "received" or "submitted" or "unavailable" && Backend is "quartz" or "virtual-hid" or "synthetic-touchpad",
            "scroll" => Dx.HasValue && Dy.HasValue && Math.Abs(Dx.Value) <= 10000 && Math.Abs(Dy.Value) <= 10000,
            "relativePointer" => Dx.HasValue && Dy.HasValue && Math.Abs(Dx.Value)<=1000000 && Math.Abs(Dy.Value)<=1000000,
            "display" => Width is >= 320 and <= 32768 && Height is >= 200 and <= 32768,
            "layout" => Side is "left" or "right" or "top" or "bottom" && Offset.HasValue && Math.Abs(Offset.Value) <= 900,
            "clipboardReady" => Nonce?.Length==64 && Nonce.All(Uri.IsHexDigit),
            "ping" or "reset" => true,
            _ => false
        };
        if (!valid) throw new InvalidDataException("无效的输入消息");
    }
}

public static class Wire
{
    public static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static byte[] Encode(Message m) => JsonSerializer.SerializeToUtf8Bytes(m, Json);
    public static Message Decode(byte[] data) => JsonSerializer.Deserialize<Message>(data, Json) ?? throw new InvalidDataException();
    public static async Task<byte[]> ReadFrame(Stream stream, CancellationToken token,int limit=16384)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header,token);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length<=0 || length>limit) throw new InvalidDataException("无效的数据包长度");
        byte[] data = new byte[length]; await stream.ReadExactlyAsync(data,token); return data;
    }
    public static async Task WriteFrame(Stream stream, byte[] data, CancellationToken token)
    {
        byte[] frame = new byte[data.Length+4]; BinaryPrimitives.WriteInt32BigEndian(frame,data.Length);
        data.CopyTo(frame,4); await stream.WriteAsync(frame,token);
    }
    public static byte[] Proof(byte[] secret, string role, byte[] client, byte[] server) =>
        HMACSHA256.HashData(secret, (byte[])[..Encoding.UTF8.GetBytes("ListFXFlow/1/"+role),..client,..server]);
    public static byte[] Public(ECDiffieHellman identity)
    {
        var p = identity.ExportParameters(false); return [4,..p.Q.X!,..p.Q.Y!];
    }
    public static byte[] SharedSecret(ECDiffieHellman identity, byte[] peer)
    {
        if (peer.Length != 65 || peer[0] != 4) throw new InvalidDataException("设备身份无效");
        using var other = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = peer[1..33], Y = peer[33..65] } });
        byte[] raw = identity.DeriveRawSecretAgreement(other.PublicKey);
        try { return SHA256.HashData([..Encoding.UTF8.GetBytes("ListFXFlow/1/ECDH"),..raw]); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
}

public sealed class SecureChannel : IDisposable
{
    private readonly AesGcm sender, receiver;
    private ulong sent, received;
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("ListFXFlow/1");
    public SecureChannel(byte[] secret, byte[] client, byte[] server, bool isServer)
    {
        sender = new AesGcm(Wire.Proof(secret,isServer ? "s2c" : "c2s",client,server),16);
        receiver = new AesGcm(Wire.Proof(secret,isServer ? "c2s" : "s2c",client,server),16);
    }
    private static byte[] Nonce(ulong sequence) { byte[] n = new byte[12]; BinaryPrimitives.WriteUInt64BigEndian(n.AsSpan(4),sequence); return n; }
    public byte[] Seal(Message message)
    {
        message.Validate(); return SealData(Wire.Encode(message));
    }
    public byte[] SealData(byte[] plaintext)
    {
        if (sent == ulong.MaxValue) throw new InvalidDataException();
        byte[] nonce = Nonce(++sent), output = new byte[plaintext.Length+28]; nonce.CopyTo(output,0);
        sender.Encrypt(nonce,plaintext,output.AsSpan(12,plaintext.Length),output.AsSpan(12+plaintext.Length,16),Aad); return output;
    }
    public Message Open(byte[] payload)
    {
        var message=Wire.Decode(OpenData(payload)); message.Validate(); return message;
    }
    public byte[] OpenData(byte[] payload)
    {
        if (payload.Length < 28 || received == ulong.MaxValue || !payload.AsSpan(0,12).SequenceEqual(Nonce(received+1))) throw new InvalidDataException("过期或无序的数据包");
        byte[] plaintext = new byte[payload.Length-28];
        receiver.Decrypt(payload.AsSpan(0,12),payload.AsSpan(12,plaintext.Length),payload.AsSpan(12+plaintext.Length,16),plaintext,Aad);
        received++; return plaintext;
    }
    public void Dispose() { sender.Dispose(); receiver.Dispose(); }
}

public sealed record KeyEntry(int Hid,int Mac,int Scan,bool Extended);
public static class KeyMap
{
    public static readonly KeyEntry[] Entries = Load();
    private static KeyEntry[] Load()
    {
        var assembly = typeof(KeyMap).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("keymap.json")))!;
        return JsonSerializer.Deserialize<KeyEntry[]>(stream,new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    public static readonly Dictionary<int,KeyEntry> ByHid = Entries.ToDictionary(k => k.Hid);
    public static readonly Dictionary<int,KeyEntry> ByScan = Entries.ToDictionary(k => k.Scan | (k.Extended ? 0x100 : 0));
}
