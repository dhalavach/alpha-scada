import type { Alarm, Unit } from "../api/types";
import { unitName } from "../lib/format";

type AlarmsScreenProps = {
  alarms: Alarm[];
  units: Unit[];
  loadAlarms: () => Promise<void>;
  ackAlarm: (alarmId: string) => Promise<void>;
  mayAcknowledge: boolean;
};

export default function AlarmsScreen({ alarms, units, loadAlarms, ackAlarm, mayAcknowledge }: AlarmsScreenProps) {
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
            {mayAcknowledge && <button onClick={() => ackAlarm(alarm.id)}>Acknowledge</button>}
          </article>
        ))}
      </section>
    </section>
  );
}
