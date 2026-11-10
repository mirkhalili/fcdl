using System.Security.Cryptography;
using System.Text;

namespace JacquardStudio;

/// <summary>Versioned AES-GCM envelope. A random content key is independently wrapped for the user and the shared recovery password.</summary>
public static class EncryptedProject
{
    private static readonly byte[] Magic=Encoding.ASCII.GetBytes("SAMENC01");
    // A shared recovery password in a distributable application is extractable. It does not provide independent secrecy.
    private const string RecoveryPassword="Mir@1354@sam";
    private const int Iterations=310_000, HeaderSize=8+16+16+12+16+32+12+16+32+12+16;
    public static bool IsRecoveryPassword(string password) => password==RecoveryPassword;
    public static bool IsEncrypted(ReadOnlySpan<byte> bytes) => bytes.Length>=Magic.Length && bytes[..Magic.Length].SequenceEqual(Magic);
    private static byte[] Derive(string password,byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
    private static void Wrap(byte[] contentKey,string password,byte[] salt,byte[] nonce,byte[] tag,byte[] wrapped)
    {
        byte[] key=Derive(password,salt); try { using var aes=new AesGcm(key,16); aes.Encrypt(nonce,contentKey,wrapped,tag,Magic); } finally {CryptographicOperations.ZeroMemory(key);}
    }
    public static byte[] Encrypt(byte[] plaintext,string password)
    {
        if(string.IsNullOrEmpty(password) || password.Length>1024) throw new ArgumentException("رمز پروژه باید بین ۱ تا ۱۰۲۴ نویسه باشد.");
        if(plaintext.Length>128*1024*1024) throw new InvalidDataException("حجم پروژه بیش از حد مجاز است.");
        var contentKey=RandomNumberGenerator.GetBytes(32); var salt=RandomNumberGenerator.GetBytes(16); var recoverySalt=RandomNumberGenerator.GetBytes(16);
        var nonce=RandomNumberGenerator.GetBytes(12); var tag=new byte[16]; var wrapped=new byte[32]; var rn=RandomNumberGenerator.GetBytes(12); var rt=new byte[16]; var rw=new byte[32]; var dataNonce=RandomNumberGenerator.GetBytes(12); var dataTag=new byte[16]; var encrypted=new byte[plaintext.Length];
        try
        {
            Wrap(contentKey,password,salt,nonce,tag,wrapped); Wrap(contentKey,RecoveryPassword,recoverySalt,rn,rt,rw);
            using var header=new MemoryStream(); foreach(var part in new[]{Magic,salt,recoverySalt,nonce,tag,wrapped,rn,rt,rw,dataNonce}) header.Write(part);
            var aad=header.ToArray(); using(var aes=new AesGcm(contentKey,16)) aes.Encrypt(dataNonce,plaintext,encrypted,dataTag,aad);
            using var output=new MemoryStream(); output.Write(aad); output.Write(dataTag); output.Write(encrypted); return output.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(contentKey); }
    }
    public static byte[] Decrypt(byte[] data,string password)
    {
        if(data.Length<HeaderSize || data.Length>128*1024*1024+HeaderSize || !IsEncrypted(data)) throw new InvalidDataException("ساختار پروژهٔ رمزگذاری‌شده معتبر نیست.");
        if(string.IsNullOrEmpty(password) || password.Length>1024) throw new ArgumentException("رمز پروژه معتبر نیست.");
        int pos=8; byte[] Read(int count) { var x=data.AsSpan(pos,count).ToArray();pos+=count;return x; }
        var salt=Read(16); var recoverySalt=Read(16); var nonce=Read(12); var tag=Read(16); var wrapped=Read(32); var rn=Read(12); var rt=Read(16); var rw=Read(32); var dn=Read(12); int aadLength=pos; var dt=Read(16); var ciphertext=Read(data.Length-pos); var contentKey=new byte[32];
        bool TryUnwrap(byte[] s,byte[] n,byte[] t,byte[] w) { byte[] key=Derive(password,s); try {using var aes=new AesGcm(key,16); aes.Decrypt(n,w,t,contentKey,Magic);return true;} catch(CryptographicException){return false;} finally {CryptographicOperations.ZeroMemory(key);} }
        try
        {
            if(!TryUnwrap(salt,nonce,tag,wrapped) && !TryUnwrap(recoverySalt,rn,rt,rw)) throw new InvalidDataException("رمز اشتباه است یا فایل آسیب دیده است.");
            var plain=new byte[ciphertext.Length]; try { using var aes=new AesGcm(contentKey,16); aes.Decrypt(dn,ciphertext,dt,plain,data.AsSpan(0,aadLength)); return plain; } catch(CryptographicException) { CryptographicOperations.ZeroMemory(plain); throw new InvalidDataException("رمز اشتباه است یا فایل آسیب دیده است."); }
        }
        finally {CryptographicOperations.ZeroMemory(contentKey);}
    }
}
