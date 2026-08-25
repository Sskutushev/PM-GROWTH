import type { Lookup } from "../api";

export function SelectFilter({
  label,
  value,
  onChange,
  options,
  all,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: Lookup[];
  all: string;
}) {
  return (
    <label>
      {label}
      <select value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">{all}</option>
        {options.map((option) => (
          <option key={option.id} value={option.id}>
            {option.code ? `${option.code} · ` : ""}
            {option.name}
          </option>
        ))}
      </select>
    </label>
  );
}
