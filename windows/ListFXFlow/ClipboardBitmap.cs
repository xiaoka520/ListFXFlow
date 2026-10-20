using System.IO;
using System.Windows;
using System.Collections.Specialized;
using System.Windows.Media.Imaging;

namespace ListFXFlow;

internal static class ClipboardBitmap
{
    private const long MaxBytes=134217728;
    private static void Validate(BitmapSource bitmap) {
        if(bitmap.PixelWidth<=0 || bitmap.PixelHeight<=0 || bitmap.PixelWidth>32768 || bitmap.PixelHeight>32768 || (long)bitmap.PixelWidth*bitmap.PixelHeight>100000000)
            throw new InvalidDataException("图片尺寸过大或无效");
    }
    public static byte[] Encode(BitmapSource bitmap) {
        Validate(bitmap);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new MemoryStream(); encoder.Save(stream);
        if(stream.Length>MaxBytes) throw new InvalidDataException("图片超过 128 MB");
        return stream.ToArray();
    }
    internal sealed record Prepared(BitmapSource Bitmap,byte[] Png,string Path);
    public static Prepared Decode(string path) {
        var info=new FileInfo(path);
        if(info.Length is <=0 or >MaxBytes) throw new InvalidDataException("图片大小无效");
        byte[] png=File.ReadAllBytes(path);
        using var stream=new MemoryStream(png,false);
        var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var bitmap=decoder.Frames[0]; Validate(bitmap); bitmap.Freeze(); return new(bitmap,png,path);
    }
    public static DataObject DataObject(Prepared image) {
        var data=new DataObject(); data.SetImage(image.Bitmap);
        data.SetData("PNG",new MemoryStream(image.Png,false));
        data.SetFileDropList(new StringCollection { image.Path });
        data.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes(1)));
        return data;
    }
}
