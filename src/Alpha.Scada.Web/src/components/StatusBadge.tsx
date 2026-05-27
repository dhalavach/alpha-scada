type StatusBadgeProps = {
  status: string;
};

export default function StatusBadge({ status }: StatusBadgeProps) {
  const tone = status === "Live" ? "good" : "warn";
  return <span className={`statusBadge ${tone}`}><b />{status}</span>;
}
