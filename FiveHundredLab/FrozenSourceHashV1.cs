using System.Security.Cryptography;
using System.Text;
namespace FiveHundredLab;
public static class FrozenSourceHashV1
{
    // Git text=auto may check out CRLF on Windows. Only line-ending differences are normalized.
    public static string Compute(string path)
    {
        var text=Encoding.UTF8.GetString(File.ReadAllBytes(path)).Replace("\r\n","\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
