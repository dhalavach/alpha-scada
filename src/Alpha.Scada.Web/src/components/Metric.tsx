type MetricProps = {
  label: string;
  value: string;
};

export default function Metric({ label, value }: MetricProps) {
  return <span className="metric"><small>{label}</small><strong>{value}</strong></span>;
}
