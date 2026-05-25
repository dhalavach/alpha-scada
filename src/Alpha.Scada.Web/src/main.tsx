import * as signalR from "@microsoft/signalr";
import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import "./styles.css";

type ScreenName = "Overview" | "Tags" | "Trends" | "Alarms" | "Reports" | "Admin";

type User = {
  userId: string;
  tenantId: string;
  email: string;
  displayName: string;
  role: string;
};

type LoginResponse = {
  accessToken: string;
  expiresAtUtc: string;
  user: {
    id: string;
    tenantId: string;
    email: string;
    displayName: string;
    role: string;
  };
};

type Tenant = { id: string; key: string; name: string; region: string };
type Site = { id: string; tenantId: string; key: string; name: string; region: string; status: string };
type Unit = { id: string; tenantId: string; siteId: string; key: string; name: string; model: string; status: string; lastSeenUtc?: string };
type Tag = { tagId: string; tenantId: string; unitId: string; tagKey: string; name: string; subsystem: string; value: number; engineeringUnit: string; quality: string; timestampUtc: string };
type HistoryPoint = { timestampUtc: string; value: number; quality: string };
type Alarm = { id: string; unitId: string; tagId?: string; severity: string; message: string; state: string; raisedAtUtc: string; acknowledgedAtUtc?: string; clearedAtUtc?: string };
type Report = { id: string; unitId: string; period: string; electricalKwh: number; thermalKwh: number; runtimeHours: number; availabilityPercent: number; estimatedWoodChipsKg: number; estimatedBiocharM3: number; alarmCount: number; generatedAtUtc: string };
type SystemProbe = { health: string; ready: string; metrics: string };

const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";
const tokenKey = "alpha_scada_token";

const navItems: ScreenName[] = ["Overview", "Tags", "Trends", "Alarms", "Reports", "Admin"];

const processSteps = [
  { label: "Fuel Feed", key: "fuel.wood_chip_feed_kg_h", unit: "kg/h" },
  { label: "Gasifier", key: "gasifier.reactor_temp_c", unit: "degC" },
  { label: "Gas Cleaning", key: "gas_cleaning.filter_dp_mbar", unit: "mbar" },
  { label: "Engine", key: "engine.electrical_output_kw", unit: "kW" },
  { label: "Heat Recovery", key: "heat.thermal_output_kw", unit: "kW" }
];

