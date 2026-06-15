import type { Alarm } from "../api/types";

type AlarmPreviewProps = {
  alarms: Alarm[];
  loadAlarms: () => Promise<void>;
  ackAlarm: (alarmId: string) => Promise<void>;
  mayAcknowledge: boolean;
};

export default function AlarmPreview({ alarms, loadAlarms, ackAlarm, mayAcknowledge }: AlarmPreviewProps) {
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
          {mayAcknowledge && <button onClick={() => ackAlarm(alarm.id)}>Acknowledge</button>}
        </article>
      ))}
    </section>
  );
}
