import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api, type Entry } from "../../api";
import { ErrorBanner } from "../../components/ErrorBanner";
import { Modal } from "../../components/Modal";
import { formatDate, money, number } from "../../lib/format";

export function DeleteEntryDialog({
  entry,
  onDeleted,
  onClose,
}: {
  entry: Entry;
  onDeleted: () => void;
  onClose: () => void;
}) {
  const client = useQueryClient();

  const mutation = useMutation({
    mutationFn: () => api.remove(entry.id, entry.version),
    onSuccess: async () => {
      onDeleted();
      await client.invalidateQueries({ queryKey: ["entries"] });
      onClose();
    },
  });

  return (
    <Modal eyebrow="Удаление" title="Удалить запись?" onClose={onClose}>
      <p className="confirm-text">
        {formatDate(entry.date)} · {entry.employeeName} ·{" "}
        <span className="code">{entry.projectCode}</span> ·{" "}
        {number(entry.hours)} ч · {money(entry.amount)}
      </p>
      <p className="muted">
        Действие необратимо. В закрытом месяце удаление запрещено — сервер
        ответит отказом.
      </p>
      {mutation.error && <ErrorBanner error={mutation.error} />}
      <div className="actions">
        <button type="button" onClick={onClose}>
          Отмена
        </button>
        <button
          className="danger-solid"
          type="button"
          onClick={() => mutation.mutate()}
          disabled={mutation.isPending}
        >
          {mutation.isPending ? "Удаляем…" : "Удалить"}
        </button>
      </div>
    </Modal>
  );
}
