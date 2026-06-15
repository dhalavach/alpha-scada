import type { Alarm, Report, Tag } from "../api/types";
import AlarmPreview from "../components/AlarmPreview";
import Kpi from "../components/Kpi";
import ProcessStep from "../components/ProcessStep";
import ReportPreview from "../components/ReportPreview";
import TagMatrix from "../components/TagMatrix";
import { buildOverviewKpis, buildProcessSteps, tagValue } from "../lib/format";

type OverviewScreenProps = {
  tags: Tag[];
  groupedTags: Record<string, Tag[]>;
  alarms: Alarm[];
  reports: Report[];
  selectedUnitId: string;
  loadUnit: (unitId: string) => Promise<void>;
  loadAlarms: () => Promise<void>;
  ackAlarm: (alarmId: string) => Promise<void>;
  runReport: () => Promise<void>;
  reportRunning: boolean;
  mayAcknowledge: boolean;
  mayRunReports: boolean;
};

export default function OverviewScreen({
  tags,
  groupedTags,
  alarms,
  reports,
  selectedUnitId,
  loadUnit,
  loadAlarms,
  ackAlarm,
  runReport,
  reportRunning,
  mayAcknowledge,
  mayRunReports
}: OverviewScreenProps) {
  const processSteps = buildProcessSteps(tags);
  const kpis = buildOverviewKpis(tags);

  return (
    <>
      <section className="kpiGrid">
        {kpis.map(kpi => (
          <Kpi
            key={kpi.key}
            label={kpi.label}
            value={tagValue(tags, kpi.key, kpi.unit)}
            tone={kpi.tone}
          />
        ))}
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

        <AlarmPreview alarms={alarms} loadAlarms={loadAlarms} ackAlarm={ackAlarm} mayAcknowledge={mayAcknowledge} />
        <TagMatrix groupedTags={groupedTags} />
        <ReportPreview
          reports={reports}
          runReport={runReport}
          reportRunning={reportRunning}
          mayRunReports={mayRunReports}
        />
      </section>
    </>
  );
}
