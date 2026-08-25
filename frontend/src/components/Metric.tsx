export function Metric({
  label,
  value,
  hint,
  loading = false,
}: {
  label: string;
  value: string;
  hint?: string;
  loading?: boolean;
}) {
  return (
    <div className="metric">
      <span>{label}</span>
      {loading ? (
        <span className="skeleton" aria-hidden="true" />
      ) : (
        <strong>{value}</strong>
      )}
      {hint && <small>{hint}</small>}
    </div>
  );
}
