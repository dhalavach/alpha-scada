/*
ANNOTATION FOR LEARNING:
- File: src/Alpha.Scada.Tenant/Application/TenantService.cs
- Module role: Alpha.Scada.Tenant is the tenant registry. It is the source of truth for customer/operator records and tenant keys used to scope every downstream asset, tag, telemetry, alarm, and report query.
- Local role: This file is an application-service layer: it coordinates repositories, policies, external calls, and DTOs without being an HTTP controller itself.
- Architecture connection: application files orchestrate use cases and message handling; they may call repositories/clients but should keep transport details at the boundary.
- .NET/C# concepts to notice: async/await represents non-blocking I/O; it matters here because database, HTTP, NATS, and SignalR calls should not block server threads.
- Reading tip: start with the public method/route/record names, then trace dependencies through constructor parameters; in .NET those parameters are usually supplied by the dependency-injection container.
*/

using Alpha.Scada.Contracts;
using Alpha.Scada.Tenant.Infrastructure;

namespace Alpha.Scada.Tenant.Application;

public sealed class TenantService(TenantRepository repository)
{
    public Task<IReadOnlyCollection<TenantDto>> GetTenantsAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetTenantsAsync(user, cancellationToken);

    public Task<TenantDto?> ResolveAsync(string tenantKey, CancellationToken cancellationToken) =>
        repository.ResolveAsync(tenantKey, cancellationToken);

    public Task<TenantDto?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(tenantId, cancellationToken);
}
