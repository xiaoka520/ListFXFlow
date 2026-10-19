using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ListFXFlow;

public sealed class ClipboardPacket(string kind,string id)
{
    public string Kind { get; set; }=kind;
    public string Id { get; set; }=id;
    public string? Text { get; set; }
    public string[]? Roots { get; set; }
    public int? Count { get; set; }
    public long? Total { get; set; }
    public string? Path { get; set; }
    public long? Size { get; set; }
    public bool? Directory { get; set; }
    public byte[]? Bytes { get; set; }
    public string? Hash { get; set; }
    public void Validate() {
        if(!Guid.TryParseExact(Id,"D",out _)) throw new InvalidDataException("无效的剪贴板编号");
        bool valid=Kind switch {
            "text"=>Text!=null && Encoding.UTF8.GetByteCount(Text)<=524288,
            "begin"=>Roots is { Length: >0 and <=256 } && Count>=Roots.Length && Count<=100000 && Total is >=0 and <=1099511627776 && Roots.All(p=>!p.Contains('/') && ClipboardPath.Valid(p)) && Roots.Distinct(StringComparer.OrdinalIgnoreCase).Count()==Roots.Length,
            "entry"=>Path!=null && ClipboardPath.Valid(Path) && Size is >=0 and <=1099511627776 && Directory.HasValue && (Directory!=true || Size==0),
            "data"=>Bytes is { Length: >0 and <=196608 },
            "fileEnd"=>Hash?.Length==64 && Hash.All(Uri.IsHexDigit),
            "commit" or "cancel" or "done"=>true,
            _=>false
        };
        if(!valid) throw new InvalidDataException("剪贴板数据或文件名不受支持");
    }
}
public static class ClipboardPath
{
    public static bool Valid(string path) {
        if(Encoding.UTF8.GetByteCount(path)>4096 || !path.IsNormalized(NormalizationForm.FormC)) return false;
        var parts=path.Split('/'); if(parts.Length>64) return false;
        foreach(var name in parts) {
            if(name.Length==0 || name is "." or ".." || Encoding.UTF8.GetByteCount(name)>255 || name.EndsWith('.') || name.EndsWith(' ') || name.Any(c=>c<32 || "\\:<>\"|?*".Contains(c))) return false;
            string first=name.Split('.')[0].ToUpperInvariant();
            if(first is "CON" or "PRN" or "AUX" or "NUL" || first.Length==4 && (first.StartsWith("COM") || first.StartsWith("LPT")) && first[3] is >= '1' and <= '9') return false;
        }
        return true;
    }
}
public sealed record ClipboardEntry(string LocalPath,string RelativePath,long Size,bool Directory);
public sealed record ClipboardManifest(string[] Roots,List<ClipboardEntry> Entries,long Total)
{
    public static ClipboardManifest Build(string[] paths,CancellationToken token) {
        if(paths.Length is 0 or >256) throw new IOException("一次最多复制 256 个顶层项目");
        var roots=new List<string>(); var entries=new List<ClipboardEntry>(); var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total=0;
        void Add(string local,string relative) {
            token.ThrowIfCancellationRequested(); relative=relative.Normalize(NormalizationForm.FormC);
            if(!ClipboardPath.Valid(relative) || !seen.Add(relative) || entries.Count>=100000) throw new IOException("不支持的文件名或名称冲突："+relative);
            var attributes=File.GetAttributes(local);
            if((attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("不支持复制链接："+relative);
            bool dir=(attributes&FileAttributes.Directory)!=0; long size=dir ? 0 : new FileInfo(local).Length;
            total=checked(total+size); if(total>1099511627776) throw new IOException("文件总量超过 1 TB");
            entries.Add(new(local,relative,size,dir));
            if(dir) foreach(var child in System.IO.Directory.EnumerateFileSystemEntries(local)) Add(child,relative+"/"+System.IO.Path.GetFileName(child));
        }
        foreach(var path in paths) { string root=System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)).Normalize(NormalizationForm.FormC); roots.Add(root); Add(path,root); }
        return new(roots.ToArray(),entries,total);
    }
}
public sealed record ClipboardResult(string? Text=null,string[]? Files=null);

public sealed class ClipboardInbox(string cache) : IDisposable
{
    public string? Id { get; private set; }
    public long Received { get; private set; }
    public long Total { get; private set; }
    private string? folder;
    private string[] roots=[];
    private int expected;
    private long declared,remaining;
    private readonly HashSet<string> entries=new(StringComparer.OrdinalIgnoreCase),directories=new(StringComparer.OrdinalIgnoreCase);
    private FileStream? file;
    private IncrementalHash? digest;
    public void Abort() {
        file?.Dispose(); file=null; digest?.Dispose(); digest=null;
        if(folder!=null) { try { Directory.Delete(folder,true); } catch(IOException) {} catch(UnauthorizedAccessException) {} }
        folder=null; Id=null; roots=[]; entries.Clear(); directories.Clear(); Received=Total=declared=remaining=0;
    }
    public ClipboardResult? Receive(ClipboardPacket p) {
        p.Validate();
        if(p.Kind=="text") { Abort(); return new(Text:p.Text); }
        if(p.Kind=="begin") {
            Abort(); Directory.CreateDirectory(cache);
            var drive=new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(cache))!);
            if(drive.AvailableFreeSpace<p.Total!.Value+16777216) throw new IOException("接收端磁盘空间不足");
            folder=System.IO.Path.Combine(cache,Guid.NewGuid().ToString("D")); Directory.CreateDirectory(folder);
            Id=p.Id; roots=p.Roots!; expected=p.Count!.Value; Total=p.Total.Value; return null;
        }
        if(Id!=p.Id || folder==null) return null;
        switch(p.Kind) {
            case "entry":
                if(file!=null || entries.Count>=expected || !roots.Contains(p.Path!.Split('/')[0],StringComparer.Ordinal) || !entries.Add(p.Path)) throw new InvalidDataException("文件项目顺序错误");
                int separator=p.Path.LastIndexOf('/'); if(separator>=0 && !directories.Contains(p.Path[..separator])) throw new InvalidDataException("缺少父目录："+p.Path);
                string target=System.IO.Path.Combine(folder,p.Path.Replace('/',System.IO.Path.DirectorySeparatorChar));
                if(System.IO.Path.Exists(target)) throw new InvalidDataException("文件名称冲突");
                if(p.Directory!.Value) { Directory.CreateDirectory(target); directories.Add(p.Path); }
                else {
                    if(declared>Total-p.Size!.Value) throw new InvalidDataException("文件大小超出声明"); declared+=p.Size.Value;
                    file=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536); remaining=p.Size.Value; digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                }
                break;
            case "data":
                if(file==null || p.Bytes!.LongLength>remaining) throw new InvalidDataException("无效文件分块");
                file.Write(p.Bytes); digest!.AppendData(p.Bytes); remaining-=p.Bytes.Length; Received+=p.Bytes.Length; break;
            case "fileEnd":
                if(file==null || remaining!=0 || !Convert.ToHexString(digest!.GetHashAndReset()).Equals(p.Hash,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件未完成或校验失败");
                file.Dispose(); file=null; digest.Dispose(); digest=null; break;
            case "commit":
                if(file!=null || entries.Count!=expected || declared!=Total || Received!=Total || !roots.All(entries.Contains)) throw new InvalidDataException("文件传输未完成");
                var files=roots.Select(root=>System.IO.Path.Combine(folder,root)).ToArray(); folder=null; Id=null; return new(Files:files);
            case "cancel": Abort(); break;
            default: throw new InvalidDataException();
        }
        return null;
    }
    public void Dispose()=>Abort();
}
