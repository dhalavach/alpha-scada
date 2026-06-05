/*
ANNOTATION FOR LEARNING:
- File: tests/Alpha.Scada.Tests/TestImages.cs
- Module role: Alpha.Scada.Tests is the test suite. These files are executable documentation for architecture decisions, service behavior, and integration edges.
- Local role: This file is a test fixture/specification. It documents expected behavior and catches regressions across service and messaging boundaries.
- Architecture connection: tests double as executable architecture notes, especially where they verify tenant isolation, broker behavior, schema config, and failure handling.
- .NET/C# concepts to notice: NATS subjects are dot-delimited routing keys; JetStream adds durable streams, consumers, acknowledgements, and duplicate detection.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

// LEARN: declares the logical namespace; namespaces organize types and help dependency direction stay visible.
namespace Alpha.Scada.Tests;

// LEARN: declares a static helper class whose members are called on the type itself.
public static class TestImages
{
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string Postgres = "timescale/timescaledb:2.17.2-pg16";
// LEARN: declares a member with explicit visibility so the type boundary is clear.
    public const string Nats = "nats:2.12-alpine";
}
