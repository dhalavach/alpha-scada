import type { Report, Unit } from "../api/types";
import Metric from "../components/Metric";
import { format, unitName } from "../lib/format";

type ReportsScreenProps = {
  reports: Report[];
  units: Unit[];
  runReport: () => Promise<void>;
  loadReports: () => Promise<void>;
  reportRunning: boolean;
};

export default function ReportsScreen({ reports, units, runReport, loadReports, reportRunning }: ReportsScreenProps) {
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
            <button className="primaryButton" onClick={runReport} disabled={reportRunning}>
              {reportRunning ? "Generating..." : "Run Report"}
            </button>
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
