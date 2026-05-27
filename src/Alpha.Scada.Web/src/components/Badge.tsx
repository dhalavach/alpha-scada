type BadgeProps = {
  value: string;
  tone: "good" | "warn";
};

export default function Badge({ value, tone }: BadgeProps) {
  return <span className={`badge ${tone}`}>{value}</span>;
}