function App() {
  const [token, setToken] = useState(() => localStorage.getItem(tokenKey) ?? "");
  const [activeScreen, setActiveScreen] = useState<ScreenName>("Overview");
  const [user, setUser] = useState<User | null>(null);
  const [tenants, setTenants] = useState<Tenant[]>([]);
  const [sites, setSites] = useState<Site[]>([]);
  const [selectedSiteId, setSelectedSiteId] = useState("");
  const [units, setUnits] = useState<Unit[]>([]);
  const [selectedUnitId, setSelectedUnitId] = useState("");
  const [tags, setTags] = useState<Tag[]>([]);
  const [alarms, setAlarms] = useState<Alarm[]>([]);
  const [reports, setReports] = useState<Report[]>([]);
  const [status, setStatus] = useState("Disconnected");
  const [tagSearch, setTagSearch] = useState("");
  const [subsystemFilter, setSubsystemFilter] = useState("all");
  const [selectedTrendTagId, setSelectedTrendTagId] = useState("");
  const [trendWindow, setTrendWindow] = useState(30);
  const [history, setHistory] = useState<HistoryPoint[]>([]);
  const [historyStatus, setHistoryStatus] = useState("Idle");
  const [system, setSystem] = useState<SystemProbe>({ health: "unknown", ready: "unknown", metrics: "" });

  const selectedSite = sites.find(site => site.id === selectedSiteId) ?? sites[0];
  const selectedUnit = units.find(unit => unit.id === selectedUnitId);
  const selectedTrendTag = tags.find(tag => tag.tagId === selectedTrendTagId);
  const groupedTags = useMemo(() => groupBy(tags, tag => tag.subsystem), [tags]);
  const subsystems = useMemo(() => Object.keys(groupedTags).sort(), [groupedTags]);
  const updatedAt = tags[0]?.timestampUtc ? new Date(tags[0].timestampUtc).toLocaleTimeString() : "--";
  const onlineUnits = units.filter(unit => unit.status.toLowerCase() === "online").length;

  const filteredTags = useMemo(() => {
    const needle = tagSearch.trim().toLowerCase();
    return tags.filter(tag => {
      const matchesSubsystem = subsystemFilter === "all" || tag.subsystem === subsystemFilter;
      const matchesSearch = !needle
        || tag.name.toLowerCase().includes(needle)
        || tag.tagKey.toLowerCase().includes(needle)
        || tag.subsystem.toLowerCase().includes(needle);
      return matchesSubsystem && matchesSearch;
    });
  }, [tags, tagSearch, subsystemFilter]);

  useEffect(() => {
    if (!token) return;
    loadInitial();
  }, [token]);

  useEffect(() => {
    if (!token) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBase}/hubs/telemetry`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();

    connection.onreconnecting(() => setStatus("Reconnecting"));
    connection.onreconnected(() => setStatus("Live"));
    connection.onclose(() => setStatus("Disconnected"));
    connection.on("telemetryUpdated", () => {
      if (selectedUnitId) loadUnit(selectedUnitId);
    });
    connection.on("alarmsChanged", loadAlarms);
    connection.on("unitStatusChanged", loadSitesAndUnits);

    connection.start()
      .then(() => setStatus("Live"))
      .catch(() => setStatus("Offline"));

    return () => {
      connection.stop();
    };
  }, [token, selectedUnitId]);

  useEffect(() => {
    if (!token || activeScreen !== "Trends" || !selectedTrendTagId) return;
    loadHistory();
  }, [token, activeScreen, selectedTrendTagId, trendWindow]);

  async function login(email: string, password: string) {
    const response = await fetch(`${apiBase}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password })
    });
    if (!response.ok) {
      throw new Error("Login failed");
    }

    const body = await response.json() as LoginResponse;
    localStorage.setItem(tokenKey, body.accessToken);
    setToken(body.accessToken);
    setUser({
      userId: body.user.id,
      tenantId: body.user.tenantId,
      email: body.user.email,
      displayName: body.user.displayName,
      role: body.user.role
    });
  }

  async function logout() {
    if (token) {
      await fetch(`${apiBase}/api/auth/logout`, { method: "POST", headers: authHeaders(token) });
    }
    localStorage.removeItem(tokenKey);
    setToken("");
    setUser(null);
  }

  async function loadInitial() {
    const [me, nextTenants] = await Promise.all([
      getJson<User>("/api/me"),
      getJson<Tenant[]>("/api/tenants")
    ]);
    setUser(me);
    setTenants(nextTenants);
    await loadSitesAndUnits();
    await Promise.all([loadAlarms(), loadReports(), loadSystem()]);
  }

  async function loadSitesAndUnits() {
    const nextSites = await getJson<Site[]>("/api/sites");
    setSites(nextSites);
    const siteId = selectedSiteId || nextSites[0]?.id || "";
    if (siteId) {
      await loadUnitsForSite(siteId);
    }
  }

  async function loadUnitsForSite(siteId: string) {
    setSelectedSiteId(siteId);
    const siteUnits = await getJson<Unit[]>(`/api/sites/${siteId}/units`);
    setUnits(siteUnits);
    const unitId = siteUnits.find(unit => unit.id === selectedUnitId)?.id ?? siteUnits[0]?.id ?? "";
    setSelectedUnitId(unitId);
    if (unitId) {
      await loadUnit(unitId);
    } else {
      setTags([]);
    }
  }

  async function selectUnit(unitId: string) {
    setSelectedUnitId(unitId);
    await loadUnit(unitId);
  }

  async function loadUnit(unitId: string) {
    const nextTags = await getJson<Tag[]>(`/api/units/${unitId}/tags/current`);
    setTags(nextTags);
    setSelectedTrendTagId(current => current && nextTags.some(tag => tag.tagId === current)
      ? current
      : nextTags[0]?.tagId ?? "");
  }

  async function loadAlarms() {
    setAlarms(await getJson<Alarm[]>("/api/alarms/active"));
  }

  async function loadReports() {
    setReports(await getJson<Report[]>("/api/reports/monthly"));
  }

  async function loadHistory() {
    if (!selectedTrendTagId) return;
    setHistoryStatus("Loading");
    try {
      const points = await getJson<HistoryPoint[]>(`/api/tags/${selectedTrendTagId}/history?minutes=${trendWindow}`);
      setHistory(points);
      setHistoryStatus("Ready");
    } catch {
      setHistory([]);
      setHistoryStatus("Failed");
    }
  }

  async function loadSystem() {
    const [healthResult, readyResult, metricsResult] = await Promise.allSettled([
      fetch(`${apiBase}/health`).then(response => response.ok ? response.json() : Promise.reject()),
      fetch(`${apiBase}/ready`).then(response => response.ok ? response.json() : Promise.reject()),
      fetch(`${apiBase}/metrics`).then(response => response.ok ? response.text() : Promise.reject())
    ]);

    setSystem({
      health: healthResult.status === "fulfilled" ? healthResult.value.status ?? "ok" : "failed",
      ready: readyResult.status === "fulfilled" ? readyResult.value.status ?? "ready" : "failed",
      metrics: metricsResult.status === "fulfilled" ? metricsResult.value : ""
    });
  }

  async function runReport() {
    if (!selectedUnitId) return;
    await fetch(`${apiBase}/api/reports/monthly/run`, {
      method: "POST",
      headers: { ...authHeaders(token), "Content-Type": "application/json" },
      body: JSON.stringify({ unitId: selectedUnitId })
    });
    await loadReports();
  }

  async function ackAlarm(alarmId: string) {
    await fetch(`${apiBase}/api/alarms/${alarmId}/ack`, {
      method: "POST",
      headers: authHeaders(token)
    });
    await loadAlarms();
  }

  async function getJson<T>(path: string): Promise<T> {
    const response = await fetch(`${apiBase}${path}`, { headers: authHeaders(token) });
    if (!response.ok) throw new Error(`${path} failed`);
    return response.json();
  }

  if (!token) {
    return <Login onLogin={login} />;
  }

  return (
    <main className="opsShell">
      <aside className="sideRail">
        <div className="brandBlock">
          <span className="brandMark">A</span>
          <div>
            <p className="eyebrow">Alpha SCADA</p>
            <strong>Night Ops</strong>
          </div>
        </div>

        <nav className="navStack" aria-label="Primary">
          {navItems.map(item => (
            <button
              className={activeScreen === item ? "navItem active" : "navItem"}
              key={item}
              onClick={() => setActiveScreen(item)}
            >
              <span>{item.slice(0, 2).toUpperCase()}</span>
              {item}
            </button>
          ))}
        </nav>

        <section className="railSection">
          <p className="railLabel">Sites</p>
          {sites.map(site => (
            <button
              className={`assetButton ${site.id === selectedSiteId ? "selected" : ""}`}
              key={site.id}
              onClick={() => loadUnitsForSite(site.id)}
            >
              <span>{site.name}</span>
              <small>{site.region} / {site.status}</small>
            </button>
          ))}
        </section>

        <section className="railSection">
          <p className="railLabel">Units</p>
          {units.map(unit => (
            <button
              className={`assetButton ${unit.id === selectedUnitId ? "selected" : ""}`}
              key={unit.id}
              onClick={() => selectUnit(unit.id)}
            >
              <span>{unit.name}</span>
              <small>{unit.status} / {unit.model}</small>
            </button>
          ))}
        </section>
      </aside>

      <section className="workspace">
        <header className="commandBar">
          <div>
            <p className="eyebrow">{selectedSite?.name ?? "No site selected"}</p>
            <h1>{activeScreen === "Overview" ? selectedUnit?.name ?? "Unit Overview" : activeScreen}</h1>
            <p className="caption">{selectedUnit?.model ?? "Combined Heat and Power Unit"} / Last sample {updatedAt}</p>
          </div>
          <div className="commandActions">
            <StatusBadge status={status} />
            <StatPill label="Alarms" value={String(alarms.length)} tone={alarms.length > 0 ? "danger" : "good"} />
            <StatPill label="Online" value={`${onlineUnits}/${units.length || 0}`} tone="cyan" />
            <button className="ghostButton" onClick={logout}>Logout</button>
          </div>
        </header>

        {activeScreen === "Overview" && (
          <OverviewScreen
            tags={tags}
            groupedTags={groupedTags}
            alarms={alarms}
            reports={reports}
            selectedUnitId={selectedUnitId}
            loadUnit={loadUnit}
            loadAlarms={loadAlarms}
            ackAlarm={ackAlarm}
            runReport={runReport}
          />
        )}

        {activeScreen === "Tags" && (
          <TagsScreen
            tags={filteredTags}
            allTags={tags}
            tagSearch={tagSearch}
            subsystemFilter={subsystemFilter}
            subsystems={subsystems}
            setTagSearch={setTagSearch}
            setSubsystemFilter={setSubsystemFilter}
          />
        )}

        {activeScreen === "Trends" && (
          <TrendsScreen
            tags={tags}
            selectedTag={selectedTrendTag}
            selectedTagId={selectedTrendTagId}
            trendWindow={trendWindow}
            history={history}
            historyStatus={historyStatus}
            setSelectedTagId={setSelectedTrendTagId}
            setTrendWindow={setTrendWindow}
            refresh={loadHistory}
          />
        )}

        {activeScreen === "Alarms" && (
          <AlarmsScreen alarms={alarms} units={units} loadAlarms={loadAlarms} ackAlarm={ackAlarm} />
        )}

        {activeScreen === "Reports" && (
          <ReportsScreen reports={reports} units={units} runReport={runReport} loadReports={loadReports} />
        )}

        {activeScreen === "Admin" && (
          <AdminScreen
            user={user}
            tenants={tenants}
            sites={sites}
            units={units}
            system={system}
            refreshSystem={loadSystem}
          />
        )}
      </section>
    </main>
  );
}

