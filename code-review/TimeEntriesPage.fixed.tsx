import { useQuery } from '@tanstack/react-query'
type Entry = { id: string; employeeId: string; date: string; hours: number; amount: number }
export function TimeEntriesPage({ year, month }: { year: number; month: number }) {
  const query = useQuery<Entry[]>({
    queryKey: ['time-entries', year, month],
    queryFn: async ({ signal }) => {
      const response = await fetch(`/api/time-entries?year=${year}&month=${month}`, { signal })
      if (!response.ok) throw new Error('Не удалось загрузить табель')
      return response.json() as Promise<Entry[]>
    },
  })
  if (query.isPending) return <p>Загрузка…</p>
  if (query.error) return <p role="alert">{query.error.message}</p>
  return <ul>{query.data.map(entry => <li key={entry.id}>{entry.date}: {entry.hours} ч</li>)}</ul>
}
