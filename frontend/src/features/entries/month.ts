// The reference dataset from the assignment lives in 2026, so the screens open on the month
// that actually has data instead of an empty current month.
export const currentMonth = "2026-03";

// A new entry starts on the 1st of the month the user is looking at, not on an unrelated day.
export const firstDayOf = (month: string) => `${month}-01`;
