using System.Security.Cryptography;
using System.Text;

namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class MessageIds
{
    public static string Deterministic(string subject, ReadOnlySpan<byte> payload)
    {
        var subjectBytes = Encoding.UTF8.GetBytes(subject);
        var buffer = new byte[subjectBytes.Length + 1 + payload.Length];
        subjectBytes.CopyTo(buffer);
        buffer[subjectBytes.Length] = 0x1f;
        payload.CopyTo(buffer.AsSpan(subjectBytes.Length + 1));
        var hash = SHA256.HashData(buffer);
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }
}
