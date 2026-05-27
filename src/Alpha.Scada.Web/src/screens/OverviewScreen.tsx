import type { Alarm, Report, Tag } from "../api/types";
import AlarmPreview from "../components/AlarmPreview";
import Kpi from "../components/Kpi";
import ProcessStep from "../components/ProcessStep";
import ReportPreview from "../components/ReportPreview";
import TagMatrix from "../components/TagMatrix";
import { processSteps, tagValue } from "../lib/format";

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
  runReport
}: OverviewScreenProps) {
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
