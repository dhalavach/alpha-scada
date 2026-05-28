import type { Report } from "../api/types";
import { format } from "../lib/format";

type ReportPreviewProps = {
  reports: Report[];
  runReport: () => Promise<void>;
  reportRunning: boolean;
};

export default function ReportPreview({ reports, runReport, reportRunning }: ReportPreviewProps) {
  return (
    <section className="panel reportPanel">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Output</p>
          <h2>Monthly Reports</h2>
        </div>
        <button className="primaryButton" onClick={runReport} disabled={reportRunning}>
          {reportRunning ? "Generating..." : "Run Report"}
        </button>
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