function OverviewScreen({
  tags,
  groupedTags,
  alarms,
  reports,
  selectedUnitId,
  loadUnit,
  loadAlarms,
  ackAlarm,
  runReport
}: {
  tags: Tag[];
  groupedTags: Record<string, Tag[]>;
  alarms: Alarm[];
  reports: Report[];
  selectedUnitId: string;
  loadUnit: (unitId: string) => Promise<void>;
  loadAlarms: () => Promise<void>;
  ackAlarm: (alarmId: string) => Promise<void>;
  runReport: () => Promise<void>;
}) {
  return (
    <>
      <section className="kpiGrid">
        <Kpi label="Electrical Output" value={tagValue(tags, "engine.electrical_output_kw", "kW")} tone="cyan" />
        <Kpi label="Thermal Output" value={tagValue(tags, "heat.thermal_output_kw", "kW")} tone="lime" />
        <Kpi label="Fuel Feed" value={tagValue(tags, "fuel.wood_chip_feed_kg_h", "kg/h")} tone="blue" />
        <Kpi label="CO Level" value={tagValue(tags, "safety.co_ppm", "ppm")} tone="amber" />
      </section>

      <section className="mainGrid">
        <section className="panel processPanel">
          <div className="panelHeader">
            <div>
              <p className="eyebrow">Process</p>
              <h2>Energy Conversion Line</h2>
            </div>
            <button className="iconButton" onClick={() => selectedUnitId && loadUnit(selectedUnitId)} title="Refresh live values">Refresh</button>
          </div>
          <div className="processFlow">
            {processSteps.map((step, index) => (
              <ProcessStep
                key={step.key}
                label={step.label}
                value={tagValue(tags, step.key, step.unit)}
                isLast={index === processSteps.length - 1}
              />
            ))}
          </div>
        </section>

        <AlarmPreview alarms={alarms} loadAlarms={loadAlarms} ackAlarm={ackAlarm} />
        <TagMatrix groupedTags={groupedTags} />
        <ReportPreview reports={reports} runReport={runReport} />
      </section>
    </>
  );
}

