import { useEffect, useMemo, useState } from "react";
import { apiBase, authHeaders, getJson, tokenKey } from "./api/client";
import type { Alarm, HistoryPoint, LoginResponse, Report, ScreenName, Site, SystemProbe, Tag, TelemetryUpdate, Tenant, Unit, User } from "./api/types";
import StatPill from "./components/StatPill";
import StatusBadge from "./components/StatusBadge";
import useSignalR from "./hooks/useSignalR";
import { groupBy } from "./lib/format";
import AdminScreen from "./screens/AdminScreen";
import AlarmsScreen from "./screens/AlarmsScreen";
import Login from "./screens/Login";
import OverviewScreen from "./screens/OverviewScreen";
import ReportsScreen from "./screens/ReportsScreen";
import TagsScreen from "./screens/TagsScreen";
import TrendsScreen from "./screens/TrendsScreen";

const navItems: ScreenName[] = ["Overview", "Tags", "Trends", "Alarms", "Reports", "Admin"];

export default function App() {
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
  const [reportRunning, setReportRunning] = useState(false);
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

  useSignalR({
    token,
    setStatus,
    applyTelemetryUpdate,
    loadAlarms,
    loadSitesAndUnits,
    onReportCompleted
  });

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
      getJson<User>("/api/me", token),
      getJson<Tenant[]>("/api/tenants", token)
    ]);
    setUser(me);
    setTenants(nextTenants);
    await loadSitesAndUnits();
    await Promise.all([loadAlarms(), loadReports(), loadSystem()]);
  }

  async function loadSitesAndUnits() {
    const nextSites = await getJson<Site[]>("/api/sites", token);
    setSites(nextSites);
    const siteId = selectedSiteId || nextSites[0]?.id || "";
    if (siteId) {
      await loadUnitsForSite(siteId);
    }
  }

  async function loadUnitsForSite(siteId: string) {
    setSelectedSiteId(siteId);
    const siteUnits = await getJson<Unit[]>(`/api/sites/${siteId}/units`, token);
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
    const nextTags = await getJson<Tag[]>(`/api/units/${unitId}/tags/current`, token);
    setTags(nextTags);
    setSelectedTrendTagId(current => current && nextTags.some(tag => tag.tagId === current)
      ? current
      : nextTags[0]?.tagId ?? "");
  }

  function applyTelemetryUpdate(update: TelemetryUpdate) {
    if (update.unitId !== selectedUnitId || update.samples.length === 0) return;

    setTags(currentTags => {
      const samplesByTag = new Map(update.samples.map(sample => [sample.tagId, sample]));
      let hasChanges = false;
      const nextTags = currentTags.map(tag => {
        const sample = samplesByTag.get(tag.tagId);
        if (!sample) return tag;

        hasChanges = true;
        return {
          ...tag,
          value: sample.value,
          quality: sample.quality,
          timestampUtc: sample.timestampUtc
        };
      });

      return hasChanges ? nextTags : currentTags;
    });
  }

  async function loadAlarms() {
    setAlarms(await getJson<Alarm[]>("/api/alarms/active", token));
  }

  async function loadReports() {
    setReports(await getJson<Report[]>("/api/reports/monthly", token));
  }

  async function loadHistory() {
    if (!selectedTrendTagId) return;
    setHistoryStatus("Loading");
    try {
      const points = await getJson<HistoryPoint[]>(`/api/tags/${selectedTrendTagId}/history?minutes=${trendWindow}`, token);
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
    setReportRunning(true);
    try {
      const response = await fetch(`${apiBase}/api/reports/monthly/run`, {
        method: "POST",
        headers: { ...authHeaders(token), "Content-Type": "application/json" },
        body: JSON.stringify({ unitId: selectedUnitId })
      });
      if (!response.ok) {
        setReportRunning(false);
      }
    } catch {
      setReportRunning(false);
    }
  }

  async function onReportCompleted() {
    setReportRunning(false);
    await loadReports();
  }

  async function ackAlarm(alarmId: string) {
    await fetch(`${apiBase}/api/alarms/${alarmId}/ack`, {
      method: "POST",
      headers: authHeaders(token)
    });
    await loadAlarms();
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
            reportRunning={reportRunning}
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
          <ReportsScreen reports={reports} units={units} runReport={runReport} loadReports={loadReports} reportRunning={reportRunning} />
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
