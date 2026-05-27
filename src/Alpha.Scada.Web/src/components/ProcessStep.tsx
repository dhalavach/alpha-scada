type ProcessStepProps = {
  label: string;
  value: string;
  isLast: boolean;
};

export default function ProcessStep({ label, value, isLast }: ProcessStepProps) {
  return (
    <article className="processStep">
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
      </div>
      {!isLast && <b className="flowLink" aria-hidden="true" />}
    </article>
  );
}
