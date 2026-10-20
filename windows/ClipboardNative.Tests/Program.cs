using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ListFXFlow;

internal static class Program
{
    [STAThread] private static void Main() {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        try {
            // Construct a WPF bitmap with transparency, as supplied by screenshot apps.
            byte[] pixels=[10,20,30,128, 40,50,60,255];
            var bitmap=BitmapSource.Create(2,1,96,96,PixelFormats.Bgra32,null,pixels,8); bitmap.Freeze();
            byte[] png=ClipboardBitmap.Encode(bitmap); File.WriteAllBytes(path,png);
            var prepared=ClipboardBitmap.Decode(path);
            if(!prepared.Bitmap.IsFrozen) throw new Exception("Cross-thread bitmap is not frozen");
            var data=ClipboardBitmap.DataObject(prepared);
            if(!data.ContainsImage() || data.GetImage().PixelWidth!=2) throw new Exception("Native bitmap format missing");
            byte[] decoded=new byte[8]; prepared.Bitmap.CopyPixels(decoded,8,0);
            if(!decoded.SequenceEqual(pixels)) throw new Exception("Image pixels/transparency changed");
            if(data.GetData("PNG") is not MemoryStream stream || !stream.ToArray().SequenceEqual(png)) throw new Exception("Lossless PNG format missing");
            if(data.GetFileDropList().Count!=1 || data.GetFileDropList()[0]!=path) throw new Exception("Explorer file paste format missing");
            byte[] large=ClipboardImageTests.Fixture(); File.WriteAllBytes(path,large);
            if(ClipboardBitmap.Decode(path).Bitmap.PixelWidth!=720) throw new Exception("Large cross-platform PNG decode failed");
            File.WriteAllText(path,"invalid image");
            bool rejected=false;
            try { ClipboardBitmap.Decode(path); } catch(Exception e) when(e is IOException or FormatException or NotSupportedException) { rejected=true; }
            if(!rejected) throw new Exception("Corrupt image was accepted");
            Console.WriteLine("PASS native WPF clipboard formats, transparent PNG, Explorer file paste, large PNG and invalid-image rejection (private DataObject; system clipboard untouched)");
        } finally { File.Delete(path); }
    }
}