function TagsScreen({
  tags,
  allTags,
  tagSearch,
  subsystemFilter,
  subsystems,
  setTagSearch,
  setSubsystemFilter
}: {
  tags: Tag[];
  allTags: Tag[];
  tagSearch: string;
  subsystemFilter: string;
  subsystems: string[];
  setTagSearch: (value: string) => void;
  setSubsystemFilter: (value: string) => void;
}) {
  const goodQuality = allTags.filter(tag => tag.quality === "good").length;
  return (
    <section className="screenStack">
      <section className="kpiGrid three">
        <Kpi label="Visible Tags" value={String(tags.length)} tone="cyan" />
        <Kpi label="Good Quality" value={`${goodQuality}/${allTags.length}`} tone="lime" />
        <Kpi label="Subsystems" value={String(subsystems.length)} tone="blue" />
      </section>

      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Telemetry</p>
            <h2>Live Tag Browser</h2>
          </div>
          <div className="filterRow">
            <input value={tagSearch} onChange={event => setTagSearch(event.target.value)} placeholder="Search tags" />
            <select value={subsystemFilter} onChange={event => setSubsystemFilter(event.target.value)}>
              <option value="all">All subsystems</option>
              {subsystems.map(subsystem => <option key={subsystem} value={subsystem}>{subsystem}</option>)}
            </select>
          </div>
        </div>
        <div className="dataTable">
          <div className="tableHead">
            <span>Tag</span>
            <span>Subsystem</span>
            <span>Value</span>
            <span>Quality</span>
            <span>Timestamp</span>
          </div>
          {tags.map(tag => (
            <div className="tableRow" key={tag.tagId}>
              <span><strong>{tag.name}</strong><small>{tag.tagKey}</small></span>
              <span>{tag.subsystem}</span>
              <span className="mono">{format(tag.value)} {tag.engineeringUnit}</span>
              <span><Badge value={tag.quality} tone={tag.quality === "good" ? "good" : "warn"} /></span>
              <span>{new Date(tag.timestampUtc).toLocaleTimeString()}</span>
            </div>
          ))}
        </div>
      </section>
    </section>
  );
}

