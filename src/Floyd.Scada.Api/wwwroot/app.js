const state = {
  tags: new Map(),
};

const tagRows = document.querySelector("#tagRows");
const alarmList = document.querySelector("#alarmList");
const report = document.querySelector("#report");
const statusEl = document.querySelector("#connectionStatus");
const refreshButton = document.querySelector("#refreshButton");

refreshButton.addEventListener("click", () => {
  refresh();
});

connectTelemetry();
refresh();
setInterval(refreshAlarmsAndReport, 5000);

function connectTelemetry() {
  const protocol = window.location.protocol === "https:" ? "wss" : "ws";
  const socket = new WebSocket(`${protocol}://${window.location.host}/ws/telemetry`);

  socket.addEventListener("open", () => setStatus("Live", "good"));
  socket.addEventListener("close", () => {
    setStatus("Disconnected", "bad");
    setTimeout(connectTelemetry, 2000);
  });
  socket.addEventListener("error", () => setStatus("Error", "bad"));
  socket.addEventListener("message", event => {
    const message = JSON.parse(event.data);
    const tags = message.tags ?? [];
    for (const tag of tags) {
      state.tags.set(tag.tagKey, tag);
    }
    renderTags();
  });
}

async function refresh() {
  const tags = await getJson("/api/tags/current");
  for (const tag of tags) {
    state.tags.set(tag.tagKey, tag);
  }
  renderTags();
  await refreshAlarmsAndReport();
}

async function refreshAlarmsAndReport() {
  const [alarms, reportSnapshot] = await Promise.all([
    getJson("/api/alarms/active"),
    getJson("/api/reports/monthly"),
  ]);

  renderAlarms(alarms);
  renderReport(reportSnapshot);
}

function renderTags() {
  const tags = Array.from(state.tags.values())
    .sort((a, b) => `${a.subsystem}${a.name}`.localeCompare(`${b.subsystem}${b.name}`));

  tagRows.innerHTML = tags.map(tag => `
    <tr>
      <td>${escapeHtml(tag.subsystem)}</td>
      <td>${escapeHtml(tag.name)}</td>
      <td><strong>${formatNumber(tag.value)}</strong> ${escapeHtml(tag.engineeringUnit)}</td>
      <td><span class="pill">${escapeHtml(tag.quality)}</span></td>
    </tr>
  `).join("");

  setKpi("electrical", "engine.electrical_output_kw", "kW");
  setKpi("thermal", "heat.thermal_output_kw", "kW");
  setKpi("fuel", "fuel.wood_chip_feed_kg_h", "kg/h");
  setKpi("safety", "safety.co_ppm", "ppm CO");
}

function renderAlarms(alarms) {
  if (!alarms.length) {
    alarmList.innerHTML = `<p class="empty">No active alarms</p>`;
    return;
  }

  alarmList.innerHTML = alarms.map(alarm => `
    <div class="alarm ${escapeHtml(alarm.severity)}">
      <strong>${escapeHtml(alarm.name)}</strong>
      <span>${escapeHtml(alarm.message)}</span>
    </div>
  `).join("");
}

function renderReport(snapshot) {
  const rows = [
    ["Period", snapshot.period],
    ["Electrical", `${formatNumber(snapshot.electricalKwh)} kWh`],
    ["Thermal", `${formatNumber(snapshot.thermalKwh)} kWh`],
    ["Runtime", `${formatNumber(snapshot.runtimeHours)} h`],
    ["Availability", `${formatNumber(snapshot.availabilityPercent)}%`],
    ["Wood chips", `${formatNumber(snapshot.estimatedWoodChipsKg)} kg`],
    ["Biochar", `${formatNumber(snapshot.estimatedBiocharM3)} m3`],
    ["Active alarms", snapshot.alarmCount],
  ];

  report.innerHTML = rows.map(([label, value]) => `
    <div>
      <dt>${escapeHtml(label)}</dt>
      <dd>${escapeHtml(value)}</dd>
    </div>
  `).join("");
}

function setKpi(elementId, tagKey, unit) {
  const tag = state.tags.get(tagKey);
  document.querySelector(`#${elementId}`).textContent = tag
    ? `${formatNumber(tag.value)} ${unit}`
    : "--";
}

async function getJson(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`);
  }
  return response.json();
}

function setStatus(label, kind) {
  statusEl.textContent = label;
  statusEl.dataset.kind = kind;
}

function formatNumber(value) {
  return Number(value).toLocaleString(undefined, {
    maximumFractionDigits: 1,
  });
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
