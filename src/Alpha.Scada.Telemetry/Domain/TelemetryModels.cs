namespace Alpha.Scada.Telemetry.Domain;

public sealed record TelemetrySample(Guid TenantId, Guid UnitId, Guid TagId, DateTimeOffset TimestampUtc, double Value, string Quality);