function TrendsScreen({
  tags,
  selectedTag,
  selectedTagId,
  trendWindow,
  history,
  historyStatus,
  setSelectedTagId,
  setTrendWindow,
  refresh
}: {
  tags: Tag[];
  selectedTag?: Tag;
  selectedTagId: string;
  trendWindow: number;
  history: HistoryPoint[];
  historyStatus: string;
  setSelectedTagId: (value: string) => void;
  setTrendWindow: (value: number) => void;
  refresh: () => Promise<void>;
}) {
  return (
    <section className="screenStack">
      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Historian</p>
            <h2>Trend Explorer</h2>
          </div>
          <div className="filterRow">
            <select value={selectedTagId} onChange={event => setSelectedTagId(event.target.value)}>
              {tags.map(tag => <option key={tag.tagId} value={tag.tagId}>{tag.name}</option>)}
            </select>
            {[15, 30, 60, 240].map(minutes => (
              <button
                className={trendWindow === minutes ? "segmented active" : "segmented"}
                key={minutes}
                onClick={() => setTrendWindow(minutes)}
              >
                {minutes}m
              </button>
            ))}
            <button className="iconButton" onClick={refresh}>Refresh</button>
          </div>
        </div>
        <TrendChart points={history} unit={selectedTag?.engineeringUnit ?? ""} />
      </section>

      <section className="kpiGrid three">
        <Kpi label="Selected Tag" value={selectedTag ? selectedTag.name : "--"} tone="cyan" />
        <Kpi label="History Points" value={String(history.length)} tone="lime" />
        <Kpi label="History Status" value={historyStatus} tone="blue" />
      </section>

      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Samples</p>
            <h2>Recent History</h2>
          </div>
        </div>
        <div className="dataTable compact">
          <div className="tableHead">
            <span>Time</span>
            <span>Value</span>
            <span>Quality</span>
          </div>
          {history.slice(-12).reverse().map(point => (
            <div className="tableRow" key={`${point.timestampUtc}-${point.value}`}>
              <span>{new Date(point.timestampUtc).toLocaleTimeString()}</span>
              <span className="mono">{format(point.value)} {selectedTag?.engineeringUnit}</span>
              <span><Badge value={point.quality} tone={point.quality === "good" ? "good" : "warn"} /></span>
            </div>
          ))}
        </div>
      </section>
    </section>
  );
}

function AlarmsScreen({ alarms, units, loadAlarms, ackAlarm }: {
  alarms: Alarm[];
  units: Unit[];
  loadAlarms: () => Promise<void>;
  ackAlarm: (alarmId: string) => Promise<void>;
}) {
  return (
    <section className="screenStack">
      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Events</p>
            <h2>Alarm Console</h2>
          </div>
          <button className="iconButton" onClick={loadAlarms}>Refresh</button>
        </div>
        {alarms.length === 0 ? <p className="empty">No active alarms</p> : alarms.map(alarm => (
          <article className={`alarm alarmWide ${alarm.severity}`} key={alarm.id}>
            <div>
              <strong>{alarm.severity}</strong>
              <span>{alarm.message}</span>
              <small>{unitName(units, alarm.unitId)} / {new Date(alarm.raisedAtUtc).toLocaleString()}</small>
            </div>
            <button onClick={() => ackAlarm(alarm.id)}>Acknowledge</button>
          </article>
        ))}
      </section>
    </section>
  );
}

