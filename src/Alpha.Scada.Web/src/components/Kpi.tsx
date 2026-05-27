type KpiProps = {
  label: string;
  value: string;
  tone: "cyan" | "lime" | "blue" | "amber";
};

export default function Kpi({ label, value, tone }: KpiProps) {
  return (
    <article className={`kpi ${tone}`}>
      <span>{label}</span>
      <strong title={value}>{value}</strong>
      <small>live telemetry</small>
    </article>
  );
}
