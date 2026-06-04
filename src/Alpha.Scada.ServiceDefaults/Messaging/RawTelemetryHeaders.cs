namespace Alpha.Scada.ServiceDefaults.Messaging;

public static class RawTelemetryHeaders
{
    // Wolverine's NATS transport needs envelope headers to route raw JSON payloads.
    // The payload body stays the external telemetry contract, not a Wolverine envelope.
    public const string NatsMessageId = "Nats-Msg-Id";
    public const string WolverineMessageId = "id";
    public const string WolverineMessageType = "message-type";
    public const string WolverineContentType = "content-type";
    public const string Subject = "alpha.edge.subject";
}
