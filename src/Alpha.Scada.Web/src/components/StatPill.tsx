type StatPillProps = {
  label: string;
  value: string;
  tone: "good" | "danger" | "cyan";
};

export default function StatPill({ label, value, tone }: StatPillProps) {
  return <span className={`statPill ${tone}`}><small>{label}</small>{value}</span>;
}
