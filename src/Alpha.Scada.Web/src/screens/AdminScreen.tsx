import type { Site, SystemProbe, Tenant, Unit, User } from "../api/types";
import Detail from "../components/Detail";
import Kpi from "../components/Kpi";

type AdminScreenProps = {
  user: User | null;
  tenants: Tenant[];
  sites: Site[];
  units: Unit[];
  system: SystemProbe;
  refreshSystem: () => Promise<void>;
};

export default function AdminScreen({ user, tenants, sites, units, system, refreshSystem }: AdminScreenProps) {
  return (
    <section className="screenStack">
      <section className="kpiGrid three">
        <Kpi label="Health" value={system.health} tone="cyan" />
        <Kpi label="Readiness" value={system.ready} tone="lime" />
        <Kpi label="Tenants" value={String(tenants.length)} tone="blue" />
      </section>

      <section className="adminGrid">
        <section className="panel">
          <div className="panelHeader">
            <div>
              <p className="eyebrow">Identity</p>
              <h2>Current User</h2>
            </div>
          </div>
          <Detail label="Name" value={user?.displayName ?? "--"} />
          <Detail label="Email" value={user?.email ?? "--"} />
          <Detail label="Role" value={user?.role ?? "--"} />
          <Detail label="Tenant" value={user?.tenantId ?? "--"} mono />
        </section>

        <section className="panel">
          <div className="panelHeader">
            <div>
              <p className="eyebrow">Runtime</p>
              <h2>System Probe</h2>
            </div>
            <button className="iconButton" onClick={refreshSystem}>Refresh</button>
          </div>
          <Detail label="Health" value={system.health} />
          <Detail label="Ready" value={system.ready} />
          <pre className="metricsBox">{system.metrics || "No metrics loaded"}</pre>
        </section>
      </section>

      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Configuration</p>
            <h2>Tenant / Site / Unit Inventory</h2>
          </div>
        </div>
        <div className="dataTable">
          <div className="tableHead">
            <span>Type</span>
            <span>Name</span>
            <span>Key / Model</span>
            <span>Region / Status</span>
          </div>
          {tenants.map(tenant => (
            <div className="tableRow" key={tenant.id}>
              <span>Tenant</span>
              <span><strong>{tenant.name}</strong></span>
              <span className="mono">{tenant.key}</span>
              <span>{tenant.region}</span>
            </div>
          ))}
          {sites.map(site => (
            <div className="tableRow" key={site.id}>
              <span>Site</span>
              <span><strong>{site.name}</strong></span>
              <span className="mono">{site.key}</span>
              <span>{site.region} / {site.status}</span>
            </div>
          ))}
          {units.map(unit => (
            <div className="tableRow" key={unit.id}>
              <span>Unit</span>
              <span><strong>{unit.name}</strong></span>
              <span>{unit.model}</span>
              <span>{unit.status}</span>
            </div>
          ))}
        </div>
      </section>
    </section>
  );
}