function ReportsScreen({ reports, units, runReport, loadReports }: {
  reports: Report[];
  units: Unit[];
  runReport: () => Promise<void>;
  loadReports: () => Promise<void>;
}) {
  return (
    <section className="screenStack">
      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Output</p>
            <h2>Monthly Report Runs</h2>
          </div>
          <div className="filterRow">
            <button className="iconButton" onClick={loadReports}>Refresh</button>
            <button className="primaryButton" onClick={runReport}>Run Report</button>
          </div>
        </div>
        <div className="reportGrid">
          {reports.length === 0 ? <p className="empty">No report runs yet</p> : reports.map(report => (
            <article className="report reportCard" key={report.id}>
              <strong>{report.period}</strong>
              <span>{unitName(units, report.unitId)}</span>
              <div className="reportStats">
                <Metric label="Electrical" value={`${format(report.electricalKwh)} kWh`} />
                <Metric label="Thermal" value={`${format(report.thermalKwh)} kWh`} />
                <Metric label="Runtime" value={`${format(report.runtimeHours)} h`} />
                <Metric label="Availability" value={`${format(report.availabilityPercent)}%`} />
                <Metric label="Wood chips" value={`${format(report.estimatedWoodChipsKg)} kg`} />
                <Metric label="Biochar" value={`${format(report.estimatedBiocharM3)} m3`} />
                <Metric label="Alarms" value={String(report.alarmCount)} />
              </div>
            </article>
          ))}
        </div>
      </section>
    </section>
  );
}

