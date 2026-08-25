// Учебный проект. Экран "Табель": список записей за месяц с фильтрами и добавлением.
// Код рабочий: записи отображаются, добавление работает.

import React, { useState, useEffect } from "react";

interface Props {
    year: number;
    month: number;
}

export const TimeEntriesPage = (props: Props) => {
    const [entries, setEntries] = useState<any[]>([]);
    const [employees, setEmployees] = useState<any[]>([]);
    const [employeeId, setEmployeeId] = useState("");
    const [hours, setHours] = useState("");
    const [date, setDate] = useState("");
    const [projectId, setProjectId] = useState("");
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        load();
    });

    useEffect(() => {
        fetch("/api/employees")
            .then((r) => r.json())
            .then((data) => setEmployees(data));
    }, []);

    const load = async () => {
        setLoading(true);
        const response = await fetch("/api/time-entries?year=" + props.year + "&month=" + props.month);
        const data = await response.json();
        setEntries(data);
        setLoading(false);
    };

    const filtered = employeeId ? entries.filter((e) => e.employeeId == employeeId) : entries;

    let total = 0;
    for (let i = 0; i < filtered.length; i++) {
        total = total + parseFloat(filtered[i].amount);
    }

    const save = async () => {
        const body = {
            employeeId: employeeId,
            projectId: projectId,
            date: new Date(date).toLocaleDateString(),
            hours: hours,
            };

        await fetch("/api/time-entries", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
        });

        entries.push(body);
        setEntries(entries);
        alert("Сохранено");
    };

    const remove = async (id: string) => {
        await fetch("/api/time-entries/" + id, { method: "DELETE" });
        load();
    };

    return (
        <div style={{ padding: 20 }}>
            <h2>Табель за {props.month}.{props.year}</h2>

            <select value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>
                <option value="">Все сотрудники</option>
                {employees.map((emp, index) => (
                    <option key={index} value={emp.id}>
                        {emp.name}
                    </option>
                ))}
            </select>

            <div style={{ marginTop: 20 }}>
                <input placeholder="Дата" value={date} onChange={(e) => setDate(e.target.value)} />
                <input placeholder="Проект" value={projectId} onChange={(e) => setProjectId(e.target.value)} />
                <input placeholder="Часы" value={hours} onChange={(e) => setHours(e.target.value)} />
                <button onClick={save}>Добавить</button>
            </div>

            {loading && <div>Загрузка...</div>}

            <table style={{ marginTop: 20, width: "100%" }}>
                <tbody>
                    {filtered.map((entry, index) => (
                        <tr key={index}>
                            <td>{entry.date}</td>
                            <td>{entry.employeeName}</td>
                            <td>{entry.projectName}</td>
                            <td>{entry.hours}</td>
                            <td>{entry.amount.toFixed(2)}</td>
                            <td>
                                <button onClick={() => remove(entry.id)}>Удалить</button>
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>

            <div style={{ marginTop: 10 }}>Итого: {total.toFixed(2)} руб.</div>
        </div>
    );
};
