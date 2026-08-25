import { useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { EntriesPage } from "./features/entries/EntriesPage";
import { ReportPage } from "./features/report/ReportPage";
import "./App.css";

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 20_000, retry: 1 } },
});

type Screen = "entries" | "report";

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <Workspace />
    </QueryClientProvider>
  );
}

function Workspace() {
  const [screen, setScreen] = useState<Screen>("entries");
  const [menuOpen, setMenuOpen] = useState(false);

  const go = (next: Screen) => {
    setScreen(next);
    setMenuOpen(false);
  };

  return (
    <div className="shell">
      <aside className={menuOpen ? "open" : ""}>
        <div className="brand">
          <span aria-hidden="true">PM</span>
          <strong>GROWTH</strong>
          <button
            className="burger"
            aria-label={menuOpen ? "Скрыть меню" : "Показать меню"}
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen((open) => !open)}
          >
            ☰
          </button>
        </div>
        <p className="kicker">Проектный контроль</p>
        <nav>
          <button
            className={screen === "entries" ? "active" : ""}
            aria-current={screen === "entries" ? "page" : undefined}
            onClick={() => go("entries")}
          >
            <span aria-hidden="true">◫</span> <span>Табель</span>
          </button>
          <button
            className={screen === "report" ? "active" : ""}
            aria-current={screen === "report" ? "page" : undefined}
            onClick={() => go("report")}
          >
            <span aria-hidden="true">↗</span> <span>Отчёт по проектам</span>
          </button>
        </nav>
        <div className="aside-foot">
          <i aria-hidden="true" /> Система работает штатно
        </div>
      </aside>
      <main key={screen} className="screen-in">
        {screen === "entries" ? <EntriesPage /> : <ReportPage />}
      </main>
    </div>
  );
}