function AdminScreen({ user, tenants, sites, units, system, refreshSystem }: {
  user: User | null;
  tenants: Tenant[];
  sites: Site[];
  units: Unit[];
  system: SystemProbe;
  refreshSystem: () => Promise<void>;
}) {
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

function AlarmPreview({ alarms, loadAlarms, ackAlarm }: {
  alarms: Alarm[];
  loadAlarms: () => Promise<void>;
  ackAlarm: (alarmId: string) => Promise<void>;
}) {
  return (
    <section className="panel alarmPanel">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Events</p>
          <h2>Active Alarms</h2>
        </div>
        <button className="iconButton" onClick={loadAlarms} title="Refresh alarms">Refresh</button>
      </div>
      {alarms.length === 0 ? <p className="empty">No active alarms</p> : alarms.map(alarm => (
        <article className={`alarm ${alarm.severity}`} key={alarm.id}>
          <strong>{alarm.severity}</strong>
          <span>{alarm.message}</span>
          <button onClick={() => ackAlarm(alarm.id)}>Acknowledge</button>
        </article>
      ))}
    </section>
  );
}

function TagMatrix({ groupedTags }: { groupedTags: Record<string, Tag[]> }) {
  return (
    <section className="panel tagPanel">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Telemetry</p>
          <h2>Subsystem Tag Matrix</h2>
        </div>
      </div>
      <div className="tagGrid">
        {Object.entries(groupedTags).map(([subsystem, subsystemTags]) => (
          <article className="subsystem" key={subsystem}>
            <h3>{subsystem}</h3>
            {subsystemTags.map(tag => (
              <div className="tagRow" key={tag.tagId}>
                <span>{tag.name}</span>
                <strong>{format(tag.value)} {tag.engineeringUnit}</strong>
              </div>
            ))}
          </article>
        ))}
      </div>
    </section>
  );
}

function ReportPreview({ reports, runReport }: { reports: Report[]; runReport: () => Promise<void> }) {
  return (
    <section className="panel reportPanel">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Output</p>
          <h2>Monthly Reports</h2>
        </div>
        <button className="primaryButton" onClick={runReport}>Run Report</button>
      </div>
      {reports.length === 0 ? <p className="empty">No report runs yet</p> : reports.slice(0, 3).map(report => (
        <article className="report" key={report.id}>
          <strong>{report.period}</strong>
          <span>{format(report.electricalKwh)} kWh electrical / {format(report.thermalKwh)} kWh thermal</span>
          <span>{format(report.estimatedWoodChipsKg)} kg wood chips / {format(report.estimatedBiocharM3)} m3 biochar</span>
        </article>
      ))}
    </section>
  );
}

function Login({ onLogin }: { onLogin: (email: string, password: string) => Promise<void> }) {
  const [email, setEmail] = useState("admin@alpha.local");
  const [password, setPassword] = useState("ChangeMe!123");
  const [error, setError] = useState("");

  return (
    <main className="login">
      <form onSubmit={async event => {
        event.preventDefault();
        setError("");
        try {
          await onLogin(email, password);
        } catch {
          setError("Login failed");
        }
      }}>
        <span className="brandMark large">A</span>
        <p className="eyebrow">Alpha SCADA</p>
        <h1>Sign in</h1>
        <label>Email<input value={email} onChange={event => setEmail(event.target.value)} /></label>
        <label>Password<input type="password" value={password} onChange={event => setPassword(event.target.value)} /></label>
        {error && <p className="error">{error}</p>}
        <button className="primaryButton" type="submit">Login</button>
      </form>
    </main>
  );
}

function Kpi({ label, value, tone }: { label: string; value: string; tone: "cyan" | "lime" | "blue" | "amber" }) {
  return (
    <article className={`kpi ${tone}`}>
      <span>{label}</span>
      <strong title={value}>{value}</strong>
      <small>live telemetry</small>
    </article>
  );
}

function TrendChart({ points, unit }: { points: HistoryPoint[]; unit: string }) {
  if (points.length === 0) {
    return <div className="chartEmpty">No history samples in this window</div>;
  }

  const width = 900;
  const height = 280;
  const padding = 36;
  const values = points.map(point => point.value);
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = Math.max(max - min, 1);
  const coordinates = points.map((point, index) => {
    const x = padding + (index / Math.max(points.length - 1, 1)) * (width - padding * 2);
    const y = height - padding - ((point.value - min) / span) * (height - padding * 2);
    return `${x},${y}`;
  }).join(" ");

  return (
    <div className="trendChart">
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Tag history trend">
        <line x1={padding} y1={padding} x2={padding} y2={height - padding} />
        <line x1={padding} y1={height - padding} x2={width - padding} y2={height - padding} />
        <polyline points={coordinates} />
        <text x={padding} y={24}>{format(max)} {unit}</text>
        <text x={padding} y={height - 10}>{format(min)} {unit}</text>
      </svg>
    </div>
  );
}

function ProcessStep({ label, value, isLast }: { label: string; value: string; isLast: boolean }) {
  return (
    <article className="processStep">
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
      </div>
      {!isLast && <b className="flowLink" aria-hidden="true" />}
    </article>
  );
}

function StatusBadge({ status }: { status: string }) {
  const tone = status === "Live" ? "good" : "warn";
  return <span className={`statusBadge ${tone}`}><b />{status}</span>;
}

function StatPill({ label, value, tone }: { label: string; value: string; tone: "good" | "danger" | "cyan" }) {
  return <span className={`statPill ${tone}`}><small>{label}</small>{value}</span>;
}

function Badge({ value, tone }: { value: string; tone: "good" | "warn" }) {
  return <span className={`badge ${tone}`}>{value}</span>;
}

function Metric({ label, value }: { label: string; value: string }) {
  return <span className="metric"><small>{label}</small><strong>{value}</strong></span>;
}

function Detail({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return <div className="detailRow"><span>{label}</span><strong className={mono ? "mono" : ""}>{value}</strong></div>;
}

function tagValue(tags: Tag[], key: string, unit: string) {
  const tag = tags.find(item => item.tagKey === key);
  return tag ? `${format(tag.value)} ${unit}` : "--";
}

function unitName(units: Unit[], unitId: string) {
  return units.find(unit => unit.id === unitId)?.name ?? "Unit";
}

function format(value: number) {
  return Number(value).toLocaleString(undefined, { maximumFractionDigits: 1 });
}

function authHeaders(token: string) {
  return { Authorization: `Bearer ${token}` };
}

function groupBy<T>(items: T[], selector: (item: T) => string) {
  return items.reduce<Record<string, T[]>>((groups, item) => {
    const key = selector(item);
    groups[key] ??= [];
    groups[key].push(item);
    return groups;
  }, {});
}

createRoot(document.getElementById("root")!).render(<App />);
