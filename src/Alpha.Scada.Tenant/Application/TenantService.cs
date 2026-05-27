using Alpha.Scada.Contracts;
using Alpha.Scada.Tenant.Infrastructure;

namespace Alpha.Scada.Tenant.Application;

public sealed class TenantService(TenantRepository repository)
{
    public Task<IReadOnlyCollection<TenantDto>> GetTenantsAsync(CurrentUserDto user, CancellationToken cancellationToken) =>
        repository.GetTenantsAsync(user, cancellationToken);

    public Task<TenantDto?> ResolveAsync(string tenantKey, CancellationToken cancellationToken) =>
        repository.ResolveAsync(tenantKey, cancellationToken);
}
