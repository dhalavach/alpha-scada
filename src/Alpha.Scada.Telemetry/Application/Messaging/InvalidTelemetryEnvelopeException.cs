namespace Alpha.Scada.Telemetry.Application.Messaging;

public sealed class InvalidTelemetryEnvelopeException(string message) : Exception(message);
