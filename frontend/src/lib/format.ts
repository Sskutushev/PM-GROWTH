const moneyFormat = new Intl.NumberFormat("ru-RU", {
  style: "currency",
  currency: "RUB",
  maximumFractionDigits: 2,
});

const numberFormat = new Intl.NumberFormat("ru-RU", {
  maximumFractionDigits: 2,
});

const dayFormat = new Intl.DateTimeFormat("ru-RU", {
  day: "2-digit",
  month: "short",
});

export const money = (value: number) => moneyFormat.format(value);

export const number = (value: number) => numberFormat.format(value);

// The API sends a plain date. Parsing it without a time zone would shift the day backwards
// west of UTC, so the local midnight is spelled out.
export const formatDate = (value: string) =>
  dayFormat.format(new Date(`${value}T00:00:00`));

// "N записей" in Russian needs three forms, and the table header shows the count on every page.
export const plural = (count: number, forms: [string, string, string]) => {
  const tens = Math.abs(count) % 100;
  const units = tens % 10;

  if (tens > 10 && tens < 20) return forms[2];
  if (units > 1 && units < 5) return forms[1];
  if (units === 1) return forms[0];

  return forms[2];
};

export const entriesLabel = (count: number) =>
  `${number(count)} ${plural(count, ["запись", "записи", "записей"])}`;
