type DetailProps = {
  label: string;
  value: string;
  mono?: boolean;
};

export default function Detail({ label, value, mono }: DetailProps) {
  return <div className="detailRow"><span>{label}</span><strong className={mono ? "mono" : ""}>{value}</strong></div>;
}
