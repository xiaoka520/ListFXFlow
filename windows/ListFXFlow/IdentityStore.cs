using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ListFXFlow;

public static class IdentityStore
{
    private static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ListFXFlow");
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] data,bool protect)
    {
        var input=new Blob { Size=data.Length,Data=Marshal.AllocHGlobal(data.Length) }; Marshal.Copy(data,0,input.Data,data.Length);
        Blob output=default;
        try {
            bool success=protect ? CryptProtectData(ref input,"ListFXFlow",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output) : CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            byte[] result=new byte[output.Size]; Marshal.Copy(output.Data,result,0,result.Length); return result;
        } finally { Marshal.FreeHGlobal(input.Data); if(output.Data!=IntPtr.Zero) LocalFree(output.Data); }
    }
    public static byte[]? Read(string name) { string path=Path.Combine(DirectoryPath,name+".dpapi"); return File.Exists(path) ? Transform(File.ReadAllBytes(path),false) : null; }
    public static void Write(string name,byte[] data) {
        Directory.CreateDirectory(DirectoryPath); string path=Path.Combine(DirectoryPath,name+".dpapi");
        string temporary=path+".tmp"; File.WriteAllBytes(temporary,Transform(data,true)); File.Move(temporary,path,true);
    }
    public static ECDiffieHellman Identity() {
        var identity=ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var stored=Read("identity");
        if(stored!=null) { try { identity.ImportPkcs8PrivateKey(stored,out _); } finally { CryptographicOperations.ZeroMemory(stored); } }
        else { var encoded=identity.ExportPkcs8PrivateKey(); try { Write("identity",encoded); } finally { CryptographicOperations.ZeroMemory(encoded); } }
        return identity;
    }
}
