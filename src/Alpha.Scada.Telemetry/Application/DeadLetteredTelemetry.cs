namespace Alpha.Scada.Telemetry.Application;

public sealed record DeadLetteredTelemetry(
    string Subject,
    string MessageId,
    string ErrorType,
    string ErrorMessage,
    DateTimeOffset DeadLetteredAtUtc,
    string PayloadBase64,
    bool PayloadTruncated);

public static class DeadLetteredTelemetryFactory
{
    public const int MaxPayloadBytes = 64 * 1024;

    public static DeadLetteredTelemetry Create(
        string subject,
        string messageId,
        Exception exception,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset deadLetteredAtUtc)
    {
        var payloadLength = Math.Min(payload.Length, MaxPayloadBytes);
        return new DeadLetteredTelemetry(
            subject,
            messageId,
            exception.GetType().Name,
            exception.Message,
            deadLetteredAtUtc,
            Convert.ToBase64String(payload[..payloadLength].Span),
            payload.Length > MaxPayloadBytes);
    }
}
